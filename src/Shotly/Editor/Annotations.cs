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

    public Color Color { get; protected set; }

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

    /// <summary>Куда ставить конец при зажатом Shift: стрелка — ровный угол, прямоугольник — квадрат.</summary>
    public virtual Point Constrain(Point end) => Snap.Square(Start, end);
}

/// <summary>Привязки для рисования с зажатым Shift.</summary>
public static class Snap
{
    /// <summary>Шаг угла для прямых линий: горизонталь, вертикаль, 45° и промежуточные через 15°.</summary>
    public const double AngleStep = 15;

    /// <summary>Конец отрезка, повёрнутый к ближайшему «ровному» углу. Длина сохраняется.</summary>
    public static Point Angle(Point from, Point to)
    {
        var v = to - from;
        double len = v.Length;
        if (len < 0.5)
            return to;
        double step = AngleStep * Math.PI / 180;
        double angle = Math.Round(Math.Atan2(v.Y, v.X) / step) * step;
        return from + new Vector(Math.Cos(angle) * len, Math.Sin(angle) * len);
    }

    /// <summary>Противоположный угол квадрата: сторона по большему из смещений.</summary>
    public static Point Square(Point from, Point to)
    {
        var v = to - from;
        double side = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
        return new Point(from.X + (v.X < 0 ? -side : side), from.Y + (v.Y < 0 ? -side : side));
    }
}

public sealed class ArrowAnnotation : DragAnnotation
{
    public ArrowAnnotation(Color color, Point start, double thickness) : base(color, start, thickness) { }

    public override Point Constrain(Point end) => Snap.Angle(Start, end);

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

/// <summary>
/// Штрих от руки: копит точки и строит из них одну линию.
/// С зажатым Shift от последней точки тянется прямой отрезок (с привязкой к ровным углам),
/// а когда Shift отпущен, штрих продолжается от конца этого отрезка.
/// </summary>
public abstract class StrokeAnnotation : Annotation
{
    private readonly List<Point> _points = new();
    private Point? _straightTo;

    /// <summary>
    /// Стабилизация: каждая новая точка сдвигается к курсору только на эту долю пути.
    /// Дрожание руки и «ступеньки» мыши сглаживаются, а линия отстаёт от курсора всего на пару пикселей.
    /// </summary>
    private const double Smoothing = 0.4;
    private Point _smoothed;

    protected StrokeAnnotation(Color color, Point start, double thickness) : base(color)
    {
        Thickness = thickness;
        _points.Add(start);
        _smoothed = start;
    }

    public double Thickness { get; }

    public override bool IsEmpty => false; // даже точка — осознанное действие

    public void Add(Point p, bool straight = false)
    {
        if (straight)
        {
            _straightTo = Snap.Angle(_points[^1], p);
            return;
        }

        FixStraight();
        _smoothed += (p - _smoothed) * Smoothing;
        // Не копим тысячи точек при медленном движении мыши.
        if ((_smoothed - _points[^1]).Length >= 2)
            _points.Add(_smoothed);
    }

    /// <summary>Делает прямой отрезок частью штриха (Shift отпущен или мышь отпущена).</summary>
    public void FixStraight()
    {
        if (_straightTo is { } end)
        {
            _points.Add(end);
            _smoothed = end;
            _straightTo = null;
        }
    }

    /// <summary>
    /// Рисует весь штрих одной геометрией: самопересечения не темнеют (маркер)
    /// и не дают швов (кисть размытия).
    /// </summary>
    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    protected void RenderStroke(DrawingContext dc, Brush brush)
    {
        var pen = new Pen(brush, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        var points = _straightTo is { } end ? _points.Append(end).ToList() : _points;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
            if (points.Count == 1)
            {
                ctx.LineTo(points[0] + new Vector(0.1, 0), true, true); // точка → круглая клякса
            }
            else
            {
                // Плавная кривая вместо ломаной: каждая точка становится опорной для кривой Безье
                // между серединами соседних отрезков, поэтому на изгибах нет углов.
                for (int i = 1; i < points.Count - 1; i++)
                    ctx.QuadraticBezierTo(points[i], Mid(points[i], points[i + 1]), true, true);
                ctx.LineTo(points[^1], true, true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>Непрозрачная кисть от руки: обычная линия выбранным цветом.</summary>
public sealed class BrushAnnotation : StrokeAnnotation
{
    public BrushAnnotation(Color color, Point start, double thickness) : base(color, start, thickness) { }

    public override void Render(DrawingContext dc) => RenderStroke(dc, Frozen(new SolidColorBrush(Color)));
}

/// <summary>Полупрозрачный маркер от руки.</summary>
public sealed class MarkerAnnotation : StrokeAnnotation
{
    public MarkerAnnotation(Color color, Point start, double thickness) : base(color, start, thickness) { }

    public override void Render(DrawingContext dc) =>
        RenderStroke(dc, Frozen(new SolidColorBrush(Color.FromArgb(0x70, Color.R, Color.G, Color.B))));
}

/// <summary>
/// Текст. По умолчанию без обводки; обводку контрастным цветом можно включить кнопкой на панели.
/// Пока текст набирается, вокруг него рамка: за неё текст перетаскивают, а за угловые ручки меняют размер.
/// </summary>
public sealed class TextAnnotation : Annotation
{
    // Фирменный Onest (встроен в exe), при сбое загрузки — Segoe UI.
    private static Typeface Face => Shotly.UI.Brand.TextFaceSemiBold;
    private static readonly Color FrameColor = Shotly.UI.Brand.Ember;

    public TextAnnotation(Color color, Point origin, double fontSize) : base(color)
    {
        Origin = origin;
        FontSize = fontSize;
    }

    public Point Origin { get; set; }
    public double FontSize { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>Тонкая контрастная обводка букв, чтобы текст читался на пёстром фоне.</summary>
    public bool Outline { get; set; }

    public void Recolor(Color color) => Color = color;

    /// <summary>Пока текст набирается, рисуем курсор и рамку.</summary>
    public bool IsEditing { get; set; } = true;

    public override bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <summary>Копия для повторной правки: старая остаётся в истории без изменений, и Ctrl+Z её вернёт.</summary>
    public TextAnnotation Clone() => new(Color, Origin, FontSize) { Text = Text, Outline = Outline, IsEditing = false };

    /// <summary>Отступ рамки от букв.</summary>
    private double Padding => Math.Max(4, FontSize * 0.35);

    /// <summary>Рамка вокруг текста (вместе с местом под курсор): за неё текст берут и перетаскивают.</summary>
    public Rect Frame
    {
        get
        {
            var lines = Text.Split('\n');
            double lineHeight = Format("Ag").Height;
            double width = lines.Max(l => l.Length == 0 ? 0 : Format(l).WidthIncludingTrailingWhitespace);
            var r = new Rect(Origin.X, Origin.Y, Math.Max(width + FontSize * 0.25, FontSize * 0.6), lineHeight * lines.Length);
            r.Inflate(Padding, Padding * 0.6);
            return r;
        }
    }

    public bool HitTest(Point p) => Frame.Contains(p);

    /// <summary>Углы рамки по порядку: левый верхний, правый верхний, левый нижний, правый нижний.</summary>
    public static Point Corner(Rect r, int i) => i switch
    {
        0 => r.TopLeft,
        1 => r.TopRight,
        2 => r.BottomLeft,
        _ => r.BottomRight,
    };

    /// <summary>Номер угловой ручки под точкой (0–3) или −1. <paramref name="tolerance"/> — радиус захвата в пикселях.</summary>
    public int HandleAt(Point p, double tolerance)
    {
        var frame = Frame;
        for (int i = 0; i < 4; i++)
            if ((Corner(frame, i) - p).Length <= tolerance)
                return i;
        return -1;
    }

    private FormattedText Format(string text) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize,
            Brushes.Black, pixelsPerDip: 1.0);

    public override void Render(DrawingContext dc)
    {
        if (IsEditing)
            RenderFrame(dc);

        if (Text.Length > 0)
        {
            var geometry = Format(Text).BuildGeometry(Origin);
            if (Outline)
            {
                bool dark = Color.R * 0.299 + Color.G * 0.587 + Color.B * 0.114 < 140;
                var outline = new Pen(Frozen(new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x1F, 0x1F, 0x1F))),
                    Math.Max(1, FontSize * 0.12)) { LineJoin = PenLineJoin.Round };
                outline.Freeze();
                dc.DrawGeometry(null, outline, geometry);
            }
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

    /// <summary>
    /// Пунктир Ember поверх светлой подложки: рамку видно и на тёмном, и на светлом фоне.
    /// Толщина растёт вместе с размером шрифта (а он — с масштабом монитора).
    /// </summary>
    /// <summary>Половина стороны угловой ручки.</summary>
    public double HandleHalf => 3.5 * Math.Max(1, FontSize / 20);

    private void RenderFrame(DrawingContext dc)
    {
        double k = Math.Max(1, FontSize / 20);
        var frame = Frame;
        var under = new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF))), 3 * k);
        under.Freeze();
        var dashed = new Pen(Frozen(new SolidColorBrush(FrameColor)), 1.5 * k)
        {
            DashStyle = new DashStyle(new double[] { 3, 2 }, 0),
            DashCap = PenLineCap.Flat,
        };
        dashed.Freeze();
        dc.DrawRoundedRectangle(null, under, frame, 3 * k, 3 * k);
        dc.DrawRoundedRectangle(null, dashed, frame, 3 * k, 3 * k);

        // Ручки по углам: за них меняют размер текста.
        var fill = Frozen(new SolidColorBrush(FrameColor));
        var edge = new Pen(Brushes.White, 1 * k);
        edge.Freeze();
        double h = HandleHalf;
        foreach (var c in new[] { frame.TopLeft, frame.TopRight, frame.BottomLeft, frame.BottomRight })
            dc.DrawRectangle(fill, edge, new Rect(c.X - h, c.Y - h, 2 * h, 2 * h));
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
