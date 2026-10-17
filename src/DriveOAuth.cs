using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;

namespace SqlBackupTool;

/// <summary>
/// One-time browser OAuth (user types Google email/password on Google's page — Aze-like feel).
/// Refresh token stored via DPAPI for headless --backup after reboot.
/// </summary>
public static class DriveOAuth
{
    public static string DefaultClientSecretsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SQLBackupTool", "client_secret.json");

    public static string TokenStoreDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SQLBackupTool", "GoogleOAuth");

    public static string ResolveClientSecretsPath(AppConfig cfg) =>
        !string.IsNullOrWhiteSpace(cfg.DriveOAuthClientPath) && File.Exists(cfg.DriveOAuthClientPath)
            ? cfg.DriveOAuthClientPath
            : DefaultClientSecretsPath;

    /// <summary>Interactive: opens browser. Returns refresh token or throws with Turkish guidance.</summary>
    public static async Task<string> AuthorizeInteractiveAsync(AppConfig cfg, CancellationToken ct = default)
    {
        var path = ResolveClientSecretsPath(cfg);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "OAuth istemci dosyası yok: " + path +
                "\n\nKısa kurulum: Google Cloud'da bir kez 'Masaüstü uygulaması' OAuth istemcisi oluşturup client_secret.json olarak bu yola koyun (HIZLI_BASLANGIC.md).",
                path);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        var secrets = await GoogleClientSecrets.FromStreamAsync(stream, ct);
        Directory.CreateDirectory(TokenStoreDir);
        var cred = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            new[] { DriveService.Scope.DriveFile, DriveService.Scope.Drive },
            string.IsNullOrWhiteSpace(cfg.DriveMail) ? "user" : cfg.DriveMail.Trim(),
            ct,
            new FileDataStore(TokenStoreDir, true));

        // Some re-auth paths omit refresh_token; try a refresh once if missing.
        if (string.IsNullOrEmpty(cred.Token?.RefreshToken))
        {
            try { await cred.RefreshTokenAsync(ct); }
            catch { /* ignore — we'll fail below with a clear message */ }
        }
        var refresh = cred.Token?.RefreshToken;
        if (string.IsNullOrEmpty(refresh))
            throw new InvalidOperationException(
                "Google yenileme jetonu alınamadı. Google Cloud OAuth istemcisinde 'yenileme jetonu' / test kullanıcısı ayarlarını kontrol edin.");
        return refresh;
    }

    public static UserCredential? FromRefreshToken(AppConfig cfg)
    {
        var refresh = cfg.GetDriveRefreshToken();
        if (string.IsNullOrEmpty(refresh)) return null;
        var path = ResolveClientSecretsPath(cfg);
        if (!File.Exists(path))
            throw new FileNotFoundException("OAuth client_secret.json gerekli (jeton yenileme): " + path, path);
        var secrets = GoogleClientSecrets.FromStream(new FileStream(path, FileMode.Open, FileAccess.Read)).Secrets;
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = new[] { DriveService.Scope.DriveFile, DriveService.Scope.Drive }
        });
        return new UserCredential(flow, "user", new TokenResponse { RefreshToken = refresh });
    }
}
