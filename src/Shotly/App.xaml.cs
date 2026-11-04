using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Shotly.Capture;
using Shotly.Core;
using Shotly.Tray;
using Shotly.UI;

namespace Shotly;

public partial class App : Application, ITrayCommands
{
    private Mutex? _singleInstance;
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

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Shotly.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Уже запущен — второй экземпляр просто выходит (хоткеи всё равно заняты первым).
            Shutdown();
            return;
        }

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

        Log.Info($"Shotly {typeof(App).Assembly.GetName().Version} запущен");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void RegisterHotkeys()
    {
        var h = _settings.Hotkeys;
        var errors = new[]
        {
            _hotkeys!.Register(h.Region, CaptureRegion),
            _hotkeys.Register(h.Window, CaptureWindow),
            _hotkeys.Register(h.FullScreen, CaptureFullScreen),
            _hotkeys.Register(h.RepeatLast, RepeatLastRegion),
        };

        foreach (var error in errors)
        {
            if (error is null) continue;
            Log.Error($"Горячая клавиша не зарегистрирована: {error}");
            _tray?.Notify("Горячая клавиша недоступна", error + ". Её можно поменять в файле настроек.", error: true);
        }
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
        Deliver(snapshot, bounds.Value, _settings.QuickAction);
    });

    public void CaptureFullScreen() => RunCapture(() =>
    {
        var snapshot = ScreenCapture.CaptureVirtualScreen();
        var (x, y) = ScreenCapture.CursorPosition();
        var monitor = Monitors.FromPoint(snapshot.Monitors, x, y);
        Deliver(snapshot, monitor.Bounds, _settings.QuickAction);
        return Task.CompletedTask;
    });

    public void RepeatLastRegion() => RunCapture(async () =>
    {
        var snapshot = ScreenCapture.CaptureVirtualScreen();
        if (_settings.LastRegion is { } last
            && _settings.LastRegionLayout == snapshot.LayoutKey
            && !last.Intersect(snapshot.VirtualBounds).IsEmpty)
        {
            Deliver(snapshot, last, _settings.QuickAction);
            return;
        }

        // Области ещё нет или мониторы поменялись — координаты могли «уехать», поэтому просим выделить заново.
        await RegionAsync(snapshot);
    });

    private async Task RegionAsync() => await RegionAsync(ScreenCapture.CaptureVirtualScreen());

    private async Task RegionAsync(Snapshot snapshot)
    {
        _session = new CaptureSession(snapshot);
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

        Deliver(snapshot, result.Region, result.Action == CaptureAction.Save ? QuickAction.Save : QuickAction.Copy);
    }

    private void Deliver(Snapshot snapshot, PxRect region, QuickAction action)
    {
        var image = snapshot.Crop(region);

        // Сначала сохраняем, потом копируем, и каждое действие отдельно:
        // занятый буфер обмена не должен приводить к потере снимка.
        if (action is QuickAction.Save or QuickAction.CopyAndSave)
        {
            try
            {
                var path = _output.Save(image);
                _tray?.Notify("Снимок сохранён", path);
            }
            catch (Exception ex)
            {
                Log.Error("Не удалось сохранить снимок", ex);
                _tray?.Notify("Снимок не сохранён", ex.Message, error: true);
            }
        }

        if (action is QuickAction.Copy or QuickAction.CopyAndSave)
        {
            try
            {
                _output.CopyToClipboard(image);
            }
            catch (Exception ex)
            {
                Log.Error("Не удалось скопировать в буфер обмена", ex);
                _tray?.Notify("Буфер обмена занят", "Снимок не скопирован. " + ex.Message, error: true);
            }
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

    // ---- Настройки из меню трея --------------------------------------------------------

    public void SetFormat(ImageFormatKind format)
    {
        _settings.Format = format;
        _store.Save(_settings);
        _tray?.UpdateFormat(format);
    }

    public void ChooseSaveFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Папка для снимков" };
        if (Directory.Exists(_settings.SaveFolder))
            dialog.InitialDirectory = _settings.SaveFolder;
        if (dialog.ShowDialog() == true)
        {
            _settings.SaveFolder = dialog.FolderName;
            _store.Save(_settings);
        }
    }

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

    public void OpenSettingsFile() => StartShell(_store.FilePath);

    public void SetAutostart(bool enabled)
    {
        try
        {
            Autostart.Set(enabled);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось изменить автозапуск", ex);
            _tray?.Notify("Автозапуск не изменён", ex.Message, error: true);
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
