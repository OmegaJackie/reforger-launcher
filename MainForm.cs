using System.Text.RegularExpressions;

namespace ReforgerLauncher;

// The launcher window: tabs across the top (History, Favorites, Server, Advanced) in A3Launcher's layout and Arma
// Reforger's colours. History / Favorites have the server filters on the left; Server has the Server Configs list (in
// folders, then the game's own mod presets) on the left and the selected one on the right with its Configuration /
// Scenarios / Mods tabs. Descriptions are tooltips. The footer has the status line and Save all.
public partial class MainForm : Form
{
    public static readonly string[] PageTabs = { "History", "Favorites", "Server", "Advanced" };

    public readonly App App;
    public readonly Launch Launch;
    public string Page = "";
    public bool Building;              // true while the code itself changes lists (their events are ignored)
    readonly Dictionary<string, Label> tabs = new();
    readonly Dictionary<string, Control> pages = new();
    Panel side, sideFilters, sideConfigs;
    public TreeView Tree;
    TreeNode dropNode;
    public Label Status;
    Label serverStatus, stopLink;
    bool serverRunning;
    CheckBox deps;
    ContextMenuStrip newMenu, copyMenu, exportMenu;
    readonly HashSet<string> collapsed = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Forms.Timer serverTimer = new() { Interval = 3000 };
    readonly System.Windows.Forms.Timer queryTimer = new() { Interval = 80 };
    PendingJoin pendingJoin;

    class PendingJoin { public string Name; public DateTime Since; }

    public MainForm(App app)
    {
        App = app;
        Launch = new Launch(app);
        Launch.AfterGameStart = () => { if (App.Data.Advanced.Minimize) WindowState = FormWindowState.Minimized; };
        App.Web.OnLookup = g => { Status.Text = $"Looking up {g} on the workshop..."; Application.DoEvents(); };
        Ui.Owner = this;
        Native.InitDarkMode();

        Size = new Size(1440, 900);
        MinimumSize = new Size(1150, 660);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Base;
        BackColor = Theme.C("Back");
        ForeColor = Theme.C("Text");
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        Native.WindowChrome(this);

        SuspendLayout();
        var tabsBar = BuildTabsBar();
        BuildSide();
        var sideLine = new Panel { Dock = DockStyle.Left, Width = 1, Tag = "line" };
        var footer = BuildFooter();

        var content = new Panel { Dock = DockStyle.Fill };
        pages["Servers"] = BuildServersPage();   // History and Favorites
        pages["Server"] = BuildItemPane();
        pages["Advanced"] = BuildAdvancedPage();
        foreach (var p in pages.Values) content.Controls.Add(p);

        Controls.Add(content);
        Controls.Add(sideLine);
        Controls.Add(side);
        Controls.Add(footer);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 2, Tag = "rule" });
        Controls.Add(tabsBar);
        Theme.Style(this);
        AfterStyleScenarios();
        ResumeLayout();

        serverTimer.Tick += (s, e) => { try { UpdateServerStatus(); } catch { } };
        queryTimer.Tick += (s, e) => { try { UpdateQueries(); } catch (Exception ex) { Status.Text = "Server query: " + ex.Message; } };
        Shown += (s, e) =>
        {
            serverTimer.Start();
            if (Page == "Server" && SubTab == "Configuration") BeginInvoke(() => Ui.Safe(() => ShowSubTab("Configuration", false)));
            if (App.BadConfigs.Count > 0) Ui.Message("These files were skipped:\n\n" + string.Join("\n", App.BadConfigs), MessageBoxIcon.Warning);
            if (Page is "History" or "Favorites" && !serversLoaded) { serversLoaded = true; Ui.Safe(RefreshServers); }
        };
        FormClosing += OnClosing;
        FormClosed += (s, e) => { serverTimer.Stop(); queryTimer.Stop(); };

        UpdateTree(null);
        ShowPage("Server");
        UpdateServerStatus();
        UpdateTitle();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Frame

    Panel BuildTabsBar()
    {
        // A3Launcher's tabs: big thin words across the top, the open one Reforger orange and underlined. The running server
        // and the header links sit on the right.
        var bar = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(10, 0, 14, 0), BackColor = Theme.C("Bar") };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        foreach (var p in PageTabs)
        {
            var name = p;
            var l = new Label { Text = p, AutoSize = true, Cursor = Cursors.Hand, Margin = new Padding(4, 6, 14, 0), Padding = new Padding(0, 0, 0, 8), Font = Theme.TabFont, Tag = "tab" };
            l.Click += (s, e) => Ui.Safe(() => ShowPage(name));
            l.MouseEnter += (s, e) => { if (Page != name) l.ForeColor = Theme.C("Highlight"); };
            l.MouseLeave += (s, e) => { if (Page != name) l.ForeColor = Theme.C("TabOff"); };
            l.Paint += (s, e) => { if (Page == name) e.Graphics.FillRectangle(Theme.AccentBrush, 2, l.Height - 4, l.Width - 4, 3); };
            tabs[p] = l;
            flow.Controls.Add(l);
        }
        serverStatus = new Label { AutoSize = true, Margin = new Padding(3, 21, 18, 3), Font = new Font("Segoe UI Semibold", 9.5f) };
        Label Link(string text, Action act, string tip)
        {
            var l = new Label { Text = text, AutoSize = true, Cursor = Cursors.Hand, Margin = new Padding(2, 22, 2, 3), Tag = "link" };
            l.Click += (s, e) => Ui.Safe(act);
            l.MouseEnter += (s, e) => { if (l != stopLink || serverRunning) l.ForeColor = Theme.C("LinkHover"); };
            l.MouseLeave += (s, e) => l.ForeColor = Theme.C(l != stopLink || serverRunning ? "Link" : "Dim");
            Tips.Set(l, tip);
            return l;
        }
        Label Bar() => new() { Text = "|", AutoSize = true, Margin = new Padding(2, 22, 2, 3), Tag = "dim" };
        stopLink = Link("stop server", StopServer, "Stops every dedicated server on this PC, including start-server.bat's restart loop.");
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        right.Controls.AddRange(new Control[] { serverStatus, stopLink, Bar(),
            Link("server log", OpenServerLog, "The running server's console.log (or the last local test's)."), Bar(),
            Link("open game", () => { if (App.GameDir != null) Launch.StartGame(); else Launch.Open($"steam://rungameid/{Game.AppId}"); }, "Starts Arma Reforger with the launch options from Advanced."), Bar(),
            Link("workshop", () => Launch.Open("https://reforger.armaplatform.com/workshop"), "reforger.armaplatform.com/workshop in your browser.") });
        bar.Controls.Add(right);
        bar.Controls.Add(flow);
        return bar;
    }

    void BuildSide()
    {
        side = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(12, 0, 8, 8), BackColor = Theme.C("Side") };
        sideFilters = BuildServerFilters();
        sideConfigs = new Panel { Dock = DockStyle.Fill };
        Tree = new TreeView
        {
            Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, ShowPlusMinus = false, ShowRootLines = false, FullRowSelect = true,
            ItemHeight = 26, Indent = 16, DrawMode = TreeViewDrawMode.OwnerDrawAll, AllowDrop = true, LabelEdit = false,
        };
        Tree.DrawNode += (s, e) =>
        {
            Theme.DrawNode(Tree, e);
            if (e.Node == dropNode && e.Bounds.Height > 0)
                using (var p = new Pen(Theme.C("Accent"), 2)) e.Graphics.DrawRectangle(p, 1, e.Bounds.Y + 1, Tree.ClientSize.Width - 3, e.Bounds.Height - 2);
        };
        Tree.AfterSelect += (s, e) => { if (!Building) Ui.Safe(ShowCurrentItem); };
        Tree.NodeMouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Right) { Tree.SelectedNode = e.Node; Ui.Safe(() => NodeMenu(e.Node).Show(Tree, e.Location)); return; }
            // The chevron of a folder opens / closes it.
            if (e.Node.Tag is ConfigFolder && e.X < 6 + e.Node.Level * Tree.Indent + 14) { if (e.Node.IsExpanded) e.Node.Collapse(); else e.Node.Expand(); }
        };
        Tree.NodeMouseHover += (s, e) => Tips.ShowAt(Tree, e.Node.ToolTipText, Tree.PointToClient(Cursor.Position));
        Tree.MouseLeave += (s, e) => Tips.ShowAt(Tree, null, Point.Empty);
        Tree.AfterCollapse += (s, e) => { if (e.Node.Tag is ConfigFolder f) collapsed.Add(f.Path); };
        Tree.AfterExpand += (s, e) => { if (e.Node.Tag is ConfigFolder f) collapsed.Remove(f.Path); };
        Tree.BeforeCollapse += (s, e) => { if (e.Node.Tag is string) e.Cancel = true; };   // the two group headings stay open
        Tree.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F2) { Ui.Safe(Rename); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { Ui.Safe(Delete); e.Handled = true; }
        };
        Tree.ItemDrag += (s, e) => { if (e.Item is TreeNode n && n.Tag is ConfigItem or ConfigFolder) Tree.DoDragDrop(n, DragDropEffects.Move); };
        Tree.DragOver += (s, e) =>
        {
            var dragged = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            var target = Tree.GetNodeAt(Tree.PointToClient(new Point(e.X, e.Y)));
            bool ok = dragged != null && DropFolder(dragged, target) != null;
            e.Effect = ok ? DragDropEffects.Move : DragDropEffects.None;
            SetDropNode(ok ? target : null);
        };
        Tree.DragLeave += (s, e) => SetDropNode(null);
        Tree.DragDrop += (s, e) =>
        {
            SetDropNode(null);
            var dragged = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            var target = Tree.GetNodeAt(Tree.PointToClient(new Point(e.X, e.Y)));
            var folder = dragged == null ? null : DropFolder(dragged, target);
            if (folder != null) Ui.Safe(() => MoveTo(dragged.Tag, folder));
        };
        newMenu = Ui.NewMenu(("Server config...", () => NewConfig()), ("Folder...", () => NewFolder(CurrentFolderPath)), ("Game mod preset...", NewPreset));
        copyMenu = Ui.NewMenu(("Server config...", () => CopyAs(true)), ("Game mod preset...", () => CopyAs(false)));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        Button newB = null, copyB = null;
        newB = Ui.NewButton("New...", () => newMenu.Show(newB, 0, newB.Height));
        copyB = Ui.NewButton("Copy as...", () => copyMenu.Show(copyB, 0, copyB.Height));
        var renameB = Ui.NewButton("Rename", Rename);
        var deleteB = Ui.NewButton("Delete", Delete);
        bar.Controls.AddRange(new Control[] { newB, copyB, renameB, deleteB });
        foreach (Control b in bar.Controls) { b.AutoSize = false; b.Size = new Size(134, 30); b.Padding = new Padding(0); b.Margin = new Padding(0, 0, 6, 6); }
        Tips.Set(newB, "A new server config (in the selected folder), a folder, or a game mod preset.");
        Tips.Set(copyB, "The selected config's or preset's mods as a new server config or game mod preset.");
        Tips.Set(renameB, "Renames the selected config (its file, on Save all), folder or preset. Also F2 or right-click.");
        Tips.Set(deleteB, "Deletes the selected config (to the Recycle Bin on Save all), preset, or folder (its configs move up).");
        sideConfigs.Controls.Add(Tree);
        sideConfigs.Controls.Add(bar);
        sideConfigs.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 });
        side.Controls.Add(sideConfigs);
        side.Controls.Add(sideFilters);
    }

    void SetDropNode(TreeNode n)
    {
        if (n == dropNode) return;
        dropNode = n;
        Tree.Invalidate();
    }

    Panel BuildFooter()
    {
        // Fixed-height buttons inside the padding, so none is cut off at the bottom.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(14, 10, 12, 10), BackColor = Theme.C("Bar") };
        var line = new Panel { Dock = DockStyle.Top, Height = 1, Tag = "line" };
        deps = new CheckBox { Text = "Add dependencies when saving", AutoSize = true, Checked = true, Margin = new Padding(3, 6, 14, 0) };
        Tips.Set(deps, "Save all, Save and START SERVER also add every mod the ticked mods need (from each mod's own info or its workshop page).");
        exportMenu = Ui.NewMenu(
            ("Save as mod list (.txt)...", () => Export("text")), ("Save as server config mods (.json)...", () => Export("json")),
            ("Copy server config mods to clipboard", () => Export("clipJson")), ("Copy mod list to clipboard", () => Export("clipText")));
        Button export = null;
        export = Ui.NewButton("Export...", () => exportMenu.Show(export, 0, export.Height));
        var reload = Ui.NewButton("Reload", () => Reload());
        var save = Ui.NewButton("Save all", () => SaveAll(), "primary");
        foreach (var b in new[] { export, reload, save }) { b.AutoSize = false; b.Size = new Size(b == save ? 120 : 100, 30); b.Padding = new Padding(0); b.Margin = new Padding(6, 0, 0, 0); }
        Tips.Set(export, "The selected config's or preset's mods as a list or a server config mods array.");
        Tips.Set(reload, "Reads presets, mods and configs from disk again (unsaved changes are lost).");
        Tips.Set(save, "Writes every changed server config and the game's mod presets. Close the game first: it rewrites its presets when it exits. " +
            "Every preset save is backed up to mod-presets-backups.");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
        buttons.Controls.AddRange(new Control[] { deps, export, reload, save });
        Status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Tag = "dim" };
        footer.Controls.Add(Status);
        footer.Controls.Add(buttons);
        footer.Controls.Add(line);
        return footer;
    }

    public void ShowPage(string name)
    {
        var panel = name is "History" or "Favorites" ? "Servers" : name;
        foreach (var (k, p) in pages) p.Visible = k == panel;
        Page = name;
        foreach (var (k, l) in tabs) { l.ForeColor = Theme.C(k == name ? "TabOn" : "TabOff"); l.Invalidate(); }
        side.Visible = panel != "Advanced";
        sideFilters.Visible = panel == "Servers";
        sideConfigs.Visible = panel == "Server";
        if (panel == "Server") ShowCurrentItem();
        if (panel == "Servers")
        {
            serverTab = name;
            UpdateServerList();
            // The first visit reads the game's logs and asks every server (once the window is up, so it never waits on it).
            if (!serversLoaded && Visible) { serversLoaded = true; RefreshServers(); }
        }
    }

    public void SetBusy(bool busy, string text = "")
    {
        UseWaitCursor = busy;
        Enabled = !busy;
        if (!string.IsNullOrEmpty(text)) Status.Text = text;
        Application.DoEvents();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Server configs (in folders) and game presets on the left

    public ModList CurrentItem => Tree.SelectedNode?.Tag as ModList;

    // The folder new configs go into: the selected folder, or the selected config's.
    string CurrentFolderPath => Tree.SelectedNode?.Tag switch
    {
        ConfigFolder f => f.Path,
        ConfigItem c => FolderOf(c),
        _ => "",
    };

    public string FolderOf(ConfigItem c) => App.Data.ConfigFolderOf.TryGetValue(c.Name, out var f) ? f : "";

    static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static bool InFolder(string path, string folder) => SamePath(path, folder) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(ConfigItem c)
    {
        var n = Regex.Replace(Regex.Replace(c.Name, @"^config\.?", ""), @"\.?json$", "");
        return n.Length == 0 ? "config" : n;
    }

    static string NodeText(ModList item) =>
        (item is ConfigItem c ? DisplayName(c) : item.Name) + $"  ({item.Mods.Count})" + (item.Changed ? " *" : "");

    TreeNode FindNode(object tag)
    {
        TreeNode Walk(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                if (ReferenceEquals(n.Tag, tag) || (tag is string s && n.Tag is string t && s == t)
                    || (tag is ConfigFolder f && n.Tag is ConfigFolder g && SamePath(g.Path, f.Path))) return n;
                var hit = Walk(n.Nodes);
                if (hit != null) return hit;
            }
            return null;
        }
        return tag == null ? null : Walk(Tree.Nodes);
    }

    void UpdateTitle() => Text = App.Title.ToUpperInvariant() + (App.Dirty ? " *" : "");

    // Marks an item changed and refreshes its line on the left without rebuilding the tree (which would steal focus).
    public void SetDirty(ModList item)
    {
        if (item != null)
        {
            item.Changed = true;
            var node = FindNode(item);
            if (node != null) node.Text = NodeText(item);
        }
        UpdateTitle();
        UpdateItemHeader();
    }

    // select: a ModList, a ConfigFolder, a group ("configs" / "presets"), or null (the first config).
    public void UpdateTree(object select)
    {
        Building = true;
        Tree.BeginUpdate();
        Tree.Nodes.Clear();
        var configsRoot = Tree.Nodes.Add("SERVER CONFIGS"); configsRoot.Tag = "configs";
        configsRoot.ToolTipText = "Your server configs: config*.json next to start-server.bat.\n\nRight-click for a new config or folder. Drag configs and " +
            "folders into folders. F2 renames, Delete deletes.";
        var folderNodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        TreeNode FolderNode(string path)
        {
            if (string.IsNullOrEmpty(path)) return configsRoot;
            if (folderNodes.TryGetValue(path, out var n)) return n;
            int cut = path.LastIndexOf('/');
            var parent = FolderNode(cut < 0 ? "" : path[..cut]);
            var folder = new ConfigFolder { Path = path };
            n = new TreeNode(folder.Name) { Tag = folder };
            parent.Nodes.Add(n);
            folderNodes[path] = n;
            return n;
        }
        // Folders first (sorted), then the configs in each.
        foreach (var c in App.Configs)
        {
            var folder = FolderOf(c);
            if (folder.Length > 0 && !App.Data.ConfigFolders.Any(x => SamePath(x, folder))) App.Data.ConfigFolders.Add(folder);
        }
        foreach (var f in App.Data.ConfigFolders.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)) FolderNode(f);
        foreach (var c in App.Configs.OrderBy(DisplayName, StringComparer.CurrentCultureIgnoreCase))
            FolderNode(FolderOf(c)).Nodes.Add(new TreeNode(NodeText(c)) { Tag = c, ToolTipText = $"{c.ServerName}\n{c.Name}" });
        foreach (var n in folderNodes.Values) n.ToolTipText = $"{n.Nodes.Count} item(s)";
        var presetsRoot = Tree.Nodes.Add("GAME MOD PRESETS"); presetsRoot.Tag = "presets";
        presetsRoot.ToolTipText = "The game's own mod presets (its mod manager's lists). Save all writes them back to the game.";
        foreach (var p in App.Doc.Presets)
            presetsRoot.Nodes.Add(new TreeNode(NodeText(p)) { Tag = p, ToolTipText = p.Name == App.Doc.UsedPreset ? "Game mod preset - the one the game uses now" : "Game mod preset" });
        Tree.ExpandAll();
        foreach (var (path, node) in folderNodes) if (collapsed.Contains(path)) node.Collapse();
        Tree.EndUpdate();
        Building = false;
        var sel = select != null ? FindNode(select) : null;
        sel ??= FirstConfig(configsRoot) ?? presetsRoot.FirstNode ?? configsRoot;
        if (sel.Parent != null && !sel.Parent.IsExpanded) sel.Parent.Expand();
        Tree.SelectedNode = sel;
        sel.EnsureVisible();
        UpdateTitle();
    }

    static TreeNode FirstConfig(TreeNode root)
    {
        foreach (TreeNode n in root.Nodes) if (n.Tag is ConfigItem) return n;
        foreach (TreeNode n in root.Nodes)
        {
            var inner = n.Tag is ConfigFolder ? FirstConfig(n) : null;
            if (inner != null) return inner;
        }
        return null;
    }

    // The right-click menu of a line on the left.
    ContextMenuStrip NodeMenu(TreeNode node)
    {
        var entries = new List<(string, Action)>();
        switch (node.Tag)
        {
            case "configs":
                entries.Add(("New server config...", () => NewConfig("")));
                entries.Add(("New folder...", () => NewFolder("")));
                break;
            case "presets":
                entries.Add(("New game mod preset...", NewPreset));
                break;
            case ConfigFolder f:
                entries.Add(("New server config here...", () => NewConfig(f.Path)));
                entries.Add(("New folder inside...", () => NewFolder(f.Path)));
                entries.Add(("Rename...", Rename));
                entries.Add(("Delete folder", Delete));
                break;
            case ConfigItem c:
                entries.Add(("Rename...", Rename));
                entries.Add(("Copy as server config...", () => CopyAs(true)));
                entries.Add(("Copy as game mod preset...", () => CopyAs(false)));
                entries.Add(("Start server", () => StartServerFor(c)));
                entries.Add(("Open file", OpenConfigFile));
                entries.Add(("Show in folder", () => { if (File.Exists(c.Path)) System.Diagnostics.Process.Start("explorer.exe", "/select," + Native.Quote(c.Path)); else Ui.Message($"{c.Name} isn't saved yet."); }));
                entries.Add(("Delete", Delete));
                break;
            case Preset:
                entries.Add(("Rename...", Rename));
                entries.Add(("Copy as server config...", () => CopyAs(true)));
                entries.Add(("Copy as game mod preset...", () => CopyAs(false)));
                entries.Add(("Delete", Delete));
                break;
        }
        var menu = Ui.NewMenu(entries.ToArray());
        if (node.Tag is ConfigItem or ConfigFolder)
        {
            // Move to: the top level and every folder it may go in.
            var move = new ToolStripMenuItem("Move to folder");
            foreach (var t in new[] { "" }.Concat(App.Data.ConfigFolders.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)))
            {
                var target = t;
                var item = move.DropDownItems.Add(target.Length == 0 ? "(top level)" : target.Replace("/", "  ›  "), null, (s, e) => Ui.Safe(() => MoveTo(node.Tag, target)));
                item.Enabled = DropFolderFor(node.Tag, target) != null;
            }
            menu.Items.Insert(node.Tag is ConfigItem ? 3 : 2, move);
        }
        Theme.StyleMenu(menu);
        return menu;
    }

    // Where a dragged config / folder would land: a folder or the configs heading ("" = the top).
    string DropFolder(TreeNode dragged, TreeNode target)
    {
        if (target == null) return null;
        string dest = target.Tag switch
        {
            ConfigFolder f => f.Path,
            "configs" => "",
            ConfigItem c => FolderOf(c),
            _ => null,
        };
        return dest == null ? null : DropFolderFor(dragged.Tag, dest);
    }

    // dest, if what may move there: never a folder into itself or its own subfolders, never where it already is.
    string DropFolderFor(object what, string dest)
    {
        if (what is ConfigFolder src)
        {
            if (InFolder(dest, src.Path) || SamePath(dest, src.Parent)) return null;
            var name = dest.Length == 0 ? src.Name : dest + "/" + src.Name;
            if (App.Data.ConfigFolders.Any(x => SamePath(x, name))) return null;
            return dest;
        }
        if (what is ConfigItem ci) return SamePath(dest, FolderOf(ci)) ? null : dest;
        return null;
    }

    // Moves a config or a folder (with everything in it) into a folder ("" = the top).
    public void MoveTo(object what, string folder)
    {
        if (DropFolderFor(what, folder) == null) return;
        if (what is ConfigItem c)
        {
            if (folder.Length == 0) App.Data.ConfigFolderOf.Remove(c.Name); else App.Data.ConfigFolderOf[c.Name] = folder;
            SaveData();
            UpdateTree(c);
            Status.Text = $"Moved {c.Name} to {(folder.Length == 0 ? "the top level" : folder)}.";
        }
        else if (what is ConfigFolder f)
        {
            var newPath = folder.Length == 0 ? f.Name : folder + "/" + f.Name;
            RepathFolder(f.Path, newPath);
            UpdateTree(new ConfigFolder { Path = newPath });
            Status.Text = $"Moved the folder {f.Name} to {(folder.Length == 0 ? "the top level" : folder)}.";
        }
    }

    // A folder's path changes (rename / move): its subfolders and the configs in them follow.
    void RepathFolder(string oldPath, string newPath)
    {
        string Map(string p) => InFolder(p, oldPath) ? newPath + p[oldPath.Length..] : p;
        App.Data.ConfigFolders = App.Data.ConfigFolders.Select(Map).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var k in App.Data.ConfigFolderOf.Keys.ToList()) App.Data.ConfigFolderOf[k] = Map(App.Data.ConfigFolderOf[k]);
        foreach (var p in collapsed.ToList()) { var m = Map(p); if (m != p) { collapsed.Remove(p); collapsed.Add(m); } }
        SaveData();
    }

    static readonly Regex BadFolderChars = new(@"[/\\]");

    string ReadFolderName(string prompt, string def, string parent, string except)
    {
        while (true)
        {
            var name = Ui.Input(prompt, def).Trim();
            if (name.Length == 0) return null;
            if (BadFolderChars.IsMatch(name)) { Ui.Message("A folder name can't contain / or \\.", MessageBoxIcon.Warning); def = name; continue; }
            var path = parent.Length == 0 ? name : parent + "/" + name;
            if (!SamePath(path, except) && App.Data.ConfigFolders.Any(x => SamePath(x, path))) { Ui.Message($"There's already a folder {path}.", MessageBoxIcon.Warning); def = name; continue; }
            return path;
        }
    }

    public void NewFolder(string parent)
    {
        var path = ReadFolderName(parent.Length == 0 ? "Name for the new folder:" : $"Name for the new folder inside {parent}:", "New folder", parent, null);
        if (path == null) return;
        App.Data.ConfigFolders.Add(path);
        SaveData();
        UpdateTree(new ConfigFolder { Path = path });
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Running server (top right): its name and address

    void UpdateServerStatus()
    {
        var procs = Launch.Servers();
        // "stop server" is dimmed while nothing runs (a disabled label is near invisible on the dark).
        serverRunning = procs.Count > 0;
        stopLink.ForeColor = Theme.C(serverRunning ? "Link" : "Dim");
        stopLink.Cursor = serverRunning ? Cursors.Hand : Cursors.Default;
        if (pendingJoin != null)
        {
            var local = procs.FirstOrDefault(p => p.Mode == "local");
            if (local == null)
            {
                if ((DateTime.Now - pendingJoin.Since).TotalSeconds > 20)
                {
                    Status.Text = "The local server stopped before it was ready. Server log shows why.";
                    pendingJoin = null;
                }
            }
            else if (Launch.IsReady(local))
            {
                var name = pendingJoin.Name;
                pendingJoin = null;
                if (Game.Running().Count > 0) Status.Text = $"{name} is ready, but the game is already running: close it and click JOIN.";
                else { Launch.StartGame(Launch.ClientArgsFor(local)); Status.Text = $"{name} is ready - starting the game into it."; }
            }
        }
        if (procs.Count == 0)
        {
            serverStatus.Text = "No server running";
            serverStatus.ForeColor = Theme.C("Dim");
            Tips.Set(serverStatus, "START SERVER on a server config, or PLAY a multiplayer scenario, starts one.");
            return;
        }
        var p0 = procs[0];
        var info = Launch.Info(p0);
        var address = Launch.AddressOf(p0);
        int minutes = (int)(DateTime.Now - p0.Started).TotalMinutes;
        var more = procs.Count > 1 ? $"   (+{procs.Count - 1})" : "";
        var name0 = info.Name.Length > 46 ? info.Name[..45] + "…" : info.Name;
        serverStatus.Text = $"●  {name0}   {address}{(info.Ready ? "" : "   starting...")}{more}";
        serverStatus.ForeColor = Theme.C(info.Ready ? "Good" : "Warn");
        var tip = new List<string> { info.Name, "", $"Address: {address}" };
        if (info.JoinCode.Length > 0) tip.Add($"Direct join code: {info.JoinCode}");
        var lan = Launch.LanAddress();
        if (lan.Length > 0) tip.Add($"On this network: {lan}:{(info.Port > 0 ? info.Port : 2001)}");
        tip.Add(p0.Mode switch { "public" => $"Config: {Path.GetFileName(p0.Config)}", "local" => "Local test (PLAY): this PC only", _ => "" });
        tip.Add($"{(info.Ready ? "Ready" : "Starting")} - running {minutes} min (process {p0.Id})");
        if (procs.Count > 1) tip.Add($"{procs.Count - 1} more server(s) running");
        Tips.Set(serverStatus, string.Join("\n", tip.Where(x => x != null)));
    }

    bool ConfirmNoServer()
    {
        if (Launch.Servers().Count == 0) return true;
        if (!Ui.Ask("A server is already running (only one can use port 2001). Stop it and continue?")) return false;
        Launch.StopServers();
        Thread.Sleep(2000);
        return true;
    }

    void StopServer()
    {
        if (Launch.Servers().Count == 0) return;
        if (!Ui.Ask("Stop the running server? Anyone on it is disconnected.")) return;
        pendingJoin = null;
        Launch.StopServers();
        Thread.Sleep(800);
        UpdateServerStatus();
    }

    void OpenServerLog()
    {
        var proc = Launch.Servers().FirstOrDefault();
        var log = Launch.ServerLog(proc) ?? Launch.ServerLog(new ServerProc { Profile = Path.Combine(App.Root, "profile-test") });
        if (log == null) { Ui.Message("No server log found yet."); return; }
        Launch.Open(log.FullName);
    }

    // Starts the dedicated server with a server config through start-server.bat (saving the config first if needed).
    public void StartServerFor(ConfigItem item)
    {
        if (string.IsNullOrEmpty(item.ScenarioId)) { Ui.Message("Pick a scenario for this config first (its Scenarios tab)."); return; }
        if (App.ServerDir == null) { Ui.Message("ArmaReforgerServer.exe wasn't found (start-server.bat's SERVER_DIR or Steam).", MessageBoxIcon.Error); return; }
        var bat = Path.Combine(App.Root, "start-server.bat");
        if (!File.Exists(bat)) { Ui.Message($"start-server.bat is missing from {App.Root}.", MessageBoxIcon.Error); return; }
        // The server downloads every mod from the Workshop: unpublished ones make it fail.
        List<string> notOnWorkshop;
        SetBusy(true, "Checking the mods are on the Workshop...");
        try
        {
            notOnWorkshop = item.Mods.Where(e => !App.Installed.ContainsKey(e.Key) && !App.ServerMods.ContainsKey(e.Key) && App.Web.Lookup(e.Key) == null)
                .Select(e => $"{e.Value} ({e.Key})").ToList();
        }
        finally { SetBusy(false); }
        if (notOnWorkshop.Count > 0 && !Ui.Ask("These mods aren't on the Workshop (unpublished, private or offline check), so the server can't download them and will fail to start:\n\n" +
            string.Join("\n", notOnWorkshop) + "\n\nUse PLAY for unpublished mods. Start the server anyway?")) return;
        if (item.Changed || string.IsNullOrEmpty(item.Path))
        {
            if (deps.Checked) App.AddDependencies(item);
            App.SaveConfig(item);
            var node = FindNode(item);
            if (node != null) node.Text = NodeText(item);
            UpdateTitle();
            UpdateItemHeader();
        }
        if (!ConfirmNoServer()) return;
        // start-server.bat uses SERVER_DIR when it doesn't set its own (the shipped one doesn't): the server found here.
        Environment.SetEnvironmentVariable("SERVER_DIR", App.ServerDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(bat, item.Name) { WorkingDirectory = App.Root, UseShellExecute = true });
        Status.Text = $"Started start-server.bat {item.Name} in its own window (it restarts the server if it exits).";
        var how = Json.Str(item.Config, "publicAddress") == "local" ? "Host LAN is on, so only this network can join."
            : $"Friends need UDP {Json.Str(item.Config, "publicPort")} forwarded to this PC - see README.md.";
        Ui.Message($"Server starting with {item.Name}. Its console window shows the log; the top right shows its name and address when it's up.\n\n" +
            $"Join it from the game's server browser (search for \"{item.ServerName}\") or JOIN on its Scenarios tab. {how}");
        UpdateServerStatus();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Making, copying, renaming and deleting presets, configs and folders

    string ReadPresetName(string prompt, string def, ModList except)
    {
        while (true)
        {
            var n = GameSettings.SafeName(Ui.Input(prompt, def));
            if (n.Length == 0) return null;
            var other = App.Doc.Find(n);
            if (other == null || ReferenceEquals(other, except)) return n;
            Ui.Message($"There's already a game preset named '{n}'.", MessageBoxIcon.Warning);
            def = n;
        }
    }

    string ReadConfigName(string prompt, string def, ConfigItem except)
    {
        while (true)
        {
            var text = Ui.Input(prompt, def);
            if (text.Length == 0) return null;
            var file = App.ConfigFileName(text);
            if (file == null) continue;
            var other = App.Configs.FirstOrDefault(c => c.Name == file);
            bool onDisk = File.Exists(Path.Combine(App.Root, file));
            bool ownFile = except != null && !string.IsNullOrEmpty(except.Path) && Path.GetFileName(except.Path) == file;
            if ((other == null || ReferenceEquals(other, except)) && (!onDisk || ownFile)) return file;
            Ui.Message($"{file} already exists in {App.Root}.", MessageBoxIcon.Warning);
            def = text;
        }
    }

    public Preset AddGamePreset(string name, ModList source)
    {
        var p = new Preset(name);
        if (source != null) foreach (var e in source.Mods) p.Mods.Add(e.Key, e.Value);
        p.Changed = true;
        App.Doc.Presets.Add(p);
        App.PresetsDirty = true;
        return p;
    }

    // A new server config in a folder: the source config's settings (else config.json's), the source's mods, and the given
    // scenario (else one from the mods, else Conflict - Everon) with the mods it needs.
    public ConfigItem AddServerConfig(string file, ModList source, string folder, Scenario scenario = null)
    {
        var cfg = source is ConfigItem sc ? Json.Clone(sc.Config) : App.NewConfigSettings();
        var item = App.NewConfigItem(file, cfg, "");
        // The mods come from the source as it is now (a copied config file may predate unsaved ticks).
        item.Mods.Clear();
        if (source != null)
        {
            foreach (var e in source.Mods) item.Mods.Add(e.Key, e.Value);
            if (source is ConfigItem src) foreach (var (k, v) in src.Versions) item.Versions[k] = v;
        }
        if (scenario != null || source is not ConfigItem)
        {
            var pick = scenario ?? App.Scenarios.FirstOrDefault(s => s.ModGuid.Length > 0 && item.Mods.Contains(s.ModGuid));
            Json.Section(cfg, "game")["scenarioId"] = pick?.Id ?? App.DefaultScenarioId;
            if (pick != null) foreach (var e in App.RequiredMods(pick).Mods) item.Mods.Add(e.Key, e.Value);
        }
        if (source is not ConfigItem)
        {
            var baseName = Json.Str(App.NewConfigSettings(), "game.name");
            var what = source?.Name ?? DisplayName(item);
            var name = baseName.Length > 0 ? $"{baseName} - {what}" : what;
            Json.Section(cfg, "game")["name"] = name.Length > 100 ? name[..100] : name;
        }
        item.Changed = true;
        App.Configs.Add(item);
        if (!string.IsNullOrEmpty(folder)) { App.Data.ConfigFolderOf[item.Name] = folder; SaveData(); }
        return item;
    }

    public void NewConfig(string folder = null)
    {
        folder ??= CurrentFolderPath;
        var where = folder.Length > 0 ? $" in {folder}" : "";
        var file = ReadConfigName($"Name for the new server config{where} (saved as config.<name>.json):", "server", null);
        if (file == null) return;
        var item = AddServerConfig(file, null, folder);
        UpdateTree(item);
        ShowPage("Server");
        ShowSubTab("Configuration");
        Status.Text = $"Made {file} from config.json's settings - Save all writes it.";
    }

    void NewPreset()
    {
        var n = ReadPresetName("Name for the new game mod preset:", $"Preset {App.Doc.Presets.Count + 1}", null);
        if (n == null) return;
        UpdateTree(AddGamePreset(n, null));
        ShowPage("Server");
        ShowSubTab("Mods");
    }

    void CopyAs(bool config)
    {
        var src = CurrentItem;
        if (src == null) { Ui.Message("Pick a server config or game preset to copy first."); return; }
        if (!config)
        {
            var def = src is ConfigItem c ? c.ServerName : $"{src.Name} copy";
            var n = ReadPresetName($"Name for the new game mod preset (with the {src.Mods.Count} mods of '{src.Name}'):", def, null);
            if (n != null) UpdateTree(AddGamePreset(n, src));
        }
        else
        {
            var def = src is ConfigItem ci ? DisplayName(ci) + "-copy" : src.Name;
            var file = ReadConfigName($"Name for the new server config with the {src.Mods.Count} mods of '{src.Name}' (saved as config.<name>.json):", def, null);
            if (file == null) return;
            UpdateTree(AddServerConfig(file, src, src is ConfigItem cc ? FolderOf(cc) : CurrentFolderPath));
            ShowSubTab("Configuration");
        }
    }

    public void Rename()
    {
        switch (Tree.SelectedNode?.Tag)
        {
            case Preset p:
            {
                var n = ReadPresetName($"New name for '{p.Name}':", p.Name, p);
                if (n == null || n == p.Name) return;
                if (App.Doc.UsedPreset == p.Name) App.Doc.UsedPreset = n;
                p.Name = n;
                App.PresetsDirty = true;
                SetDirty(p);
                UpdateTree(p);
                break;
            }
            case ConfigItem c:
            {
                if (c.Name == "config.json" && !Ui.Ask("config.json is the config start-server.bat uses when you give it none. Rename it anyway?")) return;
                var file = ReadConfigName("New name (saved as config.<name>.json):", DisplayName(c), c);
                if (file == null || file == c.Name) return;
                if (App.Data.ConfigFolderOf.Remove(c.Name, out var folder)) { App.Data.ConfigFolderOf[file] = folder; SaveData(); }
                c.Name = file;
                SetDirty(c);
                UpdateTree(c);
                Status.Text = $"Renamed to {file} - Save all renames the file.";
                break;
            }
            case ConfigFolder f:
            {
                var newPath = ReadFolderName($"New name for the folder {f.Path}:", f.Name, f.Parent, f.Path);
                if (newPath == null || newPath == f.Path) return;
                RepathFolder(f.Path, newPath);
                UpdateTree(new ConfigFolder { Path = newPath });
                break;
            }
        }
    }

    public void Delete()
    {
        switch (Tree.SelectedNode?.Tag)
        {
            case Preset p:
                if (!Ui.Ask($"Delete the game preset '{p.Name}'? (Nothing changes until you Save all.)")) return;
                App.Doc.Presets.Remove(p);
                if (App.Doc.UsedPreset == p.Name) App.Doc.UsedPreset = "";
                App.PresetsDirty = true;
                UpdateTree("presets");
                break;
            case ConfigItem c:
            {
                var extra = c.Name == "config.json" ? "\n\nconfig.json is the config start-server.bat uses when you give it none." : "";
                if (!Ui.Ask($"Delete the server config {c.Name}? It goes to the Recycle Bin when you Save all.{extra}")) return;
                var folder = FolderOf(c);
                App.Configs.Remove(c);
                App.Data.ConfigFolderOf.Remove(c.Name);
                SaveData();
                if (!string.IsNullOrEmpty(c.Path)) App.DeletedConfigs.Add(c.Path);
                UpdateTree(folder.Length > 0 ? new ConfigFolder { Path = folder } : "configs");
                break;
            }
            case ConfigFolder f:
            {
                int inside = App.Configs.Count(c => InFolder(FolderOf(c), f.Path));
                var parent = f.Parent;
                var up = parent.Length == 0 ? "the top level" : parent;
                if (!Ui.Ask($"Delete the folder {f.Path}?" + (inside > 0 ? $"\n\nIts {inside} config(s) and subfolders move up to {up}; no file is deleted." : ""))) return;
                // Everything inside moves up one level.
                string Up(string p) => SamePath(p, f.Path) ? parent : InFolder(p, f.Path) ? (parent.Length == 0 ? "" : parent + "/") + p[(f.Path.Length + 1)..] : p;
                App.Data.ConfigFolders = App.Data.ConfigFolders.Where(x => !SamePath(x, f.Path)).Select(Up)
                    .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var k in App.Data.ConfigFolderOf.Keys.ToList())
                {
                    var m = Up(App.Data.ConfigFolderOf[k]);
                    if (m.Length == 0) App.Data.ConfigFolderOf.Remove(k); else App.Data.ConfigFolderOf[k] = m;
                }
                SaveData();
                UpdateTree(parent.Length > 0 ? new ConfigFolder { Path = parent } : "configs");
                break;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Saving, reloading, exporting

    // Returns true when everything was saved.
    public bool SaveAll()
    {
        var changed = App.AllItems().Where(i => i.Changed).ToList();
        var lines = new List<string>();
        bool all = true;
        SetBusy(true, "Checking dependencies...");
        try
        {
            if (deps.Checked)
                foreach (var item in changed)
                {
                    var r = App.AddDependencies(item);
                    if (r.Added.Count > 0) lines.Add($"Added dependencies to '{item.Name}': " + string.Join(", ", r.Added.Select(g => item.Mods[g])));
                }
            SetBusy(true, "Saving...");
            foreach (var item in changed.OfType<ConfigItem>()) { App.SaveConfig(item); lines.Add($"Saved {item.Name}."); }
            foreach (var path in App.DeletedConfigs.ToList())
                if (File.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    lines.Add($"Moved {Path.GetFileName(path)} to the Recycle Bin.");
                }
            App.DeletedConfigs.Clear();
            if (App.PresetsDirty || App.Doc.Presets.Any(p => p.Changed))
            {
                bool save = true;
                if (Game.Running().Count > 0)
                {
                    lines.Add("Game presets NOT saved: Arma Reforger is running. Close it, then Save all again.");
                    save = false; all = false;
                }
                else if (App.Doc.ChangedOnDisk())
                {
                    SetBusy(false);
                    if (Ui.Message("The game changed its presets since this window loaded them. Overwrite them with the ones here?\n\n(No leaves the game's presets alone; Reload picks up its version.)",
                        MessageBoxIcon.Warning, MessageBoxButtons.YesNo) != DialogResult.Yes)
                    { save = false; all = false; lines.Add("Game presets NOT saved."); }
                }
                if (save)
                {
                    var backup = App.Doc.Save(App.BackupDir, true);
                    App.PresetsDirty = false;
                    lines.Add($"Saved the game presets (backup: {backup}).");
                }
            }
        }
        finally { SetBusy(false); }

        var missingLines = changed.Select(item =>
        {
            var m = App.MissingOf(item);
            return m.Count == 0 ? null : $"'{item.Name}': " + string.Join(", ", m.Take(12).Select(g => item.Mods[g])) + (m.Count > 12 ? ", ..." : "");
        }).Where(s => s != null).ToList();
        if (missingLines.Count > 0) lines.Add("\nNot on this PC (download them in the game's Workshop):\n" + string.Join("\n", missingLines));
        UpdateTree(Tree.SelectedNode?.Tag);
        if (lines.Count == 0) lines.Add("Nothing to save.");
        Ui.Message(string.Join("\n", lines));
        return all;
    }

    void Reload(bool force = false)
    {
        if (!force && App.Dirty && !Ui.Ask("Discard your unsaved changes and reload everything from disk?")) return;
        var tag = Tree.SelectedNode?.Tag;
        var key = tag is ModList m ? $"{m.IsConfig}|{m.Name}" : "";
        SetBusy(true, "Reading mods and configs...");
        try { App.Load(); } finally { SetBusy(false); }
        object select = tag is ModList ? App.AllItems().FirstOrDefault(i => $"{i.IsConfig}|{i.Name}" == key) : tag;
        UpdateTree(select);
        if (pages["Servers"].Visible) UpdateServerList();
    }

    void Export(string how)
    {
        var item = CurrentItem;
        if (item == null) { Ui.Message("Pick a server config or game preset first."); return; }
        switch (how)
        {
            case "clipJson": Clipboard.SetText(Mods.ServerModsJson(item)); Status.Text = $"Copied {item.Mods.Count} mods as a server config mods array."; break;
            case "clipText": Clipboard.SetText(Mods.ListText(item)); Status.Text = $"Copied {item.Mods.Count} mods as a list."; break;
            default:
                using (var sfd = new SaveFileDialog { InitialDirectory = App.Root, FileName = Regex.Replace(Regex.Replace(item.Name, @"\.json$", ""), "[\\\\/:*?\"<>|]", "_") })
                {
                    string text;
                    if (how == "json") { sfd.Filter = "Server config mods array (*.json)|*.json"; text = Mods.ServerModsJson(item); }
                    else { sfd.Filter = "Mod list (*.txt)|*.txt"; text = Mods.ListText(item); }
                    if (sfd.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(sfd.FileName, text, new System.Text.UTF8Encoding(false));
                    Status.Text = $"Wrote {item.Mods.Count} mods to {sfd.FileName}";
                }
                break;
        }
    }

    void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (!App.Dirty) return;
        var answer = Ui.Message("Save your changes before closing?", MessageBoxIcon.Question, MessageBoxButtons.YesNoCancel);
        if (answer == DialogResult.Cancel) e.Cancel = true;
        else if (answer == DialogResult.Yes)
        {
            bool saved = false;
            try { saved = SaveAll(); } catch (Exception ex) { SetBusy(false); Ui.Message(ex.Message, MessageBoxIcon.Error); }
            if (!saved) e.Cancel = true;
        }
    }
}
