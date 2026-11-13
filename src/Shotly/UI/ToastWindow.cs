using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shotly.Native;
using Shotly.Localization;

namespace Shotly.UI;

/// <summary>
/// Уведомление в фирменном стиле в правом нижнем углу основного монитора: знак Orbit, заголовок и текст Onest
/// на фоне Coal. Системные всплывающие подсказки трея нельзя оформить, поэтому Shotly рисует своё окно.
/// Окно создаётся только на время показа, не забирает фокус и не попадает в снимки экрана.
/// Одновременно показывается одно уведомление: новое заменяет предыдущее.
/// </summary>
public sealed class ToastWindow : Window
{
    private static ToastWindow? _current;

    private readonly DispatcherTimer _timer;
    private readonly Action? _onClick;
    private bool _closing;
    private bool _closed;

    private ToastWindow(string title, string text, bool error, Action? onClick)
    {
        _onClick = onClick;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 372;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = Brand.TextFont;
        Opacity = 0;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var mark = new Image { Source = Brand.Mark, Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = error ? Brand.Solid(Color.FromRgb(0xFF, 0x8A, 0x80)) : Brand.TextBrush,
            // Заголовок в некоторых языках длиннее строки: переносим, но не больше двух строк.
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 20 * 2,
        };
        var body = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            LineHeight = 17,
            Foreground = Brand.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 17 * 3,
            Margin = new Thickness(0, 3, 0, 0),
            Visibility = string.IsNullOrWhiteSpace(text) || text == title ? Visibility.Collapsed : Visibility.Visible,
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(heading);
        texts.Children.Add(body);
        var close = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, -4, -6, 0),
            Cursor = Cursors.Hand,
            ToolTip = L.ToastClose,
            Child = Icons.Element(Glyph.Close, Brand.MutedBrush, 12),
        };
        var closeHover = Brand.Solid(Colors.White, 0x1A);
        close.MouseEnter += (_, _) => close.Background = closeHover;
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            FadeOut();
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(close, 2);
        grid.Children.Add(mark);
        grid.Children.Add(texts);
        grid.Children.Add(close);

        // Полоса слева: обычное уведомление — цвета знака (Ember → Amber), ошибка — Ember.
        var stripeBrush = error ? Brand.EmberBrush : Gradient();
        var stripe = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = stripeBrush, Margin = new Thickness(0, 2, 12, 2) };
        var layout = new DockPanel();
        DockPanel.SetDock(stripe, Dock.Left);
        layout.Children.Add(stripe);
        layout.Children.Add(grid);

        var card = new Border
        {
            Background = Brand.Solid(Brand.Coal, 0xFA),
            BorderBrush = Brand.Solid(Colors.White, 0x1F),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 14, 16, 14),
            Margin = new Thickness(8), // место под тень
            Child = layout,
            Cursor = onClick is not null ? Cursors.Hand : Cursors.Arrow,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.35,
                Color = Colors.Black,
            },
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            var action = _onClick;
            FadeOut();
            action?.Invoke();
        };
        Content = card;

        // Пока курсор над уведомлением, оно не исчезает.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(onClick is not null || error ? 7 : 4.5) };
        _timer.Tick += (_, _) => FadeOut();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { if (!_closing) _timer.Start(); };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            var ex = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE, new IntPtr(ex
                | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
            NativeMethods.SetWindowDisplayAffinity(h, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea; // основной монитор, в DIP
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            if (_current == this)
                _current = null;
        };
    }

    /// <summary>Показывает уведомление. Возвращает false, если окно создать не удалось (тогда нужен системный вариант).</summary>
    public static bool TryShow(string title, string text, bool error, Action? onClick)
    {
        try
        {
            _current?.CloseNow();
            var toast = new ToastWindow(title, text, error, onClick);
            _current = toast;
            toast.Show();
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.Error("Не удалось показать уведомление Shotly", ex);
            return false;
        }
    }

    public static void CloseAll() => _current?.CloseNow();

    private void CloseNow()
    {
        _closing = true;
        _timer.Stop();
        SafeClose();
    }

    private void SafeClose()
    {
        if (!_closed)
            Close();
    }

    private void FadeOut()
    {
        if (_closing)
            return;
        _closing = true;
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => SafeClose();
        BeginAnimation(OpacityProperty, fade);
    }

    private static Brush Gradient()
    {
        var brush = new LinearGradientBrush(Brand.Ember, Brand.Amber, 90);
        brush.Freeze();
        return brush;
    }
}
