#nullable disable
#pragma warning disable
using System.Globalization;
using System.Text;
namespace LocalFut15;

static class IconStore
{
    public const int IconFlag = 12;

    static int Num(string v) => int.TryParse((v ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;

    public static List<UtBackend.P> Apply(List<UtBackend.P> db)
    {
        string tsv = UtBackend.FindFile("icons16.tsv");
        if (!File.Exists(tsv)) { Console.WriteLine("[ICONS] icons16.tsv not found next to the exe: no icon cards"); return db; }

        int flag = IconFlag;
        try { var ff = UtBackend.FindFile("icons_flag.txt"); if (File.Exists(ff) && int.TryParse(File.ReadAllText(ff).Trim(), out var fv) && fv > 0) flag = fv; } catch { }

        var added = new List<UtBackend.P>();
        var resolved = new List<string>(); var unmatched = new List<string>();
        var usedAsset = new HashSet<long>();
        foreach (var raw in File.ReadLines(tsv, new UTF8Encoding(false)))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var c = line.Split('\t');
            if (c.Length < 4) continue;
            if (c[0].Trim().ToLowerInvariant() == "id") continue;
            string name = c[1].Trim(); int rating = Num(c[2]); string pos = c[3].Trim().ToUpperInvariant();
            if (name.Length == 0 || rating <= 0) continue;
            long pid = Num(c[0]);
            if (pid <= 0) { unmatched.Add(name + " (" + rating + " " + pos + ")  [no player id in icons16.tsv]"); continue; }
            if (!usedAsset.Add(pid)) continue;
            var face = new int[6];
            for (int i = 0; i < 6; i++) face[i] = c.Length > 4 + i ? Num(c[4 + i]) : 0;
            int nation = c.Length > 10 ? Num(c[10]) : 0;
            long rid = ((long)flag << 24) + pid;
            added.Add(new UtBackend.P(rid, pid, name, rating, pos, nation, 0, 0, flag, face, true, "icon"));
            resolved.Add(string.Join("\t", pid, rid, name, rating, pos, nation, face[0], face[1], face[2], face[3], face[4], face[5]));
        }

        Console.WriteLine($"[ICONS] icons16.tsv: +{added.Count} legend cards (rareflag {flag}), {unmatched.Count} skipped (no id)");
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "save"); Directory.CreateDirectory(dir);
            var o = new List<string> { "id\tresourceId\tname\trating\tposition\tnation\tpace\tshooting\tpassing\tdribbling\tdefending\tphysical" };
            o.AddRange(resolved);
            File.WriteAllLines(Path.Combine(dir, "icons_resolved.tsv"), o, new UTF8Encoding(false));
            string um = Path.Combine(dir, "icons_unmatched.txt");
            if (unmatched.Count > 0) { File.WriteAllLines(um, unmatched, new UTF8Encoding(false)); Console.WriteLine("[ICONS] legends without an id (skipped): save\\icons_unmatched.txt"); }
            else if (File.Exists(um)) File.Delete(um);
        }
        catch { }

        var merged = new List<UtBackend.P>(db.Count + added.Count);
        merged.AddRange(db); merged.AddRange(added);
        return merged;
    }
}
