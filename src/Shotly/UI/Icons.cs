using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shotly.UI;

public enum Glyph
{
    // Панель редактора.
    Edit, Arrow, Rectangle, Marker, Text, Blur, BlurBrush, Pixelate, Undo, Redo, Copy, Save, Close, Minus, Plus,
    // Меню трея.
    Region, Window, Screen, Repeat, Timer, Folder, Settings, Update, Delete, Exit,
}

/// <summary>
/// Векторные значки 16×16. Иконочные шрифты Windows (Segoe Fluent Icons, MDL2 Assets) есть не везде:
/// без них вместо значков были пустые квадраты, а символы вроде «↗» Windows подставляла мелкими и тонкими.
/// Один набор контуров рисует и панель редактора (WPF), и меню трея (картинки для WinForms).
/// </summary>
public static class Icons
{
    private const double Size = 16;

    private readonly record struct Part(string Data, bool Filled = false, double Thickness = 1.5, bool Dashed = false);

    private static readonly Dictionary<Glyph, Part[]> Shapes = new()
    {
        [Glyph.Edit] = new[]
        {
            new Part("M10.5,2.8 L13.2,5.5 L5.6,13.1 L2.6,13.4 L2.9,10.4 Z"),
            new Part("M9,4.3 L11.7,7"),
        },
        [Glyph.Arrow] = new[] { new Part("M3.5,12.5 L12,4", Thickness: 1.8), new Part("M6,3.5 H12.5 V10", Thickness: 1.8) },
        [Glyph.Rectangle] = new[] { new Part("M2.5,3.5 H13.5 V12.5 H2.5 Z") },
        [Glyph.Marker] = new[]
        {
            new Part("M6.2,9.3 L11.3,4.2 L13.3,6.2 L8.2,11.3 Z"),
            new Part("M6.2,9.3 L4.4,12.6 L4.9,13.1 L8.2,11.3"),
            new Part("M9.5,14 H14", Thickness: 1.8),
        },
        [Glyph.Text] = new[] { new Part("M3,3.5 H13 M8,3.5 V13 M6,13 H10", Thickness: 1.7) },
        [Glyph.Blur] = new[] { new Part("M2,2 H14 V14 H2 Z", Thickness: 1.3, Dashed: true), new Part(Drop(8, 8.6, 0.62), Filled: true) },
        [Glyph.BlurBrush] = new[] { new Part("M2,13 C4,9.5 6.5,14.5 9,11.5", Thickness: 2.4), new Part(Drop(11, 6.5, 0.72), Filled: true) },
        [Glyph.Pixelate] = new[]
        {
            new Part("M2,2 h4 v4 h-4 Z M10,2 h4 v4 h-4 Z M6,6 h4 v4 h-4 Z M2,10 h4 v4 h-4 Z M10,10 h4 v4 h-4 Z", Filled: true),
        },
        [Glyph.Undo] = new[] { new Part("M5.5,3.5 L2.5,6.5 L5.5,9.5"), new Part("M2.5,6.5 H9.5 A3.5,3.5 0 0 1 9.5,13.5 H6.5") },
        [Glyph.Redo] = new[] { new Part("M10.5,3.5 L13.5,6.5 L10.5,9.5"), new Part("M13.5,6.5 H6.5 A3.5,3.5 0 0 0 6.5,13.5 H9.5") },
        [Glyph.Copy] = new[]
        {
            new Part("M5.5,6.8 A1.3,1.3 0 0 1 6.8,5.5 H12.2 A1.3,1.3 0 0 1 13.5,6.8 V12.2 A1.3,1.3 0 0 1 12.2,13.5 H6.8 A1.3,1.3 0 0 1 5.5,12.2 Z"),
            new Part("M2.5,10.5 V3.8 A1.3,1.3 0 0 1 3.8,2.5 H10.5"),
        },
        [Glyph.Save] = new[] { new Part("M8,2.5 V9.5 M4.8,6.5 L8,9.7 L11.2,6.5"), new Part("M2.5,10.5 V13.5 H13.5 V10.5") },
        [Glyph.Close] = new[] { new Part("M4,4 L12,12 M12,4 L4,12", Thickness: 1.6) },
        [Glyph.Minus] = new[] { new Part("M4,8 H12", Thickness: 1.7) },
        [Glyph.Plus] = new[] { new Part("M4,8 H12 M8,4 V12", Thickness: 1.7) },

        [Glyph.Region] = new[] { new Part("M2.5,6 V2.5 H6 M10,2.5 H13.5 V6 M13.5,10 V13.5 H10 M6,13.5 H2.5 V10") },
        [Glyph.Window] = new[]
        {
            new Part("M2.5,4 A1.5,1.5 0 0 1 4,2.5 H12 A1.5,1.5 0 0 1 13.5,4 V12 A1.5,1.5 0 0 1 12,13.5 H4 A1.5,1.5 0 0 1 2.5,12 Z"),
            new Part("M2.5,5.8 H13.5"),
        },
        [Glyph.Screen] = new[]
        {
            new Part("M1.8,3.8 A1.3,1.3 0 0 1 3.1,2.5 H12.9 A1.3,1.3 0 0 1 14.2,3.8 V9.7 A1.3,1.3 0 0 1 12.9,11 H3.1 A1.3,1.3 0 0 1 1.8,9.7 Z"),
            new Part("M5.5,13.8 H10.5 M8,11 V13.8"),
        },
        [Glyph.Repeat] = new[] { new Part("M13,8.5 A5,5 0 1 1 11.3,4.3"), new Part("M12,1.8 V4.8 H9") },
        [Glyph.Timer] = new[] { new Part("M3,9.3 A5,5 0 1 0 13,9.3 A5,5 0 1 0 3,9.3 Z"), new Part("M8,9.3 V6.6 M6.3,2 H9.7") },
        [Glyph.Folder] = new[]
        {
            new Part("M2,4.6 A1.1,1.1 0 0 1 3.1,3.5 H6 L7.5,5 H12.9 A1.1,1.1 0 0 1 14,6.1 V11.9 A1.1,1.1 0 0 1 12.9,13 H3.1 A1.1,1.1 0 0 1 2,11.9 Z"),
        },
        [Glyph.Settings] = new[]
        {
            new Part("M2.5,5 H13.5 M2.5,11 H13.5"),
            new Part(Circle(10, 5, 2.1), Filled: true),
            new Part(Circle(6, 11, 2.1), Filled: true),
        },
        [Glyph.Update] = new[] { new Part(Circle(8, 8, 5.8)), new Part("M8,4.8 V10.8 M5.5,8.4 L8,10.9 L10.5,8.4") },
        [Glyph.Delete] = new[] { new Part("M2.8,4.5 H13.2 M6.3,4.5 V2.8 H9.7 V4.5"), new Part("M4.3,4.5 L4.9,13.5 H11.1 L11.7,4.5") },
        [Glyph.Exit] = new[] { new Part("M8,2.3 V7.5"), new Part("M4.6,4.6 A5,5 0 1 0 11.4,4.6") },
    };

    private static readonly Dictionary<(Glyph, Color), DrawingImage> Cache = new();

    /// <summary>Значок как элемент WPF размером 16×16 DIP: вектор, чёткий при любом масштабе монитора.</summary>
    public static UIElement Element(Glyph glyph, Brush brush, double size = Size) => new Image
    {
        Source = Source(glyph, brush is SolidColorBrush s ? s.Color : Brand.Text),
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public static DrawingImage Source(Glyph glyph, Color color)
    {
        if (Cache.TryGetValue((glyph, color), out var cached))
            return cached;

        var brush = Brand.Solid(color);
        var group = new DrawingGroup();
        // Прозрачный квадрат задаёт поле 16×16, иначе картинка обрезалась бы по контуру значка.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Size, Size))));
        foreach (var part in Shapes[glyph])
        {
            var geometry = Geometry.Parse(part.Data);
            if (part.Filled)
            {
                group.Children.Add(new GeometryDrawing(brush, null, geometry));
                continue;
            }

            var pen = new Pen(brush, part.Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            if (part.Dashed)
            {
                pen.DashStyle = new DashStyle(new double[] { 1.6, 1.25 }, 0);
                pen.DashCap = PenLineCap.Flat;
                pen.LineJoin = PenLineJoin.Miter;
                pen.StartLineCap = pen.EndLineCap = PenLineCap.Flat;
            }
            group.Children.Add(new GeometryDrawing(null, pen, geometry));
        }
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        Cache[(glyph, color)] = image;
        return image;
    }

    /// <summary>Значок как PNG нужного размера в пикселях: для меню трея (WinForms не рисует вектор WPF).</summary>
    public static System.Drawing.Bitmap Bitmap(Glyph glyph, Color color, int pixels) => Bitmap(Source(glyph, color), pixels);

    /// <summary>Любая векторная картинка WPF (например, знак Shotly) как квадратный PNG для WinForms.</summary>
    public static System.Drawing.Bitmap Bitmap(ImageSource source, int pixels)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(source, new Rect(0, 0, pixels, pixels));

        var target = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var decoded = new System.Drawing.Bitmap(stream);
        return new System.Drawing.Bitmap(decoded); // копия не зависит от закрытого потока
    }

    private static string Circle(double cx, double cy, double r) =>
        string.Create(CultureInfo.InvariantCulture, $"M{cx - r},{cy} A{r},{r} 0 1 0 {cx + r},{cy} A{r},{r} 0 1 0 {cx - r},{cy} Z");

    /// <summary>Капля высотой около 9 DIP с центром округлой части в (cx, cy), масштабированная k.</summary>
    private static string Drop(double cx, double cy, double k)
    {
        string P(double x, double y) =>
            string.Create(CultureInfo.InvariantCulture, $"{cx + x * k},{cy + y * k}");
        string r = (4 * k).ToString(CultureInfo.InvariantCulture);
        return $"M{P(0, -8)} C{P(0, -8)} {P(-4, -3)} {P(-4, 0)} A{r},{r} 0 0 0 {P(4, 0)} C{P(4, -3)} {P(0, -8)} {P(0, -8)} Z";
    }
}
