# Güvenlik özeti — SQL Yedekleme Aracı

Kısa, son kullanıcı / yönetici odaklı özet.

## İyi durumdakiler

| Konu | Davranış |
|------|----------|
| Parolalar | ZIP ve SQL parolaları `config.json` içinde **düz metin değil**; Windows DPAPI ile şifreli |
| Günlükler | Parola / bağlantı dizesi yazılmaz |
| SQL betiği | Veritabanı adları `QUOTENAME`; staging/ad allowlist + quote kaçışı |
| Silme | Yalnızca `{DB}_yyyyMMdd_HHmmss.zip`, yalnızca `SQLBackup` / `Staging` klasörlerinde |
| Kurulum betikleri | `Invoke-Expression` yok |
| SYSTEM modu | Ayrı config kopyası `ProgramData\...\Service\`; LocalMachine DPAPI (daha zayıf — `sa` kullanmayın) |

## Düzeltilen riskler (bu sürüm)

1. **Yüksek — Görev parolası komut satırında:** “Oturum açılmadan” modunda Windows parolası artık `schtasks /RP ...` ile process listesinde görünmez; kısa ömürlü, ACL’li geçici dosya + `Register-ScheduledTask` kullanılır.
2. **Yüksek — Yol / SQL enjeksiyon yüzeyi:** Staging yolu ve veritabanı adı allowlist; SQL’den dönen `.bak` yolu staging kökü dışında reddedilir; ZIP adı uygulama tarafından üretilir.
3. **Orta — config / yedek ACL:** `config.json` kullanıcı+SYSTEM; yerel `SQLBackup` klasörü kullanıcı+SYSTEM+Administrators (UNC paylaşımda NTFS paylaşım izinleri yöneticiye bırakılır).

## Bilinçli sınırlar (yapısal)

- **LocalMachine DPAPI (SYSTEM modu):** Aynı makinedeki yönetici süreçleri SQL/ZIP parolasını çözebilir → özel `db_backupoperator` hesabı kullanın, `sa` değil.
- **Google Drive hedefi:** Senkron istemci oturumu ve WebView2 gerekir; sunucularda yerel/UNC tercih edin.
- **Yedek dosyaları:** ZIP’ler hedef klasöre yazılır; ağ paylaşımında paylaşım ACL’lerini sıkı tutun. İsteğe bağlı AES-256 şifrelemeyi açın.
- **Zamanlanmış görev:** Interactive modda mevcut kullanıcı; SYSTEM en yüksek yetki — yalnızca SQL auth ile.

## Önerilen sunucu ayarı

1. Hedef: `C:\SQLBackup` veya UNC (Drive değil).
2. Şifreleme: açık (parolayı güvenli yerde saklayın).
3. SQL: Windows auth veya kısıtlı SQL login (`db_backupoperator`).
4. Mod: oturum hep açıksa Interactive; değilse NoLogon veya SYSTEM+SQL auth.

## Test / CI

Gerçek Windows+SQL duman testi: `.github/workflows/windows-sql-smoke.yml` (ücretsiz `windows-latest` + LocalDB, **repo secret gerekmez**).
