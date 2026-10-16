namespace Baccarat.Infrastructure.Persistence;

// Holds a single-writer lease for the window lifetime. All content is local;
// this store never persists authentication, network tokens or automation approvals.
public sealed class WorkspaceFileStore : IDisposable
{
    public string FilePath { get; }
    public string BackupPath => FilePath + ".bak";
    private readonly FileStream lease;
    private const int MaximumBytes = 32 * 1024 * 1024;

    public WorkspaceFileStore(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        lease = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public byte[]? Read(bool backup = false)
    {
        var path = backup ? BackupPath : FilePath;
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Workspace exceeds the 32 MiB limit.");
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (result.Length + count > MaximumBytes) throw new InvalidDataException("Workspace exceeds the 32 MiB limit.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    public void Save(byte[] data, bool recover = false)
    {
        if (data.Length > MaximumBytes) throw new InvalidDataException("Workspace exceeds the 32 MiB limit.");
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath))
            {
                if (recover)
                {
                    // Preserve rejected evidence and retain the last valid backup.
                    File.Copy(FilePath, FilePath + ".rejected-" + Guid.NewGuid().ToString("N"));
                    File.Move(temporary, FilePath, overwrite: true);
                }
                else File.Replace(temporary, FilePath, BackupPath);
            }
            else File.Move(temporary, FilePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary); // Only our uniquely named temporary file.
        }
    }

    public void Dispose() => lease.Dispose();
}
