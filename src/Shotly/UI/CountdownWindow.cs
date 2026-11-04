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

        _number = new TextBlock
        {
            FontSize = 40,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new Border
        {
            CornerRadius = new CornerRadius(48),
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1F, 0x1F, 0x1F)),
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
