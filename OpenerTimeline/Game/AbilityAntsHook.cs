using System;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using OpenerTimeline.Data;

namespace OpenerTimeline.Game;

/// <summary>
/// Makes the game draw its proc outline ("ants") around the action the opener expects next, so the
/// cue is on the hotbar itself as well as on the timeline.
/// </summary>
internal sealed unsafe class AbilityAntsHook : IDisposable
{
    private readonly Hook<ActionManager.Delegates.IsActionHighlighted>? hook;

    public AbilityAntsHook()
    {
        hook = Plugin.GameInteropProvider.HookFromAddress<ActionManager.Delegates.IsActionHighlighted>(
            ActionManager.MemberFunctionPointers.IsActionHighlighted, Detour);
    }

    /// <summary>The opener step to highlight; zero for none.</summary>
    public int CurrentStep { get; set; }

    public void SetEnabled(bool enabled)
    {
        if (hook == null || hook.IsEnabled == enabled)
            return;

        if (enabled)
            hook.Enable();
        else
            hook.Disable();
    }

    /// <summary>Whether the game highlights an action on its own, ignoring what this hook adds.</summary>
    public bool IsHighlightedByGame(uint actionId)
    {
        var manager = ActionManager.Instance();
        if (manager == null)
            return false;

        return hook is { IsEnabled: true }
                   ? hook.Original(manager, ActionType.Action, actionId)
                   : manager->IsActionHighlighted(ActionType.Action, actionId);
    }

    public void Dispose()
    {
        hook?.Dispose();
    }

    private bool Detour(ActionManager* manager, ActionType actionType, uint actionId)
    {
        var original = hook!.Original(manager, actionType, actionId);
        var step = CurrentStep;
        if (original || step == 0 || actionType != ActionType.Action)
            return original;

        if (step > 0)
        {
            // The hotbar asks about the action in the slot, which may be showing as something else.
            return actionId == (uint)step || manager->GetAdjustedActionId(actionId) == (uint)step;
        }

        return GroupOfActions.TryGetDefault(step, out var group) && group.IsMember(actionId);
    }
}
