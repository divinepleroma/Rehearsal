namespace Rehearsal;

/// <summary>
/// What the plugin is called and where it lives, in one place. The manifest, the README and the
/// product page repeat these; the tests check that they agree.
/// </summary>
public static class Branding
{
    public const string Name = "Rehearsal";

    /// <summary>The chat command, as it is written everywhere in the UI.</summary>
    public const string Command = "/rehearsal";

    /// <summary>A shorter way to type it, registered as well unless another plugin got there first.</summary>
    public const string ShortCommand = "/opener";

    /// <summary>What chat messages start with.</summary>
    public const string ChatTag = "[" + Name + "] ";

    public const string RepoUrl = "https://github.com/divinepleroma/Rehearsal";

    /// <summary>The product page: what it is, how to install it, the quick guide.</summary>
    public const string SiteUrl = "https://divinepleroma.github.io/Rehearsal/";

    /// <summary>The address people add to Dalamud's custom plugin repositories.</summary>
    public const string PluginRepoUrl = SiteUrl + "repo.json";
}
