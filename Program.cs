namespace ReforgerLauncher;

// Command line: --root <folder with config*.json> (default: the exe's folder), --settings <ReforgerGameSettings.conf>,
// --addons <the game's downloaded mods>, --offline (no workshop website lookups).
public class Options
{
    public string Root;
    public string SettingsFile;
    public string AddonsDir;
    public bool Offline;
    public string SnapDir;   // --snap <folder>: pictures of every tab off-screen, then exit (for checking a build)

    public static Options Parse(string[] args)
    {
        var o = new Options { Root = AppContext.BaseDirectory.TrimEnd('\\') };
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].TrimStart('-', '/').ToLowerInvariant();
            string next = i + 1 < args.Length ? args[i + 1] : null;
            switch (a)
            {
                case "root": o.Root = Path.GetFullPath(next); i++; break;
                case "settings": o.SettingsFile = next; i++; break;
                case "addons": o.AddonsDir = next; i++; break;
                case "offline": o.Offline = true; break;
                case "snap": o.SnapDir = Path.GetFullPath(next); i++; break;
            }
        }
        return o;
    }
}

public static class Program
{
    public static long LoadMs, FormMs;   // startup timing, written by --snap

    [STAThread]
    public static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        // An error in an event handler: a plain message instead of .NET's crash dialog.
        Application.ThreadException += (s, e) => Ui.ShowError(e.Exception);
        try
        {
            var app = new App(Options.Parse(args));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            app.Load();
            LoadMs = sw.ElapsedMilliseconds; sw.Restart();
            using var form = new MainForm(app);
            FormMs = sw.ElapsedMilliseconds;
            if (app.Opt.SnapDir != null) form.SnapAllAndClose(app.Opt.SnapDir);
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message + "\n\n" + ex.StackTrace, App.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
