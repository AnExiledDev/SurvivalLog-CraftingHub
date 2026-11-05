using System;
using CraftingHub.Core;
using ExiledToolkit.Kit;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace CraftingHub.Adapters;

/// <summary>Steps the session ten times a second on real time, so a paused game still crafts from the queue.</summary>
public sealed class CraftTicker : MonoBehaviour
{
    private const float TickSeconds = 0.1f;

    internal static HubSession? Session;
    internal static Action<string> Log = _ => { };

    /// <summary>The Toolkit's clock at the mod's first read of it, the first frame; the session's timers count from there.</summary>
    private static double _startedAt = double.NaN;

    private readonly TickFailures _failures = new();
    private float _nextTickAt;

    public CraftTicker(IntPtr pointer) : base(pointer)
    {
    }

    /// <summary>
    /// Seconds since the mod first asked. The Toolkit's clock counts from system start, too far out for the session's
    /// float timers to keep a tenth of a second, so they count from the first frame instead. Main thread only.
    /// </summary>
    internal static float Now()
    {
        var now = Clock.Now;

        if (double.IsNaN(_startedAt)) _startedAt = now;

        return (float)(now - _startedAt);
    }

    private void Update()
    {
        RecipeRefresh.Flush();

        var now = Now();

        if (now < _nextTickAt) return;

        _nextTickAt = now + TickSeconds;

        try
        {
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

    [HideFromIl2Cpp]
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
}
