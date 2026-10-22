namespace ReforgerLauncher;

// ADVANCED: the game's launch options (only ones its exe knows), launcher behaviour, and the folders in use. The help is
// in each row's tooltip.
public partial class MainForm
{
    CheckBox advWindow, advNoSound, advMinimize, advInApp;
    NumericUpDown advMaxFps;
    TextBox advExtra;
    Label advPreview;
    bool advLoading;

    Control BuildAdvancedPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(26, 6, 20, 10) };
        var grid = NewSettingGrid();
        var a = App.Data.Advanced;
        void Row(string label, string tip, params Control[] controls)
        {
            var l = Ui.NewLabel(label); l.Margin = new Padding(3, 7, 24, 3);
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
            f.Controls.AddRange(controls);
            grid.Controls.Add(l); grid.Controls.Add(f);
            if (tip != null) Tips.Set(tip, new[] { l }.Concat(controls).ToArray());
        }
        CheckBox Check(string text, bool on)
        {
            var cb = new CheckBox { Text = text, AutoSize = true, Checked = on, Margin = new Padding(3, 5, 3, 3) };
            cb.CheckedChanged += (s, e) => Ui.Safe(ReadAdvanced);
            return cb;
        }
        advLoading = true;
        var launchHeading = AddSection(grid, "GAME LAUNCH OPTIONS");
        Tips.Set(launchHeading, "Used by PLAY, JOIN and open game.");
        advWindow = Check("Start in a window", a.Window);
        Row("Window", "-window: the game starts windowed instead of full screen.", advWindow);
        advMaxFps = new NumericUpDown { Minimum = 0, Maximum = 1000, Width = 80, Value = Math.Clamp(a.MaxFps, 0, 1000) };
        advMaxFps.ValueChanged += (s, e) => Ui.Safe(ReadAdvanced);
        Row("Max FPS", "-maxFPS: a frame rate cap. 0 = no limit.", advMaxFps);
        advNoSound = Check("No sound", a.NoSound);
        Row("Sound", "-nosound: starts the game without sound.", advNoSound);
        advExtra = new TextBox { Width = 520, Text = a.Extra ?? "", PlaceholderText = "-logStats 60000" };
        advExtra.TextChanged += (s, e) => Ui.Safe(ReadAdvanced);
        Row("Extra parameters", "Added to the game's command line as typed.", advExtra);
        AddSection(grid, "LAUNCHER");
        advMinimize = Check("Minimize the launcher when the game starts", a.Minimize);
        Row("On launch", null, advMinimize);
        advInApp = Check("Run the server inside the launcher", a.ServerInApp);
        Row("Server", "On: START SERVER runs the dedicated server without a window of its own; the Console tab shows its log, players and RCON, " +
            "and the launcher restarts it if it stops by itself.\n\nOff: start-server.bat runs it in its own console window, as before.", advInApp);
        AddSection(grid, "COMMAND LINE");
        advPreview = Ui.NewLabel("", 900, "label"); advPreview.Font = new Font("Consolas", 9f);
        Row("Game", "What starts the game, plus what PLAY / JOIN add (-client -addr ... -addons ...).", advPreview);
        AddSection(grid, "FOLDERS");
        foreach (var (name, path) in new[] { ("Game", App.GameDir), ("Dedicated server", App.ServerDir), ("Downloaded mods", App.AddonsDir),
            ("Game logs", Path.Combine(App.DocsDir, "logs")), ("This launcher", App.Root) })
        {
            var open = Ui.NewButton("Open", () => { if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Launch.Open(path); });
            open.Enabled = !string.IsNullOrEmpty(path);
            open.Margin = new Padding(3, 2, 10, 2);
            Row(name, null, open, Ui.NewLabel(string.IsNullOrEmpty(path) ? "not found" : path, 700, "label"));
        }
        advLoading = false;
        page.Controls.Add(grid);
        UpdateAdvancedPreview();
        return page;
    }

    void ReadAdvanced()
    {
        if (advLoading) return;
        var a = App.Data.Advanced;
        a.Window = advWindow.Checked;
        a.MaxFps = (int)advMaxFps.Value;
        a.NoSound = advNoSound.Checked;
        a.Extra = advExtra.Text.Trim();
        a.Minimize = advMinimize.Checked;
        a.ServerInApp = advInApp.Checked;
        SaveData();
        UpdateAdvancedPreview();
    }

    void UpdateAdvancedPreview()
    {
        var exe = App.GameDir != null ? Path.Combine(App.GameDir, "ArmaReforgerSteam.exe") : "ArmaReforgerSteam.exe";
        advPreview.Text = (Native.Quote(exe) + " " + Native.JoinArgs(App.Data.AdvancedArgs())).TrimEnd();
    }

    // Test hooks for the Advanced page.
    public void SetAdvanced(bool window, int maxFps, string extra)
    {
        advWindow.Checked = window; advMaxFps.Value = maxFps; advExtra.Text = extra;
    }
    public string AdvancedPreview => advPreview.Text;
}
