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

## Yayınlanmamış

Yenilikler:
- Mola hatırlatıcısı sistem tepsisi menüsünden 1 saatliğine veya gün sonuna kadar susturulabiliyor (ör. toplantı veya ekran paylaşımı sırasında); susturma istenince kaldırılabiliyor
- Günlük hedef: alt çubuktan bir günlük kullanım hedefi (4–12 saat) seçilebiliyor. Bugünkü aktif süre hedefe ulaşınca günde bir kez bildirim gösteriliyor, grafikte yeşil hedef çizgisi çiziliyor ve bugünkü kartta hedefin yüzde kaçına ulaşıldığı yazıyor
- Günlük Geçmiş tablosunda dönem seçici: son 30 günün yanında kayıtlı her ay ayrı ayrı görüntülenebiliyor; seçilen dönemin toplam ve günlük ortalama aktif süresi tablonun üstünde yazıyor

İyileştirmeler:
- Uygulama arka planda çalışırken masaüstü veya Başlat menüsü kısayoluna tıklamak artık pencereyi doğrudan öne getiriyor ("zaten çalışıyor" uyarısı çıkmıyor)
- Günlük Geçmiş tablosunda aşağı kaydırırken tablo artık her birkaç saniyede bir en üste atlamıyor, seçili satır da kaybolmuyor
- Kurulum ve kaldırma sırasında açık olan uygulama artık düzgün kapatılıyor; son dakikaların kullanım süresi kaybolmuyor ve saatin yanında hayalet simge kalmıyor
- Kurulum sihirbazı masaüstü ve Başlat menüsü kısayollarını, antivirüs programlarının daha az şüphelendiği Windows'un kendi yöntemiyle oluşturuyor

---

## Sürüm 1.3.0 (24 Kasım 2026)

Yenilikler:
- "Geçmişi Sıfırla" düğmesi: bugün dahil tüm kayıtlı günleri silip sayaçları sıfırdan başlatır (temiz başlangıç). Silmeden önce onay sorulur, eski geçmiş veri klasörüne yedeklenir ve ayarlar korunur

---

## Sürüm 1.2.2 (22 Kasım 2026)

Düzeltmeler:
- Windows ile otomatik başlarken ekranda bir anlığına açılıp kapanan komut penceresi artık görünmüyor (mevcut ayar güncellemeyle kendiliğinden yeni yönteme geçer)

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
