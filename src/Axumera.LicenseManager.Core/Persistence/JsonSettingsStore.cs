using System.Text.Json;

namespace Axumera.LicenseManager.Core.Persistence;

/// <summary>
/// App settings. Notably the ONLY reference to the signing key this tool
/// persists: the filesystem path to the operator's private_key.pem. The key
/// itself is never copied into app data, never embedded, never uploaded.
/// </summary>
public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _gate = new();

    public JsonSettingsStore(AppDataPaths paths) : this(paths.SettingsFile) { }

    public JsonSettingsStore(string filePath) => _filePath = filePath;

    public string PrivateKeyPath
    {
        get => Read("privateKeyPath") ?? string.Empty;
        set => Write("privateKeyPath", value);
    }

    public string LastSaveDirectory
    {
        get => Read("lastSaveDirectory") ?? string.Empty;
        set => Write("lastSaveDirectory", value);
    }

    private string? Read(string key)
    {
        lock (_gate)
        {
            var (content, _) = AtomicJson.ReadValidJson(_filePath);
            if (content is null)
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                {
                    return el.GetString();
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }
    }

    private void Write(string key, string value)
    {
        lock (_gate)
        {
            var current = new Dictionary<string, string>();
            var (content, _) = AtomicJson.ReadValidJson(_filePath);
            if (content is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            current[prop.Name] = prop.Value.GetString()!;
                        }
                    }
                }
                catch (JsonException)
                {
                    current.Clear();
                }
            }

            current[key] = value;

            var json = JsonSerializer.Serialize(current, Options);
            AtomicJson.Write(_filePath, json);
        }
    }
}