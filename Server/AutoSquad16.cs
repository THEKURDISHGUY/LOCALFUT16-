#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static bool IsPlayerCard(long id)
    {
        foreach (var i in (JsonArray)Club["items"]!) if (L(i?["id"]) == id) return (i["itemType"]?.ToString() ?? "player") == "player";
        return false;
    }

    static bool ClubSearchMatch(JsonNode c, Dictionary<string, string> q)
    {
        string it = (c["itemType"]?.ToString() ?? "player").ToLowerInvariant();
        string ty = q.TryGetValue("type", out var t0) ? (t0 ?? "").ToLowerInvariant() : "";
        switch (ty)
        {
            case "": case "any": case "-1": break;
            case "player": if (it != "player") return false; break;
            case "staff": case "manager": if (it != "staff" && it != "manager") return false; break;
            case "badge": case "custom": if (it != "custom") return false; break;
            case "kit": case "stadium": case "ball": case "trophy": if (it != ty) return false; break;
            case "equippables": if (it != "kit" && it != "stadium" && it != "ball" && it != "custom") return false; break;
            case "consumable": case "development": case "training": return false;
            default: if (it != ty) return false; break;
        }
        if (it != "player") return true;

        string pos = (q.TryGetValue("position", out var p0) ? p0 : q.TryGetValue("pos", out var p1) ? p1 : "").ToUpperInvariant();
        string cp = (c["preferredPosition"]?.ToString() ?? "").ToUpperInvariant();
        if (pos != "" && pos != "ANY" && pos != "-1")
        {
            if (pos == "DEFENSE" || pos == "DEFENDER") { if (PosGroup(cp) != 1) return false; }
            else if (pos == "MIDFIELD" || pos == "MIDFIELDER") { if (PosGroup(cp) != 2) return false; }
            else if (pos == "ATTACK" || pos == "ATTACKER" || pos == "FORWARD") { if (PosGroup(cp) != 3) return false; }
            else if (cp != pos) return false;
        }
        long Q2(string k) => q.TryGetValue(k, out var v) && long.TryParse(v, out var n) ? n : -1;
        long nat = Q2("nation"), lg = Q2("league"), tm = Q2("team");
        if (nat >= 0 && L(c["nation"]) != nat) return false;
        if (lg >= 0 && L(c["leagueId"]) != lg) return false;
        if (tm >= 0 && L(c["teamid"]) != tm) return false;
        string lev = (q.TryGetValue("level", out var l0) ? l0 : "any").ToLowerInvariant();
        if (lev != "" && lev != "any" && lev != "-1")
        {
            long r = L(c["rating"]); bool special = L(c["rareflag"]) > 1;
            if (lev == "bronze" && !(r <= 64 && !special)) return false;
            if (lev == "silver" && !(r >= 65 && r <= 74 && !special)) return false;
            if (lev == "gold" && !(r >= 75 && !special)) return false;
            if ((lev == "sp" || lev == "special") && !special) return false;
        }
        return true;
    }

    static string SquadLayout()
    {
        try { var f = FindFile("squad_layout.txt"); if (File.Exists(f)) { var w = File.ReadAllText(f).Trim().ToLowerInvariant(); if (w == "mirror" || w == "reverse") return w; } } catch { }
        return "normal";
    }

    static void CleanSquadBody(JsonObject sb)
    {
        if (sb == null) return;
        bool changed = false;
        if (sb["players"] is JsonArray pls)
            foreach (var pl in pls)
            {
                long id = L(pl?["itemData"]?["id"]);
                if (id > 0 && !IsPlayerCard(id)) { pl!["itemData"] = new JsonObject { ["id"] = 0, ["dream"] = false }; changed = true; Console.WriteLine($"[SQUAD] item {id} is not a player card -> removed from the line-up"); }
            }
        try
        {
            long mg = L(sb["manager"]?[0]?["id"]);
            if (mg > 0 && !((JsonArray)Club["items"]!).Any(i => L(i?["id"]) == mg && i?["itemType"]?.ToString() == "staff"))
            { sb["manager"] = new JsonArray(new JsonObject { ["id"] = 0, ["dream"] = false }); changed = true; }
        }
        catch { }
        if (changed) { Club["squadBody"] = sb.DeepClone(); Save(); }
    }

    static void EnsureAutoSquad(bool forceArg = false)
    {
        if (File.Exists(FindFile("no_autosquad.txt"))) return;
        string redo = FindFile("autosquad_now.txt");
        string layout = SquadLayout();
        bool force = forceArg || File.Exists(redo) || (Club["autoSquadLayout"]?.ToString() ?? "normal") != layout;
        if (Club["autoSquadV3"] != null && !force) return;

        var items = (JsonArray)Club["items"]!;
        var unIds = new HashSet<long>(((Club["unassigned"] as JsonArray) ?? new JsonArray()).Select(n => L(n)));
        var pool = items.Where(i => i != null && (i["itemType"]?.ToString() ?? "player") == "player" && i["pile"]?.ToString() != "trade"
                                    && !IsDup(L(i["id"])) && !unIds.Contains(L(i["id"]))).Select(i => (JsonObject)i!).ToList();
        if (pool.Count < 11) { Console.WriteLine("[AUTOSQUAD] fewer than 11 players in the club -> skipped"); return; }

        string[][] slotPos =
        {
            new[] { "GK" }, new[] { "RB", "RWB" }, new[] { "CB" }, new[] { "CB" }, new[] { "LB", "LWB" },
            new[] { "RM", "RW", "RWB" }, new[] { "CM", "CDM", "CAM" }, new[] { "CM", "CDM", "CAM" }, new[] { "LM", "LW", "LWB" }, new[] { "ST", "CF" }, new[] { "ST", "CF" },
            new[] { "GK" }, new[] { "CB" }, new[] { "RB", "LB", "RWB", "LWB" }, new[] { "CM", "CDM", "CAM" }, new[] { "RM", "LM", "RW", "LW" }, new[] { "ST", "CF" }, new[] { "ST", "CF", "CAM", "RW", "LW" },
        };
        if (layout == "mirror")
        {
            void Swap(int a, int b) { var t = slotPos[a]; slotPos[a] = slotPos[b]; slotPos[b] = t; }
            Swap(1, 4); Swap(5, 8);
        }
        else if (layout == "reverse") Array.Reverse(slotPos, 0, 11);
        var used = new HashSet<long>();
        string PosOf(JsonObject p) => (p["preferredPosition"]?.ToString() ?? "").ToUpperInvariant();
        JsonObject Pick(string[] poss)
        {
            var c = pool.Where(p => !used.Contains(L(p["id"])) && poss.Contains(PosOf(p))).OrderByDescending(p => L(p["rating"])).FirstOrDefault();
            if (c == null) c = pool.Where(p => !used.Contains(L(p["id"])) && (poss[0] == "GK" || PosOf(p) != "GK")).OrderByDescending(p => L(p["rating"])).FirstOrDefault();
            if (c != null) used.Add(L(c["id"]));
            return c;
        }

        var slots = new JsonArray(); var ids = new JsonArray(); var xiRatings = new List<long>(); var names = new List<string>();
        for (int i = 0; i < slotPos.Length; i++)
        {
            var c = Pick(slotPos[i]); long id = c == null ? 0 : L(c["id"]);
            slots.Add(new JsonObject { ["index"] = i, ["itemData"] = new JsonObject { ["id"] = id, ["dream"] = false }, ["kitNumber"] = i + 1 });
            if (id > 0) { ids.Add(id); if (i < 11) xiRatings.Add(L(c["rating"])); if (i < 11) names.Add($"{PosOf(c)} {c["name"]} {c["rating"]}"); }
        }

        var mg = items.FirstOrDefault(i => i != null && (i["itemType"]?.ToString() ?? "") == "staff") as JsonObject;
        if (mg == null && Managers.Count > 0) { mg = ManagerItem(Managers[Rng.Next(Managers.Count)]); items.Add(mg); Console.WriteLine($"[AUTOSQUAD] random manager: {mg["name"]}"); }
        var mgrArr = new JsonArray(new JsonObject { ["id"] = mg == null ? 0L : L(mg["id"]), ["dream"] = false });

        int ovr = xiRatings.Count == 0 ? 0 : (int)(xiRatings.Sum() / xiRatings.Count);
        var body = new JsonObject
        {
            ["id"] = 0, ["custom"] = "[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]", ["squadName"] = ClubNameNow() + " XI", ["chemistry"] = 100,
            ["starRating"] = ovr >= 75 ? 5 : ovr >= 65 ? 3 : 2, ["rating"] = ovr, ["formation"] = "f442", ["squadType"] = "REGULAR_SQUAD", ["manager"] = mgrArr, ["players"] = slots,
        };
        Club["squadBody"] = body; Club["squad"] = ids; Club["autoSquadV3"] = 1; Club["autoSquadLayout"] = layout;
        SquadsAdoptBody();
        if (File.Exists(redo)) { try { File.Move(redo, redo + ".done", true); } catch { } }
        Save();
        Console.WriteLine($"[AUTOSQUAD] f442 ({layout}), XI + 7 subs set automatically (XI rating {ovr}): {string.Join(", ", names)}");
    }

    static JsonObject NativeSquadReply(JsonObject sb)
    {
        var all = (JsonArray)Club["items"]!;
        JsonObject CardOf(long id)
        {
            if (id <= 0) return null;
            var c = all.FirstOrDefault(x => L(x?["id"]) == id)?.DeepClone() as JsonObject;
            if (c != null) { c["dream"] = false; FillCard(c); }
            return c;
        }
        var byIdx = new Dictionary<long, JsonObject>();
        if (sb["players"] is JsonArray pls)
            foreach (var pl in pls)
            {
                long idx = L(pl?["index"]); if (idx < 0 || idx > 22 || byIdx.ContainsKey(idx)) continue;
                var card = CardOf(L(pl?["itemData"]?["id"])); if (card == null) continue;
                long kn = pl?["kitNumber"] != null ? L(pl["kitNumber"]) : idx + 1; if (kn <= 0) kn = idx + 1;
                byIdx[idx] = new JsonObject { ["index"] = idx, ["kitNumber"] = kn, ["itemData"] = card };
            }
        var players = new JsonArray(); var kick = new List<long>(); long captain = 0;
        for (long i = 0; i < 23; i++)
        {
            if (byIdx.TryGetValue(i, out var p))
            {
                players.Add(p);
                long id = L(p["itemData"]?["id"]);
                if (captain == 0) captain = id;
                if (kick.Count < 5) kick.Add(id);
            }
            else players.Add(new JsonObject { ["index"] = i, ["kitNumber"] = 0L });
        }
        while (kick.Count > 0 && kick.Count < 5) kick.Add(kick[^1]);
        var mgrArr = new JsonArray();
        long mgId = 0; try { mgId = L(sb["manager"]?[0]?["id"]); } catch { }
        var mgCard = CardOf(mgId);
        if (File.Exists(FindFile("usemanager.txt")))
            mgrArr.Add(mgCard != null ? new JsonObject { ["id"] = mgId, ["dream"] = false, ["itemData"] = mgCard } : new JsonObject { ["id"] = 0, ["dream"] = false });
        var kt = new JsonArray();
        for (int i = 0; i < kick.Count; i++) kt.Add(new JsonObject { ["index"] = (long)i, ["id"] = kick[i], ["dream"] = false });
        var __r = new JsonObject
        {
            ["id"] = L(sb["id"]), ["personaId"] = 1000000002L, ["squadName"] = sb["squadName"]?.ToString() ?? (ClubNameNow() + " XI"),
            ["formation"] = sb["formation"]?.ToString() ?? "f442", ["captain"] = captain, ["chemistry"] = SquadChemistry(sb), ["changed"] = 0L,
            ["starRating"] = sb["starRating"] != null ? L(sb["starRating"]) : 3L, ["rating"] = sb["rating"] != null ? L(sb["rating"]) : 0L,
            ["custom"] = sb["custom"]?.ToString() ?? "[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]", ["squadType"] = "REGULAR_SQUAD",
            ["players"] = players, ["actives"] = IdentityActives(), ["manager"] = mgrArr, ["club"] = new JsonArray(), ["kicktakers"] = kt,
        };
        try { File.WriteAllText(Path.Combine(Dir, "last_squad_reply.json"), __r.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); } catch { }
        return __r;
    }
}
