using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The meter's own buttons and menus (5 November 2026, the owner, with pictures of another meter): Tools (the buff and cooldown timers, the pet tracker and
/// its list, sound alerts, clearing the fight, the history, finding a player), the short "…" (where everything is, the Ko-fi page, exit), and Find a player:
/// a name and a region, the characters the website's search finds, then a card of the one picked (class, server, Combat Power, Item Level, the best parse
/// of a public fight) with its full page in Character Lookup one click away. The full page stays on the website, the one place it is kept up to date.
/// </summary>
public partial class MainWindow
{
    private const string KofiPage = "https://ko-fi.com/atreia";

    // ------------------------------------------------------------------ Tools and "…"

    private void OnOverlayToolsClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenOverlayMenu((FrameworkElement)sender, OverlayToolsItems());
    }

    /// <summary>The Tools menu (also drawn by render-overlay … tools).</summary>
    internal IList<Control> OverlayToolsItems()
    {
        MeterSettings s = MeterSettings.Load();
        return new Control[]
        {
            OverlayItem("Buff and cooldown timers…", OpenAssistFromMeter),
            OverlayItem("Pet tracker map", TogglePetMap, s.ShowPetMap, !PrivacyConsent.Restricted),
            OverlayItem("Pet tracker list", TogglePetList, s.ShowPetMap && s.PetMapShowList, s.ShowPetMap && !PrivacyConsent.Restricted),
            OverlayItem("Sound alerts", () => ChangeOverlaySetting(x => x.MuteSounds = !x.MuteSounds), !s.MuteSounds),
            new Separator(),
            OverlayItem("Clear current fight", ResetMeter),
            OverlayItem("Fight history", OpenHistory),
            OverlayItem("Find a player", ShowFindPlayer),
        };
    }

    /// <summary>The lines of the meter's own menu "…" (also drawn by render-overlay … menu): where the rest is, nothing more (5 November 2026, the owner:
    /// no huge menu, "just redirect"). The meter's switches are on its Meter look page, the training dummy and Grind in the pill's menu.</summary>
    internal IList<Control> OverlaySettingsItems() => new Control[]
    {
        OverlayItem("Fight details", () =>
        {
            ShowClient();
            _ = OpenFightReportAsync();
        }),
        OverlayItem("History", OpenHistory),
        OverlayItem("Find a player", ShowFindPlayer),
        OverlayItem("Characters", () =>
        {
            ShowClient();
            OpenChecklist(true);
        }),
        OverlayItem("Settings", () =>
        {
            ShowClient();
            if (_shellBuilt)
            {
                ShowShellPage("meterlook");
            }
            else
            {
                OnSettingsClicked(this, new RoutedEventArgs());
            }
        }),
        OverlayItem("What's new", () => OnAnnouncementsClicked(this, new RoutedEventArgs())),
        OverlayItem("Support us on Ko-fi ♥", () => OpenInBrowser(KofiPage)),
        new Separator(),
        OverlayItem("Hide the meter", HideMeterOverlay),
        OverlayItem("Exit Atreia App", Close),
    };

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no browser to open: nothing else to do
        }
    }

    /// <summary>The Skill Assist window (buffs on you, the cooldowns, their places and sizes, every class's skills) from the meter: on its own, without bringing
    /// the meter window up over the game.</summary>
    private void OpenAssistFromMeter()
    {
        if (_assistWindow is not null)
        {
            _assistWindow.Activate();
            return;
        }

        _assistWindow = new AssistWindow(() => _liveSource?.Entities as Aion2EntityDirectory);
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _assistWindow.Owner = this;
        }

        _assistWindow.Closed += (_, _) => _assistWindow = null;
        _assistWindow.Show();
        _assistWindow.Activate();
    }

    /// <summary>The meter's reset (the button, Tools → Clear current fight): the fight on screen goes to the history, a dummy parse starts over.</summary>
    private void ResetMeter()
    {
        _dummy?.Model.Reset();
        _dummyCardHidden = false;
        ClearDamageData();
        RefreshRows();
    }

    private void OnOverlayPetMapClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        TogglePetMap();
        UpdatePetMapButton(MeterSettings.Load());
    }

    /// <summary>The pet tracker's list beside its map, on or off (the map's own switch stays).</summary>
    private void TogglePetList()
    {
        var settings = MeterSettings.Load();
        settings.PetMapShowList = !settings.PetMapShowList;
        settings.Save();
        _petMapController?.SettingsChanged();
        ApplyPetFarm(settings);
        _settingsWindow?.SyncOverlaySwitches(settings);
    }

    /// <summary>The bar's pet tracker button lit while the map is on.</summary>
    private void UpdatePetMapButton(MeterSettings settings)
    {
        OverlayPetMapGlyph.SetResourceReference(TextBlock.ForegroundProperty, settings.ShowPetMap ? "Brush.Accent" : "Brush.TextSubtle");
        OverlayPetMapButton.Visibility = PrivacyConsent.Restricted ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------ Find a player

    private string _findRegion = "";
    private int _findAsked;

    /// <summary>The one server the search keeps (0: every server of the region) and the faction tab of the server picker.</summary>
    private int _findServer;
    private bool _findServersAsmodian;

    /// <summary>The region's block in the game's server numbers (1 NA East, 2 NA West, 3 Europe, 4 LATAM, 5 Asia); 0 for all regions.</summary>
    private static int BlockOf(string region) => region switch { "nae" => 1, "naw" => 2, "eu" => 3, "la" => 4, "as" => 5, _ => 0 };

    private void OnOverlayFindClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (OverlayFind.Visibility == Visibility.Visible)
        {
            CloseFindPlayer();
        }
        else
        {
            ShowFindPlayer();
        }
    }

    private void OnOverlayFindCloseClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseFindPlayer();
    }

    /// <summary>Opens the search under the bar, the region the own server's, the box ready for typing (the meter's window takes the keyboard for it).</summary>
    private void ShowFindPlayer()
    {
        if (_findRegion.Length == 0)
        {
            int server = (_liveSource?.Entities as Aion2EntityDirectory)?.LocalCharacter?.ServerId ?? 0;
            _findRegion = server > 0 ? FindPlayerClient.RegionOf(server) : "all";
            _findServersAsmodian = server / 1000 == 2;
            OverlayFindRegionText.Text = FindPlayerClient.Regions.First(r => r.Code == _findRegion).Name;
            ShowFindServer();
        }

        OverlayFind.Visibility = Visibility.Visible;
        _overlayPeekUntil = DateTime.Now + TimeSpan.FromMinutes(2); // the meter stays up while the search is used
        if (_meterOverlay is { IsVisible: true } overlay && !overlay.IsActive)
        {
            overlay.Activate();
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            OverlayFindBox.Focus();
            Keyboard.Focus(OverlayFindBox);
            OverlayFindBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseFindPlayer()
    {
        _findAsked++; // an answer still on its way is dropped
        OverlayFind.Visibility = Visibility.Collapsed;
        OverlayFindServers.Visibility = Visibility.Collapsed;
        OverlayFindResults.Children.Clear();
        OverlayFindCard.Visibility = Visibility.Collapsed;
        OverlayFindStatus.Visibility = Visibility.Collapsed;
    }

    private void OnOverlayFindTextChanged(object sender, TextChangedEventArgs e) =>
        OverlayFindHint.Visibility = OverlayFindBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnOverlayFindKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = RunFindAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseFindPlayer();
        }
    }

    private void OnOverlayFindRegionClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenOverlayMenu((FrameworkElement)sender, FindPlayerClient.Regions.Select(r => (Control)OverlayItem(r.Name, () =>
        {
            _findRegion = r.Code;
            OverlayFindRegionText.Text = r.Name;
            _findServer = 0; // the servers are the region's
            OverlayFindServers.Visibility = Visibility.Collapsed;
            ShowFindServer();
            if (OverlayFindBox.Text.Trim().Length >= 2)
            {
                _ = RunFindAsync();
            }
        }, r.Code == _findRegion)).ToList());
    }

    /// <summary>The server pill: "All servers" or the one picked; dimmed while all regions are searched (a server belongs to one region).</summary>
    private void ShowFindServer()
    {
        OverlayFindServerText.Text = _findServer > 0 ? Aion2Servers.NameOf(_findServer).Split(" - ")[^1] : "All servers";
        OverlayFindServer.Opacity = BlockOf(_findRegion) > 0 ? 1 : 0.45;
    }

    private void OnOverlayFindServerClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (BlockOf(_findRegion) == 0)
        {
            ShowFindStatus("Pick a region first to search one of its servers. All regions searches every server.");
            return;
        }

        if (OverlayFindServers.Visibility == Visibility.Visible)
        {
            OverlayFindServers.Visibility = Visibility.Collapsed;
            return;
        }

        FillServerPicker();
        OverlayFindServers.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The server picker as on the website: Elyos and Asmodians tabs, "All servers", then the region's servers A to Z. A pick narrows the results to that server
    /// (the search itself is the region's) and searches again when a name is in the box.
    /// </summary>
    private void FillServerPicker()
    {
        var servers = Aion2Servers.LiveServers(BlockOf(_findRegion));
        var stack = new StackPanel();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        foreach (bool asmo in new[] { false, true })
        {
            bool on = asmo == _findServersAsmodian;
            var tab = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(on ? Color.FromArgb(0x99, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                Background = new SolidColorBrush(on ? Color.FromArgb(0x26, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)) };
            var text = new TextBlock { Text = $"{(asmo ? "Asmodians" : "Elyos")}  {servers.Count(x => x.Asmodian == asmo)}", FontSize = 11.5,
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal };
            text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.OverlayText" : "Brush.TextMuted");
            tab.Child = text;
            bool which = asmo;
            tab.MouseLeftButtonUp += (_, ev) =>
            {
                ev.Handled = true;
                _findServersAsmodian = which;
                FillServerPicker();
            };
            tabs.Children.Add(tab);
        }

        stack.Children.Add(tabs);
        var chips = new WrapPanel();
        chips.Children.Add(ServerChip("All servers", 0));
        foreach (var server in servers.Where(x => x.Asmodian == _findServersAsmodian))
        {
            chips.Children.Add(ServerChip(server.Name, server.Id));
        }

        stack.Children.Add(chips);
        OverlayFindServers.Child = stack;
    }

    /// <summary>The picker open on a region (the pictures of the meter).</summary>
    internal void OpenServerPicker(string region, int server, bool asmodian)
    {
        _findRegion = region;
        _findServer = server;
        _findServersAsmodian = asmodian;
        OverlayFindRegionText.Text = FindPlayerClient.Regions.First(r => r.Code == region).Name;
        ShowFindServer();
        FillServerPicker();
        OverlayFindServers.Visibility = Visibility.Visible;
    }

    private Border ServerChip(string name, int id)
    {
        bool on = id == _findServer;
        var chip = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(on ? Color.FromArgb(0x33, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)) };
        var text = new TextBlock { Text = name, FontSize = 11.5 };
        text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Accent" : "Brush.OverlayText");
        chip.Child = text;
        chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        chip.MouseLeave += (_, _) => chip.Background = new SolidColorBrush(on ? Color.FromArgb(0x33, 0xDC, 0xB8, 0x77) : Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
        chip.MouseLeftButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            _findServer = id;
            OverlayFindServers.Visibility = Visibility.Collapsed;
            ShowFindServer();
            if (OverlayFindBox.Text.Trim().Length >= 2)
            {
                _ = RunFindAsync();
            }
        };
        return chip;
    }

    private async Task RunFindAsync()
    {
        string name = OverlayFindBox.Text.Trim();
        if (name.Length < 2)
        {
            ShowFindStatus("Enter at least 2 letters of the name.");
            return;
        }

        int asked = ++_findAsked;
        OverlayFindResults.Children.Clear();
        OverlayFindCard.Visibility = Visibility.Collapsed;
        ShowFindStatus("Searching…");
        FindAnswer answer = await FindPlayerClient.SearchAsync(name, _findRegion.Length > 0 ? _findRegion : "all");
        if (asked != _findAsked)
        {
            return;
        }

        if (answer.Error is { } error)
        {
            ShowFindStatus(error);
            return;
        }

        IReadOnlyList<FoundPlayer> players = _findServer > 0 ? answer.Players.Where(p => p.ServerId == _findServer).ToList() : answer.Players;
        if (players.Count == 0)
        {
            ShowFindStatus(_findServer > 0
                ? $"No character called \"{name}\" on {OverlayFindServerText.Text}" + (answer.Players.Count > 0 ? $" ({answer.Players.Count} on other servers: pick All servers)." : ".")
                : $"No character called \"{name}\" in {OverlayFindRegionText.Text}.");
            return;
        }

        ShowFindResults(players);
        if (players.Count == 1)
        {
            ShowFindCard(players[0]);
        }
    }

    private void ShowFindStatus(string? text)
    {
        OverlayFindStatus.Text = text ?? "";
        OverlayFindStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The characters found, one line each: class, name, level, server and region, Combat Power; a click shows its card.</summary>
    internal void ShowFindResults(IReadOnlyList<FoundPlayer> players)
    {
        ShowFindStatus(players.Count > 1 ? $"{players.Count} characters. Pick one:" : null);
        OverlayFindResults.Children.Clear();
        foreach (FoundPlayer player in players.Take(12))
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Source = player.ClassName is { } cls ? new ClassIconConverter().Convert(cls, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource : null };
            grid.Children.Add(icon);
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock { Text = player.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            nameText.Foreground = player.ClassName is { } c ? ClassBrushes.Text(c) : (Brush)FindResource("Brush.OverlayText");
            who.Children.Add(nameText);
            var line = new TextBlock { Text = (player.Level is int lv ? $"Lv {lv}  ·  " : "") + FindPlayerClient.Line(player), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
            line.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            who.Children.Add(line);
            Grid.SetColumn(who, 1);
            grid.Children.Add(who);
            if (player.CombatPower is int cp)
            {
                var power = new TextBlock { Text = FindPlayerClient.Number(cp), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 2, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC3, 0x5A)) };
                Grid.SetColumn(power, 2);
                grid.Children.Add(power);
            }

            // never fully transparent: in the see-through look Windows gives clicks on such pixels to the game (5 November 2026: the rows could not be clicked)
            var rest = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
            var row = new Border { Child = grid, Padding = new Thickness(7, 4, 7, 4), CornerRadius = new CornerRadius(6), Background = rest, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 2) };
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
            row.MouseLeave += (_, _) => row.Background = rest;
            FoundPlayer picked = player;
            row.MouseLeftButtonUp += (_, ev) =>
            {
                ev.Handled = true;
                ShowFindCard(picked);
            };
            OverlayFindResults.Children.Add(row);
        }
    }

    private FoundPlayer? _findShown;

    /// <summary>The card of one character: its class, name, class · server · region, Combat Power, Item Level and best parse, and its full page on the website.
    /// A character the website has no figures of (not a player of the meter) gets them from its official page a moment later.</summary>
    internal void ShowFindCard(FoundPlayer player, bool asked = false)
    {
        _findShown = player;
        bool reading = false;
        if (!asked && !Headless && (player.CombatPower is null || player.ItemLevel is null))
        {
            reading = true; // "…" until the official page answered
            _ = FillFigures(player);
        }

        var stack = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var frame = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 11, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            Child = new Image { Width = 30, Height = 30, Source = player.ClassName is { } cls ? new ClassIconConverter().Convert(cls, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource : null } };
        head.Children.Add(frame);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = player.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Foreground = player.ClassName is { } c ? ClassBrushes.Text(c) : (Brush)FindResource("Brush.OverlayText");
        who.Children.Add(name);
        var line = new TextBlock { Text = FindPlayerClient.Line(player), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        line.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        who.Children.Add(line);
        Grid.SetColumn(who, 1);
        head.Children.Add(who);
        stack.Children.Add(head);

        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
        string waiting = reading ? "…" : "–";
        tiles.Children.Add(Tile("Combat power", player.CombatPower is int cp ? FindPlayerClient.Number(cp) : waiting, Color.FromRgb(0xE8, 0xC3, 0x5A), null));
        tiles.Children.Add(Tile("Item level", player.ItemLevel is int il ? FindPlayerClient.Number(il) : waiting, null, null));
        tiles.Children.Add(Tile("Best parse", player.BestPercent is int pct ? pct.ToString(CultureInfo.CurrentCulture) + "%" : "–", player.BestPercent is int p ? ParseColour(p) : null,
            player.BestPercent is not null
                ? $"{player.BestBoss}: {(player.BestDps is double dps ? PlayerRow.Compact((long)Math.Round(dps)) + " DPS, " : "")}rank {player.BestRank} of {player.BestOf} {player.ClassName ?? "players"} - the best public parse uploaded with the meter"
                : "No parse: this character's fights were never uploaded with the meter (or are not public)"));
        stack.Children.Add(tiles);

        var open = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(0, 7, 0, 7), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            IsEnabled = player.Page is not null };
        var label = new TextBlock { Text = "Open player page", FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        open.Child = label;
        open.ToolTip = "Everything about the character in Character Lookup on the website: gear, stats, skills, parses";
        open.MouseEnter += (_, _) => open.Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        open.MouseLeave += (_, _) => open.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        open.MouseLeftButtonUp += (_, ev) =>
        {
            ev.Handled = true;
            if (player.Page is { } page)
            {
                OpenInBrowser(page);
            }
        };
        stack.Children.Add(open);

        OverlayFindCard.Child = new Border
        {
            Child = stack, Padding = new Thickness(12, 11, 12, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
        };
        OverlayFindCard.Visibility = Visibility.Visible;
    }

    /// <summary>The colours parse percentiles are known by: grey, green, blue, purple, orange, pink at 99, gold at 100.</summary>
    private static Color ParseColour(int pct) => pct switch
    {
        >= 100 => Color.FromRgb(0xE5, 0xCC, 0x80),
        >= 99 => Color.FromRgb(0xE2, 0x68, 0xA8),
        >= 95 => Color.FromRgb(0xFF, 0x80, 0x00),
        >= 75 => Color.FromRgb(0xA3, 0x35, 0xEE),
        >= 50 => Color.FromRgb(0x00, 0x70, 0xFF),
        >= 25 => Color.FromRgb(0x1E, 0xFF, 0x00),
        _ => Color.FromRgb(0x9D, 0x9D, 0x9D),
    };

    /// <summary>Reads the Combat Power and Item Level the website has none of from the character's official page, then shows the card again with them.</summary>
    private async Task FillFigures(FoundPlayer player)
    {
        (int? cp, int? il) = await FindPlayerClient.FiguresAsync(player);
        if (!ReferenceEquals(_findShown, player) || OverlayFind.Visibility != Visibility.Visible)
        {
            return; // another character was picked, or the search closed
        }

        ShowFindCard(player with { CombatPower = player.CombatPower ?? cp, ItemLevel = player.ItemLevel ?? il }, asked: true);
    }

    private Border Tile(string title, string value, Color? colour, string? tip)
    {
        var stack = new StackPanel();
        var head = new TextBlock { Text = title, FontSize = 10.5 };
        head.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(head);
        var figure = new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        figure.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        if (colour is { } c)
        {
            figure.Foreground = new SolidColorBrush(c);
        }
        else
        {
            figure.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        }

        stack.Children.Add(figure);
        return new Border
        {
            Child = stack, Padding = new Thickness(9, 6, 9, 7), Margin = new Thickness(0, 0, 5, 0), CornerRadius = new CornerRadius(7), ToolTip = tip,
            Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
    }
}
