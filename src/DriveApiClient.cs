using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;

namespace SqlBackupTool;

/// <summary>
/// Google Drive upload: user OAuth refresh token (Aze-like) or service-account JSON.
/// No Drive for Desktop required. Secrets never logged.
/// </summary>
public sealed class DriveApiClient : IDriveUploader
{
    static readonly Regex ZipNameRx = new(@"^[\w.\- ]+_\d{8}_\d{6}\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    const string DefaultFolderName = "SQLBackup";

    readonly DriveService _svc;
    readonly string _folderId;

    DriveApiClient(DriveService svc, string folderId)
    {
        _svc = svc;
        _folderId = folderId;
    }

    /// <summary>ZIP leaf names produced by the app (db_yyyyMMdd_HHmmss.zip).</summary>
    public static bool IsSafeZipFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && ZipNameRx.IsMatch(fileName);

    /// <summary>Shared preflight for real Drive and <see cref="FakeDriveUploader"/> (no network).</summary>
    public static void ValidateUploadArgs(string localZipPath, string fileName)
    {
        if (!File.Exists(localZipPath)) throw new FileNotFoundException("Yüklenecek ZIP yok: " + localZipPath);
        if (!IsSafeZipFileName(fileName)) throw new InvalidOperationException("Güvenli olmayan Drive dosya adı: " + fileName);
    }

    public static DriveApiClient Create(AppConfig cfg)
    {
        var initializer = new BaseClientService.Initializer { ApplicationName = "SQLYedeklemeAraci" };

        // Prefer user OAuth (Aze: browser login once) — works headless after reboot with refresh token.
        if (cfg.HasDriveUserOAuth)
        {
            var uc = DriveOAuth.FromRefreshToken(cfg)
                     ?? throw new InvalidOperationException("Drive OAuth jetonu çözülemedi.");
            initializer.HttpClientInitializer = uc;
        }
        else if (cfg.HasDriveServiceAccount)
        {
            var json = cfg.GetDriveApiKeyJson();
            if (string.IsNullOrEmpty(json) && File.Exists(cfg.DriveApiKeyPath))
                json = File.ReadAllText(cfg.DriveApiKeyPath);
            if (string.IsNullOrEmpty(json))
                throw new InvalidOperationException("Drive service account anahtarı yok.");
            var cred = GoogleCredential.FromJson(json).CreateScoped(DriveService.Scope.Drive);
            if (!string.IsNullOrWhiteSpace(cfg.DriveApiImpersonate))
                cred = cred.CreateWithUser(cfg.DriveApiImpersonate.Trim());
            initializer.HttpClientInitializer = cred;
        }
        else
            throw new InvalidOperationException(
                "Google Drive bağlı değil. Aze ekranında 'Google Drive'ı Bağla'ya basın (tarayıcıda bir kez giriş), veya AnahtarDosyasi kullanın.");

        var svc = new DriveService(initializer);
        var folderId = cfg.DriveApiFolderId;
        if (string.IsNullOrWhiteSpace(folderId))
            folderId = EnsureFolder(svc, DefaultFolderName);
        return new DriveApiClient(svc, folderId.Trim());
    }

    /// <summary>Find or create a folder named <paramref name="name"/> under My Drive root.</summary>
    public static string EnsureFolder(DriveService svc, string name)
    {
        var list = svc.Files.List();
        list.Q = $"mimeType='application/vnd.google-apps.folder' and name='{name.Replace("'", "\\'")}' and trashed=false and 'root' in parents";
        list.Fields = "files(id,name)";
        list.Spaces = "drive";
        var res = list.Execute();
        var existing = res.Files?.FirstOrDefault();
        if (existing?.Id != null) return existing.Id;

        var meta = new Google.Apis.Drive.v3.Data.File
        {
            Name = name,
            MimeType = "application/vnd.google-apps.folder"
        };
        var create = svc.Files.Create(meta);
        create.Fields = "id";
        var created = create.Execute();
        if (string.IsNullOrEmpty(created.Id)) throw new InvalidOperationException("Drive klasörü oluşturulamadı: " + name);
        Logger.Info($"Drive klasörü oluşturuldu: {name} ({created.Id})");
        return created.Id;
    }

    public string UploadZip(string localZipPath, string fileName)
    {
        ValidateUploadArgs(localZipPath, fileName);

        var meta = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = new List<string> { _folderId }
        };
        using var stream = new FileStream(localZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var req = _svc.Files.Create(meta, stream, "application/zip");
        req.Fields = "id,name,size";
        req.SupportsAllDrives = true;
        var progress = req.Upload();
        if (progress.Status != UploadStatus.Completed)
            throw new InvalidOperationException("Drive yükleme tamamlanmadı: " + (progress.Exception?.Message ?? progress.Status.ToString()));
        var id = req.ResponseBody?.Id;
        if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Drive yükleme yanıtında dosya kimliği yok.");
        Logger.Success($"Drive API'ye yüklendi: {fileName} (id={id})");
        return id;
    }

    public void EnforceRetention(string dbSafeName, int keep)
    {
        if (keep < 1) return;
        var prefix = dbSafeName + "_";
        string? page = null;
        var matches = new List<(string Id, string Name)>();
        do
        {
            var list = _svc.Files.List();
            list.Q = $"'{_folderId}' in parents and trashed=false and mimeType!='application/vnd.google-apps.folder'";
            list.Fields = "nextPageToken, files(id,name)";
            list.PageSize = 100;
            list.PageToken = page;
            list.SupportsAllDrives = true;
            list.IncludeItemsFromAllDrives = true;
            var res = list.Execute();
            foreach (var f in res.Files ?? Enumerable.Empty<Google.Apis.Drive.v3.Data.File>())
            {
                if (string.IsNullOrEmpty(f.Name) || string.IsNullOrEmpty(f.Id)) continue;
                if (!f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ZipNameRx.IsMatch(f.Name)) continue;
                matches.Add((f.Id, f.Name));
            }
            page = res.NextPageToken;
        } while (!string.IsNullOrEmpty(page));

        foreach (var old in matches.OrderByDescending(m => m.Name, StringComparer.Ordinal).Skip(keep))
        {
            try
            {
                var del = _svc.Files.Delete(old.Id);
                del.SupportsAllDrives = true;
                del.Execute();
                Logger.Info($"Drive API eski yedek silindi (saklama {keep}): {old.Name}");
            }
            catch (Exception ex) { Logger.Warn($"Drive API silinemedi {old.Name}: {ex.Message}"); }
        }
    }

    public void Dispose() => _svc.Dispose();
}
