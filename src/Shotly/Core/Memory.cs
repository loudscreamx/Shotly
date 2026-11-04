using System;
using System.Runtime;
using Shotly.Native;

namespace Shotly.Core;

public static class Memory
{
    /// <summary>
    /// После захвата освобождаем снимок экрана (на трёх 4K-мониторах это ~100 МБ)
    /// и отдаём память системе, чтобы в трее процесс снова весил десятки мегабайт.
    /// </summary>
    public static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }
}
