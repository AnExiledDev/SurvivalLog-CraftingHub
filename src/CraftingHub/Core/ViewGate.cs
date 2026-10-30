using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>
/// The player's stamina as the page shows it, before it is spread over the recipe list: how much they have, whether
/// it holds the queue up, and what one craft costs. Small enough to compare every tick.
/// </summary>
public readonly record struct StaminaGist(int Have, bool IsOut, int Cost)
{
    /// <summary>The rules are <see cref="StaminaView.From"/>'s; null when stamina could not be read.</summary>
    public static StaminaGist? Of(StaminaRead? read, bool hasQueue, bool hasStoppedForStamina = false)
    {
        if (read is not { } stamina || stamina.IsUnanswered) return null;

        var isOut = hasQueue && (stamina.IsShort || hasStoppedForStamina && !stamina.CoversACraft);

        return new StaminaGist(stamina.Have, isOut, stamina.Cost);
    }

    public StaminaView For(IEnumerable<string> recipeKeys)
    {
        var cost = Cost;

        return new StaminaView(Have, IsOut, cost > 0 ? recipeKeys.Distinct().ToDictionary(key => key, _ => cost) : new Dictionary<string, int>());
    }
}

/// <summary>
/// Everything the panel is drawn from. Lists and maps compare by which one they are, and the queue and the targets,
/// changed in place, by how many times they changed, so two equal inputs always draw the same panel and comparing
/// them costs no more than a few fields.
/// </summary>
public readonly record struct ViewInputs(
    IReadOnlyDictionary<string, string> Marks,
    CraftQueue Queue,
    int QueueVersion,
    bool IsRunning,
    bool IsPaused,
    long ActiveOrderId,
    string Status,
    bool KeepOn,
    KeepTargets Targets,
    int TargetsVersion,
    IReadOnlyDictionary<string, int> KeepByKey,
    IReadOnlyDictionary<int, int>? KeepHave,
    QueueOutlook Outlook,
    IReadOnlyDictionary<string, int> Yields,
    IReadOnlyList<string> RecipeKeys,
    StaminaGist? Stamina,
    bool TakesUnits,
    IReadOnlyList<DyeOption> Dyes,
    float CraftSpeed = 0f)
{
    public string Build() => HubView.Build(Marks, Queue.Entries, IsRunning, Status, new KeepView(KeepOn, Targets.All, KeepByKey, KeepHave), IsPaused, Outlook, Yields, ActiveOrderId, Stamina?.For(RecipeKeys), TakesUnits, Dyes, CraftSpeed);
}

/// <summary>
/// Decides what the page is sent. Inputs equal to the last ones send nothing and build nothing; changed inputs build
/// the view, which is sent only when it differs from the last one sent.
/// </summary>
public sealed class ViewGate
{
    private ViewInputs? _built;
    private string? _sent;

    /// <summary>The view to send, or null when the page already shows it.</summary>
    public string? Next(in ViewInputs inputs)
    {
        if (_built is { } built && built.Equals(inputs)) return null;

        _built = inputs;

        var view = inputs.Build();

        if (view == _sent) return null;

        _sent = view;

        return view;
    }

    /// <summary>The page lost what it showed (it was loaded again), so the next view is sent even when unchanged.</summary>
    public void Forget()
    {
        _built = null;
        _sent = null;
    }
}
