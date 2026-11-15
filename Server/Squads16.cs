#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

static partial class UtBackend
{
    static JsonArray SquadsArr()
    {
        if (Club["squads"] is not JsonArray a) { a = new JsonArray(); Club["squads"] = a; }
        return a;
    }

    static JsonObject SquadById(long id)
    {
        foreach (var n in SquadsArr()) if (n is JsonObject o && L(o["id"]) == id) return o;
        return null;
    }

    static long ActiveSquadId()
    {
        long a = L(Club["activeSquadId"]);
        if (a > 0 && SquadById(a) != null) return a;
        foreach (var n in SquadsArr()) if (n is JsonObject o && L(o["id"]) > 0) return L(o["id"]);
        return 0;
    }

    static long NextSquadId()
    {
        long max = 0;
        foreach (var n in SquadsArr()) if (n is JsonObject o) max = Math.Max(max, L(o["id"]));
        return max + 1;
    }

    static bool IsAutoSquadName(string n)
        => string.IsNullOrWhiteSpace(n) || n == Brand + " XI" || n == ClubNameNow() + " XI" || n == ClubNameNow() || n == Brand;

    static bool IsStaffId(long id)
    {
        if (id <= 0) return false;
        foreach (var i in (JsonArray)Club["items"]!) if (L(i?["id"]) == id) return (i["itemType"]?.ToString() ?? "") == "staff";
        return false;
    }

    static JsonObject PickRealManager()
    {
        var items = (JsonArray)Club["items"]!;
        var unIds = new HashSet<long>(((Club["unassigned"] as JsonArray) ?? new JsonArray()).Select(n => L(n)));
        var mg = items.FirstOrDefault(i => i != null && (i["itemType"]?.ToString() ?? "") == "staff" && i["pile"]?.ToString() != "trade"
                                           && !IsDup(L(i["id"])) && !unIds.Contains(L(i["id"]))) as JsonObject;
        if (mg == null && Managers.Count > 0)
        {
            mg = ManagerItem(Managers[Rng.Next(Managers.Count)]); items.Add(mg);
            Console.WriteLine($"[SQUADS] no manager card in the club -> real coach added: {mg["name"]} (card id {mg["id"]}, coach id {mg["resourceId"]})");
        }
        if (mg == null)
        {
            mg = ManagerItem(new M(5000001, "Stand-in Manager", "")); items.Add(mg);
            Console.WriteLine("[SQUADS] no managers loaded -> stand-in manager card added so you can play");
        }
        return mg;
    }

    static bool EnsureSquadManager(JsonObject sq)
    {
        long mg = 0; try { mg = L(sq["manager"]?[0]?["id"]); } catch { }
        if (IsStaffId(mg)) return false;
        long use = 0;
        var act = SquadById(ActiveSquadId());
        if (act != null) { try { use = L(act["manager"]?[0]?["id"]); } catch { } }
        if (!IsStaffId(use)) { var m = PickRealManager(); use = m == null ? 0L : L(m["id"]); }
        sq["manager"] = new JsonArray(new JsonObject { ["id"] = use, ["dream"] = false });
        if (use > 0) Console.WriteLine($"[SQUADS] squad {L(sq["id"])}: manager set to card {use} ({FindItem(use)?["name"]})");
        return true;
    }

    static void SquadCleanSlots(JsonObject sq)
    {
        if (sq["players"] is not JsonArray pls) return;
        foreach (var slot in pls)
        {
            long id = L(slot?["itemData"]?["id"]);
            if (id > 0 && (IsDup(id) || !IsPlayerCard(id))) slot["itemData"] = new JsonObject { ["id"] = 0, ["dream"] = false };
        }
    }

    static void SquadPut(JsonObject sq)
    {
        var arr = SquadsArr(); long id = L(sq["id"]);
        for (int i = 0; i < arr.Count; i++)
            if (arr[i] is JsonObject o && L(o["id"]) == id) { arr[i] = sq; return; }
        arr.Add(sq);
    }

    static (int xi, int subs) SquadCount(JsonObject sq)
    {
        int xi = 0, sb = 0;
        if (sq?["players"] is JsonArray pl)
            foreach (var s in pl)
            {
                if (L(s?["itemData"]?["id"]) <= 0) continue;
                long ix = L(s["index"]);
                if (ix < 11) xi++; else if (ix < 18) sb++;
            }
        return (xi, sb);
    }

    static void EnsureSquads()
    {
        var arr = SquadsArr();
        if (arr.Count == 0)
        {
            var body = (Club["squadBody"] as JsonObject)?.DeepClone() as JsonObject;
            if (body == null || SquadCount(body) != (11, 7))
            {
                try { EnsureAutoSquad(true); } catch (Exception ex) { Console.WriteLine("[SQUADS] auto squad failed: " + ex.Message); }
                body = (Club["squadBody"] as JsonObject)?.DeepClone() as JsonObject;
            }
            if (body == null || (body["players"] as JsonArray)?.Count is null or 0)
            {
                var cards = (JsonArray)Club["items"]!;
                var ids = ((JsonArray)(Club["squad"] ?? new JsonArray())).Select(n => L(n)).ToList();
                var slots = new JsonArray();
                for (int i = 0; i < 18; i++)
                    slots.Add(new JsonObject { ["index"] = i, ["itemData"] = new JsonObject { ["id"] = i < ids.Count ? ids[i] : 0L, ["dream"] = false }, ["kitNumber"] = i + 1 });
                int ovr = ids.Count == 0 ? 0 : ids.Take(11).Select(id => (int)L(cards.FirstOrDefault(c => L(c?["id"]) == id)?["rating"])).Sum() / Math.Max(1, Math.Min(11, ids.Count));
                body = new JsonObject
                {
                    ["id"] = 1L, ["custom"] = "[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]", ["squadName"] = "Squad 1", ["chemistry"] = 100,
                    ["starRating"] = ovr >= 75 ? 5 : ovr >= 65 ? 3 : 2, ["rating"] = ovr, ["formation"] = "f442", ["squadType"] = "REGULAR_SQUAD",
                    ["manager"] = new JsonArray(new JsonObject { ["id"] = 0, ["dream"] = false }), ["players"] = slots,
                };
            }
            body["id"] = 1L;
            if (IsAutoSquadName(body["squadName"]?.ToString())) body["squadName"] = "Squad 1";
            SquadCleanSlots(body); EnsureSquadManager(body);
            arr.Add(body); Club["activeSquadId"] = 1L; Club["squadBody"] = body.DeepClone(); Save();
            Console.WriteLine($"[SQUADS] \"{body["squadName"]}\" (id 1) created automatically, manager card {L(body["manager"]?[0]?["id"])}");
            return;
        }
        bool changed = false;
        if (Club["squadFixV1"] == null)
        {
            Club["squadFixV1"] = 1; changed = true;
            if (SquadById(ActiveSquadId()) is JsonObject a0 && SquadCount(a0) != (11, 7))
            {
                Console.WriteLine($"[SQUADS] squad {L(a0["id"])} had {SquadCount(a0).xi}/11 in the XI and {SquadCount(a0).subs}/7 subs -> filling it with your best players by position");
                try { EnsureAutoSquad(true); SquadsAdoptBody(); } catch (Exception ex) { Console.WriteLine("[SQUADS] auto squad failed: " + ex.Message); }
            }
        }
        if (SquadById(L(Club["activeSquadId"])) == null) { Club["activeSquadId"] = ActiveSquadId(); changed = true; }
        foreach (var n in arr) if (n is JsonObject o && EnsureSquadManager(o)) changed = true;
        if (changed)
        {
            if (SquadById(ActiveSquadId()) is JsonObject ac) Club["squadBody"] = ac.DeepClone();
            Save();
        }
    }

    static void SquadsAdoptBody()
    {
        if (SquadsArr().Count == 0) return;
        long id = ActiveSquadId(); if (id <= 0) return;
        var nb = (Club["squadBody"] as JsonObject)?.DeepClone() as JsonObject; if (nb == null) return;
        var old = SquadById(id);
        nb["id"] = id;
        if (IsAutoSquadName(nb["squadName"]?.ToString())) nb["squadName"] = old?["squadName"]?.ToString() ?? ("Squad " + id);
        SquadCleanSlots(nb); EnsureSquadManager(nb);
        SquadPut(nb); Club["squadBody"] = nb.DeepClone();
    }

    static void PurgeFromSquads(long cardId)
    {
        void Clear(JsonObject sq)
        {
            if (sq?["players"] is JsonArray pl)
                foreach (var slot in pl) if (L(slot?["itemData"]?["id"]) == cardId) slot["itemData"] = new JsonObject { ["id"] = 0, ["dream"] = false };
        }
        foreach (var n in SquadsArr()) Clear(n as JsonObject);
        Clear(Club["squadBody"] as JsonObject);
    }

    static JsonObject SquadStore(long id, JsonObject req)
    {
        var old = SquadById(id);
        var sq = (JsonObject)req.DeepClone();
        SquadCleanSlots(sq);
        bool makeActive = false;
        try { if (sq["active"] != null && sq["active"].GetValue<bool>()) makeActive = true; } catch { }
        sq.Remove("active");
        if (old != null)
        {
            if (sq["manager"] == null && old["manager"] != null) sq["manager"] = old["manager"].DeepClone();
            if (string.IsNullOrWhiteSpace(sq["squadName"]?.ToString())) sq["squadName"] = old["squadName"]?.ToString() ?? ("Squad " + id);
            if (sq["formation"] == null && old["formation"] != null) sq["formation"] = old["formation"].DeepClone();
            if (sq["players"] == null && old["players"] != null) sq["players"] = old["players"].DeepClone();
        }
        else if (string.IsNullOrWhiteSpace(sq["squadName"]?.ToString())) sq["squadName"] = "Squad " + id;
        if (sq["players"] == null) sq["players"] = new JsonArray();
        if (sq["formation"] == null) sq["formation"] = "f442";
        if (sq["custom"] == null) sq["custom"] = "[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]";
        sq["squadType"] = "REGULAR_SQUAD";
        sq["id"] = id;
        EnsureSquadManager(sq);
        SquadPut(sq);
        if (makeActive || id == ActiveSquadId() || SquadsArr().Count == 1) { Club["activeSquadId"] = id; Club["squadBody"] = sq.DeepClone(); }
        Save();
        return sq;
    }

    static JsonObject SquadListReply()
    {
        long act = ActiveSquadId();
        var list = new JsonArray();
        foreach (var n in SquadsArr())
        {
            if (n is not JsonObject s) continue;
            long id = L(s["id"]);
            list.Add(new JsonObject
            {
                ["id"] = id, ["personaId"] = 1000000002L, ["squadName"] = s["squadName"]?.ToString() ?? ("Squad " + id), ["formation"] = s["formation"]?.ToString() ?? "f442",
                ["active"] = id == act, ["changed"] = false, ["chemistry"] = SquadChemistry(s), ["starRating"] = L(s["starRating"]), ["rating"] = L(s["rating"]),
                ["valid"] = true, ["newsquad"] = 0L,
            });
        }
        return new JsonObject { ["activeSquadId"] = act, ["squad"] = list, ["count"] = list.Count };
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleSquads(string method, string path, string target, byte[] body)
    {
        if (!path.Contains("/fifa16/squad")) return null;
        if (File.Exists(FindFile("no_multisquad.txt"))) return null;
        bool isList = path.EndsWith("/squad/list");
        bool isActive = path.EndsWith("/squad/active");
        bool isBase = path.EndsWith("/fifa16/squad");
        var m = Regex.Match(path, @"/fifa16/squad/(\d+)$");
        if (!isList && !isActive && !isBase && !m.Success) return null;

        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);
        EnsureSquads();
        JsonObject req = null; try { req = JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject; } catch { }
        long reqId = m.Success ? long.Parse(m.Groups[1].Value) : -1;

        if (method == "GET")
        {
            if (isList) { var lr = SquadListReply(); Console.WriteLine($"[SQUADS] list: {SquadsArr().Count} squad(s), active {lr["activeSquadId"]}"); return J(lr); }
            if (File.Exists(FindFile("nosquad.txt"))) return J(new JsonObject());
            long id = reqId > 0 && SquadById(reqId) != null ? reqId : ActiveSquadId();
            var sq = (JsonObject)SquadById(id).DeepClone();
            SquadCleanSlots(sq);
            return J(NativeSquadReply(sq));
        }

        if (method == "DELETE" && m.Success)
        {
            var arr = SquadsArr();
            if (arr.Count <= 1 || SquadById(reqId) == null) { Console.WriteLine($"[SQUADS] squad {reqId} not deleted (last squad or unknown)"); return J(new JsonObject()); }
            arr.Remove(SquadById(reqId));
            Club["activeSquadId"] = ActiveSquadId();
            if (SquadById(ActiveSquadId()) is JsonObject ac) Club["squadBody"] = ac.DeepClone();
            Save(); Console.WriteLine($"[SQUADS] squad {reqId} deleted, active is now {Club["activeSquadId"]}");
            return J(new JsonObject());
        }

        if (method == "PUT" || method == "POST")
        {
            if (req == null) req = new JsonObject();
            long id;
            bool create = false;
            if (method == "POST" && isBase) { id = NextSquadId(); create = true; }
            else if (isActive || reqId <= 0) id = ActiveSquadId();
            else id = reqId;
            var saved = SquadStore(id, req);
            int filled = (saved["players"] as JsonArray)?.Count(p => L(p?["itemData"]?["id"]) > 0) ?? 0;
            var cnt = SquadCount(saved);
            Console.WriteLine($"[SQUADS] {(create ? "created" : "saved")} squad {id} \"{saved["squadName"]}\" {saved["formation"]}, XI {cnt.xi}/11 subs {cnt.subs}/7 ({filled} total), manager card {L(saved["manager"]?[0]?["id"])}" + (id == ActiveSquadId() ? " (active)" : ""));
            if (File.Exists(FindFile("emptyputreply.txt")) && !create) return J(new JsonObject());
            return J(NativeSquadReply((JsonObject)saved.DeepClone()));
        }
        return null;
    }
}
