using System.Text.Json;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Ui;
using AionDPS.Update;

namespace AionDPS.Upload;

/// <summary>
/// The CP calculator's measured changes (Combat/CpChecks.cs), shared with the website when the player switched it on (Settings, Upload, or the CP
/// calculator; off by default, and only with the privacy terms accepted). The website's weekly refit of the Combat Power model uses them together with
/// the changes it sees on character pages, so the calculator gets more exact for everyone. One small upload per measured change: at most a few a day.
/// <para>
/// What goes: the class, the region, the day, the Item Level (to ten) and Combat Power (to a thousand) after the change, how much they changed, and how
/// much each stat of the model moved (the stat's own amount, before any weight). No name, no server, no legion, no account (the upload carries no sign-in)
/// and no IP address is kept; the one identifier is the random value this installation made for itself (<see cref="MeterSettings.EnsureAnonymousId"/>),
/// which the site keeps only as a keyed hash, to hold each installation to a few changes a day.
/// </para>
/// </summary>
public static class CpShare
{
    public const int FormatVersion = 1;

    /// <summary>The stats of the model a gear change can move (the website takes no other).</summary>
    private static readonly HashSet<string> GearKeys = new(StringComparer.Ordinal)
    {
        "atk", "crit", "acc", "critAtk", "pveAtk", "pen", "dmg", "wdb", "cdmg", "front", "cspd", "multi", "perf", "dbl", "pve", "cdr", "hp", "def", "block",
    };

    private const int MostPerDay = 20;
    private static readonly object Gate = new();
    private static DateTime _day = DateTime.MinValue;
    private static int _sentToday;
    private static int _sent;

    /// <summary>How many changes this run has sent.</summary>
    public static int Sent => Volatile.Read(ref _sent);

    /// <summary>Whether measured changes are sent: the player switched it on and the privacy terms are accepted.</summary>
    public static bool On => !PrivacyConsent.Restricted && PrivacyConsent.CanUpload && MeterSettings.Load().ShareCpMeasurements;

    /// <summary>The website model's stats, each weighed one, without progression and without the offence or guard a percentage counts with: what a change
    /// counts as then is how much each stat moved. The meter's own weights (stats the website has none for) don't count: the website takes only its own.</summary>
    internal static CpModel Unit(CpModel model) =>
        new(model.Weights.Keys.Concat(GearKeys).Distinct().ToDictionary(k => k, _ => 1.0, StringComparer.Ordinal), model.Confidence, false, PercentRef: 0, GuardRef: 0, Own: false);

    /// <summary>
    /// The upload of a measured change; null when it isn't one to send: stats that didn't move as the tables say (a gap), stones whose values are a range
    /// (the calculator's number for them isn't exact), a stat the website doesn't take, a character below Item Level 1,000, a measurement without the first
    /// login's Item Level.
    /// </summary>
    internal static string? Build(CpMeasurement m, int classCode, int serverId, string anonymousId, DateTime utcNow, CpModel? model = null)
    {
        string? cls = Aion2SkillNames.ClassFromCode(classCode);
        string? region = Aion2Servers.RegionCodeOf(serverId) switch { "latam" => "la", "asia" => "as", var r => r };
        if (m.Gaps.Count > 0 || cls is null || region is null || m.ItemLevel < 1000 || m.ItemLevelBefore <= 0 || m.Actual == 0)
        {
            return null;
        }

        var (parts, width) = CpChecks.Predicted(m.Before, m.After, m.Frame, m.ItemLevel, null, Unit(model ?? Aion2CpData.Model));
        if (width > 1e-6 || parts.Count == 0 || parts.Keys.Any(k => !GearKeys.Contains(k)))
        {
            return null;
        }

        int cp = (int)Math.Round(m.CpAfter / 1000.0, MidpointRounding.AwayFromZero) * 1000;
        if (cp < 1000 || Math.Abs(m.Actual) > cp / 2)
        {
            return null;
        }

        var document = new
        {
            kind = "cp-change",
            formatVersion = FormatVersion,
            clientVersion = AppVersion.Text,
            anonymousId,
            day = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            region,
            cls,
            il = (int)Math.Round(m.ItemLevel / 10.0, MidpointRounding.AwayFromZero) * 10,
            dil = m.ItemLevel - m.ItemLevelBefore,
            cp,
            dcp = m.Actual,
            d = parts.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => Math.Round(p.Value, 4)),
        };
        return JsonSerializer.Serialize(document);
    }

    /// <summary>Sends a change just measured, when sharing is on (at most 20 a day; one more try a minute later when the site couldn't be reached).</summary>
    public static void Offer(CpMeasurement m, int classCode, int serverId)
    {
        if (!On)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        lock (Gate)
        {
            if (_day != now.Date)
            {
                _day = now.Date;
                _sentToday = 0;
            }

            if (_sentToday >= MostPerDay)
            {
                return;
            }

            _sentToday++;
        }

        string? json = Build(m, classCode, serverId, MeterSettings.EnsureAnonymousId(), now);
        if (json is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                }

                if (!On)
                {
                    return; // switched off meanwhile
                }

                var (result, retry) = await UploadClient.SendCpChangeAsync(json).ConfigureAwait(false);
                if (result.Success)
                {
                    Interlocked.Increment(ref _sent);
                    return;
                }

                if (!retry)
                {
                    return;
                }
            }
        });
    }
}
