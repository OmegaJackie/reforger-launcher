using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReforgerLauncher;

static class Native
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("uxtheme.dll", EntryPoint = "#133")] static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);
    [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#104")] static extern void RefreshImmersiveColorPolicyState();
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    static bool darkReady;

    // Dark scrollbars (Windows' own dark theme for menus and scrollbars).
    public static void InitDarkMode()
    {
        if (darkReady) return;
        darkReady = true;
        try { SetPreferredAppMode(2); RefreshImmersiveColorPolicyState(); } catch { }
    }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }

    public static int HeaderHeight(ListView list)
    {
        var header = SendMessage(list.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);   // LVM_GETHEADER
        return header != IntPtr.Zero && GetWindowRect(header, out var r) ? r.Bottom - r.Top : 0;
    }

    // Lists get dark scrollbars from the dark items-view theme (GridList paints over the column lines it adds).
    public static void DarkScroll(Control c)
    {
        try
        {
            AllowDarkModeForWindow(c.Handle, true);
            SetWindowTheme(c.Handle, c is ListView ? "DarkMode_ItemsView" : "DarkMode_Explorer", null);
            SendMessage(c.Handle, 0x031A, IntPtr.Zero, IntPtr.Zero);   // WM_THEMECHANGED
        }
        catch { }
    }

    // Arma 3's amber (#C4821A) title bar and window border with dark caption text (Windows 11 DWM colours; older Windows
    // keeps a dark title bar).
    public static void WindowChrome(Form f)
    {
        f.HandleCreated += (s, e) =>
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(f.Handle, 20, ref on, 4);   // dark mode
                foreach (var (attr, color) in new[] { (35, "Title"), (34, "Title"), (36, "TitleText") })   // caption, border, caption text
                {
                    var c = Theme.C(color);
                    int bgr = c.R | (c.G << 8) | (c.B << 16);
                    DwmSetWindowAttribute(f.Handle, attr, ref bgr, 4);
                }
            }
            catch { }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }

    // Another process's command line (ProcessCommandLineInformation, Windows 8.1+), or "" when it can't be read.
    public static string CommandLine(int pid)
    {
        IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return "";
        try
        {
            NtQueryInformationProcess(h, 60, IntPtr.Zero, 0, out int need);
            if (need <= 0) return "";
            IntPtr buf = Marshal.AllocHGlobal(need);
            try
            {
                if (NtQueryInformationProcess(h, 60, buf, need, out _) != 0) return "";
                var us = Marshal.PtrToStructure<UnicodeString>(buf);
                return us.Buffer == IntPtr.Zero ? "" : Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return ""; }
        finally { CloseHandle(h); }
    }

    public static IEnumerable<(Process Proc, string CommandLine)> ProcessesNamed(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
            yield return (p, CommandLine(p.Id));
    }

    // Windows command-line quoting (the rules CommandLineToArgvW reads back).
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        var sb = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (char ch in arg)
        {
            if (ch == '\\') { slashes++; continue; }
            if (ch == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); slashes = 0; continue; }
            sb.Append('\\', slashes); slashes = 0; sb.Append(ch);
        }
        sb.Append('\\', slashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    public static string JoinArgs(IEnumerable<string> args) => string.Join(" ", args.Where(a => a != null).Select(Quote));
}
