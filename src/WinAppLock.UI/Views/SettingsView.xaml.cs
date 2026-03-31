using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinAppLock.Core.Data;
using WinAppLock.Core.Models;

namespace WinAppLock.UI.Views;

/// <summary>
/// Ayarlar sayfası code-behind.
/// Kullanıcı tercihlerini yükler, düzenler ve veritabanına kaydeder.
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly AppDatabase _database;
    private AppSettings _settings;

    public SettingsView()
    {
        InitializeComponent();
        _database = new AppDatabase();
        _settings = _database.GetSettings();
        LoadSettings();
    }

    /// <summary>
    /// ResourceDictionary'den lokalize metin çeker.
    /// </summary>
    private static string L(string key, string fallback = "")
    {
        return Application.Current.TryFindResource(key)?.ToString() ?? fallback;
    }

    /// <summary>Mevcut ayarları UI kontrollerine yükler.</summary>
    private void LoadSettings()
    {
        // Güvenlik Ayarları
        TxtMaxAttempts.Text = _settings.MaxAttempts.ToString();
        TxtCooldown.Text = _settings.CooldownSeconds.ToString();

        // Görünüm (Retro) Ayarları
        ComboThemeBase.SelectedIndex = (int)_settings.ThemeBase;
        ComboNavStyle.SelectedIndex = (int)_settings.NavigationStyle;
        ComboAnimations.SelectedIndex = (int)_settings.AnimationStyle;
        ComboTitleGradient.SelectedIndex = (int)_settings.TitleBarGradient;
        ComboIconSet.SelectedIndex = (int)_settings.IconSet;
        CheckSoundEffects.IsChecked = _settings.SoundEnabled;

        // Genel Ayarlar
        CheckStartWithWindows.IsChecked = _settings.StartWithWindows;
        ComboLanguage.SelectedIndex = _settings.Language == "en" ? 1 : 0;
    }

    /// <summary>Ayarları veritabanına kaydeder.</summary>
    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtMaxAttempts.Text, out var maxAttempts) && maxAttempts > 0)
            _settings.MaxAttempts = maxAttempts;

        if (int.TryParse(TxtCooldown.Text, out var cooldown) && cooldown > 0)
            _settings.CooldownSeconds = cooldown;

        // Görünüm (Retro) Ayarları
        _settings.ThemeBase = (ThemeBase)ComboThemeBase.SelectedIndex;
        _settings.NavigationStyle = (NavigationStyle)ComboNavStyle.SelectedIndex;
        _settings.AnimationStyle = (AnimationStyle)ComboAnimations.SelectedIndex;
        _settings.TitleBarGradient = (GradientStyle)ComboTitleGradient.SelectedIndex;
        _settings.IconSet = (IconSet)ComboIconSet.SelectedIndex;
        _settings.SoundEnabled = CheckSoundEffects.IsChecked == true;

        // Genel Ayarlar
        _settings.StartWithWindows = CheckStartWithWindows.IsChecked == true;
        _settings.Language = ComboLanguage.SelectedIndex == 1 ? "en" : "tr";

        _database.SaveSettings(_settings);

        // Kaydedildi bildirimi
        BtnSaveSettings.Content = "Uygulandı";

        // 2 saniye sonra eski metne dön
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) =>
        {
            BtnSaveSettings.Content = "Uygula";
            timer.Stop();
        };
        timer.Start();
    }

    /// <summary>Şifre değiştirme dialogu (mevcut şifreyi doğrula → yeni şifre).</summary>
    private void BtnChangePassword_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Şifre değiştirme dialogu implementasyonu
        MessageBox.Show(L("Str_ChangePasswordSoon", "Şifre değiştirme özelliği yakında eklenecek."),
            "WinAppLock", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboLanguage == null || !IsLoaded) return;
        var lang = ComboLanguage.SelectedIndex == 1 ? "en" : "tr";
        LocalizationManager.ApplyLanguage(lang);
    }

    /// <summary>GitHub bağlantısını varsayılan tarayıcıda açar.</summary>
    private void GithubLink_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/aybrkaknc/winapplock",
            UseShellExecute = true
        });
    }
}
