using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shotly.UI;

namespace Shotly.Overlay;

/// <summary>
/// Палитра для своего цвета: по горизонтали оттенок, по вертикали от светлого через чистый цвет к тёмному,
/// внизу полоса серых от белого до чёрного. Цвет выбирается щелчком или перетаскиванием.
/// Картинка палитры строится один раз на всё время работы программы.
/// </summary>
public sealed class ColorSpectrum : FrameworkElement
{
    private const int W = 232, SpectrumH = 112, Gap = 6, GrayH = 16;
    private const int TotalH = SpectrumH + Gap + GrayH;

    private static readonly Lazy<BitmapSource> SpectrumImage = new(BuildSpectrum);
    private static readonly Lazy<BitmapSource> GrayImage = new(BuildGray);
    private static readonly Pen MarkerOuter = Frozen(new Pen(Brand.Solid(Brand.Coal), 3));
    private static readonly Pen MarkerInner = Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Pen EdgePen = Frozen(new Pen(Brand.Solid(Colors.White, 0x33), 1));

    private Point? _marker;

    public ColorSpectrum()
    {
        Width = W;
        Height = TotalH;
        Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
    }

    /// <summary>Цвет под курсором меняется (во время перетаскивания — много раз).</summary>
    public event Action<Color>? ColorPicked;

    /// <summary>Кнопку мыши отпустили: выбор закончен.</summary>
    public event Action? PickFinished;

    protected override void OnRender(DrawingContext dc)
    {
        var spectrum = new Rect(0, 0, W, SpectrumH);
        var gray = new Rect(0, SpectrumH + Gap, W, GrayH);
        dc.PushClip(new RectangleGeometry(spectrum, 5, 5));
        dc.DrawImage(SpectrumImage.Value, spectrum);
        dc.Pop();
        dc.DrawRoundedRectangle(null, EdgePen, spectrum, 5, 5);
        dc.PushClip(new RectangleGeometry(gray, 4, 4));
        dc.DrawImage(GrayImage.Value, gray);
        dc.Pop();
        dc.DrawRoundedRectangle(null, EdgePen, gray, 4, 4);

        if (_marker is { } m)
        {
            dc.DrawEllipse(null, MarkerOuter, m, 6, 6);
            dc.DrawEllipse(null, MarkerInner, m, 6, 6);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        CaptureMouse();
        Pick(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured)
            return;
        e.Handled = true;
        Pick(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsMouseCaptured)
            return;
        ReleaseMouseCapture();
        PickFinished?.Invoke();
    }

    private void Pick(Point p)
    {
        double x = Math.Clamp(p.X, 0, W - 1);
        Color color;
        if (p.Y < SpectrumH + Gap / 2.0)
        {
            double y = Math.Clamp(p.Y, 0, SpectrumH - 1);
            color = SpectrumAt(x / (W - 1), y / (SpectrumH - 1));
            _marker = new Point(x, y);
        }
        else
        {
            double t = x / (W - 1);
            byte v = (byte)Math.Round(255 * (1 - t));
            color = Color.FromRgb(v, v, v);
            _marker = new Point(x, SpectrumH + Gap + GrayH / 2.0);
        }

        InvalidateVisual();
        ColorPicked?.Invoke(color);
    }

    /// <summary>u — оттенок (0…1), v — сверху вниз: светлый → чистый цвет (середина) → тёмный.</summary>
    private static Color SpectrumAt(double u, double v)
    {
        double hue = u * 360;
        double s, l;
        if (v < 0.5)
        {
            s = 1;
            l = 0.92 - (0.92 - 0.5) * (v / 0.5);   // от почти белого до чистого
        }
        else
        {
            s = 1;
            l = 0.5 - (0.5 - 0.1) * ((v - 0.5) / 0.5); // от чистого до почти чёрного
        }
        return FromHsl(hue, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double hp = h / 60;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        double m = l - c / 2;
        static byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromRgb(B(r + m), B(g + m), B(b + m));
    }

    private static BitmapSource BuildSpectrum()
    {
        var pixels = new byte[W * SpectrumH * 4];
        for (int y = 0; y < SpectrumH; y++)
            for (int x = 0; x < W; x++)
            {
                var c = SpectrumAt(x / (double)(W - 1), y / (double)(SpectrumH - 1));
                int i = (y * W + x) * 4;
                pixels[i] = c.B;
                pixels[i + 1] = c.G;
                pixels[i + 2] = c.R;
                pixels[i + 3] = 255;
            }
        var bmp = BitmapSource.Create(W, SpectrumH, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4);
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource BuildGray()
    {
        var pixels = new byte[W * 4];
        for (int x = 0; x < W; x++)
        {
            byte v = (byte)Math.Round(255 * (1 - x / (double)(W - 1)));
            pixels[x * 4] = pixels[x * 4 + 1] = pixels[x * 4 + 2] = v;
            pixels[x * 4 + 3] = 255;
        }
        var bmp = BitmapSource.Create(W, 1, 96, 96, PixelFormats.Bgra32, null, pixels, W * 4);
        bmp.Freeze();
        return bmp;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
