using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Shotly.Core;

namespace Shotly.Tray;

/// <summary>
/// Тёмное меню трея по брендбуку: фон Coal, подсветка пункта тёплым серым, значки и текст светлые.
/// Системное меню WinForms светлое и с синей подсветкой, поэтому рисуем его сами.
/// </summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    public static readonly Color Coal = Color.FromArgb(0x1C, 0x19, 0x17);
    public static readonly Color Hover = Color.FromArgb(0x2F, 0x29, 0x26);
    public static readonly Color Line = Color.FromArgb(0x3D, 0x35, 0x32);
    public static readonly Color Text = Color.FromArgb(0xF5, 0xEE, 0xE8);
    public static readonly Color Muted = Color.FromArgb(0xA8, 0x9F, 0x98);

    /// <summary>Пункт-шапка со знаком и названием: не подсвечивается при наведении.</summary>
    public const string HeaderTag = "header";

    public TrayMenuRenderer() : base(new Palette())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Coal);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Line);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Полоса под значки того же цвета, что и меню: без светлой колонки слева.
        using var brush = new SolidBrush(Coal);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled || HeaderTag.Equals(e.Item.Tag))
            return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = RoundedRect(r, 5);
        using var brush = new SolidBrush(Hover);
        g.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool shortcut = e.Item is ToolStripMenuItem m && e.Text == m.ShortcutKeyDisplayString && !string.IsNullOrEmpty(e.Text);
        e.TextColor = !e.Item.Enabled || shortcut ? Muted : Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is { Selected: true } ? Text : Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Line);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Цвета для того, что базовый рендерер рисует сам (подменю, рамки).</summary>
    private sealed class Palette : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Coal;
        public override Color ImageMarginGradientBegin => Coal;
        public override Color ImageMarginGradientMiddle => Coal;
        public override Color ImageMarginGradientEnd => Coal;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Line;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
    }
}

/// <summary>
/// Фирменные шрифты для WinForms. WPF читает их из ресурсов сам, а меню трея рисует GDI,
/// поэтому те же файлы регистрируются в процессе (только для Shotly, в систему не ставятся).
/// Если что-то не получилось, меню остаётся на системном шрифте.
/// </summary>
internal static class TrayFonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static bool _loaded;

    public static Font Menu(Font fallback) => Create("Onest", 9f, FontStyle.Regular, fallback);
    public static Font MenuBold(Font fallback) => Create("Onest", 9f, FontStyle.Bold, fallback);
    public static Font Header(Font fallback) => Create("Unbounded", 9.5f, FontStyle.Bold, fallback);

    private static Font Create(string family, float size, FontStyle style, Font fallback)
    {
        EnsureLoaded();
        try
        {
            foreach (var f in Collection.Families)
                if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase) && f.IsStyleAvailable(style))
                    return new Font(f, size, style, GraphicsUnit.Point);
        }
        catch (Exception ex)
        {
            Log.Error($"Шрифт {family} недоступен в меню", ex);
        }
        return new Font(fallback, style);
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;

        foreach (var file in new[] { "Onest-Regular.ttf", "Onest-Bold.ttf", "Unbounded-Bold.ttf" })
        {
            try
            {
                var info = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Shotly;component/Assets/Fonts/{file}"));
                if (info is null)
                    continue;
                byte[] data;
                using (var stream = info.Stream)
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    data = copy.ToArray();
                }

                // Память под шрифт живёт до конца процесса: GDI+ и GDI читают её, пока шрифт используется.
                IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, memory, data.Length);
                Collection.AddMemoryFont(memory, data.Length);
                uint count = 0;
                AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
            }
            catch (Exception ex)
            {
                Log.Error($"Не удалось загрузить шрифт {file}", ex);
            }
        }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);
}
