using System.IO;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>A pet the Black Cloud Merchant offered to a character: the pet, its price after the discount, the page of offers it was on.</summary>
public sealed class BlackCloudOffer
{
    public int PetId { get; set; }
    public long Price { get; set; }
    public int Page { get; set; }
}

/// <summary>One character as the Black Cloud notes know it: its pet collection (pet id -> level, from its last login) and the merchant's last offers to it.</summary>
public sealed class BlackCloudCharacter
{
    public string Name { get; set; } = "";
    public int ServerId { get; set; }
    public DateTime PetsAt { get; set; }
    public Dictionary<int, int> Pets { get; set; } = new();
    public DateTime OffersAt { get; set; }
    public List<BlackCloudOffer> Offers { get; set; } = new();

    /// <summary>When the merchant renews its offers (Unix ms, from the shop's own page), 0 = not known.</summary>
    public long RenewMs { get; set; }

    /// <summary>The renewal the "new offers" note was shown for (each renewal once).</summary>
    public long AnnouncedMs { get; set; }

    /// <summary>The pets bought from the merchant, with when (Unix ms): not marked on the shop again until its offers renew.</summary>
    public Dictionary<int, long> Bought { get; set; } = new();
}

/// <summary>
/// The Black Cloud Merchant for every character: which pets each character still needs (its collection at its last login, kept up to date by the game's
/// level and new-pet messages) and what the merchant offered it last, with the time the offers renew. The game sends the offers only when the shop is opened
/// (frame 0x9156, see Aion2FrameDecoder.DecodeShop). Two notes come from it: a pet of the merchant's list on offer (<see cref="Aion2MerchantPets"/>, the ones
/// that can't be farmed out in the world: field boss, dungeon, chests and shops only, special) (<see cref="Offered"/>), and the offers renewing (<see cref="DueRenewals"/>). Kept in
/// aion2-blackcloud.json beside the settings; only on this computer.
/// </summary>
public static class Aion2BlackCloud
{
    private static readonly object Gate = new();
    private static Dictionary<string, BlackCloudCharacter> _characters = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Dictionary<int, (DateTime At, List<BlackCloudOffer> Offers)>> Pages = new(StringComparer.Ordinal);
    private static bool _persist;

    /// <summary>When the offers renew for the pages read while the character was not known yet.</summary>
    private static long _unknownRenewMs;

    /// <summary>Pets bought while the meter did not know the character yet (kept in memory only).</summary>
    private static readonly Dictionary<int, long> UnknownBought = new();

    /// <summary>Raised (on the capturing thread) when a pet was bought from the merchant: the character (empty when not known) and the pet.</summary>
    public static event Action<string, int>? Bought;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-blackcloud.json");

    /// <summary>Raised (on the capturing thread) when a shop page came and pets are on offer: the character it was opened on (empty when the meter does not know
    /// it yet) and every pet on offer on the pages read since the offers renewed, once each at its lowest price, the wanted ones first.</summary>
    public static event Action<string, IReadOnlyList<BlackCloudWanted>>? Offered;

    /// <summary>The page of the shop that came last (the one the shop shows): the character, the page's byte, its pets and when it came. Null before any.</summary>
    public static BlackCloudPage? LastPage { get; private set; }

    /// <summary>Raised when something changed (a collection or offers), for the pages that show it.</summary>
    public static event Action? Changed;

    /// <summary>Starts keeping the notes in <see cref="StorePath"/> (the live meter only) and loads what is there.</summary>
    public static void EnablePersistence()
    {
        lock (Gate)
        {
            _persist = true;
            try
            {
                if (File.Exists(StorePath))
                {
                    var loaded = JsonSerializer.Deserialize<List<BlackCloudCharacter>>(File.ReadAllText(StorePath)) ?? new();
                    _characters = loaded.Where(c => c.Name.Length > 0).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (ex is JsonException)
                {
                    AtomicFile.KeepDamaged(StorePath);
                }
            }
        }
    }

    /// <summary>Forgets everything (the self-test and the pictures of the pages).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _characters = new(StringComparer.Ordinal);
            Pages.Clear();
            _unknownRenewMs = 0;
            LastPage = null;
            UnknownBought.Clear();
        }
    }

    /// <summary>A character's pet collection as the game listed it.</summary>
    public static void NotePets(string name, int serverId, IReadOnlyList<Aion2PetState> pets)
    {
        if (name.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            BlackCloudCharacter c = Of(name, serverId);
            c.Pets = pets.GroupBy(p => p.PetId).ToDictionary(g => g.Key, g => g.Max(p => p.Level));
            c.PetsAt = DateTime.UtcNow;
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// A page of the shop came for <paramref name="name"/>: its pets replace what that page offered before (an emptied page drops them); offers seen before the
    /// last known renewal are dropped too. <paramref name="renewMs"/>: when the offers renew (0 = not read). <see cref="Offered"/> then gets every pet on offer
    /// on all the pages read since the renewal, so the pop-up keeps the pets of a tab while the player turns to the next one. A page read before the meter
    /// knows the character (it was started after the login: the game sends the character's record at the login and on a teleport) still raises it, with an
    /// empty name, and is kept only in memory.
    /// </summary>
    public static void NoteOffers(string name, int serverId, int page, IReadOnlyList<Aion2ShopPet> pets, long renewMs, DateTime at)
    {
        List<BlackCloudWanted> offered;
        lock (Gate)
        {
            BlackCloudCharacter? c = name.Length > 0 ? Of(name, serverId) : null;
            DateTime utc = at.ToUniversalTime();
            if (!Pages.TryGetValue(name, out var pages))
            {
                Pages[name] = pages = new();
            }

            long knownRenew = c?.RenewMs ?? _unknownRenewMs;
            if (knownRenew > 0 && new DateTimeOffset(utc).ToUnixTimeMilliseconds() >= knownRenew)
            {
                pages.Clear(); // renewed since: the pages read before are of the old offers
            }

            pages[page] = (utc, pets.Select(p => new BlackCloudOffer { PetId = p.PetId, Price = p.Price, Page = page }).ToList());
            LastPage = new BlackCloudPage(name, page, pets.Select(p => p.PetId).ToList(), utc);
            List<BlackCloudOffer> all = pages.OrderBy(p => p.Key).SelectMany(p => p.Value.Offers).ToList();
            if (c is not null)
            {
                c.Offers = all;
                c.OffersAt = utc;
                if (renewMs > 0 && renewMs != c.RenewMs)
                {
                    c.RenewMs = renewMs;
                }
            }
            else if (renewMs > 0)
            {
                _unknownRenewMs = renewMs;
            }

            offered = all.GroupBy(o => o.PetId).Select(g => Wanted(g.Key, g.Where(o => o.Price > 0).Select(o => o.Price).DefaultIfEmpty(0).Min()))
                .OrderByDescending(w => w.Merchant && w.Needed.Count > 0).ThenByDescending(w => w.Needed.Count > 0).ToList();
            if (c is not null)
            {
                Save();
            }
        }

        if (name.Length > 0)
        {
            Changed?.Invoke();
        }

        if (offered.Count > 0)
        {
            Offered?.Invoke(name, offered);
        }
    }

    /// <summary>
    /// A pet was bought from the merchant: the shop's item for it came into the bag (a recording of 4 November 2026: Sentinel K'nash bought 6 s after the
    /// page came, the item, the pet's progress and its new level in the same tenth of a second; the shop sent no page again). It is not marked on the shop
    /// again until the offers renew (every hour, on the hour).
    /// </summary>
    public static void NoteBought(string name, int serverId, int petId, DateTime at)
    {
        long ms = Ms(at);
        lock (Gate)
        {
            if (name.Length > 0)
            {
                Of(name, serverId).Bought[petId] = ms;
                Save();
            }
            else
            {
                UnknownBought[petId] = ms;
            }
        }

        Bought?.Invoke(name, petId);
        Changed?.Invoke();
    }

    /// <summary>Whether <paramref name="name"/> bought the pet since the offers last renewed (the hour began).</summary>
    public static bool BoughtThisRound(string name, int petId, DateTime utcNow)
    {
        long now = Ms(utcNow), start = now - (now % RenewEveryMs);
        lock (Gate)
        {
            long? at = name.Length > 0 && _characters.TryGetValue(name, out var c) && c.Bought.TryGetValue(petId, out long known) ? known
                : UnknownBought.TryGetValue(petId, out long unknown) ? unknown : null;
            return at >= start;
        }
    }

    /// <summary>Which known characters still need a pet: missing from their collection, or below the top level (their level then, 0 = missing).</summary>
    public static BlackCloudWanted Wanted(int petId, long price = 0)
    {
        lock (Gate)
        {
            var needed = _characters.Values.Where(c => c.Pets.Count > 0 && c.Pets.GetValueOrDefault(petId) < Aion2Pets.TopLevel)
                .OrderBy(c => c.Pets.GetValueOrDefault(petId)).ThenBy(c => c.Name, StringComparer.CurrentCulture)
                .Select(c => (c.Name, c.Pets.GetValueOrDefault(petId))).ToList();
            var done = _characters.Values.Where(c => c.Pets.GetValueOrDefault(petId) >= Aion2Pets.TopLevel).Select(c => c.Name).OrderBy(n => n, StringComparer.CurrentCulture).ToList();
            return new BlackCloudWanted(petId, price, Aion2MerchantPets.Is(petId), needed, done);
        }
    }

    /// <summary>Every character the notes know (copies), the one seen last first.</summary>
    public static IReadOnlyList<BlackCloudCharacter> Characters()
    {
        lock (Gate)
        {
            return _characters.Values.OrderByDescending(c => c.OffersAt > c.PetsAt ? c.OffersAt : c.PetsAt).Select(c => new BlackCloudCharacter
            {
                Name = c.Name, ServerId = c.ServerId, PetsAt = c.PetsAt, Pets = new Dictionary<int, int>(c.Pets), OffersAt = c.OffersAt, RenewMs = c.RenewMs,
                AnnouncedMs = c.AnnouncedMs, Offers = c.Offers.Select(o => new BlackCloudOffer { PetId = o.PetId, Price = o.Price, Page = o.Page }).ToList(),
                Bought = new Dictionary<int, long>(c.Bought),
            }).ToList();
        }
    }

    /// <summary>
    /// The shop's product list renews every hour, on the hour: every renewal time a shop page gave was the next whole hour after it was read (six readings
    /// of 2 to 5 November 2026, 6 to 47 minutes before), and the shop itself counts down from an hour ("Product list will refresh in 00:36:03" 24 minutes
    /// after the hour, the owner's picture of 6 November). So one opening tells every renewal after it.
    /// </summary>
    public const long RenewEveryMs = 3_600_000;

    /// <summary>A renewal of the same hourly round older than this is not announced any more (the meter started long after it).</summary>
    private const long AnnounceWithinMs = 20 * 60_000;

    private static long Ms(DateTime utc) => new DateTimeOffset(utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc).ToUnixTimeMilliseconds();

    /// <summary>The last renewal at or before <paramref name="now"/> in the hourly round of a known one (0: none yet).</summary>
    private static long LastRenewal(long known, long now) => known <= 0 || now < known ? 0 : known + (now - known) / RenewEveryMs * RenewEveryMs;

    /// <summary>
    /// When the shop's product list renews next and when the one on offer began (UTC), from the newest renewal any shop page gave; null before the shop was
    /// ever opened with the meter running.
    /// </summary>
    public static (DateTime Next, DateTime Since)? Clock(DateTime utcNow)
    {
        long now = Ms(utcNow), known;
        lock (Gate)
        {
            known = Math.Max(_unknownRenewMs, _characters.Values.Select(c => c.RenewMs).DefaultIfEmpty(0).Max());
        }

        if (known <= 0)
        {
            return null;
        }

        long next = now < known ? known : LastRenewal(known, now) + RenewEveryMs;
        return (DateTimeOffset.FromUnixTimeMilliseconds(next).UtcDateTime, DateTimeOffset.FromUnixTimeMilliseconds(next - RenewEveryMs).UtcDateTime);
    }

    /// <summary>
    /// The characters whose offers have renewed (every hour, see <see cref="RenewEveryMs"/>) since the shop was opened on them, with how many of the
    /// <paramref name="watched"/> pets (the merchant's list that the player ticked) they still miss or have below the top level; a character with none is left
    /// out. Each renewal is given once (<see cref="MarkAnnounced"/>), and one more than 20 minutes old not at all.
    /// </summary>
    public static IReadOnlyList<(string Name, int Wanted)> DueRenewals(DateTime utcNow, IReadOnlyCollection<int> watched)
    {
        long now = Ms(utcNow);
        lock (Gate)
        {
            return _characters.Values.Select(c => (c, last: LastRenewal(c.RenewMs, now)))
                .Where(x => x.last > 0 && now - x.last < AnnounceWithinMs && x.c.AnnouncedMs != x.last)
                .Select(x => (x.c.Name, watched.Count(p => x.c.Pets.GetValueOrDefault(p) < Aion2Pets.TopLevel)))
                .Where(c => c.Item2 > 0).ToList();
        }
    }

    /// <summary>The renewal notes of these characters were shown (the renewal of <paramref name="utcNow"/>'s hour).</summary>
    public static void MarkAnnounced(IEnumerable<string> names, DateTime utcNow)
    {
        long now = Ms(utcNow);
        lock (Gate)
        {
            foreach (string name in names)
            {
                if (_characters.TryGetValue(name, out var c))
                {
                    c.AnnouncedMs = LastRenewal(c.RenewMs, now);
                }
            }

            Save();
        }
    }

    // Under the lock.
    private static BlackCloudCharacter Of(string name, int serverId)
    {
        if (!_characters.TryGetValue(name, out var c))
        {
            _characters[name] = c = new BlackCloudCharacter { Name = name };
        }

        if (serverId > 0)
        {
            c.ServerId = serverId;
        }

        return c;
    }

    // Under the lock.
    private static void Save()
    {
        if (!_persist)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(_characters.Values.ToList()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // kept in memory; the next change tries again
        }
    }
}

/// <summary>A pet on offer (or in the list): its price, whether it is on the merchant's list (<see cref="Aion2MerchantPets"/>: can't be farmed out in the world),
/// the characters that still need it with their level (0 = missing) and those that have it at the top level.</summary>
public sealed record BlackCloudWanted(int PetId, long Price, bool Merchant, IReadOnlyList<(string Name, int Level)> Needed, IReadOnlyList<string> Done);

/// <summary>One page of the shop as it came: the character, the page's byte, its pets, when (UTC).</summary>
public sealed record BlackCloudPage(string Name, int Page, IReadOnlyList<int> Pets, DateTime At);
