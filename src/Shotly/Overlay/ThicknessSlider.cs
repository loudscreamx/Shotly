using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Shotly.UI;

namespace Shotly.Overlay;

/// <summary>
/// Плавный ползунок толщины на панели редактора: дорожка с заливкой Ember и светлый бегунок.
/// Рисуется в OnRender, без шаблонов, поэтому не зависит от тем и не пересоздаётся при перерисовке панели.
/// Значение от 0 до 1; во что оно превращается, решает редактор.
/// </summary>
public sealed class ThicknessSlider : FrameworkElement
{
    private const double ThumbRadius = 7;
    private const double TrackHeight = 4;

    private static readonly Brush TrackBrush = Brand.Solid(Colors.White, 0x2E);
    private static readonly Brush FillBrush = Brand.EmberBrush;
    private static readonly Brush ThumbBrush = Brand.Solid(Brand.Cream);
    private static readonly Pen ThumbPen = Frozen(new Pen(Brand.EmberBrush, 2));
    private static readonly Brush DisabledBrush = Brand.Solid(Brand.Text, 0x40);

    private double _value;

    public ThicknessSlider()
    {
        Width = 112;
        Height = 28;
        Cursor = Cursors.Hand;
        Focusable = false;
    }

    /// <summary>Пользователь двигает бегунок (значение от 0 до 1).</summary>
    public event Action<double>? ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            if (Math.Abs(value - _value) < 0.0005)
                return;
            _value = value;
            InvalidateVisual();
        }
    }

    public bool Active
    {
        get => IsEnabled;
        set
        {
            if (IsEnabled == value)
                return;
            IsEnabled = value;
            Cursor = value ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    private double TrackLeft => ThumbRadius + 1;
    private double TrackRight => Math.Max(TrackLeft + 1, ActualWidth - ThumbRadius - 1);

    protected override void OnRender(DrawingContext dc)
    {
        // Прозрачный фон: бегунок можно схватить в любом месте полосы, а не только точно по линии.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        double cy = ActualHeight / 2;
        double x = TrackLeft + (TrackRight - TrackLeft) * _value;
        var track = new Rect(TrackLeft, cy - TrackHeight / 2, TrackRight - TrackLeft, TrackHeight);
        dc.DrawRoundedRectangle(IsEnabled ? TrackBrush : DisabledBrush, null, track, TrackHeight / 2, TrackHeight / 2);
        if (!IsEnabled)
            return;

        dc.DrawRoundedRectangle(FillBrush, null, new Rect(track.X, track.Y, Math.Max(TrackHeight, x - track.X), TrackHeight),
            TrackHeight / 2, TrackHeight / 2);
        dc.DrawEllipse(ThumbBrush, ThumbPen, new Point(x, cy), ThumbRadius - 1, ThumbRadius - 1);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsEnabled)
            return;
        CaptureMouse();
        SetFromMouse(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured)
        {
            e.Handled = true;
            SetFromMouse(e);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void SetFromMouse(MouseEventArgs e)
    {
        double x = e.GetPosition(this).X;
        Value = (x - TrackLeft) / (TrackRight - TrackLeft);
        ValueChanged?.Invoke(_value);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
