using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Shotly.Core;
using Shotly.Editor;
using Shotly.Overlay;

namespace Shotly.Capture;

public enum CaptureAction { Copy, Save }

public sealed record CaptureResult(PxRect Region, CaptureAction Action, IReadOnlyList<Annotation> Annotations);

public enum SelectionState { Idle, Dragging, Moving, Selected, Editing }

/// <summary>
/// Одна сессия выделения области. Держит общее состояние (в физических пикселях)
/// для всех окон-оверлеев: по одному окну на монитор.
/// </summary>
public sealed class CaptureSession
{
    private const int MinSize = 4; // меньше — считаем кликом, а не выделением

    private readonly List<OverlayWindow> _overlays = new();
    private readonly TaskCompletionSource<CaptureResult?> _tcs = new();
    private (int X, int Y) _anchor;
    private PxRect _moveOrigin;
    private bool _finished;

    public CaptureSession(Snapshot snapshot) => Snapshot = snapshot;

    public Snapshot Snapshot { get; }
    public PxRect? Selection { get; private set; }
    public SelectionState State { get; private set; } = SelectionState.Idle;
    public (int X, int Y) Cursor { get; private set; }

    /// <summary>Редактор: появляется, когда пользователь переходит к правке выделения.</summary>
    public EditorState? Editor { get; private set; }

    /// <summary>Перерисовать все оверлеи.</summary>
    public event Action? Changed;

    public Task<CaptureResult?> RunAsync()
    {
        try
        {
            Cursor = ScreenCapture.CursorPosition();
            foreach (var monitor in Snapshot.Monitors)
            {
                var overlay = new OverlayWindow(this, monitor);
                _overlays.Add(overlay);
                overlay.Show();
            }

            // Активируем оверлей под курсором, чтобы он сразу получал клавиатуру (Esc, Enter, стрелки).
            var active = Monitors.FromPoint(Snapshot.Monitors, Cursor.X, Cursor.Y);
            _overlays.FirstOrDefault(o => o.Monitor == active)?.ActivateNow();
        }
        catch
        {
            CloseOverlays();
            throw;
        }

        return _tcs.Task;
    }

    // ---- Мышь (координаты всегда берём из GetCursorPos: они физические и общие для всех мониторов) ----

    public void OnMouseDown()
    {
        var p = ScreenCapture.CursorPosition();
        Cursor = p;

        if (State == SelectionState.Editing)
        {
            Editor?.Begin(new System.Windows.Point(p.X, p.Y));
            Changed?.Invoke();
            return;
        }

        if (State == SelectionState.Selected && Selection is { } sel && sel.Contains(p.X, p.Y))
        {
            State = SelectionState.Moving;
            _anchor = p;
            _moveOrigin = sel;
        }
        else
        {
            State = SelectionState.Dragging;
            _anchor = p;
            Selection = new PxRect(p.X, p.Y, 0, 0);
        }

        Changed?.Invoke();
    }

    public void OnMouseMove()
    {
        var p = ScreenCapture.CursorPosition();
        if (p == Cursor && State != SelectionState.Dragging)
            return;
        Cursor = p;

        if (State == SelectionState.Editing)
        {
            if (Editor is { IsDrawing: true } editor)
            {
                editor.Update(new System.Windows.Point(p.X, p.Y));
                Changed?.Invoke();
            }
            return;
        }

        switch (State)
        {
            case SelectionState.Dragging:
                // +1: курсор включает пиксель, на котором стоит.
                var r = PxRect.FromPoints(_anchor.X, _anchor.Y, p.X, p.Y);
                Selection = new PxRect(r.X, r.Y, r.Width + 1, r.Height + 1).Intersect(Snapshot.VirtualBounds);
                break;
            case SelectionState.Moving:
                Selection = ClampToMonitors(_moveOrigin.Offset(p.X - _anchor.X, p.Y - _anchor.Y));
                break;
        }

        Changed?.Invoke();
    }

    public void OnMouseUp()
    {
        if (State == SelectionState.Editing)
        {
            Editor?.End();
            Changed?.Invoke();
            return;
        }

        if (State == SelectionState.Dragging)
        {
            if (Selection is not { } sel || sel.Width < MinSize || sel.Height < MinSize)
            {
                // Клик без перетаскивания = весь монитор под курсором.
                Selection = Monitors.FromPoint(Snapshot.Monitors, Cursor.X, Cursor.Y).Bounds;
            }

            State = SelectionState.Selected;
        }
        else if (State == SelectionState.Moving)
        {
            State = SelectionState.Selected;
        }

        Changed?.Invoke();
    }

    /// <summary>Сдвиг (или изменение размера с Shift) выделения стрелками на 1 пиксель, с Ctrl — на 10.</summary>
    public void Nudge(int dx, int dy, bool resize)
    {
        if (State != SelectionState.Selected || Selection is not { } sel)
            return;

        Selection = resize
            ? new PxRect(sel.X, sel.Y, Math.Max(1, sel.Width + dx), Math.Max(1, sel.Height + dy)).Intersect(Snapshot.VirtualBounds)
            : ClampToMonitors(sel.Offset(dx, dy));
        Changed?.Invoke();
    }

    /// <summary>
    /// Не даём сдвинуть выделение в «мёртвую зону» виртуального стола, где нет ни одного монитора
    /// (там BitBlt даёт чёрные пиксели). Если выделение не помещается целиком на один монитор,
    /// ограничиваемся общими границами стола.
    /// </summary>
    private PxRect ClampToMonitors(PxRect r)
    {
        var target = Monitors.FromRect(Snapshot.Monitors, r);
        return r.Width <= target.Bounds.Width && r.Height <= target.Bounds.Height
            ? r.ClampInside(target.Bounds)
            : r.ClampInside(Snapshot.VirtualBounds);
    }

    /// <summary>Перейти от выделения к правке: стрелки, рамки, маркер, текст, пикселизация.</summary>
    public void StartEditing()
    {
        if (State != SelectionState.Selected || Selection is not { } sel)
            return;
        var scale = Monitors.FromRect(Snapshot.Monitors, sel).Scale;
        Editor = new EditorState(Snapshot, sel, scale);
        State = SelectionState.Editing;
        Changed?.Invoke();
    }

    /// <summary>Любое изменение в редакторе (инструмент, цвет, текст, undo) — перерисовать.</summary>
    public void Edit(Action<EditorState> change)
    {
        if (State != SelectionState.Editing || Editor is null)
            return;
        change(Editor);
        Changed?.Invoke();
    }

    /// <summary>Правый клик: сбросить выделение, а если его нет — выйти. В режиме правки ничего не делает, чтобы не потерять работу.</summary>
    public void ResetOrCancel()
    {
        if (State == SelectionState.Editing)
            return;

        if (State == SelectionState.Selected)
        {
            Selection = null;
            State = SelectionState.Idle;
            Changed?.Invoke();
        }
        else
        {
            Cancel();
        }
    }

    public void Complete(CaptureAction action)
    {
        if (State is not (SelectionState.Selected or SelectionState.Editing) || Selection is not { } sel || sel.IsEmpty)
            return;
        Editor?.FinishText();
        Finish(new CaptureResult(sel, action, Editor?.Items.ToList() ?? new List<Annotation>()));
    }

    public void Cancel() => Finish(null);

    private void Finish(CaptureResult? result)
    {
        if (_finished)
            return;
        _finished = true;
        CloseOverlays();
        // Отдаём результат после закрытия окон, чтобы оверлей точно не попал в следующий кадр.
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _tcs.TrySetResult(result)));
    }

    private void CloseOverlays()
    {
        foreach (var o in _overlays)
        {
            try { o.Close(); }
            catch (Exception ex) { Log.Error("Не удалось закрыть оверлей", ex); }
        }
        _overlays.Clear();
        Changed = null;
    }
}
