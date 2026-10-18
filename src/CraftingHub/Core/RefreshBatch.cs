using System;

namespace CraftingHub.Core;

/// <summary>
/// Turns a burst of workbench recipe-list rebuilds into one. The game rebuilds the whole list after every item it
/// moves, so pulling a recipe's materials asks for many rebuilds in a row. Each request is held instead, and the newest
/// runs once, when something is about to read the list or the frame ends. Closing the window drops a held request.
/// </summary>
public sealed class RefreshBatch<T> where T : class
{
    private T? _held;
    private bool _isRunning;

    public bool IsHolding => _held != null;

    /// <summary>
    /// Answers whether a rebuild the game asked for runs now. Only one started by <see cref="Flush"/> does, along with
    /// any the game asks for while it runs; every other request is held, replacing the one held before it.
    /// </summary>
    public bool RunsNow(T request)
    {
        if (_isRunning) return true;

        _held = request;

        return false;
    }

    /// <summary>Runs the held rebuild, if there is one. Returns whether it ran.</summary>
    public bool Flush(Action<T> rebuild)
    {
        if (_isRunning || _held is not { } request) return false;

        _held = null;
        _isRunning = true;

        try
        {
            rebuild(request);
        }
        finally
        {
            _isRunning = false;
        }

        return true;
    }

    public void Drop() => _held = null;
}
