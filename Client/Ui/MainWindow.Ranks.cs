using AionDPS.History;
using AionDPS.Upload;

namespace AionDPS.Ui;

public partial class MainWindow
{
    /// <summary>A boss fight just uploaded, for your rank on it a moment later: its page, your character and class, the boss.</summary>
    private readonly record struct JustUploaded(string Url, string You, string? ClassName, string Boss);

    private static JustUploaded? UploadedFight(EncounterUploadRequest payload, UploadResult result) =>
        result.Success && result.Url is { Length: > 0 } url && payload.Participants.FirstOrDefault(p => p.IsSelf) is { } me
            ? new JustUploaded(url, me.Name, me.ClassName, payload.BossNpcName)
            : null;

    /// <summary>
    /// Your rank on the bosses just uploaded ("fight percentiles"), asked of the website and kept for History, so it is there when History opens. Shown
    /// only in History and on the fight's page: no note over the game and nothing added to the upload line (6 November 2026, the owner: "remove that weird
    /// percentile pop up after a raid. Just leave it in the history page and the webpage").
    /// </summary>
    private static async Task FetchRanksAsync(IReadOnlyList<JustUploaded> fights)
    {
        if (Headless)
        {
            return;
        }

        foreach (JustUploaded f in fights.TakeLast(4))
        {
            await FightRanks.FetchAsync(f.Url, f.You);
        }
    }
}
