using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AionDPS.Ui;

/// <summary>
/// The boss line on the meter (5 November 2026, the owner: "remove the boss bar and instead add it over the meter, and knowing the boss's HP we calculate how
/// long it has to die compared to the DPS", then: "simple, not a huge bar, just a text like 50m/120m and time to enrage / time to kill"): between the meter's
/// head and its lines, one line - what is left of the boss and its whole, its name, when it dies at the pace of the last 15 seconds of its health readings
/// (everybody's damage; the group's DPS until there is a pace) and its enrage: green when it dies first, red when the enrage comes first. The boss is the one the combat overlay followed (the target,
/// else the boss last hit, else the boss casting); it has its own switch in Meter look (MeterBossLine), no longer the combat overlay's health bar switch, which
/// a player turning the old big bar off had turned off with it.
/// </summary>
public partial class MainWindow
{
    private TextBlock? _bossName;
    private TextBlock? _bossNumbers;
    private TextBlock? _bossKill;
    private TextBlock? _bossRage;

    /// <summary>How far back the health readings are taken for the pace.</summary>
    private static readonly TimeSpan BossPaceOver = TimeSpan.FromSeconds(15);

    private void UpdateOverlayBossBar()
    {
        if (Headless)
        {
            return; // the pictures fill it themselves
        }

        if (!_gateSettings.MeterBossLine || _pvpOnly || _historyMode || _source?.Entities is not Aion2.Aion2EntityDirectory dir
            || CombatOverlayData.BossHp(dir, DateTime.Now) is not { } boss)
        {
            OverlayBossBar.Visibility = Visibility.Collapsed;
            return;
        }

        // A boss the app's list does not have yet (a new raid's: the game's hit points make it one) is named from the game's monster names; its enrage is not
        // shown rather than guessed (the usual five minutes would be wrong for most new bosses).
        int? npc = dir.BossNpcIdOf(boss.Id) ?? dir.NpcIdOf(boss.Id);
        string name = npc is int id ? Aion2.Protocol.Aion2Npcs.NameOf(id, LocalizationManager.Instance.Language) ?? "" : "";
        TimeSpan? rage = boss.Since is DateTime since && Aion2.Protocol.Aion2BossRage.Known(dir.BossNpcIdOf(boss.Id)) ? TimeSpan.FromSeconds(boss.RageSeconds) - (DateTime.Now - since) : null;
        // when it dies: at the pace its health fell (everybody's damage), else at the group's DPS on the meter
        TimeSpan? kill = KillTime(dir, boss.Id);
        double groupDps = _rows.Where(r => !r.IsEnemy).Sum(r => r.Dps ?? 0);
        if (kill is null && groupDps > 0 && boss.Hp > 0 && boss.Hp / groupDps <= 3600)
        {
            kill = TimeSpan.FromSeconds(boss.Hp / groupDps);
        }

        ShowBossBar(name, boss.Hp, boss.Max, kill, rage);
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

    /// <summary>Fills the line (also the pictures of the meter).</summary>
    internal void ShowBossBar(string name, long hp, long max, TimeSpan? kill, TimeSpan? rage)
    {
        if (_bossNumbers is null)
        {
            BuildBossBar();
        }

        LocalizationManager loc = LocalizationManager.Instance;
        _bossNumbers!.Text = max > 0 ? $"{PlayerRow.Compact(hp)} / {PlayerRow.Compact(max)}" : PlayerRow.Compact(hp);
        _bossName!.Text = name.Length > 0 ? name : loc["Overlay.BossBar.Boss"];
        bool rageFirst = kill is TimeSpan k && rage is TimeSpan r && r > TimeSpan.Zero && k > r;
        _bossKill!.Text = kill is TimeSpan dies ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossLine.Kill"], Clock(dies)) : loc["Overlay.BossLine.NoKill"];
        _bossKill.Foreground = kill is null ? OverlayPictures.Muted : rageFirst ? OverlayPictures.Red : new SolidColorBrush(Color.FromRgb(0x6C, 0xD4, 0x8A));
        _bossKill.ToolTip = loc["Overlay.BossBar.DiesTip"];
        if (rage is TimeSpan left)
        {
            _bossRage!.Text = left > TimeSpan.Zero ? string.Format(CultureInfo.CurrentCulture, loc["Overlay.BossLine.Enrage"], Clock(left)) : loc["Overlay.Enraged"];
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

    /// <summary>
    /// One line of text, no bar (5 November 2026, the owner: "simple, not a huge bar, just a text like 50m/120m and time to enrage / time to kill"): what is left
    /// of the boss and its whole, its name, then when it dies and its enrage on the right.
    /// </summary>
    private void BuildBossBar()
    {
        var line = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _bossKill = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0) };
        _bossKill.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(12));
        right.Children.Add(_bossKill);
        _bossRage = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0) };
        _bossRage.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(12));
        right.Children.Add(_bossRage);
        DockPanel.SetDock(right, Dock.Right);
        line.Children.Add(right);
        _bossNumbers = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _bossNumbers.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverlayText");
        _bossNumbers.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(12));
        DockPanel.SetDock(_bossNumbers, Dock.Left);
        line.Children.Add(_bossNumbers);
        _bossName = new TextBlock { Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = OverlayPictures.Muted };
        _bossName.SetResourceReference(TextBlock.FontSizeProperty, OverlayPictures.SizeKey(11));
        line.Children.Add(_bossName);
        OverlayBossBar.Child = line;
    }
}
