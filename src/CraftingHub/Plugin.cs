using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using CraftingHub.Adapters;
using HarmonyLib;

namespace CraftingHub;

[BepInPlugin(Guid, "Crafting Hub", Version)]
[BepInDependency(OptionalLive.ToolkitGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(StackingGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInIncompatibility(BetterCraftingGuid)]
public sealed class Plugin : BasePlugin
{
    private const string Guid = "com.dev.survivallog.craftinghub";
    private const string Version = "0.9.11";
    private const string StackingGuid = "com.dev.survivallog.stacking";

    /// <summary>BetterCrafting also changes the workbench's storage list, so BepInEx loads only one of the two.</summary>
    private const string BetterCraftingGuid = "com.ivmakk.survivallog.bettercrafting";

    public override void Load()
    {
        if (!OptionalLive.IsToolkitInstalled) Log.LogInfo(OptionalLive.MissingNote);

        var useAllHomeStorages = Config.Bind("Storage", "UseAllHomeStorages", true,
            "Workbenches use materials from every storage in your home, not only the storages linked to them. Off: only the linked storages, as in the base game.\n" +
            "工作台可以使用家里所有储物家具中的材料，不只是接入它的那几个。关闭后只用接入的储物家具，和原版游戏一样。");
        var maxDepth = Config.Bind("Chain", "MaxDepth", 4,
            "How many layers of intermediates can be crafted for one recipe. Making planks for a crate is one layer, and 0 turns intermediates off.\n" +
            "做一个配方时，最多可以先做几层中间材料（先做出来、再拿去做成品的材料）。0 表示不做中间材料。");
        var showMarks = Config.Bind("Chain", "ShowMarks", true,
            "Mark recipes you can make once their intermediates are crafted: they show \"Makes first\" in amber and get a \"Craft with intermediates\" button. Off hides both, and queued orders still make intermediates.\n" +
            "给先做中间材料就能做的配方加上标记：以琥珀色显示「先制作」，并提供「连同中间材料一起制作」按钮。关闭后两者都不显示，队列里的订单仍会先做中间材料。");
        var keepStocked = Config.Bind("Targets", "KeepStocked", true,
            "Adds a Keep button to every recipe and a \"Keep in stock\" list under the queue. Each time you open a workbench, crafts are queued to bring kept items back up to their counts.\n" +
            "在每个配方上加「保持」按钮，并在队列下方加「保持库存」列表。每次打开工作台，都会把需要的制作加入队列，让这些物品补回你设定的数量。");
        var autoStart = Config.Bind("Targets", "AutoStart", true,
            new ConfigDescription("After kept items are topped up, start the queue by itself if nothing else was in it. Off: the crafts are only queued, and you press \"Craft queue\" yourself.\n" +
            "补充库存后，如果队列里原本没有别的订单，就自动开始制作。关闭后只加入队列，需要你自己点「制作队列」。", null, "ModManager.ShowWhen=KeepStocked"));
        var resumeOnReopen = Config.Bind("Queue", "ResumeOnReopen", true,
            "Closing a workbench pauses a running queue, and it carries on when you open a workbench again. Off: closing stops the queue, and you press \"Craft queue\" to carry on.\n" +
            "关掉工作台时，正在制作的队列会暂停，再次打开工作台后自动继续。关闭本选项后，关掉工作台会停止队列，要点「制作队列」才会继续。");
        var otherBenches = Config.Bind("Queue", "OtherBenches", true,
            "A paused queue carries on at whichever workbench you open next. Off: it waits for the workbench it was running on. Only matters when ResumeOnReopen is on.\n" +
            "暂停的队列会在你下一个打开的工作台继续，不一定是原来那台。关闭本选项后，只会在原来那台工作台继续。只在 ResumeOnReopen 开启时有用。");

        var log = new HubLog(Log, Path.Combine(Paths.BepInExRootPath, "crafting-hub.log"));
        var harmony = new Harmony(Guid);
        var keep = new KeepPorts(new HomeStock(log.Write), new KeepStore(Path.Combine(Paths.ConfigPath, Guid + ".targets.txt"), log.Write), SaveSlot.CurrentKey);

        log.TrimIfLarge();
        log.Write($"===== Crafting Hub {Version} started");
        OptionalLive.Watch(useAllHomeStorages, isOn => StoragePool.Use(harmony, isOn, log.Write));
        Workbench.Apply(harmony, log.Write);
        RecipeRefresh.Apply(harmony, log.Write);
        PageMessages.Apply(harmony, log.Write);
        CraftTicker.Log = log.Write;
        HubSettings ReadSettings() => new(Math.Max(0, maxDepth.Value), showMarks.Value, keepStocked.Value, autoStart.Value, resumeOnReopen.Value, otherBenches.Value);

        var session = new HubSession(new Workbench(log.Write), new WorkbenchPage(log.Write), keep, ReadSettings(), IsStackingLoaded, log.Write, log.WriteFileOnly);

        OptionalLive.Watch(() =>
        {
            var settings = ReadSettings();

            session.UseSettings(settings);
            log.Write(Describe(settings));
        }, maxDepth, showMarks, keepStocked, autoStart, resumeOnReopen, otherBenches);

        CraftTicker.Session = session;
        AddComponent<CraftTicker>();
        HubWarmUp.Start(log.WriteFileOnly);
    }

    private static string Describe(HubSettings settings) =>
        $"intermediates {(settings.MaxDepth > 0 ? $"up to {settings.MaxDepth} deep" : "off")}, marks {OnOff(settings.ShowMarks)}, keep stocked {OnOff(settings.KeepStocked)}, auto start {OnOff(settings.AutoStartTopUps)}, resume on reopen {OnOff(settings.ResumeOnReopen)}, other workbenches {OnOff(settings.ResumeAtOtherBenches)}";

    private static string OnOff(bool isOn) => isOn ? "on" : "off";

    /// <summary>Stacking's fill takes part of a stack, so a material held in one big stack can still be used.</summary>
    private static bool IsStackingLoaded() => IL2CPPChainloader.Instance.Plugins.ContainsKey(StackingGuid);
}
