# WindowScreenTime (Windows Screen Time)

Windows 11 üzerinde arka planda (Sistem Tepsisi / System Tray) çok düşük kaynak tüketimiyle çalışan, kullanıcının **aktif**, **boşta (idle)** ve **uykuda (sleep)** geçen sürelerini takip eden, ayrıca Windows Sistem Olay Günlüğü'nden (Event Viewer) geçmiş günlerin verilerini içeri aktarabilen modern masaüstü uygulaması.

## Öne Çıkan Özellikler

- **Aktif, Boşta (Idle) ve Uyku Takibi:**
  - `GetLastInputInfo` (Win32 API) ile klavye ve fare hareketlerini izler; belirlenen süre boyunca (varsayılan 5 dk) hareketsizlikte otomatik olarak **Boşta (Idle)** moduna geçer.
  - Bilgisayar ekranı kapandığında (`GUID_CONSOLE_DISPLAY_state`), kilitlendiğinde (`Win + L`) veya uykuya geçtiğinde arka plan sayacını tamamen durdurarak Windows'un uyku moduna geçişini engellemez ve bu süreyi **Uykuda Geçen Süre** olarak kaydeder.
  - Gece `00:00` geçişlerinde süreleri otomatik olarak günlere böler.
- **Windows Olay Günlüğü (Event Viewer) Geçmiş Aktarımı:**
  - Windows Sistem Günlüğü'ndeki açılış, kapanış ve Modern Standby (`Event ID 6005, 6006, 12, 13, 42, 1, 107, 506, 507`) olaylarını analiz ederek son 30 günün kullanım ve uyku geçmişini tek tıkla içeri aktarır. Beklenmedik kapanışlar (`41`, `6008`) doğru şekilde ele alınır.
  - Olay Günlüğü yalnızca bilgisayarın açık kaldığı süreyi bilir; kilitli / ekran kapalı / boşta ayrımı yapamaz. Uygulamanın canlı ölçtüğü günlerin üzerine yazılmaz.
- **Modern Windows 11 Koyu Tema Arayüzü (WPF):**
  - Bugünkü aktif süre, 7/30 günlük ortalama ve ilk açılış/son aktiflik özet kartları.
  - Son 7, 14 ve 30 günlük etkileşimli çubuk grafik (Bar Chart).
  - Günlük detaylı kullanım tablosu (İlk Açılış, Son Aktiflik, Aktif Süre, Boşta Geçen Süre, Uykuda Geçen Süre, Toplam Açık).
- **Hakkında Penceresi ve Güncelleme Kontrolü:**
  - Ana penceredeki veya Sistem Tepsisi menüsündeki **Hakkında** ile açılır: sürüm bilgisi, kısa tanıtım ve künye.
  - `mafiamax/WindowScreenTime` GitHub Releases üzerinden yeni sürüm kontrolü ve SHA-256 doğrulamalı tek tıkla kurulum.
  - **Veri Klasörünü Aç** ile kullanım geçmişinin saklandığı klasöre (`%LocalAppData%\WindowScreenTime`) hızlı erişim.

## Künye

- **Proje Sahibi:** Erhan.M
- **Geliştirme:** Claude (Anthropic) ve Gemini (Google) ile geliştirildi
- © 2026 Erhan.M

## Kurulum

1. [Releases](https://github.com/mafiamax/WindowScreenTime/releases) sayfasından en son **`WindowScreenTime_Setup.exe`** dosyasını indirin ve çalıştırın.
2. Kurulum sihirbazı uygulamayı `%LocalAppData%\Programs\WindowScreenTime` dizinine kurar, Başlat Menüsü ve Masaüstü kısayollarını oluşturur.

## Kaynak Koddan Derleme

Gereksinimler: **.NET 8.0 SDK** (`net8.0-windows`)

```powershell
# Ana uygulamayı derleme
dotnet build WindowScreenTime.csproj -c Release

# Kurulum paketini (WindowScreenTime_Setup.exe) oluşturma
Compress-Archive -Path "bin\Release\net8.0-windows\*" -DestinationPath "Installer\payload.zip" -Force
dotnet publish Installer\WindowScreenTime.Setup.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .
```
