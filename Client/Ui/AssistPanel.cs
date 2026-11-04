using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>What an assist overlay shows (see <see cref="AssistPanel.Part"/>).</summary>
public enum AssistPart { All, Cooldowns, Effects }

/// <summary>
/// The content of the assist: the local player's skill cooldowns (frame 0x4738, see <see cref="Aion2Cooldowns"/>) and the effects running on the
/// character (frames 0x2a38 / 0x2b38 / 0x2c38, see <see cref="Aion2CombatState.BuffsOn"/>).
/// <para>The overlays (4 November 2026, rebuilt from rows sorted by time left): a grid of skill pictures, each skill in its own place, as a skill bar shows them -
/// ready: the picture as it is; cooling down: darker, with a sweep that uncovers it clockwise and the whole seconds left in the middle (a countdown that only
/// goes down, <see cref="Aion2Cooldowns.View"/>). The effects: their pictures in the order they came, the time left under each. One look, light on dark, no
/// colours of its own.</para>
/// <para>The window: the same pictures in a list with the names and a box per skill or effect (ticked: the overlay shows it). Its rows are built once and
/// only updated, so a box can be clicked while the times run. Basic attacks (every class), chain skills and passives are never listed.</para>
/// </summary>
public sealed class AssistPanel : StackPanel
{
    /// <summary>
    /// An effect the game puts on everybody in a place, not on the player for something he did: the Abyss's own zone states ("Middle Abyss Reshanta"), the
    /// artifacts' bonus on the whole faction ("Lower Reshanta Artifact I"), its attunement and fighting-will levels, a profile frame, the test states left
    /// in the game's data ("For testing ConvertDamageToHpHeal_..."). By the English name (<paramref name="english"/>) for the known ones; and any effect
    /// that is no class skill (id below 10,000,000), has no picture of its own and no end (<paramref name="endless"/>) - a zone's or the server's state,
    /// as the open world and the Abyss put them on. A class skill's standing effect (a stance) and an item's effect (a scroll, food) have their pictures.
    /// </summary>
    public static bool IsWorldEffect(int skillId, string english, bool endless)
    {
        if (Aion2Artwork.IsWorldEffectName(english))
        {
            return true;
        }

        return endless && skillId < FirstSkillId && Aion2SkillIcons.PathFor(skillId) is null && Aion2Artwork.EffectIconPath(skillId) is null;
    }

    /// <summary>Skill ids from here are real skills; the small ids are shared cooldown groups.</summary>
    private const int FirstSkillId = 10_000_000;

    /// <summary>The size of a skill's picture in the overlay and in the window's list.</summary>
    private const double OverlayTile = 40, OverlayEffectTile = 34, ListTile = 30;

    private static readonly Brush OverlayText = Brushes.White;
    private static readonly Brush OverlayMuted = Frozen(Color.FromRgb(0xB8, 0xBE, 0xC4));

    /// <summary>The thin edge around a picture.</summary>
    private static readonly Brush TileEdge = Frozen(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));

    /// <summary>A cooling skill's picture is darkened all over, the part of the cooldown still to go darker still.</summary>
    private static readonly Brush Shade = Frozen(Color.FromArgb(0x47, 0x00, 0x00, 0x00));
    private static readonly Brush Sweep = Frozen(Color.FromArgb(0x9E, 0x00, 0x00, 0x00));

    /// <summary>The outline that keeps the white numbers readable on any picture.</summary>
    private static readonly Effect Outline = FrozenOutline();

    private readonly bool _overlay;
    private readonly Dictionary<string, BitmapImage?> _images = new();

    /// <summary>How long an effect that is gone stays (so a renewed one does not vanish and come back in another place).</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(2.5);

    private sealed class EffectRow(int order)
    {
        public int Order { get; } = order;
        public DateTime LastActive { get; set; }
        public BuffNotice? Buff { get; set; }

        /// <summary>How often the end of the effect moved forward while it was still running (a renewal).</summary>
        public int Renewals { get; set; }

        /// <summary>A short effect that is renewed again and again - a Chanter's mantra lasts 2.4 s and is renewed every second for as long as the Chanter keeps it up. In effect permanent.</summary>
        public bool Aura { get; set; }
    }

    /// <summary>The effects are listed by time left (renewed auras last) instead of by first appearance.</summary>
    public bool SortByTime { get; set; }

    /// <summary>What an overlay shows: the cooldowns or the effects on the player (no "chain skill ready" list: the owner found it of no use, for every class) - two overlays of their own, each
    /// placed on its own (one box with both was hard to read). The window shows both, in cards of their own.</summary>
    public AssistPart Part { get; set; } = AssistPart.All;

    /// <summary>The overlay shows the ready skills too, each in its place (<see cref="MeterSettings.AssistShowReady"/>); off: only the ones cooling down.</summary>
    public bool ShowReady { get; set; } = true;

    private readonly Dictionary<int, EffectRow> _effects = new();
    private int _order;

    public AssistPanel(bool overlay)
    {
        _overlay = overlay;
    }

    /// <summary>Skill ids the player does not want in the overlay (see <see cref="MeterSettings.AssistOverlayHidden"/>). The overlay leaves them out; the window lists them with an unticked box.</summary>
    public HashSet<int> Hidden { get; set; } = new();

    /// <summary>
    /// Whether a skill is listed (the overlay and the window): a skill the game has shown with a cooldown of more than 2 s - never a basic attack (Rending Blow,
    /// Keen Strike, Vicious Strike ... of every class, and those learned to have no cooldown, <see cref="Aion2Cooldowns.NotWorth"/>), a chain skill or a passive.
    /// A skill not used yet shows up with its first cooldown.
    /// </summary>
    public static bool Listed(int skillId) =>
        skillId >= FirstSkillId && !Aion2Artwork.IsPassive(skillId) && Aion2Cooldowns.NotWorth(skillId) is null && Aion2Cooldowns.IsKnown(skillId);

    /// <summary>The window's box of a skill or effect was clicked: the id and whether the overlay shows it now.</summary>
    public Action<int, bool>? ShowInOverlayChanged { get; set; }

    /// <summary>Whether the last <see cref="Refresh"/> had something to show (a skill or an effect).</summary>
    public bool HasContent { get; private set; }

    // ---------- what is built once ----------
    private readonly Dictionary<int, SkillTile> _skillTiles = new();
    private readonly Dictionary<int, EffectTile> _effectTiles = new();
    private readonly Dictionary<int, ListRow> _skillRows = new();
    private readonly Dictionary<int, ListRow> _effectRows = new();
    private readonly WrapPanel _skillGrid = new();
    private readonly WrapPanel _effectGrid = new();
    private readonly StackPanel _skillList = new();
    private readonly StackPanel _effectList = new();
    private TextBlock? _noSkills;
    private TextBlock? _noEffects;
    private TextBlock? _placeholder;
    private bool _built;

    /// <summary>What the panel holds now, by id and group: rebuilt only when this changes.</summary>
    private string _skillLayout = "", _effectLayout = "";

    public void Refresh(Aion2EntityDirectory? dir)
    {
        DateTime now = DateTime.Now; // the capture stamps its frames with the local clock
        if (!_built)
        {
            Build();
        }

        bool skills = Part != AssistPart.Effects, effects = Part != AssistPart.Cooldowns;
        int skillCount = skills ? RefreshSkills(now) : 0;
        int effectCount = effects ? RefreshEffects(dir, now) : 0;
        HasContent = skillCount + effectCount > 0;
        if (_overlay && _placeholder is not null)
        {
            // nothing to show (seen only while it is unlocked, to be placed): its name says which of the two overlays it is
            _placeholder.Visibility = HasContent ? Visibility.Collapsed : Visibility.Visible;
            _skillGrid.Visibility = skillCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            _effectGrid.Visibility = effectCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            _effectGrid.Margin = new Thickness(0, skillCount > 0 && effectCount > 0 ? 6 : 0, 0, 0);
        }
    }

    private void Build()
    {
        _built = true;
        LocalizationManager loc = LocalizationManager.Instance;
        bool skills = Part != AssistPart.Effects, effects = Part != AssistPart.Cooldowns;
        if (_overlay)
        {
            _placeholder = Label(Part == AssistPart.Effects ? loc["Assist.Buffs"] : loc["Assist.Cooldowns"], "Brush.Text", 11, bold: true);
            Children.Add(_placeholder);
            if (skills)
            {
                Children.Add(_skillGrid);
            }

            if (effects)
            {
                Children.Add(_effectGrid);
            }

            return;
        }

        if (skills)
        {
            var card = new StackPanel();
            card.Children.Add(Heading(loc["Assist.Cooldowns"]));
            _noSkills = Label(loc["Assist.NoCooldowns"], "Brush.TextMuted", 12);
            _noSkills.TextWrapping = TextWrapping.Wrap;
            card.Children.Add(_noSkills);
            card.Children.Add(_skillList);
            Children.Add(Card(card));
            var note = Label(loc["Assist.Note"], "Brush.TextSubtle", 11);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(2, 0, 2, 0);
            if (!effects)
            {
                Children.Add(note);
            }
        }

        if (effects)
        {
            var card = new StackPanel();
            card.Children.Add(Heading(loc["Assist.Buffs"]));
            _noEffects = Label(loc["Assist.NoBuffs"], "Brush.TextMuted", 12);
            card.Children.Add(_noEffects);
            card.Children.Add(_effectList);
            Children.Add(Card(card));
        }
    }

    // ---------- cooldowns ----------

    /// <summary>Updates the skills; the count of those shown (the overlay) or listed (the window).</summary>
    private int RefreshSkills(DateTime now)
    {
        // every skill in its place, in the game's order: the ones seen with a cooldown; basic attacks, chain skills and passives never
        var main = new List<int>();
        foreach (int id in Aion2Cooldowns.Skills())
        {
            if (Listed(id) && (!_overlay || !Hidden.Contains(id)))
            {
                main.Add(id);
            }
        }

        if (_overlay)
        {
            var shown = new List<(int Id, CooldownView View)>();
            foreach (int id in main)
            {
                CooldownView view = Aion2Cooldowns.View(id, now);
                if (view.Running || ShowReady)
                {
                    shown.Add((id, view));
                }
            }

            string layout = string.Join(",", shown.Select(s => s.Id));
            if (layout != _skillLayout)
            {
                _skillLayout = layout;
                _skillGrid.Children.Clear();
                foreach ((int id, _) in shown)
                {
                    _skillGrid.Children.Add(SkillTileOf(id, OverlayTile));
                }
            }

            foreach ((int id, CooldownView view) in shown)
            {
                _skillTiles[id].Show(view);
            }

            return shown.Count;
        }

        // the window: the list in the same order (an unticked skill stays in its place, faded)
        LocalizationManager loc = LocalizationManager.Instance;
        string windowLayout = string.Join(",", main.Select(i => Hidden.Contains(i) ? "-" + i : i.ToString(CultureInfo.InvariantCulture)));
        if (windowLayout != _skillLayout)
        {
            _skillLayout = windowLayout;
            _skillList.Children.Clear();
            foreach (int id in main)
            {
                ListRow row = SkillRowOf(id);
                row.Box.IsChecked = !Hidden.Contains(id);
                row.Fade();
                _skillList.Children.Add(row);
            }

            if (_noSkills is not null)
            {
                _noSkills.Visibility = main.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        foreach (int id in main)
        {
            CooldownView view = Aion2Cooldowns.View(id, now);
            ListRow row = _skillRows[id];
            row.Tile.Show(view);
            row.SetStatus(view.Running ? (view.WholeSeconds >= 60 ? CooldownText(view.WholeSeconds) : view.WholeSeconds.ToString(CultureInfo.CurrentCulture) + " s") : loc["Assist.Ready"], view.Running);
        }

        return main.Count;
    }

    private SkillTile SkillTileOf(int id, double size)
    {
        if (!_skillTiles.TryGetValue(id, out SkillTile? tile))
        {
            _skillTiles[id] = tile = new SkillTile(IconOf(id, Aion2SkillNames.DisplayOf(id)), size) { Margin = new Thickness(0, 0, 4, 4) };
        }

        return tile;
    }

    private ListRow SkillRowOf(int id)
    {
        if (!_skillRows.TryGetValue(id, out ListRow? row))
        {
            string name = Aion2SkillNames.DisplayOf(id);
            var tile = new SkillTile(IconOf(id, name), ListTile);
            _skillRows[id] = row = new ListRow(id, tile, tile, name, LocalizationManager.Instance["Assist.ShowInList"]);
            row.Box.Click += (_, _) =>
            {
                bool on = row.Box.IsChecked == true;
                if (on)
                {
                    Hidden.Remove(id);
                }
                else
                {
                    Hidden.Add(id);
                }

                row.Fade();
                ShowInOverlayChanged?.Invoke(id, on);
            };
        }

        return row;
    }

    /// <summary>Whole seconds as the tiles show them: 75 as 1:15.</summary>
    private static string CooldownText(int whole) =>
        whole >= 60 ? $"{whole / 60}:{whole % 60:00}" : whole.ToString(CultureInfo.CurrentCulture);

    // ---------- effects ----------

    /// <summary>Updates the effects on the player; the count shown.</summary>
    private int RefreshEffects(Aion2EntityDirectory? dir, DateTime now)
    {
        var shown = new List<(int Id, string Time, bool Active)>();
        if (dir is not null && dir.LocalPlayerId is >= 0 and int self)
        {
            // one entry per effect: a short effect that is renewed all the time (the Chanter's mantras) gets a new instance or a new end every few
            // seconds, so they are kept in the order they first appeared and stay a moment after their end instead of vanishing and coming back
            foreach (var group in dir.Combat.BuffsOn(self, now).GroupBy(b => b.SkillId))
            {
                if (_overlay && Hidden.Contains(group.Key))
                {
                    continue; // unticked in the assist window
                }

                if (Aion2Artwork.IsPassiveEffect(group.Key))
                {
                    continue; // a passive skill's standing effect (Identify Weakness, weapon training ...) is not something put on you
                }

                string english = Aion2SkillNames.NameOf(group.Key);
                if (IsWorldEffect(group.Key, english, group.All(b => b.DurationMs < 0)))
                {
                    continue; // the zone's own state, an artifact's bonus on the whole faction, a test state of the game's data: nothing the player does or watches
                }

                if (long.TryParse(english, out _) && Aion2SkillIcons.PathFor(group.Key) is null && Aion2Artwork.EffectIconPath(group.Key) is null)
                {
                    continue; // a nameless state (rest, speed ...) says nothing to the player
                }

                BuffNotice best = group.OrderBy(b => b.Ends is null ? DateTime.MaxValue : b.Ends.Value).Last();
                if (!_effects.TryGetValue(group.Key, out var known))
                {
                    known = new EffectRow(++_order);
                    _effects[group.Key] = known;
                }

                // a renewal: the end moved forward while the effect was still running. Renewed twice, a short effect is an aura.
                (known.Renewals, known.Aura) = EffectAura.Next(known.Buff?.Ends, best.Ends, now, best.DurationMs, known.Renewals, known.Aura);
                known.LastActive = now;
                known.Buff = best;
            }

            // by first appearance - or by time left: the running ones with the least left first, auras and effects without an end after them
            IEnumerable<KeyValuePair<int, EffectRow>> ordered = _effects.OrderBy(kv => kv.Value.Order);
            if (SortByTime)
            {
                ordered = _effects
                    .OrderBy(kv => kv.Value.Aura || kv.Value.Buff is not { Ends: not null } ? 1 : 0)
                    .ThenBy(kv => kv.Value.Aura || kv.Value.Buff is not { Ends: { } end } ? 0 : (end - now).TotalSeconds)
                    .ThenBy(kv => kv.Value.Order);
            }

            foreach (var (skill, row) in ordered.ToList())
            {
                if (now - row.LastActive > TimeSpan.FromSeconds(30))
                {
                    _effects.Remove(skill);
                    continue;
                }

                if (now - row.LastActive > Linger || row.Buff is not { } b)
                {
                    continue;
                }

                bool active = row.LastActive == now;
                bool endless = row.Aura || b.Ends is null;
                string time = !active || endless ? "" : EffectText(Math.Max(0, (b.Ends!.Value - now).TotalSeconds));
                shown.Add((skill, time, active));
            }
        }

        string layout = string.Join(",", shown.Select(s => s.Id));
        if (_overlay)
        {
            if (layout != _effectLayout)
            {
                _effectLayout = layout;
                _effectGrid.Children.Clear();
                foreach ((int id, _, _) in shown)
                {
                    _effectGrid.Children.Add(EffectTileOf(id));
                }
            }

            foreach ((int id, string time, bool active) in shown)
            {
                _effectTiles[id].Show(time, active);
            }

            return shown.Count;
        }

        if (layout != _effectLayout)
        {
            _effectLayout = layout;
            _effectList.Children.Clear();
            foreach ((int id, _, _) in shown)
            {
                ListRow row = EffectRowOf(id);
                row.Box.IsChecked = !Hidden.Contains(id);
                row.Fade();
                _effectList.Children.Add(row);
            }

            if (_noEffects is not null)
            {
                _noEffects.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        foreach ((int id, string time, bool active) in shown)
        {
            ListRow row = _effectRows[id];
            row.SetStatus(time.Length > 0 ? time : active ? "∞" : "", active && time.Length > 0);
            row.Opacity = active ? 1 : 0.5;
        }

        return shown.Count;
    }

    private EffectTile EffectTileOf(int id)
    {
        if (!_effectTiles.TryGetValue(id, out EffectTile? tile))
        {
            _effectTiles[id] = tile = new EffectTile(IconOf(id, Aion2SkillNames.DisplayOf(id)), OverlayEffectTile);
        }

        return tile;
    }

    private ListRow EffectRowOf(int id)
    {
        if (!_effectRows.TryGetValue(id, out ListRow? row))
        {
            string name = Aion2SkillNames.DisplayOf(id);
            var picture = new SkillTile(IconOf(id, name), ListTile);
            _effectRows[id] = row = new ListRow(id, picture, picture, name, LocalizationManager.Instance["Assist.ShowInOverlay"]);
            row.Box.Click += (_, _) =>
            {
                bool on = row.Box.IsChecked == true;
                if (on)
                {
                    Hidden.Remove(id);
                }
                else
                {
                    Hidden.Add(id);
                }

                row.Fade();
                ShowInOverlayChanged?.Invoke(id, on);
            };
        }

        return row;
    }

    /// <summary>An effect's time left: 45s, 4:30, 12m, 2h.</summary>
    private static string EffectText(double seconds)
    {
        int whole = (int)Math.Ceiling(seconds - 1e-9);
        return whole >= 3600 ? $"{whole / 3600}h" : whole >= 600 ? $"{whole / 60}m" : whole >= 60 ? $"{whole / 60}:{whole % 60:00}" : $"{whole}s";
    }

    // ---------- the pieces ----------

    private ImageSource IconOf(int id, string name) =>
        (ImageSource?)Picture(Aion2SkillIcons.PathFor(id) ?? Aion2Artwork.EffectIconPath(id)) ?? StatusIcons.For(name, Aion2Artwork.IsDebuffEffect(id));

    /// <summary>
    /// A skill's picture with its cooldown on it: as it is while ready; while it cools down darker all over, the part still to go darker still (a sweep that
    /// uncovers it clockwise from the top), the whole seconds left in the middle.
    /// </summary>
    private sealed class SkillTile : Grid
    {
        private readonly double _size;
        private readonly Rectangle _shade;
        private readonly ShapePath _sweep;
        private readonly TextBlock _seconds;
        private double _fraction = -1;
        private int _whole = -1;

        public SkillTile(ImageSource icon, double size)
        {
            _size = size;
            Width = Height = size;
            double radius = Math.Max(3, size / 10);
            Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius);
            Children.Add(new Image { Source = icon, Width = size, Height = size, Stretch = Stretch.UniformToFill });
            _shade = new Rectangle { Fill = Shade, Visibility = Visibility.Collapsed };
            _sweep = new ShapePath { Fill = Sweep, Visibility = Visibility.Collapsed };
            _seconds = new TextBlock
            {
                Foreground = OverlayText, FontWeight = FontWeights.Bold, FontSize = Math.Round(size * 0.4), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Effect = Outline, Visibility = Visibility.Collapsed,
            };
            Children.Add(_shade);
            Children.Add(_sweep);
            Children.Add(new Border { BorderBrush = TileEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius) });
            Children.Add(_seconds);
        }

        public void Show(CooldownView view)
        {
            if (!view.Running)
            {
                if (_whole != 0)
                {
                    _whole = 0;
                    _fraction = -1;
                    _shade.Visibility = _sweep.Visibility = _seconds.Visibility = Visibility.Collapsed;
                }

                return;
            }

            _shade.Visibility = Visibility.Visible;
            if (Math.Abs(view.Fraction - _fraction) > 0.002)
            {
                _fraction = view.Fraction;
                _sweep.Data = SweepOf(_size, _fraction);
                _sweep.Visibility = _fraction > 0.001 ? Visibility.Visible : Visibility.Collapsed;
            }

            int whole = view.WholeSeconds;
            if (whole != _whole)
            {
                _whole = whole;
                _seconds.Text = CooldownText(whole);
                _seconds.FontSize = Math.Round(_size * (whole >= 60 ? 0.3 : 0.4));
                _seconds.Visibility = Visibility.Visible;
            }
        }

        /// <summary>The part of the tile still to go: from the angle reached so far clockwise back to the top (a circle larger than the tile, cut by it).</summary>
        private static Geometry SweepOf(double size, double fraction)
        {
            if (fraction >= 0.999)
            {
                return new RectangleGeometry(new Rect(0, 0, size, size));
            }

            double c = size / 2, r = size;
            double angle = (1 - fraction) * 2 * Math.PI;
            Point At(double a) => new(c + r * Math.Sin(a), c - r * Math.Cos(a));
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(c, c), true, true);
                ctx.LineTo(At(angle), false, false);
                ctx.ArcTo(At(0), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, false, false);
            }

            geometry.Freeze();
            return geometry;
        }
    }

    /// <summary>An effect's picture with the time left under it (nothing for one without an end); faded for a moment after it ended.</summary>
    private sealed class EffectTile : StackPanel
    {
        private readonly TextBlock _time;

        public EffectTile(ImageSource icon, double size)
        {
            Width = size + 8;
            Margin = new Thickness(0, 0, 2, 2);
            double radius = Math.Max(3, size / 10);
            var picture = new Grid { Width = size, Height = size, Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius) };
            picture.Children.Add(new Image { Source = icon, Width = size, Height = size, Stretch = Stretch.UniformToFill });
            picture.Children.Add(new Border { BorderBrush = TileEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius) });
            Children.Add(picture);
            _time = new TextBlock
            {
                Foreground = OverlayText, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Height = 16, HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, Effect = Outline, Margin = new Thickness(0, 1, 0, 0),
            };
            Children.Add(_time);
        }

        public void Show(string time, bool active)
        {
            if (_time.Text != time)
            {
                _time.Text = time;
            }

            Opacity = active ? 1 : 0.45;
        }
    }

    /// <summary>A row of the window's lists: the box (ticked: in the overlay), the picture, the name (the id in its tooltip) and what is left.</summary>
    private sealed class ListRow : Grid
    {
        public CheckBox Box { get; }
        public SkillTile Tile { get; }
        private readonly FrameworkElement _body;
        private readonly TextBlock _status;
        private bool? _strong;

        public ListRow(int id, SkillTile tile, FrameworkElement picture, string name, string tip)
        {
            Tile = tile;
            Margin = new Thickness(0, 3, 0, 3);
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = tip };
            Children.Add(Box);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            picture.Margin = new Thickness(0, 0, 10, 0);
            picture.VerticalAlignment = VerticalAlignment.Center;
            body.Children.Add(picture);
            var label = new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = "#" + id };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            Grid.SetColumn(label, 1);
            body.Children.Add(label);
            _status = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), MinWidth = 44, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(_status, 2);
            body.Children.Add(_status);
            Grid.SetColumn(body, 1);
            Children.Add(body);
            _body = body;
        }

        /// <summary>An unticked row is faded.</summary>
        public void Fade() => _body.Opacity = Box.IsChecked == true ? 1 : 0.5;

        /// <param name="strong">A running time (the text colour, semi-bold); else muted ("Ready", why a skill is not listed).</param>
        public void SetStatus(string text, bool strong)
        {
            if (_status.Text != text)
            {
                _status.Text = text;
            }

            if (_strong != strong)
            {
                _strong = strong;
                _status.FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal;
                _status.SetResourceReference(TextBlock.ForegroundProperty, strong ? "Brush.Text" : "Brush.TextMuted");
            }
        }
    }

    private BitmapImage? Picture(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (!_images.TryGetValue(path, out BitmapImage? image))
        {
            image = null;
            if (System.IO.File.Exists(path))
            {
                image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.DecodePixelWidth = 80;
                image.EndInit();
                image.Freeze();
            }

            _images[path] = image;
        }

        return image;
    }

    private TextBlock Heading(string text)
    {
        TextBlock block = Label(text, "Brush.Text", 14, bold: true);
        block.Margin = new Thickness(0, 0, 0, 6);
        return block;
    }

    private TextBlock Label(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (_overlay)
        {
            block.Foreground = brush == "Brush.Text" ? OverlayText : OverlayMuted;
        }
        else
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }

        return block;
    }

    private static Border Card(UIElement child)
    {
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 6), Child = child };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return border;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Effect FrozenOutline()
    {
        var shadow = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 4, Opacity = 1 };
        shadow.Freeze();
        return shadow;
    }
}
