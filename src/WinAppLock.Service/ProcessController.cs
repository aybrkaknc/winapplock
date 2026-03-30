using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace WinAppLock.Service;

/// <summary>
/// Windows API (P/Invoke) kullanarak process thread'lerini askıya alma ve devam ettirme.
/// SuspendThread tüm thread'leri dondurur, ResumeThread kaldığı yerden devam ettirir.
/// 
/// Bu sınıf SYSTEM yetkisiyle çalışan Windows Service içinden çağrılmalıdır.
/// Standart kullanıcı yetkisiyle bazı process'ler askıya alınamayabilir.
/// </summary>
public static class ProcessController
{
    // ─── Win32 API Sabitleri ───
    private const uint THREAD_SUSPEND_RESUME = 0x0002;
    private const uint THREAD_QUERY_INFORMATION = 0x0040;

    // ─── Win32 API İmportları ───

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
    /// Process dondurulur ancak sonlandırılmaz — veriler korunur.
    /// </summary>
    /// <param name="processId">Askıya alınacak process'in ID'si</param>
    /// <returns>Başarıyla askıya alınan thread sayısı</returns>
    /// <exception cref="ArgumentException">Process bulunamadığında</exception>
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

            if (threadHandle == IntPtr.Zero)
            {
                Log.Debug("Thread açılamadı: TID {ThreadId}, Hata: {Error}",
                    thread.Id, Marshal.GetLastWin32Error());
                continue;
            }

            try
            {
                var result = SuspendThread(threadHandle);
                if (result == -1)
                {
                    Log.Warning("Thread askıya alınamadı: TID {ThreadId}, Hata: {Error}",
                        thread.Id, Marshal.GetLastWin32Error());
                }
                else
                {
                    suspendedCount++;
                }
            }
            finally
            {
                CloseHandle(threadHandle);
            }
        }

        Log.Information("Process askıya alındı: PID {ProcessId}, {Count}/{Total} thread donduruldu",
            processId, suspendedCount, process.Threads.Count);

        return suspendedCount;
    }

    /// <summary>
    /// Askıya alınmış process'in tüm thread'lerini devam ettirir.
    /// Uygulama kaldığı yerden çalışmaya devam eder.
    /// </summary>
    /// <param name="processId">Devam ettirilecek process'in ID'si</param>
    /// <returns>Başarıyla devam ettirilen thread sayısı</returns>
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
            var threadHandle = OpenThread(
                THREAD_SUSPEND_RESUME,
                false,
                (uint)thread.Id
            );

            if (threadHandle == IntPtr.Zero)
                continue;

            try
            {
                // ResumeThread suspend sayacını düşürür. Birden fazla kez
                // suspend edilmişse, o kadar resume gerekir.
                int result;
                do
                {
                    result = ResumeThread(threadHandle);
                } while (result > 1); // Sayaç 0'a düşene kadar devam et

                if (result >= 0)
                    resumedCount++;
            }
            finally
            {
                CloseHandle(threadHandle);
            }
        }

        Log.Information("Process devam ettirildi: PID {ProcessId}, {Count} thread resume edildi",
            processId, resumedCount);

        return resumedCount;
    }

    /// <summary>
    /// Process'in hâlâ askıda olup olmadığını kontrol eder.
    /// Heartbeat mekanizması bu metodu kullanarak dışarıdan resume edilmiş
    /// process'leri tespit eder.
    /// </summary>
    /// <param name="processId">Kontrol edilecek process ID'si</param>
    /// <returns>Process askıdaysa true, aktifse veya bulunamadıysa false</returns>
    public static bool IsProcessSuspended(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            foreach (ProcessThread thread in process.Threads)
            {
                // Herhangi bir thread WaitSleepJoin dışında bir durumda ise
                // process aktif demektir
                if (thread.ThreadState == System.Diagnostics.ThreadState.Running ||
                    thread.ThreadState == System.Diagnostics.ThreadState.Ready ||
                    thread.ThreadState == System.Diagnostics.ThreadState.Standby)
                {
                    return false;
                }
            }
            // Tüm thread'ler bekleme durumunda — muhtemelen askıda
            return true;
        }
        catch
        {
            return false; // Process bulunamadı veya erişim hatası
        }
    }

    /// <summary>
    /// Process'in hâlâ çalışıp çalışmadığını kontrol eder.
    /// </summary>
    /// <param name="processId">Kontrol edilecek process ID'si</param>
    /// <returns>Process çalışıyorsa true</returns>
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
}
