# SQL Yedekleme Aracı

Aze Backup tarzı **basit tanımlama**: Google e-posta + uygulama şifresi, SQL sunucu, saat → Test → Kaydet.

- **Kaynak:** SQL Server (`BACKUP DATABASE`) — MDF kopyası yok  
- **Hedef:** Google Drive (OAuth API; oturum kapalı sunucuda da çalışır)  
- **Onay maili:** “yedekleme başarılı”  
- **Sürekli çalışma:** tepsi (Windows ile başlat) + günlük Görev Zamanlayıcı  

## Başlangıç

→ **[HIZLI_BASLANGIC.md](HIZLI_BASLANGIC.md)** (Aze kontrol listesi)

Kısa: `setup.bat` → Basit Ayarlar ekranı → **Google Drive'ı Bağla** → Test → Kaydet.

`basit-ayarlar.json` alanları: `DriveMail`, `DriveSifre`, `Saat`, `SqlSunucu`, `SqlKullanici`, `SqlSifre`, `SaklamaAdedi`, `WindowsIleBaslat`.

## Mimari

```
SQL BACKUP → Staging (.bak) → ZIP → Google Drive API (SQLBackup klasörü)
                                   → SMTP onay maili (Gmail uygulama şifresi)
```

## Güvenlik

Şifreler / OAuth jetonu `config.json` içinde **DPAPI**. Günlüklerde token yok. Ayrıntı: `GUVENLIK.md`.

## Test

- Mac: `python3 tests/validate_package.py`  
- GHA: LocalDB + `Hedef=Yerel` (Drive secret yok)
