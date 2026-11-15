#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static int _seasonCycle = 0;
    const int SeasonGames = 10;
    const int SeasonId = 3001;

    static JsonObject SeasonState()
    {
        if (Club["season"] is not JsonObject s)
        {
            s = new JsonObject
            {
                ["id"] = SeasonId, ["divisionId"] = 10, ["points"] = 0, ["played"] = 0,
                ["wins"] = 0, ["draws"] = 0, ["losses"] = 0, ["goalsFor"] = 0, ["goalsAgainst"] = 0,
                ["state"] = "NOT_STARTED", ["seasonsPlayed"] = 0,
            };
            Club["season"] = s;
        }
        return s;
    }

    static int SI(JsonObject o, string k) { try { return (int)L(o[k]); } catch { return 0; } }

    static JsonObject SeasonEntry(bool full, int division)
    {
        var s = SeasonState();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var e = new JsonObject
        {
            ["id"] = SeasonId, ["seasonId"] = SeasonId, ["name"] = Brand + " Online Season",
            ["state"] = s["state"]!.DeepClone(), ["divisionId"] = division, ["division"] = division,
        };
        if (full)
        {
            e["status"] = s["state"]!.DeepClone(); e["active"] = true; e["type"] = "ONLINE"; e["mode"] = "ONLINE";
            e["points"] = s["points"]!.DeepClone(); e["gamesPlayed"] = s["played"]!.DeepClone(); e["played"] = s["played"]!.DeepClone();
            e["totalGames"] = SeasonGames; e["gamesPerSeason"] = SeasonGames; e["gamesRemaining"] = SeasonGames - SI(s, "played");
            e["wins"] = s["wins"]!.DeepClone(); e["draws"] = s["draws"]!.DeepClone(); e["losses"] = s["losses"]!.DeepClone();
            e["goalsFor"] = s["goalsFor"]!.DeepClone(); e["goalsAgainst"] = s["goalsAgainst"]!.DeepClone();
            e["startTime"] = now - 86400; e["endTime"] = now + 86400L * 365; e["unlocked"] = true; e["enabled"] = true;
            e["entryFee"] = 0; e["requirements"] = new JsonArray(); e["rewards"] = new JsonArray();
        }
        return e;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleSeason(string method, string path, string target, byte[] body)
    {
        if (!path.Contains("fifa16/season")) return null;
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);

        string bodyTxt = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 600));
        Console.WriteLine($"[SEASON-REQ] {method} {target} body: {bodyTxt}");

        int division = 11;
        int qi = target.IndexOf('?');
        if (qi >= 0)
            foreach (var kv in target[(qi + 1)..].Split('&'))
            {
                var p = kv.Split('=', 2);
                if (p.Length == 2 && p[0].Equals("divisionList", StringComparison.OrdinalIgnoreCase)) int.TryParse(p[1].Split(',')[0], out division);
            }

        var s = SeasonState();

        if (method == "GET")
        {
            string tt = "1"; try { var f = FindFile("season_try.txt"); if (File.Exists(f)) tt = File.ReadAllText(f).Trim(); } catch { }
            if (tt == "auto") tt = (((_seasonCycle++) % 6) + 1).ToString();
            bool mine = path.Contains("/user");
            var full = new JsonArray(SeasonEntry(true, division));
            var min = new JsonArray(SeasonEntry(false, division));
            var none = new JsonArray();
            if (mine && s["state"]!.ToString() == "NOT_STARTED") full = new JsonArray();
            int n = full.Count;
            Console.WriteLine($"[SEASON] sending list variant {tt} ({n} season(s), division {division})");
            switch (tt)
            {
                case "2": return J(new JsonObject { ["seasons"] = min });
                case "3": return J(new JsonObject { ["seasonList"] = full, ["userSeasons"] = mine ? full.DeepClone() : none });
                case "4": return J(new JsonObject { ["list"] = full, ["seasons"] = min, ["count"] = n, ["total"] = n });
                case "5": return J(full);
                case "6": return J(new JsonObject { ["divisionList"] = new JsonArray(division), ["seasons"] = full, ["totalResults"] = n, ["endOfList"] = true });
                default:
                    return J(new JsonObject
                    {
                        ["seasons"] = full, ["seasonList"] = full.DeepClone(), ["userSeasons"] = mine ? full.DeepClone() : none.DeepClone(),
                        ["count"] = n, ["total"] = n, ["totalResults"] = n, ["offset"] = 0, ["endOfList"] = true,
                    });
            }
        }

        var req = default(JsonNode); try { req = JsonNode.Parse(bodyTxt.Length == 0 ? "{}" : Encoding.UTF8.GetString(body)); } catch { }
        string joined = path + " " + (req?.ToJsonString() ?? "");
        joined = joined.ToLowerInvariant();

        if (s["state"]!.ToString() == "NOT_STARTED") s["state"] = "IN_PROGRESS";

        bool isResult = joined.Contains("result") || joined.Contains("win") || joined.Contains("loss") || joined.Contains("draw")
                        || joined.Contains("goal") || joined.Contains("match");
        if (isResult)
        {
            int gf = 0, ga = 0;
            try { gf = (int)L(req?["goalsFor"] ?? req?["homeGoals"] ?? req?["goals"] ?? req?["scoreFor"]); } catch { }
            try { ga = (int)L(req?["goalsAgainst"] ?? req?["awayGoals"] ?? req?["scoreAgainst"]); } catch { }
            bool won = (bool?)(req?["won"]?.GetValue<bool>()) ?? (joined.Contains("\"win\"") || joined.Contains("/win") || (gf > ga));
            bool draw = !won && (joined.Contains("draw") || (gf == ga && (gf > 0 || joined.Contains("goal"))));
            if (won) { s["wins"] = SI(s, "wins") + 1; s["points"] = SI(s, "points") + 3; }
            else if (draw) { s["draws"] = SI(s, "draws") + 1; s["points"] = SI(s, "points") + 1; }
            else s["losses"] = SI(s, "losses") + 1;
            s["played"] = SI(s, "played") + 1;
            s["goalsFor"] = SI(s, "goalsFor") + gf; s["goalsAgainst"] = SI(s, "goalsAgainst") + ga;

            if (SI(s, "played") >= SeasonGames)
            {
                int pts = SI(s, "points");
                int reward = 500 + pts * 100;
                Club["coins"] = (int)L(Club["coins"]) + reward;
                if (pts >= 20 && SI(s, "divisionId") > 1) s["divisionId"] = SI(s, "divisionId") - 1;
                s["seasonsPlayed"] = SI(s, "seasonsPlayed") + 1;
                Console.WriteLine($"[SEASON] finished with {pts} points, +{reward} coins, division now {SI(s, "divisionId")}");
                foreach (var k in new[] { "points", "played", "wins", "draws", "losses", "goalsFor", "goalsAgainst" }) s[k] = 0;
                s["state"] = "NOT_STARTED";
            }
        }
        Save();
        var ans = SeasonEntry(true, division);
        ans["credits"] = Club["coins"]!.DeepClone();
        return J(ans);
    }
}
