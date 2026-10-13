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
    private const string Version = "0.5.2";

    public override void Load()
    {
        var useAllHomeStorages = Config.Bind("Storage", "UseAllHomeStorages", true,
            "Workbenches use materials from every storage in your home, not only the storages linked to them. Off: only the linked storages, as in the base game.\n" +
            "工作台可以使用家里所有储物家具中的材料，不只是接入它的那几个。关闭后只用接入的储物家具，和原版游戏一样。").Value;
        var maxDepth = Config.Bind("Chain", "MaxDepth", 4,
            "How many layers of intermediates can be crafted for one recipe. Making planks for a crate is one layer, and 0 turns intermediates off.\n" +
            "做一个配方时，最多可以先做几层中间材料（先做出来、再拿去做成品的材料）。0 表示不做中间材料。").Value;
        var showMarks = Config.Bind("Chain", "ShowMarks", true,
            "Mark recipes you can make once their intermediates are crafted: they show \"Makes first\" in amber and get a \"Craft with intermediates\" button. Off hides both, and queued orders still make intermediates.\n" +
            "给先做中间材料就能做的配方加上标记：以琥珀色显示「Makes first」，并提供「Craft with intermediates」按钮。关闭后两者都不显示，队列里的订单仍会先做中间材料。").Value;
        var keepStocked = Config.Bind("Targets", "KeepStocked", true,
            "Adds a Keep button to every recipe and a \"Keep in stock\" list under the queue. Each time you open a workbench, crafts are queued to bring kept items back up to their counts.\n" +
            "在每个配方上加「Keep」按钮，并在队列下方加「Keep in stock」列表。每次打开工作台，都会把需要的制造加入队列，让这些物品补回你设定的数量。").Value;
        var autoStart = Config.Bind("Targets", "AutoStart", true,
            "After kept items are topped up, start the queue by itself if nothing else was in it. Off: the crafts are only queued, and you press \"Craft queue\" yourself.\n" +
            "补充库存后，如果队列里原本没有别的订单，就自动开始制造。关闭后只加入队列，需要你自己点「Craft queue」。").Value;
        var resumeOnReopen = Config.Bind("Queue", "ResumeOnReopen", true,
            "Closing a workbench pauses a running queue, and it carries on when you open a workbench again. Off: closing stops the queue, and you press \"Craft queue\" to carry on.\n" +
            "关掉工作台时，正在制造的队列会暂停，再次打开工作台后自动继续。关闭本选项后，关掉工作台会停止队列，要点「Craft queue」才会继续。").Value;
        var otherBenches = Config.Bind("Queue", "OtherBenches", true,
            "A paused queue carries on at whichever workbench you open next. Off: it waits for the workbench it was running on. Only matters when ResumeOnReopen is on.\n" +
            "暂停的队列会在你下一个打开的工作台继续，不一定是原来那台。关闭本选项后，只会在原来那台工作台继续。只在 ResumeOnReopen 开启时有用。").Value;

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
