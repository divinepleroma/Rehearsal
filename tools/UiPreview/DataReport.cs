using System;
using System.Collections.Generic;
using System.Linq;
using Rehearsal.Data;
using Rehearsal.Training;

namespace Rehearsal.Preview;

/// <summary>
/// Prints what the data layer derives from the game files, so it can be checked by eye against
/// what the jobs actually do: upgrade chains, level-adjusted openers and estimated timings.
/// </summary>
internal static class DataReport
{
    public static void Print(ActionCatalog catalog, OpenerLibrary library)
    {
        Console.WriteLine();
        Console.WriteLine($"level cap: {catalog.MaxLevel}");

        Console.WriteLine();
        Console.WriteLine("== upgrades used by the bundled openers ==");
        var used = library.All.SelectMany(o => library.TryGet(o, out var a) ? a : []).Where(id => id > 0).Select(id => (uint)id).Distinct();
        foreach (var id in used.OrderBy(id => id))
        {
            var chain = new List<string>();
            var current = id;
            for (var hop = 0; hop < 8 && catalog.UpgradeOf(current) is { } upgrade; hop++)
            {
                chain.Add($"{catalog.GetName((int)upgrade.Lower)} (until lv{upgrade.Level})");
                current = upgrade.Lower;
            }

            if (chain.Count > 0)
                Console.WriteLine($"  {catalog.GetName((int)id),-24} lv{catalog.LevelOf(id),3} <- {string.Join(" <- ", chain)}");
        }

        Console.WriteLine();
        Console.WriteLine("== every bundled opener fitted to older level caps (kept/removed/replaced) ==");
        foreach (var opener in library.All)
        {
            library.TryGet(opener, out var actions);
            var parts = new List<string> { $"needs lv{catalog.RequiredLevel(actions)}" };
            foreach (var level in new[] { 90, 80, 70 })
            {
                var adapted = catalog.Adapt(actions, level);
                parts.Add($"lv{level}: {adapted.Actions.Count}/-{adapted.Removed}/~{adapted.Replaced}");
            }

            Console.WriteLine($"  {opener.Job} {opener.Name,-42} {actions.Count,2} steps  {string.Join("  ", parts)}");
        }

        foreach (var (job, name, level) in new[] { (Jobs.RDM, "Standard (The Balance)", 90), (Jobs.BLM, "Standard (The Balance)", 80), (Jobs.DRG, "Standard (The Balance)", 70) })
        {
            if (!library.TryGet(new OpenerRef(job, name, true), out var actions))
                continue;

            Console.WriteLine();
            Console.WriteLine($"== {job} {name} at level {level} ==");
            var adapted = catalog.Adapt(actions, level);
            Console.WriteLine("  full   : " + string.Join(", ", actions.Select(catalog.GetName)));
            Console.WriteLine("  fitted : " + string.Join(", ", adapted.Actions.Select(catalog.GetName)));

            var specs = actions.Select(id => new StepSpec(catalog.IsGcd(id), catalog.GetGcdSeconds(id), catalog.GetCastSeconds(id))).ToList();
            var timing = OpenerTiming.Estimate(specs);
            Console.WriteLine("  timing : " + string.Join("  ", actions.Select((id, i) =>
                $"{(catalog.IsGcd(id) ? "#" + timing[i].GcdNumber : "+" + timing[i].GcdNumber)} {catalog.GetName(id)} @{timing[i].Time:0.0}")));
        }
    }
}
