# Windows Screen Time - Sürüm Notları

Bu dosya her sürümde yapılan değişiklikleri listeler. GitHub'daki sürüm notları ve uygulama içindeki
güncelleme penceresi bu dosyadan otomatik olarak üretilir.

Yazım kuralları:
- Her sürüm `## Sürüm X.Y.Z (gün Ay yıl)` başlığıyla başlar; en yeni sürüm en üstte yer alır.
- Başlıktaki sürüm, `WindowScreenTime.csproj` içindeki `<Version>` ve yayın etiketi (`vX.Y.Z`) ile aynı olmalıdır.
- Maddeler kısa, kullanıcının anlayacağı dilde ve `- ` ile yazılır.
- Henüz yayınlanmamış değişiklikler `## Yayınlanmamış` başlığı altında toplanır; yayından önce bu başlık
  yeni sürüm numarasıyla değiştirilir.

---

## Sürüm 1.2.1 (18 Kasım 2026)

Düzeltmeler:
- Kurulum sihirbazında sürüm numarası artık doğru gösteriliyor (eskiden hep v1.0.0 yazıyordu)
- Kurulum sihirbazında İptal ve Kurulumu Başlat düğmelerinin üzerine gelince yazılar okunur kalıyor

---

## Sürüm 1.2.0 (18 Kasım 2026)

Yenilikler:
- Mola hatırlatıcısı: kesintisiz çalışma süresi dolunca sistem tepsisinden bildirim (30-120 dk veya kapalı)
- Geçmişi CSV olarak dışa aktarma (Excel'de doğrudan açılır)
- Grafikte Aktif, Boşta ve Uykuda süreleri üç renkle birlikte gösteriliyor
- Açık tema eklendi; Koyu, Açık veya Windows temasına uyan Sistem seçeneği
- Güncelleme penceresindeki sürüm notları artık düz ve okunaklı metin olarak gösteriliyor

---

## Sürüm 1.1.0 (16 Kasım 2026)

Yenilikler:
- Hakkında penceresi (künye, güncelleme kontrolü, veri klasörü)
- İlk Açılış saatinin 00:00 görünmesi düzeltildi
- Kilit ekranı ve uyku süreleri daha doğru ölçülüyor
- Olay Günlüğü taraması artık mevcut verilerin üzerine yazmıyor
- Güncellemeler SHA-256 ile doğrulanıyor

---

## Sürüm 1.0.0 (16 Kasım 2026)

İlk sürüm:
- Aktif, boşta ve uykuda geçen sürelerin sistem tepsisinden takibi
- Günlük özet kartları, 7/14/30 günlük grafik ve geçmiş tablosu
- Windows Olay Günlüğü'nden geçmiş günlerin aktarılması
- Kurulum sihirbazı ve GitHub üzerinden güncelleme kontrolü
