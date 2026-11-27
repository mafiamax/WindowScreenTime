# Proje Planı: Windows Screen Time (WindowScreenTime)

## 1. Proje Özeti
Windows 11 üzerinde arka planda (Sistem Tepsisi / System Tray) çok düşük kaynak tüketimiyle çalışan, kullanıcının **aktif** ve **boşta (idle)** kalma sürelerini takip eden bağımsız bir masaüstü uygulaması.

> Not (Kasım 2026): İlk planda yer alan "Windows Olay Günlüğü'nden geçmiş aktarımı" kaldırıldı (GitHub issue #3). Olay Günlüğü kilitli / ekranı kapalı / boşta geçen zamanı ayırt edemediği için istatistikleri bozuyordu; uygulama yalnızca kendi ölçtüğü verileri kullanır.

---

## 2. Teknoloji Yığını (Tech Stack)
- **Dil & Platform:** C# / .NET 8.0 (Windows-specific hedef: `net8.0-windows`)
- **Arayüz (UI):** WPF (Windows Presentation Foundation) — Modern Windows 11 (Fluent / Koyu Tema) görünümü
- **Sistem Tepsisi (Tray Icon):** `Hcodet.NotifyIcon.Wpf` veya yerel `System.Windows.Forms.NotifyIcon` entegrasyonu
- **Veritabanı / Depolama:** `Microsoft.Data.Sqlite` (Tek dosyalık yerel SQLite veritabanı veya hafif JSON depolama)
- **Windows API (P/Invoke):**
  - `user32.dll -> GetLastInputInfo`: Klavye ve fareye en son ne zaman dokunulduğunu milisaniye bazında ölçmek için.
  - `Microsoft.Win32.SystemEvents`: Oturum kilitleme (`SessionSwitch`), uyku/uyanma (`PowerModeChanged`) ve kapanış olaylarını yakalamak için.

---

## 3. Temel Özellikler

### A. Arka Plan Takip Motoru (Tracking Engine)
1. **Aktif / Boşta (Idle) Ayrımı:**
   - Her 10 saniyede bir `GetLastInputInfo` kontrol edilir.
   - Kullanıcı belirlenen süre boyunca (varsayılan: **5 dakika**) klavye veya fare kullanmadıysa sayaç **"Boşta (Idle)"** moduna geçer ve aktif süreye ekleme yapmaz.
2. **Uyku ve Kilit Ekranı Algılama:**
   - Bilgisayar uykuya geçtiğinde veya `Win + L` ile kilitlendiğinde aktif süre sayımı anında duraklatılır.
3. **Gece Yarısı Geçişi (00:00 Split):**
   - Bilgisayar gece 00:00'ı geçerek açık kalırsa, o ana kadarki süre önceki güne yazılır ve yeni gün için sayaç sıfırdan başlar.

### B. Masaüstü Arayüzü (Dashboard Penceresi)
1. **Özet Kartları (Üst Bölüm):**
   - **Bugünkü Aktif Süre** (Örn: `4 sa 25 dk`)
   - **Günlük Ortalama Kullanım** (Son 7 ve 30 gün bazında)
   - **Bugünkü İlk Açılış & Son Aktiflik Saati**
2. **Gün Gün Kullanım Grafiği & Listesi (Orta/Alt Bölüm):**
   - Son 7 / 14 / 30 günün çubuk grafiği (Bar Chart)
   - Tarih, İlk Açılış, Son Kapanış, Aktif Süre ve Boşta Geçen Süre sütunlarını içeren detaylı tablo.
3. **Sistem Tepsisi (System Tray) & Başlangıç Ayarları:**
   - Pencere kapatıldığında (`X` tuşu) uygulama tamamen kapanmaz, sağ altta saat yanındaki tepsiye küçülür (üzerine gelince *"Bugün: 4s 25dk"* yazar).
   - *"Windows başladığında otomatik çalıştır"* (Registry `HKCU\...\Run` anahtarı) ve *"Boşta sayma süresi (dk)"* ayarları.

---

## 4. Klasör ve Dosya Yapısı
```text
WindowScreenTime/
├── WindowScreenTime.csproj
├── App.xaml / App.xaml.cs         # Tek örnek (Single Instance) kontrolü ve Tray Icon başlatma
├── Models/
│   ├── DailyUsage.cs              # Günlük özet modeli (Tarih, AktifSaniye, BoştaSaniye, İlkAçılış, SonAktiflik)
│   └── AppSettings.cs             # Kullanıcı ayarları (IdleTimeoutMinutes, RunOnStartup)
├── Services/
│   ├── IdleDetector.cs            # Win32 GetLastInputInfo P/Invoke sarmalayıcısı
│   ├── UsageTrackerService.cs     # Ana zamanlayıcı, kilit/uyku olayları ve süre hesaplama
│   └── DataStorageService.cs      # Yerel JSON veri ve Registry başlangıç yönetimi
└── Views/
    ├── MainWindow.xaml            # Modern Windows 11 Dashboard arayüzü
    └── MainWindow.xaml.cs         # Arayüz etkileşimleri ve veri bağlama
```

---

## 5. Geliştirme Adımları (Yeni Projede Yapılacaklar)
1. `.NET 8 WPF` projesinin oluşturulması (`dotnet new wpf`).
2. `IdleDetector` ve `UsageTrackerService` sınıflarının yazılıp arka plan sayacının kurulması.
3. Yerel veri kayıt (`DataStorageService`) modülünün eklenmesi.
4. Windows 11 koyu tema uyumlu Dashboard arayüzünün (`MainWindow.xaml`) ve Sistem Tepsisi (Tray Icon) entegrasyonunun tamamlanması.
5. Projenin derlenip (`dotnet build` / `dotnet run`) test edilmesi.
