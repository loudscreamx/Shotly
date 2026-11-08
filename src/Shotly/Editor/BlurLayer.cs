using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shotly.Core;

namespace Shotly.Editor;

/// <summary>
/// Размытая копия выделенной области снимка. Считается один раз за правку, при первом мазке размытием,
/// и рисуется кистью с абсолютной привязкой: любая фигура, закрашенная этой кистью,
/// показывает размытые пиксели ровно под собой. Берётся из исходного снимка, а не из аннотаций.
/// </summary>
public static class BlurLayer
{
    public static Brush Create(Snapshot snapshot, PxRect bounds, int radius)
    {
        var r = bounds.Intersect(snapshot.VirtualBounds);
        var blurred = BoxBlur(snapshot.Crop(r), Math.Max(2, radius));
        var brush = new ImageBrush(blurred)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(r.X, r.Y, r.Width, r.Height),
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Три прохода box blur по горизонтали и вертикали — почти гауссово размытие за линейное время,
    /// не зависящее от радиуса. Сильное: мелкий текст под ним не читается.
    /// </summary>
    private static BitmapSource BoxBlur(BitmapSource source, int radius)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
        var a = new byte[stride * h];
        converted.CopyPixels(a, stride, 0);
        var b = new byte[a.Length];

        for (int pass = 0; pass < 3; pass++)
        {
            Blur1D(a, b, count: h, length: w, lineStep: stride, pixelStep: 4, radius);
            Blur1D(b, a, count: w, length: h, lineStep: 4, pixelStep: stride, radius);
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, a, stride);
        result.Freeze();
        return result;
    }

    /// <summary>Скользящее среднее вдоль строк (или столбцов); за краем повторяется крайний пиксель.</summary>
    private static void Blur1D(byte[] src, byte[] dst, int count, int length, int lineStep, int pixelStep, int radius)
    {
        int window = 2 * radius + 1;
        Parallel.For(0, count, line =>
        {
            int origin = line * lineStep;
            for (int c = 0; c < 3; c++)
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++)
                    sum += src[origin + Math.Clamp(k, 0, length - 1) * pixelStep + c];

                for (int i = 0; i < length; i++)
                {
                    dst[origin + i * pixelStep + c] = (byte)((sum + window / 2) / window);
                    int add = Math.Min(i + radius + 1, length - 1);
                    int remove = Math.Max(i - radius, 0);
                    sum += src[origin + add * pixelStep + c] - src[origin + remove * pixelStep + c];
                }
            }
        });
    }
}
