namespace LocalMeetingSubtitle.Core.Audio;

/// <summary>Small helpers for PCM analysis (levels).</summary>
public static class AudioMath
{
    /// <summary>Root-mean-square amplitude of the samples (0..1 for normalized float PCM).</summary>
    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double v = samples[i];
            sum += v * v;
        }
        return Math.Sqrt(sum / samples.Length);
    }

    public static double Peak(ReadOnlySpan<float> samples)
    {
        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var a = Math.Abs((double)samples[i]);
            if (a > peak) peak = a;
        }
        return peak;
    }

    /// <summary>Maps RMS to a perceptual 0..1 meter value using dBFS over a -60 dB floor.</summary>
    public static double RmsToLevel(double rms, double floorDb = -60.0)
    {
        if (rms <= 1e-9) return 0.0;
        double db = 20.0 * Math.Log10(rms);
        if (db <= floorDb) return 0.0;
        if (db >= 0) return 1.0;
        return (db - floorDb) / (0.0 - floorDb);
    }
}
