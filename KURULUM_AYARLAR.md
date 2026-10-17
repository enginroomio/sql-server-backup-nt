# SQL Yedekleme Aracı — Kurulum ve Ayarlar

## 2 Dakikada Kurulum (Standart Kullanıcı)

Teknik bilgi gerekmez. Sırayla yapın:

1. **Google Drive for Desktop'ı kurun:** https://www.google.com/drive/download/ adresinden indirin, kurun ve **kişisel** Google hesabınızla giriş yapın. Dosya Gezgini'nde "Google Drive" görünmeli.
2. **`setup.bat` dosyasına çift tıklayın.** Ekranda "Adım 1/5 … 5/5" mesajlarını göreceksiniz. Windows "Değişiklik yapılsın mı?" diye sorarsa **Evet** deyin (yalnızca bir kez sorulur).
3. Uygulama açılınca **Kurulum Sihirbazı** gelir:
   - **Adım 1 – Bağlantı:** "Test Et"e basın. "X veritabanı bulundu ✓" yazısını görmelisiniz.
   - **Adım 2 – Google Drive:** Klasör genelde otomatik bulunur (yeşil ✓). Bulunmazsa "Gözat" ile "Drive'ım / My Drive" klasörünü seçin.
   - **Adım 3 – Bitir:** Özeti kontrol edin.
4. **"Test Yedeği Al"** düğmesine basın ve "✓ Yedekleme başarılı" yazısını bekleyin.
5. **"Kurulumu Tamamla ✓"** düğmesine basın. Bitti! Her gece 02:00'de yedek otomatik alınır.

**Günlük kullanım:** Uygulamanın üstündeki 4 kutucuk yeşilse her şey yolunda. Kırmızı bir kutunun üzerine fareyle gelin, ne yapmanız gerektiği yazar. Ayarlar → **Hızlı Kontrol** her şeyi tek tıkla denetler.

**İsteğe bağlı – Şifreleme (önerilen):** Ayarlar → ▶ Gelişmiş Ayarlar → "Yedeklerimi şifrele" → en az 6 karakterlik parola → Ayarları Kaydet. **Parolayı bir yere not edin**; parola olmadan yedek açılamaz.

**Geri yükleme:** Drive'daki `.zip` dosyasını 7-Zip ile açın (şifreliyse parolayı girin), içindeki `.bak` dosyasını SSMS ile geri yükleyin (aşağıda "Geri Yükleme").

---

# Teknik Detaylar / Yönetici Bölümü

## Windows Server Ortamında Kurulum (Yönetici)

### 1) Minimum gereksinimler
- **Windows Server 2016 veya üzeri** (64 bit). Arayüz için **Desktop Experience (GUI)** önerilir. **Server Core**'da arayüz açılmaz; `setup.ps1` yine çalışır ve yedekleme zamanlanmış görevden `SQLYedeklemeAraci.exe --backup` ile arayüzsüz yapılır (ayarlar bir GUI makinede hazırlanıp `config.json` kopyalanabilir).
- **SQL Server:** Express veya herhangi bir sürüm. Kurulu örnekler otomatik bulunur (kayıt defteri `HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL` + `MSSQL$…`/`MSSQLSERVER` hizmetleri). `.\SQLEXPRESS` varsa o seçilir; varsayılan örnek `(local)` olarak görünür.
- **.NET kurulumu gerekmez** (uygulama self-contained tek exe).

### 2) Google Drive mı, yerel/ağ klasörü mü?
| | Google Drive (senkron klasörü) | Yerel / Ağ klasörü |
|---|---|---|
| Destek | Drive for Desktop **Windows Server 2016+** (64 bit) destekler | Her Windows sürümü |
| Gereksinim | **Microsoft WebView2** (yükleyici otomatik kurmaya çalışır), kişisel hesapla giriş | Yazma izni |
| Ağ yolu (UNC/SMB) | **Desteklenmez** (Drive, ağ/UNC birimlerinde çalışmaz) | **Desteklenir** (`\\sunucu\paylasim\...`) |
| Oturum | Senkron uygulaması kullanıcı oturumunda çalışır; sorunsuz senkron için **etkileşimli oturum** gerekir. Sunucularda kararlılık sorunları bildirilmiştir | Oturum gerekmez |
| Öneri | Masaüstü / sürekli oturum açık sunucu | **Kalıcı oturumu olmayan sunucularda önerilir** |

Uygulamada: Ayarlar → **Yedek hedefi** → "Yerel / Ağ klasörü (Windows Server)". Kaydederken klasöre küçük bir dosya yazılıp silinerek **yazma testi** yapılır; başarısızsa kırmızı hata gösterilir. Yedekler `<klasör>\SQLBackup\` içine aynı adlandırma, ZIP, doğrulama ve 7 günlük saklama kuralıyla yazılır.
> SYSTEM modunda ağ paylaşımına **bilgisayar hesabı** (`ALANADI\SUNUCU$`) ile erişilir; paylaşım ve NTFS izinlerini bu hesaba verin.

### 3) Zamanlanmış görev modları (Ayarlar / Sihirbaz → "Sunucu Modu")
| Mod | Ne zaman? | Yönetici | Kimlik | DPAPI kapsamı |
|---|---|---|---|---|
| **Oturum açıkken çalıştır (varsayılan)** | Masaüstü, oturum hep açık | Hayır | Mevcut kullanıcı | CurrentUser |
| **Oturum açılmadan çalıştır** | Sunucu; oturum kapalıyken de çalışsın | **Evet** (UAC) | **Aynı** Windows kullanıcısı (`/RU <kullanıcı> /RP <parola> /RL LIMITED`) | CurrentUser (aynı kullanıcı olduğu için çalışır) |
| **SYSTEM hesabıyla çalıştır** | Kullanıcı parolası kaydetmeden; yalnızca SQL oturum bilgisiyle | **Evet** (UAC) | `NT AUTHORITY\SYSTEM` (`/RU SYSTEM`) | **LocalMachine** |

- Windows parolası yalnızca bir kez maskeli kutuda istenir ve doğrudan Görev Zamanlayıcı'ya iletilir; **uygulama saklamaz ve günlüğe yazmaz**. (Not: yönetici onayıyla çalıştırılan `schtasks` komut satırı, çalıştığı birkaç saniye boyunca yöneticilerce görülebilir.)
- Yönetici olmayan kullanıcıya açık Türkçe mesaj gösterilir ve "Oturum açıkken çalıştır" modunda kalma seçeneği sunulur.
- SYSTEM modunda ayarların bir kopyası `C:\ProgramData\SQLBackupTool\Service\config.json`'a yazılır (izin: kullanıcı + SYSTEM + Administrators); günlükler `C:\ProgramData\SQLBackupTool\logs\` altındadır ve Olay Görüntüleyicisi'nde de görünür. Bildirim balonu gösterilmez.
- Mod değiştirildiğinde kayıtlı parolalar (ZIP ve SQL) yeni DPAPI kapsamıyla **otomatik yeniden şifrelenir**.
- Görev durumu satırı: Durum, **Son çalıştırma → sonuç (0 = başarılı)**, sonraki çalışma ve hesap.
- **Olay Günlüğü:** `--backup` her çalıştırmada Application günlüğüne bir kayıt yazar (Kaynak `SQLYedeklemeAraci`; Olay 1000 = Bilgi "SQL yedekleme başarılı: N veritabanı", 1001 = Hata + veritabanı bazında hatalar). Kaynak `setup.bat` tarafından oluşturulur; yoksa sessizce yalnızca dosya günlüğü kullanılır.

**LocalMachine kapsamının bedeli:** LocalMachine DPAPI ile şifrelenen SQL parolasını o makinede **yönetici olarak çalışan herhangi bir işlem** çözebilir (CurrentUser'dan zayıftır). Bu yüzden SYSTEM modunda **`sa` kullanmayın**; yalnızca `db_backupoperator` yetkili özel bir SQL kullanıcısı oluşturun (aşağıda).

### 4) SQL Server izinleri (sunucu senaryosu)
- `BACKUP DATABASE` için her veritabanında **`db_backupoperator` yeterlidir; sysadmin gerekmez.**
- Windows hesabı (mod a/b) veya SQL oturumu (mod c, Gelişmiş Ayarlar → "SQL Server oturum açma bilgisi kullan") kullanılabilir. SQL oturumu için SQL Server'da **Karma kimlik doğrulama (Mixed Mode)** açık olmalıdır.
- Ek not: `RESTORE VERIFYONLY` bazı sürümlerde `dbcreator` sunucu rolü isteyebilir; doğrulama hata verirse bu rolü ekleyin.

Özel yedek kullanıcısı (tüm kullanıcı veritabanları için):
```sql
USE [master];
CREATE LOGIN [yedek_kullanici] WITH PASSWORD = N'<GUCLEU_PAROLA_BURAYA>', CHECK_POLICY = ON;
-- Windows hesabı için: CREATE LOGIN [ALANADI\yedek_hesabi] FROM WINDOWS;
DECLARE @db sysname, @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0;
OPEN c; FETCH NEXT FROM c INTO @db;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'USE ' + QUOTENAME(@db) + N';
      IF USER_ID(N''yedek_kullanici'') IS NULL CREATE USER [yedek_kullanici] FOR LOGIN [yedek_kullanici];
      ALTER ROLE db_backupoperator ADD MEMBER [yedek_kullanici];';
    EXEC sp_executesql @sql;
    FETCH NEXT FROM c INTO @db;
END
CLOSE c; DEALLOCATE c;
```
Yeni veritabanı eklendiğinde betiği tekrar çalıştırın.

### 5) Yedek verilerinin korunması
- Sunucularda **şifreleme (AES-256) önerilir**: Gelişmiş Ayarlar → "Yedeklerimi şifrele". Parola DPAPI ile korunur (mod c'de LocalMachine).
- `.bak` geçici dizini `C:\ProgramData\SQLBackupTool\Staging` yalnızca kullanıcı + SYSTEM + SQL hizmet hesabına açıktır; `.bak` ZIP doğrulandıktan hemen sonra silinir.
- Temizlik yalnızca aracın kendi dosyalarını (`{DB}_yyyyMMdd_HHmmss.zip`) ve yalnızca kendi klasörlerinde (`<hedef>\SQLBackup` ve Staging, alt klasöre inmeden) siler.

### 6) WebView2
Google Drive for Desktop kurulumu WebView2 eksik olduğu için başarısız olursa WebView2 Runtime'ı elle kurun: https://developer.microsoft.com/en-us/microsoft-edge/webview2/ — ardından Drive kurulumunu tekrarlayın.


## Güvenlik Önlemleri

- **Önce doğrula, sonra sil:** `.bak` dosyası ancak ZIP yeniden açılıp tamamen çözüldükten, boyutu ve CRC32 değeri orijinalle eşleştikten sonra silinir. Doğrulama başarısız olursa bozuk ZIP silinir, `.bak` **korunur** ve günlükte dosya yolu yazılır; diğer veritabanları etkilenmez.
- **Şifreleme (isteğe bağlı):** ZIP girdisi **AES-256** ile şifrelenir (SharpZipLib 1.4.2; SharpCompress şifreli ZIP yazamadığı için seçilmedi). Parola `config.json` içinde **asla düz metin değildir**: Windows DPAPI (CurrentUser) ile şifrelenir; yalnızca aynı Windows kullanıcısı çözebilir. Şifreli ZIP'ler 7-Zip/WinRAR ile açılır (Windows'un yerleşik ZIP açıcısı AES desteklemez).
- **Silme sınırları:** Saklama temizliği yalnızca `{Veritabanı}_{yyyyMMdd_HHmmss}.zip` adına tam uyan dosyaları, yalnızca `Drive'ım\SQLBackup` ve geçici klasörün **doğrudan içinde** (alt klasörlere inmeden) siler. Klasör adı `SQLBackup`/`Staging` değilse veya sürücü köküyse temizlik yapılmaz. Silinemeyen dosya günlüğe HATA olarak yazılır. Yeni bir doğrulanmış yedek oluşmadan eski yedek silinmez.
- **Geçici klasör:** `.bak` dosyaları şifresiz ve hassas olduğu için yalnızca kısa süre burada durur ve doğrulamadan hemen sonra silinir. Klasör izinleri yalnızca sizin kullanıcınız + SYSTEM + SQL Server hizmet hesabıyla sınırlandırılır (SQL Server `.bak` dosyasını kendi hizmet hesabıyla yazdığı için bu hesap zorunludur).
- **Manifest:** Her doğrulanmış yedek için `C:\ProgramData\SQLBackupTool\Staging\manifest.csv` dosyasına bir satır eklenir (zaman, veritabanı, .bak boyutu, zip adı, verified=OK, şifreleme). Son satırlar Günlükler sekmesinde görünür.
- **Anlaşılır hatalar:** Sık görülen SQL hataları (18456 oturum reddi, 4060 veritabanı açılamadı, 2/53 sunucuya ulaşılamadı, 229/262 yetki yok, disk dolu, 1844 sıkıştırma desteklenmiyor → otomatik sıkıştırmasız yedek) kısa Türkçe açıklama + önerilen çözümle günlükte ve bildirimde gösterilir.
- **Yönetici hakkı:** Normal kullanım yönetici gerektirmez. Yalnızca `setup.bat` geçici klasör izinleri için **bir kez** UAC onayı ister. Zamanlanmış görev kullanıcı adına, yöneticisiz kaydedilir.


## 1. Kesinleşmiş Ayarlar

| Ayar | Değer |
|---|---|
| SQL Server örneği | `.\SQLEXPRESS` (otomatik algılanır; Windows Kimlik Doğrulaması, isteğe bağlı SQL oturumu) |
| Kapsam | Tüm **kullanıcı** veritabanları (master, model, msdb, tempdb hariç; çevrimdışı / salt okunur / snapshot olanlar atlanır) |
| Hedef | Kişisel Google Drive (varsayılan) veya Yerel/Ağ klasörü (UNC) → → `Drive'ım\SQLBackup` klasörü (Google Drive for Desktop ile senkron) |
| Sıklık | Her gün **02:00** (Windows Görev Zamanlayıcı, görev adı `SQLBackupTool_Gunluk`) + uygulamada "Şimdi Yedekle" |
| Saklama | Veritabanı başına son **7** yedek; eskiler Drive'dan ve yerelden otomatik silinir |
| Sıkıştırma | `BACKUP ... WITH COMPRESSION` (destekleniyorsa) + `.zip` (Optimal) |
| Doğrulama | `CHECKSUM` + `RESTORE VERIFYONLY` + ZIP içerik/boyut kontrolü |
| Şifreleme | İsteğe bağlı AES-256 ZIP (varsayılan kapalı, önerilir); parola DPAPI ile korunur |
| Bildirim | Başarı ve hatada Windows tepsi bildirimi, uygulama içi günlük görüntüleyici |
| Ayar dosyası | `%LOCALAPPDATA%\SQLBackupTool\config.json` (güncellemelerde korunur) |
| Günlükler | `%LOCALAPPDATA%\SQLBackupTool\logs\SQLBackup_yyyyMMdd.log` (30 gün tutulur) |
| Geçici klasör | `C:\ProgramData\SQLBackupTool\Staging` |
| Uygulama | `%LOCALAPPDATA%\Programs\SQLBackupTool\SQLYedeklemeAraci.exe` |

> **Not (SQL Server Express):** Express sürümü yerel yedek sıkıştırmasını desteklemez (hata 1844). Betik bu durumda otomatik olarak sıkıştırmasız yedek alır; boyut küçültme ZIP adımıyla sağlanır.

> **Not (Geçici klasör):** `BACKUP TO DISK` komutu dosyayı sizin kullanıcınızla değil, **SQL Server hizmet hesabıyla** (`NT Service\MSSQL$SQLEXPRESS`) yazar. Bu hesap `%LOCALAPPDATA%` altına yazamadığı için geçici klasör `C:\ProgramData\SQLBackupTool\Staging` olarak seçildi ve kurulum bu klasöre gerekli izni verir.

## 2. Kurulum

1. [Google Drive for Desktop](https://www.google.com/drive/download/) kurun ve **kişisel** Google hesabınızla oturum açın.
2. Proje klasöründeki **`setup.bat`** dosyasına çift tıklayın. Kurulum otomatik olarak:
   - Geçici klasörü oluşturur ve SQL hizmet hesabına izin verir (tek seferlik UAC onayı),
   - `dist\SQLYedeklemeAraci.exe` yoksa .NET 8 SDK'yı winget ile kurup uygulamayı derler,
   - Uygulamayı `%LOCALAPPDATA%\Programs\SQLBackupTool\` içine kopyalar,
   - Masaüstüne **"SQL Yedekleme Aracı"** kısayolunu oluşturur,
   - Günlük 02:00 zamanlanmış görevini kaydeder,
   - Uygulamayı ilk kurulum sihirbazıyla açar.
3. Sihirbazda: **Bağlantıyı Test Et → Drive klasörünü seç → Zamanlanmış Görevi Kaydet → (isteğe bağlı) Test Yedeği Al → Kurulumu Tamamla**.

Kurulum tekrar çalıştırılabilir; mevcut ayarlar korunur.

Zamanlanmış görevin çalıştırdığı komut:
```
schtasks /Create /TN "SQLBackupTool_Gunluk" /TR "\"%LOCALAPPDATA%\Programs\SQLBackupTool\SQLYedeklemeAraci.exe\" --backup" /SC DAILY /ST 02:00 /F
```
Elle yedek: `SQLYedeklemeAraci.exe --backup` (çıkış kodu 0 = başarılı, 1 = en az bir veritabanı başarısız).

## 3. Yedekleme Betiği (backup.master.sql)

Bu betik uygulamanın içine gömülüdür (Ayarlar → "SQL Betiğini Dışa Aktar").

```sql
/* =====================================================================
   SUNUCU / SÜRÜM NOTLARI
   * Her SQL Server sürümünde çalışır (Express, Web, Standard, Enterprise, Developer).
   * WITH COMPRESSION Express'te desteklenmez (hata 1844); bu durumda betik
     otomatik olarak sıkıştırmasız yedeğe geçer (uygulama ayrıca ZIP'ler).
   * Örnekten bağımsızdır: varsayılan örnek (MSSQLSERVER / "(local)") veya
     adlandırılmış örnek (.\SQLEXPRESS, .\DIGER) için aynı betik kullanılır.
   * Elle çalıştırma alternatifi:
       sqlcmd -S .\SQLEXPRESS -E -i backup.master.sql
     (SQL oturumu için -E yerine: -U yedek_kullanici -P <parola>)
   ===================================================================== */
/* =====================================================================
   SQL Yedekleme Aracı - Ana Yedekleme Betiği (backup.master.sql)
   ---------------------------------------------------------------------
   AYARLAR (uygulama bu değerleri otomatik doldurur):
     $(StagingDir) : .bak dosyalarının geçici olarak yazılacağı klasör.
                     SQL Server HİZMET HESABININ (NT Service\MSSQL$SQLEXPRESS)
                     bu klasöre yazma izni olmalıdır.
                     Varsayılan: C:\ProgramData\SQLBackupTool\Staging
     $(Timestamp)  : Dosya adı zaman damgası (yyyyMMdd_HHmmss)
     $(OnlyDb)     : Boş bırakılırsa TÜM kullanıcı veritabanları yedeklenir;
                     bir ad verilirse yalnızca o veritabanı yedeklenir.

   Elle çalıştırma örneği (sqlcmd):
     sqlcmd -S .\SQLEXPRESS -E -i backup.master.sql ^
       -v StagingDir="C:\ProgramData\SQLBackupTool\Staging" Timestamp="20261014_020000" OnlyDb=""

   DAVRANIŞ:
     * Sistem veritabanları (master, model, msdb, tempdb) HARİÇ tutulur.
     * Çevrimdışı (OFFLINE), salt okunur (READ_ONLY) ve anlık görüntü
       (snapshot) veritabanları atlanır ve sonuçta "ATLANDI" olarak raporlanır.
     * Her veritabanı WITH COMPRESSION, INIT, CHECKSUM ile yedeklenir.
       SQL Server Express sıkıştırmayı desteklemiyorsa (hata 1844)
       sıkıştırmasız tekrar denenir (uygulama ardından ZIP ile sıkıştırır).
     * Her yedek RESTORE VERIFYONLY ... WITH CHECKSUM ile doğrulanır
       ("güvenli yedek" garantisi).
     * Bir veritabanındaki hata diğerlerini durdurmaz.
     * Sonuç tablosu döner: DbName, FilePath, Status (BASARILI/HATA/ATLANDI), Message
   ===================================================================== */
SET NOCOUNT ON;

DECLARE @StagingDir NVARCHAR(400) = N'$(StagingDir)';
DECLARE @Timestamp  NVARCHAR(20)  = N'$(Timestamp)';
DECLARE @OnlyDb     SYSNAME       = NULLIF(N'$(OnlyDb)', N'');

IF RIGHT(@StagingDir, 1) = N'\' SET @StagingDir = LEFT(@StagingDir, LEN(@StagingDir) - 1);

DECLARE @Results TABLE (DbName SYSNAME, FilePath NVARCHAR(600) NULL, Status NVARCHAR(20), Message NVARCHAR(4000) NULL);

-- Atlanacak veritabanlarını raporla (çevrimdışı / salt okunur / snapshot)
INSERT INTO @Results (DbName, FilePath, Status, Message)
SELECT d.name, NULL, N'ATLANDI',
       CASE WHEN d.source_database_id IS NOT NULL THEN N'Anlık görüntü (snapshot) veritabanı'
            WHEN d.state <> 0 THEN N'Veritabanı çevrimiçi değil: ' + d.state_desc
            ELSE N'Veritabanı salt okunur' END
FROM sys.databases d
WHERE d.database_id > 4
  AND (@OnlyDb IS NULL OR d.name = @OnlyDb)
  AND (d.state <> 0 OR d.is_read_only = 1 OR d.source_database_id IS NOT NULL);

DECLARE @db SYSNAME, @file NVARCHAR(600), @sql NVARCHAR(MAX), @err NVARCHAR(4000);

DECLARE db_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT d.name FROM sys.databases d
    WHERE d.database_id > 4            -- sistem veritabanları hariç
      AND d.state = 0                  -- yalnızca ONLINE
      AND d.is_read_only = 0           -- salt okunur olanlar hariç
      AND d.source_database_id IS NULL -- snapshot hariç
      AND (@OnlyDb IS NULL OR d.name = @OnlyDb)
    ORDER BY d.name;

OPEN db_cursor;
FETCH NEXT FROM db_cursor INTO @db;
WHILE @@FETCH_STATUS = 0
BEGIN
    -- Dosya adında geçersiz karakterleri temizle
    SET @file = @StagingDir + N'\' +
        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@db,
            N'\',N'_'),N'/',N'_'),N':',N'_'),N'*',N'_'),N'?',N'_'),N'"',N'_'),N'<',N'_'),N'>',N'_'),N'|',N'_')
        + N'_' + @Timestamp + N'.bak';
    BEGIN TRY
        BEGIN TRY
            SET @sql = N'BACKUP DATABASE ' + QUOTENAME(@db) + N' TO DISK = @f WITH COMPRESSION, INIT, CHECKSUM, NAME = @n;';
            EXEC sp_executesql @sql, N'@f NVARCHAR(600), @n NVARCHAR(200)', @f = @file, @n = @db;
        END TRY
        BEGIN CATCH
            IF ERROR_NUMBER() <> 1844 THROW;  -- 1844: bu sürüm sıkıştırmayı desteklemiyor (Express)
            SET @sql = N'BACKUP DATABASE ' + QUOTENAME(@db) + N' TO DISK = @f WITH INIT, CHECKSUM, NAME = @n;';
            EXEC sp_executesql @sql, N'@f NVARCHAR(600), @n NVARCHAR(200)', @f = @file, @n = @db;
        END CATCH;

        -- Doğrulama: yedek dosyası okunabilir ve sağlam mı?
        RESTORE VERIFYONLY FROM DISK = @file WITH CHECKSUM;

        INSERT INTO @Results VALUES (@db, @file, N'BASARILI', N'Yedek alındı ve doğrulandı');
    END TRY
    BEGIN CATCH
        SET @err = N'Hata ' + CAST(ERROR_NUMBER() AS NVARCHAR(10)) + N': ' + ERROR_MESSAGE();
        INSERT INTO @Results VALUES (@db, @file, N'HATA', @err);
    END CATCH;
    FETCH NEXT FROM db_cursor INTO @db;
END
CLOSE db_cursor;
DEALLOCATE db_cursor;

SELECT DbName, FilePath, Status, Message FROM @Results ORDER BY DbName;
```

## 4. GEREKLİ İZİNLER

### 4.1 SQL Server izinleri
Windows Kimlik Doğrulaması kullanıldığı için yedeği alan **Windows kullanıcınızın** SQL Server'da yetkisi olmalıdır:
- `sysadmin` sunucu rolü (önerilen, tüm veritabanları), **veya**
- Her veritabanında `db_owner` ya da `db_backupoperator` rolü (asgari yetki).

SQL Server Express'i kuran kullanıcı genellikle zaten `sysadmin`'dir. Uygulamadaki "Bağlantıyı Test Et" düğmesi bunu gösterir.

Mevcut yetkiyi kontrol etme (SSMS veya `sqlcmd -S .\SQLEXPRESS -E`):
```sql
SELECT SUSER_SNAME() AS Oturum, IS_SRVROLEMEMBER('sysadmin') AS SysadminMi;
-- Belirli bir veritabanında:
USE [VeritabaniAdi];
SELECT IS_MEMBER('db_owner') AS DbOwner, IS_MEMBER('db_backupoperator') AS BackupOperator;
```

Yetki verme (bir sysadmin hesabıyla çalıştırın; `MAKINE\Kullanici` yerine kendi bilgisayar\kullanıcı adınızı yazın):
```sql
USE [master];
CREATE LOGIN [MAKINE\Kullanici] FROM WINDOWS;
ALTER SERVER ROLE sysadmin ADD MEMBER [MAKINE\Kullanici];
```
Asgari yetki (her veritabanı için):
```sql
USE [VeritabaniAdi];
CREATE USER [MAKINE\Kullanici] FOR LOGIN [MAKINE\Kullanici];
EXEC sp_addrolemember 'db_backupoperator', [MAKINE\Kullanici];
```
Not: Asgari yetkide `RESTORE VERIFYONLY` için ayrıca sunucu düzeyinde `dbcreator` rolü gerekebilir; en sorunsuz seçenek `sysadmin`'dir.

### 4.2 Dosya sistemi izinleri
- `C:\ProgramData\SQLBackupTool\Staging`: SQL Server hizmet hesabı (`NT Service\MSSQL$SQLEXPRESS`) için **Değiştirme** izni gerekir; diğer kullanıcıların erişimi kaldırılır (yalnızca siz + SYSTEM + SQL hizmeti). `setup.bat` bunu ayarlar. Elle ayarlamak için (yönetici komut istemi):
  ```
  icacls "C:\ProgramData\SQLBackupTool\Staging" /inheritance:r /grant:r "%USERDOMAIN%\%USERNAME%:(OI)(CI)F" /grant:r "*S-1-5-18:(OI)(CI)F" /grant:r "NT Service\MSSQL$SQLEXPRESS:(OI)(CI)M"
  ```
- `%LOCALAPPDATA%\SQLBackupTool` (ayarlar, günlükler) kullanıcının kendi klasörüdür, ek izin gerekmez.
- Zamanlanmış görev farklı bir hesapla çalıştırılırsa o hesabın da bu klasörlere ve Google Drive klasörüne erişimi olmalıdır.

### 4.3 Windows Görev Zamanlayıcı
- Görev mevcut kullanıcı adına oluşturulur; **yönetici yetkisi gerekmez**.
- Varsayılan mod "Yalnızca kullanıcı oturum açtığında çalıştır"dır: saat 02:00'de **bilgisayar açık ve kullanıcı oturumu açık** olmalıdır (ekran kilitli olabilir). Kaçırılan çalışmalar için Görev Zamanlayıcı'da görevi açıp *Ayarlar → "Zamanlanmış başlatma kaçırılırsa görevi en kısa sürede çalıştır"* seçeneğini işaretleyebilirsiniz.
- İsteğe bağlı "Kullanıcı oturum açmış olsun veya olmasın çalıştır" modu için parolanızın Windows'ta saklanması gerekir:
  `schtasks /Change /TN "SQLBackupTool_Gunluk" /RU "MAKINE\Kullanici" /RP *`
  Bu modda bildirimler görünmez ve Google Drive for Desktop oturum kapalıyken senkronize etmez; dosyalar oturum açıldığında yüklenir.

### 4.4 Google Drive for Desktop
- İndirme: https://www.google.com/drive/download/ (ücretsiz).
- **Kişisel** Google hesabınızla oturum açmış olmalıdır. Uygulama "Drive'ım / My Drive" klasörünü otomatik bulur (`G:\My Drive`, `%USERPROFILE%\My Drive` vb.); bulunamazsa Ayarlar'dan elle seçin.
- Yedekler `Drive'ım\SQLBackup` klasörüne yazılır ve Drive tarafından buluta yüklenir. Drive kotanızın yeterli olduğundan emin olun.

### 4.5 İnternet ve Windows Defender
- Uygulama doğrudan internete bağlanmaz ve OAuth/API anahtarı kullanmaz; yükleme Google Drive for Desktop tarafından yapılır.
- Yedek dosyaları `%LOCALAPPDATA%`, `C:\ProgramData\SQLBackupTool` ve Drive klasöründeki normal kullanıcı dosyalarıdır. Defender genellikle uyarı vermez; imzasız exe ilk açılışta SmartScreen uyarısı gösterebilir ("Ek bilgi → Yine de çalıştır").

## 5. Geri Yükleme (Restore)
1. Drive'daki `.zip` dosyasını çıkarın (`VeritabaniAdi_yyyyMMdd_HHmmss.bak`).
2. SSMS: *Databases → Restore Database → Device* veya:
```sql
RESTORE DATABASE [VeritabaniAdi] FROM DISK = N'C:\Yol\VeritabaniAdi_20261014_020000.bak' WITH REPLACE, RECOVERY;
```
