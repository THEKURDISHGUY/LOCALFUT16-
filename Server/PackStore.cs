#nullable disable
#pragma warning disable
using System.Globalization;
using System.Text;
namespace LocalFut15;

static class PackStore
{
    sealed class Base
    {
        public long Id; public int Ovr, Club, Nation, League; public string Pos; public int[] Face;
        public string[] LongT, ShortT; public string LongN, ShortN;
    }

    static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("ø", "o").Replace("Ø", "O").Replace("đ", "d").Replace("Đ", "D").Replace("ł", "l").Replace("Ł", "L")
             .Replace("ß", "ss").Replace("æ", "ae").Replace("Æ", "AE").Replace("œ", "oe").Replace("ı", "i").Replace("þ", "th");
        var sb = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
    static string[] Toks(string norm) => norm.Length == 0 ? new string[0] : norm.Split(' ');

    static List<string> SplitCsv(string line)
    {
        var r = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { r.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        r.Add(sb.ToString()); return r;
    }
    static int Num(string v) => int.TryParse((v ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;

    static int Group(string p) => p switch
    {
        "GK" => 0,
        "CB" or "LB" or "RB" or "LWB" or "RWB" or "SW" => 1,
        "CDM" or "CM" or "CAM" or "LM" or "RM" => 2,
        _ => 3,
    };
    static int PosDist(string a, string b) => a == b ? 0 : Group(a) == Group(b) ? 1 : 2;

    static List<Base> LoadBase()
    {
        var l = new List<Base>();
        foreach (var line in PlayersData.Rows)
        {
            var f = line.Split(',');
            if (f.Length < 13) continue;
            try
            {
                var nm = f[12].Split('|');
                string sh = nm[0], lo = nm.Length > 1 && nm[1].Length > 0 ? nm[1] : nm[0];
                var b = new Base
                {
                    Id = long.Parse(f[0]), Ovr = int.Parse(f[1]), Pos = f[2], Club = int.Parse(f[3]), Nation = int.Parse(f[4]), League = int.Parse(f[5]),
                    Face = new int[6], ShortN = Norm(sh), LongN = Norm(lo),
                };
                for (int i = 0; i < 6; i++) b.Face[i] = int.Parse(f[6 + i]);
                b.ShortT = Toks(b.ShortN); b.LongT = Toks(b.LongN);
                l.Add(b);
            }
            catch { }
        }
        return l;
    }

    public static List<UtBackend.P> Apply(List<UtBackend.P> db)
    {
        string csv = UtBackend.FindFile("players16.csv");
        if (!File.Exists(csv)) { Console.WriteLine("[PACKS] players16.csv not found (next to the exe / project folder): packs only contain the base cards"); return db; }
        var bases = LoadBase();
        if (bases.Count < 500)
        {
            Console.WriteLine("[PACKS] PlayersData.cs has no named players (empty stub, or built before names were added). Run  dotnet run -- --make-players D:\\path\\male_players.csv  then rebuild. players16.csv cards are NOT added yet.");
            return db;
        }

        var idx = new Dictionary<string, List<int>>();
        for (int i = 0; i < bases.Count; i++)
            foreach (var t in bases[i].LongT.Concat(bases[i].ShortT))
            {
                if (t.Length < 2) continue;
                if (!idx.TryGetValue(t, out var l)) idx[t] = l = new List<int>();
                if (l.Count == 0 || l[l.Count - 1] != i) l.Add(i);
            }

        int Score(Base b, string nn, string[] ct)
        {
            if (nn == b.LongN || nn == b.ShortN) return 100;
            var cs = ct.Where(t => t.Length >= 2).ToArray();
            if (cs.Length > 0 && cs.All(t => b.LongT.Contains(t))) return 80;
            if (b.ShortT.Length >= 2 && b.ShortT[0].Length == 1 && ct.Length >= 2 && ct[0][0] == b.ShortT[0][0]
                && string.Join(" ", ct.Skip(1)) == string.Join(" ", b.ShortT.Skip(1))) return 70;
            if (b.LongT.Length >= 2 && b.LongT.All(t => ct.Contains(t))) return 60;
            if (cs.Length >= 2) { var all = new HashSet<string>(b.LongT.Concat(b.ShortT)); if (cs.All(all.Contains)) return 50; }
            return 0;
        }

        var cache = new Dictionary<string, List<(int i, int score)>>();
        List<(int i, int score)> Cands(string nn)
        {
            if (cache.TryGetValue(nn, out var hit)) return hit;
            var res = new List<(int, int)>();
            var ct = Toks(nn);
            List<int> pool = null;
            foreach (var t in ct)
                if (t.Length >= 2 && idx.TryGetValue(t, out var l) && (pool == null || l.Count < pool.Count)) pool = l;
            if (pool != null)
                foreach (var i in pool) { int sc = Score(bases[i], nn, ct); if (sc > 0) res.Add((i, sc)); }
            return cache[nn] = res;
        }

        var extras = new List<UtBackend.P>();
        var seen = new HashSet<string>();
        var unmatched = new SortedDictionary<string, int>();
        int rows = 0, matched = 0, sameAsBase = 0, dup = 0;
        var cardKeys = new HashSet<string>();

        bool first = true;
        foreach (var line in File.ReadLines(csv, new UTF8Encoding(false)))
        {
            if (first) { first = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var c = SplitCsv(line);
            if (c.Count < 12) continue;
            string name = c[0].Trim(); string pos = c[3].Trim().ToUpperInvariant(); int rating = Num(c[5]);
            if (name.Length == 0 || rating <= 0) continue;
            if (pos.Length == 0) pos = "CM";
            rows++;
            string nn = Norm(name);
            Base best = null; long bestKey = long.MinValue;
            foreach (var (i, sc) in Cands(nn))
            {
                var b = bases[i]; int diff = Math.Abs(rating - b.Ovr);
                if (diff > 20) continue;
                long key = sc * 1000L - PosDist(pos, b.Pos) * 100L - diff;
                if (key > bestKey) { bestKey = key; best = b; }
            }
            if (best == null) { unmatched[name + " (" + rating + " " + pos + ")"] = 1; continue; }
            matched++;
            if (rating == best.Ovr && pos == best.Pos) { sameAsBase++; continue; }
            var face = new[] { Num(c[6]), Num(c[7]), Num(c[8]), Num(c[9]), Num(c[10]), Num(c[11]) };
            bool special = rating > best.Ovr;
            long rid = special ? 16777216L + best.Id : best.Id;
            string k = rid + "|" + rating + "|" + pos + "|" + string.Join(",", face);
            if (!seen.Add(k)) { dup++; continue; }
            extras.Add(new UtBackend.P(rid, best.Id, name, rating, pos, best.Nation, best.League, best.Club, special ? 1 : 0, face, special));
        }

        Console.WriteLine($"[PACKS] players16.csv: {rows} cards read, {matched} matched to real FIFA 16 players ({sameAsBase} are the normal base card, {dup} exact duplicates), +{extras.Count} extra cards added to the packs, {unmatched.Count} unmatched");
        if (unmatched.Count > 0)
        {
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "save"); Directory.CreateDirectory(dir);
                File.WriteAllLines(Path.Combine(dir, "unmatched_cards.txt"), unmatched.Keys, new UTF8Encoding(false));
                Console.WriteLine("[PACKS] cards whose name is not in PlayersData.cs were skipped (they have no player id, so no face): save\\unmatched_cards.txt");
            }
            catch { }
        }
        try
        {
            string sc = UtBackend.FindFile("special_cards.csv");
            if (File.Exists(sc))
            {
                var byId = new Dictionary<long, Base>(); foreach (var b in bases) byId[b.Id] = b;
                int added = 0, miss = 0; bool h = true;
                foreach (var line in File.ReadLines(sc, new UTF8Encoding(false)))
                {
                    if (h) { h = false; continue; }
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var c = SplitCsv(line); if (c.Count < 4) continue;
                    long pid = Num(c[0]); int rt = Num(c[2]);
                    if (!byId.TryGetValue(pid, out var bb) || rt <= 0) { miss++; continue; }
                    string ps = c[3].Trim().ToUpperInvariant(); if (ps.Length == 0) ps = bb.Pos;
                    var fc = new int[6]; for (int i = 0; i < 6; i++) fc[i] = c.Count > 4 + i ? Num(c[4 + i]) : bb.Face[i];
                    string ty = c.Count > 10 ? c[10].Trim().ToLowerInvariant() : "";
                    int flag = ty == "toty" || ty == "tots" ? 5 : ty == "totw" ? 3 : Num(ty) > 0 ? Num(ty) : 1;
                    long rid2 = ((long)flag << 24) + pid;
                    string k2 = rid2 + "|" + rt + "|" + ps + "|" + string.Join(",", fc);
                    if (!seen.Add(k2)) continue;
                    extras.Add(new UtBackend.P(rid2, pid, c[1].Trim(), rt, ps, bb.Nation, bb.League, bb.Club, flag, fc, true, ty == "toty" || ty == "tots" ? ty : "")); added++;
                }
                Console.WriteLine($"[PACKS] special_cards.csv: +{added} special cards, {miss} rows skipped (unknown id / rating)");
            }
        }
        catch (Exception ex) { Console.WriteLine("[PACKS] special_cards.csv failed: " + ex.Message); }
        var merged = new List<UtBackend.P>(db.Count + extras.Count);
        merged.AddRange(db); merged.AddRange(extras);
        return merged;
    }

    public static List<UtBackend.M> LoadManagers()
    {
        var l = new List<UtBackend.M>();
        string f = UtBackend.FindFile("male_coaches.csv");
        if (!File.Exists(f)) { Console.WriteLine("[MANAGERS] male_coaches.csv not found next to the exe: packs have no managers"); return l; }
        var hdr = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); bool first = true;
        var seen = new HashSet<long>();
        foreach (var line in File.ReadLines(f, new UTF8Encoding(false)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var c = SplitCsv(line);
            if (first) { first = false; for (int i = 0; i < c.Count; i++) hdr[c[i].Trim()] = i; continue; }
            string G(string k) => hdr.TryGetValue(k, out var i) && i < c.Count ? c[i].Trim() : "";
            long id = Num(G("coach_id")); if (id <= 0 || !seen.Add(id)) continue;
            string nm = G("long_name"); if (nm.Length == 0) nm = G("short_name"); if (nm.Length == 0) continue;
            l.Add(new UtBackend.M(id, nm, G("nationality_name")));
        }
        Console.WriteLine($"[MANAGERS] {l.Count} managers loaded from {Path.GetFileName(f)}");
        return l;
    }

    public static UtBackend.Pack[] AddRebornPacks(UtBackend.Pack[] existing)
    {
        var list = new List<UtBackend.Pack>(existing);
        int next = Math.Max(9000, list.Count == 0 ? 0 : list.Max(p => p.Id)) + 1;
        var defs = new (string n, string cat, string bio, int coins, int cards, int g, int s, int b, int rare, double sp, string mode)[]
        {
            ("FUT REBORN Bronze Pack",       "bronze", "3 bronze players",                                   400,   3,   0,  5, 95, 0, 0,    ""),
            ("FUT REBORN Silver Pack",       "silver", "4 silver players, at least 1 rare",                  2500,  4,   5, 95,  0, 1, 0,    ""),
            ("FUT REBORN Gold Pack",         "gold",   "5 gold players, at least 2 rares",                   7500,  5, 100,  0,  0, 2, 0.05, ""),
            ("FUT REBORN Jumbo Gold Pack",   "gold",   "12 gold players, at least 4 rares",                  25000, 12, 100, 0,  0, 4, 0.10, ""),
            ("FUT REBORN Special Pack",      "special","5 special cards: 2 TOTY + 1 TOTS + 2 special", 800000, 5, 100, 0,  0, 5, 0,    "special"),
            ("FUT REBORN All Cards Pack",    "gold",   "5 cards from EVERY card in the game, any rating",    20000, 5, 100, 0,  0, 0, 0,    "all"),
        };
        foreach (var d in defs)
        {
            if (list.Any(p => string.Equals(p.Name, d.n, StringComparison.OrdinalIgnoreCase))) continue;
            if (d.mode == "all" || d.n.Contains("Gold")) continue;
            if (d.mode != "special" && list.Any(p => string.Equals(p.Category, d.cat, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(new UtBackend.Pack(next++, d.n, d.cat, d.bio, d.coins, 0, d.cards, d.g, d.s, d.b, d.rare, d.sp, d.mode));
        }
        return list.ToArray();
    }
}
