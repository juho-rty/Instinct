using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>
/// 칸 하나하나의 그림: 먹을거리(사냥감·열매·물고기·돌·기름진 흙), 칸 건물(지은 것과 짓는 것).
/// OverlayLayer가 부른다. 그림은 모두 코드로 그린다(원작 그림 없음).
/// </summary>
public static class Cells
{
    public const int W = WorldMap.Width;

    public static Vector2 At(int cell) => new((cell % W + 0.5f) * Art.Cell, (cell / W + 0.5f) * Art.Cell);

    public static int CellAt(Vector2 world)
    {
        int x = (int)(world.X / Art.Cell), y = (int)(world.Y / Art.Cell);
        return x < 0 || y < 0 || x >= W || y >= WorldMap.Height ? -1 : y * W + x;
    }

    public static int CountSeen(bool[] seen)
    {
        int n = 0;
        for (int i = 0; i < seen.Length; i++) if (seen[i]) n++;
        return n;
    }

    static readonly Color Deer = new("8a5a32"), Berry = new("b0333a"), Leaf = new("4f7a3a"), FishC = new("4a7fa6"),
        Rock = new("8d8a84"), Wheat = new("c9a23a"), Wood = new("a77b4c"), Thatch = new("cfae6a");

    public static string FeatureName(Instinct.Sim.Feature f) => f switch
    {
        Instinct.Sim.Feature.Game => "사냥감", Instinct.Sim.Feature.Berries => "열매 덤불", Instinct.Sim.Feature.Fish => "물고기", Instinct.Sim.Feature.Stone => "돌", Instinct.Sim.Feature.Fertile => "기름진 흙", _ => "",
    };

    public static string GroundName(Ground g) => g switch
    {
        Ground.Deep => "깊은 바다", Ground.Shallows => "얕은 바다", Ground.Sand => "모래톱", Ground.Grass => "풀밭", Ground.Woods => "숲",
        Ground.Hill => "언덕", Ground.Peak => "봉우리", Ground.Dry => "마른 들", Ground.Marsh => "늪", _ => "",
    };

    /// <summary>먹을거리 표시. 작은 바탕 원 위에 그려서 나무 사이에서도 보인다.</summary>
    public static void FeatureIcon(CanvasItem c, Instinct.Sim.Feature f, Vector2 p)
    {
        if (f == Instinct.Sim.Feature.None) return;
        if (Art.IsPng($"features/{f}.png") && Art.Tex($"features/{f}.png") is { } shot)
        {
            // 블렌더 그림: 칸 가운데보다 조금 아래가 땅. 그림자까지 그림에 들어 있다.
            c.DrawTextureRect(shot, Art.EnvRect(p + new Vector2(0, 16), 62), false);
            return;
        }
        if (Art.Tex($"features/{f}.svg") is { } feature)
        {
            var tint = f == Instinct.Sim.Feature.Stone ? new Color("9ac9df") : f == Instinct.Sim.Feature.Fish ? new Color("6bd9cf") : new Color("edc779");
            c.DrawCircle(p + new Vector2(0, 2), 20, new Color("13282e", 0.94f));
            c.DrawArc(p + new Vector2(0, 2), 20, 0, Mathf.Tau, 32, tint, 1.6f, true);
            c.DrawTextureRect(feature, new Rect2(p.X - 17, p.Y - 16, 34, 34), false);
            return;
        }
        c.DrawCircle(p + new Vector2(0, 2), 15, new Color(Art.Paper, 0.72f));
        c.DrawArc(p + new Vector2(0, 2), 15, 0, Mathf.Tau, 24, new Color(Art.Ink, 0.35f), 1.2f, true);
        switch (f)
        {
            case Instinct.Sim.Feature.Game: // 사슴 머리와 뿔
                c.DrawCircle(p + new Vector2(0, 5), 5.5f, Deer);
                c.DrawPolyline(new[] { p + new Vector2(-3, 0), p + new Vector2(-6, -7), p + new Vector2(-10, -9) }, Art.Ink, 2f, true);
                c.DrawPolyline(new[] { p + new Vector2(3, 0), p + new Vector2(6, -7), p + new Vector2(10, -9) }, Art.Ink, 2f, true);
                c.DrawLine(p + new Vector2(-6, -7), p + new Vector2(-5, -11), Art.Ink, 1.6f, true);
                c.DrawLine(p + new Vector2(6, -7), p + new Vector2(5, -11), Art.Ink, 1.6f, true);
                break;
            case Instinct.Sim.Feature.Berries:
                c.DrawColoredPolygon(new[] { p + new Vector2(0, -10), p + new Vector2(7, -5), p + new Vector2(0, -2) }, Leaf);
                Art.Dot(c, p + new Vector2(-4, 1), 4, Berry, 1);
                Art.Dot(c, p + new Vector2(4, 2), 4, Berry, 1);
                Art.Dot(c, p + new Vector2(0, 7), 4, Berry, 1);
                break;
            case Instinct.Sim.Feature.Fish:
                c.DrawColoredPolygon(new[] { p + new Vector2(-10, 2), p + new Vector2(-4, -4), p + new Vector2(4, -4), p + new Vector2(8, 2), p + new Vector2(4, 7), p + new Vector2(-4, 7) }, FishC);
                c.DrawColoredPolygon(new[] { p + new Vector2(7, 2), p + new Vector2(13, -4), p + new Vector2(13, 8) }, FishC);
                c.DrawCircle(p + new Vector2(-5, 0), 1.3f, Art.Ink);
                break;
            case Instinct.Sim.Feature.Stone:
                Art.Shape(c, new[] { p + new Vector2(-11, 8), p + new Vector2(-8, -1), p + new Vector2(-2, -3), p + new Vector2(1, 8) }, Rock, 1.2f);
                Art.Shape(c, new[] { p + new Vector2(-2, 8), p + new Vector2(1, -6), p + new Vector2(7, -8), p + new Vector2(11, 0), p + new Vector2(10, 8) }, Rock.Lightened(0.15f), 1.2f);
                break;
            case Instinct.Sim.Feature.Fertile:
                for (int k = -1; k <= 1; k++)
                {
                    var b = p + new Vector2(k * 6, 9);
                    c.DrawLine(b, b + new Vector2(0, -15), Art.Ink, 1.4f, true);
                    for (int j = 0; j < 3; j++)
                    {
                        c.DrawCircle(b + new Vector2(-2, -6 - j * 3.5f), 1.8f, Wheat);
                        c.DrawCircle(b + new Vector2(2, -7.5f - j * 3.5f), 1.8f, Wheat);
                    }
                }
                break;
        }
    }

    /// <summary>칸 건물 하나. done이 1보다 작으면 짓는 중(흐리게, 아래에 진척 막대).</summary>
    public static void Structure(CanvasItem c, StructureKind k, Vector2 p, float done, bool shadow = true)
    {
        bool built = done >= 1;
        if (Art.IsPng($"structures/{k}.png") && Art.Tex($"structures/{k}.png") is { } shot)
        {
            // 블렌더 그림. 지도에서는 칸보다 조금 크게, 단추(shadow=false)에서는 작은 칸에 맞춘다.
            float size = shadow ? 84 : 70;
            c.DrawTextureRect(shot, Art.EnvRect(p + new Vector2(0, shadow ? 20 : 17), size), false, new Color(1, 1, 1, built ? 1 : 0.5f));
            if (!built)
            {
                c.DrawRect(new Rect2(p.X - 23, p.Y + 24, 46, 6), new Color("142a30"));
                c.DrawRect(new Rect2(p.X - 22, p.Y + 25, 44 * Math.Clamp(done, 0, 1), 4), Art.Gold);
            }
            return;
        }
        if (Art.Tex($"structures/{k}.svg") is { } sprite)
        {
            c.DrawTextureRect(sprite, new Rect2(p.X - 28, p.Y - 36, 56, 56), false, new Color(1, 1, 1, built ? 1 : 0.55f));
            if (!built)
            {
                c.DrawRect(new Rect2(p.X - 23, p.Y + 22, 46, 6), new Color("142a30"));
                c.DrawRect(new Rect2(p.X - 22, p.Y + 23, 44 * Math.Clamp(done, 0, 1), 4), Art.Gold);
            }
            return;
        }
        var tone = built ? 1f : 0.55f;
        Color T(Color col) => new(col, tone);
        var ink = new Color(Art.Ink, built ? 1 : 0.5f);
        if (shadow) Art.Shadow(c, p + new Vector2(0, 14), 22);
        void Roof(Vector2 b, float w, float h, Color col) =>
            Shp(new[] { b + new Vector2(-w, 0), b + new Vector2(0, -h), b + new Vector2(w, 0) }, T(col));
        void Shp(Vector2[] pts, Color col)
        {
            c.DrawColoredPolygon(pts, col);
            var l = new Vector2[pts.Length + 1];
            pts.CopyTo(l, 0);
            l[^1] = pts[0];
            c.DrawPolyline(l, ink, 1.6f, true);
        }
        void Rect(float x, float y, float w, float h, Color col) =>
            Shp(new[] { p + new Vector2(x, y), p + new Vector2(x + w, y), p + new Vector2(x + w, y + h), p + new Vector2(x, y + h) }, T(col));

        switch (k)
        {
            case StructureKind.Hut:
                Rect(-11, 2, 22, 11, Wood.Lightened(0.2f));
                Roof(p + new Vector2(0, 4), 16, 20, Thatch);
                Rect(-3, 6, 6, 7, Art.Ink);
                break;
            case StructureKind.Lodge:
                Roof(p + new Vector2(0, 13), 15, 26, Wood);
                c.DrawPolyline(new[] { p + new Vector2(-4, -14), p + new Vector2(-9, -21), p + new Vector2(-13, -20) }, ink, 2, true);
                c.DrawPolyline(new[] { p + new Vector2(4, -14), p + new Vector2(9, -21), p + new Vector2(13, -20) }, ink, 2, true);
                break;
            case StructureKind.Weir:
                for (int i = -2; i <= 2; i++) c.DrawLine(p + new Vector2(i * 6, -10 + Math.Abs(i) * 3), p + new Vector2(i * 6, 10 + Math.Abs(i) * 3), T(Wood.Darkened(0.2f)), 3, true);
                c.DrawPolyline(new[] { p + new Vector2(-14, 0), p + new Vector2(0, 6), p + new Vector2(14, 0) }, ink, 1.6f, true);
                break;
            case StructureKind.Field:
                Shp(new[] { p + new Vector2(-22, -6), p + new Vector2(18, -12), p + new Vector2(22, 10), p + new Vector2(-18, 14) }, T(new Color("9a7a48")));
                for (int i = 0; i < 4; i++)
                {
                    float t = (i + 0.5f) / 4;
                    c.DrawLine(p + new Vector2(-22 + 4 * t, -6 + 20 * t), p + new Vector2(18 + 4 * t, -12 + 22 * t), T(built ? Wheat : Leaf), 3, true);
                }
                break;
            case StructureKind.Granary:
                Rect(-12, -4, 24, 17, new Color("d7c39a"));
                Roof(p + new Vector2(0, -3), 16, 16, Thatch.Darkened(0.1f));
                c.DrawLine(p + new Vector2(-12, 4), p + new Vector2(12, 4), ink, 1.2f);
                break;
            case StructureKind.Lumber:
                for (int i = 0; i < 3; i++)
                {
                    var b = p + new Vector2(-10 + i * 4 + (i == 2 ? 2 : 0), 8 - i * 7);
                    Rect(b.X - p.X, b.Y - p.Y, 18 - i * 2, 6, Wood);
                    c.DrawCircle(b + new Vector2(18 - i * 2, 3), 3, T(new Color("e3c08a")));
                }
                c.DrawLine(p + new Vector2(-16, 14), p + new Vector2(-6, -16), ink, 2, true);
                break;
            case StructureKind.Quarry:
                Rect(-14, 2, 12, 10, Rock);
                Rect(0, 4, 13, 9, Rock.Lightened(0.1f));
                Rect(-7, -8, 12, 10, Rock.Lightened(0.2f));
                c.DrawLine(p + new Vector2(8, -2), p + new Vector2(16, -14), ink, 2, true);
                break;
            case StructureKind.Palisade:
                for (int i = -3; i <= 3; i++)
                {
                    float x = i * 6, h = 20 + (i % 2 == 0 ? 3 : 0);
                    Shp(new[] { p + new Vector2(x - 2.6f, 12), p + new Vector2(x - 2.6f, 12 - h + 4), p + new Vector2(x, 12 - h), p + new Vector2(x + 2.6f, 12 - h + 4), p + new Vector2(x + 2.6f, 12) }, T(Wood));
                }
                break;
            case StructureKind.WarHut:
                Rect(-13, 0, 26, 13, Wood.Darkened(0.1f));
                Roof(p + new Vector2(0, 2), 17, 16, new Color("8a4a32"));
                c.DrawLine(p + new Vector2(-16, -20), p + new Vector2(16, 12), ink, 2, true);
                c.DrawLine(p + new Vector2(16, -20), p + new Vector2(-16, 12), ink, 2, true);
                break;
            case StructureKind.Tower:
                c.DrawLine(p + new Vector2(-8, 14), p + new Vector2(-4, -12), ink, 2.4f, true);
                c.DrawLine(p + new Vector2(8, 14), p + new Vector2(4, -12), ink, 2.4f, true);
                c.DrawLine(p + new Vector2(-7, 6), p + new Vector2(6, -2), ink, 1.4f, true);
                Rect(-8, -20, 16, 8, Wood);
                Roof(p + new Vector2(0, -20), 11, 9, Thatch);
                break;
            case StructureKind.Shrine:
                Shp(new[] { p + new Vector2(-6, 13), p + new Vector2(-7, -10), p + new Vector2(0, -18), p + new Vector2(7, -10), p + new Vector2(6, 13) }, T(Rock.Lightened(0.15f)));
                c.DrawCircle(p + new Vector2(0, -5), 3, T(Art.Bad));
                c.DrawLine(p + new Vector2(-14, 13), p + new Vector2(14, 13), ink, 2);
                break;
        }
        if (!built)
        {
            c.DrawRect(new Rect2(p.X - 18, p.Y + 18, 36, 5), new Color(Art.Ink, 0.7f));
            c.DrawRect(new Rect2(p.X - 17, p.Y + 19, 34 * Math.Clamp(done, 0, 1), 3), Art.Gold);
        }
    }

    /// <summary>먹을거리와 칸 건물을 그린다(본 칸만).</summary>
    public static void DrawAll(CanvasItem c, Simulation s)
    {
        var map = s.Map;
        var taken = new HashSet<int>(s.Structures.Select(x => x.Cell));
        for (int i = 0; i < map.Feat.Length; i++)
            if (map.Feat[i] != Instinct.Sim.Feature.None && map.Seen[i] && !taken.Contains(i))
                FeatureIcon(c, map.Feat[i], At(i));
        foreach (var st in s.Structures.OrderBy(x => x.Cell / W).ThenBy(x => x.Id))
            Structure(c, st.Kind, At(st.Cell), (float)(st.Done / Math.Max(0.01, st.Work)));
    }
}

/// <summary>안개: 아직 보지 못한 칸을 종이빛으로 덮는다. 시야가 넓어질 때만 다시 만든다.</summary>
public partial class FogLayer : Node2D
{
    public Main Game = null!;
    ImageTexture? _tex;
    int _sig = -1;
    Simulation? _sim;
    const int K = 4; // 칸 하나에 점 넷

    public override void _Ready() => TextureFilter = TextureFilterEnum.Linear;
    public override void _Process(double delta) => Sync();

    public void Sync()
    {
        var s = Game.S;
        int sig = Cells.CountSeen(s.Map.Seen);
        if (sig == _sig && _sim == s) return;
        _sig = sig;
        _sim = s;
        int w = WorldMap.Width * K, h = WorldMap.Height * K;
        var a = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                a[y * w + x] = s.Map.Seen[y / K * WorldMap.Width + x / K] ? 0 : 1;
        Art.Blur(a, w, h, 2);
        Art.Blur(a, w, h, 2);
        var data = new byte[w * h * 4];
        var paper = new Color("243b43");
        for (int i = 0; i < a.Length; i++)
        {
            float n = Art.Hash(i % w, i / w, 77) * 0.04f;
            data[i * 4] = (byte)((paper.R - n) * 255);
            data[i * 4 + 1] = (byte)((paper.G - n) * 255);
            data[i * 4 + 2] = (byte)((paper.B - n * 1.4f) * 255);
            data[i * 4 + 3] = (byte)(Mathf.Clamp(a[i] * 1.15f, 0, 0.97f) * 255);
        }
        _tex = ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgba8, data));
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_tex != null) DrawTextureRect(_tex, new Rect2(Vector2.Zero, TerrainLayer.WorldSize), false);
    }
}

/// <summary>짓기 모드의 표시: 지을 수 있는 칸에 초록 테, 마우스 아래 칸에 미리보기. 안개 위에 그린다.</summary>
public partial class MarkLayer : Node2D
{
    public Main Game = null!;
    static readonly StyleBoxFlat TipBox = MakeTipBox();
    static StyleBoxFlat MakeTipBox()
    {
        var sb = new StyleBoxFlat { BgColor = new Color("101e24", 0.92f), BorderColor = new Color("82724d", 0.7f), AntiAliasing = true };
        sb.SetCornerRadiusAll(8);
        sb.SetBorderWidthAll(1);
        return sb;
    }

    public override void _Draw()
    {
        if (Game.Placing is not { } kind) return;
        foreach (int c in Game.PlaceOk)
        {
            var p = Cells.At(c);
            DrawRect(new Rect2(p - new Vector2(29, 29), new Vector2(58, 58)), new Color(Art.Good, 0.16f));
            DrawRect(new Rect2(p - new Vector2(29, 29), new Vector2(58, 58)), new Color(Art.Good, 0.75f), false, 2.5f);
        }
        int h = Game.Hover;
        if (h < 0) return;
        var hp = Cells.At(h);
        string why = Game.S.WhyCantPlace(kind, h);
        bool ok = why.Length == 0;
        DrawRect(new Rect2(hp - new Vector2(31, 31), new Vector2(62, 62)), new Color(ok ? Art.Good : Art.Bad, 0.95f), false, 4f);
        Cells.Structure(this, kind, hp, ok ? 1 : 0.0f);
        var def = StructureCatalog.Of(kind);
        string text = ok ? $"{def.Name}: {Game.S.PlacePreview(kind, h)}" : $"{def.Name}: {why}";
        const int fs = 16;
        float tw = Art.Font.GetStringSize(text, HorizontalAlignment.Left, -1, fs).X;
        var at = hp + new Vector2(0, -44);
        TipBox.BorderColor = new Color(ok ? Art.Good : Art.Bad, 0.85f);
        DrawStyleBox(TipBox, new Rect2(at.X - tw / 2 - 10, at.Y - 20, tw + 20, 26));
        DrawString(Art.Font, new Vector2(at.X - tw / 2, at.Y - 2), text, HorizontalAlignment.Left, -1, fs, ok ? new Color("b6e89e") : new Color("ff9e85"));
    }
}
