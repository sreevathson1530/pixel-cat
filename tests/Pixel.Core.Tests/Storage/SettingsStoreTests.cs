using Pixel.Core.Storage;

namespace Pixel.Core.Tests.Storage;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pixel-settings-").FullName;
    private string PathOf(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = SettingsStore.Load(PathOf("nope.json"));
        Assert.Equal(new Settings(), s);
        Assert.Equal("black", s.Palette);
        Assert.Equal(0, s.IdleUnloadMinutes); // brain stays awake
    }

    [Fact]
    public void Saved_settings_round_trip()
    {
        var path = PathOf("settings.json");
        var s = new Settings { Palette = "orange", Scale = 4, Muted = true, LlamaBackend = "vulkan" };
        SettingsStore.Save(path, s);
        Assert.Equal(s, SettingsStore.Load(path));
    }

    [Fact]
    public void Unknown_fields_are_ignored_and_missing_fields_keep_defaults()
    {
        var path = PathOf("partial.json");
        File.WriteAllText(path, """{ "palette": "grey", "someFutureThing": 42 }""");
        var s = SettingsStore.Load(path);
        Assert.Equal("grey", s.Palette);
        Assert.Equal(new Settings().Scale, s.Scale);
    }

    [Fact]
    public void Corrupt_file_gives_defaults_instead_of_crashing()
    {
        var path = PathOf("corrupt.json");
        File.WriteAllText(path, "{ this is not json");
        Assert.Equal(new Settings(), SettingsStore.Load(path));
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var path = Path.Combine(_dir, "a", "b", "settings.json");
        SettingsStore.Save(path, new Settings());
        Assert.True(File.Exists(path));
    }
}
