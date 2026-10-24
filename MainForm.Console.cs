using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ReforgerLauncher;

// CONSOLE: the server running inside the launcher (or any dedicated server on this PC): its live log, its players, and
// an RCON command line. START SERVER runs the server here without a window of its own; the launcher restarts it when it
// stops by itself, and stops it cleanly (RCON #shutdown or Ctrl+C - it saves first). With RCON on (the "rcon" section of
// its config, offered when a config without one is started) it lists the players, kicks, bans and takes commands.
public partial class MainForm
{
    // A line of the log view: the server's own, or the launcher's (notes, commands sent, RCON answers).
    struct LogLine
    {
        public string Text;
        public char Kind;   // n normal, e error, w warning, g good, q the server echoing RCON traffic, s sent, r RCON answer, m RCON message, l launcher note
    }

    // Reads what a server adds to its console.log.
    sealed class LogTail
    {
        public readonly string Path;
        long pos;
        string partial = "";
        readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();
        public LogTail(string path, long start) { Path = path; pos = start; }

        public List<string> Read()
        {
            var lines = new List<string>();
            try
            {
                using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < pos) pos = 0;
                if (fs.Length == pos) return lines;
                fs.Seek(pos, SeekOrigin.Begin);
                var buf = new byte[Math.Min(fs.Length - pos, 4 << 20)];
                int n = fs.Read(buf, 0, buf.Length);
                pos += n;
                var chars = new char[decoder.GetCharCount(buf, 0, n)];
                decoder.GetChars(buf, 0, n, chars, 0);
                var text = partial + new string(chars);
                int last = text.LastIndexOf('\n');
                if (last < 0) { partial = text; return lines; }
                partial = text[(last + 1)..];
                foreach (var l in text[..last].Split('\n')) lines.Add(l.TrimEnd('\r'));
            }
            catch (IOException) { }
            return lines;
        }
    }

    public ServerProc ConsoleProc;               // the server the Console shows
    Process ownedProc;                           // started here: restarted when it stops by itself
    string ownedConfig = "";
    bool stoppingOwned, ownedReady;
    int ownedLogStart;
    readonly List<DateTime> autoRestarts = new();
    DateTime? restartAt;
    public RconClient Rcon;
    int rconPid;
    bool rconConnecting;
    DateTime rconRetryAt, playersAt;
    string rconProblem = "", rconStateKey = "";
    LogTail tail;
    readonly List<LogLine> logLines = new();
    List<int> shownLines = new();
    GridList logList, playerList;
    TextBox logFind, cmdBox;
    ComboBox logView, conConfig;
    CheckBox logFollow, conAutoRestart;
    Label conTitle, conSub, conState, rconState, playersTitle;
    Button conStart, conRestart, conStop, sendButton, enableRcon;
    FlowLayoutPanel playerButtons;
    readonly List<string> cmdHistory = new();
    int cmdHistoryPos;
    readonly System.Windows.Forms.Timer tailTimer = new() { Interval = 300 };
    readonly System.Windows.Forms.Timer logFindTimer = new() { Interval = 250 };
    static readonly Font LogFont = new("Consolas", 9f);
    const int MaxLogLines = 30000;

    public class ConsolePlayer { public int Id; public string Uid = "", Name = ""; }
    public IEnumerable<string> ConsoleText => logLines.Select(l => l.Text);
    public bool RestartPending => restartAt != null;
    public List<ConsolePlayer> Players = new();

    Control BuildConsolePage()
    {
        var page = new Panel { Dock = DockStyle.Fill };

        // ---- Header: the server, its state, and START / RESTART / STOP
        var header = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(18, 10, 16, 0) };
        conTitle = new Label { AutoSize = true, Location = new Point(16, 8), Font = new Font("Segoe UI Semibold", 17f), Tag = "accent" };
        var subFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Location = new Point(16, 46) };
        conState = new Label { AutoSize = true, Margin = new Padding(3, 0, 12, 0), Font = Theme.HeaderFont };
        conSub = new Label { AutoSize = true, Margin = new Padding(0), Tag = "dim" };
        subFlow.Controls.AddRange(new Control[] { conState, conSub });
        Button Big(string t, Action a, int w, string kind = "") { var b = Ui.NewButton(t, a, kind); b.AutoSize = false; b.Size = new Size(w, 38); b.Padding = new Padding(0); b.Margin = new Padding(8, 0, 0, 0); return b; }
        conConfig = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, Margin = new Padding(0, 8, 0, 0), MaxDropDownItems = 20 };
        conConfig.Format += (s, e) => { if (e.ListItem is ConfigItem c) e.Value = $"{DisplayName(c)}  -  {c.ServerName}"; };
        conStart = Big("Start server", () => { if (conConfig.SelectedItem is ConfigItem c) StartServerFor(c, true); }, 150, "primary");
        conStart.Font = new Font("Segoe UI Semibold", 10.5f);
        conRestart = Big("Restart", () => RestartConsoleServer(), 110);
        conStop = Big("Stop", () => StopConsoleServer(true), 100);
        Tips.Set(conConfig, "The server config START SERVER runs.");
        Tips.Set(conStart, "Starts the dedicated server with this config, inside the launcher (no window of its own).");
        Tips.Set(conRestart, "Stops the server cleanly (it saves first) and starts it again with its config - picks up config changes.");
        Tips.Set(conStop, "Stops the server cleanly: RCON #shutdown (or Ctrl+C without RCON), which saves its progress first.");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange(new Control[] { conConfig, conStart, conRestart, conStop });
        header.Controls.Add(buttons);
        header.Controls.Add(subFlow);
        header.Controls.Add(conTitle);

        // ---- Players on the right
        var players = new Panel { Dock = DockStyle.Right, Width = 300, Padding = new Padding(12, 8, 14, 8), BackColor = Theme.C("Side") };
        playersTitle = Ui.NewLabel("Players", 0, "sub"); playersTitle.Font = Theme.Section; playersTitle.Margin = new Padding(0, 4, 0, 8);
        playerList = new GridList { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        Theme.DoubleBuffer(playerList);
        playerList.Columns.Add("#", 44); playerList.Columns.Add("Name", 200);
        playerList.Columns[0].TextAlign = HorizontalAlignment.Right;
        playerList.Resize += (s, e) => Theme.FillColumn(playerList, 1);
        Theme.GridStyle(playerList);
        playerList.MouseMove += (s, e) =>
        {
            var it = playerList.GetItemAt(e.X, e.Y);
            Tips.ShowAt(playerList, it?.Tag is ConsolePlayer p && p.Uid.Length > 0 ? $"{p.Name}\nPlayer # {p.Id}\nUID {p.Uid}" : null, e.Location);
        };
        playerList.MouseLeave += (s, e) => Tips.ShowAt(playerList, null, Point.Empty);
        var banMenu = Ui.NewMenu(("For 1 hour...", () => BanPlayer(3600)), ("For 1 day...", () => BanPlayer(86400)), ("For 7 days...", () => BanPlayer(604800)),
            ("For 30 days...", () => BanPlayer(2592000)), ("Permanently...", () => BanPlayer(0)));
        playerList.ContextMenuStrip = Ui.NewMenu(("Kick...", KickPlayer), ("Ban for 1 day...", () => BanPlayer(86400)), ("Ban permanently...", () => BanPlayer(0)),
            ("Copy UID", () => { if (SelectedPlayer is { } p && p.Uid.Length > 0) Ui.Copy(p.Uid); }),
            ("Copy name", () => { if (SelectedPlayer is { } p) Ui.Copy(p.Name); }));
        Button Small(string t, Action a, string tip) { var b = Ui.NewButton(t, a); b.AutoSize = false; b.Size = new Size(84, 28); b.Padding = new Padding(0); b.Margin = new Padding(0, 0, 6, 6); Tips.Set(b, tip); return b; }
        Button banB = null;
        banB = Small("Ban...", () => banMenu.Show(banB, 0, banB.Height), "Bans the selected player (#ban create): for a time or for good. They're kicked at once.");
        playerButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        playerButtons.Controls.AddRange(new Control[]
        {
            Small("Kick", KickPlayer, "Kicks the selected player (#kick); they can join again."), banB,
            Small("Refresh", () => RefreshPlayers(true), "Asks the server for its players (#players); it's also done every 15 s."),
            Small("Ban list", () => RunCommand("#ban list"), "The server's bans (#ban list)."),
            Small("Unban...", Unban, "Lifts a ban by identity ID or name (#ban remove)."),
            Small("Start over", () =>
            {
                if (Ui.Ask("Start the scenario over (#restart)?\n\nIt saves, then begins a NEW playthrough from the scenario's start - the saved progress isn't " +
                    "continued. To restart and keep the progress, use RESTART at the top instead (with Continue from the last save on in the config).")) RunCommand("#restart");
            }, "#restart: the scenario starts over from the beginning as a new playthrough (it saves first, but doesn't continue that save). " +
                "RESTART at the top restarts the server instead, which continues the save when the config's Continue from the last save is on."),
        });
        players.Controls.Add(playerList);
        players.Controls.Add(playerButtons);
        players.Controls.Add(Ui.NewFlow(DockStyle.Top, playersTitle));

        // ---- The log
        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 12, 4) };
        logFind = new TextBox { Width = 220, PlaceholderText = "Find in the log" };
        logFindTimer.Tick += (s, e) => { logFindTimer.Stop(); RebuildShownLines(); };
        logFind.TextChanged += (s, e) => { logFindTimer.Stop(); logFindTimer.Start(); };
        logView = Ui.NewCombo(200, "Everything", "Errors and warnings", "RCON and launcher", "Everything + RCON traffic");
        logView.SelectedIndexChanged += (s, e) => RebuildShownLines();
        logFollow = new CheckBox { Text = "Follow", AutoSize = true, Checked = true, Margin = new Padding(10, 6, 3, 0) };
        logFollow.CheckedChanged += (s, e) => { if (logFollow.Checked) ScrollLogToEnd(); };
        conAutoRestart = new CheckBox { Text = "Restart it if it stops", AutoSize = true, Checked = App.Data.Advanced.AutoRestart, Margin = new Padding(10, 6, 3, 0) };
        conAutoRestart.CheckedChanged += (s, e) => { App.Data.Advanced.AutoRestart = conAutoRestart.Checked; SaveData(); };
        var openLog = Ui.NewButton("Open log", () => { if (tail != null) Launch.Open(tail.Path); else OpenServerLog(); });
        var clear = Ui.NewButton("Clear", () => { logLines.Clear(); RebuildShownLines(); });
        Tips.Set(logView, "Errors and warnings = the (E) and (W) lines; RCON and launcher = commands, their answers and the launcher's notes. " +
            "The server's own lines about RCON traffic (Received / Process Command) only show with Everything + RCON traffic.");
        Tips.Set(logFollow, "Keeps the newest line in view.");
        Tips.Set(conAutoRestart, "Starts the server again 15 s after it stops by itself (a crash), like start-server.bat. Not after STOP or #shutdown.");
        Tips.Set(openLog, "The server's console.log in Notepad (or your .log viewer).");
        Tips.Set(clear, "Empties this view (the log file stays).");
        var bar = Ui.NewFlow(DockStyle.Top, Ui.NewLabel("Find", 0, "dim"), logFind, Ui.NewLabel("Show", 0, "dim"), logView, logFollow, conAutoRestart, openLog, clear);
        bar.Padding = new Padding(0, 0, 0, 6);
        logList = new GridList { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.None, VirtualMode = true };
        Theme.DoubleBuffer(logList);
        logList.Columns.Add("", 800);
        logList.Resize += (s, e) => Theme.FillColumn(logList);
        Theme.GridStyle(logList, 18, false);
        logList.RetrieveVirtualItem += (s, e) => e.Item = new ListViewItem(e.ItemIndex < shownLines.Count ? logLines[shownLines[e.ItemIndex]].Text : "");
        logList.DrawSubItem += (s, e) => DrawLogLine(e);
        logList.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                var text = string.Join("\r\n", logList.SelectedIndices.Cast<int>().Where(i => i < shownLines.Count).Select(i => logLines[shownLines[i]].Text));
                if (text.Length > 0) Ui.Copy(text);
                e.Handled = true;
            }
        };
        logList.MouseWheel += (s, e) => { if (e.Delta > 0 && logFollow.Checked) logFollow.Checked = false; };
        logList.MouseMove += (s, e) =>
        {
            var it = logList.GetItemAt(5, e.Y);
            string tip = null;
            if (it != null && it.Index < shownLines.Count)
            {
                var text = logLines[shownLines[it.Index]].Text;
                if (TextRenderer.MeasureText(text, LogFont).Width > logList.ClientSize.Width - 20) tip = text;
            }
            Tips.ShowAt(logList, tip, e.Location);
        };
        logList.MouseLeave += (s, e) => Tips.ShowAt(logList, null, Point.Empty);
        logPanel.Controls.Add(logList);
        logPanel.Controls.Add(bar);

        // ---- RCON command line
        var cmdBar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 46, ColumnCount = 4, Padding = new Padding(14, 6, 14, 8) };
        cmdBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cmdBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cmdBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cmdBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rconState = new Label { AutoSize = true, Margin = new Padding(0, 7, 10, 0), Font = Theme.HeaderFont };
        cmdBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 8, 0), Font = LogFont };
        cmdBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendCommandBox(); }
            else if (e.KeyCode == Keys.Up && cmdHistory.Count > 0) { e.Handled = true; cmdHistoryPos = Math.Max(0, cmdHistoryPos - 1); cmdBox.Text = cmdHistory[cmdHistoryPos]; cmdBox.SelectionStart = cmdBox.Text.Length; }
            else if (e.KeyCode == Keys.Down && cmdHistory.Count > 0) { e.Handled = true; cmdHistoryPos = Math.Min(cmdHistory.Count, cmdHistoryPos + 1); cmdBox.Text = cmdHistoryPos < cmdHistory.Count ? cmdHistory[cmdHistoryPos] : ""; cmdBox.SelectionStart = cmdBox.Text.Length; }
        };
        Tips.Set(cmdBox, "An RCON command, Enter sends it (Up / Down: earlier ones). The # can be left out.\n\n" +
            "#players\n#kick <player #>\n#ban create <player # or identity ID or name> <seconds, 0 = for good> [reason]\n#ban remove <identity ID or name>\n" +
            "#ban list [page]\n#restart - the scenario starts over as a new playthrough (doesn't continue the save)\n#shutdown - the server saves and stops");
        sendButton = Ui.NewButton("Send", SendCommandBox, "primary");
        sendButton.AutoSize = false; sendButton.Size = new Size(90, 28); sendButton.Margin = new Padding(0, 1, 0, 0);
        enableRcon = Ui.NewButton("Turn on RCON...", EnableRconForConsole);
        enableRcon.AutoSize = false; enableRcon.Size = new Size(150, 28); enableRcon.Margin = new Padding(0, 1, 8, 0);
        Tips.Set(enableRcon, "Adds RCON to the server's config (this PC only: 127.0.0.1:19999, with a generated password). It takes effect when the server restarts.");
        cmdBar.Controls.Add(rconState, 0, 0); cmdBar.Controls.Add(cmdBox, 1, 0); cmdBar.Controls.Add(enableRcon, 2, 0); cmdBar.Controls.Add(sendButton, 3, 0);

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(logPanel);
        body.Controls.Add(players);
        page.Controls.Add(body);
        page.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, Tag = "line" });
        page.Controls.Add(cmdBar);
        page.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, Tag = "line" });
        page.Controls.Add(header);

        tailTimer.Tick += (s, e) => { try { ConsoleTick(); } catch (Exception ex) { Status.Text = "Console: " + ex.Message; } };
        return page;
    }

    ConsolePlayer SelectedPlayer => playerList.SelectedItems.Count > 0 ? playerList.SelectedItems[0].Tag as ConsolePlayer : null;

    // ---------------------------------------------------------------------------------------------------------------
    // The log view

    static readonly Regex RconEcho = new(@"\[RCON\] (\[CLIENT: \d+\] Received|Process Command:|\S+ Authorized as Client|Message acknowledged)");

    static char KindOf(string line) =>
        line.Contains("(E):") ? 'e' : line.Contains("(W):") ? 'w'
        : RconEcho.IsMatch(line) ? 'q'
        : line.Contains("Entered online game state") || line.Contains("registered with address") ? 'g' : 'n';

    bool LineShown(in LogLine l)
    {
        bool ok = logView.SelectedIndex switch { 1 => l.Kind is 'e' or 'w', 2 => l.Kind is 's' or 'r' or 'm' or 'l', 3 => true, _ => l.Kind != 'q' };
        var find = logFind.Text.Trim();
        return ok && (find.Length == 0 || l.Text.Contains(find, StringComparison.OrdinalIgnoreCase));
    }

    void AddLines(IEnumerable<string> lines, char kind = '\0')
    {
        // Only ever on the window's thread (an RCON answer can resume elsewhere when there's no UI context).
        if (InvokeRequired) { var copy = lines.ToList(); BeginInvoke(() => AddLines(copy, kind)); return; }
        int before = shownLines.Count;
        foreach (var text in lines)
        {
            var l = new LogLine { Text = text, Kind = kind == '\0' ? KindOf(text) : kind };
            logLines.Add(l);
            if (LineShown(l)) shownLines.Add(logLines.Count - 1);
        }
        if (logLines.Count > MaxLogLines) { logLines.RemoveRange(0, logLines.Count - MaxLogLines + 5000); RebuildShownLines(); return; }
        if (shownLines.Count != before) { logList.VirtualListSize = shownLines.Count; if (logFollow.Checked) ScrollLogToEnd(); }
    }

    void AddNote(string text) => AddLines(new[] { $"{DateTime.Now:HH:mm:ss}  — {text}" }, 'l');

    void RebuildShownLines()
    {
        var list = new List<int>(logLines.Count);
        for (int i = 0; i < logLines.Count; i++) if (LineShown(logLines[i])) list.Add(i);
        shownLines = list;
        logList.SelectedIndices.Clear();
        logList.VirtualListSize = shownLines.Count;
        logList.Invalidate();
        if (logFollow.Checked) ScrollLogToEnd();
    }

    void ScrollLogToEnd()
    {
        if (shownLines.Count > 0 && logList.IsHandleCreated && logList.Visible) logList.EnsureVisible(shownLines.Count - 1);
    }

    void DrawLogLine(DrawListViewSubItemEventArgs e)
    {
        var g = e.Graphics; var b = e.Bounds;
        bool sel = logList.SelectedIndices.Contains(e.ItemIndex);
        g.FillRectangle(sel ? Theme.SelectBrush : Theme.BackBrush, b);
        if (e.ItemIndex >= shownLines.Count) return;
        var l = logLines[shownLines[e.ItemIndex]];
        var color = l.Kind switch
        {
            'e' => "Bad", 'w' => "Warn", 'g' => "Good", 'q' => "Dim", 's' => "Link", 'r' => "Highlight", 'm' => "SubHeader", 'l' => "Accent", _ => "Label",
        };
        TextRenderer.DrawText(g, l.Text, LogFont, new Rectangle(b.X + 4, b.Y, b.Width - 8, b.Height), Theme.C(color),
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Which server, its log, its RCON

    // From the 3-second check of the running servers: the one started here, else the one already shown, else any.
    void UpdateConsoleProc(List<ServerProc> procs)
    {
        ServerProc pick = null;
        if (ownedProc != null && !HasExited(ownedProc)) pick = procs.FirstOrDefault(p => p.Id == ownedProc.Id) ?? (ConsoleProc?.Id == ownedProc.Id ? ConsoleProc : null);
        pick ??= ConsoleProc != null ? procs.FirstOrDefault(p => p.Id == ConsoleProc.Id) : null;
        pick ??= procs.FirstOrDefault(p => p.Mode == "public") ?? procs.FirstOrDefault();
        if (pick?.Id != ConsoleProc?.Id) SwitchConsole(pick, false);
        UpdateConsoleHeader();
    }

    static bool HasExited(Process p) { try { return p.HasExited; } catch { return true; } }

    void SwitchConsole(ServerProc proc, bool fresh)
    {
        if (tail != null) AddLines(tail.Read());
        CloseRcon();
        tail = null;
        ConsoleProc = proc;
        rconRetryAt = DateTime.MinValue;
        rconProblem = "";
        Players = new();
        UpdatePlayerList();
        if (proc != null)
        {
            var name = Launch.Info(proc).Name;
            AddNote(fresh ? $"Starting {name}" : $"Showing {name} (process {proc.Id})");
            OpenTail(fresh);
        }
        UpdateConsoleHeader();
    }

    // The console.log of the server shown: the one its process made (it appears a moment after the start). A server that
    // was already running shows its last part.
    void OpenTail(bool fromStart)
    {
        if (ConsoleProc == null || tail != null) return;
        var log = Launch.ServerLog(ConsoleProc);
        if (log == null || log.CreationTime < ConsoleProc.Started.AddSeconds(-Launch.LogTolerance)) return;
        long start = 0;
        if (!fromStart && log.Length > 400_000) start = log.Length - 400_000;
        tail = new LogTail(log.FullName, start);
        var lines = tail.Read();
        if (start > 0 && lines.Count > 0) lines.RemoveAt(0);   // most likely cut in half
        AddLines(lines);
    }

    void ConsoleTick()
    {
        if (tail == null && ConsoleProc != null) OpenTail(ownedProc != null && ConsoleProc.Id == ownedProc.Id);
        if (tail != null) { var lines = tail.Read(); if (lines.Count > 0) AddLines(lines); }

        if (ownedProc != null && !ownedReady && ConsoleProc?.Id == ownedProc.Id && Launch.Info(ConsoleProc).Ready) ownedReady = true;

        // The server started here stopped: by STOP / #shutdown, or by itself. Only a server that was up is started
        // again (a crash), and not more than 3 times in 10 minutes; one that stops while starting has a reason in its log
        // (a config it refuses, a missing mod...) that another try won't change.
        if (ownedProc != null && HasExited(ownedProc))
        {
            int code = 0; try { code = ownedProc.ExitCode; } catch { }
            if (tail != null) AddLines(tail.Read());
            autoRestarts.RemoveAll(t => t < DateTime.Now.AddMinutes(-10));
            if (stoppingOwned) AddNote("The server stopped.");
            else if (!ownedReady)
            {
                var errors = logLines.Skip(Math.Min(ownedLogStart, logLines.Count)).Where(l => l.Kind == 'e' && !l.Text.Contains("RESOURCES"))
                    .Select(l => Regex.Replace(l.Text, @"^\S+\s+\w+\s+\(E\):\s*", "").Trim()).Distinct().Take(4).ToList();
                AddNote($"The server stopped while starting (exit code {code}), so it isn't started again." + (errors.Count > 0 ? " Its log says: " + string.Join(" / ", errors) : ""));
                Status.Text = "The server couldn't start - the Console shows why.";
            }
            else if (App.Data.Advanced.AutoRestart && ownedConfig.Length > 0 && autoRestarts.Count < 3)
            {
                AddNote($"The server stopped by itself (exit code {code}). Starting it again in 15 s - STOP cancels that.");
                autoRestarts.Add(DateTime.Now);
                restartAt = DateTime.Now.AddSeconds(15);
            }
            else AddNote($"The server stopped by itself (exit code {code})" + (autoRestarts.Count >= 3 ? " for the third time in 10 minutes, so it isn't started again." : "."));
            ownedProc = null;
            stoppingOwned = false;
            CloseRcon();
            UpdateServerStatus();
        }
        if (restartAt != null && DateTime.Now >= restartAt)
        {
            restartAt = null;
            if (File.Exists(ownedConfig)) StartInApp(ownedConfig, false);
            else AddNote($"{ownedConfig} is gone, so the server wasn't started again.");
        }

        EnsureRcon();
        if (Rcon is { LoggedIn: true } && DateTime.Now >= playersAt) RefreshPlayers(false);
        if (restartAt != null || (ownedProc != null && !stoppingOwned)) UpdateConsoleHeader();
    }

    // Logs in to the shown server's RCON once it's up (and again if the connection is lost).
    async void EnsureRcon()
    {
        var proc = ConsoleProc;
        if (proc == null || rconConnecting || (Rcon != null && !Rcon.Closed && rconPid == proc.Id) || DateTime.Now < rconRetryAt) { UpdateRconState(); return; }
        var r = Launch.RconOf(proc);
        if (r == null) { rconProblem = proc.Mode == "public" ? "off" : "none"; rconRetryAt = DateTime.Now.AddSeconds(5); UpdateRconState(); return; }
        if (!Launch.Info(proc).Ready) { rconProblem = "waiting"; rconRetryAt = DateTime.Now.AddSeconds(1); UpdateRconState(); return; }
        rconConnecting = true;
        UpdateRconState();
        try
        {
            var c = await RconClient.ConnectAsync(r.Value.Host, r.Value.Port, r.Value.Password);
            if (ConsoleProc?.Id != proc.Id) { c.Dispose(); return; }
            CloseRcon();
            Rcon = c; rconPid = proc.Id; rconProblem = "";
            c.Message += m => BeginInvoke(() => AddLines(m.Split('\n').Select(x => x.TrimEnd('\r')), 'm'));
            c.Lost += () => BeginInvoke(() => { if (Rcon == c) { AddNote("RCON stopped answering - reconnecting."); CloseRcon(); rconRetryAt = DateTime.Now.AddSeconds(5); } });
            AddNote($"RCON connected ({r.Value.Host}:{r.Value.Port}).");
            playersAt = DateTime.MinValue;
        }
        catch (Exception ex)
        {
            rconProblem = ex is UnauthorizedAccessException ? "password" : "noanswer";
            rconRetryAt = DateTime.Now.AddSeconds(ex is UnauthorizedAccessException ? 60 : 10);
        }
        finally { rconConnecting = false; UpdateRconState(); }
    }

    void CloseRcon()
    {
        Rcon?.Dispose();
        Rcon = null; rconPid = 0;
    }

    void UpdateRconState()
    {
        if (rconState == null) return;
        bool on = Rcon is { LoggedIn: true, Closed: false };
        var (text, color, tip) = on ? ("● RCON", "Good", $"RCON connected to {Rcon.Host}:{Rcon.Port}.")
            : ConsoleProc == null ? ("● RCON", "Dim", "No server running.")
            : rconConnecting ? ("● RCON", "Warn", "Connecting...")
            : rconProblem switch
            {
                "off" => ("● RCON OFF", "Dim", "This server's config has no RCON. TURN ON RCON adds it (this PC only); it takes effect when the server restarts."),
                "none" => ("● RCON", "Dim", "A PLAY test server has no config, so no RCON: its log shows here, and STOP stops it cleanly."),
                "waiting" => ("● RCON", "Warn", "Connects when the server is up."),
                "password" => ("● RCON", "Bad", "The server refused the RCON password in its config (was the config changed after it started? RESTART picks it up)."),
                _ => ("● RCON", "Bad", "RCON doesn't answer (trying again every 10 s). Check the rcon address and port in the config."),
            };
        var key = $"{text}|{color}|{tip}|{on}|{ConsoleProc?.Id}|{rconProblem}";
        if (key == rconStateKey) return;
        rconStateKey = key;
        rconState.Text = text;
        rconState.ForeColor = Theme.C(color);
        Tips.Set(rconState, tip);
        cmdBox.Enabled = sendButton.Enabled = on;
        sendButton.BackColor = Theme.C(on ? "Accent" : "Field");
        sendButton.ForeColor = Theme.C(on ? "AccentText" : "Dim");
        cmdBox.PlaceholderText = on ? "#players, #kick 3, #ban create 3 3600 griefing, #restart ..." : ConsoleProc == null ? "No server running" : "RCON isn't connected";
        bool canEnable = ConsoleProc?.Mode == "public" && rconProblem == "off";
        enableRcon.Visible = canEnable;
        foreach (Control b in playerButtons.Controls) b.Enabled = on;
    }

    void UpdateConsoleHeader()
    {
        if (conTitle == null) return;
        var proc = ConsoleProc;
        bool running = proc != null && !(ownedProc != null && proc.Id == ownedProc.Id && HasExited(ownedProc));
        conStart.Visible = conConfig.Visible = !running && restartAt == null;
        conRestart.Visible = running;
        conStop.Visible = running || restartAt != null;
        if (!running)
        {
            conTitle.Text = restartAt != null ? Path.GetFileName(ownedConfig) : "No server running";
            conState.Text = restartAt != null ? $"● RESTARTING IN {Math.Max(0, (int)(restartAt.Value - DateTime.Now).TotalSeconds)} S" : "● STOPPED";
            conState.ForeColor = Theme.C(restartAt != null ? "Warn" : "Dim");
            conSub.Text = logLines.Count > 0 ? "the last log stays below" : "";
            if (conConfig.Items.Count != App.Configs.Count || !conConfig.Items.Cast<ConfigItem>().SequenceEqual(App.Configs)) FillConfigCombo();
            return;
        }
        var info = Launch.Info(proc);
        conTitle.Text = info.Name;
        conState.Text = info.Ready ? "● RUNNING" : "● STARTING";
        conState.ForeColor = Theme.C(info.Ready ? "Good" : "Warn");
        int minutes = (int)(DateTime.Now - proc.Started).TotalMinutes;
        const string dot = "   ·   ";
        var bits = new List<string> { Launch.AddressOf(proc) };
        if (Rcon is { LoggedIn: true }) bits.Add($"{Players.Count} player{(Players.Count == 1 ? "" : "s")}");
        bits.Add(minutes < 60 ? $"up {minutes} min" : $"up {minutes / 60} h {minutes % 60} min");
        bits.Add(proc.Mode == "public" ? Path.GetFileName(proc.Config) : proc.Mode == "local" ? "PLAY test (this PC only)" : "");
        conSub.Text = string.Join(dot, bits.Where(x => x.Length > 0));
        Tips.Set(conSub, $"Process {proc.Id}" + (info.JoinCode.Length > 0 ? $"\nDirect join code: {info.JoinCode}" : "") + (ownedProc?.Id == proc.Id ? "\nStarted by the launcher" : "\nStarted outside the launcher (start-server.bat or another program)"));
    }

    void FillConfigCombo()
    {
        var keep = conConfig.SelectedItem as ConfigItem ?? CurrentItem as ConfigItem;
        conConfig.BeginUpdate();
        conConfig.Items.Clear();

        foreach (var c in App.Configs) conConfig.Items.Add(c);
        conConfig.EndUpdate();
        conConfig.SelectedItem = keep != null && App.Configs.Contains(keep) ? keep : App.Configs.FirstOrDefault();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Starting, stopping, restarting

    // The server with a config, inside the launcher.
    void StartInApp(string configPath, bool interactive)
    {
        if (App.ServerDir == null) { Ui.Message("ArmaReforgerServer.exe wasn't found (start-server.bat's SERVER_DIR or Steam).", MessageBoxIcon.Error); return; }
        var p = Launch.StartServer(configPath);
        ownedProc = p; ownedConfig = configPath; stoppingOwned = false; restartAt = null; ownedReady = false;
        ownedLogStart = logLines.Count;
        SwitchConsole(new ServerProc { Id = p.Id, Config = configPath, Profile = Path.Combine(App.Root, "profile"), Started = DateTime.Now }, true);
        if (interactive) ShowPage("Console");
        UpdateServerStatus();
    }

    async void StopConsoleServer(bool ask)
    {
        if (ConsoleProc == null)
        {
            if (restartAt != null) { restartAt = null; AddNote("Restart cancelled."); UpdateConsoleHeader(); }
            return;
        }
        var proc = ConsoleProc;
        var name = Launch.Info(proc).Name;
        if (ask && !Ui.Ask($"Stop {name}? Anyone on it is disconnected. It saves its progress first.")) return;
        await StopServerAsync(proc);
    }

    async Task StopServerAsync(ServerProc proc)
    {
        if (ownedProc != null && ownedProc.Id == proc.Id) stoppingOwned = true;
        restartAt = null;
        pendingJoin = null;
        AddNote(Rcon is { LoggedIn: true } && rconPid == proc.Id ? "Stopping the server (RCON #shutdown)..." : "Stopping the server (Ctrl+C)...");
        conStop.Enabled = conRestart.Enabled = false;
        try
        {
            bool ok = await Launch.StopServerAsync(proc, rconPid == proc.Id ? Rcon : null);
            if (!ok) AddNote("The server didn't stop.");
            else if (ownedProc?.Id != proc.Id) AddNote("The server stopped.");
        }
        finally { conStop.Enabled = conRestart.Enabled = true; }
        ConsoleTick();
        UpdateServerStatus();
    }

    async void RestartConsoleServer()
    {
        var proc = ConsoleProc;
        if (proc == null) return;
        if (proc.Config.Length == 0) { Ui.Message("This server was started without a config (a PLAY test): STOP it and PLAY again."); return; }
        if (!Ui.Ask($"Restart {Launch.Info(proc).Name}? Anyone on it is disconnected; it saves first and starts again with {Path.GetFileName(proc.Config)}.")) return;
        var config = proc.Config;
        await StopServerAsync(proc);
        StartInApp(config, false);
    }

    // Stops every server using the port (asked first). False when the answer was no.
    bool ConfirmPortFree(int port)
    {
        var using_ = Launch.Servers().Where(p => Launch.PortOf(p) == port).ToList();
        if (using_.Count == 0) return true;
        var names = string.Join("\n", using_.Select(p => $"{Launch.Info(p).Name} (process {p.Id})"));
        if (!Ui.Ask($"Port {port} is already used by:\n\n{names}\n\nStop it (it saves first) and go on?")) return false;
        foreach (var p in using_)
        {
            var t = StopServerAsync(p);
            SetBusy(true, "Stopping the server...");
            try { while (!t.IsCompleted) { Application.DoEvents(); Thread.Sleep(20); } } finally { SetBusy(false); }
        }
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // RCON: commands, players

    void SendCommandBox()
    {
        var text = cmdBox.Text.Trim();
        if (text.Length == 0) return;
        cmdBox.Text = "";
        cmdHistory.Remove(text);
        cmdHistory.Add(text);
        cmdHistoryPos = cmdHistory.Count;
        RunCommand(text);
    }

    public async void RunCommand(string command)
    {
        await RunCommandAsync(command);
    }

    public async Task<string> RunCommandAsync(string command)
    {
        command = command.Trim();
        if (!command.StartsWith('#')) command = "#" + command;
        if (Rcon is not { LoggedIn: true }) { AddNote("RCON isn't connected."); return null; }
        if (command.StartsWith("#shutdown", StringComparison.OrdinalIgnoreCase) && ownedProc != null && ConsoleProc?.Id == ownedProc.Id) stoppingOwned = true;
        AddLines(new[] { "> " + command }, 's');
        try
        {
            var result = await Rcon.SendAsync(command);
            AddLines(result.Length > 0 ? result.TrimEnd('\n').Split('\n').Select(x => "  " + x.TrimEnd('\r')) : new[] { "  (done - no reply)" }, 'r');
            if (command.StartsWith("#players", StringComparison.OrdinalIgnoreCase)) SetPlayers(result);
            return result;
        }
        catch (Exception ex) { AddLines(new[] { "  " + ex.Message }, 'e'); return null; }
    }

    async void RefreshPlayers(bool shown)
    {
        playersAt = DateTime.Now.AddSeconds(15);
        if (shown) { await RunCommandAsync("#players"); return; }
        var c = Rcon;
        if (c is not { LoggedIn: true }) return;
        try { SetPlayers(await c.SendAsync("#players")); } catch { }
    }

    static readonly Regex PlayerLine = new(@"^\s*#?(\d+)\s*;\s*([^;]*?)\s*;\s*(.*?)\s*$");

    // "Players on server: [Player#] ; [Player UID] ; [Player Name]" and a line per player in that order.
    void SetPlayers(string result)
    {
        if (InvokeRequired) { BeginInvoke(() => SetPlayers(result)); return; }
        if (result == null || !result.Contains("Players on server", StringComparison.OrdinalIgnoreCase)) return;
        var list = new List<ConsolePlayer>();
        foreach (var line in result.Split('\n'))
        {
            if (line.Contains("[Player")) continue;
            var m = PlayerLine.Match(line.TrimEnd('\r'));
            if (m.Success) list.Add(new ConsolePlayer { Id = int.Parse(m.Groups[1].Value), Uid = m.Groups[2].Value, Name = m.Groups[3].Value });
        }
        Players = list;
        UpdatePlayerList();
        UpdateConsoleHeader();
    }

    void UpdatePlayerList()
    {
        if (playerList == null) return;
        var keep = SelectedPlayer?.Id;
        playerList.BeginUpdate();
        playerList.Items.Clear();
        foreach (var p in Players.OrderBy(p => p.Id))
        {
            var it = new ListViewItem(p.Id.ToString()) { Tag = p };
            it.SubItems.Add(p.Name);
            playerList.Items.Add(it);
            if (p.Id == keep) it.Selected = true;
        }
        playerList.EndUpdate();
        playersTitle.Text = Rcon is { LoggedIn: true } ? $"Players ({Players.Count})" : "Players";
    }

    void KickPlayer()
    {
        if (SelectedPlayer is not { } p) { Ui.Message("Pick a player first."); return; }
        if (!Ui.Ask($"Kick {p.Name}?")) return;
        RunCommand($"#kick {p.Id}");
        playersAt = DateTime.Now.AddSeconds(2);
    }

    void BanPlayer(int seconds)
    {
        if (SelectedPlayer is not { } p) { Ui.Message("Pick a player first."); return; }
        var how = seconds == 0 ? "for good" : seconds >= 86400 ? $"for {seconds / 86400} day{(seconds >= 172800 ? "s" : "")}" : $"for {seconds / 3600} hour{(seconds >= 7200 ? "s" : "")}";
        var reason = Ui.InputOrCancel($"Ban {p.Name} {how}? They're kicked at once.\n\nReason (optional):", "");
        if (reason == null) return;
        // By identity ID when the server gave it (it outlasts the session), else the player number.
        var who = p.Uid.Length == 36 ? p.Uid : p.Id.ToString();
        RunCommand($"#ban create {who} {seconds}" + (reason.Length > 0 ? " " + reason.Replace("\n", " ") : ""));
        playersAt = DateTime.Now.AddSeconds(3);
    }

    void Unban()
    {
        var who = Ui.Input("Identity ID or name to unban (#ban list shows them):", "").Trim();
        if (who.Length == 0) return;
        RunCommand($"#ban remove {(who.Contains(' ') ? "\"" + who + "\"" : who)}");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // RCON settings

    // Adds RCON to a config, or completes one the server would refuse: what's missing becomes this PC only (127.0.0.1),
    // port 19999, a generated password, admin; settings already there stay. The config is saved.
    public bool EnableRcon(ConfigItem item)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var rcon = Json.Section(item.Config, "rcon");
        if (Json.Str(rcon, "address").Length == 0) rcon["address"] = "127.0.0.1";
        if (Json.Long(rcon, "port") <= 0) rcon["port"] = 19999;
        if (Json.Str(rcon, "password").Length < 3)
            rcon["password"] = new string(Enumerable.Range(0, 16).Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        if (Json.Str(rcon, "permission").Length == 0) rcon["permission"] = "admin";
        App.SaveConfig(item);
        var node = FindNode(item);
        if (node != null) node.Text = NodeText(item);
        staleTabs.Add("Configuration");
        UpdateTitle();
        return true;
    }

    // Before a config starts: RCON settings the server would refuse get completed (or nothing starts), and - for a server
    // run here - a config without RCON is offered it once. False when the start should stop.
    bool PrepareRcon(ConfigItem item, bool offer)
    {
        if (RconIncomplete(item.Config))
        {
            var has = string.Join(", ", (Json.Get(item.Config, "rcon") as System.Text.Json.Nodes.JsonObject)?.Select(kv => kv.Key) ?? Enumerable.Empty<string>());
            if (!Ui.Ask($"{item.Name} has RCON settings ({(has.Length > 0 ? has : "empty")}) but no RCON address or password, so the server refuses the whole " +
                "config (\"required params are missing: address password\").\n\nComplete them? What's missing becomes this PC only (127.0.0.1), port 19999 and " +
                "a generated password; the RCON settings already there stay.\n\n(No: nothing starts - fix or remove them on its Configuration tab.)")) return false;
            EnableRcon(item);
            Status.Text = $"Completed the RCON settings of {item.Name}.";
            return true;
        }
        if (!offer || Json.Has(item.Config, "rcon") || App.Data.RconOffered.Contains(item.Name, StringComparer.OrdinalIgnoreCase)) return true;
        if (Ui.Ask($"Turn on RCON for {item.Name}?\n\nThe Console tab then lists the players, kicks and bans, takes server commands and stops the server " +
            "cleanly. It's added to the config for this PC only (127.0.0.1:19999) with a generated password.\n\n(No: you won't be asked again for this config.)"))
            EnableRcon(item);
        else { App.Data.RconOffered.Add(item.Name); SaveData(); }
        return true;
    }

    void EnableRconForConsole()
    {
        var proc = ConsoleProc;
        if (proc == null || proc.Config.Length == 0) return;
        var item = App.Configs.FirstOrDefault(c => !string.IsNullOrEmpty(c.Path) && c.Path.Equals(proc.Config, StringComparison.OrdinalIgnoreCase));
        if (item == null) { Ui.Message($"{proc.Config} isn't one of the configs here, so the launcher can't change it."); return; }
        if (item.Changed && !Ui.Ask($"{item.Name} has unsaved changes. They're saved along with RCON - go on?")) return;
        EnableRcon(item);
        App.Data.RconOffered.Remove(item.Name);
        SaveData();
        AddNote($"RCON added to {item.Name} (127.0.0.1:19999).");
        if (Ui.Ask("RCON takes effect when the server restarts. Restart it now? Anyone on it is disconnected; it saves first.")) RestartWithoutAsking(proc);
    }

    async void RestartWithoutAsking(ServerProc proc)
    {
        var config = proc.Config;
        await StopServerAsync(proc);
        StartInApp(config, false);
    }

    // Closing the launcher while its server runs: stop it, or leave it running without a window.
    bool AskAboutServerOnClose(FormClosingEventArgs e)
    {
        if (ownedProc == null || HasExited(ownedProc) || ConsoleProc?.Id != ownedProc.Id) return true;
        var name = Launch.Info(ConsoleProc).Name;
        var answer = Ui.Message($"{name} is running inside the launcher.\n\nYes - stop it (it saves first)\n" +
            "No - leave it running in the background, without a window: the Console tab shows it again next time (it won't restart by itself meanwhile)\n" +
            "Cancel - keep the launcher open", MessageBoxIcon.Question, MessageBoxButtons.YesNoCancel);
        if (answer == DialogResult.Cancel) { e.Cancel = true; return false; }
        if (answer == DialogResult.Yes)
        {
            e.Cancel = true;
            var proc = ConsoleProc;
            BeginInvoke(async () => { await StopServerAsync(proc); ownedProc = null; Close(); });
            return false;
        }
        ownedProc = null;
        return true;
    }
}
