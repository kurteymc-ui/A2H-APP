using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// The Black Cloud Merchant card of the Pets page: the two notes (a ticked pet on offer, the offers renewed), the merchant's pets that can't be farmed out
/// in the world in their four groups (only from a field boss, only in dungeons, only chests and shops, special: <see cref="Aion2MerchantPets"/>), each with a
/// tick (no note for an unticked one: finished, or not wanted), where its soul drops and which characters still need it, then each character's last offers
/// with the time they renew. The characters' collections and offers come from <see cref="Aion2BlackCloud"/>.
/// </summary>
public partial class SettingsWindow
{
    private readonly HashSet<int> _blackCloudUnwatched = new();

    /// <summary>The widest the card's lines get before they wrap: the page scrolls sideways, so a long line would otherwise widen the whole page.</summary>
    public const double BlackCloudTextWidth = 860;

    private void InitBlackCloud(MeterSettings settings)
    {
        BlackCloudPopupBox.IsChecked = settings.BlackCloudPopup;
        BlackCloudRenewBox.IsChecked = settings.BlackCloudRenewNote;
        BlackCloudClockBox.IsChecked = settings.BlackCloudClock;
        AddPetAlerts(settings);
        _blackCloudUnwatched.Clear();
        _blackCloudUnwatched.UnionWith(settings.BlackCloudUnwatched);
        FillBlackCloud();
        // while the card is on screen (the Pets page takes the panel out of this window, which is then never shown or closed)
        BlackCloudPets.Loaded += (_, _) =>
        {
            Aion2BlackCloud.Changed -= OnBlackCloudChanged;
            Aion2BlackCloud.Changed += OnBlackCloudChanged;
            FillBlackCloud();
        };
        BlackCloudPets.Unloaded += (_, _) => Aion2BlackCloud.Changed -= OnBlackCloudChanged;
    }

    private void OnBlackCloudChanged() => Dispatcher.BeginInvoke(new Action(FillBlackCloud));

    private void ReadBlackCloud(MeterSettings s)
    {
        s.BlackCloudPopup = BlackCloudPopupBox.IsChecked ?? true;
        s.BlackCloudRenewNote = BlackCloudRenewBox.IsChecked ?? true;
        s.BlackCloudClock = BlackCloudClockBox.IsChecked ?? true;
        s.BlackCloudUnwatched = _blackCloudUnwatched.OrderBy(i => i).ToList();
        s.PetAlertSpecies = _petAlertSpecies.Count == 0 || Aion2Pets.Species.All(_petAlertSpecies.Contains) ? null : Aion2Pets.Species.Where(_petAlertSpecies.Contains).ToList();
        s.ShowMapOnPet = _petNearBox?.IsChecked ?? false;
        s.PetNearMetres = (_petNearRange?.SelectedItem as ComboBoxItem)?.Tag is int metres ? metres : s.PetNearMetres;
    }

    private readonly HashSet<string> _petAlertSpecies = new();
    private CheckBox? _petNearBox;
    private ComboBox? _petNearRange;

    /// <summary>The ranges the map can come up at for a pet near (metres).</summary>
    private static readonly int[] PetNearRanges = { 100, 200, 300, 500, 800, 1000 };

    /// <summary>
    /// The Pets page's alert card (7 November 2026): which genera the pet alerts are for (the Black Cloud pop-up, its renewal note and the map near a pet), and
    /// the map that comes up when the character is near a pet it still needs, with how near; and in the Black Cloud card, marking the card on the shop's page.
    /// Finished pets never alert.
    /// </summary>
    private void AddPetAlerts(MeterSettings settings)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = loc["PetAlerts.Title"] });
        stack.Children.Add(new TextBlock { Style = (Style)FindResource("CardHint"), Text = loc["PetAlerts.Hint"], TextWrapping = TextWrapping.Wrap, MaxWidth = BlackCloudTextWidth,
            HorizontalAlignment = HorizontalAlignment.Left });

        // the genera, each with its colour (the pop-up's chip and the mark on the shop's card); the last one stays ticked
        _petAlertSpecies.Clear();
        _petAlertSpecies.UnionWith(settings.PetAlertSpecies is { Count: > 0 } picked ? picked : Aion2Pets.Species);
        var genera = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
        var boxes = new List<CheckBox>();
        foreach (string species in Aion2Pets.Species)
        {
            string id = species;
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(ShopMarks.SpeciesColor(id)), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0) });
            var name = new TextBlock { Text = Aion2Pets.SpeciesShown(id, loc.Language), VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            label.Children.Add(name);
            var box = new CheckBox { Content = label, IsChecked = _petAlertSpecies.Contains(id), Margin = new Thickness(0, 0, 18, 4), VerticalContentAlignment = VerticalAlignment.Center };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true)
                {
                    _petAlertSpecies.Add(id);
                }
                else if (_petAlertSpecies.Count > 1)
                {
                    _petAlertSpecies.Remove(id);
                }
                else
                {
                    box.IsChecked = true; // none would mean no alert at all: the switches above do that
                }
            };
            boxes.Add(box);
            genera.Children.Add(box);
        }

        stack.Children.Add(genera);

        // the map near a pet: the switch and how near
        var near = new WrapPanel();
        _petNearBox = new CheckBox { Content = loc["PetAlerts.Map"], IsChecked = settings.ShowMapOnPet, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            ToolTip = loc["PetAlerts.MapTip"] };
        near.Children.Add(_petNearBox);
        _petNearRange = new ComboBox { MinWidth = 90, VerticalAlignment = VerticalAlignment.Center };
        int now = PetNearRanges.MinBy(r => Math.Abs(r - settings.PetNearMetres));
        foreach (int metres in PetNearRanges)
        {
            var item = new ComboBoxItem { Content = string.Format(CultureInfo.CurrentCulture, loc["PetAlerts.Within"], metres), Tag = metres };
            _petNearRange.Items.Add(item);
            if (metres == now)
            {
                _petNearRange.SelectedItem = item;
            }
        }

        near.Children.Add(_petNearRange);
        stack.Children.Add(near);
        var how = Small(loc["PetAlerts.MapHint"], "Brush.TextSubtle", 11.5);
        how.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(how);
        var card = new Border { Style = (Style)FindResource("SettingsCard"), Child = stack };
        PanelPets.Children.Insert(Math.Min(1, PanelPets.Children.Count), card);
    }

    /// <summary>The ticked list and the offers, made again (a collection or an offer changed, or a tick).</summary>
    private void FillBlackCloud()
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        BlackCloudPets.Children.Clear();
        BlackCloudOffers.Children.Clear();
        var heading = Small(loc["BlackCloud.OnlyTitle"], "Brush.Text", 12.5, bold: true);
        heading.Margin = new Thickness(0, 0, 0, 4);
        BlackCloudPets.Children.Add(heading);
        foreach (IGrouping<MerchantPetKind, MerchantPet> group in Aion2MerchantPets.All.GroupBy(p => p.Kind))
        {
            var groupTitle = Small(string.Format(CultureInfo.CurrentCulture, loc["BlackCloud.Group." + group.Key], group.Count()), "Brush.Accent", 12, bold: true);
            groupTitle.Margin = new Thickness(0, 8, 0, 2);
            BlackCloudPets.Children.Add(groupTitle);
            foreach (MerchantPet pet in group.OrderBy(p => Aion2Pets.PetName(p.PetId, language) ?? "", StringComparer.CurrentCulture))
            {
                BlackCloudPets.Children.Add(MerchantPetRow(pet, Aion2BlackCloud.Wanted(pet.PetId), language));
            }
        }

        var note = Small(loc["BlackCloud.OnlyNote"], "Brush.TextSubtle", 11);
        note.Margin = new Thickness(0, 6, 0, 0);
        BlackCloudPets.Children.Add(note);

        // each character's last offers, with the time they renew
        var characters = Aion2BlackCloud.Characters().Where(c => c.OffersAt > DateTime.MinValue).ToList();
        var offersTitle = Small(loc["BlackCloud.OffersTitle"], "Brush.Text", 12.5, bold: true);
        offersTitle.Margin = new Thickness(0, 0, 0, 4);
        BlackCloudOffers.Children.Add(offersTitle);
        if (characters.Count == 0)
        {
            BlackCloudOffers.Children.Add(Small(loc["BlackCloud.NoOffers"], "Brush.TextSubtle", 11.5));
            return;
        }

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (BlackCloudCharacter c in characters)
        {
            DateTime seen = c.OffersAt.ToLocalTime();
            string when = string.Format(CultureInfo.CurrentCulture, loc["BlackCloud.Seen"], seen.ToString("g", CultureInfo.CurrentCulture));
            if (c.RenewMs > 0)
            {
                DateTime renew = DateTimeOffset.FromUnixTimeMilliseconds(c.RenewMs).LocalDateTime;
                when += "  ·  " + string.Format(CultureInfo.CurrentCulture, loc[nowMs >= c.RenewMs ? "BlackCloud.RenewedAt" : "BlackCloud.RenewsAt"], renew.ToString("t", CultureInfo.CurrentCulture));
            }

            var head = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 1), MaxWidth = BlackCloudTextWidth, HorizontalAlignment = HorizontalAlignment.Left };
            var who = new Run(c.Name) { FontWeight = FontWeights.SemiBold };
            who.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
            head.Inlines.Add(who);
            var at = new Run("   " + when) { FontSize = 11 };
            at.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextSubtle");
            head.Inlines.Add(at);
            BlackCloudOffers.Children.Add(head);
            string offered = c.Offers.Count == 0 ? loc["BlackCloud.NoPetOffered"]
                : string.Join("  ·  ", c.Offers.Select(o => (Aion2Pets.PetName(o.PetId, language) ?? "#" + o.PetId)
                    + (Aion2MerchantPets.Of(o.PetId) is { } merchant ? " (" + loc["BlackCloud.KindShort." + merchant.Kind] + ")" : "")
                    + " " + o.Price.ToString("N0", CultureInfo.CurrentCulture)));
            var line = Small(offered, c.Offers.Any(o => Aion2MerchantPets.Is(o.PetId)) ? "Brush.Accent" : "Brush.TextSubtle", 11.5);
            line.Margin = new Thickness(12, 0, 0, 2);
            BlackCloudOffers.Children.Add(line);
        }
    }

    /// <summary>One pet of the list: the tick, its picture, its name, genus, where its soul drops (a field boss or dungeon one) and who still needs it.</summary>
    private UIElement MerchantPetRow(MerchantPet pet, BlackCloudWanted w, string language)
    {
        var loc = LocalizationManager.Instance;
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var tick = new CheckBox { IsChecked = !_blackCloudUnwatched.Contains(w.PetId), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = loc["BlackCloud.Tick.Tip"] };
        int id = w.PetId;
        tick.Click += (_, _) =>
        {
            if (tick.IsChecked == true)
            {
                _blackCloudUnwatched.Remove(id);
            }
            else
            {
                _blackCloudUnwatched.Add(id);
            }
        };
        row.Children.Add(tick);
        if (Aion2Pets.PetIconPath(w.PetId) is { } icon)
        {
            var image = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            try
            {
                image.Source = new BitmapImage(new Uri(icon));
                row.Children.Add(image);
            }
            catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or UriFormatException)
            {
                // no picture then
            }
        }

        string species = Aion2Pets.SpeciesOf(w.PetId) is { } key ? Aion2Pets.SpeciesShown(key, language) : ""; // the game's own genus names (English: Cogni, Fera, Natura, Varian, Special)
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = BlackCloudTextWidth - 70 };
        var name = new Run(Aion2Pets.PetName(w.PetId, language) ?? "#" + w.PetId) { FontWeight = FontWeights.SemiBold };
        name.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");
        text.Inlines.Add(name);
        if (species.Length > 0 || pet.WhereIn(language) is not null)
        {
            var source = new Run((species.Length > 0 ? "  ·  " + species : "") + (pet.WhereIn(language) is { } where ? "  ·  " + where : ""));
            source.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextSubtle");
            text.Inlines.Add(source);
        }

        string status = WantedText(w);
        if (status.Length > 0)
        {
            var rest = new Run("  ·  " + status);
            rest.SetResourceReference(TextElement.ForegroundProperty, w.Needed.Count > 0 ? "Brush.Accent" : "Brush.TextSubtle");
            text.Inlines.Add(rest);
        }

        row.Children.Add(text);
        return row;
    }

    /// <summary>"missing: YIELD, Dead · level 2: Fighter", "every character has it", or nothing while no character's pets are known.</summary>
    private static string WantedText(BlackCloudWanted w)
    {
        var loc = LocalizationManager.Instance;
        var parts = w.Needed.GroupBy(n => n.Level).OrderBy(g => g.Key).Select(g => g.Key == 0
            ? string.Format(loc["BlackCloud.Missing"], string.Join(", ", g.Select(n => n.Name)))
            : string.Format(loc["BlackCloud.Level"], g.Key, string.Join(", ", g.Select(n => n.Name)))).ToList();
        if (parts.Count == 0 && w.Done.Count > 0)
        {
            parts.Add(loc["BlackCloud.AllDone"]);
        }

        return string.Join("  ·  ", parts);
    }

    private static TextBlock Small(string text, string brush, double size, bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            MaxWidth = BlackCloudTextWidth, HorizontalAlignment = HorizontalAlignment.Left };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
