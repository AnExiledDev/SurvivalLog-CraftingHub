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
        new[] { new MadeOnTheWay("plank", "Plank", 2) },
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
        Assert.Equal("Can make now 2", page.Text("filterChip('Can make now').textContent"));
        Assert.Equal("Needs parts first 1", page.Text("filterChip('Needs parts first').textContent"));
        Assert.Equal("Short 1", page.Text("filterChip('Short').textContent"));
        Assert.True(page.Truth("filterChip('Kept in stock') === null"));

        page.Press("filterChip('Can make now')");

        Assert.Equal("plank,torch", page.Text("cardKeys()"));
        Assert.Equal("2 of 5", page.Text("$('cc-count').textContent"));

        page.Press("filterChip('Needs parts first')");

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
        page.Press("filterChip('Kept in stock')");

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

        Assert.Equal("5/6", page.Text("cardPart('plank', '.cc-mat').textContent"));
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
    public void Clicking_a_card_lays_its_recipe_on_the_bench_the_way_the_games_own_row_does()
    {
        var page = BenchWithRecipes();

        page.Press("card('plank')");

        Assert.Equal("plank", page.Text("sentActions('RECIPE_CLICK')[0].data.recipeKey"));
        Assert.True(page.Truth("card('plank').classList.contains('cc-sel')"));
    }

    [Fact]
    public void A_locked_recipe_cannot_be_laid_on_the_bench_or_queued()
    {
        var page = BenchWithRecipes();

        page.Press("card('shirt')");
        page.Press("cardPart('shirt', '.cc-add')");

        Assert.Equal(0, page.Number("sentActions('RECIPE_CLICK').length"));
        Assert.True(page.Truth("cardPart('shirt', '.cc-add').disabled"));
        Assert.Empty(Sent(page));
    }

    [Fact]
    public void A_recipe_on_the_bench_that_needs_parts_offers_to_make_them_with_the_quantity_set()
    {
        var page = BenchWithRecipes();

        page.Press("card('crate')");

        Assert.Equal("Makes first: Plank x2", page.Text("$('cc-panel').querySelector('.cc-plan').textContent"));

        page.Run("bench().values.craftQty = 4");
        page.Press("$('cc-panel').querySelector('.btn')");

        Assert.Equal(new PageCommand[] { new PageCommand.CraftNow("crate", 4) }, Sent(page));
    }

    [Fact]
    public void Keep_on_a_card_keeps_ten_and_pressed_again_stops_keeping()
    {
        var page = BenchWithRecipes();

        page.Show(View(keep: new KeepView(true, Array.Empty<KeepTarget>(), new Dictionary<string, int>())));
        page.Press("cardPart('plank', '.cc-pin')");
        page.Show(View(keep: new KeepView(true, new[] { new KeepTarget(Bench.Plank, "plank", "Plank", 10) }, new Dictionary<string, int> { ["plank"] = 10 })));

        Assert.Equal("Keep 10", page.Text("cardPart('plank', '.cc-pin').textContent"));

        page.Press("cardPart('plank', '.cc-pin')");

        Assert.Equal(new PageCommand[] { new PageCommand.Keep("plank", 10), new PageCommand.Keep("plank", 0) }, Sent(page));
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
    public void A_cards_materials_amount_and_add_share_one_row_across_its_bottom_and_keep_sits_with_the_status()
    {
        var page = BenchWithRecipes();

        page.Show(View(keep: new KeepView(true, Array.Empty<KeepTarget>(), new Dictionary<string, int>())));
        page.Run("var foot = cardPart('plank', '.cc-add').parentElement");

        Assert.True(page.Truth("foot.matches('.cc-card-foot')"));
        Assert.True(page.Truth("foot.parentElement === card('plank')"));
        Assert.True(page.Truth("cardPart('plank', '.cc-more').parentElement.parentElement === foot"));
        Assert.True(page.Truth("cardPart('plank', '.cc-mat').parentElement.parentElement === foot"));
        Assert.True(page.Truth("foot.querySelector('.cc-card-name') === null"));
        Assert.True(page.Truth("cardPart('plank', '.cc-pin').parentElement === cardPart('plank', '.cc-card-st').parentElement"));
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
    public void Orders_move_up_and_down_by_id_but_not_past_the_ends()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));
        page.Press("queueRow(0).querySelector('.cc-up')");
        page.Press("queueRow(0).querySelector('.cc-down')");
        page.Press("queueRow(1).querySelector('.cc-up')");
        page.Press("queueRow(1).querySelector('.cc-down')");

        Assert.Equal(new PageCommand[] { new PageCommand.Move(4, 1), new PageCommand.Move(9, 0) }, Sent(page));
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

        page.Press("card('plank')");

        Assert.Equal("plank", page.Text("sentActions('RECIPE_CLICK')[0].data.recipeKey"));
        Assert.True(page.Truth("isBenchShown()"));
    }

    [Fact]
    public void The_queue_laying_a_recipe_leaves_the_player_on_the_bag()
    {
        var page = BenchWithRecipes();

        page.Run("window.__craftinghub.act('recipe', 'crate')");

        Assert.False(page.Truth("isBenchShown()"));

        page.Press("card('plank')");

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
        Assert.True(page.Truth("benchDoc().querySelector('.backpack-panel').contains(benchDoc().querySelector('.workbench-section'))"));
        Assert.Equal(1, page.Number("benchDoc().querySelectorAll('#cc-tab-bench').length"));
    }

    [Fact]
    public void The_whole_queues_materials_list_short_ones_and_the_parts_it_makes_on_the_way()
    {
        var page = BenchWithRecipes();

        page.Show(View(TwoOrders, outlook: TwoOrdersOutlook));

        Assert.Equal("Fibre31short 2", page.Text("$('cc-totals').querySelectorAll('tr')[0].textContent"));
        Assert.True(page.Truth("$('cc-totals').querySelectorAll('tr')[0].classList.contains('no')"));
        Assert.Equal("Log25ok", page.Text("$('cc-totals').querySelectorAll('tr')[1].textContent"));
        Assert.Equal("Plank x2", page.Text("$('cc-made').textContent"));
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

    /// <summary>A workbench open with the fixture's recipes listed and the chain marks shown, nothing queued.</summary>
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
        long activeOrderId = 0) =>
        HubView.Build(Marks, queue ?? Array.Empty<QueueEntry>(), isRunning, status, keep ?? NoKeep, isPaused, outlook, yields, activeOrderId);

    /// <summary>What the script asked of the plugin, past its hello.</summary>
    private static List<PageCommand> Sent(PageScript page) => page.Posted.Where(command => command is not PageCommand.Ready).ToList();
}
