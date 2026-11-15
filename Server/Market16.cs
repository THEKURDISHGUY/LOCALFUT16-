#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    const long MarketBase = 4000000000L;

    const int RareOnlyFrom = 80;
    static bool KeepCard(P p) => p.Special || p.Rare >= 1 || p.Rating < RareOnlyFrom;

    static int MarketLiveCount()
    {
        try { return Db.Count(p => p.AssetId > 100 && p.AssetId < 900000 && KeepCard(p)) + Managers.Count + ConList.Length + IdCat().Count; }
        catch { return 0; }
    }

    static Dictionary<string, string> QueryOf(string target)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int q = target.IndexOf('?'); if (q < 0) return d;
        foreach (var kv in target[(q + 1)..].Split('&'))
        {
            var p = kv.Split('=', 2);
            if (p.Length == 2) { try { d[p[0]] = Uri.UnescapeDataString(p[1].Replace('+', ' ')); } catch { d[p[0]] = p[1]; } }
        }
        return d;
    }
    static int PriceStep(long v)
    {
        if (v < 1000) return (int)(Math.Max(150, v) / 50 * 50);
        if (v < 10000) return (int)(v / 100 * 100);
        if (v < 50000) return (int)(v / 250 * 250);
        if (v < 100000) return (int)(v / 500 * 500);
        return (int)Math.Min(v / 1000 * 1000, 15000000);
    }
    static int MarketPrice(P p)
    {
        double b = 150 * Math.Pow(1.28, Math.Max(0, p.Rating - 55));
        if (p.Rare >= 1) b *= 1.5;
        if (p.Special) b *= 2.5;
        return PriceStep((long)b);
    }

    static JsonObject MarketItem(P p)
    {
        var before = Club["nextId"]!.DeepClone();
        var it = Item(p, Q(p.Rating));
        Club["nextId"] = before;
        it["id"] = MarketBase - 1000000000L + p.Id;
        return it;
    }
    static JsonObject MarketEntry(P p, bool closed = false, int paid = 0)
    {
        int buy = MarketPrice(p), start = PriceStep(buy * 6L / 10);
        return new JsonObject
        {
            ["tradeId"] = MarketBase + p.Id, ["itemData"] = MarketItem(p), ["tradeState"] = closed ? "closed" : "active",
            ["buyNowPrice"] = buy, ["currentBid"] = closed ? paid : 0, ["offers"] = 0, ["watched"] = false,
            ["bidState"] = closed ? "highest" : "none", ["startingBid"] = start, ["confirmedBidState"] = closed ? "highest" : "none", ["confidenceValue"] = 100,
            ["sellerEstablishedTime"] = 1400000000, ["sellerId"] = 0, ["sellerName"] = "FUT AI", ["expires"] = closed ? -1 : 3600,
            ["currencyType"] = "COINS", ["tradeOwner"] = false,
        };
    }
    static JsonObject MarketCredits(JsonArray list)
    {
        var o = CreditsObj(); o["auctionInfo"] = list; o["bidTokens"] = new JsonObject(); o["total"] = list.Count; return o;
    }

    static void StaffFill(JsonObject o)
    {
        void S(string k, JsonNode v) { if (o[k] == null) o[k] = v; }
        long aid = 0; try { aid = L(o["assetId"]); } catch { }
        S("itemId", L(o["id"])); S("definitionId", aid); S("resourceGameYear", 2016); S("contracts", 7);
        long rt = 0; try { rt = L(o["rating"]); } catch { }
        if (rt <= 0) o["rating"] = 80;
        o["rareflag"] = 1;
        long na = 0; try { na = L(o["nation"]); } catch { }
        if (na <= 0) o["nation"] = 14;
        long lg = 0; try { lg = L(o["leagueId"]); } catch { }
        if (lg <= 0) o["leagueId"] = 13;
        S("teamId", 0); S("untradeable", false); S("tradeable", true); S("loans", 0); S("training", 0);
        S("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds()); S("attributeList", new JsonArray()); S("statsList", new JsonArray());
        S("marketDataMinPrice", 150); S("marketDataMaxPrice", 15000000);
    }
    static void FixStaff()
    {
        try
        {
            if (Club["items"] is JsonArray a)
                foreach (var n in a)
                    if (n is JsonObject o && o["itemType"]?.ToString() == "staff" && o["contracts"] == null) StaffFill(o);
        }
        catch (Exception ex) { Console.WriteLine("[STAFF] fix failed: " + ex.Message); }
    }

    const long MgrOff = 200000000L, ConOff = 250000000L, IdOff = 300000000L;

    static List<JsonObject> _idCat;
    static List<JsonObject> IdCat()
    {
        if (_idCat != null) return _idCat;
        var l = new List<JsonObject>();
        try
        {
            var f = FindFile("club_identity_catalog.json");
            if (File.Exists(f))
                foreach (var n in JsonNode.Parse(File.ReadAllText(f))!["items"]!.AsArray()) if (n is JsonObject o) l.Add(o);
        }
        catch (Exception ex) { Console.WriteLine("[MARKET] identity catalog read failed: " + ex.Message); }
        _idCat = l.OrderBy(x => x["family"]?.ToString()).ThenBy(x => x["displayName"]?.ToString(), StringComparer.OrdinalIgnoreCase).ThenBy(x => L(x["category"])).ThenBy(x => L(x["resourceId"])).ToList();
        return _idCat;
    }
    static int IdPrice(JsonObject o)
    {
        string t = o["itemType"]?.ToString();
        long b = t == "stadium" ? 2500 : t == "ball" ? 1200 : t == "kit" ? 600 : 400;
        if (L(o["rareflag"]) > 0) b = b * 3 / 2;
        return PriceStep(b);
    }
    static JsonObject IdItemFor(JsonObject spec, long id)
    {
        var it = (JsonObject)spec.DeepClone(); long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        it["id"] = id; it["itemId"] = id; it["timestamp"] = now; it["pile"] = 0; it["itemState"] = "free"; it["owners"] = 0; it["untradeable"] = false;
        it["discardValue"] = 10; it["lastSalePrice"] = 0; it["weightrare"] = L(spec["rareflag"]) > 0 ? 100 : 0; it["resourceGameYear"] = 2016;
        it["marketDataMinPrice"] = 150; it["marketDataMaxPrice"] = 15000000; it["name"] = spec["displayName"]?.ToString() ?? "";
        return it;
    }
    static JsonObject MarketIdItem(int idx) => IdItemFor(IdCat()[idx], MarketBase - 1000000000L + IdOff + idx);
    static JsonObject MarketSearchIdentity(Dictionary<string, string> q, string ty = "")
    {
        int num = 21, start = 0;
        if (q.TryGetValue("num", out var sn)) int.TryParse(sn, out num);
        if (q.TryGetValue("start", out var ss)) int.TryParse(ss, out start);
        num = Math.Min(Math.Max(num, 1), 50); start = Math.Max(0, start);
        string cat = (q.TryGetValue("cat", out var cc) ? cc : "").ToLowerInvariant();
        string sub = (q.TryGetValue("subtype", out var st) ? st : q.TryGetValue("kit", out var kt) ? kt : "").ToLowerInvariant();
        string tyl = (ty ?? "").ToLowerInvariant();
        if (tyl.Contains("clubinfo") || tyl == "club") tyl = "";
        string csub = q.TryGetValue("cardsubtype", out var cst) ? cst : "";
        if (csub == "9") tyl += " kit"; else if (csub == "10") tyl += " stadium"; else if (csub == "11") tyl += " badge"; else if (csub == "30") tyl += " ball";
        else if (csub == "2") sub += " home"; else if (csub == "3") sub += " away"; else if (csub == "5") sub += " goalkeeper";
        cat = (cat + " " + tyl).Trim();
        var all = IdCat();
        IEnumerable<int> r = Enumerable.Range(0, all.Count);
        string fam = null;
        if (cat.Contains("badge") || cat.Contains("crest") || cat.Contains("club") || cat.Contains("custom")) fam = "badges";
        else if (cat.Contains("kit") || cat.Contains("shirt") || cat.Contains("jersey") || cat.Contains("home") || cat.Contains("away")) fam = "kits";
        else if (cat.Contains("stad")) fam = "stadiums";
        else if (cat.Contains("ball")) fam = "balls";
        if (fam != null) r = r.Where(i => all[i]["family"]?.ToString() == fam);
        if (fam == "kits" || cat.Contains("home") || cat.Contains("away") || sub.Length > 0)
        {
            string k = cat + " " + sub;
            if (k.Contains("home")) r = r.Where(i => L(all[i]["category"]) == 2);
            else if (k.Contains("away")) r = r.Where(i => L(all[i]["category"]) == 3);
            else if (k.Contains("third") || k.Contains("gk") || k.Contains("goal")) r = r.Where(i => L(all[i]["category"]) == 5);
        }
        r = IdentityFilter(r, all, q);
        if (q.TryGetValue("minb", out var mnb) && int.TryParse(mnb, out var minb)) r = r.Where(i => IdPrice(all[i]) >= minb);
        if (q.TryGetValue("maxb", out var mxb) && int.TryParse(mxb, out var maxb)) r = r.Where(i => IdPrice(all[i]) <= maxb);
        var list = new JsonArray();
        foreach (var i in r.Skip(start).Take(num)) list.Add(MarketEntryGen(MarketBase + IdOff + i, MarketIdItem(i), IdPrice(all[i])));
        Console.WriteLine($"[MARKET] clubInfo type={ty} cat={cat} sub={sub} cardsubtype={csub} family={fam ?? "all"} -> {list.Count} listings");
        return MarketCredits(list);
    }

    public record MCon(int Idx, long Rid, int CardAsset, int CardSub, string Tier, int Rating, int Rare, string Kind, int B, int S, int G, int Amount, string Name);
    static readonly MCon[] ConList =
    {
        new MCon(0, 5001001L, 7, 201, "bronze", 50, 0, "contract_player", 8, 2, 1, 0, "Bronze Player Contract"),
        new MCon(1, 5001004L, 7, 201, "bronze", 60, 1, "contract_player", 15, 6, 3, 0, "Rare Bronze Player Contract"),
        new MCon(2, 5001002L, 7, 201, "silver", 65, 0, "contract_player", 10, 10, 8, 0, "Silver Player Contract"),
        new MCon(3, 5001005L, 7, 201, "silver", 70, 1, "contract_player", 20, 24, 18, 0, "Rare Silver Player Contract"),
        new MCon(4, 5001003L, 7, 201, "gold", 80, 0, "contract_player", 15, 11, 13, 0, "Gold Player Contract"),
        new MCon(5, 5001006L, 7, 201, "gold", 90, 1, "contract_player", 28, 24, 28, 0, "Rare Gold Player Contract"),
        new MCon(6, 5001007L, 8, 202, "bronze", 50, 0, "contract_manager", 8, 2, 1, 0, "Bronze Manager Contract"),
        new MCon(7, 5001010L, 8, 202, "bronze", 60, 1, "contract_manager", 15, 6, 3, 0, "Rare Bronze Manager Contract"),
        new MCon(8, 5001008L, 8, 202, "silver", 65, 0, "contract_manager", 8, 10, 8, 0, "Silver Manager Contract"),
        new MCon(9, 5001011L, 8, 202, "silver", 70, 1, "contract_manager", 18, 24, 18, 0, "Rare Silver Manager Contract"),
        new MCon(10, 5001009L, 8, 202, "gold", 80, 0, "contract_manager", 11, 11, 13, 0, "Gold Manager Contract"),
        new MCon(11, 5001012L, 8, 202, "gold", 90, 1, "contract_manager", 24, 24, 28, 0, "Rare Gold Manager Contract"),
        new MCon(12, 5002001L, 10, 219, "bronze", 55, 0, "fitness_player", 0, 0, 0, 20, "Bronze Player Fitness"),
        new MCon(13, 5002004L, 10, 220, "bronze", 55, 1, "fitness_team", 0, 0, 0, 10, "Bronze Squad Fitness"),
        new MCon(14, 5002002L, 10, 219, "silver", 70, 0, "fitness_player", 0, 0, 0, 40, "Silver Player Fitness"),
        new MCon(15, 5002005L, 10, 220, "silver", 70, 1, "fitness_team", 0, 0, 0, 20, "Silver Squad Fitness"),
        new MCon(16, 5002003L, 10, 219, "gold", 80, 0, "fitness_player", 0, 0, 0, 60, "Gold Player Fitness"),
        new MCon(17, 5002006L, 10, 220, "gold", 80, 1, "fitness_team", 0, 0, 0, 30, "Gold Squad Fitness"),
        new MCon(18, 5002007L, 9, 211, "bronze", 55, 0, "healing", 0, 0, 0, 1, "Bronze Healing"),
        new MCon(19, 5002028L, 9, 218, "bronze", 60, 1, "healing", 0, 0, 0, 1, "Rare Bronze All Healing"),
        new MCon(20, 5002008L, 9, 211, "silver", 70, 0, "healing", 0, 0, 0, 2, "Silver Healing"),
        new MCon(21, 5002029L, 9, 218, "silver", 74, 1, "healing", 0, 0, 0, 2, "Rare Silver All Healing"),
        new MCon(22, 5002009L, 9, 211, "gold", 80, 0, "healing", 0, 0, 0, 5, "Gold Healing"),
        new MCon(23, 5002030L, 9, 218, "gold", 85, 1, "healing", 0, 0, 0, 4, "Rare Gold All Healing"),
        new MCon(24, 5003022L, 1, 61, "bronze", 55, 0, "training_player", 0, 0, 0, 5, "Bronze Player Training"),
        new MCon(25, 5003040L, 1, 67, "bronze", 64, 1, "training_player", 0, 0, 0, 3, "Rare Bronze All Training"),
        new MCon(26, 5003023L, 1, 61, "silver", 65, 0, "training_player", 0, 0, 0, 10, "Silver Player Training"),
        new MCon(27, 5003041L, 1, 67, "silver", 74, 1, "training_player", 0, 0, 0, 6, "Rare Silver All Training"),
        new MCon(28, 5003024L, 1, 61, "gold", 85, 0, "training_player", 0, 0, 0, 15, "Gold Player Training"),
        new MCon(29, 5003042L, 1, 67, "gold", 95, 1, "training_player", 0, 0, 0, 10, "Rare Gold All Training"),
    };

    static List<M> _mgrSorted;
    static List<M> MgrSorted() { if (_mgrSorted == null) _mgrSorted = Managers.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).ToList(); return _mgrSorted; }

    static int MgrPrice(int idx) => PriceStep(1000 + (idx % 9) * 500);
    static int ConPrice(MCon c)
    {
        double b = c.Tier == "gold" ? 700 : c.Tier == "silver" ? 300 : 150;
        if (c.Rare >= 1) b *= 1.5;
        if (c.Kind.StartsWith("training_")) b *= 2;
        return PriceStep((long)b);
    }

    static JsonObject MarketMgrItem(M m, int idx)
    {
        var before = Club["nextId"]!.DeepClone();
        var it = ManagerItem(m);
        Club["nextId"] = before;
        it["id"] = MarketBase - 1000000000L + MgrOff + idx;
        return it;
    }
    static JsonObject MarketConItem(MCon c)
    {
        var before = Club["nextId"]!.DeepClone();
        var it = ConsumableItem(c.Rid, c.CardAsset, c.CardSub, c.Tier, c.Rating, c.Rare, c.Kind, c.B, c.S, c.G, c.Amount);
        Club["nextId"] = before;
        long fid = MarketBase - 1000000000L + ConOff + c.Idx;
        it["id"] = fid; it["itemId"] = fid; it["name"] = c.Name;
        return it;
    }
    static JsonObject MarketEntryGen(long tradeId, JsonObject item, int buy, bool closed = false, int paid = 0)
    {
        int start = PriceStep(buy * 6L / 10);
        return new JsonObject
        {
            ["tradeId"] = tradeId, ["itemData"] = item, ["tradeState"] = closed ? "closed" : "active",
            ["buyNowPrice"] = buy, ["currentBid"] = closed ? paid : 0, ["offers"] = 0, ["watched"] = false,
            ["bidState"] = closed ? "highest" : "none", ["startingBid"] = start, ["confirmedBidState"] = closed ? "highest" : "none", ["confidenceValue"] = 100,
            ["sellerEstablishedTime"] = 1400000000, ["sellerId"] = 0, ["sellerName"] = "FUT AI", ["expires"] = closed ? -1 : 3600,
            ["currencyType"] = "COINS", ["tradeOwner"] = false,
        };
    }
    static JsonObject MarketEntryById(long tradeId)
    {
        long k = tradeId - MarketBase;
        if (k >= IdOff) { int ii = (int)(k - IdOff); var ic = IdCat(); return ii >= 0 && ii < ic.Count ? MarketEntryGen(tradeId, MarketIdItem(ii), IdPrice(ic[ii])) : null; }
        if (k >= ConOff) { var c = ConList.FirstOrDefault(x => x.Idx == k - ConOff); return c == null ? null : MarketEntryGen(tradeId, MarketConItem(c), ConPrice(c)); }
        if (k >= MgrOff) { int i = (int)(k - MgrOff); var ml = MgrSorted(); return i >= 0 && i < ml.Count ? MarketEntryGen(tradeId, MarketMgrItem(ml[i], i), MgrPrice(i)) : null; }
        var p = Db.FirstOrDefault(x => x.Id == k);
        return p == null ? null : MarketEntry(p);
    }

    static JsonObject MarketSearchExtra(string ty, Dictionary<string, string> q)
    {
        string t0 = ty.ToLowerInvariant();
        string catx = q.TryGetValue("cat", out var cx0) ? cx0.ToLowerInvariant() : "";
        string subx = q.TryGetValue("subtype", out var sx0) ? sx0.ToLowerInvariant() : "";
        string csx = q.TryGetValue("cardsubtype", out var csx0) ? csx0 : "";
        string all0 = t0 + " " + catx + " " + subx;
        bool idKw = false;
        foreach (var kw in new[] { "clubinfo", "badge", "crest", "emblem", "kit", "shirt", "jersey", "stadium", "ball", "custom", "club" }) if (all0.Contains(kw)) { idKw = true; break; }
        bool idSub = csx == "9" || csx == "10" || csx == "11" || csx == "30";
        bool conKw = false;
        foreach (var kw in new[] { "develop", "consum", "train", "contract", "fitness", "heal", "playstyle" }) if (all0.Contains(kw)) { conKw = true; break; }
        bool staffKw = t0 == "staff" || t0 == "manager" || t0 == "managers" || t0 == "coach" || t0 == "headcoach";
        if (idKw || idSub || (!conKw && !staffKw))
        {
            if (!idKw && !idSub) Console.WriteLine($"[MARKET] UNKNOWN search type='{ty}' cat='{catx}' cardsubtype='{csx}' -> club items (badges / kits / stadiums / balls); full query: " + string.Join("&", q.Select(kv => kv.Key + "=" + kv.Value)));
            return MarketSearchIdentity(q, ty);
        }
        int num = 21, start = 0;
        if (q.TryGetValue("num", out var sn)) int.TryParse(sn, out num);
        if (q.TryGetValue("start", out var ss)) int.TryParse(ss, out start);
        num = Math.Min(Math.Max(num, 1), 50); start = Math.Max(0, start);
        string t = ty.ToLowerInvariant();
        string cat = q.TryGetValue("cat", out var cc) ? cc.ToLowerInvariant() : "";
        var list = new JsonArray();
        if (t == "staff" || t == "manager" || t == "managers" || t == "coach" || t == "headcoach")
        {
            var ml = MgrSorted();
            for (int i = start; i < ml.Count && i < start + num; i++)
                list.Add(MarketEntryGen(MarketBase + MgrOff + i, MarketMgrItem(ml[i], i), MgrPrice(i)));
        }
        else
        {
            IEnumerable<MCon> r = ConList;
            if (t.Contains("train") || cat.Contains("playstyle") || cat.Contains("train")) r = r.Where(c => c.Kind.StartsWith("training_"));
            else if (t.Contains("contract") || cat.Contains("contract"))
            {
                r = r.Where(c => c.Kind.StartsWith("contract_"));
                if (cat.Contains("manager")) r = r.Where(c => c.Kind == "contract_manager");
                else if (cat.Contains("player")) r = r.Where(c => c.Kind == "contract_player");
            }
            else if (t.Contains("fitness") || cat.Contains("fitness")) r = r.Where(c => c.Kind.StartsWith("fitness_"));
            else if (t.Contains("heal") || cat.Contains("heal")) r = r.Where(c => c.Kind == "healing");
            if (q.TryGetValue("lev", out var lev) && lev.Length > 0)
            { var lv = lev.ToLowerInvariant(); var rr = r.Where(c => c.Tier == lv).ToList(); if (rr.Count > 0) r = rr; }
            foreach (var c in r.OrderBy(x => x.Idx).Skip(start).Take(num)) list.Add(MarketEntryGen(MarketBase + ConOff + c.Idx, MarketConItem(c), ConPrice(c)));
        }
        Console.WriteLine($"[MARKET] type={ty} cat={cat} -> {list.Count} listings");
        return MarketCredits(list);
    }

    static (int, JsonObject) MarketBuyExtra(long tradeId, long bid)
    {
        long k = tradeId - MarketBase;
        if (k >= IdOff)
        {
            int ii = (int)(k - IdOff); var ic = IdCat();
            if (ii < 0 || ii >= ic.Count) return (461, new JsonObject { ["reason"] = "auction expired or not found", ["message"] = "auction expired or not found", ["code"] = 461 });
            int buyI = IdPrice(ic[ii]), startI = PriceStep(buyI * 6L / 10);
            if (bid < startI) bid = startI;
            int payI = (int)Math.Min(bid, buyI);
            if (L(Club["coins"]) < payI) return (470, new JsonObject { ["reason"] = "not enough coins", ["message"] = "not enough coins", ["code"] = 470 });
            Club["coins"] = (int)L(Club["coins"]) - payI;
            long nid = Math.Max(1, L(Club["nextId"])); Club["nextId"] = nid + 1;
            var gotI = IdItemFor(ic[ii], nid); gotI["owners"] = 1;
            ((JsonArray)Club["items"]!).Add(gotI.DeepClone());
            var unI = new JsonArray(); if (Club["unassigned"] is JsonArray oU) foreach (var o1 in oU) unI.Add(L(o1)); unI.Add(nid); Club["unassigned"] = unI;
            Save();
            Console.WriteLine($"[MARKET] bought {gotI["name"]} ({gotI["itemType"]}) for {payI} coins -> balance {Club["coins"]}");
            return (200, MarketCredits(new JsonArray(MarketEntryGen(tradeId, gotI, buyI, true, payI))));
        }
        MCon cc = null; M mm = null; int mi = -1;
        if (k >= ConOff) cc = ConList.FirstOrDefault(x => x.Idx == k - ConOff);
        else { mi = (int)(k - MgrOff); var ml = MgrSorted(); if (mi >= 0 && mi < ml.Count) mm = ml[mi]; }
        if (cc == null && mm == null) return (461, new JsonObject { ["reason"] = "auction expired or not found", ["message"] = "auction expired or not found", ["code"] = 461 });
        int buy = cc != null ? ConPrice(cc) : MgrPrice(mi);
        int startBid = PriceStep(buy * 6L / 10);
        if (bid < startBid) bid = startBid;
        int pay = (int)Math.Min(bid, buy);
        if (L(Club["coins"]) < pay) return (470, new JsonObject { ["reason"] = "not enough coins", ["message"] = "not enough coins", ["code"] = 470 });
        Club["coins"] = (int)L(Club["coins"]) - pay;
        JsonObject got;
        if (cc != null) { got = ConsumableItem(cc.Rid, cc.CardAsset, cc.CardSub, cc.Tier, cc.Rating, cc.Rare, cc.Kind, cc.B, cc.S, cc.G, cc.Amount); got["name"] = cc.Name; }
        else got = ManagerItem(mm);
        ((JsonArray)Club["items"]!).Add(got.DeepClone());
        if (cc == null)
        {
        var un = new JsonArray(); if (Club["unassigned"] is JsonArray oldUn) foreach (var o0 in oldUn) un.Add(L(o0)); un.Add(L(got["id"])); Club["unassigned"] = un;
        }
        Save();
        Console.WriteLine($"[MARKET] bought {(cc != null ? cc.Name : mm.Name)} for {pay} coins -> balance {Club["coins"]}");
        return (200, MarketCredits(new JsonArray(MarketEntryGen(tradeId, got, buy, true, pay))));
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleMarket(string method, string path, string target, byte[] body)
    {
        { var ia = HandleIdentityActivate(method, path, body); if (ia != null) return ia.Value; }
        bool isSearch = method == "GET" && path.Contains("fifa16/transfermarket");
        bool isBid = (method == "PUT" || method == "POST") && path.Contains("fifa16/trade/") && (path.EndsWith("/bid") || path.EndsWith("/offer"));
        bool isStatus = method == "GET" && path.Contains("fifa16/trade/status");
        if (!isSearch && !isBid && !isStatus) return null;
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n, int code = 200) => (code, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);
        Console.WriteLine($"[MARKET-REQ] {method} {target}" + (body.Length > 0 ? "  body: " + Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 300)) : ""));
        var q = QueryOf(target);

        if (isSearch)
        {
            if (q.TryGetValue("type", out var ty) && ty.Length > 0 && !ty.Equals("player", StringComparison.OrdinalIgnoreCase))
            { return J(MarketSearchExtra(ty, q)); }
            int num = 21, start = 0;
            if (q.TryGetValue("num", out var sn)) int.TryParse(sn, out num);
            if (q.TryGetValue("start", out var ss)) int.TryParse(ss, out start);
            num = Math.Min(Math.Max(num, 1), 50);
            IEnumerable<P> r = Db.Where(p => p.AssetId > 100 && p.AssetId < 900000 && KeepCard(p));
            string levL = q.TryGetValue("lev", out var lev0) ? (lev0 ?? "").ToLowerInvariant() : "";
            string rareL = q.TryGetValue("rare", out var rare0) ? (rare0 ?? "").ToLowerInvariant() : "";
            bool wantSp = levL == "special" || levL == "sp" || rareL == "sp" || rareL == "special";
            bool tier = levL == "bronze" || levL == "silver" || levL == "gold";
            if (wantSp) r = r.Where(p => p.Special || p.Rare >= 2);
            if (tier)
            {
                r = r.Where(p => Q(p.Rating) == levL);
                if (!wantSp) r = r.Where(p => !(p.Special || p.Rare >= 2));
            }
            else if (rareL == "1" || rareL == "true" || rareL == "rare") r = r.Where(p => p.Rare >= 1);
            Console.WriteLine($"[MARKET] quality filter lev='{levL}' rare='{rareL}' -> {(wantSp ? "special" : tier ? levL : "all")}");
            if (q.TryGetValue("pos", out var pos) && pos.Length > 0)
            {
                string pu = pos.ToUpperInvariant();
                if (pu == "DEFENSE" || pu == "DEFENDER") r = r.Where(p => PosGroup(p.Pos) == 1);
                else if (pu == "MIDFIELD" || pu == "MIDFIELDER") r = r.Where(p => PosGroup(p.Pos) == 2);
                else if (pu == "ATTACK" || pu == "ATTACKER" || pu == "FORWARD") r = r.Where(p => PosGroup(p.Pos) == 3);
                else r = r.Where(p => p.Pos == pu);
            }
            if (q.TryGetValue("nat", out var nat) && int.TryParse(nat, out var nv)) r = r.Where(p => p.Nation == nv);
            if (q.TryGetValue("leag", out var lg) && int.TryParse(lg, out var lv)) r = r.Where(p => p.League == lv);
            if (q.TryGetValue("team", out var tm) && int.TryParse(tm, out var tv)) r = r.Where(p => p.Club == tv);
            if (q.TryGetValue("maskedDefId", out var md) && long.TryParse(md, out var mv)) r = r.Where(p => p.AssetId == mv);
            if (q.TryGetValue("minb", out var mnb) && int.TryParse(mnb, out var minb)) r = r.Where(p => MarketPrice(p) >= minb);
            if (q.TryGetValue("maxb", out var mxb) && int.TryParse(mxb, out var maxb)) r = r.Where(p => MarketPrice(p) <= maxb);
            if (q.TryGetValue("micr", out var mcr) && int.TryParse(mcr, out var micr)) r = r.Where(p => PriceStep(MarketPrice(p) * 6L / 10) >= micr);
            if (q.TryGetValue("macr", out var mxr) && int.TryParse(mxr, out var macr)) r = r.Where(p => PriceStep(MarketPrice(p) * 6L / 10) <= macr);
            var page = r.OrderByDescending(p => p.Rating).ThenBy(p => p.Id).Skip(start).Take(num).ToList();
            var list = new JsonArray(); foreach (var p in page) list.Add(MarketEntry(p));
            Console.WriteLine($"[MARKET] search start={start} num={num} -> {list.Count} listings");
            return J(MarketCredits(list));
        }

        if (isStatus)
        {
            var list = new JsonArray();
            if (q.TryGetValue("tradeIds", out var ids))
                foreach (var s in ids.Split(',')) if (long.TryParse(s, out var tid)) { var e0 = MarketEntryById(tid); if (e0 != null) list.Add(e0); }
            return J(MarketCredits(list));
        }

        long tradeId = 0;
        try { var seg = path.Split('/'); tradeId = long.Parse(seg[^2]); } catch { }
        if (tradeId - MarketBase >= MgrOff)
        {
            long bid0 = 0; try { var rq0 = JsonNode.Parse(Encoding.UTF8.GetString(body)); bid0 = L(rq0?["bid"]); } catch { }
            var ex = MarketBuyExtra(tradeId, bid0); return J(ex.Item2, ex.Item1);
        }
        var card = Db.FirstOrDefault(x => x.Id == tradeId - MarketBase);
        if (card == null) return J(new JsonObject { ["reason"] = "auction expired or not found", ["message"] = "auction expired or not found", ["code"] = 461 }, 461);
        long bid = 0; try { var rq = JsonNode.Parse(Encoding.UTF8.GetString(body)); bid = L(rq?["bid"]); } catch { }
        int buy = MarketPrice(card), startBid = PriceStep(buy * 6L / 10);
        if (bid < startBid) bid = startBid;
        int pay = (int)Math.Min(bid, buy);
        if (L(Club["coins"]) < pay) return J(new JsonObject { ["reason"] = "not enough coins", ["message"] = "not enough coins", ["code"] = 470 }, 470);

        Club["coins"] = (int)L(Club["coins"]) - pay;
        var got = Item(card, Q(card.Rating));
        var itemsArr = (JsonArray)Club["items"]!;
        var have = itemsArr.FirstOrDefault(x => x != null && x["itemType"]?.ToString() == "player" && L(x["assetId"]) == card.AssetId && !IsDup(L(x["id"])) && x["pile"]?.ToString() != "trade");
        if (have != null) DupMap()[L(got["id"]).ToString()] = L(have["id"]);
        itemsArr.Add(got.DeepClone());
        var un = new JsonArray(); if (Club["unassigned"] is JsonArray oldUn) foreach (var o0 in oldUn) un.Add(L(o0)); un.Add(L(got["id"])); Club["unassigned"] = un;
        Save();
        Console.WriteLine($"[MARKET] bought {card.Name} {card.Rating} for {pay} coins -> balance {Club["coins"]}");

        var entry = MarketEntry(card, true, pay); entry["itemData"] = got;
        var res = MarketCredits(new JsonArray(entry));
        return J(res);
    }
}
