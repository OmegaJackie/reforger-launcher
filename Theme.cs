using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ReforgerLauncher;

// Arma Reforger's and Arma 3's colours. Reforger's own UI palette (UIColors / GUIColors in data007.pak
// scripts/Game/Global/Constants.c): the "Reforger orange" #E2A74F as the accent (CONTRAST_COLOR), #FFCB7B highlighted
// text, #FFDC69 sub-headers, #43C25D confirm green, #F94343 warning red, #EACB83 slight-warning yellow, #0080FF online
// blue, #2A2A2A edit fields, selections a 30 % orange tint over the dark (CONTRAST_DEFAULT). Arma 3's default UI colour
// (0.77, 0.51, 0.08 = #C4821A amber) is the title bar and window border. The layout stays A3Launcher's: big thin tabs,
// grids with small capital headers over a (here orange) rule.
public static class Theme
{
    static readonly Dictionary<string, Color> colors = new()
    {
        ["Back"] = Hex("#1e1e1e"), ["Side"] = Hex("#191919"), ["Bar"] = Hex("#141414"), ["Panel"] = Hex("#1e1e1e"),
        ["Field"] = Hex("#2a2a2a"), ["Hover"] = Hex("#3a3a3a"), ["Line"] = Hex("#333333"),
        ["Text"] = Hex("#f2f2f2"), ["Dim"] = Hex("#8c8c8c"), ["Label"] = Hex("#bdbdbc"), ["SubText"] = Hex("#9a9a9a"),
        ["Accent"] = Hex("#e2a74f"), ["AccentHover"] = Hex("#efc78b"), ["AccentText"] = Hex("#1a1a1a"), ["Select"] = Hex("#594729"),
        ["Highlight"] = Hex("#ffcb7b"), ["SubHeader"] = Hex("#ffdc69"), ["Title"] = Hex("#c4821a"), ["TitleText"] = Hex("#141414"),
        ["TabOn"] = Hex("#e2a74f"), ["TabOff"] = Hex("#8c8c8c"), ["Button"] = Hex("#2a2a2a"), ["ButtonLine"] = Hex("#4d4d4d"),
        ["Bad"] = Hex("#f94343"), ["Good"] = Hex("#43c25d"), ["Warn"] = Hex("#eacb83"), ["Info"] = Hex("#0080ff"),
        ["Link"] = Hex("#7aafff"), ["LinkHover"] = Hex("#b8d4ff"), ["Sage"] = Hex("#565c54"), ["FavName"] = Hex("#ffdc69"),
    };

    static Color Hex(string h) => ColorTranslator.FromHtml(h);
    public static Color C(string name) => colors[name];

    public static readonly Font Base = new("Segoe UI", 9.5f);
    public static readonly Font ButtonFont = new("Segoe UI Semibold", 8.25f);
    public static readonly Font HeaderFont = new("Segoe UI Semibold", 8.25f);
    public static readonly Font NameFont = new("Segoe UI Semibold", 9.75f);
    public static readonly Font SmallFont = new("Segoe UI", 8.25f);
    public static readonly Font GroupFont = new("Segoe UI Semibold", 8.25f);
    public static readonly Font TabFont = new("Segoe UI Light", 19f);
    public static readonly Font SubTabFont = new("Segoe UI Light", 14f);
    public static readonly Font Heading = new("Segoe UI", 12f);
    public static readonly Font Section = new("Segoe UI Semibold", 10.5f);

    public static readonly SolidBrush BackBrush = new(C("Back"));
    public static readonly SolidBrush SideBrush = new(C("Side"));
    public static readonly SolidBrush AccentBrush = new(C("Accent"));
    public static readonly SolidBrush SelectBrush = new(C("Select"));
    public static readonly SolidBrush RuleBrush = new(C("Accent"));
    public static readonly Pen LinePen = new(C("Line"));
    public static readonly Pen BoxPen = new(C("Label"));
    public static readonly Pen TickPen = new(C("Accent"), 2);
    public static readonly Pen TickPenSelected = new(C("Text"), 2);

    // Icon glyphs: Segoe MDL2 Assets on Windows 10/11, plain symbols otherwise.
    public static readonly Font GlyphFont, SmallGlyphFont;
    public static readonly Dictionary<string, string> Glyphs;

    static string G(int code) => ((char)code).ToString();

    static Theme()
    {
        bool mdl2 = new InstalledFontCollection().Families.Any(f => f.Name == "Segoe MDL2 Assets");
        if (mdl2)
        {
            GlyphFont = new Font("Segoe MDL2 Assets", 11f);
            SmallGlyphFont = new Font("Segoe MDL2 Assets", 9f);
            Glyphs = new()
            {
                ["Star"] = G(0xE734), ["StarOn"] = G(0xE735), ["Lock"] = G(0xE72E), ["Refresh"] = G(0xE72C), ["Busy"] = G(0xE895),
                ["People"] = G(0xE716), ["Play"] = G(0xE768), ["Download"] = G(0xE896), ["Folder"] = G(0xF12B), ["FolderOpen"] = G(0xED25),
                ["Config"] = G(0xE713), ["Preset"] = G(0xE8FD), ["Down"] = G(0xE70D), ["Right"] = G(0xE76C),
            };
        }
        else
        {
            GlyphFont = new Font("Segoe UI Symbol", 11f);
            SmallGlyphFont = new Font("Segoe UI Symbol", 9f);
            Glyphs = new()
            {
                ["Star"] = "☆", ["StarOn"] = "★", ["Lock"] = "#", ["Refresh"] = "⟳", ["Busy"] = "…", ["People"] = "☺",
                ["Play"] = "▶", ["Download"] = "↓", ["Folder"] = "□", ["FolderOpen"] = "□", ["Config"] = "⚙", ["Preset"] = "≡",
                ["Down"] = "▾", ["Right"] = "▸",
            };
        }
    }

    // Applies the launcher's colours to a control and everything in it. Tag strings say what a control is:
    // buttons "primary" / "nav", labels "dim" / "accent" / "sub" / "bad" / "label" / "link", panels "line".
    public static void Style(Control control)
    {
        var tag = control.Tag as string;
        switch (control)
        {
            case Panel p when tag == "line": p.BackColor = C("Line"); break;
            case Panel p when tag == "rule": p.BackColor = C("Accent"); break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Cursor = Cursors.Hand;
                // Small bold capitals; the main action filled Reforger orange, the others dark with a grey edge that turns
                // orange on hover (Reforger's buttons).
                if (b.Font.Name == "Segoe UI" && b.Font.Size == 9.5f && b.Font.Style == FontStyle.Regular) b.Font = ButtonFont;
                b.Text = b.Text.ToUpperInvariant();
                if (tag == "primary")
                {
                    b.BackColor = C("Accent"); b.ForeColor = C("AccentText");
                    b.FlatAppearance.MouseOverBackColor = C("AccentHover"); b.FlatAppearance.MouseDownBackColor = C("Accent");
                }
                else
                {
                    b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = C("ButtonLine");
                    b.BackColor = C("Button"); b.ForeColor = C("Text");
                    b.FlatAppearance.MouseOverBackColor = C("Select"); b.FlatAppearance.MouseDownBackColor = C("Field");
                    b.MouseEnter += (s, e) => b.FlatAppearance.BorderColor = C("Accent");
                    b.MouseLeave += (s, e) => b.FlatAppearance.BorderColor = C("ButtonLine");
                }
                break;
            case TextBox t: t.BackColor = C("Field"); t.ForeColor = C("Text"); t.BorderStyle = BorderStyle.FixedSingle; break;
            case ComboBox cb: cb.FlatStyle = FlatStyle.Flat; cb.BackColor = C("Field"); cb.ForeColor = C("Text"); break;
            case NumericUpDown n: n.BackColor = C("Field"); n.ForeColor = C("Text"); n.BorderStyle = BorderStyle.FixedSingle; break;
            case CheckBox ch: ch.ForeColor = C("Text"); ch.FlatStyle = FlatStyle.Flat; ch.FlatAppearance.CheckedBackColor = C("Select"); break;
            case TrackBar tb: tb.BackColor = C("Back"); break;
            case ListView lv:
                lv.BackColor = C("Panel"); lv.ForeColor = C("Text"); lv.BorderStyle = BorderStyle.None;
                lv.HandleCreated += (s, e) => Native.DarkScroll(lv);
                break;
            case TreeView tv:
                tv.BackColor = C("Side"); tv.ForeColor = C("Text"); tv.BorderStyle = BorderStyle.None;
                tv.HandleCreated += (s, e) => Native.DarkScroll(tv);
                break;
            case Label l:
                l.ForeColor = tag switch
                {
                    "dim" => C("Dim"), "accent" => C("Highlight"), "sub" => C("SubHeader"), "bad" => C("Bad"), "label" => C("Label"), "link" => C("Link"),
                    _ => C("Text"),
                };
                break;
        }
        foreach (Control child in control.Controls) Style(child);
    }

    // Lists drawn like A3Launcher's grids in Reforger's colours: small bold capitals over an orange rule as headers, rows on
    // the plain background, the selected one tinted orange, orange ticks. A taller row comes from a 1-pixel-wide image
    // list of that height. Without cells, the caller draws the cells itself.
    public static void GridStyle(ListView list, int rowHeight = 26, bool cells = true)
    {
        list.OwnerDraw = true;
        list.SmallImageList = new ImageList { ImageSize = new Size(1, rowHeight) };
        list.DrawColumnHeader += (s, e) => DrawHeader(e);
        list.DrawItem += (s, e) => e.DrawDefault = false;
        if (cells) list.DrawSubItem += (s, e) => DrawCell(list, e);
        // Hovering makes the list repaint a row's first cell on its own; repaint the whole row so it stays right.
        int hot = -1;
        list.MouseMove += (s, e) =>
        {
            int idx = list.GetItemAt(5, e.Y)?.Index ?? -1;
            if (idx == hot) return;
            int count = list.VirtualMode ? list.VirtualListSize : list.Items.Count;
            if (hot >= 0 && hot < count) list.Invalidate(list.GetItemRect(hot));
            if (idx >= 0) list.Invalidate(list.GetItemRect(idx));
            hot = idx;
        };
    }

    public static void DrawHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        g.FillRectangle(BackBrush, e.Bounds);
        var r = e.Bounds; r.X += 6; r.Width -= 12; r.Height -= 3;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
        TextRenderer.DrawText(g, e.Header.Text.ToUpperInvariant(), HeaderFont, r, C("Label"), flags);
        g.FillRectangle(RuleBrush, e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 2);
        if (e.Header.Text.Length > 0) g.DrawLine(LinePen, e.Bounds.Right - 1, e.Bounds.Top + 5, e.Bounds.Right - 1, e.Bounds.Bottom - 7);
    }

    public static bool IsSelected(ListView list, DrawListViewSubItemEventArgs e) =>
        list.VirtualMode ? list.SelectedIndices.Contains(e.ItemIndex) : e.Item.Selected;

    public static void DrawCell(ListView list, DrawListViewSubItemEventArgs e)
    {
        var g = e.Graphics; var b = e.Bounds;
        bool sel = IsSelected(list, e);
        g.FillRectangle(sel ? SelectBrush : BackBrush, b);
        if (sel && e.ColumnIndex == 0) g.FillRectangle(AccentBrush, b.X, b.Y, 3, b.Height);
        int x = b.X + 6;
        if (e.ColumnIndex == 0 && (list.CheckBoxes || list is GridList { CheckColumn: true }))
        {
            // The tick box (GridList toggles it on a click left of x + 22).
            var box = new Rectangle(b.X + 6, b.Y + (b.Height - 13) / 2, 12, 12);
            g.DrawRectangle(BoxPen, box);
            if (e.Item.Checked)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLines(sel ? TickPenSelected : TickPen, new[] { new Point(box.X + 2, box.Y + 6), new Point(box.X + 5, box.Y + 9), new Point(box.X + 10, box.Y + 3) });
                g.SmoothingMode = SmoothingMode.Default;
            }
            x = b.X + 25;
        }
        var color = sel ? Color.White : e.Item.UseItemStyleForSubItems ? e.Item.ForeColor : e.SubItem.ForeColor;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        if (list.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
        TextRenderer.DrawText(g, e.SubItem.Text, list.Font, new Rectangle(x, b.Y, Math.Max(0, b.Right - x - 6), b.Height), color, flags);
    }

    // The configs tree: group headings in small capitals (sub-header yellow), folders with a chevron and folder icon,
    // configs with a gear, presets with a list icon; the selected one tinted orange with an orange edge.
    public static void DrawNode(TreeView tree, DrawTreeNodeEventArgs e)
    {
        if (e.Bounds.Height <= 0) return;
        var g = e.Graphics;
        var r = new Rectangle(0, e.Bounds.Y, tree.ClientSize.Width, e.Bounds.Height);
        bool group = e.Node.Tag is string;
        bool sel = e.Node.IsSelected && !group;
        g.FillRectangle(sel ? SelectBrush : SideBrush, r);
        if (sel) g.FillRectangle(AccentBrush, 0, r.Y, 3, r.Height);
        const TextFormatFlags one = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        int x = 6 + e.Node.Level * 16;
        if (group)
        {
            TextRenderer.DrawText(g, e.Node.Text, GroupFont, new Rectangle(x, r.Y, Math.Max(0, r.Width - x - 4), r.Height), C("SubHeader"), one);
            return;
        }
        string glyph; Color glyphColor;
        const TextFormatFlags icon = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        switch (e.Node.Tag)
        {
            case ConfigFolder:
                TextRenderer.DrawText(g, Glyphs[e.Node.IsExpanded ? "Down" : "Right"], SmallGlyphFont, new Rectangle(x - 2, r.Y, 14, r.Height), C("Dim"), icon);
                x += 14;
                glyph = Glyphs[e.Node.IsExpanded ? "FolderOpen" : "Folder"]; glyphColor = C("Accent");
                break;
            case ConfigItem: glyph = Glyphs["Config"]; glyphColor = sel ? C("Highlight") : C("Label"); break;
            default: glyph = Glyphs["Preset"]; glyphColor = sel ? C("Highlight") : C("Label"); break;
        }
        TextRenderer.DrawText(g, glyph, SmallGlyphFont, new Rectangle(x, r.Y, 18, r.Height), glyphColor, icon);
        x += 22;
        var color = sel ? Color.White : e.Node.ForeColor != Color.Empty ? e.Node.ForeColor : C("Text");
        TextRenderer.DrawText(g, e.Node.Text, tree.Font, new Rectangle(x, r.Y, Math.Max(0, r.Width - x - 4), r.Height), color, one);
    }

    // Menus (right-click, New..., Export...) dark with an orange-tinted selection, like the rest.
    public static void StyleMenu(ToolStripDropDown menu)
    {
        menu.Renderer = MenuRenderer.Instance;
        menu.BackColor = C("Field");
        menu.ForeColor = C("Text");
        foreach (ToolStripItem i in menu.Items)
        {
            i.ForeColor = C("Text");
            if (i is ToolStripMenuItem mi && mi.HasDropDownItems) StyleMenu(mi.DropDown);
        }
    }

    sealed class MenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => C("Select");
        public override Color MenuItemBorder => C("Accent");
        public override Color MenuBorder => C("ButtonLine");
        public override Color ToolStripDropDownBackground => C("Field");
        public override Color ImageMarginGradientBegin => C("Field");
        public override Color ImageMarginGradientMiddle => C("Field");
        public override Color ImageMarginGradientEnd => C("Field");
        public override Color MenuItemSelectedGradientBegin => C("Select");
        public override Color MenuItemSelectedGradientEnd => C("Select");
        public override Color MenuItemPressedGradientBegin => C("Select");
        public override Color MenuItemPressedGradientEnd => C("Select");
        public override Color SeparatorDark => C("Line");
        public override Color SeparatorLight => C("Line");
    }

    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public static readonly MenuRenderer Instance = new();
        MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? C("Text") : C("Dim"); base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = C("Label"); base.OnRenderArrow(e); }
    }

    // The first column (or another) takes whatever width the others leave. Runs from Resize: a list being torn down still
    // gets a last resize with no columns.
    public static void FillColumn(ListView list, int fill = 0)
    {
        if (list == null || list.IsDisposed || list.Disposing || list.Columns.Count <= fill) return;
        int others = 0;
        for (int i = 0; i < list.Columns.Count; i++) if (i != fill) others += list.Columns[i].Width;
        list.Columns[fill].Width = Math.Max(220, list.ClientSize.Width - others - 2);
    }

    public static void DoubleBuffer(Control c) =>
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(c, true);

    // A placeholder picture for scenarios without one.
    public static Bitmap Placeholder(string title, string subtitle)
    {
        int w = 640, h = 360;
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var rect = new Rectangle(0, 0, w, h);
        using (var brush = new LinearGradientBrush(rect, Hex("#4a3a1f"), Hex("#161616"), 35f)) g.FillRectangle(brush, rect);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using (var f = new Font("Segoe UI Semibold", 30f)) using (var b = new SolidBrush(C("Accent"))) g.DrawString(title, f, b, new RectangleF(20, 40, w - 40, 200), sf);
        if (!string.IsNullOrEmpty(subtitle))
            using (var f = new Font("Segoe UI", 18f)) using (var b = new SolidBrush(C("Text"))) g.DrawString(subtitle.ToUpperInvariant(), f, b, new RectangleF(20, 210, w - 40, 80), sf);
        return bmp;
    }
}

// Hover help: the descriptions live in tooltips on the controls they describe (dark, orange-edged, wrapped).
public static class Tips
{
    static ToolTip tip;
    static ToolTip cell;

    static ToolTip Make()
    {
        var t = new ToolTip { OwnerDraw = true, InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 30000, UseFading = false, UseAnimation = false };
        t.Popup += (s, e) =>
        {
            var text = t.GetToolTip(e.AssociatedControl);
            if (string.IsNullOrEmpty(text) && e.AssociatedControl != null && cellText.TryGetValue(e.AssociatedControl, out var ct)) text = ct;
            var size = TextRenderer.MeasureText(text ?? "", Theme.Base, new Size(420, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(Math.Min(440, size.Width + 20), size.Height + 14);
        };
        t.Draw += (s, e) =>
        {
            using (var b = new SolidBrush(Theme.C("Field"))) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.C("Accent"))) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            var r = new Rectangle(10, 7, e.Bounds.Width - 20, e.Bounds.Height - 14);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.Base, r, Theme.C("Text"), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        };
        return t;
    }

    static readonly Dictionary<Control, string> cellText = new();

    public static void Set(Control c, string text)
    {
        if (c == null || string.IsNullOrEmpty(text)) return;
        tip ??= Make();
        tip.SetToolTip(c, text);
    }

    public static void Set(string text, params Control[] controls) { foreach (var c in controls) Set(c, text); }

    // A tip for a part of a control (a list cell), shown at the mouse; null hides it.
    public static void ShowAt(Control c, string text, Point at)
    {
        cell ??= Make();
        if (string.IsNullOrEmpty(text)) { cell.Hide(c); cellText.Remove(c); return; }
        if (cellText.TryGetValue(c, out var shown) && shown == text) return;
        cellText[c] = text;
        cell.Show(text, c, at.X + 14, at.Y + 20, 8000);
    }
}

// A list that paints the empty part below its rows itself: the dark theme (which gives it dark scrollbars) draws faint
// column lines there. Also on WM_PRINTCLIENT, so pictures of the window match the screen.
// UseRows() makes it a virtual list over an array of rows (the list asks only for the rows it shows: 1,250 mods fill in
// milliseconds instead of seconds), and CheckColumn gives it ticks drawn and toggled here (click or Space).
public class GridList : ListView
{
    ListViewItem[] rows = Array.Empty<ListViewItem>();
    public bool CheckColumn;
    public event Action<int> CheckToggled;

    public void UseRows()
    {
        VirtualMode = true;
        RetrieveVirtualItem += (s, e) => e.Item = e.ItemIndex < rows.Length ? rows[e.ItemIndex] : new ListViewItem("");
    }

    public ListViewItem[] Rows
    {
        get => rows;
        set
        {
            rows = value ?? Array.Empty<ListViewItem>();
            SelectedIndices.Clear();
            VirtualListSize = rows.Length;
            Invalidate();
        }
    }

    public IEnumerable<ListViewItem> SelectedRows => SelectedIndices.Cast<int>().Where(i => i < rows.Length).Select(i => rows[i]).ToList();

    public void InvalidateRow(int i) { if (i >= 0 && i < VirtualListSize) Invalidate(GetItemRect(i)); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!CheckColumn || e.Button != MouseButtons.Left) return;
        var hit = HitTest(e.Location);
        if (hit.Item != null && e.X < hit.Item.Bounds.X + 22) CheckToggled?.Invoke(hit.Item.Index);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!CheckColumn || e.KeyCode != Keys.Space) return;
        foreach (int i in SelectedIndices.Cast<int>().ToList()) CheckToggled?.Invoke(i);
        e.Handled = true;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if ((m.Msg == 0x000F || m.Msg == 0x0318) && View == View.Details && IsHandleCreated)
        {
            try
            {
                int count = VirtualMode ? VirtualListSize : Items.Count;
                int top = count > 0 ? GetItemRect(count - 1).Bottom : Native.HeaderHeight(this);
                if (top < Native.HeaderHeight(this)) return;
                if (top >= ClientSize.Height) return;
                using var g = m.Msg == 0x0318 ? Graphics.FromHdc(m.WParam) : CreateGraphics();
                using var b = new SolidBrush(BackColor);
                g.FillRectangle(b, 0, top, ClientSize.Width, ClientSize.Height - top);
            }
            catch { }
        }
    }
}

// Small control factories and dialogs shared by the windows.
public static class Ui
{
    public static Form Owner;
    public static Func<string, MessageBoxIcon, MessageBoxButtons, DialogResult> MessageHook;   // tests answer here

    public static Button NewButton(string text, Action onClick, string kind = "")
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3), Padding = new Padding(8, 3, 8, 3), Tag = kind };
        if (onClick != null) b.Click += (s, e) => Safe(onClick);
        return b;
    }

    public static Label NewLabel(string text, int width = 0, string kind = "")
    {
        var l = new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 6, 3), Tag = kind };
        if (width > 0) l.MaximumSize = new Size(width, 0);
        return l;
    }

    public static FlowLayoutPanel NewFlow(DockStyle dock = DockStyle.Top, params Control[] controls)
    {
        var f = new FlowLayoutPanel { Dock = dock, AutoSize = true, WrapContents = true };
        f.Controls.AddRange(controls);
        return f;
    }

    public static ContextMenuStrip NewMenu(params (string Text, Action Do)[] entries)
    {
        var m = new ContextMenuStrip();
        foreach (var (text, act) in entries) m.Items.Add(text, null, (s, e) => Safe(act));
        Theme.StyleMenu(m);
        return m;
    }

    public static ComboBox NewCombo(int width, params string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, MaxDropDownItems = 25 };
        c.Items.AddRange(items);
        if (c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }

    public static DialogResult Message(string text, MessageBoxIcon icon = MessageBoxIcon.Information, MessageBoxButtons buttons = MessageBoxButtons.OK)
    {
        if (MessageHook != null) return MessageHook(text, icon, buttons);
        return Owner != null && !Owner.IsDisposed
            ? MessageBox.Show(Owner, text, App.Title, buttons, icon)
            : MessageBox.Show(text, App.Title, buttons, icon);
    }

    public static bool Ask(string text) => Message(text, MessageBoxIcon.Question, MessageBoxButtons.YesNo) == DialogResult.Yes;

    public static void ShowError(Exception ex)
    {
        if (Owner != null && !Owner.IsDisposed) { Owner.Enabled = true; Owner.UseWaitCursor = false; }
        Message(ex.Message, MessageBoxIcon.Error);
    }

    // Runs an action; an error becomes a message instead of ending the program.
    public static void Safe(Action action)
    {
        try { action(); } catch (Exception ex) { ShowError(ex); }
    }

    public static Func<string, string, string> InputHook;   // tests answer here

    // A one-line question in the launcher's colours.
    public static string Input(string prompt, string value)
    {
        if (InputHook != null) return InputHook(prompt, value);
        using var f = new Form
        {
            Text = App.Title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(560, 150), Font = Theme.Base, BackColor = Theme.C("Back"), ForeColor = Theme.C("Text"),
        };
        Native.WindowChrome(f);
        var label = new Label { Text = prompt, Location = new Point(16, 14), MaximumSize = new Size(528, 0), AutoSize = true };
        var box = new TextBox { Text = value ?? "", Location = new Point(16, 70), Width = 528 };
        var ok = NewButton("OK", null, "primary"); ok.DialogResult = DialogResult.OK; ok.AutoSize = false; ok.Size = new Size(100, 30); ok.Location = new Point(338, 106);
        var cancel = NewButton("Cancel", null); cancel.DialogResult = DialogResult.Cancel; cancel.AutoSize = false; cancel.Size = new Size(100, 30); cancel.Location = new Point(444, 106);
        f.Controls.AddRange(new Control[] { label, box, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        Theme.Style(f);
        f.Shown += (s, e) => { box.Focus(); box.SelectAll(); };
        f.Load += (s, e) => { if (label.Bottom + 10 > box.Top) { int dy = label.Bottom + 12 - box.Top; box.Top += dy; ok.Top += dy; cancel.Top += dy; f.ClientSize = new Size(f.ClientSize.Width, f.ClientSize.Height + dy); } };
        return f.ShowDialog(Owner) == DialogResult.OK ? box.Text.Trim() : "";
    }
}
