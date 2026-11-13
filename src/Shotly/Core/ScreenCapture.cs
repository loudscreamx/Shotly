using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shotly.Native;
using Shotly.Localization;

namespace Shotly.Core;

/// <summary>
/// Замороженный кадр всего виртуального рабочего стола + конфигурация мониторов на момент съёмки.
/// Оверлей рисуется поверх этого кадра, и итоговый файл вырезается из него же,
/// поэтому то, что пользователь видит при выделении, совпадает с результатом пиксель в пиксель.
/// </summary>
public sealed class Snapshot
{
    public Snapshot(BitmapSource image, PxRect virtualBounds, IReadOnlyList<MonitorInfo> monitors)
    {
        Image = image;
        VirtualBounds = virtualBounds;
        Monitors = monitors;
        LayoutKey = Core.Monitors.LayoutKey(monitors);
    }

    public BitmapSource Image { get; }
    public PxRect VirtualBounds { get; }
    public IReadOnlyList<MonitorInfo> Monitors { get; }
    public string LayoutKey { get; }

    /// <summary>Вырезает область (в физических координатах рабочего стола). Область обрезается по границам кадра.</summary>
    public BitmapSource Crop(PxRect region)
    {
        var r = region.Intersect(VirtualBounds);
        if (r.IsEmpty)
            throw new ArgumentException(L.RegionOffScreen(region), nameof(region));

        var cropped = new CroppedBitmap(Image,
            new Int32Rect(r.X - VirtualBounds.X, r.Y - VirtualBounds.Y, r.Width, r.Height));
        cropped.Freeze();
        return cropped;
    }
}

public static class ScreenCapture
{
    /// <summary>
    /// Снимает весь виртуальный рабочий стол одним BitBlt.
    /// Один вызов вместо съёмки по мониторам: все мониторы снимаются в один и тот же момент.
    /// </summary>
    public static Snapshot CaptureVirtualScreen()
    {
        var monitors = Monitors.GetAll();
        var bounds = new PxRect(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

        if (bounds.IsEmpty || monitors.Count == 0)
            throw new InvalidOperationException(L.NoMonitors);

        var image = Blt(bounds);
        return new Snapshot(image, bounds, monitors);
    }

    private static BitmapSource Blt(PxRect r)
    {
        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, hBitmap = IntPtr.Zero, oldObj = IntPtr.Zero;
        try
        {
            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                throw new Win32Exception(L.CaptureCopyFailed + " (GetDC)");

            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, r.Width, r.Height);
            if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), L.CaptureMemoryFailed);

            oldObj = NativeMethods.SelectObject(memDc, hBitmap);

            // CAPTUREBLT — чтобы в кадр попадали layered-окна (всплывающие подсказки, меню).
            if (!NativeMethods.BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw new Win32Exception(Marshal.GetLastWin32Error(), L.CaptureCopyFailed + " (BitBlt)");

            // GetDIBits требует, чтобы битмап не был выбран в DC.
            NativeMethods.SelectObject(memDc, oldObj);
            oldObj = IntPtr.Zero;

            var wb = new WriteableBitmap(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null);
            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = r.Width,
                biHeight = -r.Height, // отрицательная высота = строки сверху вниз, как в WPF
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            };

            wb.Lock();
            try
            {
                int lines = NativeMethods.GetDIBits(memDc, hBitmap, 0, (uint)r.Height, wb.BackBuffer,
                    ref header, NativeMethods.DIB_RGB_COLORS);
                if (lines != r.Height)
                    throw new Win32Exception(L.CaptureCopyFailed + " (GetDIBits)");
                wb.AddDirtyRect(new Int32Rect(0, 0, r.Width, r.Height));
            }
            finally
            {
                wb.Unlock();
            }

            wb.Freeze();
            return wb;
        }
        finally
        {
            if (oldObj != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldObj);
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Границы активного окна без невидимой рамки. Null, если активного окна нет
    /// (рабочий стол, панель задач, свёрнутое окно).
    /// </summary>
    public static PxRect? GetForegroundWindowBounds()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero
            || hwnd == NativeMethods.GetShellWindow()
            || hwnd == NativeMethods.GetDesktopWindow()
            || NativeMethods.IsIconic(hwnd)
            || !NativeMethods.IsWindowVisible(hwnd)
            || IsTaskbar(hwnd))
            return null;

        NativeMethods.RECT rc;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out rc, Marshal.SizeOf<NativeMethods.RECT>()) != 0)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out rc))
                return null;
        }

        var rect = Monitors.ToPx(rc);
        return rect.IsEmpty ? null : rect;
    }

    private static bool IsTaskbar(IntPtr hwnd)
    {
        var name = new System.Text.StringBuilder(64);
        NativeMethods.GetClassName(hwnd, name, name.Capacity);
        var cls = name.ToString();
        return cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    /// <summary>Текущая позиция курсора в физических пикселях.</summary>
    public static (int X, int Y) CursorPosition()
    {
        if (NativeMethods.GetCursorPos(out var p))
            return (p.X, p.Y);

        // Курсор недоступен (защищённый рабочий стол) — считаем, что он на основном мониторе.
        return (0, 0);
    }
}
