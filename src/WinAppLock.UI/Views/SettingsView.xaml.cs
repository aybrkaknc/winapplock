using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinAppLock.Core.Data;
using WinAppLock.Core.Models;
using WinAppLock.Core.Registry;

namespace WinAppLock.UI.Views;

/// <summary>
/// Ayarlar sayfası code-behind.
/// Kullanıcı tercihlerini yükler, düzenler ve veritabanına kaydeder.
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly AppDatabase _database;
    private AppSettings _settings = null!;

    private bool _isLoading;

    public SettingsView()
    {
        InitializeComponent();
        _database = new AppDatabase();
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
        _isLoading = true;
        _settings = _database.GetSettings();

        ToggleSoundEnabled.IsChecked = _settings.SoundEnabled;
        ToggleStartWithWindows.IsChecked = _settings.StartWithWindows;
        ComboLanguage.SelectedIndex = _settings.Language == "en" ? 1 : 0;
        
        // Dropdown'ları Başlat
        InitializeMaxAttemptsCombo();
        InitializeCooldownCombo();
        InitializeUiTimeoutCombo();
        InitializeAutoRelockCombo();

        _isLoading = false;
    }

    private void InitializeMaxAttemptsCombo()
    {
        ComboMaxAttempts.Items.Clear();
        var options = new[] { 3, 5, 10 };
        int selectedIndex = 0;

        for (int i = 0; i < options.Length; i++)
        {
            int val = options[i];
            string content = val + L("Str_Attempts", " Deneme");
            ComboMaxAttempts.Items.Add(new ComboBoxItem { Content = content, Tag = val });

            if (_settings.MaxAttempts == val) selectedIndex = i;
        }
        ComboMaxAttempts.SelectedIndex = selectedIndex;
    }

    private void InitializeCooldownCombo()
    {
        ComboCooldown.Items.Clear();
        var options = new[] { 30, 60, 120, 300, 600 };
        int selectedIndex = 0;

        for (int i = 0; i < options.Length; i++)
        {
            int sec = options[i];
            string content = sec < 60 
                ? sec + L("Str_Seconds", " sn") 
                : (sec / 60) + L("Str_Minute", " Dakika");

            ComboCooldown.Items.Add(new ComboBoxItem { Content = content, Tag = sec });

            if (_settings.CooldownSeconds == sec) selectedIndex = i;
        }
        ComboCooldown.SelectedIndex = selectedIndex;
    }

    private void InitializeUiTimeoutCombo()
    {
        ComboUiTimeout.Items.Clear();
        var options = new[] { 0, 30, 60, 300, 600 };
        int selectedIndex = 0;

        for (int i = 0; i < options.Length; i++)
        {
            int sec = options[i];
            string content = sec switch
            {
                0 => L("Str_AlwaysPrompt", "Her Zaman Sor"),
                _ when sec < 60 => sec + L("Str_Seconds", " sn"),
                _ => (sec / 60) + L("Str_Minute", " Dakika")
            };

            ComboUiTimeout.Items.Add(new ComboBoxItem { Content = content, Tag = sec });

            if (_settings.UiTimeoutSeconds == sec) selectedIndex = i;
        }
        ComboUiTimeout.SelectedIndex = selectedIndex;
    }

    private void InitializeAutoRelockCombo()
    {
        ComboAutoRelock.Items.Clear();
        var options = new[] { 0, 5, 10, 15, 30, 60 };
        int selectedIndex = 0;

        for (int i = 0; i < options.Length; i++)
        {
            int min = options[i];
            string content = min switch
            {
                0 => L("Str_RelockDisabled", "Kapalı"),
                60 => L("Str_RelockHour", "1 Saat"),
                _ => string.Format(L("Str_RelockMinutes", "{0} Dakika"), min)
            };

            ComboAutoRelock.Items.Add(new ComboBoxItem { Content = content, Tag = min });

            if (_settings.AutoRelockMinutes == min)
                selectedIndex = i;
        }
        ComboAutoRelock.SelectedIndex = selectedIndex;
    }

    /// <summary>Sayfadaki tüm seçimleri toplayıp veritabanına kaydeder.</summary>
    private void ProcessAndSaveSettings()
    {
        if (!IsLoaded || _isLoading) return;

        // Max Attempts
        if (ComboMaxAttempts.SelectedItem is ComboBoxItem selAttempts && selAttempts.Tag is int maxAttempts)
            _settings.MaxAttempts = maxAttempts;

        // Cooldown
        if (ComboCooldown.SelectedItem is ComboBoxItem selCooldown && selCooldown.Tag is int cooldown)
            _settings.CooldownSeconds = cooldown;

        // UI Timeout
        if (ComboUiTimeout.SelectedItem is ComboBoxItem selTimeout && selTimeout.Tag is int timeout)
            _settings.UiTimeoutSeconds = timeout;

        // Auto Relock
        if (ComboAutoRelock.SelectedItem is ComboBoxItem selectedRelock && selectedRelock.Tag is int relockMins)
            _settings.AutoRelockMinutes = relockMins;

        _settings.SoundEnabled = ToggleSoundEnabled.IsChecked == true;
        _settings.StartWithWindows = ToggleStartWithWindows.IsChecked == true;
        _settings.Language = ComboLanguage.SelectedIndex == 1 ? "en" : "tr";

        _database.SaveSettings(_settings);
        
        // Windows başlangıcına ekleme/kaldırma işlemi
        StartupManager.SetStartup(_settings.StartWithWindows);
        
        Debug.WriteLine("Settings auto-saved.");
    }

    /// <summary>Herhangi bir ayar değiştiğinde tetiklenir (Auto-Save).</summary>
    private void AutoSave_Changed(object sender, RoutedEventArgs e)
    {
        ProcessAndSaveSettings();
    }

    /// <summary>Şifre değiştirme overlay (doğrulama → yeni yöntem/şifre).</summary>
    private void BtnChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var cpw = new ChangePasswordWindow
        {
            Owner = Window.GetWindow(this)
        };

        
        bool? result = cpw.ShowDialog();
        
        // Eğer başarılı geldiyse UI'nin haberi olsun (veya gerekirse yeniden ayar yüklensin)
        if (result == true)
        {
            LoadSettings(); // Gerekliyse yeni ayarları tekrar al
        }
    }

    private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboLanguage == null || !IsLoaded) return;
        var lang = ComboLanguage.SelectedIndex == 1 ? "en" : "tr";
        LocalizationManager.ApplyLanguage(lang);
        ProcessAndSaveSettings();
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
