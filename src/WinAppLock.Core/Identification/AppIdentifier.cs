using System.Diagnostics;
using System.Security.Cryptography;
using WinAppLock.Core.Models;

namespace WinAppLock.Core.Identification;

/// <summary>
/// Çalıştırılabilir dosyaların benzersiz kimliğini oluşturur.
/// 3 katmanlı tanımlama kullanır:
///   1. Dosya adı (hızlı ön filtreleme)
///   2. SHA-256 hash (yeniden adlandırmaya karşı koruma)
///   3. PE Header bilgisi (güncelleme sonrası otomatik eşleştirme)
/// </summary>
public static class AppIdentifier
{
    /// <summary>
    /// Verilen exe dosyasının tam kimlik bilgisini oluşturur.
    /// </summary>
    /// <param name="executablePath">Exe dosyasının tam yolu</param>
    /// <returns>AppIdentity nesnesi (tüm tanımlama bilgileri dolu)</returns>
    /// <exception cref="FileNotFoundException">Dosya bulunamadığında</exception>
    public static AppIdentity CreateIdentity(string executablePath)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Çalıştırılabilir dosya bulunamadı.", executablePath);

        var fileInfo = new FileInfo(executablePath);
        var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);

        return new AppIdentity
        {
            ExecutableName = fileInfo.Name,
            ExecutablePath = executablePath,
            DirectoryPath = fileInfo.DirectoryName ?? string.Empty,
            Sha256Hash = ComputeFileHash(executablePath),
            ProductName = versionInfo.ProductName,
            CompanyName = versionInfo.CompanyName,
            FileVersion = versionInfo.FileVersion,
            FileSize = fileInfo.Length
        };
    }

    /// <summary>
    /// Çalışan bir process'ten kimlik bilgisi oluşturur.
    /// </summary>
    /// <param name="process">Çalışan process nesnesi</param>
    /// <returns>AppIdentity nesnesi veya null (erişim hatası durumunda)</returns>
    public static AppIdentity? CreateFromProcess(Process process)
    {
        try
        {
            var mainModule = process.MainModule;
            if (mainModule?.FileName == null)
                return null;

            return CreateIdentity(mainModule.FileName);
        }
        catch (Exception)
        {
            // İzin hatası veya process kapanmış olabilir
            return null;
        }
    }

    /// <summary>
    /// Bir process'in kilitli uygulama ile eşleşip eşleşmediğini kontrol eder.
    /// Tanımlama katmanları sırasıyla denenir:
    ///   1. Hash eşleşmesi (en kesin)
    ///   2. PE Header eşleşmesi (güncelleme sonrası tanımlama)
    ///   3. Dosya adı eşleşmesi (son çare, fallback)
    /// </summary>
    /// <param name="processIdentity">Çalışan process'in kimliği</param>
    /// <param name="lockedApp">Kilitli uygulama kaydı</param>
    /// <returns>Eşleşme varsa true</returns>
    public static bool IsMatch(AppIdentity processIdentity, LockedApp lockedApp)
    {
        var storedIdentity = lockedApp.Identity;

        // Katman 0: KLASÖR TABANLI BATTANİYE KİLİDİ (En geniş kapsam)
        // Eğer uygulama kök klasörleri eşleşiyorsa (ve sistem klasöründe değilse), tüm çocuk/kardeş süreçleri otomatik yakalar.
        if (lockedApp.LockChildProcesses && 
            !string.IsNullOrEmpty(storedIdentity.DirectoryPath) &&
            !string.IsNullOrEmpty(processIdentity.DirectoryPath) &&
            !IsSystemFolder(storedIdentity.DirectoryPath))
        {
            // İki klasörün sonuna mutlaka '\' koyarak C:\Geek ile C:\GeekUpdater klasörlerinin birbirine karışmasını engelliyoruz.
            var storedDir = storedIdentity.DirectoryPath.TrimEnd('\\') + "\\";
            var processDir = processIdentity.DirectoryPath.TrimEnd('\\') + "\\";
            
            // Yeni açılan process'in yolu, kilitli uygulamanın klasörünün içinde mi?
            if (processDir.StartsWith(storedDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Katman 1: Hash eşleşmesi (en güvenilir)
        if (!string.IsNullOrEmpty(processIdentity.Sha256Hash) &&
            !string.IsNullOrEmpty(storedIdentity.Sha256Hash) &&
            processIdentity.Sha256Hash == storedIdentity.Sha256Hash)
        {
            return true;
        }

        // Katman 2: PE Header eşleşmesi (güncelleme sonrası hash değiştiğinde)
        if (!string.IsNullOrEmpty(processIdentity.ProductName) &&
            !string.IsNullOrEmpty(storedIdentity.ProductName) &&
            string.Equals(processIdentity.ProductName, storedIdentity.ProductName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(processIdentity.CompanyName, storedIdentity.CompanyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Katman 3: Dosya adı eşleşmesi (fallback)
        if (string.Equals(processIdentity.ExecutableName, storedIdentity.ExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Dosyanın SHA-256 hash'ini hesaplar.
    /// </summary>
    /// <param name="filePath">Dosya yolu</param>
    /// <returns>Hex string olarak SHA-256 hash</returns>
    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Kritik sistem klasörlerini (Kazara sistemi kitlememek için) filtreler
    /// </summary>
    public static bool IsSystemFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;

        var normalized = Path.GetFullPath(path).TrimEnd('\\');

        // Kök dizin (C:\, D:\ vs.)
        if (normalized.Equals(Path.GetPathRoot(normalized)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return true;

        // Windows Klasörleri
        var sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
        if (normalized.StartsWith(sysRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        // Program Files Kökü (Sadece C:\Program Files\) - Uygulamalar bu klasörün İÇİNDEKİ klasörlerde olduğu için kök kilitlenemez
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\');
        if (normalized.Equals(pf, StringComparison.OrdinalIgnoreCase))
            return true;

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\');
        if (normalized.Equals(pf86, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
