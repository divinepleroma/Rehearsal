using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenerTimeline.Data;

/// <summary>
/// Opener steps are stored as plain ints (the upstream OpenerCreator format):
/// a positive value is an Action row id, zero and negative values are the special steps below.
/// </summary>
public static class SpecialActions
{
    /// <summary>Matches any action.</summary>
    public const int CatchAll = 0;

    /// <summary>Matches any item use (tinctures, gemdraughts...).</summary>
    public const int Potion = -100;

    public const string CatchAllName = "Any action";
    public const string PotionName = "Potion";
    public const string UnknownName = "Unknown action";

    /// <summary>Generic icons used for the special steps.</summary>
    public const uint CatchAllIcon = 405;
    public const uint UnknownIcon = 786;
}

public readonly struct GroupOfActions(int id, string name, Jobs job, uint[] actions, bool isGCD = true)
{
    public int Id => id;
    public string Name => name;
    public Jobs Job => job;
    public IReadOnlyList<uint> Actions => actions;
    public bool IsGCD => isGCD;

    public bool IsMember(uint a)
    {
        return Array.IndexOf(actions, a) >= 0;
    }

    public bool HasId(int i)
    {
        return id == i;
    }

    public static bool TryGetDefault(int id, out GroupOfActions value)
    {
        foreach (var group in DefaultGroups)
            if (group.Id == id)
            {
                value = group;
                return true;
            }

        value = default;
        return false;
    }

    public static List<int> GetFilteredGroups(string name, Jobs job, ActionTypes actionType)
    {
        return DefaultGroups
               .Where(a =>
                          (name.Length == 0 || a.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase))
                          && (job == a.Job || job == Jobs.ANY)
                          && ((actionType == ActionTypes.GCD && a.IsGCD)
                              || (actionType == ActionTypes.OGCD && !a.IsGCD)
                              || actionType == ActionTypes.ANY)
               )
               .Select(a => a.Id)
               .OrderByDescending(id => id)
               .ToList();
    }

    public static readonly GroupOfActions[] DefaultGroups =
    [
        new GroupOfActions(
            -1,
            "Dancer Step",
            Jobs.DNC,
            [
                15999, // Emboite
                16000, // Entrechat
                16001, // Jete
                16002  // Pirouette
            ]
        ),
        new GroupOfActions(
            -2,
            "Mudra",
            Jobs.NIN,
            [
                2259,  // Ten
                18805, // Ten
                2261,  // Chi
                18806, // Chi
                2263,  // Jin
                18807  // Jin
            ],
            false
        ),
        new GroupOfActions(
            -3,
            "Refulgent Arrow Proc",
            Jobs.BRD,
            [
                16495, // Burst Shot
                7409   // Refulgent Arrow
            ]
        ),
        new GroupOfActions(
            -4,
            "Verthunder/Veraero",
            Jobs.RDM,
            [
                25855, // Verthunder III
                25856  // Veraero III
            ]
        ),
        new GroupOfActions(
            -5,
            "Saber/Starfall Dance",
            Jobs.DNC,
            [
                16005, // Saber Dance
                25792  // Starfall Dance
            ]
        ),
        new GroupOfActions(
            -6,
            "Dancer Priority GCD",
            Jobs.DNC,
            [
                16005, // Saber Dance
                25792, // Starfall Dance
                36983, // Last Dance
                15992, // Fountainfall
                15991  // Reverse Cascade
            ]
        )
    ];

    public static List<string> GroupsNames => DefaultGroups.Select(g => g.Name).ToList();
}
