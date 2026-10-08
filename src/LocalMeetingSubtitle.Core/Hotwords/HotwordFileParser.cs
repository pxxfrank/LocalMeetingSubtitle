using System.Text;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Hotwords;

public sealed record HotwordImportResult(IReadOnlyList<Hotword> Hotwords, IReadOnlyList<string> Warnings);

/// <summary>
/// Parses hotword files. TXT: one phrase per line ("#" comments and blank lines ignored).
/// CSV: <c>text[,enabled][,score][,group]</c> with an optional header row and quoted fields.
/// </summary>
public static class HotwordFileParser
{
    public static HotwordImportResult ParseTxt(string content)
    {
        var list = new List<Hotword>();
        var warnings = new List<string>();
        int lineNo = 0;
        foreach (var rawLine in SplitLines(content))
        {
            lineNo++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            string text = line;
            float score = 1.5f;
            int colon = line.LastIndexOf(':');
            if (colon > 0 && float.TryParse(line[(colon + 1)..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                score = parsed;
                text = line[..colon].Trim();
            }

            AddIfValid(text, score, null, list, warnings, lineNo);
        }
        return new HotwordImportResult(list, warnings);
    }

    public static HotwordImportResult ParseCsv(string content)
    {
        var list = new List<Hotword>();
        var warnings = new List<string>();
        int row = 0;
        foreach (var fields in ParseCsvRows(content))
        {
            row++;
            if (fields.Count == 0) continue;
            var first = fields[0].Trim();
            if (row == 1 && first.Equals("text", StringComparison.OrdinalIgnoreCase)) continue; // header
            if (first.Length == 0 || first.StartsWith('#')) continue;

            bool enabled = fields.Count < 2 || ParseBool(fields[1].Trim(), true);
            float score = 1.5f;
            if (fields.Count >= 3)
            {
                float.TryParse(fields[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out score);
                if (score <= 0) score = 1.5f;
            }
            string? group = fields.Count >= 4 && fields[3].Trim().Length > 0 ? fields[3].Trim() : null;

            AddIfValid(first, score, group, list, warnings, row, enabled);
        }
        return new HotwordImportResult(list, warnings);
    }

    private static void AddIfValid(string text, float score, string? group, List<Hotword> list,
        List<string> warnings, int line, bool enabled = true)
    {
        var normalized = HotwordValidator.Normalize(text);
        var check = HotwordValidator.Validate(normalized);
        if (!check.IsValid)
        {
            warnings.Add($"Line {line}: skipped \"{text}\" ({check.Message})");
            return;
        }
        if (list.Any(h => string.Equals(h.Text, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"Line {line}: duplicate \"{normalized}\" skipped");
            return;
        }
        list.Add(new Hotword { Text = normalized, Enabled = enabled, Score = score });
    }

    private static bool ParseBool(string value, bool fallback) => value.ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "y" or "on" or "enable" or "enabled" => true,
        "0" or "false" or "no" or "n" or "off" or "disable" or "disabled" => false,
        _ => fallback
    };

    private static IEnumerable<string> SplitLines(string content)
        => content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static IEnumerable<List<string>> ParseCsvRows(string content)
    {
        var rows = new List<List<string>>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': fields.Add(sb.ToString()); sb.Clear(); break;
                    case '\r': break;
                    case '\n':
                        fields.Add(sb.ToString()); sb.Clear();
                        rows.Add(fields); fields = new List<string>();
                        break;
                    default: sb.Append(c); break;
                }
            }
        }
        if (sb.Length > 0 || fields.Count > 0)
        {
            fields.Add(sb.ToString());
            rows.Add(fields);
        }
        return rows.Where(r => r.Any(f => f.Trim().Length > 0));
    }
}
