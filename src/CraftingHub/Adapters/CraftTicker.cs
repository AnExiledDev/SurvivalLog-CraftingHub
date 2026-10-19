using System;
using CraftingHub.Core;
using UnityEngine;

namespace CraftingHub.Adapters;

/// <summary>Steps the session ten times a second on real time, so a paused game still crafts from the queue.</summary>
public sealed class CraftTicker : MonoBehaviour
{
    private const float TickSeconds = 0.1f;

    internal static HubSession? Session;
    internal static Action<string> Log = _ => { };

    private readonly TickFailures _failures = new();
    private float _nextTickAt;

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
}
