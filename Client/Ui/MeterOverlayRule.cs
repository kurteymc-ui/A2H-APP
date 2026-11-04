namespace AionDPS.Ui;

/// <summary>
/// When the meter overlay is on screen by itself (MainWindow.UpdateMeterOverlay), apart from the game being in front. A meter switched off (the eye
/// button, the overlay's X, its hotkey, the tray or Settings) stays off: nothing here switches it on again (4 Nov 2026, players: it kept coming back).
/// <list type="bullet">
/// <item>"Only during boss fights" off: on screen the whole time.</item>
/// <item>On: it comes up by itself when the own character fights a boss (it hit the boss or was hit by it, or it fought while a boss was fought
/// right there), unless "Come up by itself for boss fights" is off; once up it stays while the player goes on fighting, and steps aside the chosen
/// time after the last fight. A dungeon or a training dummy alone no longer brings it up.</item>
/// <item>Always while the eye button just asked for it, the mouse is on it, one of its menus is open or a dummy run waits, runs or just ended.</item>
/// </list>
/// </summary>
public static class MeterOverlayRule
{
    /// <summary>How close the own fighting must be to a boss fought nearby to count as the own boss fight (a healer's hit on an add, the adds of the boss).</summary>
    public static readonly TimeSpan NearBossFight = TimeSpan.FromSeconds(10);

    public static bool Shown(bool onlyBossFights, bool comeUpForBoss, TimeSpan hold, TimeSpan sinceOwnBossFight, TimeSpan sinceBossNearby,
        TimeSpan sinceOwnCombat, bool upNow, bool keptAnyway)
    {
        if (!onlyBossFights || keptAnyway)
        {
            return true;
        }

        bool ownBossFight = sinceOwnBossFight < hold || (sinceBossNearby < hold && sinceOwnCombat < NearBossFight);
        return (comeUpForBoss && ownBossFight) || (upNow && sinceOwnCombat < hold);
    }
}
