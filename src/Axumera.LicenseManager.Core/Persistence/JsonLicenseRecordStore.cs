using System.Text.Json;
using System.Text.Json.Serialization;
using Axumera.LicenseManager.Core.Models;

namespace Axumera.LicenseManager.Core.Persistence;

/// <summary>
/// Persistent local license ledger (records only — never key material, never
/// the signed artifact). Written atomically; corrupt files are backed up and
/// the store recovers to an empty list instead of crashing.
/// </summary>
public sealed class JsonLicenseRecordStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly object _gate = new();

    public JsonLicenseRecordStore(AppDataPaths paths) : this(paths.LicensesFile) { }

    public JsonLicenseRecordStore(string filePath) => _filePath = filePath;

    public IReadOnlyList<LicenseRecord> Load()
    {
        lock (_gate)
        {
            var (content, _) = AtomicJson.ReadValidJson(_filePath);
            if (content is null)
            {
                return Array.Empty<LicenseRecord>();
            }

            try
            {
                var doc = JsonDocument.Parse(content);
                using (doc)
                {
                    if (!doc.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
                    {
                        return Array.Empty<LicenseRecord>();
                    }

                    var list = new List<LicenseRecord>();
                    foreach (var item in records.EnumerateArray())
                    {
                        var record = Deserialize(item);
                        if (record is not null)
                        {
                            list.Add(record);
                        }
                    }

                    return list;
                }
            }
            catch (JsonException)
            {
                return Array.Empty<LicenseRecord>();
            }
        }
    }

    private static LicenseRecord? Deserialize(JsonElement item)
    {
        try
        {
            var record = new LicenseRecord
            {
                Id = item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : Guid.NewGuid().ToString("N"),
                SchoolName = item.TryGetProperty("schoolName", out var sn) && sn.ValueKind == JsonValueKind.String ? sn.GetString()! : string.Empty,
                HardwareId = item.TryGetProperty("hardwareId", out var hw) && hw.ValueKind == JsonValueKind.String ? hw.GetString()! : string.Empty,
                Expires = item.TryGetProperty("expires", out var ex) && ex.ValueKind == JsonValueKind.String ? ex.GetString()! : string.Empty,
                CreatedUtc = item.TryGetProperty("createdUtc", out var cu) && cu.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(cu.GetString(), out var created) ? created : DateTimeOffset.UtcNow,
                LicenseFilePath = item.TryGetProperty("licenseFilePath", out var fp) && fp.ValueKind == JsonValueKind.String ? fp.GetString()! : string.Empty,
                IsArchived = item.TryGetProperty("isArchived", out var ar) && ar.ValueKind == JsonValueKind.True,
                ArchivedUtc = item.TryGetProperty("archivedUtc", out var au) && au.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(au.GetString(), out var archived) ? archived : (DateTimeOffset?)null,
            };

            return string.IsNullOrEmpty(record.SchoolName) ? null : record;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object ToSerializable(IEnumerable<LicenseRecord> records) => new
    {
        version = 1,
        records = records.Select(r => new
        {
            id = r.Id,
            schoolName = r.SchoolName,
            hardwareId = r.HardwareId,
            expires = r.Expires,
            createdUtc = r.CreatedUtc.ToString("o"),
            licenseFilePath = r.LicenseFilePath,
            isArchived = r.IsArchived,
            archivedUtc = r.ArchivedUtc?.ToString("o"),
        }).ToArray(),
    };

    private void SaveAll(IEnumerable<LicenseRecord> records)
    {
        string json = JsonSerializer.Serialize(ToSerializable(records), Options);
        AtomicJson.Write(_filePath, json);
    }

    public LicenseRecord? Get(string id)
        => Load().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    public void Add(LicenseRecord record)
    {
        lock (_gate)
        {
            var all = Load().ToList();
            all.Add(record);
            SaveAll(all);
        }
    }

    public bool Archive(string id)
        => Mutate(id, r => CopyWithStatus(r, isArchived: true, DateTimeOffset.UtcNow));

    public bool Restore(string id)
        => Mutate(id, r => CopyWithStatus(r, isArchived: false, null));

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var all = Load().ToList();
            int removed = all.RemoveAll(r => string.Equals(r.Id, id, StringComparison.Ordinal));
            if (removed == 0)
            {
                return false;
            }

            SaveAll(all);
            return true;
        }
    }

    private static LicenseRecord CopyWithStatus(LicenseRecord r, bool isArchived, DateTimeOffset? archivedUtc) => new()
    {
        Id = r.Id,
        SchoolName = r.SchoolName,
        HardwareId = r.HardwareId,
        Expires = r.Expires,
        CreatedUtc = r.CreatedUtc,
        LicenseFilePath = r.LicenseFilePath,
        IsArchived = isArchived,
        ArchivedUtc = archivedUtc,
    };

    private bool Mutate(string id, Func<LicenseRecord, LicenseRecord> transform)
    {
        lock (_gate)
        {
            var all = Load().ToList();
            int index = all.FindIndex(r => string.Equals(r.Id, id, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            all[index] = transform(all[index]);
            SaveAll(all);
            return true;
        }
    }
}