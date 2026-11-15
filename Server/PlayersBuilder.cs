#nullable disable
#pragma warning disable
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
namespace LocalFut15;

static class PlayersBuilder
{
    public static List<UtBackend.P> Load()
    {
        var list = new List<UtBackend.P>();
        foreach (var line in PlayersData.Rows)
        {
            var f = line.Split(',');
            if (f.Length < 12) continue;
            try
            {
                long id = long.Parse(f[0]); int ovr = int.Parse(f[1]);
                int[] face = new int[6]; for (int i = 0; i < 6; i++) face[i] = int.Parse(f[6 + i]);
                int team = int.Parse(f[3]), nation = int.Parse(f[4]), league = int.Parse(f[5]);
                for (int rare = 0; rare < 2; rare++)
                    list.Add(new UtBackend.P(rare == 0 ? id : 16777216L + id, id, "", ovr, f[2], nation, league, team, rare, face));
            }
            catch { }
        }
        return list;
    }

    public static void LoadTable()
    {
        int n = 0;
        foreach (var line in PlayersData.Rows)
        {
            var f = line.Split(',');
            if (f.Length < 12) continue;
            long id = long.Parse(f[0]); int ovr = int.Parse(f[1]);
            string tier = ovr >= 75 ? "gold" : ovr >= 65 ? "silver" : "bronze";
            for (int rare = 0; rare < 2; rare++)
            {
                var face = new JsonArray();
                for (int i = 0; i < 6; i++) face.Add((long)int.Parse(f[6 + i]));
                Catalog.Players[rare == 0 ? id : 16777216L + id] = new JsonObject
                {
                    ["rating"] = (long)ovr, ["preferredPosition"] = f[2], ["teamid"] = long.Parse(f[3]), ["nation"] = long.Parse(f[4]),
                    ["leagueId"] = long.Parse(f[5]), ["assetId"] = id, ["tier"] = tier, ["rareflag"] = (long)rare, ["face"] = face,
                };
            }
            n++;
        }
        if (n > 0) Log.Info($"PlayersData.cs: {n} FIFA 16 players ({n * 2} cards) loaded");
    }

    public static bool Validate(params object[] anything)
    {
        int n = PlayersData.Rows.Length, withNames = 0;
        foreach (var line in PlayersData.Rows) if (line.Split(',').Length >= 13) withNames++;
        Console.WriteLine($"PlayersData.cs: {n} players, {withNames} with names" + (n == 0 ? "  (EMPTY STUB: run --make-players first)" : withNames == 0 ? "  (no names: re-run --make-players with the new builder)" : ""));
        return n > 0;
    }

    static IEnumerable<List<string>> ReadCsv(string path)
    {
        using var sr = new StreamReader(path, new UTF8Encoding(false), true, 1 << 20);
        var row = new List<string>(); var sb = new StringBuilder(); bool q = false;
        int ci;
        while ((ci = sr.Read()) >= 0)
        {
            char c = (char)ci;
            if (q)
            {
                if (c == '"') { if (sr.Peek() == '"') { sr.Read(); sb.Append('"'); } else q = false; }
                else sb.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\n') { if (sb.Length > 0 && sb[^1] == '\r') sb.Length--; row.Add(sb.ToString()); sb.Clear(); yield return row; row = new List<string>(); }
            else sb.Append(c);
        }
        if (sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); yield return row; }
    }

    static int Num(string v) => string.IsNullOrWhiteSpace(v) ? 0 : double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)d : 0;

    public static void WriteCs(string csv, string outPath)
    {
        if (!File.Exists(csv)) { Console.WriteLine("CSV not found: " + csv); return; }
        if (string.IsNullOrEmpty(outPath))
        {
            string dir = null;
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (var d = new DirectoryInfo(start); d != null && dir == null; d = d.Parent)
                    if (File.Exists(Path.Combine(d.FullName, "LocalFUT16.csproj"))) dir = d.FullName;
            outPath = Path.Combine(dir ?? Directory.GetCurrentDirectory(), "PlayersData.cs");
        }
        Console.WriteLine("Reading " + csv + " (FIFA 16 rows only; large files take a minute)...");

        const int WantVersion = 16;
        var hdr = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Col(params string[] names) { foreach (var n in names) if (hdr.TryGetValue(n, out var i)) return i; return -1; }
        string G(List<string> r, int i) => i >= 0 && i < r.Count ? r[i] : "";
        int cShort = -1, cLong = -1, cId = -1, cVer = -1, cUpd = -1, cOvr = -1, cPos = -1, cTeam = -1, cNat = -1, cLeague = -1;
        int[] cOut = null, cGk = null, cAttr = null;
        var best = new Dictionary<int, (int upd, int[] d, string pos, string nm)>();
        bool first = true;

        foreach (var r in ReadCsv(csv))
        {
            if (first)
            {
                first = false;
                for (int i = 0; i < r.Count; i++) hdr[r[i].Trim()] = i;
                cShort = Col("short_name", "Name", "name"); cLong = Col("long_name"); cId = Col("player_id", "sofifa_id", "ID", "id"); cVer = Col("fifa_version"); cUpd = Col("fifa_update", "update");
                cOvr = Col("overall", "Overall"); cPos = Col("player_positions", "Position", "Positions", "position");
                cTeam = Col("club_team_id", "Club_ID", "club_id"); cNat = Col("nationality_id", "Nationality_ID", "nation_id"); cLeague = Col("league_id", "League_ID");
                cOut = new[] { Col("pace"), Col("shooting"), Col("passing"), Col("dribbling"), Col("defending"), Col("physic", "physical") };
                cGk = new[] { Col("goalkeeping_diving", "gk_diving", "GKDiving"), Col("goalkeeping_handling", "gk_handling", "GKHandling"), Col("goalkeeping_kicking", "gk_kicking", "GKKicking"),
                              Col("goalkeeping_reflexes", "gk_reflexes", "GKReflexes"), Col("goalkeeping_speed", "gk_speed", "pace"), Col("goalkeeping_positioning", "gk_positioning", "GKPositioning") };
                cAttr = new[] { Col("movement_acceleration", "acceleration"), Col("movement_sprint_speed", "sprint_speed"), Col("attacking_finishing", "finishing"), Col("power_shot_power", "shot_power"),
                                Col("attacking_short_passing", "short_passing"), Col("skill_long_passing", "long_passing"), Col("skill_dribbling", "dribbling"), Col("skill_ball_control", "ball_control"),
                                Col("defending_marking_awareness", "marking"), Col("defending_standing_tackle", "standing_tackle"), Col("power_strength", "strength"), Col("power_stamina", "stamina") };
                if (cId < 0 || cOvr < 0) { Console.WriteLine("CSV has no player id / overall column"); return; }
                continue;
            }
            if (cVer >= 0 && Num(G(r, cVer)) != WantVersion) continue;
            int pid = Num(G(r, cId)); if (pid <= 0) continue;
            int upd = Num(G(r, cUpd));
            if (best.TryGetValue(pid, out var old) && old.upd > upd) continue;
            string pos = G(r, cPos).Split(',')[0].Trim().ToUpperInvariant(); if (pos == "") pos = "CM";
            var d = new int[10];
            d[0] = Num(G(r, cOvr)); d[1] = Num(G(r, cTeam)); d[2] = Num(G(r, cNat)); d[3] = Num(G(r, cLeague));
            if (pos == "GK") for (int i = 0; i < 6; i++) d[4 + i] = Num(G(r, cGk[i]));
            else
            {
                for (int i = 0; i < 6; i++) d[4 + i] = Num(G(r, cOut[i]));
                if (d[4] + d[5] + d[6] + d[7] + d[8] + d[9] == 0)
                {
                    int a(int k) => Num(G(r, cAttr[k]));
                    d[4] = (a(0) + a(1)) / 2; d[5] = (a(2) + a(3)) / 2; d[6] = (a(4) + a(5)) / 2;
                    d[7] = (a(6) + a(7)) / 2; d[8] = (a(8) + a(9)) / 2; d[9] = (a(10) + a(11)) / 2;
                }
            }
            best[pid] = (upd, d, pos, (G(r, cShort) + "|" + G(r, cLong)).Replace(",", " ").Replace("\"", "").Replace("\\", ""));
        }

        int written = 0, noIds = 0;
        var sb = new StringBuilder();
        sb.AppendLine("// GENERATED by:  LocalFUT16 --make-players " + Path.GetFileName(csv) + "   (FIFA 16 players)");
        sb.AppendLine("// Row: id,overall,position,clubTeamId,nationId,leagueId,face0..face5,short|long name   (rare card = 16777216 + id, made at load)");
        sb.AppendLine("namespace LocalFut15;");
        sb.AppendLine("static class PlayersData");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly string[] Rows =");
        sb.AppendLine("    {");
        foreach (var kv in best.OrderBy(k => k.Key))
        {
            var d = kv.Value.d; if (d[0] < 40) continue;
            if (d[1] == 0 || d[2] == 0) noIds++;
            sb.Append("        \"").Append(kv.Key).Append(',').Append(d[0]).Append(',').Append(kv.Value.pos).Append(',')
              .Append(d[1]).Append(',').Append(d[2]).Append(',').Append(d[3]);
            for (int i = 0; i < 6; i++) sb.Append(',').Append(d[4 + i]);
            sb.Append(',').Append(kv.Value.nm);
            sb.AppendLine("\",");
            written++;
        }
        sb.AppendLine("    };");
        sb.AppendLine("}");
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Wrote {written} FIFA 16 players ({written * 2} cards) -> {outPath}");
        if (written == 0) Console.WriteLine("NO PLAYERS FOUND: check the CSV has fifa_version 16 rows (or use a FIFA 16 only file).");
        if (written > 0 && noIds > written / 2)
            Console.WriteLine("WARNING: this CSV has no club_team_id / nationality_id columns, cards will have no club badge or flag. Use male_players.csv from the EA Sports FC 24 Complete Player Dataset.");
        Console.WriteLine("Now rebuild and run the server (dotnet run).");
    }
}
