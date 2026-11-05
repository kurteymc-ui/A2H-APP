using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AionDPS.Ui;

/// <summary>The titles of the meter's own three columns, which follow the mode (DPS, rDPS, HPS …; Damage, Heal, Taken in All in One): set by the main window.</summary>
public sealed class MeterTitles : INotifyPropertyChanged
{
    public static MeterTitles Instance { get; } = new();

    private string _rate = "DPS";
    private string _share = "%";
    private string _total = "Total";

    public string Rate => _rate;

    public string Share => _share;

    public string Total => _total;

    public void Set(string rate, string share, string total)
    {
        Change(ref _rate, rate, nameof(Rate));
        Change(ref _share, share, nameof(Share));
        Change(ref _total, total, nameof(Total));
    }

    private void Change(ref string field, string value, string name)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// One line of the meter over the game, built from a <see cref="MeterLook"/> (its bar, size, font and columns) and bound to a
/// <see cref="PlayerRow"/>; or, with <see cref="IsHeader"/>, the line of column titles over them. The look page's preview uses the same lines.
/// </summary>
public sealed class MeterRowView : Grid
{
    public static readonly DependencyProperty LookProperty = DependencyProperty.Register(nameof(Look), typeof(MeterLook), typeof(MeterRowView),
        new PropertyMetadata(null, (d, _) => ((MeterRowView)d).Build()));

    public static readonly DependencyProperty IsHeaderProperty = DependencyProperty.Register(nameof(IsHeader), typeof(bool), typeof(MeterRowView),
        new PropertyMetadata(false, (d, _) => ((MeterRowView)d).Build()));

    public static readonly DependencyProperty ClickableProperty = DependencyProperty.Register(nameof(Clickable), typeof(bool), typeof(MeterRowView),
        new PropertyMetadata(false, (d, _) => ((MeterRowView)d).Build()));

    public MeterLook? Look
    {
        get => (MeterLook?)GetValue(LookProperty);
        set => SetValue(LookProperty, value);
    }

    public bool IsHeader
    {
        get => (bool)GetValue(IsHeaderProperty);
        set => SetValue(IsHeaderProperty, value);
    }

    public bool Clickable
    {
        get => (bool)GetValue(ClickableProperty);
        set => SetValue(ClickableProperty, value);
    }

    /// <summary>A line of the meter was clicked (the main window opens the player's details).</summary>
    public static event Action<PlayerRow>? RowClicked;

    private static readonly ClassIconConverter Icons = new();
    private static readonly BooleanToVisibilityConverter Shown = new();
    private static readonly Dictionary<string, ControlTemplate> BarTemplates = new();
    private static readonly Dictionary<string, Style> BarStyles = new();

    /// <summary>The solid bars are strong colour under the text: a soft dark shadow keeps the names and numbers readable on every class colour.</summary>
    private static readonly DropShadowEffect TextShadow = MakeTextShadow();

    /// <summary>With the solid bars the column titles stand on a darker band, the head of a table.</summary>
    private static readonly Brush HeadBand = MakeHeadBand();

    private static DropShadowEffect MakeTextShadow()
    {
        var shadow = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.75 };
        shadow.Freeze();
        return shadow;
    }

    private static Brush MakeHeadBand()
    {
        var band = new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x00, 0x00));
        band.Freeze();
        return band;
    }

    /// <summary>The template of the meter's lines in this look.</summary>
    public static DataTemplate TemplateFor(MeterLook look, bool clickable)
    {
        var factory = new FrameworkElementFactory(typeof(MeterRowView));
        factory.SetValue(ClickableProperty, clickable);
        factory.SetValue(LookProperty, look);
        var template = new DataTemplate { VisualTree = factory };
        template.Seal();
        return template;
    }

    public MeterRowView()
    {
        MouseLeftButtonUp += (_, e) =>
        {
            if (Clickable && !IsHeader && DataContext is PlayerRow row)
            {
                e.Handled = true;
                RowClicked?.Invoke(row);
            }
        };
    }

    private void Build()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        if (Look is not { } look)
        {
            return;
        }

        LocalizationManager loc = LocalizationManager.Instance;
        TextElement.SetFontFamily(this, look.FontFamily);
        Height = IsHeader ? Math.Round(look.TinySize + 9) : look.RowHeight;
        Margin = new Thickness(0, 0, 0, IsHeader ? 1 : look.Gap);
        bool table = look.Bar == "table"; // the old-school table: plain weights, lowercase units, the combat power before the name
        Background = IsHeader && look.Bar is "solid" or "table" ? HeadBand : Brushes.Transparent;
        Cursor = Clickable && !IsHeader ? System.Windows.Input.Cursors.Hand : null;
        if (!IsHeader)
        {
            var bar = new ProgressBar
            {
                Template = BarTemplate(look.Bar), Style = BarStyle(look.Bar), Minimum = 0, Maximum = 100, IsHitTestVisible = false,
                Background = new SolidColorBrush(look.Bar switch { "glow" => Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), "flat" => Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF),
                    "solid" => Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), "capsule" => Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF), "thread" => Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
                    "table" => Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF),
                    _ => Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF) }),
            };
            bar.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding(nameof(PlayerRow.FillPercent)) { Mode = BindingMode.OneWay });
            Children.Add(bar);
            if (look.BarParts)
            {
                // after the class part: grey for the buffs given to the others, green for the healing on them (MeterBarParts)
                var parts = new MeterBarPartsView { Shape = look.Bar };
                parts.SetBinding(MeterBarPartsView.OwnProperty, new Binding(nameof(PlayerRow.FillPercent)) { Mode = BindingMode.OneWay });
                parts.SetBinding(MeterBarPartsView.GivenEndProperty, new Binding(nameof(PlayerRow.GivenFillPercent)) { Mode = BindingMode.OneWay });
                parts.SetBinding(MeterBarPartsView.HealEndProperty, new Binding(nameof(PlayerRow.HealFillPercent)) { Mode = BindingMode.OneWay });
                Children.Add(parts);
            }
            if (look.Bar is "glow" or "flat")
            {
                var edge = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
                edge.SetBinding(Border.BackgroundProperty, new Binding(nameof(PlayerRow.ClassEdgeBrush)));
                Children.Add(edge);
            }
        }

        var line = new Grid { Margin = new Thickness(look.Bar == "capsule" ? 13 : 10, 0, look.Bar == "capsule" ? 12 : 10, 0) };
        if (!IsHeader && look.Bar == "solid")
        {
            line.Effect = TextShadow; // one shadow for the whole line, not one per text
        }

        Children.Add(line);
        void Add(UIElement element, GridLength width, string? shared = null)
        {
            var column = new ColumnDefinition { Width = width };
            if (shared is not null)
            {
                column.SharedSizeGroup = shared;
            }

            line.ColumnDefinitions.Add(column);
            SetColumn(element, line.ColumnDefinitions.Count - 1);
            line.Children.Add(element);
        }

        TextBlock Text(double size, string brush, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Right)
        {
            var text = new TextBlock { FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis };
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            if (bold)
            {
                text.FontWeight = FontWeights.SemiBold;
            }

            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return text;
        }

        TextBlock Title(string text, string? tip, HorizontalAlignment align = HorizontalAlignment.Right)
        {
            TextBlock title = Text(look.TinySize, "Brush.TextMuted", !table, align);
            title.Text = text;
            title.ToolTip = tip;
            return title;
        }

        // a figure of the row: as one text, or (small units) its digits and after them its unit, smaller and dimmer, on the same baseline
        void BindFigure(TextBlock cell, string path)
        {
            if (table)
            {
                cell.SetBinding(TextBlock.TextProperty, new Binding(path) { Converter = FigureParts.Lower, Mode = BindingMode.OneWay });
                return;
            }

            if (!look.SmallUnits)
            {
                cell.SetBinding(TextBlock.TextProperty, new Binding(path));
                return;
            }

            var digits = new Run();
            digits.SetBinding(Run.TextProperty, new Binding(path) { Converter = FigureParts.Digits, Mode = BindingMode.OneWay });
            var unit = new Run { FontSize = Math.Round(cell.FontSize * 0.78, 1), FontWeight = FontWeights.Normal };
            unit.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextMuted");
            unit.SetBinding(Run.TextProperty, new Binding(path) { Converter = FigureParts.Unit, Mode = BindingMode.OneWay });
            cell.Inlines.Add(digits);
            cell.Inlines.Add(unit);
        }

        // rank, class icon and the name side
        if (look.Has("rank"))
        {
            TextBlock rank = IsHeader ? Title("#", null) : Text(look.SmallSize, "Brush.TextSubtle", true);
            if (!IsHeader)
            {
                rank.SetBinding(TextBlock.TextProperty, new Binding(nameof(PlayerRow.RankDisplay)));
            }

            rank.Margin = new Thickness(0, 0, 6, 0);
            rank.MinWidth = Math.Round(14 * look.TextScale);
            Add(rank, GridLength.Auto);
        }

        if (look.Has("icon"))
        {
            var icon = new Image { Width = look.IconSize, Height = look.IconSize, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality); // smooth at 15-21 px (the icons are 128 px)
            if (!IsHeader)
            {
                icon.SetBinding(Image.SourceProperty, new Binding(nameof(PlayerRow.ClassName)) { Converter = Icons });
            }

            Add(icon, GridLength.Auto);
        }

        if (IsHeader)
        {
            Add(Title(loc["Look.ColName"], null, HorizontalAlignment.Left), new GridLength(1, GridUnitType.Star));
        }
        else
        {
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            if (table && look.Has("cp"))
            {
                // a table's way: the combat power in front of the name ("191.2k YIELD"), as a gear score stands there
                TextBlock power = Text(look.NameSize, "Brush.TextMuted", align: HorizontalAlignment.Left);
                power.Margin = new Thickness(0, 0, 6, 0);
                power.SetBinding(TextBlock.TextProperty, new Binding(nameof(PlayerRow.CombatPowerText)) { Converter = FigureParts.Lower, Mode = BindingMode.OneWay });
                name.Children.Add(power);
            }

            TextBlock who = Text(look.NameSize, "Brush.OverlayText", !table, HorizontalAlignment.Left);
            who.SetBinding(TextBlock.TextProperty, new Binding(nameof(PlayerRow.NameDisplay)));
            if (look.Has("classcolor"))
            {
                who.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(PlayerRow.ClassTextBrush)));
            }

            name.Children.Add(who);
            if (look.Has("cp") && !table)
            {
                TextBlock cp = Text(look.TinySize, "Brush.TextMuted", align: HorizontalAlignment.Left);
                cp.Margin = new Thickness(7, 1, 0, 0);
                cp.SetBinding(TextBlock.TextProperty, new Binding(nameof(PlayerRow.CombatPowerText)));
                name.Children.Add(cp);
            }

            // no party buffs after the name any more (5 November 2026, the owner: "Undefeated Mantra · Predation" made every line long for no reason);
            // the meter window's list keeps them (Settings), the rDPS and Given columns say what the buffs were worth
            var nameSide = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            if (look.BarParts)
            {
                // the "+" figures of the bar's parts at the end of the name side: what the buffs gave the others (grey; the Given column says it when
                // shown) and the healing on the others (green), per second. They take their room from the name, not from the meter's width (6 November
                // 2026, the owner: "the meter should not exceed the size set by the user").
                var plus = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                void Plus(string path, Brush brush)
                {
                    TextBlock figure = Text(look.TinySize, "Brush.TextMuted", bold: !table);
                    figure.Foreground = brush;
                    figure.Margin = new Thickness(6, 0, 0, 0);
                    figure.SetBinding(TextBlock.TextProperty, new Binding(path) { Converter = table ? FigureParts.Lower : null, Mode = BindingMode.OneWay });
                    figure.SetBinding(VisibilityProperty, new Binding(path) { Converter = FilledToVisible.Instance, Mode = BindingMode.OneWay });
                    plus.Children.Add(figure);
                }

                if (!look.Has("given"))
                {
                    Plus(nameof(PlayerRow.BarGivenText), MeterBarParts.GivenText);
                }

                Plus(nameof(PlayerRow.BarHealText), MeterBarParts.HealText);
                DockPanel.SetDock(plus, Dock.Right);
                nameSide.Children.Add(plus);
            }

            nameSide.Children.Add(name);
            Add(nameSide, new GridLength(1, GridUnitType.Star));
        }

        // the columns, in their fixed order
        foreach (MeterColumn column in MeterLook.ColumnsOf(look))
        {
            string? tip = column.TipKey.Length > 0 ? loc[column.TipKey] : null;
            switch (column.Id)
            {
                case "dps":
                {
                    TextBlock rate = IsHeader ? Title("", tip) : Text(table ? look.SmallSize : look.RateSize, "Brush.OverlayText", !table);
                    rate.Margin = new Thickness(8, 0, 0, 0);
                    rate.FontWeight = table ? FontWeights.Normal : IsHeader ? FontWeights.SemiBold : FontWeights.Bold;
                    if (IsHeader)
                    {
                        rate.SetBinding(TextBlock.TextProperty, new Binding(nameof(MeterTitles.Rate)) { Source = MeterTitles.Instance });
                        if (table)
                        {
                            rate.TextDecorations = TextDecorations.Underline; // the column the table is ordered by
                        }
                    }
                    else
                    {
                        BindFigure(rate, column.Path);
                    }

                    Add(rate, GridLength.Auto, "Rate");
                    break;
                }

                case "share" or "total":
                {
                    TextBlock cell = IsHeader ? Title("", tip) : Text(column.Id == "share" || table ? look.SmallSize : Math.Round(12 * look.NameSize / 13, 1), table ? "Brush.OverlayText" : "Brush.TextSubtle");
                    if (IsHeader)
                    {
                        cell.SetBinding(TextBlock.TextProperty, new Binding(column.Id == "share" ? nameof(MeterTitles.Share) : nameof(MeterTitles.Total)) { Source = MeterTitles.Instance });
                    }
                    else
                    {
                        if (!table)
                        {
                            cell.SetResourceReference(StyleProperty, column.Id == "share" ? "AllHealSubtle" : "AllTakenSubtle");
                        }

                        BindFigure(cell, column.Path);
                    }

                    Add(cell, new GridLength(look.WidthOf(column)));
                    break;
                }

                default:
                {
                    TextBlock cell = IsHeader ? Title(loc[column.TitleKey], tip) : Text(look.SmallSize, table ? "Brush.OverlayText" : "Brush.TextSubtle");
                    if (!IsHeader)
                    {
                        BindFigure(cell, column.Path);
                    }

                    Add(cell, new GridLength(look.WidthOf(column)));
                    break;
                }
            }
        }
    }

    /// <summary>The bar's shape for each style: PART_Track is the whole line, PART_Indicator the filled part (the player's share of the best).</summary>
    private static ControlTemplate BarTemplate(string bar)
    {
        if (BarTemplates.TryGetValue(bar, out ControlTemplate? known))
        {
            return known;
        }

        string inner = bar switch
        {
            "flat" => "<Border x:Name='PART_Track' Background='{TemplateBinding Background}'/>" +
                      "<Border x:Name='PART_Indicator' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}'/>",
            // the class colour, strong and even over the whole line, square ends, no edge: a raid log's table row
            "solid" => "<Border x:Name='PART_Track' Background='{TemplateBinding Background}'/>" +
                       "<Border x:Name='PART_Indicator' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}' Opacity='0.72'/>",
            // the old-school table: the class colour muted over the whole line on a dark band, square ends
            "table" => "<Border x:Name='PART_Track' Background='{TemplateBinding Background}'/>" +
                       "<Border x:Name='PART_Indicator' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}' Opacity='0.58'/>",
            "thread" => "<Border x:Name='PART_Track' Height='2' VerticalAlignment='Bottom' Background='{TemplateBinding Background}'/>" +
                        "<Border x:Name='PART_Indicator' Height='2' VerticalAlignment='Bottom' HorizontalAlignment='Left' Background='{TemplateBinding BorderBrush}'/>",
            "capsule" => "<Border x:Name='PART_Track' Margin='3,2' CornerRadius='6' Background='{TemplateBinding Background}'/>" +
                         "<Border x:Name='PART_Indicator' Margin='3,2' CornerRadius='6' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}'/>",
            "frame" => "<Border x:Name='PART_Track' Margin='2,1' CornerRadius='3' BorderThickness='1' BorderBrush='#18FFFFFF' Background='{TemplateBinding Background}'/>" +
                       "<Grid x:Name='PART_Indicator' Margin='2,1' HorizontalAlignment='Left'>" +
                       "<Border CornerRadius='3' Background='{TemplateBinding Foreground}' Opacity='0.55'/>" +
                       "<Border CornerRadius='3' BorderThickness='1' BorderBrush='{TemplateBinding BorderBrush}'/></Grid>",
            _ => "<Border x:Name='PART_Track' Background='{TemplateBinding Background}'/>" +
                 "<Grid x:Name='PART_Indicator' HorizontalAlignment='Left'><Border Background='{TemplateBinding Foreground}'/>" +
                 "<Border Height='1' VerticalAlignment='Bottom' Background='{TemplateBinding BorderBrush}'/></Grid>",
        };
        var template = (ControlTemplate)XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ProgressBar'>" +
            "<Grid>" + inner + "</Grid></ControlTemplate>");
        template.Seal();
        BarTemplates[bar] = template;
        return template;
    }

    /// <summary>
    /// A bar in a look's style for a table outside the meter (the fight report's players): the class colour at <paramref name="fraction"/> of the line,
    /// drawn the way the meter draws its own lines, so a report looks like the meter the player set up.
    /// </summary>
    internal static ProgressBar BarFor(MeterLook look, string? className, double fraction)
    {
        return new ProgressBar
        {
            Template = BarTemplate(look.Bar),
            Minimum = 0, Maximum = 1, Value = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1),
            Foreground = look.Bar switch { "glow" => ClassBrushes.Glow(className), "solid" or "table" => ClassBrushes.Edge(className), _ => ClassBrushes.For(className) },
            BorderBrush = ClassBrushes.Line(className),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false,
            Focusable = false,
        };
    }

    /// <summary>The bar's colours: the class's (its glow for the glow style, its nearly solid edge colour for the solid style), an enemy's red in PvP.</summary>
    private static Style BarStyle(string bar)
    {
        if (BarStyles.TryGetValue(bar, out Style? known))
        {
            return known;
        }

        var style = new Style(typeof(ProgressBar));
        string fill = bar switch { "glow" => nameof(PlayerRow.ClassGlowBrush), "solid" or "table" => nameof(PlayerRow.ClassEdgeBrush), _ => nameof(PlayerRow.ClassBrush) };
        style.Setters.Add(new Setter(Control.ForegroundProperty, new Binding(fill)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new Binding(nameof(PlayerRow.ClassLineBrush))));
        var enemy = new DataTrigger { Binding = new Binding(nameof(PlayerRow.IsEnemy)), Value = true };
        enemy.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Brush.OverlayEnemy")));
        enemy.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Brush.DangerSoft")));
        style.Triggers.Add(enemy);
        style.Seal();
        BarStyles[bar] = style;
        return style;
    }
}

/// <summary>
/// A figure of the meter cut into its digits and its unit ("12.4K" into "12.4" and "K", "38%" into "38" and "%"), for the looks that print the unit
/// small (<see cref="MeterLook.SmallUnits"/>). A text with no digits before its letters (a dash, a word) stays whole in the digits part.
/// </summary>
public sealed class FigureParts : IValueConverter
{
    public static readonly FigureParts Digits = new(unit: false);
    public static readonly FigureParts Unit = new(unit: true);

    /// <summary>The whole figure with its unit in lowercase ("1.29B" to "1.29b", "471.2M" to "471.2m"): the old-school table's way of writing them.</summary>
    public static readonly FigureParts Lower = new(unit: false, lower: true);

    private readonly bool _unit;
    private readonly bool _lower;

    private FigureParts(bool unit, bool lower = false)
    {
        _unit = unit;
        _lower = lower;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string text = value as string ?? "";
        if (_lower)
        {
            int end = text.Length;
            while (end > 0 && char.IsLetter(text[end - 1]))
            {
                end--;
            }

            // only a unit right after digits ("12.4K"); a word ("Lv", "—") stays as it is
            return end > 0 && end < text.Length && char.IsDigit(text[end - 1]) ? text[..end] + text[end..].ToLowerInvariant() : text;
        }

        int cut = text.Length;
        while (cut > 0 && (char.IsLetter(text[cut - 1]) || text[cut - 1] == '%'))
        {
            cut--;
        }

        if (cut == 0)
        {
            return _unit ? "" : text;
        }

        return _unit ? text[cut..] : text[..cut];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
