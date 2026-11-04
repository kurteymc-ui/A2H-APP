namespace AionDPS.Combat;

/// <summary>
/// How a player's direct hits landed, counted as they come (the meter's optional columns: crit, back, front, perfect, double, parried).
/// Damage over time and heals are not counted; the hit kinds are shares of the hits that carried them (<see cref="DamageEvent.Kinds"/>),
/// the crits a share of all direct hits.
/// </summary>
public struct HitStats
{
    /// <summary>Direct hits (no tick, no heal, some damage).</summary>
    public int Direct;
    public int Crit;

    /// <summary>The direct hits whose frame said how they landed (the base of the hit kind shares).</summary>
    public int Kinded;
    public int Back;
    public int Front;
    public int Perfect;
    public int Double;
    public int Parry;

    public void Add(DamageEvent ev)
    {
        if (ev.IsHeal || ev.IsTick || ev.Amount <= 0)
        {
            return;
        }

        Direct++;
        if (ev.IsCritical)
        {
            Crit++;
        }

        if (ev.Kinds is not HitKinds kinds)
        {
            return;
        }

        Kinded++;
        if ((kinds & HitKinds.Back) != 0)
        {
            Back++;
        }

        if ((kinds & HitKinds.Front) != 0)
        {
            Front++;
        }

        if ((kinds & HitKinds.Perfect) != 0)
        {
            Perfect++;
        }

        if ((kinds & HitKinds.Double) != 0)
        {
            Double++;
        }

        if ((kinds & HitKinds.Parry) != 0)
        {
            Parry++;
        }
    }

    public void Merge(in HitStats other)
    {
        Direct += other.Direct;
        Crit += other.Crit;
        Kinded += other.Kinded;
        Back += other.Back;
        Front += other.Front;
        Perfect += other.Perfect;
        Double += other.Double;
        Parry += other.Parry;
    }

    /// <summary>A share in percent, null without a base.</summary>
    public static double? Share(int part, int of) => of > 0 ? 100.0 * part / of : null;

    public readonly double? CritPercent => Share(Crit, Direct);

    public readonly double? BackPercent => Share(Back, Kinded);

    public readonly double? FrontPercent => Share(Front, Kinded);

    public readonly double? PerfectPercent => Share(Perfect, Kinded);

    public readonly double? DoublePercent => Share(Double, Kinded);

    public readonly double? ParryPercent => Share(Parry, Kinded);
}
