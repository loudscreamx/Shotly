using System;
using System.Drawing;
using System.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Shotly.Core;
using Shotly.Localization;
using Shotly.UI;

namespace Shotly.Tray;

/// <summary>Действия, которые меню трея передаёт приложению.</summary>
public interface ITrayCommands
{
    void CaptureRegion();
    void CaptureWindow();
    void CaptureFullScreen();
    void RepeatLastRegion();
    void CaptureRegionDelayed(int seconds);
    void OpenSaveFolder();
    void OpenSettings();
    void CheckForUpdates();
    void Uninstall();
    void Exit();

    /// <summary>Запустить захват после того, как меню трея исчезнет с экрана.</summary>
    void FromTrayMenu(Action capture);
}

/// <summary>Иконка в трее (WinForms NotifyIcon: самый надёжный вариант без сторонних пакетов).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly ToolStripMenuItem _region;
    private readonly ToolStripMenuItem _window;
    private readonly ToolStripMenuItem _full;
    private readonly ToolStripMenuItem _repeat;
    private readonly int _iconPixels;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _delayed;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly Font _systemFont;
    /// <summary>Пункты меню и откуда брать их текст: при смене языка меню переписывается на месте.</summary>
    private readonly System.Collections.Generic.List<(ToolStripItem Item, Func<string> Text)> _labels = new();
    private Action? _balloonAction;

    public TrayIcon(ITrayCommands commands, AppSettings settings)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            ShowImageMargin = true,
            Padding = new Padding(0, 4, 0, 4),
        };
        // Значки рисуются вектором под масштаб основного монитора (как и иконка трея).
        _iconPixels = SystemInformation.SmallIconSize.Width;
        menu.ImageScalingSize = new Size(_iconPixels, _iconPixels);
        _menu = menu;
        _systemFont = menu.Font;
        menu.Font = TrayFonts.Menu(_systemFont);
        menu.HandleCreated += (_, _) => RoundCorners(menu.Handle);

        Action later(Action a) => () => commands.FromTrayMenu(a);

        // Шапка: знак Orbit, название Unbounded и версия справа.
        var header = new ToolStripMenuItem("Shotly")
        {
            Tag = TrayMenuRenderer.HeaderTag,
            Image = Icons.Bitmap(Brand.Mark, _iconPixels),
            Font = TrayFonts.Header(_systemFont),
            ShortcutKeyDisplayString = Installation.Version,
            Padding = new Padding(0, 3, 0, 3),
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        _region = Item(() => L.MenuRegion, Glyph.Region, later(commands.CaptureRegion));
        _window = Item(() => L.MenuWindow, Glyph.Window, later(commands.CaptureWindow));
        _full = Item(() => L.MenuFullScreen, Glyph.Screen, later(commands.CaptureFullScreen));
        _repeat = Item(() => L.MenuRepeat, Glyph.Repeat, later(commands.RepeatLastRegion));
        menu.Items.Add(_region);
        menu.Items.Add(_window);
        menu.Items.Add(_full);
        menu.Items.Add(_repeat);

        var delayed = _delayed = Item(() => L.MenuDelayed, Glyph.Timer, null);
        delayed.DropDown.Renderer = menu.Renderer;
        delayed.DropDown.Font = menu.Font;
        delayed.DropDown.HandleCreated += (_, _) => RoundCorners(delayed.DropDown.Handle);
        if (delayed.DropDown is ToolStripDropDownMenu dropDown)
            dropDown.ShowImageMargin = false;
        foreach (var seconds in new[] { 3, 5, 10 })
            delayed.DropDownItems.Add(Item(() => L.MenuSeconds(seconds), null, later(() => commands.CaptureRegionDelayed(seconds))));
        menu.Items.Add(delayed);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item(() => L.MenuOpenFolder, Glyph.Folder, commands.OpenSaveFolder));
        var settingsItem = _settingsItem = Item(() => L.MenuSettings, Glyph.Settings, commands.OpenSettings);
        settingsItem.Font = TrayFonts.MenuBold(_systemFont);
        menu.Items.Add(settingsItem);
        menu.Items.Add(Item(() => L.MenuCheckUpdates, Glyph.Update, commands.CheckForUpdates));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item(() => L.MenuUninstall, Glyph.Delete, commands.Uninstall));
        menu.Items.Add(Item(() => L.MenuExit, Glyph.Exit, commands.Exit));

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Shotly",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                commands.FromTrayMenu(commands.CaptureRegion);
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonAction;
            _balloonAction = null;
            action?.Invoke();
        };

        UpdateHotkeys(settings.Hotkeys);

        // Тема панели задач и масштаб могут поменяться на ходу — перерисовываем значок.
        SystemEvents.UserPreferenceChanged += OnSystemChanged;
        SystemEvents.DisplaySettingsChanged += OnSystemChanged;
    }

    private void OnSystemChanged(object? sender, EventArgs e)
    {
        // SystemEvents вызывает обработчик в своём потоке — возвращаемся в UI-поток.
        if (_ui is not null)
            _ui.Post(_ => RefreshIcon(), null);
        else
            RefreshIcon();
    }

    private void RefreshIcon()
    {
        try
        {
            var old = _icon.Icon;
            _icon.Icon = CreateIcon();
            old?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось обновить значок в трее", ex);
        }
    }

    /// <summary>
    /// Переписывает меню на текущем языке. Значок и само меню не пересоздаются,
    /// чтобы иконка не прыгала в трее. Шрифт тоже меняется: у китайского, японского и корейского он свой.
    /// </summary>
    public void UpdateLanguage()
    {
        // Шапка не меняется: в ней только «Shotly» латиницей и номер версии.
        _menu.Font = TrayFonts.Menu(_systemFont);
        _delayed.DropDown.Font = _menu.Font;
        _settingsItem.Font = TrayFonts.MenuBold(_systemFont);
        foreach (var (item, text) in _labels)
            item.Text = text();
    }

    /// <summary>Подписи сочетаний в меню после изменения настроек.</summary>
    public void UpdateHotkeys(HotkeySettings h)
    {
        _region.ShortcutKeyDisplayString = HotkeyManager.Display(h.Region);
        _window.ShortcutKeyDisplayString = HotkeyManager.Display(h.Window);
        _full.ShortcutKeyDisplayString = HotkeyManager.Display(h.FullScreen);
        _repeat.ShortcutKeyDisplayString = HotkeyManager.Display(h.RepeatLast);
    }

    /// <summary>Уведомление, по щелчку на которое выполняется действие (например, установка обновления).</summary>
    public void Notify(string title, string text, Action onClick) => Show(title, text, error: false, onClick);

    public void Notify(string title, string text, bool error = false) => Show(title, text, error, null);

    /// <summary>
    /// Фирменное уведомление Shotly; если его окно не удалось показать — системное уведомление трея.
    /// </summary>
    private void Show(string title, string text, bool error, Action? onClick)
    {
        if (ToastWindow.TryShow(title, text, error, onClick))
            return;

        _balloonAction = onClick;
        try
        {
            _icon.ShowBalloonTip(3000, title, string.IsNullOrWhiteSpace(text) ? title : text,
                error ? ToolTipIcon.Error : ToolTipIcon.None);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось показать уведомление", ex);
        }
    }

    private ToolStripMenuItem Item(Func<string> text, Glyph? glyph, Action? action)
    {
        var item = new ToolStripMenuItem(text())
        {
            Padding = new Padding(0, 3, 0, 3),
        };
        _labels.Add((item, text));
        if (glyph is { } g)
            item.Image = Icons.Bitmap(g, Brand.Text, _iconPixels);
        if (action is not null)
            item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Скруглённые углы меню в Windows 11 (на Windows 10 атрибут не поддерживается, вызов ничего не делает).</summary>
    private static void RoundCorners(IntPtr hwnd)
    {
        try
        {
            int round = DWMWCP_ROUNDSMALL;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUNDSMALL = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Трей-глиф Shotly: отдельный упрощённый знак на всё поле, кадры 16–64 px отрисованы
    /// под каждый размер. Цвета подобраны под тёмную или светлую панель задач.
    /// </summary>
    private static Icon CreateIcon()
    {
        int size = SystemInformation.SmallIconSize.Width; // учитывает масштаб основного монитора
        string name = TaskbarIsLight() ? "TrayLight.ico" : "TrayDark.ico";
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(name)
            ?? typeof(TrayIcon).Assembly.GetManifestResourceStream("Shotly.ico");
        if (stream is null)
            return (Icon)SystemIcons.Application.Clone();
        return new Icon(stream, size, size);
    }

    /// <summary>Светлая ли панель задач (Параметры → Персонализация → Цвета → режим Windows).</summary>
    private static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось прочитать тему Windows", ex);
            return false; // по умолчанию в Windows 10/11 панель тёмная
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemChanged;
        SystemEvents.DisplaySettingsChanged -= OnSystemChanged;
        ToastWindow.CloseAll();
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
