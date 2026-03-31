using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace WinAppLock.Service;

/// <summary>
/// Windows API (P/Invoke) ile temel süreç kontrolü.
/// IFEO mimarisinde SuspendThread/ResumeThread artık ana akışta kullanılmaz.
/// Bu sınıf yalnızca eski oturumlara uyumluluk ve acil durum işlemleri için korunmaktadır.
/// </summary>
public static class ProcessController
{
    /// <summary>
    /// Process'in hâlâ çalışıp çalışmadığını kontrol eder.
    /// </summary>
    /// <param name="processId">Kontrol edilecek process ID'si.</param>
    /// <returns>Process çalışıyorsa true.</returns>
    public static bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Aşağıdaki Suspend/Resume metodları IFEO mimarisinde
    // ana akışta kullanılmaz. Yalnızca aşağıdaki durumlarda kalıyor:
    //   1. LockStateManager.LockAll() — manual lock komutu
    //   2. LockStateManager.ExecuteDeadManSwitch() — UI çöküşü güvenliği
    //   3. WindowObserver SuspendAppProcesses() — arka plan tespiti
    // ═══════════════════════════════════════════════════════════════

    private const uint THREAD_SUSPEND_RESUME = 0x0002;
    private const uint THREAD_QUERY_INFORMATION = 0x0040;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Belirtilen process'in tüm thread'lerini askıya alır.
    /// IFEO sonrası yalnızca acil durum senaryolarında kullanılır.
    /// </summary>
    /// <param name="processId">Askıya alınacak process'in ID'si.</param>
    /// <returns>Askıya alınan thread sayısı.</returns>
    public static int SuspendProcess(int processId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            Log.Warning("Process bulunamadı: PID {ProcessId}", processId);
            return 0;
        }

        var suspendedCount = 0;

        foreach (ProcessThread thread in process.Threads)
        {
            var threadHandle = OpenThread(
                THREAD_SUSPEND_RESUME | THREAD_QUERY_INFORMATION,
                false,
                (uint)thread.Id
            );

            if (threadHandle == IntPtr.Zero) continue;

            try
            {
                var result = SuspendThread(threadHandle);
                if (result >= 0) suspendedCount++;
            }
            finally
            {
                CloseHandle(threadHandle);
            }
        }

        Log.Information("Process askıya alındı: PID {ProcessId}, {Count}/{Total} thread",
            processId, suspendedCount, process.Threads.Count);

        return suspendedCount;
    }

    /// <summary>
    /// Askıya alınmış process'in tüm thread'lerini devam ettirir.
    /// </summary>
    /// <param name="processId">Devam ettirilecek process'in ID'si.</param>
    /// <returns>Devam ettirilen thread sayısı.</returns>
    public static int ResumeProcess(int processId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            Log.Warning("Process bulunamadı (resume): PID {ProcessId}", processId);
            return 0;
        }

        var resumedCount = 0;

        foreach (ProcessThread thread in process.Threads)
        {
            var threadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
            if (threadHandle == IntPtr.Zero) continue;

            try
            {
                int result;
                do
                {
                    result = ResumeThread(threadHandle);
                } while (result > 1);

                if (result >= 0) resumedCount++;
            }
            finally
            {
                CloseHandle(threadHandle);
            }
        }

        Log.Information("Process devam ettirildi: PID {ProcessId}, {Count} thread",
            processId, resumedCount);

        return resumedCount;
    }

    /// <summary>
    /// Process'in askıda olup olmadığını kontrol eder.
    /// </summary>
    /// <param name="processId">Kontrol edilecek process ID'si.</param>
    /// <returns>Tüm thread'ler bekleme durumundaysa true.</returns>
    public static bool IsProcessSuspended(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            foreach (ProcessThread thread in process.Threads)
            {
                if (thread.ThreadState == System.Diagnostics.ThreadState.Running ||
                    thread.ThreadState == System.Diagnostics.ThreadState.Ready ||
                    thread.ThreadState == System.Diagnostics.ThreadState.Standby)
                {
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
