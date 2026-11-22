using System;
using System.Collections.Generic;
using Dalamud.Game.Config;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Rehearsal.Data;
using Rehearsal.UI.Timeline;
using HotbarSlotType = FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule.HotbarSlotType;

namespace Rehearsal.Game;

/// <summary>
/// Works out what the player has to press for an action by looking at where it sits on their own
/// hotbars and what that slot is bound to.
/// </summary>
internal sealed unsafe class HotbarHints
{
    private const int StandardHotbars = 10;
    private const int StandardSlots = 12;
    private const int CrossSlots = 16;
    private const uint MedicineCategory = 44;
    private const int MaxLearned = 4000;

    private readonly Dictionary<uint, Entry> byAction = new();
    private readonly Dictionary<string, Entry> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ActionCatalog catalog;
    private readonly ExcelSheet<GeneralAction> generalActions;
    private readonly Func<uint, bool> isHighlighted;
    private readonly ExcelSheet<Item> items;
    private readonly Settings settings;
    private bool loggedFailure;
    private Entry? potion;

    /// <param name="isHighlighted">Whether the game itself is highlighting an action (its proc outline).</param>
    public HotbarHints(ActionCatalog catalog, Settings settings, Func<uint, bool> isHighlighted)
    {
        this.catalog = catalog;
        this.settings = settings;
        this.isHighlighted = isHighlighted;
        generalActions = Plugin.DataManager.GetExcelSheet<GeneralAction>();
        items = Plugin.DataManager.GetExcelSheet<Item>();
    }

    /// <summary>Set when a new action replacement was observed and the settings should be saved.</summary>
    public bool LearnedSomething { get; set; }

    /// <summary>Re-reads every hotbar. Cheap enough to call a couple of times per second.</summary>
    public void Refresh()
    {
        byAction.Clear();
        byName.Clear();
        potion = null;

        try
        {
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
                return;

            var input = UIInputData.Instance();
            var actionManager = ActionManager.Instance();
            var source = settings.HintSource;
            var preferPad = source == HintSource.Gamepad || (source == HintSource.Auto && IsGamepadMode());

            var standard = module->StandardHotbars;
            for (var bar = 0; bar < standard.Length && bar < StandardHotbars; bar++)
            {
                var slots = standard[bar].Slots;
                for (var index = 0; index < slots.Length && index < StandardSlots; index++)
                {
                    ref var slot = ref slots[index];
                    if (slot.CommandType == HotbarSlotType.Empty)
                        continue;

                    var hint = KeyboardHint(input, bar, index);
                    var score = hint.Kind == HintKind.Key ? 100 : 20;
                    if (hint is { Kind: HintKind.Key, Ctrl: false, Shift: false, Alt: false })
                        score += 5;
                    Register(ref slot, hint, score, actionManager);
                }
            }

            if (source == HintSource.Keyboard)
                return;

            var cross = module->CrossHotbars;
            for (var set = 0; set < cross.Length; set++)
            {
                var slots = cross[set].Slots;
                for (var index = 0; index < slots.Length && index < CrossSlots; index++)
                {
                    ref var slot = ref slots[index];
                    if (slot.CommandType == HotbarSlotType.Empty)
                        continue;

                    // Slot order follows the game's own input ids: left trigger first, and within a
                    // trigger the d-pad then the face buttons, each left, up, right, down.
                    var hint = new KeyHint
                    {
                        Kind = HintKind.Pad,
                        Bar = (byte)(set + 1),
                        Slot = (byte)(index + 1),
                        PadRightTrigger = index >= 8,
                        PadFace = index % 8 >= 4,
                        PadDirection = (byte)(index % 4)
                    };
                    Register(ref slot, hint, preferPad ? 200 - set : 60 - set, actionManager);
                }
            }
        }
        catch (Exception ex)
        {
            if (!loggedFailure)
            {
                loggedFailure = true;
                Plugin.Log.Error(ex, "Could not read the hotbars; keybind hints are unavailable");
            }
        }
    }

    /// <summary>The hint for an opener step.</summary>
    /// <param name="stepId">Opener step id (action, group or special step).</param>
    /// <param name="isCurrent">It is the step expected right now, so live game state may narrow it down.</param>
    public KeyHint Get(int stepId, bool isCurrent)
    {
        if (stepId == SpecialActions.CatchAll)
            return default;

        if (stepId == SpecialActions.Potion)
            return potion?.Hint ?? KeyHint.Missing;

        if (stepId > 0)
            return TryResolve((uint)stepId, out var hint) ? hint : KeyHint.Missing;

        if (!GroupOfActions.TryGetDefault(stepId, out var group))
            return default;

        // A group is satisfied by any of its members. If they all live on one button, show it.
        KeyHint? only = null;
        var distinct = 0;
        foreach (var member in group.Actions)
        {
            if (!TryResolve(member, out var memberHint))
                continue;

            if (only == null || !SameButton(only.Value, memberHint))
                distinct++;
            only ??= memberHint;
        }

        switch (distinct)
        {
            case 0:
                return KeyHint.Missing;
            case 1:
                return only!.Value;
        }

        // Otherwise the game knows which one it wants right now (a dance step, a proc).
        if (isCurrent)
        {
            foreach (var member in group.Actions)
            {
                if (isHighlighted(member) && TryResolve(member, out var memberHint))
                    return memberHint;
            }
        }

        return KeyHint.Varies;
    }

    private bool TryResolve(uint actionId, out KeyHint hint)
    {
        if (byAction.TryGetValue(actionId, out var entry))
        {
            hint = entry.Hint;
            return true;
        }

        // A button that only turns into this action mid-combat: use what we saw it replace before.
        if (settings.LearnedBases.TryGetValue(actionId, out var slotted) && byAction.TryGetValue(slotted, out entry))
        {
            hint = entry.Hint;
            return true;
        }

        // Same action under another id.
        if (catalog.TryGet(actionId, out var info) && info.Name.Length > 0 && byName.TryGetValue(info.Name, out entry))
        {
            hint = entry.Hint;
            return true;
        }

        hint = default;
        return false;
    }

    private void Register(ref RaptureHotbarModule.HotbarSlot slot, KeyHint hint, int score, ActionManager* actionManager)
    {
        switch (slot.CommandType)
        {
            case HotbarSlotType.Action:
            {
                var slotted = slot.CommandId;
                Add(slotted, hint, score);

                // The button may currently be something else: a trait upgrade or a combo follow-up.
                if (actionManager != null)
                {
                    var adjusted = actionManager->GetAdjustedActionId(slotted);
                    if (adjusted != 0 && adjusted != slotted)
                    {
                        Add(adjusted, hint, score);
                        Learn(adjusted, slotted);
                    }
                }

                if (slot.ApparentSlotType == HotbarSlotType.Action && slot.ApparentActionId != 0 && slot.ApparentActionId != slotted)
                {
                    Add(slot.ApparentActionId, hint, score);
                    Learn(slot.ApparentActionId, slotted);
                }

                break;
            }
            case HotbarSlotType.GeneralAction:
            {
                if (generalActions.TryGetRow(slot.CommandId, out var general) && general.Action.RowId != 0)
                    Add(general.Action.RowId, hint, score);
                break;
            }
            case HotbarSlotType.Item:
            {
                // High quality items are stored with an offset of one million.
                var itemId = slot.CommandId % 1_000_000;
                if (items.TryGetRow(itemId, out var item) && item.ItemUICategory.RowId == MedicineCategory
                                                          && (potion == null || score > potion.Value.Score))
                    potion = new Entry(hint, score);
                break;
            }
        }
    }

    private void Add(uint actionId, KeyHint hint, int score)
    {
        if (actionId == 0)
            return;

        if (!byAction.TryGetValue(actionId, out var existing) || score > existing.Score)
            byAction[actionId] = new Entry(hint, score);

        if (catalog.TryGet(actionId, out var info) && info.Name.Length > 0
                                                   && (!byName.TryGetValue(info.Name, out existing) || score > existing.Score))
            byName[info.Name] = new Entry(hint, score);
    }

    private void Learn(uint transformed, uint slotted)
    {
        if (!catalog.Contains(transformed))
            return;
        if (settings.LearnedBases.TryGetValue(transformed, out var known) && known == slotted)
            return;
        if (settings.LearnedBases.Count >= MaxLearned)
            return;

        settings.LearnedBases[transformed] = slotted;
        LearnedSomething = true;
    }

    private static KeyHint KeyboardHint(UIInputData* input, int bar, int index)
    {
        var hint = new KeyHint { Kind = HintKind.Slot, Bar = (byte)(bar + 1), Slot = (byte)(index + 1) };
        if (input == null)
            return hint;

        var keybind = input->GetKeybind((InputId)((int)InputId.HOTBAR_1_1 + bar * StandardSlots + index));
        if (keybind == null)
            return hint;

        // Each input has a primary and a secondary binding; show the first one that is set.
        foreach (ref var setting in keybind->KeySettings)
        {
            var label = KeyLabels.For(setting.Key);
            if (label == null)
                continue;

            hint.Kind = HintKind.Key;
            hint.Key = label;
            hint.Ctrl = (setting.KeyModifier & KeyModifierFlag.Ctrl) != 0;
            hint.Shift = (setting.KeyModifier & KeyModifierFlag.Shift) != 0;
            hint.Alt = (setting.KeyModifier & KeyModifierFlag.Alt) != 0;
            break;
        }

        return hint;
    }

    private static bool SameButton(in KeyHint a, in KeyHint b)
    {
        return a.Kind == b.Kind && a.Bar == b.Bar && a.Slot == b.Slot && a.Key == b.Key && a.Ctrl == b.Ctrl && a.Shift == b.Shift
               && a.Alt == b.Alt;
    }

    private static bool IsGamepadMode()
    {
        try
        {
            return Plugin.GameConfig.TryGet(UiConfigOption.PadMode, out bool padMode) && padMode;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct Entry(KeyHint Hint, int Score);
}
