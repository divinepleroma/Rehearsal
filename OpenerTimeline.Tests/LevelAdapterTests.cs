using OpenerTimeline.Data;

namespace OpenerTimeline.Tests;

public class LevelAdapterTests
{
    // A miniature job: Jolt (2) -> Jolt II (62) -> Jolt III (84), plus a few stand-alone actions.
    private const uint Jolt = 1, JoltII = 2, JoltIII = 3, Embolden = 10, Prefulgence = 11, Riposte = 12, Enchanted = 13;

    private static readonly Dictionary<uint, int> Levels = new()
    {
        [Jolt] = 2, [JoltII] = 62, [JoltIII] = 84, [Embolden] = 58, [Prefulgence] = 100, [Riposte] = 1, [Enchanted] = 1
    };

    private static readonly Dictionary<uint, Upgrade> Upgrades = new()
    {
        [JoltII] = new Upgrade(Jolt, 62),
        [JoltIII] = new Upgrade(JoltII, 84),
        // An action whose own row says level 1 but which only exists once a trait is learned.
        [Enchanted] = new Upgrade(Riposte, 50)
    };

    private static int LevelOf(uint id) => Levels.GetValueOrDefault(id);
    private static Upgrade? UpgradeOf(uint id) => Upgrades.TryGetValue(id, out var upgrade) ? upgrade : null;

    private static AdaptedOpener Adapt(int level, params int[] opener) => LevelAdapter.Adapt(opener, level, LevelOf, UpgradeOf);

    [Fact]
    public void AtTheCap_NothingChanges()
    {
        var adapted = Adapt(100, (int)JoltIII, (int)Embolden, (int)Prefulgence);

        Assert.Equal([(int)JoltIII, (int)Embolden, (int)Prefulgence], adapted.Actions);
        Assert.False(adapted.Changed);
    }

    [Fact]
    public void UnlearnedActions_AreLeftOut()
    {
        var adapted = Adapt(90, (int)JoltIII, (int)Prefulgence, (int)Embolden);

        Assert.Equal([(int)JoltIII, (int)Embolden], adapted.Actions);
        Assert.Equal(1, adapted.Removed);
        Assert.Equal(0, adapted.Replaced);
    }

    [Fact]
    public void UpgradedActions_FallBackToTheVersionYouHave()
    {
        Assert.Equal([(int)JoltII], Adapt(80, (int)JoltIII).Actions);
        Assert.Equal([(int)Jolt], Adapt(60, (int)JoltIII).Actions);
        Assert.Equal(1, Adapt(60, (int)JoltIII).Replaced);
    }

    [Fact]
    public void NothingToFallBackTo_RemovesTheStep()
    {
        var adapted = Adapt(1, (int)JoltIII);

        Assert.Empty(adapted.Actions);
        Assert.Equal(1, adapted.Removed);
    }

    [Fact]
    public void TraitLevel_CountsEvenWhenTheActionRowSaysLevelOne()
    {
        Assert.Equal([(int)Riposte], Adapt(40, (int)Enchanted).Actions);
        Assert.Equal([(int)Enchanted], Adapt(50, (int)Enchanted).Actions);
    }

    [Fact]
    public void SpecialSteps_AreKept()
    {
        var adapted = Adapt(1, SpecialActions.CatchAll, SpecialActions.Potion);

        Assert.Equal([SpecialActions.CatchAll, SpecialActions.Potion], adapted.Actions);
    }

    [Fact]
    public void Groups_StayWhileAnyMemberIsUsable()
    {
        // -4 is Verthunder III / Veraero III, both learned at 82.
        int VerLevel(uint id) => id is 25855 or 25856 ? 82 : 0;
        Assert.Single(LevelAdapter.Adapt([-4], 90, VerLevel, _ => null).Actions);
        Assert.Empty(LevelAdapter.Adapt([-4], 80, VerLevel, _ => null).Actions);
    }

    [Fact]
    public void RequiredLevel_IsTheHighestStep()
    {
        Assert.Equal(100, LevelAdapter.RequiredLevel([(int)JoltIII, (int)Prefulgence], LevelOf, UpgradeOf));
        Assert.Equal(84, LevelAdapter.RequiredLevel([(int)JoltIII, (int)Embolden, 0, -100], LevelOf, UpgradeOf));
        Assert.Equal(50, LevelAdapter.RequiredLevel([(int)Enchanted], LevelOf, UpgradeOf));
        Assert.Equal(1, LevelAdapter.RequiredLevel([], LevelOf, UpgradeOf));
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(50, 50)]
    [InlineData(51, 60)]
    [InlineData(70, 70)]
    [InlineData(89, 90)]
    [InlineData(90, 90)]
    [InlineData(100, 100)]
    [InlineData(120, 100)]
    public void Bracket_IsTheExpansionCap(int level, int expected)
    {
        Assert.Equal(expected, LevelAdapter.Bracket(level));
    }
}

public class UpgradeParserTests
{
    private static readonly HashSet<string> Names =
    [
        "Jolt", "Jolt II", "Jolt III", "Verthunder", "Verthunder III", "Veraero", "Veraero III", "Verflare", "Verholy",
        "Venomous Bite", "Windbite", "Caustic Bite", "Stormbite", "Fire II", "Blizzard II", "High Fire II", "High Blizzard II",
        "Summon Ifrit", "Summon Ifrit II", "Summon Titan", "Summon Titan II", "Summon Garuda", "Summon Garuda II", "Katon", "Hyoton",
        "Goka Mekkyaku", "Hyosho Ranryu", "Kassatsu", "Fang and Claw", "Wheeling Thrust", "Full Thrust", "Heavens' Thrust",
        "Chaos Thrust", "Chaotic Spring", "Drakesbane", "Thunder", "Thunder III", "Ruin II", "Broil", "Broil II"
    ];

    private static List<(string From, string To)> Parse(string text) => UpgradeParser.Parse(text, Names.Contains);

    [Fact]
    public void SingleUpgrade()
    {
        Assert.Equal([("Thunder", "Thunder III")], Parse("Upgrades Thunder to Thunder III."));
    }

    [Fact]
    public void LongestNameWins()
    {
        // "Jolt II" must not be read as "Jolt".
        Assert.Equal([("Jolt II", "Jolt III")], Parse("Upgrades Jolt II to Jolt III. Also increases the potency of Verthunder to 360."));
    }

    [Fact]
    public void SeveralUpgradesJoinedByAnd()
    {
        Assert.Equal([("Verthunder", "Verthunder III"), ("Veraero", "Veraero III")],
                     Parse("Upgrades Verthunder to Verthunder III and Veraero to Veraero III.  Upon gaining 3 Mana Stacks, Verthunder III is upgraded to Verflare."));
    }

    [Fact]
    public void ListOfThree()
    {
        Assert.Equal([("Summon Ifrit", "Summon Ifrit II"), ("Summon Titan", "Summon Titan II"), ("Summon Garuda", "Summon Garuda II")],
                     Parse("Upgrades Summon Ifrit to Summon Ifrit II, Summon Titan to Summon Titan II, and Summon Garuda to Summon Garuda II."));
    }

    [Fact]
    public void PairedLists()
    {
        Assert.Equal([("Venomous Bite", "Caustic Bite"), ("Windbite", "Stormbite")],
                     Parse("Upgrades Venomous Bite and Windbite to Caustic Bite and Stormbite respectively."));
        Assert.Equal([("Fire II", "High Fire II"), ("Blizzard II", "High Blizzard II")],
                     Parse("Upgrades Fire II and Blizzard II to High Fire II and High Blizzard II."));
    }

    [Fact]
    public void ConditionalUpgrade_IgnoresTheCondition()
    {
        Assert.Equal([("Katon", "Goka Mekkyaku"), ("Hyoton", "Hyosho Ranryu")],
                     Parse("Upgrades Katon and Hyoton to Goka Mekkyaku and Hyosho Ranryu while under the effect of Kassatsu."));
    }

    [Fact]
    public void UpgradeAfterOtherSentences()
    {
        Assert.Equal([("Full Thrust", "Heavens' Thrust"), ("Chaos Thrust", "Chaotic Spring")],
                     Parse("Upgrades Full Thrust to Heavens' Thrust and Chaos Thrust to Chaotic Spring. Also increases the potency of Drakesbane to 400."));
    }

    [Fact]
    public void OnlyTheUpgradeClauseCounts()
    {
        // "increases the potency of Ruin II to 180" is not an upgrade of Ruin II.
        Assert.Equal([("Broil", "Broil II")], Parse("Upgrades Broil to Broil II and increases the potency of Ruin II to 180."));
    }

    [Fact]
    public void TextWithoutUpgrades_GivesNothing()
    {
        Assert.Empty(Parse("Increases the potency of Fang and Claw to 300."));
        Assert.Empty(Parse(""));
    }
}
