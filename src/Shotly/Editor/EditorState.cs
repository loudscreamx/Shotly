using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Shotly.Core;

namespace Shotly.Editor;

public enum EditorTool { Arrow, Rectangle, Marker, Text, Pixelate, Blur, BlurBrush }

/// <summary>
/// Состояние редактора внутри одной сессии захвата: инструмент, цвет, список аннотаций, undo/redo.
/// Ничего не знает об окнах: получает точки в физических пикселях от CaptureSession.
/// </summary>
public sealed class EditorState
{
    public static readonly IReadOnlyList<Color> Palette = new[]
    {
        // Первые два и тёмный — цвета палитры Ember из брендбука; зелёный и синий оставлены для разметки.
        Color.FromRgb(0xF2, 0x54, 0x1B), // Ember, оранжево-красный (по умолчанию)
        Color.FromRgb(0xFF, 0xB0, 0x00), // Amber, янтарный
        Color.FromRgb(0x30, 0xA4, 0x6C), // зелёный
        Color.FromRgb(0x3B, 0x9C, 0xFF), // синий
        Color.FromRgb(0x1C, 0x19, 0x17), // Coal, тёплый чёрный
        Color.FromRgb(0xFF, 0xFF, 0xFF), // белый
    };

    /// <summary>Толщины на каждый инструмент по ступеням размера (в DIP при 100%, дальше растут с DPI).</summary>
    private static readonly Dictionary<EditorTool, double[]> Sizes = new()
    {
        [EditorTool.Arrow] = new[] { 2.0, 3.5, 5, 8, 12 },
        [EditorTool.Rectangle] = new[] { 1.5, 2, 3, 5, 8 },
        [EditorTool.Marker] = new[] { 8.0, 12, 18, 28, 40 },
        [EditorTool.Text] = new[] { 12.0, 16, 20, 28, 40 },
        [EditorTool.Pixelate] = new[] { 5.0, 7, 10, 15, 22 },
        [EditorTool.BlurBrush] = new[] { 14.0, 22, 32, 48, 72 },
    };

    public const int SizeLevels = 5;
    private const int DefaultLevel = 2;

    private List<Annotation> _items = new();
    private readonly Stack<Annotation[]> _undo = new();
    private readonly Stack<Annotation[]> _redo = new();
    private readonly Snapshot _snapshot;
    private readonly PxRect _bounds;
    private readonly double _scale;
    private readonly IDictionary<string, int> _levels;
    private Brush? _blurLayer;

    // Повторная правка уже готового текста: список до неё (для Ctrl+Z) и место текста в списке.
    private Annotation[]? _beforeReopen;
    private int _reopenIndex = -1;

    // Перетаскивание текста за рамку.
    private bool _movingText;
    private Point _moveFrom;
    private Point _moveOrigin;

    /// <param name="scale">Масштаб монитора с выделением: толщины линий и размер текста растут вместе с DPI.</param>
    /// <param name="levels">Выбранные размеры по инструментам; общий словарь из настроек, поэтому запоминаются между снимками.</param>
    public EditorState(Snapshot snapshot, PxRect bounds, double scale, IDictionary<string, int>? levels = null)
    {
        _snapshot = snapshot;
        _bounds = bounds;
        _scale = Math.Max(1, scale);
        _levels = levels ?? new Dictionary<string, int>();
    }

    public EditorTool Tool { get; set; } = EditorTool.Arrow;
    public Color Color { get; set; } = Palette[0];

    /// <summary>Аннотация, которая рисуется прямо сейчас (или текст, который набирается).</summary>
    public Annotation? Active { get; private set; }

    public IReadOnlyList<Annotation> Items => _items;
    public bool CanUndo => _undo.Count > 0 || Active is TextAnnotation;
    public bool CanRedo => _redo.Count > 0;
    public bool IsTyping => Active is TextAnnotation;

    /// <summary>Мышь зажата: рисуется фигура или перетаскивается текст.</summary>
    public bool IsDrawing => Active is not null and not TextAnnotation || _movingText;

    /// <summary>Есть ли у инструмента размер (у размытия области его нет: сила размытия общая).</summary>
    public bool HasSize => Sizes.ContainsKey(Tool);

    /// <summary>Ступень размера текущего инструмента, от 0 до <see cref="SizeLevels"/> − 1.</summary>
    public int SizeLevel => LevelOf(Tool);

    /// <summary>Толщина кисти текущего инструмента в физических пикселях (для круга-предпросмотра у курсора).</summary>
    public double BrushDiameter => Tool is EditorTool.Marker or EditorTool.BlurBrush ? SizeOf(Tool) : 0;

    /// <summary>Всё, что нужно нарисовать, включая незавершённую аннотацию.</summary>
    public IEnumerable<Annotation> Visible
    {
        get
        {
            foreach (var a in _items)
                yield return a;
            if (Active is not null)
                yield return Active;
        }
    }

    private int LevelOf(EditorTool tool) =>
        _levels.TryGetValue(tool.ToString(), out var l) ? Math.Clamp(l, 0, SizeLevels - 1) : DefaultLevel;

    private double SizeOf(EditorTool tool) =>
        Sizes.TryGetValue(tool, out var steps) ? steps[LevelOf(tool)] * _scale : 0;

    /// <summary>Сделать тоньше (−1) или толще (+1). Размер набираемого текста меняется сразу.</summary>
    public void StepSize(int delta)
    {
        if (!HasSize)
            return;
        _levels[Tool.ToString()] = Math.Clamp(SizeLevel + delta, 0, SizeLevels - 1);
        if (Tool == EditorTool.Text && Active is TextAnnotation t)
            t.FontSize = SizeOf(EditorTool.Text);
    }

    /// <summary>Текст под точкой: сначала набираемый, потом готовые (верхний — последний нарисованный).</summary>
    public TextAnnotation? TextAt(Point p)
    {
        if (Active is TextAnnotation active && active.HitTest(p))
            return active;
        if (Tool != EditorTool.Text)
            return null; // другими инструментами рисуют поверх текста, а не двигают его
        for (int i = _items.Count - 1; i >= 0; i--)
            if (_items[i] is TextAnnotation t && t.HitTest(p))
                return t;
        return null;
    }

    public void Begin(Point p)
    {
        p = Clamp(p);

        if (TextAt(p) is { } text)
        {
            if (text != Active)
            {
                FinishText();
                Reopen(text);
            }
            _movingText = true;
            _moveFrom = p;
            _moveOrigin = ((TextAnnotation)Active!).Origin;
            return;
        }

        FinishText();
        double size = SizeOf(Tool);
        Active = Tool switch
        {
            EditorTool.Arrow => new ArrowAnnotation(Color, p, size),
            EditorTool.Rectangle => new RectAnnotation(Color, p, size),
            EditorTool.Marker => new MarkerAnnotation(Color, p, size),
            EditorTool.Text => new TextAnnotation(Color, p, size),
            EditorTool.Pixelate => new PixelateAnnotation(_snapshot, p, size),
            EditorTool.Blur => new BlurAnnotation(BlurFill(), p),
            EditorTool.BlurBrush => new BlurBrushAnnotation(BlurFill(), p, size),
            _ => null,
        };
    }

    /// <param name="constrain">Зажат Shift: кисть и маркер ведут прямую, стрелка — под ровным углом, рамка — квадрат.</param>
    public void Update(Point p, bool constrain = false)
    {
        p = Clamp(p);
        if (_movingText && Active is TextAnnotation t)
        {
            t.Origin = _moveOrigin + (p - _moveFrom);
            return;
        }

        switch (Active)
        {
            case DragAnnotation drag: drag.End = constrain ? Clamp(drag.Constrain(p)) : p; break;
            case StrokeAnnotation stroke: stroke.Add(p, constrain); break;
        }
    }

    public void End()
    {
        if (_movingText)
        {
            _movingText = false; // текст остаётся в наборе: можно дописать или подвинуть ещё раз
            return;
        }
        if (Active is null or TextAnnotation)
            return; // текст завершается не отпусканием мыши, а Enter / Esc / кликом в другом месте
        Commit();
    }

    public void TypeText(string text)
    {
        if (Active is not TextAnnotation t || string.IsNullOrEmpty(text))
            return;
        foreach (var ch in text)
        {
            if (!char.IsControl(ch))
                t.Text += ch;
        }
    }

    public void NewLine()
    {
        if (Active is TextAnnotation t)
            t.Text += "\n";
    }

    public void Backspace()
    {
        if (Active is not TextAnnotation { Text.Length: > 0 } t)
            return;
        // Эмодзи и другие суррогатные пары удаляем целиком.
        int cut = t.Text.Length >= 2 && char.IsLowSurrogate(t.Text[^1]) && char.IsHighSurrogate(t.Text[^2]) ? 2 : 1;
        t.Text = t.Text[..^cut];
    }

    /// <summary>Отменить фигуру, которая рисуется прямо сейчас (Esc с зажатой мышью). Текст возвращается на место.</summary>
    public void CancelActive()
    {
        if (_movingText && Active is TextAnnotation t)
        {
            t.Origin = _moveOrigin;
            _movingText = false;
        }
        else if (IsDrawing)
        {
            Active = null;
        }
    }

    /// <summary>Завершить набор текста (Enter, Esc, клик в другом месте, сохранение).</summary>
    public void FinishText()
    {
        if (Active is TextAnnotation t)
        {
            t.IsEditing = false;
            _movingText = false;
            Commit();
        }
    }

    public void Undo()
    {
        if (IsDrawing)
            return; // мышь ещё зажата — сначала доделать или отменить фигуру (Esc)

        if (Active is TextAnnotation t)
        {
            // Набираемый текст отменяется как обычная аннотация и возвращается по Ctrl+Y.
            // Если это правка готового текста, возвращается его прежний вид.
            t.IsEditing = false;
            Active = null;
            if (!t.IsEmpty)
                _redo.Push(WithText(t));
            if (_beforeReopen is { } before)
                _items = before.ToList();
            ForgetReopen();
            return;
        }

        if (_undo.Count == 0)
            return;
        _redo.Push(_items.ToArray());
        _items = _undo.Pop().ToList();
    }

    public void Redo()
    {
        if (IsDrawing)
            return;
        FinishText();
        if (_redo.Count == 0)
            return;
        _undo.Push(_items.ToArray());
        _items = _redo.Pop().ToList();
    }

    /// <summary>Готовый текст снова становится редактируемым: вместо него в списке появится правленая копия.</summary>
    private void Reopen(TextAnnotation text)
    {
        _beforeReopen = _items.ToArray();
        _reopenIndex = _items.IndexOf(text);
        _items.RemoveAt(_reopenIndex);
        var copy = text.Clone();
        copy.IsEditing = true;
        Active = copy;
    }

    private void ForgetReopen()
    {
        _beforeReopen = null;
        _reopenIndex = -1;
    }

    /// <summary>Список с этим текстом на своём месте: туда, где он был до правки, или поверх всего.</summary>
    private Annotation[] WithText(TextAnnotation t)
    {
        var list = _items.ToList();
        list.Insert(_reopenIndex >= 0 ? Math.Min(_reopenIndex, list.Count) : list.Count, t);
        return list.ToArray();
    }

    private void Commit()
    {
        var a = Active;
        Active = null;
        var before = _beforeReopen;
        try
        {
            if (a is null)
                return;

            if (before is not null && a is TextAnnotation edited)
            {
                // Правка готового текста: пустой — удалён, иначе встаёт на прежнее место в списке.
                var original = (TextAnnotation)before[_reopenIndex];
                if (edited.Text == original.Text && edited.Origin == original.Origin && edited.FontSize == original.FontSize)
                {
                    _items = before.ToList(); // ничего не поменялось — и в истории ничего нет
                    return;
                }
                if (!edited.IsEmpty)
                    _items = WithText(edited).ToList();
                _undo.Push(before);
                _redo.Clear();
                return;
            }

            if (a is StrokeAnnotation stroke)
                stroke.FixStraight();
            if (a.IsEmpty)
                return;
            if (a is PixelateAnnotation p)
                p.IsPreview = false;
            _undo.Push(_items.ToArray());
            _items.Add(a);
            _redo.Clear();
        }
        finally
        {
            ForgetReopen();
        }
    }

    /// <summary>Размытая копия выделения: считается при первом размытии и дальше общая для всех мазков.</summary>
    private Brush BlurFill() => _blurLayer ??= BlurLayer.Create(_snapshot, _bounds, (int)Math.Round(10 * _scale));

    /// <summary>Рисовать можно только внутри выделения: за его пределами аннотацию всё равно не будет видно.</summary>
    private Point Clamp(Point p) =>
        new(Math.Clamp(p.X, _bounds.X, _bounds.Right), Math.Clamp(p.Y, _bounds.Y, _bounds.Bottom));
}
