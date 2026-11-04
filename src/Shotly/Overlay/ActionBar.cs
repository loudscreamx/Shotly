using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shotly.Editor;

namespace Shotly.Overlay;

public enum BarCommand { Copy, Save, Cancel, Edit, Undo, Redo }

/// <summary>
/// Панель рядом с выделением. Два режима:
/// после выделения — «Правка · Копировать · Сохранить · ×»;
/// в режиме правки — инструменты, цвета, отмена/повтор и те же действия.
/// Размеры в DIP, поэтому панель одинакова на мониторах со 100% и 200%.
/// </summary>
public sealed class ActionBar : Border
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol";
    private const string TextFont = "Segoe UI Variable Text, Segoe UI";
    private static readonly Brush HoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush ActiveBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0x3B, 0x9C, 0xFF)));
    private static readonly Brush ForegroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)));
    private static readonly Brush DisabledBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0xF5, 0xF5)));
    private static readonly Brush DividerBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));

    private string _signature = string.Empty;

    public event Action<BarCommand>? CommandInvoked;
    public event Action<EditorTool>? ToolChosen;
    public event Action<Color>? ColorChosen;

    public ActionBar()
    {
        Background = Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x1F, 0x1F, 0x1F)));
        BorderBrush = DividerBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(4);
        Cursor = Cursors.Arrow;
        SnapsToDevicePixels = true;

        // Клики по панели не должны начинать новое выделение или рисование под ней.
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        MouseLeftButtonUp += (_, e) => e.Handled = true;
        MouseRightButtonUp += (_, e) => e.Handled = true;

        Update(null);
    }

    /// <summary>Перестраивает панель, только если что-то видимое изменилось (не на каждое движение мыши).</summary>
    public void Update(EditorState? editor)
    {
        var signature = editor is null
            ? "select"
            : $"edit|{editor.Tool}|{editor.Color}|{editor.CanUndo}|{editor.CanRedo}";
        if (signature == _signature)
            return;
        _signature = signature;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (editor is null)
        {
            panel.Children.Add(Button("", "Правка", "Стрелки, рамки, текст, маркер, пикселизация (E)", BarCommand.Edit));
            panel.Children.Add(Divider());
        }
        else
        {
            panel.Children.Add(Tool("↗", "Стрелка (A)", EditorTool.Arrow, editor.Tool));
            panel.Children.Add(Tool("▭", "Рамка (R)", EditorTool.Rectangle, editor.Tool));
            panel.Children.Add(Tool("", "Маркер (M)", EditorTool.Marker, editor.Tool));
            panel.Children.Add(Tool("", "Текст (T)", EditorTool.Text, editor.Tool));
            panel.Children.Add(Tool("▦", "Пикселизация (B)", EditorTool.Pixelate, editor.Tool));
            panel.Children.Add(Divider());
            foreach (var color in EditorState.Palette)
                panel.Children.Add(Swatch(color, color == editor.Color));
            panel.Children.Add(Divider());
            panel.Children.Add(Button("", null, "Отменить (Ctrl+Z)", BarCommand.Undo, editor.CanUndo));
            panel.Children.Add(Button("", null, "Повторить (Ctrl+Y)", BarCommand.Redo, editor.CanRedo));
            panel.Children.Add(Divider());
        }

        panel.Children.Add(Button("", "Копировать", "Копировать в буфер (Enter, Ctrl+C)", BarCommand.Copy));
        panel.Children.Add(Button("", "Сохранить", "Сохранить в файл (Ctrl+S)", BarCommand.Save));
        panel.Children.Add(Divider());
        panel.Children.Add(Button("", null, "Отменить снимок (Esc)", BarCommand.Cancel));
        Child = panel;
    }

    private UIElement Button(string glyph, string? label, string tooltip, BarCommand command, bool enabled = true)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Glyph(glyph, enabled ? ForegroundBrush : DisabledBrush));
        if (label is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily(TextFont),
                FontSize = 12,
                Foreground = ForegroundBrush,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        return Clickable(content, tooltip, active: false, enabled, () => CommandInvoked?.Invoke(command));
    }

    private UIElement Tool(string glyph, string tooltip, EditorTool tool, EditorTool current) =>
        Clickable(Glyph(glyph, ForegroundBrush), tooltip, active: tool == current, enabled: true,
            () => ToolChosen?.Invoke(tool));

    private UIElement Swatch(Color color, bool active)
    {
        var dot = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = Frozen(new SolidColorBrush(color)),
            BorderBrush = active ? ForegroundBrush : DividerBrush,
            BorderThickness = new Thickness(active ? 2 : 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var item = Clickable(dot, "Цвет", active: false, enabled: true, () => ColorChosen?.Invoke(color));
        if (item is Border b)
            b.Padding = new Thickness(5, 6, 5, 6);
        return item;
    }

    private static TextBlock Glyph(string glyph, Brush brush) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily(IconFont),
        FontSize = 14,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        MinWidth = 14,
        TextAlignment = TextAlignment.Center,
    };

    private static UIElement Divider() => new Border
    {
        Width = 1,
        Margin = new Thickness(4, 6, 4, 6),
        Background = DividerBrush,
    };

    private static Border Clickable(UIElement content, string tooltip, bool active, bool enabled, Action onClick)
    {
        var idle = active ? ActiveBrush : Brushes.Transparent;
        var item = new Border
        {
            Child = content,
            Padding = new Thickness(9, 6, 9, 6),
            CornerRadius = new CornerRadius(6),
            Background = idle,
            ToolTip = tooltip,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
        };

        if (enabled)
        {
            item.MouseEnter += (_, _) => item.Background = active ? ActiveBrush : HoverBrush;
            item.MouseLeave += (_, _) => item.Background = idle;
        }

        item.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (enabled)
                onClick();
        };
        return item;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
