# Hızlı Başlangıç (Windows Server)

Teknik ekip olmadan, **4 ayar** ile kurulum.

## Adımlar

1. Klasörü sunucuya kopyalayın (`dist\SQLYedeklemeAraci.exe` dahil olmalı).
2. `basit-ayarlar.json` dosyasını düzenleyin:

```json
{
  "Sunucu": ".\\SQLEXPRESS",
  "YedekKlasoru": "C:\\SQLBackup",
  "SaklamaAdedi": 7,
  "Saat": "02:00"
}
```

3. `Kurulum-Sunucu.bat` → çift tık → UAC’de **Evet**.
4. Uygulamada **Test Yedeği Al**. Yeşil kutular = tamam.

## Ne yedeklenir?

Tüm **kullanıcı** veritabanları (master/model/msdb/tempdb hariç). Yedekler `YedekKlasoru\SQLBackup\` altında `.zip` olarak durur.

## Oturum kapalı sunucu

Ayarlar → **Sunucu Modu**:

- **Oturum açılmadan** — Windows parolanız bir kez istenir (görev zamanlayıcıya kaydedilir; uygulama saklamaz).
- **SYSTEM** — Windows parolası gerekmez; SQL kullanıcı/parola (Gelişmiş Ayarlar) gerekir.

## Sorun olursa

Ayarlar → **Hızlı Kontrol**. Ayrıntı: `KURULUM_AYARLAR.md`. Güvenlik: `GUVENLIK.md`.
