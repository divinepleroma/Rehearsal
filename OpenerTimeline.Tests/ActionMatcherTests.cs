using OpenerTimeline.Data;
using OpenerTimeline.Training;

namespace OpenerTimeline.Tests;

public class ActionMatcherTests
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [141] = "Fire",
        [3577] = "Fire IV",
        [2259] = "Ten",
        [18805] = "Ten",
        [7477] = "Hakaze",
        [36963] = "Gyofu"
    };

    private static string NameOf(int id)
    {
        if (id == SpecialActions.CatchAll)
            return SpecialActions.CatchAllName;
        if (id < 0)
            return GroupOfActions.TryGetDefault(id, out var group) ? group.Name : SpecialActions.UnknownName;
        return Names.GetValueOrDefault(id, SpecialActions.UnknownName);
    }

    [Fact]
    public void SameId_Matches()
    {
        Assert.True(new ActionMatcher(NameOf).Matches(3577, 3577));
    }

    [Fact]
    public void DifferentAction_DoesNotMatch()
    {
        Assert.False(new ActionMatcher(NameOf).Matches(3577, 7477));
    }

    [Fact]
    public void NameThatMerelyContainsTheOther_DoesNotMatch()
    {
        // OpenerCreator compared names with "contains", so Fire IV passed for Fire.
        var matcher = new ActionMatcher(NameOf);

        Assert.False(matcher.Matches(141, 3577));
        Assert.False(matcher.Matches(3577, 141));
    }

    [Fact]
    public void SameNameUnderAnotherId_Matches()
    {
        // The mudra used during Ten Chi Jin has its own id.
        Assert.True(new ActionMatcher(NameOf).Matches(2259, 18805));
    }

    [Fact]
    public void UnknownActions_NeverMatchEachOtherByName()
    {
        Assert.False(new ActionMatcher(NameOf).Matches(900001, 900002));
    }

    [Fact]
    public void CatchAll_MatchesEveryAction()
    {
        Assert.True(new ActionMatcher(NameOf).Matches(SpecialActions.CatchAll, 12345));
        Assert.True(ActionMatcher.IsWildcard(SpecialActions.CatchAll));
        Assert.False(ActionMatcher.IsWildcard(3577));
    }

    [Fact]
    public void Group_MatchesItsMembersOnly()
    {
        var matcher = new ActionMatcher(NameOf);

        Assert.True(matcher.Matches(-4, 25855));  // Verthunder III
        Assert.True(matcher.Matches(-4, 25856));  // Veraero III
        Assert.False(matcher.Matches(-4, 3577));
        Assert.False(matcher.Matches(-9999, 3577)); // no such group
    }

    [Fact]
    public void Potion_MatchesItemsAndNothingElse()
    {
        var matcher = new ActionMatcher(NameOf);

        Assert.True(matcher.Matches(SpecialActions.Potion, 44162, true));
        Assert.False(matcher.Matches(SpecialActions.Potion, 44162));
        Assert.False(matcher.Matches(3577, 3577, true));
        Assert.False(matcher.Matches(SpecialActions.CatchAll, 44162, true));
    }

    [Fact]
    public void AdjustedId_Matches()
    {
        // The opener lists the base action; a trait currently upgrades it.
        var matcher = new ActionMatcher(NameOf, id => id == 7477 ? 36963u : id);

        Assert.True(matcher.Matches(7477, 36963));
        Assert.True(matcher.Matches(7477, 7477));
        Assert.False(matcher.Matches(36963, 7477));
    }
}
