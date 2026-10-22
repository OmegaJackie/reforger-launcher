namespace ReforgerLauncher;

// --snap <folder>: opens off-screen, waits for the servers to answer, saves a picture of every tab (the Server tab once
// per sub-tab, a preset and the configs heading too), and closes. For checking a build without putting a window on the
// screen.
public partial class MainForm
{
    public void SnapAllAndClose(string dir)
    {
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-20000, -20000);
        ShowInTaskbar = false;
        Shown += (s, e) => BeginInvoke(() =>
        {
            long shownAt = (long)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "exe-startup.txt"), $"window shown {shownAt} ms after start (loading {Program.LoadMs} ms, building the window {Program.FormMs} ms)\n");
                void Snap(string name)
                {
                    var end = DateTime.Now.AddMilliseconds(400);
                    while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
                    using var bmp = new Bitmap(Width, Height);
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(Path.Combine(dir, $"exe-{name}.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                ShowPage("History");
                var until = DateTime.Now.AddSeconds(12);
                while ((QueriesPending || !serversLoaded) && DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(20); }
                Snap("history");
                ShowPage("Favorites"); Snap("favorites");
                var config = App.Configs.FirstOrDefault(c => c.Name == "config.kunar.json") ?? App.Configs.FirstOrDefault();
                ShowPage("Server");
                if (config != null)
                {
                    UpdateTree(config);
                    foreach (var sub in SubTabNames) { ShowSubTab(sub); Snap("server-" + sub.ToLowerInvariant()); }
                    ShowSubTab("Configuration");
                }
                var preset = App.Doc.Presets.FirstOrDefault();
                if (preset != null) { UpdateTree(preset); Snap("server-preset"); }
                UpdateTree("configs"); Snap("server-configs");
                ShowPage("Advanced"); Snap("advanced");
                File.WriteAllText(Path.Combine(dir, "exe-status.txt"), $"servers: {App.Data.Servers.Count}, answered: {Live.Values.Count(l => l.Online)}, scenarios: {App.Scenarios.Count}, mods: {App.Installed.Count}\n");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "exe-error.txt"), ex.ToString()); }
            foreach (var i in App.AllItems()) i.Changed = false;
            App.PresetsDirty = false;
            Close();
        });
    }
}
