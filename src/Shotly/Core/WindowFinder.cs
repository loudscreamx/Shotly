using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Shotly.Native;

namespace Shotly.Core;

/// <summary>
/// Окна верхнего уровня на момент снимка: для подсветки окна под курсором в режиме выделения.
/// Список собирается один раз за сессию, сразу после снимка и до появления оверлеев, поэтому
/// совпадает с замороженным кадром, не содержит наших окон, а движение мыши стоит только
/// перебора нескольких десятков прямоугольников без единого вызова Win32.
/// </summary>
public sealed class WindowFinder
{
    // Рабочий стол с иконками: клик по нему снимает весь монитор, а не «окно» размером со все экраны.
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW",
    };

    private const int MinSize = 8;

    private readonly List<PxRect> _windows;

    private WindowFinder(List<PxRect> windows) => _windows = windows;

    /// <summary>Окна в порядке Z (сверху вниз), обрезанные по границам снимка.</summary>
    public IReadOnlyList<PxRect> Windows => _windows;

    /// <summary>Самое верхнее окно, содержащее точку, или null (под курсором только рабочий стол).</summary>
    public PxRect? At(int x, int y)
    {
        foreach (var r in _windows)
            if (r.Contains(x, y))
                return r;
        return null;
    }

    /// <summary>Перебирает видимые окна. Любая ошибка — пустой список: подсветка просто не появится, а выделение работает.</summary>
    public static WindowFinder Collect(Snapshot snapshot)
    {
        var result = new List<PxRect>();
        try
        {
            uint ownPid = (uint)Environment.ProcessId;
            var className = new StringBuilder(64);
            NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
            {
                if (TryGetBounds(hwnd, ownPid, className, snapshot, out var rect))
                    result.Add(rect);
                return true;
            };
            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось получить список окон", ex);
            result.Clear();
        }
        return new WindowFinder(result);
    }

    private static bool TryGetBounds(IntPtr hwnd, uint ownPid, StringBuilder className, Snapshot snapshot, out PxRect rect)
    {
        rect = default;
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            return false;

        // Свои окна (уведомления, отсчёт задержки) не подсвечиваем.
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == ownPid)
            return false;

        // Прозрачные для мыши окна-накладки (игровые панели, индикаторы записи) — их «не видно» под курсором.
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((ex & NativeMethods.WS_EX_TRANSPARENT) != 0)
            return false;

        // Окна других виртуальных столов и спящие UWP-приложения: Win32 считает их видимыми, на экране их нет.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
            return false;

        className.Clear();
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        if (IgnoredClasses.Contains(className.ToString()))
            return false;

        if (Bounds(hwnd) is not { } r)
            return false;

        // Развёрнутое окно чуть выходит за край своего монитора — обрезаем по нему.
        if (NativeMethods.IsZoomed(hwnd))
            r = r.Intersect(Monitors.FromRect(snapshot.Monitors, r).Bounds);

        r = r.Intersect(snapshot.VirtualBounds);
        if (r.Width < MinSize || r.Height < MinSize)
            return false;

        rect = r;
        return true;
    }

    /// <summary>
    /// Видимые границы окна в физических пикселях: без невидимой рамки для изменения размера (DWM),
    /// а если DWM не ответил — обычный прямоугольник окна.
    /// </summary>
    public static PxRect? Bounds(IntPtr hwnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeMethods.RECT rc, Marshal.SizeOf<NativeMethods.RECT>()) != 0
            && !NativeMethods.GetWindowRect(hwnd, out rc))
            return null;

        var rect = Monitors.ToPx(rc);
        return rect.IsEmpty ? null : rect;
    }
}
