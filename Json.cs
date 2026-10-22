using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReforgerLauncher;

// JSON helpers: lenient reading, indented writing that keeps names readable, and dotted paths ("game.gameProperties.x").
public static class Json
{
    public static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    public static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static readonly JsonSerializerOptions CompactOptions = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonNode Parse(string text) => JsonNode.Parse(text, null, ReadOptions);
    public static string Write(JsonNode node) => node == null ? "null" : node.ToJsonString(WriteOptions);
    public static string Compact(JsonNode node) => node == null ? "null" : node.ToJsonString(CompactOptions);
    public static string Quote(string s) => JsonSerializer.Serialize(s ?? "", CompactOptions);

    public static JsonNode Get(JsonNode node, string path)
    {
        foreach (var key in path.Split('.'))
        {
            if (node is not JsonObject o || !o.TryGetPropertyValue(key, out node)) return null;
        }
        return node;
    }

    // Whether the path exists (its value may be null).
    public static bool Has(JsonObject root, string path)
    {
        JsonNode node = root;
        foreach (var key in path.Split('.'))
        {
            if (node is not JsonObject o || !o.ContainsKey(key)) return false;
            node = o[key];
        }
        return true;
    }

    public static string Str(JsonNode node, string path = null)
    {
        var n = path == null ? node : Get(node, path);
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out string s)) return s;
            return v.ToJsonString();
        }
        return n == null ? "" : n.ToJsonString();
    }

    public static long Long(JsonNode node, string path = null)
    {
        var n = path == null ? node : Get(node, path);
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out long l)) return l;
            if (v.TryGetValue(out double d)) return (long)d;
            if (v.TryGetValue(out string s) && long.TryParse(s, out l)) return l;
        }
        return 0;
    }

    public static bool Bool(JsonNode node, string path = null)
    {
        var n = path == null ? node : Get(node, path);
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out bool b)) return b;
            if (v.TryGetValue(out long l)) return l != 0;
            if (v.TryGetValue(out string s)) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // Truthiness like the script's: a non-empty string, non-zero number, true, any object or non-empty array.
    public static bool Truthy(JsonNode n)
    {
        switch (n)
        {
            case null: return false;
            case JsonValue v:
                if (v.TryGetValue(out bool b)) return b;
                if (v.TryGetValue(out string s)) return s.Length > 0;
                if (v.TryGetValue(out double d)) return d != 0;
                return true;
            case JsonArray a: return a.Count > 0;
            default: return true;
        }
    }

    public static JsonObject Section(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject o) return o;
        o = new JsonObject();
        parent[key] = o;
        return o;
    }

    public static void Set(JsonObject root, string path, JsonNode value)
    {
        var keys = path.Split('.');
        var obj = root;
        for (int i = 0; i < keys.Length - 1; i++) obj = Section(obj, keys[i]);
        obj[keys[^1]] = value;
    }

    // Removes a setting, then any section it leaves empty (game itself stays).
    public static void Remove(JsonObject root, string path)
    {
        var keys = path.Split('.');
        var parents = new List<JsonObject>();
        var obj = root;
        for (int i = 0; i < keys.Length - 1; i++)
        {
            parents.Add(obj);
            if (obj[keys[i]] is not JsonObject next) return;
            obj = next;
        }
        obj.Remove(keys[^1]);
        for (int i = keys.Length - 2; i >= 0; i--)
        {
            if (i == 0 && keys[0] == "game") break;
            if (parents[i][keys[i]] is JsonObject section && section.Count == 0) parents[i].Remove(keys[i]);
            else break;
        }
    }

    public static JsonObject Clone(JsonObject o) => (JsonObject)o.DeepClone();
}
