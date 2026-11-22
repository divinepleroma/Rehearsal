using System;
using System.Collections.Generic;

namespace Rehearsal.Data;

/// <summary>An action that a trait turns into a stronger one at some level.</summary>
/// <param name="Lower">The action it replaces.</param>
/// <param name="Level">The level at which the upgrade is learned.</param>
public readonly record struct Upgrade(uint Lower, int Level);

/// <summary>What <see cref="LevelAdapter.Adapt"/> did to an opener.</summary>
/// <param name="Actions">The opener as it can be played at the requested level.</param>
/// <param name="Removed">Steps dropped because nothing equivalent exists at that level.</param>
/// <param name="Replaced">Steps swapped for the version of the action available at that level.</param>
public readonly record struct AdaptedOpener(List<int> Actions, int Removed, int Replaced)
{
    public bool Changed => Removed > 0 || Replaced > 0;
}

/// <summary>
/// Fits an opener written for the level cap to a lower level (level sync, old Ultimates, a job
/// that is still being levelled): upgraded actions fall back to the version you have, and actions
/// you have not learned are left out. The result is a reasonable approximation, not a guide.
/// </summary>
public static class LevelAdapter
{
    /// <summary>Level caps of the expansions; a level belongs to the first cap it does not exceed.</summary>
    public static readonly int[] LevelCaps = [50, 60, 70, 80, 90, 100];

    /// <summary>The expansion level cap a character level plays at.</summary>
    public static int Bracket(int level)
    {
        foreach (var cap in LevelCaps)
            if (level <= cap)
                return cap;
        return LevelCaps[^1];
    }

    /// <summary>The level needed to use every step of an opener.</summary>
    /// <param name="opener">Opener steps.</param>
    /// <param name="levelOf">Level at which an action is learned; 0 when unknown.</param>
    /// <param name="upgradeOf">The upgrade that produces an action, if it is one.</param>
    public static int RequiredLevel(IReadOnlyList<int> opener, Func<uint, int> levelOf, Func<uint, Upgrade?> upgradeOf)
    {
        var required = 1;
        foreach (var step in opener)
        {
            if (step <= 0)
                continue;

            var id = (uint)step;
            required = Math.Max(required, levelOf(id));
            if (upgradeOf(id) is { } upgrade)
                required = Math.Max(required, upgrade.Level);
        }

        return required;
    }

    /// <summary>Rewrites an opener for a level.</summary>
    /// <param name="opener">Opener steps (the stored id format: actions, groups, special steps).</param>
    /// <param name="level">The level to play it at.</param>
    /// <param name="levelOf">Level at which an action is learned; 0 when unknown.</param>
    /// <param name="upgradeOf">The upgrade that produces an action, if it is one.</param>
    public static AdaptedOpener Adapt(IReadOnlyList<int> opener, int level, Func<uint, int> levelOf, Func<uint, Upgrade?> upgradeOf)
    {
        var result = new List<int>(opener.Count);
        var removed = 0;
        var replaced = 0;

        foreach (var step in opener)
        {
            if (step == SpecialActions.CatchAll || step == SpecialActions.Potion)
            {
                result.Add(step);
                continue;
            }

            if (step < 0)
            {
                // A group stays as long as at least one of its members can be used.
                if (GroupOfActions.TryGetDefault(step, out var group) && AnyUsable(group, level, levelOf, upgradeOf))
                    result.Add(step);
                else
                    removed++;
                continue;
            }

            var resolved = Resolve((uint)step, level, levelOf, upgradeOf);
            if (resolved == 0)
            {
                removed++;
            }
            else
            {
                if (resolved != (uint)step)
                    replaced++;
                result.Add((int)resolved);
            }
        }

        return new AdaptedOpener(result, removed, replaced);
    }

    /// <summary>The version of an action usable at a level; 0 when there is none.</summary>
    private static uint Resolve(uint action, int level, Func<uint, int> levelOf, Func<uint, Upgrade?> upgradeOf)
    {
        var current = action;

        // Chains are short (Jolt, Jolt II, Jolt III); the bound only guards against cyclic data.
        for (var hop = 0; hop < 8; hop++)
        {
            var upgrade = upgradeOf(current);
            var learnedAt = Math.Max(levelOf(current), upgrade?.Level ?? 0);
            if (learnedAt <= level)
                return current;
            if (upgrade == null)
                return 0;

            current = upgrade.Value.Lower;
        }

        return 0;
    }

    private static bool AnyUsable(GroupOfActions group, int level, Func<uint, int> levelOf, Func<uint, Upgrade?> upgradeOf)
    {
        foreach (var member in group.Actions)
            if (Resolve(member, level, levelOf, upgradeOf) == member)
                return true;
        return false;
    }
}
