using System;
using GameCore.HotUpdate;

namespace CraftingHub.Adapters;

/// <summary>Asks the game which language it is showing, only once the game has made its settings. Anything that goes wrong reads as English.</summary>
internal static class GameLanguage
{
    public static bool IsChinese()
    {
        try
        {
            return GameSingleton.Existing<SettingManager>()?.IsChinese() == true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
