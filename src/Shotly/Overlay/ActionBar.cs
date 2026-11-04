using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shotly.Overlay;

public enum BarCommand { Copy, Save, Cancel }

/// <summary>
/// Мини-панель действий рядом с выделением: тёмная плашка, иконки Segoe Fluent и короткие подписи.
/// Размеры в DIP, поэтому панель одинакова на мониторах со 100% и 200%.
/// </summary>
public sealed class ActionBar : Border
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ForegroundBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

    public event Action<BarCommand>? CommandInvoked;

    public ActionBar()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1F, 0x1F, 0x1F));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(4);
        Cursor = Cursors.Arrow;
        SnapsToDevicePixels = true;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Item("", "Копировать", "Копировать в буфер (Enter, Ctrl+C)", BarCommand.Copy));
        panel.Children.Add(Item("", "Сохранить", "Сохранить в файл (Ctrl+S)", BarCommand.Save));
        panel.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(4, 6, 4, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
        });
        panel.Children.Add(Item("", null, "Отменить (Esc)", BarCommand.Cancel));
        Child = panel;

        // Клики по панели не должны начинать новое выделение под ней.
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        MouseLeftButtonUp += (_, e) => e.Handled = true;
        MouseRightButtonUp += (_, e) => e.Handled = true;
    }

    private UIElement Item(string glyph, string? label, string tooltip, BarCommand command)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(IconFont),
            FontSize = 14,
            Foreground = ForegroundBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (label is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 12,
                Foreground = ForegroundBrush,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var item = new Border
        {
            Child = content,
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
        };
        item.MouseEnter += (_, _) => item.Background = HoverBrush;
        item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
        item.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            CommandInvoked?.Invoke(command);
        };
        return item;
    }
}
