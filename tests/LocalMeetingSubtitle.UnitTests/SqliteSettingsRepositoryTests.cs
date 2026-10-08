using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class SqliteSettingsRepositoryTests
{
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSettingsRepository();

        var settings = new AppSettings
        {
            LastAudioDeviceId = "device-9",
            LastModelId = "sensevoice-small",
            AutoScrollEnabled = false,
            FloatingSubtitleVisible = true,
            FloatingFontSize = 33.5,
            FloatingBackgroundOpacity = 0.55,
            FloatingClickThrough = true,
            MinimizeToTrayOnClose = false,
            SubtitleFontFamily = "Microsoft YaHei",
            SubtitleFontSize = 22,
            AsrNumThreads = 4,
            ExportIncludeTimestamps = false,
            EnableVadSegmenting = false
        };

        await repository.SaveAsync(settings);
        var loaded = await repository.LoadAsync();

        Assert.Equal("device-9", loaded.LastAudioDeviceId);
        Assert.Equal("sensevoice-small", loaded.LastModelId);
        Assert.False(loaded.AutoScrollEnabled);
        Assert.True(loaded.FloatingSubtitleVisible);
        Assert.Equal(33.5, loaded.FloatingFontSize, precision: 3);
        Assert.Equal(0.55, loaded.FloatingBackgroundOpacity, precision: 3);
        Assert.True(loaded.FloatingClickThrough);
        Assert.False(loaded.MinimizeToTrayOnClose);
        Assert.Equal("Microsoft YaHei", loaded.SubtitleFontFamily);
        Assert.Equal(22, loaded.SubtitleFontSize, precision: 3);
        Assert.Equal(4, loaded.AsrNumThreads);
        Assert.False(loaded.ExportIncludeTimestamps);
        Assert.False(loaded.EnableVadSegmenting);

        // Saving again overwrites the single row rather than appending.
        settings.AsrNumThreads = 8;
        await repository.SaveAsync(settings);
        Assert.Equal(8, (await repository.LoadAsync()).AsrNumThreads);
    }

    [Fact]
    public async Task LoadAsync_OnEmptyDatabase_ReturnsDefaults()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateSettingsRepository();

        var loaded = await repository.LoadAsync();

        Assert.True(loaded.AutoScrollEnabled);
        Assert.True(loaded.MinimizeToTrayOnClose);
        Assert.True(loaded.ExportIncludeTimestamps);
        Assert.True(loaded.EnableVadSegmenting);
        Assert.Equal("Microsoft YaHei UI", loaded.SubtitleFontFamily);
        Assert.Null(loaded.LastAudioDeviceId);
    }
}
