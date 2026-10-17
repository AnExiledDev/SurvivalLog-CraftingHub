using System.Linq;

namespace CraftingHub.Core;

/// <summary>The panel's own words in the game's language, as the statement that hands them to page.js before every call.</summary>
public static class PageWords
{
    private static readonly string English = Statement(isChinese: false);
    private static readonly string Chinese = Statement(isChinese: true);

    public static string Script(bool isChinese) => isChinese ? Chinese : English;

    private static string Statement(bool isChinese) =>
        "window.__craftinghubWords={" + string.Join(",", Texts.Page.Select(word => HubView.Str(word.Key) + ":" + HubView.Str(word.Phrase.Pick(isChinese)))) + "};";
}
