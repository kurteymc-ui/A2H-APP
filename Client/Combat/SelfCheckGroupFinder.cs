using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>The group finder's join frame (0x0b97) names who joined: a real frame of 2026-11-01 (MVP joining the owner's group).</summary>
public static class SelfCheckGroupFinder
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Group finder:");
        byte[] mvp = Convert.FromHexString("0b970c03e789020000002305034d5650060000002d000000ec0a000001160516050430d2020000000000000101");
        string? name = Aion2GroupFinder.JoinedName(mvp);
        bool joined = name == "MVP";
        bool other = Aion2GroupFinder.JoinedName(Convert.FromHexString("0b970d03e789020000002305034d5650")) is null; // not the join shape
        Console.WriteLine($"  -> the join frame names {name ?? "nobody"} (expected MVP), another shape names nobody: {joined && other}");
        return joined && other;
    }
}
