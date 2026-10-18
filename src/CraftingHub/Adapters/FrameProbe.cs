using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using GameCore.HotUpdate.ReduxUI;
using HarmonyLib;

namespace CraftingHub.Adapters;

/// <summary>
/// Finds where a slow frame went. The workbench methods that run when a craft starts, finishes or pulls materials
/// are timed, along with this mod's own work, and any frame over the threshold is written to the log with what ran
/// in it and whether a garbage collection happened. Static because Harmony patches have nowhere else to look.
/// </summary>
internal static class FrameProbe
{
    private const double SlowFrameMs = 50;
    private const int MaxSlowLines = 400;

    /// <summary>The workbench steps behind starting, finishing and collecting a craft, and pulling its materials.</summary>
    private static readonly string[] TimedMethods =
    {
        nameof(Reducer_Web_ToolTable.RefreshRecipeList),
        nameof(Reducer_Web_ToolTable.RA_Make),
        nameof(Reducer_Web_ToolTable.RA_RecipeClick),
        nameof(Reducer_Web_ToolTable.RA_CheckResult),
        nameof(Reducer_Web_ToolTable.RA_Result),
        nameof(Reducer_Web_ToolTable.RA_GetNew),
        nameof(Reducer_Web_ToolTable.RA_RefreshBag),
        nameof(Reducer_Web_ToolTable.RA_RefreshBagUI),
        nameof(Reducer_Web_ToolTable.ContinueBatchAfterCollect),
        nameof(Reducer_Web_ToolTable.ApplyFillRecipe),
        nameof(Reducer_Web_ToolTable.SelectItemsForRecipeLinked),
        nameof(Reducer_Web_ToolTable.RefreshBagItems),
        nameof(Reducer_Web_ToolTable.UpdateMaxCraftCount),
        nameof(Reducer_Web_ToolTable.UpdateWorkbenchPreview),
        nameof(Reducer_Web_ToolTable.CheckMaterialSufficiencyAcrossLinked),
        nameof(Reducer_Web_ToolTable.MarkShortageAcrossLinked),
        nameof(Reducer_Web_ToolTable.BuildMaterialsJson),
        nameof(Reducer_Web_ToolTable.RefreshBenchOriginMarks),
        nameof(Reducer_Web_ToolTable.CountOwnedInPool),
        nameof(Reducer_Web_ToolTable.GetAllLinkedOwnerIds),
    };

    private static readonly Dictionary<string, Span> Spans = new();
    private static readonly Dictionary<MethodBase, string> Names = new();
    private static Action<string> _log = _ => { };
    private static int _slowLines;
    private static int _managedGcs;
    private static int _gameGcs;

    private sealed class Span
    {
        public int Calls;
        public long Ticks;
    }

    public static void Apply(Harmony harmony, Action<string> log)
    {
        _log = log;
        _managedGcs = GC.CollectionCount(0);
        _gameGcs = GameGcs();

        // First in and last out, so a method's time includes every other mod's patches on it.
        var prefix = new HarmonyMethod(typeof(FrameProbe), nameof(Started)) { priority = Priority.First };
        var postfix = new HarmonyMethod(typeof(FrameProbe), nameof(Finished)) { priority = Priority.Last };
        var timed = 0;

        foreach (var name in TimedMethods)
        {
            // Overloads that only forward to the full one would count the same work twice.
            var method = typeof(Reducer_Web_ToolTable).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(candidate => candidate.Name == name)
                .OrderByDescending(candidate => candidate.GetParameters().Length)
                .FirstOrDefault();

            if (method == null)
            {
                log("frame timing: no workbench method " + name);

                continue;
            }

            try
            {
                Names[method] = name;
                harmony.Patch(method, prefix: prefix, postfix: postfix);
                timed++;
            }
            catch (Exception ex)
            {
                log($"frame timing: could not time {name}: {ex.Message}");
            }
        }

        log($"frame timing on: {timed} workbench methods, frames over {SlowFrameMs} ms are logged");
    }

    /// <summary>Start of a stretch of this mod's own work, ended by <see cref="End"/>.</summary>
    public static long Begin() => Stopwatch.GetTimestamp();

    public static void End(string name, long startedAt) => Add(name, Stopwatch.GetTimestamp() - startedAt);

    /// <summary>Called once a frame. A frame's length shows in the next frame's delta, so the work counted since the last call is what made it slow.</summary>
    public static void FrameEnded(float frameSeconds)
    {
        var managedGcs = GC.CollectionCount(0);
        var gameGcs = GameGcs();
        var frameMs = frameSeconds * 1000;

        if (frameMs > SlowFrameMs && _slowLines < MaxSlowLines)
        {
            _slowLines++;
            _log($"slow frame {frameMs:0} ms: {Describe()} | gc managed +{managedGcs - _managedGcs} game +{gameGcs - _gameGcs}");
        }

        _managedGcs = managedGcs;
        _gameGcs = gameGcs;
        Spans.Clear();
    }

    private static string Describe()
    {
        if (Spans.Count == 0) return "nothing timed ran";

        var parts = Spans
            .OrderByDescending(span => span.Value.Ticks)
            .Select(span => $"{span.Key} {span.Value.Calls}x {span.Value.Ticks * 1000.0 / Stopwatch.Frequency:0.#} ms");

        return string.Join(", ", parts);
    }

    private static void Started(out long __state) => __state = Stopwatch.GetTimestamp();

    private static void Finished(long __state, MethodBase __originalMethod)
    {
        try
        {
            Add(__originalMethod != null && Names.TryGetValue(__originalMethod, out var name) ? name : "workbench", Stopwatch.GetTimestamp() - __state);
        }
        catch
        {
            // Timing must never break the game's own method.
        }
    }

    private static void Add(string name, long ticks)
    {
        if (!Spans.TryGetValue(name, out var span)) Spans[name] = span = new Span();

        span.Calls++;
        span.Ticks += ticks;
    }

    private static int GameGcs()
    {
        try
        {
            return Il2CppSystem.GC.CollectionCount(0);
        }
        catch
        {
            return 0;
        }
    }
}
