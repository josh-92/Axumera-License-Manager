using System.Text;

namespace Axumera.LicenseManager.Core.Persistence;

/// <summary>
/// Atomic, crash-safe JSON file writes: serialize to a temp file in the same
/// directory, flush to disk, then replace the destination. A torn/corrupt read
/// can therefore only ever be a pre-existing file, which the callers recover
/// from explicitly.
/// </summary>
public static class AtomicJson
{
    /// <summary>Writes <paramref name="content"/> atomically to <paramref name="path"/>.</summary>
    public static void Write(string path, string content)
    {
        string dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);

        string tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // File.Replace requires an existing destination and also preserves
                // the destination's ACL (critical for the restricted accounts.json).
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// Reads the file; returns <c>(null, null)</c> when the file does not exist,
    /// and backs the file up to <c>name.corrupt-&lt;timestamp&gt;</c> before
    /// returning <c>(null, backupPath)</c> when the content is invalid JSON. The
    /// app must not crash on either outcome.
    /// </summary>
    public static (string? Content, string? BackupPath) ReadValidJson(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return (null, null);
            }

            string content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
            {
                return (null, BackupTorn(path));
            }

            using var doc = System.Text.Json.JsonDocument.Parse(content);
            _ = doc.RootElement;
            return (content, null);
        }
        catch (IOException)
        {
            return (null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, null);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, BackupTorn(path));
        }
    }

    private static string? BackupTorn(string path)
    {
        string backup = $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            File.Move(path, backup);
            return backup;
        }
        catch (IOException)
        {
            return null;
        }
    }
}