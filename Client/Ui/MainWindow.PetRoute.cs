using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>The pet route's switches on the Interactive map page: the guide on or off, the genera, where it starts, the next stop, and the map's shape for the minimap.</summary>
public partial class MainWindow
{
    /// <summary>The route card's content and the map display card's (the page puts the display card first, then the route card, above the gathering and cube cards),
    /// and what refreshes the route's status line (the page's timer calls it).</summary>
    private (UIElement Card, UIElement Display, Action Refresh) PetRouteControls()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        MeterSettings now = MeterSettings.Load();
        var stack = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(ModulePage.Stroked(loc["PetRoute.Title"]));
        TextBlock lede = Wrapped(loc["PetRoute.Lede"], "Brush.TextMuted", 12);
        lede.Margin = new Thickness(0, 2, 0, 8);
        stack.Children.Add(lede);

        void Save(Action<MeterSettings> change)
        {
            MeterSettings s = MeterSettings.Load();
            change(s);
            s.Save();
        }

        CheckBox Check(string label, bool on, Action<MeterSettings, bool> set, string? tip = null)
        {
            var box = new CheckBox { IsChecked = on, Content = ShellText(label, "Brush.Text", 12.5), ToolTip = tip, Margin = new Thickness(0, 0, 18, 6), VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) =>
            {
                Save(s => set(s, box.IsChecked == true));
                _petMapController?.SettingsChanged(); // at once, not at the controller's next read of the settings
            };
            return box;
        }

        stack.Children.Add(Check(loc["PetRoute.On"], now.PetRouteOn, (s, v) => s.PetRouteOn = v));

        // the genera, as the website's chips
        var genera = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        genera.Children.Add(ShellText(loc["PetRoute.Genera"], "Brush.TextMuted", 12));
        ((TextBlock)genera.Children[0]).Margin = new Thickness(0, 0, 10, 0);
        var picked = (now.PetRouteGenera is { Count: > 0 } g ? g : PetRoute.Genera.ToList()).ToHashSet();
        var genusBoxes = new List<CheckBox>();
        foreach (string genus in PetRoute.Genera)
        {
            string id = genus;
            string shown = Aion2Pets.SpeciesShown(Aion2Pets.SpeciesOfGenus(genus), loc.Language); // as the game names it
            CheckBox box = Check(shown, picked.Contains(id), (s, v) =>
            {
                var list = (s.PetRouteGenera is { Count: > 0 } had ? had : PetRoute.Genera.ToList()).Where(x => x != id).ToList();
                if (v)
                {
                    list.Add(id);
                }

                // the last genus stays ticked: none (stored as "no choice") planned every genus again (3 November 2026 review)
                if (list.Count > 0)
                {
                    s.PetRouteGenera = PetRoute.Genera.Where(list.Contains).ToList();
                }
            });
            box.Click += (_, _) =>
            {
                if (genusBoxes.All(b => b.IsChecked != true))
                {
                    box.IsChecked = true;
                }
            };
            genusBoxes.Add(box);
            genera.Children.Add(box);
        }

        stack.Children.Add(genera);
        var start = new WrapPanel();
        start.Children.Add(Check(loc["PetRoute.FromHere"], now.PetRouteFromHere, (s, v) => s.PetRouteFromHere = v, loc["PetRoute.FromHereTip"]));
        start.Children.Add(Check(loc["PetRoute.InList"], now.PetRouteInList, (s, v) => s.PetRouteInList = v, loc["PetRoute.InListTip"]));
        stack.Children.Add(start);

        // the stop being farmed, and the buttons
        TextBlock status = Wrapped("", "Brush.Text", 12.5);
        status.Margin = new Thickness(0, 4, 0, 6);
        stack.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var next = new Button { Content = loc["PetRoute.Next"], Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0), ToolTip = loc["PetRoute.NextTip"] };
        next.Click += (_, _) => _petMapController?.NextStop();
        var again = new Button { Content = loc["PetRoute.Again"], Padding = new Thickness(12, 3, 12, 3), ToolTip = loc["PetRoute.AgainTip"] };
        again.Click += (_, _) => _petMapController?.ReplanHere(keepSkipped: false);
        buttons.Children.Add(next);
        buttons.Children.Add(again);
        stack.Children.Add(buttons);

        // the map's own card (3 November 2026: the switches touched most, on top, apart from the route): the two overlays (the map, and the list of what is near
        // with how many metres away; also in the configuration), the map's shape for the minimap, the compass, how to zoom
        var display = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        display.Children.Add(ModulePage.Stroked(loc["PetMap.DisplayTitle"]));
        TextBlock displayLede = Wrapped(loc["PetMap.DisplayLede"], "Brush.TextMuted", 12);
        displayLede.Margin = new Thickness(0, 2, 0, 8);
        display.Children.Add(displayLede);
        var parts = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        TextBlock partsLabel = ShellText(loc["PetMap.PartsLabel"], "Brush.TextMuted", 12);
        partsLabel.Margin = new Thickness(0, 0, 10, 6);
        partsLabel.VerticalAlignment = VerticalAlignment.Center;
        parts.Children.Add(partsLabel);
        parts.Children.Add(Check(loc["PetMap.PartMap"], now.PetMapShowMap, (s, v) => s.PetMapShowMap = v, loc["PetMap.PartMapTip"]));
        parts.Children.Add(Check(loc["PetMap.PartList"], now.PetMapShowList, (s, v) => s.PetMapShowList = v, loc["PetMap.PartListTip"]));
        display.Children.Add(parts);

        // the map's shape, to lay it over the game's minimap
        var shape = new WrapPanel();
        shape.Children.Add(Check(loc["PetRoute.Round"], now.PetMapRound, (s, v) => s.PetMapRound = v));
        shape.Children.Add(Check(loc["PetRoute.MarksOnly"], now.PetMapMarkersOnly, (s, v) => s.PetMapMarkersOnly = v, loc["PetRoute.MarksOnlyTip"]));
        shape.Children.Add(Check(loc["PetMap.Compass"], now.PetMapCompass, (s, v) => s.PetMapCompass = v, loc["PetMap.CompassTip"]));
        shape.Children.Add(Check(loc["PetMap.CompassFade"], now.PetMapCompassFade, (s, v) => s.PetMapCompassFade = v, loc["PetMap.CompassFadeTip"]));
        display.Children.Add(shape);

        // no full map any more: the map itself zooms out to the whole map (mouse wheel or its +/- buttons; PetMapZoom)
        display.Children.Add(Wrapped(loc["PetMap.ZoomHint"], "Brush.TextMuted", 12));

        void Refresh()
        {
            if (!MeterSettings.Load().PetRouteOn)
            {
                status.Text = loc["PetRoute.Off"];
                return;
            }

            if (_petMapController?.Route is not { } plan)
            {
                status.Text = loc["PetRoute.NoRoute"];
                return;
            }

            if (plan.Stops.Count == 0)
            {
                status.Text = loc["PetRoute.Done"];
                return;
            }

            PetRouteStop stop = plan.Stops[0];
            string pets = string.Join(", ", stop.Pets.Select(id => Aion2Pets.PetName(id, loc.Language) ?? PetRoute.NameOf(id) ?? $"#{id}"));
            status.Text = string.Format(CultureInfo.CurrentCulture, loc["PetRoute.Stop"], stop.Step, plan.Stops.Count, stop.Zone) + ": " + pets;
        }

        Refresh();
        return (stack, display, Refresh);
    }
}
