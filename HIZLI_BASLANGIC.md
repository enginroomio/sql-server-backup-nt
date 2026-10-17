# Hızlı Başlangıç (Windows Server)

Teknik ekip olmadan, birkaç ayar ile kurulum.

## Adımlar

1. Klasörü sunucuya kopyalayın (`dist\SQLYedeklemeAraci.exe` dahil olmalı).
2. `basit-ayarlar.json` dosyasını düzenleyin (zorunlu 4 alan + isteğe bağlı e-posta):

```json
{
  "Sunucu": ".\\SQLEXPRESS",
  "YedekKlasoru": "C:\\SQLBackup",
  "SaklamaAdedi": 7,
  "Saat": "02:00",
  "Eposta": {
    "Acik": true,
    "SmtpSunucu": "smtp.ornek.com",
    "SmtpPort": 587,
    "Ssl": true,
    "Kimden": "yedek@ornek.com",
    "Kime": "yonetici@ornek.com",
    "SmtpKullanici": "yedek@ornek.com",
    "SmtpParola": "SMTP_PAROLANIZ",
    "BasaridaGonder": true,
    "HatadaGonder": true
  }
}
```

3. `Kurulum-Sunucu.bat` → çift tık → UAC’de **Evet** (veya `Uygula-Ayarlar.ps1`).
4. Uygulamada **Test Yedeği Al**. Yeşil kutular = tamam.  
   E-posta açıksa başarı mailinin konusu: **yedekleme başarılı** (gövde de aynı ifadeyi içerir).

## E-posta notları

- Varsayılan: `Eposta.Acik = false` (mail kapalı).
- Günlük görev (`--backup`) ve Test Yedeği aynı motoru kullanır → başarıda mail gider.
- SMTP parolası diske düz metin yazılmaz (DPAPI). `SmtpParola`’yı bir kez yazıp uyguladıktan sonra dosyadan silebilirsiniz.
- Mail gitmezse günlükte uyarı görünür; yedek yine **başarılı** kalır.

## Ne yedeklenir?

Tüm **kullanıcı** veritabanları (master/model/msdb/tempdb hariç). Yedekler `YedekKlasoru\SQLBackup\` altında `.zip` olarak durur.

## Oturum kapalı sunucu

Ayarlar → **Sunucu Modu**:

- **Oturum açılmadan** — Windows parolanız bir kez istenir (görev zamanlayıcıya kaydedilir; uygulama saklamaz).
- **SYSTEM** — Windows parolası gerekmez; SQL kullanıcı/parola (Gelişmiş Ayarlar) gerekir.

## Sorun olursa

Ayarlar → **Hızlı Kontrol**. Ayrıntı: `KURULUM_AYARLAR.md`. Güvenlik: `GUVENLIK.md`.
