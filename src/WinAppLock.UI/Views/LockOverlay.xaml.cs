using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinAppLock.Core.Data;
using WinAppLock.Core.Models;
using WinAppLock.Core.Security;

namespace WinAppLock.UI.Views;

/// <summary>
/// Fullscreen şifre giriş overlay'i.
/// Kilitli uygulama algılandığında Service tarafından tetiklenir.
/// 
/// Özellikler:
/// - PIN pad veya şifre girişi (kullanıcı ayarına göre)
/// - Hatalı girişte titreşim animasyonu + ses efekti
/// - Doğru girişte başarı animasyonu
/// - Alt+Tab, Alt+F4, Win tuşu engelleme
/// - Deneme limiti + bekleme süresi
/// </summary>
public partial class LockOverlay : Window
{
    private readonly AppDatabase _database;
    private readonly AppSettings _settings;
    private readonly IAuthenticator _authenticator;

    private int _processId;
    private string _appName = string.Empty;
    private string _passwordHash = string.Empty;

    private string _currentPin = string.Empty;
    private int _failedAttempts;
    private DateTime? _cooldownUntil;
    private bool _isAuthSuccessful;

    /// <summary>Doğrulama başarılı olduğunda tetiklenir.</summary>
    public event Action<int>? AuthSuccess;

    /// <summary>Kullanıcı iptal ettiğinde tetiklenir.</summary>
    public event Action<int>? AuthCancelled;

    public LockOverlay()
    {
        InitializeComponent();

        // Tasarım modunda mıyız kontrol et (Designer'ın çökmesini engeller)
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
        {
            // Derleyicinin (CS8618 vb.) uninitialized error vermemesi için sahte (dummy) atamalar yapıyoruz.
            // Zaten tasarım aşamasında bu sınıfların metotları çalışmayacak.
            _database = null!;
            _settings = null!;
            _authenticator = null!;
            return;
        }

        _database = new AppDatabase();
        _settings = _database.GetSettings();

        // Doğrulama yöntemine göre authenticator seç
        _authenticator = _settings.AuthMethod == AuthMethod.Pin
            ? new PinAuthenticator(_settings.PinLength)
            : new PasswordAuthenticator();

        Loaded += OnLoaded;
    }

    /// <summary>
    /// ResourceDictionary'den lokalize metin çeker.
    /// </summary>
    private static string L(string key, string fallback = "")
    {
        return Application.Current.TryFindResource(key)?.ToString() ?? fallback;
    }

    /// <summary>
    /// Overlay'i belirtilen process için gösterir.
    /// </summary>
    /// <param name="processId">Askıya alınan process ID'si</param>
    /// <param name="appName">Uygulama görüntü adı</param>
    /// <param name="iconBase64">Uygulama ikonu (Base64, opsiyonel)</param>
    /// <param name="customPasswordHash">Uygulamaya özel şifre hash'i (null ise master kullanılır)</param>
    public void ShowForProcess(int processId, string appName, string? iconBase64 = null, string? customPasswordHash = null)
    {
        _processId = processId;
        _appName = appName;
        _passwordHash = customPasswordHash ?? _settings.MasterPasswordHash;
        _currentPin = string.Empty;
        _failedAttempts = 0;
        _cooldownUntil = null;
        _isAuthSuccessful = false;

        // UI güncelle
        OverlayAppName.Text = appName;
        OverlayStatusText.Text = string.Empty;
        OverlayAttemptsText.Text = string.Empty;

        // İkon ayarla
        if (!string.IsNullOrEmpty(iconBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(iconBase64);
                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                OverlayAppIcon.Source = bitmap;
                OverlayAppIcon.Visibility = Visibility.Visible;
            }
            catch
            {
                OverlayAppIcon.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            OverlayAppIcon.Visibility = Visibility.Collapsed;
        }

        if (_settings.AuthMethod == AuthMethod.Pin)
        {
            PinInputArea.Visibility = Visibility.Visible;
            PasswordInputArea.Visibility = Visibility.Collapsed;
            OverlayPromptText.Text = L("Str_EnterPin", "Bu uygulama kilitli. PIN kodunu gir.");
            InitPinDots();
        }
        else
        {
            PinInputArea.Visibility = Visibility.Collapsed;
            PasswordInputArea.Visibility = Visibility.Visible;
            OverlayPromptText.Text = L("Str_EnterPassword", "Bu uygulama kilitli. Şifreni gir.");
            OverlayPasswordBox.Password = string.Empty;
        }

        Show();
    }

    /// <summary>Pencere yüklendiğinde giriş animasyonunu oynat.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var animation = (Storyboard)FindResource("FadeInAnimation");
        animation.Begin();

        // Şifre modunda otomatik odaklan
        if (_settings.AuthMethod == AuthMethod.Password)
        {
            OverlayPasswordBox.Focus();
        }
    }

    // ═══════════════════════════════════════
    // PIN Pad İşlemleri
    // ═══════════════════════════════════════

    /// <summary>PIN uzunluğuna göre nokta göstergelerini oluşturur.</summary>
    private void InitPinDots()
    {
        PinDotsPanel.Children.Clear();
        for (var i = 0; i < _settings.PinLength; i++)
        {
            var dot = new Ellipse
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(6, 0, 6, 0),
                Fill = (Brush)FindResource("BrushBackgroundLight"),
                Stroke = (Brush)FindResource("BrushBorderDefault"),
                StrokeThickness = 1
            };
            PinDotsPanel.Children.Add(dot);
        }
    }

    /// <summary>PIN noktalarının doluluğunu günceller.</summary>
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

    /// <summary>Sayısal buton tıklama (0-9).</summary>
    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsCoolingDown()) return;

        if (sender is Button btn && _currentPin.Length < _settings.PinLength)
        {
            _currentPin += btn.Content.ToString();
            UpdatePinDots();

            // PIN dolduğunda otomatik doğrula
            if (_currentPin.Length == _settings.PinLength)
            {
                ValidateInput(_currentPin);
            }
        }
    }

    /// <summary>Son girilen rakamı siler.</summary>
    private void BtnPinBackspace_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPin.Length > 0)
        {
            _currentPin = _currentPin[..^1];
            UpdatePinDots();
        }
    }

    /// <summary>PIN'i manuel gönder (✓ butonu).</summary>
    private void BtnPinSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPin.Length > 0)
        {
            ValidateInput(_currentPin);
        }
    }

    // ═══════════════════════════════════════
    // Şifre Giriş İşlemleri
    // ═══════════════════════════════════════

    /// <summary>Şifre alanında Enter tuşu ile gönder.</summary>
    private void OverlayPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnPasswordSubmit_Click(sender, e);
        }
    }

    /// <summary>Şifreyi gönder butonu.</summary>
    private void BtnPasswordSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (IsCoolingDown()) return;

        var password = OverlayPasswordBox.Password;
        if (!string.IsNullOrEmpty(password))
        {
            ValidateInput(password);
        }
    }

    // ═══════════════════════════════════════
    // Doğrulama Mantığı
    // ═══════════════════════════════════════

    /// <summary>
    /// Şifre doğrulama.
    /// </summary>
    private void ValidateInput(string input)
    {
        if (IsCoolingDown()) return;

        if (_authenticator.Verify(input, _passwordHash))
        {
            // Başarılı
            _isAuthSuccessful = true;
            OnAuthenticationSuccess();
        }
        else
        {
            // Başarısız
            OnAuthenticationFailed();
        }
    }

    /// <summary>
    /// Çarpıya, ALT+F4'e veya ESC'ye tıklandığında (Şifre başarıyla girilmeden kapatılırsa),
    /// process'i arkada açık bırakmamak adına AuthCancelled fırlatarak Service'e iptal bildiririz.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isAuthSuccessful)
        {
            AuthCancelled?.Invoke(_processId);
        }
        base.OnClosing(e);
    }

    /// <summary>Kullanıcı İptal Et butonuna bastığında.</summary>
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>Başarılı doğrulama: animasyon oynat ve overlay'i kapat.</summary>
    private void OnAuthenticationSuccess()
    {
        _database.LogAccessAttempt(_appName, true);

        // Başarı sesi
        if (_settings.SoundEnabled)
        {
            SystemSounds.Beep.Play();
        }

        // Başarı animasyonu sonrası pencereyi kapat
        var animation = (Storyboard)FindResource("SuccessAnimation");
        animation.Completed += (_, _) =>
        {
            AuthSuccess?.Invoke(_processId);
            Close();
        };
        animation.Begin();
    }

    /// <summary>Başarısız doğrulama: titreşim + hata mesajı + deneme sayacı.</summary>
    private void OnAuthenticationFailed()
    {
        _failedAttempts++;
        _database.LogAccessAttempt(_appName, false);

        // Hata sesi
        if (_settings.SoundEnabled)
        {
            SystemSounds.Hand.Play();
        }

        // PIN'i temizle
        _currentPin = string.Empty;
        UpdatePinDots();
        OverlayPasswordBox.Password = string.Empty;

        // Titreşim animasyonu
        var shake = (Storyboard)FindResource("ShakeAnimation");
        shake.Begin();

        // Kalan deneme hesapla
        var remaining = _settings.MaxAttempts - _failedAttempts;

        if (remaining <= 0)
        {
            // Bekleme süresini başlat
            _cooldownUntil = DateTime.UtcNow.AddSeconds(_settings.CooldownSeconds);
            OverlayStatusText.Text = L("Str_TooManyAttempts", "Çok fazla hatalı giriş!");
            OverlayAttemptsText.Text = string.Format(L("Str_WaitSeconds", "{0} saniye beklemelisin."), _settings.CooldownSeconds);

            // Geri sayım başlat
            StartCooldownTimer();
        }
        else
        {
            OverlayStatusText.Text = L("Str_WrongPassword", "Yanlış şifre!");
            OverlayAttemptsText.Text = string.Format(L("Str_AttemptsRemaining", "{0} deneme hakkın kaldı."), remaining);
        }
    }

    /// <summary>Bekleme süresi aktif mi kontrol eder.</summary>
    private bool IsCoolingDown()
    {
        if (_cooldownUntil == null) return false;

        if (DateTime.UtcNow >= _cooldownUntil.Value)
        {
            _cooldownUntil = null;
            _failedAttempts = 0;
            OverlayStatusText.Text = string.Empty;
            OverlayAttemptsText.Text = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>Bekleme süresi geri sayım zamanlayıcısı.</summary>
    private void StartCooldownTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        timer.Tick += (_, _) =>
        {
            if (_cooldownUntil == null || DateTime.UtcNow >= _cooldownUntil.Value)
            {
                timer.Stop();
                _cooldownUntil = null;
                _failedAttempts = 0;
                OverlayStatusText.Text = string.Empty;
                OverlayAttemptsText.Text = L("Str_TryAgain", "Tekrar deneyebilirsin.");
                return;
            }

            var remaining = (_cooldownUntil.Value - DateTime.UtcNow).TotalSeconds;
            OverlayAttemptsText.Text = string.Format(L("Str_WaitSeconds", "{0} saniye beklemelisin."), (int)remaining);
        };

        timer.Start();
    }

    // ═══════════════════════════════════════
    // Tuş Engelleme
    // ═══════════════════════════════════════

    /// <summary>
    /// Alt+Tab, Alt+F4, Win tuşu gibi kaçış girişimlerini engeller.
    /// </summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Alt+F4 engelle
        if (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            e.Handled = true;
        }

        // Alt+Tab engelle
        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            e.Handled = true;
        }

        // Win tuşu engelle
        if (e.Key == Key.LWin || e.Key == Key.RWin)
        {
            e.Handled = true;
        }

        // Escape basıldığında kapansın (İptal)
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }

        // Sayısal tuşlarla PIN girişi (klavyeden)
        if (_settings.AuthMethod == AuthMethod.Pin && PinInputArea.Visibility == Visibility.Visible)
        {
            var digit = e.Key switch
            {
                Key.D0 or Key.NumPad0 => "0",
                Key.D1 or Key.NumPad1 => "1",
                Key.D2 or Key.NumPad2 => "2",
                Key.D3 or Key.NumPad3 => "3",
                Key.D4 or Key.NumPad4 => "4",
                Key.D5 or Key.NumPad5 => "5",
                Key.D6 or Key.NumPad6 => "6",
                Key.D7 or Key.NumPad7 => "7",
                Key.D8 or Key.NumPad8 => "8",
                Key.D9 or Key.NumPad9 => "9",
                _ => null
            };

            if (digit != null && _currentPin.Length < _settings.PinLength)
            {
                if (IsCoolingDown()) return;
                _currentPin += digit;
                UpdatePinDots();

                if (_currentPin.Length == _settings.PinLength)
                {
                    ValidateInput(_currentPin);
                }
            }

            // Backspace
            if (e.Key == Key.Back && _currentPin.Length > 0)
            {
                _currentPin = _currentPin[..^1];
                UpdatePinDots();
            }
        }
    }
}
