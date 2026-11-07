using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The fight report: one fight (live or from the history) on tabs - damage, healing, damage taken, shields, defense, effects, support (what each player
/// added to the others), rotation and a timeline of the fight - with every player's skills, a chart of them over time, the effects on them and on the
/// boss. Built in code from a <see cref="FightReport"/>; nothing in it changes the fight.
/// </summary>
public sealed class FightReportWindow : Window
{
    public static readonly string[] Tabs = { "Damage", "Healing", "Damage taken", "Shields", "Defense", "Buffs", "Buffs per skill", "Support", "Rotation", "Timeline", "Stagger" };

    private static readonly CultureInfo En = CultureInfo.InvariantCulture;
    private static readonly Brush GoodBrush = ReportPalette.Brush(ReportPalette.Good);
    private static readonly Brush BadBrush = ReportPalette.Brush(ReportPalette.Bad);
    private static readonly Brush HoverBrush = ReportPalette.Brush(Colors.White, 0x0A);
    private static readonly Brush SelectedBrush = ReportPalette.Brush(Color.FromRgb(0xDC, 0xB8, 0x77), 0x14);
    private static readonly Brush TrackBrush = ReportPalette.Brush(Colors.White, 0x12);
    private static readonly Brush BlueBrush = ReportPalette.Brush(Color.FromRgb(0x5B, 0x9B, 0xD5));

    private readonly FightReport _r;

    /// <summary>The look the player gave the meter (Overlays → Meter look): the players' lines here are drawn the same way, bars and font.</summary>
    private readonly MeterLook _look = MeterLook.From(MeterSettings.Load());
    private readonly StackPanel _page = new();
    private readonly StackPanel _tabBar = new() { Orientation = Orientation.Horizontal };
    private readonly ContentControl _body = new();
    private string _tab = Tabs[0];
    private int _selected;

    /// <summary>The fight's page on the website when it was uploaded (from its History entry), for "Open on the website" and "Copy link".</summary>
    private readonly string? _shareUrl;
    private readonly Func<Task<(string? Url, string Message)>>? _upload;

    /// <summary>An uploaded fight sent again: the website then has what each skill use did (its Rotation details) for a copy uploaded before 1.19.19.</summary>
    private readonly Func<Task<(string? Url, string Message)>>? _resend;
    private readonly string _uploadLabel;

    private static string? SiteUrl(string? url) => Brand.IsSiteUrl(url) ? url : null;

    /// <param name="shareUrl">The fight's page on the website when it was uploaded: "Open on the website" and "Copy link".</param>
    /// <param name="upload">Uploads the fight (from the History) when it is not online yet: an "Upload" button, which gives the two above once it went up.</param>
    /// <param name="uploadLabel">The upload button's text ("Upload", "Upload 3 boss fights").</param>
    public FightReportWindow(FightReport report, string? shareUrl = null, Func<Task<(string? Url, string Message)>>? upload = null, string? uploadLabel = null)
    {
        _r = report;
        _shareUrl = SiteUrl(shareUrl);
        _upload = _shareUrl is null ? upload : null;
        _resend = _shareUrl is not null ? upload : null;
        _uploadLabel = uploadLabel ?? "Upload";
        if (_shareUrl is not null)
        {
            _ = LoadRanksAsync(_shareUrl);
        }

        Title = $"{report.Title} · {Brand.AppName}";
        Width = 1200;
        Height = 900;
        MinWidth = 780;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        _selected = report.Players.FirstOrDefault(p => p.Player.IsSelf)?.Player.Id ?? report.Players.FirstOrDefault()?.Player.Id ?? 0;

        _page.Margin = new Thickness(30, 22, 30, 30);
        _page.MaxWidth = 1240;
        _page.Children.Add(Header());
        _page.Children.Add(Stats());
        var tabs = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 18, 0, 0), Child = _tabBar };
        tabs.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _page.Children.Add(tabs);
        _body.Margin = new Thickness(0, 16, 0, 0);
        _page.Children.Add(_body);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        ThemedChrome.Apply(this);
        ShowTab(Tabs[0]);
    }

    /// <summary>The page itself, inside the window's scroll viewer and chrome (the picture mode takes it out and draws it).</summary>
    internal StackPanel PageContent => _page;

    public void ShowTab(string tab)
    {
        _tab = Tabs.Contains(tab) ? tab : Tabs[0];
        _tabBar.Children.Clear();
        foreach (string name in Tabs)
        {
            bool on = name == _tab;
            var label = new TextBlock { Text = name, FontSize = 14, Margin = new Thickness(0, 0, 0, 9), FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextMuted");
            var underline = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = on ? ReportPalette.Brush(Color.FromRgb(0xDC, 0xB8, 0x77)) : Brushes.Transparent };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 26, -1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            stack.Children.Add(label);
            stack.Children.Add(underline);
            string target = name;
            stack.MouseLeftButtonUp += (_, _) => ShowTab(target);
            stack.MouseEnter += (_, _) => { if (target != _tab) label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text"); };
            stack.MouseLeave += (_, _) => { if (target != _tab) label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted"); };
            _tabBar.Children.Add(stack);
        }

        _body.Content = _tab switch
        {
            "Healing" => HealingTab(),
            "Damage taken" => TakenTab(),
            "Shields" => ShieldsTab(),
            "Defense" => DefenseTab(),
            "Buffs" => BuffsTab(),
            "Buffs per skill" => BuffsPerSkillTab(),
            "Support" => SupportTab(),
            "Rotation" => RotationTab(),
            "Timeline" => TimelineTab(),
            "Stagger" => StaggerTab(),
            _ => DamageTab(),
        };
    }

    private void Select(int playerId)
    {
        _selected = playerId;
        ShowTab(_tab);
    }

    private ReportPlayerLine? Selected(IEnumerable<ReportPlayerLine> among)
    {
        var list = among.ToList();
        return list.FirstOrDefault(p => p.Player.Id == _selected) ?? list.FirstOrDefault();
    }

    // ---------- the top of the page ----------

    private FrameworkElement Header()
    {
        var dock = new DockPanel { LastChildFill = true };
        var copy = new Button { Content = "Copy for chat", Padding = new Thickness(14, 6, 14, 6), VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(ChatText());
                copy.Content = "Copied";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                copy.Content = "Clipboard busy, try again";
            }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var status = new TextBlock { FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        if (_shareUrl is { } url)
        {
            AddShareButtons(buttons, url, 0);
            if (_resend is { } resend)
            {
                var again = new Button { Content = _uploadLabel, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                    ToolTip = "Sends this fight to the website again with what each skill use did, so clicking a skill on its Rotation tab shows its damage, crits and hit types" };
                again.Click += async (_, _) =>
                {
                    again.IsEnabled = false;
                    again.Content = "Sending…";
                    (string? Url, string Message) result;
                    try
                    {
                        result = await resend();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Net.Http.HttpRequestException or TaskCanceledException)
                    {
                        result = (null, "Sending failed: " + ex.Message);
                    }

                    status.Text = result.Url is not null ? "Done: the website's Rotation tab now shows what each skill use did." : result.Message;
                    status.Visibility = Visibility.Visible;
                    again.Content = result.Url is not null ? "Details added" : "Try again";
                    again.IsEnabled = result.Url is null;
                };
                buttons.Children.Insert(0, again);
            }
        }
        else if (_upload is { } upload)
        {
            // not online yet: uploads it from here; once up, the page's two buttons take the upload button's place
            var send = new Button { Content = _uploadLabel, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand,
                ToolTip = "Puts this fight on atreia.app, with a page you can share" };
            send.FontWeight = FontWeights.SemiBold;
            send.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
            send.Click += async (_, _) =>
            {
                if (!PrivacyConsent.EnsureForUploadSwitch(Window.GetWindow(send)))
                {
                    return;
                }

                send.IsEnabled = false;
                send.Content = "Uploading…";
                (string? Url, string Message) result;
                try
                {
                    result = await upload();
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Net.Http.HttpRequestException or TaskCanceledException)
                {
                    result = (null, "Upload failed: " + ex.Message);
                }

                status.Text = result.Message;
                status.Visibility = Visibility.Visible;
                if (SiteUrl(result.Url) is { } page)
                {
                    int at = buttons.Children.IndexOf(send);
                    buttons.Children.Remove(send);
                    AddShareButtons(buttons, page, Math.Max(0, at));
                }
                else
                {
                    send.IsEnabled = true;
                    send.Content = "Try again";
                }
            };
            buttons.Children.Add(send);
        }

        buttons.Children.Add(copy);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(buttons);
        right.Children.Add(status);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (_r.BossNpcId is int npc && Picture(Aion2Artwork.BossThumbPath(npc)) is { } thumb)
        {
            row.Children.Add(new Border
            {
                Width = 56, Height = 56, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 14, 0), ClipToBounds = true,
                Background = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill },
            });
        }

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = _r.Title, FontSize = 26, FontWeight = FontWeights.SemiBold };
        ThemeManager.UseTitleFont(title);
        titles.Children.Add(title);
        titles.Children.Add(Muted(_r.Start.ToString("MMM d, yyyy, h:mm tt", CultureInfo.GetCultureInfo("en-US")), 13));
        row.Children.Add(titles);
        dock.Children.Add(row);
        return dock;
    }

    /// <summary>The uploaded fight's page: "Open on the website" and "Copy link", put in at <paramref name="at"/>.</summary>
    private static void AddShareButtons(StackPanel buttons, string url, int at)
    {
        // uploaded: the page on the website, to open or to paste to the group
        var open = new Button { Content = "Open on the website", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, ToolTip = url };
        open.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                open.Content = "Could not open the browser";
            }
        };
        var link = new Button { Content = "Copy link", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, ToolTip = url };
        link.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(url);
                link.Content = "Link copied";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                link.Content = "Clipboard busy, try again";
            }
        };
        buttons.Children.Insert(at, open);
        buttons.Children.Insert(at + 1, link);
    }

    private FrameworkElement Stats()
    {
        var root = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        var big = new StackPanel { Orientation = Orientation.Horizontal };
        big.Children.Add(BigStat("Damage", N(_r.Damage), null));
        big.Children.Add(BigStat("Party DPS", N(_r.PartyDps), ReportPalette.Brush(Color.FromRgb(0xDC, 0xB8, 0x77))));
        var duration = BigStat("Duration", ReportPalette.Clock(_r.Seconds), null);
        if (_r.Killed && duration is StackPanel d && d.Children[1] is TextBlock value)
        {
            value.Inlines.Add(new Run("  Killed") { FontSize = 14, FontWeight = FontWeights.Normal, Foreground = GoodBrush });
        }

        big.Children.Add(duration);
        root.Children.Add(big);

        var small = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        small.Children.Add(SmallStat("Deaths", _r.Deaths.ToString(En), null));
        small.Children.Add(SmallStat("Peak DPS", $"{N(_r.PeakDps)} · lowest {N(_r.LowestDps)}", null));
        small.Children.Add(SmallStat("Average DPS", N(_r.AverageDps), null));
        small.Children.Add(SmallStat("Damage taken", $"{N(_r.Taken)} · {N(_r.TakenDps)} DPS", BadBrush));
        root.Children.Add(small);
        return root;
    }

    private static FrameworkElement BigStat(string label, string value, Brush? color)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 44, 0) };
        stack.Children.Add(Muted(label, 12.5));
        var text = new TextBlock { Text = value, FontSize = 32, FontWeight = FontWeights.SemiBold };
        text.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        if (color is not null)
        {
            text.Foreground = color;
        }

        stack.Children.Add(text);
        return stack;
    }

    private static FrameworkElement SmallStat(string label, string value, Brush? color)
    {
        var text = new TextBlock { Margin = new Thickness(0, 0, 26, 0), FontSize = 13.5 };
        var name = new Run(label + "  ");
        name.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextMuted");
        text.Inlines.Add(name);
        var run = new Run(value);
        if (color is not null)
        {
            run.Foreground = color;
        }

        text.Inlines.Add(run);
        return text;
    }

    private string ChatText()
    {
        var lines = new List<string>
        {
            $"{_r.Title} · {ReportPalette.Clock(_r.Seconds)}{(_r.Killed ? " · killed" : "")} · party DPS {N(_r.PartyDps)} · damage {N(_r.Damage)}",
        };
        int rank = 0;
        foreach (ReportPlayerLine p in _r.Players.Where(p => p.Damage > 0).Take(8))
        {
            lines.Add($"{++rank}. {p.Player.Name} ({p.Player.ClassName}) {N(p.Dps)} DPS · {Pct(p.Share)}" + (p.Rdps is double rd ? $" · rDPS {N(rd)}" : ""));
        }

        return string.Join("\n", lines);
    }

    // ---------- the tabs ----------

    // the ranks of an uploaded fight, from its page on the website (null while they load or when the site didn't answer)
    private IReadOnlyDictionary<string, UploadClient.FightRank>? _ranks;
    private int _rankMin = 10;
    private bool _ranksDone;

    private async Task LoadRanksAsync(string url)
    {
        var answer = await UploadClient.GetRanksAsync(url);
        _ranksDone = true;
        if (answer is { } a)
        {
            _ranks = a.Ranks;
            _rankMin = a.Min;
        }

        if (_tab == "Damage")
        {
            ShowTab(_tab);
        }
    }

    /// <summary>The Rank column of an uploaded fight: the percentile, else a dash that says why (not enough logs of the class yet, not ranked, still loading).</summary>
    private (string Text, string? Tip) RankOf(ReportPlayerLine p)
    {
        if (!_ranksDone)
        {
            return ("…", "Loading the ranks from the website");
        }

        if (_ranks is null)
        {
            return ("–", "The website didn't answer: the ranks show when it does.");
        }

        if (!_ranks.TryGetValue(p.Player.Name, out UploadClient.FightRank? r))
        {
            return ("–", _r.Killed ? "Not ranked: the website has no rank for this player." : "Only killed bosses are ranked.");
        }

        if (r.Of < _rankMin)
        {
            return ("–", $"Not enough data yet: a rank shows once {_rankMin} or more {p.Player.ClassName}s have a log on {_r.Title} ({r.Of} so far).");
        }

        return (Math.Floor(r.Percent).ToString("0", En), $"Better than {r.Percent.ToString("0.#", En)}% of {RankTier.Bracket(r.Low, r.High)}{p.Player.ClassName}s on {_r.Title} by rDPS · rank {r.Place} of {r.Of}");
    }

    private FrameworkElement DamageTab()
    {
        var players = _r.Players.Where(p => p.Damage > 0).ToList();
        var root = new StackPanel();
        bool rdps = players.Any(p => p.Rdps is not null);
        var cols = new List<Col> { new("Damage", 120, p => N(p.Damage), Bold: true), new("DPS", 100, p => N(p.Dps)) };
        if (_shareUrl is not null)
        {
            // in the website's colour for the percentile, as on the fight's page
            cols.Insert(0, new Col("Rank", 70, p => RankOf(p).Text, Bold: true,
                Color: p => _ranks is not null && _ranks.TryGetValue(p.Player.Name, out UploadClient.FightRank? fr) && fr.Of >= _rankMin ? RankTier.Brush(fr.Percent) : null,
                Tip: p => RankOf(p).Tip));
        }

        if (rdps)
        {
            cols.Add(new Col("rDPS", 100, p => p.Rdps is double r ? N(r) : "–",
                Tip: _ => "Raid DPS, an estimate: their damage, less what the others' buffs added to it, plus what their own buffs added to the others. The buffs' values come from the skills' descriptions, not from a measurement in the game."));
        }

        cols.Add(new Col("Share", 80, p => Pct(p.Share)));
        cols.Add(new Col("Accuracy", 90, p => Acc(p.Accuracy), Tip: p => AccuracyTip(p)));
        root.Children.Add(Section("Players", "Click a player to see their skills."));
        root.Children.Add(PlayersTable(players, p => p.Damage, cols.ToArray()));
        if (Selected(players) is not { } sel)
        {
            return Empty(root, "No damage in this fight.");
        }

        root.Children.Add(PlayerHeading(sel.Player.Name + "'s skills", players));
        var card = new StackPanel();
        card.Children.Add(Summary(("Damage", N(sel.Damage)), ("DPS", N(sel.Dps)), ("rDPS", sel.Rdps is double r ? N(r) : null), ("Share", Pct(sel.Share)),
            ("Accuracy", sel.Accuracy is double a ? Acc(a) : null), ("Deaths", sel.Deaths.Count > 0 ? sel.Deaths.Count.ToString(En) : null)));
        card.Children.Add(SkillTable(sel.Skills, sel.Player.ClassName, BlueBrush, DamageColumns(full: true), sel.Damage, sel.Dps));
        root.Children.Add(Card(card));
        root.Children.Add(ChartCard(sel.DamageSeries));
        return root;
    }

    private FrameworkElement HealingTab()
    {
        var players = _r.Players.Where(p => p.Healing > 0 || p.Absorbed > 0).OrderByDescending(p => p.Healing).ToList();
        var root = new StackPanel();
        root.Children.Add(Section("Players", "Click a player to see their skills."));
        root.Children.Add(PlayersTable(players, p => p.Healing,
            new Col("Healing", 120, p => N(p.Healing), Bold: true), new Col("HPS", 90, p => N(p.Hps)), new Col("Share", 80, p => Pct(p.HealShare)),
            new Col("Effective", 110, p => p.HealChecked > 0 ? N(p.Healing - p.Overheal) : "–", Tip: OverhealTip),
            new Col("Overheal", 90, p => p.HealChecked > 0 && p.Healing > 0 ? Pct(100.0 * p.Overheal / p.Healing) : "–", Tip: OverhealTip),
            new Col("Shielded", 100, p => N(p.Absorbed), Color: _ => BlueBrush)));
        if (Selected(players) is not { } sel)
        {
            return Empty(root, "No healing in this fight.");
        }

        root.Children.Add(PlayerHeading(sel.Player.Name + "'s skills", players));
        var card = new StackPanel();
        bool overKnown = sel.HealChecked > 0;
        card.Children.Add(Summary(("Healing", N(sel.Healing)), ("HPS", N(sel.Hps)),
            ("Effective", overKnown ? N(sel.Healing - sel.Overheal) : null), ("Overheal", overKnown && sel.Healing > 0 ? Pct(100.0 * sel.Overheal / sel.Healing) : null),
            ("Shielded", N(sel.Absorbed))));
        var cols = new[]
        {
            new SCol("OUTPUT", "Healing", 110, s => N(s.Total), Bold: true), new SCol("OUTPUT", "Share", 80, s => Pct(s.Share)),
            new SCol("OUTPUT", "Overheal", 85, s => !overKnown || s.Absorb || s.Total <= 0 ? "–" : Pct(100.0 * s.Overheal / s.Total)),
            new SCol("COUNT", "Hits", 70, s => N(s.Hits)),
            new SCol("PER HIT", "Average", 90, s => N(s.Average)), new SCol("PER HIT", "Highest", 90, s => N(s.Highest)),
        };
        card.Children.Add(SkillTable(sel.Heals, sel.Player.ClassName, GoodBrush, cols, sel.Heals.Sum(s => s.Total), 0));
        root.Children.Add(Card(card));
        root.Children.Add(ChartCard(sel.HealSeries));
        return root;
    }

    private FrameworkElement TakenTab()
    {
        var players = _r.Players.Where(p => p.Taken > 0).OrderByDescending(p => p.Taken).ToList();
        var root = new StackPanel();
        root.Children.Add(Section("Players", "Click a player to see what hit them."));
        root.Children.Add(PlayersTable(players, p => p.Taken,
            new Col("Damage taken", 130, p => N(p.Taken), Bold: true), new Col("DPS", 90, p => N(p.TakenDps)), new Col("Share", 80, p => Pct(p.TakenShare))));
        if (Selected(players) is not { } sel)
        {
            return Empty(root, "Nobody took damage in this fight.");
        }

        root.Children.Add(PlayerHeading(sel.Player.Name + "'s damage taken", players));
        var card = new StackPanel();
        card.Children.Add(Summary(("Damage taken", N(sel.Taken)), ("DPS", N(sel.TakenDps)), ("Hits", N(sel.HitsTaken))));
        var cols = DamageColumns(full: false);
        foreach (ReportSource source in sel.TakenBy)
        {
            var head = new DockPanel { Margin = new Thickness(18, 14, 18, 2) };
            var numbers = new TextBlock { FontSize = 14 };
            numbers.Inlines.Add(new Run(N(source.Total)) { FontWeight = FontWeights.SemiBold });
            var share = new Run("    " + Pct(source.Share));
            share.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextMuted");
            numbers.Inlines.Add(share);
            DockPanel.SetDock(numbers, Dock.Right);
            head.Children.Add(numbers);
            head.Children.Add(new TextBlock { Text = source.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
            card.Children.Add(head);
            card.Children.Add(SkillTable(source.Skills, null, BlueBrush, cols, source.Total, source.Total / _r.Seconds));
        }

        root.Children.Add(Card(card));
        root.Children.Add(ChartCard(sel.TakenSeries));
        return root;
    }

    private FrameworkElement ShieldsTab()
    {
        var players = _r.Players.Where(p => p.Absorbed > 0).OrderByDescending(p => p.Absorbed).ToList();
        var root = new StackPanel();
        root.Children.Add(Section("Players", "What shields absorbed of the damage aimed at each player."));
        root.Children.Add(PlayersTable(players, p => p.Absorbed,
            new Col("Absorbed", 120, p => N(p.Absorbed), Bold: true),
            new Col("Of incoming", 110, p => p.Absorbed + p.Taken > 0 ? Pct(100.0 * p.Absorbed / (p.Absorbed + p.Taken)) : "–"),
            new Col("Hits", 80, p => N(p.ShieldsOn.Sum(s => s.Hits)))));
        if (Selected(players) is not { } sel)
        {
            return Empty(root, "No shield absorbed damage in this fight.");
        }

        root.Children.Add(PlayerHeading(sel.Player.Name + "'s shields", players));
        var cols = new[]
        {
            new SCol("OUTPUT", "Absorbed", 110, s => N(s.Total), Bold: true), new SCol("OUTPUT", "Share", 80, s => Pct(s.Share)),
            new SCol("COUNT", "Hits", 70, s => N(s.Hits)),
            new SCol("PER HIT", "Average", 90, s => N(s.Average)), new SCol("PER HIT", "Highest", 90, s => N(s.Highest)),
        };
        var card = new StackPanel();
        card.Children.Add(Summary(("Absorbed", N(sel.Absorbed)), ("Damage taken", N(sel.Taken))));
        card.Children.Add(SkillTable(sel.ShieldsOn, sel.Player.ClassName, BlueBrush, cols, sel.Absorbed, 0));
        root.Children.Add(Card(card));
        return root;
    }

    private FrameworkElement DefenseTab()
    {
        var players = _r.Players.Where(p => p.HitsTaken > 0 || p.Dodged + p.Parried + p.Blocked + p.Resisted > 0 || p.Deaths.Count > 0)
            .OrderByDescending(p => p.HitsTaken).ToList();
        var root = new StackPanel();
        root.Children.Add(Section("Players", "Attacks taken and avoided."));
        root.Children.Add(PlayersTable(players, p => p.HitsTaken,
            new Col("Hits taken", 100, p => N(p.HitsTaken), Bold: true), new Col("Dodged", 80, p => N(p.Dodged)), new Col("Parried", 80, p => N(p.Parried)),
            new Col("Blocked", 80, p => N(p.Blocked)), new Col("Resisted", 80, p => N(p.Resisted)),
            new Col("Avoided", 80, p => Pct(Avoided(p))), new Col("Deaths", 70, p => N(p.Deaths.Count), Color: p => p.Deaths.Count > 0 ? BadBrush : null)));
        if (Selected(players) is not { } sel)
        {
            return Empty(root, "No attacks on the players in this fight.");
        }

        root.Children.Add(PlayerHeading(sel.Player.Name + "'s defense", players));
        var card = new StackPanel();
        card.Children.Add(Summary(("Hits taken", N(sel.HitsTaken)), ("Avoided", Pct(Avoided(sel))), ("Damage taken", N(sel.Taken)), ("Deaths", N(sel.Deaths.Count))));
        if (sel.Deaths.Count > 0)
        {
            var deaths = new StackPanel { Margin = new Thickness(18, 10, 18, 8) };
            deaths.Children.Add(Muted("DEATHS", 11.5, semibold: true));
            foreach (ReportDeath death in sel.Deaths)
            {
                var line = new TextBlock { FontSize = 13.5, Margin = new Thickness(0, 6, 0, 0) };
                line.Inlines.Add(new Run(ReportPalette.Clock(death.At)) { Foreground = BadBrush, FontWeight = FontWeights.SemiBold });
                line.Inlines.Add(new Run("   " + (death.KilledBy is { } by ? "killed by " + by : "died") + (death.LastHit is long hit ? $" · last hit {N(hit)}" : "")));
                deaths.Children.Add(line);
            }

            card.Children.Add(deaths);
        }

        var cols = new[] { new SCol("", "Avoided", 90, s => N(s.Hits), Bold: true), new SCol("", "Share", 80, s => Pct(s.Share)), new SCol("", "Per min", 80, s => s.PerMinute.ToString("0.0", En)) };
        card.Children.Add(sel.AvoidedBy.Count > 0 ? SkillTable(sel.AvoidedBy, null, GoodBrush, cols, 0, 0, total: false) : Muted("Nothing was dodged, parried, blocked or resisted.", 13, margin: new Thickness(18, 12, 18, 16)));
        root.Children.Add(Card(card));
        return root;
    }

    private static double Avoided(ReportPlayerLine p)
    {
        int avoided = p.Dodged + p.Parried + p.Blocked + p.Resisted;
        return avoided + p.HitsTaken > 0 ? 100.0 * avoided / (avoided + p.HitsTaken) : 0;
    }

    private FrameworkElement BuffsTab()
    {
        var root = new StackPanel();
        root.Children.Add(Pills(_r.Players, HorizontalAlignment.Left));
        if (Selected(_r.Players) is not { } sel)
        {
            return Empty(root, "No players in this fight.");
        }

        var card = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        card.Children.Add(Muted(sel.Player.IsSelf ? "YOUR BUFFS" : sel.Player.Name.ToUpperInvariant() + "'S BUFFS", 12, semibold: true));
        card.Children.Add(sel.Buffs.Count > 0 ? EffectGrid(sel.Buffs, sel.Player.Id) : Muted("No effects were seen on this player.", 13, margin: new Thickness(0, 10, 0, 0)));
        if (sel.Passives.Count > 0)
        {
            var passives = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            passives.Children.Add(Muted("Passives", 12.5, margin: new Thickness(0, 2, 10, 4)));
            foreach (ReportEffect p in sel.Passives)
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 6) };
                if (EffectIcon(p.SkillId, p.Name) is { } icon)
                {
                    chip.Children.Add(new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0) });
                }

                chip.Children.Add(new TextBlock { Text = p.Name, FontSize = 12.5 });
                passives.Children.Add(chip);
            }

            card.Children.Add(passives);
        }

        if (_r.BossEffects.Count > 0)
        {
            card.Children.Add(Muted("DEBUFFS ON " + (_r.BossName ?? _r.Title).ToUpperInvariant(), 12, semibold: true, margin: new Thickness(0, 26, 0, 0)));
            card.Children.Add(EffectGrid(_r.BossEffects, sel.Player.Id));
        }

        root.Children.Add(Card(card));
        return root;
    }

    /// <summary>The most effects the table of buffs per skill has columns for.</summary>
    private const int MostBuffColumns = 12;

    /// <summary>
    /// Buffs per skill (5 November 2026, the owner, with a picture of another meter's table): for each skill the player used, how many of its uses had each of
    /// the effects on the player at that moment - which skills went out under the damage buffs and which missed them. The damage buffs the meter prices first
    /// (Mantra, Gale, Fury …), then the others by how long they were on; the mouse on an effect's icon names it, its class and who gave it.
    /// </summary>
    private FrameworkElement BuffsPerSkillTab()
    {
        var root = new StackPanel();
        var players = _r.Players.Where(p => p.Rotation is { HasData: true }).ToList();
        root.Children.Add(Pills(players.Count > 0 ? players : _r.Players, HorizontalAlignment.Left));
        if (Selected(players) is not { Rotation: { } rotation } sel)
        {
            return Empty(root, "The capture saw no skills used in this fight.");
        }

        var uses = rotation.Uses.Where(u => !u.Passive).ToList();
        var buffs = rotation.Buffs.Where(b => b.Spans is { Count: > 0 } && b.UptimePercent >= 3)
            .OrderByDescending(b => RaidDps.IsSynergy(RaidDps.DisplayBase(b.SkillId)))
            .ThenByDescending(b => b.UptimePercent)
            .Take(MostBuffColumns).ToList();
        if (uses.Count == 0 || buffs.Count == 0)
        {
            return Empty(root, uses.Count == 0 ? "The capture saw no skills used in this fight." : "No effects were seen on this player while they used their skills.");
        }

        var skills = uses.GroupBy(u => u.Name).Select(g => (Name: g.Key, SkillId: g.First().SkillId, At: g.Select(u => u.At).ToList()))
            .OrderByDescending(x => x.At.Count).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
        var grid = new Grid { Margin = new Thickness(18, 12, 18, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 190 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        foreach (BuffRow _ in buffs)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        }

        // the head: "Skill", "Casts" and each effect's icon, named by the mouse
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(grid, Muted("Skill", 12, semibold: true, margin: new Thickness(0, 0, 0, 8)), 0, 0);
        var castsHead = Muted("Casts", 12, semibold: true, margin: new Thickness(0, 0, 10, 8));
        castsHead.HorizontalAlignment = HorizontalAlignment.Right;
        AddCell(grid, castsHead, 0, 1);
        for (int c = 0; c < buffs.Count; c++)
        {
            BuffRow buff = buffs[c];
            var icon = IconBox(EffectIcon(buff.SkillId, buff.Name), 28);
            ((FrameworkElement)icon).HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)icon).Margin = new Thickness(0, 0, 0, 8);
            ((FrameworkElement)icon).ToolTip = BuffTip(buff);
            AddCell(grid, icon, 0, c + 2);
        }

        // a line per skill: its icon and name, its casts, and per effect the share of its casts made while the effect was on
        int row = 1;
        foreach (var skill in skills)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var skillIcon = IconBox(SkillIcon(skill.SkillId, skill.Name, sel.Player.ClassName), 22);
            ((FrameworkElement)skillIcon).Margin = new Thickness(0, 0, 8, 0);
            name.Children.Add(skillIcon);
            name.Children.Add(new TextBlock { Text = Aion2SkillNames.Display(skill.Name), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            AddCell(grid, name, row, 0);
            var casts = Right(N(skill.At.Count));
            casts.Margin = new Thickness(0, 0, 10, 0);
            AddCell(grid, casts, row, 1);
            for (int c = 0; c < buffs.Count; c++)
            {
                IReadOnlyList<(double Start, double End)> spans = buffs[c].Spans!;
                int under = skill.At.Count(at => spans.Any(sp => sp.Start <= at && at <= sp.End));
                double share = 100.0 * under / skill.At.Count;
                var cell = new Border
                {
                    Margin = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Background = ReportPalette.Brush(Color.FromRgb(0xDC, 0x6E, 0x78), (byte)Math.Round(0x14 + share / 100 * 0x5C)),
                    Child = new TextBlock
                    {
                        Text = Pct0(share), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = share >= 99.5 ? FontWeights.SemiBold : FontWeights.Normal, Opacity = share > 0 ? 1 : 0.55,
                    },
                    ToolTip = $"{Aion2SkillNames.Display(skill.Name)}: {under} of {skill.At.Count} casts under {buffs[c].Name}",
                };
                AddCell(grid, cell, row, c + 2);
            }

            row++;
        }

        var card = new StackPanel();
        card.Children.Add(Muted((sel.Player.IsSelf ? "YOUR" : sel.Player.Name.ToUpperInvariant() + "'S") + " BUFFS PER SKILL", 12, semibold: true, margin: new Thickness(18, 14, 18, 0)));
        card.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid });
        root.Children.Add(Card(card));
        root.Children.Add(Muted("The share of each skill's casts made while the effect was on the player. The damage buffs the meter counts in rDPS come first; the mouse on an icon names the effect, its class and who gave it.",
            12, margin: new Thickness(4, 8, 4, 0)));
        return root;
    }

    /// <summary>An effect's tip: its name, the class it belongs to (or that it is an item's or another effect) and who put it on the player.</summary>
    private static string BuffTip(BuffRow buff)
    {
        string? cls = Aion2SkillNames.ClassOf(buff.SkillId);
        string kind = RaidDps.IsSynergy(RaidDps.DisplayBase(buff.SkillId)) ? "damage buff" : "effect";
        string from = buff.Givers.Length == 0 ? "" : "\nFrom: " + buff.Givers.Replace("★", "you");
        return $"{buff.Name}\n{(cls is not null ? cls + " " + kind : "Item, scroll or other " + kind)}  ·  on {Pct0(buff.UptimePercent)} of the fight{from}";
    }

    private static void AddCell(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private FrameworkElement SupportTab()
    {
        var root = new StackPanel();
        if (_r.Support.Count == 0)
        {
            return Empty(root, "No player put effects on the others or on the target in this fight.");
        }

        var card = new StackPanel { Margin = new Thickness(18, 10, 18, 16) };
        foreach (ReportSupport giver in _r.Support)
        {
            ReportPlayerLine? p = _r.PlayerOf(giver.PlayerId);
            var head = new DockPanel { Margin = new Thickness(0, 14, 0, 6) };
            if (giver.GivenDps > 0)
            {
                var plus = new TextBlock { Text = $"+{N(giver.GivenDps)} DPS", Foreground = GoodBrush, FontSize = 14, FontWeight = FontWeights.SemiBold };
                DockPanel.SetDock(plus, Dock.Right);
                head.Children.Add(plus);
            }

            head.Children.Add(NameCell(p?.Player, 14));
            card.Children.Add(head);
            var grid = TwoColumns(giver.Items.Count);
            int i = 0;
            foreach (ReportSupportItem item in giver.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 18, 4) };
                foreach (double w in new[] { 34.0, double.NaN, 96, 46, 96 })
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
                }

                AddAt(row, IconBox(EffectIcon(item.SkillId, item.Name), 28), 0);
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(Named(item.Name, item.OnTarget));
                names.Children.Add(Muted(item.OnTarget ? "Target" : "Party", 11.5));
                AddAt(row, names, 1);
                AddAt(row, Bar(item.UptimePercent, item.OnTarget ? BadBrush : GoodBrush), 2);
                AddAt(row, Right(Pct0(item.UptimePercent), muted: true), 3);
                AddAt(row, Right(item.Damage is double dmg ? "+" + N(dmg) : "–", color: item.Damage is not null ? GoodBrush : null), 4);
                Place(grid, row, i++, giver.Items.Count);
            }

            card.Children.Add(grid);
        }

        root.Children.Add(Card(card));
        root.Children.Add(Muted("The DPS a player added: their party effects (Mantra, Gale, Protective Light ...) and debuffs on the target, worked out from what each effect adds and how long it was on.",
            12, margin: new Thickness(4, 8, 4, 0)));
        return root;
    }

    private FrameworkElement RotationTab()
    {
        var root = new StackPanel();
        var players = _r.Players.Where(p => p.Rotation is { HasData: true }).ToList();
        root.Children.Add(Pills(players.Count > 0 ? players : _r.Players, HorizontalAlignment.Left));
        if (Selected(players) is not { Rotation: { } rotation } sel)
        {
            return Empty(root, "The capture saw no skills used in this fight.");
        }

        var card = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
        var timeline = new RotationTimelineView();
        timeline.Show(rotation, sel.RotationDps);
        timeline.Configure(showPassives: false, showDps: true, showStagger: true);
        var bar = new DockPanel { Margin = new Thickness(18, 2, 18, 4), LastChildFill = true };
        var whole = new Button { Content = "Whole fight", Padding = new Thickness(10, 3, 10, 3), Cursor = Cursors.Hand };
        whole.Click += (_, _) => timeline.ShowWholeFight();
        DockPanel.SetDock(whole, Dock.Right);
        bar.Children.Add(whole);
        bar.Children.Add(Muted("Scroll over the timeline to zoom; drag across it to spread a stretch of the fight out; click a skill for what that use did. Shift + drag moves along.", 12));
        card.Children.Add(bar);
        card.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = timeline, Margin = new Thickness(10, 4, 10, 0) });
        var details = new Border { Margin = new Thickness(18, 8, 18, 8), Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        details.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        details.Child = Muted("Click a skill on the timeline to see what that use did.", 12.5);
        timeline.UseClicked += use => details.Child = UseDetails(sel, rotation, use);
        card.Children.Add(details);
        var skills = rotation.Skills.Select(s => new ReportSkill(s.Name, s.SkillId, 0, s.SharePercent, 0, s.Uses, s.PerMinute, s.Uses, s.AvgGapSeconds > 0 ? s.AvgGapSeconds : null,
            null, null, null, null, null, null, null, s.FirstAt, 0)).ToList();
        var cols = new[]
        {
            new SCol("", "Uses", 70, s => N(s.Uses), Bold: true), new SCol("", "Per min", 80, s => s.PerMinute.ToString("0.0", En)),
            new SCol("", "Avg gap", 90, s => s.AvgInterval is double g ? g.ToString("0.0", En) + " s" : "–"),
            new SCol("", "First use", 90, s => ReportPalette.Clock(s.Average)), new SCol("", "Damage share", 110, s => s.Share > 0 ? Pct(s.Share) : "–"),
        };
        card.Children.Add(SkillTable(skills, sel.Player.ClassName, BlueBrush, cols, 0, 0, total: false));
        root.Children.Add(Card(card));
        return root;
    }

    /// <summary>What one use did (the rotation's details on a click): when, which of that skill's uses, the damage and the kinds of its hits, the effects on the player then.</summary>
    private FrameworkElement UseDetails(ReportPlayerLine player, RotationReport rotation, RotationUse use)
    {
        var root = new StackPanel();
        var same = rotation.Uses.Where(u => u.Name == use.Name).ToList();
        int nth = same.IndexOf(use);
        var head = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add(new System.Windows.Documents.Run(Aion2SkillNames.Display(use.Name)) { FontWeight = FontWeights.SemiBold });
        root.Children.Add(head);
        root.Children.Add(Muted(TimeSpan.FromSeconds(use.At).ToString(@"m\:ss\.f") + $" · use {nth + 1} of {same.Count}"
            + (nth > 0 ? $" · {(use.At - same[nth - 1].At).ToString("0.0", En)} s after the one before" : " · the first"), 12));
        int index = -1;
        for (int i = 0; i < rotation.Uses.Count; i++)
        {
            if (ReferenceEquals(rotation.Uses[i], use) || Equals(rotation.Uses[i], use))
            {
                index = i;
                break;
            }
        }

        if (index >= 0 && index < player.UseHits.Count)
        {
            UseHits h = player.UseHits[index];
            string Of(int n) => h.Hits > 0 ? $"{n} / {h.Hits}" : n.ToString(En);
            var stats = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            long biggest = player.UseHits.Where((x, i) => i < rotation.Uses.Count && rotation.Uses[i].Name == use.Name).Select(x => x.Biggest).DefaultIfEmpty(0).Max();
            foreach ((string label, string value) in new[]
            {
                ("Damage of this use", h.Damage > 0 ? N(h.Damage) : "–"), ("Biggest hit of it this fight", biggest > 0 ? N(biggest) : "–"), ("Hits", h.Hits.ToString(En)),
                ("Critical", Of(h.Crit)), ("Back attack", Of(h.Back)), ("Front attack", Of(h.Front)), ("Perfect", Of(h.Perfect)), ("Double hit", Of(h.Double)),
                ("Multi-strike", Of(h.Multi)), ("Parried", Of(h.Parry)),
            })
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 22, 6) };
                cell.Children.Add(Muted(label, 11));
                cell.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold });
                stats.Children.Add(cell);
            }

            root.Children.Add(stats);
        }

        // the effects on the player then, the ones that add damage (the party synergies the meter prices: Mantra, Gale, Fury ...) first and marked
        var on = rotation.Buffs.Where(b => b.Spans is { } spans && spans.Any(sp => sp.Start <= use.At && use.At <= sp.End))
            .OrderByDescending(b => RaidDps.IsSynergy(RaidDps.DisplayBase(b.SkillId)))
            .Select(b => Aion2SkillNames.Display(b.Name) + (RaidDps.IsSynergy(RaidDps.DisplayBase(b.SkillId)) ? " (damage buff)" : "")).ToList();
        root.Children.Add(Muted("On " + player.Player.Name + ": " + (on.Count > 0 ? string.Join(", ", on) : "nothing that was tracked"), 12, margin: new Thickness(0, 6, 0, 0)));
        return root;
    }

    private static readonly Color StaggerColor = Color.FromRgb(0x6F, 0x9F, 0xD8);

    /// <summary>A stretch picked on the Stagger tab's chart: the timeline opens on it.</summary>
    private (double From, double To)? _pick;

    /// <summary>
    /// The fight on one time axis, its moments (bursts, drops, stagger breaks, deaths) to click, and what happened in the stretch picked: each player's
    /// damage against their usual, the skills they used there (and what each hit for and staggered), the boss's casts.
    /// </summary>
    private FrameworkElement TimelineTab()
    {
        var root = new StackPanel();
        var chart = new FightTimelineChart(_r, id => _r.PlayerOf(id)?.Player.ClassName ?? "") { Margin = new Thickness(6, 6, 12, 4) };
        var card = new StackPanel();
        var bar = new DockPanel { Margin = new Thickness(18, 12, 18, 0), LastChildFill = true };
        var whole = new Button { Content = "Whole fight", Padding = new Thickness(10, 3, 10, 3), Cursor = Cursors.Hand };
        DockPanel.SetDock(whole, Dock.Right);
        bar.Children.Add(whole);
        bar.Children.Add(Muted("Click the chart for the five seconds up to that moment, or drag across it for a stretch: what everyone did there shows below.", 12));
        card.Children.Add(bar);
        card.Children.Add(chart);
        var legend = new WrapPanel { Margin = new Thickness(84, 4, 18, 14) };
        legend.Children.Add(LegendItem("Party", ReportPalette.Party, null));
        foreach (ReportPlayerLine p in _r.Players.Where(p => p.Damage > 0))
        {
            int id = p.Player.Id;
            FrameworkElement item = LegendItem(p.Player.Name, ClassBrushes.ClassColor(p.Player.ClassName), null);
            item.Cursor = Cursors.Hand;
            item.ToolTip = "Click to hide or show this player's line";
            item.MouseLeftButtonUp += (_, _) =>
            {
                chart.Toggle(id);
                item.Opacity = chart.IsShown(id) ? 1 : 0.4;
            };
            legend.Children.Add(item);
        }

        legend.Children.Add(LegendItem("Damage taken", Color.FromRgb(0x9A, 0x92, 0x8A), null, dashed: true));
        if (_r.Stagger.HasData)
        {
            legend.Children.Add(LegendItem("Stagger bar", StaggerColor, "How full the boss's stagger bar was; a diamond: it broke"));
        }

        card.Children.Add(legend);
        root.Children.Add(Card(card));

        // the moments, and the details of the stretch picked
        var details = new ContentControl();
        var chips = new WrapPanel { Margin = new Thickness(16, 12, 16, 8) };
        var chipOf = new Dictionary<FightMoment, Border>();
        void Show(double from, double to, FightMoment? moment)
        {
            chart.Select(from, to);
            foreach ((FightMoment m, Border chip) in chipOf)
            {
                bool on = ReferenceEquals(m, moment);
                chip.Background = on ? SelectedBrush : ReportPalette.Brush(Colors.White, 0x06);
                chip.BorderBrush = on ? ReportPalette.Brush(Color.FromRgb(0xDC, 0xB8, 0x77), 0xB0) : ReportPalette.Brush(Colors.White, 0x18);
            }

            details.Content = WindowDetails(FightMoments.Window(_r, from, to), moment);
        }

        if (_r.Moments.Count > 0)
        {
            var moments = new StackPanel();
            moments.Children.Add(new TextBlock { Text = "Moments", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(18, 14, 18, 0) });
            moments.Children.Add(Muted("The bursts and drops of the party's damage, the boss's stagger breaks and the deaths. Click one to see that stretch.", 12, margin: new Thickness(18, 2, 18, 0)));
            foreach (FightMoment m in _r.Moments)
            {
                var text = new StackPanel { Orientation = Orientation.Horizontal };
                text.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = ReportPalette.Brush(FightTimelineChart.MomentColor(m.Kind)), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                text.Children.Add(Muted(ReportPalette.Clock(m.At) + "  ", 12));
                text.Children.Add(new TextBlock { Text = m.Title, FontSize = 12.5 });
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(9, 4, 11, 4), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    Background = ReportPalette.Brush(Colors.White, 0x06), BorderBrush = ReportPalette.Brush(Colors.White, 0x18), Child = text,
                    ToolTip = m.Why.Count > 0 ? string.Join("\n", m.Why) : null,
                };
                FightMoment picked = m;
                chip.MouseLeftButtonUp += (_, _) => Show(picked.Start, picked.End, picked);
                chipOf[m] = chip;
                chips.Children.Add(chip);
            }

            moments.Children.Add(chips);
            root.Children.Add(Card(moments));
        }

        root.Children.Add(Card(details));
        chart.Picked += (from, to) => Show(from, to, _r.Moments.FirstOrDefault(m => Math.Abs(m.Start - from) < 0.01 && Math.Abs(m.End - to) < 0.01));
        whole.Click += (_, _) => Show(0, _r.Seconds, null);

        // the stretch picked on the Stagger tab, else the biggest burst, else the whole fight
        FightMoment? first = _r.Moments.Where(m => m.Kind == MomentKind.Burst).OrderByDescending(m => m.Dps).FirstOrDefault();
        if (_pick is (double pickFrom, double pickTo))
        {
            _pick = null;
            Show(pickFrom, pickTo, null);
        }
        else if (first is not null)
        {
            Show(first.Start, first.End, first);
        }
        else
        {
            Show(0, _r.Seconds, null);
        }

        return root;
    }

    /// <summary>A stretch of the fight: when, the party's DPS there, why (for a moment), then a line per player with their skills there.</summary>
    private FrameworkElement WindowDetails(FightWindow w, FightMoment? moment)
    {
        var root = new StackPanel { Margin = new Thickness(18, 14, 18, 12) };
        var head = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add(new Run($"{ReportPalette.Clock(w.Start)} – {ReportPalette.Clock(w.End)}") { FontWeight = FontWeights.SemiBold });
        if (moment is not null)
        {
            head.Inlines.Add(new Run("   " + moment.Title) { Foreground = ReportPalette.Brush(FightTimelineChart.MomentColor(moment.Kind)), FontWeight = FontWeights.SemiBold });
        }
        else if (w.Start <= 0.01 && w.End >= _r.Seconds - 0.01)
        {
            head.Inlines.Add(new Run("   The whole fight"));
        }

        root.Children.Add(head);
        foreach (string why in moment?.Why ?? Array.Empty<string>())
        {
            root.Children.Add(Muted("• " + why, 12.5, margin: new Thickness(0, 3, 0, 0)));
        }

        double usual = _r.AverageDps > 0 ? _r.AverageDps : _r.PartyDps;
        var stats = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        void Stat(string label, string value, Brush? color = null)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 26, 6) };
            cell.Children.Add(Muted(label, 11));
            var v = new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold };
            if (color is not null)
            {
                v.Foreground = color;
            }

            cell.Children.Add(v);
            stats.Children.Add(cell);
        }

        Stat("Party DPS here", N(w.Dps), usual > 0 ? (w.Dps >= usual ? GoodBrush : BadBrush) : null);
        Stat("Against the usual", usual > 0 ? Pct(100 * w.Dps / usual) : "–");
        Stat("Damage", N(w.Damage));
        if (_r.Stagger.HasData)
        {
            Stat("Stagger taken off", N(w.Players.Sum(p => p.Stagger)), ReportPalette.Brush(StaggerColor));
            if (w.Breaks > 0)
            {
                Stat("Breaks", w.Breaks.ToString(En), ReportPalette.Brush(StaggerColor));
            }
        }

        if (w.BossCasts.Count > 0)
        {
            Stat("Boss casts", string.Join(", ", w.BossCasts.Take(3).Select(Aion2SkillNames.Display)));
        }

        root.Children.Add(stats);
        if (w.Players.Count == 0)
        {
            root.Children.Add(Muted("Nobody of the party hit anything that counts here.", 12.5));
            return root;
        }

        // a line per player: name, damage, DPS against their usual, stagger, deaths, then their skills there
        long top = Math.Max(1, w.Players.Max(p => p.Damage));
        foreach (WindowPlayer wp in w.Players)
        {
            ReportPlayer? player = _r.PlayerOf(wp.Player)?.Player;
            var line = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_r.Stagger.HasData ? 90 : 0) });
            AddAt(line, NameCell(player, 13.5), 0);
            AddAt(line, Bar(100.0 * wp.Damage / top, ReportPalette.Brush(ClassBrushes.ClassColor(player?.ClassName ?? ""), 0xC0)), 1);
            AddAt(line, Right(N(wp.Damage), bold: true), 2);
            double change = wp.Usual > 0 ? wp.Dps / wp.Usual : 0;
            TextBlock rate = Right(wp.Usual > 0 ? $"{N(wp.Dps)} · {(change >= 1 ? "+" : "−")}{Math.Abs(Math.Round(100 * (change - 1))):0}%" : N(wp.Dps), color: wp.Usual > 0 ? (change >= 1 ? GoodBrush : BadBrush) : null, size: 12.5);
            rate.ToolTip = $"DPS here; their DPS over the whole fight is {N(wp.Usual)}";
            AddAt(line, rate, 3);
            if (_r.Stagger.HasData)
            {
                TextBlock st = Right(wp.Stagger > 0 ? N(wp.Stagger) : "–", color: ReportPalette.Brush(StaggerColor), size: 12.5);
                st.ToolTip = "Stagger they took off the boss's bar here";
                AddAt(line, st, 4);
            }

            root.Children.Add(line);
            var skills = new WrapPanel { Margin = new Thickness(27, 4, 0, 2) };
            foreach (WindowSkill sk in wp.Skills.Take(14))
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
                item.Children.Add(IconBox(SkillIcon(sk.SkillId, sk.Name, player?.ClassName), 22));
                var label = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
                if (sk.Uses > 1)
                {
                    label.Inlines.Add(new Run($"×{sk.Uses} ") { FontWeight = FontWeights.SemiBold });
                }

                if (sk.Damage > 0)
                {
                    var dmg = new Run(ReportPalette.Short(sk.Damage));
                    dmg.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextMuted");
                    label.Inlines.Add(dmg);
                }

                if (sk.Stagger > 0)
                {
                    label.Inlines.Add(new Run($" ◆{sk.Stagger}") { Foreground = ReportPalette.Brush(StaggerColor) });
                }

                item.Children.Add(label);
                item.ToolTip = Aion2SkillNames.Display(sk.Name) + "\n" + (sk.Uses > 0 ? $"Used {sk.Uses} time{(sk.Uses == 1 ? "" : "s")} here · " : "") + $"{N(sk.Damage)} damage"
                    + (sk.Stagger > 0 ? $" · {sk.Stagger} stagger" : "");
                skills.Children.Add(item);
            }

            if (wp.Deaths > 0)
            {
                skills.Children.Add(new TextBlock { Text = wp.Deaths > 1 ? $"died {wp.Deaths} times" : "died", Foreground = BadBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            }

            root.Children.Add(skills);
        }

        return root;
    }

    /// <summary>
    /// Who staggered the boss (7 November 2026, the owner: "for the stagger I meant for the tab in fights to be edited"): the bar over the fight with every
    /// stagger check marked where its mechanic starts and ends (<see cref="StaggerChart"/>), the checks as chips; under it what was picked - a check (the last
    /// one that failed to begin with) or a stretch dragged over the chart, or the whole fight - with every player's exact amount taken off and the skills
    /// that did it (hover them); then each player's part over the fight and the chosen player's skills by the stagger they caused.
    /// </summary>
    private FrameworkElement StaggerTab()
    {
        var root = new StackPanel();
        StaggerReport st = _r.Stagger;
        if (!st.HasData)
        {
            return Empty(root, "The game sent no stagger bar for this fight's boss (not every boss has one, and a fight from before the meter read it has none).");
        }

        var head = new StackPanel();
        head.Children.Add(Summary(("Stagger taken off", N(st.Total)), ("Breaks", st.Breaks.Count.ToString(En)),
            ("Matched to a skill exactly", Pct(st.ExactPercent))));
        head.Children.Add(Muted("The bar over the fight. Each stagger check is a band from the boss's cast that starts it (its name over the line) to the end of that cast " +
            "(the red dashed line): green when the bar broke in time, red when it didn't. Hover for the bar to the point; click a check, or drag over a stretch, " +
            "to see who took how much off it and with which skills.", 12, margin: new Thickness(18, 0, 18, 6)));
        var chart = new StaggerChart(_r) { Margin = new Thickness(6, 0, 12, 6) };
        head.Children.Add(chart);
        var chipsHost = new ContentControl { Margin = new Thickness(18, 2, 18, 8), Focusable = false };
        head.Children.Add(chipsHost);
        root.Children.Add(Card(head));

        // what was picked: a check (its card: the bar over the cast's time, every player with the exact amount and the skills, hover them) or a stretch
        var people = _r.Players.Select(p => new StaggerPerson(p.Player.Id, p.Player.Name, p.Player.ClassName)).ToList();
        StaggerPerson NameOf(int id) => new(id, _r.PlayerOf(id)?.Player.Name ?? "#" + id, _r.PlayerOf(id)?.Player.ClassName ?? "");
        var pickedTitle = Section("", "");
        var pickedHost = new ContentControl { Focusable = false };
        root.Children.Add(pickedTitle);
        root.Children.Add(pickedHost);
        void Show(double from, double to, StaggerCheck? check)
        {
            chart.Select(from, to, check);
            bool whole = check is null && from <= 0.01 && to >= _r.Seconds - 0.01;
            int index = check is null ? -1 : _r.StaggerChecks.ToList().FindIndex(c => c.Start == check.Start);
            chipsHost.Content = _r.StaggerChecks.Count == 0 ? null : StaggerCardView.Chips(_r.StaggerChecks, check, null, overlay: false,
                c => Show(chart.Span(c).From, chart.Span(c).To, c), () => Show(0, _r.Seconds, null), whole,
                c => ReportPalette.Clock((c.Start - _r.StaggerCheckStart).TotalSeconds));
            int at = root.Children.IndexOf(pickedTitle);
            root.Children.RemoveAt(at);
            pickedTitle = check is not null
                ? Section($"Check {index + 1} at {ReportPalette.Clock(from)}: {StaggerCardView.Title(check)}", "who took how much off it, with which skills (hover them)")
                : whole ? Section("The whole fight", "who took how much off the bar, with which skills (hover them)")
                : Section($"{Stamp(from)} to {Stamp(to)} ({to - from:0.0} s)", "who took how much off the bar in this stretch, with which skills (hover them)");
            root.Children.Insert(at, pickedTitle);
            pickedHost.Content = StaggerPicked(from, to, check, people, NameOf);
        }

        chart.Picked += Show;
        StaggerCheck? first = _r.StaggerChecks.LastOrDefault(c => c.Outcome == StaggerOutcome.Failed) ?? _r.StaggerChecks.LastOrDefault();
        if (first is not null)
        {
            Show(chart.Span(first).From, chart.Span(first).To, first);
        }
        else
        {
            Show(0, _r.Seconds, null);
        }

        var players = _r.Players.Where(p => st.Of(p.Player.Id) is not null).OrderByDescending(p => st.Of(p.Player.Id)!.Amount).ToList();
        if (players.Count == 0)
        {
            return root;
        }

        root.Children.Add(Section("Stagger by player", "the amount each took off the boss's bar over the whole fight"));
        root.Children.Add(PlayersTable(players, p => st.Of(p.Player.Id)?.Amount ?? 0,
            new Col("Stagger", 100, p => N(st.Of(p.Player.Id)?.Amount ?? 0), Bold: true, Color: _ => ReportPalette.Brush(StaggerColor)),
            new Col("Share", 80, p => Pct(st.Of(p.Player.Id)?.Share ?? 0)),
            new Col("Breaks", 80, p => (st.Of(p.Player.Id)?.Breaks ?? 0) is int b and > 0 ? b.ToString(En) : "–", Tip: _ => "Breaks they took the last of the bar for"),
            new Col("Per minute", 100, p => (60.0 * (st.Of(p.Player.Id)?.Amount ?? 0) / Math.Max(1, _r.Seconds)).ToString("0", En))));
        if (Selected(players) is { } sel && st.Of(sel.Player.Id) is { } line)
        {
            root.Children.Add(PlayerHeading("Stagger by skill", players));
            var skills = line.Skills.Select(s => new ReportSkill(s.Name, s.SkillId, s.Amount, line.Amount > 0 ? 100.0 * s.Amount / line.Amount : 0, 0, s.Hits, s.Hits * 60 / Math.Max(1, _r.Seconds), 0,
                null, null, null, null, null, null, null, null, s.Hits > 0 ? (double)s.Amount / s.Hits : 0, 0)).ToList();
            var cols = new[]
            {
                new SCol("", "Stagger", 90, s => N(s.Total), Bold: true), new SCol("", "Share", 80, s => Pct(s.Share)),
                new SCol("", "Hits that staggered", 140, s => N(s.Hits)), new SCol("", "Per hit", 80, s => s.Average.ToString("0.#", En)),
            };
            root.Children.Add(Card(SkillTable(skills, sel.Player.ClassName, ReportPalette.Brush(StaggerColor), cols, line.Amount, 0, total: false)));
        }

        return root;
    }

    /// <summary>A moment of the fight to the tenth of a second: 1:23.4.</summary>
    private static string Stamp(double seconds)
    {
        double t = Math.Max(0, seconds);
        return ((int)t / 60).ToString(En) + ":" + (t % 60).ToString("00.0", En);
    }

    /// <summary>
    /// What was picked on the stagger chart: a check - its card, the bar over the cast's time with where it started and ended, every player with the
    /// exact amount they took off and the skills that did it (hover them), the skills that hit the boss and took nothing off faded - or a stretch of the
    /// fight (the whole fight too) with each player's exact amount in it.
    /// </summary>
    private FrameworkElement StaggerPicked(double from, double to, StaggerCheck? check, IReadOnlyList<StaggerPerson> people, Func<int, StaggerPerson> nameOf)
    {
        var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        // as wide as the card, up to 900
        var frame = new Grid();
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 900 });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        frame.Children.Add(stack);
        if (check is not null)
        {
            stack.Children.Add(StaggerCardView.Card(check, check.End ?? check.Start.AddSeconds(check.Limit ?? 0), people, nameOf, null, overlay: false));
            return Card(frame);
        }

        StaggerReport st = _r.Stagger;
        bool whole = from <= 0.01 && to >= _r.Seconds - 0.01;
        StaggerReport credit = st;
        if (!whole)
        {
            var hits = st.Hits.Where(h => h.At >= from && h.At <= to).ToList();
            int total = hits.Sum(h => h.Amount);
            string NameOfSkill(int id) => st.Players.SelectMany(p => p.Skills).FirstOrDefault(k => k.SkillId == id)?.Name ?? (id != 0 ? Aion2SkillNames.DisplayOf(id) : "Attack");
            var lines = hits.Where(h => h.Player != 0).GroupBy(h => h.Player)
                .Select(g => new StaggerLine(g.Key, g.Sum(h => h.Amount), total > 0 ? 100.0 * g.Sum(h => h.Amount) / total : 0,
                    st.Breaks.Count(b => b.Player == g.Key && b.At >= from && b.At <= to),
                    g.GroupBy(h => h.SkillId).Select(k => new StaggerSkill(NameOfSkill(k.Key), k.Key, k.Sum(h => h.Amount), k.Count())).OrderByDescending(k => k.Amount).ToList()))
                .OrderByDescending(l => l.Amount).ToList();
            credit = new StaggerReport
            {
                Hits = hits, Players = lines, Total = total, Breaks = st.Breaks.Where(b => b.At >= from && b.At <= to).ToList(),
                Bar = st.Bar.Where(b => b.At >= from && b.At <= to).ToList(),
            };
        }

        var used = _r.StaggerTouches.Where(t => t.At >= from && t.At <= to).GroupBy(t => (t.Player, t.SkillId)).Select(g => new StaggerUse(g.Key.Player, g.Key.SkillId, g.Count())).ToList();
        int breaks = credit.Breaks.Count;
        stack.Children.Add(Muted($"{credit.Total:N0} taken off the boss's stagger bar" + (breaks > 0 ? $", {breaks} break{(breaks == 1 ? "" : "s")}" : "") +
            (whole ? " over the fight" : " in this stretch"), 13, semibold: true, margin: new Thickness(0, 0, 0, 8)));
        stack.Children.Add(StaggerCardView.Rows(credit, used, people, nameOf, overlay: false, compact: false, "seconds from the fight's start", whole ? "over the fight" : "in this stretch"));
        return Card(frame);
    }

    // ---------- the building blocks ----------

    private sealed record Col(string Header, double Width, Func<ReportPlayerLine, string> Text, bool Bold = false, Func<ReportPlayerLine, Brush?>? Color = null,
        Func<ReportPlayerLine, string?>? Tip = null);

    private sealed record SCol(string Group, string Header, double Width, Func<ReportSkill, string> Text, bool Bold = false, string? Tip = null);

    /// <summary>An accuracy with one decimal ("99.4%"), "100%" when nothing was parried.</summary>
    private static string Acc(double? v) => v is double d && double.IsFinite(d) ? (d >= 99.95 ? "100" : d.ToString("0.0", En)) + "%" : "–";

    private const string AccuracyHelp = "Accuracy: the hits that landed in full, out of all the attacks. Against a boss a miss shows as a parry or a block: the hit still lands, " +
        "for about a third to half of its damage. An attack the target dodged (no damage at all) counts as a miss too.";

    private static string AccuracyTip(ReportPlayerLine p)
    {
        int known = p.Skills.Sum(s => s.KnownHits), parried = p.Skills.Sum(s => s.ParriedHits);
        return known == 0 ? "The game said nothing of how their hits landed in this fight." :
            $"{AccuracyHelp}\n\n{p.Player.Name}: {known - parried:N0} of {known + p.Missed:N0} attacks landed in full; {parried:N0} parried or blocked" + (p.Missed > 0 ? $", {p.Missed:N0} dodged." : ".");
    }

    /// <summary>The overheal columns' tooltip.</summary>
    private static string? OverhealTip(ReportPlayerLine p) => p.HealChecked > 0
        ? "Effective: the healing that filled missing hit points. Overheal: the part that went past full health."
        : "No overheal for this fight: it was recorded before the meter read the party's hit points.";

    private static SCol[] DamageColumns(bool full)
    {
        var cols = new List<SCol>
        {
            new("OUTPUT", "Damage", 104, s => N(s.Total), Bold: true), new("OUTPUT", "Share", 70, s => Pct(s.Share)), new("OUTPUT", "DPS", 80, s => N(s.Dps)),
            new("COUNT", "Hits", 60, s => N(s.Hits)), new("COUNT", "Per min", 66, s => s.PerMinute.ToString("0.0", En)),
        };
        if (full)
        {
            cols.Add(new SCol("COUNT", "Uses", 58, s => s.Uses > 0 ? N(s.Uses) : "–"));
            cols.Add(new SCol("COUNT", "Avg interval", 92, s => s.AvgInterval is double g ? g.ToString("0.0", En) + " s" : "–"));
        }

        cols.Add(new SCol("HIT QUALITY", "Accuracy", 82, s => Acc(s.Accuracy), Tip: AccuracyHelp)); // first, so it shows without scrolling
        cols.Add(new SCol("HIT QUALITY", "Crit", 58, s => Pct0(s.Crit)));
        cols.Add(new SCol("HIT QUALITY", "Back", 58, s => Pct0(s.Back)));
        cols.Add(new SCol("HIT QUALITY", "Front", 58, s => Pct0(s.Front)));
        cols.Add(new SCol("HIT QUALITY", "Perfect", 64, s => Pct0(s.Perfect)));
        cols.Add(new SCol("HIT QUALITY", "Double hit", 80, s => Pct0(s.Double)));
        cols.Add(new SCol("HIT QUALITY", "Multi-hit", 74, s => Pct0(s.Multi)));

        cols.Add(new SCol("PER HIT", "Average", 86, s => N(s.Average)));
        cols.Add(new SCol("PER HIT", "Highest", 86, s => N(s.Highest)));
        return cols.ToArray();
    }

    private static Border Card(UIElement child)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16), Child = child, ClipToBounds = true };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        return card;
    }

    private static FrameworkElement Section(string title, string hint)
    {
        var text = new TextBlock { Margin = new Thickness(0, 4, 0, 10) };
        text.Inlines.Add(new Run(title) { FontSize = 17, FontWeight = FontWeights.SemiBold });
        var h = new Run("    " + hint) { FontSize = 12.5 };
        h.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextMuted");
        text.Inlines.Add(h);
        return text;
    }

    private static FrameworkElement Empty(StackPanel root, string message)
    {
        root.Children.Add(Card(Muted(message, 13.5, margin: new Thickness(18, 16, 18, 16))));
        return root;
    }

    private FrameworkElement PlayerHeading(string title, IReadOnlyList<ReportPlayerLine> players)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
        var pills = Pills(players, HorizontalAlignment.Right);
        DockPanel.SetDock(pills, Dock.Right);
        dock.Children.Add(pills);
        dock.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return dock;
    }

    /// <summary>One pill per player (class icon and name); the chosen one is outlined in gold.</summary>
    private FrameworkElement Pills(IEnumerable<ReportPlayerLine> players, HorizontalAlignment align)
    {
        var wrap = new WrapPanel { HorizontalAlignment = align, Margin = new Thickness(0, 0, 0, align == HorizontalAlignment.Left ? 12 : 0) };
        foreach (ReportPlayerLine p in players)
        {
            bool on = p.Player.Id == _selected;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(10, 4, 12, 4), Margin = new Thickness(6, 0, 0, 6), Cursor = Cursors.Hand,
                Background = on ? SelectedBrush : ReportPalette.Brush(Colors.White, 0x06),
                BorderBrush = on ? ReportPalette.Brush(Color.FromRgb(0xDC, 0xB8, 0x77), 0xB0) : ReportPalette.Brush(Colors.White, 0x18),
                Child = NameCell(p.Player, 12.5, small: true),
            };
            int id = p.Player.Id;
            pill.MouseLeftButtonUp += (_, _) => Select(id);
            wrap.Children.Add(pill);
        }

        return wrap;
    }

    private static FrameworkElement NameCell(ReportPlayer? player, double size, bool small = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (player is null)
        {
            row.Children.Add(new TextBlock { Text = "?", FontSize = size });
            return row;
        }

        if (ClassIcon(player.ClassName) is { } icon)
        {
            row.Children.Add(new Image { Source = icon, Width = small ? 16 : 20, Height = small ? 16 : 20, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        var name = new TextBlock { Text = player.Name, FontSize = size, FontWeight = small ? FontWeights.Normal : FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (!small && player.IsSelf)
        {
            name.Foreground = ReportPalette.Brush(Color.FromRgb(0xDC, 0xB8, 0x77));
        }

        row.Children.Add(name);
        if (!small && player.CombatPower is int cp)
        {
            row.Children.Add(Muted("  " + N(cp), 12, margin: new Thickness(4, 1, 0, 0)));
        }

        return row;
    }

    /// <summary>The players of a tab: one line each, a bar under it in the class colour scaled to the biggest, the chosen one marked; a click picks a player.</summary>
    private FrameworkElement PlayersTable(IReadOnlyList<ReportPlayerLine> players, Func<ReportPlayerLine, double> barValue, params Col[] cols)
    {
        var stack = new StackPanel();
        Grid Layout()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            foreach (Col c in cols)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c.Width) });
            }

            return grid;
        }

        var header = Layout();
        header.Margin = new Thickness(16, 12, 16, 8);
        AddAt(header, Muted("Player", 12.5), 0);
        for (int i = 0; i < cols.Length; i++)
        {
            AddAt(header, Right(cols[i].Header, muted: true, size: 12.5), i + 2);
        }

        stack.Children.Add(header);
        double max = players.Count == 0 ? 1 : Math.Max(1, players.Max(barValue));
        foreach (ReportPlayerLine p in players)
        {
            bool on = p.Player.Id == _selected;
            var grid = Layout();
            grid.Margin = new Thickness(on ? 13 : 16, 10, 16, 10);
            grid.SetValue(TextElement.FontFamilyProperty, _look.FontFamily);
            AddAt(grid, NameCell(p.Player, 14), 0);
            var link = new TextBlock { Text = on ? "Viewing" : "View skills →", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            if (on)
            {
                link.Foreground = ReportPalette.Brush(Color.FromRgb(0xDC, 0xB8, 0x77));
            }
            else
            {
                link.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            }

            AddAt(grid, link, 1);
            for (int i = 0; i < cols.Length; i++)
            {
                TextBlock cell = Right(cols[i].Text(p), bold: cols[i].Bold, color: cols[i].Color?.Invoke(p), size: cols[i].Bold ? 14.5 : 13.5);
                if (cols[i].Tip?.Invoke(p) is { } tip)
                {
                    cell.ToolTip = tip;
                }

                AddAt(grid, cell, i + 2);
            }

            // the bar behind the name and link, in the class colour and the meter's own bar style (Meter look)
            ProgressBar bar = MeterRowView.BarFor(_look, p.Player.ClassName, barValue(p) / max);
            bar.Margin = new Thickness(on ? 0 : 3, 1, 0, 1);
            var holder = new Grid();
            // the bar spans the player column and the link column
            var barHost = new Grid();
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cols.Sum(c => c.Width) + 16) });
            Grid.SetColumnSpan(bar, 2);
            barHost.Children.Add(bar);
            holder.Children.Add(barHost);
            holder.Children.Add(grid);

            var row = new Border
            {
                Child = holder, Cursor = Cursors.Hand, BorderThickness = new Thickness(on ? 3 : 0, 1, 0, 0),
                Background = on ? SelectedBrush : Brushes.Transparent,
            };
            if (on)
            {
                row.BorderBrush = ReportPalette.Brush(Color.FromRgb(0xDC, 0xB8, 0x77));
            }
            else
            {
                row.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            }

            if (!on)
            {
                row.MouseEnter += (_, _) => row.Background = HoverBrush;
                row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            }

            int id = p.Player.Id;
            row.MouseLeftButtonUp += (_, _) => Select(id);
            stack.Children.Add(row);
        }

        return Card(stack);
    }

    /// <summary>A table of skills with grouped headers (output, count, hit quality, per hit), an icon and a share bar on every line and a total line.</summary>
    private static FrameworkElement SkillTable(IReadOnlyList<ReportSkill> skills, string? className, Brush barBrush, SCol[] cols, long sum, double totalRate, bool total = true)
    {
        const double nameWidth = 250;
        double width = nameWidth + cols.Sum(c => c.Width) + 36;
        var stack = new StackPanel { MinWidth = width, Margin = new Thickness(10, 6, 10, 10) };
        Grid Layout()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = nameWidth });
            foreach (SCol c in cols)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c.Width) });
            }

            return grid;
        }

        // the group names over their columns
        if (cols.Any(c => c.Group.Length > 0))
        {
            var groups = Layout();
            groups.Margin = new Thickness(8, 6, 8, 0);
            for (int i = 0; i < cols.Length;)
            {
                int j = i;
                while (j < cols.Length && cols[j].Group == cols[i].Group)
                {
                    j++;
                }

                if (cols[i].Group.Length > 0)
                {
                    var label = Muted(cols[i].Group, 10.5, semibold: true);
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    Grid.SetColumnSpan(label, j - i);
                    AddAt(groups, label, i + 1);
                }

                i = j;
            }

            stack.Children.Add(groups);
        }

        var header = Layout();
        header.Margin = new Thickness(8, 6, 8, 6);
        AddAt(header, Muted("Skill", 12.5), 0);
        for (int i = 0; i < cols.Length; i++)
        {
            TextBlock head = Right(cols[i].Header, muted: true, size: 12.5);
            if (cols[i].Tip is { } tip)
            {
                head.ToolTip = tip;
                head.Cursor = Cursors.Help;
                ToolTipService.SetInitialShowDelay(head, 150);
                ToolTipService.SetShowDuration(head, 30_000);
            }

            AddAt(header, head, i + 1);
        }

        stack.Children.Add(header);
        double maxShare = skills.Count == 0 ? 1 : Math.Max(0.01, skills.Max(s => s.Share));
        int index = 0;
        foreach (ReportSkill s in skills)
        {
            var grid = Layout();
            grid.Margin = new Thickness(8, 7, 8, 7);
            var name = new Grid();
            name.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            name.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddAt(name, IconBox(SkillIcon(s.SkillId, s.Name, className), 24), 0);
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            var title = new TextBlock { FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis };
            title.Inlines.Add(new Run(s.Name));
            if (s.Absorb)
            {
                title.Inlines.Add(new Run("  Absorb") { Foreground = BlueBrush, FontSize = 12 });
            }

            label.Children.Add(title);
            var track = new Grid { Height = 3, Margin = new Thickness(0, 4, 0, 0) };
            track.Children.Add(new Border { Background = TrackBrush, CornerRadius = new CornerRadius(1.5) });
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, s.Share / maxShare), GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - s.Share / maxShare), GridUnitType.Star) });
            fill.Children.Add(new Border { Background = barBrush, CornerRadius = new CornerRadius(1.5) });
            track.Children.Add(fill);
            label.Children.Add(track);
            AddAt(name, label, 1);
            AddAt(grid, name, 0);
            for (int i = 0; i < cols.Length; i++)
            {
                AddAt(grid, Right(cols[i].Text(s), bold: cols[i].Bold, size: cols[i].Bold ? 14 : 13), i + 1);
            }

            stack.Children.Add(new Border { Child = grid, CornerRadius = new CornerRadius(6), Background = index++ % 2 == 0 ? ReportPalette.Brush(Colors.White, 0x05) : Brushes.Transparent });
        }

        if (total && skills.Count > 0)
        {
            var sums = Layout();
            sums.Margin = new Thickness(8, 10, 8, 4);
            AddAt(sums, new TextBlock { Text = "Total", FontSize = 14, FontWeight = FontWeights.SemiBold }, 0);
            int hits = skills.Sum(s => s.Hits);
            for (int i = 0; i < cols.Length; i++)
            {
                string value = cols[i].Header switch
                {
                    "Damage" or "Healing" or "Absorbed" => N(sum > 0 ? sum : skills.Sum(s => s.Total)),
                    "DPS" => totalRate > 0 ? N(totalRate) : "",
                    "Overheal" => skills.Where(s => !s.Absorb).Sum(s => s.Total) is long healed and > 0 && skills.Any(s => s.Overheal > 0)
                        ? Pct(100.0 * skills.Sum(s => s.Overheal) / healed) : "",
                    "Hits" => N(hits),
                    "Per min" => skills.Sum(s => s.PerMinute).ToString("0.0", En),
                    "Uses" => skills.Sum(s => s.Uses) is int u and > 0 ? N(u) : "",
                    "Average" => hits > 0 ? N((double)skills.Sum(s => s.Total) / hits) : "",
                    "Crit" => Weighted(skills, s => s.Crit),
                    "Back" => Weighted(skills, s => s.Back),
                    "Front" => Weighted(skills, s => s.Front),
                    "Perfect" => Weighted(skills, s => s.Perfect),
                    "Double hit" => Weighted(skills, s => s.Double),
                    "Multi-hit" => Weighted(skills, s => s.Multi),
                    "Accuracy" => skills.Sum(s => s.KnownHits) is int kh and > 0 ? Acc(100.0 * (kh - skills.Sum(s => s.ParriedHits)) / kh) : "",
                    _ => "",
                };
                AddAt(sums, Right(value, bold: cols[i].Bold, size: 14), i + 1);
            }

            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = sums, Margin = new Thickness(0, 4, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            stack.Children.Add(line);
        }

        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack };
    }

    /// <summary>A rate over all the skills, each weighted by its hits; empty when the game said nothing of any.</summary>
    private static string Weighted(IReadOnlyList<ReportSkill> skills, Func<ReportSkill, double?> rate)
    {
        double hits = 0, sum = 0;
        foreach (ReportSkill s in skills)
        {
            if (rate(s) is double r)
            {
                hits += s.Hits;
                sum += r * s.Hits;
            }
        }

        return hits > 0 ? Pct0(sum / hits) : "";
    }

    private static FrameworkElement Summary(params (string Label, string? Value)[] items)
    {
        var wrap = new WrapPanel { Margin = new Thickness(18, 14, 18, 6) };
        foreach (var (label, value) in items)
        {
            if (value is null)
            {
                continue;
            }

            var text = new TextBlock { Margin = new Thickness(0, 0, 26, 0), FontSize = 14 };
            var name = new Run(label + "  ");
            name.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextMuted");
            text.Inlines.Add(name);
            text.Inlines.Add(new Run(value) { FontWeight = FontWeights.SemiBold });
            wrap.Children.Add(text);
        }

        return wrap;
    }

    private Border ChartCard(IReadOnlyList<ReportSeries> series)
    {
        if (series.Count == 0)
        {
            return new Border();
        }

        var chart = new StackedAreaChart(series, _r.BinSeconds, _r.Seconds) { Margin = new Thickness(6, 14, 12, 0) };
        var stack = new StackPanel();
        stack.Children.Add(chart);
        var legend = new WrapPanel { Margin = new Thickness(56, 2, 18, 14) };
        foreach (var (name, color) in chart.Legend)
        {
            legend.Children.Add(LegendItem(name, color, null));
        }

        stack.Children.Add(legend);
        return Card(stack);
    }

    private static FrameworkElement LegendItem(string name, Color color, string? tip, bool dashed = false)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 4), Background = Brushes.Transparent, ToolTip = tip };
        item.Children.Add(new Line
        {
            X1 = 0, X2 = 14, Y1 = 0, Y2 = 0, Stroke = ReportPalette.Brush(color), StrokeThickness = 2.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            StrokeDashArray = dashed ? new DoubleCollection { 2, 1.5 } : null,
        });
        item.Children.Add(new TextBlock { Text = name, FontSize = 12.5 });
        return item;
    }

    /// <summary>Effects in two columns: icon, name (red when harmful) with its time and level, "yours ×n", the uptime bar, its percent and how often it was put on.</summary>
    private FrameworkElement EffectGrid(IReadOnlyList<ReportEffect> effects, int you)
    {
        var grid = TwoColumns(effects.Count);
        grid.Margin = new Thickness(0, 8, 0, 0);
        int i = 0;
        foreach (ReportEffect e in effects)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 22, 5), Background = Brushes.Transparent };
            foreach (double w in new[] { 36.0, double.NaN, 70, 96, 50, 36 })
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            }

            AddAt(row, IconBox(EffectIcon(e.SkillId, e.Name, e.Harmful), 28), 0);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Named(e.Name, e.Harmful));
            names.Children.Add(Muted($"{ReportPalette.Clock(e.Seconds)} · LV {Math.Max(1, e.Level)}", 11.5));
            AddAt(row, names, 1);
            int yours = e.By(you);
            if (yours > 0)
            {
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0), Background = ReportPalette.Brush(Color.FromRgb(0x5B, 0x9B, 0xD5), 0x26), BorderBrush = ReportPalette.Brush(Color.FromRgb(0x5B, 0x9B, 0xD5), 0x66),
                    BorderThickness = new Thickness(1), Child = new TextBlock { Text = $"yours ×{yours}", FontSize = 11, Foreground = ReportPalette.Brush(Color.FromRgb(0x8F, 0xBF, 0xEA)) },
                };
                AddAt(row, pill, 2);
            }

            AddAt(row, Bar(e.UptimePercent, e.Harmful ? BadBrush : GoodBrush), 3);
            AddAt(row, Right(Pct0(e.UptimePercent), muted: true), 4);
            AddAt(row, Right("×" + e.Applications.ToString(En), muted: true), 5);
            if (e.Givers.Count > 0)
            {
                row.ToolTip = "From " + string.Join(", ", e.Givers.Select(g => _r.PlayerOf(g)?.Player.Name ?? "?"));
            }

            Place(grid, row, i++, effects.Count);
        }

        return grid;
    }

    /// <summary>An effect's name: red when it harms, else the text colour.</summary>
    private static TextBlock Named(string name, bool harmful)
    {
        var t = new TextBlock { Text = name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis };
        if (harmful)
        {
            t.Foreground = BadBrush;
        }

        return t;
    }

    private static Grid TwoColumns(int count)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < (count + 1) / 2; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        return grid;
    }

    /// <summary>Fills the left column first, then the right one (reading down, as the lists are sorted).</summary>
    private static void Place(Grid grid, UIElement element, int i, int count)
    {
        int half = (count + 1) / 2;
        Grid.SetColumn(element, i < half ? 0 : 1);
        Grid.SetRow(element, i < half ? i : i - half);
        grid.Children.Add(element);
    }

    private static FrameworkElement Bar(double percent, Brush brush)
    {
        var track = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        track.Children.Add(new Border { Background = TrackBrush, CornerRadius = new CornerRadius(2.5) });
        var fill = new Grid();
        double f = Math.Clamp(percent / 100, 0, 1);
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, f), GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - f), GridUnitType.Star) });
        fill.Children.Add(new Border { Background = brush, CornerRadius = new CornerRadius(2.5) });
        track.Children.Add(fill);
        return track;
    }

    private static FrameworkElement IconBox(ImageSource? icon, double size)
    {
        var box = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
        if (icon is not null)
        {
            box.Background = new ImageBrush(icon) { Stretch = Stretch.UniformToFill };
        }
        else
        {
            box.Background = ReportPalette.Brush(Colors.White, 0x0C);
        }

        return box;
    }

    private static void AddAt(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static TextBlock Muted(string text, double size, bool semibold = false, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal, Margin = margin ?? new Thickness(0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return t;
    }

    private static TextBlock Right(string text, bool bold = false, bool muted = false, Brush? color = null, double size = 13.5)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        t.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        if (color is not null)
        {
            t.Foreground = color;
        }
        else if (muted)
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        }

        return t;
    }

    private static string N(double v) => double.IsFinite(v) ? Math.Round(v).ToString("N0", En) : "–";

    private static string Pct(double v) => double.IsFinite(v) ? v.ToString("0.0", En) + "%" : "–";

    private static string Pct0(double? v) => v is double d && double.IsFinite(d) ? d.ToString("0", En) + "%" : "–";

    private static ImageSource? Picture(string? path) => path is not null && File.Exists(path) ? OverlayPictures.Load(path) : null;

    private static ImageSource? ClassIcon(string className) =>
        Picture(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "classes", "icons", className + ".png"));

    private static ImageSource? SkillIcon(int skillId, string name, string? className) =>
        Picture((skillId != 0 ? Aion2SkillIcons.PathFor(skillId) : null) ?? Aion2SkillIcons.PathForName(name, className));

    /// <summary>An effect's icon: its skill's, a skill of the same name, the item that causes it, else a drawn sign for what its name says (never an empty box).</summary>
    private static ImageSource? EffectIcon(int skillId, string name, bool harmful = false) =>
        Picture(Aion2SkillIcons.PathFor(skillId) ?? Aion2SkillIcons.PathForName(name, null) ?? Aion2Artwork.EffectIconPath(skillId)) ?? StatusIcons.For(name, harmful);
}
