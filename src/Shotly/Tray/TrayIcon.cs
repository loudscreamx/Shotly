using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Shotly.Core;
using Shotly.Native;

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
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;

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

    /// <summary>Иконка рисуется в коде: синий скруглённый квадрат с белой «рамкой выделения».</summary>
    private static Icon CreateIcon()
    {
        int size = SystemInformation.SmallIconSize.Width; // учитывает масштаб основного монитора
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float r = size * 0.25f;
            using var path = new GraphicsPath();
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(size - r - 1, 0, r, r, 270, 90);
            path.AddArc(size - r - 1, size - r - 1, r, r, 0, 90);
            path.AddArc(0, size - r - 1, r, r, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(0x3B, 0x9C, 0xFF));
            g.FillPath(fill, path);

            float inset = size * 0.25f;
            using var pen = new Pen(Color.White, Math.Max(1f, size / 12f)) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, inset, inset, size - 2 * inset - 1, size - 2 * inset - 1);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }
}
