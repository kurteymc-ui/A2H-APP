namespace AionDPS.Aion2;

/// <summary>
/// The character played in the game's own lists. The game's records of a character - the login's character list, the legion list, the owner of a bound
/// item - keep the server it was made on, while the session names the server it plays on now (NCSOFT's character pages too): two characters made on Nemon
/// (2309) and played on Zikel (2302) since kept 2309 in all three (seen 2026-11-02 and 3), so they never matched the character played. An entry is the
/// character played by its name and server, else by its name alone when no other entry has that name; and the list's other entries on that old server are
/// on the new one too (they moved with it).
/// </summary>
public static class Aion2Moved
{
    /// <summary>The entry of the character played: the one with its name and server (or no server read), else the only one with its name; null when there is
    /// none, or more than one with its name on other servers.</summary>
    public static T? Find<T>(IEnumerable<T> list, Func<T, string> name, Func<T, int> server, string played, int playedServer) where T : class
    {
        T? exact = null, only = null;
        int named = 0;
        foreach (T entry in list)
        {
            if (name(entry) != played)
            {
                continue;
            }

            named++;
            only = entry;
            if (exact is null && (server(entry) == playedServer || server(entry) == 0))
            {
                exact = entry;
            }
        }

        return exact ?? (named == 1 ? only : null);
    }

    /// <summary>(old, new) when the list's entry of the character played names another server than the session; null when it names the same (or none).</summary>
    public static (int From, int To)? Of(int listed, int playedServer) => listed > 0 && playedServer > 0 && listed != playedServer ? (listed, playedServer) : null;

    /// <summary>The server an entry of the list is on now: the new one when it names the old server of the character played, else its own.</summary>
    public static int Now(int server, (int From, int To)? moved) => moved is { } m && server == m.From ? m.To : server;
}
