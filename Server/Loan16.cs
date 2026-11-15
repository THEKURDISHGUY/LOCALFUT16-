#nullable disable
#pragma warning disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static partial class UtBackend
{
    static int _loanCycle = 0;
    static JsonArray LoanOffer()
    {
        if (Club["loanOffer"] is not JsonArray a || a.Count == 0)
        {
            a = new JsonArray();
            var rnd = new Random(16);
            var pool = Db.Where(p => p.Rating >= 62 && p.Rating <= 74 && !p.Special && p.Rare == 0 && p.AssetId > 100 && p.AssetId < 900000)
                         .OrderBy(_ => rnd.Next()).Take(6).ToList();
            foreach (var p in pool) { var it = Item(p, Q(p.Rating)); it["loans"] = 10; it["loanPlayer"] = true; a.Add(it); }
            Club["loanOffer"] = a; Save();
        }
        return a;
    }

    static (int, string, byte[], Dictionary<string, string>?)? HandleLoan(string method, string path, string target, byte[] body)
    {
        if (!path.Contains("fifa16/loan")) return null;
        (int, string, byte[], Dictionary<string, string>?) J(JsonNode n) => (200, "application/json", Encoding.UTF8.GetBytes(n.ToJsonString()), null);
        Console.WriteLine($"[LOAN-REQ] {method} {target}" + (body.Length > 0 ? "  body: " + Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 300)) : ""));
        bool signed = Club["loanSigned"] is JsonNode ls && ls.GetValue<bool>();

        if (method == "GET")
        {
            var offer = signed ? new JsonArray() : (JsonArray)LoanOffer().DeepClone();
            string tt = "1"; try { var f = FindFile("loan_try.txt"); if (File.Exists(f)) tt = File.ReadAllText(f).Trim(); } catch { }
            if (tt == "auto") tt = (((_loanCycle++) % 8) + 1).ToString();
            var wrapped = new JsonArray(); foreach (var c in offer) wrapped.Add(new JsonObject { ["itemData"] = c.DeepClone(), ["loans"] = 10 });
            Console.WriteLine($"[LOAN] offering {offer.Count} loan players, shape {tt}");
            switch (tt)
            {
                case "2": return J(offer);
                case "3": return J(new JsonObject { ["itemData"] = offer });
                case "4": return J(new JsonObject { ["players"] = wrapped });
                case "5": return J(new JsonObject { ["loanPlayers"] = wrapped, ["count"] = wrapped.Count });
                case "7":
                    {
                        var o7 = (JsonArray)offer.DeepClone(); foreach (var c7 in o7) { c7["contract"] = 10; c7["loans"] = 10; c7["itemState"] = "loan"; }
                        return J(new JsonObject { ["itemData"] = o7 });
                    }
                case "8":
                    {
                        var slots8 = new JsonArray(); int i8 = 0; foreach (var c8 in offer) slots8.Add(new JsonObject { ["index"] = i8++, ["itemData"] = c8.DeepClone(), ["kitNumber"] = i8 });
                        return J(new JsonObject { ["players"] = slots8, ["itemData"] = offer });
                    }
                case "6": return J(new JsonObject { ["loanPlayerList"] = wrapped, ["itemData"] = offer, ["totalResults"] = wrapped.Count });
                case "1": return J(new JsonObject { ["loans"] = offer });
                default:
                    return J(new JsonObject
                    {
                        ["itemData"] = offer, ["players"] = offer.DeepClone(), ["loanPlayers"] = offer.DeepClone(), ["items"] = offer.DeepClone(),
                        ["count"] = offer.Count, ["total"] = offer.Count, ["credits"] = Club["coins"]!.DeepClone(),
                    });
            }
        }

        if (signed) return J(new JsonObject { ["itemData"] = new JsonArray(), ["success"] = false, ["reason"] = "already signed" });
        long wanted = 0;
        try
        {
            var rq = JsonNode.Parse(Encoding.UTF8.GetString(body));
            wanted = L(rq?["id"] ?? rq?["itemId"] ?? rq?["itemData"]?[0]?["id"] ?? rq?["playerId"]);
        }
        catch { }
        var offerArr = LoanOffer();
        var pick = offerArr.FirstOrDefault(x => x != null && L(x["id"]) == wanted) ?? offerArr[0];
        var card = (JsonObject)pick.DeepClone();
        ((JsonArray)Club["items"]!).Add(card.DeepClone());
        var un = new JsonArray(); if (Club["unassigned"] is JsonArray oldUn) foreach (var o0 in oldUn) un.Add(L(o0)); un.Add(L(card["id"])); Club["unassigned"] = un;
        Club["loanSigned"] = true; Club["loanOffer"] = new JsonArray();
        Save();
        Console.WriteLine($"[LOAN] signed {card["name"]} {card["rating"]} (id {card["id"]})");
        return J(new JsonObject { ["itemData"] = new JsonArray(card), ["success"] = true, ["credits"] = Club["coins"]!.DeepClone() });
    }
}
