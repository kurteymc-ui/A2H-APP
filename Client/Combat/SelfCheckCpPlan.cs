namespace AionDPS.Combat;

/// <summary>
/// The order of Stigma and skill levels in the CP calculator (Combat/CpPlan.cs, 6 November 2026): the website's class build (Gladiator), the lower ranks
/// first - a Stigma to 10 before another from 10 on, a skill to 10 before another from 12 to 15 - the Shards a step takes, and a class without a build.
/// </summary>
public static class SelfCheckCpPlan
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] CP calculator: Stigmas and skills in the class build's order:");
        bool ok = true;
        CpClassPlan? gladiator = CpPlan.For("Gladiator");
        bool loaded = gladiator is not null && CpPlan.For("gladiator") is not null && CpPlan.For("Nobody") is null && CpPlan.Key("Zikel's Blessing") == "zikel s blessing"
            && gladiator.StigmaPlan.Count > 0 && gladiator.SkillOrder.Count > 0;
        Console.WriteLine($"  -> the eight class builds read, names as the build writes them: {loaded}");
        ok &= loaded;
        if (gladiator is null)
        {
            return false;
        }

        // Stigmas: Lunge Stance 3, Zikel's Blessing 5, Rage Burst 7, Focused Block 10. The build: Lunge Stance to 5 first, then Zikel's Blessing to 10,
        // Rage Burst on from 7 after Lunge Stance's 20; Focused Block is at the build's 10, so it goes on only past the build (to 15)
        var names = new[] { "lunge stance", "zikel s blessing", "rage burst", "focused block" };
        List<CpLevelStep> stigmas = CpPlan.StigmaPath(names, new[] { 3, 5, 7, 10 }, gladiator, 20);
        var firsts = stigmas.GroupBy(s => s.Index).Select(g => g.First()).ToList();
        bool stigmaOrder = firsts.Count == 4 && firsts[0] == new CpLevelStep(0, 3, 5) && firsts[1] == new CpLevelStep(1, 5, 10) && firsts[2] == new CpLevelStep(2, 7, 10)
            && firsts[3] == new CpLevelStep(3, 10, 15) && stigmas.IndexOf(firsts[3]) > stigmas.FindLastIndex(s => s.Index == 2 && s.To == 20);
        bool shards = CpPlan.ShardsTo(3, 5) == 2 && CpPlan.ShardsTo(5, 10) == 10 && CpPlan.ShardsTo(10, 15) == 20 && CpPlan.ShardsTo(15, 20) == 40;
        Console.WriteLine($"  -> Stigmas in the build's plan, one rank at a time, the Shards each step takes: {stigmaOrder && shards} " +
            $"({string.Join(", ", firsts.Select(s => $"{names[s.Index]} {s.From}->{s.To}"))})");
        ok &= stigmaOrder && shards;

        // skills: Overhead Slam 12, Rending Blow 4, Keen Strike 9 (the build takes it to 12): Rending Blow to 5, Keen Strike to 10, then Overhead Slam 12 to 15
        var skillNames = new[] { "overhead slam", "rending blow", "keen strike", "some other skill" };
        List<CpLevelStep> skills = CpPlan.SkillPath(skillNames, new[] { 12, 4, 9, 3 }, gladiator, 20);
        var skillFirsts = skills.GroupBy(s => s.Index).Select(g => g.First()).ToList();
        bool skillOrder = skillFirsts.Count == 4 && skillFirsts[0] == new CpLevelStep(1, 4, 5) && skillFirsts[1] == new CpLevelStep(2, 9, 10)
            && skillFirsts[2] == new CpLevelStep(0, 12, 15) && skillFirsts[3] == new CpLevelStep(3, 3, 5)
            && skills.Any(s => s.Index == 2 && s.From == 10 && s.To == 12) && skills.FindIndex(s => s.Index == 2 && s.To == 12) < skills.FindIndex(s => s.Index == 2 && s.To == 15);
        Console.WriteLine($"  -> skills by ranks in the build's order, a skill to 10 before another from 12 to 15, one the build doesn't name after: {skillOrder} " +
            $"({string.Join(", ", skillFirsts.Select(s => $"{skillNames[s.Index]} {s.From}->{s.To}"))})");
        ok &= skillOrder;

        // the calculator's skill steps go by the levels that count (CpWhole.SkillRanks: 8, 12, 16, 20; +500 / +900 / +1,000 on reaching 12 / 16 / 20):
        // Rending Blow to 8, Keen Strike to the build's 12, Overhead Slam 12 to 16, the other skill to 8
        List<CpLevelStep> counted = CpPlan.SkillPath(skillNames, new[] { 12, 4, 9, 3 }, gladiator, 20, CpWhole.SkillRanks);
        var countedFirsts = counted.GroupBy(s => s.Index).Select(g => g.First()).ToList();
        bool byTables = countedFirsts.Count == 4 && countedFirsts[0] == new CpLevelStep(1, 4, 8) && countedFirsts[1] == new CpLevelStep(2, 9, 12)
            && countedFirsts[2] == new CpLevelStep(0, 12, 16) && countedFirsts[3] == new CpLevelStep(3, 3, 8) && counted.All(s => s.To is 8 or 12 or 16 or 20 || s.Index == 2);
        Console.WriteLine($"  -> by the levels that count (8, 12, 16, 20): {byTables} ({string.Join(", ", countedFirsts.Select(s => $"{skillNames[s.Index]} {s.From}->{s.To}"))})");
        ok &= byTables;

        // a class without a build: by ranks only
        List<CpLevelStep> plain = CpPlan.StigmaPath(new[] { "a", "b" }, new[] { 12, 6 }, null, 20);
        bool none = plain.Count > 0 && plain[0] == new CpLevelStep(1, 6, 10);
        Console.WriteLine($"  -> without a build, the lower rank first: {none}");
        ok &= none;
        return ok;
    }
}
