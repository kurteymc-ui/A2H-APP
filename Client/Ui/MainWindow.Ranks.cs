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
    /// Your rank on the bosses just uploaded (6 November 2026, "fight percentiles"): asked of the website and kept for History, added to the upload line
    /// ("Boss fight uploaded. 78th percentile of Templars."), and shown over the game with <see cref="MeterSettings.RankPopup"/>. <paramref name="said"/> is
    /// what the upload line said: a newer line is left alone.
    /// </summary>
    private async Task ShowRanksAsync(IReadOnlyList<JustUploaded> fights, string said, string? url)
    {
        if (fights.Count == 0 || Headless)
        {
            return;
        }

        var lines = new List<RankToastLine>();
        foreach (JustUploaded f in fights.TakeLast(4))
        {
            if (await FightRanks.FetchAsync(f.Url, f.You) is { } rank)
            {
                lines.Add(new RankToastLine(f.Boss, f.ClassName, rank));
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        if (UploadStatusText.Text.StartsWith(said, StringComparison.Ordinal))
        {
            string add;
            if (lines.Count == 1)
            {
                RankToastLine one = lines[0];
                add = one.Rank.Ranked
                    ? $" {RankTier.Percentile(one.Rank.Percent)} of {RankTier.Many(one.ClassName)} on {one.Boss}."
                    : one.Rank.Of > 0 ? $" Not ranked yet: {one.Rank.Of} of {one.Rank.Min} {RankTier.Many(one.ClassName)} logged on {one.Boss} so far." : "";
            }
            else
            {
                var ranked = lines.Where(l => l.Rank.Ranked).ToList();
                add = ranked.Count == 0 ? "" : " Your ranks: " + string.Join(" · ", ranked.Select(l => $"{l.Boss} {RankTier.Number(l.Rank.Percent)}")) + ".";
            }

            if (add.Length > 0)
            {
                ShowUploadStatus(said + add, url);
            }
        }

        if (MeterSettings.Load().RankPopup)
        {
            RankToast.Show(lines);
        }
    }
}
