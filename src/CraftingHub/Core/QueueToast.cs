namespace CraftingHub.Core;

/// <summary>How a queue message reads as a toast.</summary>
public enum QueueToastTone
{
    Info,
    Done,
    Warning,
}

/// <summary>
/// The queue's messages show on the workbench's status line, which nobody sees once the workbench is closed. Those that
/// still matter then (the queue finished, stopped or paused, or a notice) become a toast instead.
/// </summary>
public static class QueueToast
{
    /// <summary>The toast for a runner command, or null when the bench is open or the command is not news.</summary>
    public static (string Text, QueueToastTone Tone)? Of(RunnerCommand command, bool isBenchOpen)
    {
        if (isBenchOpen) return null;

        (string Text, QueueToastTone Tone)? toast = command switch
        {
            RunnerCommand.Stopped { IsDone: true } stopped => (stopped.Reason, QueueToastTone.Done),
            RunnerCommand.Stopped stopped => (stopped.Reason, QueueToastTone.Warning),
            RunnerCommand.Paused paused => (paused.Reason, QueueToastTone.Info),
            RunnerCommand.Notice notice => (notice.Text, QueueToastTone.Info),
            _ => null,
        };

        return toast is { } t && !string.IsNullOrEmpty(t.Text) ? toast : null;
    }
}
