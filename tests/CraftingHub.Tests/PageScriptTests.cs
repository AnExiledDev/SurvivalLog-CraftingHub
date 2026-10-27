using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>The workbench screen page.js draws: the searchable recipe list, the craft queue and the line under the bench.</summary>
public class PageScriptTests
{
    // The game's recipe objects as ToolTable.html receives them: plank and torch can be made now, crate needs parts
    // first, rope is short, shirt is locked. Torch's group is not one of the page's own, so it is listed under "other".
    private const string Recipes = """
        [
          { recipeKey: 'plank', name: 'Plank', icon: 'plank.png', canCraft: true, noteCategory: 1,
            materialsJson: '[{"icon":"log.png","name":"Log","need":1,"have":5,"hasEnough":true}]' },
          { recipeKey: 'rope', name: 'Rope', icon: 'rope.png', canCraft: false, noteCategory: 1,
            materialsJson: '[{"icon":"fibre.png","name":"Fibre","need":3,"have":1,"hasEnough":false}]' },
          { recipeKey: 'crate', name: 'Crate', icon: 'crate.png', canCraft: false, noteCategory: 2,
            materialsJson: '[{"name":"Plank","need":4,"have":0,"hasEnough":false,"sub":"plank"},{"name":"Rope","need":1,"have":0,"hasEnough":false,"sub":"rope"}]' },
          { recipeKey: 'shirt', name: 'Shirt', icon: 'shirt.png', canCraft: false, craftLocked: true, craftLockText: 'Needs a loom', noteCategory: 3,
            materialsJson: '[{"name":"Fibre","need":6,"have":1,"hasEnough":false}]' },
          { recipeKey: 'torch', name: 'Torch', icon: 'torch.png', canCraft: true, isNew: true, noteCategory: 9,
            materialsJson: '[{"name":"Log","need":1,"have":5,"hasEnough":true}]' }
        ]
        """;

    private static readonly Dictionary<string, string> Marks = new() { ["crate"] = "Makes first: Plank ×2" };
    private static readonly KeepView NoKeep = new(false, Array.Empty<KeepTarget>(), new Dictionary<string, int>());

    private static readonly QueueEntry[] TwoOrders = { new("crate", "Crate", 2, Id: 4), new("rope", "Rope", 1, Id: 9) };

    private static readonly QueueOutlook TwoOrdersOutlook = new(
        new[] { new OrderOutlook(4, OrderState.Ready, "Ready"), new OrderOutlook(9, OrderState.Short, "Short: Fibre x2") },
        new[] { new MaterialTotal(Bench.Fibre, "Fibre", 3, 1, 2), new MaterialTotal(Bench.Log, "Log", 2, 5, 0) },
        "Heads up: Rope is short of Fibre x2. The queue will stop there.");

    // ---------------------------------------------------------------- the recipe list

    [Fact]
    public void The_hub_lists_every_recipe_in_the_games_group_order_and_leaves_the_games_own_list_in_the_page()
    {
        var page = BenchWithRecipes();

        Assert.Equal("plank,rope,crate,shirt,torch", page.Text("cardKeys()"));
        Assert.Equal(5, page.Number("$('recipe-list').querySelectorAll('.recipe-item').length"));
        Assert.True(page.Truth("benchDoc().querySelector('.notes-section') !== null"));
        Assert.Contains(".combined-panel.cc-hub .notes-section{display:none!important}", page.Text("$('cc-style').textContent"));
        Assert.Equal("5 of 5", page.Text("$('cc-count').textContent"));
    }

    [Fact]
    public void Search_finds_a_recipe_by_its_name_or_by_one_of_its_materials()
    {
        var page = BenchWithRecipes();

        page.Type("torch");

        Assert.Equal("torch", page.Text("cardKeys()"));

        page.Type("FIBRE");

        Assert.Equal("rope,shirt", page.Text("cardKeys()"));
        Assert.True(page.Truth("cardPart('rope', '.cc-mat').classList.contains('hit')"));
        Assert.Equal(0, page.Number("benchDoc().querySelectorAll('.cc-ghead').length"));
    }

    [Fact]
    public void A_search_with_no_match_says_so()
    {
        var page = BenchWithRecipes();

        page.Type("anvil");

        Assert.Equal("", page.Text("cardKeys()"));
        Assert.Equal("No recipes match \"anvil\".", page.Text("$('cc-list').textContent"));
    }

    [Fact]
    public void The_search_text_its_focus_and_the_scroll_survive_the_game_redrawing_its_list()
    {
        var page = BenchWithRecipes();

        page.Run("$('cc-search').focus(); var searchBox = $('cc-search')");
        page.Type("plan");
        page.Run("$('cc-list').scrollTop = 120");
        page.RecipeMsg(Recipes);

        Assert.Equal(2, page.Number("bench().renders"));
        Assert.True(page.Truth("searchBox === $('cc-search')"));
        Assert.Equal("plan", page.Text("$('cc-search').value"));
        Assert.True(page.Truth("benchDoc().activeElement === $('cc-search')"));
        Assert.Equal(120, page.Number("$('cc-list').scrollTop"));
        Assert.Equal("plank,crate", page.Text("cardKeys()"));
    }

    [Fact]
    public void A_new_search_starts_the_list_from_the_top()
    {
        var page = BenchWithRecipes();

        page.Run("$('cc-list').scrollTop = 120");
        page.Type("r");

        Assert.Equal(0, page.Number("$('cc-list').scrollTop"));
    }

    [Fact]
    public void The_list_shows_what_the_game_sends_after_each_redraw()
    {
        var page = BenchWithRecipes();

        page.RecipeMsg("[{ recipeKey: 'rope', name: 'Rope', canCraft: true, noteCategory: 1, materialsJson: '[]' }]");

        Assert.Equal("rope", page.Text("cardKeys()"));
        Assert.Equal("Can make now", page.Text("cardPart('rope', '.cc-card-st').textContent"));
    }

    [Fact]
    public void A_card_shows_the_materials_the_game_sends_next_even_for_the_same_recipes()
    {
        var page = new PageScript();

        page.OpenBench();
        page.RecipeMsg("""[{ recipeKey: 'plank', name: 'Plank', canCraft: true, noteCategory: 1, materialsJson: '[{"name":"Log","need":1,"have":5,"hasEnough":true}]' }]""");
        page.Show(View());

        Assert.Equal("1/5", page.Text("cardPart('plank', '.cc-mat').textContent"));

        page.RecipeMsg("""[{ recipeKey: 'plank', name: 'Plank', canCraft: false, noteCategory: 1, materialsJson: '[{"name":"Log","need":1,"have":0,"hasEnough":false}]' }]""");

        Assert.Equal("1/0", page.Text("cardPart('plank', '.cc-mat').textContent"));
        Assert.Equal("cc-mat no", page.Text("cardPart('plank', '.cc-mat').className"));
        Assert.Equal("Short: Log 1", page.Text("cardPart('plank', '.cc-card-st').textContent"));
    }

    [Fact]
    public void A_card_counts_what_is_held_from_the_joined_material_list_when_the_fixed_list_carries_no_counts()
    {
        // The page joins the fixed list (materialsJson: no have, no hasEnough) with the counts sent apart into _mats.
        var page = new PageScript();

        page.OpenBench();
        page.RecipeMsg("""[{ recipeKey: 'plank', name: 'Plank', canCraft: true, noteCategory: 1, materialsHave: '5', materialsJson: '[{"name":"Log","need":1}]', _mats: [{ name: 'Log', need: 1, have: 5, hasEnough: true }] }]""");
        page.Show(View());

        Assert.Equal("1/5", page.Text("cardPart('plank', '.cc-mat').textContent"));
        Assert.Equal("Plank", page.Text("cardPart('plank', '.cc-card-name').textContent"));

        page.RecipeMsg("""[{ recipeKey: 'plank', name: 'Plank', canCraft: false, noteCategory: 1, materialsHave: '0', materialsJson: '[{"name":"Log","need":1}]', _mats: [{ name: 'Log', need: 1, have: 0, hasEnough: false }] }]""");

        Assert.Equal("1/0", page.Text("cardPart('plank', '.cc-mat').textContent"));
        Assert.Equal("Short: Log 1", page.Text("cardPart('plank', '.cc-card-st').textContent"));
    }

    [Fact]
    public void With_Stacking_a_recipe_whose_materials_are_all_owned_can_be_made_even_when_the_game_says_not()
    {
        var page = BenchWithRecipes();

        page.Show(View(takesUnits: true));
        page.RecipeMsg("""[{ recipeKey: 'box', name: 'Box', canCraft: false, noteCategory: 1, materialsJson: '[{"name":"Scrap","need":3,"have":68,"hasEnough":true},{"name":"Wire","need":1,"have":1,"hasEnough":true}]' }, { recipeKey: 'rod', name: 'Rod', canCraft: false, noteCategory: 1, materialsJson: '[{"name":"Scrap","need":3,"have":68,"hasEnough":true},{"name":"Wire","need":2,"have":1,"hasEnough":false}]' }]""");

        Assert.Equal("Can make now", page.Text("cardPart('box', '.cc-card-st').textContent"));
        Assert.Equal("Short: Wire 1", page.Text("cardPart('rod', '.cc-card-st').textContent"));
    }

    [Fact]
    public void Without_Stacking_the_game_decides_whether_a_recipe_can_be_made_from_owned_materials()
    {
        var page = BenchWithRecipes();

        page.RecipeMsg("""[{ recipeKey: 'box', name: 'Box', canCraft: false, noteCategory: 1, materialsJson: '[{"name":"Scrap","need":3,"have":68,"hasEnough":true}]' }]""");

        Assert.Equal("Cannot make now", page.Text("cardPart('box', '.cc-card-st').textContent"));
    }

    [Fact]
    public void A_recipe_unlocked_under_the_same_key_can_be_laid_on_the_bench_and_queued()
    {
        var page = new PageScript();
        const string shirt = """recipeKey: 'shirt', name: 'Shirt', canCraft: true, noteCategory: 3, materialsJson: '[{"name":"Fibre","need":6,"have":9,"hasEnough":true}]'""";

        page.OpenBench();
        page.RecipeMsg("[{ " + shirt + ", craftLocked: true, craftLockText: 'Needs a loom' }]");
        page.Show(View());

        Assert.True(page.Truth("cardPart('shirt', '.cc-add').disabled"));

        page.RecipeMsg("[{ " + shirt + " }]");

        Assert.Equal("Can make now", page.Text("cardPart('shirt', '.cc-card-st').textContent"));
        Assert.False(page.Truth("cardPart('shirt', '.cc-add').disabled"));

        page.Press("cardPart('shirt', '.cc-card-name')");

        Assert.Equal("shirt", page.Text("sentActions('RECIPE_CLICK')[0].data.recipeKey"));
    }

    [Fact]
    public void The_search_box_tells_the_game_when_it_has_focus_so_typing_does_not_move_the_player()
    {
        var page = BenchWithRecipes();

        page.Run("$('cc-search').focus()");
        page.Run("$('cc-search').blur()");

        Assert.Equal("[true,false]", page.Text("JSON.stringify(sentActions('SEARCH_FOCUS').map(function (e) { return e.data.focused; }))"));
    }

    [Fact]
    public void Keys_typed_into_the_search_never_reach_the_games_key_handling_and_enter_leaves_the_box()
    {
        var page = BenchWithRecipes();

        page.Run("var heard = 0; benchDoc().addEventListener('keydown', function () { heard += 1; }); $('cc-search').focus()");
        page.Run("fire($('cc-search'), 'keydown', { key: 'w' }); fire($('cc-search'), 'keydown', { key: 'Enter' })");

        Assert.Equal(0, page.Number("heard"));
        Assert.True(page.Truth("benchDoc().activeElement === null"));
        Assert.Equal("[true,false]", page.Text("JSON.stringify(sentActions('SEARCH_FOCUS').map(function (e) { return e.data.focused; }))"));
    }

    [Fact]
    public void Clearing_the_search_shows_everything_and_keeps_the_box_ready_to_type()
    {
        var page = BenchWithRecipes();

        page.Type("torch");
        page.Press("benchDoc().querySelector('.cc-search-clear')");

        Assert.Equal("plank,rope,crate,shirt,torch", page.Text("cardKeys()"));
        Assert.Equal("", page.Text("$('cc-search').value"));
        Assert.True(page.Truth("benchDoc().activeElement === $('cc-search')"));
    }

    [Fact]
    public void Each_filter_counts_every_recipe_of_its_kind_and_shows_only_those()
    {
        var page = BenchWithRecipes();

        Assert.Equal("All 5", page.Text("filterChip('All').textContent"));
        Assert.Equal("Can make 2", page.Text("filterChip('Can make').textContent"));
        Assert.Equal("Parts first 1", page.Text("filterChip('Parts first').textContent"));
        Assert.Equal("Short 1", page.Text("filterChip('Short').textContent"));
        Assert.True(page.Truth("filterChip('Kept') === null"));

        page.Press("filterChip('Can make')");

        Assert.Equal("plank,torch", page.Text("cardKeys()"));
        Assert.Equal("2 of 5", page.Text("$('cc-count').textContent"));

        page.Press("filterChip('Parts first')");

        Assert.Equal("crate", page.Text("cardKeys()"));

        page.Press("filterChip('Short')");

        Assert.Equal("rope", page.Text("cardKeys()"));
    }

    [Fact]
    public void The_kept_filter_shows_kept_recipes_and_falls_back_to_all_when_keeping_is_turned_off()
    {
        var page = BenchWithRecipes();
        var keep = new KeepView(true, new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5) }, new Dictionary<string, int> { ["rope"] = 5 });

        page.Show(View(keep: keep));
        page.Press("filterChip('Kept')");

        Assert.Equal("rope", page.Text("cardKeys()"));

        page.Show(View());

        Assert.Equal("plank,rope,crate,shirt,torch", page.Text("cardKeys()"));
    }

    [Fact]
    public void A_group_chip_shows_only_that_group_with_how_many_of_it_you_can_make()
    {
        var page = BenchWithRecipes();

        Assert.Equal("Group 1 1", page.Text("groupChip('Group 1').textContent"));
        Assert.True(page.Truth("groupChip('Group 4') === null"));

        page.Press("groupChip('Group 1')");

        Assert.Equal("plank,rope", page.Text("cardKeys()"));
        Assert.Equal(0, page.Number("benchDoc().querySelectorAll('.cc-ghead').length"));
    }

    [Fact]
    public void The_group_row_stays_on_one_line_however_many_groups_there_are_while_the_filters_wrap()
    {
        var page = BenchWithRecipes();

        Assert.True(page.Truth("$('cc-groups').classList.contains('cc-chips')"));
        Assert.True(page.Truth("$('cc-groups').classList.contains('cc-slide')"));
        Assert.Contains("flex-wrap:nowrap", page.Text("cssRule('.cc-chips.cc-slide')"));
        Assert.Contains("overflow-x:auto", page.Text("cssRule('.cc-chips.cc-slide')"));
        Assert.Contains("flex:none", page.Text("cssRule('.cc-fchip')"));
        Assert.Contains("white-space:nowrap", page.Text("cssRule('.cc-fchip')"));
        Assert.False(page.Truth("$('cc-filters').classList.contains('cc-slide')"));
        Assert.Contains("flex-wrap:wrap", page.Text("cssRule('.cc-chips')"));
    }

    [Fact]
    public void The_mouse_wheel_over_the_group_row_scrolls_it_sideways_and_stops_at_its_ends()
    {
        var page = BenchWithRecipes();

        page.Run("$('cc-groups').scrollWidth = 900; $('cc-groups').clientWidth = 600");
        page.Run("var wheel = fire(groupChip('Group 1'), 'wheel', { deltaY: 120 })");

        Assert.Equal(120, page.Number("$('cc-groups').scrollLeft"));
        Assert.True(page.Truth("wheel.defaultPrevented"));

        page.Run("fire(groupChip('Group 1'), 'wheel', { deltaY: 3, deltaMode: 1 })");

        Assert.Equal(168, page.Number("$('cc-groups').scrollLeft"));

        page.Run("fire(groupChip('Group 1'), 'wheel', { deltaY: 1000 })");

        Assert.Equal(300, page.Number("$('cc-groups').scrollLeft"));

        page.Run("fire(groupChip('Group 1'), 'wheel', { deltaY: -1000 })");

        Assert.Equal(0, page.Number("$('cc-groups').scrollLeft"));
    }

    [Fact]
    public void A_sideways_swipe_over_the_group_row_scrolls_it_by_the_swipe_not_the_small_vertical_drift()
    {
        var page = BenchWithRecipes();

        page.Run("$('cc-groups').scrollWidth = 900; $('cc-groups').clientWidth = 600");
        page.Run("fire(groupChip('Group 1'), 'wheel', { deltaX: 80, deltaY: 5 })");

        Assert.Equal(80, page.Number("$('cc-groups').scrollLeft"));
    }

    [Fact]
    public void A_group_row_that_fits_leaves_the_wheel_to_scroll_the_page()
    {
        var page = BenchWithRecipes();

        page.Run("$('cc-groups').scrollWidth = 500; $('cc-groups').clientWidth = 600");
        page.Run("var wheel = fire(groupChip('Group 1'), 'wheel', { deltaY: 120 })");

        Assert.Equal(0, page.Number("$('cc-groups').scrollLeft"));
        Assert.False(page.Truth("wheel.defaultPrevented"));
    }

    [Fact]
    public void Picking_a_group_chip_keeps_the_row_where_it_was_scrolled()
    {
        var page = BenchWithRecipes();

        page.Run("$('cc-groups').scrollWidth = 900; $('cc-groups').clientWidth = 600; $('cc-groups').scrollLeft = 200");
        page.Press("groupChip('Group 3')");

        Assert.Equal("shirt", page.Text("cardKeys()"));
        Assert.Equal(200, page.Number("$('cc-groups').scrollLeft"));
    }

    [Fact]
    public void Collapsing_a_group_goes_through_the_games_own_setting_so_it_is_remembered()
    {
        var page = BenchWithRecipes();

        Assert.Contains("1 / 2 can make", page.Text("benchDoc().querySelectorAll('.cc-ghead')[0].textContent"));

        page.Press("benchDoc().querySelectorAll('.cc-ghead')[0]");

        Assert.Equal("crate,shirt,torch", page.Text("cardKeys()"));
        Assert.True(page.Truth("bench().values.collapsedCats.has(1)"));
        Assert.Equal("{\"cat\":1,\"collapsed\":true}", page.Text("JSON.stringify(sentActions('NOTE_CAT_TOGGLE')[0].data)"));

        page.Press("benchDoc().querySelectorAll('.cc-ghead')[0]");

        Assert.Equal("plank,rope,crate,shirt,torch", page.Text("cardKeys()"));
        Assert.Equal("{\"cat\":1,\"collapsed\":false}", page.Text("JSON.stringify(sentActions('NOTE_CAT_TOGGLE')[1].data)"));
    }

    [Fact]
    public void Each_card_says_whether_it_can_be_made_and_what_is_missing()
    {
        var page = BenchWithRecipes();

        Assert.Equal("Can make now", page.Text("cardPart('plank', '.cc-card-st').textContent"));
        Assert.Equal("Makes first: Plank x2", page.Text("cardPart('crate', '.cc-card-st').textContent"));
        Assert.Equal("Short: Fibre 2", page.Text("cardPart('rope', '.cc-card-st').textContent"));
        Assert.Equal("Needs a loom", page.Text("cardPart('shirt', '.cc-card-st').textContent"));
        Assert.Equal("cc-mat via", page.Text("cardPart('crate', '.cc-mat').className"));
        Assert.Equal("cc-mat no", page.Text("cardPart('rope', '.cc-mat').className"));
    }

    [Fact]
    public void The_amount_on_a_card_scales_its_materials_and_is_what_add_queues()
    {
        var page = BenchWithRecipes();

        for (var i = 0; i < 5; i++) page.Press("cardPart('plank', '.cc-more')");

        Assert.Equal("6/5", page.Text("cardPart('plank', '.cc-mat').textContent"));
        Assert.Equal("cc-mat no", page.Text("cardPart('plank', '.cc-mat').className"));

        page.Press("cardPart('plank', '.cc-add')");

        Assert.Equal(new PageCommand[] { new PageCommand.Add("plank", 6) }, Sent(page));
        Assert.Equal(0, page.Number("sentActions('RECIPE_CLICK').length"));
    }

    [Fact]
    public void A_card_amount_never_goes_below_one()
    {
        var page = BenchWithRecipes();

        page.Press("cardPart('plank', '.cc-less')");
        page.Press("cardPart('plank', '.cc-add')");

        Assert.Equal(new PageCommand[] { new PageCommand.Add("plank", 1) }, Sent(page));
    }

    [Fact]
    public void Clicking_a_cards_picture_or_name_lays_its_recipe_on_the_bench_the_way_the_games_own_row_does()
    {
        var page = BenchWithRecipes();

        page.Press("cardPart('plank', '.cc-card-name')");

        Assert.Equal("plank", page.Text("sentActions('RECIPE_CLICK')[0].data.recipeKey"));
        Assert.True(page.Truth("card('plank').classList.contains('cc-sel')"));

        page.Press("cardPart('crate', '.cc-card-ic')");

        Assert.Equal("crate", page.Text("sentActions('RECIPE_CLICK')[1].data.recipeKey"));
        Assert.True(page.Truth("card('crate').classList.contains('cc-sel')"));
        Assert.False(page.Truth("card('plank').classList.contains('cc-sel')"));
    }

    [Fact]
    public void Clicking_the_rest_of_a_card_leaves_the_bench_alone()
    {
        var page = BenchWithRecipes();

        page.Press("cardPart('plank', '.cc-val')");
        page.Press("cardPart('plank', '.cc-mat')");
        page.Press("cardPart('plank', '.cc-card-foot')");
        page.Press("cardPart('plank', '.cc-card-st')");
        page.Press("card('plank')");

        Assert.Equal(0, page.Number("sentActions('RECIPE_CLICK').length"));
    }

    [Fact]
    public void A_recipe_is_not_laid_on_the_bench_by_hand_while_the_queue_holds_it()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, isRunning: true, outlook: TwoOrdersOutlook, activeOrderId: 4));
        page.Press("cardPart('plank', '.cc-card-name')");
        page.Show(View(TwoOrders, isRunning: true, isPaused: true, outlook: TwoOrdersOutlook, activeOrderId: 4));
        page.Press("cardPart('plank', '.cc-card-ic')");

        Assert.Equal(0, page.Number("sentActions('RECIPE_CLICK').length"));

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Press("cardPart('plank', '.cc-card-name')");

        Assert.Equal(1, page.Number("sentActions('RECIPE_CLICK').length"));
    }

    [Fact]
    public void A_locked_recipe_cannot_be_laid_on_the_bench_or_queued()
    {
        var page = BenchWithRecipes();

        page.Press("cardPart('shirt', '.cc-card-name')");
        page.Press("cardPart('shirt', '.cc-card-ic')");
        page.Press("cardPart('shirt', '.cc-add')");

        Assert.Equal(0, page.Number("sentActions('RECIPE_CLICK').length"));
        Assert.True(page.Truth("cardPart('shirt', '.cc-add').disabled"));
        Assert.Empty(Sent(page));
    }

    [Fact]
    public void A_recipe_on_the_bench_that_needs_parts_offers_to_make_them_with_the_amount_set_on_its_card()
    {
        var page = BenchWithRecipes();

        // The bag cannot make a crate yet, so the page holds its own quantity box at 1.
        page.Run("bench().values.maxCraftCount = 0");
        page.Press("cardPart('crate', '.cc-more')");
        page.Press("cardPart('crate', '.cc-more')");
        page.Press("cardPart('crate', '.cc-more')");
        page.Press("cardPart('crate', '.cc-card-name')");

        Assert.Equal("Makes first: Plank x2", page.Text("$('cc-panel').querySelector('.cc-plan').textContent"));

        page.Press("$('cc-panel').querySelector('.btn')");

        Assert.Equal(new PageCommand[] { new PageCommand.CraftNow("crate", 4) }, Sent(page));
    }

    [Fact]
    public void The_bell_on_a_card_keeps_one_shows_the_kept_count_and_pressed_again_stops_keeping()
    {
        var page = BenchWithRecipes();

        page.Show(View(keep: new KeepView(true, Array.Empty<KeepTarget>(), new Dictionary<string, int>())));
        page.Press("cardPart('plank', '.cc-pin')");
        page.Show(View(keep: new KeepView(true, new[] { new KeepTarget(Bench.Plank, "plank", "Plank", 1) }, new Dictionary<string, int> { ["plank"] = 1 })));

        Assert.Equal("1", page.Text("cardPart('plank', '.cc-pin').textContent"));
        Assert.True(page.Truth("cardPart('plank', '.cc-pin').classList.contains('on')"));
        Assert.Equal("", page.Text("cardPart('rope', '.cc-pin').textContent"));

        page.Press("cardPart('plank', '.cc-pin')");

        Assert.Equal(new PageCommand[] { new PageCommand.Keep("plank", 1), new PageCommand.Keep("plank", 0) }, Sent(page));
    }

    [Fact]
    public void A_cards_name_has_its_own_line_and_wraps_rather_than_being_cut_short()
    {
        var page = BenchWithRecipes();

        Assert.Equal("TorchNEW", page.Text("cardPart('torch', '.cc-card-name').textContent"));
        Assert.True(page.Truth("cardPart('torch', '.cc-card-name').parentElement.matches('.cc-card-head')"));
        Assert.True(page.Truth("cardPart('torch', '.cc-card-head').parentElement === card('torch')"));
        Assert.True(page.Truth("cardPart('torch', '.cc-card-head').querySelector('.cc-add') === null"));
        Assert.DoesNotContain("nowrap", page.Text("cssRule('.cc-card-name')"));
        Assert.DoesNotContain("ellipsis", page.Text("cssRule('.cc-card-name')"));
        Assert.Contains("overflow-wrap:break-word", page.Text("cssRule('.cc-card-name')"));
    }

    [Fact]
    public void A_cards_materials_have_their_own_row_and_cost_keep_amount_and_add_share_the_bottom_row()
    {
        var page = BenchWithRecipes();

        page.Show(WithStamina(View(keep: new KeepView(true, Array.Empty<KeepTarget>(), new Dictionary<string, int>())), "{\"have\":42,\"out\":false,\"cost\":{\"plank\":3}}"));
        page.Run("var foot = cardPart('plank', '.cc-add').parentElement");

        Assert.True(page.Truth("foot.matches('.cc-card-foot')"));
        Assert.True(page.Truth("foot.parentElement === card('plank')"));
        Assert.True(page.Truth("cardPart('plank', '.cc-more').parentElement.parentElement === foot"));
        Assert.True(page.Truth("cardPart('plank', '.cc-pin').parentElement === foot"));
        Assert.True(page.Truth("cardPart('plank', '.cc-cost').parentElement === foot"));
        Assert.True(page.Truth("cardPart('plank', '.cc-mat').parentElement.parentElement === card('plank')"));
        Assert.True(page.Truth("foot.querySelector('.cc-mat') === null"));
        Assert.True(page.Truth("foot.querySelector('.cc-card-name') === null"));
        Assert.Contains("grid-column:1/-1", page.Text("cssRule('.cc-card-foot')"));
    }

    [Fact]
    public void A_cards_bottom_row_sits_at_its_bottom_so_add_lines_up_beside_a_card_whose_name_wraps()
    {
        var page = BenchWithRecipes();

        Assert.True(page.Truth("card('plank').children[card('plank').children.length - 1] === cardPart('plank', '.cc-card-foot')"));
        Assert.Contains("align-content:space-between", page.Text("cssRule('.cc-card')"));
    }

    // ---------------------------------------------------------------- the queue

    [Fact]
    public void Each_order_shows_its_crafts_and_where_it_stands()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("2 crafts", page.Text("queueRow(0).querySelector('.cc-qsub').textContent"));
        Assert.Equal("Ready", page.Text("queueRow(0).querySelector('.cc-qst').textContent"));
        Assert.Equal("Short: Fibre x2", page.Text("queueRow(1).querySelector('.cc-qst').textContent"));
        Assert.True(page.Truth("queueRow(1).classList.contains('short')"));
        Assert.Equal("2 orders, 3 crafts", page.Text("$('cc-actions').querySelector('.cc-dim').textContent"));
    }

    [Fact]
    public void A_top_up_order_says_so_beside_its_crafts_where_a_long_name_cannot_cut_it_off()
    {
        var page = BenchWithRecipes();

        page.Show(View(new[] { new QueueEntry("plank", "Plank", 3, IsTopUp: true, Id: 2) }));

        Assert.Equal("Plank", page.Text("queueRow(0).querySelector('.cc-qname').textContent"));
        Assert.True(page.Truth("queueRow(0).querySelector('.cc-tag').parentElement === queueRow(0).querySelector('.cc-qsub')"));
        Assert.Equal("Top-up", page.Text("queueRow(0).querySelector('.cc-tag').textContent"));
        Assert.Contains("-webkit-line-clamp:2", page.Text("cssRule('.cc-qname')"));
        Assert.DoesNotContain("nowrap", page.Text("cssRule('.cc-qname')"));
    }

    [Fact]
    public void An_order_of_a_recipe_that_makes_several_says_how_many_it_makes()
    {
        var page = BenchWithRecipes();

        page.Show(View(new[] { new QueueEntry("plank", "Plank", 3, Id: 2) }, yields: new Dictionary<string, int> { ["plank"] = 2 }));

        Assert.Equal("3 crafts, makes 6", page.Text("queueRow(0).querySelector('.cc-qsub').textContent"));
        Assert.Equal("x2", page.Text("cardPart('plank', '.cc-yield').textContent"));
    }

    [Fact]
    public void An_order_row_has_no_number_or_arrows_only_the_drag_handle_to_move_it()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.True(page.Truth("queueRow(0).querySelector('.cc-handle') !== null"));
        Assert.Equal(0, page.Number("$('cc-qlist').querySelectorAll('.cc-up').length + $('cc-qlist').querySelectorAll('.cc-down').length + $('cc-qlist').querySelectorAll('.cc-num').length"));
    }

    [Fact]
    public void Dragging_an_order_by_its_handle_onto_another_moves_it_there()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Run("fire(queueRow(0).querySelector('.cc-handle'), 'mousedown'); fire(queueRow(1), 'mousemove'); fire(queueRow(1), 'mouseup'); advance(1)");

        Assert.Equal(new PageCommand[] { new PageCommand.Move(4, 1) }, Sent(page));
    }

    [Fact]
    public void A_drag_dropped_where_it_started_moves_nothing()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Run("fire(queueRow(1).querySelector('.cc-handle'), 'mousedown'); fire(queueRow(1), 'mousemove'); fire(queueRow(1), 'mouseup'); advance(1)");

        Assert.Empty(Sent(page));
    }

    [Fact]
    public void A_drag_let_go_of_while_the_page_has_lost_focus_moves_nothing_on_the_next_release()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Run("fire(queueRow(0).querySelector('.cc-handle'), 'mousedown'); fire(queueRow(1), 'mousemove'); blurBench()");

        Assert.False(page.Truth("queueRow(0).classList.contains('drag') || queueRow(1).classList.contains('drop')"));

        page.Run("fire(queueRow(1), 'mousemove'); fire(queueRow(1), 'mouseup'); advance(1)");

        Assert.Empty(Sent(page));
    }

    [Fact]
    public void A_drag_that_leaves_the_page_moves_nothing_but_one_crossing_between_rows_still_does()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Run("fire(queueRow(0).querySelector('.cc-handle'), 'mousedown'); fire(queueRow(1), 'mousemove'); fire(queueRow(0), 'mouseout', { relatedTarget: queueRow(1) }); fire(queueRow(1), 'mouseup'); advance(1)");

        Assert.Equal(new PageCommand[] { new PageCommand.Move(4, 1) }, Sent(page));

        page.Run("fire(queueRow(0).querySelector('.cc-handle'), 'mousedown'); fire(queueRow(1), 'mousemove'); leavePage(); fire(queueRow(1), 'mouseup'); advance(1)");

        Assert.Single(Sent(page));
    }

    [Fact]
    public void An_orders_amount_counts_every_click_before_the_redraw_and_never_reaches_zero()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Press("queueRow(0).querySelector('.cc-more')");
        page.Press("queueRow(0).querySelector('.cc-more')");
        page.Press("queueRow(0).querySelector('.cc-less')");
        page.Press("queueRow(1).querySelector('.cc-less')");

        Assert.Equal(new PageCommand[] { new PageCommand.Amount(4, 3), new PageCommand.Amount(4, 4), new PageCommand.Amount(4, 3) }, Sent(page));
    }

    [Fact]
    public void The_x_removes_its_own_order_by_id_wherever_that_order_now_sits()
    {
        var page = BenchWithRecipes();
        var ropeFirst = new[] { TwoOrders[1], TwoOrders[0] };

        page.Show(View(ropeFirst, isRunning: true, outlook: TwoOrdersOutlook, activeOrderId: 4));
        page.Press("queueRow(0).querySelector('.cc-x')");
        page.Press("queueRow(1).querySelector('.cc-x')");

        Assert.Equal(new PageCommand[] { new PageCommand.Remove(9), new PageCommand.Remove(4) }, Sent(page));
    }

    [Fact]
    public void The_queue_is_started_and_cleared_from_its_own_buttons_which_wait_for_an_order()
    {
        var page = BenchWithRecipes();

        page.Press("$('cc-actions').querySelector('.cc-go')");
        page.Press("$('cc-actions').querySelector('.cc-clear')");
        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Press("$('cc-actions').querySelector('.cc-go')");
        page.Press("$('cc-actions').querySelector('.cc-clear')");

        Assert.Equal(new PageCommand[] { new PageCommand.Start(), new PageCommand.Clear() }, Sent(page));
    }

    [Fact]
    public void A_running_queue_offers_stop_instead_of_start()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, isRunning: true, outlook: TwoOrdersOutlook));

        Assert.True(page.Truth("$('cc-actions').querySelector('.cc-go') === null"));
        Assert.Equal("Crafting", page.Text("$('cc-pill').textContent"));

        page.Press("$('cc-actions').querySelector('.cc-stop')");

        Assert.Equal(new PageCommand[] { new PageCommand.Stop() }, Sent(page));
    }

    [Fact]
    public void Before_start_the_heads_up_names_where_the_queue_will_stop()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("Heads up: Rope is short of Fibre x2. The queue will stop there.", page.Text("$('cc-notes').querySelector('.cc-banner.bad').textContent"));

        page.Show(View(TwoOrders, isRunning: true, outlook: TwoOrdersOutlook));

        Assert.True(page.Truth("$('cc-notes').querySelector('.cc-banner.bad') === null"));
    }

    [Fact]
    public void A_paused_queue_shows_the_runs_own_word_on_where_it_carries_on()
    {
        var page = BenchWithRecipes();
        const string elsewhere = "the queue carries on at the workbench it was running on; press Stop, then Craft queue, to run it here";

        page.Show(View(TwoOrders, isRunning: true, isPaused: true, outlook: TwoOrdersOutlook, status: elsewhere));

        Assert.Equal("Paused", page.Text("$('cc-pill').textContent"));
        Assert.Equal(elsewhere, page.Text("$('cc-notes').textContent"));
        Assert.True(page.Truth("$('cc-notes').querySelector('.cc-banner') !== null"));
    }

    [Fact]
    public void A_button_refused_while_paused_says_why()
    {
        var page = BenchWithRecipes();
        const string refused = "the queue is paused and carries on here in a moment; press Stop first to craft this now";

        page.Show(View(TwoOrders, isRunning: true, isPaused: true, outlook: TwoOrdersOutlook, status: refused));

        Assert.Equal(refused, page.Text("$('cc-notes').textContent"));
    }

    [Fact]
    public void The_running_orders_bar_follows_the_games_own_craft_progress()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, isRunning: true, outlook: TwoOrdersOutlook, activeOrderId: 4));
        page.Run("$('craft-progress').style.transform = 'scaleX(0.5)'");
        page.Poll();

        Assert.Equal("scaleX(0.5)", page.Text("$('cc-prog').style.transform"));
        Assert.True(page.Truth("queueRow(0).contains($('cc-prog'))"));
    }

    [Fact]
    public void The_bar_stays_with_the_order_being_crafted_after_another_is_moved_above_it()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, isRunning: true, outlook: TwoOrdersOutlook, activeOrderId: 9));

        Assert.True(page.Truth("queueRow(1).contains($('cc-prog'))"));
        Assert.False(page.Truth("queueRow(0).contains($('cc-prog'))"));
    }

    // ---------------------------------------------------------------- the bench as a tab of the bag panel

    [Fact]
    public void The_bench_moves_into_the_bag_panel_as_a_tab_after_the_bags_own_and_the_screen_opens_on_the_bag()
    {
        var page = BenchWithRecipes();

        Assert.True(page.Truth("benchDoc().querySelector('.backpack-panel').contains(benchDoc().querySelector('.workbench-section'))"));
        Assert.False(page.Truth("benchDoc().querySelector('.combined-panel').contains(benchDoc().querySelector('.workbench-section'))"));
        Assert.True(page.Truth("$('tab-bag').nextSibling === $('cc-tab-bench')"));
        Assert.True(page.Truth("$('cc-tab-bench').classList.contains('left-tab')"));
        Assert.Equal("Bench", page.Text("$('cc-tab-bench').textContent"));
        Assert.False(page.Truth("isBenchShown()"));
        Assert.False(page.Truth("$('cc-tab-bench').classList.contains('active')"));
        Assert.Contains("display:none", page.Text("cssRule('.backpack-panel.cc-tabbed:not(.cc-bench-view)>.workbench-section')"));
    }

    [Fact]
    public void The_workbench_tab_shows_the_bench_and_moves_the_bag_grid_out_of_reach_and_the_bag_tab_brings_the_bag_back()
    {
        var page = BenchWithRecipes();
        var fitsBefore = page.Number("bench().fits");

        page.Press("$('cc-tab-bench')");

        Assert.True(page.Truth("isBenchShown()"));
        Assert.True(page.Truth("$('cc-tab-bench').classList.contains('active')"));
        Assert.Contains("left:-20000px", page.Text("cssRule('.backpack-panel.cc-bench-view>#backpack-grid,.backpack-panel.cc-bench-view>#rack-rows')"));
        Assert.True(page.Number("bench().fits") > fitsBefore);

        page.Press("$('tab-bag')");

        Assert.False(page.Truth("isBenchShown()"));
        Assert.False(page.Truth("$('cc-tab-bench').classList.contains('active')"));
    }

    [Fact]
    public void Any_of_the_games_own_tabs_leaves_the_bench()
    {
        var page = BenchWithRecipes();

        page.Press("$('cc-tab-bench')");
        page.Press("$('tab-drawer')");

        Assert.False(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void The_bench_stays_up_when_the_game_redraws_its_list_and_the_hub_redraws()
    {
        var page = BenchWithRecipes();

        page.Press("$('cc-tab-bench')");
        page.RecipeMsg(Recipes);
        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Poll();

        Assert.True(page.Truth("isBenchShown()"));
        Assert.True(page.Truth("$('cc-tab-bench').classList.contains('active')"));
        Assert.Equal(1, page.Number("benchDoc().querySelectorAll('#cc-tab-bench').length"));
    }

    [Fact]
    public void Picking_a_recipe_brings_the_bench_up_to_show_what_was_laid_on_it()
    {
        var page = BenchWithRecipes();

        page.Press("cardPart('plank', '.cc-card-name')");

        Assert.Equal("plank", page.Text("sentActions('RECIPE_CLICK')[0].data.recipeKey"));
        Assert.True(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void The_queue_laying_a_recipe_leaves_the_player_on_the_bag()
    {
        var page = BenchWithRecipes();

        page.Run("window.__craftinghub.act('recipe', 'crate')");

        Assert.False(page.Truth("isBenchShown()"));

        page.Press("cardPart('plank', '.cc-card-name')");

        Assert.True(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void Resting_on_a_tab_during_a_drag_switches_to_it_so_an_item_can_be_carried_between_bag_and_bench()
    {
        var page = BenchWithRecipes();

        page.Run("placeAt($('tab-bag'), 10, 10, 80, 30); placeAt($('cc-tab-bench'), 100, 10, 80, 30); gameDrag(true)");
        page.Run("moveMouse(120, 20)");
        page.Advance(399);

        Assert.False(page.Truth("isBenchShown()"));

        page.Run("moveMouse(130, 25)");
        page.Advance(1);

        Assert.True(page.Truth("isBenchShown()"));

        page.Run("moveMouse(40, 20)");
        page.Advance(400);

        Assert.False(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void Passing_over_a_tab_or_resting_on_it_without_a_drag_switches_nothing()
    {
        var page = BenchWithRecipes();

        page.Run("placeAt($('cc-tab-bench'), 100, 10, 80, 30)");
        page.Run("moveMouse(120, 20)");
        page.Advance(1000);

        Assert.False(page.Truth("isBenchShown()"));

        page.Run("gameDrag(true); moveMouse(120, 20)");
        page.Advance(200);
        page.Run("moveMouse(500, 500)");
        page.Advance(1000);

        Assert.False(page.Truth("isBenchShown()"));

        page.Run("moveMouse(120, 20)");
        page.Advance(200);
        page.Run("gameDrag(false)");
        page.Advance(1000);

        Assert.False(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void A_drag_resting_on_another_of_the_games_tabs_is_left_to_the_games_own_drop_on_a_tab()
    {
        var page = BenchWithRecipes();

        page.Run("placeAt($('tab-drawer'), 200, 10, 80, 30); gameDrag(true)");
        page.Press("$('cc-tab-bench')");
        page.Run("moveMouse(220, 20)");
        page.Advance(1000);

        Assert.True(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void The_bench_comes_up_when_a_craft_starts_on_it_but_a_player_who_goes_back_to_the_bag_stays_there()
    {
        var page = BenchWithRecipes();

        page.Run("$('crafting-overlay').style.display = 'flex'");
        page.Poll();

        Assert.True(page.Truth("isBenchShown()"));

        page.Press("$('tab-bag')");
        page.Poll();

        Assert.False(page.Truth("isBenchShown()"));

        page.Run("$('crafting-overlay').style.display = 'none'");
        page.Poll();
        page.Run("$('crafting-overlay').style.display = 'flex'");
        page.Poll();

        Assert.True(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void An_item_dropped_on_the_bench_grid_brings_the_bench_up_and_one_dropped_elsewhere_does_not()
    {
        var page = BenchWithRecipes();

        page.Run("bench().values.workbench.ownerId = 77");
        page.Run("bench().values.core.UnitySendEvent('ITEM_MOVE', { itemId: 5, fromOwnerId: 77, toOwnerId: 12, x: 0, y: 0 })");

        Assert.False(page.Truth("isBenchShown()"));

        page.Run("bench().values.core.UnitySendEvent('ITEM_MOVE', { itemId: 6, fromOwnerId: 12, toOwnerId: 77, x: 1, y: 0 })");

        Assert.True(page.Truth("isBenchShown()"));
        Assert.Equal(2, page.Number("sentActions('ITEM_MOVE').length"));
    }

    [Fact]
    public void A_craft_the_queue_starts_leaves_the_player_where_they_are_and_one_they_start_brings_the_bench_up()
    {
        var page = BenchWithRecipes();

        page.Run("window.__craftinghub.act('craft', 2)");
        page.Run("$('crafting-overlay').style.display = 'flex'");
        page.Poll();

        Assert.False(page.Truth("isBenchShown()"));

        page.Run("$('crafting-overlay').style.display = 'none'");
        page.Poll();
        page.Press("$('btn-craft')");
        page.Run("$('crafting-overlay').style.display = 'flex'");
        page.Poll();

        Assert.True(page.Truth("isBenchShown()"));
        Assert.Equal(2, page.Number("sentActions('CRAFT').length"));
    }

    [Fact]
    public void A_craft_that_comes_up_during_a_drag_brings_the_bench_up_only_after_the_drop_and_only_if_it_is_still_up()
    {
        var page = BenchWithRecipes();

        page.Press("$('btn-craft')");
        page.Run("gameDrag(true); $('crafting-overlay').style.display = 'flex'");
        page.Poll();

        Assert.False(page.Truth("isBenchShown()"));

        page.Run("gameDrag(false)");
        page.Poll();

        Assert.True(page.Truth("isBenchShown()"));

        page.Press("$('tab-bag')");
        page.Run("$('crafting-overlay').style.display = 'none'");
        page.Poll();
        page.Press("$('btn-craft')");
        page.Run("gameDrag(true); $('crafting-overlay').style.display = 'flex'");
        page.Poll();
        page.Run("$('crafting-overlay').style.display = 'none'");
        page.Poll();
        page.Run("gameDrag(false)");
        page.Poll();

        Assert.False(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void The_page_is_not_refitted_during_a_drag_but_is_once_the_item_is_dropped()
    {
        var page = BenchWithRecipes();
        var fitsBefore = page.Number("bench().fits");

        page.Run("gameDrag(true)");
        page.Press("$('cc-tab-bench')");
        page.Poll();

        Assert.True(page.Truth("isBenchShown()"));
        Assert.Equal(fitsBefore, page.Number("bench().fits"));

        page.Run("gameDrag(false)");
        page.Poll();

        Assert.Equal(fitsBefore + 1, page.Number("bench().fits"));
    }

    [Fact]
    public void A_drag_finds_the_grids_where_they_are_now_not_where_they_were_when_it_started()
    {
        var page = BenchWithRecipes();

        page.Run("placeAt($('workbench-grid'), 100, 100, 400, 200); var kept = { left: -20000, top: 0, right: -19600, bottom: 200 }");

        Assert.True(page.Truth("bench().values.workbench.isMouseOver(150, 150, kept)"));
        Assert.False(page.Truth("bench().values.workbench.isMouseOver(-19900, 150, kept)"));
        Assert.Equal("{\"row\":1,\"col\":2}", page.Text("JSON.stringify(bench().values.workbench.getGridCoordinates(180, 140, 1, 1, kept, 2))"));
    }

    [Fact]
    public void Resting_on_the_part_of_an_open_tab_scrolled_out_of_the_tab_strip_switches_nothing()
    {
        var page = BenchWithRecipes();

        page.Run("$('tab-bag').classList.remove('active'); $('tab-drawer').classList.add('active')");
        page.Run("placeAt($('leftTabsScroll'), 100, 10, 140, 30); placeAt($('tab-drawer'), 200, 10, 80, 30)");
        page.Press("$('cc-tab-bench')");
        page.Run("gameDrag(true); moveMouse(260, 20)");
        page.Advance(1000);

        Assert.True(page.Truth("isBenchShown()"));

        page.Run("moveMouse(220, 20)");
        page.Advance(400);

        Assert.False(page.Truth("isBenchShown()"));
    }

    // ---------------------------------------------------------------- when drawing fails

    [Fact]
    public void A_hub_that_cannot_draw_gives_the_game_its_own_list_back_and_returns_once_it_can()
    {
        var page = BenchWithRecipes();

        page.Run("benchDoc().createElement = function () { throw new Error('cannot draw'); }");
        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.False(page.Truth("benchDoc().querySelector('.combined-panel').classList.contains('cc-hub')"));
        Assert.True(page.Truth("$('cc-mid') === null && $('cc-queue') === null && $('cc-panel') === null"));
        Assert.True(page.Truth("$('cc-tab-bench') === null"));
        Assert.True(page.Truth("benchDoc().querySelector('.workbench-section').nextSibling === benchDoc().querySelector('.notes-section')"));
        Assert.Equal("backpack-panel", page.Text("benchDoc().querySelector('.backpack-panel').className"));

        page.Run("delete benchDoc().createElement");
        page.Poll();

        Assert.True(page.Truth("benchDoc().querySelector('.combined-panel').classList.contains('cc-hub')"));
        Assert.Equal("plank,rope,crate,shirt,torch", page.Text("cardKeys()"));
        Assert.True(page.Truth("benchDoc().querySelector('.combined-panel').contains(benchDoc().querySelector('.workbench-section'))"));
        Assert.True(page.Truth("$('cc-tab-bench') === null"));
    }

    [Fact]
    public void A_hub_that_keeps_failing_to_draw_leaves_the_bench_in_the_games_panel_instead_of_moving_it_at_every_try()
    {
        var page = BenchWithRecipes();

        page.Run("benchDoc().createElement = function () { throw new Error('cannot draw'); }");
        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        // From here the bench tab could be drawn again, but the search box never can.
        page.Run("benchDoc().createElement = function (tag) { if (tag === 'input') throw new Error('cannot draw'); return FakeDocument.prototype.createElement.call(this, tag); }");
        page.Run("var benchMoves = 0; var bagPanel = benchDoc().querySelector('.backpack-panel'); var bagAppend = bagPanel.appendChild; bagPanel.appendChild = function (child) { benchMoves += 1; return bagAppend.call(this, child); }");
        page.Poll();
        page.Poll();

        Assert.Equal(0, page.Number("benchMoves"));
        Assert.True(page.Truth("benchDoc().querySelector('.combined-panel').contains(benchDoc().querySelector('.workbench-section'))"));
        Assert.True(page.Truth("$('cc-tab-bench') === null"));
    }

    [Fact]
    public void A_hub_that_fails_to_draw_every_time_stops_trying_and_says_so_once_until_something_new_arrives()
    {
        var page = BenchWithRecipes();

        page.Run("var draws = 0; benchDoc().createElement = function () { draws += 1; throw new Error('cannot draw'); }");
        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        for (var i = 0; i < 10; i++) page.Poll();

        Assert.Equal(2, page.Number("draws"));
        Assert.Single(Diagnostics(page));
        Assert.Contains("could not draw on the workbench page", Diagnostics(page)[0].Text);

        page.Run("delete benchDoc().createElement");
        page.Poll();

        Assert.True(page.Truth("$('cc-mid') === null"));

        page.Show(View());

        Assert.False(page.Truth("$('cc-mid') === null"));
        Assert.Equal("plank,rope,crate,shirt,torch", page.Text("cardKeys()"));
    }

    // ---------------------------------------------------------------- what the script reports to the plugin's log

    [Fact]
    public void A_new_frame_still_on_its_empty_page_is_not_reported_and_a_bench_page_that_stays_without_its_hooks_is_reported_once_per_load()
    {
        var page = new PageScript();

        page.Run("var frame = openBlankFrame()");

        for (var i = 0; i < 5; i++) page.Poll();

        Assert.Empty(Diagnostics(page));

        page.Run("loadBench(frame); delete bench().renderRecipes");

        for (var i = 0; i < 3; i++) page.Poll();

        Assert.Empty(Diagnostics(page));

        page.Poll();

        Assert.Single(Diagnostics(page));
        Assert.Contains("core true", Diagnostics(page)[0].Text);
        Assert.Contains("renderRecipes undefined", Diagnostics(page)[0].Text);

        for (var i = 0; i < 5; i++) page.Poll();

        Assert.Single(Diagnostics(page));

        page.Run("loadBench(frame); delete bench().renderRecipes");

        for (var i = 0; i < 4; i++) page.Poll();

        Assert.Equal(2, Diagnostics(page).Count);
    }

    [Fact]
    public void A_frame_out_of_reach_for_a_moment_is_not_reported_but_one_that_stays_out_of_reach_is_reported_once()
    {
        var page = new PageScript();

        page.Run("var frame = openBlankFrame(); denyFrame(frame)");

        for (var i = 0; i < 3; i++) page.Poll();

        page.Run("allowFrame(frame, null); loadBench(frame)");
        page.Poll();

        Assert.Empty(Diagnostics(page));
        Assert.False(page.Truth("$('cc-mid') === null"));

        page.Run("var other = openBlankFrame(); denyFrame(other)");

        for (var i = 0; i < 6; i++) page.Poll();

        Assert.Single(Diagnostics(page));
        Assert.Contains("cannot reach the workbench page", Diagnostics(page)[0].Text);
    }

    [Fact]
    public void The_whole_queues_materials_list_short_ones_first_and_no_parts_list_is_drawn()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("Fibre31short 2", page.Text("$('cc-totals').querySelectorAll('tr')[0].textContent"));
        Assert.True(page.Truth("$('cc-totals').querySelectorAll('tr')[0].classList.contains('no')"));
        Assert.Equal("Log25ok", page.Text("$('cc-totals').querySelectorAll('tr')[1].textContent"));
        Assert.False(page.Truth("!!$('cc-made')"));
    }

    [Fact]
    public void Kept_items_show_what_the_home_holds_and_change_from_their_own_row()
    {
        var page = BenchWithRecipes();
        var targets = new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5), new KeepTarget(Bench.Plank, "plank", "Plank", 9) };

        page.Show(View(keep: new KeepView(true, targets, new Dictionary<string, int> { ["rope"] = 5, ["plank"] = 9 }, new Dictionary<int, int> { [Bench.Rope] = 3 })));

        Assert.Equal("3 / 5", page.Text("$('cc-keeps').querySelectorAll('.cc-keep')[0].querySelector('.cc-kval').textContent"));
        Assert.Equal("? / 9", page.Text("$('cc-keeps').querySelectorAll('.cc-keep')[1].querySelector('.cc-kval').textContent"));

        page.Press("$('cc-keeps').querySelectorAll('.cc-keep')[0].querySelector('.cc-more')");
        page.Press("$('cc-keeps').querySelectorAll('.cc-keep')[0].querySelector('.cc-x')");

        Assert.Equal(new PageCommand[] { new PageCommand.Keep("rope", 6), new PageCommand.Unkeep(Bench.Rope) }, Sent(page));
    }

    [Fact]
    public void A_redraw_waits_while_the_mouse_is_down_on_the_hub_and_happens_on_release()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Run("fire(queueRow(0).querySelector('.cc-x'), 'mousedown')");
        page.Show(View(new[] { TwoOrders[1] }));

        Assert.Equal(2, page.Number("$('cc-qlist').querySelectorAll('.cc-qrow').length"));

        page.Run("fire(benchDoc().body, 'mouseup'); advance(1)");

        Assert.Equal(1, page.Number("$('cc-qlist').querySelectorAll('.cc-qrow').length"));
    }

    // ---------------------------------------------------------------- the plugin driving the bench

    [Fact]
    public void A_queued_craft_sets_the_quantity_and_presses_the_benchs_own_craft_button()
    {
        var page = BenchWithRecipes();

        page.Run("window.__craftinghub.act('craft', 3)");

        Assert.Equal("{\"qty\":3}", page.Text("JSON.stringify(sentActions('CRAFT')[0].data)"));
    }

    [Fact]
    public void A_queued_recipe_is_laid_on_the_bench_as_a_row_click_would()
    {
        var page = BenchWithRecipes();

        page.Run("window.__craftinghub.act('recipe', 'crate')");

        Assert.Equal("crate", page.Text("sentActions('RECIPE_CLICK')[0].data.recipeKey"));
        Assert.True(page.Truth("card('crate').classList.contains('cc-sel')"));
    }

    [Fact]
    public void A_queued_click_goes_to_the_bench_page_the_hub_is_on_not_one_it_could_not_hook_into()
    {
        var page = new PageScript();

        page.Run("var broken = openBench(); delete broken.renderRecipes; var good = openBench()");
        page.Poll();
        page.Run("window.__craftinghub.act('recipe', 'crate')");

        Assert.Equal(1, page.Number("good.sent.filter(function (e) { return e.action === 'RECIPE_CLICK'; }).length"));
        Assert.Equal(0, page.Number("broken.sent.length"));
    }

    [Fact]
    public void A_queued_click_with_no_bench_to_go_to_is_reported_once_and_again_only_after_one_has_worked()
    {
        var page = new PageScript();

        for (var i = 0; i < 3; i++) page.Run("window.__craftinghub.act('craft', 1)");

        Assert.Single(Diagnostics(page));
        Assert.Contains("no workbench page for craft", Diagnostics(page)[0].Text);

        page.OpenBench();
        page.RecipeMsg(Recipes);
        page.Run("window.__craftinghub.act('craft', 1)");

        Assert.Single(Diagnostics(page));

        page.Run("document.body.removeChild(document.querySelector('iframe'))");
        page.Run("window.__craftinghub.act('craft', 1)");
        page.Run("window.__craftinghub.act('craft', 1)");

        Assert.Equal(2, Diagnostics(page).Count);
    }

    // ---------------------------------------------------------------- keeping up with a busy game

    [Fact]
    public void The_same_view_and_the_same_recipes_sent_again_rebuild_nothing()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Run("var plankCard = card('plank'); var firstRow = queueRow(0); var writesBefore = domWrites");
        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal(0, page.Number("domWrites - writesBefore"));

        page.RecipeMsg(Recipes);

        Assert.True(page.Truth("card('plank') === plankCard"));
        Assert.True(page.Truth("queueRow(0) === firstRow"));
    }

    [Fact]
    public void A_change_to_the_queue_leaves_the_recipe_list_as_it_is()
    {
        var page = BenchWithRecipes();

        page.Run("var plankCard = card('plank')");
        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.True(page.Truth("card('plank') === plankCard"));
        Assert.Equal(2, page.Number("$('cc-qlist').querySelectorAll('.cc-qrow').length"));
    }

    [Fact]
    public void New_counts_for_one_recipe_rebuild_only_that_card()
    {
        var page = BenchWithRecipes();

        page.Press("cardPart('plank', '.cc-card-name')");
        page.Run("var plankCard = card('plank'); var ropeCard = card('rope'); var crateCard = card('crate'); var torchCard = card('torch')");
        page.RecipeMsg(Recipes.Replace("\"need\":3,\"have\":1", "\"need\":3,\"have\":2"));

        Assert.True(page.Truth("card('plank') === plankCard && card('crate') === crateCard && card('torch') === torchCard"));
        Assert.False(page.Truth("card('rope') === ropeCard"));
        Assert.Equal("Short: Fibre 1", page.Text("cardPart('rope', '.cc-card-st').textContent"));
        Assert.Equal("3/2", page.Text("cardPart('rope', '.cc-mat').textContent"));
        Assert.Equal("plank,rope,crate,shirt,torch", page.Text("cardKeys()"));
        Assert.True(page.Truth("card('plank').classList.contains('cc-sel')"));
    }

    [Fact]
    public void A_new_mark_on_one_recipe_rebuilds_only_that_card()
    {
        var page = BenchWithRecipes();

        page.Run("var plankCard = card('plank'); var ropeCard = card('rope'); var crateCard = card('crate')");
        page.Show(View(marks: new Dictionary<string, string> { ["crate"] = "Makes first: Plank ×2", ["rope"] = "Makes first: Fibre ×3" }));

        Assert.True(page.Truth("card('plank') === plankCard && card('crate') === crateCard"));
        Assert.False(page.Truth("card('rope') === ropeCard"));
        Assert.Equal("Makes first: Fibre x3", page.Text("cardPart('rope', '.cc-card-st').textContent"));
    }

    [Fact]
    public void Picking_a_recipe_moves_the_highlight_without_rebuilding_the_list()
    {
        var page = BenchWithRecipes();

        page.Run("var plankCard = card('plank'); var crateCard = card('crate')");
        page.Press("cardPart('plank', '.cc-card-name')");
        page.Press("cardPart('crate', '.cc-card-name')");

        Assert.True(page.Truth("card('plank') === plankCard && card('crate') === crateCard"));
        Assert.False(page.Truth("plankCard.classList.contains('cc-sel')"));
        Assert.True(page.Truth("crateCard.classList.contains('cc-sel')"));
    }

    [Fact]
    public void A_poll_with_nothing_changed_writes_nothing_to_the_page()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, isRunning: true, outlook: TwoOrdersOutlook, activeOrderId: 4));
        page.Run("$('craft-progress').style.transform = 'scaleX(0.5)'");
        page.Poll();
        page.Run("var writesBefore = domWrites");
        page.Poll();
        page.Poll();

        Assert.Equal(0, page.Number("domWrites - writesBefore"));
        Assert.Equal("scaleX(0.5)", page.Text("$('cc-prog').style.transform"));
    }

    // ---------------------------------------------------------------- stamina

    [Fact]
    public void The_queue_column_shows_the_stamina_the_player_has()
    {
        var page = BenchWithRecipes();

        page.Show(WithStamina(View(), "{\"have\":42,\"out\":false,\"cost\":{}}"));

        Assert.Equal("Stamina 42", page.Text("$('cc-stamina').textContent"));
        Assert.True(page.Truth("$('cc-stat').contains($('cc-stamina'))"));
        Assert.Equal("", page.Text("$('cc-sta').style.display"));
        Assert.Equal("none", page.Text("$('cc-stamina-out').style.display"));
    }

    [Fact]
    public void A_card_shows_the_stamina_one_craft_of_it_takes()
    {
        var page = BenchWithRecipes();

        page.Show(WithStamina(View(), "{\"have\":42,\"out\":false,\"cost\":{\"crate\":8}}"));

        Assert.Equal("8", page.Text("cardPart('crate', '.cc-cost').textContent"));
        Assert.Equal("8 stamina", page.Text("cardPart('crate', '.cc-cost').getAttribute('data-slip-tip')"));
        Assert.True(page.Truth("cardPart('plank', '.cc-cost') === null"));

        page.Run("var crateCard = card('crate')");
        page.Show(WithStamina(View(), "{\"have\":34,\"out\":false,\"cost\":{\"crate\":8}}"));

        Assert.True(page.Truth("card('crate') === crateCard"));
        Assert.Equal("Stamina 34", page.Text("$('cc-stamina').textContent"));
    }

    [Fact]
    public void Running_out_of_stamina_puts_a_bar_across_the_queue_and_says_so_beside_the_count()
    {
        var page = BenchWithRecipes();

        page.Show(WithStamina(View(), "{\"have\":0,\"out\":true,\"cost\":{\"crate\":8}}"));

        Assert.Equal("", page.Text("$('cc-stamina-out').style.display"));
        Assert.Contains("Stamina too low to craft", page.Text("$('cc-stamina-out').textContent"));
        Assert.Equal("Stamina 0 (out)", page.Text("$('cc-stamina').textContent"));
        Assert.True(page.Truth("$('cc-stamina').classList.contains('cc-warn')"));

        page.Show(WithStamina(View(), "{\"have\":20,\"out\":false,\"cost\":{\"crate\":8}}"));

        Assert.Equal("none", page.Text("$('cc-stamina-out').style.display"));
        Assert.Equal("Stamina 20", page.Text("$('cc-stamina').textContent"));
        Assert.False(page.Truth("$('cc-stamina').classList.contains('cc-warn')"));
    }

    [Fact]
    public void Without_a_stamina_reading_nothing_about_stamina_is_shown()
    {
        var page = BenchWithRecipes();

        page.Show(WithStamina(View(), "{\"have\":0,\"out\":true,\"cost\":{\"crate\":8}}"));
        page.Show(WithStamina(View(), "null"));

        Assert.Equal("", page.Text("$('cc-stamina').textContent"));
        Assert.Equal("none", page.Text("$('cc-stamina-out').style.display"));
        Assert.Equal(0, page.Number("benchDoc().querySelectorAll('.cc-cost').length"));

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("", page.Text("$('cc-stamina').textContent"));
        Assert.Equal("none", page.Text("$('cc-stamina-out').style.display"));
        Assert.Equal(0, page.Number("benchDoc().querySelectorAll('.cc-cost').length"));
        Assert.Empty(Diagnostics(page));
    }

    [Fact]
    public void The_queue_says_how_much_stamina_its_orders_take_and_the_meter_shows_it_against_what_the_player_has()
    {
        var page = BenchWithRecipes();

        // Crate: 2 crafts at 8 each; rope: 1 craft at 2.
        page.Show(WithStamina(View(TwoOrders, outlook: TwoOrdersOutlook), "{\"have\":20,\"out\":false,\"cost\":{\"crate\":8,\"rope\":2}}"));

        Assert.Equal("queue needs 18", page.Text("$('cc-qneed').textContent"));
        Assert.Equal("16", page.Text("queueRow(0).querySelector('.cc-qcost').textContent"));
        Assert.Equal("16 stamina", page.Text("queueRow(0).querySelector('.cc-qcost').getAttribute('data-slip-tip')"));
        Assert.Equal("100%", page.Text("$('cc-meter').children[0].style.width"));
        Assert.Equal("10%", page.Text("$('cc-meter').children[1].style.left"));
        Assert.Equal("90%", page.Text("$('cc-meter').children[1].style.width"));
        Assert.Equal("0%", page.Text("$('cc-meter').children[2].style.width"));

        page.Show(WithStamina(View(TwoOrders, outlook: TwoOrdersOutlook), "{\"have\":9,\"out\":false,\"cost\":{\"crate\":8,\"rope\":2}}"));

        Assert.Equal("50%", page.Text("$('cc-meter').children[0].style.width"));
        Assert.Equal("50%", page.Text("$('cc-meter').children[2].style.left"));
        Assert.Equal("50%", page.Text("$('cc-meter').children[2].style.width"));
    }

    [Fact]
    public void Without_a_cost_for_any_queued_recipe_the_queue_need_is_left_out()
    {
        var page = BenchWithRecipes();

        page.Show(WithStamina(View(TwoOrders, outlook: TwoOrdersOutlook), "{\"have\":20,\"out\":false,\"cost\":{\"plank\":3}}"));

        Assert.Equal("", page.Text("$('cc-qneed').textContent"));
        Assert.Equal("none", page.Text("$('cc-qneed').style.display"));
        Assert.Equal("Stamina 20", page.Text("$('cc-stamina').textContent"));
        Assert.True(page.Truth("queueRow(0).querySelector('.cc-qcost') === null"));
    }

    [Fact]
    public void The_materials_fold_to_the_short_ones_and_open_to_the_whole_table()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("none", page.Text("$('cc-totals').style.display"));
        Assert.Equal("1 short \u00b7 show all 2", page.Text("$('cc-matsum').textContent"));
        Assert.Equal("Fibre short 2", page.Text("$('cc-shorts').textContent"));
        Assert.Equal(1, page.Number("$('cc-shorts').querySelectorAll('.cc-short').length"));

        page.Press("$('cc-matfold')");

        Assert.Equal("", page.Text("$('cc-totals').style.display"));
        Assert.Equal("1 short \u00b7 need / you have", page.Text("$('cc-matsum').textContent"));
        Assert.Equal("", page.Text("$('cc-shorts').textContent"));

        page.Press("$('cc-matfold')");

        Assert.Equal("none", page.Text("$('cc-totals').style.display"));
    }

    [Fact]
    public void With_nothing_queued_the_materials_say_so_rather_than_fold_away()
    {
        var page = BenchWithRecipes();

        Assert.Equal("", page.Text("$('cc-totals').style.display"));
        Assert.Equal("", page.Text("$('cc-matsum').textContent"));
        Assert.Contains("Nothing queued", page.Text("$('cc-totals').textContent"));
    }

    [Fact]
    public void Keep_in_stock_is_a_tab_beside_the_queue_only_while_keeping_is_on()
    {
        var page = BenchWithRecipes();
        var keep = new KeepView(true, new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5) }, new Dictionary<string, int> { ["rope"] = 5 });

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("none", page.Text("$('cc-qtabs').style.display"));
        Assert.Equal("none", page.Text("$('cc-keepsect').style.display"));

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook, keep: keep));

        Assert.Equal("Queue 2|Keep in stock 1", page.Text("$('cc-qtabs').children.map(function (tab) { return tab.textContent; }).join('|')"));
        Assert.Equal("", page.Text("$('cc-qpane').style.display"));
        Assert.Equal("none", page.Text("$('cc-keepsect').style.display"));

        page.Press("$('cc-qtabs').children[1]");

        Assert.Equal("none", page.Text("$('cc-qpane').style.display"));
        Assert.Equal("", page.Text("$('cc-keepsect').style.display"));
        Assert.True(page.Truth("$('cc-qtabs').children[1].classList.contains('on')"));

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("", page.Text("$('cc-qpane').style.display"));
        Assert.Equal("none", page.Text("$('cc-keepsect').style.display"));
    }

    /// <summary>A workbench open with the fixture's recipes listed and the chain marks shown, nothing queued.</summary>
    [Fact]
    public void A_chinese_game_gets_the_panel_in_chinese()
    {
        var page = new PageScript();

        page.Run(PageWords.Script(isChinese: true));
        page.OpenBench();
        page.RecipeMsg(Recipes);
        page.Show(View());

        Assert.Equal("5 / 5", page.Text("$('cc-count').textContent"));
        Assert.Equal("搜索配方或材料", page.Text("$('cc-search').placeholder"));
        Assert.Contains("队列是空的。在任意配方上点「添加」。", page.Text("$('cc-queue').textContent"));
        Assert.DoesNotContain("Craft queue", page.Text("$('cc-queue').textContent"));
    }

    [Fact]
    public void The_bell_amount_buttons_and_materials_say_what_they_are_for_in_a_tag_Info_Pack_reads()
    {
        var page = BenchWithRecipes();

        page.Show(View(keep: new KeepView(true, Array.Empty<KeepTarget>(), new Dictionary<string, int>())));

        Assert.Equal("Keep 1 of this in your home; opening any workbench queues the crafts to top it back up", page.Text("cardPart('plank', '.cc-pin').getAttribute('data-slip-tip')"));
        Assert.Equal("One less", page.Text("cardPart('plank', '.cc-less').getAttribute('data-slip-tip')"));
        Assert.Equal("One more", page.Text("cardPart('plank', '.cc-more').getAttribute('data-slip-tip')"));
        Assert.Equal("How many crafts Add puts in the queue", page.Text("cardPart('plank', '.cc-val').getAttribute('data-slip-tip')"));
        // A material's name, which Info Pack also looks the item up by.
        Assert.Equal("Log", page.Text("cardPart('plank', '.cc-mat').getAttribute('data-slip-tip')"));
        Assert.Equal("Log", page.Text("cardPart('plank', '.cc-mat').getAttribute('data-slip-name')"));
    }

    [Fact]
    public void The_item_a_card_or_a_queue_order_makes_is_tagged_for_its_item_tooltip()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("Plank", page.Text("cardPart('plank', '.cc-card-ic').getAttribute('data-slip-name')"));
        Assert.Equal("Plank", page.Text("cardPart('plank', '.cc-card-name').getAttribute('data-slip-name')"));
        Assert.Equal("Plank", page.Text("cardPart('plank', '.cc-card-name').getAttribute('data-slip-tip')"));
        Assert.Equal("Crate", page.Text("queueRow(0).querySelector('.cc-qname').getAttribute('data-slip-name')"));
        Assert.Equal("Crate", page.Text("queueRow(0).querySelector('.cc-qic').getAttribute('data-slip-name')"));
    }

    [Fact]
    public void Without_Info_Pack_the_hub_shows_its_own_tooltip_after_a_short_wait_and_hides_it_when_the_mouse_moves_off()
    {
        var page = BenchWithRecipes();

        page.Run("fire(cardPart('plank', '.cc-mat').querySelector('b'), 'mouseover', { clientX: 100, clientY: 50 })");
        page.Advance(100);

        Assert.True(page.Truth("$('cc-tip') === null || $('cc-tip').style.display === 'none'"));

        page.Advance(400);

        Assert.Equal("Log", page.Text("$('cc-tip').textContent"));
        Assert.Equal("", page.Text("$('cc-tip').style.display"));
        Assert.Equal("114px", page.Text("$('cc-tip').style.left"));

        page.Run("fire(card('plank'), 'mouseover', {})");

        Assert.Equal("none", page.Text("$('cc-tip').style.display"));
    }

    [Fact]
    public void A_tooltip_left_before_the_wait_is_over_never_shows()
    {
        var page = BenchWithRecipes();

        page.Run("fire(cardPart('plank', '.cc-more'), 'mouseover', {})");
        page.Advance(200);
        page.Run("fire(card('plank'), 'mouseover', {})");
        page.Advance(1000);

        Assert.True(page.Truth("$('cc-tip') === null"));
    }

    [Fact]
    public void With_Info_Pack_drawing_tags_the_hub_leaves_the_tooltip_to_it()
    {
        var page = BenchWithRecipes();

        page.Run("window.__slinfopack = { tags: ['item', 'name', 'tip'] }");
        page.Run("fire(cardPart('plank', '.cc-more'), 'mouseover', {})");
        page.Advance(1000);

        Assert.True(page.Truth("$('cc-tip') === null"));
    }

    // ---------------------------------------------------------------- UI size

    [Fact]
    public void The_two_columns_follow_the_ui_size_as_far_as_the_whole_workbench_fits_at_the_pages_own_size()
    {
        var page = BenchWithToolkit();

        Assert.Equal(2, page.Number("handed.length"));

        foreach (var id in new[] { "cc-mid", "cc-queue" })
        {
            Assert.Equal(1, page.Number($"handedId('{id}').length"));
            Assert.True(page.Truth($"handedId('{id}')[0].options.fit === benchDoc().querySelector('.ui-container')"));
            Assert.True(page.Truth($"handedId('{id}')[0].options.pageFits === true"));
        }
    }

    [Fact]
    public void Columns_drawn_before_the_toolkit_loads_wait_for_it()
    {
        var page = BenchWithRecipes();

        Assert.Equal(2, page.Number("window.__exiledUiWait.length"));
        Assert.Equal("cc-mid,cc-queue", page.Text("window.__exiledUiWait.map(function (entry) { return entry[0].id; }).join(',')"));
        Assert.True(page.Truth("window.__exiledUiWait[1][1].fit === benchDoc().querySelector('.ui-container')"));
    }

    [Fact]
    public void A_new_zoom_fits_the_page_to_the_window_again_at_once_but_during_a_drag_only_once_the_item_is_dropped()
    {
        var page = BenchWithToolkit();
        var fitsBefore = page.Number("bench().fits");

        page.Run("handedId('cc-mid')[0].options.onZoom(1.3)");

        Assert.Equal(fitsBefore + 1, page.Number("bench().fits"));

        page.Run("gameDrag(true)");
        page.Run("handedId('cc-queue')[0].options.onZoom(1.2)");
        page.Poll();

        Assert.Equal(fitsBefore + 1, page.Number("bench().fits"));

        page.Run("gameDrag(false)");
        page.Poll();

        Assert.Equal(fitsBefore + 2, page.Number("bench().fits"));
    }

    [Fact]
    public void A_tip_in_a_bigger_column_is_drawn_as_big_and_placed_by_the_pointer_on_screen()
    {
        var page = BenchWithToolkit();

        page.Run("$('cc-mid').style.zoom = '1.5'");
        page.Run("fire(cardPart('plank', '.cc-mat').querySelector('b'), 'mouseover', { clientX: 100, clientY: 50 })");
        page.Advance(500);

        Assert.Equal("1.5", page.Text("$('cc-tip').style.zoom"));
        Assert.Equal("76px", page.Text("$('cc-tip').style.left"));
        Assert.Equal("45px", page.Text("$('cc-tip').style.top"));

        // Near the window's corner it flips to the other side of the pointer by its size on screen, 300 by 75.
        page.Run("bench().innerWidth = 1920; bench().innerHeight = 1080; $('cc-tip').offsetWidth = 200; $('cc-tip').offsetHeight = 50");
        page.Run("fire(card('plank'), 'mouseover', {})");
        page.Run("fire(cardPart('plank', '.cc-mat').querySelector('b'), 'mouseover', { clientX: 1800, clientY: 1050 })");
        page.Advance(500);

        Assert.Equal("993px", page.Text("$('cc-tip').style.left"));
        Assert.Equal("643px", page.Text("$('cc-tip').style.top"));
    }

    [Fact]
    public void A_tip_on_a_workbench_the_game_drew_smaller_to_fit_the_window_still_shows_beside_the_pointer()
    {
        var page = BenchWithRecipes();

        page.Run("benchDoc().body.style.zoom = '0.8'");
        page.Run("fire(cardPart('plank', '.cc-mat').querySelector('b'), 'mouseover', { clientX: 100, clientY: 50 })");
        page.Advance(500);

        Assert.Equal("", page.Text("$('cc-tip').style.zoom"));
        Assert.Equal("143px", page.Text("$('cc-tip').style.left"));
        Assert.Equal("85px", page.Text("$('cc-tip').style.top"));
    }

    // ---------------------------------------------------------------- colors

    // The game keeps colored recipes off its list; the plugin sends each dyeable recipe's colors by its plain key.
    private static readonly DyeOption[] ChairColors =
    {
        new("plank", "1,2,90", 90, "#c03030", "Red", 1, 2),
        new("plank", "1,2,91", 91, "#3050c0", "Blue", 1, 0),
    };

    [Fact]
    public void A_dyeable_card_offers_plain_and_each_color_with_the_dye_on_hand()
    {
        var page = BenchWithColors();

        Assert.Equal(3, page.Number("cardPart('plank', '.cc-dyes').children.length"));
        Assert.True(page.Truth("cardPart('plank', '.cc-dye.plain').classList.contains('on')"));
        Assert.Equal("2", page.Text("dyeDot('plank', '1,2,90').textContent"));
        Assert.True(page.Truth("dyeDot('plank', '1,2,91').classList.contains('out')"));
        Assert.True(page.Truth("cardPart('rope', '.cc-dyes') === null"));
    }

    [Fact]
    public void A_picked_color_is_what_the_card_adds_keeps_and_lays_on_the_bench()
    {
        var page = BenchWithColors(new KeepView(true, Array.Empty<KeepTarget>(), new Dictionary<string, int>()));

        page.Press("dyeDot('plank', '1,2,90')");
        page.Press("cardPart('plank', '.cc-add')");
        page.Press("cardPart('plank', '.cc-pin')");
        page.Press("cardPart('plank', '.cc-card-name')");

        Assert.Equal(new PageCommand[] { new PageCommand.Add("1,2,90", 1), new PageCommand.Keep("1,2,90", 1) }, Sent(page));
        Assert.Equal("1,2,90", page.Text("sentActions('RECIPE_CLICK')[0].data.recipeKey"));
        Assert.Equal("Plank (Red)", page.Text("cardPart('plank', '.cc-card-name').textContent"));
        Assert.True(page.Truth("card('plank').classList.contains('cc-sel')"));
    }

    [Fact]
    public void Going_back_to_plain_adds_the_plain_recipe()
    {
        var page = BenchWithColors();

        page.Press("dyeDot('plank', '1,2,91')");
        page.Press("cardPart('plank', '.cc-dye.plain')");
        page.Press("cardPart('plank', '.cc-add')");

        Assert.Equal(new PageCommand[] { new PageCommand.Add("plank", 1) }, Sent(page));
        Assert.Equal("Plank", page.Text("cardPart('plank', '.cc-card-name').textContent"));
    }

    [Fact]
    public void A_color_the_bench_no_longer_offers_falls_back_to_plain()
    {
        var page = BenchWithColors();

        page.Press("dyeDot('plank', '1,2,90')");
        page.Show(View());
        page.Press("cardPart('plank', '.cc-add')");

        Assert.Equal(new PageCommand[] { new PageCommand.Add("plank", 1) }, Sent(page));
    }

    [Fact]
    public void The_view_sends_each_dyeable_recipes_colors_by_its_plain_key()
    {
        var json = View(dyes: ChairColors);

        Assert.Contains("\"dyes\":{\"plank\":[{\"key\":\"1,2,90\",\"dye\":90,\"hex\":\"#c03030\",\"color\":\"Red\",\"need\":1,\"have\":2},", json);
        Assert.Contains("\"dyes\":{}", View());
    }

    private static PageScript BenchWithColors(KeepView? keep = null)
    {
        var page = new PageScript();

        page.OpenBench();
        page.RecipeMsg(Recipes);
        page.Show(View(keep: keep, dyes: ChairColors));

        return page;
    }

    private static PageScript BenchWithToolkit()
    {
        var page = new PageScript();

        page.Run("fakeToolkit()");
        page.OpenBench();
        page.RecipeMsg(Recipes);
        page.Show(View());

        return page;
    }

    private static PageScript BenchWithRecipes()
    {
        var page = new PageScript();

        page.OpenBench();
        page.RecipeMsg(Recipes);
        page.Show(View());

        return page;
    }

    private static string View(
        IReadOnlyList<QueueEntry>? queue = null,
        bool isRunning = false,
        bool isPaused = false,
        QueueOutlook? outlook = null,
        KeepView? keep = null,
        IReadOnlyDictionary<string, int>? yields = null,
        string status = "",
        long activeOrderId = 0,
        bool takesUnits = false,
        IReadOnlyList<DyeOption>? dyes = null,
        IReadOnlyDictionary<string, string>? marks = null) =>
        HubView.Build(marks ?? Marks, queue ?? Array.Empty<QueueEntry>(), isRunning, status, keep ?? NoKeep, isPaused, outlook, yields, activeOrderId, takesUnitsOffStacks: takesUnits, dyes: dyes);

    /// <summary>The plugin's view with its stamina reading, which the plugin sends as the view's last field.</summary>
    private static string WithStamina(string view, string stamina) => view[..^1] + ",\"stamina\":" + stamina + "}";

    /// <summary>What the script asked of the plugin, past its hello.</summary>
    private static List<PageCommand> Sent(PageScript page) => page.Posted.Where(command => command is not PageCommand.Ready).ToList();

    /// <summary>What the script wrote to the plugin's log.</summary>
    private static List<PageCommand.Diagnostic> Diagnostics(PageScript page) => page.Posted.OfType<PageCommand.Diagnostic>().ToList();
}
