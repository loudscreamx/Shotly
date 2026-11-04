using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Shotly.Native;

namespace Shotly.Core;

/// <summary>Монитор: границы в физических пикселях и его собственный DPI.</summary>
public sealed record MonitorInfo(IntPtr Handle, PxRect Bounds, PxRect WorkArea, uint Dpi, bool IsPrimary)
{
    public double Scale => Dpi / 96.0;
}

public static class Monitors
{
    /// <summary>
    /// Список мониторов на текущий момент. Не кэшируем: конфигурация может смениться
    /// в любой момент (подключили проектор, поменяли масштаб, ноутбук закрыли).
    /// </summary>
    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var result = new List<MonitorInfo>();
        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rc, IntPtr data) =>
        {
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                return true; // монитор пропал между вызовами — просто пропускаем

            uint dpi = 96;
            if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx > 0)
                dpi = dx;

            result.Add(new MonitorInfo(
                hMonitor,
                ToPx(mi.rcMonitor),
                ToPx(mi.rcWork),
                dpi,
                (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return result;
    }

    /// <summary>Отпечаток конфигурации мониторов: нужен, чтобы понять, актуальна ли сохранённая «последняя область».</summary>
    public static string LayoutKey(IEnumerable<MonitorInfo> monitors) =>
        string.Join(";", monitors
            .OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
            .Select(m => $"{m.Bounds.X},{m.Bounds.Y},{m.Bounds.Width},{m.Bounds.Height},{m.Dpi}"));

    /// <summary>Монитор, содержащий точку; если точка в «дыре» между мониторами — ближайший.</summary>
    public static MonitorInfo FromPoint(IReadOnlyList<MonitorInfo> monitors, int x, int y)
    {
        foreach (var m in monitors)
            if (m.Bounds.Contains(x, y))
                return m;

        return monitors
            .OrderBy(m => DistanceSq(m.Bounds, x, y))
            .First();
    }

    /// <summary>Монитор, на который приходится большая часть прямоугольника.</summary>
    public static MonitorInfo FromRect(IReadOnlyList<MonitorInfo> monitors, PxRect rect) =>
        monitors.OrderByDescending(m => m.Bounds.Intersect(rect).Area).First();

    private static long DistanceSq(PxRect r, int x, int y)
    {
        long dx = x < r.X ? r.X - x : x >= r.Right ? x - r.Right + 1 : 0;
        long dy = y < r.Y ? r.Y - y : y >= r.Bottom ? y - r.Bottom + 1 : 0;
        return dx * dx + dy * dy;
    }

    internal static PxRect ToPx(NativeMethods.RECT r) => PxRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom);
}
