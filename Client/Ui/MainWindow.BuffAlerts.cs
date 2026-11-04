using System.Windows;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>
/// Buff alerts: a line on the meter (and a sound, when switched on) the moment a buff worth keeping drops off the local player while he fights - the party's
/// damage buffs (Mantra, Gale, Fury, Light of Protection, the Gladiator's counter …: the ones rDPS prices), scrolls and food, and any effect named in
/// Settings → Meter look ("Also watch"). Potions, which run out by design, are left out. A buff counts once it was on for 5 seconds; "fighting" is a hit of
/// the player's own in the last 8 seconds.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<int, (string Name, DateTime Since)> _watchedBuffs = new();
    private DateTime _buffAlertUntil = DateTime.MinValue;
    private DateTime _buffAlertCheckedAt = DateTime.MinValue;

    private void UpdateOverlayBuffAlert()
    {
        DateTime now = DateTime.Now; // the capture stamps its frames with the local clock
        if (Headless || !_gateSettings.BuffDropAlert || _source?.Entities is not Aion2EntityDirectory directory || directory.LocalPlayerId < 0)
        {
            OverlayBuffCallout.Visibility = Visibility.Collapsed;
            _watchedBuffs.Clear();
            return;
        }

        if ((now - _buffAlertCheckedAt).TotalMilliseconds >= 200)
        {
            _buffAlertCheckedAt = now;
            CheckBuffDrops(directory, now);
        }

        OverlayBuffCallout.Visibility = now < _buffAlertUntil ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CheckBuffDrops(Aion2EntityDirectory directory, DateTime now)
    {
        int self = directory.LocalPlayerId;
        string extra = _gateSettings.BuffAlertNames ?? "";
        var also = extra.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => n.ToLowerInvariant()).ToHashSet();
        var on = new Dictionary<int, string>();
        foreach (BuffNotice b in directory.Combat.BuffsOn(self, now))
        {
            if (Watched(b.SkillId, also) is { } watched)
            {
                on.TryAdd(watched.Key, watched.Name);
            }
        }

        bool fighting = FoughtWithin(self, now, TimeSpan.FromSeconds(8));
        var dropped = new List<string>();
        foreach ((int key, (string name, DateTime since)) in _watchedBuffs.ToList())
        {
            if (on.ContainsKey(key))
            {
                continue;
            }

            _watchedBuffs.Remove(key);
            if (fighting && now - since >= TimeSpan.FromSeconds(5))
            {
                dropped.Add(name);
            }
        }

        foreach ((int key, string name) in on)
        {
            _watchedBuffs.TryAdd(key, (name, now));
        }

        if (dropped.Count == 0)
        {
            return;
        }

        OverlayBuffCalloutText.Text = (dropped.Count == 1 ? dropped[0] : string.Join(", ", dropped)) + " dropped";
        _buffAlertUntil = now.AddSeconds(6);
        if (_gateSettings.BuffDropSound)
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
    }

    /// <summary>Whether an effect on the player is one to keep: its key (the effect's family) and name, else null.</summary>
    private static (int Key, string Name)? Watched(int skillId, HashSet<string> also)
    {
        int family = RaidDps.DisplayBase(skillId);
        string name = Aion2SkillNames.DisplayOf(skillId);
        if (RaidDps.IsSynergy(family))
        {
            return (family, Aion2SkillNames.DisplayOf(family) is { Length: > 0 } n && !n.All(char.IsDigit) ? n : name);
        }

        // scrolls and food: an effect caused by an item whose icon is a scroll, a dish or a drink (potions run out by design)
        if (Aion2Artwork.EffectIconPath(skillId) is { } icon && System.IO.Path.GetFileName(icon) is { } file
            && (file.Contains("Scroll", StringComparison.OrdinalIgnoreCase) || file.Contains("Food", StringComparison.OrdinalIgnoreCase) || file.Contains("Drink", StringComparison.OrdinalIgnoreCase)))
        {
            return (skillId, name);
        }

        return also.Count > 0 && also.Contains(name.ToLowerInvariant()) ? (family, name) : null;
    }

    /// <summary>A hit of the player's own within <paramref name="within"/> (the newest events are at the end of the list).</summary>
    private bool FoughtWithin(int self, DateTime now, TimeSpan within)
    {
        IReadOnlyList<DamageEvent> events = _aggregator.Events;
        for (int i = events.Count - 1; i >= 0 && i >= events.Count - 400; i--)
        {
            DamageEvent e = events[i];
            if (now - e.Timestamp > within)
            {
                return false;
            }

            if (!e.IsHeal && e.SourceObjectId == self)
            {
                return true;
            }
        }

        return false;
    }
}
