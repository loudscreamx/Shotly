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
    void SetFormat(ImageFormatKind format);
    void ChooseSaveFolder();
    void OpenSaveFolder();
    void OpenSettingsFile();
    void SetAutostart(bool enabled);
    void Exit();
}

/// <summary>Иконка в трее (WinForms NotifyIcon: самый надёжный вариант без сторонних пакетов).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _png;
    private readonly ToolStripMenuItem _jpg;
    private readonly ToolStripMenuItem _autostart;

    public TrayIcon(ITrayCommands commands, AppSettings settings)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Item("Область", settings.Hotkeys.Region, commands.CaptureRegion));
        menu.Items.Add(Item("Активное окно", settings.Hotkeys.Window, commands.CaptureWindow));
        menu.Items.Add(Item("Весь экран", settings.Hotkeys.FullScreen, commands.CaptureFullScreen));
        menu.Items.Add(Item("Повторить последнюю область", settings.Hotkeys.RepeatLast, commands.RepeatLastRegion));

        var delayed = new ToolStripMenuItem("С задержкой");
        foreach (var seconds in new[] { 3, 5, 10 })
            delayed.DropDownItems.Add(Item($"{seconds} с", null, () => commands.CaptureRegionDelayed(seconds)));
        menu.Items.Add(delayed);

        menu.Items.Add(new ToolStripSeparator());

        var format = new ToolStripMenuItem("Формат");
        _png = Item("PNG", null, () => commands.SetFormat(ImageFormatKind.Png));
        _jpg = Item("JPG", null, () => commands.SetFormat(ImageFormatKind.Jpg));
        format.DropDownItems.Add(_png);
        format.DropDownItems.Add(_jpg);
        menu.Items.Add(format);
        menu.Items.Add(Item("Папка сохранения…", null, commands.ChooseSaveFolder));
        menu.Items.Add(Item("Открыть папку со снимками", null, commands.OpenSaveFolder));
        menu.Items.Add(Item("Открыть файл настроек", null, commands.OpenSettingsFile));

        _autostart = new ToolStripMenuItem("Запускать при входе в Windows") { CheckOnClick = true };
        _autostart.Click += (_, _) => commands.SetAutostart(_autostart.Checked);
        menu.Items.Add(_autostart);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Выход", null, commands.Exit));

        menu.Opening += (_, _) => _autostart.Checked = SafeAutostartState();

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
                commands.CaptureRegion();
        };

        UpdateFormat(settings.Format);
    }

    public void UpdateFormat(ImageFormatKind format)
    {
        _png.Checked = format == ImageFormatKind.Png;
        _jpg.Checked = format == ImageFormatKind.Jpg;
    }

    public void Notify(string title, string text, bool error = false) =>
        _icon.ShowBalloonTip(3000, title, text, error ? ToolTipIcon.Error : ToolTipIcon.Info);

    private static ToolStripMenuItem Item(string text, string? shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text);
        if (!string.IsNullOrEmpty(shortcut))
            item.ShortcutKeyDisplayString = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private static bool SafeAutostartState()
    {
        try { return Autostart.IsEnabled; }
        catch (Exception ex) { Log.Error("Не удалось прочитать автозапуск", ex); return false; }
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
