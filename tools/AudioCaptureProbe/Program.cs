// AudioCaptureProbe — a small offline utility to inspect WASAPI loopback render devices
// and capture real system playback audio through the LocalMeetingSubtitle.Audio service.
//
//   AudioCaptureProbe list
//   AudioCaptureProbe capture [--device <id|index>] [--seconds <n>] [--out <file.wav>]

using LocalMeetingSubtitle.Audio;
using LocalMeetingSubtitle.Core.Audio;
using NAudio.Wave;

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "";

try
{
    switch (command)
    {
        case "list":
            return await RunListAsync();
        case "capture":
            return await RunCaptureAsync(args);
        case "":
        case "help":
        case "-h":
        case "--help":
            PrintUsage();
            return command == "" ? 1 : 0;
        default:
            Console.Error.WriteLine($"Unknown command: {args[0]}");
            PrintUsage();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("AudioCaptureProbe — WASAPI loopback inspection utility");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  AudioCaptureProbe list");
    Console.WriteLine("  AudioCaptureProbe capture [--device <id|index>] [--seconds <n>] [--out <file.wav>]");
    Console.WriteLine();
    Console.WriteLine("Defaults: system default render device, 5 seconds, loopback-<timestamp>.wav");
}

static async Task<int> RunListAsync()
{
    await using var service = new WasapiLoopbackCaptureService();
    var devices = service.EnumerateDevices();

    if (devices.Count == 0)
    {
        Console.WriteLine("No active render (playback) devices found.");
        return 0;
    }

    Console.WriteLine($"Render devices ({devices.Count}):");
    for (int i = 0; i < devices.Count; i++)
    {
        var d = devices[i];
        string marker = d.IsDefault ? "  <-- default" : "";
        Console.WriteLine($"  [{i}] {d.FriendlyName}{marker}");
        Console.WriteLine($"        id: {d.Id}");
    }
    return 0;
}

static async Task<int> RunCaptureAsync(string[] args)
{
    string? deviceArg = GetOption(args, "--device");
    string outPath = GetOption(args, "--out") ?? $"loopback-{DateTime.Now:yyyyMMdd-HHmmss}.wav";

    int seconds = 5;
    string? secondsArg = GetOption(args, "--seconds");
    if (secondsArg is not null && (!int.TryParse(secondsArg, out seconds) || seconds <= 0))
    {
        Console.Error.WriteLine($"Invalid --seconds value: '{secondsArg}' (expected a positive integer).");
        return 1;
    }

    await using var service = new WasapiLoopbackCaptureService();

    string? resolvedId = null;
    if (deviceArg is not null)
    {
        if (int.TryParse(deviceArg, out int index))
        {
            var devices = service.EnumerateDevices();
            if (index < 0 || index >= devices.Count)
            {
                Console.Error.WriteLine($"Device index {index} is out of range (0..{devices.Count - 1}).");
                return 1;
            }
            resolvedId = devices[index].Id;
        }
        else
        {
            resolvedId = deviceArg;
        }
    }

    WaveFileWriter? writer = null;
    long totalSamples = 0;
    int channels = 1;
    int sampleRate = 0;
    double lastLevel = 0.0;
    double lastRms = 0.0;
    long lastPrint = Environment.TickCount64;
    string? errorMessage = null;

    service.FramesAvailable += (_, e) =>
    {
        if (writer is null)
        {
            channels = Math.Max(1, e.Format.Channels);
            sampleRate = e.Format.SampleRate;
            writer = new WaveFileWriter(outPath, new WaveFormat(sampleRate, 16, channels));
        }

        var pcm = ToPcm16(e.Samples);
        writer.Write(pcm, 0, pcm.Length);
        totalSamples += e.Samples.Length;

        double rms = AudioMath.Rms(ChannelConverter.ToMono(e.Samples, channels));
        lastRms = rms;
        lastLevel = AudioMath.RmsToLevel(rms);

        long now = Environment.TickCount64;
        if (now - lastPrint >= 500)
        {
            lastPrint = now;
            double elapsed = totalSamples / (double)(channels * Math.Max(1, sampleRate));
            Console.WriteLine($"  level={lastLevel:F3}  rms={lastRms:F4}  captured={elapsed:F2}s");
        }
    };

    service.LevelChanged += (_, level) => lastLevel = level;
    service.Error += (_, e) =>
    {
        errorMessage = e.Message;
        Console.Error.WriteLine($"Capture error{(e.Fatal ? " (fatal)" : "")}: {e.Message}");
    };
    service.DeviceChanged += (_, e) =>
        Console.WriteLine($"  [device] default render device changed -> {e.NewDefaultDeviceId}");

    Console.WriteLine($"Capturing {seconds}s from {(resolvedId ?? "the default render device")}...");
    await service.StartAsync(resolvedId ?? string.Empty);
    Console.WriteLine($"Source format: {service.SourceFormat}");
    Console.WriteLine($"Writing 16-bit PCM WAV: {Path.GetFullPath(outPath)}");
    Console.WriteLine();

    await Task.Delay(TimeSpan.FromSeconds(seconds));

    await service.StopAsync();

    if (writer is null)
    {
        // No buffers arrived (silent system). Still emit a valid, empty WAV for the source format.
        var fmt = service.SourceFormat;
        writer = new WaveFileWriter(outPath, new WaveFormat(fmt.SampleRate, 16, Math.Max(1, fmt.Channels)));
        channels = Math.Max(1, fmt.Channels);
        sampleRate = fmt.SampleRate;
        Console.Error.WriteLine("Warning: no audio buffers were captured (system silent / nothing playing).");
    }

    writer.Dispose();
    writer = null;

    double capturedSeconds = channels * sampleRate > 0 ? totalSamples / (double)(channels * sampleRate) : 0;
    Console.WriteLine();
    Console.WriteLine($"Done. Captured {totalSamples} samples (~{capturedSeconds:F2}s, {channels} ch @ {sampleRate} Hz).");
    Console.WriteLine($"Saved: {Path.GetFullPath(outPath)}");

    if (errorMessage is not null)
    {
        Console.Error.WriteLine($"Capture failed: {errorMessage}");
        return 1;
    }

    return 0;
}

static string? GetOption(string[] args, string name)
{
    for (int i = 1; i < args.Length; i++)
    {
        string a = args[i];
        if (a == name)
        {
            if (i + 1 < args.Length) return args[i + 1];
        }
        else if (a.StartsWith(name + "=", StringComparison.Ordinal))
        {
            return a[(name.Length + 1)..];
        }
    }
    return null;
}

static byte[] ToPcm16(float[] samples)
{
    var bytes = new byte[samples.Length * 2];
    for (int i = 0; i < samples.Length; i++)
    {
        int v = (int)Math.Round(samples[i] * 32767.0);
        if (v > 32767) v = 32767;
        else if (v < -32768) v = -32768;
        bytes[i * 2] = (byte)(v & 0xFF);
        bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
    }
    return bytes;
}
