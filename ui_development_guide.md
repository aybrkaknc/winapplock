# Visual Studio'da WinAppLock UI Geliştirme Rehberi

WinAppLock, hem arka planda sistem seviyesinde çalışan bir **Service (SYSTEM yetkileri gerektirir)**, hem de son kullanıcı ile etkileşime giren **UI (WPF)** bileşenlerinden oluşan karmaşık bir uygulamadır.

Visual Studio içerisinde arayüzü (XAML) tasarlarken ve testleri (F5) başlatırken sürekli hata fırlatmaması veya IFEO döngülerine girmemesi için aşağıdaki kuralları uygulamanız büyük bir zaman kazandıracaktır.

---

## 🚀 1. Visual Studio'yu KESİNLİKLE "Yönetici" Olarak Çalıştırın

Projenin bel kemiğini oluşturan özelliklerden biri, uygulamaları kilitlerken Windows Kayıt Defteri'ne (Registry - **HKLM**) "Debugger" verisi (IFEO) yazmasıdır. 

Eğer Visual Studio'yu standart modda açarsanız ve `F5` ile **Run** derseniz:
- Registry işlemlerinde anında `SecurityException` veya `Access Denied` yetki hataları alırsınız.
- Bu yüzden, Visual Studio kısayoluna sağ tıklayın ve her zaman **Yönetici olarak çalıştır** seçeneğini kullanın. Mümkünse VS kısayolu "Uyumluluk" sekmesinden "Bu programı yönetici olarak çalıştır" kutusunu işaretleyin.

---

## 🎨 2. XAML Designer'ın Çökmemesi (Design Mode Korunması)

WPF'in XAML Designer ekranı, kodu canlı olarak çizerken uygulamanızın `Constructor` (Kurucu - `InitializeComponent`) kısımlarını anlık çalıştırır.
Siz UI ekranını (Örn: `LockOverlay.xaml.cs`) kodlarken, constructor kısmında *SQLite Veritabanı* veya *Pipe Service* bağlantısı gerçekleştirmeye çalışırsanız VS Designer çöker veya hata gösterir.

Bunu engellemek için kurucu fonksiyonlarınızı her zaman **Design Mode** kontrolü içerisine aldığınızdan emin olun:

```csharp
public LockOverlay()
{
    InitializeComponent();

    // XAML Designer arayüzünde görüntülerken SQLite veya Service çağrılarını tetiklemeyi DURDUR!
    if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
    {
        return; 
    }

    // Normal çalışma zamanı (Runtime) işlemleri buranın altına gelir...
    _database = new AppDatabase();
}
```

---

## 🔗 3. Service ve UI'ı Aynı Anda Başlatmak (Multiple Startup)

Gatekeeper şifre ekranını (`LockOverlay`) görsel olarak test etmek için, şifre dinleyicisini açacak olan `WinAppLock.Service`'in ana arka planda aktif olması şarttır.

Her ikisini de VS içerisinden aynı anda başlatıp "StartTest.bat" ihtiyacını ortadan kaldırabilirsiniz:

1. **Solution Explorer**'daki en üstte bulunan **WinAppLock** (Solution) dosyasına sağ tıklayın ve **Properties (Özellikler)** öğesini seçin.
2. Sol menüden **Startup Project** (Başlangıç Projesi) kısmına gidin.
3. **Multiple startup projects** (Çoklu başlangıç projeleri) radyo butonunu seçin.
4. Listedeki projelerden hem `WinAppLock.Service` hem de `WinAppLock.UI`'ın karşısındaki Action sütununu **Start** (Başlat) olarak ayarlayın. `WinAppLock.Gatekeeper` ise **None** kalsın (onu Windows kendisi IFEO üzerinden tetikler).
5. **Uygula (Apply)** dedikten ve **F5** bastığınızda iki proje de hata ayıklama moduyla yan yana açılacaktır.

---

## 🖌 4. Tema (Renk/Brush) Değişiklikleri ve Hataları

Uygulamada kullandığımız tüm tasarım özellikleri `WinAppLock.UI\Themes\DarkTheme.xaml` sözlüğünde (Dictionary) barındırılır.

**StaticResource vs. DynamicResource:**
- Eğer arayüzde olmayan bir rengi `Background="{StaticResource BulunmayanRenk}"` olarak çağırırsanız, XAML ayrıştırıcısı patlar (Oluşum Hatası/XamlParseException) ve pencereniz ekranda çıkmaz.
- Özellikle yeni tema elemanları yaratırken; tasarım sürecinde önce `DarkTheme.xaml` dosyasına rengi **kaydettiğinizden** emin olun. Sonra `.xaml` dosyalarında kullanın.

> **İPUCU:** Visual Studio'daki Hot Reload ("Ateş" ikonu) özelliği sayesinde `.xaml` üzerinde yaptığınız renk veya hizalama değişikliklerini program kapatıp açmadan anında görebilirsiniz.
  
---

## 🔌 5. Dummy (Sahte) Tetikleyici İle XAML Eğitimi

"Ben sadece Tasarım yapacağım, Kilitli Uygulama vs test edip durmakla zaman kaybetmek istemiyorum" diyorsanız, arka planda Servisi meşgul etmeden sadece arayüzü sınamak için bir Buton ekleyebilirsiniz.

Örneğin geçici olarak `MainWindow.xaml` ekranına şu butonu ekleyin:
```xml
<Button Content="Test LockOverlay Tasarımını Göster" Click="BtnTestLockOverlay_Click"/>
```

Ve C# tarafına şu kodu yapıştırın:
```csharp
private void BtnTestLockOverlay_Click(object sender, RoutedEventArgs e)
{
    var overlay = new WinAppLock.UI.Views.LockOverlay();
    overlay.ShowForProcess(9999, "Tasarım.exe", null, null);
    overlay.Activate();
}
```
*Bu sayede Gatekeeper ile uğraşmadan butonla doğrudan XAML sayfasını ekranda fırlatıp nasıl göründüğünü denetleyebilirsiniz! UI işiniz bittikten sonra sileceğinizi unutmayın.*
