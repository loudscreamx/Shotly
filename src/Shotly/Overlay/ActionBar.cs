using System;
using System.Linq;
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

    /// <summary>Ползунок толщины передвинут (положение от 0 до 1).</summary>
    public event Action<double>? ThicknessChanged;

    // Блок толщины живёт дольше одной перестройки панели: иначе перестройка во время перетаскивания
    // отбирала бы у ползунка мышь. Значение и цвет обновляются на месте.
    private readonly ThicknessSlider _slider = new();
    private readonly Border _sizeDot = new()
    {
        CornerRadius = new CornerRadius(9),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        BorderThickness = new Thickness(1),
    };
    private readonly TextBlock _sizeValue = new()
    {
        Width = 24,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 4, 0),
    };
    private readonly Border _sizeGroup;

    // Палитра своего цвета раскрывается над рядом кнопок и тоже живёт между перестройками.
    private readonly ColorSpectrum _spectrum = new();
    private readonly Border _pickerRow;
    private readonly ContentControl _toolbarRow = new() { Focusable = false };

    /// <summary>Нажата кнопка «Обводка» у текста.</summary>
    public event Action? OutlineToggled;

    /// <summary>Панель изменила размер (раскрылась палитра) — её нужно заново поставить у выделения.</summary>
    public event Action? LayoutChanged;

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

        _slider.ValueChanged += v => ThicknessChanged?.Invoke(v);
        _sizeValue.FontFamily = Brand.TextFont;
        _sizeValue.Foreground = ForegroundBrush;
        _sizeDot.BorderBrush = SwatchEdgeBrush;
        var sizeContent = new StackPanel { Orientation = Orientation.Horizontal };
        sizeContent.Children.Add(new Border { Width = 22, Height = 22, Child = _sizeDot, Margin = new Thickness(4, 0, 2, 0) });
        sizeContent.Children.Add(_slider);
        sizeContent.Children.Add(_sizeValue);
        // Своя светлая подложка выделяет ползунок среди значков, чтобы его было видно сразу.
        _sizeGroup = new Border
        {
            Child = sizeContent,
            Background = Brand.Solid(Colors.White, 0x12),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(2, 3, 2, 3),
            Padding = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _spectrum.ColorPicked += color => ColorChosen?.Invoke(color);
        _spectrum.PickFinished += () => SetPickerOpen(false);
        var pickerContent = new StackPanel();
        pickerContent.Children.Add(new TextBlock
        {
            Text = "Свой цвет",
            FontFamily = Brand.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = ForegroundBrush,
            Margin = new Thickness(2, 0, 0, 6),
        });
        pickerContent.Children.Add(_spectrum);
        _pickerRow = new Border
        {
            Child = pickerContent,
            Padding = new Thickness(8, 6, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed,
        };
        var rows = new StackPanel();
        rows.Children.Add(_pickerRow);
        rows.Children.Add(_toolbarRow);
        Child = rows;

        Update(null);
    }

    private bool PickerOpen => _pickerRow.Visibility == Visibility.Visible;

    private void SetPickerOpen(bool open)
    {
        if (open == PickerOpen)
            return;
        _pickerRow.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _signature = string.Empty; // перерисовать кнопку своего цвета (активна, пока палитра открыта)
        LayoutChanged?.Invoke();
    }

    /// <summary>Перестраивает панель, только если что-то видимое изменилось (не на каждое движение мыши).</summary>
    public void Update(EditorState? editor)
    {
        if (editor is not null)
            SyncSize(editor);

        if (editor is null && PickerOpen)
            SetPickerOpen(false);

        var signature = editor is null
            ? "select"
            : $"edit|{editor.Tool}|{editor.Color}|{editor.CanUndo}|{editor.CanRedo}|{editor.HasSize}|{editor.TextOutline}|{EditorState.CustomColor}|{PickerOpen}";
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
            panel.Children.Add(Tool(Glyph.Pen, "Кисть (D). С Shift — прямая линия", EditorTool.Pen, editor.Tool));
            panel.Children.Add(Tool(Glyph.Marker, "Маркер, полупрозрачный (M). С Shift — прямая линия", EditorTool.Marker, editor.Tool));
            panel.Children.Add(Tool(Glyph.Text, "Текст (T). Рамку можно перетащить, а за углы — изменить размер", EditorTool.Text, editor.Tool));
            panel.Children.Add(Divider());
            panel.Children.Add(Tool(Glyph.Blur, "Размытие области (B)", EditorTool.Blur, editor.Tool));
            panel.Children.Add(Tool(Glyph.BlurBrush, "Размытие кистью (Shift+B). С Shift — прямая линия", EditorTool.BlurBrush, editor.Tool));
            panel.Children.Add(Tool(Glyph.Pixelate, "Пикселизация (P)", EditorTool.Pixelate, editor.Tool));
            panel.Children.Add(Divider());
            AddSizeControl(panel, editor);
            if (editor.Tool == EditorTool.Text)
            {
                bool on = editor.TextOutline;
                panel.Children.Add(Clickable(Icons.Element(Glyph.TextOutline, on ? ActiveForegroundBrush : ForegroundBrush),
                    on ? "Обводка текста включена" : "Обводка текста: контрастный контур вокруг букв", on, enabled: true,
                    () => OutlineToggled?.Invoke()));
            }
            panel.Children.Add(Divider());
            foreach (var color in EditorState.Palette)
                panel.Children.Add(Swatch(color, color == editor.Color));
            panel.Children.Add(CustomSwatch(editor));
            panel.Children.Add(Divider());
            panel.Children.Add(Button(Glyph.Undo, null, "Отменить (Ctrl+Z)", BarCommand.Undo, editor.CanUndo));
            panel.Children.Add(Button(Glyph.Redo, null, "Повторить (Ctrl+Y)", BarCommand.Redo, editor.CanRedo));
            panel.Children.Add(Divider());
        }

        panel.Children.Add(Button(Glyph.Copy, "Копировать", "Копировать в буфер (Enter, Ctrl+C)", BarCommand.Copy));
        panel.Children.Add(Button(Glyph.Save, "Сохранить", "Сохранить в файл (Ctrl+S)", BarCommand.Save));
        panel.Children.Add(Divider());
        panel.Children.Add(Button(Glyph.Close, null, "Закрыть без сохранения (Esc; если есть правки — Esc дважды)", BarCommand.Cancel));
        _toolbarRow.Content = panel;
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

    /// <summary>Ставит на панель блок толщины: кружок-предпросмотр, ползунок и число.</summary>
    private void AddSizeControl(Panel panel, EditorState editor)
    {
        if (_sizeGroup.Parent is Panel old)
            old.Children.Remove(_sizeGroup);
        _sizeGroup.ToolTip = !editor.HasSize
            ? "У размытия области толщина не меняется"
            : editor.Tool == EditorTool.Text
                ? "Размер текста: перетащите ползунок, колесо мыши или [ ]"
                : "Толщина: перетащите ползунок, колесо мыши или [ ]";
        panel.Children.Add(_sizeGroup);
    }

    /// <summary>Значение, цвет и число без перестройки панели (вызывается на каждое изменение редактора).</summary>
    private void SyncSize(EditorState editor)
    {
        bool has = editor.HasSize;
        _slider.Active = has;
        if (!_slider.IsMouseCaptured)
            _slider.Value = editor.SizePosition;

        double d = has ? 4 + 14 * editor.SizePosition : 4; // 4…18 DIP
        _sizeDot.Width = _sizeDot.Height = d;
        _sizeDot.CornerRadius = new CornerRadius(d / 2);
        _sizeDot.Background = has ? Brand.Solid(editor.Color) : DisabledBrush;
        _sizeValue.Text = has ? Math.Round(editor.Size).ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
        _sizeValue.Foreground = has ? ForegroundBrush : DisabledBrush;
    }

    /// <summary>
    /// Кнопка своего цвета: радужный кружок, а когда свой цвет уже выбран — кружок этого цвета.
    /// Щелчок раскрывает палитру над панелью.
    /// </summary>
    private UIElement CustomSwatch(EditorState editor)
    {
        bool customActive = !EditorState.Palette.Contains(editor.Color);
        Brush fill = EditorState.CustomColor is { } custom
            ? Brand.Solid(custom)
            : new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(0xFF, 0x3B, 0x30), 0),
                new(Color.FromRgb(0xFF, 0xCC, 0x00), 0.25),
                new(Color.FromRgb(0x34, 0xC7, 0x59), 0.5),
                new(Color.FromRgb(0x00, 0x7A, 0xFF), 0.75),
                new(Color.FromRgb(0xAF, 0x52, 0xDE), 1),
            }, 45);
        var dot = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = fill,
            BorderBrush = customActive ? ForegroundBrush : SwatchEdgeBrush,
            BorderThickness = new Thickness(customActive ? 2 : 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = EditorState.CustomColor is null ? "" : "+",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brand.Solid(Colors.White, 0xE0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            },
        };
        var ring = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderBrush = customActive || PickerOpen ? Brand.AmberBrush : Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Child = dot,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var item = Clickable(ring, EditorState.CustomColor is null || customActive ? "Свой цвет: открыть палитру" : "Свой цвет (щёлкните ещё раз, чтобы выбрать другой)", active: false, enabled: true, () =>
        {
            if (!PickerOpen && EditorState.CustomColor is { } c && !customActive)
                ColorChosen?.Invoke(c); // повторный щелчок по уже выбранному своему цвету сначала включает его
            else
                SetPickerOpen(!PickerOpen);
        });
        item.Padding = new Thickness(3, 5, 3, 5);
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
