using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Upload;

/// <summary>One counter of a character as the site's roster page shows it: the count the meter saw, the bonus, the cap that applies (the membership's where it
/// has one), the daily and weekly refill amounts (the site adds them at 16:00 game time, Wednesday for the weekly ones) and, below its level, the level it opens at.</summary>
public sealed record RosterTicketUpload(int Id, string Name, int N, int B, int Cap, int D, int W, int? Lock = null);

public sealed record RosterOdyleUpload(int N, int B, int Cap, int PerTick, long? TickMs);

public sealed record RosterCountUpload(int N, long? StaleMs, int Max);

public sealed record RosterCharacterUpload(
    string Name, int ServerId, string? Server, string? Cls, int Level, int ItemLevel, string? Legion, long SeenMs, bool Online, bool? Premium,
    RosterOdyleUpload? Odyle, RosterCountUpload? Missions, RosterCountUpload? Points, IReadOnlyList<RosterTicketUpload> Tickets);

public sealed record RosterUpload(int V, IReadOnlyList<RosterCharacterUpload> Characters);

/// <summary>
/// The roster on the phone: while the meter is signed in to AION 2 Helper (and Settings' <see cref="MeterSettings.SyncRoster"/> is on), the characters'
/// Odyle Energy and counters go to the site (POST /meter/api/roster), which keeps them for the player's own Discord account only and shows them at
/// aion2helper.com/meter/roster with the same recharges worked out live. Sent a little after something changed (a login brings a burst), at most every
/// two minutes, and only the characters that changed since the last send.
/// </summary>
public static class RosterSync
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(2);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The roster's counters after the Odyle Energy (the checklist's roster has the same).</summary>
    public static readonly int[] Tickets = { 1, 8, 4, 80000001, 101 };

    private static readonly Dictionary<string, string> Sent = new(StringComparer.Ordinal);
    private static System.Threading.Timer? _timer;
    private static DateTime _changedAt = DateTime.MinValue;
    private static DateTime _sentAt = DateTime.MinValue;
    private static bool _pending = true; // the first send after the start carries everything
    private static int _busy;

    /// <summary>The meter's start: listen to the counters and check every 15 seconds whether a send is due.</summary>
    public static void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        Aion2Tickets.Changed += () =>
        {
            _changedAt = DateTime.UtcNow;
            _pending = true;
        };
        Account.Changed += () =>
        {
            lock (Sent)
            {
                Sent.Clear(); // another account: everything again
            }

            _pending = true;
        };
        PrivacyConsent.Changed += () => _pending = true; // accepted later: the roster goes up then
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
    }

    private static async Task TickAsync()
    {
        DateTime now = DateTime.UtcNow;
        if (!_pending || !Account.SignedIn || now - _changedAt < Quiet || now - _sentAt < Gap || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            // the privacy choice first, as every other upload (3 November 2026 review: the roster went up even after "Decline")
            if (!MeterSettings.Load().SyncRoster || !PrivacyConsent.CanUpload || UploadClient.IsBlocked)
            {
                _pending = false;
                return;
            }

            _pending = false;
            _sentAt = now;
            var changed = new List<(string Key, string Body, RosterCharacterUpload Character)>();
            foreach (RosterCharacterUpload c in Build(Aion2Tickets.Snapshot(), Aion2Tickets.CurrentName, now))
            {
                string key = c.Name + "|" + c.ServerId;
                // what changed: the content without the moment of the reading's online flag churn
                string body = JsonSerializer.Serialize(c with { Online = false }, Json);
                lock (Sent)
                {
                    if (Sent.TryGetValue(key, out string? before) && before == body)
                    {
                        continue;
                    }
                }

                changed.Add((key, body, c));
            }

            if (changed.Count == 0 || Account.Token is not { } token)
            {
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadClient.ApiBaseUrl}/api/roster")
            {
                Content = JsonContent.Create(new RosterUpload(1, changed.Select(x => x.Character).ToList()), options: Json),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                lock (Sent)
                {
                    foreach (var (key, body, _) in changed)
                    {
                        Sent[key] = body;
                    }
                }
            }
            else if ((int)response.StatusCode != 401)
            {
                _pending = true; // try again at the next gap (signed out: the next sign-in sends it)
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _pending = true;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>The roster as the site takes it: the characters the meter has counters of (40 at most), each with what the roster shows.</summary>
    public static List<RosterCharacterUpload> Build(IReadOnlyList<TicketCharacter> characters, string? online, DateTime utcNow)
    {
        var list = new List<RosterCharacterUpload>();
        foreach (TicketCharacter c in characters.Where(c => c.Tickets.Count > 0).Take(Aion2Tickets.MaxCharacters))
        {
            bool? premium = Aion2Tickets.HasPremium(c, utcNow);
            bool member = premium != false;
            DateTime seen = c.SavedAt.Kind == DateTimeKind.Local ? c.SavedAt.ToUniversalTime() : c.SavedAt;
            long seenMs = new DateTimeOffset(seen, TimeSpan.Zero).ToUnixTimeMilliseconds();
            RosterOdyleUpload? odyle = null;
            if (Aion2Tickets.OdyleNow(c, seen, online: true) is { } o)
            {
                c.Tickets.TryGetValue(Aion2Tickets.OdyleTickId, out TicketValue tick);
                odyle = new RosterOdyleUpload(o.Count, o.Bonus, o.Cap, o.PerTick, tick.TimeMs > 0 ? tick.TimeMs : null);
            }

            RosterCountUpload? missions = null;
            if (c.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue m))
            {
                // stale from the first daily reset after the reading (09:00 on the German servers' clock); a reading of no known time is stale already
                long? staleMs = m.TimeMs > 0
                    ? new DateTimeOffset(Aion2Tickets.LastMissionsReset(DateTimeOffset.FromUnixTimeMilliseconds(m.TimeMs).LocalDateTime.AddDays(1)).ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds()
                    : 0;
                missions = new RosterCountUpload(Math.Min(m.Count, Aion2Tickets.MissionsPerDay), staleMs, Aion2Tickets.MissionsPerDay);
            }

            RosterCountUpload? points = c.Tickets.TryGetValue(Aion2Tickets.WeeklyPointsId, out TicketValue p)
                // over at the first weekly reset after the game last changed them (Wednesday, after the maintenance)
                ? new RosterCountUpload(Math.Min(p.Count, Aion2Tickets.WeeklyPointsMax), p.TimeMs > 0
                    ? new DateTimeOffset(Aion2Tickets.NextWeeklyResetAfter(DateTimeOffset.FromUnixTimeMilliseconds(p.TimeMs).LocalDateTime).ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds()
                    : 0, Aion2Tickets.WeeklyPointsMax)
                : null;
            var tickets = new List<RosterTicketUpload>();
            foreach (int id in Tickets)
            {
                if (Aion2Tickets.Find(id) is not { } def)
                {
                    continue;
                }

                bool locked = Aion2Tickets.IsLocked(c, id);
                if (!locked && !c.Tickets.ContainsKey(id))
                {
                    continue;
                }

                TicketValue v = locked ? default : c.Tickets[id];
                int cap = member && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;
                tickets.Add(new RosterTicketUpload(id, def.ShortIn("en"), v.Count, v.Bonus, cap, def.DailyCount, def.WeeklyCount, locked ? Aion2Tickets.UnlockLevel(id) : null));
            }

            string? cls = c.ClassCode > 0 ? Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode) : null;
            string? server = c.ServerId > 0 ? Aion2.Protocol.Aion2Servers.NameOf(c.ServerId) : null;
            list.Add(new RosterCharacterUpload(c.Name, c.ServerId, server, cls, c.Level, c.ItemLevel, c.Legion, seenMs, c.Name == online, premium,
                odyle, missions, points, tickets));
        }

        return list;
    }
}
