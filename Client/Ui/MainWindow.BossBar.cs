using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The boss bar on the meter (5 November 2026, the owner: "remove the boss bar and instead add it over the meter, and knowing the boss's HP we calculate how
/// long it has to die compared to the DPS"): between the meter's head and its lines, the boss being fought - its name and what is left of it in percent, its
/// health as a bar with the numbers, and when it dies at the pace of the last 15 seconds of its health readings (everybody's damage, not only the group's
/// lines), set against its enrage: green when it dies first, red when the enrage comes first. The boss is the one the combat overlay followed (the target,
/// else the boss last hit, else the boss casting); the bar goes with the meter's switch for the boss's health (Settings, Combat).
/// </summary>
public partial class MainWindow
{
    private TextBlock? _bossName;
    private TextBlock? _bossPercent;
    private TextBlock? _bossNumbers;
    private TextBlock? _bossKill;
    private TextBlock? _bossRage;
    private ScaleTransform? _bossFill;

    /// <summary>How far back the health readings are taken for the pace.</summary>
    private static readonly TimeSpan BossPaceOver = TimeSpan.FromSeconds(15);

    private void UpdateOverlayBossBar()
    {
        if (Headless)
        {
            return; // the pictures fill it themselves
        }

        if (!_gateSettings.ShowBossHp || _pvpOnly || _historyMode || _source?.Entities is not Aion2.Aion2EntityDirectory dir
            || CombatOverlayData.BossHp(dir, DateTime.Now) is not { } boss)
        {
            OverlayBossBar.Visibility = Visibility.Collapsed;
            return;
        }

        string name = dir.BossNpcIdOf(boss.Id) is int npc ? Aion2.Protocol.Aion2Npcs.NameOf(npc, LocalizationManager.Instance.Language) ?? "" : "";
        TimeSpan? rage = boss.Since is DateTime since ? TimeSpan.FromSeconds(boss.RageSeconds) - (DateTime.Now - since) : null;
        ShowBossBar(name, boss.Hp, boss.Max, KillTime(dir, boss.Id), rage);
    }

    /// <summary>When the boss dies at the pace its health fell over the last <see cref="BossPaceOver"/> (its own readings: everybody's damage, heals of it
    /// counted against); null while it does not fall (a pause, a phase, too few readings).</summary>
    internal static TimeSpan? KillTime(Aion2.Aion2EntityDirectory dir, int id)
    {
        if (dir.HitPoints.Latest(id) is not { Hp: > 0 } last)
        {
            return null;
        }

        var samples = dir.HitPoints.SamplesAround(id, last.At - BossPaceOver, last.At);
        return samples.Count >= 2 ? KillTime(samples[0].Hp, samples[0].At, last.Hp, last.At) : null;
    }

    /// <summary>The time to go from <paramref name="nowHp"/> to 0 at the pace from the earlier reading to the latest; null without a fall over at least 3 s.</summary>
    internal static TimeSpan? KillTime(long thenHp, DateTime then, long nowHp, DateTime now)
    {
        double seconds = (now - then).TotalSeconds, lost = thenHp - nowHp;
        if (seconds < 3 || lost <= 0 || nowHp <= 0)
        {
            return nowHp <= 0 ? TimeSpan.Zero : null;
        }

        double left = nowHp / (lost / seconds);
        return left > 3600 ? null : TimeSpan.FromSeconds(left);
    }

    /// <summary>Fills the bar (also the pictures of the meter).</summary>
    internal void ShowBossBar(string name, long hp, long max, TimeSpan? kill, TimeSpan? rage)
    {
        if (_bossFill is null)
        {
            BuildBossBar();
        }

        LocalizationManager loc = LocalizationManager.Instance;
        double fraction = max > 0 ? Math.Clamp((double)hp / max, 0, 1) : 0;
        _bossName!.Text = name.Length > 0 ? name : loc["Overlay.BossBar.Boss"];
        _bossPercent!.Text = (fraction * 100).ToString(fraction < 0.1 ? "0.0" : "0", CultureInfo.CurrentCulture) + "%";
        _bossFill!.ScaleX = fraction;
        _bossNumbers!.Text = $"{PlayerRow.Compact(hp)} / {PlayerRow.Compact(max)}";
        bool rageFirst = kill is TimeSpan k && rage is TimeSpan r && r > TimeSpan.Zero && k > r;
        _bossKill!.Text = kill is TimeSpan dies ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossBar.Dies"], Clock(dies)) : loc["Overlay.BossBar.NoPace"];
        _bossKill.Foreground = kill is null ? OverlayPictures.Muted : rageFirst ? OverlayPictures.Red : new SolidColorBrush(Color.FromRgb(0x6C, 0xD4, 0x8A));
        _bossKill.ToolTip = loc["Overlay.BossBar.DiesTip"];
        if (rage is TimeSpan left)
        {
            _bossRage!.Text = left > TimeSpan.Zero ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.Enrage"], Clock(left)) : loc["Overlay.Enraged"];
            _bossRage.Foreground = left <= HpBarView.RageSoonBefore ? OverlayPictures.Red : left <= HpBarView.RageWarnBefore ? OverlayPictures.Amber : OverlayPictures.Muted;
            _bossRage.Visibility = Visibility.Visible;
        }
        else
        {
            _bossRage!.Visibility = Visibility.Collapsed;
        }

        OverlayBossBar.Visibility = Visibility.Visible;

        static string Clock(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    private void BuildBossBar()
    {
        var stack = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
        _bossPercent = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
        _bossPercent.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        _bossPercent.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(13));
        DockPanel.SetDock(_bossPercent, Dock.Right);
        top.Children.Add(_bossPercent);
        _bossName = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
        _bossName.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        _bossName.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(13));
        top.Children.Add(_bossName);
        stack.Children.Add(top);

        // the health: a dark track, the red that is left, the numbers in the middle with a dark edge
        var bar = new Grid { Height = 16 };
        bar.Children.Add(new Border { Background = OverlayPictures.Track, CornerRadius = new CornerRadius(3) });
        _bossFill = new ScaleTransform(1, 1);
        bar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(3), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = _bossFill,
            Background = new LinearGradientBrush(Color.FromRgb(0xC2, 0x34, 0x3E), Color.FromRgb(0x8E, 0x1E, 0x28), 90),
        });
        _bossNumbers = new TextBlock
        {
            FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = OverlayPictures.Text,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 1 },
        };
        _bossNumbers.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(11));
        bar.Children.Add(_bossNumbers);
        stack.Children.Add(bar);

        // when it dies, and its enrage
        var bottom = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        _bossRage = new TextBlock { FontWeight = FontWeights.SemiBold };
        _bossRage.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(11));
        DockPanel.SetDock(_bossRage, Dock.Right);
        bottom.Children.Add(_bossRage);
        _bossKill = new TextBlock { FontWeight = FontWeights.SemiBold };
        _bossKill.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(11));
        bottom.Children.Add(_bossKill);
        stack.Children.Add(bottom);
        OverlayBossBar.Child = stack;
    }
}
