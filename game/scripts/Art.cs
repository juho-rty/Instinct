using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>
/// 그림. 먹선과 옅은 물감(담채)으로 그린 지도책 같은 결을 낸다.
/// 모든 그림은 여기서 코드로 그리는 임시 그림이고, game/assets 아래에 같은 이름의 PNG가 있으면 그것을 대신 쓴다(ASSETS.md).
/// </summary>
public static class Art
{
    public const float Cell = 64f;

    public static readonly Color Ink = new("2b2420");
    public static readonly Color Paper = new("efe6d0");
    public static readonly Color PaperDark = new("d9cba8");
    public static readonly Color Ours = new("34507a");
    public static readonly Color Good = new("91bd77");
    public static readonly Color Bad = new("e79479");
    public static readonly Color Gold = new("e5ba68");

    static readonly Color[] RivalColors = { new("a8452f"), new("8a6a1f"), new("5e7a3a"), new("7b4a7a"), new("3f7f7a"), new("9a5a2a"), new("5a5a8a") };

    public static Color OwnerColor(int owner) => owner == 0 ? Ours : owner < 0 ? new Color(0, 0, 0, 0) : RivalColors[owner % RivalColors.Length];

    public static readonly Color[] GroundColors =
    {
        new("315765"), // Deep
        new("589c9a"), // Shallows
        new("dccf9f"), // Sand
        new("8eaa72"), // Grass
        new("60886b"), // Woods
        new("a3a079"), // Hill
        new("83918d"), // Peak
        new("cbbd88"), // Dry
        new("8fa587"), // Marsh
    };

    public static Font Font = ThemeDB.FallbackFont;

    // ───────── 그림 자산 ─────────

    static readonly Dictionary<string, Texture2D?> Cache = new();

    /// <summary>game/assets/{path} 가 있으면 그 그림, 없으면 null(그러면 코드로 그린다).</summary>
    public static Texture2D? Tex(string path)
    {
        if (Cache.TryGetValue(path, out var t)) return t;
        // 블렌더로 찍은 PNG가 있으면 SVG를 청해도 PNG가 먼저다
        string full = System.IO.Path.ChangeExtension("res://assets/" + path, ".png");
        if (!Godot.FileAccess.FileExists(full)) full = System.IO.Path.ChangeExtension(full, ".svg");
        Texture2D? tex = null;
        if (Godot.FileAccess.FileExists(full))
        {
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(full));
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        Cache[path] = tex;
        return tex;
    }

    static readonly Dictionary<string, float> TopFrac = new();

    /// <summary>game/assets/{path}에 PNG(블렌더 그림)가 실제로 있는가.</summary>
    public static bool IsPng(string path) => Godot.FileAccess.FileExists("res://assets/" + path);

    /// <summary>자산이 있으면 발밑(아래 가운데)을 pos에 맞춰 그리고 true.</summary>
    public static bool Sprite(CanvasItem c, string path, Vector2 pos, float height)
    {
        var t = Tex(path);
        if (t == null) return false;
        if (IsPng(path))
        {
            // 블렌더 정사각 그림: 사물 꼭대기에서 땅의 원점까지가 height가 되게 키운다
            if (!TopFrac.TryGetValue(path, out float top))
                TopFrac[path] = top = t.GetImage() is { } im ? im.GetUsedRect().Position.Y / (float)im.GetHeight() : 0.3f;
            c.DrawTextureRect(t, EnvRect(pos, height / Math.Max(0.15f, EnvGround - top)), false);
            return true;
        }
        float w = height * t.GetWidth() / t.GetHeight();
        c.DrawTextureRect(t, new Rect2(pos.X - w / 2, pos.Y - height, w, height), false);
        return true;
    }

    /// <summary>가로로 프레임이 늘어선 그림에서 한 프레임을 그린다.</summary>
    public static bool Strip(CanvasItem c, string path, int frames, int frame, Vector2 pos, float height, bool flip)
    {
        var t = Tex(path);
        if (t == null) return false;
        float fw = t.GetWidth() / (float)frames, fh = t.GetHeight();
        float w = height * fw / fh;
        var dst = new Rect2(pos.X - w / 2, pos.Y - height, w, height);
        if (flip) dst = new Rect2(pos.X + w / 2, pos.Y - height, -w, height);
        c.DrawTextureRectRegion(t, dst, new Rect2(fw * (frame % frames), 0, fw, fh));
        return true;
    }

    // ───────── 잡음 ─────────

    public static float Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)s * 374761393u + (uint)x * 668265263u + (uint)y * 2246822519u + 0x9E3779B9u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    public static float Noise(float x, float y, int s)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Hash(x0, y0, s), b = Hash(x0 + 1, y0, s), c = Hash(x0, y0 + 1, s), d = Hash(x0 + 1, y0 + 1, s);
        return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
    }

    public static float Fbm(float x, float y, int s) => Noise(x, y, s) * 0.6f + Noise(x * 2.1f, y * 2.1f, s + 7) * 0.28f + Noise(x * 4.3f, y * 4.3f, s + 13) * 0.12f;

    /// <summary>상자 흐림(가로·세로를 따로). 두 번 하면 가우스에 가깝다.</summary>
    public static void Blur(float[] a, int w, int h, int r)
    {
        if (r <= 0) return;
        var tmp = new float[a.Length];
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += a[row + Math.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[row + x] = sum * inv;
                sum += a[row + Math.Min(w - 1, x + r + 1)] - a[row + Math.Max(0, x - r)];
            }
        }
        for (int x = 0; x < w; x++)
        {
            float sum = 0;
            for (int k = -r; k <= r; k++) sum += tmp[Math.Clamp(k, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                a[y * w + x] = sum * inv;
                sum += tmp[Math.Min(h - 1, y + r + 1) * w + x] - tmp[Math.Max(0, y - r) * w + x];
            }
        }
    }

    // ───────── 붓질 ─────────

    public static void Shape(CanvasItem c, Vector2[] pts, Color fill, float ink = 1.6f)
    {
        c.DrawColoredPolygon(pts, fill);
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0);
        loop[^1] = pts[0];
        c.DrawPolyline(loop, Ink, ink, true);
    }

    public static void Box(CanvasItem c, float x, float y, float w, float h, Color fill, float ink = 1.6f) =>
        Shape(c, new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) }, fill, ink);

    public static void Dot(CanvasItem c, Vector2 p, float r, Color fill, float ink = 1.4f)
    {
        c.DrawCircle(p, r, fill);
        c.DrawArc(p, r, 0, Mathf.Tau, 20, Ink, ink, true);
    }

    public static void Shadow(CanvasItem c, Vector2 p, float w)
    {
        c.DrawSetTransform(p, 0, new Vector2(1, 0.4f));
        c.DrawCircle(Vector2.Zero, w, new Color(0.1f, 0.08f, 0.05f, 0.18f));
        c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    public static void Text(CanvasItem c, Vector2 pos, string s, int size, Color col, bool center = true, int outline = 4)
    {
        float w = Font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
        var at = center ? pos - new Vector2(w / 2, 0) : pos;
        if (outline > 0) c.DrawStringOutline(Font, at, s, HorizontalAlignment.Left, -1, size, outline, new Color(Paper, 0.96f));
        c.DrawString(Font, at, s, HorizontalAlignment.Left, -1, size, col);
    }

    // ───────── 땅 위의 것들 ─────────
    // 지형 장식은 붓 묶음(Pen)에 모아 메시 하나로 그린다. 그림 파일(decor/*.png)이 있으면 그것이 먼저다(DecorSprite).

    /// <summary>블렌더 정사각 그림(256칸)에서 땅의 원점이 위에서 몇 번째 줄인가의 비율(env.py의 GROUND).</summary>
    public const float EnvGround = 208f / 256f;

    /// <summary>정사각 블렌더 그림을 땅의 원점(foot)에 맞춰 size 크기로 놓을 자리.</summary>
    public static Rect2 EnvRect(Vector2 foot, float size) => new(foot.X - size / 2, foot.Y - size * EnvGround, size, size);

    static Texture2D? _decorAtlas;
    static bool _decorAtlasTried;
    static readonly string[] DecorPaths = { "decor/tree.png", "decor/tree2.png", "decor/pine.png", "decor/pine2.png", "decor/peak.png", "decor/hill.png" };

    /// <summary>지형 장식 6종을 3×2 한 장(768×512)에 모은 아틀라스. 수천 그루의 나무·산을 그리기 호출 한 번(DrawMesh)으로 그린다.</summary>
    public static Texture2D? DecorAtlas()
    {
        if (_decorAtlasTried) return _decorAtlas;
        _decorAtlasTried = true;
        const int S = 256;
        var atlas = Image.CreateEmpty(S * 3, S * 2, false, Image.Format.Rgba8);
        bool any = false;
        for (int i = 0; i < DecorPaths.Length; i++)
        {
            string res = "res://assets/" + DecorPaths[i];
            if (!Godot.FileAccess.FileExists(res)) res = "res://assets/" + DecorPaths[i].Replace("2.png", ".png");
            if (!Godot.FileAccess.FileExists(res)) continue;
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(res));
            if (img == null) continue;
            if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
            if (img.GetWidth() != S || img.GetHeight() != S) img.Resize(S, S, Image.Interpolation.Bilinear);
            atlas.BlitRect(img, new Rect2I(0, 0, S, S), new Vector2I((i % 3) * S, (i / 3) * S));
            any = true;
        }
        if (!any) return null;
        atlas.GenerateMipmaps();
        _decorAtlas = ImageTexture.CreateFromImage(atlas);
        return _decorAtlas;
    }

    /// <summary>장식 갈래(0 나무·1 소나무·2 봉우리·3 언덕)의 아틀라스 UV와 놓을 자리. 없으면 null.</summary>
    public static (Rect2 uv, Rect2 rect)? DecorQuad(int kind, float s, float tone, Vector2 pos)
    {
        if (DecorAtlas() == null) return null;
        bool alt = tone > 0.5f;
        var (slot, size) = kind switch
        {
            0 => (alt ? 1 : 0, 66f),
            1 => (alt ? 3 : 2, 68f),
            2 => (4, 104f),
            3 => (5, 58f),
            _ => (-1, 0f)
        };
        if (slot < 0) return null;
        const float pad = 0.5f / 256f;
        float u0 = ((slot % 3) + pad) / 3f, u1 = ((slot % 3) + 1 - pad) / 3f;
        float v0 = ((slot / 3) + pad) / 2f, v1 = ((slot / 3) + 1 - pad) / 2f;
        return (new Rect2(u0, v0, u1 - u0, v1 - v0), EnvRect(pos, size * s));
    }

    /// <summary>장식 갈래(0 나무·1 소나무·2 봉우리·3 언덕)의 그림과 놓을 자리. tone으로 두 가지 나무 중 하나를 고른다. 없으면 null.</summary>
    public static (Texture2D tex, Rect2 rect)? DecorSprite(int kind, float s, float tone, Vector2 pos)
    {
        bool alt = tone > 0.5f;
        var (path, size) = kind switch
        {
            0 => (alt ? "decor/tree2.png" : "decor/tree.png", 66f),
            1 => (alt ? "decor/pine2.png" : "decor/pine.png", 68f),
            2 => ("decor/peak.png", 104f),
            3 => ("decor/hill.png", 58f),
            _ => ("", 0f)
        };
        if (path.Length == 0) return null;
        var t = Tex(path) ?? (alt ? Tex(path.Replace("2.png", ".png")) : null);
        return t == null ? null : (t, EnvRect(pos, size * s));
    }

    static void Shape(Pen pen, Vector2[] pts, Color fill, float ink)
    {
        pen.Fill(pts, fill);
        pen.Stroke(pts, Ink, ink, true);
    }

    public static void Tree(Pen pen, Vector2 p, float s, float tone)
    {
        var leaf = new Color("6f8f58").Lerp(new Color("8aa568"), tone);
        pen.Line(p, p + new Vector2(0, -8 * s), Ink, 2.2f * s);
        var c = p + new Vector2(0, -15 * s);
        pen.Circle(c, 8.5f * s, leaf);
        pen.Arc(c, 8.5f * s, 0, Mathf.Tau, 20, Ink, 1.3f);
        pen.Arc(p + new Vector2(-1.5f * s, -16 * s), 4.5f * s, 3.4f, 5.2f, 6, new Color(Paper, 0.35f), 1.2f);
    }

    public static void Pine(Pen pen, Vector2 p, float s, float tone)
    {
        var leaf = new Color("5f7f5c").Lerp(new Color("77936a"), tone);
        pen.Line(p, p + new Vector2(0, -6 * s), Ink, 2f * s);
        Shape(pen, new[] { p + new Vector2(-8 * s, -5 * s), p + new Vector2(0, -28 * s), p + new Vector2(8 * s, -5 * s) }, leaf, 1.3f);
    }

    public static void Peak(Pen pen, Vector2 p, float s, float tone)
    {
        var rock = new Color("9d968a").Lerp(new Color("b3ac9d"), tone);
        var top = p + new Vector2(2 * s, -40 * s);
        Shape(pen, new[] { p + new Vector2(-26 * s, 0), top, p + new Vector2(28 * s, 0) }, rock, 1.7f);
        // 그늘진 비탈과 눈
        pen.Fill(new[] { top, p + new Vector2(28 * s, 0), p + new Vector2(8 * s, 0), p + new Vector2(4 * s, -18 * s) }, new Color(Ink, 0.22f));
        pen.Fill(new[] { top, top + new Vector2(-8 * s, 12 * s), top + new Vector2(-2 * s, 9 * s), top + new Vector2(3 * s, 13 * s), top + new Vector2(8 * s, 11 * s) }, new Color("f4f0e4"));
    }

    public static void Hill(Pen pen, Vector2 p, float s, float tone)
    {
        var col = new Color("a9a274").Lerp(new Color("bdb388"), tone);
        var pts = new Vector2[9];
        for (int k = 0; k < 9; k++)
        {
            float a = Mathf.Pi + Mathf.Pi * k / 8f;
            pts[k] = p + new Vector2(Mathf.Cos(a) * 20 * s, Mathf.Sin(a) * 13 * s);
        }
        pen.Fill(pts, col);
        pen.Stroke(pts, Ink, 1.4f);
    }

    public static void Tuft(Pen pen, Vector2 p, float s, Color col)
    {
        pen.Line(p, p + new Vector2(-3 * s, -7 * s), col, 1.3f);
        pen.Line(p, p + new Vector2(0, -9 * s), col, 1.3f);
        pen.Line(p, p + new Vector2(3 * s, -7 * s), col, 1.3f);
    }

    // ───────── 집과 마을 ─────────

    static readonly Color[] Roofs = { new("b89a62"), new("b89a62"), new("9a6a4a"), new("8a5a48"), new("6f6a70"), new("7d8790") };
    static readonly Color[] Walls = { new("d9c9a0"), new("e2d5b4"), new("e6dcc4"), new("eae2cf"), new("d8cfc2"), new("dcdcd6") };

    public static void House(CanvasItem c, Vector2 p, float s, int era, float tone)
    {
        if (Sprite(c, $"town/house_{era}.png", p, (IsPng($"town/house_{era}.png") ? 26 : 22) * s)) return;
        var wall = Walls[era].Darkened(tone * 0.08f);
        var roof = Roofs[era].Darkened(tone * 0.12f);
        if (era == 0)
        {
            // 움막
            Shape(c, new[] { p + new Vector2(-9 * s, 0), p + new Vector2(0, -15 * s), p + new Vector2(9 * s, 0) }, roof, 1.4f);
            c.DrawLine(p + new Vector2(0, 0), p + new Vector2(0, -7 * s), Ink, 2f * s);
            return;
        }
        float h = era >= 5 ? 16 : 9;
        Box(c, p.X - 8 * s, p.Y - h * s, 16 * s, h * s, wall, 1.4f);
        if (era >= 5) for (int k = 0; k < 2; k++) c.DrawRect(new Rect2(p.X - 5 * s + k * 6 * s, p.Y - 12 * s, 3 * s, 4 * s), new Color(Ink, 0.5f));
        else Shape(c, new[] { p + new Vector2(-10 * s, -h * s), p + new Vector2(0, -(h + 9) * s), p + new Vector2(10 * s, -h * s) }, roof, 1.4f);
        c.DrawRect(new Rect2(p.X - 1.5f * s, p.Y - 5 * s, 3 * s, 5 * s), new Color(Ink, 0.7f));
    }

    /// <summary>도성. 단계가 오를수록 울타리가 성벽이 되고 집이 는다.</summary>
    public static void Town(CanvasItem c, Vector2 p, int stage, int era, Color flag)
    {
        if (Sprite(c, $"town/capital_{stage}.png", p + new Vector2(0, 30), IsPng($"town/capital_{stage}.png") ? 112 : 96)) return;
        Shadow(c, p + new Vector2(0, 6), 44);
        if (stage == 0)
        {
            House(c, p + new Vector2(-18, -2), 1.2f, 0, 0.2f);
            House(c, p + new Vector2(16, -6), 1.1f, 0, 0.6f);
            House(c, p + new Vector2(0, 12), 1.3f, 0, 0.4f);
            return;
        }
        if (stage == 1)
        {
            // 나무 울타리
            for (int k = 0; k < 14; k++)
            {
                float a = Mathf.Tau * k / 14f;
                var q = p + new Vector2(Mathf.Cos(a) * 40, Mathf.Sin(a) * 22);
                c.DrawLine(q, q + new Vector2(0, -8), Ink, 2.5f);
            }
        }
        else
        {
            var stone = era >= 4 ? new Color("b5b0a8") : new Color("cfc6ae");
            Box(c, p.X - 44, p.Y - 16, 88, 30, new Color(stone, 0.55f), 2f);
            foreach (float dx in new[] { -44f, 44f })
            {
                Box(c, p.X + dx - 7, p.Y - 28, 14, 44, stone, 1.8f);
                Box(c, p.X + dx - 9, p.Y - 34, 18, 7, stone.Darkened(0.1f), 1.6f);
            }
        }
        int n = 3 + stage;
        for (int k = 0; k < n; k++)
        {
            float hx = Hash(k, stage, 91) - 0.5f, hy = Hash(k, stage, 92) - 0.5f;
            House(c, p + new Vector2(hx * 60, hy * 22 + 6), 1.0f + Hash(k, 3, 5) * 0.2f, era, Hash(k, 1, 7));
        }
        if (stage >= 3)
        {
            // 궁
            var stone = new Color("d8ccb0");
            Box(c, p.X - 11, p.Y - 34, 22, 26, stone, 1.8f);
            Shape(c, new[] { p + new Vector2(-15, -34), p + new Vector2(0, -50), p + new Vector2(15, -34) }, Roofs[Math.Min(era, 4)].Darkened(0.1f), 1.8f);
        }
        Flag(c, p + new Vector2(0, stage >= 3 ? -50 : -22), flag, 0, 1f);
    }

    public static void Flag(CanvasItem c, Vector2 top, Color col, float wave, float s)
    {
        c.DrawLine(top + new Vector2(0, 14 * s), top + new Vector2(0, -16 * s), Ink, 1.8f);
        float w = Mathf.Sin(wave) * 2.5f * s;
        Shape(c, new[]
        {
            top + new Vector2(0, -16 * s), top + new Vector2(8 * s, -14 * s + w), top + new Vector2(16 * s, -15 * s - w),
            top + new Vector2(16 * s, -6 * s - w), top + new Vector2(8 * s, -5 * s + w), top + new Vector2(0, -7 * s),
        }, col, 1.3f);
    }

    // ───────── 건물 ─────────

    public static void BuildingIcon(CanvasItem c, Building b, Vector2 p, float s)
    {
        string key = BuildingCatalog.Of(b).Key;
        if (Sprite(c, $"buildings/{key}.png", p, 34 * s)) return;
        Shadow(c, p + new Vector2(0, 1), 13 * s);
        var stone = new Color("d5cbb4");
        var wood = new Color("b08a5a");
        var tile = new Color("8a5a48");
        switch (b)
        {
            case Building.Granary:
                Box(c, p.X - 8 * s, p.Y - 13 * s, 16 * s, 13 * s, new Color("d9c48a"));
                Shape(c, new[] { p + new Vector2(-10 * s, -13 * s), p + new Vector2(0, -24 * s), p + new Vector2(10 * s, -13 * s) }, new Color("c2a25a"));
                c.DrawLine(p + new Vector2(-8 * s, -6 * s), p + new Vector2(8 * s, -6 * s), Ink, 1.2f);
                break;
            case Building.Market:
                Box(c, p.X - 11 * s, p.Y - 4 * s, 22 * s, 4 * s, wood);
                c.DrawLine(p + new Vector2(-10 * s, -4 * s), p + new Vector2(-10 * s, -15 * s), Ink, 1.6f);
                c.DrawLine(p + new Vector2(10 * s, -4 * s), p + new Vector2(10 * s, -15 * s), Ink, 1.6f);
                for (int k = 0; k < 4; k++)
                    Shape(c, new[] { p + new Vector2((-12 + k * 6) * s, -15 * s), p + new Vector2((-6 + k * 6) * s, -15 * s), p + new Vector2((-7 + k * 6) * s, -20 * s), p + new Vector2((-11 + k * 6) * s, -20 * s) },
                        k % 2 == 0 ? new Color("b5442f") : new Color("ead9a8"), 1.1f);
                break;
            case Building.Temple:
                Box(c, p.X - 12 * s, p.Y - 3 * s, 24 * s, 3 * s, stone);
                for (int k = 0; k < 4; k++) Box(c, p.X + (-10 + k * 6) * s, p.Y - 15 * s, 2.5f * s, 12 * s, stone, 1.1f);
                Shape(c, new[] { p + new Vector2(-13 * s, -15 * s), p + new Vector2(0, -24 * s), p + new Vector2(13 * s, -15 * s) }, new Color("c9b98a"));
                break;
            case Building.Library:
                Box(c, p.X - 10 * s, p.Y - 14 * s, 20 * s, 14 * s, new Color("e2d8c0"));
                Shape(c, new[] { p + new Vector2(-12 * s, -14 * s), p + new Vector2(-8 * s, -20 * s), p + new Vector2(8 * s, -20 * s), p + new Vector2(12 * s, -14 * s) }, new Color("5f6f8a"));
                for (int k = 0; k < 3; k++) c.DrawRect(new Rect2(p.X + (-7 + k * 6) * s, p.Y - 11 * s, 3 * s, 6 * s), new Color(Ink, 0.55f));
                break;
            case Building.Barracks:
                Box(c, p.X - 12 * s, p.Y - 9 * s, 24 * s, 9 * s, wood);
                Shape(c, new[] { p + new Vector2(-13 * s, -9 * s), p + new Vector2(-9 * s, -15 * s), p + new Vector2(9 * s, -15 * s), p + new Vector2(13 * s, -9 * s) }, new Color("7a4a3a"));
                c.DrawLine(p + new Vector2(-4 * s, -15 * s), p + new Vector2(5 * s, -27 * s), Ink, 1.5f, true);
                c.DrawLine(p + new Vector2(4 * s, -15 * s), p + new Vector2(-5 * s, -27 * s), Ink, 1.5f, true);
                break;
            case Building.Castle:
                Box(c, p.X - 11 * s, p.Y - 14 * s, 22 * s, 14 * s, stone);
                Box(c, p.X - 15 * s, p.Y - 22 * s, 8 * s, 22 * s, stone.Darkened(0.06f));
                Box(c, p.X + 7 * s, p.Y - 22 * s, 8 * s, 22 * s, stone.Darkened(0.06f));
                for (int k = 0; k < 2; k++)
                {
                    float x = p.X + (k == 0 ? -15 : 7) * s;
                    c.DrawRect(new Rect2(x, p.Y - 25 * s, 2.5f * s, 3 * s), Ink);
                    c.DrawRect(new Rect2(x + 5.5f * s, p.Y - 25 * s, 2.5f * s, 3 * s), Ink);
                }
                c.DrawRect(new Rect2(p.X - 2.5f * s, p.Y - 8 * s, 5 * s, 8 * s), new Color(Ink, 0.75f));
                break;
            case Building.Mine:
                Shape(c, new[] { p + new Vector2(-14 * s, 0), p + new Vector2(-3 * s, -18 * s), p + new Vector2(4 * s, -12 * s), p + new Vector2(14 * s, 0) }, new Color("9a9388"));
                Shape(c, new[] { p + new Vector2(-5 * s, 0), p + new Vector2(-5 * s, -8 * s), p + new Vector2(5 * s, -8 * s), p + new Vector2(5 * s, 0) }, new Color("3a302a"), 2f);
                break;
            case Building.Harbor:
                Box(c, p.X - 13 * s, p.Y - 3 * s, 26 * s, 3 * s, wood);
                Shape(c, new[] { p + new Vector2(-8 * s, -4 * s), p + new Vector2(8 * s, -4 * s), p + new Vector2(5 * s, -9 * s), p + new Vector2(-5 * s, -9 * s) }, new Color("7a5a3a"));
                c.DrawLine(p + new Vector2(0, -9 * s), p + new Vector2(0, -26 * s), Ink, 1.5f);
                Shape(c, new[] { p + new Vector2(1 * s, -25 * s), p + new Vector2(10 * s, -12 * s), p + new Vector2(1 * s, -12 * s) }, new Color("f0e8d4"), 1.2f);
                break;
            case Building.University:
                Box(c, p.X - 13 * s, p.Y - 12 * s, 26 * s, 12 * s, new Color("e0d6c2"));
                Box(c, p.X - 4 * s, p.Y - 22 * s, 8 * s, 22 * s, new Color("e8dfcc"));
                Shape(c, new[] { p + new Vector2(-6 * s, -22 * s), p + new Vector2(0, -30 * s), p + new Vector2(6 * s, -22 * s) }, new Color("5f6f8a"));
                Dot(c, p + new Vector2(0, -16 * s), 2.2f * s, new Color("f4ecd6"), 1f);
                break;
            case Building.Factory:
                Box(c, p.X - 13 * s, p.Y - 10 * s, 26 * s, 10 * s, new Color("b9a79a"));
                for (int k = 0; k < 3; k++)
                    Shape(c, new[] { p + new Vector2((-13 + k * 8.6f) * s, -10 * s), p + new Vector2((-13 + k * 8.6f) * s, -16 * s), p + new Vector2((-4.4f + k * 8.6f) * s, -10 * s) }, tile, 1.2f);
                Box(c, p.X + 7 * s, p.Y - 28 * s, 5 * s, 18 * s, new Color("8a6a5a"));
                break;
        }
    }

    public static void Scaffold(CanvasItem c, Vector2 p, float s, float done)
    {
        if (!Sprite(c, "buildings/site.png", p, 30 * s))
        {
            var wood = new Color("b08a5a");
            for (int k = 0; k < 3; k++) c.DrawLine(p + new Vector2((-9 + k * 9) * s, 0), p + new Vector2((-9 + k * 9) * s, -18 * s), Ink, 1.6f);
            c.DrawLine(p + new Vector2(-11 * s, -7 * s), p + new Vector2(11 * s, -7 * s), wood, 2.2f);
            c.DrawLine(p + new Vector2(-11 * s, -15 * s), p + new Vector2(11 * s, -15 * s), wood, 2.2f);
            c.DrawLine(p + new Vector2(-9 * s, 0), p + new Vector2(0, -7 * s), Ink, 1.1f, true);
        }
        c.DrawRect(new Rect2(p.X - 11 * s, p.Y + 3, 22 * s, 4), new Color(Ink, 0.6f));
        c.DrawRect(new Rect2(p.X - 11 * s + 1, p.Y + 4, (22 * s - 2) * done, 2), Gold);
    }

    // ───────── 사람 ─────────

    /// <summary>걸어 다니는 사람 하나. phase는 걸음(라디안), dir은 −1/+1.</summary>
    public static void Walker(CanvasItem c, Vector2 p, float phase, int dir, Color tunic, int kind, bool moving, float s = 1f)
    {
        // 블렌더에서 찍은 사람 그림(Folk.cs): 군사는 병사, 등짐꾼은 짐 진 이, 나머지는 남녀를 번갈아
        string body = kind == 3 ? "soldier" : kind == 1 ? "woman" : "man";
        string anim = !moving ? "idle" : kind == 2 ? "carry_food" : "walk";
        if (ActorLayer.DrawFolk(c, body, anim, new Vector2(dir, 0.35f), phase / Mathf.Tau, p, ActorLayer.FolkHeight * s,
                kind == 3 ? Colors.White.Lerp(tunic, 0.3f) : null)) return;
        int frame = moving ? (int)(phase / (Mathf.Pi / 2)) & 3 : 0;
        if (Strip(c, kind == 3 ? "units/soldier.png" : "units/walker.png", 4, frame, p, 22 * s, dir < 0)) return;

        Shadow(c, p, 5 * s);
        float swing = moving ? Mathf.Sin(phase) * 3.2f * s : 0;
        float bob = moving ? Mathf.Abs(Mathf.Cos(phase)) * 1.2f * s : 0;
        var hip = p + new Vector2(0, -6 * s - bob);
        c.DrawLine(hip, p + new Vector2(swing, 0), Ink, 1.8f * s, true);
        c.DrawLine(hip, p + new Vector2(-swing, 0), Ink, 1.8f * s, true);
        // 몸
        Shape(c, new[] { hip + new Vector2(-3.4f * s, 1 * s), hip + new Vector2(-2.6f * s, -8 * s), hip + new Vector2(2.6f * s, -8 * s), hip + new Vector2(3.4f * s, 1 * s) }, tunic, 1.2f);
        // 팔
        c.DrawLine(hip + new Vector2(dir * 1 * s, -7 * s), hip + new Vector2(dir * 4 * s - swing * 0.5f, -2 * s), Ink, 1.4f * s, true);
        var head = hip + new Vector2(0, -11 * s);
        Dot(c, head, 2.9f * s, new Color("e6c9a4"), 1.1f);
        switch (kind)
        {
            case 1: // 삿갓
                Shape(c, new[] { head + new Vector2(-5.5f * s, -1 * s), head + new Vector2(0, -6 * s), head + new Vector2(5.5f * s, -1 * s) }, new Color("d9c48a"), 1.1f);
                break;
            case 2: // 등짐
                Box(c, hip.X - dir * 6 * s - 2 * s, hip.Y - 9 * s, 4.5f * s, 6.5f * s, new Color("b08a5a"), 1.1f);
                break;
            case 3: // 군사: 투구와 창
                c.DrawArc(head, 3.1f * s, Mathf.Pi, Mathf.Tau, 8, Ink, 2.2f * s, true);
                c.DrawLine(hip + new Vector2(dir * 4.5f * s, 2 * s), hip + new Vector2(dir * 4.5f * s, -20 * s), Ink, 1.3f, true);
                Shape(c, new[] { hip + new Vector2(dir * 4.5f * s - 1.6f * s, -20 * s), hip + new Vector2(dir * 4.5f * s, -24.5f * s), hip + new Vector2(dir * 4.5f * s + 1.6f * s, -20 * s) }, new Color("cfd2d4"), 1f);
                break;
        }
    }

    public static void Cart(CanvasItem c, Vector2 p, float phase, int dir)
    {
        if (Strip(c, "units/cart.png", 4, (int)(phase / (Mathf.Pi / 2)) & 3, p + new Vector2(0, 6), 34, dir < 0)) return;
        Shadow(c, p, 9);
        Box(c, p.X - 8, p.Y - 10, 16, 6, new Color("b08a5a"), 1.3f);
        Box(c, p.X - 6, p.Y - 14, 12, 4, new Color("d9c48a"), 1.1f);
        Dot(c, p + new Vector2(-5, -2), 3, new Color("8a6a4a"), 1.2f);
        Dot(c, p + new Vector2(5, -2), 3, new Color("8a6a4a"), 1.2f);
        c.DrawLine(p + new Vector2(-5, -2), p + new Vector2(-5 + Mathf.Cos(phase) * 3, -2 + Mathf.Sin(phase) * 3), Ink, 1f);
        c.DrawLine(p + new Vector2(dir * 8, -7), p + new Vector2(dir * 14, -6), Ink, 1.3f);
    }

    static readonly Dictionary<(Vector2 a, Vector2 b, int seed), Vector2[]> RoadCache = new();

    /// <summary>두 지방을 잇는 길의 굽은 선.</summary>
    public static Vector2[] RoadCurve(Vector2 a, Vector2 b, int seed)
    {
        if (RoadCache.TryGetValue((a, b, seed), out var cached)) return cached;
        var mid = (a + b) / 2;
        var n = (b - a).Orthogonal().Normalized();
        var ctrl = mid + n * (Hash(seed, 3, 17) - 0.5f) * a.DistanceTo(b) * 0.35f;
        var pts = new Vector2[17];
        for (int k = 0; k <= 16; k++)
        {
            float t = k / 16f;
            pts[k] = a.Lerp(ctrl, t).Lerp(ctrl.Lerp(b, t), t);
        }
        RoadCache[(a, b, seed)] = pts;
        return pts;
    }

    public static Vector2 Along(Vector2[] pts, float t)
    {
        t = Mathf.Clamp(t, 0, 1) * (pts.Length - 1);
        int k = Math.Min(pts.Length - 2, (int)t);
        return pts[k].Lerp(pts[k + 1], t - k);
    }

    public static string ResourceIcon(Instinct.Sim.Resource r) => r switch
    {
        Instinct.Sim.Resource.Livestock => "res_livestock",
        Instinct.Sim.Resource.Copper => "res_copper",
        Instinct.Sim.Resource.Iron => "res_iron",
        Instinct.Sim.Resource.Spice => "res_spice",
        Instinct.Sim.Resource.Coal => "res_coal",
        Instinct.Sim.Resource.Oil => "res_oil",
        Instinct.Sim.Resource.Uranium => "res_uranium",
        _ => "stone",
    };

    public static string Num(double v) => v >= 10000 ? $"{v / 10000:0.#}만" : $"{v:N0}";
}
