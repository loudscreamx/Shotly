using System;
using System.Drawing;
using System.Windows.Forms;
using Shotly.Core;

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
    private readonly ToolStripMenuItem _region;
    private readonly ToolStripMenuItem _window;
    private readonly ToolStripMenuItem _full;
    private readonly ToolStripMenuItem _repeat;
    private Action? _balloonAction;

    public TrayIcon(ITrayCommands commands, AppSettings settings)
    {
        var menu = new ContextMenuStrip();
        Action later(Action a) => () => commands.FromTrayMenu(a);
        _region = Item("Область", null, later(commands.CaptureRegion));
        _window = Item("Активное окно", null, later(commands.CaptureWindow));
        _full = Item("Весь экран", null, later(commands.CaptureFullScreen));
        _repeat = Item("Повторить последнюю область", null, later(commands.RepeatLastRegion));
        menu.Items.Add(_region);
        menu.Items.Add(_window);
        menu.Items.Add(_full);
        menu.Items.Add(_repeat);

        var delayed = new ToolStripMenuItem("С задержкой");
        foreach (var seconds in new[] { 3, 5, 10 })
            delayed.DropDownItems.Add(Item($"{seconds} с", null, later(() => commands.CaptureRegionDelayed(seconds))));
        menu.Items.Add(delayed);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Открыть папку со снимками", null, commands.OpenSaveFolder));
        var settingsItem = Item("Настройки…", null, commands.OpenSettings);
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        menu.Items.Add(settingsItem);
        menu.Items.Add(Item("Проверить обновления", null, commands.CheckForUpdates));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Удалить Shotly…", null, commands.Uninstall));
        menu.Items.Add(Item("Выход", null, commands.Exit));

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
    }

    /// <summary>Подписи сочетаний в меню после изменения настроек.</summary>
    public void UpdateHotkeys(HotkeySettings h)
    {
        _region.ShortcutKeyDisplayString = h.Region;
        _window.ShortcutKeyDisplayString = h.Window;
        _full.ShortcutKeyDisplayString = h.FullScreen;
        _repeat.ShortcutKeyDisplayString = h.RepeatLast;
    }

    /// <summary>Уведомление, по щелчку на которое выполняется действие (например, установка обновления).</summary>
    public void Notify(string title, string text, Action onClick)
    {
        Notify(title, text);
        _balloonAction = onClick;
    }

    public void Notify(string title, string text, bool error = false)
    {
        _balloonAction = null;
        try
        {
            _icon.ShowBalloonTip(3000, title, string.IsNullOrWhiteSpace(text) ? title : text,
                error ? ToolTipIcon.Error : ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось показать уведомление", ex);
        }
    }

    private static ToolStripMenuItem Item(string text, string? shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text);
        if (!string.IsNullOrEmpty(shortcut))
            item.ShortcutKeyDisplayString = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Знак Shotly из встроенного .ico: Windows выбирает кадр под размер иконок трея с учётом масштаба.</summary>
    private static Icon CreateIcon()
    {
        int size = SystemInformation.SmallIconSize.Width; // учитывает масштаб основного монитора
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Shotly.ico");
        if (stream is null)
            return (Icon)SystemIcons.Application.Clone();
        return new Icon(stream, size, size);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
