using System.Text;
using System.Text.RegularExpressions;
using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Hotwords;

public sealed record HotwordCorrectionResult(string Text, IReadOnlyList<TextCorrectionRule> AppliedRules);

/// <summary>
/// Deterministic, rule-based text correction. Rules run in priority order; each rule is
/// applied at most once so unrelated text is never mangled by runaway replacement.
/// Whole-token mode (default) only matches when the pattern is not part of a larger word.
/// </summary>
public sealed class TextCorrectionEngine
{
    private const int MaxReplacementsPerRule = 64;

    public HotwordCorrectionResult Apply(string text, IReadOnlyList<TextCorrectionRule> rules, bool collectApplied = false)
    {
        var applied = new List<TextCorrectionRule>();
        if (string.IsNullOrEmpty(text) || rules.Count == 0)
        {
            return new HotwordCorrectionResult(text, applied);
        }

        var current = text;
        foreach (var rule in rules
                     .Where(r => r.Enabled && !string.IsNullOrEmpty(r.Pattern))
                     .OrderByDescending(r => r.Priority)
                     .ThenBy(r => r.Id))
        {
            var replaced = ApplyRule(current, rule);
            if (!ReferenceEquals(replaced, current) && !string.Equals(replaced, current, StringComparison.Ordinal))
            {
                current = replaced;
                if (collectApplied) applied.Add(rule);
            }
        }

        return new HotwordCorrectionResult(current, applied);
    }

    private static string ApplyRule(string text, TextCorrectionRule rule)
    {
        if (rule.IsRegex)
        {
            var options = rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            try
            {
                return Regex.Replace(text, rule.Pattern, rule.Replacement, options, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                return text; // invalid pattern: skip rather than fail the pipeline
            }
        }

        if (!rule.WholeTokenOnly)
        {
            if (rule.CaseSensitive)
            {
                return ReplaceCount(text, rule.Pattern, rule.Replacement, StringComparison.Ordinal);
            }
            return ReplaceCount(text, rule.Pattern, rule.Replacement, StringComparison.OrdinalIgnoreCase);
        }

        // Whole-token match: pattern must not be flanked by word characters.
        var boundary = @"(?<![\p{L}\p{N}_])" + Regex.Escape(rule.Pattern) + @"(?![\p{L}\p{N}_])";
        var opts = rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        try
        {
            // Evaluate rules in a bounded way: never more than MaxReplacementsPerRule swaps.
            int count = 0;
            return Regex.Replace(text, boundary, m =>
            {
                if (count++ >= MaxReplacementsPerRule) return m.Value;
                return rule.Replacement;
            }, opts);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static string ReplaceCount(string text, string pattern, string replacement, StringComparison comparison)
    {
        var sb = new StringBuilder();
        int index = 0;
        int count = 0;
        while (count < MaxReplacementsPerRule)
        {
            int found = text.IndexOf(pattern, index, comparison);
            if (found < 0) break;
            sb.Append(text, index, found - index);
            sb.Append(replacement);
            index = found + pattern.Length;
            count++;
        }
        sb.Append(text, index, text.Length - index);
        return sb.ToString();
    }
}
