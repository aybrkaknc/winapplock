using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinAppLock.Core.Data;
using WinAppLock.Core.Models;
using WinAppLock.Core.Security;

namespace WinAppLock.UI.Views;

/// <summary>
/// Şifre Değiştirme (Change Password) Sihirbazı.
/// 4 adımda kimlik doğrular, yeni yöntem seçtirir, yeni şifreyi ayarlar
/// ve en son güvenlik için eski kurtarma anahtarını silip yenisini üretir.
/// </summary>
public partial class ChangePasswordWindow : Window
{
    private readonly AppDatabase _database;
    private AppSettings _settings;
    
    // Durum Değişkenleri
    private int _currentStep = 1;
    private const int TOTAL_STEPS = 4;
    
    // Doğrulama (Limit) Değişkenleri
    private int _failedAttempts = 0;
    private DateTime? _lockoutEndTime;
    private string _currentPin = string.Empty;

    // Yeni Seçimler
    private AuthMethod _selectedAuthMethod;
    private int _selectedPinLength = 4; // FIXED
    private string _generatedRecoveryKey = string.Empty;
    
    // Numpad Setup Alanı İçin
    private string _setupPin1 = string.Empty;
    private string _setupPin2 = string.Empty;

    public ChangePasswordWindow()
    {
        InitializeComponent();
        
        _database = new AppDatabase();
        _settings = _database.GetSettings();

        // Başlangıç değerleri
        _selectedAuthMethod = _settings.AuthMethod;
        _selectedPinLength = 4; // Artık her yerde 4.
        
        // UI Hazırlıkları
        UpdateStepUI();
        InitializeAuthStep();
    }

    /// <summary>ResourceDictionary'den lokalize metin çeker.</summary>
    private static string L(string key, string fallback = "")
    {
        return Application.Current.TryFindResource(key)?.ToString() ?? fallback;
    }

    // ═══════════════════════════════════════

    // ADIM 1: Doğrulama (Auth) İşlemleri
    // ═══════════════════════════════════════

    private void InitializeAuthStep()
    {
        if (_settings.AuthMethod == AuthMethod.Pin)
        {
            AuthPinArea.Visibility = Visibility.Visible;
            AuthPasswordArea.Visibility = Visibility.Collapsed;
            InitPinDots();
        }
        else
        {
            AuthPasswordArea.Visibility = Visibility.Visible;
            AuthPinArea.Visibility = Visibility.Collapsed;
            InputCurrentPassword.Focus();
        }
    }

    private void InitPinDots()
    {
        PinDotsPanel.Children.Clear();
        for (var i = 0; i < _settings.PinLength; i++)
        {
            var dot = new Ellipse
            {
                Width = 14, Height = 14,
                Margin = new Thickness(6, 0, 6, 0),
                Fill = (Brush)FindResource("BrushBackgroundLight"),
                Stroke = (Brush)FindResource("BrushBorderDefault"),
                StrokeThickness = 1
            };
            PinDotsPanel.Children.Add(dot);
        }
    }

    private void UpdatePinDots()
    {
        for (var i = 0; i < PinDotsPanel.Children.Count; i++)
        {
            if (PinDotsPanel.Children[i] is Ellipse dot)
            {
                dot.Fill = i < _currentPin.Length
                    ? (Brush)FindResource("BrushAccentPrimary")
                    : (Brush)FindResource("BrushBackgroundLight");
            }
        }
    }

    private void Numpad_Click(object sender, RoutedEventArgs e)
    {
        if (IsCoolingDown()) return;

        if (sender is System.Windows.Controls.Button btn && _currentPin.Length < _settings.PinLength)
        {
            _currentPin += btn.Content.ToString();
            UpdatePinDots();

            // PIN tam girildiyse otomatik doğrula
            if (_currentPin.Length == _settings.PinLength)
            {
                VerifyCurrentIdentity(_currentPin);
            }
        }
    }

    private void NumpadBackspace_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPin.Length > 0)
        {
            _currentPin = _currentPin[..^1];
            UpdatePinDots();
        }
    }

    private void VerifyCurrentIdentity(string input)
    {
        if (IsCoolingDown()) return;

        // Hash Doğrulaması
        if (PasswordHasher.Verify(input, _settings.MasterPasswordHash))
        {
            // Başarılı! Sonraki adıma geç
            AuthErrorText.Text = string.Empty;
            _failedAttempts = 0;
            MoveToNextStep();
        }
        else
        {
            // Hatalı Giriş
            _failedAttempts++;
            _currentPin = string.Empty;
            UpdatePinDots();
            InputCurrentPassword.Clear();

            // Normal limitin 2 katını uyguluyoruz
            int maxAttemptsAllowed = _settings.MaxAttempts * 2;
            int remaining = maxAttemptsAllowed - _failedAttempts;

            if (remaining <= 0)
            {
                _lockoutEndTime = DateTime.Now.AddSeconds(_settings.CooldownSeconds);
                AuthErrorText.Text = string.Format(L("Str_CP_LockedOut", "Çok fazla hatalı giriş! Saniye bekle..."));
            }
            else
            {
                AuthErrorText.Text = string.Format(L("Str_CP_Incorrect", "Yanlış giriş! Kalan deneme: {0}"), remaining);
            }
        }
    }

    private bool IsCoolingDown()
    {
        if (_lockoutEndTime.HasValue)
        {
            if (DateTime.Now < _lockoutEndTime.Value)
            {
                var remaining = _lockoutEndTime.Value - DateTime.Now;
                AuthErrorText.Text = $"Kilitlisiniz. Kalan süre: {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                return true;
            }
            _lockoutEndTime = null;
            AuthErrorText.Text = string.Empty;
            _failedAttempts = 0; // Süre dolunca hataları sıfırla
        }
        return false;
    }

    // ═══════════════════════════════════════
    // ADIM 2: Yöntem Seçimi
    // ═══════════════════════════════════════

    private void CardAuthPin_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedAuthMethod = AuthMethod.Pin;
        HighlightSelectedCard(CardAuthPin, CardAuthPassword);
    }

    private void CardAuthPassword_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedAuthMethod = AuthMethod.Password;
        HighlightSelectedCard(CardAuthPassword, CardAuthPin);
    }

    private void HighlightSelectedCard(System.Windows.Controls.Border selected, System.Windows.Controls.Border unselected)
    {
        selected.BorderBrush = (Brush)FindResource("BrushAccentPrimary");
        selected.BorderThickness = new Thickness(2);
        unselected.BorderBrush = (Brush)FindResource("BrushBorderDefault");
        unselected.BorderThickness = new Thickness(1);
    }

    // ═══════════════════════════════════════
    // ADIM 4: Recovery Key Eylemleri
    // ═══════════════════════════════════════

    private void BtnCopyRecoveryKey_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_generatedRecoveryKey);
        BtnCopyRecoveryKey.Content = L("Str_Copied", "✓  Kopyalandı!");
    }

    // ═══════════════════════════════════════
    // Navigasyon & Akış Yönetimi
    // ═══════════════════════════════════════

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 2) // 1. Adıma (Auth) geri dönülemez! Sadece yeni ayarlarda dönülebilir.
        {
            _currentStep--;
            UpdateStepUI();
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        this.Close(); // Sihirbazdan herhangi bir zamanda çıkılabilir
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        // 1. Adım tetiklemesi
        if (_currentStep == 1)
        {
            var input = _settings.AuthMethod == AuthMethod.Pin ? _currentPin : InputCurrentPassword.Password;
            if (!string.IsNullOrEmpty(input)) VerifyCurrentIdentity(input);
            return;
        }

        // Adıma özgü doğrulama
        if (!ValidateCurrentStep()) return;

        // Adıma özgü çıkış olayları
        if (_currentStep == 2)
        {
            // Pin uzunluğu sabit 4 hane olduğu için buradaki atama iptal edildi.
        }

        MoveToNextStep();
    }

    private void MoveToNextStep()
    {
        if (_currentStep < TOTAL_STEPS)
        {
            _currentStep++;
            OnStepEntering(_currentStep);
            UpdateStepUI();
        }
        else
        {
            CompleteWizard();
        }
    }

    private bool ValidateCurrentStep()
    {
        if (_currentStep == 3)
        {
            string pw1, pw2;
            
            if (_selectedAuthMethod == AuthMethod.Pin)
            {
                pw1 = _setupPin1;
                pw2 = _setupPin2;
            }
            else
            {
                pw1 = SetupPasswordBox1.Password;
                pw2 = SetupPasswordBox2.Password;
            }

            if (string.IsNullOrEmpty(pw1))
            {
                SetPasswordError.Text = L("Str_PasswordEmpty", "Şifre boş olamaz.");
                return false;
            }

            if (pw1 != pw2)
            {
                SetPasswordError.Text = L("Str_PasswordMismatch", "Şifreler eşleşmiyor.");
                return false;
            }

            IAuthenticator authenticator = _selectedAuthMethod == AuthMethod.Pin
                ? new PinAuthenticator(_selectedPinLength)
                : new PasswordAuthenticator();

            if (!authenticator.ValidateFormat(pw1, out var errorMessage))
            {
                SetPasswordError.Text = errorMessage;
                return false;
            }

            SetPasswordError.Text = string.Empty;
        }
        return true;
    }

    private void OnStepEntering(int step)
    {
        switch (step)
        {
            case 2:
                // Pre-select kartları
                if (_selectedAuthMethod == AuthMethod.Pin) CardAuthPin_Click(CardAuthPin, null!);
                else CardAuthPassword_Click(CardAuthPassword, null!);
                break;

            case 3:
                if (_selectedAuthMethod == AuthMethod.Pin)
                {
                    SetupPasswordArea.Visibility = Visibility.Collapsed;
                    SetupPinArea.Visibility = Visibility.Visible;
                    
                    Step3Title.Text = string.Format(L("Str_MasterPinSet", "Master PIN Belirle ({0} Hane)"), _selectedPinLength);
                    Step3Description.Text = L("Str_MasterPinSetDesc", "Bu PIN'i her kilitli uygulamayı açmak için kullanacaksın.");
                    
                    _setupPin1 = string.Empty;
                    _setupPin2 = string.Empty;
                    InitSetupPinDots();
                }
                else
                {
                    SetupPasswordArea.Visibility = Visibility.Visible;
                    SetupPinArea.Visibility = Visibility.Collapsed;
                    
                    Step3Title.Text = L("Str_MasterPasswordSet", "Master Şifre Belirle");
                    Step3Description.Text = L("Str_MasterPasswordSetDesc", "Bu şifreyi her kilitli uygulamayı açmak için kullanacaksın.");
                    SetupPasswordBox1.MaxLength = 64;
                    SetupPasswordBox2.MaxLength = 64;
                    SetupPasswordBox1.Clear();
                    SetupPasswordBox2.Clear();
                    SetupPasswordBox1.Focus();
                }
                break;

            case 4:
                // Şifreler ve ayarlardaki her şey tamam, veritabanını GÜNCELLE ve Recovery üret!
                SaveNewPasswordAndGenerateRecoveryKey();
                
                // Kapat, İptal, Geri butonlarını gizle ki yeni key alındığında tekrar işlem yapılamasın
                BtnCancel.Visibility = Visibility.Collapsed;
                BtnBack.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void UpdateStepUI()
    {
        Step1Auth.Visibility = Visibility.Collapsed;
        Step2Method.Visibility = Visibility.Collapsed;
        Step3SetPassword.Visibility = Visibility.Collapsed;
        Step4Recovery.Visibility = Visibility.Collapsed;

        switch (_currentStep)
        {
            case 1: Step1Auth.Visibility = Visibility.Visible; break;
            case 2: Step2Method.Visibility = Visibility.Visible; break;
            case 3: Step3SetPassword.Visibility = Visibility.Visible; break;
            case 4: Step4Recovery.Visibility = Visibility.Visible; break;
        }

        var stepNames = new[] { "", L("Str_CP_Step1", "Doğrulama"), L("Str_Step2Name", "Kilit Yöntemi"), L("Str_Step3Name", "Şifre Belirle"), L("Str_Step4Name", "Kurtarma Anahtarı") };
        WizardStepText.Text = string.Format(L("Str_StepFormat", "Adım {0}/{1} — {2}"), _currentStep, TOTAL_STEPS, stepNames[_currentStep]);

        var progressWidth = (double)_currentStep / TOTAL_STEPS * (472); // 520 - padding
        WizardProgressBar.Width = Math.Max(progressWidth, 20);

        // Geri butonu: 2. ve 3. adımda görünür
        BtnBack.Visibility = (_currentStep == 2 || _currentStep == 3) ? Visibility.Visible : Visibility.Collapsed;

        // İleri butonu metni
        if (_currentStep == 1) BtnNext.Content = L("Str_CP_Verify", "Doğrula");
        else if (_currentStep > 1 && _currentStep < TOTAL_STEPS) BtnNext.Content = L("Str_BtnNext", "Devam →");
        else if (_currentStep == TOTAL_STEPS) BtnNext.Content = L("Str_BtnComplete", "Tamamla ✓");
    }

    private void SaveNewPasswordAndGenerateRecoveryKey()
    {
        // 1. Yeni Recovery Key Üret
        _generatedRecoveryKey = RecoveryManager.GenerateRecoveryKey();
        RecoveryKeyDisplay.Text = _generatedRecoveryKey;
        BtnCopyRecoveryKey.Content = L("Str_BtnCopy", "📋  Panoya Kopyala");

        // 2. Modelleri ve Ayarları Güncelle
        _settings.AuthMethod = _selectedAuthMethod;
        _settings.PinLength = _selectedPinLength;
        
        string finalPassword = _selectedAuthMethod == AuthMethod.Pin ? _setupPin1 : SetupPasswordBox1.Password;
        _settings.MasterPasswordHash = PasswordHasher.Hash(finalPassword);
        _settings.RecoveryKeyHash = RecoveryManager.HashRecoveryKey(_generatedRecoveryKey);

        // 3. Veritabanına Yaz
        _database.SaveSettings(_settings);
    }

    private void CompleteWizard()
    {
        // 4. adım bittiğinde Close tetiklenir
        this.DialogResult = true;
        this.Close();
    }

    // ═══════════════════════════════════════
    // Yeni Kurulum Numpad Yönetimi
    // ═══════════════════════════════════════

    private void InitSetupPinDots()
    {
        SetupPin1DotsPanel.Children.Clear();
        SetupPin2DotsPanel.Children.Clear();
        for (var i = 0; i < 4; i++) // Strict 4
        {
            SetupPin1DotsPanel.Children.Add(CreateDot());
            SetupPin2DotsPanel.Children.Add(CreateDot());
        }
    }

    private Ellipse CreateDot()
    {
        return new Ellipse
        {
            Width = 14, Height = 14,
            Margin = new Thickness(6, 0, 6, 0),
            Fill = (Brush)FindResource("BrushBackgroundLight"),
            Stroke = (Brush)FindResource("BrushBorderDefault"),
            StrokeThickness = 1
        };
    }

    private void UpdateSetupPinDots()
    {
        for (var i = 0; i < 4; i++)
        {
            if (SetupPin1DotsPanel.Children[i] is Ellipse dot1)
                dot1.Fill = i < _setupPin1.Length ? (Brush)FindResource("BrushAccentPrimary") : (Brush)FindResource("BrushBackgroundLight");

            if (SetupPin2DotsPanel.Children[i] is Ellipse dot2)
                dot2.Fill = i < _setupPin2.Length ? (Brush)FindResource("BrushAccentPrimary") : (Brush)FindResource("BrushBackgroundLight");
        }
    }

    private void SetupNumpad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn)
        {
            string val = btn.Content.ToString() ?? "";
            SetPasswordError.Text = "";

            if (_setupPin1.Length < _selectedPinLength)
            {
                _setupPin1 += val;
            }
            else if (_setupPin2.Length < _selectedPinLength)
            {
                _setupPin2 += val;
                if (_setupPin2.Length == _selectedPinLength && _setupPin1 != _setupPin2)
                {
                    SetPasswordError.Text = L("Str_PasswordMismatch", "Şifreler eşleşmiyor.");
                }
            }
            UpdateSetupPinDots();
        }
    }

    private void SetupNumpadBackspace_Click(object sender, RoutedEventArgs e)
    {
        SetPasswordError.Text = "";
        
        if (_setupPin2.Length > 0)
        {
            _setupPin2 = _setupPin2[..^1];
        }
        else if (_setupPin1.Length > 0)
        {
            _setupPin1 = _setupPin1[..^1];
        }
        UpdateSetupPinDots();
    }
}
