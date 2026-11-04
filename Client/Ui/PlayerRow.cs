using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AionDPS.Ui;

/// <summary>
/// One row in the main window's player list. Deliberately a plain mutable view model (not a
/// record) so the DataGrid can update fields in place as new DamageEvents arrive, without
/// rebuilding the whole row -- matches how a live meter actually behaves (numbers tick up),
/// not how a one-shot report would.
/// </summary>
public sealed class PlayerRow : INotifyPropertyChanged
{
    private long _damage;
    private double? _dps;
    private long? _relicAp;
    private int _rank;
    private double _sharePercent;
    private long _damageTaken;
    private bool _showShareBar = true;
    private bool _showDamageTaken = true;
    private bool _showRelicAp;
    private string _defenseDisplay = "";
    private string _pvpDisplay = "";

    private string _name = "?";
    private string _className = "?";
    private int _level;
    private string _faction = "";
    private bool _isEnemy;

    public int ObjectId { get; }

    public string Name
    {
        get => _name;
        set { if (_name == value) { return; } _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(NameDisplay)); }
    }

    private int _deaths;

    /// <summary>Deaths in the fight shown, in the damage-taken mode (0 elsewhere).</summary>
    public int Deaths
    {
        get => _deaths;
        set { if (_deaths == value) { return; } _deaths = value; OnPropertyChanged(); OnPropertyChanged(nameof(NameDisplay)); }
    }

    /// <summary>The name, followed by a skull and the count when the player died.</summary>
    private string _combatPowerText = "";

    /// <summary>The combat power the game showed of the player, short ("191.2K"); empty when unknown. The in-game meter shows it after the name.</summary>
    public string CombatPowerText
    {
        get => _combatPowerText;
        set { if (_combatPowerText == value) { return; } _combatPowerText = value; OnPropertyChanged(); }
    }

    public string NameDisplay => Deaths > 0 ? $"{Name}  ☠{(Deaths > 1 ? Deaths.ToString() : "")}" : Name;

    public string ClassName
    {
        get => _className;
        set { if (_className == value) { return; } _className = value; OnPropertyChanged(); OnPropertyChanged(nameof(ClassBrush)); OnPropertyChanged(nameof(ClassEdgeBrush)); OnPropertyChanged(nameof(ClassTextBrush)); OnPropertyChanged(nameof(ClassGlowBrush)); OnPropertyChanged(nameof(ClassLineBrush)); }
    }

    public int Level
    {
        get => _level;
        set { if (_level == value) { return; } _level = value; OnPropertyChanged(); }
    }

    /// <summary>"Elyos"/"Asmodian", or empty while nothing has placed this player on a side yet.
    /// Derived, never read from the log -- see Combat/FactionResolver.</summary>
    public string Faction
    {
        get => _faction;
        set { if (_faction == value) { return; } _faction = value; OnPropertyChanged(); }
    }

    /// <summary>Drives the row's background. Kept separate from <see cref="Faction"/> because the
    /// two answer different questions: a faction can be known while the side is not (nobody has
    /// registered a character yet), and a side can be known while the faction has no name.</summary>
    public bool IsEnemy
    {
        get => _isEnemy;
        set { if (_isEnemy == value) { return; } _isEnemy = value; OnPropertyChanged(); }
    }

    public long Damage
    {
        get => _damage;
        set { if (_damage == value) { return; } _damage = value; OnPropertyChanged(); OnPropertyChanged(nameof(DamageDisplay)); OnPropertyChanged(nameof(DamageCompact)); OnPropertyChanged(nameof(RateOrDamageCompact)); OnPropertyChanged(nameof(TotalOrTakenCompact)); OnPropertyChanged(nameof(SortKey)); }
    }

    private bool _isEstimated;

    /// <summary>Most of this player's damage is estimated, not read from the game (a crowded world boss: the server sends other players' hits without
    /// their damage, so the meter counts each hit at the average of its skill). Shown with a "~" before the figure.</summary>
    public bool IsEstimated
    {
        get => _isEstimated;
        set
        {
            if (_isEstimated != value)
            {
                _isEstimated = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DamageDisplay));
                OnPropertyChanged(nameof(DamageCompact));
            }
        }
    }

    /// <summary>The damage with its thousands separators; "~" before it while it is an estimate.</summary>
    public string DamageDisplay => (IsEstimated ? "~" : "") + Damage.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Null renders as "n/a" in the grid -- see DpsCalculator's remarks on why a single
    /// hit (or otherwise zero elapsed time) must not show a fabricated rate.</summary>
    public double? Dps
    {
        get => _dps;
        set { if (_dps == value) { return; } _dps = value; OnPropertyChanged(); RateChanged(); }
    }

    /// <summary>Damage and rate in thousands/millions, for the compact overlay's dense lines.</summary>
    public string DamageCompact => (IsEstimated ? "~" : "") + Compact(Damage);

    public string DpsCompact => (RdpsView ? Rdps ?? Dps : Dps) is double d ? Compact((long)Math.Round(d)) : "-";

    /// <summary>rDPS (Combat/RaidDps): this player's damage without other people's buffs, plus what their own buffs added to the
    /// others. Null until it has been worked out for the fight on screen.</summary>
    public double? Rdps
    {
        get => _rdps;
        set { if (_rdps == value) { return; } _rdps = value; RateChanged(); }
    }

    private double? _rdps;

    /// <summary>nDPS (Combat/RaidDps): the player's rate without the buffs other people put on them; null until worked out.</summary>
    public double? Ndps
    {
        get => _ndps;
        set { if (_ndps == value) { return; } _ndps = value; OnPropertyChanged(); OnPropertyChanged(nameof(NdpsText)); }
    }

    private double? _ndps;

    /// <summary>What this player's buffs added to the others' damage per second (rDPS − nDPS); null until worked out.</summary>
    public double? Given
    {
        get => _given;
        set { if (_given == value) { return; } _given = value; OnPropertyChanged(); OnPropertyChanged(nameof(GivenText)); }
    }

    private double? _given;

    /// <summary>The meter's optional rate columns (Meter look): rDPS, nDPS and what the buffs gave, short.</summary>
    public string RdpsText => Rdps is double v ? Compact((long)Math.Round(v)) : "–";

    public string NdpsText => Ndps is double v ? Compact((long)Math.Round(v)) : "–";

    public string GivenText => Given is double v && v >= 0.5 ? "+" + Compact((long)Math.Round(v)) : "–";

    /// <summary>The party buffs on this player right now ("Undefeated Mantra · Wind of Power"), empty when none or switched off.</summary>
    public string BuffLine
    {
        get => _buffLine;
        set { if (_buffLine == value) { return; } _buffLine = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBuffLine)); }
    }

    private string _buffLine = "";

    public bool HasBuffLine => _buffLine.Length > 0;

    /// <summary>The rDPS view: the rate shown (and the order) is rDPS instead of DPS.</summary>
    public bool RdpsView
    {
        get => _rdpsView;
        set { if (_rdpsView == value) { return; } _rdpsView = value; RateChanged(); }
    }

    private bool _rdpsView;

    /// <summary>What the list is sorted by: the damage, or rDPS in the rDPS view.</summary>
    public double SortKey => RdpsView ? Rdps ?? Dps ?? 0 : Damage;

    private void RateChanged()
    {
        OnPropertyChanged(nameof(RdpsText));
        OnPropertyChanged(nameof(DpsDisplay));
        OnPropertyChanged(nameof(DpsCompact));
        OnPropertyChanged(nameof(RateOrHealDisplay));
        OnPropertyChanged(nameof(RateOrDamageCompact));
        OnPropertyChanged(nameof(SortKey));
    }

    /// <summary>Short form for the compact overlay: 1.24B, 91.60M, 412.3K, 2.8K, 950.</summary>
    public static string Compact(long value) => Math.Abs(value) switch
    {
        >= 1_000_000_000 => (value / 1e9).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "B",
        >= 1_000_000 => (value / 1e6).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "M",
        >= 1_000 => (value / 1e3).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + "K",
        _ => value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
    };

    public string DpsDisplay => (RdpsView ? Rdps ?? Dps : Dps) is double d ? d.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : "n/a";

    /// <summary>AP the relics currently in this player's bag will pay out once exchanged (see
    /// Data/RelicApDatabase) -- shown on its own, not folded into the session's real AP total,
    /// because the two are not the same kind of number: this is a projection, not AP already
    /// earned, and it's what decides who still needs relics handed to them before the group
    /// exchanges.</summary>
    public long? RelicAp
    {
        get => _relicAp;
        set { if (_relicAp == value) { return; } _relicAp = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApDisplay)); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    /// <summary>Mirror MeterSettings.ShowRelicAp - see ShowDamageTaken's own remarks.</summary>
    public bool ShowRelicAp
    {
        get => _showRelicAp;
        set { if (_showRelicAp == value) { return; } _showRelicAp = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApDisplay)); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    /// <summary>Per the user: only the relic share, not the combined total -- this line's whole
    /// purpose is deciding who still needs relics handed to them before the group exchanges them,
    /// and a number that mixes in already-earned AP obscures exactly that. Blank once the relics
    /// are exchanged (RelicAp drops back to 0/null), same as any player who never picked one up,
    /// or whenever the setting is off.</summary>
    public string ApDisplay => ShowRelicAp && RelicAp is long relic && relic > 0
        ? $"Relic AP: {relic:N0}"
        : "";

    /// <summary>1-based position by damage within the rows currently shown, independent of how
    /// the user sorted the grid; 0 (blank) until the first refresh ranks the row.</summary>
    public int Rank
    {
        get => _rank;
        set { if (_rank == value) { return; } _rank = value; OnPropertyChanged(); OnPropertyChanged(nameof(RankDisplay)); }
    }

    /// <summary>This row's damage as a share of the biggest row shown, 0-100 - the length of the coloured
    /// bar behind the row (the website's meter bars work the same way).</summary>
    public double FillPercent
    {
        get => _fillPercent;
        set { if (_fillPercent == value) { return; } _fillPercent = value; OnPropertyChanged(); }
    }

    private double _fillPercent;

    /// <summary>Taken mode: damage the group shields absorbed on this player, drawn as the blue part of the bar
    /// behind the part that got through (<see cref="Damage"/>). 0 in every other mode.</summary>
    public long Absorbed
    {
        get => _absorbed;
        set { if (_absorbed == value) { return; } _absorbed = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAbsorbed)); OnPropertyChanged(nameof(AbsorbedDisplay)); OnPropertyChanged(nameof(AbsorbedCompact)); }
    }

    /// <summary>All in One modes: the shield absorbed something on this player, shown in blue beside the damage taken.</summary>
    public bool HasAbsorbed => Absorbed > 0 && (AllMode || AllCompact);

    public string AbsorbedDisplay => HasAbsorbed ? $"+{Absorbed:N0}" : "";

    public string AbsorbedCompact => Absorbed > 0 ? Compact(Absorbed) : "";

    private long _absorbed;

    /// <summary>Length of the blue bar: the damage that got through plus the absorbed damage, as a share of the
    /// biggest such total shown, 0-100. 0 when nothing was absorbed, so no blue bar shows.</summary>
    public double AbsorbedFillPercent
    {
        get => _absorbedFillPercent;
        set { if (_absorbedFillPercent == value) { return; } _absorbedFillPercent = value; OnPropertyChanged(); }
    }

    private double _absorbedFillPercent;

    /// <summary>The class colour, translucent, for that bar. Same colours as the website's meter.</summary>
    public System.Windows.Media.Brush ClassBrush => ClassBrushes.For(ClassName);

    /// <summary>The edge of the bar and the name's colour in the in-game meter.</summary>
    public System.Windows.Media.Brush ClassEdgeBrush => ClassBrushes.Edge(ClassName);

    public System.Windows.Media.Brush ClassTextBrush => ClassBrushes.Text(ClassName);

    /// <summary>The bar's glow and the bright line along its bottom (the new look of both views).</summary>
    public System.Windows.Media.Brush ClassGlowBrush => ClassBrushes.Glow(ClassName);

    public System.Windows.Media.Brush ClassLineBrush => ClassBrushes.Line(ClassName);

    /// <summary>The bar colour setting changed: bind the bar to the new brush.</summary>
    public void RefreshClassBrush()
    {
        OnPropertyChanged(nameof(ClassBrush));
        OnPropertyChanged(nameof(ClassEdgeBrush));
        OnPropertyChanged(nameof(ClassTextBrush));
        OnPropertyChanged(nameof(ClassGlowBrush));
        OnPropertyChanged(nameof(ClassLineBrush));
    }

    public string RankDisplay => Rank > 0 ? Rank.ToString() : "";

    /// <summary>This row's share of the shown rows' combined damage, 0-100 - what the bar under the
    /// Damage/DPS line visualises.</summary>
    public double SharePercent
    {
        get => _sharePercent;
        set { if (_sharePercent == value) { return; } _sharePercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShareDisplay)); OnPropertyChanged(nameof(ShareOrHealCompact)); }
    }

    public string ShareDisplay => SharePercent.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + "%";

    /// <summary>Damage this player RECEIVED inside the shown window (from the selected target
    /// only when one is picked) - the tank/aggro question the dealt-damage columns cannot answer.</summary>
    public long DamageTaken
    {
        get => _damageTaken;
        set { if (_damageTaken == value) { return; } _damageTaken = value; OnPropertyChanged(); OnPropertyChanged(nameof(TakenDisplay)); OnPropertyChanged(nameof(TotalOrTakenCompact)); OnPropertyChanged(nameof(TakenCompact)); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    public string TakenDisplay => !AllCompact && (ShowDamageTaken || AllMode) && DamageTaken > 0 ? $"↓ {DamageTaken:N0}" : "";

    private bool _allCompact;

    /// <summary>The All in One Compact mode is on: the usual Damage columns, plus a second line under the name with the
    /// healing and the damage taken (see AllLine). The row carries <see cref="Healing"/> and <see cref="DamageTaken"/> like in All.</summary>
    public bool AllCompact
    {
        get => _allCompact;
        set
        {
            if (_allCompact == value)
            {
                return;
            }

            _allCompact = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TakenDisplay));
            OnPropertyChanged(nameof(HasAbsorbed));
            OnPropertyChanged(nameof(AbsorbedDisplay));
        }
    }

    public string HealCompact => Healing > 0 ? Compact(Healing) : "–";

    public string TakenCompact => DamageTaken > 0 ? Compact(DamageTaken) : "–";

    private bool _allMode;
    private long _healing;

    /// <summary>The All in One mode is on: the row then also carries <see cref="Healing"/> and the damage taken from
    /// monsters in <see cref="DamageTaken"/>, and the columns that otherwise show the rate or the share show those.</summary>
    public bool AllMode
    {
        get => _allMode;
        set
        {
            if (_allMode == value)
            {
                return;
            }

            _allMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TakenDisplay));
            OnPropertyChanged(nameof(HasAbsorbed));
            OnPropertyChanged(nameof(AbsorbedDisplay));
            OnPropertyChanged(nameof(RateOrHealDisplay));
            OnPropertyChanged(nameof(RateOrDamageCompact));
            OnPropertyChanged(nameof(ShareOrHealCompact));
            OnPropertyChanged(nameof(TotalOrTakenCompact));
            OnPropertyChanged(nameof(HasSecondaryInfo));
        }
    }

    /// <summary>Healing done in the shown window, in All in One mode (0 elsewhere).</summary>
    public long Healing
    {
        get => _healing;
        set
        {
            if (_healing == value)
            {
                return;
            }

            _healing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RateOrHealDisplay));
            OnPropertyChanged(nameof(ShareOrHealCompact));
            OnPropertyChanged(nameof(HealCompact));
        }
    }

    /// <summary>The window's and the chips' rate column: DPS, or the healing in All in One mode.</summary>
    public string RateOrHealDisplay => AllMode ? (Healing > 0 ? $"+{Healing:N0}" : "–") : DpsDisplay;

    /// <summary>The compact overlay's columns in All in One mode read Damage, Heal, Taken instead of DPS, share, total.</summary>
    public string RateOrDamageCompact => AllMode ? DamageCompact : DpsCompact;

    public string ShareOrHealCompact => AllMode ? (Healing > 0 ? Compact(Healing) : "–") : ShareDisplay;

    public string TotalOrTakenCompact => AllMode ? (DamageTaken > 0 ? Compact(DamageTaken) : "–") : DamageCompact;

    /// <summary>Whether the second info line (AP/Taken/Defense/Pvp) has anything to show at all -
    /// per the user, that line should default to invisible (and its Auto row collapse to zero
    /// height with it) rather than sit there empty, since AP/Taken/Defense are all opt-in settings
    /// now and most rows will have none of them on.</summary>
    public bool HasSecondaryInfo =>
        ApDisplay.Length > 0 || TakenDisplay.Length > 0 || DefenseDisplay.Length > 0 || PvpDisplay.Length > 0;

    /// <summary>Mirror MeterSettings.ShowShareBars/ShowDamageTaken - set on every refresh so a
    /// changed setting reaches rows that already exist.</summary>
    public bool ShowShareBar
    {
        get => _showShareBar;
        set { if (_showShareBar == value) { return; } _showShareBar = value; OnPropertyChanged(); }
    }

    public bool ShowDamageTaken
    {
        get => _showDamageTaken;
        set { if (_showDamageTaken == value) { return; } _showDamageTaken = value; OnPropertyChanged(); OnPropertyChanged(nameof(TakenDisplay)); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    /// <summary>Avoided-attack tally ("D 3 · P 12 · B 8 (41%)", see Combat/DefenseStats) - blank
    /// when nothing was aimed at this player in the shown window, or when the setting is off.</summary>
    public string DefenseDisplay
    {
        get => _defenseDisplay;
        set { if (_defenseDisplay == value) { return; } _defenseDisplay = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    /// <summary>Kills/deaths/biggest hit against players (see Combat/PvpStats) - only filled in
    /// PVP mode, blank otherwise.</summary>
    public string PvpDisplay
    {
        get => _pvpDisplay;
        set { if (_pvpDisplay == value) { return; } _pvpDisplay = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSecondaryInfo)); }
    }

    private Combat.HitStats _hits;

    /// <summary>How this player's direct hits landed in the fight shown (the meter's optional columns); empty outside the damage views.</summary>
    public Combat.HitStats Hits
    {
        get => _hits;
        set
        {
            if (_hits.Equals(value))
            {
                return;
            }

            _hits = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CritText));
            OnPropertyChanged(nameof(BackText));
            OnPropertyChanged(nameof(FrontText));
            OnPropertyChanged(nameof(PerfectText));
            OnPropertyChanged(nameof(DoubleText));
            OnPropertyChanged(nameof(ParryText));
            OnPropertyChanged(nameof(HitsText));
        }
    }

    private static string Percent(double? value) => value is double v ? v.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "%" : "–";

    public string CritText => Percent(_hits.CritPercent);

    public string BackText => Percent(_hits.BackPercent);

    public string FrontText => Percent(_hits.FrontPercent);

    public string PerfectText => Percent(_hits.PerfectPercent);

    public string DoubleText => Percent(_hits.DoublePercent);

    public string ParryText => Percent(_hits.ParryPercent);

    public string HitsText => _hits.Direct > 0 ? Compact(_hits.Direct) : "–";

    private IReadOnlyList<SupportBadge> _supportBadges = Array.Empty<SupportBadge>();

    /// <summary>The party buffs of the supports (Cleric, Chanter …) on this player over the fight shown, with their uptime (the meter's optional column).</summary>
    public IReadOnlyList<SupportBadge> SupportBadges
    {
        get => _supportBadges;
        set
        {
            if (ReferenceEquals(_supportBadges, value) || (_supportBadges.Count == value.Count && _supportBadges.SequenceEqual(value)))
            {
                return;
            }

            _supportBadges = value;
            OnPropertyChanged();
        }
    }

    public PlayerRow(int objectId)
    {
        ObjectId = objectId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The opt-in extras that still get a second line under the row (relic AP, avoided
    /// attacks, PVP stats). Damage received has its own column, so it does not count here.</summary>
    public bool HasExtraInfo => ApDisplay.Length > 0 || DefenseDisplay.Length > 0 || PvpDisplay.Length > 0;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(HasSecondaryInfo))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasExtraInfo)));
        }
    }
}

/// <summary>One party buff on a player over the fight shown: its icon, the share of the fight it was on, and the colour of the class that gave it.</summary>
public sealed record SupportBadge(string Name, System.Windows.Media.ImageSource? Icon, int Percent, System.Windows.Media.Brush Edge, string Tip)
{
    public string PercentText => Percent + "%";
}
