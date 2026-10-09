using LocalMeetingSubtitle.Audio;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Storage;

namespace LocalMeetingSubtitle.UnitTests;

/// <summary>
/// Covers the opt-in post-meeting recording: the WAV writer round-trip and the on-disk cleanup
/// that deletes expired temporary recordings and orphaned files.
/// </summary>
public sealed class RecordingTests
{
    [Fact]
    public async Task Wave_recording_round_trips_a_tone_to_16k_mono_wav()
    {
        const int rate = 16000;
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "tone.wav");

            await using var recorder = new WaveRecordingService();
            await recorder.StartAsync(path);
            Assert.True(recorder.IsRecording);

            var format = AudioFormat.Float32(rate, 1);
            const int totalSamples = rate; // exactly one second
            const int chunkSize = 1600;
            for (int offset = 0; offset < totalSamples; offset += chunkSize)
            {
                int length = Math.Min(chunkSize, totalSamples - offset);
                var buffer = new float[length];
                for (int i = 0; i < length; i++)
                {
                    int n = offset + i;
                    buffer[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * n / rate));
                }

                recorder.Write(buffer, format);
            }

            var result = await recorder.StopAsync();

            Assert.Equal(rate, result.SampleRate);
            Assert.Equal(1, result.Channels);
            Assert.True(result.SizeBytes > 0);
            Assert.InRange(result.Duration.TotalSeconds, 0.9, 1.1);
            Assert.True(File.Exists(result.Path));

            var (samples, storedRate, channels) = ReadWav16(result.Path);
            Assert.Equal(rate, storedRate);
            Assert.Equal(1, channels);
            Assert.InRange(samples.Length, rate - 160, rate + 160); // ~1 s

            var rms = Math.Sqrt(samples.Sum(s => (double)s * s) / samples.Length);
            Assert.True(rms > 0.1, $"Expected a non-silent tone, RMS was {rms}.");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Cleanup_deletes_expired_temporary_assets_and_old_orphans()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var assets = new SqliteAudioAssetRepository(temp.Database);

        var directory = CreateTempDirectory();
        try
        {
            var store = new LocalAudioAssetStore(directory);
            var now = DateTimeOffset.UtcNow;

            // (a) An expired temporary asset: row + file are both removed.
            var expiredId = Guid.NewGuid().ToString("N");
            var expiredPath = Path.Combine(directory, expiredId + ".wav");
            File.WriteAllBytes(expiredPath, new byte[] { 1, 2, 3 });
            await assets.AddAsync(new AudioAsset
            {
                AudioAssetId = expiredId,
                Path = expiredPath,
                Kind = AudioAssetKind.TempRecording,
                IsTemporary = true,
                DeleteAfterUtc = now.AddHours(-1)
            });

            // (b) A retained asset's file must survive.
            var retainedId = Guid.NewGuid().ToString("N");
            var retainedPath = Path.Combine(directory, retainedId + ".wav");
            File.WriteAllBytes(retainedPath, new byte[] { 4, 5, 6 });
            await assets.AddAsync(new AudioAsset
            {
                AudioAssetId = retainedId,
                Path = retainedPath,
                Kind = AudioAssetKind.RetainedRecording,
                IsTemporary = false,
                DeleteAfterUtc = null
            });

            // (c) An orphaned file older than 24 h with no row is removed.
            var oldOrphan = Path.Combine(directory, "orphan-old.wav");
            File.WriteAllBytes(oldOrphan, new byte[] { 7 });
            File.SetLastWriteTimeUtc(oldOrphan, DateTime.UtcNow.AddHours(-25));

            // (d) A fresh orphan (< 24 h) is kept.
            var freshOrphan = Path.Combine(directory, "orphan-fresh.wav");
            File.WriteAllBytes(freshOrphan, new byte[] { 8 });

            var deleted = await store.CleanupAsync(assets, DateTimeOffset.UtcNow);

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(expiredPath));
            Assert.Null(await assets.GetAsync(expiredId));
            Assert.True(File.Exists(retainedPath));
            Assert.NotNull(await assets.GetAsync(retainedId));
            Assert.False(File.Exists(oldOrphan));
            Assert.True(File.Exists(freshOrphan));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Minimal 16-bit PCM WAV reader: returns normalized samples plus rate/channels.</summary>
    private static (float[] Samples, int SampleRate, int Channels) ReadWav16(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));

        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        int dataStart = 0, dataLength = 0;

        int pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);

            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, pos + 10);
                sampleRate = BitConverter.ToInt32(bytes, pos + 12);
                bitsPerSample = BitConverter.ToInt16(bytes, pos + 22);
            }
            else if (id == "data")
            {
                dataStart = pos + 8;
                dataLength = size;
            }

            pos += 8 + size + (size & 1); // chunks are word-aligned
        }

        Assert.Equal(16, bitsPerSample);

        var samples = new float[dataLength / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = BitConverter.ToInt16(bytes, dataStart + (i * 2));
            samples[i] = s / 32768f;
        }

        return (samples, sampleRate, channels);
    }
}
