# Hızlı Başlangıç — Aze tarzı (basit tanımlama)

Aze Backup gibi: birkaç alan → Test → Kaydet → unut.

**Kaynak:** SQL Server (`BACKUP DATABASE`)  
**Hedef:** Google Drive  
**Onay:** yedek bitince e-posta (“yedekleme başarılı”)

---

## Ekrandaki alanlar (Basit Ayarlar)

| Alan | Ne yazılır |
|------|------------|
| **Google e-posta** | Drive / Gmail adresiniz |
| **Google uygulama şifresi** | Onay **maili** için (aşağıya bakın) — düz hesap şifresi değil |
| **Google Drive'ı Bağla** | Tarayıcı açılır; Google’a **bir kez** giriş (şifreyi Google sayfasında yazarsınız) |
| **SQL sunucu** | Örn. `.\SQLEXPRESS` |
| **SQL kullanıcı / şifre** | Boş bırakırsanız Windows kimliği |
| **Yedek saati** | Örn. `02:00` |
| **Saklama adedi** | Örn. `7` |
| **Windows ile başlat** | Oturum açılınca tepside çalışır |
| **Otomatik yedek** | Her gün seçilen saatte Görev Zamanlayıcı |

Düğmeler: **Test Et** → **Ayarları Kaydet**.

---

## 5 dakikalık kurulum (Windows Server)

1. `dist\SQLYedeklemeAraci.exe` olan klasörü sunucuya kopyalayın; `setup.bat` veya exe’u çalıştırın.
2. **Bir kez** (yönetici PC’de de olur): Google Cloud’da “Masaüstü uygulaması” OAuth istemcisi oluşturun → `client_secret.json` indirin → şuraya koyun:  
   `C:\ProgramData\SQLBackupTool\client_secret.json`  
   (örnek şablon: `client_secret.json.ornek`)
3. Google Hesabı → Güvenlik → **2 Adımlı Doğrulama** → **Uygulama şifreleri** → bir şifre üretin.
4. Uygulamada Basit Ayarlar:
   - Google e-posta + uygulama şifresi
   - **Google Drive'ı Bağla** (tarayıcı)
   - SQL sunucu (+ isteğe bağlı SQL kullanıcı/şifre)
   - Saat → **Test Et** → **Ayarları Kaydet**
5. Bitti. Drive’da `SQLBackup` klasörüne ZIP düşer; size onay maili gider.

### `basit-ayarlar.json` (aynı alanlar)

```json
{
  "DriveMail": "sizin@gmail.com",
  "DriveSifre": "xxxx xxxx xxxx xxxx",
  "Saat": "02:00",
  "SqlSunucu": ".\\SQLEXPRESS",
  "SqlKullanici": "",
  "SqlSifre": "",
  "SaklamaAdedi": 7,
  "WindowsIleBaslat": true,
  "Hedef": "GoogleDriveApi"
}
```

Sonra: `Uygula-Ayarlar.ps1`  
Not: Drive OAuth jetonu GUI’de “Bağla” ile alınır; JSON’daki `DriveSifre` yalnızca onay maili (SMTP) içindir.

---

## Yeniden başlatma / oturum

| Ne | Nasıl çalışır |
|----|----------------|
| Ayarlar | `%LOCALAPPDATA%\SQLBackupTool\config.json` — şifreler **DPAPI**, düz metin yok |
| Tepsi | “Windows ile başlat” → oturum açılınca `--tray` |
| Günlük yedek | Görev Zamanlayıcı `SQLBackupTool_Gunluk` → `exe --backup` |
| Drive yükleme | Kayıtlı **OAuth yenileme jetonu** ile (masaüstü Drive uygulaması **gerekmez**) |

Oturum hiç açılmayacak sunucu: Kaydet’ten sonra Ayarlar → Sunucu Modu → **Oturum açılmadan** (NoLogon) veya SYSTEM + SQL auth. OAuth jetonu aynı kullanıcı DPAPI’sinde olmalı (NoLogon = aynı Windows kullanıcısı).

---

## Google neden “düz şifre” kabul etmiyor?

Google, uygulamaların Drive’a e-posta+hesap şifresiyle girmesini kapattı.  
Aze’deki gibi alanlar durur; **şifre alanı = uygulama şifresi (mail)**, **Drive dosyası = bir kez tarayıcıda giriş (OAuth)**.

---

## Diğer hedefler (ileri)

| Hedef | Ne zaman |
|-------|----------|
| `GoogleDriveApi` | Varsayılan Aze yolu (oturumsuz yükleme) |
| `GoogleDrive` | Drive for Desktop senkron klasörü (oturum açık PC) |
| `Yerel` | `C:\SQLBackup` / UNC — CI ve kapalı ağ |

Güvenlik: `GUVENLIK.md`. E-posta ayrıntısı: store `sql-yedekleme-eposta.md`.
