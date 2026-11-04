using System.IO;
using AionDPS.History;
using AionDPS.Ui;
using Microsoft.Data.Sqlite;

namespace AionDPS.Combat;

/// <summary>Settings that survive a crash (atomic save, a copy of the last start), "reset to defaults" with its undo, and the history remembering which boss fights went online.</summary>
public static class SelfCheckSettings
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Settings backup, reset with undo, uploaded fights:");
        string dir = Path.Combine(Path.GetTempPath(), "aiondps-settings-selftest-" + Guid.NewGuid().ToString("N"));
        string? before = Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR");
        Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", dir);
        bool ok = true;
        try
        {
            var settings = new MeterSettings { HotkeyHideUi = "Ctrl+Alt+Q", AutoUploadBoss = true, Language = "de", SetupCompleted = true };
            settings.Save();
            bool noTemp = !File.Exists(Path.Combine(dir, "meter-settings.json.tmp"));
            MeterSettings.BackupOnStart();

            // a power cut while it was written: half a file
            File.WriteAllText(Path.Combine(dir, "meter-settings.json"), "{ \"HotkeyHideUi\": \"Ctrl+Al");
            MeterSettings back = MeterSettings.Load();
            bool restored = back.HotkeyHideUi == "Ctrl+Alt+Q" && back.AutoUploadBoss && MeterSettings.RecoveredFromBackup;
            bool kept = Directory.GetFiles(dir, "meter-settings.damaged-*.json").Length == 1;
            Console.WriteLine($"  -> saving leaves no half file ({noTemp}); a damaged file comes back from the last start's copy ({restored}) and is kept ({kept})");
            ok &= noTemp && restored && kept;

            // reset keeps what must never change by itself, and can be taken back
            back.Save();
            MeterSettings.ResetToDefaults();
            MeterSettings reset = MeterSettings.Load();
            bool defaults = reset.HotkeyHideUi == new MeterSettings().HotkeyHideUi && reset.Language == "";
            bool kept2 = reset.AutoUploadBoss && reset.SetupCompleted;
            bool undone = MeterSettings.UndoReset() && MeterSettings.Load().HotkeyHideUi == "Ctrl+Alt+Q" && !MeterSettings.UndoReset();
            Console.WriteLine($"  -> reset gives the defaults ({defaults}), keeps the upload choice and the wizard ({kept2}), and undo brings it all back once ({undone})");
            ok &= defaults && kept2 && undone;
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIONDPS_DATA_DIR", before);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // every default hotkey is a different combination (the new "move the overlays" one too), each one a valid binding
        var fresh = new MeterSettings();
        var keys = typeof(MeterSettings).GetProperties().Where(p => p.Name.StartsWith("Hotkey", StringComparison.Ordinal) && p.PropertyType == typeof(string))
            .Select(p => HotkeyBinding.Parse((string?)p.GetValue(fresh))).Where(b => !b.IsEmpty).Select(b => b.ToString()).ToList();
        bool distinct = keys.Count >= 20 && keys.Distinct().Count() == keys.Count && keys.Contains("Ctrl+Alt+V");
        Console.WriteLine($"  -> the {keys.Count} default hotkeys are all different, the overlays' move key among them: {distinct}");
        // a boss fight brings the meter up by itself unless switched off (4 Nov 2026, the owner: no click for every boss)
        bool popUp = fresh.MeterPopUpForBoss
            && System.Text.Json.JsonSerializer.Deserialize<MeterSettings>(System.Text.Json.JsonSerializer.Serialize(new MeterSettings { MeterPopUpForBoss = false }))?.MeterPopUpForBoss == false;
        Console.WriteLine($"  -> the meter pops up for a boss fight by default, and the choice is kept: {popUp}");
        return ok && distinct && popUp && OverlayRule();
    }

    /// <summary>When the meter is on screen by itself (MeterOverlayRule): only for the own boss fights, never for a dungeon or a dummy alone.</summary>
    private static bool OverlayRule()
    {
        TimeSpan hold = TimeSpan.FromSeconds(30), never = TimeSpan.MaxValue;
        static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);
        bool Shown(bool onlyBoss = true, bool comeUp = true, TimeSpan? ownBoss = null, TimeSpan? bossNear = null, TimeSpan? ownCombat = null, bool up = false, bool kept = false) =>
            MeterOverlayRule.Shown(onlyBoss, comeUp, hold, ownBoss ?? never, bossNear ?? never, ownCombat ?? never, up, kept);
        bool trash = !Shown(ownCombat: S(1));                                        // fighting trash in a dungeon, no boss yet: hidden
        bool boss = Shown(ownBoss: S(1), ownCombat: S(1));                          // the own character hits a boss: up
        bool healer = Shown(bossNear: S(2), ownCombat: S(3));                         // a boss fought right there while the own character fights its adds: up
        bool othersOnly = !Shown(bossNear: S(2), ownCombat: S(40));                   // another group's boss nearby, the own character idle: hidden
        bool stays = Shown(ownBoss: S(50), ownCombat: S(5), up: true);                // up, and the player goes on fighting after the boss: stays
        bool leaves = !Shown(ownBoss: S(50), ownCombat: S(35), up: true);             // half a minute after the last fight: steps aside
        bool notByItself = !Shown(comeUp: false, ownBoss: S(1), ownCombat: S(1));     // "come up by itself" off: the boss alone does not bring it
        bool always = Shown(onlyBoss: false);                                         // "only during boss fights" off: on screen the whole time
        bool peek = Shown(kept: true);                                                // the eye button, the mouse, a menu or a dummy run: up
        bool all = trash && boss && healer && othersOnly && stays && leaves && notByItself && always && peek;
        Console.WriteLine($"  -> the meter comes up only for the own boss fights (trash {trash}, boss {boss}, adds next to a boss {healer}, someone else's boss {othersOnly}), " +
            $"stays while fighting on ({stays}) and leaves after ({leaves}); off: no boss brings it ({notByItself}); always-on ({always}), eye button ({peek})");
        return all;
    }
}
