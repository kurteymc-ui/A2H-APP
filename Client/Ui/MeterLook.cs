using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>One of the meter's optional columns: its id (kept in the settings), the title over it, how wide it is at the normal text size,
/// the row property it shows and what it means.</summary>
public sealed record MeterColumn(string Id, string TitleKey, double Width, string Path, string TipKey, bool Number = true);

/// <summary>
/// How the meter over the game looks (Overlays → Meter look): the bars, the height of a line, the font and its size, the columns, the
/// parts around the list and which way it grows. Read from the settings; <see cref="MeterRowView"/> builds the lines from it.
/// </summary>
public sealed class MeterLook
{
    /// <summary>The bar styles, in the order the page offers them. "solid" is the strong class colour of a raid log's table; "table" the muted class colour of a
    /// dense old-school damage table (the owner's picture, 4 November 2026), its figures with lowercase units ("1.29b", "471.2m") and its columns in a table's
    /// order (<see cref="ColumnsOf"/>).</summary>
    public static readonly string[] Bars = { "glow", "flat", "solid", "thread", "capsule", "frame", "table" };

    public static readonly string[] Sizes = { "compact", "normal", "roomy" };

    /// <summary>The fonts, all of them part of Windows (the next one in the list stands in where one is missing).</summary>
    public static readonly (string Id, string Name, string Families)[] Fonts =
    {
        ("segoe", "Segoe UI", "Segoe UI Variable Text, Segoe UI"),
        ("bahnschrift", "Bahnschrift", "Bahnschrift, Segoe UI"),
        ("condensed", "Bahnschrift Condensed", "Bahnschrift SemiCondensed, Bahnschrift, Segoe UI"),
        ("mono", "Cascadia Mono", "Cascadia Mono, Consolas, Segoe UI"),
        ("trebuchet", "Trebuchet MS", "Trebuchet MS, Segoe UI"),
    };

    /// <summary>
    /// The columns a line can have, in the order they stand (left to right after the name). DPS, share and total are the meter's own three
    /// (their titles follow the mode: Heal, Taken … in All in One); the others are worked out from the hits of the fight shown. The widths are
    /// the narrowest that still hold their figures ("100%", "12.4K"): 1 November 2026 a few pixels came off each, and the Buffs column (a
    /// strip of party-buff badges four badges wide, empty for most players) went, so a meter with every hit kind is no longer far too wide.
    /// </summary>
    public static readonly MeterColumn[] Columns =
    {
        new("crit", "Look.ColCrit", 36, nameof(PlayerRow.CritText), "Look.ColCritTip"),
        new("back", "Look.ColBack", 36, nameof(PlayerRow.BackText), "Look.ColBackTip"),
        new("front", "Look.ColFront", 38, nameof(PlayerRow.FrontText), "Look.ColFrontTip"),
        new("perfect", "Look.ColPerfect", 40, nameof(PlayerRow.PerfectText), "Look.ColPerfectTip"),
        new("double", "Look.ColDouble", 40, nameof(PlayerRow.DoubleText), "Look.ColDoubleTip"),
        new("parry", "Look.ColParry", 40, nameof(PlayerRow.ParryText), "Look.ColParryTip"),
        new("hits", "Look.ColHits", 40, nameof(PlayerRow.HitsText), "Look.ColHitsTip"),
        new("ndps", "Look.ColNdps", 46, nameof(PlayerRow.NdpsText), "Look.ColNdpsTip"),
        new("rdps", "Look.ColRdps", 46, nameof(PlayerRow.RdpsText), "Look.ColRdpsTip"),
        new("given", "Look.ColGiven", 46, nameof(PlayerRow.GivenText), "Look.ColGivenTip"),
        new("dps", "", 0, nameof(PlayerRow.RateOrDamageCompact), "Look.ColDpsTip"),
        new("share", "", 42, nameof(PlayerRow.ShareOrHealCompact), "Look.ColShareTip"),
        new("total", "", 48, nameof(PlayerRow.TotalOrTakenCompact), "Look.ColTotalTip"),
    };

    /// <summary>The parts of the name side that can be switched off.</summary>
    public static readonly string[] NameParts = { "rank", "icon", "cp", "classcolor" };

    /// <summary>What a meter shows that was never set up.</summary>
    public static readonly string[] DefaultColumns = { "icon", "cp", "dps", "share", "total" };

    /// <summary>The panel behind the lines (<see cref="MeterSettings.OverlayLook"/>): the theme's dark colour with the gold edge, see-through glass, a plain
    /// graphite grey with a grey edge, or near black (the old-school table's).</summary>
    public static readonly string[] Panels = { "Themed", "Clear", "Graphite", "Black" };

    /// <summary>The order of the columns in the table style: the damage, the rates, the share, then the hit kinds, as an old-school damage table has them
    /// (DMG, DPS, nDPS, rDPS, D%, CRIT, F.A, B.A).</summary>
    private static readonly string[] TableOrder = { "total", "dps", "ndps", "rdps", "given", "share", "crit", "front", "back", "perfect", "double", "parry", "hits" };

    /// <summary>The columns a look shows, in the order it shows them (left to right after the name).</summary>
    public static IEnumerable<MeterColumn> ColumnsOf(MeterLook look) => look.Bar == "table"
        ? TableOrder.Select(id => Columns.First(c => c.Id == id)).Where(c => look.Has(c.Id))
        : Columns.Where(c => look.Has(c.Id));

    /// <summary>The panel a setting names, in any case; anything else is the themed one (as it always was).</summary>
    public static string PanelOf(string? overlayLook) => Panels.FirstOrDefault(p => string.Equals(p, overlayLook, StringComparison.OrdinalIgnoreCase)) ?? "Themed";

    /// <summary>
    /// The premade designs on the look page, in the order it shows them; the first is the usual look (the reset goes back to it). Each one sets the
    /// whole look at once, and every switch on the page can still change it after.
    /// </summary>
    public static readonly MeterLookPreset[] Presets =
    {
        // the usual look: the class colour as a soft glow, the group's total in big, on see-through glass (5 November 2026, the owner; the buttons of every
        // design are always there since, so the pill and the reset are never hidden)
        new("standard", Bar: "glow", Size: "normal", Font: "segoe", TextScale: 1.0, Columns: null, Titles: false, SmallUnits: false,
            Panel: "Clear", HeaderButtons: "always", ShowTotal: true, ShowStatus: true),
        // a raid log's damage table: strong class colours filling the lines, a row of column titles, the units small after the digits, a plain grey panel
        // and the hit kinds beside the meter's own numbers; no big total (the table carries the numbers)
        new("raidlog", Bar: "solid", Size: "normal", Font: "segoe", TextScale: 0.95, Columns: new[] { "icon", "cp", "crit", "back", "front", "dps", "share", "total" },
            Titles: true, SmallUnits: true, Panel: "Graphite", HeaderButtons: "always", ShowTotal: false, ShowStatus: true),
        // as little as can be: thin lines under the names, the names in their class colour (no icons), the rate and the share, on see-through glass
        new("minimal", Bar: "thread", Size: "compact", Font: "condensed", TextScale: 1.0, Columns: new[] { "classcolor", "dps", "share" }, Titles: false,
            SmallUnits: false, Panel: "Clear", HeaderButtons: "always", ShowTotal: false, ShowStatus: false),
        // easy to read at a glance: big text on roomy round bars, the ranks, the names in their class colour
        new("bold", Bar: "capsule", Size: "roomy", Font: "bahnschrift", TextScale: 1.15, Columns: new[] { "rank", "icon", "classcolor", "dps", "share" },
            Titles: false, SmallUnits: false, Panel: "Themed", HeaderButtons: "always", ShowTotal: true, ShowStatus: true),
        // for working a fight out: framed bars, dense lines in a fixed-width font, the hit kinds and rDPS with their titles
        new("analyst", Bar: "frame", Size: "compact", Font: "mono", TextScale: 0.95, Columns: new[] { "icon", "cp", "crit", "back", "perfect", "rdps", "dps", "share", "total" },
            Titles: true, SmallUnits: false, Panel: "Themed", HeaderButtons: "always", ShowTotal: true, ShowStatus: true),
        // old school (the owner's picture of a dense damage table, 4 November 2026): tight lines on near black, muted class colours across the line, the
        // combat power before the name, every figure in a column with its title - damage, DPS, nDPS, rDPS, share, crit, front, back - units in lowercase
        new("oldschool", Bar: "table", Size: "compact", Font: "segoe", TextScale: 0.95, Columns: new[] { "icon", "cp", "total", "dps", "ndps", "rdps", "share", "crit", "front", "back" },
            Titles: true, SmallUnits: false, Panel: "Black", HeaderButtons: "always", ShowTotal: false, ShowStatus: true),
    };

    /// <summary>The usual look (the page's reset).</summary>
    public static MeterLookPreset Standard => Presets[0];

    public string Bar { get; init; } = "glow";
    public string Size { get; init; } = "normal";
    public string Font { get; init; } = "segoe";

    /// <summary>The text size, 0.85 to 1.3 of the normal one.</summary>
    public double TextScale { get; init; } = 1.0;

    public IReadOnlySet<string> Shown { get; init; } = new HashSet<string>(DefaultColumns);

    /// <summary>A line of titles over the columns.</summary>
    public bool Titles { get; init; }

    /// <summary>The letter or % sign after a figure (12.4K, 38%) is printed smaller and dimmer, so the digits stand out.</summary>
    public bool SmallUnits { get; init; }

    public static MeterLook From(MeterSettings s) => new()
    {
        Bar = Bars.Contains(s.MeterBarStyle) ? s.MeterBarStyle : "glow",
        Size = Sizes.Contains(s.MeterRowSize) ? s.MeterRowSize : "normal",
        Font = Fonts.Any(f => f.Id == s.MeterFont) ? s.MeterFont : "segoe",
        TextScale = Math.Clamp(s.MeterTextScale, 0.85, 1.3),
        // only what the meter still has: a column saved before it went (the Buffs column, "support") is left out
        Shown = (s.MeterColumns ?? DefaultColumns.ToList()).Where(id => NameParts.Contains(id) || Columns.Any(c => c.Id == id)).ToHashSet(),
        Titles = s.MeterColumnTitles,
        SmallUnits = s.MeterSmallUnits,
    };

    public bool Has(string id) => Shown.Contains(id);

    /// <summary>The height of a line.</summary>
    public double RowHeight => Math.Round((Size switch { "compact" => 22, "roomy" => 33, _ => 27 }) * Math.Max(1.0, 0.55 + 0.45 * TextScale));

    /// <summary>The space between two lines.</summary>
    public double Gap => Bar is "capsule" or "frame" ? 2 : 1;

    public FontFamily FontFamily => new(Fonts.FirstOrDefault(f => f.Id == Font).Families ?? "Segoe UI");

    private double SizeFactor => (Size == "compact" ? 0.93 : Size == "roomy" ? 1.05 : 1.0) * TextScale;

    public double NameSize => Math.Round(13 * SizeFactor, 1);

    public double RateSize => Math.Round(13.5 * SizeFactor, 1);

    public double SmallSize => Math.Round(11.5 * SizeFactor, 1);

    public double TinySize => Math.Round(10.5 * SizeFactor, 1);

    public double IconSize => Size == "compact" ? 15 : Size == "roomy" ? 21 : 18;

    /// <summary>The width a column takes at this text size.</summary>
    public double WidthOf(MeterColumn column) => Math.Round(column.Width * Math.Max(1.0, TextScale) * (Font == "mono" ? 1.12 : 1.0));

    /// <summary>
    /// How wide the meter is: the 364 of the usual three columns (the names keep the room they had before the columns got narrower), wider for
    /// every column added, narrower for one taken away; with the text size.
    /// </summary>
    public double PanelWidth
    {
        get
        {
            double width = 364;
            foreach (MeterColumn column in Columns)
            {
                bool own = column.Id is "dps" or "share" or "total";
                double room = column.Id == "dps" ? 58 : WidthOf(column);
                if (own && !Has(column.Id))
                {
                    width -= room;
                }
                else if (!own && Has(column.Id))
                {
                    width += room;
                }
            }

            width += Has("rank") ? 22 : 0;
            width -= Has("icon") ? 0 : 25;
            width *= 0.6 + 0.4 * TextScale;
            return Math.Round(Math.Clamp(width, 240, 1100));
        }
    }

    /// <summary>Everything that changes the lines, as one text: the lines are built again only when it changes.</summary>
    public string Key => string.Join("|", Bar, Size, Font, TextScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Titles, SmallUnits,
        string.Join(",", Shown.OrderBy(c => c, StringComparer.Ordinal)));
}

/// <summary>
/// One premade design of the look page (<see cref="MeterLook.Presets"/>): a whole set of the look's settings, put on the meter with one click. The meter's
/// size, opacity and place, which way it grows and where the best player stands are the player's own and stay as they are.
/// </summary>
public sealed record MeterLookPreset(string Id, string Bar, string Size, string Font, double TextScale, string[]? Columns, bool Titles, bool SmallUnits,
    string Panel, string HeaderButtons, bool ShowTotal, bool ShowStatus)
{
    /// <summary>The design's name and the short line under it on the page.</summary>
    public string NameKey => "Look.Preset." + Id;

    public string NoteKey => "Look.Preset." + Id + ".Note";

    /// <summary>Puts the design into the settings (null columns: the usual ones, so the standard design follows them if they ever change).</summary>
    public void ApplyTo(MeterSettings s)
    {
        s.MeterBarStyle = Bar;
        s.MeterRowSize = Size;
        s.MeterFont = Font;
        s.MeterTextScale = TextScale;
        s.MeterColumns = Columns?.ToList();
        s.MeterColumnTitles = Titles;
        s.MeterSmallUnits = SmallUnits;
        s.OverlayLook = Panel;
        s.MeterHeaderButtons = HeaderButtons;
        s.MeterShowTotal = ShowTotal;
        s.MeterShowStatus = ShowStatus;
    }

    /// <summary>Fresh settings with only this design in them (the pictures of the build).</summary>
    public MeterSettings Settings()
    {
        var s = new MeterSettings();
        ApplyTo(s);
        return s;
    }

    /// <summary>Whether the settings are this design as it came: the page marks it; any switch the player changes after leaves it unmarked.</summary>
    public bool Matches(MeterSettings s)
    {
        MeterLook look = MeterLook.From(s);
        return look.Bar == Bar && look.Size == Size && look.Font == Font && Math.Abs(look.TextScale - TextScale) < 0.005 && look.Titles == Titles
            && look.SmallUnits == SmallUnits && MeterLook.PanelOf(s.OverlayLook) == Panel && (s.MeterHeaderButtons == "hover" ? "hover" : "always") == HeaderButtons
            && s.MeterShowTotal == ShowTotal && s.MeterShowStatus == ShowStatus && look.Shown.SetEquals(Columns ?? MeterLook.DefaultColumns);
    }

    /// <summary>The look of the design's small picture on the page: its lines with only the parts a narrow card has room for (the name side, the rate, the share).</summary>
    public MeterLook Thumbnail() => new()
    {
        Bar = Bar, Size = Size, Font = Font, TextScale = TextScale, Titles = Titles, SmallUnits = SmallUnits,
        Shown = (Columns ?? MeterLook.DefaultColumns).Where(c => c is "rank" or "icon" or "classcolor" or "dps" or "share").ToHashSet(),
    };
}
