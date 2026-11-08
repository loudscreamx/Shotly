using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shotly.Core;
using Shotly.Native;

namespace Shotly.UI;

/// <summary>
/// Отклик «снимок сделан» для захватов без оверлея (весь экран, активное окно, повтор области):
/// короткая светлая вспышка и рамка Ember → Amber вокруг снятой области, затем всё плавно гаснет.
///
/// Как и оверлей, рисуется отдельным окном на каждый монитор, который задевает область: так рамка
/// остаётся чёткой на мониторах с разным DPI. Окна прозрачны для мыши, не забирают фокус
/// и исключены из захвата экрана, поэтому не попадут ни в этот, ни в следующий снимок.
/// </summary>
public sealed class CaptureFlash : Window
{
    private static readonly List<CaptureFlash> Open = new();

    private readonly PxRect _piece;
    private readonly FlashSurface _surface;
    private bool _closed;

    private CaptureFlash(IReadOnlyList<MonitorInfo> monitors, MonitorInfo monitor, PxRect region)
    {
        _piece = region.Intersect(monitor.Bounds);

        // Сторона рамки рисуется, если за ней область не продолжается на другом мониторе.
        bool continues(int x, int y)
        {
            if (!region.Contains(x, y))
                return false;
            foreach (var m in monitors)
                if (m.Bounds.Contains(x, y))
                    return true;
            return false;
        }
        int midX = _piece.X + _piece.Width / 2, midY = _piece.Y + _piece.Height / 2;
        var sides = new Sides(
            Left: !continues(_piece.X - 1, midY),
            Top: !continues(midX, _piece.Y - 1),
            Right: !continues(_piece.Right, midY),
            Bottom: !continues(midX, _piece.Bottom));

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Примерная позиция в DIP, точную в физических пикселях ставит Place().
        Left = _piece.X / monitor.Scale;
        Top = _piece.Y / monitor.Scale;
        Width = Math.Max(1, _piece.Width / monitor.Scale);
        Height = Math.Max(1, _piece.Height / monitor.Scale);

        _surface = new FlashSurface(region, _piece, sides);
        Content = _surface;

        SourceInitialized += (_, _) =>
        {
            var h = Handle;
            var ex = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE, new IntPtr(ex
                | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT));
            NativeMethods.SetWindowDisplayAffinity(h, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            Place();
        };
        Loaded += (_, _) =>
        {
            Place();
            Animate();
        };
        DpiChanged += (_, _) => Place();
        Closed += (_, _) =>
        {
            _closed = true;
            Open.Remove(this);
        };
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Показывает отклик над областью <paramref name="region"/> (физические пиксели) на всех мониторах, которые она задевает.</summary>
    public static void Show(IReadOnlyList<MonitorInfo> monitors, PxRect region)
    {
        CloseAll();
        foreach (var monitor in monitors)
        {
            if (region.Intersect(monitor.Bounds).IsEmpty)
                continue;
            try
            {
                var flash = new CaptureFlash(monitors, monitor, region);
                Open.Add(flash);
                flash.Show();
            }
            catch (Exception ex)
            {
                // Отклик — украшение: если окно не создалось, снимок всё равно сделан.
                Log.Error("Не удалось показать вспышку захвата", ex);
            }
        }
    }

    public static void CloseAll()
    {
        foreach (var flash in Open.ToArray())
            flash.SafeClose();
        Open.Clear();
    }

    /// <summary>Ставит окно ровно на свою часть области в физических пикселях (дважды — см. OverlayWindow.PlaceOnMonitor).</summary>
    private void Place()
    {
        var h = Handle;
        if (h == IntPtr.Zero)
            return;
        for (int i = 0; i < 2; i++)
        {
            NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, _piece.X, _piece.Y, _piece.Width, _piece.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }
    }

    private void Animate()
    {
        // Вспышка: светлая заливка гаснет за 220 мс, рамка держится чуть дольше и уходит к 650 мс.
        var wash = new DoubleAnimation(0.32, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        _surface.BeginAnimation(FlashSurface.WashProperty, wash);

        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(650) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        fade.Completed += (_, _) => SafeClose();
        BeginAnimation(OpacityProperty, fade);
    }

    private void SafeClose()
    {
        if (_closed)
            return;
        _closed = true;
        try { Close(); }
        catch (Exception ex) { Log.Error("Не удалось закрыть вспышку захвата", ex); }
    }

    private readonly record struct Sides(bool Left, bool Top, bool Right, bool Bottom);

    /// <summary>
    /// Рисует свою часть области: заливку и рамку. Рамка идёт только по настоящим краям области:
    /// на стыке двух мониторов её нет, иначе снимок «разрезало» бы линией посередине.
    /// Градиент Ember → Amber считается по всей области, поэтому на соседних мониторах он продолжается.
    /// </summary>
    private sealed class FlashSurface : FrameworkElement
    {
        public static readonly DependencyProperty WashProperty = DependencyProperty.Register(
            nameof(Wash), typeof(double), typeof(FlashSurface),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private const double Thickness = 4; // DIP: на 100% и 200% рамка выглядит одинаково
        private readonly PxRect _region;
        private readonly PxRect _piece;
        private readonly Sides _sides;

        public FlashSurface(PxRect region, PxRect piece, Sides sides)
        {
            _region = region;
            _piece = piece;
            _sides = sides;
            SnapsToDevicePixels = true;
        }

        public double Wash
        {
            get => (double)GetValue(WashProperty);
            set => SetValue(WashProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0)
                return;

            if (Wash > 0)
                dc.DrawRectangle(Brand.Solid(Brand.Cream, (byte)Math.Round(Wash * 255)), null, new Rect(0, 0, w, h));

            // Физические пиксели → DIP этого окна (масштаб из фактического размера, как в SelectionSurface).
            double sx = _piece.Width / w, sy = _piece.Height / h;
            double top = (_region.Y - _piece.Y) / sy;
            double bottom = (_region.Bottom - _piece.Y) / sy;
            var brush = new LinearGradientBrush(Brand.Ember, Brand.Amber, new Point(0, top), new Point(0, bottom))
            {
                MappingMode = BrushMappingMode.Absolute,
            };
            brush.Freeze();

            double t = Math.Min(Thickness, Math.Min(w, h) / 2);
            if (_sides.Left)
                dc.DrawRectangle(brush, null, new Rect(0, 0, t, h));
            if (_sides.Right)
                dc.DrawRectangle(brush, null, new Rect(w - t, 0, t, h));
            if (_sides.Top)
                dc.DrawRectangle(brush, null, new Rect(0, 0, w, t));
            if (_sides.Bottom)
                dc.DrawRectangle(brush, null, new Rect(0, h - t, w, t));
        }
    }
}
