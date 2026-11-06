using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The CP calculator (Characters → CP calculator): the whole character as the game sent it at login - gear, Stigmas, skill levels, Daevanion, Genus Insight,
/// the pet collection, titles, the wing, Arcana and attributes (<see cref="CpWhole"/>). On top the Combat Power with where it comes from as one bar, and the best
/// next steps over every part, each with how sure its gain is (exact, a chance, an average of a roll, a range). Below a tab per part: the gear as the game's own item
/// pictures with every line's CP, the Stigmas and skills by level, the Genus Insight boards line by line with what a new Analysis gives, the pets by genus and level,
/// and what the calculator was checked against. Values come from the item data, CP from the website's model, fitted to the game's own number when it came.
/// A reading page: no overlay, no hotkey.
/// </summary>
public sealed partial class CpCalcPage : Grid
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    /// <summary>The steps a group of the best next steps shows before its "more" link.</summary>
    private const int StepsPerGroup = 3;
    private const int MostChanges = 30;
    private const int PieceSteps = 5;

    /// <summary>A Stigma's specialisation slots (five).</summary>
    private const int StigmaSpecSlots = 5;

    /// <summary>The colours of the grades, from the game's rarity ladder: S+ orange, S gold, A purple, B blue, C green, D grey.</summary>
    private static readonly Dictionary<string, Color> LetterColors = new(StringComparer.Ordinal)
    {
        ["S+"] = Color.FromRgb(0xFF, 0x8A, 0x3D), ["S"] = Color.FromRgb(0xF0, 0xC0, 0x40), ["A"] = Color.FromRgb(0xB0, 0x7C, 0xFF),
        ["B"] = Color.FromRgb(0x3A, 0x9B, 0xE8), ["C"] = Color.FromRgb(0x4C, 0xC4, 0x6A), ["D"] = Color.FromRgb(0xAA, 0xB2, 0xBD),
    };

    /// <summary>Manastone colours: white, green, blue, purple.</summary>
    private static readonly Color[] TierColors = { Color.FromRgb(0xAA, 0xB2, 0xBD), Color.FromRgb(0xD8, 0xDD, 0xE3), Color.FromRgb(0x4C, 0xC4, 0x6A), Color.FromRgb(0x3A, 0x9B, 0xE8), Color.FromRgb(0xB0, 0x7C, 0xFF) };

    /// <summary>The quality of a Genus Insight line as the game colours it: Common white, Rare green, Epic blue, Unique gold, Heroic orange.</summary>
    private static readonly Color[] GradeColors = { Color.FromRgb(0x8A, 0x93, 0x9E), Color.FromRgb(0xD8, 0xDD, 0xE3), Color.FromRgb(0x5B, 0xD3, 0x6B), Color.FromRgb(0x4A, 0xA8, 0xFF), Color.FromRgb(0xFF, 0xC9, 0x4D), Color.FromRgb(0xFF, 0x8A, 0x3D) };

    /// <summary>Each source's colour in the bar and on its tab.</summary>
    private static readonly Dictionary<CpSource, Color> SourceColors = new()
    {
        [CpSource.Gear] = Color.FromRgb(0xF0, 0xC0, 0x40), [CpSource.Stigmas] = Color.FromRgb(0xB0, 0x7C, 0xFF), [CpSource.Skills] = Color.FromRgb(0x3A, 0x9B, 0xE8),
        [CpSource.Daevanion] = Color.FromRgb(0x2E, 0xC4, 0xB6), [CpSource.Genus] = Color.FromRgb(0xFF, 0x8A, 0x3D), [CpSource.Pets] = Color.FromRgb(0x4C, 0xC4, 0x6A),
        [CpSource.Titles] = Color.FromRgb(0xE8, 0x6A, 0xA8), [CpSource.Wings] = Color.FromRgb(0x7F, 0xD3, 0xFF), [CpSource.Arcana] = Color.FromRgb(0xE0, 0x56, 0x6B),
        [CpSource.Attributes] = Color.FromRgb(0x8A, 0x93, 0xC8), [CpSource.Base] = Color.FromRgb(0x6B, 0x74, 0x80),
    };

    private static readonly Color Gold = Color.FromRgb(0xF0, 0xC0, 0x40);
    private static readonly Color Green = Color.FromRgb(0x4C, 0xC4, 0x6A);
    private static readonly Color Grey = Color.FromRgb(0x8A, 0x93, 0x9E);

    /// <summary>The tabs below the steps, in their order.</summary>
    private enum Tab
    {
        Gear,
        Stigmas,
        Skills,
        Genus,
        Pets,
        Daevanion,
        Titles,
        Arcana,
        Check,
    }

    private readonly Func<Aion2EntityDirectory?> _directory;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _body = new();
    private readonly TextBlock _note = new() { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly List<CpChange> _changes = new();
    private string _signature = "?";
    private string? _character;
    private int _server;
    private IReadOnlyList<Aion2EquippedItem>? _lastGear;
    private int? _selected;
    private Tab _tab = Tab.Gear;

    /// <summary>The groups of the best next steps opened with "more" (kept while the page rebuilds).</summary>
    private readonly HashSet<CpStepGroup> _openGroups = new();

    // a change of the worn pieces in this session waiting for the game's stat update, to check the stats it moved against the tables
    private sealed record PendingCheck(DateTime At, IReadOnlyList<Aion2EquippedItem> Before, IReadOnlyList<Aion2EquippedItem> After, Dictionary<int, int> FrameBefore, List<CpChange> Changes);

    private PendingCheck? _pending;
    private Dictionary<int, int>? _frameAtGear;
    private (DateTime At, List<CpStatGap> Gaps, bool Seen, List<CpChange> Changes)? _sessionCheck;
    private readonly HashSet<CpChange> _confirmed = new();

    /// <summary>The model's stats by the game stat that names them on the page.</summary>
    private static readonly Dictionary<string, string> KeyTokens = new(StringComparer.Ordinal)
    {
        ["atk"] = "WeaponFixingDamage", ["crit"] = "Critical", ["acc"] = "WeaponAccuracy", ["critAtk"] = "CriticalAddDamage", ["pveAtk"] = "PvEAddDamage",
        ["dmg"] = "AmplifyAllDamage", ["wdb"] = "AmplifyWeaponDamage", ["cdmg"] = "AmplifyCriticalDamage", ["front"] = "AmplifyFrontAttack", ["cspd"] = "CombatSpeed",
        ["multi"] = "AdditionalHitRate", ["perf"] = "Perfect", ["dbl"] = "HardHit", ["pve"] = "PvEAmplifyDamage", ["hp"] = "HPMax", ["def"] = "ArmorDefense", ["block"] = "Block",
    };

    public CpCalcPage(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        Margin = new Thickness(14, 10, 14, 14);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        // the page's title as the other character pages have it (6 November 2026, the owner: "fix the CP to be a nicer style")
        var title = new TextBlock { Text = Loc("Main.MenuTools.CpCalc"), FontSize = 26, Foreground = Res("Brush.Text") };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
        head.Children.Add(title);
        _note.Foreground = Res("Brush.TextMuted");
        head.Children.Add(_note);
        SetRow(head, 0);
        Children.Add(head);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _body };
        SetRow(scroll, 1);
        Children.Add(scroll);

        _refresh.Tick += (_, _) => Poll();
        _refresh.Start();
        LocalizationManager.Instance.PropertyChanged += OnLanguage;
        if (!PrivacyConsent.Restricted && !MainWindow.Headless)
        {
            _ = Aion2CpData.RefreshModelAsync(Http).ContinueWith(_ => Dispatcher.InvokeAsync(() => Poll(force: true)), TaskScheduler.Default);
        }

        Poll(force: true);
    }

    /// <summary>Reads the gear again now (the picture mode: no timer runs there).</summary>
    public void Refresh() => Poll(force: true);

    /// <summary>Opens a piece (the picture mode).</summary>
    internal void Select(int slot)
    {
        _selected = slot;
        Poll(force: true);
    }

    /// <summary>Opens a tab by its name (gear, stigmas, skills, genus, pets, daevanion, titles, arcana, check; the picture mode).</summary>
    internal void ShowTab(string name)
    {
        if (Enum.TryParse(name, ignoreCase: true, out Tab tab))
        {
            _tab = tab;
            Poll(force: true);
        }
    }

    public void Stop()
    {
        _refresh.Stop();
        LocalizationManager.Instance.PropertyChanged -= OnLanguage;
    }

    private void OnLanguage(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Poll(force: true);

    private static string Loc(string key) => LocalizationManager.Instance[key];

    private static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Loc(key), args);

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private static string Lang => LocalizationManager.Instance.Language;

    private static SolidColorBrush Solid(Color c) => new(c);

    private static SolidColorBrush Tint(Color c, byte alpha) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    private static ImageSource? Picture(string? path) => path is not null && File.Exists(path) ? OverlayPictures.Load(path) : null;

    // ------------------------------------------------------------------ data

    private CpOfficialRead? Official => _character is null ? null : CpChecks.OfficialOf(_character, _server);

    private IReadOnlyList<(string Stat, int Value)>? StonesOf(Aion2EquippedItem item) => CpCharacterPage.StonesFor(Official?.Pieces, item);

    /// <summary>The character the page counts for: its Item Level (the last check's, else the login list's, else the character page's) and what was measured on it.</summary>
    private CpCharacter CharacterOf(Aion2EntityDirectory? directory)
    {
        if (_character is null)
        {
            return new CpCharacter();
        }

        int? itemLevel = CpChecks.State(_character, _server)?.Points.LastOrDefault()?.ItemLevel is int il and > 0 ? il
            : directory is not null && Aion2Moved.Find(directory.AccountCharacters(), c => c.Name, c => c.ServerId, _character, _server)?.ItemLevel is int own and > 0 ? own
            : Official?.ItemLevel;
        return new CpCharacter(itemLevel, CpChecks.Factors(_character, _server));
    }

    /// <summary>Everything of the character the calculator counts, as the game sent it.</summary>
    private CpWholeInput InputOf(Aion2EntityDirectory? directory, IReadOnlyList<Aion2EquippedItem> gear)
    {
        Aion2CharacterInfo? me = directory?.LocalCharacter;
        return new CpWholeInput(
            me is null ? null : Aion2SkillNames.ClassFromCode(me.ClassCode),
            gear,
            StonesOf,
            directory?.LocalAttributes,
            directory?.LocalSkills,
            directory?.LocalDaevanion,
            directory?.LocalTitles,
            directory?.OwnedTitleIds,
            directory?.LocalWing?.WingId,
            directory?.OwnedWingIds,
            directory?.LocalSpecies,
            directory?.LocalPetStates,
            _character is null ? null : CpChecks.Factors(_character, _server),
            HeldBy(_character, _server),
            directory?.LocalStats);
    }

    /// <summary>How many of an item the character holds: its bag and storage and its account's storage on the server, as the meter last saw them (null when
    /// it never saw them). A piece of gear in the server storage does not count: it may be bound to the character that obtained it, which the meter cannot
    /// see (5 November 2026, a tester: the calculator said to equip a Kromede weapon another character had looted); materials and the like still do.</summary>
    private static Func<int, long>? HeldBy(string? name, int server)
    {
        if (name is null)
        {
            return null;
        }

        CharacterHoldings? mine = Aion2Inventories.Characters().FirstOrDefault(c => c.Name == name && (server == 0 || c.ServerId == 0 || c.ServerId == server));
        AccountHoldings? account = Aion2Inventories.AccountOf(name);
        if (mine is null && account is null)
        {
            return null;
        }

        return item => (mine?.Bag.GetValueOrDefault(item) ?? 0) + (mine?.Storage.GetValueOrDefault(item) ?? 0)
            + (Aion2.Protocol.Aion2CpData.Item(item) is null ? account?.Items.GetValueOrDefault(item) ?? 0 : 0);
    }

    private void AddChanges(IEnumerable<CpChange> changes)
    {
        _changes.InsertRange(0, changes.Where(c => Math.Abs(c.Delta) >= 0.5 || c.Kind == CpChangeKind.Equipped).Reverse());
        if (_changes.Count > MostChanges)
        {
            _changes.RemoveRange(MostChanges, _changes.Count - MostChanges);
        }
    }

    private static string GearSignature(IReadOnlyList<Aion2EquippedItem>? gear) => gear is null ? "-" : string.Join(';', gear.Select(g =>
        $"{g.SlotIndex}:{g.ItemId}:{g.Enchant}:{string.Join(',', (g.Stats ?? Array.Empty<Aion2RolledStat>()).Select(s => s.StatId + "=" + s.Value))}:{string.Join(',', (g.Stones ?? Array.Empty<Aion2Stone>()).Select(s => s.StatId + "/" + s.Tier))}"));

    /// <summary>The rest of the character the page shows, for noticing that it changed: skills, Daevanion, Genus Insight, pets, titles, the wing, attributes.</summary>
    private static string RestSignature(Aion2EntityDirectory? d) => d is null ? "-" : string.Join('|',
        string.Join(',', d.LocalSkills.Select(s => $"{s.SkillId}:{s.Level}:{s.Equipped}:{s.Stigma}")),
        string.Join(',', d.LocalDaevanion.Select(b => b.BoardId + ":" + b.NodeIds.Count)),
        string.Join(',', d.LocalSpecies.Select(k => $"{k.SpeciesId}:{k.Level}:{string.Join('.', k.Effects.Select(e => e.Page + "/" + e.Slot + "/" + e.StatId + "/" + e.Value))}")),
        string.Join(',', d.LocalPetStates.Select(p => p.PetId + ":" + p.Level)),
        string.Join(',', d.LocalTitles.Select(t => t.Slot + ":" + t.TitleId)), d.OwnedTitleIds?.Count, d.OwnedWingIds?.Count, d.LocalWing?.WingId,
        d.LocalAttributes is { } a ? string.Join(',', a.OrderBy(kv => kv.Key).Select(kv => kv.Value)) : "-");

    private void Poll(bool force = false)
    {
        Aion2EntityDirectory? directory = _directory();
        IReadOnlyList<Aion2EquippedItem>? gear = directory?.LocalEquipment;
        Aion2CharacterInfo? me = directory?.LocalCharacter;
        string? name = me?.Name;
        if (me is { Restored: false, ServerId: > 0 } && name is { Length: > 0 })
        {
            CpChecks.AskOfficial(name, me.ServerId, fresh: false); // read once per character at its login; nothing new when that happened already
        }

        IReadOnlyDictionary<int, int>? frame = directory?.LocalStats;
        string frameSig = frame is null ? "-" : string.Join(',', new[] { 31, 317, 19, 128, 104, 318, 193, 52, 307, 1, 13, 5, 14, 8, 4, 10 }.Select(id => frame.GetValueOrDefault(id)));
        string signature = $"{name}|{GearSignature(gear)}|{frameSig}|{RestSignature(directory)}|{Aion2CpData.Model.FromWebsite}|{Lang}|{CpChecks.Version}|{_selected}|{_tab}|{_pending is null}|{ExchangePrices.Version}";
        if (!force && signature == _signature && _pending is null)
        {
            return;
        }

        _signature = signature;
        var current = frame is null ? null : new Dictionary<int, int>(frame);
        if (gear is { Count: > 0 })
        {
            if (name != _character)
            {
                _changes.Clear();
                _confirmed.Clear();
                _pending = null;
                _sessionCheck = null;
                _character = name;
                _server = me?.ServerId ?? 0;
                _selected = null;
            }
            else if (_lastGear is { Count: > 0 } before && GearSignature(before) != GearSignature(gear))
            {
                var (_, totals) = CpCalc.Evaluate(gear, frame, stonesOf: StonesOf, character: CharacterOf(directory));
                var changes = CpCalc.Changes(before, gear, totals, DateTime.Now, stonesOf: StonesOf).ToList();
                AddChanges(changes);
                if (_frameAtGear is not null)
                {
                    _pending = new PendingCheck(DateTime.UtcNow, before, gear, _frameAtGear, changes);
                }
            }

            _lastGear = gear;
        }

        CheckPending(current);
        if (_pending is null && current is not null)
        {
            _frameAtGear = current;
        }

        Rebuild(gear, directory);
    }

    /// <summary>
    /// A change of the worn pieces in this session against the game's stat update that follows it: once the stats it touches moved (or after six seconds), each
    /// touched stat's change in the game's totals is set against what the tables say the pieces add (a stone whose value is a range, and a stat the game's
    /// update didn't carry, aside).
    /// </summary>
    private void CheckPending(Dictionary<int, int>? frame)
    {
        if (_pending is not { } p || frame is null)
        {
            return;
        }

        Dictionary<int, long> before = CpCalc.StatSums(CpCalc.Pieces(p.Before, new CpTotals(0, 0, 0, 0, 0)));
        Dictionary<int, long> after = CpCalc.StatSums(CpCalc.Pieces(p.After, new CpTotals(0, 0, 0, 0, 0)));
        var touched = before.Keys.Union(after.Keys).Where(id => before.GetValueOrDefault(id) != after.GetValueOrDefault(id)).OrderBy(id => id).ToList();
        bool moved = touched.Any(id => frame.GetValueOrDefault(id) != p.FrameBefore.GetValueOrDefault(id));
        if (!moved && DateTime.UtcNow - p.At < TimeSpan.FromSeconds(6))
        {
            return;
        }

        var ranged = p.Before.Concat(p.After).GroupBy(g => g.SlotIndex)
            .Where(g => g.Select(x => x.ItemId + ":" + string.Join(',', (x.Stones ?? Array.Empty<Aion2Stone>()).Select(st => st.StatId + "/" + st.Tier))).Distinct().Count() > 1)
            .SelectMany(g => g.SelectMany(x => x.Stones ?? Array.Empty<Aion2Stone>()).Select(st => st.StatId)).ToHashSet();
        var gaps = new List<CpStatGap>();
        bool seen = false;
        foreach (int id in touched)
        {
            long tables = after.GetValueOrDefault(id) - before.GetValueOrDefault(id);
            long game = frame.GetValueOrDefault(id) - p.FrameBefore.GetValueOrDefault(id);
            if (game == 0 || ranged.Contains(id))
            {
                continue;
            }

            seen = true;
            if (game != tables)
            {
                gaps.Add(new CpStatGap(id, game, tables));
            }
        }

        _sessionCheck = (DateTime.Now, gaps, seen, p.Changes);
        if (seen && gaps.Count == 0)
        {
            _confirmed.UnionWith(p.Changes);
        }

        _pending = null;
    }

    // ------------------------------------------------------------------ the page

    /// <summary>What the page shows of one look: the whole character, the gear with what a change adds, the game's number and the fit.</summary>
    private sealed record View(CpWholeResult Whole, IReadOnlyList<CpPiece> Pieces, IReadOnlyList<CpPiece> Marginal, CpTotals Totals, int? GameCp, DateTime? GameAt, bool FromPage,
        double? Fit, double Unfitted);

    private void Rebuild(IReadOnlyList<Aion2EquippedItem>? gear, Aion2EntityDirectory? directory)
    {
        _body.Children.Clear();
        CpModel model = Aion2CpData.Model;
        _note.Text = Loc("CpCalc.Note") + " " + Loc(model.FromWebsite ? "CpCalc.ModelLive" : "CpCalc.ModelBuiltIn");
        if (!Aion2CpData.Available)
        {
            _body.Children.Add(Card(Label(Loc("CpCalc.NoData"), brush: Res("Brush.TextMuted"))));
            return;
        }

        if (gear is not { Count: > 0 } || CpCalc.Pieces(gear, new CpTotals(0, 0, 0, 0, 0)).Count == 0)
        {
            _body.Children.Add(Card(Label(Loc("CpCalc.NoGear"), brush: Res("Brush.TextMuted"))));
            return;
        }

        // the game's own number: the legion list at the last login (the calculator fitted to it at that login's gear), else the character page
        CpWholeInput input = InputOf(directory, gear);
        CpCheckPoint? last = _character is null ? null : CpChecks.State(_character, _server)?.Points.LastOrDefault();
        int? gameCp = last?.Cp is > 0 ? last.Cp : Official?.Cp;
        DateTime? gameAt = last?.Cp is > 0 ? last.At : Official?.At;
        bool fromPage = last?.Cp is not > 0 && Official?.Cp is > 0;
        CpWholeInput atGame = last?.Cp is > 0
            ? input with { Gear = last.Gear.Select(g => g.ToItem()).Concat(gear.Where(g => Aion2CpData.Item(g.ItemId) is null)).ToList() }
            : input;
        double unfitted = CpWhole.Evaluate(atGame, model).Total;
        double? fit = CpWhole.Fit(gameCp, atGame, model);
        CpWholeResult whole = CpWhole.Evaluate(input, model, fit ?? 1);
        CpTotals marginal = whole.Totals.Marginal;
        var view = new View(whole, whole.Pieces.GroupBy(p => p.Slot).Select(g => g.First()).ToList(),
            CpCalc.Pieces(gear, marginal, model, StonesOf).GroupBy(p => p.Slot).Select(g => g.First()).ToList(), marginal, gameCp, gameAt, fromPage, fit, unfitted);

        _body.Children.Add(Hero(view, model));
        _body.Children.Add(GroupedStepsCard(view.Whole.Steps, Loc("CpCalc.Steps"), Loc("CpCalc.Steps.Note")));
        if (ValueCard(view.Whole.Steps) is { } value)
        {
            _body.Children.Add(value);
        }

        _body.Children.Add(TabStrip(view));
        UIElement content = _tab switch
        {
            Tab.Gear => GearTab(view, model),
            Tab.Stigmas => StigmasTab(view),
            Tab.Skills => SkillsTab(view),
            Tab.Genus => GenusTab(view),
            Tab.Pets => PetsTab(view),
            Tab.Daevanion => DaevanionTab(view),
            Tab.Titles => TitlesTab(view),
            Tab.Arcana => ArcanaTab(view),
            _ => CheckTab(view, model, directory),
        };
        _body.Children.Add(content);
    }

    // ------------------------------------------------------------------ the head: Combat Power and where it comes from

    private static readonly CpSource[] BarOrder =
    {
        CpSource.Gear, CpSource.Stigmas, CpSource.Skills, CpSource.Daevanion, CpSource.Genus, CpSource.Pets, CpSource.Titles, CpSource.Wings, CpSource.Arcana,
        CpSource.Attributes, CpSource.Base,
    };

    private UIElement Hero(View v, CpModel model)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // left: the number, on the class colour's wash with the class emblem faint behind it (6 November 2026: "fix the CP to be a nicer style")
        Aion2CharacterInfo? me = _directory()?.LocalCharacter;
        string cls = me is null ? "" : Aion2SkillNames.ClassFromCode(me.ClassCode) ?? "";
        Color accent = cls.Length > 0 ? ClassBrushes.ClassColor(cls) : Gold;
        var host = new Grid { ClipToBounds = true };
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x48, accent.R, accent.G, accent.B), 0));
        wash.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 0.7));
        host.Children.Add(new Border { Background = wash });
        if (cls.Length > 0 && MainWindow.ClassIcon(cls) is { } emblem)
        {
            host.Children.Add(new Image { Source = emblem, Width = 170, Height = 170, Opacity = 0.11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -34, -40) });
        }

        var left = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        left.Children.Add(new TextBlock { Text = Loc("CpCalc.Hero.Title").ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Solid(accent) });
        if (_character is not null)
        {
            var name = new TextBlock { Text = _character, FontSize = 22, Foreground = Res("Brush.Text"), Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Title");
            left.Children.Add(name);
            int? il = CharacterOf(_directory()).ItemLevel;
            string who = string.Join("  ·  ", new[] { cls, il is int level ? F("CpCalc.Hero.ItemLevel", level.ToString("N0", CultureInfo.CurrentCulture)) : "" }.Where(x => x.Length > 0));
            if (who.Length > 0)
            {
                left.Children.Add(new TextBlock { Text = who, FontSize = 12.5, Foreground = Res("Brush.TextMuted"), Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            }
        }

        double total = v.Whole.Total;
        var number = new TextBlock { Text = Math.Round(v.GameCp ?? total).ToString("N0", CultureInfo.CurrentCulture), FontSize = 42, FontWeight = FontWeights.SemiBold, Foreground = Solid(Gold), Margin = new Thickness(0, 10, 0, 0) };
        left.Children.Add(number);
        string when = v.GameAt is DateTime at ? at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "";
        left.Children.Add(Label(v.GameCp is null ? Loc("CpCalc.Hero.Estimate") : F(v.FromPage ? "CpCalc.Hero.Page" : "CpCalc.Hero.Game", when), 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 0, 0, 0)));
        if (v.GameCp is int game)
        {
            string calc = Math.Round(v.Unfitted).ToString("N0", CultureInfo.CurrentCulture);
            double off = (v.Unfitted - game) / game * 100;
            var fitLine = Label(v.Fit is double f ? F("CpCalc.Hero.Fitted", calc, f.ToString("0.00", CultureInfo.CurrentCulture)) : F("CpCalc.Hero.Unfitted", calc, (off >= 0 ? "+" : "−") + Math.Abs(off).ToString("0.#", CultureInfo.CurrentCulture) + " %"),
                11.5, brush: Res("Brush.TextSubtle"), margin: new Thickness(0, 6, 0, 0));
            fitLine.ToolTip = Loc("CpCalc.Hero.FitTip");
            left.Children.Add(fitLine);
        }

        double session = _changes.Sum(c => c.Delta);
        if (_changes.Count > 0)
        {
            left.Children.Add(Chip(Loc("CpCalc.Session") + "  " + Signed(session), session < 0 ? Color.FromRgb(0xE0, 0x56, 0x6B) : Green, new Thickness(0, 10, 0, 0), Loc("CpCalc.SessionTip")));
        }

        host.Children.Add(left);
        var leftCard = new Border { Child = host, Background = Res("Brush.Panel"), BorderBrush = Tint(accent, 0x60), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        host.SizeChanged += (_, e) => host.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 11, 11);
        SetColumn(leftCard, 0);
        grid.Children.Add(leftCard);

        // right: where it comes from, one bar and the parts under it, two to a line, each with its share
        var right = new StackPanel();
        right.Children.Add(Heading(Loc("CpCalc.Hero.Where")));
        var parts = BarOrder.Select(s => (Source: s, Cp: Math.Max(0, v.Whole.Parts.GetValueOrDefault(s)))).Where(p => p.Cp >= 0.5).ToList();
        double sum = Math.Max(1, parts.Sum(p => p.Cp));
        var bar = new Grid { Height = 22, Margin = new Thickness(0, 0, 0, 14) };
        for (int k = 0; k < parts.Count; k++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(parts[k].Cp, GridUnitType.Star) });
            var piece = new Border
            {
                Background = Solid(SourceColors[parts[k].Source]), Margin = new Thickness(k == 0 ? 0 : 2, 0, 0, 0),
                CornerRadius = new CornerRadius(k == 0 ? 7 : 2, k == parts.Count - 1 ? 7 : 2, k == parts.Count - 1 ? 7 : 2, k == 0 ? 7 : 2),
                ToolTip = SourceName(parts[k].Source) + ": " + Number(parts[k].Cp) + " CP (" + (parts[k].Cp / sum * 100).ToString("0.#", CultureInfo.CurrentCulture) + " %)",
            };
            SetColumn(piece, k);
            bar.Children.Add(piece);
        }

        right.Children.Add(bar);
        var legend = new UniformGrid { Columns = 3 };
        foreach (var (source, cp) in parts)
        {
            Tab? tab = TabOf(source);
            var item = new Grid { Margin = new Thickness(0, 0, 14, 8), Cursor = tab is null ? null : Cursors.Hand, Background = Brushes.Transparent };
            item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Add(item, 0, new Border { Width = 4, Height = 26, CornerRadius = new CornerRadius(2), Background = Solid(SourceColors[source]), Margin = new Thickness(0, 0, 9, 0) });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = SourceName(source), FontSize = 12, Foreground = Res("Brush.TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis });
            words.Children.Add(new TextBlock { Text = Short(cp), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
            Add(item, 1, words);
            Add(item, 2, new TextBlock { Text = (cp / sum * 100).ToString("0.#", CultureInfo.CurrentCulture) + " %", FontSize = 11.5, Foreground = Res("Brush.TextSubtle"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 2) });
            if (tab is Tab target)
            {
                item.MouseLeftButtonUp += (_, _) =>
                {
                    _tab = target;
                    Poll(force: true);
                };
            }

            legend.Children.Add(item);
        }

        right.Children.Add(legend);
        right.Children.Add(Label(Loc("CpCalc.Hero.Note"), 11.5, brush: Res("Brush.TextSubtle"), margin: new Thickness(0, 2, 0, 0)));
        var rightCard = Card(right);
        SetColumn(rightCard, 2);
        grid.Children.Add(rightCard);
        return grid;
    }

    private static Tab? TabOf(CpSource source) => source switch
    {
        CpSource.Gear => Tab.Gear, CpSource.Stigmas => Tab.Stigmas, CpSource.Skills => Tab.Skills, CpSource.Daevanion => Tab.Daevanion, CpSource.Genus => Tab.Genus,
        CpSource.Pets => Tab.Pets, CpSource.Titles or CpSource.Wings => Tab.Titles, CpSource.Arcana or CpSource.Attributes => Tab.Arcana, _ => null,
    };

    private static string SourceName(CpSource source) => Loc("CpCalc.Source." + source);

    // ------------------------------------------------------------------ the steps

    private UIElement StepsCard(IReadOnlyList<CpStep> steps, string title, string? note)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(title));
        if (steps.Count == 0)
        {
            stack.Children.Add(Label(Loc("CpCalc.Steps.None"), 12.5, brush: Res("Brush.TextMuted")));
        }

        foreach (CpStep s in steps)
        {
            stack.Children.Add(StepRow(s));
        }

        if (note is not null)
        {
            stack.Children.Add(Label(note, 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 8, 0, 0)));
        }

        var card = Card(stack);
        card.Margin = new Thickness(0, 0, 0, 10);
        return card;
    }

    /// <summary>
    /// The best next steps by group: what costs nothing first (titles, wings, gear the character already has), then the other groups by their best step;
    /// each group's best few and the rest behind a "more" link.
    /// </summary>
    private UIElement GroupedStepsCard(IReadOnlyList<CpStep> steps, string title, string? note)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(title));
        if (steps.Count == 0)
        {
            stack.Children.Add(Label(Loc("CpCalc.Steps.None"), 12.5, brush: Res("Brush.TextMuted")));
        }

        var groups = steps.GroupBy(s => s.Group).OrderBy(g => g.Key == CpStepGroup.Free ? 0 : 1).ThenByDescending(g => g.Max(s => s.Rank)).ToList();
        Func<CpStep, double?> price = StepPrice();
        foreach (var group in groups)
        {
            var list = InOrder(group, price);
            // the group as a tag in its colour, how many steps it has beside it
            Color tone = GroupColor(group.Key);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, stack.Children.Count > 1 ? 16 : 2, 0, 6) };
            head.Children.Add(new Border
            {
                Background = Tint(tone, 0x22), BorderBrush = Tint(tone, 0x70), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3),
                Child = new TextBlock { Text = Loc("CpCalc.Group." + group.Key), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Solid(tone) },
            });
            head.Children.Add(new TextBlock { Text = "  " + F("CpCalc.Group.Count", list.Count), FontSize = 11.5, Foreground = Res("Brush.TextSubtle"), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(head);
            foreach (CpStep s in list.Take(StepsPerGroup))
            {
                stack.Children.Add(StepRow(s));
            }

            if (list.Count > StepsPerGroup)
            {
                CpStepGroup key = group.Key;
                var rest = new StackPanel { Visibility = _openGroups.Contains(key) ? Visibility.Visible : Visibility.Collapsed };
                foreach (CpStep s in list.Skip(StepsPerGroup))
                {
                    rest.Children.Add(StepRow(s));
                }

                stack.Children.Add(rest);
                var more = new TextBlock { FontSize = 11.5, Foreground = Res("Brush.Accent"), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(104, 2, 0, 0),
                    Text = _openGroups.Contains(key) ? Loc("CpCalc.Steps.Less") : F("CpCalc.Steps.More", list.Count - StepsPerGroup) };
                more.MouseLeftButtonUp += (_, _) =>
                {
                    bool open = !_openGroups.Remove(key);
                    if (open)
                    {
                        _openGroups.Add(key);
                    }

                    rest.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    more.Text = open ? Loc("CpCalc.Steps.Less") : F("CpCalc.Steps.More", list.Count - StepsPerGroup);
                };
                stack.Children.Add(more);
            }
        }

        if (note is not null)
        {
            stack.Children.Add(Label(note, 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 10, 0, 0)));
        }

        var card = Card(stack);
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    /// <summary>
    /// A group's steps in the order to do them (6 November 2026, the owner: "all upgrades should be cheapest amount of Kina to most expensive, cheapest first"):
    /// Stigmas and skills in the class build's order (<see cref="CpStep.Order"/>, see <see cref="CpPlan"/>); everything else the cheapest first - free, then by
    /// what it costs (at the server's market prices when they are in, else its Kina), the most CP first among equals, the ones whose cost isn't known last.
    /// </summary>
    private static List<CpStep> InOrder(IEnumerable<CpStep> group, Func<CpStep, double?> price) =>
        group.OrderBy(s => s.Order > 0 ? 0 : 1).ThenBy(s => s.Order)
            .ThenBy(s => price(s) is null ? 1 : 0).ThenBy(s => price(s) ?? 0).ThenByDescending(s => s.Rank).ToList();

    /// <summary>What a step costs in Kina: 0 when free, its market price (Kina and items) when the server's prices are in, else the Kina it takes; null when not known.</summary>
    private Func<CpStep, double?> StepPrice()
    {
        MarketBook? book = MainWindow.Headless ? null : ExchangePrices.For(_server);
        Func<int, long>? held = HeldBy(_character, _server);
        var cache = new Dictionary<CpStep, double?>(ReferenceEqualityComparer.Instance);
        return s =>
        {
            if (cache.TryGetValue(s, out double? known))
            {
                return known;
            }

            double? value = s.Cost is not { } cost ? null
                : cost.IsFree ? 0
                : book is not null && Price(s, book, held) is { Total: > 0 } priced ? priced.Total
                : cost.Kina > 0 ? cost.Kina : null;
            cache[s] = value;
            return value;
        };
    }

    private static Color GroupColor(CpStepGroup group) => group switch
    {
        CpStepGroup.Free => Green,
        CpStepGroup.Gear => SourceColors[CpSource.Gear],
        CpStepGroup.Skills => SourceColors[CpSource.Stigmas],
        CpStepGroup.Collection => SourceColors[CpSource.Genus],
        _ => SourceColors[CpSource.Daevanion],
    };

    /// <summary>One step: what it gives in green, its picture, what to do and what it costs, how sure the gain is.</summary>
    private UIElement StepRow(CpStep s)
    {
        var row = new Grid { Margin = new Thickness(0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string gain = s.Certainty == CpCertainty.Range ? Range(s.Gain, s.GainMax, signed: true) : Signed(s.Gain);
        // the gain as a green tag, so the column reads at a glance
        Add(row, 0, new Border
        {
            Background = Tint(Green, 0x1E), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = gain, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Heal") },
        });
        if (StepIcon(s) is { } icon)
        {
            icon.Margin = new Thickness(0, 0, 9, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            Add(row, 1, icon);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = StepText(s), FontSize = 12.5, Foreground = Res("Brush.Text"), TextWrapping = TextWrapping.Wrap });
        if (CostText(s) is { } cost)
        {
            text.Children.Add(new TextBlock { Text = cost.Text, FontSize = 11, Foreground = cost.Free ? Solid(Green) : Res("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap,
                ToolTip = cost.Tip, Margin = new Thickness(0, 1, 0, 0) });
        }

        Add(row, 2, text);
        var (chipText, chipColor, tip) = Certainty(s);
        var chip = Chip(chipText, chipColor, new Thickness(10, 0, 0, 0), tip);
        chip.VerticalAlignment = VerticalAlignment.Center;
        Add(row, 3, chip);
        // each step on its own line with a soft highlight under the mouse
        var line = new Border { Child = row, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(-6, 0, -6, 1), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent };
        line.MouseEnter += (_, _) => line.Background = Res("Brush.Control");
        line.MouseLeave += (_, _) => line.Background = Brushes.Transparent;
        return line;
    }

    /// <summary>What a step costs in one line, and its tooltip: Kina and items (on average for a level that can fail), what the character holds of them, the
    /// CP per million Kina; "free" for what it already has.</summary>
    private (string Text, string? Tip, bool Free)? CostText(CpStep s)
    {
        if (s.Kind == CpStepKind.Stigma && s.Shards > 0)
        {
            return (F("CpCalc.Cost.Shards", s.Shards) + (s.Order > 0 ? "  ·  " + Loc("CpCalc.Cost.BuildOrder") : ""), Loc("CpCalc.Cost.ShardsTip"), false);
        }

        if (s.Kind == CpStepKind.Skill && s.Order > 0)
        {
            return (Loc("CpCalc.Cost.BuildOrder"), Loc("CpCalc.Cost.SkillTip"), false);
        }

        if (s.Cost is not { } c)
        {
            return null;
        }

        if (c.IsFree)
        {
            return (Loc(s.Held ? "CpCalc.Cost.Held" : "CpCalc.Cost.Free"), null, true);
        }

        Func<int, long>? held = HeldBy(_character, _server);
        var parts = new List<string>();
        if (c.Kina > 0)
        {
            parts.Add(F("CpCalc.Cost.Kina", Compact(c.Kina)));
        }

        var materials = c.Materials.Select(m => F("CpCalc.Cost.Item", Compact(m.Count), LootText.ItemName(m.ItemId))).ToList();
        parts.AddRange(materials.Take(3));
        if (materials.Count > 3)
        {
            parts.Add(F("CpCalc.Cost.MoreItems", materials.Count - 3));
        }

        string line = (c.Average ? "≈ " : "") + string.Join(" · ", parts);
        if (c.Average)
        {
            line += "  " + F("CpCalc.Cost.Tries", c.Tries.ToString("0.#", CultureInfo.CurrentCulture));
        }

        if (s.PerMillion is double perMillion)
        {
            line += "  ·  " + F("CpCalc.Cost.PerMillion", Number(perMillion));
        }

        var tip = new List<string> { Loc(c.Average ? "CpCalc.Cost.AverageTip" : "CpCalc.Cost.Tip") };
        if (c.Kina > 0)
        {
            tip.Add(F("CpCalc.Cost.Kina", c.Kina.ToString("N0", CultureInfo.CurrentCulture))
                + (held is null ? "" : "   " + F("CpCalc.Cost.Have", held(Aion2CpCosts.KinaId).ToString("N0", CultureInfo.CurrentCulture))));
        }

        foreach (CpMaterial m in c.Materials)
        {
            tip.Add(F("CpCalc.Cost.Item", Math.Ceiling(m.Count).ToString("N0", CultureInfo.CurrentCulture), LootText.ItemName(m.ItemId))
                + (held is null ? "" : "   " + F("CpCalc.Cost.Have", held(m.ItemId).ToString("N0", CultureInfo.CurrentCulture))));
        }

        return (line, string.Join("\n", tip), false);
    }

    /// <summary>A big number short: 1.57M, 461K, 7,850.</summary>
    private static string Compact(double v)
    {
        double a = Math.Abs(v);
        return a >= 1e9 ? (v / 1e9).ToString("0.##", CultureInfo.CurrentCulture) + "B"
            : a >= 1e6 ? (v / 1e6).ToString("0.##", CultureInfo.CurrentCulture) + "M"
            : a >= 1e4 ? (v / 1e3).ToString("0", CultureInfo.CurrentCulture) + "K"
            : Math.Ceiling(v).ToString("N0", CultureInfo.CurrentCulture);
    }

    private static (string Text, Color Color, string Tip) Certainty(CpStep s) => s.Certainty switch
    {
        CpCertainty.Exact => (Loc("CpCalc.Certainty.Exact"), Green, Loc("CpCalc.Certainty.ExactTip")),
        CpCertainty.Chance when s.Kind == CpStepKind.Enhance && s.Chance is double c => (F("CpCalc.Certainty.Try", (c * 100).ToString("0.#", CultureInfo.CurrentCulture)), Gold,
            Loc("CpCalc.Certainty.TryTip")),
        CpCertainty.Chance => (s.Chance is double c ? F("CpCalc.U.Chance", (c * 100).ToString("0.#", CultureInfo.CurrentCulture)) : Loc("CpCalc.Certainty.Chance"), Gold, Loc("CpCalc.Certainty.ChanceTip")),
        CpCertainty.Average when s.Kind == CpStepKind.Transfer => (F("CpCalc.Certainty.Splendent", Math.Round((s.ComboChance ?? 0) * 100), Signed(s.GainMax)), Color.FromRgb(0x5B, 0xB8, 0xF0),
            F("CpCalc.Certainty.SplendentTip", LootText.ItemName(s.ComboId))),
        CpCertainty.Average when s.Kind == CpStepKind.Daevanion => (F("CpCalc.Certainty.Each", Number(s.GainMax)), Color.FromRgb(0x2E, 0xC4, 0xB6), Loc("CpCalc.Certainty.EachTip")),
        CpCertainty.Average => (F("CpCalc.Certainty.Average", Math.Round((s.Chance ?? 0) * 100), Number(s.GainMax)), Color.FromRgb(0x5B, 0xB8, 0xF0), Loc("CpCalc.Certainty.AverageTip")),
        _ => (Loc("CpCalc.Certainty.Range"), Grey, Loc("CpCalc.Certainty.RangeTip")),
    };

    private static FrameworkElement? StepIcon(CpStep s)
    {
        switch (s.Kind)
        {
            case CpStepKind.BetterItem or CpStepKind.Transfer:
                return IconTile(s.OtherId, 24, null);
            case CpStepKind.Enhance or CpStepKind.Breakthrough or CpStepKind.Reroll or CpStepKind.Stone:
                return IconTile(s.ItemId, 24, null);
            case CpStepKind.Stigma or CpStepKind.Skill:
                return SkillIcon(s.OtherId, 24, s.Kind == CpStepKind.Stigma ? SourceColors[CpSource.Stigmas] : SourceColors[CpSource.Skills]);
            case CpStepKind.Wing:
                return SmallPicture(Aion2Wings.Find(s.OtherId, Lang)?.IconPath, 24, SourceColors[CpSource.Wings]);
            default:
            {
                CpSource source = s.Kind switch
                {
                    CpStepKind.Genus => CpSource.Genus, CpStepKind.PetLevel or CpStepKind.PetNew => CpSource.Pets, CpStepKind.Title => CpSource.Titles,
                    CpStepKind.Daevanion => CpSource.Daevanion, _ => CpSource.Base,
                };
                return Dot(SourceColors[source], 24);
            }
        }
    }

    private static string StepText(CpStep s) => s.Kind switch
    {
        CpStepKind.Enhance => F("CpCalc.U.Enhance", SlotOf(s.ItemId), s.ToLevel),
        CpStepKind.Breakthrough => F("CpCalc.U.Breakthrough", SlotOf(s.ItemId), s.ToLevel),
        CpStepKind.Reroll => F("CpCalc.S.Reroll", SlotOf(s.ItemId), Aion2CpData.Name(s.FromStat ?? "", Lang)),
        CpStepKind.Stone => F("CpCalc.U.Stone", SlotOf(s.ItemId), s.ToLevel, Aion2CpData.Name(s.ToStat ?? "", Lang)),
        CpStepKind.BetterItem when s.Held => F("CpCalc.U.BetterHeld", SlotOf(s.ItemId), LootText.ItemName(s.OtherId), s.ToLevel),
        CpStepKind.BetterItem => F("CpCalc.U.Better", SlotOf(s.ItemId), LootText.ItemName(s.OtherId), s.ToLevel),
        CpStepKind.Transfer => F("CpCalc.U.Transfer", SlotOf(s.ItemId), LootText.ItemName(s.ItemId), LootText.ItemName(s.OtherId), s.ToLevel),
        CpStepKind.Stigma => F("CpCalc.S.Stigma", Aion2SkillNames.DisplayOf(s.OtherId), s.FromLevel, s.ToLevel),
        CpStepKind.Skill => F("CpCalc.S.Skill", Aion2SkillNames.DisplayOf(s.OtherId), s.FromLevel, s.ToLevel),
        CpStepKind.Genus => F("CpCalc.S.Genus", Aion2Artwork.SpeciesName(s.OtherId, Lang), s.Slot, Aion2CpData.Name(s.FromStat ?? "", Lang)),
        CpStepKind.PetLevel => F("CpCalc.S.PetLevel", GenusName(s.Key), s.ToLevel, s.Count),
        CpStepKind.PetNew => F("CpCalc.S.PetNew", GenusName(s.Key), s.Count),
        CpStepKind.Title => F("CpCalc.S.Title", Aion2Titles.Find(s.OtherId, Lang)?.Name ?? "#" + s.OtherId, Loc("CpCalc.TitleSlot." + s.Slot)),
        CpStepKind.Wing => F("CpCalc.S.Wing", Aion2Wings.Find(s.OtherId, Lang)?.Name ?? "#" + s.OtherId),
        CpStepKind.Daevanion => F("CpCalc.S.Daevanion", Aion2DaevanionCatalog.BoardName(s.OtherId), s.Count),
        _ => "",
    };

    /// <summary>The name of a genus by the tables' key (Intellect ...) as the game writes it.</summary>
    private static string GenusName(string? key) =>
        Aion2CpSources.Genera.FirstOrDefault(x => x.Key == key) is { SpeciesId: > 0 } genus ? Aion2Artwork.SpeciesName(genus.SpeciesId, Lang) : key ?? "";

    // ------------------------------------------------------------------ the tabs

    private UIElement TabStrip(View v)
    {
        var strip = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (Tab tab in Enum.GetValues<Tab>())
        {
            bool on = tab == _tab;
            double cp = tab switch
            {
                Tab.Titles => v.Whole.Parts[CpSource.Titles] + v.Whole.Parts[CpSource.Wings],
                Tab.Arcana => v.Whole.Parts[CpSource.Arcana] + v.Whole.Parts[CpSource.Attributes],
                Tab.Check => -1,
                _ => v.Whole.Parts[(CpSource)Enum.Parse(typeof(CpSource), tab.ToString())],
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            Color color = tab switch { Tab.Titles => SourceColors[CpSource.Titles], Tab.Arcana => SourceColors[CpSource.Arcana], Tab.Check => Grey, _ => SourceColors[(CpSource)Enum.Parse(typeof(CpSource), tab.ToString())] };
            content.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Solid(color), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = Loc("CpCalc.Tab." + tab), FontSize = 12.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = on ? Res("Brush.Text") : Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
            if (cp >= 0.5)
            {
                content.Children.Add(new TextBlock { Text = "  " + Short(cp), FontSize = 11.5, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
            }

            Brush idle = Res("Brush.Control"), hover = Res("Brush.ControlHover");
            var pill = new Border
            {
                Child = content, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                Background = on ? Tint(color, 0x26) : Brushes.Transparent, BorderBrush = on ? Tint(color, 0xB0) : Brushes.Transparent, BorderThickness = new Thickness(1),
            };
            idle = Brushes.Transparent;
            if (!on)
            {
                pill.MouseEnter += (_, _) => pill.Background = hover;
                pill.MouseLeave += (_, _) => pill.Background = idle;
            }

            pill.MouseLeftButtonUp += (_, _) =>
            {
                _tab = tab;
                Poll(force: true);
            };
            strip.Children.Add(pill);
        }

        // the tabs in one bar, as the Journey page's periods sit in theirs
        strip.Margin = new Thickness(0);
        return new Border { Child = strip, Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Background = Res("Brush.Panel"), BorderBrush = Res("Brush.Border") };
    }

    /// <summary>A tab's first line: the part's CP and its share of the whole, and what the part is.</summary>
    private static UIElement Summary(View v, double cp, string text)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        double total = Math.Max(1, v.Whole.Total);
        stack.Children.Add(new TextBlock { Text = F("CpCalc.Tab.Summary", Number(cp), (cp / total * 100).ToString("0.#", CultureInfo.CurrentCulture)), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
        stack.Children.Add(Label(text, 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 2, 0, 0)));
        return stack;
    }

    private UIElement? StepsOf(View v, Func<CpStep, bool> which, int most = 6)
    {
        var steps = v.Whole.Steps.Where(which).Take(most).ToList();
        return steps.Count == 0 ? null : StepsCard(steps, Loc("CpCalc.Steps.Here"), null);
    }

    // ---------------------------------------------------------------- gear

    private UIElement GearTab(View v, CpModel model)
    {
        var page = new StackPanel();
        page.Children.Add(Summary(v, v.Whole.Parts[CpSource.Gear], Loc("CpCalc.Gear.Head")));
        var grades = v.Marginal.ToDictionary(p => p.Slot, p => CpCalc.Grade(p, v.Totals, model));
        char race = CpWhole.RaceOf(v.Pieces.Select(p => new Aion2EquippedItem(p.Slot, p.ItemId)).ToList()); // the other race's pieces are never the best for this one
        var best = v.Marginal.ToDictionary(p => p.Slot, p => CpCalc.BestInSlot(p, v.Totals, model, race));
        CpPiece selected = v.Pieces.FirstOrDefault(p => p.Slot == _selected) ?? v.Pieces[0];

        // the gear as the game shows it on the left, the open piece on the right
        var main = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });
        var left = new StackPanel();
        var headRow = new Grid();
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Add(headRow, 0, Heading(Loc("CpCalc.Gear")));
        if (CpCalc.Overall(v.Marginal, v.Totals, model) is { } overall)
        {
            // the tip: how many of the class's ideal lines the pieces have, all together
            var counts = v.Pieces.Select(p => IdealCount(p, ClassName())).Where(c => c is not null).Select(c => c!.Value).ToList();
            string? tip = counts.Count > 0 ? F("CpCalc.BestLines", counts.Sum(c => c.Have), counts.Sum(c => c.Of)) : null;
            var g = Chip(Loc("CpCalc.Grade") + "  " + overall.Letter + "  " + F("CpCalc.Score", Math.Round(overall.Score)), LetterColors[overall.Letter], new Thickness(0, 0, 0, 8), tip);
            Add(headRow, 1, g);
        }

        left.Children.Add(headRow);
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -8, 0) };
        foreach (CpPiece piece in v.Pieces)
        {
            grid.Children.Add(PieceCard(piece, grades.GetValueOrDefault(piece.Slot), best.GetValueOrDefault(piece.Slot), piece.Slot == selected.Slot));
        }

        left.Children.Add(grid);
        left.Children.Add(Label(Loc("CpCalc.Footer"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 4, 0, 0)));
        left.Children.Add(Label(Loc("CpCalc.GradeNote"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 4, 0, 0)));
        left.Children.Add(Label(StonesNote(), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 4, 0, 0)));
        var leftCard = Card(left);
        leftCard.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(leftCard, 0);
        main.Children.Add(leftCard);
        var pieceSteps = v.Whole.Steps.Where(s => s.Slot == selected.Slot && s.Kind is CpStepKind.Enhance or CpStepKind.Breakthrough or CpStepKind.Reroll or CpStepKind.Stone
                or CpStepKind.BetterItem or CpStepKind.Transfer)
            .Take(PieceSteps).ToList();
        var detail = Card(Detail(selected, grades.GetValueOrDefault(selected.Slot), best.GetValueOrDefault(selected.Slot), pieceSteps, v.Totals));
        detail.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(detail, 2);
        main.Children.Add(detail);
        page.Children.Add(main);

        // the live changes of this session
        var live = new StackPanel();
        live.Children.Add(Heading(Loc("CpCalc.Changes")));
        if (_changes.Count == 0)
        {
            live.Children.Add(Label(Loc("CpCalc.NoChanges"), 12.5, brush: Res("Brush.TextMuted")));
        }

        foreach (CpChange c in _changes)
        {
            string time = c.At.ToString("t", CultureInfo.CurrentCulture);
            live.Children.Add(Row(Signed(c.Delta), ChangeText(c), _confirmed.Contains(c) ? "✓ " + time : time, c.Delta < 0 ? Res("Brush.Danger") : Res("Brush.Heal"), c.ItemId));
        }

        if (_confirmed.Count > 0)
        {
            live.Children.Add(Label(Loc("CpCalc.Live.Confirmed"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 8, 0, 0)));
        }

        page.Children.Add(Card(live));
        return page;
    }

    // ---------------------------------------------------------------- Stigmas

    private UIElement StigmasTab(View v)
    {
        var page = new StackPanel();
        page.Children.Add(Summary(v, v.Whole.Parts[CpSource.Stigmas], Loc("CpCalc.Stigmas.Head")));
        if (v.Whole.Stigmas.Count == 0)
        {
            page.Children.Add(Card(Label(Loc("CpCalc.Stigmas.None"), 12.5, brush: Res("Brush.TextMuted"))));
            return page;
        }

        int top = CpWhole.StigmaTop;
        var equipped = v.Whole.Stigmas.Where(s => s.Equipped).ToList();
        var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 0) };
        foreach (CpStigmaRow s in equipped)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Add(row, 0, SkillIcon(s.SkillId, 46, SourceColors[CpSource.Stigmas]));
            var text = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = Aion2SkillNames.DisplayOf(s.SkillId), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(LevelBar(s.Level, top, SourceColors[CpSource.Stigmas], F("CpCalc.LevelOf", s.Level, top)));
            var under = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            under.Children.Add(new TextBlock { Text = s.NextLevel >= 1 ? F("CpCalc.Stigmas.Next", s.Level + 1, Number(s.NextLevel)) : Loc("CpCalc.Stigmas.Top"), FontSize = 11.5, Foreground = s.NextLevel >= 1 ? Res("Brush.Heal") : Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
            under.Children.Add(new TextBlock { Text = "    ", FontSize = 11 });
            for (int k = 0; k < StigmaSpecSlots; k++)
            {
                under.Children.Add(new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Fill = k < s.SpecFilled ? Solid(Gold) : Brushes.Transparent, Stroke = Solid(Gold), StrokeThickness = 1.2 });
            }

            under.Children.Add(new TextBlock { Text = " " + F("CpCalc.Stigmas.Spec", s.SpecFilled, StigmaSpecSlots), FontSize = 11, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
            text.Children.Add(under);
            Add(row, 1, text);
            Add(row, 2, BigCp(s.Cp));
            var card = Card(row);
            card.Margin = new Thickness(0, 0, 10, 10);
            cards.Children.Add(card);
        }

        page.Children.Add(cards);
        var rest = v.Whole.Stigmas.Where(s => !s.Equipped).ToList();
        if (rest.Count > 0)
        {
            var list = new StackPanel();
            list.Children.Add(Heading(Loc("CpCalc.Stigmas.Unequipped")));
            var wrap = new WrapPanel();
            foreach (CpStigmaRow s in rest)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 6), Opacity = 0.75 };
                item.Children.Add(SkillIcon(s.SkillId, 22, Grey));
                item.Children.Add(Label("  " + Aion2SkillNames.DisplayOf(s.SkillId) + "  " + F("CpCalc.Lv", s.Level), 12));
                wrap.Children.Add(item);
            }

            list.Children.Add(wrap);
            page.Children.Add(Card(list));
        }

        return page;
    }

    // ---------------------------------------------------------------- skills

    private UIElement SkillsTab(View v)
    {
        var page = new StackPanel();
        double per = v.Whole.Skills.FirstOrDefault()?.PerLevel ?? 0;
        page.Children.Add(Summary(v, v.Whole.Parts[CpSource.Skills], F("CpCalc.Skills.Head", Number(per))));
        if (v.Whole.Skills.Count == 0)
        {
            page.Children.Add(Card(Label(Loc("CpCalc.Skills.None"), 12.5, brush: Res("Brush.TextMuted"))));
            return page;
        }

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var active = new StackPanel();
        active.Children.Add(Heading(Loc("CpCalc.Skills.Active")));
        var passive = new StackPanel();
        passive.Children.Add(Heading(Loc("CpCalc.Skills.Passive")));
        foreach (CpSkillRow s in v.Whole.Skills)
        {
            (s.Passive ? passive : active).Children.Add(SkillRow(s));
        }

        var a = Card(active);
        a.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(a, 0);
        columns.Children.Add(a);
        var p = Card(passive);
        p.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(p, 2);
        columns.Children.Add(p);
        page.Children.Add(columns);
        return page;
    }

    private static UIElement SkillRow(CpSkillRow s)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), Opacity = s.Counted ? 1 : 0.55 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        Add(row, 0, SkillIcon(s.SkillId, 32, s.Passive ? Grey : SourceColors[CpSource.Skills]));
        var text = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = Aion2SkillNames.DisplayOf(s.SkillId), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 });
        line.Children.Add(new TextBlock { Text = "  " + F("CpCalc.LevelOf", s.Level, CpWhole.SkillTop), FontSize = 12, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Bottom });
        text.Children.Add(line);
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        string bonus = s.Level > s.BaseLevel ? F("CpCalc.Skills.Bonus", s.BaseLevel, s.Level - s.BaseLevel) : F("CpCalc.Skills.Trained", s.BaseLevel);
        sub.Children.Add(new TextBlock { Text = s.Counted ? bonus : Loc("CpCalc.Skills.NotCounted"), FontSize = 11, Foreground = Res("Brush.TextMuted") });
        if (!s.Passive)
        {
            sub.Children.Add(new TextBlock { Text = "   ", FontSize = 11 });
            for (int k = 0; k < CpWhole.SpecLevels.Length; k++)
            {
                bool open = k < s.SpecOpen, filled = k < s.SpecFilled && open;
                sub.Children.Add(new Ellipse
                {
                    Width = 8, Height = 8, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                    Fill = filled ? Solid(Gold) : Brushes.Transparent, Stroke = open ? Solid(Gold) : Solid(Grey), StrokeThickness = 1.2,
                    ToolTip = open ? F("CpCalc.Skills.SpecOpen", k + 1) : F("CpCalc.Skills.NextSpec", CpWhole.SpecLevels[k], k + 1),
                });
            }

            if (s.NextSpecAt is int at)
            {
                sub.Children.Add(new TextBlock { Text = " " + F("CpCalc.Skills.NextSpec", at, s.SpecOpen + 1), FontSize = 11, Foreground = Res("Brush.TextMuted") });
            }
        }

        text.Children.Add(sub);
        Add(row, 1, text);
        Add(row, 2, new TextBlock { Text = s.Counted ? Number(s.Cp) : "—", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        return new Border { Child = row, Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(6), Background = Res("Brush.RowAlt"), Margin = new Thickness(0, 0, 0, 3) };
    }

    // ---------------------------------------------------------------- Genus Insight

    private UIElement GenusTab(View v)
    {
        var page = new StackPanel();
        page.Children.Add(Summary(v, v.Whole.Parts[CpSource.Genus], Loc("CpCalc.Genus.Head")));
        if (v.Whole.Genus.Count == 0)
        {
            page.Children.Add(Card(Label(Loc("CpCalc.Genus.None"), 12.5, brush: Res("Brush.TextMuted"))));
            return page;
        }

        if (StepsOf(v, s => s.Kind == CpStepKind.Genus) is { } steps)
        {
            page.Children.Add(steps);
        }

        var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 0) };
        foreach (CpGenusRow g in v.Whole.Genus)
        {
            var stack = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = Aion2Artwork.SpeciesName(g.SpeciesId, Lang), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Solid(SourceColors[CpSource.Genus]) });
            names.Children.Add(Label(F("CpCalc.Genus.Level", g.Level) + (g.Presets > 1 ? "  ·  " + F("CpCalc.Genus.Presets", g.Presets) : ""), 11.5, brush: Res("Brush.TextMuted")));
            Add(head, 0, names);
            Add(head, 1, BigCp(g.Cp));
            stack.Children.Add(head);
            foreach (CpGenusLine l in g.Lines)
            {
                stack.Children.Add(GenusLineRow(l));
            }

            var card = Card(stack);
            card.Margin = new Thickness(0, 0, 10, 10);
            cards.Children.Add(card);
        }

        page.Children.Add(cards);
        if (StrengthNote(v.Totals, Aion2CpData.Model) is { } strength)
        {
            page.Children.Add(Label(strength + " " + Loc("CpCalc.Genus.Shared"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 2, 0, 4)));
        }

        page.Children.Add(Label(Loc("CpCalc.Genus.Note"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 2, 0, 0)));
        return page;
    }

    /// <summary>A line of a board: slot, the stat in its grade's colour, the value, its CP, and what a new Analysis would do: better on average, or hard to beat.</summary>
    private static UIElement GenusLineRow(CpGenusLine l)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        Add(row, 0, new TextBlock { Text = l.Slot.ToString(CultureInfo.CurrentCulture), FontSize = 11, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Color c = GradeColors[Math.Clamp(l.Grade, 0, 5)];
        name.Children.Add(new TextBlock { Text = Aion2CpData.Name(l.Stat, Lang), FontSize = 12.5, Foreground = Solid(c), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 210, ToolTip = Loc("CpCalc.Genus.Grade" + Math.Clamp(l.Grade, 1, 5)) });
        name.Children.Add(new TextBlock { Text = "  +" + Value(l.Stat, l.Value), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
        Add(row, 1, name);

        // a new Analysis: worth it on average (blue), or a line few rolls beat (green)
        if (l.Average - l.Now >= 1)
        {
            Add(row, 2, Chip(F("CpCalc.Genus.Reroll", Number(l.Average - l.Now)), Color.FromRgb(0x5B, 0xB8, 0xF0), new Thickness(6, 0, 6, 0), F("CpCalc.Genus.Better", Math.Round(l.Better * 100))));
        }
        else if (l.Cp is > 0 && l.Better <= 0.15 && l.Now - l.Average >= Math.Max(25, l.Now / 4))
        {
            Add(row, 2, Chip(Loc("CpCalc.Genus.Keep"), Green, new Thickness(6, 0, 6, 0), F("CpCalc.Genus.Better", Math.Round(l.Better * 100))));
        }

        Add(row, 3, new TextBlock { Text = l.Cp is double cp ? Number(cp) : Loc("CpCalc.NotInCp"), FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = l.Cp is null ? Res("Brush.TextMuted") : Res("Brush.Text"), FontWeight = l.Cp is null ? FontWeights.Normal : FontWeights.SemiBold });
        return new Border { Child = row, Padding = new Thickness(6, 3, 6, 3), CornerRadius = new CornerRadius(5), Background = Res("Brush.RowAlt"), Margin = new Thickness(0, 0, 0, 2) };
    }

    // ---------------------------------------------------------------- pets

    private UIElement PetsTab(View v)
    {
        var page = new StackPanel();
        page.Children.Add(Summary(v, v.Whole.Parts[CpSource.Pets], Loc("CpCalc.Pets.Head")));
        if (v.Whole.Pets.All(p => p.ByLevel.Sum() == 0))
        {
            page.Children.Add(Card(Label(Loc("CpCalc.Pets.None"), 12.5, brush: Res("Brush.TextMuted"))));
            return page;
        }

        if (StepsOf(v, s => s.Kind is CpStepKind.PetLevel or CpStepKind.PetNew) is { } steps)
        {
            page.Children.Add(steps);
        }

        var cards = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) };
        foreach (CpPetRow p in v.Whole.Pets)
        {
            var stack = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = GenusName(p.Key), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Solid(SourceColors[CpSource.Pets]) });
            names.Children.Add(Label(F("CpCalc.Pets.Owned", p.ByLevel.Sum(), p.InGame), 11.5, brush: Res("Brush.TextMuted")));
            Add(head, 0, names);
            Add(head, 1, BigCp(p.Cp));
            stack.Children.Add(head);

            // how many at each level, as one bar (level 3 brightest) with the rest still to collect
            var bar = new Grid { Height = 10, Margin = new Thickness(0, 2, 0, 4) };
            int[] counts = { p.ByLevel[2], p.ByLevel[1], p.ByLevel[0], Math.Max(0, p.InGame - p.ByLevel.Sum()) };
            byte[] alphas = { 0xFF, 0xAA, 0x66, 0x00 };
            for (int k = 0; k < counts.Length; k++)
            {
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, counts[k]), GridUnitType.Star) });
                var seg = new Border { Background = k == 3 ? Res("Brush.Border") : Tint(SourceColors[CpSource.Pets], alphas[k]), Margin = new Thickness(0, 0, 1, 0) };
                SetColumn(seg, k);
                bar.Children.Add(seg);
            }

            stack.Children.Add(bar);
            stack.Children.Add(Label(F("CpCalc.Pets.Levels", p.ByLevel[0], p.ByLevel[1], p.ByLevel[2]), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 0, 0, 6)));
            foreach (CpSourceLine l in p.Lines)
            {
                stack.Children.Add(StatRow(l));
            }

            if (p.StepGain[2] >= 0.5 || p.StepGain[0] >= 0.5)
            {
                stack.Children.Add(Label(F("CpCalc.Pets.Each", Number(p.StepGain[0]), Number(p.StepGain[0] + p.StepGain[1] + p.StepGain[2])), 11.5, brush: Res("Brush.Heal"), margin: new Thickness(0, 6, 0, 0)));
            }

            var card = Card(stack);
            card.Margin = new Thickness(0, 0, 10, 10);
            cards.Children.Add(card);
        }

        page.Children.Add(cards);
        if (v.Whole.PetsNearLevel.Count > 0)
        {
            var near = new StackPanel();
            near.Children.Add(Heading(Loc("CpCalc.Pets.Near")));
            var wrap = new UniformGrid { Columns = 3 };
            foreach (var (petId, level, progress, needed) in v.Whole.PetsNearLevel)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 14, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Add(row, 0, SmallPicture(Aion2Pets.PetIconPath(petId), 34, SourceColors[CpSource.Pets]));
                var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = (Aion2Pets.PetName(petId, Lang) ?? "#" + petId) + "  " + F("CpCalc.Lv", level), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(LevelBar(progress, needed, SourceColors[CpSource.Pets], F("CpCalc.Pets.Souls", progress, needed)));
                Add(row, 1, text);
                wrap.Children.Add(row);
            }

            near.Children.Add(wrap);
            page.Children.Add(Card(near));
        }

        return page;
    }

    // ---------------------------------------------------------------- Daevanion

    private UIElement DaevanionTab(View v)
    {
        var page = new StackPanel();
        page.Children.Add(Summary(v, v.Whole.Parts[CpSource.Daevanion], Loc("CpCalc.Daevanion.Head")));
        if (v.Whole.Boards.Count == 0)
        {
            page.Children.Add(Card(Label(Loc("CpCalc.Daevanion.None"), 12.5, brush: Res("Brush.TextMuted"))));
            return page;
        }

        var cards = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) };
        foreach (CpBoardRow b in v.Whole.Boards)
        {
            var stack = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = Aion2DaevanionCatalog.BoardName(b.BoardId), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Solid(SourceColors[CpSource.Daevanion]) });
            names.Children.Add(LevelBar(b.Open, Math.Max(1, b.Total), SourceColors[CpSource.Daevanion], F("CpCalc.Daevanion.Nodes", b.Open, b.Total)));
            Add(head, 0, names);
            Add(head, 1, BigCp(b.Cp));
            stack.Children.Add(head);
            foreach (CpSourceLine l in b.Lines.Take(6))
            {
                stack.Children.Add(StatRow(l));
            }

            if (b.SkillLevels > 0)
            {
                stack.Children.Add(Label(F("CpCalc.Daevanion.Skills", b.SkillLevels), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 4, 0, 0)));
            }

            if (b.CpLeft >= 1)
            {
                stack.Children.Add(Label(F("CpCalc.Daevanion.Left", Number(b.CpLeft), b.ClosedStatNodes), 11.5, brush: Res("Brush.Heal"), margin: new Thickness(0, 4, 0, 0)));
            }

            var card = Card(stack);
            card.Margin = new Thickness(0, 0, 10, 10);
            cards.Children.Add(card);
        }

        page.Children.Add(cards);
        return page;
    }

    // ---------------------------------------------------------------- titles and the wing

    private UIElement TitlesTab(View v)
    {
        var page = new StackPanel();
        page.Children.Add(Summary(v, v.Whole.Parts[CpSource.Titles] + v.Whole.Parts[CpSource.Wings], Loc("CpCalc.Titles.Head")));
        if (StepsOf(v, s => s.Kind is CpStepKind.Title or CpStepKind.Wing) is { } steps)
        {
            page.Children.Add(steps);
        }

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var titles = new StackPanel();
        titles.Children.Add(Heading(Loc("CpCalc.Titles.Worn")));
        for (int slot = 1; slot <= 3; slot++)
        {
            CpTitleRow? t = v.Whole.Titles.FirstOrDefault(x => x.Slot == slot);
            var head = new Grid { Margin = new Thickness(0, slot == 1 ? 0 : 8, 0, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = Loc("CpCalc.TitleSlot." + slot).ToUpper(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted") });
            Aion2TitleInfo? info = t is null ? null : Aion2Titles.Find(t.TitleId, Lang);
            names.Children.Add(new TextBlock { Text = t is null ? Loc("CpCalc.Titles.Empty") : info?.Name ?? "#" + t.TitleId, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = t is null ? Res("Brush.TextMuted") : Solid(GradeTone(info?.Grade)) });
            Add(head, 0, names);
            if (t is not null)
            {
                Add(head, 1, BigCp(t.Cp));
            }

            titles.Children.Add(head);
            foreach (CpSourceLine l in t?.Lines ?? Array.Empty<CpSourceLine>())
            {
                titles.Children.Add(StatRow(l));
            }
        }

        titles.Children.Add(new Border { Height = 1, Background = Res("Brush.Border"), Margin = new Thickness(0, 12, 0, 10) });
        if (v.Whole.OwnedTitles is { } owned)
        {
            var ownedHead = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            ownedHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ownedHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Add(ownedHead, 0, Heading(F("CpCalc.Titles.Owned", v.Whole.OwnedTitleCount)));
            Add(ownedHead, 1, BigCp(v.Whole.OwnedTitlesCp));
            titles.Children.Add(ownedHead);
            foreach (CpSourceLine l in owned.Where(l => l.Cp is >= 0.5))
            {
                titles.Children.Add(StatRow(l));
            }

            int rest = owned.Count(l => l.Cp is not >= 0.5);
            if (rest > 0)
            {
                titles.Children.Add(Label(F("CpCalc.Titles.More", rest), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(6, 4, 0, 0)));
            }
        }
        else
        {
            titles.Children.Add(Label(Loc("CpCalc.Titles.OwnedUnknown"), 11.5, brush: Res("Brush.TextMuted")));
        }

        var t1 = Card(titles);
        t1.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(t1, 0);
        columns.Children.Add(t1);

        var wing = new StackPanel();
        wing.Children.Add(Heading(Loc("CpCalc.Wing.Head")));
        if (v.Whole.Wing is { } w)
        {
            Aion2WingInfo? info = Aion2Wings.Find(w.WingId, Lang);
            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Add(head, 0, SmallPicture(info?.IconPath, 46, SourceColors[CpSource.Wings]));
            var names = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = info?.Name ?? "#" + w.WingId, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Solid(GradeTone(info?.Grade)) });
            Add(head, 1, names);
            Add(head, 2, BigCp(w.Cp));
            wing.Children.Add(head);
            foreach (CpSourceLine l in w.Lines)
            {
                wing.Children.Add(StatRow(l));
            }
        }
        else
        {
            wing.Children.Add(Label(Loc("CpCalc.Wing.None"), 12.5, brush: Res("Brush.TextMuted")));
        }

        var w1 = Card(wing);
        w1.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(w1, 2);
        columns.Children.Add(w1);
        page.Children.Add(columns);
        return page;
    }

    // ---------------------------------------------------------------- Arcana and attributes

    private UIElement ArcanaTab(View v)
    {
        var page = new StackPanel();
        page.Children.Add(Summary(v, v.Whole.Parts[CpSource.Arcana] + v.Whole.Parts[CpSource.Attributes], Loc("CpCalc.Arcana.Head")));
        if (v.Whole.Arcana.Count > 0)
        {
            var cards = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, -10, 0) };
            foreach (CpArcanaRow a in v.Whole.Arcana)
            {
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var icon = IconTile(a.ItemId, 54, a.Level > 0 ? "+" + a.Level : null);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(icon);
                stack.Children.Add(new TextBlock { Text = LootText.ItemName(a.ItemId), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Solid(LootText.GradeColor(a.ItemId)), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) });
                if (a.GodStat is { } god)
                {
                    stack.Children.Add(new TextBlock { Text = Aion2CpData.Name(god, Lang) + " +" + a.GodValue, FontSize = 11.5, Foreground = Res("Brush.Text"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
                }

                stack.Children.Add(new TextBlock { Text = Number(a.Cp) + " CP", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Accent"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                if (a.SkillLevels > 0)
                {
                    stack.Children.Add(new TextBlock { Text = F("CpCalc.Arcana.Skills", a.SkillLevels), FontSize = 11, Foreground = Res("Brush.TextMuted"), TextAlignment = TextAlignment.Center });
                }

                var card = Card(stack);
                card.Margin = new Thickness(0, 0, 10, 10);
                cards.Children.Add(card);
            }

            page.Children.Add(cards);
        }

        var table = new StackPanel();
        table.Children.Add(Heading(Loc("CpCalc.Attributes.Head")));
        var grid = new Grid();
        string[] heads = { "", Loc("CpCalc.Attributes.Total"), Loc("CpCalc.Tab.Gear"), Loc("CpCalc.Tab.Pets"), SourceName(CpSource.Arcana), Loc("CpCalc.Attributes.Other"), "CP" };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int k = 1; k < heads.Length; k++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(k == heads.Length - 1 ? 84 : 96) });
        }

        void Cell(int row, int column, string text, bool strong = false, bool muted = false)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = 12, Margin = new Thickness(0, 3, 0, 3), TextAlignment = column == 0 ? TextAlignment.Left : TextAlignment.Right,
                FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, Foreground = muted ? Res("Brush.TextMuted") : Res("Brush.Text"),
            };
            SetRow(tb, row);
            SetColumn(tb, column);
            grid.Children.Add(tb);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int k = 0; k < heads.Length; k++)
        {
            Cell(0, k, heads[k].ToUpper(CultureInfo.CurrentCulture), muted: true);
        }

        int r = 1;
        foreach (CpAttributeRow a in v.Whole.Attributes.OrderByDescending(x => x.Cp))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string N(double x) => x >= 0.5 ? Math.Round(x).ToString("N0", CultureInfo.CurrentCulture) : "–";
            Cell(r, 0, Aion2CpData.Name(a.Stat, Lang));
            Cell(r, 1, N(a.Total), strong: true);
            Cell(r, 2, N(a.FromGear), muted: true);
            Cell(r, 3, N(a.FromPets), muted: true);
            Cell(r, 4, N(a.FromArcana), muted: true);
            Cell(r, 5, N(a.Other), muted: true);
            Cell(r, 6, Number(a.Cp), strong: true);
            r++;
        }

        table.Children.Add(grid);
        table.Children.Add(Label(Loc("CpCalc.Attributes.Note"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 8, 0, 0)));
        page.Children.Add(Card(table));
        return page;
    }

    // ---------------------------------------------------------------- checked against the game

    private UIElement CheckTab(View v, CpModel model, Aion2EntityDirectory? directory)
    {
        var checks = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        checks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        checks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        checks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var checkCard = Card(CheckPanel(model, directory));
        checkCard.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(checkCard, 0);
        checks.Children.Add(checkCard);
        var unitCard = Card(PerUnitPanel(v.Totals, model, CharacterOf(directory)));
        unitCard.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(unitCard, 2);
        checks.Children.Add(unitCard);
        return checks;
    }

    /// <summary>
    /// The calculator against the game: the character's Combat Power as the game sent it at the last login, the changes measured on it (the game's change
    /// next to the calculator's), the factors that came of them, the check of this session's last change against the game's stat update, and a button to read
    /// the character page again.
    /// </summary>
    private UIElement CheckPanel(CpModel model, Aion2EntityDirectory? directory)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("CpCalc.Check")));
        if (_character is null)
        {
            return stack;
        }

        CpCheckState? state = CpChecks.State(_character, _server);
        CpCheckPoint? last = state?.Points.LastOrDefault();
        if (last is not null)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = last.Cp.ToString("N0", CultureInfo.CurrentCulture), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Accent") });
            line.Children.Add(Label(F("CpCalc.Check.Game", last.At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)), 12, brush: Res("Brush.TextMuted"), margin: new Thickness(10, 0, 0, 0)));
            stack.Children.Add(line);
        }
        else
        {
            // a legion list came at this login and doesn't name the character: it is in no legion (or in another one than the list's)
            bool listed = directory is null || directory.LegionRosterAt.AddMinutes(1) < directory.LocalCharacterSince
                || Aion2Moved.Find(directory.LegionRoster(), m => m.Name, m => m.ServerId, _character, _server) is not null;
            stack.Children.Add(Label(Loc(listed ? "CpCalc.Check.NoGame" : "CpCalc.Check.NotLegion"), 12.5, brush: Res("Brush.TextMuted")));
        }

        if (Official is { Cp: int pageCp } page)
        {
            string when = page.At is DateTime at ? at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "";
            stack.Children.Add(Label(F("CpCalc.Check.Page", pageCp.ToString("N0", CultureInfo.CurrentCulture), when), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 2, 0, 0)));
        }

        // the changes measured on this character
        IReadOnlyList<CpMeasuredChange> measured = CpChecks.MeasuredChanges(_character, _server, model);
        stack.Children.Add(new Border { Height = 1, Background = Res("Brush.Border"), Margin = new Thickness(0, 10, 0, 10) });
        if (measured.Count == 0)
        {
            stack.Children.Add(Label(Loc("CpCalc.Check.None"), 12.5, brush: Res("Brush.TextMuted")));
        }
        else
        {
            stack.Children.Add(Heading(F("CpCalc.Check.Measured", measured.Count)));
            foreach (CpMeasuredChange m in measured.Take(4))
            {
                string what = m.Changes.Count == 0 ? "" : string.Join(", ", m.Changes.Select(ChangeText));
                stack.Children.Add(Row(Signed(m.Actual), what + "  ·  " + F("CpCalc.Check.Calc", Signed(m.Predicted)), m.At.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
                    Res("Brush.Heal"), m.Changes.FirstOrDefault()?.ItemId));
                foreach (CpStatGap gap in m.Gaps)
                {
                    stack.Children.Add(Label("   " + GapText(gap), 11.5, brush: Res("Brush.Warning")));
                }
            }

            IReadOnlyDictionary<string, double> factors = CpChecks.Factors(_character, _server, model);
            if (factors.Count > 0)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var (key, factor) in factors.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    chips.Children.Add(Chip(F("CpCalc.Check.Factor", KeyName(key), factor.ToString("0.00", CultureInfo.CurrentCulture)), Green, new Thickness(0, 0, 6, 4)));
                }

                stack.Children.Add(chips);
                stack.Children.Add(Label(Loc("CpCalc.Check.FactorNote"), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 2, 0, 0)));
            }
        }

        // this session's last change against the game's stat update
        if (_pending is not null)
        {
            stack.Children.Add(Label(Loc("CpCalc.Check.Waiting"), 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 10, 0, 0)));
        }
        else if (_sessionCheck is { } check)
        {
            string what = string.Join(", ", check.Changes.Select(ChangeText));
            if (!check.Seen)
            {
                stack.Children.Add(Label(F("CpCalc.Check.Unseen", what), 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 10, 0, 0)));
            }
            else if (check.Gaps.Count == 0)
            {
                stack.Children.Add(Label(F("CpCalc.Check.TablesOk", what), 12, brush: Res("Brush.Heal"), margin: new Thickness(0, 10, 0, 0)));
            }
            else
            {
                stack.Children.Add(Label(F("CpCalc.Check.TablesOff", what), 12, brush: Res("Brush.Warning"), margin: new Thickness(0, 10, 0, 0)));
                foreach (CpStatGap gap in check.Gaps)
                {
                    stack.Children.Add(Label("   " + GapText(gap), 11.5, brush: Res("Brush.Warning")));
                }
            }
        }

        if (!PrivacyConsent.Restricted && !MainWindow.Headless)
        {
            var read = new Button { Content = Loc("CpCalc.ReadPage"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            read.IsEnabled = !CpChecks.Reading(_character, _server);
            string who = _character;
            int server = _server;
            read.Click += (_, _) =>
            {
                CpChecks.AskOfficial(who, server, fresh: true);
                Poll(force: true);
            };
            stack.Children.Add(read);
        }

        // sharing the measured changes, without a name, for everyone's calculator (Upload/CpShare.cs; off until switched on)
        if (!PrivacyConsent.Restricted)
        {
            var share = new CheckBox { IsChecked = MeterSettings.Load().ShareCpMeasurements, Margin = new Thickness(0, 12, 0, 0) };
            share.Content = new TextBlock { Text = Loc("CpCalc.Share"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Res("Brush.Text") };
            share.Click += (_, _) =>
            {
                bool on = share.IsChecked == true;
                if (on && !PrivacyConsent.EnsureForUploadSwitch(Window.GetWindow(this)))
                {
                    share.IsChecked = false;
                    on = false;
                }

                MeterSettings settings = MeterSettings.Load();
                settings.ShareCpMeasurements = on;
                settings.Save();
            };
            stack.Children.Add(share);
            string note = Loc("CpCalc.Share.Note");
            stack.Children.Add(Label(CpShare.Sent > 0 ? F("CpCalc.Share.Sent", CpShare.Sent) + "  ·  " + note : note, 11, brush: Res("Brush.TextMuted"), margin: new Thickness(22, 2, 0, 0)));
        }

        return stack;
    }

    private static string KeyName(string key) => KeyTokens.TryGetValue(key, out string? token) ? Aion2CpData.Name(token, Lang) : key;

    private static string GapText(CpStatGap gap)
    {
        string token = Aion2CpData.Token(gap.StatId) ?? "#" + gap.StatId;
        string Amount(long v) => (v >= 0 ? "+" : "−") + Value(token, Math.Abs(v));
        return F("CpCalc.Check.Gap", Aion2CpData.Name(token, Lang), Amount(gap.Game), Amount(gap.Tables));
    }

    /// <summary>What one more point (or 1 %) of each stat is worth for this character, with the stats measured on it marked.</summary>
    private static UIElement PerUnitPanel(CpTotals totals, CpModel model, CpCharacter character)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("CpCalc.PerUnit")));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        int r = 0;
        foreach (CpPerUnit u in CpCalc.PerUnit(totals, model))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string amount = Aion2CpData.Percent(u.Stat) ? "+1 %" : "+1";
            var name = new TextBlock { Text = amount + "  " + Aion2CpData.Name(u.Stat, Lang), FontSize = 12.5, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            SetRow(name, r);
            grid.Children.Add(name);
            if (character.Factors?.ContainsKey(u.Key) == true)
            {
                var chip = Chip(Loc("CpCalc.Measured"), Green, new Thickness(6, 0, 6, 0));
                SetRow(chip, r);
                SetColumn(chip, 1);
                grid.Children.Add(chip);
            }

            var cp = new TextBlock { Text = Number(u.Cp) + " CP", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            SetRow(cp, r);
            SetColumn(cp, 2);
            grid.Children.Add(cp);
            r++;
        }

        stack.Children.Add(grid);
        stack.Children.Add(Label(F("CpCalc.PerUnit.Note", (totals.AttackIncrease * 100).ToString("0.#", CultureInfo.CurrentCulture)), 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 8, 0, 0)));
        if (StrengthNote(totals, model) is { } strength)
        {
            stack.Children.Add(Label(strength, 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 4, 0, 0)));
        }

        return stack;
    }

    /// <summary>What the character's percentages count at: those of the attack with its offence, those of the defence with its guard (Aion2CpData.PercentFactor);
    /// null when the game's stat list isn't there to tell its strength.</summary>
    private static string? StrengthNote(CpTotals totals, CpModel model) =>
        totals.Offense > 0 && model.PercentRef > 0
            ? F("CpCalc.Strength", (Aion2CpData.PercentFactor(totals, model) * 100).ToString("0", CultureInfo.CurrentCulture), (Aion2CpData.GuardFactor(totals, model) * 100).ToString("0", CultureInfo.CurrentCulture))
            : null;

    private string StonesNote() =>
        Loc(_character is not null && CpChecks.Reading(_character, _server) ? "CpCalc.StonesLoading" : Official is not null ? "CpCalc.StonesExact" : "CpCalc.StonesRange");

    /// <summary>A worn piece: its picture with the enhancement in the corner, its name in its grade's colour, its CP, its grade and "BiS" when it is the best of its kind.</summary>
    private UIElement PieceCard(CpPiece piece, CpGrade? grade, CpBest? best, bool open)
    {
        Color itemColor = LootText.GradeColor(piece.ItemId);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Add(row, 0, IconTile(piece.ItemId, 54, LevelBadge(piece)));

        var text = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = SlotOf(piece.ItemId).ToUpper(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted") });
        text.Children.Add(new TextBlock { Text = LootText.ItemName(piece.ItemId), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Solid(itemColor), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 2) });
        var figures = new StackPanel { Orientation = Orientation.Horizontal };
        figures.Children.Add(new TextBlock { Text = Range(piece.CpMin, piece.CpMax, signed: false) + " CP", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center });
        if (best is { IsMine: true })
        {
            figures.Children.Add(Chip(Loc("CpCalc.Bis"), Gold, new Thickness(8, 0, 0, 0)));
        }

        text.Children.Add(figures);
        Add(row, 1, text);
        Add(row, 2, GradeBadge(grade, 36));

        Brush idle = Res("Brush.Control"), hover = Res("Brush.ControlHover");
        var card = new Border
        {
            Child = row, Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
            Background = open ? Tint(itemColor, 0x1C) : idle, BorderBrush = open ? Solid(itemColor) : Res("Brush.Border"), BorderThickness = new Thickness(open ? 1.5 : 1),
            ToolTip = LootText.ItemName(piece.ItemId),
        };
        if (!open)
        {
            card.MouseEnter += (_, _) => card.Background = hover;
            card.MouseLeave += (_, _) => card.Background = idle;
        }

        card.MouseLeftButtonUp += (_, _) =>
        {
            _selected = piece.Slot;
            Poll(force: true);
        };
        return card;
    }

    /// <summary>The open piece: its picture and name, its CP and grade, whether it is the best of its kind, its lines with their CP, the best lines it could
    /// roll and what raises it.</summary>
    private UIElement Detail(CpPiece piece, CpGrade? grade, CpBest? best, IReadOnlyList<CpStep> steps, CpTotals totals)
    {
        var stack = new StackPanel();
        Color itemColor = LootText.GradeColor(piece.ItemId);

        // the head: picture, name, slot and level, the levels as chips; the piece's CP and grade on the right
        var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Add(head, 0, IconTile(piece.ItemId, 72, null));
        var names = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = LootText.ItemName(piece.ItemId), FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Solid(itemColor), TextWrapping = TextWrapping.Wrap });
        string sub = SlotOf(piece.ItemId) + (piece.Data is { Level: > 0 } d ? " · " + F("CpCalc.Level", d.Level) : "");
        names.Children.Add(Label(sub, 12, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 2, 0, 6)));
        var chips = new WrapPanel();
        if (piece.Enchant > 0 || piece.Data is not null)
        {
            chips.Children.Add(Chip("+" + piece.Enchant + (piece.Data is { } data0 && Aion2CpData.MaxEnchant(data0.Group) > 0 ? " / " + Aion2CpData.MaxEnchant(data0.Group) : ""), Color.FromRgb(0x5B, 0xB8, 0xF0), new Thickness(0, 0, 6, 0)));
        }

        if (piece.Data is { } data1 && Aion2CpData.MaxExceed(data1.Group) > 0)
        {
            chips.Children.Add(Chip("◆" + piece.Exceed + " / " + Aion2CpData.MaxExceed(data1.Group) + "  " + Loc("CpCalc.Breakthrough"), piece.Exceed > 0 ? Gold : Grey, new Thickness(0, 0, 6, 0)));
        }

        names.Children.Add(chips);
        Add(head, 1, names);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var cpLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        cpLine.Children.Add(new TextBlock { Text = Range(piece.CpMin, piece.CpMax, signed: false), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Accent") });
        cpLine.Children.Add(new TextBlock { Text = " CP", FontSize = 13, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) });
        right.Children.Add(cpLine);
        if (grade is not null)
        {
            var g = GradeBadge(grade, 30);
            g.HorizontalAlignment = HorizontalAlignment.Right;
            g.Margin = new Thickness(0, 6, 0, 0);
            right.Children.Add(g);
        }

        Add(head, 2, right);
        stack.Children.Add(head);

        // best in slot
        if (best is not null)
        {
            stack.Children.Add(BisStrip(best));
        }

        // the lines, by where they come from
        foreach (CpLineKind kind in Enum.GetValues<CpLineKind>())
        {
            var lines = piece.Lines.Where(l => l.Kind == kind).OrderByDescending(l => l.CpMax ?? -1).ToList();
            if (lines.Count == 0 && !(kind == CpLineKind.Stone && piece.EmptyStones > 0))
            {
                continue;
            }

            string title = Loc("CpCalc.Kind." + kind) + kind switch
            {
                CpLineKind.Enhance => "  +" + piece.Enchant,
                CpLineKind.Breakthrough => "  ◆" + piece.Exceed,
                _ => "",
            };
            double sumLow = lines.Sum(l => l.CpMin ?? 0), sumHigh = lines.Sum(l => l.CpMax ?? 0);
            stack.Children.Add(SectionHead(title, Range(sumLow, sumHigh, signed: false), kind == CpLineKind.Soul ? grade : null));
            foreach (CpLine line in lines)
            {
                stack.Children.Add(LineRow(line));
            }

            if (kind == CpLineKind.Stone && piece.EmptyStones > 0)
            {
                stack.Children.Add(new Border { Padding = new Thickness(8, 4, 8, 4), Child = Label("○  " + F("CpCalc.EmptyStones", piece.EmptyStones), 12.5, brush: Res("Brush.Warning")) });
            }
        }

        // the lines worth most on this slot for the class, ticked where the piece has them (every piece, best in slot or not)
        if (IdealBlock(piece, totals) is { } idealBlock)
        {
            stack.Children.Add(idealBlock);
        }

        // what raises this piece
        stack.Children.Add(new Border { Height = 1, Background = Res("Brush.Border"), Margin = new Thickness(0, 12, 0, 10) });
        stack.Children.Add(Heading(Loc("CpCalc.ThisPiece")));
        if (steps.Count == 0)
        {
            stack.Children.Add(Label(Loc("CpCalc.PieceNothing"), 12.5, brush: Res("Brush.TextMuted")));
        }

        foreach (CpStep s in steps)
        {
            stack.Children.Add(StepRow(s));
        }

        stack.Children.Add(Label(Loc("CpCalc.LineNote"), 11, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 8, 0, 0)));
        return stack;
    }

    /// <summary>"Best in slot" in green when nothing beats the piece, else the best piece of its kind with its picture and the two pieces' CP at their best.</summary>
    private static UIElement BisStrip(CpBest best)
    {
        Color tone = best.IsMine ? Green : Gold;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        if (best.IsMine)
        {
            Add(grid, 0, new TextBlock { Text = "✓", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Solid(Green), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            text.Children.Add(new TextBlock { Text = Loc("CpCalc.BisYes"), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Solid(Green) });
            text.Children.Add(Label(Loc("CpCalc.BisYesTip"), 11.5, brush: Res("Brush.TextMuted")));
        }
        else
        {
            Add(grid, 0, IconTile(best.ItemId, 36, null));
            text.Children.Add(new TextBlock { Text = F("CpCalc.BisNo", LootText.ItemName(best.ItemId)), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Solid(LootText.GradeColor(best.ItemId)), TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Label(F("CpCalc.BisNoTip", Number(best.Cp), Number(best.Mine), Number(best.Cp - best.Mine)), 11.5, brush: Res("Brush.TextMuted")));
        }

        Add(grid, 1, text);
        return new Border { Child = grid, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(8), Background = Tint(tone, 0x18), BorderBrush = Tint(tone, 0x66), BorderThickness = new Thickness(1) };
    }

    /// <summary>A section of the open piece: where its lines come from, their CP together, the grade for the soul binding.</summary>
    /// <summary>The character's class (Templar, Chanter ...) for the ideal lines; null before the game named it.</summary>
    private string? ClassName() => _directory()?.LocalCharacter is { } me ? Aion2SkillNames.ClassFromCode(me.ClassCode) : null;

    /// <summary>
    /// The six lines worth most on the piece's slot for the class (bracelets five), best first: each with its top value and its worth against the best line, ticked
    /// (with the piece's own value) where the piece has it - its skill lines too. A class without a list yet: the six lines of the piece's own pool worth most CP.
    /// </summary>
    private UIElement? IdealBlock(CpPiece piece, CpTotals totals)
    {
        string? cls = ClassName();
        IReadOnlyList<IdealLine>? ideal = Aion2IdealLines.For(cls, piece.ItemId);
        var souls = piece.Lines.Where(l => l.Kind == CpLineKind.Soul).ToList();
        var rows = new List<(string Name, string Top, string? Mine, bool Have, double Worth, string Right)>();
        if (ideal is { Count: > 0 })
        {
            foreach (IdealLine l in ideal)
            {
                CpLine? mine = souls.FirstOrDefault(x => Aion2IdealLines.Matches(l, x.Stat, x.SkillId));
                string name = l.IsSkill ? DisplaySkill(l.Skill!, piece) : Aion2CpData.Name(l.Stat!, Lang);
                string top = l.IsSkill ? "+" + l.Top : Value(l.Stat!, l.Top);
                string? yours = mine is null ? null : mine.IsSkill ? "+" + mine.Value.ToString("0", CultureInfo.CurrentCulture) : Value(mine.Stat, mine.Value);
                rows.Add((name, top, yours, mine is not null, l.Worth / 100.0, l.Worth + " %"));
            }
        }
        else if (piece.Data is { } data && CpCalc.BestLinesOf(data, 6, totals) is { Count: > 0 } byCp)
        {
            double topCp = byCp[0].Cp;
            foreach (CpIdealLine l in byCp)
            {
                CpLine? mine = souls.FirstOrDefault(x => x.IsSoulStat && x.Stat == l.Stat);
                rows.Add((Aion2CpData.Name(l.Stat, Lang), Value(l.Stat, l.Max), mine is null ? null : Value(mine.Stat, mine.Value), mine is not null,
                    topCp > 0 ? l.Cp / topCp : 0, Number(l.Cp) + " CP"));
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var block = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var head = new Grid { Margin = new Thickness(0, 10, 0, 4) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string title = ideal is { Count: > 0 } ? F("CpCalc.Ideal.Title", cls, SlotOf(piece.ItemId)) : Loc("CpCalc.Ideal.ByCp");
        Add(head, 0, new TextBlock
        {
            Text = title.ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Loc(ideal is { Count: > 0 } ? "CpCalc.Ideal.Tip" : "CpCalc.Ideal.ByCpTip"),
        });
        bool read = souls.Count > 0;
        Add(head, 1, new TextBlock
        {
            Text = read ? F("CpCalc.Ideal.Have", rows.Count(r => r.Have), rows.Count) : Loc("CpCalc.Ideal.NotRead"), FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center,
        });
        block.Children.Add(head);
        foreach (var r in rows)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            Add(row, 0, new TextBlock { Text = r.Have ? "✓" : "○", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = r.Have ? Solid(Green) : Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = r.Name, FontSize = 12.5, Foreground = r.Have ? Res("Brush.Text") : Res("Brush.TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis });
            name.Children.Add(new TextBlock { Text = "  " + r.Top, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = r.Have ? Res("Brush.Text") : Res("Brush.TextMuted") });
            if (r.Mine is not null)
            {
                name.Children.Add(new TextBlock { Text = "   " + F("CpCalc.Ideal.Yours", r.Mine), FontSize = 11.5, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
            }

            Add(row, 1, name);
            var track = new Grid { Height = 5, Width = 100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Res("Brush.Border") });
            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, 100 * Math.Clamp(r.Worth, 0, 1)) };
            fill.Background = r.Have ? Res("Brush.Accent") : Tint(Grey, 0x99);
            track.Children.Add(fill);
            Add(row, 2, track);
            Add(row, 3, new TextBlock { Text = r.Right, FontSize = 11.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Brush.TextMuted") });
            block.Children.Add(new Border { Child = row, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(5), Background = Res("Brush.RowAlt"), Margin = new Thickness(0, 0, 0, 2) });
        }

        return block;
    }

    /// <summary>How many of the class's ideal lines a piece has, and of how many; null without a list for the class and slot, or lines the game sent.</summary>
    private static (int Have, int Of)? IdealCount(CpPiece piece, string? cls)
    {
        var souls = piece.Lines.Where(l => l.Kind == CpLineKind.Soul).ToList();
        if (souls.Count == 0 || Aion2IdealLines.For(cls, piece.ItemId) is not { Count: > 0 } ideal)
        {
            return null;
        }

        return (ideal.Count(i => souls.Any(x => Aion2IdealLines.Matches(i, x.Stat, x.SkillId))), ideal.Count);
    }

    /// <summary>A skill of an ideal line by its English name, in the page's language (the piece's own skill line when it has that skill, else the class's skill).</summary>
    private static string DisplaySkill(string english, CpPiece piece)
    {
        CpLine? own = piece.Lines.FirstOrDefault(l => l.IsSkill && string.Equals(Aion2SkillNames.NameOf(l.SkillId), english, StringComparison.OrdinalIgnoreCase));
        return own is not null ? Aion2SkillNames.DisplayOf(own.SkillId) : english;
    }

    private static UIElement SectionHead(string title, string cp, CpGrade? grade)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock { Text = title.ToUpper(CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        if (grade is not null)
        {
            left.Children.Add(Chip(grade.Letter + "  " + F("CpCalc.Score", Math.Round(grade.Score)), LetterColors[grade.Letter], new Thickness(8, 0, 0, 0)));
        }

        Add(row, 0, left);
        Add(row, 1, new TextBlock { Text = cp + " CP", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>One line: the stat and its value; a soul-binding roll with a bar showing how close it is to the top this piece can roll; a stone's colour and
    /// whether its value is exact; the CP on the right.</summary>
    private static UIElement LineRow(CpLine line)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        if (line.IsSkill)
        {
            // a skill level of the soul binding: one of the piece's lines; its CP is counted with the skill (Skills), shown here, not added to the piece again
            var skill = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            skill.Children.Add(new TextBlock { Text = Aion2SkillNames.DisplayOf(line.SkillId), FontSize = 12.5, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center });
            skill.Children.Add(new TextBlock { Text = "  +" + line.Value.ToString("0", CultureInfo.CurrentCulture), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center });
            Add(row, 0, skill);
            Add(row, 1, Label(Loc("CpCalc.SkillLine"), 11.5, brush: Res("Brush.TextMuted")));
            Add(row, 2, new TextBlock
            {
                Text = line.SkillCp is > 0 ? F("CpCalc.InSkills", Number(line.SkillCp.Value)) : Loc("CpCalc.NotInCp"), FontSize = 12, TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Brush.TextMuted"),
            });
            return new Border { Child = row, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(5), Background = Res("Brush.RowAlt"), Margin = new Thickness(0, 0, 0, 2), ToolTip = Loc("CpCalc.SkillLineTip") };
        }

        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (line.Kind == CpLineKind.Stone)
        {
            name.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = Solid(TierColors[Math.Clamp(line.Tier, 0, 4)]), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc("CpCalc.Tier" + Math.Clamp(line.Tier, 1, 4)) });
        }

        string value = line.ValueMax > line.Value ? Value(line.Stat, line.Value) + "–" + Value(line.Stat, line.ValueMax) : Value(line.Stat, line.Value);
        name.Children.Add(new TextBlock { Text = Aion2CpData.Name(line.Stat, Lang), FontSize = 12.5, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center });
        name.Children.Add(new TextBlock { Text = "  +" + value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center });
        Add(row, 0, name);

        if (line.Kind == CpLineKind.Soul && line.PoolMax is int topValue && topValue > 0)
        {
            Add(row, 1, RollBar(line.Value, line.PoolMin ?? 0, topValue, line.Stat));
        }
        else if (line.Kind == CpLineKind.Stone)
        {
            Add(row, 1, Label(line.ValueMax > line.Value ? Loc("CpCalc.Tier" + Math.Clamp(line.Tier, 1, 4)) : Loc("CpCalc.Exact"), 11.5, brush: Res("Brush.TextMuted")));
        }

        Add(row, 2, new TextBlock
        {
            Text = line.CpMax is null ? Loc("CpCalc.NotInCp") : Range(line.CpMin ?? 0, line.CpMax ?? 0, signed: false), FontSize = 12.5, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Foreground = line.CpMax is null ? Res("Brush.TextMuted") : Res("Brush.Text"), FontWeight = line.CpMax is null ? FontWeights.Normal : FontWeights.SemiBold,
        });
        return new Border { Child = row, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(5), Background = Res("Brush.RowAlt"), Margin = new Thickness(0, 0, 0, 2) };
    }

    /// <summary>A stat of a source with its value and CP (the pets, Daevanion, titles, the wing).</summary>
    private static UIElement StatRow(CpSourceLine l)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = Aion2CpData.Name(l.Stat, Lang), FontSize = 12, Foreground = Res("Brush.Text"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200 });
        name.Children.Add(new TextBlock { Text = "  +" + Value(l.Stat, l.Value), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text") });
        Add(row, 0, name);
        Add(row, 1, new TextBlock { Text = l.Cp is double cp ? Number(cp) : Loc("CpCalc.NotInCp"), FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = l.Cp is null ? Res("Brush.TextMuted") : Res("Brush.Text"), FontWeight = l.Cp is null ? FontWeights.Normal : FontWeights.SemiBold });
        return new Border { Child = row, Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(5), Background = Res("Brush.RowAlt"), Margin = new Thickness(0, 0, 0, 2) };
    }

    /// <summary>How close a roll is to the top: a bar from the lowest to the highest value this piece can roll, and "92 % · max 12.98 %".</summary>
    private static UIElement RollBar(double value, int min, int max, string stat)
    {
        double share = max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : value >= max ? 1 : 0;
        Color c = share >= 0.85 ? Green : share >= 0.5 ? Gold : Color.FromRgb(0xF0, 0x7A, 0x20);
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0), ToolTip = F("CpCalc.CanRoll", Value(stat, max)) };
        var track = new Grid { Height = 5 };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Res("Brush.Border") });
        track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Solid(c), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, 120 * share) });
        track.Width = 120;
        track.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(track);
        panel.Children.Add(new TextBlock { Text = F("CpCalc.OfMax", Math.Round(value / Math.Max(1, max) * 100), Value(stat, max)), FontSize = 10.5, Foreground = Res("Brush.TextMuted"), Margin = new Thickness(0, 2, 0, 0) });
        return panel;
    }

    /// <summary>A level as a bar (a Stigma to its top, a board's nodes, a pet's souls) with its text under it.</summary>
    private static UIElement LevelBar(double value, double max, Color color, string text)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        double share = max > 0 ? Math.Clamp(value / max, 0, 1) : 0;
        var track = new Grid { Height = 6, MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Stretch };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, share), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - share), GridUnitType.Star) });
        var fill = new Border { CornerRadius = new CornerRadius(3, 0, 0, 3), Background = Solid(color) };
        var rest = new Border { CornerRadius = new CornerRadius(0, 3, 3, 0), Background = Res("Brush.Border") };
        SetColumn(rest, 1);
        track.Children.Add(fill);
        track.Children.Add(rest);
        panel.Children.Add(track);
        panel.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = Res("Brush.TextMuted"), Margin = new Thickness(0, 2, 0, 0) });
        return panel;
    }

    /// <summary>A part's CP large on the right of a card.</summary>
    private static UIElement BigCp(double cp)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new TextBlock { Text = Number(cp), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Accent") });
        line.Children.Add(new TextBlock { Text = " CP", FontSize = 11.5, Foreground = Res("Brush.TextMuted"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
        return line;
    }

    // ------------------------------------------------------------------ pictures

    private static string LevelBadge(CpPiece piece) => piece.Exceed > 0 ? $"+{piece.Enchant} ◆{piece.Exceed}" : piece.Enchant > 0 ? "+" + piece.Enchant : "";

    /// <summary>The item's picture from the game over a backdrop in its grade's colour, with the levels in the corner.</summary>
    private static Border IconTile(int itemId, double size, string? badge)
    {
        Color color = LootText.GradeColor(itemId);
        var backdrop = new RadialGradientBrush { GradientOrigin = new Point(0.3, 0.25), Center = new Point(0.3, 0.25), RadiusX = 1, RadiusY = 1 };
        backdrop.GradientStops.Add(new GradientStop(Blend(color, Color.FromRgb(0x20, 0x17, 0x0A), 0.55), 0));
        backdrop.GradientStops.Add(new GradientStop(Blend(color, Color.FromRgb(0x0C, 0x0A, 0x08), 0.22), 1));
        var inner = new Grid();
        if (CraftText.Icon(itemId) is { } image)
        {
            var picture = new Image { Source = image, Stretch = Stretch.UniformToFill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            inner.Children.Add(picture);
        }
        else
        {
            inner.Children.Add(new TextBlock { Text = "◆", FontSize = size * 0.4, Foreground = Solid(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }

        if (!string.IsNullOrEmpty(badge))
        {
            inner.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), CornerRadius = new CornerRadius(4), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 2, 2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = badge, FontSize = size >= 50 ? 11 : 10, FontWeight = FontWeights.SemiBold, Foreground = badge.Contains('◆') ? Solid(Color.FromRgb(0xFF, 0xD6, 0x6B)) : Brushes.White },
            });
        }

        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size >= 50 ? 9 : 6), BorderThickness = new Thickness(size >= 50 ? 2 : 1.5), BorderBrush = Solid(color),
            Background = backdrop, ClipToBounds = true, Child = inner, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>A skill's picture from the game in a frame of the given colour; its first letter when the meter has no picture.</summary>
    private static Border SkillIcon(int skillId, double size, Color color)
    {
        UIElement inner = Picture(Aion2SkillIcons.PathFor(skillId)) is { } image
            ? new Image { Source = image, Stretch = Stretch.UniformToFill, SnapsToDevicePixels = true }
            : new TextBlock { Text = Aion2SkillNames.DisplayOf(skillId) is { Length: > 0 } n ? n[..1] : "?", FontSize = size * 0.45, FontWeight = FontWeights.SemiBold, Foreground = Solid(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size >= 40 ? 9 : 5), BorderThickness = new Thickness(1.5), BorderBrush = Solid(color),
            Background = Tint(color, 0x22), ClipToBounds = true, Child = inner, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>A picture (a wing, a pet) in a round frame of the given colour; a dot of that colour without one.</summary>
    private static FrameworkElement SmallPicture(string? path, double size, Color color) => Picture(path) is { } image
        ? new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1.5), BorderBrush = Solid(color), Background = Tint(color, 0x22),
            ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Child = new Image { Source = image, Stretch = Stretch.UniformToFill },
        }
        : Dot(color, size);

    private static FrameworkElement Dot(Color color, double size) => new Border
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Tint(color, 0x2A), BorderBrush = Solid(color), BorderThickness = new Thickness(1.5),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Ellipse { Width = size * 0.34, Height = size * 0.34, Fill = Solid(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private static Color Blend(Color top, Color bottom, double amount) => Color.FromRgb(
        (byte)(bottom.R + (top.R - bottom.R) * amount), (byte)(bottom.G + (top.G - bottom.G) * amount), (byte)(bottom.B + (top.B - bottom.B) * amount));

    /// <summary>The colour of an item grade's name (Common white ... Unique gold, Epic orange).</summary>
    private static Color GradeTone(string? grade) => grade switch
    {
        "Rare" => Color.FromRgb(0x5B, 0xD3, 0x6B), "Legend" => Color.FromRgb(0x4A, 0xA8, 0xFF), "Unique" => Gold, "Epic" => Color.FromRgb(0xFF, 0x8A, 0x3D),
        _ => Color.FromRgb(0xD8, 0xDD, 0xE3),
    };

    /// <summary>A grade as a square in its colour (S+, S, A …); a dash when the piece has no soul binding.</summary>
    private static Border GradeBadge(CpGrade? grade, double size)
    {
        Color c = grade is null ? Color.FromRgb(0x5A, 0x63, 0x6E) : LetterColors[grade.Letter];
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 4), Background = Tint(c, 0x26), BorderBrush = Tint(c, 0xAA), BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Center, ToolTip = grade is null ? Loc("CpCalc.NoGrade") : Loc("CpCalc.Grade") + ": " + F("CpCalc.Score", Math.Round(grade.Score)),
            Child = new TextBlock { Text = grade?.Letter ?? "—", FontSize = size * (grade?.Letter.Length > 1 ? 0.38 : 0.46), FontWeight = FontWeights.Bold, Foreground = Solid(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }

    private static Border Chip(string text, Color color, Thickness margin, string? tip = null) => new()
    {
        Background = Tint(color, 0x22), BorderBrush = Tint(color, 0x88), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 2),
        Margin = margin, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = tip,
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Solid(color) },
    };

    // ------------------------------------------------------------------ text

    private static string SlotOf(int itemId) => Aion2ItemCatalog.Find(itemId) is { } info ? Loc("Details.Seen.Cat." + info.Slot) : "";

    private static string ChangeText(CpChange c) => c.Kind switch
    {
        CpChangeKind.Equipped when c.OldItemId != 0 => F("CpCalc.C.Replaced", SlotOf(c.ItemId), LootText.ItemName(c.ItemId), LootText.ItemName(c.OldItemId)),
        CpChangeKind.Equipped => F("CpCalc.C.Equipped", SlotOf(c.ItemId), LootText.ItemName(c.ItemId)),
        CpChangeKind.Enhanced => F("CpCalc.C.Enhanced", SlotOf(c.ItemId), c.From, c.To),
        CpChangeKind.SoulBound => F("CpCalc.C.Soul", SlotOf(c.ItemId)),
        CpChangeKind.Stones => F("CpCalc.C.Stones", SlotOf(c.ItemId)),
        CpChangeKind.Breakthrough => F("CpCalc.C.Breakthrough", SlotOf(c.ItemId), c.From, c.To),
        _ => "",
    };

    private static string Value(string stat, double v) => Aion2CpData.Percent(stat)
        ? (v / 100).ToString("0.##", CultureInfo.CurrentCulture) + " %"
        : v.ToString("N0", CultureInfo.CurrentCulture);

    private static string Number(double v) => Math.Abs(v) >= 100 ? Math.Round(v).ToString("N0", CultureInfo.CurrentCulture) : v.ToString("0.#", CultureInfo.CurrentCulture);

    /// <summary>A large number short: 86.1K, 950.</summary>
    private static string Short(double v) => Math.Abs(v) >= 10_000 ? (v / 1000).ToString("0.#", CultureInfo.CurrentCulture) + "K" : Number(v);

    private static string Signed(double v) => (v >= 0 ? "+" : "−") + Number(Math.Abs(v));

    private static string Range(double low, double high, bool signed)
    {
        string a = signed ? Signed(low) : Number(low);
        return Math.Abs(high - low) < 0.5 ? a : a + "–" + Number(high);
    }

    // ------------------------------------------------------------------ building blocks

    private static TextBlock Label(string text, double size = 13, FontWeight? weight = null, Brush? brush = null, Thickness? margin = null) =>
        new() { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = brush ?? Res("Brush.Text"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = margin ?? new Thickness(0) };

    /// <summary>A card's title: a short gold stroke and the title (the Journey page's way).</summary>
    private static FrameworkElement Heading(string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        line.Children.Add(new Border { Width = 3, Height = 14, CornerRadius = new CornerRadius(2), Background = Solid(Gold), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Text"), VerticalAlignment = VerticalAlignment.Center });
        return line;
    }

    private static Border Card(UIElement child) =>
        new() { Background = Res("Brush.Panel"), BorderBrush = Res("Brush.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 16), Child = child };

    /// <summary>A line of the lists: the figure on the left (a CP gain or change), the piece's small picture, the text, a small note on the right.</summary>
    private static UIElement Row(string figure, string text, string? note, Brush figureBrush, int? itemId)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Add(row, 0, new TextBlock { Text = figure, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = figureBrush, VerticalAlignment = VerticalAlignment.Center });
        if (itemId is int id && id > 0)
        {
            var icon = IconTile(id, 22, null);
            icon.Margin = new Thickness(0, 0, 8, 0);
            Add(row, 1, icon);
        }

        Add(row, 2, Label(text, 12.5));
        if (note is not null)
        {
            Add(row, 3, Label(note, 11, brush: Res("Brush.TextMuted"), margin: new Thickness(8, 0, 0, 0)));
        }

        return row;
    }

    private static void Add(Grid grid, int column, UIElement child)
    {
        SetColumn(child, column);
        grid.Children.Add(child);
    }
}
