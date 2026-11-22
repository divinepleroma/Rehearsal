using System;

namespace Rehearsal.Data;

public enum Jobs
{
    ANY,

    // Tanks
    PLD,
    WAR,
    DRK,
    GNB,

    // Healers
    WHM,
    SCH,
    AST,
    SGE,

    // Melee
    MNK,
    DRG,
    NIN,
    SAM,
    RPR,
    VPR,

    // Physical Ranged
    BRD,
    MCH,
    DNC,

    // Magical Ranged
    BLM,
    SMN,
    RDM,
    PCT,
    BLU
}

[Flags]
public enum JobCategory
{
    None = 0,
    Tank = 1 << 0,
    Healer = 1 << 1,
    Melee = 1 << 2,
    PhysicalRanged = 1 << 3,
    MagicalRanged = 1 << 4
}

public static class JobsExtensions
{
    public static string PrettyPrint(this Jobs job)
    {
        return job switch
        {
            Jobs.ANY => "Any",
            _ => job.ToString()
        };
    }

    public static string FullName(this Jobs job)
    {
        return job switch
        {
            Jobs.PLD => "Paladin",
            Jobs.WAR => "Warrior",
            Jobs.DRK => "Dark Knight",
            Jobs.GNB => "Gunbreaker",
            Jobs.WHM => "White Mage",
            Jobs.SCH => "Scholar",
            Jobs.AST => "Astrologian",
            Jobs.SGE => "Sage",
            Jobs.MNK => "Monk",
            Jobs.DRG => "Dragoon",
            Jobs.NIN => "Ninja",
            Jobs.SAM => "Samurai",
            Jobs.RPR => "Reaper",
            Jobs.VPR => "Viper",
            Jobs.BRD => "Bard",
            Jobs.MCH => "Machinist",
            Jobs.DNC => "Dancer",
            Jobs.BLM => "Black Mage",
            Jobs.SMN => "Summoner",
            Jobs.RDM => "Red Mage",
            Jobs.PCT => "Pictomancer",
            Jobs.BLU => "Blue Mage",
            _ => "Any job"
        };
    }

    public static JobCategory GetCategory(this Jobs job)
    {
        return job switch
        {
            Jobs.PLD or Jobs.WAR or Jobs.DRK or Jobs.GNB => JobCategory.Tank,
            Jobs.WHM or Jobs.SCH or Jobs.AST or Jobs.SGE => JobCategory.Healer,
            Jobs.MNK or Jobs.DRG or Jobs.NIN or Jobs.SAM or Jobs.RPR or Jobs.VPR => JobCategory.Melee,
            Jobs.BRD or Jobs.MCH or Jobs.DNC => JobCategory.PhysicalRanged,
            Jobs.BLM or Jobs.SMN or Jobs.RDM or Jobs.PCT or Jobs.BLU => JobCategory.MagicalRanged,
            _ => JobCategory.None
        };
    }

    public static string Label(this JobCategory category)
    {
        return category switch
        {
            JobCategory.Tank => "Tanks",
            JobCategory.Healer => "Healers",
            JobCategory.Melee => "Melee",
            JobCategory.PhysicalRanged => "Ranged",
            JobCategory.MagicalRanged => "Casters",
            _ => "All"
        };
    }

    public static bool FilterBy(JobCategory filter, Jobs job)
    {
        return (filter & GetCategory(job)) != 0;
    }

    public static JobCategory Toggle(JobCategory filter, JobCategory category)
    {
        if ((filter & category) != 0)
            return filter & ~category; // remove
        return filter | category;      // add
    }

    /// <summary>Maps a ClassJob abbreviation ("BLM") to the enum. Base classes have no openers and map to ANY.</summary>
    public static Jobs FromAbbreviation(string? abbreviation)
    {
        if (string.IsNullOrEmpty(abbreviation))
            return Jobs.ANY;

        return Enum.TryParse<Jobs>(abbreviation, true, out var job) && Enum.IsDefined(job) ? job : Jobs.ANY;
    }
}
