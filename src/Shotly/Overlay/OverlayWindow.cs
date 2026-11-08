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
using Shotly.Editor;
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
    private readonly Grid _brushRing;

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
        _bar.ToolChosen += tool => _session.Edit(ed => { ed.FinishText(); ed.Tool = tool; });
        _bar.ColorChosen += color => _session.Edit(ed => { ed.FinishText(); ed.Color = color; });
        _bar.ThicknessChanged += position =>
        {
            _session.Edit(ed => ed.SizePosition = position);
            UpdatePointer();
        };
        _brushRing = CreateBrushRing();

        var layer = new Canvas();
        layer.Children.Add(_brushRing);
        layer.Children.Add(_bar);
        var root = new Grid();
        root.Children.Add(_surface);
        root.Children.Add(layer);
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => PlaceOnMonitor();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(PlaceOnMonitor));

        MouseLeftButtonDown += OnLeftDown;
        MouseMove += (_, _) =>
        {
            _session.OnMouseMove((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            UpdatePointer();
        };
        MouseLeave += (_, _) => _brushRing.Visibility = Visibility.Collapsed;
        // Колесо мыши в режиме правки меняет толщину (или размер набираемого текста).
        MouseWheel += (_, e) =>
        {
            if (_session.State != SelectionState.Editing || e.Delta == 0)
                return;
            e.Handled = true;
            StepSize(Math.Sign(e.Delta));
        };
        MouseLeftButtonUp += (_, _) =>
        {
            ReleaseMouseCapture();
            _session.OnMouseUp();
        };
        LostMouseCapture += (_, _) =>
        {
            // Захват мыши отобрали (Alt+Tab, Win) — завершаем перетаскивание, а не «залипаем» в нём.
            if (_session.State is SelectionState.Dragging or SelectionState.Moving
                || _session.Editor is { IsDrawing: true })
                _session.OnMouseUp();
        };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _session.ResetOrCancel();
        };
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, e) =>
        {
            if (e.Key is Key.LeftShift or Key.RightShift)
                _session.OnShiftChanged(false);
        };
        PreviewTextInput += (_, e) =>
        {
            if (_session.Editor is { IsTyping: true })
            {
                _session.Edit(ed => ed.TypeText(e.Text));
                e.Handled = true;
            }
        };

        _session.Changed += OnSessionChanged;
        _session.SelectionCommitted += _surface.PlayPulse;
        Closed += (_, _) =>
        {
            _session.Changed -= OnSessionChanged;
            _session.SelectionCommitted -= _surface.PlayPulse;
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
        // Shift во время рисования сразу выпрямляет линию, даже если мышь стоит на месте.
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            if (!e.IsRepeat)
                _session.OnShiftChanged(true);
            return;
        }

        // AltGr приходит как Ctrl+Alt: без проверки Alt польское «ś» (AltGr+S) сохраняло бы снимок.
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Alt) == 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (_session.State == SelectionState.Editing && _session.Editor is { } editor)
        {
            e.Handled = HandleEditorKey(editor, e.Key, ctrl, shift);
            return;
        }

        int step = ctrl ? 10 : 1;
        switch (e.Key)
        {
            case Key.Escape: _session.Cancel(); break;
            case Key.Enter: _session.Complete(CaptureAction.Copy); break;
            case Key.C when ctrl: _session.Complete(CaptureAction.Copy); break;
            case Key.S when ctrl: _session.Complete(CaptureAction.Save); break;
            case Key.E when !ctrl: _session.StartEditing(); break;
            case Key.Left: _session.Nudge(-step, 0, shift); break;
            case Key.Right: _session.Nudge(step, 0, shift); break;
            case Key.Up: _session.Nudge(0, -step, shift); break;
            case Key.Down: _session.Nudge(0, step, shift); break;
            default: return;
        }

        e.Handled = true;
    }

    /// <summary>Клавиши в режиме правки. Пока набирается текст, буквы уходят в текст, а не в выбор инструмента.</summary>
    private bool HandleEditorKey(EditorState editor, Key key, bool ctrl, bool shift)
    {
        if (ctrl)
        {
            switch (key)
            {
                case Key.Z when shift: _session.Edit(ed => ed.Redo()); return true;
                case Key.Z: _session.Edit(ed => ed.Undo()); return true;
                case Key.Y: _session.Edit(ed => ed.Redo()); return true;
                case Key.C: _session.Complete(CaptureAction.Copy); return true;
                case Key.S: _session.Complete(CaptureAction.Save); return true;
                default: return false;
            }
        }

        if (editor.IsTyping)
        {
            switch (key)
            {
                case Key.Back: _session.Edit(ed => ed.Backspace()); return true;
                case Key.Enter when shift: _session.Edit(ed => ed.NewLine()); return true;
                case Key.Enter:
                case Key.Escape: _session.Edit(ed => ed.FinishText()); return true;
                default: return false; // символы придут в PreviewTextInput
            }
        }

        EditorTool? tool = key switch
        {
            Key.A => EditorTool.Arrow,
            Key.R => EditorTool.Rectangle,
            Key.M => EditorTool.Marker,
            Key.T => EditorTool.Text,
            Key.P => EditorTool.Pixelate,
            Key.B when shift => EditorTool.BlurBrush,
            Key.B => EditorTool.Blur,
            _ => null,
        };
        if (tool is { } t)
        {
            _session.Edit(ed => ed.Tool = t);
            return true;
        }

        switch (key)
        {
            // Esc с зажатой мышью отменяет только незаконченную фигуру. Иначе закрывает снимок:
            // если правки уже есть — после второго нажатия, чтобы случайный Esc не выбросил работу.
            case Key.Escape when editor.IsDrawing: _session.Edit(ed => ed.CancelActive()); return true;
            case Key.Escape: _session.RequestCloseByEsc(); return true;
            case Key.Enter: _session.Complete(CaptureAction.Copy); return true;
            case Key.OemOpenBrackets or Key.OemMinus or Key.Subtract: StepSize(-1); return true;
            case Key.OemCloseBrackets or Key.OemPlus or Key.Add: StepSize(1); return true;
            default: return false;
        }
    }

    private void OnBarCommand(BarCommand command)
    {
        switch (command)
        {
            case BarCommand.Copy: _session.Complete(CaptureAction.Copy); break;
            case BarCommand.Save: _session.Complete(CaptureAction.Save); break;
            case BarCommand.Edit: _session.StartEditing(); break;
            case BarCommand.Undo: _session.Edit(ed => ed.Undo()); break;
            case BarCommand.Redo: _session.Edit(ed => ed.Redo()); break;
            default: _session.Cancel(); break;
        }
    }

    private void OnSessionChanged()
    {
        _surface.InvalidateVisual();
        UpdateActionBar();
        UpdatePointer();
    }

    private void StepSize(int delta)
    {
        _session.Edit(ed => ed.StepSize(delta));
        UpdatePointer();
    }

    /// <summary>
    /// Курсор и круг кисти. Над текстом (своим набираемым или любым при инструменте «Текст») — «переместить»;
    /// у маркера и кисти размытия вокруг курсора круг с настоящей толщиной кисти.
    /// </summary>
    private void UpdatePointer()
    {
        var editor = _session.State == SelectionState.Editing ? _session.Editor : null;
        var (cx, cy) = _session.Cursor;
        var point = new Point(cx, cy);

        var cursor = Cursors.Cross;
        if (editor is not null && !_bar.IsMouseOver)
        {
            if (editor.IsTyping && editor.IsDrawing || !editor.IsDrawing && editor.TextAt(point) is not null)
                cursor = Cursors.SizeAll;
            else if (editor.Tool == EditorTool.Text)
                cursor = Cursors.IBeam;
        }
        if (Cursor != cursor)
            Cursor = cursor;

        double d = editor?.BrushDiameter ?? 0;
        if (d <= 0 || _bar.IsMouseOver || !Monitor.Bounds.Contains(cx, cy)
            || _session.Selection is not { } sel || !sel.Contains(cx, cy))
        {
            _brushRing.Visibility = Visibility.Collapsed;
            return;
        }

        int r = (int)Math.Round(d / 2);
        var local = _surface.ToLocal(new PxRect(cx - r, cy - r, 2 * r, 2 * r));
        _brushRing.Width = Math.Max(4, local.Width);
        _brushRing.Height = Math.Max(4, local.Height);
        Canvas.SetLeft(_brushRing, local.X + (local.Width - _brushRing.Width) / 2);
        Canvas.SetTop(_brushRing, local.Y + (local.Height - _brushRing.Height) / 2);
        _brushRing.Visibility = Visibility.Visible;
    }

    /// <summary>Круг кисти: светлая линия на тёмной подложке, видна на любом фоне.</summary>
    private static Grid CreateBrushRing()
    {
        var ring = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed, SnapsToDevicePixels = true };
        ring.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Shotly.UI.Brand.Solid(Shotly.UI.Brand.Coal, 0x99), StrokeThickness = 3 });
        ring.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Shotly.UI.Brand.Solid(Colors.White, 0xE6), StrokeThickness = 1, Margin = new Thickness(1) });
        return ring;
    }

    private void UpdateActionBar()
    {
        if (_session.State is not (SelectionState.Selected or SelectionState.Editing) || _session.Selection is not { } sel)
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

        _bar.Update(_session.Editor);
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
