using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinAppLock.Core.Data;
using WinAppLock.Core.Models;
using WinAppLock.Core.Security;

namespace WinAppLock.UI.Views;

/// <summary>
/// İlk kurulum sihirbazı code-behind.
/// 5 adımda kullanıcıyı yönlendirir: dil → yöntem → şifre → recovery key → güvenlik sorusu.
/// Tamamlandığında ayarları veritabanına kaydeder ve Dashboard'u açar.
/// </summary>
public partial class SetupWizard : Window
{
    private readonly AppDatabase _database;
    private int _currentStep = 1;
    private const int TOTAL_STEPS = 5;

    // Kullanıcı seçimleri
    private AuthMethod _selectedAuthMethod = AuthMethod.Pin;
    private int _selectedPinLength = 6;
    private string _generatedRecoveryKey = string.Empty;

    public SetupWizard()
    {
        InitializeComponent();
        
        // Dil butonlarının olaylarını bağla
        RadioLangTR.Checked += (s, e) => LocalizationManager.ApplyLanguage("tr");
        RadioLangEN.Checked += (s, e) => LocalizationManager.ApplyLanguage("en");

        _database = new AppDatabase();
        UpdateStepUI();
    }

    /// <summary>
    /// ResourceDictionary'den lokalize metin çeker.
    /// </summary>
    private static string L(string key, string fallback = "")
    {
        return Application.Current.TryFindResource(key)?.ToString() ?? fallback;
    }

    /// <summary>Sihirbaz başlığını sürükleyerek pencereyi taşıma.</summary>
    private void WizardHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    // ═══════════════════════════════════════
    // Adım 2: Kimlik Doğrulama Seçimi
    // ═══════════════════════════════════════

    /// <summary>PIN yöntemi seçildi.</summary>
    private void CardAuthPin_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedAuthMethod = AuthMethod.Pin;
        HighlightSelectedCard(CardAuthPin, CardAuthPassword);
        PinLengthSelector.Visibility = Visibility.Visible;
    }

    /// <summary>Şifre yöntemi seçildi.</summary>
    private void CardAuthPassword_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedAuthMethod = AuthMethod.Password;
        HighlightSelectedCard(CardAuthPassword, CardAuthPin);
        PinLengthSelector.Visibility = Visibility.Collapsed;
    }

    /// <summary>Seçili kartı vurgular, diğerini normal yapar.</summary>
    private void HighlightSelectedCard(System.Windows.Controls.Border selected, System.Windows.Controls.Border unselected)
    {
        selected.BorderBrush = (Brush)FindResource("BrushAccentPrimary");
        selected.BorderThickness = new Thickness(2);
        unselected.BorderBrush = (Brush)FindResource("BrushBorderDefault");
        unselected.BorderThickness = new Thickness(1);
    }

    // ═══════════════════════════════════════
    // Adım 4: Recovery Key
    // ═══════════════════════════════════════

    /// <summary>Recovery key'i panoya kopyalar.</summary>
    private void BtnCopyRecoveryKey_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_generatedRecoveryKey);
        BtnCopyRecoveryKey.Content = L("Str_Copied", "✓  Kopyalandı!");
    }

    // ═══════════════════════════════════════
    // Navigasyon
    // ═══════════════════════════════════════

    /// <summary>Bir sonraki adıma geç.</summary>
    private void BtnWizardNext_Click(object sender, RoutedEventArgs e)
    {
        // Adıma özgü doğrulama
        if (!ValidateCurrentStep()) return;

        // Adıma özgü işlemler
        OnStepLeaving(_currentStep);

        if (_currentStep < TOTAL_STEPS)
        {
            _currentStep++;
            OnStepEntering(_currentStep);
            UpdateStepUI();
        }
        else
        {
            // Son adım — kurulumu tamamla
            CompleteSetup();
        }
    }

    /// <summary>Bir önceki adıma dön.</summary>
    private void BtnWizardBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateStepUI();
        }
    }

    /// <summary>Opsiyonel adımı atla (güvenlik sorusu).</summary>
    private void BtnWizardSkip_Click(object sender, RoutedEventArgs e)
    {
        // Sadece güvenlik sorusu adımında atlanabilir
        if (_currentStep == 5)
        {
            CompleteSetup();
        }
    }

    /// <summary>Mevcut adımın doğrulamasını yapar.</summary>
    private bool ValidateCurrentStep()
    {
        switch (_currentStep)
        {
            case 3: // Şifre/PIN belirleme
                var pw1 = SetupPasswordBox1.Password;
                var pw2 = SetupPasswordBox2.Password;

                if (string.IsNullOrEmpty(pw1))
                {
                    SetupPasswordError.Text = L("Str_PasswordEmpty", "Şifre boş olamaz.");
                    return false;
                }

                if (pw1 != pw2)
                {
                    SetupPasswordError.Text = L("Str_PasswordMismatch", "Şifreler eşleşmiyor.");
                    return false;
                }

                // Format doğrulaması
                IAuthenticator authenticator = _selectedAuthMethod == AuthMethod.Pin
                    ? new PinAuthenticator(_selectedPinLength)
                    : new PasswordAuthenticator();

                if (!authenticator.ValidateFormat(pw1, out var errorMessage))
                {
                    SetupPasswordError.Text = errorMessage;
                    return false;
                }

                SetupPasswordError.Text = string.Empty;
                return true;

            default:
                return true;
        }
    }

    /// <summary>Adımdan çıkarken yapılacak işlemler.</summary>
    private void OnStepLeaving(int step)
    {
        if (step == 2)
        {
            // PIN uzunluğunu oku
            if (RadioPin4.IsChecked == true) _selectedPinLength = 4;
            else if (RadioPin5.IsChecked == true) _selectedPinLength = 5;
            else if (RadioPin6.IsChecked == true) _selectedPinLength = 6;
            else if (RadioPin7.IsChecked == true) _selectedPinLength = 7;
            else if (RadioPin8.IsChecked == true) _selectedPinLength = 8;
        }
    }

    /// <summary>Yeni adıma girerken yapılacak işlemler.</summary>
    private void OnStepEntering(int step)
    {
        switch (step)
        {
            case 3:
                // Başlığı yönteme göre güncelle
                if (_selectedAuthMethod == AuthMethod.Pin)
                {
                    Step3Title.Text = string.Format(L("Str_MasterPinSet", "Master PIN Belirle ({0} Hane)"), _selectedPinLength);
                    Step3Description.Text = L("Str_MasterPinSetDesc", "Bu PIN'i her kilitli uygulamayı açmak için kullanacaksın.");
                    SetupPasswordBox1.MaxLength = _selectedPinLength;
                    SetupPasswordBox2.MaxLength = _selectedPinLength;
                }
                else
                {
                    Step3Title.Text = L("Str_MasterPasswordSet", "Master Şifre Belirle");
                    Step3Description.Text = L("Str_MasterPasswordSetDesc", "Bu şifreyi her kilitli uygulamayı açmak için kullanacaksın.");
                    SetupPasswordBox1.MaxLength = 64;
                    SetupPasswordBox2.MaxLength = 64;
                }
                SetupPasswordBox1.Focus();
                break;

            case 4:
                // Recovery key üret
                _generatedRecoveryKey = RecoveryManager.GenerateRecoveryKey();
                RecoveryKeyDisplay.Text = _generatedRecoveryKey;
                BtnCopyRecoveryKey.Content = L("Str_BtnCopy", "📋  Panoya Kopyala");
                break;
        }
    }

    /// <summary>Adım göstergelerini ve buton görünürlüklerini günceller.</summary>
    private void UpdateStepUI()
    {
        // Tüm adımları gizle
        Step1Welcome.Visibility = Visibility.Collapsed;
        Step2AuthMethod.Visibility = Visibility.Collapsed;
        Step3SetPassword.Visibility = Visibility.Collapsed;
        Step4RecoveryKey.Visibility = Visibility.Collapsed;
        Step5SecurityQuestion.Visibility = Visibility.Collapsed;

        // Aktif adımı göster
        switch (_currentStep)
        {
            case 1: Step1Welcome.Visibility = Visibility.Visible; break;
            case 2: Step2AuthMethod.Visibility = Visibility.Visible; break;
            case 3: Step3SetPassword.Visibility = Visibility.Visible; break;
            case 4: Step4RecoveryKey.Visibility = Visibility.Visible; break;
            case 5: Step5SecurityQuestion.Visibility = Visibility.Visible; break;
        }

        // Adım metni (lokalize)
        var stepNames = new[]
        {
            "",
            L("Str_Step1Name", "Hoş Geldin"),
            L("Str_Step2Name", "Kilit Yöntemi"),
            L("Str_Step3Name", "Şifre Belirle"),
            L("Str_Step4Name", "Kurtarma Anahtarı"),
            L("Str_Step5Name", "Güvenlik Sorusu")
        };
        WizardStepText.Text = string.Format(
            L("Str_StepFormat", "Adım {0}/{1} — {2}"),
            _currentStep, TOTAL_STEPS, stepNames[_currentStep]);

        // İlerleme çubuğu genişliği (% hesap)
        var progressWidth = (double)_currentStep / TOTAL_STEPS * (520 - 48); // padding çıkar
        WizardProgressBar.Width = Math.Max(progressWidth, 20);

        // Geri butonu (ilk adımda gizli)
        BtnWizardBack.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;

        // Atla butonu (sadece güvenlik sorusu adımında)
        BtnWizardSkip.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;

        // İleri buton metni (lokalize)
        BtnWizardNext.Content = _currentStep == TOTAL_STEPS
            ? L("Str_BtnComplete", "Tamamla ✓")
            : L("Str_BtnNext", "Devam →");
    }

    // ═══════════════════════════════════════
    // Kurulum Tamamlama
    // ═══════════════════════════════════════

    /// <summary>
    /// Tüm ayarları veritabanına kaydeder ve Dashboard'u açar.
    /// </summary>
    private void CompleteSetup()
    {
        var settings = new AppSettings
        {
            Language = RadioLangTR.IsChecked == true ? "tr" : "en",
            AuthMethod = _selectedAuthMethod,
            PinLength = _selectedPinLength,
            MasterPasswordHash = PasswordHasher.Hash(SetupPasswordBox1.Password),
            RecoveryKeyHash = RecoveryManager.HashRecoveryKey(_generatedRecoveryKey),
            SetupCompleted = true
        };

        // Güvenlik sorusu (opsiyonel)
        if (!string.IsNullOrWhiteSpace(SecurityQuestionBox.Text) &&
            !string.IsNullOrWhiteSpace(SecurityAnswerBox.Text))
        {
            settings.SecurityQuestion = SecurityQuestionBox.Text.Trim();
            settings.SecurityAnswerHash = RecoveryManager.HashSecurityAnswer(SecurityAnswerBox.Text);
        }

        _database.SaveSettings(settings);

        // Dashboard'u aç
        var mainWindow = new MainWindow();
        mainWindow.Show();
        Close();
    }
}
