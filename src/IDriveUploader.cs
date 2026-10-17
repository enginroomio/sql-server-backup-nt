namespace SqlBackupTool;

/// <summary>Drive upload surface used after local ZIP success. Production: <see cref="DriveApiClient"/>; tests: <see cref="FakeDriveUploader"/>.</summary>
public interface IDriveUploader : IDisposable
{
    string UploadZip(string localZipPath, string fileName);
    void EnforceRetention(string dbSafeName, int keep);
}

/// <summary>In-memory Drive stand-in — no Google credentials / network.</summary>
public sealed class FakeDriveUploader : IDriveUploader
{
    public List<(string Path, string Name)> Uploads { get; } = new();
    public List<(string DbSafe, int Keep)> RetentionCalls { get; } = new();
    public string NextFileId { get; set; } = "fake-file-id";
    public Exception? UploadFault { get; set; }

    public string UploadZip(string localZipPath, string fileName)
    {
        DriveApiClient.ValidateUploadArgs(localZipPath, fileName);
        if (UploadFault != null) throw UploadFault;
        Uploads.Add((localZipPath, fileName));
        return NextFileId;
    }

    public void EnforceRetention(string dbSafeName, int keep)
    {
        RetentionCalls.Add((dbSafeName, keep));
        // Mirror production: keep newest N by name (lexicographic timestamp suffix).
        var prefix = dbSafeName + "_";
        var matches = Uploads
            .Where(u => u.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && DriveApiClient.IsSafeZipFileName(u.Name))
            .OrderByDescending(u => u.Name, StringComparer.Ordinal)
            .Skip(Math.Max(0, keep))
            .ToList();
        foreach (var old in matches)
            Uploads.Remove(old);
    }

    public void Dispose() { }
}
