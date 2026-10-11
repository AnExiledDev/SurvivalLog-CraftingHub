using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using CraftingHub.Adapters;
using HarmonyLib;

namespace CraftingHub;

[BepInPlugin(Guid, "Crafting Hub", Version)]
public sealed class Plugin : BasePlugin
{
    private const string Guid = "com.dev.survivallog.craftinghub";
    private const string Version = "0.1.1";

    public override void Load()
    {
        var useAllHomeStorages = Config.Bind("Storage", "UseAllHomeStorages", true,
            "Workbenches count and take materials from every storage in your home, not only the ones linked to them.").Value;
        var maxDepth = Config.Bind("Chain", "MaxDepth", 4,
            "How many layers of intermediates may be made for one recipe (planks for a crate is one layer). 0 turns intermediates off.").Value;
        var showMarks = Config.Bind("Chain", "ShowMarks", true,
            "Mark recipes you can make once their intermediates are crafted, and offer \"Craft with intermediates\" on them.").Value;

        var log = new HubLog(Log, Path.Combine(Paths.BepInExRootPath, "crafting-hub.log"));
        var harmony = new Harmony(Guid);
        var settings = new HubSettings(Math.Max(0, maxDepth), showMarks);

        log.TrimIfLarge();
        log.Write($"===== Crafting Hub {Version} started: all home storages {(useAllHomeStorages ? "on" : "off")}, intermediates {(settings.MaxDepth > 0 ? $"up to {settings.MaxDepth} deep" : "off")}");

        if (useAllHomeStorages) StoragePool.Apply(harmony, log.Write);

        Workbench.Apply(harmony, log.Write);
        PageMessages.Apply(harmony, log.Write);
        CraftTicker.Log = log.Write;
        CraftTicker.Session = new HubSession(new Workbench(log.Write), new WorkbenchPage(log.Write), settings, log.Write, log.WriteFileOnly);
        AddComponent<CraftTicker>();
    }
}
