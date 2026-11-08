using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Shotly.Native;

namespace Shotly.UI;

/// <summary>
/// Маленький счётчик обратного отсчёта в углу основного монитора.
/// Прозрачен для мыши и исключён из захвата экрана, но всё равно закрывается до снимка.
/// </summary>
public sealed class CountdownWindow : Window
{
    private readonly TextBlock _number;

    private CountdownWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 96;
        Height = 96;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var area = SystemParameters.WorkArea; // основной монитор, в DIP
        Left = area.Right - Width - 24;
        Top = area.Bottom - Height - 24;

        // По брендбуку: круг цвета Coal, кольцо Ember сверху и Amber снизу, как половины знака, цифра Unbounded.
        _number = new TextBlock
        {
            FontSize = 36,
            FontWeight = FontWeights.Bold,
            FontFamily = Brand.DisplayFont,
            Foreground = Brand.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3),
        };
        var ring = new LinearGradientBrush(Brand.Ember, Brand.Amber, 90);
        ring.Freeze();
        Content = new Border
        {
            CornerRadius = new CornerRadius(48),
            Background = Brand.Solid(Brand.Coal, 0xF0),
            BorderBrush = ring,
            BorderThickness = new Thickness(3),
            Child = _number,
        };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            var ex = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(h, NativeMethods.GWL_EXSTYLE, new IntPtr(ex
                | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE));
            NativeMethods.SetWindowDisplayAffinity(h, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        };
    }

    public static async Task RunAsync(int seconds)
    {
        var window = new CountdownWindow();
        window.Show();
        try
        {
            for (int i = seconds; i > 0; i--)
            {
                window._number.Text = i.ToString();
                await Task.Delay(1000);
            }
        }
        finally
        {
            window.Close();
        }

        // Даём DWM убрать окно с экрана до снимка.
        await Task.Delay(150);
    }
}
