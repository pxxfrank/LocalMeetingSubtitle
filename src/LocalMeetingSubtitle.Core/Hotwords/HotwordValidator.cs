using LocalMeetingSubtitle.Core.Abstractions;

namespace LocalMeetingSubtitle.Core.Hotwords;

/// <summary>Validates hotword text before it is accepted into the store.</summary>
public static class HotwordValidator
{
    public const int MaxLength = 64;

    public static HotwordValidationResult Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return HotwordValidationResult.Invalid("Hotword is empty.");
        }

        if (text.Length > MaxLength)
        {
            return HotwordValidationResult.Invalid($"Hotword exceeds {MaxLength} characters.");
        }

        foreach (var ch in text)
        {
            if (ch == '\r' || ch == '\n' || ch == '\t')
            {
                return HotwordValidationResult.Invalid("Hotword must not contain line breaks or tabs.");
            }
            if (char.IsControl(ch))
            {
                return HotwordValidationResult.Invalid("Hotword must not contain control characters.");
            }
        }

        return HotwordValidationResult.Ok;
    }

    /// <summary>Normalizes whitespace (used when importing files).</summary>
    public static string Normalize(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool prevSpace = false;
        foreach (var ch in text.Trim())
        {
            bool ws = char.IsWhiteSpace(ch);
            if (ws)
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }
}
