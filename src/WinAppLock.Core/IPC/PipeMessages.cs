namespace WinAppLock.Core.IPC;

/// <summary>
/// Named Pipe üzerinden Service ve UI arasında iletilen mesaj tipleri.
/// Her mesaj tipi belirli bir IPC komutunu temsil eder.
/// </summary>
public enum PipeMessageType
{
    // ─── Service → UI Mesajları ───
    /// <summary>Kilitli uygulama algılandı, overlay gösterilmeli.</summary>
    LockTriggered,

    /// <summary>Servis durumu bilgisi (bağlantı kontrolü).</summary>
    Heartbeat,

    /// <summary>Tüm uygulamalar kilitlendi (Ctrl+Alt+L veya tray menüsü).</summary>
    AllLocked,

    // ─── UI → Service Mesajları ───
    /// <summary>Kullanıcı doğru şifre girdi, process devam ettirilmeli.</summary>
    AuthSuccess,

    /// <summary>Kullanıcı overlay'i iptal etti, process sonlandırılmalı.</summary>
    AuthCancelled,

    /// <summary>Yeni uygulama kilitli listeye eklendi.</summary>
    AppAdded,

    /// <summary>Uygulama kilitli listeden çıkarıldı.</summary>
    AppRemoved,

    /// <summary>Uygulama kilidi aktif/pasif yapıldı (toggle).</summary>
    AppToggled,

    /// <summary>Ayarlar güncellendi, servis yeniden yüklesin.</summary>
    SettingsUpdated,

    /// <summary>Tüm kilitleri aç komutu.</summary>
    UnlockAll,

    /// <summary>Tüm kilitleri kapat komutu.</summary>
    LockAll
}

/// <summary>
/// Service ve UI arasında Named Pipe üzerinden taşınan mesaj.
/// JSON olarak serialize/deserialize edilir.
/// </summary>
public class PipeMessage
{
    /// <summary>Mesaj tipi.</summary>
    public PipeMessageType Type { get; set; }

    /// <summary>İlgili process'in ID'si (gerektiğinde).</summary>
    public int ProcessId { get; set; }

    /// <summary>İlgili uygulamanın adı (gerektiğinde).</summary>
    public string? AppName { get; set; }

    /// <summary>İlgili uygulamanın exe yolu (gerektiğinde).</summary>
    public string? AppPath { get; set; }

    /// <summary>Ek veri (JSON string olarak).</summary>
    public string? Payload { get; set; }

    /// <summary>Mesaj zaman damgası.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Named Pipe bağlantı sabitleri.
/// </summary>
public static class PipeConstants
{
    /// <summary>Service → UI yönündeki pipe adı.</summary>
    public const string SERVICE_TO_UI_PIPE = "WinAppLock_ServiceToUI";

    /// <summary>UI → Service yönündeki pipe adı.</summary>
    public const string UI_TO_SERVICE_PIPE = "WinAppLock_UIToService";

    /// <summary>Bağlantı zaman aşımı (milisaniye).</summary>
    public const int CONNECTION_TIMEOUT_MS = 5000;

    /// <summary>Mesaj okuma/yazma buffer boyutu (byte).</summary>
    public const int BUFFER_SIZE = 4096;
}
