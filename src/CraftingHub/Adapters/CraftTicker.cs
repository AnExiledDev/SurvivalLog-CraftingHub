using System;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace CraftingHub.Adapters;

/// <summary>Steps the session ten times a second on real time, so a paused game still crafts from the queue.</summary>
public sealed class CraftTicker : MonoBehaviour
{
    private const float TickSeconds = 0.1f;
    private const long FailureLogEveryMs = 60_000;
    private const int FailuresBeforeStop = 20;
    private const string BetterCraftingGuid = "com.ivmakk.survivallog.bettercrafting";

    internal static HubSession? Session;
    internal static Action<string> Log = _ => { };

    private float _nextTickAt;
    private int _failuresInARow;
    private int _failures;
    private int _failuresAtLastLog;
    private long _lastFailureLogAt;
    private bool _hasStoppedForErrors;
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
            _failuresInARow = 0;
            _hasStoppedForErrors = false;
        }
        catch (Exception ex)
        {
            NoteFailure(ex);
        }
    }

    /// <summary>
    /// The first failure is logged in full, later ones at most once a minute with a count, so a failure on every tick
    /// cannot flood the log; a run that keeps failing is stopped rather than left half-working.
    /// </summary>
    private void NoteFailure(Exception ex)
    {
        var at = Environment.TickCount64;

        _failures++;
        _failuresInARow++;

        if (_failures == 1 || at - _lastFailureLogAt >= FailureLogEveryMs)
        {
            Log($"tick failed ({_failures - _failuresAtLastLog} since the last report, {_failures} in all): {ex}");
            _lastFailureLogAt = at;
            _failuresAtLastLog = _failures;
        }

        if (_failuresInARow < FailuresBeforeStop || _hasStoppedForErrors) return;

        _hasStoppedForErrors = true;
        StopSession();
    }

    private static void StopSession()
    {
        try
        {
            Session?.StopForErrors();
        }
        catch (Exception ex)
        {
            Log("could not stop the craft queue after repeated errors: " + ex.Message);
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
