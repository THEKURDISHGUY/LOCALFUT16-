#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalFut15;

static partial class UtBackend
{
    static (int, string, byte[], Dictionary<string, string>?)? HandleTrophyAssets(string target)
    {
        string ap = target.Contains('?') ? target[..target.IndexOf('?')] : target;
        string low = ap.ToLowerInvariant();
        var m = System.Text.RegularExpressions.Regex.Match(low, @"^/fut/items/pc/(-?\d+)\.json$");
        if (m.Success)
        {
            long rid = long.Parse(m.Groups[1].Value); if (rid <= 0) rid = SPTrophyResourceId;
            string bn = "trophy_" + rid + "_gold";
            var o = new JsonObject { ["itemData"] = new JsonArray(new JsonObject { ["id"] = rid, ["assetId"] = rid, ["resourceId"] = rid, ["itemType"] = "trophy", ["assetName"] = bn, ["name"] = bn, ["image"] = bn }) };
            Console.WriteLine("[TROPHY] item definition " + rid);
            return (200, "application/json", Encoding.UTF8.GetBytes(o.ToJsonString()), null);
        }
        if (low.StartsWith("/fut/items/images/trophies/kc/") && low.EndsWith(".big"))
        {
            bool item = low.EndsWith("/item.big");
            string stem = item ? "trophy_" + SPTrophyResourceId + "_golditem.big" : System.IO.Path.GetFileName(low);
            foreach (var root in new[] { Path.Combine(Directory.GetCurrentDirectory(), "fut_assets"), Path.Combine(AppContext.BaseDirectory, "fut_assets") })
                try
                {
                    var f = Path.Combine(root, ap.Substring(5).Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(f) && new FileInfo(f).Length > 200) { Console.WriteLine("[TROPHY] served loose file " + f); return (200, "application/octet-stream", File.ReadAllBytes(f), null); }
                }
                catch { }
            var real = Fut.TryGameBig(stem);
            if (real != null && File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "trophy_unpack.txt"))) { var un = Fut.UnpackChunkZipAny(real); if (un != null) real = un; }
            if (real != null)
            {
                try
                {
                    var dst = Path.Combine(Directory.GetCurrentDirectory(), "fut_assets", ap.Substring(5).Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!); File.WriteAllBytes(dst, real);
                }
                catch { }
                Console.WriteLine($"[TROPHY] {stem} taken from the game's own files ({real.Length} bytes)");
                return (200, "application/octet-stream", real, (Dictionary<string, string>)null);
            }
            Console.WriteLine($"[TROPHY] {stem} NOT found in the game files -> minimal empty BIG. Extract it from FIFA 15's cards0.big and put it at fut_assets\\items\\images\\trophies\\kc\\item.big");
            var empty = new byte[16]; Encoding.ASCII.GetBytes("BIGF").CopyTo(empty, 0); empty[4] = 16; empty[15] = 16;
            return (200, "application/octet-stream", empty, null);
        }
        return null;
    }
}
