namespace LocalMeetingSubtitle.Core.Transcription;

/// <summary>
/// Removes the text a recognizer produces twice when two adjacent segments share audio
/// (the overlap carried from a max-length cut). Only the <i>later</i> text is ever modified:
/// a shared prefix that the model emitted again is dropped from the front of the current text.
/// The fold is punctuation/whitespace/case insensitive so <c>你好，世界</c> still matches <c>你好世界</c>.
/// </summary>
public static class OverlapTextDeduplicator
{
    public readonly record struct Result(string Text, int TrimmedChars, bool Dropped);

    /// <summary>
    /// Trims from <paramref name="currentText"/> the longest prefix it shares with the suffix of
    /// <paramref name="previousText"/>, as long as the shared run reaches <paramref name="minOverlapChars"/>.
    /// Returns the current text unchanged when nothing convincing is shared.
    /// </summary>
    public static Result Apply(string? previousText, string currentText, int minOverlapChars)
    {
        if (string.IsNullOrEmpty(previousText) || string.IsNullOrEmpty(currentText) || minOverlapChars < 1)
        {
            return new Result(currentText, 0, false);
        }

        string previous = SubtitleAccumulator.Fold(previousText!);
        string current = SubtitleAccumulator.Fold(currentText);
        int limit = Math.Min(previous.Length, current.Length);
        if (limit < minOverlapChars)
        {
            return new Result(currentText, 0, false);
        }

        // Longest suffix of the previous text that is also a prefix of the current one.
        int shared = 0;
        for (int length = limit; length >= minOverlapChars; length--)
        {
            if (previous.AsSpan(previous.Length - length).SequenceEqual(current.AsSpan(0, length)))
            {
                shared = length;
                break;
            }
        }

        if (shared == 0)
        {
            return new Result(currentText, 0, false);
        }

        // The whole segment repeats the previous one: there is nothing new to keep.
        if (shared >= current.Length)
        {
            return new Result("", shared, true);
        }

        int cut = IndexAfterFoldedChars(currentText, shared);
        return new Result(currentText[cut..].TrimStart(BoundaryTrimChars), shared, false);
    }

    /// <summary>Index in the original text just past the <paramref name="count"/>-th folded character.</summary>
    private static int IndexAfterFoldedChars(string text, int count)
    {
        int kept = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                continue;
            }

            kept++;
            if (kept == count)
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    /// <summary>Separators that belong to the dropped boundary, not to the kept text.</summary>
    private static readonly char[] BoundaryTrimChars =
        { ' ', '\t', '，', '。', '、', '；', '：', ',', '.', ';', ':' };
}
