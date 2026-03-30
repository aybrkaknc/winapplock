using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinAppLock.UI.Services;

/// <summary>
/// Global klavye kısayolunu yönetir.
/// Ctrl+Alt+L ile "Tümünü Kilitle" komutunu tetikler.
/// 
/// RegisterHotKey Win32 API'sini kullanır.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private const int HOTKEY_ID = 9001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_L = 0x4C;

    private HwndSource? _hwndSource;
    private IntPtr _windowHandle;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Kısayol tuşu basıldığında tetiklenir.</summary>
    public event Action? HotkeyPressed;

    /// <summary>
    /// Global kısayolu pencereye bağlar ve kaydeder.
    /// </summary>
    /// <param name="window">Ana pencere (mesaj işleme için gerekli)</param>
    public void Register(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        if (!RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_L))
        {
            // Kısayol başka uygulama tarafından kullanılıyor olabilir
            System.Diagnostics.Debug.WriteLine("Global hotkey kaydedilemedi (Ctrl+Alt+L zaten kayıtlı olabilir)");
        }
    }

    /// <summary>
    /// Windows mesaj döngüsünden hotkey mesajlarını yakalar.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
        }
        _hwndSource?.RemoveHook(WndProc);
        GC.SuppressFinalize(this);
    }
}
