using CraftingHub.Core;
using ExiledToolkit.Web;
using Jint;

namespace CraftingHub.Tests;

/// <summary>
/// page.js exactly as the plugin embeds it, run in Jint with Exiled's Toolkit's page API (api.js, from the Toolkit's
/// source) on top of FakePage.js (Root.html with the workbench page in an iframe, the vuplex bridge, a clock). The
/// plugin's side is the real core: what the script posts is parsed with PageCommand, and what it is shown is built
/// with HubView, as HubSession does.
/// </summary>
internal sealed class PageScript
{
    private readonly Engine _engine = new();

    /// <param name="isToolkitFirst">
    /// The game runs the Toolkit's script first, or with false Crafting Hub's first; then the Toolkit's runs only at
    /// <see cref="LoadToolkit"/>.
    /// </param>
    public PageScript(bool isToolkitFirst = true)
    {
        _engine.Execute(Resource("CraftingHub.Tests.FakePage.js"));

        if (isToolkitFirst) _engine.Execute(Resource("ExiledToolkit.api.js"));

        _engine.Execute(Resource("CraftingHub.page.js"));
        Hear();
    }

    /// <summary>Everything the script posted to the plugin, parsed as the plugin parses it.</summary>
    public List<PageCommand> Posted { get; } = new();

    /// <summary>The Toolkit's page API arriving after Crafting Hub's script.</summary>
    public void LoadToolkit() => Run(Resource("ExiledToolkit.api.js"));

    public void Run(string statements)
    {
        _engine.Execute(statements);
        Hear();
    }

    public string Text(string expression) => _engine.Evaluate($"String({expression})").AsString();

    public double Number(string expression) => _engine.Evaluate($"Number({expression})").AsNumber();

    public bool Truth(string expression) => _engine.Evaluate($"Boolean({expression})").AsBoolean();

    /// <summary>One run of the script's poll.</summary>
    public void Poll() => Run("poll()");

    public void Advance(int ms) => Run($"advance({ms})");

    /// <summary>The plugin's view, sent the way WorkbenchPage sends it: the JSON as a script literal.</summary>
    public void Show(string viewJson) => Run($"window.__craftinghub.setData({viewJson})");

    /// <summary>Opens a workbench and lets the script's poll find it.</summary>
    public void OpenBench()
    {
        Run("openBench()");
        Poll();
    }

    /// <param name="recipes">A JavaScript list of the game's recipe objects.</param>
    public void RecipeMsg(string recipes) => Run($"recipeMsg(true, {recipes})");

    /// <summary>A whole mouse click on the element the expression names.</summary>
    public void Press(string element) => Run($"press({element})");

    public void Type(string text) => Run($"typeInto($('cc-search'), {JsText.Literal(text)})");

    /// <summary>Takes what the script posted since the last look.</summary>
    private void Hear()
    {
        var count = (int)_engine.Evaluate("posted.length").AsNumber();
        var messages = Enumerable.Range(0, count).Select(i => _engine.Evaluate($"posted[{i}]").AsString()).ToList();

        _engine.Execute("posted.length = 0");
        Posted.AddRange(messages.Select(PageCommand.Parse));
    }

    private static string Resource(string name)
    {
        using var stream = typeof(PageScript).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name + " is not embedded in the test assembly");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
