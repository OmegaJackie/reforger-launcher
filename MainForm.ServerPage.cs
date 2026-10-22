using System.Text.Json.Nodes;

namespace ReforgerLauncher;

// The Configuration tab of a server config: every setting with the value in its file, edited in place (Save / Save all
// write the file). The help for each is in its tooltip.
public partial class MainForm
{
    Panel serverFields;
    Label srvProblems;
    TableLayoutPanel srvGrid, srvOther;
    public List<SettingRow> SrvRows;
    public List<SettingRow> SrvOtherRows = new();
    List<Scenario> srvScenarioChoices = new();
    bool srvLoading;

    // One setting on the page: its name, its editor and help.
    public class SettingRow
    {
        public Setting Spec;
        public Label Label;
        public Control Editor;
        public TextBox Extra;          // navmesh: the project list
        public CheckBox[] Checks;      // platforms
        public string Invalid;
    }

    Control BuildConfigPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 4, 4, 0) };
        srvProblems = Ui.NewLabel("", 900, "bad"); srvProblems.Margin = new Padding(4, 8, 3, 4);
        // The setting rows are made the first time a server config's Configuration tab is shown (InitServerRows).
        srvGrid = NewSettingGrid();
        srvOther = NewSettingGrid();
        srvOther.Visible = false;
        serverFields = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        serverFields.Controls.Add(srvOther);
        serverFields.Controls.Add(srvGrid);
        serverFields.Controls.Add(Ui.NewFlow(DockStyle.Top, srvProblems));
        page.Controls.Add(serverFields);
        return page;
    }
    static TableLayoutPanel NewSettingGrid()
    {
        var g = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 10) };
        g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return g;
    }

    static Label AddSection(TableLayoutPanel grid, string text)
    {
        var l = Ui.NewLabel(text, 0, "sub");
        l.Font = Theme.Section;
        l.Margin = new Padding(3, 16, 3, 4);
        grid.Controls.Add(l);
        grid.SetColumnSpan(l, 2);
        return l;
    }

    SettingRow NewSettingRow(TableLayoutPanel grid, Setting spec)
    {
        var row = new SettingRow { Spec = spec };
        row.Label = Ui.NewLabel(spec.L); row.Label.Margin = new Padding(3, 7, 16, 3);
        var controls = new List<Control>();
        ComboBox Combo(int width, params string[] items)
        {
            var c = Ui.NewCombo(width, items);
            c.SelectedIndexChanged += (s, e) => Ui.Safe(() => SettingEdited(row));
            return c;
        }
        switch (spec.T)
        {
            case "bool": row.Editor = Combo(170, "not set", spec.On ?? "Enabled", spec.Off ?? "Disabled"); break;
            case "enum": row.Editor = Combo(170, new[] { "not set" }.Concat(spec.Values).ToArray()); break;
            case "thirdperson": row.Editor = Combo(170, "not set", "Allowed", "Disabled", "Driver only"); break;
            case "scenario": row.Editor = Combo(440); break;
            case "navmesh":
                row.Editor = Combo(190, "not set (streamed)", "Off for all projects", "Off for these:");
                row.Extra = new TextBox { Width = spec.W, PlaceholderText = "navmesh projects" };
                row.Extra.TextChanged += (s, e) => Ui.Safe(() => SettingEdited(row));
                break;
            case "platforms":
                row.Checks = ServerSettings.PlatformNames.Select(n =>
                {
                    var cb = new CheckBox { Text = n, AutoSize = true, Margin = new Padding(3, 5, 10, 3) };
                    cb.CheckedChanged += (s, e) => Ui.Safe(() => SettingEdited(row));
                    return cb;
                }).ToArray();
                row.Editor = row.Checks[0];
                break;
            case "mods": row.Editor = Ui.NewButton("", () => ShowSubTab("Mods")); break;
            default:
                var tb = new TextBox { Width = spec.W > 0 ? spec.W : spec.T is "int" or "num" ? 90 : 260, PlaceholderText = "not set" };
                tb.TextChanged += (s, e) => Ui.Safe(() => SettingEdited(row));
                row.Editor = tb;
                break;
        }
        if (spec.T == "platforms") controls.AddRange(row.Checks); else controls.Add(row.Editor);
        if (row.Extra != null) controls.Add(row.Extra);
        if (spec.U != null) controls.Add(Ui.NewLabel(spec.U, 0, "dim"));
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
        flow.Controls.AddRange(controls.ToArray());
        // The help, range and place in the file show on hovering the name or the field.
        var tip = new List<string>();
        if (!string.IsNullOrEmpty(spec.H)) tip.Add(spec.H);
        if (spec.Min.HasValue && spec.Max.HasValue) tip.Add($"Range: {spec.Min}-{spec.Max}{(spec.U != null ? " " + spec.U : "")}.");
        if (spec.T == "text" && spec.MaxLen > 0) tip.Add($"Length: {Math.Max(spec.MinLen, 0)}-{spec.MaxLen} characters.");
        if (!string.IsNullOrEmpty(spec.P)) tip.Add($"In the file: {spec.P}. A grey name isn't in the file: the server uses its default.");
        var tipText = string.Join("\n\n", tip);
        Tips.Set(tipText, new[] { row.Label }.Concat(controls).ToArray());
        grid.Controls.Add(row.Label);
        grid.Controls.Add(flow);
        return row;
    }

    void InitServerRows()
    {
        if (SrvRows != null) return;
        SetBusy(true, "Laying out the server settings...");
        srvGrid.SuspendLayout();
        try
        {
            var rows = new List<SettingRow>();
            foreach (var spec in ServerSettings.All)
            {
                if (spec.S != null) AddSection(srvGrid, spec.S); else rows.Add(NewSettingRow(srvGrid, spec));
            }
            Theme.Style(srvGrid);
            SrvRows = rows;
        }
        finally { srvGrid.ResumeLayout(); SetBusy(false); Status.Text = ""; }
    }

    // A setting's editor -> a value, "not set", or what's wrong with it.
    ServerSettings.Parsed ReadEditor(SettingRow row)
    {
        var spec = row.Spec;
        int i = row.Editor is ComboBox cb ? cb.SelectedIndex : -1;
        switch (spec.T)
        {
            case "bool": return i <= 0 ? ServerSettings.Parsed.Clear() : ServerSettings.Parsed.Of(JsonValue.Create(i == 1));
            case "enum": return i <= 0 ? ServerSettings.Parsed.Clear() : ServerSettings.Parsed.Of(JsonValue.Create(spec.Values[i - 1]));
            case "thirdperson":
                if (i <= 0) return ServerSettings.Parsed.Clear();
                return ServerSettings.Parsed.Of(i switch { 1 => JsonValue.Create(false), 2 => JsonValue.Create(true), _ => JsonValue.Create("ENABLE_DRIVER_ONLY") });
            case "scenario":
                return i < 0 ? ServerSettings.Parsed.Fail("pick a scenario") : ServerSettings.Parsed.Of(JsonValue.Create(srvScenarioChoices[i].Id));
            case "navmesh":
                row.Extra.Enabled = i == 2;
                if (i <= 0) return ServerSettings.Parsed.Clear();
                if (i == 1) return ServerSettings.Parsed.Of(new JsonArray());
                var r = ServerSettings.ParseText(spec, row.Extra.Text);
                return r.Unset ? ServerSettings.Parsed.Fail("list the navmesh projects, or pick 'Off for all projects'") : r;
            case "platforms":
                var on = row.Checks.Select((c, k) => c.Checked ? ServerSettings.PlatformIds[k] : null).Where(x => x != null).ToList();
                return on.Count == 0 ? ServerSettings.Parsed.Clear() : ServerSettings.Parsed.Of(new JsonArray(on.Select(x => (JsonNode)JsonValue.Create(x)).ToArray()));
            default:
                return ServerSettings.ParseText(spec, row.Editor.Text);
        }
    }

    // A setting's value -> its editor.
    void WriteEditor(SettingRow row, ConfigItem item)
    {
        var spec = row.Spec;
        row.Invalid = null;
        if (spec.T == "mods") { row.Editor.Text = $"{item.Mods.Count} MODS  -  MODS TAB"; return; }
        bool found = Json.Has(item.Config, spec.P);
        var value = Json.Get(item.Config, spec.P);
        switch (spec.T)
        {
            case "bool": ((ComboBox)row.Editor).SelectedIndex = !found ? 0 : Json.Truthy(value) ? 1 : 2; break;
            case "enum": ((ComboBox)row.Editor).SelectedIndex = found ? Array.IndexOf(spec.Values, Json.Str(value)) + 1 : 0; break;
            case "thirdperson":
            {
                var s = Json.Str(value);
                ((ComboBox)row.Editor).SelectedIndex = !found ? 0 : s == "ENABLE_DRIVER_ONLY" ? 3 : (s == "DISABLE" || (value is JsonValue v && v.TryGetValue(out bool b) && b)) ? 2 : 1;
                break;
            }
            case "scenario":
            {
                var id = Json.Str(value);
                var choices = App.Scenarios.OrderBy(s => s.ModGuid.Length > 0).ThenBy(s => s.Order).ThenBy(s => s.ModName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (id.Length > 0 && !choices.Any(s => s.Id == id))
                    choices.Insert(0, new Scenario { Id = id, Name = System.Text.RegularExpressions.Regex.Replace(id, "^.*/", ""), ModName = "not found on this PC" });
                srvScenarioChoices = choices;
                var combo = (ComboBox)row.Editor;
                combo.BeginUpdate();
                combo.Items.Clear();
                foreach (var s in choices) combo.Items.Add($"{s.Name}   [{s.ModName}]");
                combo.EndUpdate();
                combo.SelectedIndex = choices.FindIndex(s => s.Id == id);
                break;
            }
            case "navmesh":
            {
                var list = value is JsonArray a ? a.Select(x => Json.Str(x)).Where(x => x.Length > 0).ToList() : new List<string>();
                var combo = (ComboBox)row.Editor;
                combo.SelectedIndex = !found ? 0 : list.Count == 0 ? 1 : 2;
                row.Extra.Text = string.Join(", ", list);
                row.Extra.Enabled = combo.SelectedIndex == 2;
                break;
            }
            case "platforms":
                for (int k = 0; k < row.Checks.Length; k++)
                    row.Checks[k].Checked = found && value is JsonArray pa && pa.Any(x => Json.Str(x) == ServerSettings.PlatformIds[k]);
                break;
            default:
                row.Editor.Text = found ? ServerSettings.Format(spec, value) : "";
                break;
        }
        row.Editor.ForeColor = Theme.C("Text");
        row.Label.ForeColor = Theme.C(found ? "Text" : "Dim");
    }

    // Applies an edited field to the selected config right away (the file is written by Save / Save all).
    void SettingEdited(SettingRow row)
    {
        if (srvLoading || CurrentItem is not ConfigItem item) return;
        var r = ReadEditor(row);
        if (!r.Ok)
        {
            row.Invalid = r.Error;
            row.Editor.ForeColor = Theme.C("Bad");
            Status.Text = $"{row.Spec.L}: {r.Error} - not applied.";
            UpdateServerProblems(item);
            return;
        }
        row.Invalid = null;
        row.Editor.ForeColor = Theme.C("Text");
        if (r.Unset) Json.Remove(item.Config, row.Spec.P); else Json.Set(item.Config, row.Spec.P, r.Value);
        row.Label.ForeColor = Theme.C(r.Unset ? "Dim" : "Text");
        SetDirty(item);
        Status.Text = $"{row.Spec.L} {(r.Unset ? "removed from" : "changed in")} {item.Name} - Save writes it.";
        if (row.Spec.P == "game.name") { var node = FindNode(item); if (node != null) node.ToolTipText = $"{item.ServerName}\n{item.Name}"; }
        if (row.Spec.T == "scenario")
        {
            var s = srvScenarioChoices[((ComboBox)row.Editor).SelectedIndex];
            int added = 0;
            foreach (var e in App.RequiredMods(s).Mods)
                if (!item.Mods.Contains(e.Key)) { item.Mods.Add(e.Key, e.Value); added++; }
            if (added > 0) Status.Text = $"Scenario set; added {added} mod(s) it needs to the mod list.";
            SetDirty(item);
            staleTabs.Add("Mods"); staleTabs.Add("Scenarios");
            foreach (var m in SrvRows.Where(x => x.Spec.T == "mods")) WriteEditor(m, item);
        }
        UpdateItemHeader();
        UpdateServerProblems(item);
    }

    // What would stop the server from accepting the file, at the top of the page.
    void UpdateServerProblems(ConfigItem item)
    {
        var cfg = item.Config;
        var list = new List<string>();
        foreach (var row in (SrvRows ?? new()).Concat(SrvOtherRows)) if (row.Invalid != null) list.Add($"{row.Spec.L}: {row.Invalid} (not applied)");
        if (Json.Str(cfg, "game.name").Length == 0) list.Add("Server name is required.");
        if (Json.Str(cfg, "game.scenarioId").Length == 0) list.Add("Scenario is required.");
        if (Json.Has(cfg, "a2s") && !Json.Has(cfg, "a2s.address")) list.Add("A2S needs an address (0.0.0.0 = all).");
        if (Json.Has(cfg, "rcon") && !(Json.Has(cfg, "rcon.address") && Json.Has(cfg, "rcon.password"))) list.Add("RCON needs an address and a password (or remove its other settings).");
        srvProblems.Text = string.Join("\n", list.Select(x => "• " + x));
        srvProblems.Visible = list.Count > 0;
    }

    void UpdateServerOther(ConfigItem item)
    {
        var paths = ServerSettings.OtherPaths(item.Config);
        srvOther.SuspendLayout();
        foreach (Control c in srvOther.Controls.Cast<Control>().ToList()) c.Dispose();
        srvOther.Controls.Clear();
        SrvOtherRows = new List<SettingRow>();
        if (paths.Count > 0)
        {
            AddSection(srvOther, "OTHER SETTINGS IN THE FILE  (as JSON)");
            foreach (var path in paths)
            {
                var help = ServerSettings.OfficialOnly.Contains(path) ? "Only for official servers (the server's schema)."
                    : "Not a setting the dedicated server knows - it may refuse to start with it. Empty the field to remove it.";
                var row = NewSettingRow(srvOther, new Setting { P = path, L = path, T = "json", W = 400, H = help });
                WriteEditor(row, item);
                SrvOtherRows.Add(row);
            }
            Theme.Style(srvOther);
            foreach (var row in SrvOtherRows) row.Label.ForeColor = Theme.C("Text");
        }
        srvOther.Visible = paths.Count > 0;
        srvOther.ResumeLayout();
    }

    void UpdateServerPage()
    {
        if (CurrentItem is not ConfigItem config) return;
        InitServerRows();
        srvLoading = true;
        serverFields.SuspendLayout();
        try
        {
            foreach (var row in SrvRows) WriteEditor(row, config);
            UpdateServerOther(config);
        }
        finally { srvLoading = false; serverFields.ResumeLayout(); }
        UpdateServerProblems(config);
    }
    void SaveServerConfig()
    {
        if (CurrentItem is not ConfigItem item) return;
        int bad = (SrvRows ?? new()).Concat(SrvOtherRows).Count(r => r.Invalid != null);
        if (bad > 0 && !Ui.Ask($"{bad} field(s) are red (not valid) and keep their old value. Save the rest?")) return;
        var note = "";
        if (deps.Checked)
        {
            var r = App.AddDependencies(item);
            if (r.Added.Count > 0) note = " Added dependencies: " + string.Join(", ", r.Added.Select(g => item.Mods[g])) + ".";
        }
        App.SaveConfig(item);
        var node = FindNode(item);
        if (node != null) node.Text = NodeText(item);
        UpdateTitle();
        UpdateItemHeader();
        foreach (var m in (SrvRows ?? new()).Where(x => x.Spec.T == "mods")) WriteEditor(m, item);
        Status.Text = $"Saved {item.Name}.{note}";
    }

    void OpenConfigFile()
    {
        if (CurrentItem is not ConfigItem item) return;
        if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path)) { Ui.Message($"{item.Name} isn't saved yet."); return; }
        if (item.Changed) Ui.Message($"{item.Name} has unsaved changes here: the file shows it as last saved.");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", Native.Quote(item.Path)) { UseShellExecute = false });
    }
}
