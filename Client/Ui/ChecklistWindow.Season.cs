using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Data;

namespace AionDPS.Ui;

/// <summary>The season card's box of characters: every character's Oath Coins and missions done; a click shows that character's missions in the card.</summary>
public sealed partial class ChecklistWindow
{
    /// <summary>The Oath Coins a character has: the live stock for the one online, else what its last login left (<see cref="Aion2Inventories"/>); null when unknown.</summary>
    private static long? OathCoinsHeld(string name)
    {
        if (name == Aion2Tickets.CurrentName && Aion2Stock.HasData)
        {
            return Aion2Stock.Count(SeasonMissions.OathCoin, StockSources.All);
        }

        return Aion2Inventories.Characters().FirstOrDefault(c => c.Name == name) is { } saved
            ? saved.Bag.GetValueOrDefault(SeasonMissions.OathCoin) + saved.Storage.GetValueOrDefault(SeasonMissions.OathCoin)
            : null;
    }

    /// <summary>
    /// One tile per character the meter has seen log in: name, the Oath Coins it has, this week's Oath Coins from the weekly missions (with a bar to what all
    /// of them give), the weekly and season-long missions done. The shown character's tile is edged. Null with fewer than two characters (nothing to choose).
    /// </summary>
    private UIElement? SeasonCharacters(string shown, IReadOnlyList<SeasonMission> missions, DateTime now)
    {
        List<TicketCharacter> characters = Aion2Tickets.Snapshot().Where(c => c.Tickets.Count > 0).ToList();
        if (characters.Count < 2)
        {
            return null;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        CultureInfo culture = CultureInfo.CurrentCulture;
        string? online = Aion2Tickets.CurrentName;
        DateTime lastReset = ChecklistManual.LastWeeklyReset(now);
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        TextBlock caption = Label(loc["Checklist.SeasonChars"], "Brush.TextMuted", 11.5);
        caption.Margin = new Thickness(0, 0, 0, 6);
        box.Children.Add(caption);
        var tiles = new WrapPanel();
        box.Children.Add(tiles);
        foreach (TicketCharacter c in characters)
        {
            IReadOnlyDictionary<int, AchievementValue> counters = Aion2Achievements.Of(c.Name);
            bool active = c.Name == shown;

            var lines = new StackPanel();
            var top = new DockPanel { LastChildFill = true };
            if (c.Name == online)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = loc["Checklist.OnlineNow"] };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Accent");
                DockPanel.SetDock(dot, Dock.Right);
                top.Children.Add(dot);
            }

            string? className = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
            if (className is not null && new ClassIconConverter().Convert(className, typeof(ImageSource), null, culture) is ImageSource icon)
            {
                var image = new Image { Source = icon, Width = 15, Height = 15, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(image, Dock.Left);
                top.Children.Add(image);
            }

            TextBlock name = Label(c.Name, "Brush.Text", 12.5, bold: true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (className is not null)
            {
                name.Foreground = ClassBrushes.Solid(className);
            }

            top.Children.Add(name);
            lines.Children.Add(top);

            // the Oath Coins the character has: now for the one online (the stock, live), else as its last login left them
            if (OathCoinsHeld(c.Name) is { } held)
            {
                var heldLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2), ToolTip = loc["Checklist.SeasonCharHeldTip"] };
                TextBlock amount = Label(held.ToString("N0", culture), "Brush.Text", 14, bold: true);
                amount.Foreground = new SolidColorBrush(SeasonLook.Gold);
                heldLine.Children.Add(amount);
                TextBlock coinName = Label("  " + CraftText.ItemName(SeasonMissions.OathCoin), "Brush.TextSubtle", 11);
                coinName.VerticalAlignment = VerticalAlignment.Bottom;
                coinName.Margin = new Thickness(0, 0, 0, 2);
                heldLine.Children.Add(coinName);
                lines.Children.Add(heldLine);
            }

            string counts;
            if (counters.Count > 0)
            {
                IReadOnlyList<SeasonProgress> progress = SeasonMissions.Progress(SeasonMissions.FactionOf(c.ServerId, counters, missions), counters, lastReset, missions);
                SeasonProgress[] weekly = progress.Where(p => p.Mission.Weekly).ToArray(), season = progress.Where(p => !p.Mission.Weekly).ToArray();
                counts = string.Format(culture, "{0} {1}/{2}  ·  {3} {4}/{5}", loc["Checklist.SeasonWeekly"], weekly.Count(p => p.Finished), weekly.Length,
                    loc["Checklist.SeasonLong"], season.Count(p => p.Finished), season.Length);
                // this week's Oath Coins from the weekly missions: the steps done, of what all of them give, with a bar
                (int earned, int possible) = SeasonMissions.WeeklyCoins(progress);
                if (possible > 0)
                {
                    TextBlock week = Label(string.Format(culture, loc["Checklist.SeasonCharWeek"], earned.ToString("N0", culture), possible.ToString("N0", culture)),
                        earned > 0 ? "Brush.Text" : "Brush.TextSubtle", 11);
                    week.Margin = new Thickness(0, 3, 0, 4);
                    week.TextWrapping = TextWrapping.Wrap;
                    week.ToolTip = loc["Checklist.SeasonCharCoinsTip"];
                    lines.Children.Add(week);
                    lines.Children.Add(SeasonLook.Bar((double)earned / possible, earned >= possible ? SeasonLook.Done : SeasonLook.Gold, 4));
                }
            }
            else
            {
                counts = loc["Checklist.SeasonCharNone"];
            }

            TextBlock done = Label(counts, "Brush.TextMuted", 10.5);
            done.Margin = new Thickness(0, 5, 0, 0);
            done.TextWrapping = TextWrapping.Wrap; // the "no missions seen yet" hint is longer than a tile
            lines.Children.Add(done);

            var tile = new Border
            {
                Width = 214, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(11, 8, 11, 9), Margin = new Thickness(0, 0, 8, 8),
                Child = lines,
            };
            if (active)
            {
                tile.SetResourceReference(Border.BackgroundProperty, "Brush.ControlRaised");
                tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            }
            else
            {
                string pick = c.Name;
                tile.SetResourceReference(Border.BackgroundProperty, "Brush.Control");
                tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                tile.Cursor = Cursors.Hand;
                tile.ToolTip = string.Format(culture, loc["Checklist.SeasonCharPick"], c.Name);
                tile.MouseEnter += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
                tile.MouseLeave += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                tile.MouseLeftButtonDown += (_, e) =>
                {
                    _seasonCharacter = pick;
                    e.Handled = true;
                    Render();
                };
            }

            tiles.Children.Add(tile);
        }

        return box;
    }
}
