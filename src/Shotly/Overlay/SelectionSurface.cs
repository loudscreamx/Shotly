using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shotly.Capture;
using Shotly.Core;

namespace Shotly.Overlay;

/// <summary>
/// Отрисовка оверлея одного монитора: замороженный кадр, затемнение вокруг выделения,
/// рамка, размер в пикселях и подсказка. Всё рисуется в одном OnRender, без дерева элементов,
/// поэтому перерисовка на каждое движение мыши дешёвая.
/// </summary>
public sealed class SelectionSurface : FrameworkElement
{
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)));       // ~45%
    private static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3B, 0x9C, 0xFF)));
    private static readonly Brush PillBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0x1F, 0x1F, 0x1F)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)));
    private static readonly Brush QuietTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)));
    private static readonly Pen AccentPen = Frozen(new Pen(AccentBrush, 1.5));
    private static readonly Pen CrosshairPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF))), 1)
    {
        DashStyle = DashStyles.Dash,
    });
    private static readonly Typeface UiFont = new("Segoe UI Variable Text, Segoe UI");

    private readonly CaptureSession _session;
    private readonly MonitorInfo _monitor;
    private readonly BitmapSource _background;

    public SelectionSurface(CaptureSession session, MonitorInfo monitor, BitmapSource background)
    {
        _session = session;
        _monitor = monitor;
        _background = background;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>Физические пиксели рабочего стола → DIP этого окна. Масштаб считаем из фактического размера окна.</summary>
    public Rect ToLocal(PxRect r)
    {
        double sx = _monitor.Bounds.Width / Math.Max(1, ActualWidth);
        double sy = _monitor.Bounds.Height / Math.Max(1, ActualHeight);
        return new Rect((r.X - _monitor.Bounds.X) / sx, (r.Y - _monitor.Bounds.Y) / sy, r.Width / sx, r.Height / sy);
    }

    private Point ToLocal(int x, int y)
    {
        var r = ToLocal(new PxRect(x, y, 0, 0));
        return new Point(r.X, r.Y);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        var full = new Rect(0, 0, w, h);
        dc.DrawImage(_background, full);

        PxRect? selection = _session.Selection;
        var visible = selection is { } s ? s.Intersect(_monitor.Bounds) : default;

        // Затемнение с «дыркой» под выделение.
        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry(full));
        if (!visible.IsEmpty)
            dim.Children.Add(new RectangleGeometry(ToLocal(visible)));
        dc.DrawGeometry(DimBrush, null, dim);

        if (selection is { } sel && !sel.IsEmpty)
        {
            var local = ToLocal(sel);
            var half = AccentPen.Thickness / 2;
            // Рамка рисуется снаружи выделения и не перекрывает пиксели, которые попадут в снимок.
            dc.DrawRectangle(null, AccentPen, new Rect(local.X - half, local.Y - half, local.Width + 2 * half, local.Height + 2 * half));

            // Размер подписываем только на мониторе, где лежит левый верхний угол выделения.
            if (_session.Editor is { } editor && !visible.IsEmpty)
                DrawAnnotations(dc, editor, visible);

            // В режиме правки размер уже не нужен и только мешает.
            if (_session.State != SelectionState.Editing
                && Monitors.FromPoint(_session.Snapshot.Monitors, sel.X, sel.Y) == _monitor)
                DrawSizeLabel(dc, local, sel);
        }
        else if (_monitor.Bounds.Contains(_session.Cursor.X, _session.Cursor.Y))
        {
            DrawCrosshair(dc, w, h);
            DrawHint(dc, w);
        }
    }

    /// <summary>
    /// Аннотации хранятся в физических пикселях; переводим их в DIP этого окна одной матрицей
    /// и обрезаем по видимой на этом мониторе части выделения.
    /// </summary>
    private void DrawAnnotations(DrawingContext dc, Shotly.Editor.EditorState editor, PxRect visible)
    {
        double sx = _monitor.Bounds.Width / Math.Max(1, ActualWidth);
        double sy = _monitor.Bounds.Height / Math.Max(1, ActualHeight);
        dc.PushClip(new RectangleGeometry(ToLocal(visible)));
        dc.PushTransform(new MatrixTransform(1 / sx, 0, 0, 1 / sy, -_monitor.Bounds.X / sx, -_monitor.Bounds.Y / sy));
        foreach (var a in editor.Visible)
            a.Render(dc);
        dc.Pop();
        dc.Pop();
    }

    private void DrawSizeLabel(DrawingContext dc, Rect local, PxRect sel)
    {
        var text = Text($"{sel.Width} × {sel.Height}", 12, TextBrush);
        var pill = new Size(text.Width + 16, text.Height + 8);

        double x = Math.Clamp(local.X, 4, Math.Max(4, ActualWidth - pill.Width - 4));
        double y = local.Y - pill.Height - 6;
        if (y < 4)
            y = local.Y + 6; // нет места сверху — рисуем внутри выделения

        dc.DrawRoundedRectangle(PillBrush, null, new Rect(new Point(x, y), pill), 6, 6);
        dc.DrawText(text, new Point(x + 8, y + 4));
    }

    private void DrawCrosshair(DrawingContext dc, double w, double h)
    {
        var p = ToLocal(_session.Cursor.X, _session.Cursor.Y);
        dc.DrawLine(CrosshairPen, new Point(0, p.Y + 0.5), new Point(w, p.Y + 0.5));
        dc.DrawLine(CrosshairPen, new Point(p.X + 0.5, 0), new Point(p.X + 0.5, h));
    }

    private void DrawHint(DrawingContext dc, double w)
    {
        var text = Text("Выделите область  ·  клик — весь монитор  ·  Esc — отмена", 13, QuietTextBrush);
        var pill = new Size(text.Width + 24, text.Height + 12);
        var origin = new Point((w - pill.Width) / 2, 24);
        dc.DrawRoundedRectangle(PillBrush, null, new Rect(origin, pill), 8, 8);
        dc.DrawText(text, new Point(origin.X + 12, origin.Y + 6));
    }

    private FormattedText Text(string value, double size, Brush brush) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiFont, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
