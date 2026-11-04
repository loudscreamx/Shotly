using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Shotly.Core;

namespace Shotly.Editor;

public enum EditorTool { Arrow, Rectangle, Marker, Text, Pixelate }

/// <summary>
/// Состояние редактора внутри одной сессии захвата: инструмент, цвет, список аннотаций, undo/redo.
/// Ничего не знает об окнах: получает точки в физических пикселях от CaptureSession.
/// </summary>
public sealed class EditorState
{
    public static readonly IReadOnlyList<Color> Palette = new[]
    {
        Color.FromRgb(0xE5, 0x48, 0x4D), // красный
        Color.FromRgb(0xFF, 0xC5, 0x3D), // жёлтый
        Color.FromRgb(0x30, 0xA4, 0x6C), // зелёный
        Color.FromRgb(0x3B, 0x9C, 0xFF), // синий
        Color.FromRgb(0x1F, 0x1F, 0x1F), // чёрный
        Color.FromRgb(0xFF, 0xFF, 0xFF), // белый
    };

    private readonly List<Annotation> _items = new();
    private readonly Stack<Annotation> _redo = new();
    private readonly Snapshot _snapshot;
    private readonly PxRect _bounds;
    private readonly double _scale;

    /// <param name="scale">Масштаб монитора с выделением: толщины линий и размер текста растут вместе с DPI.</param>
    public EditorState(Snapshot snapshot, PxRect bounds, double scale)
    {
        _snapshot = snapshot;
        _bounds = bounds;
        _scale = Math.Max(1, scale);
    }

    public EditorTool Tool { get; set; } = EditorTool.Arrow;
    public Color Color { get; set; } = Palette[0];

    /// <summary>Аннотация, которая рисуется прямо сейчас (или текст, который набирается).</summary>
    public Annotation? Active { get; private set; }

    public IReadOnlyList<Annotation> Items => _items;
    public bool CanUndo => _items.Count > 0 || Active is TextAnnotation;
    public bool CanRedo => _redo.Count > 0;
    public bool IsTyping => Active is TextAnnotation;
    public bool IsDrawing => Active is not null and not TextAnnotation;

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

    public void Begin(Point p)
    {
        FinishText();
        p = Clamp(p);

        Active = Tool switch
        {
            EditorTool.Arrow => new ArrowAnnotation(Color, p, 4 * _scale),
            EditorTool.Rectangle => new RectAnnotation(Color, p, 3 * _scale),
            EditorTool.Marker => new MarkerAnnotation(Color, p, 18 * _scale),
            EditorTool.Text => new TextAnnotation(Color, p, 20 * _scale),
            EditorTool.Pixelate => new PixelateAnnotation(_snapshot, p, 10 * _scale),
            _ => null,
        };
    }

    public void Update(Point p)
    {
        p = Clamp(p);
        switch (Active)
        {
            case DragAnnotation drag: drag.End = p; break;
            case MarkerAnnotation marker: marker.Add(p); break;
        }
    }

    public void End()
    {
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

    /// <summary>Отменить фигуру, которая рисуется прямо сейчас (Esc с зажатой мышью).</summary>
    public void CancelActive()
    {
        if (IsDrawing)
            Active = null;
    }

    /// <summary>Завершить набор текста (Enter, Esc, клик в другом месте, сохранение).</summary>
    public void FinishText()
    {
        if (Active is TextAnnotation t)
        {
            t.IsEditing = false;
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
            t.IsEditing = false;
            Active = null;
            if (!t.IsEmpty)
                _redo.Push(t);
            return;
        }

        if (_items.Count == 0)
            return;
        _redo.Push(_items[^1]);
        _items.RemoveAt(_items.Count - 1);
    }

    public void Redo()
    {
        if (IsDrawing)
            return;
        FinishText();
        if (_redo.Count > 0)
            _items.Add(_redo.Pop());
    }

    private void Commit()
    {
        var a = Active;
        Active = null;
        if (a is null || a.IsEmpty)
            return;
        if (a is PixelateAnnotation p)
            p.IsPreview = false;
        _items.Add(a);
        _redo.Clear();
    }

    /// <summary>Рисовать можно только внутри выделения: за его пределами аннотацию всё равно не будет видно.</summary>
    private Point Clamp(Point p) =>
        new(Math.Clamp(p.X, _bounds.X, _bounds.Right), Math.Clamp(p.Y, _bounds.Y, _bounds.Bottom));
}
