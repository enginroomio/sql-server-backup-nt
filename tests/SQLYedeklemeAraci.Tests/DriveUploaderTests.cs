using Xunit;

namespace SqlBackupTool.Tests;

public class DriveUploaderTests
{
    [Theory]
    [InlineData("DemoDb_20261017_120000.zip", true)]
    [InlineData("My_DB_20260101_000000.zip", true)]
    [InlineData("../evil.zip", false)]
    [InlineData("no_timestamp.zip", false)]
    [InlineData("DemoDb_20261017_120000.bak", false)]
    [InlineData("", false)]
    public void IsSafeZipFileName_matches_app_pattern(string name, bool expected)
    {
        Assert.Equal(expected, DriveApiClient.IsSafeZipFileName(name));
    }

    [Fact]
    public void ValidateUploadArgs_rejects_missing_file()
    {
        Assert.Throws<FileNotFoundException>(() =>
            DriveApiClient.ValidateUploadArgs(Path.Combine(Path.GetTempPath(), "missing-ci-" + Guid.NewGuid() + ".zip"),
                "DemoDb_20261017_120000.zip"));
    }

    [Fact]
    public void ValidateUploadArgs_rejects_unsafe_name()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ci-" + Guid.NewGuid() + ".zip");
        File.WriteAllBytes(tmp, new byte[] { 1, 2, 3 });
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                DriveApiClient.ValidateUploadArgs(tmp, "bad name.zip"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void FakeDriveUploader_upload_and_retention_without_secrets()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ci-" + Guid.NewGuid() + ".zip");
        File.WriteAllBytes(tmp, new byte[] { 9 });
        try
        {
            using var drive = new FakeDriveUploader();
            // Simulate three successive backups; keep 2 newest by name.
            drive.UploadZip(tmp, "DemoDb_20261015_100000.zip");
            drive.UploadZip(tmp, "DemoDb_20261016_100000.zip");
            drive.UploadZip(tmp, "DemoDb_20261017_100000.zip");
            Assert.Equal(3, drive.Uploads.Count);

            drive.EnforceRetention("DemoDb", keep: 2);
            Assert.Equal(2, drive.Uploads.Count);
            Assert.DoesNotContain(drive.Uploads, u => u.Name.Contains("20261015"));
            Assert.Contains(drive.Uploads, u => u.Name.Contains("20261017"));
            Assert.Single(drive.RetentionCalls);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void FakeDriveUploader_propagates_upload_fault()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ci-" + Guid.NewGuid() + ".zip");
        File.WriteAllBytes(tmp, new byte[] { 1 });
        try
        {
            using var drive = new FakeDriveUploader { UploadFault = new IOException("quota") };
            Assert.Throws<IOException>(() => drive.UploadZip(tmp, "DemoDb_20261017_120000.zip"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ApplyDestination_GoogleDriveApi_accepts_key_path_shape()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid() + ".json");
        File.WriteAllText(tmp, """{"type":"service_account","private_key":"-----BEGIN PRIVATE KEY-----\nX\n-----END PRIVATE KEY-----\n","client_email":"ci@test.iam.gserviceaccount.com"}""");
        try
        {
            var cfg = new AppConfig();
            var err = cfg.ApplyDestination("GoogleDriveApi", null, @"C:\SQLBackup\ApiCache",
                apiKeyPath: tmp, apiFolderId: "folder123");
            Assert.Null(err);
            Assert.Equal(DestKind.GoogleDriveApi, cfg.Destination);
            Assert.Equal("folder123", cfg.DriveApiFolderId);
            Assert.True(cfg.RequiresDriveApiUpload);
            // DPAPI may fail under non-interactive CI user for Protect — GetDriveApiKeyJson can be null;
            // path must still be recorded for Create() fallback.
            Assert.Equal(tmp, cfg.DriveApiKeyPath);
        }
        finally { File.Delete(tmp); }
    }
}
