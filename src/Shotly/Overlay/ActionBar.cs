using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Shotly.Editor;
using Shotly.UI;

namespace Shotly.Overlay;

public enum BarCommand { Copy, Save, Cancel, Edit, Undo, Redo }

/// <summary>
/// Панель рядом с выделением. Два режима:
/// после выделения — «Правка · Копировать · Сохранить · ×»;
/// в режиме правки — инструменты, цвета, отмена/повтор и те же действия.
/// Размеры в DIP, поэтому панель одинакова на мониторах со 100% и 200%.
/// Оформление по брендбуку: фон Coal, выбранный инструмент — Ember, текст Onest.
/// </summary>
public sealed class ActionBar : Border
{
    private static readonly Brush HoverBrush = Brand.Solid(Colors.White, 0x1A);
    private static readonly Brush ActiveBrush = Brand.EmberBrush;
    private static readonly Brush ActiveHoverBrush = Brand.Solid(Color.FromRgb(0xFF, 0x6A, 0x33));
    private static readonly Brush ForegroundBrush = Brand.TextBrush;
    private static readonly Brush ActiveForegroundBrush = Brand.CoalBrush;
    private static readonly Brush DisabledBrush = Brand.Solid(Brand.Text, 0x55);
    private static readonly Brush DividerBrush = Brand.Solid(Colors.White, 0x24);
    private static readonly Brush SwatchEdgeBrush = Brand.Solid(Colors.White, 0x59);

    private string _signature = string.Empty;

    public event Action<BarCommand>? CommandInvoked;
    public event Action<EditorTool>? ToolChosen;
    public event Action<Color>? ColorChosen;

    /// <summary>Тоньше (−1) или толще (+1).</summary>
    public event Action<int>? SizeStepped;

    public ActionBar()
    {
        Background = Brand.Solid(Brand.Coal, 0xF5);
        BorderBrush = DividerBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(4);
        Cursor = Cursors.Arrow;
        SnapsToDevicePixels = true;
        TextElement.SetFontFamily(this, Brand.TextFont);

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
            : $"edit|{editor.Tool}|{editor.Color}|{editor.CanUndo}|{editor.CanRedo}|{editor.HasSize}|{editor.SizeLevel}";
        if (signature == _signature)
            return;
        _signature = signature;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (editor is null)
        {
            panel.Children.Add(Button(Glyph.Edit, "Правка", "Стрелки, рамки, текст, маркер, пикселизация и размытие (E)", BarCommand.Edit));
            panel.Children.Add(Divider());
        }
        else
        {
            panel.Children.Add(Tool(Glyph.Arrow, "Стрелка (A). С Shift — ровный угол", EditorTool.Arrow, editor.Tool));
            panel.Children.Add(Tool(Glyph.Rectangle, "Рамка (R). С Shift — квадрат", EditorTool.Rectangle, editor.Tool));
            panel.Children.Add(Tool(Glyph.Marker, "Маркер (M). С Shift — прямая линия", EditorTool.Marker, editor.Tool));
            panel.Children.Add(Tool(Glyph.Text, "Текст (T). Текст можно перетащить за рамку", EditorTool.Text, editor.Tool));
            panel.Children.Add(Divider());
            panel.Children.Add(Tool(Glyph.Blur, "Размытие области (B)", EditorTool.Blur, editor.Tool));
            panel.Children.Add(Tool(Glyph.BlurBrush, "Размытие кистью (Shift+B). С Shift — прямая линия", EditorTool.BlurBrush, editor.Tool));
            panel.Children.Add(Tool(Glyph.Pixelate, "Пикселизация (P)", EditorTool.Pixelate, editor.Tool));
            panel.Children.Add(Divider());
            AddSizeControl(panel, editor);
            panel.Children.Add(Divider());
            foreach (var color in EditorState.Palette)
                panel.Children.Add(Swatch(color, color == editor.Color));
            panel.Children.Add(Divider());
            panel.Children.Add(Button(Glyph.Undo, null, "Отменить (Ctrl+Z)", BarCommand.Undo, editor.CanUndo));
            panel.Children.Add(Button(Glyph.Redo, null, "Повторить (Ctrl+Y)", BarCommand.Redo, editor.CanRedo));
            panel.Children.Add(Divider());
        }

        panel.Children.Add(Button(Glyph.Copy, "Копировать", "Копировать в буфер (Enter, Ctrl+C)", BarCommand.Copy));
        panel.Children.Add(Button(Glyph.Save, "Сохранить", "Сохранить в файл (Ctrl+S)", BarCommand.Save));
        panel.Children.Add(Divider());
        panel.Children.Add(Button(Glyph.Close, null, "Закрыть (Esc, а при правке — только эта кнопка)", BarCommand.Cancel));
        Child = panel;
    }

    private UIElement Button(Glyph glyph, string? label, string tooltip, BarCommand command, bool enabled = true)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Icons.Element(glyph, enabled ? ForegroundBrush : DisabledBrush));
        if (label is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = Brand.TextFont,
                FontWeight = FontWeights.Medium,
                FontSize = 12.5,
                Foreground = ForegroundBrush,
                Margin = new Thickness(7, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        return Clickable(content, tooltip, active: false, enabled, () => CommandInvoked?.Invoke(command));
    }

    private UIElement Tool(Glyph glyph, string tooltip, EditorTool tool, EditorTool current)
    {
        bool active = tool == current;
        return Clickable(Icons.Element(glyph, active ? ActiveForegroundBrush : ForegroundBrush), tooltip, active, enabled: true,
            () => ToolChosen?.Invoke(tool));
    }

    /// <summary>
    /// «− ● +»: точка показывает выбранную толщину в текущем цвете.
    /// У размытия области размера нет, но место под регулятор остаётся, чтобы панель не прыгала.
    /// </summary>
    private void AddSizeControl(Panel panel, EditorState editor)
    {
        bool has = editor.HasSize;
        int level = editor.SizeLevel;
        bool text = editor.Tool == EditorTool.Text;
        const string keys = " ([ ] или колесо мыши)";

        bool canLess = has && level > 0;
        panel.Children.Add(Compact(Clickable(Icons.Element(Glyph.Minus, canLess ? ForegroundBrush : DisabledBrush),
            text ? "Текст мельче" + keys : "Тоньше" + keys, active: false, canLess, () => SizeStepped?.Invoke(-1))));

        double d = 4 + level * 2.5; // 4…14 DIP
        var dot = new Border
        {
            Width = d,
            Height = d,
            CornerRadius = new CornerRadius(d / 2),
            Background = has ? Brand.Solid(editor.Color) : DisabledBrush,
            BorderBrush = SwatchEdgeBrush,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new Border
        {
            Width = 18,
            Background = Brushes.Transparent,
            Child = dot,
            ToolTip = has ? (text ? "Размер текста" : "Толщина") + $": {level + 1} из {EditorState.SizeLevels}" : "У размытия области размер не меняется",
        });

        bool canMore = has && level < EditorState.SizeLevels - 1;
        panel.Children.Add(Compact(Clickable(Icons.Element(Glyph.Plus, canMore ? ForegroundBrush : DisabledBrush),
            text ? "Текст крупнее" + keys : "Толще" + keys, active: false, canMore, () => SizeStepped?.Invoke(1))));
    }

    private static Border Compact(Border item)
    {
        item.Padding = new Thickness(5, 7, 5, 7);
        return item;
    }

    private UIElement Swatch(Color color, bool active)
    {
        var dot = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = Brand.Solid(color),
            // Тёмный цвет на тёмной панели виден только по обводке, поэтому она светлее разделителей.
            BorderBrush = active ? ForegroundBrush : SwatchEdgeBrush,
            BorderThickness = new Thickness(active ? 2 : 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Выбранный цвет дополнительно обведён кольцом, чтобы его было видно и у тёмного, и у светлого цвета.
        var ring = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderBrush = active ? Brand.AmberBrush : Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Child = dot,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var item = Clickable(ring, "Цвет", active: false, enabled: true, () => ColorChosen?.Invoke(color));
        item.Padding = new Thickness(3, 5, 3, 5);
        return item;
    }

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
            Padding = new Thickness(9, 7, 9, 7),
            CornerRadius = new CornerRadius(7),
            Background = idle,
            ToolTip = tooltip,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
        };

        if (enabled)
        {
            item.MouseEnter += (_, _) => item.Background = active ? ActiveHoverBrush : HoverBrush;
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
}
