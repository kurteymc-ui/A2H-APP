namespace AionDPS;

/// <summary>
/// Atreia App's identity: the name, the site it talks to, the folder it keeps its files in and where its updates come from.
/// The parts that come from other projects are listed in THIRD-PARTY-NOTICES.md.
/// </summary>
public static class Brand
{
    public const string AppName = "Atreia App";

    /// <summary>The website: links the meter opens.</summary>
    public const string Site = "https://atreia.app";

    /// <summary>The website's address until 5 November 2026: links saved before (a fight's page in History) still open; it forwards to <see cref="Site"/>.</summary>
    public const string OldSite = "https://aion2helper.com";

    /// <summary>A page of the website, at its address or the old one.</summary>
    public static bool IsSiteUrl(string? url) => url is not null && (url.StartsWith(Site + "/", StringComparison.Ordinal) || url.StartsWith(OldSite + "/", StringComparison.Ordinal));

    /// <summary>The meter's API on the site. The client calls the same paths as the upstream client did on its own site
    /// (/api/uploads, /api/players/…, /api/schedule …), here under /meter.</summary>
    public const string Api = Site + "/meter";

    /// <summary>The folder under %APPDATA% for settings, sessions and logs (its own, so it can sit next to the upstream meter).</summary>
    public const string DataFolder = "A2HMeter";

    /// <summary>Where updates come from: full releases of this repository only (never test versions).</summary>
    public const string RepositoryUrl = "https://github.com/kurteymc-ui/Atreia-App";
}
