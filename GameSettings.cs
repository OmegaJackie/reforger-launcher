using System.Text;
using System.Text.RegularExpressions;

namespace ReforgerLauncher;

// The game's mod presets: the SCR_WorkshopAddonManagerPresetStorage block of ReforgerGameSettings.conf
// (Documents\My Games\ArmaReforger\profile\.save\app1874880_user<id>\settings). The game rewrites the file when it exits,
// so it must be closed when saving. Every save backs the file up to mod-presets-backups\ first (the last 20 are kept).
public class GameSettings
{
    public string Path;
    public string NewLine = "\n";
    public List<string> Lines = new();
    public int Start = -1, End = -1;
    public string BlockText = "";
    public List<Preset> Presets = new();
    public string UsedPreset = "";

    static readonly Regex StorageRx = new(@"^\s*SCR_WorkshopAddonManagerPresetStorage\b");
    static readonly Regex MetaRx = new(@"^\s*SCR_WorkshopAddonPresetAddonMeta\b");
    static readonly Regex PresetRx = new(@"^\s*SCR_WorkshopAddonPreset\b");
    static readonly Regex GuidRx = new("^\\s*m_sGuid\\s+\"(.*)\"\\s*$");
    static readonly Regex NameRx = new("^\\s*m_sName\\s+\"(.*)\"\\s*$");
    static readonly Regex UsedRx = new("^\\s*m_sUsedPreset\\s+\"(.*)\"\\s*$");
    static readonly Regex QuotedRx = new("\"(?:[^\"\\\\]|\\\\.)*\"");

    // Text that's safe inside a conf string (the game never needs quotes or backslashes in these names).
    public static string SafeName(string s) =>
        Regex.Replace((s ?? "").Replace("\"", "'").Replace("\\", "/"), @"[\x00-\x1F]", " ").Trim();

    static int BraceDelta(string line)
    {
        var bare = QuotedRx.Replace(line, "\"\"");
        return bare.Count(c => c == '{') - bare.Count(c => c == '}');
    }

    static string Unescape(string s) => Regex.Replace(s, @"\\(.)", "$1");

    public static GameSettings Read(string path)
    {
        var text = File.ReadAllText(path);
        var doc = new GameSettings { Path = path, NewLine = text.Contains("\r\n") ? "\r\n" : "\n" };
        doc.Lines = Regex.Split(text, "\r?\n").ToList();
        for (int i = 0; i < doc.Lines.Count; i++) if (StorageRx.IsMatch(doc.Lines[i])) { doc.Start = i; break; }
        if (doc.Start < 0) return doc;

        int depth = 0, presetDepth = 0, metaDepth = 0;
        Preset preset = null;
        string metaGuid = null, metaName = null;
        bool inMeta = false;
        for (int i = doc.Start; i < doc.Lines.Count; i++)
        {
            var line = doc.Lines[i];
            if (i > doc.Start)
            {
                Match m;
                if (MetaRx.IsMatch(line)) { inMeta = true; metaGuid = ""; metaName = ""; metaDepth = depth; }
                else if (PresetRx.IsMatch(line)) { preset = new Preset(""); presetDepth = depth; }
                else if ((m = GuidRx.Match(line)).Success) { if (inMeta) metaGuid = Unescape(m.Groups[1].Value); }
                else if ((m = NameRx.Match(line)).Success)
                {
                    var value = Unescape(m.Groups[1].Value);
                    if (inMeta) metaName = value; else if (preset != null) preset.Name = value;
                }
                else if ((m = UsedRx.Match(line)).Success) doc.UsedPreset = Unescape(m.Groups[1].Value);
            }
            depth += BraceDelta(line);
            if (inMeta && depth <= metaDepth)
            {
                if (preset != null && !string.IsNullOrEmpty(metaGuid)) preset.Mods.Add(metaGuid, metaName);
                inMeta = false;
            }
            if (preset != null && depth <= presetDepth) { doc.Presets.Add(preset); preset = null; }
            if (depth <= 0) { doc.End = i; break; }
        }
        if (doc.End < 0) throw new Exception($"The mod preset block in {path} never closes - the file looks damaged, so it was left alone.");
        doc.BlockText = string.Join("\n", doc.Lines.GetRange(doc.Start, doc.End - doc.Start + 1));
        return doc;
    }

    // True when the presets on disk differ from the ones this was read from (the game saved in the meantime).
    public bool ChangedOnDisk() => Read(Path).BlockText != BlockText;

    public Preset Find(string name) => Presets.FirstOrDefault(p => p.Name == name);

    List<string> FormatStorage()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(string.Join("\n", Lines), @"\{([0-9A-Fa-f]{16})\}")) taken.Add(m.Groups[1].Value);
        long stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long counter = Random.Shared.Next(0x0FFFFFFF);
        string NewId()
        {
            string id;
            do { counter++; id = $"{stamp:X8}{counter:X8}"; } while (!taken.Add(id));
            return "\"{" + id + "}\"";
        }

        var o = new List<string>();
        o.Add(Start >= 0 ? Lines[Start] : $" SCR_WorkshopAddonManagerPresetStorage SCR_WorkshopAddonManagerPresetStorage {NewId()} {{");
        o.Add("  m_iVersion 1");
        o.Add("  m_aPresets {");
        foreach (var p in Presets)
        {
            o.Add($"   SCR_WorkshopAddonPreset {NewId()} {{");
            o.Add($"    m_sName \"{SafeName(p.Name)}\"");
            o.Add("    m_aAddonsMeta {");
            foreach (var e in p.Mods)
            {
                o.Add($"     SCR_WorkshopAddonPresetAddonMeta {NewId()} {{");
                o.Add($"      m_sGuid \"{e.Key}\"");
                o.Add($"      m_sName \"{SafeName(e.Value)}\"");
                o.Add("     }");
            }
            o.Add("    }");
            o.Add("   }");
        }
        o.Add("  }");
        if (!string.IsNullOrEmpty(UsedPreset)) o.Add($"  m_sUsedPreset \"{SafeName(UsedPreset)}\"");
        o.Add(" }");
        return o;
    }

    // Writes the presets into the settings file and returns the backup's path. Everything outside the preset block is
    // taken from the file as it is now, so other settings the game saved meanwhile survive.
    public string Save(string backupDir, bool force)
    {
        Game.AssertClosed();
        var fresh = Read(Path);
        if (!force && fresh.BlockText != BlockText)
            throw new Exception($"The presets in {Path} changed since they were loaded (did the game save them?). Reload and redo the change.");
        Lines = fresh.Lines; Start = fresh.Start; End = fresh.End;
        foreach (var p in Presets) p.Name = SafeName(p.Name);
        var block = FormatStorage();

        var lines = new List<string>();
        if (Start >= 0)
        {
            lines.AddRange(Lines.GetRange(0, Start));
            lines.AddRange(block);
            lines.AddRange(Lines.GetRange(End + 1, Lines.Count - End - 1));
        }
        else
        {
            // No presets yet: the block goes just before GameUserSettings' closing brace.
            int close = Lines.FindLastIndex(l => l.Trim() == "}");
            if (close < 0) throw new Exception($"{Path} doesn't look like a Reforger settings file - it was left alone.");
            lines.AddRange(Lines.GetRange(0, close));
            lines.AddRange(block);
            lines.AddRange(Lines.GetRange(close, Lines.Count - close));
        }

        Directory.CreateDirectory(backupDir);
        var backup = System.IO.Path.Combine(backupDir, $"ReforgerGameSettings-{DateTime.Now:yyyyMMdd-HHmmss}.conf");
        File.Copy(Path, backup, true);
        foreach (var old in new DirectoryInfo(backupDir).GetFiles("ReforgerGameSettings-*.conf").OrderByDescending(f => f.Name).Skip(20))
            old.Delete();

        File.WriteAllText(Path, string.Join(NewLine, lines), new UTF8Encoding(false));

        // Read it back; if anything didn't survive, put the original back.
        var check = Read(Path);
        bool same = check.Presets.Count == Presets.Count;
        for (int i = 0; same && i < Presets.Count; i++)
            same = check.Presets[i].Name == Presets[i].Name && string.Join(",", check.Presets[i].Mods.Keys) == string.Join(",", Presets[i].Mods.Keys);
        if (!same)
        {
            File.Copy(backup, Path, true);
            throw new Exception($"The saved presets didn't read back correctly, so the original file was restored from {backup}.");
        }
        Lines = check.Lines; Start = check.Start; End = check.End; BlockText = check.BlockText;
        foreach (var p in Presets) p.Changed = false;
        return backup;
    }
}
