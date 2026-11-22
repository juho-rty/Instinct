using Godot;

namespace InstinctGame;

/// <summary>
/// 화면의 겉: 뒤가 흐리게 비치는 유리 판, 알약 모양 단추, 코드로 그린 선 아이콘.
/// 아이콘은 벡터로 그리므로 4K에서도 또렷하다. game/assets/ui/icon_{이름}.png 가 있으면 그 그림을 쓴다(ASSETS.md).
/// </summary>
public static class Ui
{
    // 어두운 청석 바탕 위에 따뜻한 상아빛 글씨와 황동 테두리, 청록 강조색.
    public static readonly Color Text = new("f2efe4");
    public static readonly Color Sub = new("b5c4c0");
    public static readonly Color Accent = new("5fc3d4");
    public static readonly Color Tint = new("0f1d24");
    public static readonly Color Rim = new("b59a64");
    public static readonly Color Surface = new("1d333c");

    static ShaderMaterial? _glass;

    /// <summary>뒤를 흐려 비추는 재질. 판의 상자 그림(StyleBox)의 빛깔을 그 위에 덮는다.</summary>
    public static ShaderMaterial Glass
    {
        get
        {
            if (_glass != null) return _glass;
            var sh = new Shader
            {
                Code = """
                shader_type canvas_item;
                uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
                uniform float radius = 0.011;
                void fragment() {
                    vec4 c = COLOR;
                    vec2 px = SCREEN_PIXEL_SIZE;
                    vec2 r = vec2(radius * px.x / px.y, radius);
                    vec3 sum = vec3(0.0);
                    float n = 0.0;
                    for (int i = -3; i <= 3; i++) {
                        for (int j = -3; j <= 3; j++) {
                            vec2 o = vec2(float(i), float(j)) / 3.0;
                            float w = exp(-dot(o, o) * 1.6);
                            sum += texture(screen_tex, SCREEN_UV + o * r).rgb * w;
                            n += w;
                        }
                    }
                    vec3 bg = sum / n;
                    // 뒤를 조금 밝히고 채도를 줄여 글이 잘 읽히게 한다.
                    float l = dot(bg, vec3(0.299, 0.587, 0.114));
                    bg = mix(vec3(l), bg, 0.55) * 0.9 + 0.1;
                    float cover = smoothstep(0.0, 0.06, c.a);
                    COLOR = vec4(mix(bg, c.rgb, c.a), cover);
                }
                """,
            };
            _glass = new ShaderMaterial { Shader = sh };
            return _glass;
        }
    }

    public static StyleBoxFlat GlassBox(int radius = 14, int pad = 12, float tint = 0.95f)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(Tint, Math.Clamp(tint, 0.85f, 0.99f)),
            BorderColor = new Color(Rim, 0.72f),
            AntiAliasing = true,
            AntiAliasingSize = 1.1f,
            ShadowColor = new Color(0, 0, 0, 0.36f),
            ShadowSize = radius > 0 ? 10 : 4,
            ShadowOffset = new Vector2(0, 3),
        };
        sb.SetBorderWidthAll(1);
        sb.BorderWidthTop = 2;
        sb.SetCornerRadiusAll(Math.Min(radius, 14));
        sb.SetContentMarginAll(pad);
        return sb;
    }

    /// <summary>알약 모양 단추의 상자. hpad를 따로 주면 작은 단추(−/+)도 글자가 잘리지 않는다.</summary>
    public static StyleBoxFlat Pill(Color bg, Color rim, int pad = 8, int radius = 9, int hpad = -1)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = rim, AntiAliasing = true };
        sb.SetBorderWidthAll(1);
        sb.BorderWidthTop = 2;
        sb.SetCornerRadiusAll(Math.Min(radius, 10));
        int hp = hpad >= 0 ? hpad : pad + 4;
        sb.ContentMarginLeft = sb.ContentMarginRight = hp;
        sb.ContentMarginTop = sb.ContentMarginBottom = Math.Max(2, pad - 3);
        return sb;
    }

    /// <summary>판을 유리로 만든다(뒤가 흐리게 비친다).</summary>
    public static T MakeGlass<T>(T panel, int radius = 18, int pad = 12, float tint = 0.90f) where T : Control
    {
        panel.AddThemeStyleboxOverride("panel", GlassBox(radius, pad, tint));
        panel.Material = null;
        return panel;
    }

    // ───────── 아이콘 ─────────

    static readonly Dictionary<string, Texture2D?> Pngs = new();

    /// <summary>r 안에 아이콘 하나를 그린다. 선은 col, 굵기는 크기에 맞춘다.</summary>
    public static void Icon(CanvasItem c, string kind, Rect2 r, Color col)
    {
        if (Art.Tex($"ui/color_{kind}.svg") is { } colored)
        {
            float brightness = col.A < 0.7f ? 0.65f : 1f;
            c.DrawTextureRect(colored, r, false, new Color(brightness, brightness, brightness, col.A));
            return;
        }
        if (!Pngs.TryGetValue(kind, out var png)) Pngs[kind] = png = Art.Tex($"ui/icon_{kind}.png");
        if (png != null) { c.DrawTextureRect(png, r, false, col); return; }

        float s = Math.Min(r.Size.X, r.Size.Y);
        var o = r.Position + (r.Size - new Vector2(s, s)) / 2;
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s / 20f;
        float w = Math.Max(1.4f, s / 11f);
        void Line(params Vector2[] pts) => c.DrawPolyline(pts, col, w, true);
        void Loop(params Vector2[] pts)
        {
            var l = new Vector2[pts.Length + 1];
            pts.CopyTo(l, 0);
            l[^1] = pts[0];
            c.DrawPolyline(l, col, w, true);
        }
        void Fill(params Vector2[] pts) => c.DrawColoredPolygon(pts, col);
        void Ring(float x, float y, float rad) => c.DrawArc(P(x, y), rad * s / 20f, 0, Mathf.Tau, 28, col, w, true);
        void Disc(float x, float y, float rad) => c.DrawCircle(P(x, y), rad * s / 20f, col);
        void Arc(float x, float y, float rad, float a0, float a1) => c.DrawArc(P(x, y), rad * s / 20f, a0, a1, 20, col, w, true);
        void Tri(float x, float y, float h) => Fill(P(x, y - h), P(x + h * 1.15f, y), P(x, y + h));

        switch (kind)
        {
            case "pause":
                Fill(P(5, 4), P(8.5f, 4), P(8.5f, 16), P(5, 16));
                Fill(P(11.5f, 4), P(15, 4), P(15, 16), P(11.5f, 16));
                break;
            case "play1": Tri(7, 10, 6.5f); break;
            case "play2": Tri(3.5f, 10, 5.5f); Tri(10.5f, 10, 5.5f); break;
            case "play3": Tri(1.5f, 10, 4.5f); Tri(7.2f, 10, 4.5f); Tri(12.9f, 10, 4.5f); break;
            case "bolt": Fill(P(11.5f, 1.5f), P(4, 11.5f), P(9.5f, 11.5f), P(8, 18.5f), P(16, 8), P(10.5f, 8), P(12.5f, 1.5f)); break;
            case "law": // 펼친 책
                Loop(P(10, 5), P(3, 3.5f), P(3, 15.5f), P(10, 17));
                Loop(P(10, 5), P(17, 3.5f), P(17, 15.5f), P(10, 17));
                Line(P(5.2f, 7.5f), P(8, 8.2f));
                Line(P(5.2f, 11), P(8, 11.7f));
                Line(P(12, 8.2f), P(14.8f, 7.5f));
                Line(P(12, 11.7f), P(14.8f, 11));
                break;
            case "tech": // 등불
                Arc(10, 8, 5.5f, Mathf.Pi * 0.78f, Mathf.Pi * 2.22f);
                Line(P(6.2f, 12), P(7.5f, 14.5f), P(12.5f, 14.5f), P(13.8f, 12));
                Line(P(8, 17), P(12, 17));
                Line(P(10, 8), P(10, 11.5f));
                break;
            case "realm": case "legit": // 관
                Loop(P(3, 15), P(3, 6), P(7, 10), P(10, 4), P(13, 10), P(17, 6), P(17, 15));
                Line(P(3, 17.5f), P(17, 17.5f));
                break;
            case "log": // 두루마리
                Loop(P(5, 3), P(15, 3), P(15, 17), P(5, 17));
                Line(P(7.5f, 7), P(12.5f, 7));
                Line(P(7.5f, 10), P(12.5f, 10));
                Line(P(7.5f, 13), P(11, 13));
                break;
            case "people": case "pop":
                Ring(7, 6.5f, 2.6f);
                Arc(7, 16.5f, 5, Mathf.Pi, Mathf.Tau);
                Ring(14, 7.5f, 2.2f);
                Arc(14.5f, 16.5f, 4, Mathf.Pi * 1.15f, Mathf.Tau);
                break;
            case "home": // 성
                Loop(P(3, 17), P(3, 6), P(5.5f, 6), P(5.5f, 8.5f), P(8.5f, 8.5f), P(8.5f, 6), P(11.5f, 6), P(11.5f, 8.5f), P(14.5f, 8.5f), P(14.5f, 6), P(17, 6), P(17, 17));
                Arc(10, 17, 2.5f, Mathf.Pi, Mathf.Tau);
                break;
            case "new": // 새싹
                Line(P(10, 18), P(10, 9));
                Loop(P(10, 10), P(5, 9), P(3, 4), P(8, 5));
                Loop(P(10, 9), P(14, 4), P(18, 3.5f), P(16, 8.5f));
                break;
            case "save":
                Line(P(10, 3), P(10, 12.5f));
                Line(P(6.5f, 9), P(10, 12.5f), P(13.5f, 9));
                Line(P(3.5f, 12), P(3.5f, 17), P(16.5f, 17), P(16.5f, 12));
                break;
            case "load":
                Loop(P(2.5f, 5), P(8, 5), P(9.5f, 7), P(17.5f, 7), P(17.5f, 16), P(2.5f, 16));
                Line(P(2.5f, 9.5f), P(17.5f, 9.5f));
                break;
            case "gear":
                Ring(10, 10, 2.5f);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.Tau / 8;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    c.DrawLine(P(10, 10) + d * s * 0.27f, P(10, 10) + d * s * 0.42f, col, w * 1.5f, true);
                }
                Ring(10, 10, 5.6f);
                break;
            case "close":
                Line(P(5, 5), P(15, 15));
                Line(P(15, 5), P(5, 15));
                break;
            case "back": Line(P(12.5f, 4), P(6.5f, 10), P(12.5f, 16)); break;
            case "hunt": // 창
                Line(P(3.5f, 16.5f), P(14, 6));
                Fill(P(17, 3), P(11.5f, 5.5f), P(14.5f, 8.5f));
                Line(P(3.5f, 16.5f), P(6.5f, 16.5f));
                Line(P(3.5f, 16.5f), P(3.5f, 13.5f));
                break;
            case "feast": // 그릇과 김
                Arc(10, 10, 7, 0, Mathf.Pi);
                Line(P(2.5f, 10), P(17.5f, 10));
                Line(P(7, 7), P(7.8f, 5), P(7, 3));
                Line(P(12, 7), P(12.8f, 5), P(12, 3));
                break;
            case "gift":
                Loop(P(3.5f, 9), P(16.5f, 9), P(16.5f, 17), P(3.5f, 17));
                Line(P(2.5f, 6), P(17.5f, 6), P(17.5f, 9), P(2.5f, 9), P(2.5f, 6));
                Line(P(10, 6), P(10, 17));
                Line(P(10, 6), P(7, 3), P(5.5f, 5), P(10, 6), P(13, 3), P(14.5f, 5), P(10, 6));
                break;
            case "raid": case "army": // 엇갈린 칼
                Line(P(4, 4), P(15, 15));
                Line(P(16, 4), P(5, 15));
                Line(P(12.5f, 16), P(16, 12.5f));
                Line(P(4, 12.5f), P(7.5f, 16));
                break;
            case "build": // 망치
                Line(P(4, 17), P(12, 9));
                Loop(P(9, 6), P(13, 2), P(18, 7), P(14, 11));
                break;
            case "road":
                Line(P(6, 18), P(8.5f, 2));
                Line(P(14, 18), P(11.5f, 2));
                Line(P(10, 15.5f), P(10, 13));
                Line(P(10, 9.5f), P(10, 7.5f));
                Line(P(10, 4.5f), P(10, 3));
                break;
            case "stop": Fill(P(5, 5), P(15, 5), P(15, 15), P(5, 15)); break;
            case "disband":
                Disc(10, 10, 1.8f);
                Disc(4.5f, 5, 1.5f);
                Disc(15.5f, 5, 1.5f);
                Disc(4.5f, 15, 1.5f);
                Disc(15.5f, 15, 1.5f);
                break;
            case "land": // 깃발
                Line(P(5, 18), P(5, 2.5f));
                Loop(P(5, 3), P(16, 5), P(5, 10.5f));
                break;
            case "coin":
                Ring(10, 10, 7);
                Line(P(10, 5.5f), P(10, 14.5f));
                Line(P(12.6f, 7.5f), P(8.2f, 7.5f), P(8.2f, 10), P(11.8f, 10), P(11.8f, 12.5f), P(7.4f, 12.5f));
                break;
            case "unrest": // 불길
                Loop(P(10, 2.5f), P(14.5f, 8), P(15, 13), P(10, 17.5f), P(5, 13), P(6, 8.5f), P(8, 10.5f));
                Line(P(10, 17.5f), P(8.5f, 14), P(10, 11), P(11.5f, 14), P(10, 17.5f));
                break;
            case "cohesion": // 이어진 고리
                Ring(7.3f, 10, 4.3f);
                Ring(12.7f, 10, 4.3f);
                break;
            case "food": // 이삭
                Line(P(10, 18), P(10, 4));
                for (int k = 0; k < 3; k++)
                {
                    float y = 6 + k * 3.5f;
                    Loop(P(10, y + 2), P(6, y), P(7, y - 2.5f));
                    Loop(P(10, y + 2), P(14, y), P(13, y - 2.5f));
                }
                break;
            case "info":
                Ring(10, 10, 7.5f);
                Line(P(10, 9), P(10, 14.5f));
                Disc(10, 6, 1.2f);
                break;
            case "sound":
                Fill(P(3, 7.5f), P(6.5f, 7.5f), P(11, 3.5f), P(11, 16.5f), P(6.5f, 12.5f), P(3, 12.5f));
                Arc(11, 10, 3.5f, -0.9f, 0.9f);
                Arc(11, 10, 6.5f, -0.9f, 0.9f);
                break;
            case "eye":
                Line(P(2, 10), P(6, 5.5f), P(10, 4.5f), P(14, 5.5f), P(18, 10), P(14, 14.5f), P(10, 15.5f), P(6, 14.5f), P(2, 10));
                Disc(10, 10, 2.6f);
                break;
            case "heart":
                Loop(P(10, 17), P(3, 10), P(3, 6), P(6.5f, 3.5f), P(10, 6.5f), P(13.5f, 3.5f), P(17, 6), P(17, 10));
                break;
            case "box": // 빈 체크 칸
                Loop(P(3.5f, 3.5f), P(16.5f, 3.5f), P(16.5f, 16.5f), P(3.5f, 16.5f));
                break;
            case "checked": // 체크한 칸
                Fill(P(3, 3), P(17, 3), P(17, 17), P(3, 17));
                c.DrawPolyline(new[] { P(6, 10.2f), P(9, 13.2f), P(14.5f, 6.8f) }, Colors.White, w * 1.1f, true);
                break;
            case "guide": // 말풍선
                Loop(P(3, 4), P(17, 4), P(17, 13), P(9, 13), P(5, 16.5f), P(5.5f, 13), P(3, 13));
                Disc(7, 8.5f, 1.1f);
                Disc(10, 8.5f, 1.1f);
                Disc(13, 8.5f, 1.1f);
                break;
            case "wood": // 통나무 둘
                Line(P(3, 13), P(13, 3));
                Fill(P(10.5f, 2), P(16.5f, 4), P(15.5f, 9), P(12, 6.5f));
                Loop(P(4, 13.5f), P(15, 13.5f), P(15, 18), P(4, 18));
                Ring(15, 15.75f, 2.2f);
                break;
            case "stone": // 돌 무더기
                Loop(P(2.5f, 17), P(4, 11), P(8.5f, 9.5f), P(11, 13), P(10, 17));
                Loop(P(9, 17), P(11, 8), P(15, 6), P(18, 11), P(17.5f, 17));
                break;
            case "gather": // 바구니
                Loop(P(3, 9), P(17, 9), P(15, 17), P(5, 17));
                Arc(10, 9, 5, Mathf.Pi, Mathf.Tau);
                Disc(8, 7.5f, 1.4f);
                Disc(11.5f, 7, 1.4f);
                break;
            case "fish":
                Loop(P(3, 10), P(7, 6), P(12, 6), P(15, 10), P(12, 14), P(7, 14));
                Fill(P(14.5f, 10), P(18.5f, 6.5f), P(18.5f, 13.5f));
                Disc(7, 9.3f, 0.9f);
                break;
            case "scout": // 망원
                Ring(8.5f, 8.5f, 5);
                Line(P(12, 12), P(17, 17));
                break;
            case "guard": // 방패
                Loop(P(10, 2.5f), P(16.5f, 5), P(16, 11), P(10, 17.5f), P(4, 11), P(3.5f, 5));
                Line(P(10, 5.5f), P(10, 14));
                break;
            case "steward": // 열쇠
                Ring(6.5f, 7, 3.5f);
                Line(P(9, 9.5f), P(17, 17.5f));
                Line(P(13, 13.5f), P(15, 11.5f));
                Line(P(15.5f, 16), P(17.5f, 14));
                break;
            case "farm": // 밭이랑
                Loop(P(3, 5), P(17, 5), P(17, 16), P(3, 16));
                Line(P(3, 8.7f), P(17, 8.7f));
                Line(P(3, 12.3f), P(17, 12.3f));
                break;
            default:
                Disc(10, 10, 3);
                break;
        }
    }
}

/// <summary>아이콘 하나만 그리는 작은 칸.</summary>
public partial class Glyph : Control
{
    public string Kind = "";
    public Color Col = Ui.Text;

    public Glyph() { }

    public Glyph(string kind, float size, Color? col = null)
    {
        Kind = kind;
        Col = col ?? Ui.Text;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public override void _Draw() => Ui.Icon(this, Kind, new Rect2(Vector2.Zero, Size), Col);
}

/// <summary>왼쪽에 아이콘을 그리는 단추. 글이 없으면 아이콘만 가운데에 그린다.</summary>
public partial class IconButton : Button
{
    static readonly Dictionary<int, Texture2D> BlankIcons = new();
    static Texture2D BlankIcon(int s)
    {
        if (!BlankIcons.TryGetValue(s, out var t))
            BlankIcons[s] = t = ImageTexture.CreateFromImage(Image.CreateEmpty(s, s, false, Image.Format.Rgba8));
        return t;
    }

    public string Kind = "";
    public float IconSize = 18;

    bool _hadText;

    public override void _Ready()
    {
        // 아이콘 자리를 비워 둔다(그림은 _Draw에서 벡터로 그린다).
        Icon = BlankIcon(Math.Max(1, (int)IconSize));
        ExpandIcon = false;
        AddThemeConstantOverride("h_separation", 7);
        Fit();
    }

    /// <summary>글이 생기거나 사라지면 아이콘 자리를 왼쪽/가운데로 옮긴다.</summary>
    void Fit()
    {
        _hadText = Text.Length > 0;
        IconAlignment = _hadText ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        Alignment = _hadText ? HorizontalAlignment.Left : HorizontalAlignment.Center;
    }

    public override void _Draw()
    {
        if (Text.Length > 0 != _hadText) Fit();
        if (Kind.Length == 0) return;
        bool on = ButtonPressed;
        Color col = Disabled ? GetThemeColor("font_disabled_color") : on ? GetThemeColor("font_pressed_color") : GetThemeColor("font_color");
        var sb = GetThemeStylebox(Disabled ? "disabled" : on ? "pressed" : IsHovered() ? "hover" : "normal");
        float x = Text.Length == 0 ? (Size.X - IconSize) / 2 : sb.GetMargin(Side.Left);
        Ui.Icon(this, Kind, new Rect2(x, (Size.Y - IconSize) / 2, IconSize, IconSize), col);
    }
}
