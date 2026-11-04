using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>Game windows told by the screen (<see cref="HudWatcher"/>): the HUD is learnt in fights, a covered HUD hides the overlays and an uncovered one
/// brings them back, parts shown only in fights are dropped, and the meter's own overlays and a black picture decide nothing.</summary>
public static class SelfCheckHud
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Game windows by the screen (HUD):");
        const int n = HudWatcher.Points;
        var rnd = new Random(7);
        DateTime t = new(2026, 10, 31, 20, 0, 0);
        var none = new bool[n];

        // the HUD: points 0..999 a steady edge; 1000..1599 an edge only in fights (a boss's bar); the rest the moving world
        float[] Frame(bool fight, bool menu)
        {
            var e = new float[n];
            for (int i = 0; i < n; i++)
            {
                e[i] = i < 1000 ? (menu ? 4 : 60 + (float)rnd.NextDouble() * 8)
                    : i < 1600 ? (fight && !menu ? 70 + (float)rnd.NextDouble() * 6 : 3)
                    : (float)rnd.NextDouble() * 120;
            }

            return e;
        }

        var hud = new HudWatcher();
        bool notYet = true;
        for (int i = 0; i < HudWatcher.NeedLooks - 1; i++)
        {
            hud.Judge(Frame(true, false), none, true, t = t.AddSeconds(1));
            notYet &= !hud.Ready;
        }

        hud.Judge(Frame(true, false), none, true, t = t.AddSeconds(1));
        bool learnt = hud.Ready && hud.HudPoints is >= 1500 and <= 1700;
        int before = hud.HudPoints;
        Console.WriteLine($"  -> nothing before {HudWatcher.NeedLooks} looks in fights ({notYet}); learnt then, the HUD and the fight-only bar ({learnt}, {hud.HudPoints} points)");

        // the normal screen between fights: shown; the fight-only bar is dropped after a while
        bool shown = true;
        for (int i = 0; i < 40; i++)
        {
            shown &= !hud.Judge(Frame(false, false), none, false, t = t.AddSeconds(0.3));
        }

        // the bar's 600 points go, the HUD's 1000 stay (a few points of the moving world may have looked steady by chance; they stay, harmless)
        bool pruned = before - hud.HudPoints is >= 595 and <= 600 && hud.HudPoints >= 1000;
        Console.WriteLine($"  -> the normal screen keeps the overlays ({shown}); the fight-only bar is dropped ({pruned}, {before - hud.HudPoints} dropped, {hud.HudPoints} left)");

        // a game window covers the HUD: hidden at the third look; uncovered: back at the second
        bool early = !hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3)) && !hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3));
        bool hidden = hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3)) && hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3));
        bool stays = hud.Judge(Frame(false, false), none, false, t = t.AddSeconds(0.3));
        bool back = !hud.Judge(Frame(false, false), none, false, t = t.AddSeconds(0.3));
        Console.WriteLine($"  -> a covered HUD hides after the third look ({early && hidden}); uncovered, back at the second ({stays && back})");

        // fighting: never hidden; the overlays over most of the HUD, or a black picture: no judgement
        for (int i = 0; i < 3; i++)
        {
            hud.Judge(Frame(false, true), none, false, t = t.AddSeconds(0.3));
        }

        bool fight = !hud.Judge(Frame(false, true), none, true, t = t.AddSeconds(0.3));
        var mostly = new bool[n];
        for (int i = 0; i < 990; i++)
        {
            mostly[i] = true;
        }

        bool underOverlays = true;
        for (int i = 0; i < 5; i++)
        {
            underOverlays &= !hud.Judge(Frame(false, true), mostly, false, t = t.AddSeconds(0.3));
        }

        bool black = true;
        for (int i = 0; i < 5; i++)
        {
            black &= !hud.Judge(null, none, false, t = t.AddSeconds(0.3));
        }

        Console.WriteLine($"  -> never while fighting ({fight}); not with the HUD under the meter's own overlays ({underOverlays}); not on a black picture ({black})");

        // a world that changes edge where the HUD is not: nothing learnt from a screen without a steady HUD
        var noHud = new HudWatcher();
        for (int i = 0; i < 60; i++)
        {
            var e = new float[n];
            for (int k = 0; k < n; k++)
            {
                e[k] = (float)rnd.NextDouble() * 120;
            }

            noHud.Judge(e, none, true, t = t.AddSeconds(1));
        }

        bool noneLearnt = !noHud.Ready;
        Console.WriteLine($"  -> a screen without a steady HUD teaches nothing ({noneLearnt})");
        return notYet && learnt && shown && pruned && early && hidden && stays && back && fight && underOverlays && black && noneLearnt;
    }
}
