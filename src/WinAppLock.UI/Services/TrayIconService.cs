using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace WinAppLock.UI.Services;

/// <summary>
/// System Tray (bildirim alanı) ikon yönetimi.
/// Pencere küçültüldüğünde tray'de ikon gösterir.
/// 
/// NotifyIcon: Windows Forms bileşeni. WPF'te doğrudan karşılığı olmadığından
/// System.Windows.Forms referansı ile kullanılır.
/// </summary>
public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;

    /// <summary>Tray ikonu çift tıklandığında tetiklenir (pencereyi göster).</summary>
    public event Action? ShowRequested;

    /// <summary>Çıkış menüsü seçildiğinde tetiklenir.</summary>
    public event Action? ExitRequested;

    /// <summary>Tümünü Kilitle menüsü seçildiğinde tetiklenir.</summary>
    public event Action? LockAllRequested;

    /// <summary>
    /// Tray ikonunu oluşturur ve gösterir.
    /// </summary>
    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = L("Str_TrayTooltip", "WinAppLock — Uygulamalar korunuyor"),
            Visible = true,

            // Varsayılan ikon (uygulama ikonu veya sistem ikonu)
            Icon = GetAppIcon()
        };

        // Çift tıklama → pencereyi göster
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke();

        // Sağ tık menüsü
        var contextMenu = new ContextMenuStrip();

        var showItem = new ToolStripMenuItem(L("Str_TrayOpen", "WinAppLock'u Aç"));
        showItem.Click += (_, _) => ShowRequested?.Invoke();
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var lockAllItem = new ToolStripMenuItem(L("Str_TrayLockAll", "🔐 Tümünü Kilitle"));
        lockAllItem.Click += (_, _) => LockAllRequested?.Invoke();
        contextMenu.Items.Add(lockAllItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem(L("Str_TrayExit", "Çıkış"));
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    /// <summary>
    /// ResourceDictionary'den lokalize metin çeker.
    /// NotifyIcon WinForms bileşeni olduğu için DynamicResource kullanılamaz.
    /// </summary>
    private static string L(string key, string fallback = "")
    {
        return System.Windows.Application.Current.TryFindResource(key)?.ToString() ?? fallback;
    }

    /// <summary>
    /// Tray balonu bildirimi gösterir.
    /// </summary>
    /// <param name="title">Bildirim başlığı</param>
    /// <param name="message">Bildirim metni</param>
    /// <param name="icon">Bildirim ikonu</param>
    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
    }

    /// <summary>
    /// Uygulama ikonunu alır. Bulamazsa varsayılan sistem ikonunu kullanır.
    /// </summary>
    private Icon GetAppIcon()
    {
        try
        {
            // Kendi exe'sinden ikon çıkar
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
            {
                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }
        }
        catch { /* Fallback */ }

        // Fallback: Sistem varsayılan ikonu
        return SystemIcons.Shield;
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
