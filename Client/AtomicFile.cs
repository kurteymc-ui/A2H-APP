using System.IO;

namespace AionDPS;

/// <summary>
/// Writing a store's file so that a crash, a kill or a power cut mid-write never leaves half a file (3 November 2026: ten stores wrote in place; a truncated
/// file loaded as empty and the next save overwrote every character's data). The text goes to a file of its own beside it and is then put in place at once;
/// a file another program holds for a moment (virus scanner, backup, cloud sync) is tried again a few times. Reading: a file that cannot be parsed is set
/// aside as "&lt;name&gt;.damaged-&lt;time&gt;" instead of being overwritten by the next save, so nothing is lost for good.
/// </summary>
public static class AtomicFile
{
    /// <summary>Writes the text in place at once; throws IOException/UnauthorizedAccessException as File.WriteAllText does when every try failed.</summary>
    public static void WriteAllText(string path, string text)
    {
        string temp = path + ".tmp";
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(temp, text);
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 4)
            {
                Thread.Sleep(30 * attempt);
            }
        }
    }

    /// <summary>A store's file that could not be parsed: moved aside (the three newest kept) so the store starts empty without destroying it.</summary>
    public static void KeepDamaged(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            File.Move(path, path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: true);
            string folder = Path.GetDirectoryName(path)!;
            foreach (string old in Directory.GetFiles(folder, Path.GetFileName(path) + ".damaged-*").OrderByDescending(f => f).Skip(3))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // left where it is: the store starts empty this time and saves over it later
        }
    }
}
