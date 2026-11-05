using System.Text.Json;
using AionDPS.Ui;
using AionDPS.Update;

namespace AionDPS.Upload;

/// <summary>
/// The map accuracy data (5 November 2026, the owner: on by default while the app is in beta, told before anything goes - the privacy terms name it and
/// are asked again): every time the game reports the player's own exact place while the minimap reader has a reading of it, the two places go to the
/// website, so the maps' own numbers (where each world map's picture lies in the game's coordinates) can be refitted from everyone's play and shipped
/// with the next version - the map's arrow, the Hidden Cubes and the collectibles then sit on the right spot of the picture on every map from the first
/// second. Switched off in Settings, Upload; nothing goes without the privacy terms accepted (declining switches it off).
/// <para>
/// What goes, per map: the map's name and its numbers (scale and offset, so a pair is read against the picture it was made on), and for each check the
/// reading's place and the game's place (world units, rounded to a metre), the speed, how far apart in time the two were and how well the minimap fitted;
/// with the day, the app's version and the random value this installation made for itself (<see cref="MeterSettings.EnsureAnonymousId"/>, kept by the
/// site only as a keyed hash). No name, server, legion, account or time of day; the site keeps no IP address with it.
/// </para>
/// </summary>
public static class MapShare
{
    public const int FormatVersion = 1;

    /// <summary>At most this many checks a day from one installation (the site holds to a few thousand too).</summary>
    private const int MostPerDay = 2000;

    /// <summary>A map's checks go together once this many are waiting, or after <see cref="Every"/>.</summary>
    private const int Batch = 60;

    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);
    private static readonly object Gate = new();
    private static readonly Dictionary<(string Map, string Stamp), List<double[]>> Waiting = new();
    private static DateTime _day = DateTime.MinValue, _sentAt = DateTime.UtcNow;
    private static int _today, _sent;

    /// <summary>How many checks this run has sent.</summary>
    public static int Sent => Volatile.Read(ref _sent);

    /// <summary>Whether the checks go: switched on (the default) and the privacy terms accepted.</summary>
    public static bool On => !PrivacyConsent.Restricted && PrivacyConsent.CanUpload && MeterSettings.Load().ShareMapData;

    /// <summary>
    /// One check of the minimap reader against the game's own report (Ui/MinimapReader.CheckAgainstGame): the reading's place (raw, before any correction)
    /// and the game's, both world units; the speed (world units a second), the seconds between the two and the reading's fit (0 to 1).
    /// </summary>
    public static void Note(string map, string stamp, double rawX, double rawY, double gameX, double gameY, double speed, double lagSeconds, double fit, bool on)
    {
        if (!on || string.IsNullOrEmpty(map) || !double.IsFinite(rawX + rawY + gameX + gameY) || Math.Abs(gameX - rawX) > 10_000 || Math.Abs(gameY - rawY) > 10_000)
        {
            return;
        }

        List<(string Map, string Stamp, List<double[]> Points)>? due = null;
        lock (Gate)
        {
            DateTime now = DateTime.UtcNow;
            if (_day != now.Date)
            {
                _day = now.Date;
                _today = 0;
            }

            if (_today >= MostPerDay)
            {
                return;
            }

            _today++;
            if (!Waiting.TryGetValue((map, stamp), out List<double[]>? list))
            {
                Waiting[(map, stamp)] = list = new List<double[]>();
            }

            list.Add(new[]
            {
                Metre(rawX), Metre(rawY), Metre(gameX), Metre(gameY), Math.Round(Math.Clamp(speed / 100, 0, 99), 1), Math.Round(Math.Clamp(lagSeconds, -2, 2), 2),
                Math.Round(Math.Clamp(fit, 0, 1), 2),
            });
            if (list.Count >= Batch || now - _sentAt >= Every)
            {
                due = TakeLocked();
                _sentAt = now;
            }
        }

        if (due is not null)
        {
            Send(due);
        }
    }

    /// <summary>What is still waiting goes now (the program closes).</summary>
    public static void Flush()
    {
        List<(string Map, string Stamp, List<double[]> Points)> due;
        lock (Gate)
        {
            due = TakeLocked();
        }

        if (due.Count > 0 && On)
        {
            Send(due, wait: true);
        }
    }

    private static double Metre(double v) => Math.Round(v / 100) * 100;

    private static List<(string Map, string Stamp, List<double[]> Points)> TakeLocked()
    {
        var all = Waiting.Where(kv => kv.Value.Count > 0).Select(kv => (kv.Key.Map, kv.Key.Stamp, kv.Value.ToList())).ToList();
        Waiting.Clear();
        return all;
    }

    /// <summary>The upload of one map's checks (the site's /meter/api/uploads/map-points).</summary>
    internal static string Build(string map, string stamp, IReadOnlyList<double[]> points, string anonymousId, DateTime utcNow) =>
        JsonSerializer.Serialize(new
        {
            kind = "map-points",
            formatVersion = FormatVersion,
            clientVersion = AppVersion.Text,
            anonymousId,
            day = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            map,
            stamp,
            points,
        });

    private static void Send(List<(string Map, string Stamp, List<double[]> Points)> due, bool wait = false)
    {
        string id = MeterSettings.EnsureAnonymousId();
        DateTime now = DateTime.UtcNow;
        var jobs = due.Select(d => (Json: Build(d.Map, d.Stamp, d.Points, id, now), d.Points.Count)).ToList();
        Task task = Task.Run(async () =>
        {
            foreach (var (json, count) in jobs)
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

                    var (result, retry) = await UploadClient.SendMapPointsAsync(json).ConfigureAwait(false);
                    if (result.Success)
                    {
                        Interlocked.Add(ref _sent, count);
                        break;
                    }

                    if (!retry || wait)
                    {
                        break;
                    }
                }
            }
        });
        if (wait)
        {
            task.Wait(TimeSpan.FromSeconds(3)); // closing: a short wait, never a hang
        }
    }
}
