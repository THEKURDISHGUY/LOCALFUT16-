#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static int _totwCycle = 0;
    const int TotwFirstWin = 5000, TotwWinCoins = 2000, TotwDrawCoins = 500;

    static JsonObject TotwChallengeState()
    {
        if (Club["totwChallenge"] is not JsonObject s)
        {
            s = new JsonObject { ["weekId"] = 1, ["played"] = 0, ["wins"] = 0, ["draws"] = 0, ["losses"] = 0, ["firstWinDone"] = false };
            Club["totwChallenge"] = s;
        }
        return s;
    }
    static int TI(JsonObject o, string k) { try { return (int)L(o[k]); } catch { return 0; } }

    static JsonArray TotwOpponentCards()
    {
        var items = (JsonArray)Club["items"]!;
        bool IsOpp(JsonNode i) { try { return i?["totwOpp"]?.GetValue<bool>() == true; } catch { return false; } }
        if (!items.Any(i => IsOpp(i)))
        {
            foreach (var p in Db.Where(x => x.Set == "totw").Take(25)) { var it = Item(p, Q(p.Rating)); it["totwOpp"] = true; items.Add(it); }
            Save();
            Console.WriteLine("[TOTW-CH] added the TOTW cards to the item list");
        }
        var arr = new JsonArray();
        foreach (var i in items) if (IsOpp(i)) arr.Add(i.DeepClone());
        return arr;
    }

    static JsonObject TotwSquad(JsonArray cards)
    {
        var rest = cards.Select(c => (JsonObject)c.DeepClone()).OrderByDescending(c => (int)L(c["rating"])).ToList();
        int[] want = { 1, 4, 3, 3 };
        var xi = new List<JsonObject>();
        for (int g = 0; g < 4; g++) xi.AddRange(rest.Where(c => PosGroup(c["preferredPosition"]?.GetValue<string>() ?? "ST") == g).Take(want[g]));
        var all = xi.Concat(rest.Where(c => !xi.Contains(c)).Take(7)).ToList();
        var slots = new JsonArray();
        for (int i = 0; i < 18; i++)
        {
            var card = i < all.Count ? (JsonObject)all[i].DeepClone() : null;
            if (card != null) { card.Remove("totwOpp"); card["dream"] = false; }
            slots.Add(new JsonObject { ["index"] = i, ["itemData"] = card ?? new JsonObject { ["id"] = 0L, ["dream"] = false }, ["kitNumber"] = i + 1 });
        }
        int ovr = xi.Count > 0 ? (int)xi.Average(c => (int)L(c["rating"])) : 0;
        return new JsonObject
        {
            ["id"] = 0, ["custom"] = "[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]", ["squadName"] = Brand + " Team of the Week",
            ["chemistry"] = 100, ["starRating"] = 5, ["rating"] = ovr, ["formation"] = "f433", ["squadType"] = "REGULAR_SQUAD",
            ["manager"] = new JsonArray(new JsonObject { ["id"] = 0, ["dream"] = false }), ["players"] = slots,
        };
    }

    static JsonObject TotwEntries(JsonObject squad, string mode)
    {
        var entries = new JsonArray();
        if (mode == "state") { entries.Add(new JsonObject { ["key"] = 1L, ["value"] = 2L }); entries.Add(new JsonObject { ["key"] = 2L, ["value"] = 0L }); }
        else
        {
            long k = 0;
            foreach (var sl in (JsonArray)squad["players"])
            {
                if (k >= 11) break;
                var it = sl["itemData"]; long rid = 0, asset = 0;
                try { rid = L(it["resourceId"]); asset = L(it["assetId"]); } catch { }
                entries.Add(new JsonObject { ["key"] = k++, ["value"] = mode == "asset" ? asset : mode == "defid" ? (rid & 0xFFFFFF) : rid });
            }
        }
        Console.WriteLine($"[TOTW-CH] entries ({mode}): " + string.Join(",", entries.Select(e => e["key"] + "=" + e["value"])));
        return new JsonObject { ["entries"] = entries };
    }

    static JsonArray Slots11(JsonObject squad)
    {
        var slots = new JsonArray();
        foreach (var sl in (JsonArray)squad["players"]) if (slots.Count < 11) slots.Add(sl.DeepClone());
        return slots;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleTotw(string method, string path, string target, byte[] body)
    {
        if (!path.Contains("fifa16/") || !path.Contains("totw")) return null;
        if (path.Contains("store") || path.Contains("purchase") || path.Contains("/club") || path.Contains("squad") || path.Contains("tradepile")) return null;
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);

        string bodyTxt = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 600));
        Console.WriteLine($"[TOTW-REQ] {method} {target} body: {bodyTxt}");
        var s = TotwChallengeState();

        if (method == "GET")
        {
            string tt = "1"; try { var f = FindFile("totw_try.txt"); if (File.Exists(f)) tt = File.ReadAllText(f).Trim(); } catch { }
            if (tt == "auto") tt = (((_totwCycle++) % 19) + 1).ToString();

            var cards = TotwOpponentCards();
            int n = cards.Count;
            var info = new JsonObject
            {
                ["weekId"] = TotwChallengeState()["weekId"]!.DeepClone(), ["id"] = 1, ["name"] = Brand + " Team of the Week",
                ["state"] = "AVAILABLE", ["status"] = "AVAILABLE", ["available"] = true, ["enabled"] = true, ["unlocked"] = true,
                ["completed"] = TI(s, "wins") > 0, ["played"] = s["played"]!.DeepClone(), ["wins"] = s["wins"]!.DeepClone(),
                ["draws"] = s["draws"]!.DeepClone(), ["losses"] = s["losses"]!.DeepClone(),
                ["rewardCoins"] = (bool)s["firstWinDone"]! ? TotwWinCoins : TotwFirstWin,
            };
            Console.WriteLine($"[TOTW-CH] sending shape {tt} ({n} TOTW cards)");
            var sq = TotwSquad(cards);
            switch (tt)
            {
                case "2": return J(new JsonObject { ["entries"] = new JsonArray(new JsonObject { ["key"] = 0, ["value"] = 1 }, new JsonObject { ["key"] = 1, ["value"] = n }) });
                case "3": return J(cards);
                case "4": { var o = (JsonObject)info.DeepClone(); o["players"] = cards; o["itemData"] = cards.DeepClone(); return J(o); }
                case "5": return J(new JsonObject { ["items"] = cards, ["itemData"] = cards.DeepClone(), ["totalResults"] = n });
                case "6": return J(new JsonObject { ["totw"] = info, ["squad"] = sq });
                case "7":
                    return J(new JsonObject
                    {
                        ["totw"] = info.DeepClone(), ["totwInfo"] = info.DeepClone(), ["challenge"] = info.DeepClone(),
                        ["players"] = cards, ["items"] = cards.DeepClone(), ["itemData"] = cards.DeepClone(), ["squad"] = sq.DeepClone(),
                        ["count"] = n, ["total"] = n, ["totalResults"] = n, ["offset"] = 0, ["endOfList"] = true,
                        ["state"] = "AVAILABLE", ["weekId"] = 1, ["credits"] = Club["coins"]!.DeepClone(),
                    });
                case "8": return J(new JsonObject { ["squad"] = sq, ["squads"] = new JsonArray(sq.DeepClone()), ["totw"] = info });
                case "9":
                    {
                        var o = (JsonObject)sq.DeepClone();
                        o["totw"] = info.DeepClone(); o["weekId"] = 1; o["state"] = "AVAILABLE"; o["count"] = n; o["total"] = n;
                        return J(o);
                    }
                case "10":
                    {
                        var o = (JsonObject)sq.DeepClone(); o.Remove("manager"); o.Remove("custom");
                        var pl = (JsonArray)o["players"]; while (pl.Count > 11) pl.RemoveAt(pl.Count - 1);
                        return J(o);
                    }
                case "11":
                    {
                        var slots = new JsonArray();
                        foreach (var sl in (JsonArray)sq["players"]) if (slots.Count < 11) slots.Add(sl.DeepClone());
                        return J(new JsonObject { ["players"] = slots });
                    }
                case "12": { var o = new JsonObject { ["formation"] = "f433", ["players"] = Slots11(sq) }; return J(o); }
                case "13": return J(new JsonObject { ["chemistry"] = 100, ["starRating"] = 5, ["rating"] = sq["rating"]!.DeepClone(), ["players"] = Slots11(sq) });
                case "14": return J(new JsonObject { ["id"] = 0, ["squadName"] = sq["squadName"]!.DeepClone(), ["squadType"] = "REGULAR_SQUAD", ["players"] = Slots11(sq) });
                case "15": return J(new JsonObject { ["formation"] = "f433", ["chemistry"] = 100, ["starRating"] = 5, ["rating"] = sq["rating"]!.DeepClone(), ["players"] = Slots11(sq) });
                case "1":
                    return J(sq);
                case "16": return J(TotwEntries(sq, "state"));
                case "17": return J(TotwEntries(sq, "resource"));
                case "18": return J(TotwEntries(sq, "asset"));
                case "19": return J(TotwEntries(sq, "defid"));
                default:
                    return J(TotwEntries(sq, "resource"));
            }
        }

        var req = default(JsonNode); try { req = JsonNode.Parse(bodyTxt.Length == 0 ? "{}" : Encoding.UTF8.GetString(body)); } catch { }
        string joined = (path + " " + (req?.ToJsonString() ?? "")).ToLowerInvariant();
        bool isResult = joined.Contains("result") || joined.Contains("win") || joined.Contains("loss") || joined.Contains("draw")
                        || joined.Contains("goal") || joined.Contains("complete") || joined.Contains("finish");
        int reward = 0;
        if (isResult)
        {
            int gf = 0, ga = 0;
            try { gf = (int)L(req?["goalsFor"] ?? req?["homeGoals"] ?? req?["goals"] ?? req?["scoreFor"]); } catch { }
            try { ga = (int)L(req?["goalsAgainst"] ?? req?["awayGoals"] ?? req?["scoreAgainst"]); } catch { }
            bool won = (bool?)(req?["won"]?.GetValue<bool>()) ?? (joined.Contains("\"win\"") || joined.Contains("/win") || gf > ga);
            bool draw = !won && (joined.Contains("draw") || (gf == ga && (gf > 0 || joined.Contains("goal"))));
            if (won)
            {
                s["wins"] = TI(s, "wins") + 1;
                reward = (bool)s["firstWinDone"]! ? TotwWinCoins : TotwFirstWin; s["firstWinDone"] = true;
            }
            else if (draw) { s["draws"] = TI(s, "draws") + 1; reward = TotwDrawCoins; }
            else s["losses"] = TI(s, "losses") + 1;
            s["played"] = TI(s, "played") + 1;
            if (reward > 0) Club["coins"] = (int)L(Club["coins"]) + reward;
            Console.WriteLine($"[TOTW-CH] result saved: {TI(s, "wins")}W {TI(s, "draws")}D {TI(s, "losses")}L, +{reward} coins");
        }
        Save();
        return J(new JsonObject
        {
            ["weekId"] = 1, ["state"] = "AVAILABLE", ["wins"] = s["wins"]!.DeepClone(), ["draws"] = s["draws"]!.DeepClone(), ["losses"] = s["losses"]!.DeepClone(),
            ["reward"] = reward, ["credits"] = Club["coins"]!.DeepClone(),
        });
    }
}
