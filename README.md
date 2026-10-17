# SQL Yedekleme Aracı

SQL Server (Express ve üzeri) kullanıcı veritabanlarını **SQL `BACKUP DATABASE`** ile yedekler, sağlamlık kontrolü yapar, ZIP’ler ve hedefe yazar. Varsayılan hedef: **Google Drive** (senkron klasörü). Yerel/UNC yedek hedefi de desteklenir.

> **Kaynak = SQL Server · Hedef = Google Drive (veya yerel)**  
> Drive, SQL yedeğinin yerine geçmez. MDF kopyalama yoktur.

## Mimari

```
SQL Server (Express / LocalDB / full)
        │  BACKUP DATABASE → Staging (.bak) + VERIFYONLY
        ▼
   ZIP (+ isteğe bağlı AES-256)
        │
        ▼
  <Hedef>\SQLBackup\  ← Google Drive "My Drive" veya C:\SQLBackup / UNC
        │
        ▼  (Drive for Desktop senkronu — buluta yükleme istemci tarafında)
  Google Drive bulutu
```

Saklama (`SaklamaAdedi`) hedefteki `SQLBackup` klasöründe eski ZIP’leri siler (Drive senkronu buluttan da düşürür).

## Tak çalıştır — `basit-ayarlar.json`

```json
{
  "Sunucu": ".\\SQLEXPRESS",
  "Hedef": "GoogleDrive",
  "GoogleDriveKlasoru": "",
  "YedekKlasoru": "C:\\SQLBackup",
  "SaklamaAdedi": 7,
  "Saat": "02:00",
  "Eposta": { "Acik": false }
}
```

| Alan | Anlamı | Varsayılan |
|------|--------|------------|
| `Sunucu` | SQL örneği (kaynak) | `.\SQLEXPRESS` |
| `Hedef` | `GoogleDrive` veya `Yerel` | `GoogleDrive` |
| `GoogleDriveKlasoru` | Drive "My Drive" yolu; boş = otomatik bul | otomatik |
| `YedekKlasoru` | `Hedef=Yerel` iken yerel/UNC kök | `C:\SQLBackup` |
| `SaklamaAdedi` | DB başına tutulan ZIP sayısı | `7` |
| `Saat` | Günlük görev | `02:00` |
| `Eposta` | İsteğe bağlı SMTP (bkz. HIZLI_BASLANGIC) | kapalı |

1. [Google Drive for Desktop](https://www.google.com/drive/download/) kurun → **kişisel** hesapla giriş.
2. `basit-ayarlar.json` düzenleyin (`Hedef: "GoogleDrive"`).
3. **`setup.bat`** (masaüstü) veya **`Kurulum-Sunucu.bat`** → UAC **Evet**.
4. **Test Yedeği Al** → yeşil kutular. ZIP’ler `Drive'ım\SQLBackup\` (veya `My Drive\SQLBackup`) altında görünür; kısa süre sonra buluta senkron olur.

Elle: `Uygula-Ayarlar.ps1` / `--apply-basit` · `--backup`.

## Windows Server notları (Drive)

| | |
|--|--|
| Destek | Drive for Desktop, Server 2016+ (64 bit), **WebView2** gerekir |
| Oturum | Senkron **etkileşimli kullanıcı oturumunda** çalışır; oturum kapalı sunucuda Drive güvenilir değildir |
| UNC | Drive, ağ/UNC biriminde **çalışmaz** |
| Öneri | Oturumsuz sunucu → `Hedef: "Yerel"` veya UNC; Drive istiyorsanız oturumu açık tutun / NoLogon aynı kullanıcı |

API/OAuth yükleme **yok** — kasıtlı olarak senkron klasörü kullanılır (secret/OAuth istemez).

## Bu pakette

| Dosya | İşlev |
|-------|--------|
| `basit-ayarlar.json` | Hedef + SQL + e-posta |
| `src/BackupEngine.cs` | SQL BACKUP → ZIP → hedef |
| `src/MailNotifier.cs` | SMTP bildirim |
| `tests/` + `.github/workflows/` | Mac validate; GHA LocalDB smoke (**Drive atlanır**, `Hedef=Yerel`) |

Ayrıntı: **HIZLI_BASLANGIC.md**, **GUVENLIK.md**, **KURULUM_AYARLAR.md**.
