using System;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace CraftingHub.Adapters;

/// <summary>Steps the session ten times a second on real time, so a paused game still crafts from the queue.</summary>
public sealed class CraftTicker : MonoBehaviour
{
    private const float TickSeconds = 0.1f;
    private const int MaxLoggedFailures = 3;
    private const string BetterCraftingGuid = "com.ivmakk.survivallog.bettercrafting";

    internal static HubSession? Session;
    internal static Action<string> Log = _ => { };

    private float _nextTickAt;
    private int _failures;
    private bool _hasCheckedConflicts;

    public CraftTicker(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        var now = Time.unscaledTime;

        if (now < _nextTickAt) return;

        _nextTickAt = now + TickSeconds;

        try
        {
            WarnAboutConflictsOnce();
            Session?.Tick(now);
        }
        catch (Exception ex)
        {
            _failures++;

            if (_failures <= MaxLoggedFailures) Log($"tick failed (logged {_failures} of {MaxLoggedFailures}): {ex}");
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
