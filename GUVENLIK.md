# Güvenlik özeti — SQL Yedekleme Aracı

Kısa, son kullanıcı / yönetici odaklı özet.

## İyi durumdakiler

| Konu | Davranış |
|------|----------|
| Parolalar | ZIP, SQL ve **SMTP** parolaları `config.json` içinde **düz metin değil**; Windows DPAPI ile şifreli |
| Günlükler | Parola / bağlantı dizesi / SMTP credential yazılmaz (yalnızca host:port ve alıcı) |
| E-posta | Best-effort; gönderim hatası yedekleme sonucunu bozmaz |
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
- **Google Drive hedefi:** Yalnızca **hedef** (SQL kaynağın yerini almaz). Senkron istemci oturumu + WebView2 gerekir; OAuth secret uygulama içinde yok. Drive klasörüne agresif NTFS ACL uygulanmaz (senkron bozulmasın diye). Oturumsuz sunucularda yerel/UNC tercih edin.
- **Yedek dosyaları:** ZIP’ler hedef klasöre yazılır; ağ paylaşımında paylaşım ACL’lerini sıkı tutun. İsteğe bağlı AES-256 şifrelemeyi açın.
- **Zamanlanmış görev:** Interactive modda mevcut kullanıcı; SYSTEM en yüksek yetki — yalnızca SQL auth ile.

## Önerilen sunucu ayarı

1. Hedef: `C:\SQLBackup` veya UNC (Drive değil).
2. Şifreleme: açık (parolayı güvenli yerde saklayın).
3. SQL: Windows auth veya kısıtlı SQL login (`db_backupoperator`).
4. Mod: oturum hep açıksa Interactive; değilse NoLogon veya SYSTEM+SQL auth.
5. E-posta: `basit-ayarlar.json` → `Eposta.Acik=true`; SMTP parolasını uyguladıktan sonra JSON’dan silin.

## Test / CI

Gerçek Windows+SQL duman testi: `.github/workflows/windows-sql-smoke.yml` (ücretsiz `windows-latest` + LocalDB, **repo secret gerekmez**).  
SMTP smoke CI’da yok (ücretli/harici SMTP gerekir) — Windows’ta `Eposta.Acik=true` ile bir Test Yedeği alın.
