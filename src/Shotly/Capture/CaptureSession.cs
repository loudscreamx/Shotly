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
    private readonly Func<PxRect, bool>? _copyOnSelect;
    private DispatcherTimer? _nudgeCopy;
    private DispatcherTimer? _escDisarm;

    private readonly IDictionary<string, double>? _editorSizes;
    private readonly WindowFinder _windows;

    /// <param name="copyOnSelect">
    /// Если задано — каждое готовое выделение сразу копируется в буфер обмена (возвращает false, если не вышло).
    /// </param>
    /// <param name="editorSizes">Размеры инструментов из настроек: редактор меняет их на месте.</param>
    public CaptureSession(Snapshot snapshot, Func<PxRect, bool>? copyOnSelect = null, IDictionary<string, double>? editorSizes = null)
    {
        Snapshot = snapshot;
        _copyOnSelect = copyOnSelect;
        _editorSizes = editorSizes;
        // Сразу после снимка и до показа оверлеев: список окон совпадает с кадром и не содержит наших окон.
        _windows = WindowFinder.Collect(snapshot);
    }

    public Snapshot Snapshot { get; }
    public PxRect? Selection { get; private set; }
    public SelectionState State { get; private set; } = SelectionState.Idle;
    public (int X, int Y) Cursor { get; private set; }

    /// <summary>
    /// Что снимет клик без перетаскивания: окно под курсором, а на пустом рабочем столе — весь монитор.
    /// Оверлеи подсвечивают эту область, пока выделения нет. Null — подсветки нет (выделение уже есть).
    /// </summary>
    public PxRect? Hover { get; private set; }

    /// <summary>Редактор: появляется, когда пользователь переходит к правке выделения.</summary>
    public EditorState? Editor { get; private set; }

    /// <summary>Выделение, которое сейчас лежит в буфере обмена (если включено копирование при выделении).</summary>
    public PxRect? CopiedSelection { get; private set; }

    /// <summary>Перерисовать все оверлеи.</summary>
    public event Action? Changed;

    /// <summary>Выделение готово (отпустили мышь): оверлеи проигрывают короткую вспышку рамки.</summary>
    public event Action? SelectionCommitted;

    public Task<CaptureResult?> RunAsync()
    {
        try
        {
            Cursor = ScreenCapture.CursorPosition();
            Hover = ClickTarget(Cursor.X, Cursor.Y);
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
            // Подсветку оставляем, пока мышь не сдвинулась: отпустят на месте — снимем подсвеченное.
            State = SelectionState.Dragging;
            _anchor = p;
            Hover = ClickTarget(p.X, p.Y);
            Selection = new PxRect(p.X, p.Y, 0, 0);
        }

        Changed?.Invoke();
    }

    /// <param name="shift">Зажат Shift: в редакторе прямая линия кистью, ровный угол стрелки, квадратная рамка.</param>
    public void OnMouseMove(bool shift = false)
    {
        var p = ScreenCapture.CursorPosition();
        if (p == Cursor && State != SelectionState.Dragging)
            return;
        Cursor = p;

        if (State == SelectionState.Editing)
        {
            if (Editor is { IsDrawing: true } editor)
            {
                editor.Update(new System.Windows.Point(p.X, p.Y), shift);
                Changed?.Invoke();
            }
            return;
        }

        switch (State)
        {
            case SelectionState.Idle:
                Hover = ClickTarget(p.X, p.Y);
                break;
            case SelectionState.Dragging:
                // +1: курсор включает пиксель, на котором стоит.
                var r = PxRect.FromPoints(_anchor.X, _anchor.Y, p.X, p.Y);
                if (Hover is not null && r.Width < MinSize && r.Height < MinSize)
                    break; // дрожание руки при клике — ещё не выделение, подсветка остаётся
                Hover = null;
                Selection = new PxRect(r.X, r.Y, r.Width + 1, r.Height + 1).Intersect(Snapshot.VirtualBounds);
                break;
            case SelectionState.Moving:
                Selection = ClampToMonitors(_moveOrigin.Offset(p.X - _anchor.X, p.Y - _anchor.Y));
                break;
        }

        Changed?.Invoke();
    }

    /// <summary>Shift нажали или отпустили, не двигая мышь: сразу перестроить фигуру, которая рисуется.</summary>
    public void OnShiftChanged(bool shift)
    {
        if (State != SelectionState.Editing || Editor is not { IsDrawing: true } editor)
            return;
        editor.Update(new System.Windows.Point(Cursor.X, Cursor.Y), shift);
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

        bool pulse = true;
        if (State == SelectionState.Dragging)
        {
            if (Selection is not { } sel || sel.Width < MinSize || sel.Height < MinSize)
            {
                // Клик без перетаскивания = подсвеченное окно, а на рабочем столе — весь монитор.
                Selection = Hover ?? ClickTarget(_anchor.X, _anchor.Y);
            }

            Hover = null;
            State = SelectionState.Selected;
        }
        else if (State == SelectionState.Moving)
        {
            State = SelectionState.Selected;
            // Простой клик внутри выделения (например, первый из двойного) ничего не меняет — без вспышки.
            pulse = Selection != _moveOrigin;
        }
        else
        {
            Changed?.Invoke();
            return;
        }

        Changed?.Invoke();
        CommitSelection(pulse);
    }

    /// <summary>Окно под точкой, а если там только рабочий стол — монитор целиком.</summary>
    private PxRect ClickTarget(int x, int y) =>
        _windows.At(x, y) ?? Monitors.FromPoint(Snapshot.Monitors, x, y).Bounds;

    /// <summary>
    /// Готовое выделение: вспышка рамки, а если включено — сразу копия в буфер обмена.
    /// Копируем после отрисовки вспышки, чтобы отклик появлялся мгновенно даже на 4K.
    /// </summary>
    private void CommitSelection(bool pulse)
    {
        _nudgeCopy?.Stop();
        if (State != SelectionState.Selected || Selection is not { } sel || sel.IsEmpty)
            return;

        if (pulse)
            SelectionCommitted?.Invoke();

        if (_copyOnSelect is null || CopiedSelection == sel)
            return;

        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            // За это время выделение могли сдвинуть, начать правку или закрыть снимок.
            if (_finished || State != SelectionState.Selected || Selection != sel)
                return;
            if (_copyOnSelect(sel))
            {
                CopiedSelection = sel;
                Changed?.Invoke();
            }
        }));
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

        // Стрелки жмут сериями: копируем, когда выделение перестало двигаться.
        if (_copyOnSelect is not null)
        {
            if (_nudgeCopy is null)
            {
                _nudgeCopy = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                _nudgeCopy.Tick += (_, _) => CommitSelection(pulse: false);
            }
            _nudgeCopy.Stop();
            _nudgeCopy.Start();
        }
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
        Editor = new EditorState(Snapshot, sel, scale, _editorSizes);
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
            Hover = ClickTarget(Cursor.X, Cursor.Y);
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

    /// <summary>Esc уже нажат один раз при правках: второй Esc в ближайшие секунды закроет снимок.</summary>
    public bool CloseArmed { get; private set; }

    /// <summary>
    /// Esc в редакторе. Без правок закрывает сразу. С правками первый Esc показывает подсказку,
    /// второй (в течение 2,5 с) закрывает: случайное нажатие не выбросит работу, а закрыть всё равно просто.
    /// </summary>
    public void RequestCloseByEsc()
    {
        if (CloseArmed || Editor is null || Editor.Items.Count == 0)
        {
            Cancel();
            return;
        }

        CloseArmed = true;
        if (_escDisarm is null)
        {
            _escDisarm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _escDisarm.Tick += (_, _) =>
            {
                _escDisarm.Stop();
                CloseArmed = false;
                Changed?.Invoke();
            };
        }
        _escDisarm.Stop();
        _escDisarm.Start();
        Changed?.Invoke();
    }

    private void Finish(CaptureResult? result)
    {
        if (_finished)
            return;
        _finished = true;
        _nudgeCopy?.Stop();
        _escDisarm?.Stop();
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
        SelectionCommitted = null;
    }
}
