using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The gathering tool's "Picked up" card: every resource gathered, with how many came of each grade (normal, Fine or Fresh, Pure, Radiant) and their share, the
/// total and how many times it was gathered; this run of the meter or all time (see <see cref="Aion2GatherLog"/>). Redrawn once a second while shown, only when
/// something new was counted.
/// </summary>
public sealed class GatherTallyCard : Border
{
    private readonly Grid _table = new();
    // what the game rolled for the gathering under way, before it lands (Aion2GatherLog.Forecast)
    private readonly Border _roll = new() { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1) };
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private readonly Button _session = new(), _allTime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _showSession;
    private string _shown = "";

    public GatherTallyCard()
    {
        var loc = LocalizationManager.Instance;
        SetResourceReference(StyleProperty, "SettingsCard");
        Padding = new Thickness(14, 10, 14, 12);
        // the page scrolls sideways: without a width of its own the hint line would stretch it as wide as the text
        MaxWidth = 800;
        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(0, 0, 0, 12);
        var stack = new StackPanel();

        var head = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        foreach ((Button button, string text, bool session) in new[] { (_session, loc["Gather.Tally.Session"], true), (_allTime, loc["Gather.Tally.AllTime"], false) })
        {
            button.Content = text;
            button.Padding = new Thickness(10, 2, 10, 2);
            button.Margin = new Thickness(6, 0, 0, 0);
            button.Click += (_, _) =>
            {
                _showSession = session;
                _shown = "";
                Refresh();
            };
            buttons.Children.Add(button);
        }

        var reset = new Button { Content = loc["Gather.Tally.Reset"], Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(12, 0, 0, 0) };
        reset.Click += (_, _) =>
        {
            if (ThemedMessageBox.Show(Window.GetWindow(this), loc[_showSession ? "Gather.Tally.Reset.Session" : "Gather.Tally.Reset.All"], loc["Gather.Tally.Title"],
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Aion2GatherLog.Clear(_showSession);
            }
        };
        buttons.Children.Add(reset);
        head.Children.Add(buttons);
        var title = new TextBlock { Text = loc["Gather.Tally.Title"], FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        head.Children.Add(title);
        stack.Children.Add(head);

        var hint = new TextBlock { Text = loc["Gather.Tally.Hint"], TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 6) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(hint);
        // the overlay in the game with this session's tally (GatherOverlayWindow): placed and locked on the Overlays page like the others
        var overlay = new CheckBox { Content = loc["Gather.Overlay.Show"], IsChecked = MeterSettings.Load().ShowGatherOverlay, Margin = new Thickness(0, 0, 0, 8) };
        overlay.Click += (_, _) =>
        {
            var settings = MeterSettings.Load();
            settings.ShowGatherOverlay = overlay.IsChecked == true;
            settings.Save();
        };
        stack.Children.Add(overlay);
        _roll.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
        _roll.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        stack.Children.Add(_roll);
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        stack.Children.Add(_empty);
        stack.Children.Add(_table);
        Child = stack;

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        Refresh();
    }

    private static readonly string[] TierKeys = { "Gather.Tally.Normal", "Gather.Tally.Fine", "Gather.Tally.Pure", "Gather.Tally.Radiant" };

    private void Refresh()
    {
        var loc = LocalizationManager.Instance;
        GatherForecast? forecast = Aion2GatherLog.Forecast();
        string key = $"{Aion2GatherLog.Version}/{_showSession}/{loc.Language}/{forecast?.At.Ticks}";
        if (key == _shown)
        {
            return;
        }

        _shown = key;
        DrawRoll(forecast, loc);
        foreach ((Button button, bool on) in new[] { (_session, _showSession), (_allTime, !_showSession) })
        {
            button.SetResourceReference(Control.BorderBrushProperty, on ? "Brush.Accent" : "Brush.Border");
            button.SetResourceReference(Control.ForegroundProperty, on ? "Brush.Accent" : "Brush.TextMuted");
            button.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }

        IReadOnlyList<GatherTally> tallies = Aion2GatherLog.Tallies(_showSession);
        _table.Children.Clear();
        _table.RowDefinitions.Clear();
        _table.ColumnDefinitions.Clear();
        _empty.Text = loc["Gather.Tally.Empty"];
        _empty.Visibility = tallies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tallies.Count == 0)
        {
            return;
        }

        _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
        for (int c = 0; c < 7; c++)
        {
            _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c < 4 ? 96 : c == 4 ? 62 : 58) });
        }

        int row = 0;
        void Row() => _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(UIElement element, int r, int c)
        {
            Grid.SetRow(element, r);
            Grid.SetColumn(element, c);
            _table.Children.Add(element);
        }

        TextBlock Head(string text, bool right = true)
        {
            var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = right ? TextAlignment.Right : TextAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            return t;
        }

        Row();
        Put(Head(loc["Gather.Tally.Resource"], right: false), row, 0);
        for (int tier = 0; tier < 4; tier++)
        {
            TextBlock h = Head(loc[TierKeys[tier]]);
            h.Foreground = new SolidColorBrush(GatherIcons.TierColor(tier));
            Put(h, row, 1 + tier);
        }

        Put(Head(loc["Gather.Tally.Total"]), row, 5);
        Put(Head(loc["Gather.Tally.Tries"]), row, 6);
        Put(Head(loc["Gather.Tally.Fails"]), row, 7);
        row++;

        foreach (GatherTally tally in tallies)
        {
            Row();
            var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 0, 0, 0) };
            rule.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            Grid.SetColumnSpan(rule, 8);
            Put(rule, row, 0);

            int baseItem = Aion2GatherLog.BaseItemOf(tally.Key);
            var named = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = string.Format(CultureInfo.CurrentCulture, loc["Gather.Tally.LastTip"], tally.Last.ToString("g", CultureInfo.CurrentCulture)) };
            FrameworkElement icon = GatherIcons.Picture(tally.Key, 28);
            icon.Margin = new Thickness(0, 0, 9, 0);
            named.Children.Add(icon);
            var name = new TextBlock { Text = Aion2ItemNames.NameOf(baseItem, loc.Language) ?? tally.Key, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            named.Children.Add(name);
            Put(named, row, 0);

            bool[] has = Aion2GatherLog.TiersOf(tally.Key);
            bool fresh = Aion2GatherLog.IsFresh(tally.Key);
            long total = tally.Total;
            for (int tier = 0; tier < 4; tier++)
            {
                var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
                if (!has[tier])
                {
                    var none = new TextBlock { Text = "–", TextAlignment = TextAlignment.Right };
                    none.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
                    cell.Children.Add(none);
                }
                else
                {
                    // the grade's own picture (its frame shows the grade) beside the count
                    cell.Orientation = Orientation.Horizontal;
                    if (GatherIcons.Of(Aion2GatherLog.ItemOf(tally.Key, tier)) is { } gradeIcon)
                    {
                        cell.Children.Add(new Image { Source = gradeIcon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                    }

                    var numbers = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    cell.Children.Add(numbers);
                    long count = tally.ByTier[tier];
                    var amount = new TextBlock { Text = count.ToString("N0", CultureInfo.CurrentCulture), FontWeight = count > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                        TextAlignment = TextAlignment.Right,
                        Foreground = count > 0 ? new SolidColorBrush(CharacterWindow.GradeColors.GetValueOrDefault(tier + 1, CharacterWindow.GradeColors[1])) : null };
                    if (count == 0)
                    {
                        amount.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
                    }

                    numbers.Children.Add(amount);
                    var share = new TextBlock { FontSize = 10.5, TextAlignment = TextAlignment.Right,
                        Text = (tier == 1 && fresh ? loc["Gather.Tally.Fresh"] + " · " : "") + (total > 0 ? Percent(100.0 * count / total) : "") };
                    share.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                    numbers.Children.Add(share);
                }

                Put(cell, row, 1 + tier);
            }

            var sum = new TextBlock { Text = total.ToString("N0", CultureInfo.CurrentCulture), FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            sum.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            Put(sum, row, 5);
            // the gatherings set about, and those that brought nothing (with their share of the tries)
            var tries = new TextBlock { Text = tally.Tries.ToString("N0", CultureInfo.CurrentCulture), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            tries.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            Put(tries, row, 6);
            var fails = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var failCount = new TextBlock { Text = tally.Fails.ToString("N0", CultureInfo.CurrentCulture), TextAlignment = TextAlignment.Right };
            failCount.SetResourceReference(TextBlock.ForegroundProperty, tally.Fails > 0 ? "Brush.Warning" : "Brush.TextSubtle");
            fails.Children.Add(failCount);
            if (tally.Tries > 0 && tally.Fails > 0)
            {
                var failShare = new TextBlock { Text = Percent(100.0 * tally.Fails / tally.Tries), FontSize = 10.5, TextAlignment = TextAlignment.Right };
                failShare.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                fails.Children.Add(failShare);
            }

            Put(fails, row, 7);
            row++;
        }
    }

    /// <summary>The roll of the gathering under way (its grade's picture, "Ruby: Fine" in the grade's colour, or that it fails), else how to see one and how many were right.</summary>
    private void DrawRoll(GatherForecast? forecast, LocalizationManager loc)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var caption = new TextBlock { Text = loc["Gather.Roll.Title"].ToUpper(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeights.SemiBold };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        text.Children.Add(caption);
        if (forecast is { } roll)
        {
            string name = Aion2ItemNames.NameOf(Aion2GatherLog.BaseItemOf(roll.Key), loc.Language) ?? roll.Key;
            if (!roll.Fails && GatherIcons.Of(Aion2GatherLog.ItemOf(roll.Key, roll.Tier)) is { } icon)
            {
                row.Children.Add(new Image { Source = icon, Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            var result = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                Text = roll.Fails ? string.Format(CultureInfo.CurrentCulture, loc["Gather.Roll.Fails"], name)
                    : string.Format(CultureInfo.CurrentCulture, loc["Gather.Roll.Gives"], name, Aion2GatherLog.GradeName(roll.Key, roll.Tier, k => loc[k])) };
            if (roll.Fails)
            {
                result.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warning");
            }
            else
            {
                result.Foreground = new SolidColorBrush(GatherIcons.TierColor(roll.Tier));
            }

            text.Children.Add(result);
            _roll.SetResourceReference(Border.BorderBrushProperty, roll.Fails ? "Brush.Warning" : "Brush.Accent");
        }
        else
        {
            var idle = new TextBlock { Text = loc["Gather.Roll.Idle"], FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            idle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            text.Children.Add(idle);
            _roll.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        }

        (int rolls, int right) = Aion2GatherLog.RollRecord();
        if (rolls > 0)
        {
            var record = new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, loc["Gather.Roll.Record"], rolls, right), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
            record.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
            text.Children.Add(record);
        }

        row.Children.Add(text);
        _roll.Child = row;
    }

    /// <summary>"12%", "1.4%", "0.35%": two figures that mean something.</summary>
    internal static string Percent(double percent) =>
        (percent >= 10 ? percent.ToString("0", CultureInfo.CurrentCulture) : percent >= 1 ? percent.ToString("0.#", CultureInfo.CurrentCulture) : percent > 0 ? percent.ToString("0.##", CultureInfo.CurrentCulture) : "0") + "%";
}
