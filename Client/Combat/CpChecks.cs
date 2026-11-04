using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>A worn piece as a check keeps it: slot, item, the enchant number the game sends, the stones (stat id, colour) and soul-binding lines (stat id, value).</summary>
public sealed record CpCheckPiece(int Slot, int ItemId, int Enchant, int[][] Stones, long[][] Stats)
{
    public Aion2EquippedItem ToItem() =>
        new(Slot, ItemId, Enchant, Stones.Select(s => new Aion2Stone(s[0], s[1])).ToList(), Stats.Select(s => new Aion2RolledStat((int)s[0], s[1])).ToList());

    public static CpCheckPiece Of(Aion2EquippedItem g) => new(g.SlotIndex, g.ItemId, g.Enchant,
        (g.Stones ?? Array.Empty<Aion2Stone>()).Select(s => new[] { s.StatId, s.Tier }).ToArray(),
        (g.Stats ?? Array.Empty<Aion2RolledStat>()).Select(s => new[] { (long)s.StatId, s.Value }).ToArray());

    [JsonIgnore]
    public string Signature => $"{Slot}:{ItemId}:{Enchant}:{string.Join(',', Stones.Select(s => s[0] + "/" + s[1]))}:{string.Join(',', Stats.Select(s => s[0] + "=" + s[1]))}";
}

/// <summary>
/// One reading of the character's Combat Power from the game - the legion's member list the game sends at every login carries every member's Combat Power,
/// the character played's own included - with what the calculator saw then: the worn pieces it counts, the Item Level, a signature of everything else that
/// moves Combat Power (skill levels, Daevanion nodes, titles, the pieces it doesn't count, wings) and, per stat id of the stat frame, the game's total
/// less what the worn pieces put in by the tables (what the rest of the character adds).
/// </summary>
/// <remarks>Stale: the game's number didn't move although the counted pieces did (a list sent again before the server had counted the change): such a
/// reading is kept but never compared with, so the change is measured against the next reading that has the new number.</remarks>
public sealed record CpCheckPoint(DateTime At, int Cp, int ItemLevel, List<CpCheckPiece> Gear, string Rest, Dictionary<int, long> Residual, bool Stale = false)
{
    [JsonIgnore]
    public string GearSignature => string.Join(';', Gear.OrderBy(g => g.Slot).Select(g => g.Signature));
}

/// <summary>A stat of the stat frame whose change the tables did not explain: what the game's total moved by, and what the worn pieces moved it by by the tables.</summary>
public sealed record CpStatGap(int StatId, long Game, long Tables);

/// <summary>
/// A change measured on the character: its Combat Power in the game at two logins, and in between only worn pieces the calculator counts changed (nothing
/// else that moves Combat Power, and none of the stats those pieces don't touch). The calculator's own number for the change is worked out again from the two
/// sets of pieces whenever it is needed, so it always follows the model in use. <paramref name="ItemLevelBefore"/>: the Item Level at the first login (0 in
/// measurements kept before 1.20.1).
/// </summary>
public sealed record CpMeasurement(DateTime At, int CpBefore, int CpAfter, int ItemLevel, List<CpCheckPiece> Before, List<CpCheckPiece> After, Dictionary<int, int> Frame,
    List<CpStatGap> Gaps, int ItemLevelBefore = 0)
{
    [JsonIgnore]
    public int Actual => CpAfter - CpBefore;
}

/// <summary>The checks of one character: its readings (the last <see cref="CpChecks.MostPoints"/>) and the changes measured from them.</summary>
public sealed class CpCheckState
{
    public List<CpCheckPoint> Points { get; set; } = new();

    public List<CpMeasurement> Measured { get; set; } = new();
}

/// <summary>A measured change as the page shows it: the game's Combat Power change, the calculator's (without anything measured), and what changed.</summary>
public sealed record CpMeasuredChange(DateTime At, int Actual, double Predicted, IReadOnlyList<CpChange> Changes, IReadOnlyList<CpStatGap> Gaps);

/// <summary>
/// The CP calculator checked against the game, character by character, from the logins on (3 November 2026): at every login the game sends the character's own
/// Combat Power with the legion list. Between two logins at which only worn pieces changed, the game's Combat Power changed by exactly what those pieces are
/// worth; that is set against what the calculator said, and from then on the calculator counts with what was measured on this character (a factor per stat of
/// the model, found by least squares over all its measured changes and held close to 1 where little was measured). The same pair of logins shows whether the
/// game's stat totals moved exactly as the tables say. The character page (stones, the Combat Power it shows) is read once per character at its login.
/// Kept per character in a file next to the meter's other files. A measured change is sent to the website only when the player switched that on, without
/// a name (Upload/CpShare.cs).
/// </summary>
public static class CpChecks
{
    public const int MostPoints = 40;
    public const int MostMeasured = 60;

    /// <summary>A change has to be worth this much to the calculator to be measured (the game rounds, and small changes say little).</summary>
    private const double LeastChange = 60;

    /// <summary>How strongly a factor is held at 1 against the measurements (0: the measurements alone; 1: half way).</summary>
    private const double Pull = 0.1;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, CpCheckState> States = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CpOfficialRead?> Official = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DateTime> Asked = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (int Version, object Model, IReadOnlyDictionary<string, double> Factors)> FactorCache = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static Func<Aion2EntityDirectory?>? _directory;
    private static System.Threading.Timer? _timer;
    private static bool _persist;
    private static int _version;
    private static int _busy;

    /// <summary>Raised when a reading, a measurement or the character page came in (not on the UI thread).</summary>
    public static event Action? Changed;

    /// <summary>Goes up with every change, for a page that redraws when something new is there.</summary>
    public static int Version => Volatile.Read(ref _version);

    public static string StorePath => Path.Combine(Path.GetDirectoryName(Aion2CharacterStore.DefaultPath)!, "aion2-cp-checks.json");

    /// <summary>The live meter's start: loads the file and looks at the live capture every five seconds.</summary>
    public static void Start(Func<Aion2EntityDirectory?> directory)
    {
        _directory = directory;
        lock (Gate)
        {
            if (!_persist)
            {
                _persist = true;
                Load();
            }
        }

        _timer ??= new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private static string Key(string name, int server) => name + "|" + server.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (_directory?.Invoke() is { } directory)
            {
                Note(directory, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            // a check is never worth a crash of the meter (this runs on a timer thread); the next tick tries again
            Console.Error.WriteLine("cp checks: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // ------------------------------------------------------------------ readings

    /// <summary>
    /// One look at the live capture: the character page of the character played is read once per run; a legion list that arrived at this character's login and
    /// names it becomes a reading once the login's other lists are in (15 seconds; given up after two minutes without gear or stat frame).
    /// </summary>
    internal static CpCheckPoint? Note(Aion2EntityDirectory directory, DateTime now)
    {
        Aion2CharacterInfo? me = directory.LocalCharacter;
        if (me is not { Restored: false } || me.Name.Length == 0 || me.ServerId <= 0)
        {
            return null;
        }

        string who = Key(me.Name, me.ServerId);
        AskOfficial(me.Name, me.ServerId, fresh: false);
        DateTime rosterAt = directory.LegionRosterAt;
        if (rosterAt == DateTime.MinValue || rosterAt.AddMinutes(1) < directory.LocalCharacterSince || now - rosterAt < TimeSpan.FromSeconds(15))
        {
            return null;
        }

        string mark = who + "|" + rosterAt.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (Gate)
        {
            if (Done.Contains(mark))
            {
                return null;
            }
        }

        LegionMember? own = Aion2Moved.Find(directory.LegionRoster(), m => m.Name, m => m.ServerId, me.Name, me.ServerId); // the list may name the server it was made on
        IReadOnlyList<Aion2EquippedItem> gear = directory.LocalEquipment;
        IReadOnlyDictionary<int, int>? frame = directory.LocalStats;
        if (own is not { CombatPower: > 0 } || gear.Count == 0 || frame is null)
        {
            if (own is not { CombatPower: > 0 } || now - rosterAt > TimeSpan.FromMinutes(2))
            {
                lock (Gate)
                {
                    Done.Add(mark);
                }
            }

            return null;
        }

        lock (Gate)
        {
            Done.Add(mark);
        }

        CpCheckPoint point = Point(rosterAt, own.CombatPower, own.ItemLevel, gear, frame, Rest(directory, gear));
        if (Add(me.Name, me.ServerId, point) is { } measured && _persist)
        {
            CpShare.Offer(measured, me.ClassCode, me.ServerId); // only when the player switched sharing on
        }

        return point;
    }

    /// <summary>A reading out of the game's numbers: the pieces the calculator counts, the residual of every watched stat of the frame.</summary>
    internal static CpCheckPoint Point(DateTime at, int cp, int itemLevel, IReadOnlyList<Aion2EquippedItem> gear, IReadOnlyDictionary<int, int> frame, string rest)
    {
        var counted = gear.Where(g => Aion2CpData.Item(g.ItemId) is not null).OrderBy(g => g.SlotIndex).ToList();
        Dictionary<int, long> sums = CpCalc.StatSums(CpCalc.Pieces(counted, new CpTotals(0, 0, 0, 0, 0)));
        var residual = Watched().ToDictionary(id => id, id => frame.GetValueOrDefault(id) - sums.GetValueOrDefault(id));
        return new CpCheckPoint(at, cp, itemLevel, counted.Select(CpCheckPiece.Of).ToList(), rest, residual);
    }

    /// <summary>The game's stat totals of a reading, for the stats it keeps (its residual and what its pieces put in).</summary>
    private static Dictionary<int, int> Frame(CpCheckPoint point)
    {
        Dictionary<int, long> sums = CpCalc.StatSums(CpCalc.Pieces(point.Gear.Select(g => g.ToItem()).ToList(), new CpTotals(0, 0, 0, 0, 0)));
        return point.Residual.ToDictionary(kv => kv.Key, kv => (int)(kv.Value + sums.GetValueOrDefault(kv.Key)));
    }

    /// <summary>The stat ids a reading keeps: every stat of the meter's tables that Combat Power counts, and the attribute points (ids 1 to 17).</summary>
    private static IEnumerable<int> Watched() =>
        Enumerable.Range(Aion2Attributes.FirstId, Aion2Attributes.LastId - Aion2Attributes.FirstId + 1)
            .Concat(Aion2CpData.StatIds.Where(id => Aion2CpData.Token(id) is { } t && Aion2CpData.KeyOf(t) is not null))
            .Distinct();

    /// <summary>Everything else that moves Combat Power, as one text: skill and stigma levels, Daevanion nodes per board, titles worn and owned, the pieces the
    /// calculator doesn't count (Arcana, belt ...), the wings. Two readings with the same text had only the counted pieces changed in between.</summary>
    internal static string Rest(Aion2EntityDirectory directory, IReadOnlyList<Aion2EquippedItem> gear)
    {
        string skills = string.Join(',', directory.LocalSkills.OrderBy(s => s.SkillId).Select(s => $"{s.SkillId}:{s.Level}:{(s.Equipped ? 1 : 0)}{(s.Stigma ? 1 : 0)}"));
        string boards = string.Join(',', directory.LocalDaevanion.OrderBy(b => b.BoardId).Select(b => $"{b.BoardId}:{b.NodeIds.Count}"));
        string titles = string.Join(',', directory.LocalTitles.OrderBy(t => t.Slot).Select(t => $"{t.Slot}:{t.TitleId}")) + ":" + directory.OwnedCounts().GetValueOrDefault("titles");
        string others = string.Join(',', gear.Where(g => Aion2CpData.Item(g.ItemId) is null).OrderBy(g => g.SlotIndex).Select(g => $"{g.SlotIndex}:{g.ItemId}:{g.Enchant}"));
        return $"s{skills}|b{boards}|t{titles}|o{others}|w{directory.LocalWing?.WingId}";
    }

    /// <summary>Keeps a reading; when the character's reading before it differs only in counted pieces, the change between them is measured (and returned).</summary>
    internal static CpMeasurement? Add(string name, int server, CpCheckPoint point)
    {
        CpMeasurement? result = null;
        string who = Key(name, server);
        lock (Gate)
        {
            if (!States.TryGetValue(who, out CpCheckState? state))
            {
                state = new CpCheckState();
                States[who] = state;
            }

            CpCheckPoint? before = state.Points.LastOrDefault(p => !p.Stale);
            if (before is not null && point.Cp == before.Cp && point.GearSignature != before.GearSignature
                && Math.Abs(Predicted(before.Gear, point.Gear, Frame(point), point.ItemLevel, null).Parts.Values.Sum()) >= LeastChange)
            {
                point = point with { Stale = true };
            }

            state.Points.Add(point);
            if (state.Points.Count > MostPoints)
            {
                state.Points.RemoveRange(0, state.Points.Count - MostPoints);
            }

            if (before is not null && !point.Stale && Measure(before, point) is { } measured)
            {
                result = measured;
                state.Measured.Add(measured);
                if (state.Measured.Count > MostMeasured)
                {
                    state.Measured.RemoveRange(0, state.Measured.Count - MostMeasured);
                }
            }

            Save();
        }

        Interlocked.Increment(ref _version);
        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// The change between two readings, when it can be measured: the counted pieces differ, nothing else that moves Combat Power does (the rest's text, and
    /// the residual of every stat the changed pieces don't touch), the calculator's number for it is big enough and not a wide range (a stone whose value
    /// the page didn't name), and the game's change is within a factor of four of it (else something unseen changed too). Null otherwise.
    /// </summary>
    internal static CpMeasurement? Measure(CpCheckPoint before, CpCheckPoint after)
    {
        if (before.GearSignature == after.GearSignature || before.Rest != after.Rest)
        {
            return null;
        }

        var oldItems = before.Gear.Select(g => g.ToItem()).ToList();
        var newItems = after.Gear.Select(g => g.ToItem()).ToList();
        Dictionary<int, long> oldSums = CpCalc.StatSums(CpCalc.Pieces(oldItems, new CpTotals(0, 0, 0, 0, 0)));
        Dictionary<int, long> newSums = CpCalc.StatSums(CpCalc.Pieces(newItems, new CpTotals(0, 0, 0, 0, 0)));
        var touched = oldSums.Keys.Union(newSums.Keys).Where(id => oldSums.GetValueOrDefault(id) != newSums.GetValueOrDefault(id)).ToHashSet();
        foreach (int id in before.Residual.Keys.Union(after.Residual.Keys))
        {
            if (!touched.Contains(id) && before.Residual.GetValueOrDefault(id) != after.Residual.GetValueOrDefault(id))
            {
                return null; // something besides the pieces moved this stat (a collection, a buff, a node ...)
            }
        }

        var frame = after.Residual.ToDictionary(kv => kv.Key, kv => (int)(kv.Value + newSums.GetValueOrDefault(kv.Key)));
        var (parts, width) = Predicted(before.Gear, after.Gear, frame, after.ItemLevel, null);
        double predicted = parts.Values.Sum();
        int actual = after.Cp - before.Cp;
        if (Math.Abs(predicted) < LeastChange || width > Math.Max(30, Math.Abs(predicted) * 0.1) || actual == 0 || Math.Sign(actual) != Math.Sign(predicted)
            || actual / predicted is < 0.25 or > 4)
        {
            return null;
        }

        // the stats the pieces touched: did the game's totals move as the tables say? (a stone whose value is a range, and a stat the frame doesn't carry, aside)
        var ranged = StoneStatsChanged(before.Gear, after.Gear);
        var gaps = new List<CpStatGap>();
        foreach (int id in touched.OrderBy(id => id))
        {
            long tables = newSums.GetValueOrDefault(id) - oldSums.GetValueOrDefault(id);
            long game = tables + after.Residual.GetValueOrDefault(id) - before.Residual.GetValueOrDefault(id);
            if (game != tables && game != 0 && !ranged.Contains(id))
            {
                gaps.Add(new CpStatGap(id, game, tables));
            }
        }

        return new CpMeasurement(after.At, before.Cp, after.Cp, after.ItemLevel, before.Gear, after.Gear, frame, gaps, before.ItemLevel);
    }

    /// <summary>The stat ids of the stones that differ between two sets of pieces (their values are a range unless the page named them).</summary>
    private static HashSet<int> StoneStatsChanged(List<CpCheckPiece> before, List<CpCheckPiece> after)
    {
        var ids = new HashSet<int>();
        foreach (int slot in before.Select(g => g.Slot).Union(after.Select(g => g.Slot)))
        {
            CpCheckPiece? a = before.FirstOrDefault(g => g.Slot == slot), b = after.FirstOrDefault(g => g.Slot == slot);
            string Stones(CpCheckPiece? p) => p is null ? "" : p.ItemId + ":" + string.Join(',', p.Stones.Select(s => s[0] + "/" + s[1]));
            if (Stones(a) != Stones(b))
            {
                foreach (int[] s in (a?.Stones ?? Array.Empty<int[]>()).Concat(b?.Stones ?? Array.Empty<int[]>()))
                {
                    ids.Add(s[0]);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// What the calculator says a change of pieces is worth, by the model's stat (without anything measured: <paramref name="factors"/> null), counted at the
    /// character's numbers after the change; and how wide it is where stones of a range changed.
    /// </summary>
    internal static (Dictionary<string, double> Parts, double Width) Predicted(List<CpCheckPiece> before, List<CpCheckPiece> after, IReadOnlyDictionary<int, int> frame, int itemLevel,
        IReadOnlyDictionary<string, double>? factors, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        var oldItems = before.Select(g => g.ToItem()).ToList();
        var newItems = after.Select(g => g.ToItem()).ToList();
        CpTotals totals = CpCalc.Totals(frame, CpCalc.Pieces(newItems, new CpTotals(0, 0, 0, 0, 0), model), new CpCharacter(itemLevel > 0 ? itemLevel : null, factors), model);
        var parts = new Dictionary<string, double>(StringComparer.Ordinal);
        double width = 0;
        var ranged = StoneStatsChanged(before, after);
        void Count(IReadOnlyList<Aion2EquippedItem> gear, int sign)
        {
            foreach (CpPiece piece in CpCalc.Pieces(gear, totals, model))
            {
                foreach (CpLine line in piece.Lines)
                {
                    if (Aion2CpData.CpParts(line.Stat, (line.Value + line.ValueMax) / 2, totals, model) is { } split)
                    {
                        foreach (var (key, cp) in split)
                        {
                            parts[key] = parts.GetValueOrDefault(key) + sign * cp;
                        }
                    }

                    if (line.Kind == CpLineKind.Stone && line.ValueMax > line.Value && ranged.Contains(Aion2CpData.StatId(line.Stat)))
                    {
                        width += (line.CpMax ?? 0) - (line.CpMin ?? 0);
                    }
                }
            }
        }

        Count(newItems, +1);
        Count(oldItems, -1);
        foreach (string key in parts.Where(p => Math.Abs(p.Value) < 1e-6).Select(p => p.Key).ToList())
        {
            parts.Remove(key); // a line on both sides cancels out
        }

        return (parts, width);
    }

    // ------------------------------------------------------------------ what the page reads

    /// <summary>The character's readings and measured changes (copies), null before its first reading.</summary>
    public static CpCheckState? State(string name, int server)
    {
        lock (Gate)
        {
            return States.TryGetValue(Key(name, server), out CpCheckState? s)
                ? new CpCheckState { Points = s.Points.ToList(), Measured = s.Measured.ToList() }
                : null;
        }
    }

    /// <summary>The character's measured changes as the page shows them, the newest first.</summary>
    public static IReadOnlyList<CpMeasuredChange> MeasuredChanges(string name, int server, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        var list = new List<CpMeasuredChange>();
        foreach (CpMeasurement m in State(name, server)?.Measured ?? new List<CpMeasurement>())
        {
            var (parts, _) = Predicted(m.Before, m.After, m.Frame, m.ItemLevel, null, model);
            CpTotals totals = CpCalc.Totals(m.Frame, CpCalc.Pieces(m.After.Select(g => g.ToItem()).ToList(), new CpTotals(0, 0, 0, 0, 0), model), new CpCharacter(m.ItemLevel > 0 ? m.ItemLevel : null), model);
            IReadOnlyList<CpChange> changes = CpCalc.Changes(m.Before.Select(g => g.ToItem()).ToList(), m.After.Select(g => g.ToItem()).ToList(), totals, m.At, model);
            list.Add(new CpMeasuredChange(m.At, m.Actual, parts.Values.Sum(), changes, m.Gaps));
        }

        list.Reverse();
        return list;
    }

    /// <summary>
    /// The factors measured on the character, by the model's stat: the least-squares fit of "the game's change = the sum over the stats of factor x what the
    /// calculator said for that stat" over all its measured changes, each factor held towards 1 in proportion to how little was measured of it, kept within
    /// 0.5 to 2. Only stats with at least 150 CP of measured change get one; the rest stay at the model's value.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Factors(string name, int server, CpModel? model = null)
    {
        model ??= Aion2CpData.Model;
        string who = Key(name, server);
        int version = Version;
        lock (Gate)
        {
            if (FactorCache.TryGetValue(who, out var cached) && cached.Version == version && ReferenceEquals(cached.Model, model))
            {
                return cached.Factors;
            }
        }

        var rows = new List<(Dictionary<string, double> Parts, double Actual)>();
        foreach (CpMeasurement m in State(name, server)?.Measured ?? new List<CpMeasurement>())
        {
            var (parts, _) = Predicted(m.Before, m.After, m.Frame, m.ItemLevel, null, model);
            rows.Add((parts, m.Actual));
        }

        IReadOnlyDictionary<string, double> factors = Solve(rows);
        lock (Gate)
        {
            FactorCache[who] = (version, model, factors);
        }

        return factors;
    }

    /// <summary>The fit of <see cref="Factors"/> (ridge regression towards 1, each stat's pull scaled by the size of its measured changes).</summary>
    internal static IReadOnlyDictionary<string, double> Solve(IReadOnlyList<(Dictionary<string, double> Parts, double Actual)> rows)
    {
        var keys = rows.SelectMany(r => r.Parts.Where(p => Math.Abs(p.Value) > 1e-9).Select(p => p.Key)).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        int n = keys.Count;
        if (n == 0)
        {
            return result;
        }

        var m = new double[n, n];
        var v = new double[n];
        var size = keys.Select(k => rows.Sum(r => r.Parts.GetValueOrDefault(k) * r.Parts.GetValueOrDefault(k)) / rows.Count).ToArray();
        foreach (var (parts, actual) in rows)
        {
            for (int i = 0; i < n; i++)
            {
                double a = parts.GetValueOrDefault(keys[i]);
                v[i] += a * actual;
                for (int j = 0; j < n; j++)
                {
                    m[i, j] += a * parts.GetValueOrDefault(keys[j]);
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            m[i, i] += Pull * size[i] * rows.Count;
            v[i] += Pull * size[i] * rows.Count;
        }

        double[] f = GaussSolve(m, v);
        for (int i = 0; i < n; i++)
        {
            double measured = rows.Sum(r => Math.Abs(r.Parts.GetValueOrDefault(keys[i])));
            if (measured >= 150 && double.IsFinite(f[i]))
            {
                result[keys[i]] = Math.Clamp(f[i], 0.5, 2);
            }
        }

        return result;
    }

    private static double[] GaussSolve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
            {
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (pivot != col)
            {
                for (int c = 0; c < n; c++)
                {
                    (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
                }

                (x[col], x[pivot]) = (x[pivot], x[col]);
            }

            double d = Math.Abs(m[col, col]) < 1e-12 ? 1e-12 : m[col, col];
            for (int r = col + 1; r < n; r++)
            {
                double k = m[r, col] / d;
                if (k == 0)
                {
                    continue;
                }

                for (int c = col; c < n; c++)
                {
                    m[r, c] -= k * m[col, c];
                }

                x[r] -= k * x[col];
            }
        }

        var result = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = x[i];
            for (int c = i + 1; c < n; c++)
            {
                s -= m[i, c] * result[c];
            }

            result[i] = s / (Math.Abs(m[i, i]) < 1e-12 ? 1e-12 : m[i, i]);
        }

        return result;
    }

    // ------------------------------------------------------------------ the character page

    /// <summary>The character page as read at the character's login (or the player's refresh); null before it came in or when it couldn't be read.</summary>
    public static CpOfficialRead? OfficialOf(string name, int server)
    {
        lock (Gate)
        {
            return Official.GetValueOrDefault(Key(name, server));
        }
    }

    /// <summary>Whether a read of the character page is on its way.</summary>
    public static bool Reading(string name, int server)
    {
        lock (Gate)
        {
            return Asked.TryGetValue(Key(name, server), out DateTime at) && at > DateTime.MinValue && !Official.ContainsKey(Key(name, server));
        }
    }

    /// <summary>Reads the character page: once per character and run at its login, again (as a refresh on the website) when the player asks; not with the
    /// privacy terms declined. A page that couldn't be read is asked again after ten minutes.</summary>
    public static void AskOfficial(string name, int server, bool fresh)
    {
        if (Ui.PrivacyConsent.Restricted || !_persist)
        {
            return;
        }

        string who = Key(name, server);
        lock (Gate)
        {
            if (!fresh && Asked.TryGetValue(who, out DateTime at) && (Official.GetValueOrDefault(who) is not null || DateTime.UtcNow - at < TimeSpan.FromMinutes(10)))
            {
                return;
            }

            Asked[who] = DateTime.UtcNow;
            if (fresh)
            {
                Official.Remove(who);
            }
        }

        _ = CpCharacterPage.ReadAsync(name, server, fresh).ContinueWith(t =>
        {
            CpOfficialRead? read = t.IsCompletedSuccessfully ? t.Result : null;
            lock (Gate)
            {
                if (read is not null || !Official.ContainsKey(who))
                {
                    Official[who] = read;
                }
            }

            Interlocked.Increment(ref _version);
            Changed?.Invoke();
        }, TaskScheduler.Default);
    }

    // ------------------------------------------------------------------ the picture and the self-test

    /// <summary>Sets a character's checks and character page without the file (the picture mode, the self-test).</summary>
    internal static void Put(string name, int server, CpCheckState? state, CpOfficialRead? official = null)
    {
        lock (Gate)
        {
            string who = Key(name, server);
            if (state is null)
            {
                States.Remove(who);
            }
            else
            {
                States[who] = state;
            }

            if (official is not null)
            {
                Official[who] = official;
            }
        }

        Interlocked.Increment(ref _version);
    }

    // ------------------------------------------------------------------ the file

    private static void Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, CpCheckState>>(File.ReadAllText(StorePath), Json);
                foreach (var (who, state) in loaded ?? new())
                {
                    States[who] = state;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            if (ex is JsonException)
            {
                AtomicFile.KeepDamaged(StorePath);
            }
        }
    }

    private static void Save()
    {
        if (!_persist)
        {
            return;
        }

        try
        {
            AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(States, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // the next reading tries again
        }
    }
}
