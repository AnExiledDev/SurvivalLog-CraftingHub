using System;
using System.Globalization;
using CraftingHub.Core;
using ExiledToolkit.Game;
using ExiledToolkit.Web;

namespace CraftingHub.Adapters;

/// <summary>
/// page.js, run in the root document the game's pages sit in, the way Storage Hub and the ivmakk mods add their UI.
/// The web view can reload at any time and lose the script, so Exiled's Toolkit checks for it every couple of seconds
/// and the session puts it back when the page says it is missing.
/// </summary>
internal sealed class WorkbenchPage
{
    private const string ScriptResource = "CraftingHub.page.js";
    private const string Global = "window.__craftinghub";

    private readonly WebPage _page;

    public WorkbenchPage(Action<string> log)
    {
        // The words go with the first call after a load and again whenever the game's language changes, so the panel
        // follows a language change in the game's settings.
        _page = new WebPage(typeof(WorkbenchPage).Assembly, ScriptResource, Global, PageCommand.Prefix + "noscript", log)
        {
            Words = PageWords.Script,
        };
    }

    public void KeepInstalled(double now) => _page.KeepInstalled(now);

    /// <summary>
    /// The words follow the script at once, so a workbench already open when the script comes back is drawn in the
    /// game's language from its first draw, before the next call would bring them.
    /// </summary>
    public void Install()
    {
        if (_page.Install()) _page.Run(PageWords.Script(GameLanguage.IsChinese()));
    }

    /// <summary>The page loaded the script afresh, so its words go with the next call.</summary>
    public void ForgetWords() => _page.ForgetWords();

    public void Show(string viewJson) => _page.Call($"setData({viewJson})");

    public void ClickRecipe(string key) => _page.Call($"act('recipe',{JsText.Literal(key)})");

    public void Craft(int quantity) => _page.Call($"act('craft',{quantity.ToString(CultureInfo.InvariantCulture)})");

    public void ConfirmResult() => _page.Call("act('confirm')");
}
