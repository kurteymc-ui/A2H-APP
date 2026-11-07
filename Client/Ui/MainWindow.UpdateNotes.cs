using System.Windows;
using AionDPS.Update;
using VelopackUpdateInfo = Velopack.UpdateInfo;

namespace AionDPS.Ui;

/// <summary>
/// The update notes in the update card (7 November 2026, the owner: "instead of just showing update now, a drop down of all the changes, very nicely
/// written with examples, so they know what the new updates are"). The card that offers an update (OfferInstall, OfferRestart) gets a "What's new"
/// bar with every change between the installed version and the offered one (<see cref="UpdateNotes"/>, <see cref="UpdateNotesView"/>); the card
/// grows to fit it. On the first start of a new version the same card shows that version's notes once, open, with a single "Got it".
/// </summary>
public partial class MainWindow
{
    private int _notesRequest;

    /// <summary>The notes of the versions after this one up to <paramref name="update"/>'s, under the card's text (fetched, so the card shows first).</summary>
    private async void ShowUpdateNotes(VelopackUpdateInfo update)
    {
        int request = ++_notesRequest;
        UpdateNotesHost.Visibility = Visibility.Collapsed;
        UpdateRestartCard.MaxWidth = 380;
        Version to = AppVersion.Parse(update.TargetFullRelease.Version.ToString());
        IReadOnlyList<UpdateRelease> all = await UpdateNotes.LatestAsync();
        if (request != _notesRequest || UpdateRestartOverlay.Visibility != Visibility.Visible)
        {
            return; // another card, or the card was closed meanwhile
        }

        FillNotes(UpdateNotes.Between(all, AppVersion.Current, to), open: false);
    }

    private void FillNotes(IReadOnlyList<UpdateRelease> releases, bool open)
    {
        if (releases.Count == 0)
        {
            UpdateNotesHost.Visibility = Visibility.Collapsed;
            UpdateRestartCard.MaxWidth = 380;
            return;
        }

        UpdateRestartCard.MaxWidth = 600;
        UpdateNotesHost.Content = UpdateNotesView.Build(releases, open, ActualHeight > 0 ? Math.Max(180, ActualHeight - 330) : 520);
        UpdateNotesHost.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Once after an update: what this version brought, from the notes shipped with it (all versions since the one that ran last). A first install
    /// shows nothing (there is nothing "new" to someone who just installed it); it only remembers the version.
    /// </summary>
    private void ShowNotesAfterUpdate()
    {
        if (Headless || AppVersion.Current == new Version(0, 0, 0))
        {
            return;
        }

        var settings = MeterSettings.Load();
        string? seen = settings.NotesSeenVersion;
        if (seen != AppVersion.Text)
        {
            settings.NotesSeenVersion = AppVersion.Text;
            settings.Save();
        }

        // no version remembered: a first install (nothing to show), or an update from a version from before the notes - then this version's own notes
        bool earlier = seen is null && System.IO.Directory.Exists(History.FightLog.Folder);
        if ((seen is null && !earlier) || (seen is not null && AppVersion.Parse(seen) >= AppVersion.Current) || UpdateRestartOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        IReadOnlyList<UpdateRelease> bundled = UpdateNotes.Bundled();
        IReadOnlyList<UpdateRelease> releases = seen is not null
            ? UpdateNotes.Between(bundled, AppVersion.Parse(seen), AppVersion.Current)
            : bundled.Where(r => AppVersion.Parse(r.Version) == AppVersion.Current).ToList();
        if (releases.Count == 0)
        {
            return;
        }

        OpenNotesCard(releases, $"Updated to {AppVersion.Text}", seen is not null ? $"Here's what changed since {seen}." : "Here's what this version brings.");
    }

    /// <summary>The card with notes only (after an update, or the pictures): a title, a line, the notes open, one "Got it".</summary>
    internal void OpenNotesCard(IReadOnlyList<UpdateRelease> releases, string title, string line)
    {
        _pendingInstallUpdate = null;
        _pendingRestartUpdate = null;
        _notesRequest++;
        UpdateRestartTitle.Text = title;
        UpdateRestartText.Text = line;
        UpdateRestartLaterButton.Visibility = Visibility.Collapsed;
        UpdateRestartNowButton.Content = "Got it";
        UpdateRestartOverlay.Visibility = Visibility.Visible;
        FillNotes(releases, open: true);
    }

    /// <summary>For the pictures: the start-up question for a made-up version with these notes under it, their bar shut or open.</summary>
    internal void ShowUpdatePromptSample(IReadOnlyList<UpdateRelease> releases, string version, string current, bool open)
    {
        var loc = LocalizationManager.Instance;
        _notesRequest++;
        UpdateRestartTitle.Text = loc["Main.UpdateAvailable.Title"];
        UpdateRestartLaterButton.Content = loc["Main.UpdateAvailable.No"];
        UpdateRestartNowButton.Content = loc["Main.UpdateAvailable.Yes"];
        UpdateRestartLaterButton.Visibility = Visibility.Visible;
        UpdateRestartText.Text = string.Format(loc["Main.UpdateAvailable.Message"], version, current);
        UpdateRestartOverlay.Visibility = Visibility.Visible;
        FillNotes(releases, open);
    }
}
