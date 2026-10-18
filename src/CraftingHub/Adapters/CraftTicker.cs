using System;
using BepInEx.Unity.IL2CPP;
using CraftingHub.Core;
using UnityEngine;

namespace CraftingHub.Adapters;

/// <summary>Steps the session ten times a second on real time, so a paused game still crafts from the queue.</summary>
public sealed class CraftTicker : MonoBehaviour
{
    private const float TickSeconds = 0.1f;
    private const string BetterCraftingGuid = "com.ivmakk.survivallog.bettercrafting";

    internal static HubSession? Session;
    internal static Action<string> Log = _ => { };

    private readonly TickFailures _failures = new();
    private float _nextTickAt;
    private bool _hasCheckedConflicts;

    public CraftTicker(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        RecipeRefresh.Flush();

        var now = Time.unscaledTime;

        if (now < _nextTickAt) return;

        _nextTickAt = now + TickSeconds;

        try
        {
            WarnAboutConflictsOnce();
            Session?.Tick(now);
            _failures.Succeeded();
        }
        catch (Exception ex)
        {
            NoteFailure(ex);
        }
    }

    /// <summary>A recipe list rebuild the game asked for this frame runs before the frame is drawn.</summary>
    private void LateUpdate() => RecipeRefresh.Flush();

    private void NoteFailure(Exception ex)
    {
        var line = _failures.Failed(Environment.TickCount64, ex.ToString(), StopSession);

        if (line != null) Log(line);
    }

    /// <summary>True once a run is stopped. A stop that throws counts as one too, so it is not tried on every tick.</summary>
    private static bool StopSession()
    {
        try
        {
            return Session?.StopForErrors() == true;
        }
        catch (Exception ex)
        {
            Log("could not stop the craft queue after repeated errors: " + ex.Message);

            return true;
        }
    }

    /// <summary>Checked on the first tick, once every plugin has loaded.</summary>
    private void WarnAboutConflictsOnce()
    {
        if (_hasCheckedConflicts) return;

        _hasCheckedConflicts = true;

        if (IL2CPPChainloader.Instance.Plugins.ContainsKey(BetterCraftingGuid)) Log("BetterCrafting is also loaded; both change the workbench's storage list, remove one of them");
    }
}
