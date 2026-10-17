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
