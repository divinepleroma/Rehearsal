using OpenerTimeline.Data;

namespace OpenerTimeline.Tests;

public sealed class OpenerLibraryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OpenerTimelineTests", Guid.NewGuid().ToString("N"));

    public OpenerLibraryTests()
    {
        Directory.CreateDirectory(directory);
    }

    private static string BundledFile => Path.Combine(AppContext.BaseDirectory, "openers.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
            // Leftovers in the temp directory are harmless.
        }
    }

    [Fact]
    public void BundledOpeners_LoadForEveryCombatJob()
    {
        var library = new OpenerLibrary(BundledFile, null, (message, ex) => Assert.Fail($"{message}: {ex.Message}"));

        var jobs = library.All.Select(o => o.Job).Distinct().ToList();

        Assert.All(library.All, o => Assert.True(o.IsDefault));
        Assert.Contains(Jobs.BLM, jobs);
        Assert.Contains(Jobs.PLD, jobs);
        Assert.True(jobs.Count >= 20, $"only {jobs.Count} jobs have a bundled opener");
        Assert.All(library.All, o =>
        {
            Assert.True(library.TryGet(o, out var actions));
            Assert.NotEmpty(actions);
        });
    }

    [Fact]
    public void BundledOpeners_OnlyUseKnownGroups()
    {
        var library = new OpenerLibrary(BundledFile, null);

        foreach (var opener in library.All)
        {
            library.TryGet(opener, out var actions);
            foreach (var id in actions.Where(id => id < 0))
                Assert.True(GroupOfActions.TryGetDefault(id, out _), $"{opener.Job} {opener.Name} uses unknown group {id}");
        }
    }

    [Fact]
    public void TryGet_ReturnsACopy()
    {
        var library = new OpenerLibrary(BundledFile, null);
        var opener = library.ForJob(Jobs.BLM).First();

        library.TryGet(opener, out var first);
        first.Clear();
        library.TryGet(opener, out var second);

        Assert.NotEmpty(second);
    }

    [Fact]
    public void SavedOpeners_SurviveAReload()
    {
        var path = Path.Combine(directory, "openers.json");
        var library = new OpenerLibrary(null, path);

        library.Save("Mine", Jobs.SAM, [7477, 0, -100]);

        var reloaded = new OpenerLibrary(null, path);
        var opener = Assert.Single(reloaded.All);
        Assert.Equal(new OpenerRef(Jobs.SAM, "Mine", false), opener);
        Assert.True(reloaded.TryGet(opener, out var actions));
        Assert.Equal([7477, 0, -100], actions);
    }

    [Fact]
    public void Delete_RemovesTheOpenerFromDisk()
    {
        var path = Path.Combine(directory, "openers.json");
        var library = new OpenerLibrary(null, path);
        library.Save("Mine", Jobs.SAM, [1, 2]);
        library.Save("Other", Jobs.SAM, [3]);

        library.Delete("Mine", Jobs.SAM);

        var reloaded = new OpenerLibrary(null, path);
        Assert.Equal("Other", Assert.Single(reloaded.All).Name);
        Assert.False(reloaded.Exists(new OpenerRef(Jobs.SAM, "Mine", false)));
    }

    [Fact]
    public void SavedAndBundledOpeners_MayShareAName()
    {
        var library = new OpenerLibrary(BundledFile, Path.Combine(directory, "openers.json"));
        var bundled = library.ForJob(Jobs.BLM).First();

        library.Save(bundled.Name, Jobs.BLM, [1]);

        Assert.True(library.TryGet(bundled, out var original));
        Assert.True(library.TryGet(bundled with { IsDefault = false }, out var mine));
        Assert.True(original.Count > 1);
        Assert.Equal([1], mine);
    }

    [Fact]
    public void Import_AddsOpenersWithoutOverwriting()
    {
        var legacy = Path.Combine(directory, "legacy.json");
        File.WriteAllText(legacy, """{"NIN":{"Old":[1,2,3],"Kept":[9]}}""");
        var library = new OpenerLibrary(null, Path.Combine(directory, "openers.json"));
        library.Save("Kept", Jobs.NIN, [4, 5]);

        var added = library.Import(legacy);

        Assert.Equal(1, added);
        Assert.True(library.TryGet(new OpenerRef(Jobs.NIN, "Kept", false), out var kept));
        Assert.Equal([4, 5], kept);
        Assert.True(library.TryGet(new OpenerRef(Jobs.NIN, "Old", false), out var old));
        Assert.Equal([1, 2, 3], old);
    }

    [Fact]
    public void UnknownJobs_DoNotVoidTheWholeFile()
    {
        var path = Path.Combine(directory, "future.json");
        File.WriteAllText(path, """{"XYZ":{"Future job":[1]},"WAR":{"Still here":[31,37]}}""");

        var library = new OpenerLibrary(null, path);

        Assert.Equal(new OpenerRef(Jobs.WAR, "Still here", false), Assert.Single(library.All));
    }

    [Fact]
    public void NullEntries_AreDroppedInsteadOfBreakingReaders()
    {
        var path = Path.Combine(directory, "holes.json");
        File.WriteAllText(path, """{"WAR":{"Gone":null,"":[1],"Kept":[31,37]},"PLD":null,"DRK":{"Also gone":null}}""");

        var library = new OpenerLibrary(null, path, (message, ex) => Assert.Fail($"{message}: {ex.Message}"));

        var opener = Assert.Single(library.All);
        Assert.Equal(new OpenerRef(Jobs.WAR, "Kept", false), opener);
        Assert.True(library.TryGet(opener, out var actions));
        Assert.Equal([31, 37], actions);
        Assert.False(library.TryGet(new OpenerRef(Jobs.WAR, "Gone", false), out _));
    }

    [Fact]
    public void BrokenFile_IsReportedAndTreatedAsEmpty()
    {
        var path = Path.Combine(directory, "broken.json");
        File.WriteAllText(path, "{ not json");
        var errors = 0;

        var library = new OpenerLibrary(null, path, (_, _) => errors++);

        Assert.Empty(library.All);
        Assert.Equal(1, errors);
    }
}
