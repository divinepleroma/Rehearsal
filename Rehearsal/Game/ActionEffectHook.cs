using System;
using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace Rehearsal.Game;

/// <summary>
/// Reports every action and item the local player actually executes. Hooking the effect handler
/// (rather than the button press) means only things that really went off are counted.
/// </summary>
internal sealed unsafe class ActionEffectHook : IDisposable
{
    private readonly Hook<ActionEffectHandler.Delegates.Receive>? hook;
    private readonly Action<uint, bool> onUsed;
    private bool loggedFailure;

    /// <param name="onUsed">Called with the action (or item) id and whether it was an item.</param>
    public ActionEffectHook(Action<uint, bool> onUsed)
    {
        this.onUsed = onUsed;
        hook = Plugin.GameInteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
            ActionEffectHandler.MemberFunctionPointers.Receive, Detour);
        hook.Enable();
    }

    public void Dispose()
    {
        hook?.Dispose();
    }

    private void Detour(
        uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        hook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);

        // Nothing in here may ever throw back into the game.
        try
        {
            if (header == null || !Plugin.PlayerState.IsLoaded || casterEntityId != Plugin.PlayerState.EntityId)
                return;

            switch ((ActionType)header->ActionType)
            {
                case ActionType.Action:
                    onUsed(header->ActionId, false);
                    break;
                case ActionType.Item:
                    onUsed(header->ActionId, true);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!loggedFailure)
            {
                loggedFailure = true;
                Plugin.Log.Error(ex, "Failed to process a used action");
            }
        }
    }
}
