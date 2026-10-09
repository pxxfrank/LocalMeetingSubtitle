namespace LocalMeetingSubtitle.Core.Speakers;

/// <summary>
/// Deterministic, distinguishable colors for speaker labels. The same index always yields the same
/// color, so a speaker keeps its color across reloads; colors are chosen to read on both the light
/// and dark themes.
/// </summary>
public static class SpeakerColorPalette
{
    /// <summary>ARGB values (opaque). Enough for a plausible number of speakers before wrapping.</summary>
    public static readonly IReadOnlyList<int> Colors = new[]
    {
        unchecked((int)0xFFC96442), // coral (app accent)
        unchecked((int)0xFF3B7EA1), // blue
        unchecked((int)0xFF4E9A6B), // green
        unchecked((int)0xFFB07D3D), // amber
        unchecked((int)0xFF7A5AA6), // violet
        unchecked((int)0xFFB4534F), // brick
        unchecked((int)0xFF2E8B8B), // teal
        unchecked((int)0xFF8A6D3B), // bronze
        unchecked((int)0xFF9C4A8B), // magenta
        unchecked((int)0xFF5A7D2A)  // olive
    };

    public static int ColorFor(int index)
    {
        if (index < 0)
        {
            index = 0;
        }

        return Colors[index % Colors.Count];
    }
}
