using System.Text.RegularExpressions;

namespace ReforgerLauncher;

// The Scenarios tab of a config / preset: every scenario on this PC (base game, downloaded mods, unpublished mod-src
// projects) like the game's own scenario browser, with PLAY (this PC), HOST (a server for others) and JOIN.
public partial class MainForm
{
    ListView scenList;
    TextBox scenFind;
    ComboBox scenView;
    Label scenCount, detTitle, detMeta, detStatus;
    TextBox detDesc;
    PictureBox detImage;
    Button playButton, hostButton, joinButton;
    int scenSort = -1;
    bool scenDesc;
    readonly Dictionary<string, Image> thumbs = new();
    readonly System.Windows.Forms.Timer scenTimer = new() { Interval = 250 };

    Control BuildScenariosPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10) };
        scenList = new GridList { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        Theme.DoubleBuffer(scenList);
        foreach (var (t, w) in new[] { ("Scenario", 300), ("Players", 70), ("Mode", 104), ("From", 158), ("Status", 92) }) scenList.Columns.Add(t, w);
        scenList.Resize += (s, e) => Theme.FillColumn(scenList);
        Theme.GridStyle(scenList);
        scenList.Columns[1].TextAlign = HorizontalAlignment.Right;
        scenList.SelectedIndexChanged += (s, e) => Ui.Safe(UpdateScenarioDetails);
        scenList.DoubleClick += (s, e) => Ui.Safe(Play);
        scenList.ColumnClick += (s, e) =>
        {
            if (scenSort == e.Column) scenDesc = !scenDesc; else { scenSort = e.Column; scenDesc = e.Column == 1; }
            Ui.Safe(UpdateScenarioList);
        };

        // Details and actions on the right
        var details = new Panel { Dock = DockStyle.Right, Width = 380, Padding = new Padding(14, 0, 0, 0) };
        detImage = new PictureBox { Dock = DockStyle.Top, Height = 206, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.C("Panel") };
        detTitle = Ui.NewLabel("", 360); detTitle.Font = new Font("Segoe UI Semibold", 14f); detTitle.Margin = new Padding(0, 10, 0, 2);
        detMeta = Ui.NewLabel("", 360, "accent"); detMeta.Margin = new Padding(0, 2, 0, 6);
        detStatus = new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 8), Padding = new Padding(6, 2, 6, 2), Font = Theme.HeaderFont };
        var info = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        info.Controls.AddRange(new Control[] { detTitle, detMeta, detStatus });
        detDesc = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None };
        detDesc.HandleCreated += (s, e) => Native.DarkScroll(detDesc);

        playButton = Ui.NewButton("PLAY", Play, "primary");
        playButton.AutoSize = false; playButton.Size = new Size(360, 50); playButton.Font = new Font("Segoe UI Semibold", 14f);
        hostButton = Ui.NewButton("HOST", HostScenario);
        hostButton.AutoSize = false; hostButton.Size = new Size(177, 38); hostButton.Font = new Font("Segoe UI Semibold", 10.5f);
        joinButton = Ui.NewButton("JOIN", JoinScenario);
        joinButton.AutoSize = false; joinButton.Size = new Size(177, 38); joinButton.Font = hostButton.Font;
        Tips.Set(playButton, "This PC only: starts the scenario with this list's mods (a local server for multiplayer ones) and opens the game into it. " +
            "Works with unpublished mods too. Also a double-click on a scenario.");
        Tips.Set(hostButton, "A server for others. A server config: makes this its scenario (adding the mods it needs) and starts the server. " +
            "A game preset: makes a new server config with these mods and this scenario.");
        Tips.Set(joinButton, "Joins the server on this PC running this scenario, else opens the game's server browser.");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, 10, 0, 0) };
        actions.Controls.AddRange(new Control[] { playButton, hostButton, joinButton });
        details.Controls.Add(detDesc);
        details.Controls.Add(info);
        details.Controls.Add(detImage);
        details.Controls.Add(actions);

        // Filter bar
        scenFind = new TextBox { Width = 240, PlaceholderText = "Search scenarios" };
        scenTimer.Tick += (s, e) => { scenTimer.Stop(); Ui.Safe(UpdateScenarioList); };
        scenFind.TextChanged += (s, e) => { scenTimer.Stop(); scenTimer.Start(); };
        scenView = Ui.NewCombo(170, "All scenarios", "Base game", "In this mod list", "Other downloaded mods", "Multiplayer", "Single-player");
        scenView.SelectedIndexChanged += (s, e) => Ui.Safe(UpdateScenarioList);
        scenCount = Ui.NewLabel("", 0, "dim");
        Tips.Set(scenFind, "Words in the name, mode or mod.");
        Tips.Set(scenView, "In this mod list = scenarios of mods in the selected list; Other downloaded mods = PLAY / HOST add the mod.");
        var bar = Ui.NewFlow(DockStyle.Top, Ui.NewLabel("Find", 0, "dim"), scenFind, Ui.NewLabel("Show", 0, "dim"), scenView, scenCount);
        bar.Padding = new Padding(0, 0, 0, 8);

        page.Controls.Add(scenList);
        page.Controls.Add(details);
        page.Controls.Add(bar);
        return page;
    }

    void AfterStyleScenarios()
    {
        detDesc.BackColor = Theme.C("Back");
        detDesc.BorderStyle = BorderStyle.None;
    }

    public Scenario SelectedScenario => scenList.SelectedItems.Count > 0 ? scenList.SelectedItems[0].Tag as Scenario : null;

    static int ScenarioRank(Scenario s, ModList item)
    {
        if (s.ModGuid.Length > 0 && item != null && item.Mods.Contains(s.ModGuid)) return 0;
        return s.ModGuid.Length == 0 ? 1 : 2;
    }

    void UpdateScenarioList()
    {
        var item = CurrentItem;
        var current = item is ConfigItem c ? c.ScenarioId : "";
        var keep = SelectedScenario;
        var keepId = keep != null ? keep.Id : current;
        var words = scenFind.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int view = scenView.SelectedIndex;
        var rows = App.Scenarios.Select(s => (S: s, Rank: ScenarioRank(s, item))).Where(r =>
        {
            bool ok = view switch { 1 => r.Rank == 1, 2 => r.Rank == 0, 3 => r.Rank == 2, 4 => r.S.Players != 1, 5 => r.S.Players == 1, _ => true };
            foreach (var w in words) if (!$"{r.S.Name} {r.S.ModName} {r.S.Mode}".Contains(w, StringComparison.OrdinalIgnoreCase)) ok = false;
            return ok;
        });
        IOrderedEnumerable<(Scenario S, int Rank)> sorted = scenSort switch
        {
            0 => scenDesc ? rows.OrderByDescending(r => r.S.Name, StringComparer.CurrentCultureIgnoreCase) : rows.OrderBy(r => r.S.Name, StringComparer.CurrentCultureIgnoreCase),
            1 => (scenDesc ? rows.OrderByDescending(r => r.S.Players) : rows.OrderBy(r => r.S.Players)).ThenBy(r => r.S.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => (scenDesc ? rows.OrderByDescending(r => r.S.Mode, StringComparer.CurrentCultureIgnoreCase) : rows.OrderBy(r => r.S.Mode, StringComparer.CurrentCultureIgnoreCase)).ThenBy(r => r.S.Name, StringComparer.CurrentCultureIgnoreCase),
            3 => (scenDesc ? rows.OrderByDescending(r => r.S.ModName, StringComparer.CurrentCultureIgnoreCase) : rows.OrderBy(r => r.S.ModName, StringComparer.CurrentCultureIgnoreCase)).ThenBy(r => r.S.Name, StringComparer.CurrentCultureIgnoreCase),
            4 => (scenDesc ? rows.OrderByDescending(r => r.Rank) : rows.OrderBy(r => r.Rank)).ThenBy(r => r.S.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderBy(r => r.Rank).ThenBy(r => r.S.Order).ThenBy(r => r.S.ModName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.S.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var list = sorted.ToList();
        var items = list.Select(r =>
        {
            var s = r.S;
            var it = new ListViewItem((s.Id == current ? "★ " : "") + s.Name) { Tag = s };
            it.SubItems.Add(s.Players > 0 ? s.Players.ToString() : "");
            it.SubItems.Add(s.Mode);
            it.SubItems.Add(s.ModName);
            it.SubItems.Add(r.Rank switch { 0 => "In mod list", 1 => "Base game", _ => "Adds mod" });
            if (s.Id == current) it.ForeColor = Theme.C("Highlight"); else if (r.Rank == 2) it.ForeColor = Theme.C("Dim");
            return it;
        }).ToArray();
        scenList.BeginUpdate();
        scenList.Items.Clear();
        scenList.Items.AddRange(items);
        scenList.EndUpdate();
        scenCount.Text = $"{list.Count} of {App.Scenarios.Count} scenarios";
        ListViewItem select = null;
        if (!string.IsNullOrEmpty(keepId)) select = scenList.Items.Cast<ListViewItem>().FirstOrDefault(i => ((Scenario)i.Tag).Id == keepId);
        if (select == null && scenList.Items.Count > 0) select = scenList.Items[0];
        if (select != null) { select.Selected = true; select.Focused = true; select.EnsureVisible(); }
        UpdateScenarioDetails();
    }

    Image Thumbnail(Scenario s)
    {
        var key = s.Folder.Length > 0 ? s.Folder : "base|" + s.Mode;
        if (thumbs.TryGetValue(key, out var img)) return img;
        img = null;
        if (s.Folder.Length > 0 && Directory.Exists(s.Folder))
        {
            var dir = new DirectoryInfo(s.Folder);
            var file = dir.GetFiles("thumbnail.*").FirstOrDefault(f => Regex.IsMatch(f.Extension, @"^\.(png|jpe?g)$", RegexOptions.IgnoreCase))
                ?? dir.GetFiles("preview*.jpg").FirstOrDefault();
            if (file != null) { try { img = Image.FromStream(new MemoryStream(File.ReadAllBytes(file.FullName))); } catch { img = null; } }
        }
        img ??= Theme.Placeholder(s.ModGuid.Length > 0 ? s.ModName : "ARMA REFORGER", s.Mode);
        thumbs[key] = img;
        return img;
    }

    void UpdateScenarioDetails()
    {
        var s = SelectedScenario;
        var item = CurrentItem;
        foreach (var b in new[] { playButton, hostButton, joinButton }) b.Enabled = s != null;
        if (s == null)
        {
            detTitle.Text = "No scenario matches"; detMeta.Text = ""; detStatus.Visible = false; detDesc.Text = "";
            detImage.Image = null;
            return;
        }
        detImage.Image = Thumbnail(s);
        detTitle.Text = s.Name;
        var players = s.Players == 1 ? "1 player" : s.Players > 0 ? $"up to {s.Players} players" : "players not stated";
        const string dot = "  ·  ";
        detMeta.Text = (s.Mode.Length > 0 ? s.Mode + dot : "") + players + dot + s.ModName;
        // A small badge: whose scenario it is (the explanation in its tooltip).
        bool own = item is ConfigItem c && s.Id == c.ScenarioId;
        var (badge, color, tip) = ScenarioRank(s, item) switch
        {
            0 => ("IN THIS MOD LIST", "Good", "Its mod is in this mod list."),
            1 => ("BASE GAME", "Info", "Base game: it needs no mods (the mod list still loads)."),
            _ => ("ADDS ITS MOD", "Warn", "Its mod isn't in this mod list: PLAY, HOST and JOIN add it."),
        };
        detStatus.Text = (own ? "\u2605 THIS CONFIG'S SCENARIO   \u00B7   " : "") + badge;
        detStatus.BackColor = Theme.C(color);
        detStatus.ForeColor = Theme.C(color == "Info" ? "Text" : "AccentText");
        detStatus.Visible = true;
        Tips.Set(detStatus, tip + (own ? " It's this server config's scenario." : ""));
        Tips.Set(detTitle, $"{s.Name}\n{s.Id}");
        detDesc.Text = s.Description;
        detDesc.ScrollBars = s.Description.Length > 380 ? ScrollBars.Vertical : ScrollBars.None;
        detDesc.Visible = s.Description.Length > 0;
    }

    void Play()
    {
        var s = SelectedScenario;
        if (s == null) return;
        if (App.GameDir == null) { Ui.Message("The game (ArmaReforgerSteam.exe) wasn't found in your Steam libraries.", MessageBoxIcon.Error); return; }
        if (Game.Running().Count > 0) { Ui.Message("Arma Reforger is already running. Close it first: Play starts it with this mod list."); return; }
        var item = CurrentItem;
        Preset set; LocalPlan plan;
        SetBusy(true, $"Getting {s.Name} ready...");
        try { set = App.LaunchMods(item, s); plan = Launch.Plan(set); } finally { SetBusy(false); }
        if (plan.Missing.Count > 0)
        {
            Ui.Message($"These mods aren't on this PC, so {s.Name} can't start:\n\n" + string.Join("\n", plan.Missing.Select(g => $"{set.Mods[g]} ({g})")) +
                "\n\nDownload them in the game's Workshop, or untick them on the Mods tab.", MessageBoxIcon.Warning);
            return;
        }
        if (s.Players == 1)
        {
            // Single-player: the game runs the scenario itself.
            Launch.StartGame(new[] { "-server", s.Id }.Concat(Launch.ModArgs(plan.Dirs, plan.Addons)));
            Status.Text = $"Starting the game into {s.Name} ({set.Mods.Count} mods).";
            return;
        }
        if (App.ServerDir == null) { Ui.Message("ArmaReforgerServer.exe wasn't found (start-server.bat's SERVER_DIR or Steam), so multiplayer scenarios can't be played locally.", MessageBoxIcon.Error); return; }
        if (!ConfirmPortFree(2001)) return;
        string admin = ""; bool load = false;
        if (item is ConfigItem c)
        {
            admin = Json.Str(c.Config, "game.passwordAdmin");
            load = s.Id == c.ScenarioId && Json.Bool(c.Config, "game.gameProperties.persistence.loadSessionSave");
        }
        Launch.StartLocalServer(Launch.LocalServerArgs(s.Id, plan, admin, load));
        pendingJoin = new PendingJoin { Name = s.Name, Since = DateTime.Now };
        Status.Text = $"Starting {s.Name} on this PC with {set.Mods.Count} mods. The game opens by itself when it's ready (top right shows the progress).";
        UpdateServerStatus();
    }

    // HOST: a server config gets this scenario (and the mods it needs) and starts; a game preset becomes a new server config
    // with its mods and this scenario, opened on its Configuration tab.
    void HostScenario()
    {
        var s = SelectedScenario;
        if (s == null) return;
        var item = CurrentItem;
        if (item is ConfigItem c)
        {
            if (s.Id != c.ScenarioId)
            {
                if (!Ui.Ask($"Make {s.Name} the scenario of {c.Name} and start the server?")) return;
                Json.Section(c.Config, "game")["scenarioId"] = s.Id;
                int added = 0;
                foreach (var e in App.RequiredMods(s).Mods) if (!c.Mods.Contains(e.Key)) { c.Mods.Add(e.Key, e.Value); added++; }
                SetDirty(c);
                staleTabs.Add("Configuration"); staleTabs.Add("Mods");
                UpdateScenarioList();
                if (added > 0) Status.Text = $"{s.Name} set as the scenario; added {added} mod(s) it needs.";
            }
            StartServerFor(c);
            return;
        }
        var def = item != null ? item.Name : s.Name;
        var file = ReadConfigName($"HOST {s.Name}: name for the new server config with {(item != null ? $"the {item.Mods.Count} mods of '{item.Name}'" : "its mods")} (saved as config.<name>.json):", def, null);
        if (file == null) return;
        var config = AddServerConfig(file, item, "", s);
        UpdateTree(config);
        ShowSubTab("Configuration");
        Status.Text = $"Made {file} for {s.Name}. Check its settings, then START SERVER (top right).";
    }

    void JoinScenario()
    {
        var s = SelectedScenario;
        if (App.GameDir == null) { Ui.Message("The game (ArmaReforgerSteam.exe) wasn't found in your Steam libraries.", MessageBoxIcon.Error); return; }
        var procs = Launch.Servers();
        var local = procs.FirstOrDefault(p => p.Mode == "local" && (s == null || p.Scenario == s.Id));
        if (local != null)
        {
            if (Game.Running().Count > 0) { Ui.Message("Arma Reforger is already running. Close it first: joining starts it with the server's mods."); return; }
            if (!Launch.IsReady(local) && !Ui.Ask("The local server is still starting (top right says when it's ready). Start the game anyway? It retries until the server is up.")) return;
            Launch.StartGame(Launch.ClientArgsFor(local));
            Status.Text = "Game starting; it joins the server on this PC by itself (Steam must be running).";
            return;
        }
        var pub = procs.FirstOrDefault(p => p.Mode == "public");
        if (pub != null)
        {
            string serverName = "", scenarioId = "";
            try { var cfg = Json.Parse(File.ReadAllText(pub.Config)); serverName = Json.Str(cfg, "game.name"); scenarioId = Json.Str(cfg, "game.scenarioId"); } catch { }
            if (s != null && scenarioId.Length > 0 && scenarioId != s.Id && !Ui.Ask("Your public server is running a different scenario. Open the game to join it anyway?")) return;
            if (serverName.Length > 0) Ui.Copy(serverName);
            Launch.Open($"steam://rungameid/{Game.AppId}");
            Ui.Message($"Opening the game. Join your server from its server browser: search for \"{serverName}\" (copied to the clipboard).");
            return;
        }
        var what = s != null ? s.Name : "a scenario";
        if (Ui.Ask($"No server on this PC is running {what}. Open the game? Its server browser lists the public servers.")) Launch.Open($"steam://rungameid/{Game.AppId}");
    }
}
