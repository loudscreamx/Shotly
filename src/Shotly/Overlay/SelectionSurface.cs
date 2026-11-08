using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shotly.Capture;
using Shotly.Core;
using Shotly.UI;

namespace Shotly.Overlay;

/// <summary>
/// Отрисовка оверлея одного монитора: замороженный кадр, затемнение вокруг выделения,
/// рамка, размер в пикселях и подсказка. Всё рисуется в одном OnRender, без дерева элементов,
/// поэтому перерисовка на каждое движение мыши дешёвая.
/// </summary>
public sealed class SelectionSurface : FrameworkElement
{
    // Цвета палитры Ember (брендбук): рамка Ember, плашки Coal, затемнение тёплым «углём», а не чистым чёрным.
    private static readonly Brush DimBrush = Brand.Solid(Brand.Coal, 0x80);                 // ~50%
    private static readonly Brush PillBrush = Brand.Solid(Brand.Coal, 0xEB);
    private static readonly Pen PillPen = Frozen(new Pen(Brand.Solid(Colors.White, 0x1F), 1));
    private static readonly Brush TextBrush = Brand.TextBrush;
    private static readonly Brush QuietTextBrush = Brand.Solid(Brand.Text, 0xD0);
    private static readonly Brush KeyBrush = Brand.AmberBrush;
    private static readonly Pen AccentPen = Frozen(new Pen(Brand.EmberBrush, 2));
    private static readonly Pen CrosshairPen = Frozen(new Pen(Brand.Solid(Brand.Cream, 0x80), 1)
    {
        DashStyle = DashStyles.Dash,
    });

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

            if (_session.Editor is { } editor && !visible.IsEmpty)
                DrawAnnotations(dc, editor, visible);

            // Размер подписываем только на мониторе с левым верхним углом выделения; в режиме правки он не нужен.
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
        var text = Text($"{sel.Width} × {sel.Height}", 12, TextBrush, Brand.TextFaceSemiBold);
        var pill = new Size(text.Width + 18, text.Height + 8);

        double x = Math.Clamp(local.X, 4, Math.Max(4, ActualWidth - pill.Width - 4));
        double y = local.Y - pill.Height - 6;
        if (y < 4)
            y = local.Y + 6; // нет места сверху — рисуем внутри выделения

        dc.DrawRoundedRectangle(PillBrush, PillPen, new Rect(new Point(x, y), pill), 7, 7);
        dc.DrawText(text, new Point(x + 9, y + 4));
    }

    private void DrawCrosshair(DrawingContext dc, double w, double h)
    {
        var p = ToLocal(_session.Cursor.X, _session.Cursor.Y);
        dc.DrawLine(CrosshairPen, new Point(0, p.Y + 0.5), new Point(w, p.Y + 0.5));
        dc.DrawLine(CrosshairPen, new Point(p.X + 0.5, 0), new Point(p.X + 0.5, h));
    }

    /// <summary>Подсказка сверху: знак Shotly, действие и клавиши (клавиши выделены янтарным).</summary>
    private void DrawHint(DrawingContext dc, double w)
    {
        const double markSize = 20, gap = 10, padX = 14, padY = 9;
        var parts = new (string Text, bool Key)[]
        {
            ("Выделите область", false), ("  ·  ", false), ("клик", true), (" — весь монитор", false),
            ("  ·  ", false), ("Esc", true), (" — отмена", false),
        };

        var texts = new FormattedText[parts.Length];
        double textWidth = 0, textHeight = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            texts[i] = Text(parts[i].Text, 13, parts[i].Key ? KeyBrush : QuietTextBrush,
                parts[i].Key ? Brand.TextFaceSemiBold : Brand.TextFace);
            textWidth += texts[i].WidthIncludingTrailingWhitespace;
            textHeight = Math.Max(textHeight, texts[i].Height);
        }

        var pill = new Size(padX + markSize + gap + textWidth + padX, Math.Max(markSize, textHeight) + 2 * padY);
        var origin = new Point(Math.Round((w - pill.Width) / 2), 24);
        dc.DrawRoundedRectangle(PillBrush, PillPen, new Rect(origin, pill), pill.Height / 2, pill.Height / 2);
        dc.DrawImage(Brand.Mark, new Rect(origin.X + padX, origin.Y + (pill.Height - markSize) / 2, markSize, markSize));

        double x = origin.X + padX + markSize + gap;
        double y = origin.Y + (pill.Height - textHeight) / 2;
        foreach (var t in texts)
        {
            dc.DrawText(t, new Point(x, y));
            x += t.WidthIncludingTrailingWhitespace;
        }
    }

    private FormattedText Text(string value, double size, Brush brush, Typeface face) =>
        new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
