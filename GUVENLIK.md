# Güvenlik özeti

## Saklanan sırlar (DPAPI)

| Alan | Ne |
|------|----|
| ZIP şifresi | `EncryptedPassword` |
| SQL şifresi | `SqlPasswordEnc` |
| SMTP / Google uygulama şifresi | `SmtpPasswordEnc` / `DriveAppPasswordEnc` |
| Drive OAuth yenileme jetonu | `DriveRefreshTokenEnc` |
| Service account JSON | `DriveApiKeyEnc` |

Düz metin `config.json` yok. Günlüklerde parola/jeton yok.

## Aze alanları

- **DriveMail** — e-posta (düz metin, kimlik)
- **DriveSifre** — yalnızca Gmail **uygulama şifresi** (onay maili). Google hesap şifresi saklanmaz / Drive API’ye gönderilmez.
- Drive dosya erişimi → tarayıcı OAuth (jeton DPAPI)

## Drive API

Yükleme başarısızsa o veritabanı **başarısız** sayılır (yerel ZIP kalabilir). Mail best-effort.

## Windows ile başlat

HKCU Run → `SQLYedeklemeAraci.exe --tray`. Günlük yedek: Görev Zamanlayıcı `--backup`.
