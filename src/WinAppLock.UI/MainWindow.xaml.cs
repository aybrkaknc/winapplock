using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WinAppLock.Core.Data;
using WinAppLock.Core.Identification;
using WinAppLock.Core.Models;

namespace WinAppLock.UI;

/// <summary>
/// Ana pencere code-behind.
/// Navigasyon, sürükle-bırak, uygulama ekleme ve pencere kontrolleri.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppDatabase _database;

    public MainWindow()
    {
        InitializeComponent();
        _database = new AppDatabase();
        LoadLockedApps();
    }

    // ═══════════════════════════════════════
    // Title Bar Olayları
    // ═══════════════════════════════════════

    /// <summary>Başlık çubuğunu sürükleyerek pencereyi taşıma.</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Çift tıkla maximize/restore
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    /// <summary>Pencereyi küçültme.</summary>
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>Pencereyi kapatma (system tray'e gider).</summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        // TODO: System tray'e küçült (TrayIconService entegrasyonunda)
        Hide();
    }

    // ═══════════════════════════════════════
    // Navigasyon
    // ═══════════════════════════════════════

    /// <summary>Dashboard sayfasını göster.</summary>
    private void BtnNavDashboard_Click(object sender, RoutedEventArgs e)
    {
        DashboardContent.Visibility = Visibility.Visible;
        SettingsContent.Visibility = Visibility.Collapsed;
    }

    /// <summary>Ayarlar sayfasını göster.</summary>
    private void BtnNavSettings_Click(object sender, RoutedEventArgs e)
    {
        DashboardContent.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Visible;
    }

    // ═══════════════════════════════════════
    // Uygulama Ekleme
    // ═══════════════════════════════════════

    /// <summary>Dosya seçme dialogu ile uygulama ekleme.</summary>
    private void BtnAddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Kilitlenecek uygulamayı seç",
            Filter = "Çalıştırılabilir Dosyalar (*.exe)|*.exe",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            AddAppByPath(dialog.FileName);
        }
    }

    /// <summary>Sürükle-bırak ile exe dosyası ekleme — DragOver.</summary>
    private void AppListPanel_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Effects = files.Any(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    /// <summary>Sürükle-bırak ile exe dosyası ekleme — Drop.</summary>
    private void AppListPanel_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var file in files)
        {
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                AddAppByPath(file);
            }
        }
    }

    /// <summary>
    /// Belirtilen yoldaki exe'yi kilitli uygulamalar listesine ekler.
    /// Kimlik bilgilerini (hash, PE header) otomatik olarak çıkarır.
    /// </summary>
    /// <param name="exePath">Exe dosyasının tam yolu</param>
    private void AddAppByPath(string exePath)
    {
        try
        {
            // Kimlik oluştur (hash + PE header + dosya adı)
            var identity = AppIdentifier.CreateIdentity(exePath);

            // Zaten ekli mi kontrol et
            var existingApps = _database.GetAllLockedApps();
            if (existingApps.Any(a => a.Identity.Sha256Hash == identity.Sha256Hash))
            {
                MessageBox.Show("Bu uygulama zaten kilitli listede!",
                    "WinAppLock", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // İkon çıkar
            string? iconBase64 = null;
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    using var ms = new MemoryStream();
                    icon.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    iconBase64 = Convert.ToBase64String(ms.ToArray());
                }
            }
            catch { /* İkon çıkaramazsa devam et */ }

            // Görüntü adı: PE ProductName > dosya adı > exe adı
            var displayName = !string.IsNullOrEmpty(identity.ProductName)
                ? identity.ProductName
                : Path.GetFileNameWithoutExtension(exePath);

            var lockedApp = new LockedApp
            {
                DisplayName = displayName,
                Identity = identity,
                IconBase64 = iconBase64
            };

            _database.AddLockedApp(lockedApp);
            LoadLockedApps();

            // TODO: Service'e AppAdded mesajı gönder (PipeClient entegrasyonunda)
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uygulama eklenirken hata:\n{ex.Message}",
                "WinAppLock Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════
    // Uygulama Listesi
    // ═══════════════════════════════════════

    /// <summary>
    /// Kilitli uygulamalar listesini veritabanından yükler ve UI'ı günceller.
    /// </summary>
    private void LoadLockedApps()
    {
        var apps = _database.GetAllLockedApps();

        // Dinamik kartları temizle (EmptyState hariç)
        var cardsToRemove = AppListPanel.Children
            .OfType<FrameworkElement>()
            .Where(c => c != EmptyStatePanel)
            .ToList();

        foreach (var card in cardsToRemove)
            AppListPanel.Children.Remove(card);

        // Boş durum göster/gizle
        EmptyStatePanel.Visibility = apps.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Başlık alt metnini güncelle
        HeaderSubtitle.Text = $"{apps.Count} uygulama korunuyor";

        // Kartları oluştur
        foreach (var app in apps)
        {
            var card = CreateAppCard(app);
            AppListPanel.Children.Add(card);
        }
    }

    /// <summary>
    /// Tek bir kilitli uygulama için kart bileşeni oluşturur.
    /// </summary>
    /// <param name="app">Kilitli uygulama modeli</param>
    /// <returns>UI kart elementi</returns>
    private Border CreateAppCard(LockedApp app)
    {
        // ─── İkon ───
        var iconElement = new TextBlock
        {
            Text = "📦",
            FontSize = 28,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };

        // Base64 ikon varsa Image kontrolü kullan
        if (!string.IsNullOrEmpty(app.IconBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(app.IconBase64);
                var image = new System.Windows.Controls.Image
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                using var ms = new MemoryStream(bytes);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                image.Source = bitmap;

                // iconElement yerine image kullan — aşağıda panel'e image eklenir
                iconElement = null!;
                var panel = CreateAppCardPanel(app, image);
                return panel;
            }
            catch { /* Fallback: emoji ikon */ }
        }

        return CreateAppCardPanel(app, iconElement);
    }

    /// <summary>
    /// Kart panelini oluşturur (ikon + bilgi + toggle + sil butonu).
    /// </summary>
    private Border CreateAppCardPanel(LockedApp app, UIElement iconElement)
    {
        // ─── Metin Bilgileri ───
        var nameText = new System.Windows.Controls.TextBlock
        {
            Text = app.DisplayName,
            Foreground = (System.Windows.Media.Brush)FindResource("BrushTextPrimary"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("FontPrimary"),
            FontSize = (double)FindResource("FontSizeMD"),
            FontWeight = FontWeights.SemiBold
        };

        var pathText = new System.Windows.Controls.TextBlock
        {
            Text = app.Identity.ExecutablePath,
            Foreground = (System.Windows.Media.Brush)FindResource("BrushTextMuted"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("FontPrimary"),
            FontSize = (double)FindResource("FontSizeXS"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 350
        };

        var infoStack = new StackPanel();
        infoStack.Children.Add(nameText);
        infoStack.Children.Add(pathText);

        // ─── Toggle Switch ───
        var toggle = new System.Windows.Controls.Primitives.ToggleButton
        {
            IsChecked = app.IsEnabled,
            Style = (Style)FindResource("ToggleSwitch"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 8, 0),
            ToolTip = app.IsEnabled ? "Kilidi devre dışı bırak" : "Kilidi aktifleştir"
        };
        toggle.Tag = app.Id;
        toggle.Checked += (_, _) => ToggleAppLock(app.Id, true);
        toggle.Unchecked += (_, _) => ToggleAppLock(app.Id, false);

        // ─── Sil Butonu ───
        var deleteBtn = new Button
        {
            Content = "🗑",
            Style = (Style)FindResource("BtnIcon"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Kilidi kaldır"
        };
        deleteBtn.Tag = app.Id;
        deleteBtn.Click += (_, _) => RemoveApp(app.Id, app.DisplayName);

        // ─── Layout ───
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(iconElement, 0);
        Grid.SetColumn(infoStack, 1);
        Grid.SetColumn(toggle, 2);
        Grid.SetColumn(deleteBtn, 3);

        contentGrid.Children.Add(iconElement);
        contentGrid.Children.Add(infoStack);
        contentGrid.Children.Add(toggle);
        contentGrid.Children.Add(deleteBtn);

        // ─── Kart Border ───
        var card = new Border
        {
            Style = (Style)FindResource("CardPanel"),
            Margin = new Thickness(0, 0, 0, 8),
            Child = contentGrid
        };

        return card;
    }

    /// <summary>Uygulama kilidini aktif/pasif yapar.</summary>
    private void ToggleAppLock(int appId, bool isEnabled)
    {
        var apps = _database.GetAllLockedApps();
        var app = apps.FirstOrDefault(a => a.Id == appId);
        if (app == null) return;

        app.IsEnabled = isEnabled;
        _database.UpdateLockedApp(app);

        // TODO: Service'e AppToggled mesajı gönder
    }

    /// <summary>Uygulamayı kilitli listeden kaldırır (onay ile).</summary>
    private void RemoveApp(int appId, string appName)
    {
        var result = MessageBox.Show(
            $"\"{appName}\" uygulamasının kilidini kaldırmak istediğine emin misin?",
            "Kilidi Kaldır",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _database.RemoveLockedApp(appId);
            LoadLockedApps();

            // TODO: Service'e AppRemoved mesajı gönder
        }
    }

    // ═══════════════════════════════════════
    // Tümünü Kilitle
    // ═══════════════════════════════════════

    /// <summary>Tüm uygulamaları kilitle.</summary>
    private void BtnLockAll_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Service'e LockAll mesajı gönder
        MessageBox.Show("Tüm kilitli uygulamalar kilitlendi!",
            "WinAppLock", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}