using System.Text.RegularExpressions;
using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>Every word the mod shows has an English and a Chinese version, and the game's language picks between them.</summary>
public class TextsTests
{
    private static readonly IReadOnlyList<KeepTarget> NoKeeps = Array.Empty<KeepTarget>();

    public static TheoryData<int> Entries()
    {
        var data = new TheoryData<int>();

        foreach (var index in Enumerable.Range(0, Texts.All.Count)) data.Add(index);

        return data;
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void Every_text_has_plain_english_and_a_real_chinese_translation(int index)
    {
        var phrase = Texts.All[index];

        Assert.False(string.IsNullOrWhiteSpace(phrase.En));
        // The multiplication sign is the one exception: the game writes amounts as "Plank ×2", and the page shows a plain x.
        Assert.True(phrase.En.All(ch => ch is >= ' ' and <= '~' or '×'), phrase.En);
        Assert.NotEqual(phrase.En, phrase.Zh);

        // A pattern of blanks and marks only, like "{0} / {1}", has no words to translate; everything else must be Chinese.
        var words = Regex.Replace(phrase.Zh, @"\{\d\}", "");

        if (words.Any(char.IsLetter)) Assert.Contains(phrase.Zh, ch => ch is >= '一' and <= '鿿');
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void Both_languages_fill_in_the_same_blanks(int index)
    {
        var phrase = Texts.All[index];

        Assert.Equal(phrase.En.Contains("{0}"), phrase.Zh.Contains("{0}"));
        Assert.Equal(phrase.En.Contains("{1}"), phrase.Zh.Contains("{1}"));
        Assert.Equal(phrase.En.Contains("{2}"), phrase.Zh.Contains("{2}"));
    }

    [Fact]
    public void Every_panel_label_has_one_key()
    {
        Assert.Equal(Texts.Page.Count, Texts.Page.Select(word => word.Key).Distinct().Count());
    }

    [Fact]
    public void A_chinese_game_gets_the_makes_first_mark_in_chinese()
    {
        var marks = ChainMarks.Find(Bench.Book(), Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), new HashSet<string>(), maxDepth: 4, isChinese: true);

        Assert.StartsWith("先制作：", marks["crate"]);
        Assert.DoesNotContain("Makes first", marks["crate"]);
    }

    [Fact]
    public void A_chinese_game_gets_why_the_queue_cannot_go_on_in_chinese()
    {
        var shortOf = QueuePlanner.Next(new QueueEntry("crate", "Crate", 1), Bench.Book(), Bench.Mats((Bench.Log, 2)), NoKeeps, 4, Bench.Name, isChinese: true);
        var notListed = QueuePlanner.Next(new QueueEntry("anvil", "Anvil", 1), Bench.Book(), new Dictionary<int, int>(), NoKeeps, 4, Bench.Name, isChinese: true);

        Assert.StartsWith("无法制作Crate ×1：缺少", Assert.IsType<NextStep.Blocked>(shortOf).Reason);
        Assert.Equal("Anvil还不在你的配方列表里", Assert.IsType<NextStep.Blocked>(notListed).Reason);
    }

    [Fact]
    public void A_chinese_game_gets_the_short_note_and_its_heads_up_in_chinese()
    {
        var outlook = QueueForecast.Plan(new[] { new QueueEntry("rope", "Rope", 1, Id: 1) }, Bench.Book(), Bench.Mats((Bench.Fibre, 1)), NoKeeps, 4, Bench.Name, isChinese: true);

        Assert.Equal(OrderState.Short, outlook.Orders[0].State);
        Assert.Equal("缺少：Fibre x2", outlook.Orders[0].Note);
        Assert.Equal("注意：Rope缺少Fibre x2。队列会在这里停下。", outlook.HeadsUp);
    }

    [Fact]
    public void The_english_heads_up_still_names_what_is_missing()
    {
        var outlook = QueueForecast.Plan(new[] { new QueueEntry("rope", "Rope", 1, Id: 1) }, Bench.Book(), Bench.Mats((Bench.Fibre, 1)), NoKeeps, 4, Bench.Name);

        Assert.Equal("Short: Fibre x2", outlook.Orders[0].Note);
        Assert.Equal("Heads up: Rope is short of Fibre x2. The queue will stop there.", outlook.HeadsUp);
    }

    [Fact]
    public void A_chinese_game_gets_the_keep_stocked_line_in_chinese()
    {
        var plan = new TopUpPlan(Array.Empty<TopUpOrder>(), new[] { "Rope", "Net" });

        Assert.Equal("保持库存：材料不够制作Rope、Net", TopUpPlanner.Describe(plan, isChinese: true));
        Assert.Equal("Keep stocked: not enough materials for Rope, Net", TopUpPlanner.Describe(plan));
    }

    [Fact]
    public void The_runner_words_its_messages_in_the_language_the_game_shows_at_that_moment()
    {
        var isChinese = false;
        var runner = new QueueRunner(isChinese: () => isChinese);

        Assert.Equal("queue finished", RunToTheEnd(runner).Reason);

        isChinese = true;

        Assert.Equal("队列已完成", RunToTheEnd(runner).Reason);
    }

    [Fact]
    public void The_page_asks_only_for_labels_the_plugin_sends()
    {
        var keys = Texts.Page.Select(word => word.Key).ToHashSet();
        var script = PageScript();
        var asked = Regex.Matches(script, @"say\((?:[^'()]*\?\s*)?'(\w+)'(?:\s*:\s*'(\w+)')?").SelectMany(match => new[] { match.Groups[1], match.Groups[2] })
            .Concat(Regex.Matches(script, @"counted\([^,]+, '(\w+)', '(\w+)'\)").SelectMany(match => new[] { match.Groups[1], match.Groups[2] }))
            .Concat(Regex.Matches(script, @"label: '(\w+)'").Select(match => match.Groups[1]))
            .Concat(Regex.Matches(Regex.Match(script, @"say\(\{ idle:[^}]*\}").Value, @": '(\w+)'").Select(match => match.Groups[1]))
            .Where(group => group.Success)
            .Select(group => group.Value)
            .ToList();

        Assert.NotEmpty(asked);
        Assert.All(asked, key => Assert.Contains(key, keys));
    }

    [Fact]
    public void The_pages_own_english_matches_what_the_plugin_sends_in_english()
    {
        var block = Regex.Match(PageScript(), @"var ENGLISH = \{\r?\n(.*?)\r?\n  \};", RegexOptions.Singleline).Groups[1].Value;
        var english = Regex.Matches(block, @"^\s+(\w+): '((?:[^'\\]|\\.)*)',?\r?$", RegexOptions.Multiline).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

        Assert.Equal(Texts.Page.ToDictionary(word => word.Key, word => word.Phrase.En), english);
    }

    [Fact]
    public void The_words_statement_carries_every_label_in_the_chosen_language()
    {
        var script = PageWords.Script(isChinese: true);

        Assert.StartsWith("window.__craftinghubWords={", script);
        Assert.All(Texts.Page, word => Assert.Contains(HubView.Str(word.Key) + ":" + HubView.Str(word.Phrase.Zh), script));
    }

    // Runs an empty queue, so the runner words its "queue finished" line.
    private static RunnerCommand.Stopped RunToTheEnd(QueueRunner runner)
    {
        runner.Start();

        return Assert.IsType<RunnerCommand.Stopped>(runner.Tick(new BenchView(true, false, false, 0, new Dictionary<int, int>(), 0, 1), 0, () => new NextStep.Finished()));
    }

    private static string PageScript()
    {
        using var stream = typeof(TextsTests).Assembly.GetManifestResourceStream("CraftingHub.page.js") ?? throw new InvalidOperationException("page.js is not embedded in the test assembly");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
