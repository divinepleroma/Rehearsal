using System;
using Rehearsal.Data;

namespace Rehearsal.Training;

/// <summary>
/// Decides whether an action the player actually used satisfies an opener step.
/// Pure logic: game lookups are injected so it can be unit tested.
/// </summary>
/// <param name="nameOf">Display name for an opener step id (action, group or special).</param>
/// <param name="adjust">
/// Resolves an action id to what the game currently turns it into (trait upgrades, combo
/// transformations). Identity when not supplied.
/// </param>
public sealed class ActionMatcher(Func<int, string> nameOf, Func<uint, uint>? adjust = null)
{
    public bool Matches(int expected, uint actual, bool actualIsItem = false)
    {
        // Items only ever satisfy a potion step, and a potion step is only satisfied by an item.
        if (expected == SpecialActions.Potion)
            return actualIsItem;
        if (actualIsItem)
            return false;

        if (expected == SpecialActions.CatchAll)
            return true;

        if (expected < 0)
            return GroupOfActions.TryGetDefault(expected, out var group) && group.IsMember(actual);

        var expectedId = (uint)expected;
        if (expectedId == actual)
            return true;

        if (adjust != null && adjust(expectedId) == actual)
            return true;

        // Some actions exist under several ids with the same name (e.g. mudras used during
        // Ten Chi Jin). Exact names only: "Fire" must never satisfy "Fire IV".
        var expectedName = nameOf(expected);
        if (string.IsNullOrEmpty(expectedName) || expectedName == SpecialActions.UnknownName)
            return false;

        return string.Equals(expectedName, nameOf((int)actual), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for steps that would match almost anything and so can't be used to detect a skip.</summary>
    public static bool IsWildcard(int expected)
    {
        return expected == SpecialActions.CatchAll;
    }
}
