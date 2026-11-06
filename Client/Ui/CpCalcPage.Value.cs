using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Combat;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// The upgrade advisor (6 November 2026, "an upgrade advisor with market prices"): the steps that cost something, each priced at the market of the character's
/// server - its Kina, and every item it takes at what one sells for there - and ranked by CP per million Kina, the best value first.
/// </summary>
public sealed partial class CpCalcPage
{
    /// <summary>How many of the best-value steps show before "more".</summary>
    private const int ValueShown = 6;

    /// <summary>Whether the advisor's "more" is open (kept while the page rebuilds).</summary>
    private bool _valueOpen;

    /// <summary>One item of a priced step: how many, what one is worth on the market (0: no price, but the character holds enough), how many it holds.</summary>
    internal sealed record PricedPart(int ItemId, double Count, long Each, long Held);

    /// <summary>A step with its whole cost in Kina at market prices.</summary>
    internal sealed record PricedStep(CpStep Step, double Kina, double Items, IReadOnlyList<PricedPart> Parts)
    {
        public double Total => Kina + Items;

        /// <summary>CP per million Kina.</summary>
        public double PerMillion => Total > 0 ? Step.Rank / Total * 1_000_000 : 0;
    }

    /// <summary>
    /// Prices a step at <paramref name="book"/>: an item with a market price counts at it (also what the character holds: it could sell it); one without a
    /// price counts nothing when the character holds enough of it (it can't be sold), else the step can't be priced (null).
    /// </summary>
    internal static PricedStep? Price(CpStep s, MarketBook book, Func<int, long>? held)
    {
        if (s.Cost is not { IsFree: false } cost)
        {
            return null;
        }

        double items = 0;
        var parts = new List<PricedPart>();
        foreach (CpMaterial m in cost.Materials)
        {
            long have = held?.Invoke(m.ItemId) ?? 0;
            long each = book.Items.TryGetValue(m.ItemId, out MarketPrice? p) && p.Value is long v and > 0 ? v : 0;
            if (each == 0 && have < Math.Ceiling(m.Count))
            {
                return null;
            }

            items += m.Count * each;
            parts.Add(new PricedPart(m.ItemId, m.Count, each, have));
        }

        return new PricedStep(s, cost.Kina, items, parts);
    }

    /// <summary>
    /// The advisor's card: the steps that cost something ranked by CP per million Kina at the server's market prices, the best few and the rest behind "more";
    /// nothing when no step costs anything. While the prices load, a line says so (the page draws again when they come).
    /// </summary>
    private UIElement? ValueCard(IReadOnlyList<CpStep> steps)
    {
        var paid = steps.Where(s => s.Cost is { IsFree: false } && s.Rank > 0).ToList();
        if (paid.Count == 0)
        {
            return null;
        }

        var stack = new StackPanel();
        stack.Children.Add(Heading(Loc("CpCalc.Value")));
        MarketBook? book = MainWindow.Headless ? SampleBook(paid) : ExchangePrices.For(_server);
        if (book is null)
        {
            string why = _server <= 0 ? Loc("CpCalc.Value.NoServer") : ExchangePrices.IsLoading(_server) ? Loc("CpCalc.Value.Loading") : Loc("CpCalc.Value.NoPrices");
            stack.Children.Add(Label(why, 12.5, brush: Res("Brush.TextMuted")));
            return Spaced(Card(stack));
        }

        Func<int, long>? held = HeldBy(_character, _server);
        var priced = new List<PricedStep>();
        int unpriced = 0;
        foreach (CpStep s in paid)
        {
            if (Price(s, book, held) is { Total: > 0 } p)
            {
                priced.Add(p);
            }
            else
            {
                unpriced++;
            }
        }

        string age = book.Captured is DateTime at ? Ago(DateTime.UtcNow - (at.Kind == DateTimeKind.Local ? at.ToUniversalTime() : at)) : "";
        stack.Children.Add(Label(age.Length > 0 ? F("CpCalc.Value.Lede", book.Name, age) : F("CpCalc.Value.LedeNoAge", book.Name), 12, brush: Res("Brush.TextMuted"),
            margin: new Thickness(0, -6, 0, 10)));
        if (priced.Count == 0)
        {
            stack.Children.Add(Label(Loc("CpCalc.Value.NonePriced"), 12.5, brush: Res("Brush.TextMuted")));
            return Spaced(Card(stack));
        }

        var ranked = priced.OrderByDescending(p => p.PerMillion).ToList();
        double best = ranked[0].PerMillion;
        for (int i = 0; i < ranked.Count && i < ValueShown; i++)
        {
            stack.Children.Add(ValueRow(i + 1, ranked[i], best));
        }

        if (ranked.Count > ValueShown)
        {
            var rest = new StackPanel { Visibility = _valueOpen ? Visibility.Visible : Visibility.Collapsed };
            for (int i = ValueShown; i < ranked.Count; i++)
            {
                rest.Children.Add(ValueRow(i + 1, ranked[i], best));
            }

            stack.Children.Add(rest);
            int more = ranked.Count - ValueShown;
            var link = new TextBlock { FontSize = 11.5, Foreground = Res("Brush.Accent"), Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(132, 2, 0, 0),
                Text = _valueOpen ? Loc("CpCalc.Steps.Less") : F("CpCalc.Steps.More", more) };
            link.MouseLeftButtonUp += (_, _) =>
            {
                _valueOpen = !_valueOpen;
                rest.Visibility = _valueOpen ? Visibility.Visible : Visibility.Collapsed;
                link.Text = _valueOpen ? Loc("CpCalc.Steps.Less") : F("CpCalc.Steps.More", more);
            };
            stack.Children.Add(link);
        }

        string note = Loc("CpCalc.Value.Note") + (unpriced > 0 ? " " + F("CpCalc.Value.Unpriced", unpriced) : "");
        stack.Children.Add(Label(note, 11.5, brush: Res("Brush.TextMuted"), margin: new Thickness(0, 10, 0, 0)));
        return Spaced(Card(stack));
    }

    private static Border Spaced(Border card)
    {
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    /// <summary>"12 min", "3 h", "2 days".</summary>
    private static string Ago(TimeSpan t) => t.TotalMinutes < 1 ? Loc("CpCalc.Value.Now")
        : t.TotalHours < 1 ? F("CpCalc.Value.Minutes", (int)t.TotalMinutes)
        : t.TotalDays < 1 ? F("CpCalc.Value.Hours", (int)t.TotalHours)
        : F("CpCalc.Value.Days", (int)t.TotalDays);

    /// <summary>One priced step: its place, the gain, its picture, what to do and its whole cost (the parts in the tooltip), the CP per million Kina with a bar
    /// against the best.</summary>
    private UIElement ValueRow(int place, PricedStep p, double best)
    {
        CpStep s = p.Step;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        Add(row, 0, new TextBlock
        {
            Text = place.ToString(CultureInfo.CurrentCulture), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = place == 1 ? Solid(Gold) : Res("Brush.TextSubtle"),
        });
        string gain = s.Certainty == CpCertainty.Range ? Range(s.Gain, s.GainMax, signed: true) : Signed(s.Gain);
        Add(row, 1, new Border
        {
            Background = Tint(Green, 0x1E), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = gain, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res("Brush.Heal") },
        });
        if (StepIcon(s) is { } icon)
        {
            icon.Margin = new Thickness(0, 0, 9, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            Add(row, 2, icon);
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = StepText(s), FontSize = 12.5, Foreground = Res("Brush.Text"), TextWrapping = TextWrapping.Wrap });
        string cost = (s.Cost?.Average == true ? "≈ " : "") + F("CpCalc.Cost.Kina", Compact(p.Total));
        int items = p.Parts.Count(x => x.Each > 0);
        if (p.Kina > 0 && items > 0)
        {
            cost += "  " + F("CpCalc.Value.Split", Compact(p.Kina), Compact(p.Items));
        }
        else if (items > 0)
        {
            cost += "  " + Loc("CpCalc.Value.AllItems");
        }

        text.Children.Add(new TextBlock { Text = cost, FontSize = 11, Foreground = Res("Brush.TextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0), ToolTip = CostTip(p) });
        Add(row, 3, text);

        // the value: CP per million Kina, a bar against the best step
        var value = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        value.Children.Add(new TextBlock
        {
            Text = F("CpCalc.Value.Per", Number(p.PerMillion)), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = place == 1 ? Solid(Gold) : Res("Brush.Text"),
        });
        var track = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 0) };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Res("Brush.Control") });
        track.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = Solid(place == 1 ? Gold : Color.FromArgb(0xB0, Gold.R, Gold.G, Gold.B)),
            Width = Math.Max(3, 138 * (best > 0 ? Math.Clamp(p.PerMillion / best, 0, 1) : 0)),
        });
        value.Children.Add(track);
        Add(row, 4, value);

        var line = new Border { Child = row, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(-6, 0, -6, 1), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent };
        line.MouseEnter += (_, _) => line.Background = Res("Brush.Control");
        line.MouseLeave += (_, _) => line.Background = Brushes.Transparent;
        return line;
    }

    /// <summary>The cost's parts: the Kina, each item (how many, at what, what that makes, what the character holds), the whole.</summary>
    private static string CostTip(PricedStep p)
    {
        var lines = new List<string> { Loc(p.Step.Cost?.Average == true ? "CpCalc.Cost.AverageTip" : "CpCalc.Cost.Tip") };
        if (p.Kina > 0)
        {
            lines.Add(F("CpCalc.Cost.Kina", p.Kina.ToString("N0", CultureInfo.CurrentCulture)));
        }

        foreach (PricedPart part in p.Parts)
        {
            string count = Math.Ceiling(part.Count).ToString("N0", CultureInfo.CurrentCulture);
            string name = LootText.ItemName(part.ItemId);
            string have = part.Held > 0 ? "   " + F("CpCalc.Cost.Have", part.Held.ToString("N0", CultureInfo.CurrentCulture)) : "";
            lines.Add(part.Each > 0
                ? F("CpCalc.Value.PartPriced", count, name, part.Each.ToString("N0", CultureInfo.CurrentCulture), Math.Round(part.Count * part.Each).ToString("N0", CultureInfo.CurrentCulture)) + have
                : F("CpCalc.Value.PartHeld", count, name) + have);
        }

        lines.Add(F("CpCalc.Value.Whole", Math.Round(p.Total).ToString("N0", CultureInfo.CurrentCulture), Number(p.PerMillion)));
        return string.Join("\n", lines);
    }

    /// <summary>Made-up market prices for the pictures (no market is asked): every item the steps take at a price of its own.</summary>
    private static MarketBook SampleBook(IEnumerable<CpStep> steps)
    {
        var items = new Dictionary<int, MarketPrice>();
        foreach (CpMaterial m in steps.SelectMany(s => s.Cost?.Materials ?? Array.Empty<CpMaterial>()))
        {
            long each = 900 + (Math.Abs(m.ItemId) % 13 * 650);
            items[m.ItemId] = new MarketPrice(each, each, 40, each);
        }

        return new MarketBook(2302, "Eldoria", DateTime.UtcNow.AddMinutes(-12), items);
    }
}
