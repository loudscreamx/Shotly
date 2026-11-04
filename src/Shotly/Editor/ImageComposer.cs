using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shotly.Core;

namespace Shotly.Editor;

public static class ImageComposer
{
    /// <summary>
    /// Итоговая картинка: область снимка + аннотации, в исходном разрешении (1 физический пиксель = 1 пиксель файла).
    /// Без аннотаций просто вырезает область, без лишней перерисовки.
    /// </summary>
    public static BitmapSource Compose(Snapshot snapshot, PxRect region, IReadOnlyList<Annotation>? annotations)
    {
        var r = region.Intersect(snapshot.VirtualBounds);
        if (annotations is null || annotations.Count == 0 || r.IsEmpty)
            return snapshot.Crop(region);

        var visual = new DrawingVisual();
        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified); // сглаживание линий и текста
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-r.X, -r.Y));
            dc.DrawImage(snapshot.Crop(r), new Rect(r.X, r.Y, r.Width, r.Height));
            foreach (var a in annotations.Where(a => !a.IsEmpty))
                a.Render(dc);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(r.Width, r.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        // Без альфа-канала, как и снимок без правок: файлы не тяжелеют из-за одной стрелки.
        var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        return opaque;
    }
}
