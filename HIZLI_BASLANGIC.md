# Hızlı Başlangıç — SQL yedek → Google Drive

**Kaynak:** SQL Server (`BACKUP DATABASE`)  
**Hedef:** Google Drive senkron klasörü (`...\SQLBackup\`)  
Drive, SQL yedeğinin yerine geçmez.

## Adımlar (Drive hedefi)

1. [Google Drive for Desktop](https://www.google.com/drive/download/) kurun, kişisel hesapla giriş yapın.
2. `basit-ayarlar.json`:

```json
{
  "Sunucu": ".\\SQLEXPRESS",
  "Hedef": "GoogleDrive",
  "GoogleDriveKlasoru": "",
  "SaklamaAdedi": 7,
  "Saat": "02:00"
}
```

- `GoogleDriveKlasoru` boş bırakılabilir (uygulama `My Drive` / `Drive'ım` yolunu arar).
- Bulamazsa örn. `G:\\My Drive` yazın.

3. `setup.bat` veya `Kurulum-Sunucu.bat` → UAC **Evet**.
4. **Test Yedeği Al**. ZIP’ler `...\SQLBackup\` altında; Drive istemcisi buluta senkronlar.

## Yerel / UNC (Drive yoksa)

```json
{
  "Sunucu": ".\\SQLEXPRESS",
  "Hedef": "Yerel",
  "YedekKlasoru": "C:\\SQLBackup",
  "SaklamaAdedi": 7,
  "Saat": "02:00"
}
```

Windows Server’da oturum kapalıysa **Yerel/UNC** tercih edin.

## E-posta (isteğe bağlı)

`Eposta.Acik: true` + SMTP alanları → başarıda “yedekleme başarılı”. Ayrıntı: güvenlik notları / e-posta dokümanı.

## Sunucu / Drive uyarıları

- Drive senkronu kullanıcı oturumu ister.
- WebView2 gerekebilir.
- Drive UNC üzerinde çalışmaz.
- Zamanlanmış görev: oturum açık (Interactive) veya NoLogon **aynı** kullanıcı (Drive klasörüne erişim için).
