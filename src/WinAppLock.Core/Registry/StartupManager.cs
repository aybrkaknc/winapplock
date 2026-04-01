using System;
using System.IO;
using Microsoft.Win32;

namespace WinAppLock.Core.Registry;

/// <summary>
/// Uygulamanın Windows işletim sistemiyle birlikte başlatılmasını sağlar.
/// Kayıt Defterindeki HKCU\...\Run anahtarını okur ve yazar.
/// </summary>
public static class StartupManager
{
    private const string APP_NAME = "WinAppLock";
    private const string RUN_LOCATION = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Windows başlangıcında çalışıp çalışmadığını kontrol eder.
    /// </summary>
    public static bool IsRegisteredForStartup()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RUN_LOCATION);
            if (key == null) return false;

            var value = key.GetValue(APP_NAME) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Uygulamayı Windows başlangıcına ekler veya kaldırır.
    /// </summary>
    /// <param name="enable">True ise ekler, False ise siler.</param>
    /// <param name="appPath">Uygulamanın çalıştırılabilir yolu (.exe), eğer null ise otomatik alınır.</param>
    public static void SetStartup(bool enable, string? appPath = null)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RUN_LOCATION, true);
            if (key == null) return;

            if (enable)
            {
                // Mevcut exe konumunu bul
                if (string.IsNullOrEmpty(appPath))
                {
                    appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(appPath)) return;

                // Boşluklu pathlere karşı komutu tırnak içine alıyoruz
                // ve gizli (background) açılması için --hidden argümanını ekliyoruz
                string command = $"\"{appPath}\" --hidden";
                key.SetValue(APP_NAME, command);
            }
            else
            {
                // Önceden varsa sil
                if (key.GetValue(APP_NAME) != null)
                {
                    key.DeleteValue(APP_NAME);
                }
            }
        }
        catch (Exception)
        {
            // İzin vb. yoksay
        }
    }
}
