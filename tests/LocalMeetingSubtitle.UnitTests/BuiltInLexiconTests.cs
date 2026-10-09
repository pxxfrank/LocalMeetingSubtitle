using LocalMeetingSubtitle.Core.Hotwords;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Storage;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class BuiltInLexiconTests
{
    [Fact]
    public void Lexicon_is_populated_deduplicated_and_has_no_blanks()
    {
        Assert.True(BuiltInLexicon.HotwordCount >= 100, $"expected a substantial glossary, got {BuiltInLexicon.HotwordCount}");
        Assert.All(BuiltInLexicon.Hotwords, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(BuiltInLexicon.Hotwords.Count, BuiltInLexicon.Hotwords.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("昇腾", BuiltInLexicon.Hotwords);
        Assert.Contains("鸿蒙", BuiltInLexicon.Hotwords);
    }

    [Fact]
    public void Rules_are_conservative_and_apply_inside_cjk_sentences()
    {
        Assert.NotEmpty(BuiltInLexicon.Rules);
        Assert.All(BuiltInLexicon.Rules, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Pattern));
            Assert.False(string.IsNullOrWhiteSpace(r.Replacement));
            // CJK needs substring mode: whole-token CJK never matches inside a sentence (see the
            // class comment and TextCorrectionEngineTests).
            Assert.False(r.WholeTokenOnly);
        });
    }

    [Fact]
    public async Task Service_merges_the_lexicon_by_default()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var service = new DefaultHotwordService(temp.CreateHotwordRepository());
        await service.ReloadAsync();

        Assert.True(service.UseBuiltInLexicon);
        Assert.True(service.ActiveHotwords.Count >= BuiltInLexicon.HotwordCount);
        Assert.Contains(service.ActiveHotwords, h => h.Text == "昇腾");
        Assert.Contains(service.ActiveRules, r => r.Replacement == "昇腾");
    }

    [Fact]
    public async Task Service_excludes_the_lexicon_when_disabled()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var service = new DefaultHotwordService(temp.CreateHotwordRepository());
        await service.ReloadAsync();

        service.UseBuiltInLexicon = false;

        Assert.Empty(service.ActiveHotwords);
        Assert.Empty(service.ActiveRules);
    }

    [Fact]
    public async Task User_hotword_wins_over_an_identical_built_in_term()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateHotwordRepository();
        await repository.ReplaceHotwordsAsync(new[]
        {
            new Hotword { Text = "昇腾", Enabled = true, Score = 3.0f }
        });

        var service = new DefaultHotwordService(repository);
        await service.ReloadAsync();

        var matches = service.ActiveHotwords.Where(h => h.Text == "昇腾").ToList();
        Assert.Single(matches);
        Assert.Equal(3.0f, matches[0].Score);
    }

    [Fact]
    public async Task Model_hotword_file_is_produced_from_the_lexicon_alone()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var service = new DefaultHotwordService(temp.CreateHotwordRepository());
        await service.ReloadAsync();
        service.SetEngineCapability(supportsModelHotwords: true, modelCanBeRebuiltWithoutDataLoss: true);

        var path = Path.Combine(Path.GetTempPath(), "lms-lexicon-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var written = service.WriteModelHotwordFile(path);

            Assert.Equal(path, written);
            var content = await File.ReadAllTextAsync(path);
            // ModelHotwordFile splits CJK into single-character tokens.
            Assert.Contains("昇 腾", content);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Corrections_fix_predictable_mis_hearings()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var service = new DefaultHotwordService(temp.CreateHotwordRepository());
        await service.ReloadAsync();

        Assert.Contains("昇腾", service.ApplyCorrections("我们要用升腾来训练模型"));
        Assert.Contains("鲲鹏", service.ApplyCorrections("基于鲲朋服务器"));
        Assert.Contains("鸿蒙", service.ApplyCorrections("鸿盟生态"));
        // 昇 is not in the bundled model's vocabulary, so this term can only be fixed by a rule.
        Assert.Contains("毕昇", service.ApplyCorrections("毕升编译器"));
    }
}
