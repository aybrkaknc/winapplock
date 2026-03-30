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

    /// <summary>Mevcut ayarları UI kontrollerine yükler.</summary>
    private void LoadSettings()
    {
        TxtMaxAttempts.Text = _settings.MaxAttempts.ToString();
        TxtCooldown.Text = _settings.CooldownSeconds.ToString();
        TxtUiTimeout.Text = _settings.UiTimeoutSeconds.ToString();
        ToggleSoundEnabled.IsChecked = _settings.SoundEnabled;
        ToggleStartWithWindows.IsChecked = _settings.StartWithWindows;
        ComboLanguage.SelectedIndex = _settings.Language == "en" ? 1 : 0;
    }

    /// <summary>Ayarları veritabanına kaydeder.</summary>
    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtMaxAttempts.Text, out var maxAttempts) && maxAttempts > 0)
            _settings.MaxAttempts = maxAttempts;

        if (int.TryParse(TxtCooldown.Text, out var cooldown) && cooldown > 0)
            _settings.CooldownSeconds = cooldown;

        if (int.TryParse(TxtUiTimeout.Text, out var timeout) && timeout > 0)
            _settings.UiTimeoutSeconds = timeout;

        _settings.SoundEnabled = ToggleSoundEnabled.IsChecked == true;
        _settings.StartWithWindows = ToggleStartWithWindows.IsChecked == true;
        _settings.Language = ComboLanguage.SelectedIndex == 1 ? "en" : "tr";

        _database.SaveSettings(_settings);

        // Kaydedildi bildirimi
        BtnSaveSettings.Content = "✓  Kaydedildi!";

        // 2 saniye sonra eski metne dön
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) =>
        {
            BtnSaveSettings.Content = "💾  Ayarları Kaydet";
            timer.Stop();
        };
        timer.Start();
    }

    /// <summary>Şifre değiştirme dialogu (mevcut şifreyi doğrula → yeni şifre).</summary>
    private void BtnChangePassword_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Şifre değiştirme dialogu implementasyonu
        MessageBox.Show("Şifre değiştirme özelliği yakında eklenecek.",
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
