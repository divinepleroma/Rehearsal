using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rehearsal.Tests;

/// <summary>
/// The name, the version and the addresses are repeated in files no compiler looks at: the
/// manifest, the list Dalamud installs from, the product page and the README. These make sure
/// they still say the same thing, so a release cannot go out pointing at the wrong file.
/// </summary>
public class ReleaseFilesTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Rehearsal.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    private static string Read(params string[] parts)
    {
        return File.ReadAllText(Path.Combine([Root, ..parts]));
    }

    private static XDocument Project()
    {
        return XDocument.Parse(Read("Rehearsal", "Rehearsal.csproj"));
    }

    private static Version ProjectVersion()
    {
        return Version.Parse(Project().Descendants("Version").Single().Value.Trim());
    }

    private static JsonElement Manifest()
    {
        using var document = JsonDocument.Parse(Read("Rehearsal", "Rehearsal.json"));
        return document.RootElement.Clone();
    }

    /// <summary>The one entry of the list Dalamud reads.</summary>
    private static JsonElement RepoEntry()
    {
        using var document = JsonDocument.Parse(Read("docs", "repo.json"));
        return document.RootElement.EnumerateArray().Single().Clone();
    }

    [Fact]
    public void TheCommand_IsTheNameInLowerCase()
    {
        Assert.Equal(Branding.Command, "/" + Branding.Name.ToLowerInvariant());
        Assert.NotEqual(Branding.Command, Branding.ShortCommand);
        Assert.Equal($"[{Branding.Name}] ", Branding.ChatTag);
        Assert.Equal(Branding.SiteUrl + "repo.json", Branding.PluginRepoUrl);
    }

    [Fact]
    public void TheManifest_CarriesTheSameNameAndAddresses()
    {
        var manifest = Manifest();

        Assert.Equal(Branding.Name, manifest.GetProperty("Name").GetString());
        Assert.Equal(Branding.Name, manifest.GetProperty("InternalName").GetString());
        Assert.Equal(Branding.RepoUrl, manifest.GetProperty("RepoUrl").GetString());
        Assert.StartsWith(Branding.SiteUrl, manifest.GetProperty("IconUrl").GetString());
    }

    [Fact]
    public void TheRepoList_OffersExactlyTheVersionTheProjectBuilds()
    {
        var entry = RepoEntry();
        var version = ProjectVersion();

        Assert.Equal(version.ToString(4), entry.GetProperty("AssemblyVersion").GetString());

        // The release workflow attaches latest.zip to the release tagged v<major.minor.patch>.
        var download = $"{Branding.RepoUrl}/releases/download/v{version.ToString(3)}/latest.zip";
        Assert.Equal(download, entry.GetProperty("DownloadLinkInstall").GetString());
        Assert.Equal(download, entry.GetProperty("DownloadLinkUpdate").GetString());
        Assert.Equal(download, entry.GetProperty("DownloadLinkTesting").GetString());
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("InternalName")]
    [InlineData("Author")]
    [InlineData("Punchline")]
    [InlineData("Description")]
    [InlineData("RepoUrl")]
    [InlineData("IconUrl")]
    [InlineData("ApplicableVersion")]
    public void TheRepoList_RepeatsWhatTheManifestSays(string property)
    {
        Assert.Equal(Manifest().GetProperty(property).GetString(), RepoEntry().GetProperty(property).GetString());
    }

    [Fact]
    public void TheRepoList_HasTheManifestsTags()
    {
        static string[] Strings(JsonElement array)
        {
            return array.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        }

        Assert.Equal(Strings(Manifest().GetProperty("Tags")), Strings(RepoEntry().GetProperty("Tags")));
        Assert.Equal(Strings(Manifest().GetProperty("CategoryTags")), Strings(RepoEntry().GetProperty("CategoryTags")));
    }

    [Fact]
    public void TheRepoList_NamesTheDalamudTheProjectIsBuiltAgainst()
    {
        // Dalamud.NET.Sdk/15.0.0 builds against API level 15, and Dalamud refuses a plugin that claims another.
        var sdk = Project().Root!.Attribute("Sdk")!.Value;
        var level = int.Parse(sdk[(sdk.IndexOf('/') + 1)..].Split('.')[0]);

        Assert.Equal(level, RepoEntry().GetProperty("DalamudApiLevel").GetInt32());
    }

    [Fact]
    public void ThePage_GivesTheAddressToInstallFromAndTheCommand()
    {
        var page = Read("docs", "index.html");

        Assert.Contains($"value=\"{Branding.PluginRepoUrl}\"", page);
        Assert.Contains(Branding.Command, page);
        Assert.Contains(Branding.RepoUrl, page);
        Assert.Contains($"content=\"{Branding.SiteUrl}\"", page);
    }

    [Fact]
    public void TheReadme_GivesTheSameAddresses()
    {
        var readme = Read("README.md");

        Assert.Contains(Branding.PluginRepoUrl, readme);
        Assert.Contains(Branding.SiteUrl, readme);
        Assert.Contains($"`{Branding.Command}`", readme);
        Assert.StartsWith("# " + Branding.Name, readme);
    }

    [Fact]
    public void EveryPictureThePageShows_IsInTheRepository()
    {
        var sources = Read("docs", "index.html") + Read("docs", "site.css") + Read("README.md");
        var pictures = Regex.Matches(sources, @"(?:docs/)?img/[A-Za-z0-9-]+\.(?:png|jpg)")
                            .Select(match => match.Value.Replace("docs/", string.Empty))
                            .Distinct()
                            .ToArray();

        Assert.NotEmpty(pictures);
        foreach (var picture in pictures)
            Assert.True(File.Exists(Path.Combine(Root, "docs", picture)), $"{picture} is referenced but missing from docs/");
    }

    [Fact]
    public void NoPictureIsLeftBehindUnused()
    {
        // The pictures are rendered by the preview tool; one nothing shows any more is just weight.
        var sources = Read("docs", "index.html") + Read("docs", "site.css") + Read("README.md") + Read("docs", "repo.json");

        foreach (var file in Directory.GetFiles(Path.Combine(Root, "docs", "img")))
            Assert.True(sources.Contains("img/" + Path.GetFileName(file)), $"docs/img/{Path.GetFileName(file)} is not shown anywhere");
    }

    [Fact]
    public void ThePage_LoadsNothingFromElsewhere()
    {
        var page = Read("docs", "index.html");

        // Links may go anywhere; what the page itself loads must come from its own folder.
        foreach (Match match in Regex.Matches(page, @"<(?:script|img|link)\b[^>]*\b(?:src|href)=""([^""]+)"""))
            Assert.DoesNotContain("//", match.Groups[1].Value);
        Assert.Contains("default-src 'none'", page);
    }
}
