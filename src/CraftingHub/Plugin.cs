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
    private const string Version = "0.5.1";

    public override void Load()
    {
        var useAllHomeStorages = Config.Bind("Storage", "UseAllHomeStorages", true,
            "Workbenches count and take materials from every storage in your home, not only the ones linked to them.").Value;
        var maxDepth = Config.Bind("Chain", "MaxDepth", 4,
            "How many layers of intermediates may be made for one recipe (planks for a crate is one layer). 0 turns intermediates off.").Value;
        var showMarks = Config.Bind("Chain", "ShowMarks", true,
            "Mark recipes you can make once their intermediates are crafted (\"Makes first\" in amber), and offer \"Craft with intermediates\" on them.").Value;
        var keepStocked = Config.Bind("Targets", "KeepStocked", true,
            "Show the Keep button on every recipe and the \"Keep in stock\" list under the queue. Opening any workbench queues the crafts that bring your kept items back up to their counts.").Value;
        var autoStart = Config.Bind("Targets", "AutoStart", true,
            "Start the queue by itself after topping up, when the queue had nothing else in it. Off only queues the crafts.").Value;
        var resumeOnReopen = Config.Bind("Queue", "ResumeOnReopen", true,
            "Closing a workbench pauses a running queue, and it carries on when you open that workbench again. Off, closing stops the queue and \"Craft queue\" carries on.").Value;
        var otherBenches = Config.Bind("Queue", "OtherBenches", true,
            "A paused queue carries on at whichever workbench you open next, not only the one it was running on. Every workbench lists the same recipes. Off, it waits for the workbench it was running on.").Value;

        var log = new HubLog(Log, Path.Combine(Paths.BepInExRootPath, "crafting-hub.log"));
        var harmony = new Harmony(Guid);
        var settings = new HubSettings(Math.Max(0, maxDepth), showMarks, keepStocked, autoStart, resumeOnReopen, otherBenches);
        var keep = new KeepPorts(new HomeStock(log.Write), new KeepStore(Path.Combine(Paths.ConfigPath, Guid + ".targets.txt"), log.Write), SaveSlot.CurrentKey);

        log.TrimIfLarge();
        log.Write($"===== Crafting Hub {Version} started: all home storages {(useAllHomeStorages ? "on" : "off")}, intermediates {(settings.MaxDepth > 0 ? $"up to {settings.MaxDepth} deep" : "off")}, keep stocked {(keepStocked ? "on" : "off")}, resume on reopen {(resumeOnReopen ? "on" : "off")}, other workbenches {(otherBenches ? "on" : "off")}");

        if (useAllHomeStorages) StoragePool.Apply(harmony, log.Write);

        Workbench.Apply(harmony, log.Write);
        PageMessages.Apply(harmony, log.Write);
        CraftTicker.Log = log.Write;
        CraftTicker.Session = new HubSession(new Workbench(log.Write), new WorkbenchPage(log.Write), keep, settings, log.Write, log.WriteFileOnly);
        AddComponent<CraftTicker>();
    }
}
