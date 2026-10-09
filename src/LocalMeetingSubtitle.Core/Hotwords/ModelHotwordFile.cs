using System.Text;

namespace LocalMeetingSubtitle.Core.Hotwords;

/// <summary>
/// Builds the sherpa-onnx hotwords file text. The model tokenizes CJK models per character
/// and BPE models per word, so each Chinese/Japanese character becomes its own token while
/// runs of Latin letters/digits stay together.
/// </summary>
public static class ModelHotwordFile
{
    /// <summary>
    /// Default temp path for the model-level hotwords file.
    ///
    /// <para><b>The directory name MUST be ASCII.</b> sherpa-onnx opens the file from native code and
    /// cannot read a non-ASCII path; the C# wrapper does not surface the failure, so the recognizer is
    /// constructed but never becomes ready and the app silently produces no subtitles (verified A/B:
    /// the same hotwords file under a Chinese directory gives an empty result and RTF ~0.0002).</para>
    /// </summary>
    public static string DefaultTempPath { get; } =
        Path.Combine(Path.GetTempPath(), "SubtitleJun", "hotwords.txt");

    /// <summary>True when every character of the path is ASCII (the constraint native sherpa code requires).</summary>
    public static bool IsAsciiPath(string path) => path.All(char.IsAscii);

    public static string FormatToken(string phrase)
    {
        var sb = new StringBuilder(phrase.Length * 2);
        var word = new StringBuilder();
        bool first = true;

        void FlushWord()
        {
            if (word.Length == 0) return;
            if (!first) sb.Append(' ');
            sb.Append(word);
            word.Clear();
            first = false;
        }

        foreach (var ch in phrase)
        {
            if (IsCjk(ch))
            {
                FlushWord();
                if (!first) sb.Append(' ');
                sb.Append(ch);
                first = false;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                word.Append(ch);
            }
            else
            {
                FlushWord(); // punctuation/space acts as a separator
            }
        }
        FlushWord();
        return sb.ToString();
    }

    /// <summary>Builds the full file content: one formatted phrase per line.</summary>
    public static string Build(IEnumerable<string> phrases)
    {
        var sb = new StringBuilder();
        foreach (var phrase in phrases)
        {
            var token = FormatToken(phrase.Trim());
            if (token.Length > 0)
            {
                sb.Append(token).Append('\n');
            }
        }
        return sb.ToString();
    }

    public static bool IsCjk(char c)
    {
        return (c >= 0x4E00 && c <= 0x9FFF)   // CJK Unified Ideographs
            || (c >= 0x3400 && c <= 0x4DBF)   // Extension A
            || (c >= 0xF900 && c <= 0xFAFF)   // Compatibility Ideographs
            || (c >= 0x3040 && c <= 0x30FF)   // Hiragana + Katakana
            || (c >= 0xAC00 && c <= 0xD7AF);  // Hangul syllables
    }
}
