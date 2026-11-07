using System.IO;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>What one character held at its last login or change the meter saw: the bag and the character storage (item -> count).</summary>
public sealed class CharacterHoldings
{
    public string Name { get; set; } = "";
    public int ServerId { get; set; }
    public string? Account { get; set; }
    public DateTime SavedAt { get; set; }
    public Dictionary<int, long> Bag { get; set; } = new();
    public Dictionary<int, long> Storage { get; set; } = new();
}

/// <summary>The account storage of one account on one server (shared by its characters there), as last seen.</summary>
public sealed class AccountHoldings
{
    public DateTime SavedAt { get; set; }
    public Dictionary<int, long> Items { get; set; } = new();
}

/// <summary>Where some of an item lies: a character's bag and storage, or the server storage (<see cref="Character"/> null).</summary>
public readonly record struct HoldingPlace(string? Character, long Count);

/// <summary>
/// Every character's inventory, kept from its last login (<see cref="Aion2Stock"/> reads only the character logged in): the bag (cube) and the character
/// storage per character, the server storage (the game's name; container 2, "account storage" in the stock) per account and server. A login's stock waits until it surely belongs to the character named (the name changed since
/// it came, or 10 s went by under the same name), as the counters do (<see cref="Aion2Achievements"/>). Kept in aion2-inventories.json beside the settings.
/// <para>Bound items (the game names them "(Bound)") stay with their character: they never count for another one. Unbound ones can be moved between the
/// characters of one account on one server through the server storage, so they count for all of them; what lies in the server storage counts for all.</para>
/// </summary>
public static class Aion2Inventories
{
    private static readonly object Gate = new();
    private static Dictionary<string, CharacterHoldings> _characters = new(StringComparer.Ordinal);
    private static Dictionary<string, AccountHoldings> _accounts = new(StringComparer.Ordinal);
    private static bool _persist, _dirty, _stockChanged = true;
    private static DateTime _savedAt = DateTime.MinValue;
    private static System.Threading.Timer? _timer;
    private static DateTime? _snapshotSeen;
    private static string? _owner, _loginWas;
    private static DateTime _loginAt;

    // The owner taken by the 10 s rule (the name did not change): when the game names another character within a minute after all, the stock was that
    // one's, and the character guessed gets back what it had before (3 November 2026 review: the guess stayed for good and overwrote its holdings).
    private static DateTime? _guessedAt;
    private static CharacterHoldings? _beforeGuess;

    // The name moved on with no new login list: whose stock it is cannot be told, so nothing is taken until the next list.
    private static bool _ownerLost;
    private static bool _timerErrorNoted;
    private static Dictionary<int, bool>? _bound;

    /// <summary>Raised after a character's holdings were taken over.</summary>
    public static event Action? Changed;

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-inventories.json");

    /// <summary>The wall clock (a test moves it on).</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>The character logged in now (a test sets its own).</summary>
    public static Func<string?> CurrentName { get; set; } = () => Aion2Tickets.CurrentName;

    /// <summary>The server and account of a character (the tickets' record of it; a test sets its own).</summary>
    public static Func<string, (int ServerId, string? Account)> Whereabouts { get; set; } = name =>
        Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == name) is { } who ? (who.ServerId, who.Account) : (0, null);

    static Aion2Inventories()
    {
        Aion2Stock.Changed += () =>
        {
            lock (Gate)
            {
                _stockChanged = true;
            }
        };
    }

    /// <summary>Whether an item is bound to its character (the game's own name says "(Bound)").</summary>
    public static bool IsBound(int item)
    {
        lock (Gate)
        {
            _bound ??= new Dictionary<int, bool>();
            if (!_bound.TryGetValue(item, out bool bound))
            {
                bound = Aion2ItemNames.NameOf(item, "en") is { } name && name.Contains("(Bound)", StringComparison.Ordinal);
                _bound[item] = bound;
            }

            return bound;
        }
    }

    /// <summary>A key that opens a Hidden Cube ("Key: Hidden Cube", bound or not, for some days or not).</summary>
    public static bool IsCubeKey(int item) => Aion2ItemNames.NameOf(item, "en") is { } name && name.StartsWith("Key: Hidden Cube", StringComparison.Ordinal);

    /// <summary>Whether the character carries a Hidden Cube key in its bag (where it opens a cube from); null while its bag is not known.</summary>
    public static bool? HoldsCubeKey(string? name)
    {
        if (name is null)
        {
            return null;
        }

        lock (Gate)
        {
            return _characters.TryGetValue(name, out CharacterHoldings? held) ? held.Bag.Any(kv => kv.Value > 0 && IsCubeKey(kv.Key)) : null;
        }
    }

    /// <summary>A currency (Kinah, Abyss Points, coins): carried by the character, not a material to move around.</summary>
    public static bool IsCurrency(int item) => item / 1_000_000 == 930;

    /// <summary>
    /// Takes the stock of the character logged in (called a few times a second by the window; cheap when nothing changed). A new login's stock waits for its
    /// name: the name changes from the one before, or stays for 10 s.
    /// </summary>
    public static void Capture()
    {
        if (!Aion2Stock.HasData)
        {
            return;
        }

        bool changed = false;
        lock (Gate)
        {
            DateTime now = Clock();
            if (Aion2Stock.SnapshotAt != _snapshotSeen)
            {
                _snapshotSeen = Aion2Stock.SnapshotAt;
                _owner = null;
                _loginWas = CurrentName();
                _loginAt = now;
                _stockChanged = true;
                _guessedAt = null;
                _beforeGuess = null;
                _ownerLost = false;
            }

            string? current = CurrentName();
            if (_owner is not null && current is { Length: > 0 } && current != _owner)
            {
                if (_guessedAt is { } guessed && now - guessed < TimeSpan.FromMinutes(1))
                {
                    // the guess was wrong: the stock is the newly named character's
                    if (_beforeGuess is not null)
                    {
                        _characters[_owner] = _beforeGuess;
                    }
                    else
                    {
                        _characters.Remove(_owner);
                    }

                    _owner = current;
                    _stockChanged = true;
                    _dirty = true;
                    changed = true;
                }
                else
                {
                    _owner = null;
                    _ownerLost = true;
                }

                _guessedAt = null;
                _beforeGuess = null;
            }

            if (_owner is null && !_ownerLost && current is { Length: > 0 } name && (name != _loginWas || now - _loginAt >= TimeSpan.FromSeconds(10)))
            {
                _owner = name;
                if (name == _loginWas)
                {
                    _guessedAt = now;
                    _beforeGuess = _characters.GetValueOrDefault(name);
                }
            }

            if (_owner is not null && _stockChanged)
            {
                _stockChanged = false;
                (int server, string? account) = Whereabouts(_owner);
                var held = new CharacterHoldings
                {
                    Name = _owner, ServerId = server, Account = account, SavedAt = now,
                    Bag = Aion2Stock.Totals(StockSources.Inventory), Storage = Aion2Stock.Totals(StockSources.CharacterStorage),
                };
                _characters[_owner] = held;
                _accounts[AccountKey(held.Account, held.ServerId)] = new AccountHoldings { SavedAt = now, Items = Aion2Stock.Totals(StockSources.AccountStorage) };
                _dirty = true;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static string AccountKey(string? account, int server) => (account ?? "?") + "|" + server;

    /// <summary>Every character with holdings, the latest first.</summary>
    public static IReadOnlyList<CharacterHoldings> Characters()
    {
        lock (Gate)
        {
            return _characters.Values.OrderByDescending(c => c.SavedAt).ToList();
        }
    }

    /// <summary>The characters that share an account storage with this one: the same server and, where known, the same account.</summary>
    public static IReadOnlyList<CharacterHoldings> Mates(string name)
    {
        lock (Gate)
        {
            if (!_characters.TryGetValue(name, out CharacterHoldings? me))
            {
                return Array.Empty<CharacterHoldings>();
            }

            return _characters.Values.Where(c => c.Name != name && c.ServerId == me.ServerId && (me.Account is null || c.Account is null || c.Account == me.Account))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>The server storage this character uses, as last seen (null before any).</summary>
    public static AccountHoldings? AccountOf(string name)
    {
        lock (Gate)
        {
            return _characters.TryGetValue(name, out CharacterHoldings? me) && _accounts.TryGetValue(AccountKey(me.Account, me.ServerId), out AccountHoldings? a) ? a : null;
        }
    }

    /// <summary>How many of an unbound item this character's account mates hold (bag and storage), each with its name; a bound item: none.</summary>
    public static IReadOnlyList<HoldingPlace> OnOthers(string name, int item)
    {
        if (IsBound(item))
        {
            return Array.Empty<HoldingPlace>();
        }

        return Mates(name).Select(c => new HoldingPlace(c.Name, c.Bag.GetValueOrDefault(item) + c.Storage.GetValueOrDefault(item))).Where(p => p.Count > 0).ToList();
    }

    /// <summary>
    /// Everything the characters that share a server storage with <paramref name="name"/> can put together: what they hold unbound (the character itself
    /// included) and all of that storage; per item the places, the most first. Currencies are left out.
    /// </summary>
    public static IReadOnlyList<(int Item, long Total, IReadOnlyList<HoldingPlace> Places)> Combined(string name)
    {
        var places = new Dictionary<int, List<HoldingPlace>>();
        void Add(int item, string? who, long count)
        {
            if (count <= 0 || IsCurrency(item) || (who is not null && IsBound(item)))
            {
                return;
            }

            if (!places.TryGetValue(item, out List<HoldingPlace>? list))
            {
                places[item] = list = new List<HoldingPlace>();
            }

            list.Add(new HoldingPlace(who, count));
        }

        lock (Gate)
        {
            if (!_characters.TryGetValue(name, out CharacterHoldings? me))
            {
                return Array.Empty<(int, long, IReadOnlyList<HoldingPlace>)>();
            }

            foreach (CharacterHoldings c in new[] { me }.Concat(Mates(name)))
            {
                foreach (int item in c.Bag.Keys.Union(c.Storage.Keys))
                {
                    Add(item, c.Name, c.Bag.GetValueOrDefault(item) + c.Storage.GetValueOrDefault(item));
                }
            }

            if (_accounts.TryGetValue(AccountKey(me.Account, me.ServerId), out AccountHoldings? account))
            {
                foreach ((int item, long count) in account.Items)
                {
                    Add(item, null, count);
                }
            }
        }

        return places.Select(kv => (kv.Key, kv.Value.Sum(p => p.Count), (IReadOnlyList<HoldingPlace>)kv.Value.OrderByDescending(p => p.Count).ToList()))
            .OrderByDescending(x => x.Item2).ToList();
    }

    /// <summary>Forgets everything (tests, the pictures).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _characters = new Dictionary<string, CharacterHoldings>(StringComparer.Ordinal);
            _accounts = new Dictionary<string, AccountHoldings>(StringComparer.Ordinal);
            _snapshotSeen = null;
            _owner = null;
            _stockChanged = true;
            _guessedAt = null;
            _beforeGuess = null;
            _ownerLost = false;
        }
    }

    /// <summary>Puts holdings in by hand (tests, the pictures).</summary>
    public static void Put(CharacterHoldings held, Dictionary<int, long>? accountStorage = null)
    {
        lock (Gate)
        {
            _characters[held.Name] = held;
            if (accountStorage is not null)
            {
                _accounts[AccountKey(held.Account, held.ServerId)] = new AccountHoldings { SavedAt = held.SavedAt, Items = accountStorage };
            }
        }
    }

    private sealed class Store
    {
        public Dictionary<string, CharacterHoldings> Characters { get; set; } = new();
        public Dictionary<string, AccountHoldings> Accounts { get; set; } = new();
    }

    /// <summary>Starts keeping the holdings in <see cref="StorePath"/> (the live meter only), loads what is there and takes the stock every 2 seconds.</summary>
    public static void EnablePersistence()
    {
        Changed += Flush;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(force: true);
        _timer = new System.Threading.Timer(_ =>
        {
            try
            {
                Capture();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // the stock changed while it was read: the next tick takes it
            }
            catch (Exception ex)
            {
                // anything else must not end the meter from a timer thread; noted once, the next tick tries again
                if (!_timerErrorNoted)
                {
                    _timerErrorNoted = true;
                    AionDPS.CrashLog.Write("the inventories could not be taken", ex);
                }
            }
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        lock (Gate)
        {
            _persist = true;
            try
            {
                if (File.Exists(StorePath) && JsonSerializer.Deserialize<Store>(File.ReadAllText(StorePath)) is { } loaded)
                {
                    _characters = new Dictionary<string, CharacterHoldings>(loaded.Characters.Where(kv => kv.Key.Length > 0), StringComparer.Ordinal);
                    _accounts = new Dictionary<string, AccountHoldings>(loaded.Accounts, StringComparer.Ordinal);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
            {
                if (ex is JsonException)
                {
                    AionDPS.AtomicFile.KeepDamaged(StorePath);
                }

                // a broken file: the holdings come again with each character's next login
            }
        }
    }

    private static void Flush() => Flush(force: false);

    /// <summary>Writes what is not saved yet: at most every 10 seconds, or now (<paramref name="force"/>, the meter is closing).</summary>
    public static void Flush(bool force)
    {
        string json;
        lock (Gate)
        {
            if (!_persist || !_dirty || (!force && DateTime.UtcNow - _savedAt < TimeSpan.FromSeconds(10)))
            {
                return;
            }

            json = JsonSerializer.Serialize(new Store { Characters = _characters, Accounts = _accounts });
            _dirty = false;
            _savedAt = DateTime.UtcNow;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AionDPS.AtomicFile.WriteAllText(StorePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (Gate)
            {
                _dirty = true; // kept in memory; the next change tries again
            }
        }
    }
}
