using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WinAppLock.Core.IPC;

// ═══════════════════════════════════════════════════════════════
// WinAppLock Gatekeeper — IFEO Debugger Stub
// ═══════════════════════════════════════════════════════════════
//
// Bu exe, Windows IFEO (Image File Execution Options) mekanizması tarafından
// kilitli uygulamaların yerine çalıştırılır.
//
// IFEO Akışı:
//   Kullanıcı chrome.exe'ye tıklar
//   → Windows, IFEO kaydını okur
//   → Bu exe'yi başlatır: Gatekeeper.exe "C:\...\chrome.exe" [orijinal argümanlar]
//   → Service'e sorar: "Bu uygulamayı başlatabilir miyim?"
//   → İzin gelirse orijinal uygulamayı başlatır
//   → İzin gelmezse sessizce çıkar
//
// Fail-Closed politikası: Service'e ulaşamazsa uygulama başlatılMAZ.
// ═══════════════════════════════════════════════════════════════

// Konsol penceresini gizle (pencere yanıp sönmesin)
HideConsoleWindow();

// ─── Argüman Ayrıştırma ───
// IFEO, orijinal exe yolunu ilk argüman olarak verir:
// Gatekeeper.exe "C:\Program Files\Google\Chrome\Application\chrome.exe" --incognito
if (args.Length == 0)
{
    // Doğrudan çalıştırıldı (IFEO olmadan), çık
    Environment.Exit(1);
    return;
}

var originalExePath = args[0];
var originalArguments = args.Length > 1
    ? string.Join(" ", args.Skip(1))
    : null;

// Kendi kendini kilitleme koruması
var originalExeName = Path.GetFileName(originalExePath);
if (originalExeName.Equals("WinAppLock.Gatekeeper.exe", StringComparison.OrdinalIgnoreCase) ||
    originalExeName.Equals("WinAppLock.Service.exe", StringComparison.OrdinalIgnoreCase) ||
    originalExeName.Equals("WinAppLock.UI.exe", StringComparison.OrdinalIgnoreCase))
{
    // WinAppLock bileşenlerini doğrudan başlat, Service'e sorma
    LaunchOriginalApp(originalExePath, originalArguments);
    Environment.Exit(0);
    return;
}

// ─── Service'e Sor ───
var request = new GatekeeperRequest
{
    OriginalExePath = originalExePath,
    Arguments = originalArguments,
    GatekeeperPid = Environment.ProcessId,
    Timestamp = DateTime.UtcNow
};

var response = AskService(request);

if (response == null)
{
    // Fail-Safe: Service kapalı. Orijinal uygulamayı bloklamak yerine WinAppLock'u uyandır.
    if (WakeUpWinAppLockSystem())
    {
        // 5 defa 1'er saniye bekleyip tekrar şansımızı deniyoruz.
        for (int i = 0; i < 5; i++)
        {
            System.Threading.Thread.Sleep(1000);
            response = AskService(request);
            if (response != null)
                break;
        }
    }

    // Sisteme hiçbir şekilde ulaşılamazsa kapat
    if (response == null)
    {
        Environment.Exit(2);
        return;
    }
}

if (response.Verdict == GatekeeperVerdict.Allow)
{
    LaunchOriginalApp(originalExePath, originalArguments);
    Environment.Exit(0);
}
else
{
    // Deny — uygulama başlatılmıyor
    Environment.Exit(3);
}

// ═══════════════════════════════════════════════════════════════
// Yardımcı Fonksiyonlar
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Named Pipe üzerinden Service'e başlatma isteği gönderir ve yanıt bekler.
/// Duplex pipe kullanır — istek ve yanıt aynı bağlantıda taşınır.
/// </summary>
/// <param name="request">Service'e iletilecek başlatma isteği.</param>
/// <returns>Service'ten gelen yanıt. Bağlantı kurulamazsa null (Fail-Closed).</returns>
static GatekeeperResponse? AskService(GatekeeperRequest request)
{
    try
    {
        using var pipe = new NamedPipeClientStream(
            ".", PipeConstants.GATEKEEPER_PIPE,
            PipeDirection.InOut);

        pipe.Connect(PipeConstants.GATEKEEPER_CONNECT_TIMEOUT_MS);
        pipe.ReadMode = PipeTransmissionMode.Byte;

        // İsteği yaz (uzunluk ön ekli JSON)
        var requestJson = JsonSerializer.Serialize(request);
        var requestBytes = Encoding.UTF8.GetBytes(requestJson);

        pipe.Write(BitConverter.GetBytes(requestBytes.Length));
        pipe.Write(requestBytes);
        pipe.Flush();

        // Yanıtı oku (Service şifre doğrulama süresince bekleyebilir)
        var lengthBuffer = new byte[4];
        var bytesRead = pipe.Read(lengthBuffer, 0, 4);
        if (bytesRead < 4) return null;

        var responseLength = BitConverter.ToInt32(lengthBuffer, 0);
        if (responseLength <= 0 || responseLength > PipeConstants.BUFFER_SIZE) return null;

        var responseBuffer = new byte[responseLength];
        bytesRead = pipe.Read(responseBuffer, 0, responseLength);
        if (bytesRead < responseLength) return null;

        var responseJson = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
        return JsonSerializer.Deserialize<GatekeeperResponse>(responseJson);
    }
    catch (TimeoutException)
    {
        // Fail-Closed: Service çalışmıyor veya yanıt vermiyor
        return null;
    }
    catch (Exception)
    {
        // Fail-Closed: Beklenmeyen hata
        return null;
    }
}

static bool WakeUpWinAppLockSystem()
{
    try
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var uiPath = Path.Combine(basePath, "WinAppLock.UI.exe");

        if (File.Exists(uiPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uiPath,
                Arguments = "--hidden",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        return false;
    }
    catch
    {
        return false;
    }
}

/// <summary>
/// Orijinal uygulamayı kullanıcının orijinal argümanlarıyla başlatır.
/// IFEO sonsuz döngüsünü engellemek için Service, bu çağrıdan ÖNCE
/// IFEO kaydını geçici olarak kaldırmış olmalıdır.
/// </summary>
/// <param name="exePath">Orijinal exe'nin tam yolu.</param>
/// <param name="arguments">Orijinal komut satırı argümanları (opsiyonel).</param>
static void LaunchOriginalApp(string exePath, string? arguments)
{
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true // Kullanıcının oturumunda, doğru izinlerle başlat
        };

        Process.Start(startInfo);
    }
    catch (Exception)
    {
        // Başlatma hatası — sessizce çık
        Environment.Exit(4);
    }
}

/// <summary>
/// Konsol penceresini gizler. Gatekeeper arka planda çalışır,
/// kullanıcı pencere yanıp sönmesini görmemelidir.
/// </summary>
static void HideConsoleWindow()
{
    try
    {
        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, 0); // SW_HIDE = 0
        }
    }
    catch
    {
        // Gizleme başarısız olsa bile devam et
    }
}

// ─── P/Invoke ───

[System.Runtime.InteropServices.DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
