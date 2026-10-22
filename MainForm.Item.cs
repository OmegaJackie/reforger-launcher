namespace ReforgerLauncher;

// The Server tab's right side: the selected server config or game preset with its name and state on top, its
// Configuration / Scenarios / Mods tabs (presets: Scenarios / Mods), and START SERVER / Save / Open file for configs. A
// folder or a group heading shows what's in it instead.
public partial class MainForm
{
    Panel itemPane, folderPane, subContent;
    Label itemTitle, itemSub, itemMissing, itemUnsaved;
    FlowLayoutPanel itemButtons, folderButtons;
    Label folderTitle, folderSub;
    GridList folderList;
    readonly Dictionary<string, Label> subTabs = new();
    readonly Dictionary<string, Control> subPages = new();
    public string SubTab = "";
    string configSubTab = "Configuration", presetSubTab = "Mods";
    readonly HashSet<string> staleTabs = new();

    public static readonly string[] SubTabNames = { "Configuration", "Scenarios", "Mods" };

    Control BuildItemPane()
    {
        var page = new Panel { Dock = DockStyle.Fill };

        // ---- A config or preset
        itemPane = new Panel { Dock = DockStyle.Fill };
        var header = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(18, 10, 16, 0) };
        itemTitle = new Label { AutoSize = true, Location = new Point(16, 8), Font = new Font("Segoe UI Semibold", 17f), Tag = "accent", AutoEllipsis = true };
        var subFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Location = new Point(16, 46) };
        itemSub = new Label { AutoSize = true, Margin = new Padding(3, 0, 10, 0), Tag = "dim" };
        itemMissing = new Label { AutoSize = true, Margin = new Padding(3, 0, 10, 0), Tag = "bad" };
        itemUnsaved = new Label { AutoSize = true, Margin = new Padding(3, 0, 10, 0), Text = "● unsaved changes" };
        subFlow.Controls.AddRange(new Control[] { itemSub, itemMissing, itemUnsaved });
        Tips.Set(itemMissing, "Mods in this list that aren't on this PC (red on the Mods tab). Download them in the game's Workshop.");
        Tips.Set(itemUnsaved, "Save (or Save all) writes them.");

        Button Big(string t, Action a, int w, string kind = "") { var b = Ui.NewButton(t, a, kind); b.AutoSize = false; b.Size = new Size(w, 38); b.Padding = new Padding(0); b.Margin = new Padding(8, 0, 0, 0); return b; }
        var start = Big("Start server", () => { if (CurrentItem is ConfigItem c) StartServerFor(c); }, 170, "primary");
        start.Font = new Font("Segoe UI Semibold", 10.5f);
        var save = Big("Save", SaveServerConfig, 96);
        var open = Big("Open file", OpenConfigFile, 110);
        Tips.Set(start, "Saves this config, then runs start-server.bat with it in its own window (it restarts the server if it exits). " +
            "The server downloads the mods from the Workshop; others need the public port forwarded to this PC (README.md).");
        Tips.Set(save, "Writes this config's file (Save all, bottom right, writes everything).");
        Tips.Set(open, "Opens the config file in Notepad.");
        itemButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        itemButtons.Controls.AddRange(new Control[] { open, save, start });
        header.Controls.Add(itemButtons);
        header.Controls.Add(subFlow);
        header.Controls.Add(itemTitle);
        header.Resize += (s, e) => itemTitle.MaximumSize = new Size(Math.Max(200, header.Width - itemButtons.Width - 40), 0);

        // The tabs under the name
        var tabBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, Padding = new Padding(14, 0, 0, 0) };
        foreach (var n in SubTabNames)
        {
            var name = n;
            var l = new Label { Text = n, AutoSize = true, Cursor = Cursors.Hand, Margin = new Padding(4, 2, 18, 0), Padding = new Padding(0, 0, 0, 7), Font = Theme.SubTabFont, Tag = "subtab" };
            l.Click += (s, e) => Ui.Safe(() => ShowSubTab(name));
            l.MouseEnter += (s, e) => { if (SubTab != name) l.ForeColor = Theme.C("Highlight"); };
            l.MouseLeave += (s, e) => { if (SubTab != name) l.ForeColor = Theme.C("TabOff"); };
            l.Paint += (s, e) => { if (SubTab == name) e.Graphics.FillRectangle(Theme.AccentBrush, 1, l.Height - 3, l.Width - 2, 3); };
            subTabs[n] = l;
            tabBar.Controls.Add(l);
        }
        Tips.Set(subTabs["Configuration"], "Every setting of this server config, as in its file.");
        Tips.Set(subTabs["Scenarios"], "Every scenario on this PC: PLAY it here, HOST it as this server, JOIN a running one.");
        Tips.Set(subTabs["Mods"], "Every mod on this PC; ticked = in this list.");

        subContent = new Panel { Dock = DockStyle.Fill };
        subPages["Configuration"] = BuildConfigPage();
        subPages["Scenarios"] = BuildScenariosPage();
        subPages["Mods"] = BuildModsPage();
        foreach (var p in subPages.Values) subContent.Controls.Add(p);

        itemPane.Controls.Add(subContent);
        itemPane.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, Tag = "line" });
        itemPane.Controls.Add(tabBar);
        itemPane.Controls.Add(header);

        // ---- A folder or a group heading: what's in it
        folderPane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 10, 16, 12), Visible = false };
        folderTitle = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 17f), Tag = "accent", Margin = new Padding(0, 0, 0, 2) };
        folderSub = new Label { AutoSize = true, Tag = "dim", Margin = new Padding(2, 0, 0, 10) };
        var titles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        titles.Controls.AddRange(new Control[] { folderTitle, folderSub });
        folderButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 12) };
        folderList = new GridList { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        Theme.DoubleBuffer(folderList);
        foreach (var (t, w) in new[] { ("Name", 220), ("Server name", 330), ("Scenario", 240), ("Mods", 70), ("Folder", 160) }) folderList.Columns.Add(t, w);
        folderList.Columns[3].TextAlign = HorizontalAlignment.Right;
        folderList.Resize += (s, e) => Theme.FillColumn(folderList, 1);
        Theme.GridStyle(folderList);
        folderList.DoubleClick += (s, e) => Ui.Safe(() =>
        {
            var tag = folderList.SelectedItems.Count > 0 ? folderList.SelectedItems[0].Tag : null;
            var node = FindNode(tag);
            if (node != null) Tree.SelectedNode = node;
        });
        Tips.Set(folderSub, "Double-click one below to open it. Yellow = some of its mods aren't on this PC.");
        folderPane.Controls.Add(folderList);
        folderPane.Controls.Add(folderButtons);
        folderPane.Controls.Add(titles);

        page.Controls.Add(itemPane);
        page.Controls.Add(folderPane);
        return page;
    }

    // Shows the selected config / preset / folder on the right.
    void ShowCurrentItem()
    {
        if (Page != "Server") return;
        var tag = Tree.SelectedNode?.Tag;
        bool isItem = tag is ModList;
        itemPane.Visible = isItem;
        folderPane.Visible = !isItem;
        if (!isItem) { UpdateFolderPane(tag); return; }
        bool isConfig = tag is ConfigItem;
        subTabs["Configuration"].Visible = isConfig;
        itemButtons.Visible = isConfig;
        foreach (var n in SubTabNames) staleTabs.Add(n);
        ShowSubTab(isConfig ? configSubTab : presetSubTab, false);
        UpdateItemHeader();
    }

    public void ShowSubTab(string name) => ShowSubTab(name, true);

    void ShowSubTab(string name, bool remember)
    {
        bool isConfig = CurrentItem is ConfigItem;
        if (name == "Configuration" && !isConfig) name = "Mods";
        if (remember) { if (isConfig) configSubTab = name; else presetSubTab = name; }
        SubTab = name;
        foreach (var (k, p) in subPages) p.Visible = k == name;
        foreach (var (k, l) in subTabs) { l.ForeColor = Theme.C(k == name ? "TabOn" : "TabOff"); l.Invalidate(); }
        // The settings grid is slow to lay out the first time: before the window is up it waits until it is (Shown).
        if (name == "Configuration" && !Visible) return;
        if (staleTabs.Remove(name))
            switch (name)
            {
                case "Configuration": UpdateServerPage(); break;
                case "Scenarios": UpdateScenarioList(); break;
                case "Mods": UpdateModList(); break;
            }
    }

    // A sub-tab's content changes now if it's showing, else when it's next opened.
    void RefreshSubTab(string subTab)
    {
        if (SubTab == subTab && subPages[subTab].Visible && Page == "Server") { staleTabs.Add(subTab); ShowSubTab(subTab, false); }
        else staleTabs.Add(subTab);
    }

    // The name, file, scenario, mods and state of the selected config / preset.
    void UpdateItemHeader()
    {
        if (itemTitle == null) return;
        var item = CurrentItem;
        if (item == null) return;
        const string dot = "   ·   ";
        var size = App.SizeOf(item);
        var mods = $"{item.Mods.Count} mods" + (size > 0 ? $" ({Mods.FormatSize(size)})" : "");
        if (item is ConfigItem c)
        {
            itemTitle.Text = c.ServerName.Length > 0 ? c.ServerName : DisplayName(c);
            var s = App.FindScenario(c.ScenarioId);
            var scen = s != null ? s.Name : c.ScenarioId.Length > 0 ? System.Text.RegularExpressions.Regex.Replace(c.ScenarioId, "^.*/", "") + " (not on this PC)" : "no scenario";
            var folder = FolderOf(c);
            itemSub.Text = (folder.Length > 0 ? folder.Replace("/", " › ") + " › " : "") + c.Name + dot + scen + dot + mods;
            Tips.Set(itemTitle, $"{c.ServerName}\n\nFile: {(string.IsNullOrEmpty(c.Path) ? c.Name + " (not saved yet)" : c.Path)}\nScenario: {c.ScenarioId}");
        }
        else
        {
            itemTitle.Text = item.Name;
            itemSub.Text = "Game mod preset" + dot + mods + (item.Name == App.Doc.UsedPreset ? dot + "the game's current preset" : "");
            Tips.Set(itemTitle, "A mod preset of the game's own mod manager. Save all writes it back to the game.");
        }
        int missing = App.MissingOf(item).Count;
        itemMissing.Text = missing > 0 ? $"{missing} not on this PC" : "";
        itemMissing.Visible = missing > 0;
        bool unsaved = item.Changed || (item is ConfigItem ci && string.IsNullOrEmpty(ci.Path));
        itemUnsaved.Visible = unsaved;
        itemUnsaved.ForeColor = Theme.C("Warn");
        Tips.Set(itemSub, item is ConfigItem cc ? $"{cc.Name}\n{cc.ScenarioId}" : item.Name);
    }

    // A folder or group heading: its contents and what can be made in it.
    void UpdateFolderPane(object tag)
    {
        folderButtons.Controls.Clear();
        Button B(string t, Action a, string tip, string kind = "")
        {
            var b = Ui.NewButton(t, a, kind);
            b.AutoSize = false; b.Size = new Size(t.Length > 14 ? 200 : 150, 34); b.Padding = new Padding(0); b.Margin = new Padding(0, 0, 8, 0);
            Tips.Set(b, tip);
            folderButtons.Controls.Add(b);
            return b;
        }
        var rows = new List<ListViewItem>();
        ListViewItem Row(ModList m, string a, string b, string folder)
        {
            var it = new ListViewItem(m is ConfigItem ci ? DisplayName(ci) : m.Name) { Tag = m };
            it.SubItems.Add(a); it.SubItems.Add(b); it.SubItems.Add(m.Mods.Count.ToString()); it.SubItems.Add(folder);
            if (App.MissingOf(m).Count > 0) it.ForeColor = Theme.C("Warn");
            return it;
        }
        string ScenName(ConfigItem c) => App.FindScenario(c.ScenarioId)?.Name ?? System.Text.RegularExpressions.Regex.Replace(c.ScenarioId, "^.*/", "");
        folderList.Columns[1].Text = "Server name"; folderList.Columns[2].Text = "Scenario"; folderList.Columns[4].Text = "Folder";
        if (tag is "presets")
        {
            folderTitle.Text = "Game Mod Presets";
            folderSub.Text = $"{App.Doc.Presets.Count} presets" + (App.Doc.UsedPreset.Length > 0 ? $"   ·   the game uses '{App.Doc.UsedPreset}'" : "");
            B("New game mod preset", NewPreset, "An empty game mod preset; tick its mods on its Mods tab.", "primary");
            folderList.Columns[1].Text = "Size"; folderList.Columns[2].Text = "Not on this PC"; folderList.Columns[4].Text = "";
            foreach (var p in App.Doc.Presets)
            {
                int miss = App.MissingOf(p).Count;
                rows.Add(Row(p, Mods.FormatSize(App.SizeOf(p)), miss > 0 ? miss.ToString() : "", p.Name == App.Doc.UsedPreset ? "in use" : ""));
            }
        }
        else
        {
            var folder = tag is ConfigFolder f ? f.Path : "";
            folderTitle.Text = folder.Length > 0 ? ((ConfigFolder)tag).Name : "Server Configs";
            var inside = App.Configs.Where(c => folder.Length == 0 || InFolder(FolderOf(c), folder))
                .OrderBy(c => FolderOf(c), StringComparer.CurrentCultureIgnoreCase).ThenBy(DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
            int subFolders = App.Data.ConfigFolders.Count(x => folder.Length == 0 ? true : x.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
            folderSub.Text = (folder.Length > 0 ? folder.Replace("/", " › ") + "   ·   " : "") + $"{inside.Count} server configs" + (subFolders > 0 ? $", {subFolders} folders" : "");
            B(folder.Length > 0 ? "New server config here" : "New server config", () => NewConfig(folder), "A server config with config.json's settings, in this folder.", "primary");
            B(folder.Length > 0 ? "New folder inside" : "New folder", () => NewFolder(folder), "A folder for server configs. Drag configs into it on the left.");
            if (tag is ConfigFolder)
            {
                B("Rename", Rename, "Renames the folder (configs stay in it).");
                B("Delete", Delete, "Deletes the folder; its configs and subfolders move up a level. No file is deleted.");
            }
            foreach (var c in inside) rows.Add(Row(c, c.ServerName, ScenName(c), FolderOf(c).Replace("/", " › ")));
        }
        Theme.Style(folderButtons);
        folderList.BeginUpdate();
        folderList.Items.Clear();
        folderList.Items.AddRange(rows.ToArray());
        folderList.EndUpdate();
    }
}
