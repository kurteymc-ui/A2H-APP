using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// The skill bar the Skill Assist goes by (5 November 2026). The game sends the bar at every login and every map change; the meter remembers it per
/// character (and per skill preset) in the settings, so a session started after the login - or a preset switched since the bar came - shows that
/// character's own skills at once instead of every skill of the class any character ever used. Sent again, the bar replaces what was remembered.
/// </summary>
internal static class SkillBars
{
    private static readonly object Gate = new();
    private static Dictionary<string, List<int>>? _saved;

    /// <summary>The bar to show now: the one the game sent (when the preset is still the one it came under), else the character's remembered one for
    /// the active preset, else for any preset; null when none is known (then the skills known to have a cooldown are listed).</summary>
    public static IReadOnlyList<int>? For(Aion2EntityDirectory? dir)
    {
        if (dir is null)
        {
            return null;
        }

        IReadOnlyList<int> live = dir.LocalBarOrder;
        string? who = dir.LocalCharacter is { Name.Length: > 0 } c ? $"{c.Name}@{c.ServerId}" : null;
        int preset = dir.ActivePreset, livePreset = dir.LocalBarPreset;
        lock (Gate)
        {
            _saved ??= MeterSettings.Load().AssistBars.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal);
            if (live.Count > 0 && who is not null)
            {
                Remember(who + "#" + livePreset.ToString(System.Globalization.CultureInfo.InvariantCulture), live);
                Remember(who, live);
            }

            if (live.Count > 0 && (livePreset == preset || who is null))
            {
                return live;
            }

            if (who is not null && (_saved.TryGetValue(who + "#" + preset.ToString(System.Globalization.CultureInfo.InvariantCulture), out List<int>? bar)
                || _saved.TryGetValue(who, out bar)) && bar.Count > 0)
            {
                return bar;
            }

            return live.Count > 0 ? live : null;
        }
    }

    // a bar that changed is written to the settings at once (the game sends it seldom: at a login or a map change)
    private static void Remember(string key, IReadOnlyList<int> bar)
    {
        if (_saved!.TryGetValue(key, out List<int>? known) && known.SequenceEqual(bar))
        {
            return;
        }

        _saved[key] = bar.ToList();
        MeterSettings s = MeterSettings.Load();
        s.AssistBars[key] = bar.ToList();
        if (s.AssistBars.Count > 60)
        {
            // a few characters' presets at most: the oldest entries go (in the order they were added)
            foreach (string old in s.AssistBars.Keys.Take(s.AssistBars.Count - 60).ToList())
            {
                s.AssistBars.Remove(old);
            }
        }

        s.Save();
    }
}
