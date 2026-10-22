using System.Text.RegularExpressions;

namespace ReforgerLauncher;

// The Mods tab of a config / preset: every mod on this PC with search, size, version and last-played date; ticked = in
// the selected list.
public partial class MainForm
{
    GridList modList;
    TextBox modSearch;
    ComboBox modView;
    Label modCount;
    int sortColumn;
    bool sortDescending;
    int shownCount;
    readonly System.Windows.Forms.Timer searchTimer = new() { Interval = 250 };

    class ModRow
    {
        public string Guid, Name, Version, Location;
        public long Size;
        public bool Installed, Enabled;
        public DateTime? LastPlayed;
    }

    Control BuildModsPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10) };
        modList = new GridList { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, CheckColumn = true };
        modList.UseRows();
        Theme.DoubleBuffer(modList);
        foreach (var (t, w) in new[] { ("Name", 380), ("Version", 85), ("Size", 75), ("Last played", 92), ("GUID", 140), ("Status", 180) }) modList.Columns.Add(t, w);
        modList.Resize += (s, e) => Theme.FillColumn(modList);
        Theme.GridStyle(modList);
        modList.CheckToggled += i => Ui.Safe(() => ToggleMod(i));
        modList.ColumnClick += (s, e) =>
        {
            if (sortColumn == e.Column) sortDescending = !sortDescending; else { sortColumn = e.Column; sortDescending = e.Column is 2 or 3; }
            Ui.Safe(UpdateModList);
        };
        modList.ContextMenuStrip = Ui.NewMenu(
            ("Open workshop page", () => { foreach (var it in modList.SelectedRows.Take(10)) Launch.Open(Game.WorkshopUrl + it.Tag); }),
            ("Copy GUIDs", () => { var g = modList.SelectedRows.Select(i => (string)i.Tag).ToList(); if (g.Count > 0) Clipboard.SetText(string.Join("\r\n", g)); }),
            ("Open mod folder", () =>
            {
                var first = modList.SelectedRows.FirstOrDefault();
                if (first == null) return;
                var g = (string)first.Tag;
                var info = App.Installed.GetValueOrDefault(g) ?? App.ExtraMods.GetValueOrDefault(g);
                if (info != null && info.Folder.Length > 0) Launch.Open(info.Folder);
            }));

        modSearch = new TextBox { Width = 240, PlaceholderText = "Search mods" };
        searchTimer.Tick += (s, e) => { searchTimer.Stop(); Ui.Safe(UpdateModList); };
        modSearch.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };
        modView = Ui.NewCombo(160, "All mods", "Ticked", "Not ticked", "Enabled in game now", "Not on this PC");
        modView.SelectedIndexChanged += (s, e) => Ui.Safe(UpdateModList);
        var addB = Ui.NewButton("Add from list...", ShowAddListDialog);
        var tickB = Ui.NewButton("Tick all shown", () => SetShownChecked(true));
        var untickB = Ui.NewButton("Untick all shown", () => SetShownChecked(false));
        modCount = Ui.NewLabel("", 0, "dim");
        Tips.Set(modSearch, "Words in the name or GUID.");
        Tips.Set(modCount, "Click a tick box (or Space) to put a mod in or out of this list. Red = not on this PC. Right-click a mod for its workshop page or folder.");
        Tips.Set(addB, "Paste GUIDs, workshop links, names, a whole server config or the game's mod export.");
        Tips.Set(tickB, "Puts every mod in the list below into this list.");
        Tips.Set(untickB, "Takes every mod in the list below out of this list.");
        var bar = Ui.NewFlow(DockStyle.Top, Ui.NewLabel("Find", 0, "dim"), modSearch, Ui.NewLabel("Show", 0, "dim"), modView, addB, tickB, untickB, modCount);
        bar.Padding = new Padding(0, 0, 0, 8);
        page.Controls.Add(modList);
        page.Controls.Add(bar);
        return page;
    }

    static string StatusText(ModRow r)
    {
        if (r.Installed) return r.Enabled ? "Downloaded, enabled now" : "Downloaded";
        return r.Location switch { "Server" => "Server's addons only", "Project" => "Unpublished (mod-src)", _ => "Not on this PC" };
    }

    static ModRow RowOf(ModInfo m) => new() { Guid = m.Guid, Name = m.Name, Version = m.Version, Size = m.Size, Installed = m.Installed, Enabled = m.Enabled, LastPlayed = m.LastPlayed, Location = m.Location };

    void UpdateModList()
    {
        var item = CurrentItem;
        bool isConfig = item is ConfigItem;
        var rows = App.Installed.Values.Select(RowOf).ToList();
        if (isConfig) rows.AddRange(App.ExtraMods.Values.Where(m => !App.Installed.ContainsKey(m.Guid)).Select(RowOf));
        if (item != null)
            foreach (var e in item.Mods)
            {
                if (App.Installed.ContainsKey(e.Key) || (isConfig && App.ExtraMods.ContainsKey(e.Key))) continue;
                App.Web.TryCached(e.Key, out var web);
                var extra = App.ExtraMods.GetValueOrDefault(e.Key);
                rows.Add(new ModRow { Guid = e.Key, Name = e.Value ?? "", Location = extra?.Location ?? "", Version = web?.Version ?? "", Size = web?.Size ?? 0 });
            }
        var words = modSearch.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int view = modView.SelectedIndex;
        var shown = rows.Where(r =>
        {
            bool inItem = item != null && item.Mods.Contains(r.Guid);
            bool ok = view switch { 1 => inItem, 2 => !inItem, 3 => r.Enabled, 4 => App.IsMissing(item, r.Guid), _ => true };
            foreach (var w in words) if (!r.Name.Contains(w, StringComparison.OrdinalIgnoreCase) && !r.Guid.Contains(w, StringComparison.OrdinalIgnoreCase)) ok = false;
            return ok;
        });
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<ModRow> ordered;
        // Sorted by name, the item's own mods come first.
        if (sortColumn == 0 && item != null) ordered = shown.OrderByDescending(r => item.Mods.Contains(r.Guid)).ThenBy(r => 0);
        else ordered = shown.OrderBy(r => 0);
        Func<ModRow, IComparable> key = sortColumn switch
        {
            1 => r => r.Version, 2 => r => r.Size, 3 => r => r.LastPlayed ?? DateTime.MinValue, 4 => r => r.Guid, 5 => r => r.Location, _ => r => r.Name,
        };
        ordered = sortColumn is 0 or 1 or 4 or 5
            ? (sortDescending ? ordered.ThenByDescending(r => (string)key(r) ?? "", cmp) : ordered.ThenBy(r => (string)key(r) ?? "", cmp))
            : (sortDescending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        var list = ordered.ThenBy(r => r.Name, cmp).ToList();

        var items = list.Select(r =>
        {
            var it = new ListViewItem(r.Name ?? "") { Tag = r.Guid, Checked = item != null && item.Mods.Contains(r.Guid) };
            it.SubItems.Add(r.Version ?? "");
            it.SubItems.Add(Mods.FormatSize(r.Size));
            it.SubItems.Add(r.LastPlayed?.ToString("yyyy-MM-dd") ?? "");
            it.SubItems.Add(r.Guid);
            it.SubItems.Add(StatusText(r));
            if (App.IsMissing(item, r.Guid)) it.ForeColor = Theme.C("Bad");
            return it;
        }).ToArray();
        modList.Rows = items;
        shownCount = list.Count;
        UpdateModStatus();
    }

    void UpdateModStatus()
    {
        var item = CurrentItem;
        int total = App.Installed.Count;
        if (item is ConfigItem) total += App.ExtraMods.Keys.Count(k => !App.Installed.ContainsKey(k));
        modCount.Text = $"{item?.Mods.Count ?? 0} ticked   \u00B7   showing {shownCount} of {total}";
        UpdateItemHeader();
    }

    // A tick clicked (or Space): the mod goes in or out of the selected list.
    void ToggleMod(int index)
    {
        var item = CurrentItem;
        if (item == null) { Status.Text = "Pick a mod list on the left first."; return; }
        var it = modList.Rows[index];
        var g = (string)it.Tag;
        it.Checked = !it.Checked;
        if (it.Checked) item.Mods.Add(g, it.Text); else item.Mods.Remove(g);
        modList.InvalidateRow(index);
        SetDirty(item);
        UpdateModStatus();
    }

    void SetShownChecked(bool check)
    {
        var item = CurrentItem;
        if (item == null) { Ui.Message("Pick a mod list on the left first."); return; }
        foreach (var it in modList.Rows)
        {
            var g = (string)it.Tag;
            if (check) item.Mods.Add(g, it.Text); else item.Mods.Remove(g);
            it.Checked = check;
        }
        modList.Invalidate();
        SetDirty(item);
        UpdateModStatus();
    }

    void ShowAddListDialog()
    {
        var item = CurrentItem;
        if (item == null) { Ui.Message("Pick a mod list on the left first."); return; }
        using var dlg = new Form
        {
            Text = $"Add mods to '{item.Name}'", Size = new Size(720, 560), StartPosition = FormStartPosition.CenterParent, Font = Font,
            MinimizeBox = false, ShowInTaskbar = false, BackColor = Theme.C("Back"), ForeColor = Theme.C("Text"),
        };
        Native.WindowChrome(dlg);
        var box = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f) };
        box.PlaceholderText = "One mod per line: a GUID, a workshop link, \"GUID name\", or the name of a downloaded mod.\r\n" +
            "A whole server config, or the game's own mod export (JSON or -addons ...), works too. Lines starting with # are ignored.\r\n" +
            "Mods you haven't downloaded are looked up on the workshop for their names.";
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(4) };
        bool replace = false;
        var cancel = Ui.NewButton("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
        var replaceB = Ui.NewButton("Replace the mods", () => replace = true); replaceB.DialogResult = DialogResult.OK;
        var add = Ui.NewButton("Add", () => replace = false, "primary"); add.DialogResult = DialogResult.OK;
        var file = Ui.NewButton("Open file...", () =>
        {
            using var ofd = new OpenFileDialog { Filter = "Mod lists and server configs (*.json;*.txt)|*.json;*.txt|All files (*.*)|*.*", InitialDirectory = App.Root };
            if (ofd.ShowDialog(dlg) == DialogResult.OK) box.Text = Regex.Replace(File.ReadAllText(ofd.FileName), "\r?\n", "\r\n");
        });
        bar.Controls.AddRange(new Control[] { cancel, replaceB, add, file });
        Tips.Set(replaceB, "Empties this list first, then adds the pasted mods.");
        Tips.Set(file, "Reads a mod list or server config file into the box.");
        dlg.Controls.Add(box);
        dlg.Controls.Add(bar);
        dlg.CancelButton = cancel;
        Theme.Style(dlg);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var entries = Mods.ParseList(box.Text);
        if (entries.Count == 0) { Ui.Message("No mods found in that text."); return; }
        App.ListResult r;
        SetBusy(true, $"Adding {entries.Count} mods...");
        try
        {
            if (replace) item.Mods.Clear();
            r = App.AddList(item, entries);
        }
        finally { SetBusy(false); }
        SetDirty(item);
        ShowCurrentItem();
        var msg = $"Added {r.Added.Count} mods to '{item.Name}'.";
        if (r.AlreadyIn > 0) msg += $" {r.AlreadyIn} were already in it.";
        int missing = r.Added.Count(g => App.IsMissing(item, g));
        if (missing > 0) msg += $"\n\n{missing} of them aren't on this PC (shown in red).";
        if (r.Unmatched.Count > 0) msg += "\n\nNot added:\n" + string.Join("\n", r.Unmatched.Take(25));
        if (r.Unmatched.Count > 25) msg += $"\n...and {r.Unmatched.Count - 25} more";
        Ui.Message(msg);
    }
}
