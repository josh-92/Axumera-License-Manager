using Axumera.LicenseManager.Core.Models;
using Axumera.LicenseManager.Core.Persistence;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

public class JsonLicenseRecordStoreTests
{
    private static string NewFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "licenses.json");
    }

    private static LicenseRecord Sample(string school = "Sample School") =>
        LicenseRecord.Create(school, "ABCDEF12", "2027-12-31", @"C:\Somewhere\license.lic");

    [Fact]
    public void Round_trip_preserves_records()
    {
        string file = NewFile();
        var store = new JsonLicenseRecordStore(file);
        var record = Sample("Round Trip Academy");
        store.Add(record);

        var loaded = new JsonLicenseRecordStore(file).Load();
        var found = Assert.Single(loaded);
        Assert.Equal(record.Id, found.Id);
        Assert.Equal(record.SchoolName, found.SchoolName);
        Assert.Equal(record.HardwareId, found.HardwareId);
        Assert.Equal(record.Expires, found.Expires);
        Assert.Equal(record.LicenseFilePath, found.LicenseFilePath);
        Assert.False(found.IsArchived);
        Assert.Equal(record.CreatedUtc.ToUnixTimeMilliseconds(), found.CreatedUtc.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Missing_file_loads_as_empty()
    {
        var store = new JsonLicenseRecordStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nope.json"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Archive_restore_delete()
    {
        string file = NewFile();
        var store = new JsonLicenseRecordStore(file);
        var record = Sample();
        store.Add(record);

        Assert.True(store.Archive(record.Id));
        var archived = store.Get(record.Id)!;
        Assert.True(archived.IsArchived);
        Assert.NotNull(archived.ArchivedUtc);

        Assert.True(store.Restore(record.Id));
        Assert.False(store.Get(record.Id)!.IsArchived);
        Assert.Null(store.Get(record.Id)!.ArchivedUtc);

        Assert.True(store.Delete(record.Id));
        Assert.Null(store.Get(record.Id));
        Assert.False(store.Archive(record.Id));
        Assert.False(store.Delete(record.Id));
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_recovered_as_empty()
    {
        string file = NewFile();
        File.WriteAllText(file, "{ this is not json !!!");

        var store = new JsonLicenseRecordStore(file);
        Assert.Empty(store.Load());

        // Backup exists, store still functional afterwards.
        var backup = Directory.GetFiles(Path.GetDirectoryName(file)!, "licenses.json.corrupt-*").SingleOrDefault();
        Assert.NotNull(backup);
        store.Add(Sample());
        Assert.Single(store.Load());
    }

    [Fact]
    public void Wrong_shape_json_loads_as_empty()
    {
        string file = NewFile();
        File.WriteAllText(file, "{\"records\": \"not-an-array\"}");
        Assert.Empty(new JsonLicenseRecordStore(file).Load());
    }
}

public class JsonSettingsStoreTests
{
    private static string NewFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    [Fact]
    public void Settings_round_trip_flat()
    {
        string file = NewFile();
        var store = new JsonSettingsStore(file);
        Assert.Equal(string.Empty, store.PrivateKeyPath);
        Assert.Equal(string.Empty, store.LastSaveDirectory);

        store.PrivateKeyPath = @"C:\Axumera Licensing\private_key.pem";
        store.LastSaveDirectory = @"C:\Somewhere";

        var reloaded = new JsonSettingsStore(file);
        Assert.Equal(@"C:\Axumera Licensing\private_key.pem", reloaded.PrivateKeyPath);
        Assert.Equal(@"C:\Somewhere", reloaded.LastSaveDirectory);
    }

    [Fact]
    public void Settings_file_is_flat_json_no_wrapper()
    {
        string file = NewFile();
        var store = new JsonSettingsStore(file);
        store.PrivateKeyPath = "K";
        string content = File.ReadAllText(file);
        Assert.Contains("\"privateKeyPath\"", content);
        Assert.DoesNotContain("\"settings\"", content);
    }

    [Fact]
    public void Corrupt_settings_recover_cleanly()
    {
        string file = NewFile();
        File.WriteAllText(file, "{ corrupt");
        var store = new JsonSettingsStore(file);
        Assert.Equal(string.Empty, store.PrivateKeyPath);
        store.LastSaveDirectory = "X";
        Assert.Equal("X", new JsonSettingsStore(file).LastSaveDirectory);
    }
}