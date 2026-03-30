using System.Windows;
using WinAppLock.Core.Data;

namespace WinAppLock.UI.Services;

/// <summary>
/// Uygulamanın anlık dil değişimini (Türkçe / İngilizce) 
/// ResourceDictionary yönergeleri aracılığıyla yapan yardımcı sınıf.
/// </summary>
public static class LocalizationManager
{
    /// <summary>
    /// Belirtilen dil koduna göre ("tr" veya "en") geçerli sözlüğü yükler.
    /// Uygulamayı yeniden başlatmadan arayüz metinlerini günceller.
    /// </summary>
    /// <param name="langCode">tr veya en</param>
    public static void ApplyLanguage(string langCode)
    {
        if (string.IsNullOrWhiteSpace(langCode))
            langCode = "tr"; // Varsayılan

        var dictUrl = $"pack://application:,,,/WinAppLock.UI;component/Themes/Strings.{langCode}.xaml";
        var dict = new ResourceDictionary { Source = new Uri(dictUrl) };

        var appDicts = Application.Current.Resources.MergedDictionaries;
        
        // Önceki dil sözlüğünü bul ve kaldır
        var oldDict = appDicts.FirstOrDefault(d => 
            d.Source != null && 
            d.Source.OriginalString.Contains("/Themes/Strings."));
            
        if (oldDict != null) 
        {
            appDicts.Remove(oldDict);
        }

        // Yeni dil sözlüğünü ekle
        appDicts.Add(dict);
    }

    /// <summary>
    /// Veritabanındaki ayarı okur ve mevcut temanın üzerine uygular.
    /// </summary>
    public static void LoadLanguageFromSettings()
    {
        try 
        {
            var db = new AppDatabase();
            var settings = db.GetSettings();
            ApplyLanguage(settings.Language);
        }
        catch 
        {
            ApplyLanguage("tr"); // Fallback
        }
    }
}
