namespace ReforgerLauncher;

// HISTORY / FAVORITES (one grid, two views) with A3Launcher's filters on the left. History is every server the game
// joined (read from its logs, with the mods it loaded for each); Favorites the starred ones and ones added by address.
// Live state comes from A2S queries running on worker threads.
public partial class MainForm
{
    public string serverTab = "History";
    public bool serversLoaded;
    public readonly Dictionary<string, A2SInfo> Live = new();
    readonly Dictionary<string, (ServerRecord Rec, Task<A2SInfo> Task)> queries = new();
    bool refreshAll;
    (int Row, int Col) hintCell = (-1, -1);
    public ListView SrvList;
    Label srvCount;
    TextBox fltName;
    CheckBox fltFull, fltEmpty, fltPassword, fltOffline;
    ComboBox fltScenario;
    readonly System.Windows.Forms.Timer fltTimer = new() { Interval = 250 };

    static readonly Dictionary<int, string> ActionHints = new()
    {
        [0] = "Favorite: starred servers are on the Favorites tab.", [2] = "Password protected.", [6] = "Ask this server again (name, scenario, players, ping).",
        [7] = "Who's playing on it.", [8] = "JOIN: starts the game straight onto this server with the mods it loaded there last time.",
    };

    Control BuildServersPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 14, 8) };
        SrvList = new GridList { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        Theme.DoubleBuffer(SrvList);
        foreach (var (t, w) in new[] { ("Fav", 44), ("Name", 420), ("", 30), ("Map", 240), ("Players", 84), ("Ping", 60), ("", 36), ("", 36), ("", 40) }) SrvList.Columns.Add(t, w);
        SrvList.Columns[4].TextAlign = HorizontalAlignment.Right;
        SrvList.Columns[5].TextAlign = HorizontalAlignment.Right;
        Theme.GridStyle(SrvList, 42, false);
        SrvList.DrawSubItem += (s, e) => DrawServerCell(e);
        SrvList.Resize += (s, e) => Theme.FillColumn(SrvList, 1);
        SrvList.MouseClick += (s, e) => Ui.Safe(() => ServerListClick(e));
        SrvList.DoubleClick += (s, e) => Ui.Safe(() => JoinServer(SelectedServer));
        SrvList.MouseMove += (s, e) =>
        {
            var hit = SrvList.HitTest(e.Location);
            int col = hit.Item != null ? hit.Item.SubItems.IndexOf(hit.SubItem) : -1;
            int row = hit.Item?.Index ?? -1;
            SrvList.Cursor = col is 0 or 6 or 7 or 8 ? Cursors.Hand : Cursors.Default;
            if ((row, col) == hintCell) return;
            hintCell = (row, col);
            string hint = null;
            if (row >= 0 && ActionHints.TryGetValue(col, out var h) && App.Data.Record((string)SrvList.Items[row].Tag) is { } rec)
            {
                hint = h;
                Live.TryGetValue(rec.Addr, out var live);
                if (col == 2 && !((live != null && live.Password) || (live == null && rec.Password))) hint = null;
                if (col == 8 && rec.ModsKnown && rec.Mods.Any(m => !string.IsNullOrEmpty(m) && !App.Installed.ContainsKey(m)))
                    hint = "Some of its mods aren't downloaded: JOIN opens the game to download them (Direct connect; the address is copied).";
            }
            Tips.ShowAt(SrvList, hint, e.Location);
        };
        SrvList.MouseLeave += (s, e) => { hintCell = (-1, -1); Tips.ShowAt(SrvList, null, Point.Empty); };
        SrvList.ContextMenuStrip = Ui.NewMenu(
            ("Join", () => JoinServer(SelectedServer)),
            ("Refresh", () => { if (SelectedServer is { } r) StartQuery(r); }),
            ("Players...", () => { if (SelectedServer is { } r) ShowPlayers(r); }),
            ("Mods...", () => { if (SelectedServer is { } r) ShowServerMods(r); }),
            ("Make a mod list from its mods...", () => { if (SelectedServer is { } r) PresetFromServer(r); }),
            ("Favorite on / off", () => { if (SelectedServer is { } r) SetFavourite(r, !r.Fav); }),
            ("Copy address", () => { if (SelectedServer is { } r) Ui.Copy(r.Addr); }),
            ("Remove from the list", () => { if (SelectedServer is { } r) { r.Hidden = true; r.Fav = false; SaveData(); UpdateServerList(); } }));
        srvCount = Ui.NewLabel("", 0, "dim"); srvCount.Margin = new Padding(3, 4, 3, 6);
        page.Controls.Add(SrvList);
        page.Controls.Add(Ui.NewFlow(DockStyle.Top, srvCount));
        return page;
    }

    // A3Launcher's left panel on the server tabs: filters, then the buttons.
    Panel BuildServerFilters()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var heading = Ui.NewLabel("Filters"); heading.Font = Theme.Heading; heading.Margin = new Padding(2, 10, 3, 10);
        fltTimer.Tick += (s, e) => { fltTimer.Stop(); Ui.Safe(UpdateServerList); };
        fltName = new TextBox { Width = 262, PlaceholderText = "Name, address or scenario" };
        fltName.TextChanged += (s, e) => { fltTimer.Stop(); fltTimer.Start(); };
        var hide = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        CheckBox Check(string n)
        {
            var cb = new CheckBox { Text = n, AutoSize = true, Margin = new Padding(3, 3, 40, 3) };
            cb.CheckedChanged += (s, e) => { if (!Building) Ui.Safe(UpdateServerList); };
            hide.Controls.Add(cb);
            return cb;
        }
        fltFull = Check("Full"); fltEmpty = Check("Empty"); fltPassword = Check("Password"); fltOffline = Check("Offline");
        Tips.Set(fltFull, "Hides servers with no free slot."); Tips.Set(fltEmpty, "Hides servers nobody is on.");
        Tips.Set(fltPassword, "Hides password-protected servers."); Tips.Set(fltOffline, "Hides servers that didn't answer.");
        fltScenario = Ui.NewCombo(262, "All scenarios");
        fltScenario.MaxDropDownItems = 20;
        fltScenario.SelectedIndexChanged += (s, e) => { if (!Building) Ui.Safe(UpdateServerList); };
        Label Caption(string t) { var l = Ui.NewLabel(t, 0, "label"); l.Margin = new Padding(2, 8, 3, 3); return l; }
        Button Wide(string t, Action a, string kind = "") { var b = Ui.NewButton(t, a, kind); b.AutoSize = false; b.Size = new Size(262, 30); b.Margin = new Padding(3, 4, 3, 4); return b; }
        var refresh = Wide("Refresh", RefreshServers);
        var clear = Wide("Clear", () =>
        {
            Building = true;
            fltName.Text = ""; fltFull.Checked = fltEmpty.Checked = fltPassword.Checked = fltOffline.Checked = false; fltScenario.SelectedIndex = 0;
            Building = false;
            UpdateServerList();
        });
        var add = Wide("Add server...", AddServer);
        var join = Wide("Join", () => JoinServer(SelectedServer), "primary");
        Tips.Set(refresh, "Reads the game's logs for servers you joined since, and asks every server for its name, players and ping.");
        Tips.Set(clear, "Clears the filters.");
        Tips.Set(add, "A server by its address (IP:port, as in the game's Direct connect), added to Favorites.");
        Tips.Set(join, "Starts the game straight onto the selected server with the mods it loaded there last time.");
        flow.Controls.AddRange(new Control[] { heading, Caption("Name"), fltName, Caption("Hide"), hide, Caption("Scenario"), fltScenario, Ui.NewLabel(""), refresh, clear, add, join });
        panel.Controls.Add(flow);
        return panel;
    }

    public ServerRecord SelectedServer => SrvList.SelectedItems.Count > 0 ? App.Data.Record((string)SrvList.SelectedItems[0].Tag) : null;

    void SaveData() { var err = App.Data.Save(); if (err != null) Status.Text = err; }

    List<ServerRecord> ServerView()
    {
        var words = fltName.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scenario = fltScenario.SelectedIndex > 0 ? (string)fltScenario.SelectedItem : "";
        var rows = App.Data.Servers.Values.Where(rec =>
        {
            if (rec.Hidden) return false;
            if (serverTab == "Favorites" && !rec.Fav) return false;
            if (serverTab == "History" && rec.LastJoined.Length == 0) return false;
            Live.TryGetValue(rec.Addr, out var live);
            foreach (var w in words) if (!$"{rec.Name} {rec.Addr} {rec.Scenario}".Contains(w, StringComparison.OrdinalIgnoreCase)) return false;
            if (scenario.Length > 0 && rec.Scenario != scenario) return false;
            if (live != null && live.Online)
            {
                if (fltFull.Checked && live.Max > 0 && live.Players >= live.Max) return false;
                if (fltEmpty.Checked && live.Players == 0) return false;
            }
            if (fltOffline.Checked && live != null && !live.Online) return false;
            if (fltPassword.Checked && ((live != null && live.Password) || rec.Password)) return false;
            return true;
        });
        if (serverTab == "History") return rows.OrderByDescending(r => r.LastJoined, StringComparer.Ordinal).ToList();
        return rows.OrderByDescending(r => r.Fav).ThenByDescending(r => Live.TryGetValue(r.Addr, out var l) && l.Online ? l.Players : -1)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void UpdateServerList()
    {
        var keep = SrvList.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToHashSet();
        var rows = ServerView();
        SrvList.BeginUpdate();
        SrvList.Items.Clear();
        foreach (var rec in rows)
        {
            var it = new ListViewItem(rec.Title) { Tag = rec.Addr };
            for (int i = 1; i < SrvList.Columns.Count; i++) it.SubItems.Add("");
            SrvList.Items.Add(it);
            if (keep.Contains(rec.Addr)) it.Selected = true;
        }
        SrvList.EndUpdate();
        // The Scenario filter offers the scenarios of the known servers.
        var current = fltScenario.SelectedItem as string;
        Building = true;
        fltScenario.BeginUpdate();
        fltScenario.Items.Clear();
        fltScenario.Items.Add("All scenarios");
        foreach (var n in App.Data.Servers.Values.Select(s => s.Scenario).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            fltScenario.Items.Add(n);
        fltScenario.SelectedIndex = Math.Max(0, current == null ? 0 : fltScenario.Items.IndexOf(current));
        fltScenario.EndUpdate();
        Building = false;
        srvCount.Text = rows.Count == 0 ? (serverTab == "Favorites" ? "No favorite servers yet." : "No servers joined yet.")
            : serverTab == "Favorites" ? $"{rows.Count} favorite servers" : $"{rows.Count} servers joined, newest first";
        Tips.Set(srvCount, serverTab == "Favorites"
            ? "Starred servers and ones added by address. The star on a row in History adds it here."
            : "Every server the game joined, read from its logs (it keeps the last ~10 sessions) with the mods it loaded for each, and JOINs from here.");
        if (rows.Count == 0)
            Tips.Set(srvCount, serverTab == "Favorites" ? "Star a server in History, or ADD SERVER by its address." : "Join a server from the game's own server browser: it shows up here with its mods.");
    }

    void UpdateServerRow(string addr)
    {
        foreach (ListViewItem it in SrvList.Items)
            if ((string)it.Tag == addr)
            {
                if (Live.TryGetValue(addr, out var l) && l.Name.Length > 0) it.Text = l.Name;
                SrvList.Invalidate(it.Bounds);
                break;
            }
    }

    public void RefreshServers()
    {
        int found;
        SetBusy(true, "Reading the game's logs...");
        try { found = App.Data.UpdateHistory(Path.Combine(App.DocsDir, "logs")); } finally { SetBusy(false); }
        SaveData();
        UpdateServerList();
        refreshAll = true;
        var recs = SrvList.Items.Cast<ListViewItem>().Select(i => App.Data.Record((string)i.Tag)).Where(r => r != null).ToList();
        foreach (var r in recs) StartQuery(r);
        Status.Text = $"Asking {recs.Count} servers...{(found > 0 ? $"   ({found} new joins found in the game's logs)" : "")}";
        if (recs.Count == 0) refreshAll = false;
    }

    public void StartQuery(ServerRecord rec)
    {
        if (rec == null || queries.ContainsKey(rec.Addr)) return;
        queries[rec.Addr] = (rec, A2S.InfoAsync(LauncherStore.HostOf(rec), LauncherStore.QueryPorts(rec), 1500));
        queryTimer.Start();
        UpdateServerRow(rec.Addr);
    }

    public bool QueriesPending => queries.Count > 0;

    // The timer collecting answers.
    public void UpdateQueries()
    {
        foreach (var (addr, q) in queries.ToList())
        {
            if (!q.Task.IsCompleted) continue;
            queries.Remove(addr);
            var r = q.Task.Status == TaskStatus.RanToCompletion ? q.Task.Result : null;
            var rec = q.Rec;
            if (r != null && r.Online)
            {
                Live[addr] = r;
                rec.QueryPort = r.Port;
                if (r.Name.Length > 0) rec.Name = r.Name;
                if (r.Map.Length > 0) rec.Scenario = r.Map;
                rec.Password = r.Password;
            }
            else Live[addr] = new A2SInfo { Online = false };
            UpdateServerRow(addr);
        }
        if (queries.Count == 0)
        {
            queryTimer.Stop();
            SaveData();
            if (refreshAll)
            {
                refreshAll = false;
                UpdateServerList();
                Status.Text = $"Refreshed: {Live.Values.Count(l => l.Online)} of {Live.Count} servers answered.";
            }
        }
    }

    void AddServer()
    {
        var text = Ui.Input("Server address - IP:port (the game port; 2001 if left out), as in the game's Direct connect:", "");
        if (text.Length == 0) return;
        text = System.Text.RegularExpressions.Regex.Replace(text.Trim(), "^.*://", "");
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Za-z0-9.\-]+(:\d{1,5})?$")) { Ui.Message($"'{text}' isn't an address like 1.2.3.4:2001.", MessageBoxIcon.Warning); return; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @":\d+$")) text += ":2001";
        var rec = App.Data.Record(text, true);
        rec.Added = true; rec.Hidden = false; rec.Fav = true;
        SaveData();
        if (Page != "Favorites") ShowPage("Favorites"); else UpdateServerList();
        StartQuery(rec);
        Status.Text = $"Added {text} to Favorites - asking it for its name and players...";
    }

    public void SetFavourite(ServerRecord rec, bool on)
    {
        rec.Fav = on;
        if (on) rec.Hidden = false;
        SaveData();
        if (serverTab == "Favorites") UpdateServerList(); else UpdateServerRow(rec.Addr);
    }

    void ShowPlayers(ServerRecord rec)
    {
        List<A2SPlayer> players;
        SetBusy(true, $"Asking {rec.Addr} for its players...");
        try { players = A2S.Players(LauncherStore.HostOf(rec), LauncherStore.QueryPorts(rec)[0], 1500); } finally { SetBusy(false); }
        if (players == null) { Ui.Message($"{rec.Title} didn't send its player list."); return; }
        if (players.Count == 0) { Ui.Message($"Nobody is on {rec.Title} right now."); return; }
        Ui.Message($"{players.Count} players on {rec.Title}\n\n" + string.Join("\n", players.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => p.Name + (p.Minutes > 0 ? $"   ({p.Minutes} min)" : ""))));
    }

    void ShowServerMods(ServerRecord rec)
    {
        if (!rec.ModsKnown) { Ui.Message($"{rec.Title}'s mods aren't known yet: join it once from the game's server browser."); return; }
        var mods = rec.Mods.Where(m => !string.IsNullOrEmpty(m)).ToList();
        if (mods.Count == 0) { Ui.Message($"{rec.Title} runs without mods."); return; }
        var lines = mods.Select(g =>
        {
            var name = App.Installed.TryGetValue(g, out var i) ? i.Name : rec.ModNames.GetValueOrDefault(g) ?? g;
            return (Name: name, Line: (App.Installed.ContainsKey(g) ? "✓" : "✗") + "  " + name);
        }).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => x.Line);
        Ui.Message($"{mods.Count} mods on {rec.Title} (✗ = not downloaded)\n\n" + string.Join("\n", lines));
    }

    // The server's mods as a new game preset (also usable for PLAY / HOST and the in-game mod manager).
    void PresetFromServer(ServerRecord rec)
    {
        if (!rec.ModsKnown || rec.Mods.Count == 0) { Ui.Message("No known mods on this server to make a mod list from."); return; }
        var def = rec.Name.Length > 0 ? System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(rec.Name, @"[^\w \-]+", " "), @"\s+", " ").Trim() : rec.Addr;
        if (def.Length > 40) def = def[..40].Trim();
        var n = ReadPresetName("Name for the new game mod preset:", def, null);
        if (n == null) return;
        var src = new Preset("server");
        foreach (var g in rec.Mods) src.Mods.Add(g, App.Installed.TryGetValue(g, out var i) ? i.Name : rec.ModNames.GetValueOrDefault(g) ?? "");
        UpdateTree(AddGamePreset(n, src));
        ShowPage("Server");
        ShowSubTab("Mods");
        Status.Text = $"Made the preset '{n}' from {rec.Title}'s {rec.Mods.Count} mods - Save all writes it to the game.";
    }

    void ServerListClick(MouseEventArgs e)
    {
        var hit = SrvList.HitTest(e.Location);
        if (hit.Item == null || e.Button != MouseButtons.Left) return;
        var rec = App.Data.Record((string)hit.Item.Tag);
        if (rec == null) return;
        switch (hit.Item.SubItems.IndexOf(hit.SubItem))
        {
            case 0: SetFavourite(rec, !rec.Fav); break;
            case 6: StartQuery(rec); break;
            case 7: ShowPlayers(rec); break;
            case 8: JoinServer(rec); break;
        }
    }

    // JOIN: the game straight onto the server (-client -addr) with the mods it loaded for it last time.
    public void JoinServer(ServerRecord rec)
    {
        if (rec == null) return;
        if (App.GameDir == null && Launch.StartGameHook == null) { Ui.Message("The game (ArmaReforgerSteam.exe) wasn't found in your Steam libraries.", MessageBoxIcon.Error); return; }
        if (Launch.StartGameHook == null && Game.Running().Count > 0) { Ui.Message("Arma Reforger is already running. Close it first: JOIN starts it with the server's mods."); return; }
        var name = rec.Title;
        Live.TryGetValue(rec.Addr, out var live);
        if (live != null && !live.Online && !Ui.Ask($"{name} didn't answer (offline, or its query port is closed). Join anyway?")) return;
        var mods = rec.Mods.Where(m => !string.IsNullOrEmpty(m)).ToList();
        if (!rec.ModsKnown)
        {
            var item = CurrentItem;
            var with = item != null ? $"the mod list '{item.Name}' ({item.Mods.Count} mods)" : "no mods";
            var answer = Ui.Message($"The launcher doesn't know {name}'s mods yet: it learns them once you've joined it from the game's own server browser.\n\n" +
                $"Yes - join now with {with} (selected on the Server tab)\nNo - open the game and join it there (Direct connect; the address is copied)",
                MessageBoxIcon.Question, MessageBoxButtons.YesNoCancel);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.No) { Ui.Copy(rec.Addr); if (Launch.StartGame(null, t => Status.Text = t)) Status.Text = $"Game starting - Direct connect to {rec.Addr} (copied)."; return; }
            mods = item != null ? item.Mods.Keys.ToList() : new List<string>();
        }
        var missing = mods.Where(g => !App.Installed.ContainsKey(g)).ToList();
        if (missing.Count > 0)
        {
            var names = string.Join("\n", missing.Take(15).Select(g => rec.ModNames.TryGetValue(g, out var n) && !string.IsNullOrEmpty(n) ? $"{n} ({g})" : g));
            var more = missing.Count > 15 ? $"\n... and {missing.Count - 15} more" : "";
            if (Ui.Ask($"{missing.Count} of {name}'s mods aren't downloaded:\n\n{names}{more}\n\nThe game downloads them when you join from its server browser. Open the game now? (The address is copied for Direct connect.)"))
            {
                Ui.Copy(rec.Addr);
                if (!Launch.StartGame(null, t => Status.Text = t)) return;
                Status.Text = $"Game starting - Direct connect to {rec.Addr} (copied).";
            }
            return;
        }
        var args = new List<string> { "-client", "-addr", rec.Addr };
        if (mods.Count > 0) args.AddRange(new[] { "-addons", string.Join(",", mods) });
        if (!Launch.StartGame(args, t => Status.Text = t)) return;
        rec.Times = new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm") }.Concat(rec.Times.Where(t => !string.IsNullOrEmpty(t))).Take(20).ToList();
        rec.Hidden = false;
        SaveData();
        var pw = (live != null && live.Password) || rec.Password ? " It has a password: if the game doesn't ask for it, join from its server browser instead." : "";
        Status.Text = $"Game starting, joining {name} with {mods.Count} mods.{pw}";
    }

    // One cell of the server grid, drawn like A3Launcher's: star, name over address and mods, lock, scenario, players, ping,
    // and the refresh / players / join buttons.
    void DrawServerCell(DrawListViewSubItemEventArgs e)
    {
        var g = e.Graphics; var b = e.Bounds;
        var addr = (string)e.Item.Tag;
        var rec = App.Data.Record(addr);
        if (rec == null) return;
        Live.TryGetValue(addr, out var live);
        bool sel = e.Item.Selected;
        g.FillRectangle(sel ? Theme.SelectBrush : Theme.BackBrush, b);
        if (sel && e.ColumnIndex == 0) g.FillRectangle(Theme.AccentBrush, b.X, b.Y, 3, b.Height);
        const TextFormatFlags center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        const TextFormatFlags left = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        const TextFormatFlags right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var text = Theme.C("Text");
        void Glyph(string which, Color c) => TextRenderer.DrawText(g, Theme.Glyphs[which], Theme.GlyphFont, b, c, center);
        switch (e.ColumnIndex)
        {
            case 0: Glyph(rec.Fav ? "StarOn" : "Star", rec.Fav ? Theme.C("Accent") : Theme.C("Label")); break;
            case 1:
            {
                var top = new Rectangle(b.X + 4, b.Y + 3, b.Width - 8, b.Height / 2 - 1);
                var bottom = new Rectangle(b.X + 4, b.Y + b.Height / 2, b.Width - 8, b.Height / 2 - 3);
                var name = live != null && live.Name.Length > 0 ? live.Name : rec.Title;
                TextRenderer.DrawText(g, name, Theme.NameFont, top, sel ? Theme.C("Highlight") : rec.Fav ? Theme.C("FavName") : text, left);
                var n = rec.Mods.Count(m => !string.IsNullOrEmpty(m));
                var bits = new List<string> { rec.Addr, rec.ModsKnown ? (n > 0 ? $"{n} mods" : "no mods") : "mods not known yet" };
                if (live != null && live.Version.Length > 0) bits.Add("v" + live.Version);
                if (rec.LastJoined.Length > 0) bits.Add("joined " + rec.LastJoined);
                TextRenderer.DrawText(g, string.Join("  |  ", bits), Theme.SmallFont, bottom, sel ? Theme.C("Label") : Theme.C("SubText"), left);
                break;
            }
            case 2: if ((live != null && live.Password) || (live == null && rec.Password)) Glyph("Lock", Theme.C("Warn")); break;
            case 3:
            {
                var s = live != null && live.Map.Length > 0 ? live.Map : rec.Scenario;
                TextRenderer.DrawText(g, s, SrvList.Font, new Rectangle(b.X + 4, b.Y, b.Width - 8, b.Height), text, left);
                break;
            }
            case 4:
            {
                var t = live == null ? "" : !live.Online ? "offline" : $"{live.Players}/{live.Max}";
                var c = live != null && !live.Online ? Theme.C("Dim") : live != null && live.Max > 0 && live.Players >= live.Max ? Theme.C("Warn") : text;
                TextRenderer.DrawText(g, t, SrvList.Font, new Rectangle(b.X, b.Y, b.Width - 10, b.Height), c, right);
                break;
            }
            case 5:
                if (live != null && live.Online)
                {
                    var c = live.Ping < 80 ? Theme.C("Good") : live.Ping < 150 ? Theme.C("Warn") : live.Ping < 250 ? Theme.C("Accent") : Theme.C("Bad");
                    TextRenderer.DrawText(g, live.Ping.ToString(), SrvList.Font, new Rectangle(b.X, b.Y, b.Width - 10, b.Height), c, right);
                }
                break;
            case 6: Glyph(queries.ContainsKey(addr) ? "Busy" : "Refresh", Theme.C("Label")); break;
            case 7: Glyph("People", Theme.C("Label")); break;
            case 8:
            {
                // Play when every known mod is downloaded, download when some aren't (A3Launcher's two buttons).
                bool missing = rec.ModsKnown && rec.Mods.Any(m => !string.IsNullOrEmpty(m) && !App.Installed.ContainsKey(m));
                if (missing) Glyph("Download", Theme.C("Accent"));
                else Glyph("Play", rec.ModsKnown ? Theme.C("Good") : Theme.C("Dim"));
                break;
            }
        }
    }
}
