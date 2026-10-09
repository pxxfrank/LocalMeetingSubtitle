using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.UnitTests;

public sealed class SqliteHotwordRepositoryTests
{
    [Fact]
    public async Task ReplaceHotwords_ReplacesWholeSet()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateHotwordRepository();

        await repository.ReplaceHotwordsAsync(new[]
        {
            new Hotword { Text = "alpha", Score = 2.0f },
            new Hotword { Text = "beta" }
        });

        var first = await repository.GetHotwordsAsync();
        Assert.Equal(new[] { "alpha", "beta" }, first.Select(h => h.Text).OrderBy(t => t));

        await repository.ReplaceHotwordsAsync(new[]
        {
            new Hotword { Text = "gamma" }
        });

        var second = await repository.GetHotwordsAsync();
        Assert.Equal(new[] { "gamma" }, second.Select(h => h.Text));
    }

    [Fact]
    public async Task Hotwords_UpsertInsertsThenUpdates_RoundTripsFields()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateHotwordRepository();

        var groupId = await repository.UpsertGroupAsync(new HotwordGroup { Name = "Products", SortOrder = 2 });

        var hotword = new Hotword { Text = "字幕君", Enabled = true, GroupId = groupId, Score = 1.75f };
        var id = await repository.UpsertHotwordAsync(hotword);
        Assert.True(id > 0);
        Assert.Equal(id, hotword.Id);

        var loaded = Assert.Single(await repository.GetHotwordsAsync());
        Assert.Equal("字幕君", loaded.Text);
        Assert.True(loaded.Enabled);
        Assert.Equal(groupId, loaded.GroupId);
        Assert.Equal(1.75f, loaded.Score, precision: 3);

        hotword.Text = "renamed";
        hotword.Enabled = false;
        hotword.GroupId = null;
        await repository.UpsertHotwordAsync(hotword);

        var updated = Assert.Single(await repository.GetHotwordsAsync());
        Assert.Equal(id, updated.Id);
        Assert.Equal("renamed", updated.Text);
        Assert.False(updated.Enabled);
        Assert.Null(updated.GroupId);
    }

    [Fact]
    public async Task DeleteHotword_RemovesIt()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateHotwordRepository();

        var id = await repository.UpsertHotwordAsync(new Hotword { Text = "temporary" });
        await repository.DeleteHotwordAsync(id);

        Assert.Empty(await repository.GetHotwordsAsync());
    }

    [Fact]
    public async Task GroupsAndCorrectionRules_RoundTrip()
    {
        await using var temp = await TempDatabase.CreateAsync();
        var repository = temp.CreateHotwordRepository();

        var low = await repository.UpsertGroupAsync(new HotwordGroup { Name = "Low", SortOrder = 5 });
        var high = await repository.UpsertGroupAsync(new HotwordGroup { Name = "High", SortOrder = 1 });

        var groups = await repository.GetGroupsAsync();
        Assert.Equal(new[] { "High", "Low" }, groups.Select(g => g.Name));
        Assert.Equal(new[] { high, low }, groups.Select(g => g.GroupId));

        var rule = TextCorrectionRule.Create("sherpa", "Sherpa");
        rule.Priority = 10;
        rule.IsRegex = false;
        var ruleId = await repository.UpsertCorrectionRuleAsync(rule);
        Assert.True(ruleId > 0);

        var otherRule = new TextCorrectionRule { Pattern = @"\bteh\b", Replacement = "the", IsRegex = true, Priority = 20, WholeTokenOnly = false, CaseSensitive = false };
        await repository.UpsertCorrectionRuleAsync(otherRule);

        var rules = await repository.GetCorrectionRulesAsync();
        Assert.Equal(2, rules.Count);
        Assert.Equal(@"\bteh\b", rules[0].Pattern); // higher priority first
        Assert.True(rules[0].IsRegex);
        Assert.False(rules[0].WholeTokenOnly);
        Assert.Equal("sherpa", rules[1].Pattern);
        Assert.True(rules[1].WholeTokenOnly);
        Assert.True(rules[1].Enabled);

        await repository.DeleteCorrectionRuleAsync(ruleId);
        Assert.Single(await repository.GetCorrectionRulesAsync());
    }
}
