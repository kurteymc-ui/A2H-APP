using System.IO;
using System.Text.Json;

namespace AionDPS.Ui;

/// <summary>
/// Settings for the meter UI. Scoped deliberately to what the tool actually has a data source for.
/// </summary>
public sealed class MeterSettings
{

    // User interface
    public string Theme { get; set; } = "Dark";
    public string FontSize { get; set; } = "Medium";

    // "Dark", or "File:name" for a theme saved from the theme editor (see ThemeFile).

    /// <summary>Share-of-group bar under each row's Damage/DPS line (see PlayerRow.SharePercent).</summary>
    public bool ShowShareBars { get; set; } = true;

    /// <summary>Under each player's name in the meter: the party buffs on them right now (Mantra, Gale, Protective Light ...).</summary>
    public bool ShowPartyBuffs { get; set; } = true;

    /// <summary>"↓ N" damage-received figure on each row's second line (see PlayerRow.DamageTaken).
    /// Off by default, per the user: most players never want this second line at all, so the row
    /// stays at its narrower single-line height until someone opts in.</summary>
    public bool ShowDamageTaken { get; set; }



    /// <summary>GUI display language, an ISO 639-1 code from LocalizationManager.SupportedLanguages
    /// (e.g. "de"), or "" on a fresh install to mean "use whatever LocalizationManager already
    /// auto-detected from the OS at startup, and don't overwrite it here." Independent of the game client's
    /// language (see Localization.cs) -- this is purely which language the meter's OWN menus/buttons/labels render in.</summary>
    public string Language { get; set; } = "";

    /// <summary>Posting time (epoch seconds) of the newest AION 2 announcement seen when the announcements icon was last clicked;
    /// the "New" badge shows while the server knows a newer one. 0 = never, which the first check fills in silently.</summary>
    public long AnnouncementsSeen { get; set; }

    /// <summary>Start time (epoch seconds) of the newest Twitch Drops campaign seen when the Twitch icon was last clicked;
    /// the "New" badge shows while a newer campaign is running. 0 = never, which counts as new (campaigns are time limited).</summary>
    public long TwitchDropsSeen { get; set; }

    /// <summary>The meter mode last used (Damage, Heal, Taken, All, AllCompact), restored on the next
    /// launch. Saved on every change, like the always-on-top toggle.</summary>
    public string LastMode { get; set; } = "Damage";

    /// <summary>Whether the meter window starts pinned above every other window, including the game
    /// itself. Defaults to off -- per the user, existing behaviour (an ordinary window, until turned
    /// on from the View menu's "Always on top" toggle) should not change for anyone who never asked
    /// for this. That toggle and this setting are the same value now, not two independent switches:
    /// checking one updates the other and saves immediately, the same save-on-change treatment
    /// CheckForUpdates already gets, so "how I last left it" is what a fresh launch restores.</summary>
    public bool AlwaysOnTopOnStartup { get; set; }

    /// <summary>MainWindow's size/position, saved on close and restored on next launch -- found
    /// necessary by the user, who resized the window and had it reset every restart. All four
    /// null (fresh install / older settings file) means "use the XAML default", not "0x0 at the
    /// origin".</summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    /// <summary>Settings window's own size, saved on close - per the user, who resized it (the
    /// Characters tab needs real room) and had it reset every time. Position isn't remembered on
    /// purpose: it always opens CenterOwner'd on MainWindow instead, which stays correct
    /// regardless of where MainWindow itself currently is; only WindowWidth/Height above (the
    /// MAIN window) also remember position, since that one has nothing to center against.</summary>
    public double? SettingsWindowWidth { get; set; }
    public double? SettingsWindowHeight { get; set; }


    /// <summary>Whether the meter asks GitHub for a newer release -- at startup and every five
    /// minutes while it runs (see MainWindow's update timer). Default on, but a real switch and
    /// not a decorative one: this is the program's only outbound network call, and the README
    /// promises that nothing leaves the machine, so anyone who wants that promise kept literally
    /// can turn it off. The "Check for updates" menu item still works when it is off -- that one
    /// is the user asking, not the program deciding.</summary>
    public bool CheckForUpdates { get => true; set { } } // always: updates come by themselves (the switch is gone from Settings; an old "off" no longer holds the meter back)

    /// <summary>Last name / e-mail typed into the feedback window, so a second report is not retyped.</summary>
    public string FeedbackName { get; set; } = "";

    public string FeedbackEmail { get; set; } = "";

    /// <summary>Whether the own character profile is uploaded by itself a few seconds after a login
    /// (see MainWindow.ScheduleOwnProfileUpload), so the player can be found on the website. Off by
    /// default, like the boss upload: without an opt-in no game data leaves the PC. The manual upload
    /// button works either way.</summary>
    public bool AutoUploadProfile { get; set; }

    /// <summary>System-wide hotkeys (see GlobalHotkeys), written "Ctrl+Alt+H"; empty = off. They stand
    /// in for the chat commands, which cannot work because Aion 2's chat is not readable.</summary>
    public string HotkeyHideUi { get; set; } = "Ctrl+Alt+H";

    public string HotkeyPause { get; set; } = "Ctrl+Alt+P";

    public string HotkeyCopyDamage { get; set; } = "Ctrl+Alt+D";

    public string HotkeyClear { get; set; } = "Ctrl+Alt+X";

    /// <summary>Uploads the current boss, like the Upload button (see MainWindow.OnUploadCurrentBossClicked).</summary>
    public string HotkeyUploadBoss { get; set; } = "Ctrl+Alt+U";

    /// <summary>Whether a boss fight is uploaded by itself a few seconds after the boss died (see
    /// MainWindow.ScheduleBossUpload). Off by default: unlike the own profile, it sends fight data
    /// without a click, so the player opts in.</summary>
    public bool AutoUploadBoss { get; set; }

    /// <summary>Whether the game-server traffic is also written to a file on this PC (see DebugRecording), so it can be sent
    /// to the developer by hand. Off by default; nothing is sent by the program.</summary>
    public bool DebugRecording { get; set; }

    /// <summary>Whether the profiles of OTHER players the meter has seen (name, class, level, gear, legion)
    /// go online with the own profile upload, the Upload button and a boss upload. Off by default: it is
    /// data about people who did not agree to anything. With it off only the own character is sent.</summary>
    public bool UploadOtherPlayersProfiles { get; set; }

    /// <summary>Only for members of the legion Akatsuki on Kaisinel (the switch is hidden for everybody else): the complete login traffic of the character is
    /// sent once per login to the community server, which keeps it for a short time to work out what ties the characters of one player together. Off by default.</summary>
    public bool ShareLoginWithLegion { get; set; }

    /// <summary>Whether the Combat Power changes the CP calculator measures at the logins go to the website, without a name (Upload/CpShare.cs), for its weekly
    /// refit of the Combat Power model. Off until the player switches it on (Settings, Upload, or the CP calculator); needs the privacy terms accepted.</summary>
    public bool ShareCpMeasurements { get; set; }

    /// <summary>Whether the minimap reader's checks against the game's own reports go to the website without a name (Upload/MapShare.cs), to refit where
    /// each world map's picture lies: on by default while the app is in beta (the owner, 5 November 2026; the privacy terms name it and nothing goes before
    /// they are accepted), switched off in Settings, Upload.</summary>
    public bool ShareMapData { get; set; } = true;

    /// <summary>The last time the character was seen: in that legion on that server. Decides whether the settings show the switch above; written by the main window, never by the user.</summary>
    public bool LoginShareEligible { get; set; }

    /// <summary>False until the first-start wizard (WizardWindow) has been through: a fresh install, or a
    /// settings file that was lost or broken. A settings file from before the wizard existed does not have
    /// the field either, so every install sees the wizard once after updating to the version that brought it
    /// (0.11.0) - on purpose, so everybody makes the privacy choices (all off by default) once.</summary>
    public bool SetupCompleted { get; set; }

    /// <summary>The answer to the privacy policy: 0 not asked yet, 1 accepted, 2 declined (see <see cref="PrivacyConsent"/>). Nothing is uploaded unless it is 1 and <see cref="PrivacyVersion"/> is current.</summary>
    public int PrivacyDecision { get; set; }

    /// <summary>The version of the policy that was accepted (0 = none).</summary>
    public int PrivacyVersion { get; set; }

    /// <summary>The look of Hide UI: "Chips" (a click-through chip per player) or "Compact" (one
    /// transparent panel with a header, the fought boss and a dense line per player; it takes
    /// clicks, so it can be dragged, scaled and a player opened).</summary>
    public string OverlayStyle { get; set; } = "Compact";

    /// <summary>Compact look only: the panel lets every click through to the game, like the chips do.
    /// Off by default - then it takes clicks (drag, scale, open a player).</summary>
    public bool CompactClickThrough { get; set; }

    /// <summary>How opaque the overlay's dark backgrounds are, 0.2 to 1 (both looks).</summary>
    public double OverlayOpacity { get; set; } = 0.92;

    /// <summary>How opaque the timetable overlay's background is, 0.2 to 1. Null (a settings file from before
    /// this existed) follows <see cref="OverlayOpacity"/>, which is what the timetable used until then.</summary>
    public double? TimetableOpacity { get; set; }

    /// <summary>The own opacity (0 to 1) of overlays that have a setting for it, by the overlay's window title; none = the overlay's look as it was.</summary>
    public Dictionary<string, double> OverlayOpacities { get; set; } = new();

    /// <summary>How far the content of an overlay that is cut off from its sides (the map) is moved inside its frame, by the overlay's window title (x, y).</summary>
    public Dictionary<string, double[]> OverlayShifts { get; set; } = new();

    /// <summary>Overlays snap to a grid while they are dragged.</summary>
    public bool OverlayGridSnap { get; set; }

    /// <summary>The width of one grid cell in screen units (the grid is placed from the screen's top left corner).</summary>
    public int OverlayGridSize { get; set; } = 20;

    /// <summary>Overlays dock to the edges of other overlays and follow them when those are moved or change their size.</summary>
    public bool OverlayDocking { get; set; }

    /// <summary>Which overlay hangs on which, by the docked overlay's window title.</summary>
    public Dictionary<string, OverlayDock> OverlayDocks { get; set; } = new();

    /// <summary>The width the tools window has for each tool (by the tool's id): every tool keeps its own, so the checklist can be narrow and the design editor wide.</summary>
    public Dictionary<string, double> ToolWidths { get; set; } = new();

    /// <summary>The height the tools window has for each tool, like <see cref="ToolWidths"/>; a tool with tabs keeps one per tab (key "tool/tab").</summary>
    public Dictionary<string, double> ToolHeights { get; set; } = new();

    /// <summary>The list of tools on the left of the tools window is folded to a thin strip.</summary>
    public bool ToolsNavCollapsed { get; set; }

    /// <summary>Where the tools window was left and how high it is (the width is kept per tool, see <see cref="ToolWidths"/>).</summary>
    public double? ToolsWindowLeft { get; set; }

    public double? ToolsWindowTop { get; set; }

    public double? ToolsWindowHeight { get; set; }

    /// <summary>The tools shown in a window of their own ("pop out"), by the tool's id: where each was left and whether it was open when the meter closed (a pop-out
    /// closed with its X is not open any more; its place is kept for the next time).</summary>
    public Dictionary<string, PopOutPlace> PopOuts { get; set; } = new();

    /// <summary>How many minutes ahead the timetable overlay lists events that are about to start, 0 to 60 (0 = none).</summary>
    public int TimetableLookaheadMinutes { get; set; } = 60;

    /// <summary>The world boss overlay (see WorldBossOverlayWindow): the bosses switched on in the World boss settings with the time until they are there.
    /// Off until switched on.</summary>
    public bool ShowBossOverlay { get; set; }

    /// <summary>The network overlay (see NetworkOverlayWindow): ping, traffic and connection quality of the game connection. Off until switched on;
    /// the ids of the lines switched off in the Network analysis settings are kept in <see cref="NetworkOverlayHidden"/> (everything shows by default).</summary>
    public bool ShowNetworkOverlay { get; set; }

    /// <summary>The assist overlay (see AssistOverlayWindow): own cooldowns and effects, switched in the Tools > Assist window. Locked (default): click-through; unlocked: it can be moved and scaled.</summary>
    public bool ShowAssistOverlay { get; set; }

    public bool AssistOverlayLocked { get; set; } = true;
    public double? AssistOverlayLeft { get; set; }
    public double? AssistOverlayTop { get; set; }
    public double AssistOverlayScale { get; set; } = 1.0;
    public double? AssistOverlayWidth { get; set; }
    public double? AssistOverlayHeight { get; set; }

    /// <summary>The skills and effects (skill ids) the player unticked in the assist window: the overlay does not show them (everything shows by default).</summary>
    public List<int> AssistOverlayHidden { get; set; } = new();

    /// <summary>Skills the meter learned to have no cooldown (a filler such as a Templar's Judgment from level 16: six uses at least, most of them less than
    /// 2.5 s apart, and never a cooldown from the game; see Aion2Cooldowns). The assist never lists them, nor the basic attacks of the class lists. (The list
    /// kept before 4 November 2026, AssistNoCooldown, had every skill the game never sends a cooldown for, Taunt and Shield Rush among them: no longer read.)</summary>
    public List<int> AssistNoCooldownSkills { get; set; } = new();

    /// <summary>The cooldowns show the ready skills too (a bar with "Ready", a bright picture); off: only the ones cooling down.</summary>
    public bool AssistShowReady { get; set; } = true;

    /// <summary>The skills known to have a cooldown (every character; the assist shows those of the one in the game; see Aion2Cooldowns.KnownSkills): listed
    /// before they are used when the skill bar is not known.</summary>
    public List<int> AssistKnownSkills { get; set; } = new();

    /// <summary>Per skill variant (its specializations: the cast id with its last digit cleared) the times between its last uses with no cooldown number of the
    /// game in between (seconds, the last 16; see Aion2Cooldowns): what the length of its cooldown on the character is learned from.</summary>
    public Dictionary<int, List<double>> AssistCooldownGaps { get; set; } = new();

    /// <summary>How the cooldowns look: "Bars" (a row per skill: picture, name, time left or "Ready") or "Tiles" (the skill's picture with the time on it).</summary>
    public string AssistCooldownStyle { get; set; } = "Bars";

    /// <summary>Where each skill's cooldown bar is on the screen (left, top), by skill id: every skill is placed on its own, and snaps to the others.</summary>
    public Dictionary<int, double[]> AssistCooldownPlaces { get; set; } = new();

    /// <summary>The same for the squares: each look keeps its own arrangement (a column of bars, a row of squares).</summary>
    public Dictionary<int, double[]> AssistCooldownTilePlaces { get; set; } = new();

    /// <summary>Placing the cooldowns: on, one dragged moves all of them together (the arrangement kept); off, each moves on its own.</summary>
    public bool AssistCooldownsTogether { get; set; }

    /// <summary>The size of the cooldowns (1 = a bar 22 high, a tile 40), the same for every skill.</summary>
    public double AssistCooldownScale { get; set; } = 1.0;

    /// <summary>The width of a cooldown bar (at size 1).</summary>
    public double AssistBarWidth { get; set; } = 170;

    /// <summary>How see-through the cooldowns on the screen are (0.1 to 1, 1 = solid), every skill alike.</summary>
    public double AssistCooldownOpacity { get; set; } = 1.0;

    /// <summary>How visible a skill stays while it is ready (0.1 to 1, times <see cref="AssistCooldownOpacity"/>): lower, the ones cooling down stand out.</summary>
    public double AssistReadyOpacity { get; set; } = 1.0;

    /// <summary>How much of the bars' dark background shows (0 to 1): the pictures and the times stay sharp.</summary>
    public double AssistBackOpacity { get; set; } = 1.0;

    /// <summary>The skills whose return is said out loud (the voice of the voice settings): "Taunt ready".</summary>
    public List<int> AssistVoiceSkills { get; set; } = new();

    /// <summary>The volume of the cooldowns' voice, 0 to 100.</summary>
    public int AssistVoiceVolume { get; set; } = 80;

    /// <summary>Each character's skill bar as the game last sent it (base skill ids in slot order), by "name@server" and by "name@server#preset": the
    /// Skill Assist shows the right skills from the start of a session, before the game sends the bar again (5 November 2026: started after the login,
    /// it showed every Templar skill any character had used, Doom Shield among them, instead of the bar's Shield of Protection).</summary>
    public Dictionary<string, List<int>> AssistBars { get; set; } = new();

    /// <summary>Skills of the class not on the skill bar that the player ticked to show on the screen too (Skill Assist, "Other skills").</summary>
    public List<int> AssistExtraSkills { get; set; } = new();

    /// <summary>The cooldowns' three saved layouts (Skill Assist, Presets), by slot 1 to 3: how they sit on the screen, to switch between.</summary>
    public Dictionary<int, CooldownPreset> AssistPresets { get; set; } = new();

    /// <summary>The preset last saved or loaded (0: none), marked in the Skill Assist window.</summary>
    public int AssistPresetActive { get; set; }

    /// <summary>The effects on the player as an overlay of their own (the assist overlay shows the cooldowns). Null (never switched): as the assist overlay.
    /// It shares the assist overlay's lock and the list of what is hidden.</summary>
    public bool? ShowEffectsOverlay { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool EffectsOverlayOn => ShowEffectsOverlay ?? ShowAssistOverlay;

    public double? EffectsOverlayLeft { get; set; }
    public double? EffectsOverlayTop { get; set; }
    public double EffectsOverlayScale { get; set; } = 1.0;
    public double? EffectsOverlayWidth { get; set; }
    public double? EffectsOverlayHeight { get; set; }

    /// <summary>Assist window and overlay: list the effects with the least time left first (renewed auras, which never run out, come last). Off: in the order they first appeared.</summary>
    public bool AssistSortByTime { get; set; }

    /// <summary>The Skill Assist tab chosen last: "cooldowns" or "buffs" (<see cref="AssistWindow.CooldownsTab"/>, <see cref="AssistWindow.BuffsTab"/>).</summary>
    public string AssistTab { get; set; } = "cooldowns";

    public bool NetworkOverlayLocked { get; set; } = true;

    public List<string> NetworkOverlayHidden { get; set; } = new();

    /// <summary>The loot overlay (see LootOverlayWindow): what the player picked up in the running session, kept in the Loot tool. Off until switched on. Locked (default): click-through; unlocked: it can be moved and scaled.</summary>
    public bool ShowLootOverlay { get; set; }

    /// <summary>The gathering overlay (see GatherOverlayWindow): what was gathered in this run of the meter, by resource and grade. Off until switched on.
    /// Locked (default): click-through.</summary>
    public bool ShowGatherOverlay { get; set; }

    public bool GatherOverlayLocked { get; set; } = true;
    public double? GatherOverlayLeft { get; set; }
    public double? GatherOverlayTop { get; set; }
    public double GatherOverlayScale { get; set; } = 1.0;
    public double? GatherOverlayWidth { get; set; }
    public double? GatherOverlayHeight { get; set; }

    /// <summary>The season overlay (see SeasonOverlayWindow): the season missions picked with ★ on the checklist (none picked: the unfinished weekly ones) of the
    /// character logged in, live. Off until switched on.</summary>
    public bool ShowSeasonOverlay { get; set; }

    public bool SeasonOverlayLocked { get; set; } = true;
    public double? SeasonOverlayLeft { get; set; }
    public double? SeasonOverlayTop { get; set; }
    public double SeasonOverlayScale { get; set; } = 1.0;
    public double? SeasonOverlayWidth { get; set; }
    public double? SeasonOverlayHeight { get; set; }

    /// <summary>The season missions the overlay shows, by counter id, in the order picked (both factions' ids may be in it); null: none picked.</summary>
    public List<int>? SeasonOverlayMissions { get; set; }

    public bool LootOverlayLocked { get; set; } = true;
    public double? LootOverlayLeft { get; set; }
    public double? LootOverlayTop { get; set; }
    public double LootOverlayScale { get; set; } = 1.0;
    public double? LootOverlayWidth { get; set; }
    public double? LootOverlayHeight { get; set; }

    /// <summary>How many lines of loot the overlay lists at most (the rest is summed up in a last line), 3 to 30.</summary>
    public int LootOverlayLines { get; set; } = 8;

    /// <summary>The overlay leaves out the items of the plainest grade (Common): the loot worth a look stands out.</summary>
    public bool LootOverlayHideCommon { get; set; }

    /// <summary>The crafting overlay (see CraftOverlayWindow): the materials the marked recipes still lack. Off until switched on. Locked (default): click-through; unlocked: it can be moved and scaled.</summary>
    public bool ShowCraftOverlay { get; set; }

    public bool CraftOverlayLocked { get; set; } = true;
    public double? CraftOverlayLeft { get; set; }
    public double? CraftOverlayTop { get; set; }
    public double CraftOverlayScale { get; set; } = 1.0;
    public double? CraftOverlayWidth { get; set; }
    public double? CraftOverlayHeight { get; set; }

    /// <summary>How many materials the crafting overlay lists at most (the rest is summed up in a last line), 3 to 30.</summary>
    public int CraftOverlayLines { get; set; } = 10;

    /// <summary>An intermediate (an ingot for a weapon) is counted as the materials it is made of. On by default.</summary>
    public bool CraftResolve { get; set; } = true;

    /// <summary>The crafting page lists every material (true) or only what is missing (false, default).</summary>
    public bool CraftShowAll { get; set; }

    /// <summary>Which stocks the crafting tool counts: the inventory, the character storage, the account (server) storage.</summary>
    public bool CraftCountInventory { get; set; } = true;
    public bool CraftCountStorage { get; set; } = true;
    public bool CraftCountAccountStorage { get; set; } = true;

    public double? NetworkOverlayLeft { get; set; }
    public double? NetworkOverlayTop { get; set; }
    public double NetworkOverlayScale { get; set; } = 1.0;
    public double? NetworkOverlayWidth { get; set; }
    public double? NetworkOverlayHeight { get; set; }

    /// <summary>The dummy test overlay (see DummyTestOverlayWindow): the countdown of a training-dummy run. Shows only while a run is waiting, running or just finished.</summary>
    public bool ShowDummyOverlay { get; set; } = true;

    public bool DummyOverlayLocked { get; set; } = true;

    public double? DummyOverlayLeft { get; set; }
    public double? DummyOverlayTop { get; set; }
    public double DummyOverlayScale { get; set; } = 1.0;
    public double? DummyOverlayWidth { get; set; }
    public double? DummyOverlayHeight { get; set; }

    /// <summary>The length of the last dummy test run, in seconds (the field in the dummy test window starts with it).</summary>
    public int DummyTestSeconds { get; set; } = 120;

    /// <summary>The player's own hit on a training dummy (Training Scarecrow, Punching Bag ..., see DummyTargets) starts a run of
    /// <see cref="DummyTestSeconds"/> by itself; it is saved with the other runs when the time is up.</summary>
    public bool DummyAutoStart { get; set; } = true;

    /// <summary>While signed in (AION 2 Helper account), the characters' Odyle Energy and daily and weekly counters go to aion2helper.com/meter/roster too, for
    /// the player's own Discord account only (to check them on the phone). On until switched off (the checklist's roster page).</summary>
    public bool SyncRoster { get; set; } = true;

    /// <summary>While signed in, the own legion's member list from the login (names, classes, levels, Item Level and Combat Power, as the game's legion
    /// window shows them) goes to aion2helper.com, so Discord servers that follow the legion have every member on their roster (Upload/LegionShare.cs).
    /// On until switched off (the account window).</summary>
    public bool ShareLegionList { get; set; } = true;

    /// <summary>The meter overlay's boss timers line: the boss's next casts as everyone's uploads learnt them, and the time to its rage where the game data
    /// knows it (Upload/BossTimers.cs). On until switched off (the overlay's menu).</summary>
    public bool OverlayBossTimers { get; set; } = true;

    /// <summary>With each boss upload, the positions for the replay on the fight's page (the own character's, the boss's and the other monsters'; the other
    /// players' only with <see cref="UploadOtherPlayersProfiles"/>). Off until the player switches it on (Settings, Upload): positions are not part of what the
    /// privacy policy (version 2) says is sent.</summary>
    public bool UploadReplay { get; set; }

    /// <summary>The app's column of sections shows only its pictures (MainWindow.Shell.cs).</summary>
    /// <summary>The tab each hub of the app's column was left on (hub id -> page id); null until one is opened.</summary>
    public Dictionary<string, string>? AppHubTabs { get; set; }

    public bool AppNavCollapsed { get; set; }

    /// <summary>The window was given the app's size once (it was the small meter before 1.18.8); the player's own size is kept after.</summary>
    public bool AppShellSized { get; set; }

    /// <summary>The combat overlay (see CombatOverlayWindow): the boss cast bar, the target of the target and the healer's target in one frame.
    /// Off until switched on; every element has its own place and size inside the frame (<see cref="CombatElements"/>, in the frame's own units).</summary>
    /// <summary>The overlays step aside while a game window is open (enchant, inventory, map, shop, Esc ...: the game shows the mouse cursor then) and come back
    /// when it closes. Only works in the control mode that hides the cursor while playing. On by default (since 1.19.4: <see cref="MenuHideDefaulted"/>).</summary>
    public bool HideDuringMenus { get; set; } = true;

    /// <summary>The switch above was turned on once for everyone (the owner: "only have the overlays on the default page"); the player can turn it off again.</summary>
    public bool MenuHideDefaulted { get; set; }

    /// <summary>The version the shortcuts' icons were last refreshed for (Windows keeps showing an old icon on a shortcut until told to look again).</summary>
    public string? IconRefreshedFor { get; set; }

    /// <summary>The table follows the boss being fought: only its damage is shown while it is attacked, for 10 s after its death, then everything again (see MainWindow.FollowBossAutomatically). The whole fight is recorded either way. Off until switched on.</summary>
    public bool AutoBossView { get; set; }

    /// <summary>Seconds the dead boss stays on screen before the table shows everything again (see <see cref="AutoBossView"/>).</summary>
    public int AutoBossSeconds { get; set; } = 10;

    /// <summary>The stream overlay for OBS and Streamlabs (see StreamOverlayServer): a web page that shows the meter live, for the same PC or (two-PC streaming) others in the own network. Off until switched on.</summary>
    public bool StreamOverlayOn { get; set; }

    public int StreamOverlayPort { get; set; } = 8920;

    /// <summary>The random key in the link; empty until the first start (then made once).</summary>
    public string StreamOverlayKey { get; set; } = "";

    /// <summary>Show player names in the stream. Off by default: everyone is shown by their class, like in the uploads.</summary>
    public bool StreamShowNames { get; set; }

    /// <summary>Saving a session to a file also writes the rotation, effect, stagger and replay data (a few times the size of the damage alone).</summary>
    public bool SessionSaveExtras { get; set; } = true;

    /// <summary>Where the session manager (with the combat analyzer) was left: place, size and the width of its left half.</summary>
    public double? SessionWindowLeft { get; set; }
    public double? SessionWindowTop { get; set; }
    public double? SessionWindowWidth { get; set; }
    public double? SessionWindowHeight { get; set; }
    public double? SessionSplitLeft { get; set; }

    /// <summary>Reachable from other PCs of the network (a streaming PC running OBS); off: this PC only. On by default, the link carries a key and only the own network is answered.</summary>
    public bool StreamOverlayNetwork { get; set; } = true;

    /// <summary>The control mode learnt so far: "Unknown", "Hidden" (the cursor is hidden while playing) or "Classic" (always shown; menus cannot be told apart).</summary>
    public string MenuControlMode { get; set; } = "Unknown";


    public bool ShowCombatOverlay { get; set; }

    public bool CombatOverlayLocked { get; set; } = true;

    /// <summary>The cast bar and the health bar are positioned each on their own; null (a settings file from before) follows <see cref="CombatOverlayLocked"/>.</summary>
    public bool? CombatCastOverlayLocked { get; set; }

    public bool? CombatHpOverlayLocked { get; set; }

    /// <summary>How opaque the target of the target is, 0 to 1.</summary>
    public double CombatTargetOpacity { get; set; } = 0.6;

    public double? CombatOverlayLeft { get; set; }
    public double? CombatOverlayTop { get; set; }
    public double CombatOverlayScale { get; set; } = 1.0;
    public double? CombatOverlayWidth { get; set; }
    public double? CombatOverlayHeight { get; set; }

    /// <summary>The boss cast bar is a window of its own, placed and scaled apart from the target of the target (whose frame is the CombatOverlay* values).</summary>
    public double? CombatCastOverlayLeft { get; set; }
    public double? CombatCastOverlayTop { get; set; }
    public double CombatCastOverlayScale { get; set; } = 1.0;
    public double? CombatCastOverlayWidth { get; set; }
    public double? CombatCastOverlayHeight { get; set; }

    /// <summary>The target's health bar is a window of its own as well.</summary>
    public double? CombatHpOverlayLeft { get; set; }
    public double? CombatHpOverlayTop { get; set; }
    public double CombatHpOverlayScale { get; set; } = 1.0;
    public double? CombatHpOverlayWidth { get; set; }
    public double? CombatHpOverlayHeight { get; set; }

    /// <summary>Place, size and scale of each element by its id ("BossCast", "TargetOfTarget", "Healer").</summary>
    public Dictionary<string, CombatElementLayout> CombatElements { get; set; } = new();

    public bool CombatShowBossCast { get; set; } = true;
    public bool CombatShowTargetOfTarget { get; set; }

    /// <summary>The healer's target: its name, hit points and effects. Off until switched on.</summary>
    public bool CombatShowHealer { get; set; }
    public bool CombatHealerHpBar { get; set; }
    public bool CombatHealerEffects { get; set; }


    /// <summary>Which casts the bar shows: "all", "area" (at you and area attacks) or "you" (only at you).</summary>
    public string CombatCastFilter { get; set; } = "all";

    /// <summary>The most casts shown at once (1 to 3).</summary>
    public int CombatMaxCasts { get; set; } = 2;

    /// <summary>Casts of monsters that are not bosses too.</summary>
    public bool CombatCastsOfAllMonsters { get; set; }

    /// <summary>Voice output for boss casts: off until switched on. Independent of the overlay (works with it off); only while Aion is in the foreground.</summary>
    public bool SpeechOn { get; set; }

    /// <summary>The voice's language tag (such as "de-DE") and its id (<c>VoiceInformation.Id</c>); empty = the first voice of the program's language.</summary>
    public string SpeechLanguage { get; set; } = "";
    public string SpeechVoiceId { get; set; } = "";

    /// <summary>The recorded natural voice (<see cref="NaturalVoice"/>, 1.20.16) was chosen once for everyone whose voice speaks English; whoever picks
    /// a Windows voice again keeps it.</summary>
    public bool NaturalVoiceDefaulted { get; set; }

    /// <summary>What is announced: with the skill's name (off = only "Attention"), which casts (at you, at everyone = area, lethal at you, lethal at everyone).</summary>
    public bool SpeechSkillName { get; set; } = true;
    public bool SpeechOnYou { get; set; } = true;
    public bool SpeechOnAll { get; set; }
    public bool SpeechLethalYou { get; set; } = true;
    public bool SpeechLethalAll { get; set; }

    /// <summary>Volume (0 to 100 percent) per kind of announcement.</summary>
    public int SpeechVolumeOnYou { get; set; } = 100;
    public int SpeechVolumeOnAll { get; set; } = 100;
    public int SpeechVolumeLethalYou { get; set; } = 100;
    public int SpeechVolumeLethalAll { get; set; } = 100;

    /// <summary>The audio output of all the meter's sounds (<c>DeviceInformation.Id</c>); empty = the Windows default device.</summary>
    public string AudioDeviceId { get; set; } = "";

    /// <summary>Master switch of the world boss sounds: off = no boss plays its sound. Off until switched on.</summary>
    public bool BossNotify { get; set; }

    /// <summary>Per boss (NPC id as text): watched or not, and the sound with its volume that plays when it is back. A boss without an entry is not watched.</summary>
    public Dictionary<string, BossAlertSetting> BossAlerts { get; set; } = new();

    /// <summary>Place, scale and size of the world boss overlay.</summary>
    public double? BossOverlayLeft { get; set; }
    public double? BossOverlayTop { get; set; }
    public double BossOverlayScale { get; set; } = 1.0;
    public double? BossOverlayWidth { get; set; }
    public double? BossOverlayHeight { get; set; }

    /// <summary>Master switch of the timetable reminder sounds: off = no event plays its sound (the choices below are kept).</summary>
    public bool TimetableNotify { get; set; } = false;

    /// <summary>How many minutes before an event starts its sound plays, 0 to 60 (the same for every event).</summary>
    public int TimetableSoundMinutes { get; set; } = 5;

    /// <summary>Per event id: shown in the timetable overlay or not, and the reminder sound (empty = none) with its volume. An event without an entry is shown, without sound.</summary>
    public Dictionary<string, TimetableEventSetting> TimetableEvents { get; set; } = new();

    /// <summary>Scale of the compact overlay (text and all), 0.7 to 2, set by its corner grip with Ctrl held.</summary>
    public double OverlayScale { get; set; } = 1.0;

    /// <summary>How much wider (or, below 0, narrower) the compact overlay is than its Meter look makes it, before its scale; set by dragging its
    /// corner grip sideways. Kept apart from the look's own width, so a look with more columns still gets the room they need.</summary>
    public double OverlayExtraWidth { get; set; }

    /// <summary>Height of the compact overlay's player lines before its scale, set by dragging its corner grip up or down; 0 = as tall as the lines.</summary>
    public double OverlayRowsHeight { get; set; }

    /// <summary>The meter overlay: the compact panel in a window of its own over the game, beside the client window (history, settings, the fight
    /// report). On by default; the eye button, the overlay's X, its hotkey and the tray menu switch it. Switched off, it stays off: no boss fight brings it back.</summary>
    public bool MeterOverlayOn { get; set; } = true;

    /// <summary>"Only during boss fights": the overlay is on screen while the own character fights a boss and goes on fighting after it, and steps aside
    /// <see cref="MeterOverlayHideSeconds"/> after the last fight (a dungeon or a dummy alone no longer brings it up: see MeterOverlayRule). Off: it stays
    /// up while the game is in front.</summary>
    public bool MeterOverlayAutoHide { get; set; } = true;

    /// <summary>With <see cref="MeterOverlayAutoHide"/>: a boss fight of the own character brings the overlay up by itself, while the overlay is switched on
    /// (4 Nov 2026: no more switching it on again after the player closed it). Off: only the eye button or its hotkey shows it.</summary>
    public bool MeterPopUpForBoss { get; set; } = true;

    /// <summary>The open world is counted too (the Open world button on the meter page). Off: the meter counts dungeons, arenas, boss fights and training dummies only.</summary>
    public bool TrackOpenWorld { get; set; }

    /// <summary>Seconds without combat before the overlay steps aside outside instances (10 to 600).</summary>
    public int MeterOverlayHideSeconds { get; set; } = 30;

    /// <summary>The meter overlay's panel: "Themed" (the site's dark colours with the gold edge), "Clear" (see-through, nearly no frame) or "Graphite"
    /// (a plain neutral grey with a grey edge, the raid log design's). Set in the meter's menu and on the Meter look page (MeterLook.Panels).</summary>
    public string OverlayLook { get; set; } = "Themed";

    /// <summary>Whose rows are shown picks itself: everybody in an instance or arena (the raid), only the own character at a training dummy, the
    /// All / Group / Corps / Me switch's choice elsewhere. A click on the switch holds until the player goes somewhere else.</summary>
    public bool AutoScope { get; set; } = true;

    /// <summary>The meter overlay stays where it is: no moving, no scaling (its buttons still work).</summary>
    public bool MeterOverlayLocked { get; set; }

    /// <summary>Where the meter overlay was put (null = beside the client window at the first start).</summary>
    public double? MeterOverlayLeft { get; set; }

    public double? MeterOverlayTop { get; set; }

    /// <summary>The note that the client window's ✕ only hides it (the meter goes on in the tray) was shown once.</summary>
    public bool CloseToTrayHintShown { get; set; }

    /// <summary>The boss's health bar in the compact overlay (current / full, %, HP check mark).</summary>
    public bool ShowBossHp { get; set; } = true;

    /// <summary>How opaque the boss health bar window is (0.2 to 1).</summary>
    public double BossHpOpacity { get; set; } = 1.0;

    /// <summary>How opaque the boss cast bar window is (0.2 to 1).</summary>
    public double CombatCastOpacity { get; set; } = 1.0;

    /// <summary>Start from zero when a fight begins after <see cref="AutoResetSeconds"/> without any
    /// damage, or when a boss is pulled: the meter shows the current fight, the previous one is kept
    /// in the fight history. Off by default - a reset throws the fight on screen away, so it is the
    /// player's choice. Named AutoResetEnabled since 0.10.19: the earlier "AutoReset" (on by default)
    /// was never a deliberate choice, and its saved value is dropped.</summary>
    public bool AutoResetEnabled { get; set; }

    /// <summary>Seconds without damage after which the next fight starts from zero (1 to 600).</summary>
    public int AutoResetSeconds { get; set; } = 10;

    /// <summary>Whose rows the meter shows: "All" (everybody around), "Group" (you and your party) or
    /// "Corps" (your party and the one it is joined with). Switched in the overlay's header or the
    /// Mode menu.</summary>
    public string ViewScope { get; set; } = "Group";

    /// <summary>Goes round all modes: damage, healing, damage taken, All in One, All in One Compact (see MainWindow.NextMode).</summary>
    public string HotkeyMode { get; set; } = "Ctrl+Alt+M";

    /// <summary>The hotkey overlay (see HotkeyOverlayWindow): a movable, transparent list of the hotkeys. Off until switched on; the ids of the
    /// hotkeys the player switched off in the list are kept in <see cref="HotkeyOverlayHidden"/> (everything shows by default).</summary>
    public bool ShowHotkeyOverlay { get; set; }

    public List<string> HotkeyOverlayHidden { get; set; } = new();

    public double? HotkeyOverlayLeft { get; set; }
    public double? HotkeyOverlayTop { get; set; }
    public double HotkeyOverlayScale { get; set; } = 1.0;
    public double? HotkeyOverlayWidth { get; set; }
    public double? HotkeyOverlayHeight { get; set; }

    /// <summary>Shows or hides the timetable overlay (see TimetableWindow).</summary>
    public string HotkeyTimetable { get; set; } = "Ctrl+Alt+T";

    /// <summary>Switches the pet map (with its list) on or off.</summary>
    public string HotkeyPetMap { get; set; } = "Ctrl+Alt+K";
    /// <summary>Hotkey overlay on / off.</summary>
    public string HotkeyHotkeyOverlay { get; set; } = "Ctrl+Alt+O";
    /// <summary>All overlays (hotkey list, timetable, interactive map, pet info) off, and the same ones back on.</summary>
    public string HotkeyAllOverlays { get; set; } = "Ctrl+Alt+A";

    /// <summary>Every overlay and the meter unlocked (dragged and scaled where they are, over the game), and locked again: no need to open the app for it.</summary>
    public string HotkeyMoveOverlays { get; set; } = "Ctrl+Alt+V";
    /// <summary>World boss overlay on / off.</summary>
    public string HotkeyWorldBoss { get; set; } = "Ctrl+Alt+B";
    /// <summary>Network overlay on / off.</summary>
    public string HotkeyNetwork { get; set; } = "Ctrl+Alt+N";
    public string HotkeyCombatOverlay { get; set; } = "Ctrl+Alt+C";
    /// <summary>The arrows on the edge of the interactive map pointing at pets / gatherables that are out of view (one switch each); on by default.</summary>
    public bool ShowPetArrows { get; set; } = true;
    public bool ShowGatherArrows { get; set; } = true;
    /// <summary>A sound when a Hidden Cube appears in range (the game announces it only when the player is near): switch, sound id (see <see cref="NotifySounds.All"/>), volume 0 to 100. Off until switched on.</summary>
    public bool CubeSoundEnabled { get; set; } // no longer read: the sounds are switched one by one below (1.19.4)

    /// <summary>A small note at the top of the screen when a Hidden Cube comes into range ("Hidden Cube nearby · 42 m"); the cube stays on the map. On by default.</summary>
    public bool CubePopup { get; set; } = true;

    /// <summary>The chime when a cube comes into range (and the falling tone when it goes out of range). On by default.</summary>
    public bool CubeArrivalSound { get; set; } = true;

    /// <summary>The beeps that come faster the closer the player gets to the cube (like a parking sensor). Off until switched on.</summary>
    public bool CubeBeeps { get; set; }
    /// <summary>A Hidden Cube coming into range brings the pet map back when it is hidden (the hotkey, the module's switch or the map's own switch), until 10 seconds after the last cube is gone, without saving the switch (Stacy's idea, 17 October 2026). Only where the map shows cubes. Off by default.</summary>
    public bool ShowMapOnCube { get; set; }

    /// <summary>A note over the game when the Black Cloud Merchant offers one of the ticked pets only it sells (on opening the shop, any character). On by default.</summary>
    public bool BlackCloudPopup { get; set; } = true;

    /// <summary>A note over the game when the merchant's offers have renewed for a character that still needs a ticked pet (the shop then has to be opened to see
    /// them: the game sends the offers only then). On by default.</summary>
    public bool BlackCloudRenewNote { get; set; } = true;

    /// <summary>The pets only the merchant sells that the player unticked (finished, or not wanted): no note for them. Every other one is ticked, new ones too.</summary>
    public List<int> BlackCloudUnwatched { get; set; } = new();

    /// <summary>A line on the meter while a Hidden Cube is in range ("Hidden Cube nearby · 42 m"). On by default.</summary>
    public bool CubeOverlayAlert { get; set; } = true;
    /// <summary>The game's minimap is read for an exact own position (MinimapReader: the game window's picture matched against the map, while a Hidden Cube is in
    /// range or the map overlay is on). On by default (the owner, 2 Nov 2026), with a note on the meter the first time it gives a position.</summary>
    public bool MinimapReading { get; set; } = true;
    /// <summary>The note that the minimap is read was shown once.</summary>
    public bool MinimapNoticeShown { get; set; }
    /// <summary>Where the minimap is on screen, per game window size ("2560x1440"): its middle x, y and radius in the window's pixels, and metres per pixel.
    /// Found by the reader itself.</summary>
    public Dictionary<string, double[]> MinimapLayouts { get; set; } = new();
    /// <summary>Buff alerts (MainWindow.BuffAlerts.cs): a line on the meter when a damage buff, a scroll or food drops off the player while he fights. On by default.</summary>
    public bool BuffDropAlert { get; set; } = true;
    /// <summary>The buff alert's sound (Windows' exclamation). Off by default.</summary>
    public bool BuffDropSound { get; set; }
    /// <summary>More effects to watch, by name, comma separated ("Wind Vigor, Block Pain").</summary>
    public string BuffAlertNames { get; set; } = "";
    /// <summary>Group alerts (MainWindow.GroupAlerts.cs): while the game is in the background, the group finder's news (someone joined, taken into a group, going in) flashes the game's taskbar button and shows a Windows notification. On by default.</summary>
    public bool GroupAlert { get; set; } = true;
    /// <summary>The group alert's sound (Windows' asterisk) when the player is taken into a group or the group goes in; never for someone joining. On by default.</summary>
    public bool GroupAlertSound { get; set; } = true;
    /// <summary>The militia guards ("Wachmann") of the other side's maps as red badges with a skull and a danger zone on the map, and an arrow on its edge for one out of view (Stacy's idea, 17 October 2026). On by default; only the maps of the other side show any.</summary>
    public bool ShowGuards { get; set; } = true;
    public string CubeSound { get; set; } = "ping";
    public int CubeSoundVolume { get; set; } = 70;
    /// <summary>Pet arrows / gather arrows on / off.</summary>
    public string HotkeyPetArrows { get; set; } = "Ctrl+Alt+E";
    public string HotkeyGatherArrows { get; set; } = "Ctrl+Alt+G";

    /// <summary>Switches the skill assist overlay on and off.</summary>
    public string HotkeyAssist { get; set; } = "Ctrl+Alt+S";

    /// <summary>Switches the loot overlay on and off.</summary>
    public string HotkeyLoot { get; set; } = "Ctrl+Alt+L";

    /// <summary>Switches the crafting overlay on and off.</summary>
    public string HotkeyCraft { get; set; } = "Ctrl+Alt+R";

    /// <summary>The notice that appears when the eye (hide the window) is clicked was dismissed with "do not show again".</summary>
    public bool HideUiHintDismissed { get; set; }

    /// <summary>The timetable overlay: what is active now and what starts within the lookahead (<see cref="TimetableLookaheadMinutes"/>). Its place
    /// and scale are remembered (null left/top = a default corner).</summary>
    public bool ShowTimetable { get; set; } = true;

    public double? TimetableLeft { get; set; }

    public double? TimetableTop { get; set; }

    public double TimetableScale { get; set; } = 1.0;

    /// <summary>The width the timetable was pulled to (null = the default).</summary>
    public double? TimetableWidth { get; set; }

    /// <summary>The pet farming overlay (see PetFarmWindow): the monster marked or hit (from the network) and its pet's level. Off until switched on.</summary>
    public bool ShowPetFarm { get; set; }

    /// <summary>Locked (default): the pet farming overlay is click-through. Unlocked: it can be clicked, moved and scaled.</summary>
    public bool PetFarmLocked { get; set; } = true;

    /// <summary>Locked (default): the small pet counter of the targeted monster is click-through. Unlocked: it can be moved and scaled. Independent of the map's lock.</summary>
    public bool PetInfoLocked { get; set; } = true;

    /// <summary>Locked (default): the world boss overlay is click-through. Unlocked: it can be clicked, moved and scaled. Independent of the pet overlays' lock.</summary>
    public bool BossOverlayLocked { get; set; } = true;

    /// <summary>Locked (default): the timetable overlay is click-through (no header, no grip). Unlocked: it can be moved and scaled.</summary>
    public bool TimetableLocked { get; set; } = true;

    public double PetFarmScale { get; set; } = 1.0;

    /// <summary>The pet map (see the Pet-Karte settings page): shown or not, how many metres around the player it covers, how opaque it is.</summary>
    public bool ShowPetMap { get; set; }

    /// <summary>The interactive map is two overlays: the map and the list of pets and collectibles. Each is chosen on its own; shown only while the module's switch is on too.</summary>
    public bool PetMapShowMap { get; set; } = true;

    public bool PetMapShowList { get; set; } = true;

    /// <summary>The list is positioned on its own; null (a settings file from before) follows <see cref="PetFarmLocked"/>, as the map does.</summary>
    public bool? PetListLocked { get; set; }

    /// <summary>The hotkey overlay is positioned on its own; null (a settings file from before) follows <see cref="PetFarmLocked"/>, as it did.</summary>
    public bool? HotkeyOverlayLocked { get; set; }

    public int PetMapRadius { get; set; } = 150;

    public double PetMapOpacity { get; set; } = 0.7;

    // ---- the meter's look (Overlays → Meter look; MeterLook) ----

    /// <summary>The bars behind the lines: glow (the class colour fading out, a bright line under it), flat, solid (the strong class colour), thread, capsule, frame.</summary>
    public string MeterBarStyle { get; set; } = "glow";

    /// <summary>The height of a line: compact, normal, roomy.</summary>
    public string MeterRowSize { get; set; } = "normal";

    /// <summary>The meter's font (MeterLook.Fonts).</summary>
    public string MeterFont { get; set; } = "segoe";

    /// <summary>The meter's text size, 0.85 to 1.3 of the normal one.</summary>
    public double MeterTextScale { get; set; } = 1.0;

    /// <summary>The columns and parts of a line that are shown (MeterLook.Columns, NameParts); null: the usual ones.</summary>
    public List<string>? MeterColumns { get; set; }

    /// <summary>A line of titles over the columns.</summary>
    public bool MeterColumnTitles { get; set; }

    /// <summary>The letter or % sign after a figure (12.4K, 38%) is printed smaller and dimmer, as a log's table prints it. Off by default.</summary>
    public bool MeterSmallUnits { get; set; }

    /// <summary>The buttons in the meter's head: "always", or "hover" (only while the mouse is over the meter).</summary>
    public string MeterHeaderButtons { get; set; } = "always";

    /// <summary>The line with the group's total (and Me / Group / All).</summary>
    public bool MeterShowTotal { get; set; } = true;

    /// <summary>The status line under the list (the game being read, the ping).</summary>
    public bool MeterShowStatus { get; set; } = true;

    /// <summary>The meter grows upwards: its bottom edge stays where it is when lines come and go.</summary>
    public bool MeterGrowUp { get; set; }

    /// <summary>Where the meter's bottom edge was left (growing upwards keeps it there).</summary>
    public double? MeterOverlayBottom { get; set; }

    /// <summary>The best line at the bottom instead of at the top.</summary>
    public bool MeterBestLast { get; set; }

    /// <summary>The pets whose monsters the pet map shows (pet ids). Empty until the player picks some.</summary>
    public List<int> PetMapPets { get; set; } = new();

    /// <summary>The interactive map guides along the pet route (Aion2/PetRoute.cs, the website's): the next stop, a line to it and its pets. On by default.</summary>
    public bool PetRouteOn { get; set; } = true;

    /// <summary>The genera on the route (Cogni, Natura, Fera, Varian); null: all four.</summary>
    public List<string>? PetRouteGenera { get; set; }

    /// <summary>The route starts near the player (true) or at the faction's start camp, as on the website (false).</summary>
    public bool PetRouteFromHere { get; set; } = true;

    /// <summary>The route's next stop above the pet list (its name, distance, pets, teleport and the stop after it); off, the list shows only the pets.</summary>
    public bool PetRouteInList { get; set; } = true;

    /// <summary>The interactive map round (to lay over the game's minimap) instead of square.</summary>
    public bool PetMapRound { get; set; }

    /// <summary>The points of the compass around the edge of the interactive map (N, NE, E ... north is up).</summary>
    public bool PetMapCompass { get; set; } = true;

    /// <summary>The interactive map shows only its marks and the route, no map picture (to lay over the game's minimap).</summary>
    public bool PetMapMarkersOnly { get; set; }

    /// <summary>How far the interactive map is zoomed out: 0 the close-up around the player (<see cref="PetMapRadius"/>), 1 the whole map; the wheel over the map,
    /// its + and − buttons and the zoom hotkeys change it.</summary>
    public double PetMapZoom { get; set; }

    /// <summary>The interactive map follows the player (its padlock shut, the default). Off: free move, the map is dragged with the mouse to look at any part of it;
    /// the padlock on the map changes it.</summary>
    public bool PetMapFollow { get; set; } = true;

    /// <summary>Zoom the interactive map in / out (a tenth of the way to the whole map per press).</summary>
    public string HotkeyPetMapZoomIn { get; set; } = "Ctrl+Alt+PageUp";

    public string HotkeyPetMapZoomOut { get; set; } = "Ctrl+Alt+PageDown";

    /// <summary>No longer used: the full map overlay gave way to the zoom of the map (<see cref="PetMapZoom"/>). Kept so older settings files read as before.</summary>
    public bool PetMapWhole { get; set; }

    /// <summary>No longer used (was: full map overlay on / off).</summary>
    public string HotkeyPetMapWhole { get; set; } = "Ctrl+Alt+W";

    /// <summary>No longer used (was: place, scale and own size of the full map overlay).</summary>
    public double? PetWholeLeft { get; set; }
    public double? PetWholeTop { get; set; }
    public double PetWholeScale { get; set; } = 1.4;
    public double? PetWholeWidth { get; set; }
    public double? PetWholeHeight { get; set; }

    /// <summary>The map shows every pet the player still needs (all but those at the top level, from the collection the game sends) instead of
    /// <see cref="PetMapPets"/>. Null (never switched): on while no pet was picked by hand.</summary>
    public bool? PetMapAutoPets { get; set; }

    /// <summary>Whether the map follows the collection (see <see cref="PetMapAutoPets"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PetMapAuto => PetMapAutoPets ?? PetMapPets.Count == 0;

    /// <summary>Width of the region list on the Region tab of the pet settings (the player drags the splitter).</summary>
    public double PetRegionListWidth { get; set; } = 185;

    /// <summary>Rare tab of the pet settings: a pet with at most this many spawn points on a map (all its mobs there together) counts as rare on that map.</summary>
    public int PetRareMax { get; set; } = 20;

    /// <summary>Rare tab: from this many spawn points in instances a pet is plentiful there and is not listed on the maps, only under Instances.</summary>
    public int PetRareInstanceMin { get; set; } = 40;

    /// <summary>The collectibles the interactive map shows (by their English name, see the Sammeln page); none until the player picks some.</summary>
    public List<string> PetMapGatherItems { get; set; } = new();

    /// <summary>The map and the position the pet map knew last (kept so it can show them while the position is unknown, e.g. with the game closed).</summary>
    public string? PetMapLastMap { get; set; }
    public double? PetMapLastX { get; set; }
    public double? PetMapLastY { get; set; }

    /// <summary>Place and size of the pet map window and of the pet list window (each on its own).</summary>
    public double? PetMapLeft { get; set; }

    public double? PetMapTop { get; set; }

    public double PetMapScale { get; set; } = 1.0;

    public double? PetListLeft { get; set; }

    public double? PetListTop { get; set; }

    public double PetListScale { get; set; } = 1.0;

    /// <summary>Own width/height of the three overlays when the player pulled an edge (null: as large as the content), before the scale.</summary>
    public double? PetMapWidth { get; set; }
    public double? PetMapHeight { get; set; }
    public double? PetListWidth { get; set; }
    public double? PetListHeight { get; set; }
    public double? PetFarmWidth { get; set; }
    public double? PetFarmHeight { get; set; }

    public double? PetFarmLeft { get; set; }

    public double? PetFarmTop { get; set; }


    /// <summary>Network adapter the Aion 2 packet capture listens on (Aion2/Capture/CaptureAdapters):
    /// null/empty = automatic (the adapter Windows routes internet traffic through), "all" = every
    /// adapter, else a NetworkInterface.Id. Matters when a gaming VPN carries the game's traffic.</summary>
    public string? CaptureAdapterId { get; set; }

    /// <summary>A random identifier of this installation, made the first time something is sent that must not carry a name (the stat recordings and network reports); it
    /// ties those sendings of one installation together and to nothing else - no name, no account. Empty until then (see <see cref="EnsureAnonymousId"/>).</summary>
    public string AnonymousId { get; set; } = "";

    /// <summary>The stat calculator: the expert mode, the side the player mostly hits from ("unknown", "front", "back") and how much of the hits land there, in percent.</summary>
    public bool StatCalcExpert { get; set; }

    public string StatCalcSide { get; set; } = "unknown";

    /// <summary>Whether the player chose the side himself (a click on a side or on "take from my last fight"); until then the calculator takes it from the last fight or the character's stats.</summary>
    public bool StatCalcSideChosen { get; set; }

    public int StatCalcSideRate { get; set; } = 70;

    /// <summary>The identifier of this installation, made and saved when there is none yet.</summary>
    public static string EnsureAnonymousId()
    {
        MeterSettings settings = Load();
        if (settings.AnonymousId.Length == 0)
        {
            settings.AnonymousId = Guid.NewGuid().ToString("N");
            settings.Save();
        }

        return settings.AnonymousId;
    }

    /// <summary>The user's own Aion 2 character name. Aion 2 frames name every player but give no
    /// hint which one is you, so the meter matches this name; it fills it in by itself as soon as
    /// the stream reveals it (a party roster), and Settings lets it be typed for solo play.</summary>
    public string? Aion2CharacterName { get; set; }








    /// <summary>
    /// Per-user settings location, NOT next to the exe. Beside the exe is where a self-updating
    /// install is least safe to keep anything: Velopack swaps the whole application directory when
    /// an update applies, so settings written there would be replaced along with it. (The same
    /// path was already wrong for the older per-machine MSI, which put the exe under Program Files
    /// where an unprivileged process cannot write at all.)
    /// </summary>
    /// <summary>The folder the settings and what the meter learns (cast-skills.json ...) live in.</summary>
    public static string DataDirectory => Path.GetDirectoryName(SettingsPath)!;

    private static string SettingsPath => Path.Combine(
        Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir // tests only: a throw-away data folder
            ? testDir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.DataFolder),
        "meter-settings.json");

    /// <summary>Where older, elevated builds kept the file. Read once, on first launch after the
    /// upgrade, so an existing Aion folder and character list survive the move instead of the
    /// user finding an empty settings dialog.</summary>
    private static string LegacySettingsPath => Path.Combine(AppContext.BaseDirectory, "meter-settings.json");

    private static string Sibling(string suffix) => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "meter-settings." + suffix + ".json");

    /// <summary>The copy made at each start from a settings file that read fine (see <see cref="BackupOnStart"/>).</summary>
    private static string BackupPath => Sibling("backup");

    /// <summary>What "reset to defaults" took away, kept so it can be taken back (see <see cref="UndoReset"/>).</summary>
    private static string UndoPath => Sibling("undo");

    /// <summary>True once in a session when the settings file was damaged and the settings came back from the copy of the last start.</summary>
    public static bool RecoveredFromBackup { get; private set; }

    /// <summary>Changes whenever the settings file is written. A timer can compare it to reuse its last <see cref="Load"/> instead of
    /// reading and parsing the file again on every tick; a negative length means "no settings file", where nothing is worth caching.</summary>
    public static (DateTime WrittenUtc, long Length) FileStamp()
    {
        try
        {
            var info = new FileInfo(SettingsPath);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (DateTime.MinValue, -2);
        }
    }

    private static readonly object LoadLock = new();
    private static string? _lastText;
    private static (DateTime, long) _lastStamp = (DateTime.MinValue, -3);
    private static string? _lastPath; // the data folder can change (the self-tests point it elsewhere): the text kept is only that file's

    /// <summary>
    /// The settings as on disk, a fresh object each call (callers change and save it). The file is read again only when it changed (time and size): the many
    /// readers (a timer each) parse the text kept from the last read instead of going to the disk. A file another program holds for a moment (virus scanner,
    /// backup, cloud sync) is tried again and else the last good text is used: only a file that cannot be parsed counts as damaged (3 November 2026: a
    /// locked file was taken for a damaged one and the copy of the start put back, losing what was changed since).
    /// </summary>
    public static MeterSettings Load()
    {
        string path = File.Exists(SettingsPath) ? SettingsPath
            : File.Exists(LegacySettingsPath) ? LegacySettingsPath
            : SettingsPath;
        if (File.Exists(path))
        {
            lock (LoadLock)
            {
                (DateTime, long) stamp = path == SettingsPath ? FileStamp() : (DateTime.MinValue, -4);
                if (stamp.Item2 >= 0 && stamp == _lastStamp && path == _lastPath && _lastText is { } kept && Parse(kept) is { } cached)
                {
                    return cached;
                }

                ReadResult result = TryReadText(path, out string? text);
                if (result == ReadResult.Ok && Parse(text!) is { } loaded)
                {
                    if (path == SettingsPath)
                    {
                        _lastText = text;
                        _lastStamp = stamp;
                        _lastPath = path;
                    }

                    return loaded;
                }

                if (result == ReadResult.Locked)
                {
                    // held by another program right now: the last good text (else the defaults of this read only); nothing is moved or restored
                    // with nothing read yet, the defaults stand in for this read and refuse to be saved: written back, they would replace every setting
                    return path == _lastPath && _lastText is { } good && Parse(good) is { } last ? last : new MeterSettings { _notRead = true };
                }
            }

            // A crash or a power cut while it was being written: keep the damaged file beside the others and go back to the copy of the last start.
            if (path == SettingsPath)
            {
                KeepDamaged(path);
                if (File.Exists(BackupPath) && TryRead(BackupPath) is { } restored)
                {
                    RecoveredFromBackup = true;
                    try
                    {
                        File.Copy(BackupPath, SettingsPath + ".tmp", overwrite: true);
                        File.Move(SettingsPath + ".tmp", SettingsPath, overwrite: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // the next Save writes it anyway
                    }

                    return restored;
                }
            }
        }

        return new MeterSettings();
    }

    private enum ReadResult { Ok, Damaged, Locked }

    // the file's text; a file held by another program is tried again for a moment before it counts as locked (never as damaged)
    private static ReadResult TryReadText(string path, out string? text)
    {
        text = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                text = File.ReadAllText(path);
                return text.Trim().Length == 0 ? ReadResult.Damaged : ReadResult.Ok;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 4)
                {
                    return ReadResult.Locked;
                }

                Thread.Sleep(25 * attempt);
            }
        }
    }

    private static MeterSettings? Parse(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<MeterSettings>(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MeterSettings? TryRead(string path) => TryReadText(path, out string? text) == ReadResult.Ok ? Parse(text!) : null;

    /// <summary>
    /// Puts on <paramref name="into"/> every setting that <paramref name="changed"/> holds differently from <paramref name="baseline"/>: what a window changed,
    /// over what is on disk now. Everything else keeps the value of <paramref name="into"/> (a switch flipped on a page while the Settings window was open,
    /// an overlay moved), which the Settings window's Save used to write back with the value of the moment it opened.
    /// </summary>
    public static void CopyChanged(MeterSettings changed, MeterSettings baseline, MeterSettings into)
    {
        foreach (System.Reflection.PropertyInfo p in typeof(MeterSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.GetMethod is not { IsPublic: true } || p.SetMethod is not { IsPublic: true })
            {
                continue;
            }

            object? now = p.GetValue(changed);
            if (JsonSerializer.Serialize(now, p.PropertyType) != JsonSerializer.Serialize(p.GetValue(baseline), p.PropertyType))
            {
                p.SetValue(into, now);
            }
        }
    }

    private static void KeepDamaged(string path)
    {
        try
        {
            File.Move(path, Sibling("damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")), overwrite: true);
            foreach (string old in Directory.GetFiles(Path.GetDirectoryName(path)!, "meter-settings.damaged-*.json").OrderByDescending(f => f).Skip(3))
            {
                File.Delete(old); // the three newest are enough
            }
        }
        catch (IOException)
        {
        }
    }

    // the stand-in of a file that could not be read (see Load); not a property, so never in the file
    private bool _notRead;

    /// <summary>Written to a file of its own first and put in place at once, so a crash or a power cut never leaves half a file.</summary>
    public void Save()
    {
        if (_notRead)
        {
            CrashLog.Write("settings not saved: the file was held by another program when they were read", null);
            return;
        }

        AudioOutput.DeviceId = AudioDeviceId;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temp = SettingsPath + ".tmp";
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        lock (SaveLock)
        {
            // Windows refuses to replace a file another process has open for a moment (virus scanner, cloud sync, a reader), so it is tried again a few times.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.WriteAllText(temp, json);
                    File.Move(temp, SettingsPath, overwrite: true);
                    lock (LoadLock)
                    {
                        _lastText = json;
                        _lastStamp = FileStamp();
                        _lastPath = SettingsPath;
                    }

                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= 6)
                    {
                        CrashLog.Write("settings could not be saved", ex);
                        return; // the next Save writes it again; a lost write must never end the meter
                    }

                    Thread.Sleep(50 * attempt);
                }
            }
        }
    }

    private static readonly object SaveLock = new();

    /// <summary>Copies a settings file that reads fine to the backup, once at each start; a damaged file is never copied over a good backup.</summary>
    public static void BackupOnStart()
    {
        try
        {
            if (File.Exists(SettingsPath) && TryRead(SettingsPath) is not null)
            {
                File.Copy(SettingsPath, BackupPath, overwrite: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>What a reset leaves alone: the privacy and upload decisions (a reset must never switch an upload on or off), the first-start wizard, the update check and the own character.</summary>
    private static void KeepAcrossReset(MeterSettings from, MeterSettings to)
    {
        to.SetupCompleted = from.SetupCompleted;
        to.PrivacyDecision = from.PrivacyDecision;
        to.PrivacyVersion = from.PrivacyVersion;
        to.AnonymousId = from.AnonymousId;
        to.AutoUploadProfile = from.AutoUploadProfile;
        to.AutoUploadBoss = from.AutoUploadBoss;
        to.UploadOtherPlayersProfiles = from.UploadOtherPlayersProfiles;
        to.ShareLoginWithLegion = from.ShareLoginWithLegion;
        to.ShareCpMeasurements = from.ShareCpMeasurements;
        to.ShareMapData = from.ShareMapData;
        to.LoginShareEligible = from.LoginShareEligible;
        to.CheckForUpdates = from.CheckForUpdates;
        to.Aion2CharacterName = from.Aion2CharacterName;
    }

    /// <summary>Puts every setting back to its default except <see cref="KeepAcrossReset"/>; what it replaced is kept for <see cref="UndoReset"/>.</summary>
    public static void ResetToDefaults()
    {
        MeterSettings current = Load();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(UndoPath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
        var fresh = new MeterSettings();
        KeepAcrossReset(current, fresh);
        fresh.Save();
    }

    /// <summary>Takes a reset back; false when there is nothing to take back.</summary>
    public static bool UndoReset()
    {
        if (!File.Exists(UndoPath) || TryRead(UndoPath) is not { } before)
        {
            return false;
        }

        before.Save();
        File.Delete(UndoPath);
        return true;
    }
}

/// <summary>What the player chose for one timetable event (see <see cref="MeterSettings.TimetableEvents"/>).</summary>
public sealed class TimetableEventSetting
{
    public bool Show { get; set; } = true;

    /// <summary>The id of the reminder sound (see <see cref="NotifySounds.All"/>); empty = no sound.</summary>
    public string Sound { get; set; } = "";

    /// <summary>0 to 100.</summary>
    public int Volume { get; set; } = 70;
}

/// <summary>What the player chose for one world boss (see <see cref="MeterSettings.BossAlerts"/>).</summary>
public sealed class BossAlertSetting
{
    /// <summary>The switch before the boss's name: the boss is on the overlay and plays its sound.</summary>
    public bool Enabled { get; set; }

    /// <summary>The id of the sound (see <see cref="NotifySounds.All"/>); empty = no sound.</summary>
    public string Sound { get; set; } = "chime";

    /// <summary>0 to 100.</summary>
    public int Volume { get; set; } = 60;
}

/// <summary>Place, size and scale of one element of the combat overlay, in the overlay frame's own units (its scaling excluded).</summary>
public sealed class CombatElementLayout
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Scale { get; set; } = 1.0;
}

/// <summary>Place and size of one popped-out tool window, in device-independent pixels of the whole desktop (a second monitor has coordinates beyond the first).</summary>
public sealed class PopOutPlace
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>The window is to come back at the next start.</summary>
    public bool Open { get; set; }
}

/// <summary>
/// One saved layout of the cooldowns on the screen (Skill Assist, Presets; 5 November 2026): each skill's place for the bars and for the squares, the
/// look, the size, the bars' width, moving together, the ready skills shown, and which skills are left off the screen.
/// </summary>
public sealed class CooldownPreset
{
    public DateTime SavedAt { get; set; }
    public string Style { get; set; } = "Bars";
    public double Scale { get; set; } = 1.0;
    public double BarWidth { get; set; } = 170;
    public bool Together { get; set; }
    public bool ShowReady { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public double ReadyOpacity { get; set; } = 1.0;
    public double BackOpacity { get; set; } = 1.0;
    public Dictionary<int, double[]> Places { get; set; } = new();
    public Dictionary<int, double[]> TilePlaces { get; set; } = new();
    public List<int> Hidden { get; set; } = new();
    public List<int> Extra { get; set; } = new();
}
