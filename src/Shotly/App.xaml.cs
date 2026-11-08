using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Shotly.Capture;
using Shotly.Core;
using Shotly.Editor;
using Shotly.Tray;
using Shotly.UI;

namespace Shotly;

public partial class App : Application, ITrayCommands, ISettingsHost
{
    private const string MutexName = @"Local\Shotly.SingleInstance";
    private const string ExitEventName = @"Local\Shotly.Exit";
    private const string ShowSettingsEventName = @"Local\Shotly.ShowSettings";

    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private EventWaitHandle? _exitEvent;
    private EventWaitHandle? _showSettingsEvent;
    private readonly List<RegisteredWaitHandle> _waits = new();
    private SettingsWindow? _settingsWindow;
    private bool _updating;
    private bool _uninstalled;
    private SettingsStore _store = null!;
    private AppSettings _settings = null!;
    private OutputService _output = null!;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private CaptureSession? _session;
    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (Array.Exists(e.Args, a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            // Запуск из «Установленных приложений»: работающий экземпляр нужно сначала закрыть.
            UninstallFromSystemSettings();
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Уже запущен: повторный запуск exe открывает настройки в работающем экземпляре.
            Signal(ShowSettingsEventName);
            Shutdown();
            return;
        }
        _ownsMutex = true;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Необработанное исключение", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Необработанное исключение в задаче", args.Exception);
            args.SetObserved();
        };

        try
        {
            _store = new SettingsStore();
            _settings = _store.Load();
            _output = new OutputService(() => _settings);
            _tray = new TrayIcon(this, _settings);

            // Смена мониторов или масштаба во время выделения: отменяем сессию, а не рисуем по устаревшим координатам.
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _hotkeys = new HotkeyManager();
            RegisterHotkeys();

            _exitEvent = ListenForSignal(ExitEventName, () => Shutdown());
            _showSettingsEvent = ListenForSignal(ShowSettingsEventName, OpenSettings);
        }
        catch (Exception ex)
        {
            // Без трея и хоткеев процесс бесполезен и только держит мьютекс: сообщаем и выходим.
            Log.Error("Ошибка запуска", ex);
            MessageBox.Show("Shotly не смог запуститься:\n" + ex.Message + "\n\nПодробности в " + Log.FilePath,
                "Shotly", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Log.Info($"Shotly {Installation.Version} запущен");

        try
        {
            // Запись в «Установленных приложениях» обновляется при каждом запуске: путь и версия могли поменяться.
            Installation.Register();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось зарегистрировать Shotly в списке приложений", ex);
        }

        if (_settings.CheckUpdates)
            _ = CheckForUpdatesQuietlyAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var wait in _waits)
            wait.Unregister(null);
        _exitEvent?.Dispose();
        _showSettingsEvent?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        if (_ownsMutex)
        {
            // Явное освобождение: удаление из «Установленных приложений» ждёт этот мьютекс.
            try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void RegisterHotkeys()
    {
        foreach (var error in TryRegisterHotkeys())
        {
            Log.Error($"Горячая клавиша не зарегистрирована: {error}");
            _tray?.Notify("Горячая клавиша недоступна", error + ". Её можно поменять в настройках.", error: true);
        }
    }

    /// <summary>Регистрирует все сочетания заново. Пустое сочетание означает «не назначено».</summary>
    private List<string> TryRegisterHotkeys()
    {
        _hotkeys!.UnregisterAll();
        var h = _settings.Hotkeys;
        var errors = new List<string>();
        void add(string gesture, Action action)
        {
            if (string.IsNullOrWhiteSpace(gesture)) return;
            if (_hotkeys.Register(gesture, action) is { } error) errors.Add(error);
        }
        add(h.Region, CaptureRegion);
        add(h.Window, CaptureWindow);
        add(h.FullScreen, CaptureFullScreen);
        add(h.RepeatLast, RepeatLastRegion);
        return errors;
    }

    // ---- Сигналы между экземплярами ----------------------------------------------------

    private EventWaitHandle ListenForSignal(string name, Action action)
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        _waits.Add(ThreadPool.RegisterWaitForSingleObject(handle,
            (_, _) => Dispatcher.BeginInvoke(action), null, Timeout.Infinite, executeOnlyOnce: false));
        return handle;
    }

    private static bool Signal(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var handle))
            return false;
        using (handle)
            handle.Set();
        return true;
    }

    // ---- Захват ------------------------------------------------------------------------

    public void CaptureRegion() => RunCapture(RegionAsync);

    /// <summary>
    /// Захват из меню трея: ждём, пока меню исчезнет с экрана, иначе оно попадёт в кадр.
    /// </summary>
    public void FromTrayMenu(Action capture)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(250);
            capture();
        });
    }

    public void CaptureRegionDelayed(int seconds) => RunCapture(async () =>
    {
        await CountdownWindow.RunAsync(seconds);
        await RegionAsync();
    });

    public void CaptureWindow() => RunCapture(async () =>
    {
        var bounds = ScreenCapture.GetForegroundWindowBounds();
        if (bounds is null)
        {
            // Нет активного окна (рабочий стол, свёрнуто) — предлагаем выбрать область.
            await RegionAsync();
            return;
        }

        var snapshot = ScreenCapture.CaptureVirtualScreen();
        await DeliverInstantAsync(snapshot, bounds.Value);
    });

    public void CaptureFullScreen() => RunCapture(async () =>
    {
        var snapshot = ScreenCapture.CaptureVirtualScreen();
        var (x, y) = ScreenCapture.CursorPosition();
        var monitor = Monitors.FromPoint(snapshot.Monitors, x, y);
        await DeliverInstantAsync(snapshot, monitor.Bounds);
    });

    public void RepeatLastRegion() => RunCapture(async () =>
    {
        var snapshot = ScreenCapture.CaptureVirtualScreen();
        if (_settings.LastRegion is { } last
            && _settings.LastRegionLayout == snapshot.LayoutKey
            && !last.Intersect(snapshot.VirtualBounds).IsEmpty)
        {
            await DeliverInstantAsync(snapshot, last);
            return;
        }

        // Области ещё нет или мониторы поменялись — координаты могли «уехать», поэтому просим выделить заново.
        await RegionAsync(snapshot);
    });

    private async Task RegionAsync() => await RegionAsync(ScreenCapture.CaptureVirtualScreen());

    private async Task RegionAsync(Snapshot snapshot)
    {
        // Выделенная область сразу уходит в буфер обмена, без кнопки «Копировать».
        Func<PxRect, bool>? copyOnSelect = _settings.CopyOnSelect
            ? region => TryCopy(snapshot.Crop(region), notify: false)
            : null;
        _session = new CaptureSession(snapshot, copyOnSelect, _settings.EditorThickness);
        CaptureResult? result;
        try
        {
            result = await _session.RunAsync();
        }
        finally
        {
            _session = null;
        }

        if (result is null)
            return;

        _settings.LastRegion = result.Region;
        _settings.LastRegionLayout = snapshot.LayoutKey;
        _store.Save(_settings);

        BitmapSource image;
        try
        {
            image = ImageComposer.Compose(snapshot, result.Region, result.Annotations);
        }
        catch (Exception ex) when (ex is OutOfMemoryException or System.Runtime.InteropServices.COMException)
        {
            // Огромное выделение с правками может не влезть в память: отдаём хотя бы снимок без них.
            Log.Error("Не удалось наложить правки на снимок", ex);
            _tray?.Notify("Правки не сохранились", "Снимок слишком большой, сохранён без аннотаций.", error: true);
            image = snapshot.Crop(result.Region);
        }
        await DeliverAsync(image, result.Action == CaptureAction.Save ? QuickAction.Save : QuickAction.Copy);
    }

    /// <summary>
    /// Захват без оверлея (весь экран, окно, повтор области): сразу показываем вспышку с рамкой
    /// над снятой областью, чтобы было видно, что снимок сделан и что именно в него попало.
    /// </summary>
    private async Task DeliverInstantAsync(Snapshot snapshot, PxRect region)
    {
        var shown = region.Intersect(snapshot.VirtualBounds);
        if (!shown.IsEmpty)
            CaptureFlash.Show(snapshot.Monitors, shown);
        await DeliverAsync(snapshot.Crop(region), _settings.QuickAction);
    }

    private async Task DeliverAsync(BitmapSource image, QuickAction action)
    {
        bool copy = action is QuickAction.Copy or QuickAction.CopyAndSave;
        bool save = action is QuickAction.Save or QuickAction.CopyAndSave;

        // Сначала буфер обмена: это быстро, и снимок можно вставлять, пока файл ещё пишется.
        // Каждое действие обёрнуто отдельно: занятый буфер обмена не должен приводить к потере файла.
        bool copied = copy && TryCopy(image, notify: true);
        if (!save)
            return;

        try
        {
            // PNG большого экрана кодируется сотни миллисекунд — в фоне, чтобы не замирала вспышка.
            var path = await Task.Run(() => _output.Save(image));
            _tray?.Notify(copied ? "Снимок скопирован и сохранён" : "Снимок сохранён", path);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось сохранить снимок", ex);
            _tray?.Notify("Снимок не сохранён", (copied ? "В буфере обмена он есть. " : string.Empty) + ex.Message, error: true);
        }
    }

    private bool TryCopy(BitmapSource image, bool notify)
    {
        try
        {
            _output.CopyToClipboard(image);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось скопировать в буфер обмена", ex);
            if (notify)
                _tray?.Notify("Буфер обмена занят", "Снимок не скопирован. " + ex.Message, error: true);
            return false;
        }
    }

    /// <summary>
    /// Общая обёртка для всех захватов: не допускает двух захватов одновременно,
    /// ловит любые ошибки (снимок не должен ронять приложение) и освобождает память после.
    /// </summary>
    private async void RunCapture(Func<Task> capture)
    {
        if (_busy)
            return;
        _busy = true;
        // Уведомление прошлого снимка не должно перекрывать новое выделение (в сам снимок оно и так не попадает).
        ToastWindow.CloseAll();
        try
        {
            await capture();
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка захвата", ex);
            _tray?.Notify("Не удалось сделать снимок", ex.Message, error: true);
        }
        finally
        {
            _busy = false;
            Memory.Trim();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // SystemEvents вызывает обработчик в своём потоке — возвращаемся в UI-поток.
        Dispatcher.BeginInvoke(new Action(() => _session?.Cancel()));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Ошибка в UI-потоке", e.Exception);
        e.Handled = true;
        _session?.Cancel();
    }

    // ---- Настройки ---------------------------------------------------------------------

    public void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            open.Activate();
            return;
        }

        bool autostart;
        try { autostart = Autostart.IsEnabled; }
        catch (Exception ex) { Log.Error("Не удалось прочитать автозапуск", ex); autostart = false; }

        // Пока окно открыто, глобальные сочетания сняты: иначе нажатие текущего сочетания
        // в поле ввода запустило бы захват вместо того, чтобы записаться.
        _settingsWindow = new SettingsWindow(this, _settings, autostart);
        _hotkeys?.UnregisterAll();
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (!_uninstalled)
                RegisterHotkeys();
            Memory.Trim();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    IReadOnlyList<string> ISettingsHost.Apply(AppSettings draft, bool autostart)
    {
        var next = draft.Clone();
        next.LastRegion = _settings.LastRegion; // могла обновиться, пока окно было открыто
        next.LastRegionLayout = _settings.LastRegionLayout;
        next.UpdateRepository = _settings.UpdateRepository;
        _settings = next;
        _store.Save(_settings);
        _tray?.UpdateHotkeys(_settings.Hotkeys);

        var errors = new List<string>();
        try
        {
            if (Autostart.IsEnabled != autostart)
                Autostart.Set(autostart);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось изменить автозапуск", ex);
            errors.Add("Автозапуск не изменён: " + ex.Message);
        }

        // Проверяем, что сочетания свободны, и снова снимаем их, пока окно открыто.
        errors.AddRange(TryRegisterHotkeys());
        _hotkeys!.UnregisterAll();
        if (errors.Count > 0)
            errors.Add("Остальные настройки сохранены.");
        return errors;
    }

    void ISettingsHost.CheckForUpdatesInteractive() => CheckForUpdates();

    void ISettingsHost.Uninstall() => Uninstall();

    // ---- Обновления --------------------------------------------------------------------

    private async Task CheckForUpdatesQuietlyAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15)); // не мешаем запуску
            var info = await Updater.CheckAsync(_settings.EffectiveUpdateRepository);
            if (info is not null)
                _tray?.Notify("Доступна новая версия Shotly", $"Версия {info.Version}. Нажмите, чтобы установить.",
                    () => InstallUpdate(info, ask: false));
        }
        catch (Exception ex)
        {
            Log.Info($"Проверка обновлений не удалась: {ex.Message}");
        }
    }

    public async void CheckForUpdates()
    {
        if (_updating)
            return;
        _updating = true;
        UpdateInfo? info;
        try
        {
            info = await Updater.CheckAsync(_settings.EffectiveUpdateRepository);
        }
        catch (Exception ex)
        {
            Log.Error("Проверка обновлений", ex);
            ShowMessage("Не удалось проверить обновления.\n" + ex.Message, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            _updating = false;
        }

        if (info is null)
        {
            ShowMessage($"У вас последняя версия Shotly ({Installation.Version}).", MessageBoxImage.Information);
            return;
        }

        InstallUpdate(info, ask: true);
    }

    private async void InstallUpdate(UpdateInfo info, bool ask)
    {
        if (_updating)
            return;
        if (ask && (_settingsWindow is { IsVisible: true } w ? MessageBox.Show(w, $"Доступна версия {info.Version} (у вас {Installation.Version}).\n\nУстановить сейчас? Shotly перезапустится.",
                "Shotly", MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show($"Доступна версия {info.Version} (у вас {Installation.Version}).\n\nУстановить сейчас? Shotly перезапустится.",
                "Shotly", MessageBoxButton.YesNo, MessageBoxImage.Question)) != MessageBoxResult.Yes)
            return;

        _updating = true;
        try
        {
            _tray?.Notify("Загрузка обновления", $"Shotly {info.Version}…");
            var file = await Updater.DownloadAsync(info);
            Log.Info($"Установка обновления {info.Version}");
            Updater.ApplyAndRestart(file);
            Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось установить обновление", ex);
            ShowMessage("Не удалось установить обновление.\n" + ex.Message, MessageBoxImage.Warning);
            _updating = false;
        }
    }

    // ---- Удаление ----------------------------------------------------------------------

    public void Uninstall()
    {
        if (!ConfirmUninstall())
            return;
        _uninstalled = true;
        bool exeScheduled = Installation.Uninstall(_store);
        ShowUninstalled(_settings.SaveFolder, exeScheduled);
        Shutdown();
    }

    /// <summary>Вызов из «Параметры → Приложения → Установленные приложения» (exe запущен с --uninstall).</summary>
    private void UninstallFromSystemSettings()
    {
        if (!ConfirmUninstall())
        {
            Shutdown();
            return;
        }

        // Закрываем работающий экземпляр и ждём, пока он отпустит мьютекс (и exe-файл).
        Signal(ExitEventName);
        bool closed;
        using (var mutex = new Mutex(false, MutexName))
        {
            try
            {
                closed = mutex.WaitOne(TimeSpan.FromSeconds(10));
                if (closed)
                    mutex.ReleaseMutex();
            }
            catch (AbandonedMutexException)
            {
                closed = true; // прежний процесс завершился, не освободив мьютекс
            }
        }

        if (!closed)
        {
            ShowMessage("Shotly всё ещё запущен. Закройте его через значок в трее («Выход») и повторите удаление.",
                MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        var store = new SettingsStore();
        var folder = store.Load().SaveFolder;
        bool exeScheduled = Installation.Uninstall(store);
        ShowUninstalled(folder, exeScheduled);
        Shutdown();
    }

    private static bool ConfirmUninstall() =>
        MessageBox.Show("Удалить Shotly?\n\nБудут удалены программа, её настройки, автозапуск и запись в списке приложений. " +
                        "Сделанные снимки останутся на месте.",
            "Удаление Shotly", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ShowUninstalled(string folder, bool exeScheduled)
    {
        var text = "Shotly удалён. Ваши снимки остались в папке:\n" + folder;
        if (!exeScheduled)
            text += "\n\nФайл программы удалите вручную:\n" + Installation.ExePath;
        ShowMessage(text, MessageBoxImage.Information);
    }

    /// <summary>Сообщение поверх окна настроек, если оно открыто, иначе оно может оказаться под ним.</summary>
    private void ShowMessage(string text, MessageBoxImage icon)
    {
        if (_settingsWindow is { IsVisible: true } owner)
            MessageBox.Show(owner, text, "Shotly", MessageBoxButton.OK, icon);
        else
            MessageBox.Show(text, "Shotly", MessageBoxButton.OK, icon);
    }

    // ---- Прочее ------------------------------------------------------------------------

    public void OpenSaveFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.SaveFolder);
            StartShell(_settings.SaveFolder);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось открыть папку со снимками", ex);
            _tray?.Notify("Папка недоступна", ex.Message, error: true);
        }
    }

    void ITrayCommands.Exit() => Shutdown();

    private void StartShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Не удалось открыть {path}", ex);
        }
    }
}
