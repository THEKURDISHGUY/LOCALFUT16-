#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static readonly Dictionary<long, (long nat, long lg, long rt)> MgrExtra = new();

    static readonly (string n, int nat, int lg, int rt)[] BuiltinManagers =
    {
        ("Pep Guardiola", 45, 19, 90), ("Jose Mourinho", 38, 13, 89), ("Carlo Ancelotti", 27, 19, 88), ("Luis Enrique", 45, 53, 88),
        ("Diego Simeone", 52, 53, 87), ("Arsene Wenger", 18, 13, 87), ("Juergen Klopp", 21, 13, 86), ("Manuel Pellegrini", 55, 13, 85),
        ("Vicente del Bosque", 45, 53, 85), ("Rafael Benitez", 45, 53, 84), ("Louis van Gaal", 34, 13, 84), ("Antonio Conte", 27, 31, 84),
        ("Massimiliano Allegri", 27, 31, 84), ("Mauricio Pochettino", 52, 13, 82), ("Laurent Blanc", 18, 16, 82), ("Unai Emery", 45, 53, 82),
        ("Marcelo Bielsa", 52, 16, 82), ("Jorge Sampaoli", 52, 53, 82), ("Roberto Mancini", 27, 31, 82), ("Thomas Tuchel", 21, 19, 80),
        ("Ronald Koeman", 34, 13, 80), ("Zinedine Zidane", 18, 53, 80), ("Gerardo Martino", 52, 53, 80), ("Claudio Ranieri", 27, 13, 78),
        ("Brendan Rodgers", 24, 13, 78), ("Lucien Favre", 47, 19, 78), ("Roger Schmidt", 21, 19, 78), ("Slaven Bilic", 10, 13, 76),
        ("Sam Allardyce", 14, 13, 74), ("Tony Pulis", 50, 13, 72), ("Alan Pardew", 14, 13, 72), ("Alex Ferguson", 42, 13, 90),
    };

    static List<M> LoadManagers16(List<M> csvFallback)
    {
        if (File.Exists(FindFile("use_coach_csv.txt"))) return csvFallback;
        string pf = FindFile("managers_probe.txt");
        if (File.Exists(pf))
        {
            long st = 5000001; long.TryParse(File.ReadAllText(pf).Trim(), out st); if (st <= 0) st = 5000001;
            var pl = new List<M>(); MgrExtra.Clear();
            for (int i = 0; i < 600; i++) { long id = st + i; pl.Add(new M(id, "P" + id, "")); MgrExtra[id] = (14, 13, 80); }
            Console.WriteLine($"[MANAGERS] PROBE MODE: ids {st} .. {st + 599} (delete managers_probe.txt to leave it)");
            return pl;
        }
        string f = FindFile("managers16.csv");
        var rows = new List<(long id, string name, long nat, long lg, long rt)>();
        if (File.Exists(f))
        {
            bool first = true;
            foreach (var line in File.ReadAllLines(f, new UTF8Encoding(false)))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                if (first) { first = false; if (line.ToLowerInvariant().Contains("resource_id")) continue; }
                var c = line.Split(',');
                if (c.Length < 2 || c[1].Trim().Length == 0) continue;
                long P(int i, long d) => i < c.Length && long.TryParse(c[i].Trim(), out var v) ? v : d;
                rows.Add((P(0, 0), c[1].Trim(), P(2, 14), P(3, 13), P(4, 80)));
            }
        }
        else
        {
            foreach (var b in BuiltinManagers) rows.Add((0, b.n, b.nat, b.lg, b.rt));
            try
            {
                var sb = new StringBuilder("resource_id,name,nation_id,league_id,rating\n");
                foreach (var r in rows) sb.Append($",{r.name},{r.nat},{r.lg},{r.rt}\n");
                File.WriteAllText(f, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
        var list = new List<M>(); MgrExtra.Clear(); long next = 5000001;
        foreach (var r in rows)
        {
            long id = r.id > 0 ? r.id : next++;
            list.Add(new M(id, r.name, ""));
            MgrExtra[id] = (r.nat, r.lg, r.rt);
        }
        Console.WriteLine($"[MANAGERS] {list.Count} managers from {(File.Exists(f) ? "managers16.csv" : "built-in list")} (male_coaches.csv is not used)");
        return list.Count > 0 ? list : csvFallback;
    }
}
