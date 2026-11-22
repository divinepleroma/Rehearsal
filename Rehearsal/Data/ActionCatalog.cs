using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Action = Lumina.Excel.Sheets.Action;

namespace Rehearsal.Data;

/// <summary>How an action's cooldown relates to the burst cycle.</summary>
public enum CooldownClass : byte
{
    /// <summary>No cooldown worth pointing out.</summary>
    None,

    /// <summary>A cooldown of 30s or more that does not line up with the one or two minute windows.</summary>
    Long,

    /// <summary>Used every minute (55-60s recast).</summary>
    OneMinute,

    /// <summary>Used every two minutes (100-120s recast).</summary>
    TwoMinute
}

public readonly record struct ActionInfo(
    uint Id,
    string Name,
    uint IconId,
    bool IsGcd,
    float RecastSeconds,
    float CastSeconds,
    float GcdSeconds,
    byte MaxCharges,
    bool IsRoleAction,
    byte Level,
    string JobCategory,
    bool IsPlayerAction)
{
    public CooldownClass Cooldown => ActionCatalog.Classify(RecastSeconds);
}

/// <summary>
/// English text needed to work out which action upgrades which. Optional: without it openers can
/// still be fitted to a level, they just lose upgraded actions instead of falling back to the
/// older version.
/// </summary>
/// <param name="Actions">The Action sheet in English.</param>
/// <param name="Traits">The Trait sheet.</param>
/// <param name="TraitText">The TraitTransient sheet (descriptions) in English.</param>
public sealed record UpgradeSource(ExcelSheet<Action> Actions, ExcelSheet<Trait> Traits, ExcelSheet<TraitTransient> TraitText);

/// <summary>
/// Everything the plugin needs to know about player actions, read once from the Action sheet.
/// Depends on Lumina only, so the same code runs in the game and in the offline UI preview tool.
/// </summary>
public sealed class ActionCatalog
{
    public const uint SprintId = 3;
    public const uint TrueNorthId = 7546;

    /// <summary>Length of a global cooldown before any speed is applied.</summary>
    public const float BaseGcdSeconds = 2.5f;

    /// <summary>Recast group shared by everything on the global cooldown.</summary>
    private const byte GlobalCooldownGroup = 58;

    /// <summary>Icon of a strength gemdraught; stands in for "whatever potion you use".</summary>
    private const uint DefaultPotionIcon = 20710;

    private readonly Dictionary<uint, ActionInfo> actions;
    private readonly ActionInfo[] ordered;
    private readonly Dictionary<uint, Upgrade> upgrades = new();

    public ActionCatalog(ExcelSheet<Action> sheet, UpgradeSource? upgradeSource = null, Action<string, Exception>? logError = null)
    {
        PotionIcon = DefaultPotionIcon;

        actions = new Dictionary<uint, ActionInfo>();
        foreach (var row in sheet)
        {
            if (!IsPvEAction(row))
                continue;

            var onGcd = IsOnGlobalCooldown(row);
            actions[row.RowId] = new ActionInfo(
                row.RowId,
                row.Name.ExtractText(),
                row.Icon,
                onGcd,
                row.Recast100ms / 10f,
                row.Cast100ms / 10f,
                GcdLength(row, onGcd),
                row.MaxCharges,
                row.IsRoleAction,
                row.ClassJobLevel,
                row.ClassJobCategory.ValueNullable?.Name.ExtractText() ?? string.Empty,
                row.IsPlayerAction);

            MaxLevel = Math.Max(MaxLevel, row.ClassJobLevel);
        }

        ordered = actions.Values
                         .OrderByDescending(a => a.IsPlayerAction)
                         .ThenBy(a => a.Level)
                         .ThenBy(a => a.Id)
                         .ToArray();

        if (upgradeSource != null)
        {
            try
            {
                ReadUpgrades(upgradeSource);
            }
            catch (Exception ex)
            {
                upgrades.Clear();
                logError?.Invoke("Could not read action upgrades; level-adjusted openers will only drop actions", ex);
            }
        }
    }

    public uint PotionIcon { get; }
    public int Count => actions.Count;

    /// <summary>The current level cap, taken from the highest level any action is learned at.</summary>
    public int MaxLevel { get; } = 1;

    /// <summary>How many upgrade relations were found; zero when no English text was available.</summary>
    public int UpgradeCount => upgrades.Count;

    public static bool IsPvEAction(Action a)
    {
        return a.ActionCategory.RowId is 2 or 3 or 4        // Spell, Weaponskill or Ability
               && a is { IsPvP: false, ClassJobLevel: > 0 } // not an old action
               && a.ClassJobCategory.RowId != 0;            // not an old action
    }

    public static CooldownClass Classify(float recastSeconds)
    {
        return recastSeconds switch
        {
            >= 100f and <= 120f => CooldownClass.TwoMinute,
            >= 55f and <= 60f => CooldownClass.OneMinute,
            >= 30f => CooldownClass.Long,
            _ => CooldownClass.None
        };
    }

    public bool TryGet(uint id, out ActionInfo info)
    {
        return actions.TryGetValue(id, out info);
    }

    public bool Contains(uint id)
    {
        return actions.ContainsKey(id);
    }

    /// <summary>Display name of an opener step (action, group or special step).</summary>
    public string GetName(int stepId)
    {
        if (stepId == SpecialActions.CatchAll)
            return SpecialActions.CatchAllName;
        if (stepId == SpecialActions.Potion)
            return SpecialActions.PotionName;
        if (stepId < 0)
            return GroupOfActions.TryGetDefault(stepId, out var group) ? group.Name : SpecialActions.UnknownName;

        return actions.TryGetValue((uint)stepId, out var info) ? info.Name : SpecialActions.UnknownName;
    }

    /// <summary>Whether an opener step occupies a global cooldown.</summary>
    public bool IsGcd(int stepId)
    {
        if (stepId == SpecialActions.CatchAll)
            return true;
        if (stepId == SpecialActions.Potion)
            return false;
        if (stepId < 0)
            return !GroupOfActions.TryGetDefault(stepId, out var group) || group.IsGCD;

        return !actions.TryGetValue((uint)stepId, out var info) || info.IsGcd;
    }

    /// <summary>Recast of an opener step in seconds, 0 when it has none of interest.</summary>
    public float GetRecast(int stepId)
    {
        return stepId > 0 && actions.TryGetValue((uint)stepId, out var info) ? info.RecastSeconds : 0f;
    }

    /// <summary>How long the global cooldown triggered by a step lasts, before speed. Groups use their first member.</summary>
    public float GetGcdSeconds(int stepId)
    {
        if (stepId < 0 && GroupOfActions.TryGetDefault(stepId, out var group) && group.Actions.Count > 0)
            stepId = (int)group.Actions[0];

        return stepId > 0 && actions.TryGetValue((uint)stepId, out var info) && info.IsGcd ? info.GcdSeconds : BaseGcdSeconds;
    }

    /// <summary>Cast time of a step in seconds, before speed.</summary>
    public float GetCastSeconds(int stepId)
    {
        return stepId > 0 && actions.TryGetValue((uint)stepId, out var info) ? info.CastSeconds : 0f;
    }

    /// <summary>Icon of an opener step. Groups return the icon of their first member.</summary>
    public uint GetIcon(int stepId)
    {
        if (stepId == SpecialActions.CatchAll)
            return SpecialActions.CatchAllIcon;
        if (stepId == SpecialActions.Potion)
            return PotionIcon;
        if (stepId < 0)
        {
            return GroupOfActions.TryGetDefault(stepId, out var group) && group.Actions.Count > 0
                       ? GetIcon((int)group.Actions[0])
                       : SpecialActions.UnknownIcon;
        }

        return actions.TryGetValue((uint)stepId, out var info) ? info.IconId : SpecialActions.UnknownIcon;
    }

    /// <summary>Actions that are fine to press at any time and should not count as mistakes.</summary>
    public bool IsUtility(uint actionId)
    {
        return actionId == SprintId || (actions.TryGetValue(actionId, out var info) && info.IsRoleAction);
    }

    // ---- Levels ----

    /// <summary>The level an action is learned at; 0 when it is not a known action.</summary>
    public int LevelOf(uint actionId)
    {
        return actions.TryGetValue(actionId, out var info) ? info.Level : 0;
    }

    /// <summary>The trait upgrade that produces an action, if it is the upgraded form of another.</summary>
    public Upgrade? UpgradeOf(uint actionId)
    {
        return upgrades.TryGetValue(actionId, out var upgrade) ? upgrade : null;
    }

    /// <summary>The level needed to use every step of an opener.</summary>
    public int RequiredLevel(IReadOnlyList<int> opener)
    {
        return LevelAdapter.RequiredLevel(opener, LevelOf, UpgradeOf);
    }

    /// <summary>Fits an opener to a level: older versions of upgraded actions, unlearned ones left out.</summary>
    public AdaptedOpener Adapt(IReadOnlyList<int> opener, int level)
    {
        return LevelAdapter.Adapt(opener, level, LevelOf, UpgradeOf);
    }

    /// <summary>Searches actions for the editor. Results are ordered by level.</summary>
    /// <param name="maxLevel">Leave out actions learned above this level; 0 for no limit.</param>
    public List<int> Search(string text, Jobs job, ActionTypes type, int maxLevel = 0, int limit = 200)
    {
        var jobName = job.ToString();
        var result = new List<int>();
        foreach (var a in ordered)
        {
            if (text.Length > 0 && !a.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                continue;
            if (type == ActionTypes.GCD && !a.IsGcd)
                continue;
            if (type == ActionTypes.OGCD && a.IsGcd)
                continue;
            if (job != Jobs.ANY && !a.JobCategory.Contains(jobName, StringComparison.Ordinal))
                continue;
            if (maxLevel > 0 && (a.Level > maxLevel || (upgrades.TryGetValue(a.Id, out var upgrade) && upgrade.Level > maxLevel)))
                continue;

            result.Add((int)a.Id);
            if (result.Count >= limit)
                break;
        }

        return result;
    }

    private static bool IsOnGlobalCooldown(Action a)
    {
        // Mudras technically share the global cooldown group, but they are woven like abilities and
        // every opener chart shows them that way.
        if (GroupOfActions.TryGetDefault(-2, out var mudras) && mudras.IsMember(a.RowId))
            return false;

        return a.CooldownGroup == GlobalCooldownGroup || a.AdditionalCooldownGroup == GlobalCooldownGroup;
    }

    /// <summary>
    /// Actions whose own recast group is the global one carry its length (1.5s combo hits, 4s
    /// motifs). Actions with a separate cooldown (Drill, Chain Saw) still roll a normal one.
    /// </summary>
    private static float GcdLength(Action a, bool onGcd)
    {
        if (!onGcd)
            return 0f;

        return a.CooldownGroup == GlobalCooldownGroup && a.Recast100ms > 0 ? a.Recast100ms / 10f : BaseGcdSeconds;
    }

    private void ReadUpgrades(UpgradeSource source)
    {
        // Index the actions we know by their English name, which is what the trait text uses.
        var byName = new Dictionary<string, List<uint>>(StringComparer.Ordinal);
        foreach (var id in actions.Keys)
        {
            if (!source.Actions.TryGetRow(id, out var english))
                continue;

            var name = english.Name.ExtractText();
            if (name.Length == 0)
                continue;

            if (!byName.TryGetValue(name, out var ids))
                byName[name] = ids = [];
            ids.Add(id);
        }

        foreach (var trait in source.Traits)
        {
            var job = trait.ClassJob.ValueNullable?.Abbreviation.ExtractText();
            if (string.IsNullOrEmpty(job) || !source.TraitText.TryGetRow(trait.RowId, out var text))
                continue;

            var description = text.Description.ExtractText();
            if (!description.Contains("Upgrades ", StringComparison.Ordinal))
                continue;

            foreach (var (from, to) in UpgradeParser.Parse(description, name => Find(byName, name, job) != 0))
            {
                var lower = Find(byName, from, job);
                if (lower == 0 || !byName.TryGetValue(to, out var higherIds))
                    continue;

                // The same upgraded action can exist under several ids; all of them fall back alike.
                foreach (var higher in higherIds)
                {
                    if (higher != lower && actions[higher].JobCategory.Contains(job, StringComparison.Ordinal))
                        upgrades[higher] = new Upgrade(lower, trait.Level);
                }
            }
        }
    }

    /// <summary>The action of a job with that English name; the one players actually slot when there are several.</summary>
    private uint Find(Dictionary<string, List<uint>> byName, string name, string job)
    {
        if (!byName.TryGetValue(name, out var ids))
            return 0;

        uint best = 0;
        foreach (var id in ids)
        {
            var info = actions[id];
            if (!info.JobCategory.Contains(job, StringComparison.Ordinal))
                continue;
            if (best == 0 || (info.IsPlayerAction && !actions[best].IsPlayerAction))
                best = id;
        }

        return best;
    }
}
