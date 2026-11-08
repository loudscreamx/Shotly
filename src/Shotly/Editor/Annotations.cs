using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shotly.Core;

namespace Shotly.Editor;

/// <summary>
/// Аннотация поверх снимка. Все координаты и толщины — в физических пикселях рабочего стола,
/// поэтому одна и та же отрисовка используется и в оверлее (через масштаб в DIP),
/// и при сохранении итоговой картинки (1:1).
/// </summary>
public abstract class Annotation
{
    protected Annotation(Color color) => Color = color;

    public Color Color { get; }

    /// <summary>Пустая аннотация (клик без перетаскивания, пустой текст) не попадает в историю.</summary>
    public abstract bool IsEmpty { get; }

    public abstract void Render(DrawingContext dc);

    protected static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>Аннотация, которая строится перетаскиванием от точки до точки.</summary>
public abstract class DragAnnotation : Annotation
{
    protected DragAnnotation(Color color, Point start, double thickness) : base(color)
    {
        Start = start;
        End = start;
        Thickness = thickness;
    }

    public Point Start { get; }
    public Point End { get; set; }
    public double Thickness { get; }

    public override bool IsEmpty => (End - Start).Length < 3;
}

public sealed class ArrowAnnotation : DragAnnotation
{
    public ArrowAnnotation(Color color, Point start, double thickness) : base(color, start, thickness) { }

    public override void Render(DrawingContext dc)
    {
        var v = End - Start;
        double len = v.Length;
        if (len < 1)
            return;

        v /= len;
        // Крупный наконечник с выемкой: стрелку видно сразу, даже на пёстром фоне и в уменьшенной картинке.
        double head = Math.Min(len * 0.7, Thickness * 4 + 8);
        double halfWidth = head * 0.5;
        var normal = new Vector(-v.Y, v.X);
        var baseCenter = End - v * head;
        var notch = baseCenter + v * (head * 0.22);

        var brush = Frozen(new SolidColorBrush(Color));
        var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Flat };
        pen.Freeze();

        // Линия заканчивается внутри наконечника, чтобы её конец не торчал из острия и выемки.
        if ((notch - Start).Length > 0.5)
            dc.DrawLine(pen, Start, notch + v * Math.Min(head * 0.2, Thickness));

        var tip = new StreamGeometry();
        using (var ctx = tip.Open())
        {
            ctx.BeginFigure(End, isFilled: true, isClosed: true);
            ctx.LineTo(baseCenter + normal * halfWidth, true, true);
            ctx.LineTo(notch, true, true);
            ctx.LineTo(baseCenter - normal * halfWidth, true, true);
        }
        tip.Freeze();
        // Тонкая обводка тем же цветом слегка скругляет углы наконечника.
        var soft = new Pen(brush, Math.Max(1, Thickness * 0.35)) { LineJoin = PenLineJoin.Round };
        soft.Freeze();
        dc.DrawGeometry(brush, soft, tip);
    }
}

public sealed class RectAnnotation : DragAnnotation
{
    public RectAnnotation(Color color, Point start, double thickness) : base(color, start, thickness) { }

    public override bool IsEmpty => Math.Abs(End.X - Start.X) < 3 || Math.Abs(End.Y - Start.Y) < 3;

    public override void Render(DrawingContext dc)
    {
        var pen = new Pen(Frozen(new SolidColorBrush(Color)), Thickness) { LineJoin = PenLineJoin.Miter };
        pen.Freeze();
        dc.DrawRectangle(null, pen, new Rect(Start, End));
    }
}

/// <summary>Штрих от руки: копит точки и строит из них одну линию.</summary>
public abstract class StrokeAnnotation : Annotation
{
    private readonly List<Point> _points = new();

    protected StrokeAnnotation(Color color, Point start, double thickness) : base(color)
    {
        Thickness = thickness;
        _points.Add(start);
    }

    public double Thickness { get; }

    public override bool IsEmpty => false; // даже точка — осознанное действие

    public void Add(Point p)
    {
        // Не копим тысячи точек при медленном движении мыши.
        if ((p - _points[^1]).Length >= 1.5)
            _points.Add(p);
    }

    /// <summary>
    /// Рисует весь штрих одной геометрией: самопересечения не темнеют (маркер)
    /// и не дают швов (кисть размытия).
    /// </summary>
    protected void RenderStroke(DrawingContext dc, Brush brush)
    {
        var pen = new Pen(brush, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_points[0], isFilled: false, isClosed: false);
            if (_points.Count == 1)
                ctx.LineTo(_points[0] + new Vector(0.1, 0), true, true); // точка → круглая клякса
            else
                ctx.PolyLineTo(_points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>Полупрозрачный маркер от руки.</summary>
public sealed class MarkerAnnotation : StrokeAnnotation
{
    public MarkerAnnotation(Color color, Point start, double thickness) : base(color, start, thickness) { }

    public override void Render(DrawingContext dc) =>
        RenderStroke(dc, Frozen(new SolidColorBrush(Color.FromArgb(0x70, Color.R, Color.G, Color.B))));
}

/// <summary>Текст с тонкой контрастной обводкой, чтобы читался на любом фоне.</summary>
public sealed class TextAnnotation : Annotation
{
    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public TextAnnotation(Color color, Point origin, double fontSize) : base(color)
    {
        Origin = origin;
        FontSize = fontSize;
    }

    public Point Origin { get; }
    public double FontSize { get; }
    public string Text { get; set; } = string.Empty;

    /// <summary>Пока текст набирается, рисуем курсор.</summary>
    public bool IsEditing { get; set; } = true;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    private FormattedText Format(string text) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize,
            Brushes.Black, pixelsPerDip: 1.0);

    public override void Render(DrawingContext dc)
    {
        if (Text.Length > 0)
        {
            var geometry = Format(Text).BuildGeometry(Origin);
            bool dark = Color.R * 0.299 + Color.G * 0.587 + Color.B * 0.114 < 140;
            var outline = new Pen(Frozen(new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x1F, 0x1F, 0x1F))),
                Math.Max(1, FontSize * 0.12)) { LineJoin = PenLineJoin.Round };
            outline.Freeze();
            dc.DrawGeometry(null, outline, geometry);
            dc.DrawGeometry(Frozen(new SolidColorBrush(Color)), null, geometry);
        }

        if (IsEditing)
        {
            // Курсор после последней строки.
            var lines = Text.Split('\n');
            var last = Format(lines[^1].Length == 0 ? " " : lines[^1]);
            double lineHeight = Format("Ag").Height;
            double x = Origin.X + (lines[^1].Length == 0 ? 0 : last.WidthIncludingTrailingWhitespace) + 2;
            double y = Origin.Y + lineHeight * (lines.Length - 1);
            var caret = new Pen(Frozen(new SolidColorBrush(Color)), Math.Max(1, FontSize / 12));
            caret.Freeze();
            dc.DrawLine(caret, new Point(x, y), new Point(x, y + lineHeight));
        }
    }
}

/// <summary>
/// Пикселизация области. Берёт пиксели из исходного снимка (а не из уже нарисованных аннотаций)
/// и рисует их крупными блоками: надёжнее размытия, восстановить исходный текст нельзя.
/// </summary>
public sealed class PixelateAnnotation : DragAnnotation
{
    private readonly Snapshot _snapshot;
    private readonly double _block;
    private Rect _cachedFor = Rect.Empty;
    private BitmapSource? _cached;

    public PixelateAnnotation(Snapshot snapshot, Point start, double block)
        : base(Colors.Transparent, start, 0)
    {
        _snapshot = snapshot;
        _block = Math.Max(4, block);
    }

    public override bool IsEmpty => Area.IsEmpty || Area.Width < 3 || Area.Height < 3;

    /// <summary>
    /// Пока область тянется мышью, рисуем лёгкую заглушку: пересчитывать пикселизацию большой области
    /// на каждое движение мыши дорого. Настоящие блоки строятся один раз, когда мышь отпущена.
    /// </summary>
    public bool IsPreview { get; set; } = true;

    private static readonly Brush PreviewFill = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0x80, 0x80, 0x80)));

    /// <summary>Прямоугольник в целых физических пикселях внутри снимка.</summary>
    private Rect Area
    {
        get
        {
            var r = new Rect(Start, End);
            var px = PxRect.FromLtrb((int)Math.Floor(r.Left), (int)Math.Floor(r.Top),
                (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom)).Intersect(_snapshot.VirtualBounds);
            return px.IsEmpty ? Rect.Empty : new Rect(px.X, px.Y, px.Width, px.Height);
        }
    }

    public override void Render(DrawingContext dc)
    {
        var area = Area;
        if (area.IsEmpty || area.Width < 1 || area.Height < 1)
            return;

        if (IsPreview)
        {
            dc.DrawRectangle(PreviewFill, null, area);
            return;
        }

        if (_cached is null || area != _cachedFor)
        {
            var crop = _snapshot.Crop(new PxRect((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height));
            double cols = Math.Max(1, Math.Floor(area.Width / _block));
            double rows = Math.Max(1, Math.Floor(area.Height / _block));
            var small = new TransformedBitmap(crop, new ScaleTransform(cols / area.Width, rows / area.Height));
            small.Freeze();
            _cached = small;
            _cachedFor = area;
        }

        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        group.Children.Add(new ImageDrawing(_cached, area));
        group.Freeze();
        dc.DrawDrawing(group);
    }
}

/// <summary>
/// Размытие прямоугольной областью. Сама размытая картинка общая для всей правки (<see cref="BlurLayer"/>),
/// аннотация только показывает её кусок, поэтому тянуть область мышью дёшево.
/// </summary>
public sealed class BlurAnnotation : DragAnnotation
{
    private readonly Brush _layer;

    public BlurAnnotation(Brush layer, Point start) : base(Colors.Transparent, start, 0) => _layer = layer;

    public override bool IsEmpty => Math.Abs(End.X - Start.X) < 3 || Math.Abs(End.Y - Start.Y) < 3;

    public override void Render(DrawingContext dc) => dc.DrawRectangle(_layer, null, new Rect(Start, End));
}

/// <summary>Размытие кистью: штрих, закрашенный той же размытой картинкой.</summary>
public sealed class BlurBrushAnnotation : StrokeAnnotation
{
    private readonly Brush _layer;

    public BlurBrushAnnotation(Brush layer, Point start, double thickness)
        : base(Colors.Transparent, start, thickness) => _layer = layer;

    public override void Render(DrawingContext dc) => RenderStroke(dc, _layer);
}
