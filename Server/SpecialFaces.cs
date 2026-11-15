#nullable disable
#pragma warning disable
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
namespace LocalFut15;

static class SpecialFaces
{
    static readonly Dictionary<long, byte[]> Cache = new();

    public static byte[] Apply(string pth, byte[] normal)
    {
        try
        {
            if (normal == null) return normal;
            var m = Regex.Match(pth ?? "", @"/p(\d+)\.dds$");
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out var rid)) return normal;
            if ((rid >> 24) != 5) return normal;
            long plain = rid & 0xFFFFFF;
            lock (Cache) { if (Cache.TryGetValue(rid, out var hit)) return hit ?? normal; }
            string png = null;
            foreach (var root in new[] { Path.Combine(Directory.GetCurrentDirectory(), "fut_assets"), Path.Combine(AppContext.BaseDirectory, "fut_assets") })
            {
                var f = Path.Combine(root, "toty_faces", plain + ".png");
                if (File.Exists(f)) { png = f; break; }
            }
            if (png == null) { lock (Cache) Cache[rid] = null; return normal; }
            var made = Build(normal, File.ReadAllBytes(png), plain);
            lock (Cache) Cache[rid] = made;
            if (made != null) Console.WriteLine($"[SPECIAL FACE] p{rid} (player {plain}) built from {Path.GetFileName(png)}  {made.Length}B (normal was {normal.Length}B)");
            return made ?? normal;
        }
        catch (Exception ex) { Console.WriteLine("[SPECIAL FACE] error: " + ex.Message); return normal; }
    }

    static int LE(byte[] d, int o) => d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);
    static string Hex(byte[] d, int n) => BitConverter.ToString(d, 0, Math.Min(n, d.Length));

    static byte[] Build(byte[] orig, byte[] pngBytes, long plain)
    {
        if (orig.Length < 128 || orig[0] != 'D' || orig[1] != 'D' || orig[2] != 'S' || orig[3] != ' ')
        { Console.WriteLine("[SPECIAL FACE] normal head is not a plain DDS. first bytes: " + Hex(orig, 128)); return null; }
        int H = LE(orig, 12), W = LE(orig, 16), mips = Math.Max(1, LE(orig, 28));
        int pfFlags = LE(orig, 80); string cc = Encoding.ASCII.GetString(orig, 84, 4);
        int bits = LE(orig, 88); uint mR = (uint)LE(orig, 92), mG = (uint)LE(orig, 96), mB = (uint)LE(orig, 100), mA = (uint)LE(orig, 104);
        bool dxt1 = (pfFlags & 4) != 0 && cc == "DXT1", dxt5 = (pfFlags & 4) != 0 && cc == "DXT5", raw = (pfFlags & 0x40) != 0 && bits == 32;
        if (W < 1 || H < 1 || W > 1024 || H > 1024 || !(dxt1 || dxt5 || raw))
        { Console.WriteLine($"[SPECIAL FACE] unsupported format {W}x{H} mips={mips} fourCC='{cc}' bits={bits}. first bytes: " + Hex(orig, 128)); return null; }

        if (!DecodePng(pngBytes, out int sw, out int sh, out byte[] src)) { Console.WriteLine("[SPECIAL FACE] PNG for " + plain + " must be 8-bit RGBA/RGB, not interlaced"); return null; }

        var lv = new List<(int w, int h, byte[] px)>();
        lv.Add((W, H, Resize(src, sw, sh, W, H)));
        for (int i = 1; i < mips; i++)
        {
            var p = lv[i - 1]; int nw = Math.Max(1, p.w / 2), nh = Math.Max(1, p.h / 2);
            lv.Add((nw, nh, Box2(p.px, p.w, p.h, nw, nh)));
        }

        var body = new MemoryStream();
        foreach (var (w, h, px) in lv)
        {
            if (raw) WriteRaw(body, px, w, h, mR, mG, mB, mA);
            else WriteDxt(body, px, w, h, dxt5);
        }
        int total = 128 + (int)body.Length;
        int extra = orig.Length - total;
        if (extra < 0 || extra > 64)
        { Console.WriteLine($"[SPECIAL FACE] size mismatch: expected {total}B for {W}x{H} {cc} mips={mips}, normal head is {orig.Length}B -> using the normal face. first bytes: " + Hex(orig, 128)); return null; }

        var res = new byte[orig.Length];
        Array.Copy(orig, 0, res, 0, 128);
        body.Position = 0; body.Read(res, 128, (int)body.Length);
        if (extra > 0) Array.Copy(orig, total, res, total, extra);
        return res;
    }

    static int BE(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
    static bool DecodePng(byte[] d, out int w, out int h, out byte[] rgba)
    {
        w = h = 0; rgba = null;
        if (d.Length < 33 || d[0] != 0x89 || d[1] != 0x50 || d[2] != 0x4E || d[3] != 0x47) return false;
        int pos = 8, depth = 0, ct = 0, inter = 0; var idat = new MemoryStream();
        while (pos + 8 <= d.Length)
        {
            int len = BE(d, pos); string type = Encoding.ASCII.GetString(d, pos + 4, 4); int ds = pos + 8;
            if (len < 0 || ds + len > d.Length) break;
            if (type == "IHDR") { w = BE(d, ds); h = BE(d, ds + 4); depth = d[ds + 8]; ct = d[ds + 9]; inter = d[ds + 12]; }
            else if (type == "IDAT") idat.Write(d, ds, len);
            else if (type == "IEND") break;
            pos = ds + len + 4;
        }
        if (depth != 8 || inter != 0 || (ct != 6 && ct != 2) || w < 1 || h < 1) return false;
        int bpp = ct == 6 ? 4 : 3, stride = w * bpp;
        var rawData = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int got = 0; while (got < rawData.Length) { int n = z.Read(rawData, got, rawData.Length - got); if (n <= 0) break; got += n; }
            if (got < rawData.Length) return false;
        }
        var cur = new byte[stride]; var prev = new byte[stride]; rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int f = rawData[y * (stride + 1)], o = y * (stride + 1) + 1;
            for (int i = 0; i < stride; i++)
            {
                int x = rawData[o + i], a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0, v;
                switch (f)
                {
                    case 1: v = x + a; break;
                    case 2: v = x + b; break;
                    case 3: v = x + ((a + b) >> 1); break;
                    case 4:
                        { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); v = x + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c); break; }
                    default: v = x; break;
                }
                cur[i] = (byte)v;
            }
            for (int xx = 0; xx < w; xx++)
            {
                int q = (y * w + xx) * 4;
                rgba[q] = cur[xx * bpp]; rgba[q + 1] = cur[xx * bpp + 1]; rgba[q + 2] = cur[xx * bpp + 2]; rgba[q + 3] = bpp == 4 ? cur[xx * bpp + 3] : (byte)255;
            }
            var t = prev; prev = cur; cur = t;
        }
        return true;
    }

    static byte[] Resize(byte[] s, int sw, int sh, int tw, int th)
    {
        var o = new byte[tw * th * 4];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                float fx = (x + 0.5f) * sw / tw - 0.5f, fy = (y + 0.5f) * sh / th - 0.5f;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy); float tx = fx - x0, ty = fy - y0;
                float r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int px = Math.Min(sw - 1, Math.Max(0, x0 + i)), py = Math.Min(sh - 1, Math.Max(0, y0 + j));
                        float wgt = (i == 0 ? 1 - tx : tx) * (j == 0 ? 1 - ty : ty); int q = (py * sw + px) * 4; float al = s[q + 3] / 255f;
                        r += s[q] * al * wgt; g += s[q + 1] * al * wgt; b += s[q + 2] * al * wgt; a += al * wgt;
                    }
                int d = (y * tw + x) * 4;
                if (a > 0.0001f) { o[d] = Clamp(r / a); o[d + 1] = Clamp(g / a); o[d + 2] = Clamp(b / a); }
                o[d + 3] = Clamp(a * 255f);
            }
        return o;
    }
    static byte Clamp(float v) => (byte)Math.Min(255, Math.Max(0, (int)(v + 0.5f)));
    static byte[] Box2(byte[] s, int sw, int sh, int tw, int th)
    {
        var o = new byte[tw * th * 4];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int px = Math.Min(sw - 1, x * 2 + i), py = Math.Min(sh - 1, y * 2 + j), q = (py * sw + px) * 4; float al = s[q + 3] / 255f;
                        r += s[q] * al; g += s[q + 1] * al; b += s[q + 2] * al; a += al;
                    }
                int d = (y * tw + x) * 4;
                if (a > 0.0001f) { o[d] = Clamp(r / a); o[d + 1] = Clamp(g / a); o[d + 2] = Clamp(b / a); }
                o[d + 3] = Clamp(a / 4f * 255f);
            }
        return o;
    }

    static uint PackMask(int v, uint mask)
    {
        if (mask == 0) return 0;
        int sh = 0; while (((mask >> sh) & 1) == 0) sh++;
        uint max = mask >> sh; return (uint)((v * (long)max + 127) / 255) << sh;
    }
    static void WriteRaw(MemoryStream ms, byte[] px, int w, int h, uint mR, uint mG, uint mB, uint mA)
    {
        for (int i = 0; i < w * h; i++)
        {
            uint v = PackMask(px[i * 4], mR) | PackMask(px[i * 4 + 1], mG) | PackMask(px[i * 4 + 2], mB) | PackMask(px[i * 4 + 3], mA);
            ms.WriteByte((byte)v); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 24));
        }
    }

    static void WriteDxt(MemoryStream ms, byte[] px, int w, int h, bool dxt5)
    {
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        var blk = new byte[64];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                for (int j = 0; j < 4; j++)
                    for (int i = 0; i < 4; i++)
                    {
                        int x = Math.Min(w - 1, bx * 4 + i), y = Math.Min(h - 1, by * 4 + j), q = (y * w + x) * 4, d = (j * 4 + i) * 4;
                        blk[d] = px[q]; blk[d + 1] = px[q + 1]; blk[d + 2] = px[q + 2]; blk[d + 3] = px[q + 3];
                    }
                if (dxt5) AlphaBlock(ms, blk);
                ColorBlock(ms, blk, !dxt5);
            }
    }
    static int To565(int r, int g, int b) => ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255);
    static void From565(int c, out int r, out int g, out int b)
    {
        r = (c >> 11) & 31; g = (c >> 5) & 63; b = c & 31;
        r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2);
    }
    static void ColorBlock(MemoryStream ms, byte[] blk, bool dxt1)
    {
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0, opaque = 0; bool anyT = false;
        for (int i = 0; i < 16; i++)
        {
            if (dxt1 && blk[i * 4 + 3] < 128) { anyT = true; continue; }
            int r = blk[i * 4], g = blk[i * 4 + 1], b = blk[i * 4 + 2]; opaque++;
            if (r < minR) minR = r; if (g < minG) minG = g; if (b < minB) minB = b;
            if (r > maxR) maxR = r; if (g > maxG) maxG = g; if (b > maxB) maxB = b;
        }
        if (opaque == 0) { minR = minG = minB = maxR = maxG = maxB = 0; }
        int c0 = To565(maxR, maxG, maxB), c1 = To565(minR, minG, minB);
        bool three = dxt1 && anyT;
        if (three) { if (c0 > c1) { int t = c0; c0 = c1; c1 = t; } }
        else if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
        From565(c0, out int r0, out int g0, out int b0); From565(c1, out int r1, out int g1, out int b1);
        var pr = new int[4]; var pg = new int[4]; var pb = new int[4];
        pr[0] = r0; pg[0] = g0; pb[0] = b0; pr[1] = r1; pg[1] = g1; pb[1] = b1;
        if (three) { pr[2] = (r0 + r1) / 2; pg[2] = (g0 + g1) / 2; pb[2] = (b0 + b1) / 2; }
        else { pr[2] = (2 * r0 + r1) / 3; pg[2] = (2 * g0 + g1) / 3; pb[2] = (2 * b0 + b1) / 3; pr[3] = (r0 + 2 * r1) / 3; pg[3] = (g0 + 2 * g1) / 3; pb[3] = (b0 + 2 * b1) / 3; }
        uint idx = 0; int nCol = three ? 3 : 4;
        if (c0 != c1 || three)
            for (int i = 0; i < 16; i++)
            {
                int best = 0;
                if (three && blk[i * 4 + 3] < 128) best = 3;
                else
                {
                    int bd = int.MaxValue;
                    for (int k = 0; k < nCol; k++)
                    {
                        int dr = blk[i * 4] - pr[k], dg = blk[i * 4 + 1] - pg[k], db = blk[i * 4 + 2] - pb[k], dd = dr * dr + dg * dg + db * db;
                        if (dd < bd) { bd = dd; best = k; }
                    }
                }
                idx |= (uint)best << (i * 2);
            }
        ms.WriteByte((byte)c0); ms.WriteByte((byte)(c0 >> 8)); ms.WriteByte((byte)c1); ms.WriteByte((byte)(c1 >> 8));
        ms.WriteByte((byte)idx); ms.WriteByte((byte)(idx >> 8)); ms.WriteByte((byte)(idx >> 16)); ms.WriteByte((byte)(idx >> 24));
    }
    static void AlphaBlock(MemoryStream ms, byte[] blk)
    {
        int a0 = 0, a1 = 255;
        for (int i = 0; i < 16; i++) { int a = blk[i * 4 + 3]; if (a > a0) a0 = a; if (a < a1) a1 = a; }
        var pal = new int[8]; pal[0] = a0; pal[1] = a1;
        for (int k = 1; k <= 6; k++) pal[k + 1] = ((7 - k) * a0 + k * a1) / 7;
        ulong idx = 0;
        if (a0 != a1)
            for (int i = 0; i < 16; i++)
            {
                int best = 0, bd = int.MaxValue;
                for (int k = 0; k < 8; k++) { int dd = Math.Abs(blk[i * 4 + 3] - pal[k]); if (dd < bd) { bd = dd; best = k; } }
                idx |= (ulong)best << (i * 3);
            }
        ms.WriteByte((byte)a0); ms.WriteByte((byte)a1);
        for (int i = 0; i < 6; i++) ms.WriteByte((byte)(idx >> (8 * i)));
    }
}
