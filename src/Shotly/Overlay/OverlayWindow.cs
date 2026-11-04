using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shotly.Capture;
using Shotly.Core;
using Shotly.Native;

namespace Shotly.Overlay;

/// <summary>
/// Окно-оверлей ровно на один монитор. На каждом мониторе своё окно, поэтому каждое
/// живёт в одном DPI и не размывается и не съезжает на стыке мониторов с разным масштабом.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly CaptureSession _session;
    private readonly SelectionSurface _surface;
    private readonly ActionBar _bar;

    public OverlayWindow(CaptureSession session, MonitorInfo monitor)
    {
        _session = session;
        Monitor = monitor;

        Title = "Shotly";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        UseLayoutRounding = true;

        // Примерная позиция в DIP, чтобы окно сразу появилось на своём мониторе.
        // Точную позицию в физических пикселях ставит PlaceOnMonitor().
        Left = monitor.Bounds.X / monitor.Scale;
        Top = monitor.Bounds.Y / monitor.Scale;
        Width = monitor.Bounds.Width / monitor.Scale;
        Height = monitor.Bounds.Height / monitor.Scale;

        var snapshot = session.Snapshot;
        var piece = monitor.Bounds.Intersect(snapshot.VirtualBounds);
        var background = new CroppedBitmap(snapshot.Image, new Int32Rect(
            piece.X - snapshot.VirtualBounds.X, piece.Y - snapshot.VirtualBounds.Y, piece.Width, piece.Height));
        background.Freeze();

        _surface = new SelectionSurface(session, monitor, background);
        _bar = new ActionBar { Visibility = Visibility.Collapsed };
        _bar.CommandInvoked += OnBarCommand;

        var layer = new Canvas();
        layer.Children.Add(_bar);
        var root = new Grid();
        root.Children.Add(_surface);
        root.Children.Add(layer);
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => PlaceOnMonitor();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(PlaceOnMonitor));

        MouseLeftButtonDown += OnLeftDown;
        MouseMove += (_, _) => _session.OnMouseMove();
        MouseLeftButtonUp += (_, _) =>
        {
            ReleaseMouseCapture();
            _session.OnMouseUp();
        };
        LostMouseCapture += (_, _) =>
        {
            // Захват мыши отобрали (Alt+Tab, Win) — завершаем перетаскивание, а не «залипаем» в нём.
            if (_session.State is SelectionState.Dragging or SelectionState.Moving)
                _session.OnMouseUp();
        };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _session.ResetOrCancel();
        };
        PreviewKeyDown += OnKeyDown;

        _session.Changed += OnSessionChanged;
        Closed += (_, _) =>
        {
            _session.Changed -= OnSessionChanged;
            // Окно могли закрыть мимо сессии (Alt+F4) — сессия должна завершиться, иначе захват «зависнет».
            _session.Cancel();
        };
    }

    public MonitorInfo Monitor { get; }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void ActivateNow()
    {
        Activate();
        NativeMethods.SetForegroundWindow(Handle);
        Keyboard.Focus(this);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Убираем окно из Alt+Tab.
        var ex = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(ex | NativeMethods.WS_EX_TOOLWINDOW));
        PlaceOnMonitor();
    }

    /// <summary>
    /// Ставит окно точно на границы монитора в физических пикселях.
    /// Вызываем дважды: первый вызов может перенести окно на монитор с другим DPI,
    /// WPF при этом пересчитает размер по своей «рекомендации», и второй вызов возвращает точный размер.
    /// </summary>
    private void PlaceOnMonitor()
    {
        var h = Handle;
        if (h == IntPtr.Zero)
            return;

        var b = Monitor.Bounds;
        for (int i = 0; i < 2; i++)
        {
            NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var (x, y) = ScreenCapture.CursorPosition();

        // Двойной клик внутри выделения = копировать.
        if (e.ClickCount == 2 && _session.State == SelectionState.Selected
            && _session.Selection is { } sel && sel.Contains(x, y))
        {
            _session.Complete(CaptureAction.Copy);
            return;
        }

        CaptureMouse();
        _session.OnMouseDown();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        int step = ctrl ? 10 : 1;

        switch (e.Key)
        {
            case Key.Escape: _session.Cancel(); break;
            case Key.Enter: _session.Complete(CaptureAction.Copy); break;
            case Key.C when ctrl: _session.Complete(CaptureAction.Copy); break;
            case Key.S when ctrl: _session.Complete(CaptureAction.Save); break;
            case Key.Left: _session.Nudge(-step, 0, shift); break;
            case Key.Right: _session.Nudge(step, 0, shift); break;
            case Key.Up: _session.Nudge(0, -step, shift); break;
            case Key.Down: _session.Nudge(0, step, shift); break;
            default: return;
        }

        e.Handled = true;
    }

    private void OnBarCommand(BarCommand command)
    {
        switch (command)
        {
            case BarCommand.Copy: _session.Complete(CaptureAction.Copy); break;
            case BarCommand.Save: _session.Complete(CaptureAction.Save); break;
            default: _session.Cancel(); break;
        }
    }

    private void OnSessionChanged()
    {
        _surface.InvalidateVisual();
        UpdateActionBar();
    }

    private void UpdateActionBar()
    {
        if (_session.State != SelectionState.Selected || _session.Selection is not { } sel)
        {
            _bar.Visibility = Visibility.Collapsed;
            return;
        }

        // Панель показывает только тот оверлей, на чьём мониторе правый нижний угол выделения.
        if (Monitors.FromPoint(_session.Snapshot.Monitors, sel.Right - 1, sel.Bottom - 1) != Monitor)
        {
            _bar.Visibility = Visibility.Collapsed;
            return;
        }

        _bar.Visibility = Visibility.Visible;
        _bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _bar.DesiredSize;
        var local = _surface.ToLocal(sel);
        double w = ActualWidth, h = ActualHeight;
        const double gap = 8;

        // Под выделением справа; не влезает — над ним; и там нет места — внутри у нижнего края.
        double x = local.Right - size.Width;
        double y = local.Bottom + gap;
        if (y + size.Height > h - gap)
            y = local.Top - size.Height - gap;
        if (y < gap)
            y = Math.Min(local.Bottom, h) - size.Height - gap;

        x = Math.Clamp(x, gap, Math.Max(gap, w - size.Width - gap));
        y = Math.Clamp(y, gap, Math.Max(gap, h - size.Height - gap));
        Canvas.SetLeft(_bar, x);
        Canvas.SetTop(_bar, y);
    }
}
