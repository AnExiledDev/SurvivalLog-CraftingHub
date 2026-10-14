using System;

namespace CraftingHub.Core;

/// <summary>
/// How the ticker answers its own failures. The first is logged in full, later ones at most once a minute with a count,
/// so a failure on every tick cannot flood the log; a run that keeps failing is stopped rather than left half-working.
/// </summary>
public sealed class TickFailures
{
    public const long LogEveryMs = 60_000;
    public const int BeforeStop = 20;

    private int _inARow;
    private int _total;
    private int _atLastLog;
    private long _lastLogAt;
    private bool _hasStoppedRun;

    /// <summary>A tick that went through ends the failures in a row, so later ones may stop a run again.</summary>
    public void Succeeded()
    {
        _inARow = 0;
        _hasStoppedRun = false;
    }

    /// <summary>
    /// Counts a failure and, once enough come in a row, asks for the run to stop. The stop answers whether it stopped
    /// a run; only then is it not asked again until a tick goes through, so failures while nothing runs still leave
    /// the next run covered. Returns the line to log, or null while the last report is under a minute old.
    /// </summary>
    public string? Failed(long nowMs, string error, Func<bool> stopRun)
    {
        _total++;
        _inARow++;

        var line = IsReportDue(nowMs) ? Report(nowMs, error) : null;

        if (_inARow >= BeforeStop && !_hasStoppedRun) _hasStoppedRun = stopRun();

        return line;
    }

    private bool IsReportDue(long nowMs) => _total == 1 || nowMs - _lastLogAt >= LogEveryMs;

    private string Report(long nowMs, string error)
    {
        var line = $"tick failed ({_total - _atLastLog} since the last report, {_total} in all): {error}";
        _lastLogAt = nowMs;
        _atLastLog = _total;

        return line;
    }
}
