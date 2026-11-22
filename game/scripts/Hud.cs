using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>지방 건물 그림 하나를 단추·목록 옆에 그리는 작은 칸.</summary>
public partial class BuildingGlyph : Control
{
    public Building Kind;
    public bool Site;
    public float Done = 1f;

    public BuildingGlyph() { }
    public BuildingGlyph(Building k, bool site = false, float done = 1f, float size = 28)
    {
        Kind = k;
        Site = site;
        Done = done;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public override void _Draw()
    {
        float s = Math.Min(Size.X, Size.Y);
        var foot = new Vector2(Size.X / 2, Size.Y * 0.88f);
        if (Site) Art.Scaffold(this, foot, s / 36f, Done);
        else Art.BuildingIcon(this, Kind, foot, s / 38f);
    }
}

/// <summary>
/// 화면 위의 판: 위의 띠(날짜·빠르기·나라 형편), 왼쪽의 땅·군단·무리 패널, 오른쪽의 결정과 알림, 아래의 단추 줄.
/// 판은 뒤가 흐리게 비치는 유리(Ui.Glass)이고 단추는 아이콘이 붙은 알약이다. 모두 코드로 짓는다.
/// 창(법전·기술·나라·연대기·사람들·저장·설정)은 HudWindows.cs, HudPeople.cs.
/// </summary>
public partial class Hud : CanvasLayer
{
    public Main Game = null!;
    Simulation S => Game.S;

    Control _root = null!;
    Label _title = null!, _date = null!, _hint = null!;
    readonly Button[] _speed = new Button[5];
    // 올해 첫머리의 값. 칩 옆의 "올해 +3"을 셈한다.
    readonly Dictionary<string, double> _yearBase = new();
    int _baseYear = int.MinValue;
    Button _research = null!, _lawBtn = null!, _workBtn = null!, _buildBtn = null!;
    ProgressBar _researchBar = null!;
    Glyph _stageGlyph = null!;
    /// <summary>법전에서 이미 본(알린) 제도들: 새로 들일 수 있게 된 것만 배지로 알린다.</summary>
    readonly HashSet<string> _lawSeen = new();
    VBoxContainer _side = null!, _badges = null!, _toasts = null!;
    PanelContainer _sidePanel = null!, _banner = null!, _tools = null!;
    HBoxContainer _speedBox = null!;
    Label _bannerText = null!;
    Control _modalHost = null!;
    PanelContainer _modalPanel = null!;
    float _bannerLife;
    readonly List<(Control box, float life)> _toastLife = new();
    string _window = "", _logCat = "";
    int _logMin = 2;
    bool _overShown;

    public bool ModalOpen => _modalHost.Visible;

    static readonly string[] SpeedNames = { "일시정지", "1배속", "2배속", "3배속", "최고 속도" };
    static readonly string[] SpeedIcons = { "pause", "play1", "play2", "play3", "bolt" };
    static readonly string[] TerrainNames = { "바다", "평야", "숲", "구릉", "산", "초원" };

    static Color Ink => Ui.Text;
    static Color Faint(float a = 0.7f) => new(Ui.Text, Math.Min(1f, 0.35f + a * 0.65f));

    // ───────── 짓는 도우미 ─────────

    static Label L(string text, int size = 16, Color? col = null, bool wrap = false, float width = 0)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", Math.Max(12, size));
        if (col != null) l.AddThemeColorOverride("font_color", col.Value);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        if (width > 0) l.CustomMinimumSize = new Vector2(width, 0);
        return l;
    }

    /// <summary>단추. icon을 주면 왼쪽에 선 아이콘이 붙는다(글이 없으면 아이콘만).</summary>
    static Button B(string text, Action on, string tip = "", bool disabled = false, int size = 16, string icon = "")
    {
        Button b = icon.Length > 0 ? new IconButton { Kind = icon, IconSize = Math.Max(14, size + 2) } : new Button();
        b.Text = text;
        b.TooltipText = tip;
        b.Disabled = disabled;
        b.FocusMode = Control.FocusModeEnum.None;
        b.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        b.AddThemeFontSizeOverride("font_size", size);
        b.Pressed += () => Sfx.Play("click");
        b.Pressed += on;
        return b;
    }

    static VBoxContainer V(int sep = 6)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", sep);
        return v;
    }

    static HBoxContainer H(int sep = 8)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", sep);
        return h;
    }

    static void Clear(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            n.RemoveChild(c);
            c.QueueFree();
        }
    }

    static StyleBoxFlat Box(Color bg, Color border, int width = 2, int pad = 10, int radius = 10)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border, AntiAliasing = true };
        sb.SetBorderWidthAll(width);
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(pad);
        return sb;
    }

    /// <summary>제목 앞에 아이콘을 붙인 줄.</summary>
    static HBoxContainer Head(string icon, string text, int size = 18, Color? col = null)
    {
        var h = H(7);
        h.AddChild(new Glyph(icon, size, col ?? Ui.Accent));
        h.AddChild(L(text, size, col));
        return h;
    }

    /// <summary>0~1 막대(유리 위의 얇은 막대).</summary>
    static ProgressBar Meter(double v, float width, Color fill, float height = 10)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = Math.Clamp(v, 0, 1), ShowPercentage = false, CustomMinimumSize = new Vector2(width, height), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        bar.AddThemeStyleboxOverride("fill", Box(fill, new Color(fill, 0), 0, 0, 999));
        return bar;
    }

    static string Pct(double v) => $"{v * 100:0}%";

    Theme MakeTheme()
    {
        var font = new SystemFont { FontNames = new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Noto Sans KR" }, Antialiasing = TextServer.FontAntialiasing.Gray, SubpixelPositioning = TextServer.SubpixelPositioning.OneHalf };
        Art.Font = font;
        var t = new Theme { DefaultFont = font, DefaultFontSize = 17 };

        t.SetStylebox("panel", "PanelContainer", Ui.GlassBox());
        var tip = Box(Ui.Tint, new Color(Ui.Rim, 0.45f), 1, 10, 12);
        tip.ShadowColor = new Color(0, 0, 0, 0.28f);
        tip.ShadowSize = 8;
        t.SetStylebox("panel", "TooltipPanel", tip);
        t.SetColor("font_color", "TooltipLabel", Ink);
        t.SetFontSize("font_size", "TooltipLabel", 16);

        t.SetColor("font_color", "Label", Ink);
        t.SetColor("default_color", "RichTextLabel", Ink);

        // 알약 단추: 맑은 흰 유리, 위쪽 테가 빛을 받는다. 눌린 것은 쪽빛.
        t.SetStylebox("normal", "Button", Ui.Pill(Ui.Surface, new Color(Ui.Rim, 0.55f)));
        t.SetStylebox("hover", "Button", Ui.Pill(Ui.Surface.Lightened(0.12f), Ui.Accent));
        t.SetStylebox("pressed", "Button", Ui.Pill(new Color("326778"), Ui.Accent));
        t.SetStylebox("hover_pressed", "Button", Ui.Pill(Ui.Accent.Lightened(0.08f), Ui.Accent.Lightened(0.45f)));
        t.SetStylebox("disabled", "Button", Ui.Pill(new Color("1c3036"), new Color(Ui.Rim, 0.2f)));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", Ink);
        t.SetColor("font_hover_color", "Button", Ink);
        t.SetColor("font_pressed_color", "Button", Colors.White);
        t.SetColor("font_hover_pressed_color", "Button", Colors.White);
        t.SetColor("font_disabled_color", "Button", new Color(Ink, 0.38f));
        t.SetColor("font_focus_color", "Button", Ink);

        t.SetStylebox("background", "ProgressBar", Box(new Color(1, 1, 1, 0.10f), new Color(0, 0, 0, 0), 0, 0, 999));
        t.SetStylebox("fill", "ProgressBar", Box(Ui.Accent, new Color(0, 0, 0, 0), 0, 0, 999));
        t.SetColor("font_color", "ProgressBar", Ink);

        var sep = new StyleBoxLine { Color = new Color(Ui.Text, 0.14f), Thickness = 1 };
        t.SetStylebox("separator", "HSeparator", sep);
        t.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = new Color(Ui.Text, 0.14f), Thickness = 1, Vertical = true });

        // 끌개(설정 창)
        t.SetStylebox("slider", "HSlider", Box(new Color(1, 1, 1, 0.14f), new Color(0, 0, 0, 0), 0, 3, 999));
        t.SetStylebox("grabber_area", "HSlider", Box(Ui.Accent, new Color(0, 0, 0, 0), 0, 3, 999));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Ui.Accent.Lightened(0.1f), new Color(0, 0, 0, 0), 0, 3, 999));

        // 두루마리 막대
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", bar, Box(new Color(1, 1, 1, 0.06f), new Color(0, 0, 0, 0), 0, 3, 999));
            t.SetStylebox("grabber", bar, Box(new Color(Ui.Rim, 0.42f), new Color(0, 0, 0, 0), 0, 3, 999));
            t.SetStylebox("grabber_highlight", bar, Box(new Color(Ui.Rim, 0.68f), new Color(0, 0, 0, 0), 0, 3, 999));
            t.SetStylebox("grabber_pressed", bar, Box(Ui.Accent, new Color(0, 0, 0, 0), 0, 3, 999));
        }
        return t;
    }

    // ───────── 뼈대 ─────────

    public void Init()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = MakeTheme() };
        AddChild(_root);
        FitRoot();

        // 위의 띠: 유리 한 장
        var top = Ui.MakeGlass(new PanelContainer(), 0, 9);
        top.AddThemeStyleboxOverride("panel", TopBox());
        top.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _root.AddChild(top);
        var bar = H(10);
        top.AddChild(bar);
        var regimeBtn = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = "나라 현황·계층·발전 조건·승리 길 보기 (R)",
            CustomMinimumSize = new Vector2(188, 42),
        };
        var rNorm = Box(new Color("13262f", 0.92f), new Color(Ui.Rim, 0.55f), 1, 5, 8);
        rNorm.ContentMarginLeft = 10; rNorm.ContentMarginRight = 12;
        var rHov = Box(new Color("1b3541", 0.96f), Ui.Accent, 1, 5, 8);
        rHov.ContentMarginLeft = 10; rHov.ContentMarginRight = 12;
        regimeBtn.AddThemeStyleboxOverride("normal", rNorm);
        regimeBtn.AddThemeStyleboxOverride("hover", rHov);
        regimeBtn.AddThemeStyleboxOverride("pressed", rHov);
        regimeBtn.Pressed += () => { Sfx.Play("click"); Open("realm"); };
        var regimeRow = H(8);
        regimeRow.MouseFilter = Control.MouseFilterEnum.Ignore;
        regimeRow.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, 6);
        _stageGlyph = new Glyph("people", 22, Art.Gold);
        regimeRow.AddChild(_stageGlyph);
        _title = L("", 14);
        _title.MouseFilter = Control.MouseFilterEnum.Ignore;
        _title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _title.AddThemeConstantOverride("line_spacing", -2);
        regimeRow.AddChild(_title);
        regimeBtn.AddChild(regimeRow);
        bar.AddChild(regimeBtn);
        _date = L("", 15, new Color("e6bb72"));
        _date.AddThemeConstantOverride("line_spacing", -2);
        _date.CustomMinimumSize = new Vector2(146, 0);
        _date.MouseFilter = Control.MouseFilterEnum.Stop;
        bar.AddChild(_date);
        var speeds = _speedBox = H(4);
        bar.AddChild(speeds);
        for (int i = 0; i < 5; i++)
        {
            int k = i;
            _speed[i] = B("", () => Game.SetSpeed(k), $"{SpeedNames[i]} ({(i == 0 ? "스페이스" : $"숫자 {i}")})", false, 15, SpeedIcons[i]);
            _speed[i].ToggleMode = true;
            _speed[i].CustomMinimumSize = new Vector2(40, 34);
            speeds.AddChild(_speed[i]);
        }
        // 상태 막대: 영토·불만(나라) | 지도자 지지·단합. 쉬운 이름, 값, 색 막대, 올해 증감.
        bar.AddChild(new VSeparator());
        foreach (var (key, icon) in MeterDefs)
            bar.AddChild(StatusMeter(key, icon));
        bar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        // 자원 줄: 오른쪽 위, 실시간 전략 게임처럼 크게
        bar.AddChild(ResourceStrip());
        bar.AddChild(B("", () => Open("settings"), "설정 (F10)", false, 15, "gear"));

        // 왼쪽: 고른 땅이나 군단, 아무것도 고르지 않았으면 무리/국정
        var left = V();
        left.MouseFilter = Control.MouseFilterEnum.Ignore;
        left.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
        left.OffsetTop = 76;
        left.OffsetLeft = 12;
        left.OffsetRight = 12 + SideW + 32;
        left.OffsetBottom = -76;
        _root.AddChild(left);
        _sidePanel = Ui.MakeGlass(new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, Visible = false }, 14, 13);
        left.AddChild(_sidePanel);
        _side = V(6);
        _sidePanel.AddChild(_side);

        // 오른쪽: 결정과 알림
        var right = V(8);
        right.MouseFilter = Control.MouseFilterEnum.Ignore;
        right.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
        right.OffsetTop = 78;
        right.OffsetLeft = -402;
        right.OffsetRight = -12;
        right.OffsetBottom = -50;
        _root.AddChild(right);

        _decCards = V(8);
        _decCards.MouseFilter = Control.MouseFilterEnum.Ignore;
        right.AddChild(_decCards);
        _badges = V(6);
        _badges.MouseFilter = Control.MouseFilterEnum.Ignore;
        right.AddChild(_badges);
        _toasts = V(6);
        _toasts.MouseFilter = Control.MouseFilterEnum.Ignore;
        right.AddChild(_toasts);

        // 큰 알림
        var bannerHost = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bannerHost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        bannerHost.OffsetTop = 80;
        bannerHost.OffsetBottom = 180;
        _root.AddChild(bannerHost);
        _banner = Ui.MakeGlass(new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore }, 22, 18, 0.95f);
        var bs = Ui.GlassBox(22, 18, 0.96f);
        bs.BorderColor = new Color(Art.Bad, 0.8f);
        bs.SetBorderWidthAll(2);
        _banner.AddThemeStyleboxOverride("panel", bs);
        bannerHost.AddChild(_banner);
        _bannerText = L("", 21, null, true, 660);
        _bannerText.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.AddChild(_bannerText);

        // 아래: 창을 여는 단추 줄 (고정 폭으로 흔들리지 않게 유지)
        var tools = _tools = Ui.MakeGlass(new PanelContainer(), 14, 7);
        var toolBox = Ui.GlassBox(14, 7);
        toolBox.ContentMarginLeft = toolBox.ContentMarginRight = 12;
        tools.AddThemeStyleboxOverride("panel", toolBox);
        _root.AddChild(tools);
        var tb = H(6);
        tools.AddChild(tb);
        tb.AddChild(B("도성", () => { Game.Focus(S.Map.Capital); Game.Select(-1); }, "도성으로 이동하고 본거지 패널을 엽니다 (Home)", false, 15, "home"));
        _workBtn = B("일", () => ShowCamp("work"), "누가 무슨 일을 할지 정합니다 (J)", false, 15, "gather");
        tb.AddChild(_workBtn);
        _buildBtn = B("건설", () => ShowCamp("build"), "움집·창고·밭 같은 건물을 짓습니다 (B)", false, 15, "build");
        tb.AddChild(_buildBtn);
        tb.AddChild(new VSeparator());
        _lawBtn = B("법전", () => Open("law"), "제도 도입·폐지 (L)", false, 15, "law");
        _lawBtn.CustomMinimumSize = new Vector2(88, 36);
        tb.AddChild(_lawBtn);
        // 기술 단추: 고정 폭을 유지하고 아래쪽에 얇은 연구 진척 막대를 내장해 하단 바가 출렁이지 않게 한다.
        _research = B("기술", () => Open("tech"), "연구 선택 (T)", false, 15, "tech");
        _research.CustomMinimumSize = new Vector2(132, 36);
        _researchBar = Meter(0, 110, Ui.Accent, 3);
        _researchBar.MouseFilter = Control.MouseFilterEnum.Ignore;
        _researchBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        _researchBar.OffsetLeft = 10;
        _researchBar.OffsetRight = -10;
        _researchBar.OffsetTop = -5;
        _researchBar.OffsetBottom = -2;
        _research.AddChild(_researchBar);
        tb.AddChild(_research);
        tb.AddChild(B("나라", () => Open("realm"), "나라 현황·계층·승리 조건·이웃 나라 (R)", false, 15, "realm"));
        tb.AddChild(B("사람들", () => Open("people"), "백성 한 명 한 명 살펴보기 (P)", false, 15, "people"));
        tb.AddChild(B("연대기", () => Open("log"), "지금까지의 사건 기록 (C)", false, 15, "log"));
        tb.AddChild(new VSeparator());
        tb.AddChild(B("저장", () => Open("save"), "저장 (F5 빠른 저장)", false, 15, "save"));
        tb.AddChild(B("불러오기", () => Open("load"), "불러오기 (F9 빠른 불러오기)", false, 15, "load"));
        tb.AddChild(B("새 게임", AskNewGame, "새 게임을 시작합니다", false, 15, "new"));
        tools.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 14);
        tools.GrowVertical = Control.GrowDirection.Begin;
        tools.GrowHorizontal = Control.GrowDirection.Both;
        _hint = L(HintText, 13, new Color(Ui.Text, 0.85f));
        _hint.AddThemeStyleboxOverride("normal", Box(new Color(Ui.Tint, 0.90f), new Color(Ui.Rim, 0.4f), 1, 7, 8));
        _root.AddChild(_hint);
        _hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 14);
        _hint.OffsetBottom = -68;
        _hint.HorizontalAlignment = HorizontalAlignment.Right;
        _hint.GrowHorizontal = Control.GrowDirection.Begin;
        _hint.GrowVertical = Control.GrowDirection.Begin;

        BuildGuide();

        // 창과 물음: 뒤를 어둡게 한 번 덮고, 그 위를 다시 비춰 유리로 흐린다.
        _modalHost = new Control { Visible = false };
        _modalHost.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_modalHost);
        var dim = new ColorRect { Color = new Color(0.08f, 0.07f, 0.06f, 0.38f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _modalHost.AddChild(dim);
        _modalHost.AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _modalHost.AddChild(center);
        _modalPanel = Ui.MakeGlass(new PanelContainer(), 26, 22, 0.96f);
        center.AddChild(_modalPanel);
    }

    static StyleBoxFlat TopBox()
    {
        var sb = Ui.GlassBox(0, 8, 0.94f);
        sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 0;
        sb.ContentMarginLeft = sb.ContentMarginRight = 16;
        sb.BorderWidthTop = 0;
        return sb;
    }

    /// <summary>설정의 화면 글 크기에 맞춰 판 전체를 늘이거나 줄인다.</summary>
    void FitRoot()
    {
        float s = Prefs.UiScale;
        var view = GetViewport().GetVisibleRect().Size;
        _root.Scale = new Vector2(s, s);
        _root.Position = Vector2.Zero;
        _root.Size = view / s;
    }

    public void ResetGame()
    {
        Clear(_toasts);
        _toastLife.Clear();
        _banner.Visible = false;
        _overShown = false;
        _personId = -1;
        _peopleFilter = "all";
        _campTab = "work";
        _wasTribal = true;
        _techHighlight = "";
        _lawFilter = "all";
        _lawSel = "";
        _lawSeen.Clear();
        _decSig = "";
        Clear(_decCards);
        _decLeft.Clear();
        _resLast.Clear();
        _resFlash.Clear();
        _resDir.Clear();
        _seasonBase.Clear();
        _seasonRate.Clear();
        _rateTick = int.MinValue;
        _yearBase.Clear();
        _baseYear = int.MinValue;
        HideSideTip();
        CloseModal();
        ResetGuide();
        _badgeSig = "";
    }

    public override void _Process(double delta)
    {
        FadeResourceFlash((float)delta);
        var want = GetViewport().GetVisibleRect().Size / Prefs.UiScale;
        if (_root.Size != want || _root.Scale.X != Prefs.UiScale) FitRoot();

        // 미뤄 둔 옆 패널: 너무 자주 짓지 않고(0.3초), 마우스가 올라가 있으면 손을 뗄 때까지 기다린다.
        if (_sideDirty && Time.GetTicksMsec() - _sideAt > 300
            && !(_sidePanel.Visible && _sidePanel.GetGlobalRect().HasPoint(_sidePanel.GetGlobalMousePosition())))
            RefreshSide();

        if (Engine.GetProcessFrames() % 30 == 0) HintRefresh();

        float dt = Math.Min((float)delta, 0.1f); // 불러오기처럼 긴 한 틀에 알림이 한꺼번에 사라지지 않게
        for (int i = _toastLife.Count - 1; i >= 0; i--)
        {
            var (box, life) = _toastLife[i];
            life -= dt;
            if (life <= 0)
            {
                box.QueueFree();
                _toastLife.RemoveAt(i);
                continue;
            }
            box.Modulate = new Color(1, 1, 1, Math.Min(1, life / 1.2f));
            _toastLife[i] = (box, life);
        }
        if (_banner.Visible)
        {
            _bannerLife -= dt;
            _banner.Modulate = new Color(1, 1, 1, Math.Clamp(_bannerLife / 1.2f, 0, 1));
            if (_bannerLife <= 0) _banner.Visible = false;
        }
    }

    // ───────── 알림 ─────────

    public void Toast(string text, Color col)
    {
        var p = Ui.MakeGlass(new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore }, 14, 10, 0.94f);
        var sb = Ui.GlassBox(14, 10, 0.95f);
        sb.BorderColor = col;
        sb.BorderWidthLeft = 5;
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(L(text, 14, null, true, 352));
        _toasts.AddChild(p);
        _toastLife.Add((p, 10f));
        while (_toastLife.Count > 6)
        {
            _toastLife[0].box.QueueFree();
            _toastLife.RemoveAt(0);
        }
    }

    /// <summary>가운데에 크게 띄운다.</summary>
    public void Banner(string text, float life = 7)
    {
        _bannerText.Text = text;
        _bannerLife = life;
        _banner.Visible = true;
        _banner.Modulate = Colors.White;
    }

    static Color CategoryColor(string cat) => cat switch
    {
        "전쟁" => Art.Bad,
        "재앙" => new Color("df8a48"),
        "기술" => Ui.Accent,
        "살림" => Art.Good,
        "외교" => new Color("9d88d6"),
        "정치" => Art.Gold,
        _ => Ink,
    };

    /// <summary>연대기에 새 줄이 쌓였다. 가벼운 일은 연대기에만 남기고, 무거운 일만 알린다. 시간을 멈춰야 할 일이면 true.</summary>
    public bool OnLog(LogEntry e)
    {
        int w = Chronicle.Weight(e);
        if (w < 2) return false;
        string cat = Chronicle.Category(e);
        string tag = Chronicle.Tag(e.Text);
        if (tag == "승리" && S.Won.Count > 0)
        {
            ShowVictory(S.Won[^1].Course);
            return true;
        }
        if (w >= 3)
        {
            Banner(e.Text);
            Sfx.Play(tag switch { "침공" => "alarm", "몰락" or "함락" or "도성 약탈" or "혁명" => "fall", _ => "bell" });
        }
        else if (tag is "개척" or "준공") Sfx.Play("build", 0.7f);
        Toast(e.Text, CategoryColor(cat));
        return Prefs.PauseOnCrisis && w >= 3 && tag is "침공" or "함락" or "몰락" or "도성 약탈" or "혁명";
    }

    // ───────── 늘 보이는 것 ─────────

    /// <summary>날짜 글씨의 철 빛깔: 봄 연두, 여름 노랑, 가을 주황, 겨울 하늘.</summary>
    static readonly Color[] SeasonInk = { new("b6e08a"), new("f1d36b"), new("eea25a"), new("a9d4f0") };

    string _badgeSig = "";
    bool _sideDirty;
    ulong _sideAt;

    /// <summary>화면의 수치를 맞춘다. tick이면 시간이 흘러서 부르는 것이라 옆 패널은 조금 미뤄 그린다.</summary>
    public void Refresh(bool tick = false)
    {
        var r = S.Realm;
        string ruler = S.Chief is { Alive: true } c ? $"{S.RulerTitle} {S.P(c)}" : S.Tick == 0 && S.Living.Any() ? "첫 철에 우두머리 선출" : "통치자 없음";
        _title.Text = $"{S.StageName} · {S.EraName}\n{ruler}";
        int toWinter = SeasonsToWinter;
        _date.Text = $"제 {S.Year}년 · {Simulation.SeasonNames[S.Season]}\n" + (toWinter == 0 ? "겨울나기 — 창고로 버팀" : $"겨울까지 {toWinter}철");
        _date.AddThemeColorOverride("font_color", SeasonInk[S.Season]);
        _date.TooltipText = S.Tribal ? "겨울에는 들에서 거의 아무것도 나지 않습니다. 봄·여름·가을에 거둔 식량을 창고에 모아 두어야 겨울을 납니다." : "";
        for (int i = 0; i < 5; i++)
        {
            _speed[i].SetPressedNoSignal(Game.Speed == i);
            _speed[i].QueueRedraw();
        }

        int lands = S.Lands(0).Count();
        bool tribal = S.Tribal;
        _stageGlyph.Kind = tribal ? "people" : "realm";
        _stageGlyph.QueueRedraw();
        RefreshResources();
        RefreshMeters();
        RefreshDecisions();
        _workBtn.Visible = _buildBtn.Visible = S.Living.Any();

        var fresh = Catalog.All.Where(d => !d.Get(S.Inst) && !_lawSeen.Contains(d.Key) && S.WhyUnavailable(d).Length == 0).ToList();
        _lawBtn.Text = fresh.Count > 0 ? $"법전 +{fresh.Count}" : "법전";

        var tech = S.Research != null ? TechTree.Find(S.Research) : null;
        double techProg = tech != null ? Math.Clamp(S.ResearchProgress / Math.Max(0.001, S.CostOf(tech)), 0, 1) : 0;
        _research.Text = tech != null ? $"기술 · {techProg * 100:0}%"
            : S.AtCrossroad ? "기술 · 갈림길!" : TechTree.All.Any(S.CanResearch) ? "기술 · 선택!" : "기술";
        _research.TooltipText = tech != null ? $"연구 중: {tech.Name} ({S.ResearchProgress:0.#}/{S.CostOf(tech):0}) — 눌러서 기술 나무 열기 (T)" : "연구 선택 (T)";
        _researchBar.Visible = tech != null;
        if (tech != null) _researchBar.Value = techProg;

        // 배지는 내용이 바뀔 때만 다시 짓는다(철마다 지으면 누르려던 단추가 손 밑에서 사라진다).
        var badges = new List<(string title, string sub, Action act, string tip, string icon, Color accent)>();
        if (tech == null && TechTree.All.Any(S.CanResearch))
            badges.Add((S.AtCrossroad ? "기술의 갈림길 도착" : "진행 중인 연구가 없습니다",
                S.AtCrossroad ? "정복·교역·공포·전제 중 나아갈 길을 선택하세요" : "기술 나무에서 다음 연구 과제를 고르세요",
                () => Open("tech"), "단축키 T로 언제든 열 수 있습니다.", "tech", Ui.Accent));
        if (fresh.Count > 0)
        {
            string names = string.Join(", ", fresh.Take(3).Select(d => d.Name)) + (fresh.Count > 3 ? $" 외 {fresh.Count - 3}개" : "");
            var firstKey = fresh[0].Key;
            badges.Add(($"새로 들일 수 있는 제도 {fresh.Count}개", names, () => OpenLaw(firstKey),
                "제도는 원할 때 법전(L)에서 도입하거나 폐지합니다. 얻는 것과 대가를 비교해 보세요.", "law", LawCanRim));
        }
        if (S.Armies.Where(a => a.Owner != 0 && a.Muster > 0 && a.Goal >= 0 && S.Map[a.Goal].Owner == 0).OrderBy(a => a.Id).FirstOrDefault() is { } foe)
            badges.Add(($"적 침공 경보 · {foe.Muster}계절 뒤 진격", $"{foe.Name} {Art.Num(foe.Men)}명 ({S.Map[foe.At].Name} → {S.Map[foe.Goal].Name})",
                () => { Game.Focus(foe.Goal); Game.Select(foe.Goal); }, "공격받을 땅으로 군단을 보내거나 화친 사절을 보내 막으세요.", "raid", Art.Bad));
        string badgeSig = string.Join("\n", badges.Select(b => b.title + "|" + b.sub));
        if (badgeSig != _badgeSig)
        {
            _badgeSig = badgeSig;
            Clear(_badges);
            foreach (var (title, sub, act, tip, icon, accent) in badges)
                _badges.AddChild(AlertCard(icon, title, sub, accent, act, tip));
        }

        // 옆 패널: 시간이 흘러 다시 그릴 때는 모아 두었다가 _Process에서 그린다(마우스가 올라가 있으면 기다린다).
        if (tick) _sideDirty = true;
        else RefreshSide();
        GuideCheck();

        if (!S.Living.Any() && !_overShown)
        {
            _overShown = true;
            var v = V(12);
            v.AddChild(L("아무도 남지 않았다", 26));
            v.AddChild(L($"제 {S.Year}년, 마지막 사람이 죽었다. 이 땅의 이야기는 여기서 끝난다.", 16, null, true, 460));
            var row = H();
            row.AddChild(B("연대기 보기", () => Open("log"), "", false, 16, "log"));
            row.AddChild(B("불러오기", () => Open("load"), "", false, 16, "load"));
            row.AddChild(B("새 게임", AskNewGame, "", false, 16, "new"));
            v.AddChild(row);
            ShowModal(v, "");
        }
    }

    /// <summary>
    /// 조작 안내: 처음 몇 분과 안내가 열려 있을 때만 크게 보이고, 그 뒤로는 작게 접혀 마우스를 올리면 펼친다
    /// (문명·스텔라리스처럼 화면에 상시 조작법을 깔아 두지 않는다). 짓는 중에는 짓는 법을 보인다.
    /// </summary>
    void HintRefresh()
    {
        bool fresh = Time.GetTicksMsec() < 45_000 || GuideOpen;
        string text = Game.Placing is { } pk ? $"{StructureCatalog.Of(pk).Name} 짓기: 초록 칸을 좌클릭\n우클릭·Esc 그만두기 · 드래그 이동"
            : fresh ? HintText : HintFolded;
        if (_hint.Text == text) return;
        _hint.Text = text;
        bool folded = text == HintFolded;
        _hint.TooltipText = folded ? HintText : "";
        _hint.MouseFilter = folded ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        _hint.Modulate = new Color(1, 1, 1, folded ? 0.78f : 1);
    }

    const string HintFolded = "조작법 ?";

    const string HintText = "좌클릭 선택 · 드래그·WASD 이동 · 휠 확대·축소\n우클릭 군단 이동 · Space 멈춤 · Esc 설정";

    void RefreshSide()
    {
        HintRefresh();
        _sideDirty = false;
        _sideAt = Time.GetTicksMsec();
        HideSideTip();
        Clear(_side);
        if (Game.SelectedArmy is { } a && S.Armies.Contains(a)) ArmyPanel(a);
        else if (Game.Selected >= 0) LandPanel(S.Map[Game.Selected]);
        else if (S.Living.Any()) CampPanel();
        else
        {
            _sidePanel.Visible = false;
            return;
        }
        _sidePanel.Visible = true;
        MoveTipsAside(_side);
    }

    // ───────── 옆 패널의 설명: 마우스 옆이 아니라 패널 오른쪽에 ─────────

    PanelContainer? _sideTip;
    Label _sideTipText = null!;
    Control? _sideTipFor;

    /// <summary>
    /// 왼쪽 패널 안의 설명(TooltipText)을 보통 툴팁 대신 패널 바로 오른쪽에 띄운다.
    /// 보통 툴팁은 마우스 오른쪽 아래에 떠서 바로 아래 줄(일 목록의 채집·사냥…)을 가린다.
    /// </summary>
    void MoveTipsAside(Node n)
    {
        foreach (var child in n.GetChildren())
        {
            if (child is Control c && c.TooltipText.Length > 0 && c.MouseFilter != Control.MouseFilterEnum.Ignore)
            {
                string tip = c.TooltipText;
                c.TooltipText = "";
                c.MouseEntered += () => ShowSideTip(c, tip);
                c.MouseExited += () => { if (_sideTipFor == c) HideSideTip(); };
            }
            MoveTipsAside(child);
        }
    }

    void ShowSideTip(Control c, string text)
    {
        if (!IsInstanceValid(c) || !c.IsVisibleInTree()) return;
        if (_sideTip == null)
        {
            _sideTip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, ZIndex = 50 };
            var tb = Box(Ui.Tint, new Color(Ui.Rim, 0.45f), 1, 10, 12);
            tb.ShadowColor = new Color(0, 0, 0, 0.28f);
            tb.ShadowSize = 8;
            _sideTip.AddThemeStyleboxOverride("panel", tb);
            _sideTipText = L("", 15, Ink, true, 360);
            _sideTipText.MouseFilter = Control.MouseFilterEnum.Ignore;
            _sideTip.AddChild(_sideTipText);
            _root.AddChild(_sideTip);
        }
        _sideTipFor = c;
        _sideTipText.Text = text;
        _sideTip.Visible = true;
        _sideTip.Size = Vector2.Zero; // 글에 맞춰 다시 잰다
        var inv = _root.GetGlobalTransform().AffineInverse();
        var panel = inv * _sidePanel.GetGlobalRect().End;
        var top = (inv * c.GetGlobalRect().Position).Y;
        Callable.From(() =>
        {
            if (_sideTip == null || !_sideTip.Visible) return;
            float h = _sideTip.GetCombinedMinimumSize().Y;
            _sideTip.Position = new Vector2(panel.X + 10, Mathf.Clamp(top - 6, 70, Math.Max(70, _root.Size.Y - h - 12)));
        }).CallDeferred();
        _sideTip.Position = new Vector2(panel.X + 10, Math.Max(70, top - 6));
    }

    void HideSideTip()
    {
        _sideTipFor = null;
        if (_sideTip != null) _sideTip.Visible = false;
    }

    /// <summary>나라가 서기 전: 우두머리의 손으로 무리를 움직인다(사냥·잔치·선물·습격).</summary>
    void TribePanel()
    {
        var chief = S.Chief is { Alive: true } c ? c : null;
        _side.AddChild(Head("people", "무리", 21));
        int folk = S.Living.Count();
        _side.AddChild(L($"{(chief != null ? $"우두머리 {S.P(chief)}" : S.Tick == 0 ? "첫 계절이 지나면 우두머리가 정해집니다" : "우두머리 없음")} · 인구 {folk}명 · 공동 창고 식량 {S.CommonStore:0}", 14, Faint(0.8f), true, SideW));
        string next = S.NextStageNeeds();
        if (next.Length > 0) _side.AddChild(L($"다음 단계 조건: {next}", 13, Faint(0.65f), true, SideW));
        _side.AddChild(new HSeparator());
        _side.AddChild(L("한 계절에 한 가지 행동만 할 수 있습니다.", 13, Faint(0.65f)));

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var def in TribeActions.All.Where(d => !d.NeedsNeighbor))
        {
            string why = S.WhyCantTribe(def.Key);
            if (why == "우두머리 없음" && S.Tick == 0) why = "첫 계절부터 가능";
            string key = def.Key;
            var b = B(def.Name, () => DoTribe(key, -1), $"{def.Name}\n효과: {def.Gain}\n대가: {def.Cost}" + (why.Length > 0 ? $"\n\n지금은 할 수 없음: {why}" : ""),
                why.Length > 0, 16, def.Key);
            b.CustomMinimumSize = new Vector2(140, 36);
            // 크루세이더 킹즈의 결정 창처럼: 막힌 행동은 왜 막혔는지를 단추 바로 밑에 적는다(마우스를 올리지 않아도).
            var cell = V(1);
            cell.AddChild(b);
            if (why.Length > 0)
            {
                var wl = L(why, 11, Faint(0.5f), true, 140);
                wl.MaxLinesVisible = 2;
                wl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                cell.AddChild(wl);
            }
            grid.AddChild(cell);
        }
        _side.AddChild(grid);

        if (S.Neighbors.Count == 0) return;
        _side.AddChild(new HSeparator());
        _side.AddChild(Head("land", "이웃 무리", 16));
        var gift = TribeActions.Find("gift")!;
        var raid = TribeActions.Find("raid")!;
        for (int i = 0; i < S.Neighbors.Count; i++)
        {
            var n = S.Neighbors[i];
            int k = i;
            var box = V(3);
            var top = H(6);
            string flags = (n.Allied ? " · 혼인 동맹" : "") + (n.PeaceUntil > S.Year ? $" · 화친 {n.PeaceUntil - S.Year}년 남음" : "");
            top.AddChild(L($"{n.Name}", 15));
            top.AddChild(L($"전사 {n.Fighters:0}{flags}", 13, Faint(0.65f)));
            box.AddChild(top);
            var row = H(6);
            double hostile = Math.Clamp(n.Hostility / 2.5, 0, 1);
            row.AddChild(L("적대감", 13, Faint(0.65f)));
            row.AddChild(Meter(hostile, 92, hostile > 0.6 ? Art.Bad : Art.Gold));
            string wg = S.WhyCantTribe("gift", i), wr = S.WhyCantTribe("raid", i);
            var gb = B("", () => DoTribe("gift", k), $"{gift.Name} — {n.Name}\n효과: {gift.Gain}\n대가: {gift.Cost}" + (wg.Length > 0 ? $"\n\n지금은 할 수 없음: {wg}" : ""), wg.Length > 0, 14, "gift");
            gb.Text = gift.Name;
            var rb = B("", () => DoTribe("raid", k), $"{raid.Name} — {n.Name}\n효과: {raid.Gain}\n대가: {raid.Cost}" + (wr.Length > 0 ? $"\n\n지금은 할 수 없음: {wr}" : ""), wr.Length > 0, 14, "raid");
            rb.Text = raid.Name;
            row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            row.AddChild(gb);
            row.AddChild(rb);
            box.AddChild(row);
            _side.AddChild(box);
        }
    }

    void DoTribe(string key, int neighbor)
    {
        string why = S.WhyCantTribe(key, neighbor);
        if (S.TribeAct(key, neighbor)) Sfx.Play(key);
        else Toast(why.Length > 0 ? why : "지금은 할 수 없습니다.", Art.Bad);
        Game.Touched();
    }

    void ArmyPanel(Army a)
    {
        _side.AddChild(Head("army", a.Name, 21));
        var row0 = H(8);
        row0.AddChild(L($"병력 {Art.Num(a.Men)} / {Art.Num(a.FullMen)}", 15));
        row0.AddChild(Meter(a.FullMen > 0 ? a.Men / a.FullMen : 0, 110, Ui.Accent));
        _side.AddChild(row0);
        var row1 = H(8);
        row1.AddChild(L($"사기 {Pct(a.Morale)}", 15));
        row1.AddChild(Meter(a.Morale, 110, a.Morale < 0.5 ? Art.Bad : Art.Good));
        _side.AddChild(row1);
        _side.AddChild(L($"위치: {S.Map[a.At].Name}" + (a.Goal >= 0 ? $"  →  목표: {S.Map[a.Goal].Name}" : "  · 대기 중"), 15));

        // 둘레 땅의 승산
        var near = S.Map[a.At].Adjacent.Select(id => S.Map[id]).Where(p => p.Owner != 0).OrderBy(p => p.Id).ToList();
        if (near.Count > 0)
        {
            _side.AddChild(new HSeparator());
            _side.AddChild(Head("raid", "인접 지역 승률", 15));
            foreach (var p in near.Take(6))
            {
                double odds = S.AttackOdds(a.Id, p.Id);
                var row = H(8);
                string who = p.Owner < 0 ? "주인 없음" : S.Rivals.FirstOrDefault(v => v.Id == p.Owner)?.Name ?? "";
                row.AddChild(L($"{p.Name}", 14, null, false, 96));
                row.AddChild(L(who, 13, Faint(0.6f), false, 96));
                row.AddChild(Meter(odds, 70, odds >= 0.6 ? Art.Good : odds >= 0.4 ? Art.Gold : Art.Bad));
                row.AddChild(L(p.Owner < 0 ? "전투 없음" : Pct(odds), 14, odds < 0.4 ? Art.Bad : null));
                _side.AddChild(row);
            }
        }
        var foes = S.Armies.Where(x => x.Owner != 0).OrderBy(x => S.Map.Distance(S.Map[x.At], S.Map[a.At])).ThenBy(x => x.Id).Take(3).ToList();
        foreach (var f in foes)
        {
            string when = f.Muster > 0 ? $"{f.Muster}계절 뒤 출발" : f.Goal >= 0 ? $"{Josa.Ro(S.Map[f.Goal].Name)} 진격 중" : "대기 중";
            _side.AddChild(L($"적 {f.Name} {Art.Num(f.Men)}명 · {S.Map[f.At].Name} · {when}", 13, Art.Bad, true, SideW));
        }

        _side.AddChild(L("지도를 우클릭해 목적지를 정합니다. 한 계절에 한 지역씩 이동합니다. 다른 나라 땅에 들어가면 전투가 벌어지고, 이기면 그 땅을 어떻게 할지 정합니다. 주인 없는 땅은 싸우지 않고 차지합니다.",
            13, Faint(0.7f), true, SideW));
        var row2 = H();
        row2.AddChild(B("정지", () => { S.OrderArmy(a.Id, a.At); Game.Touched(); }, "", a.Goal < 0, 15, "stop"));
        row2.AddChild(B("해산", () => { S.DisbandArmy(a.Id); Game.SelectedArmy = null; Game.Touched(); }, "병사들이 농사일로 돌아갑니다.", false, 15, "disband"));
        _side.AddChild(row2);
    }

    Control AlertCard(string icon, string title, string sub, Color accent, Action act, string tip)
    {
        var b = new Button
        {
            TooltipText = tip,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(386, 56),
        };
        foreach (var (state, lift) in new[] { ("normal", 0f), ("hover", 0.08f), ("pressed", 0.12f) })
        {
            var sb = Box(new Color("10222a", 0.96f).Lightened(lift), new Color(accent, state == "hover" ? 0.95f : 0.65f), 1, 8, 10);
            sb.BorderWidthLeft = 5;
            sb.ContentMarginLeft = 12;
            sb.ContentMarginRight = 12;
            sb.ShadowColor = new Color(0, 0, 0, 0.3f);
            sb.ShadowSize = 6;
            b.AddThemeStyleboxOverride(state, sb);
        }
        b.Pressed += () => Sfx.Play("click");
        b.Pressed += act;
        var row = H(10);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, 8);
        row.AddChild(new Glyph(icon, 24, accent));
        var mid = V(1);
        mid.MouseFilter = Control.MouseFilterEnum.Ignore;
        mid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mid.Alignment = BoxContainer.AlignmentMode.Center;
        var tl = L(title, 15, Ink);
        tl.MouseFilter = Control.MouseFilterEnum.Ignore;
        tl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var sl = L(sub, 12, Faint(0.75f));
        sl.MouseFilter = Control.MouseFilterEnum.Ignore;
        sl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        mid.AddChild(tl);
        mid.AddChild(sl);
        row.AddChild(mid);
        var go = L("열기 ›", 13, accent);
        go.MouseFilter = Control.MouseFilterEnum.Ignore;
        go.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(go);
        b.AddChild(row);
        return b;
    }

    public void DoEnvoy(int rivalId, string kind, Action? after = null)
    {
        string why = S.WhyCantEnvoy(rivalId, kind);
        if (S.SendEnvoy(rivalId, kind)) Sfx.Play(kind == "trade" ? "coin" : "gift");
        else Toast(why.Length > 0 ? why : "지금은 사절을 보낼 수 없습니다.", Art.Bad);
        Game.Touched();
        after?.Invoke();
    }

    public HBoxContainer RivalEnvoyRow(Rival v, Action? after = null)
    {
        var row = H(5);
        double truceCost = S.Inst.Parley ? 0.35 : 0.6;
        void Env(string kind, string label, string icon, string gain, string cost)
        {
            string why = S.WhyCantEnvoy(v.Id, kind);
            string tip = $"{v.Name} — {label}\n＋ 얻는 것: {gain}\n− 치르는 값: {cost}" + (why.Length > 0 ? $"\n\n지금 불가: {why}" : "");
            var bt = B(label, () => DoEnvoy(v.Id, kind, after), tip, why.Length > 0, 13, icon);
            bt.AddThemeStyleboxOverride("normal", Ui.Pill(Ui.Surface, new Color(Ui.Rim, 0.55f), 6, 8, 8));
            bt.AddThemeStyleboxOverride("hover", Ui.Pill(Ui.Surface.Lightened(0.12f), Ui.Accent, 6, 8, 8));
            bt.AddThemeStyleboxOverride("disabled", Ui.Pill(new Color("16272d"), new Color(Ui.Rim, 0.18f), 6, 8, 8));
            row.AddChild(bt);
        }
        Env("trade", v.Trade ? "교역 중" : "교역 0.4", "coin", "해마다 세입 +0.08 및 앞선 기술 전파", "국고 0.4 · 낯선 신앙·역병 유입 가능");
        Env("truce", v.TreatyUntil > S.Year ? $"화친 {v.TreatyUntil - S.Year}년" : $"화친 {truceCost:0.##}", "gift",
            $"{(S.Inst.Parley ? 12 : 8)}년 평화 · 집결 중인 적 침공군 철수 · 적대감 완화", $"국고 {truceCost:0.##} · 호전적 귀족 불만 상승");
        Env("ally", v.Allied ? "동맹 중" : "국혼 0.5", "heart", "상호 방어 동맹 · 적대감 크게 하락", "국고 0.5 · 지배 가문 미혼 여성 시집 · 사돈 전쟁 참전 위험");
        return row;
    }

    void LandPanel(Province p)
    {
        var rival = p.Owner > 0 ? S.Rivals.FirstOrDefault(v => v.Id == p.Owner) : null;
        string owner = p.Owner == 0 ? "우리 땅" : rival != null ? $"{rival.Name}" : "주인 없는 땅";
        var head = H(8);
        var ownerCol = p.Owner == 0 ? Ui.Accent : p.Owner > 0 ? Art.OwnerColor(p.Owner).Lightened(0.42f) : Faint(0.65f);
        head.AddChild(new Glyph(p.Id == S.Map.Capital ? "home" : "land", 21, ownerCol));
        var pName = L(p.Name, 20);
        pName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(pName);
        head.AddChild(Tag(owner + (p.Id == S.Map.Capital ? " · 도성" : ""), new Color(ownerCol, 0.16f), new Color(ownerCol, 0.7f)));
        _side.AddChild(head);

        string info = $"{TerrainNames[(int)p.Terrain]} · 비옥도 {p.Fertility:0.00}" + (p.River ? " · 강 유역" : "") + (p.Coastal ? " · 해안" : "");
        if (p.Owner == 0 && !S.Tribal) info += $" · 백성 {Art.Num(S.LandPops()[p.Id])}명";
        _side.AddChild(L(info, 13, Faint(0.8f), true, SideW));
        if (p.Id == S.Map.Capital && S.Living.Any())
        {
            var capBtn = B("본거지(국정 · 일 · 건설) 패널로 돌아가기", () => Game.Select(-1), "선택을 해제하고 도성 운영 패널을 엽니다 (Esc)", false, 13, "home");
            capBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _side.AddChild(capBtn);
        }
        if (S.Revealed(p.Deposit))
        {
            var resRow = H(6);
            resRow.AddChild(new Glyph(Art.ResourceIcon(p.Deposit), 20, Colors.White));
            resRow.AddChild(L($"매장 자원: {ResourceCatalog.Name(p.Deposit)}", 14, Art.Gold));
            resRow.TooltipText = ResourceCatalog.Of(p.Deposit).Use;
            resRow.MouseFilter = Control.MouseFilterEnum.Stop;
            _side.AddChild(resRow);
        }

        if (rival != null)
        {
            _side.AddChild(new HSeparator());
            _side.AddChild(Head("realm", $"{rival.Name} 외교 · 군사", 15));
            var rStat = H(8);
            rStat.AddChild(L($"국력 {rival.Strength:0.00}", 14));
            rStat.AddChild(L("적대감", 13, Faint(0.7f)));
            rStat.AddChild(Meter(rival.Hostility, 88, rival.Hostility > 0.6 ? Art.Bad : Art.Gold, 8));
            rStat.AddChild(L(Pct(rival.Hostility), 13, rival.Hostility > 0.6 ? Art.Bad : Ink));
            _side.AddChild(rStat);

            _side.AddChild(RivalEnvoyRow(rival));

            _side.AddChild(new HSeparator());
            var bestArmy = S.Armies.Where(x => x.Owner == 0)
                .OrderBy(x => x.Goal < 0 ? 0 : 1).ThenBy(x => S.Map.Distance(S.Map[x.At], p)).ThenBy(x => x.Id).FirstOrDefault();
            if (bestArmy != null)
            {
                double odds = S.AttackOdds(bestArmy.Id, p.Id);
                bool canRoute = S.Route(bestArmy.At, p.Id).Count > 0;
                var atkBtn = B($"{bestArmy.Name} 출정 · 예상 승률 {Pct(odds)}", () =>
                {
                    if (S.OrderArmy(bestArmy.Id, p.Id))
                    {
                        Game.SelectArmy(bestArmy);
                        Game.Actors.Float(TerrainLayer.Center(p), "출정", Art.Bad);
                    }
                    else Toast("그곳으로 가는 육로가 없습니다.", Art.Bad);
                    Game.Touched();
                }, "이 지역으로 군단을 진격시켜 전쟁을 벌입니다. 화친이나 교역 중이면 즉시 파기됩니다.", !canRoute, 14, "raid");
                atkBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _side.AddChild(atkBtn);
            }
            else if (!S.Tribal)
            {
                string whyRaise = S.WhyCantRaise(S.Map.Capital);
                var raiseBtn = B("도성에서 군단 징집 (국고 0.5)", () =>
                {
                    if (S.RaiseArmy(S.Map.Capital) is { } na) { Game.SelectedArmy = na; Sfx.Play("hunt"); }
                    else Toast(whyRaise, Art.Bad);
                    Game.Touched();
                }, whyRaise.Length > 0 ? $"지금 불가: {whyRaise}" : "도성에서 군단을 일으켜 정복·방어에 나섭니다.", whyRaise.Length > 0, 14, "army");
                raiseBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _side.AddChild(raiseBtn);
            }
            return;
        }
        if (p.Owner < 0)
        {
            _side.AddChild(new HSeparator());
            if (S.Tribal)
            {
                _side.AddChild(L("도시국가 단계가 되면 군단을 일으켜 빈 땅을 개척할 수 있습니다.", 13, Faint(0.75f), true, SideW));
                return;
            }
            bool adj = p.Adjacent.Any(id => S.Map[id].Owner == 0);
            _side.AddChild(L(adj
                ? $"우리 영토와 맞닿은 빈 땅입니다. 군단이 도착하면 국고 0.3으로 싸움 없이 개척합니다 (현재 영토 {S.Lands(0).Count()}/{S.AdminCap})."
                : "먼저 인접한 땅부터 차례로 개척해야 닿을 수 있습니다.", 13, Faint(0.78f), true, SideW));
            var settler = S.Armies.Where(x => x.Owner == 0)
                .OrderBy(x => x.Goal < 0 ? 0 : 1).ThenBy(x => S.Map.Distance(S.Map[x.At], p)).ThenBy(x => x.Id).FirstOrDefault();
            if (settler != null)
            {
                bool canGo = adj && S.Route(settler.At, p.Id).Count > 0 && S.Realm.Treasury >= 0.3;
                string whyNo = !adj ? "우리 영토와 맞닿아 있어야 개척 가능" : S.Realm.Treasury < 0.3 ? "개척 비용 부족 (국고 0.3 필요)" : "";
                var setBtn = B($"{settler.Name} 보내 개척 (국고 0.3)", () =>
                {
                    if (S.OrderArmy(settler.Id, p.Id))
                    {
                        Game.SelectArmy(settler);
                        Game.Actors.Float(TerrainLayer.Center(p), "개척 이동", Art.Good);
                    }
                    Game.Touched();
                }, whyNo.Length > 0 ? whyNo : "군단을 보내 이 지역을 우리 영토로 편입합니다.", !canGo, 14, "land");
                setBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _side.AddChild(setBtn);
            }
            else
            {
                string whyRaise = S.WhyCantRaise(S.Map.Capital);
                var raiseBtn = B("개척용 군단 징집 (도성 · 국고 0.5)", () =>
                {
                    if (S.RaiseArmy(S.Map.Capital) is { } na) { Game.SelectedArmy = na; Sfx.Play("hunt"); }
                    Game.Touched();
                }, whyRaise.Length > 0 ? $"지금 불가: {whyRaise}" : "도성에서 군단을 일으킨 뒤 빈 땅으로 보냅니다.", whyRaise.Length > 0, 14, "army");
                raiseBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _side.AddChild(raiseBtn);
            }
            return;
        }

        if (p.Buildings.Count > 0 || (p.Road && p.Id != S.Map.Capital))
        {
            var fac = new HFlowContainer { CustomMinimumSize = new Vector2(SideW, 0) };
            fac.AddThemeConstantOverride("h_separation", 6);
            fac.AddThemeConstantOverride("v_separation", 4);
            foreach (var bk in p.Buildings)
            {
                var bd = BuildingCatalog.Of(bk);
                var chip = new PanelContainer { TooltipText = $"{bd.Name}\n효과: {bd.Gain}\n대가: {bd.Price}\n연간 유지비 {bd.Upkeep:0.00}", MouseFilter = Control.MouseFilterEnum.Stop };
                var csb = Box(new Color(Ui.Surface, 0.85f), new Color(Art.Good, 0.55f), 1, 3, 6);
                csb.ContentMarginLeft = 6; csb.ContentMarginRight = 8;
                chip.AddThemeStyleboxOverride("panel", csb);
                var ch = H(4);
                ch.MouseFilter = Control.MouseFilterEnum.Ignore;
                ch.AddChild(new BuildingGlyph(bk, size: 22));
                var cl = L(bd.Name, 13);
                cl.MouseFilter = Control.MouseFilterEnum.Ignore;
                ch.AddChild(cl);
                chip.AddChild(ch);
                fac.AddChild(chip);
            }
            if (p.Road && p.Id != S.Map.Capital)
            {
                var chip = new PanelContainer { TooltipText = "도성과 이어진 도로 — 군단 이동이 2배 빨라지고 3곳마다 통치 한도 +1.", MouseFilter = Control.MouseFilterEnum.Stop };
                var csb = Box(new Color(Ui.Surface, 0.85f), new Color(Ui.Rim, 0.55f), 1, 3, 6);
                csb.ContentMarginLeft = 6; csb.ContentMarginRight = 8;
                chip.AddThemeStyleboxOverride("panel", csb);
                var ch = H(4);
                ch.MouseFilter = Control.MouseFilterEnum.Ignore;
                ch.AddChild(new Glyph("road", 18, Colors.White));
                var cl = L("도로 연결됨", 13);
                cl.MouseFilter = Control.MouseFilterEnum.Ignore;
                ch.AddChild(cl);
                chip.AddChild(ch);
                fac.AddChild(chip);
            }
            _side.AddChild(fac);
        }
        foreach (var site in S.Sites.Where(x => x.Province == p.Id).ToList())
        {
            float done = (site.Total - site.SeasonsLeft) / (float)Math.Max(1, site.Total);
            var srow = H(6);
            if (site.Road) srow.AddChild(new Glyph("road", 22, Colors.White));
            else srow.AddChild(new BuildingGlyph(site.Kind, true, done, 24));
            srow.AddChild(L($"공사 중: {(site.Road ? "도로" : BuildingCatalog.Of(site.Kind).Name)} ({site.SeasonsLeft}계절 남음)", 14));
            _side.AddChild(srow);
            _side.AddChild(Meter(done, SideW, Ui.Accent, 8));
        }

        _side.AddChild(new HSeparator());
        var rowAct = H(6);
        if (!p.Road && p.Id != S.Map.Capital)
        {
            string why = S.WhyCantRoad(p.Id);
            var rb = B($"도로 {S.RoadCost(p):0.0}", () => { if (!S.BuildRoad(p.Id)) Toast(S.WhyCantRoad(p.Id), Art.Bad); else Sfx.Play("build"); Game.Touched(); },
                "도성과 이어지는 도로를 닦습니다. 도로 연결 3곳마다 통치 한도 +1, 군단 이동 속도 2배." + (why.Length > 0 ? $"\n\n지금 불가: {why}" : ""), why.Length > 0, 14, "road");
            rb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            rowAct.AddChild(rb);
        }
        string whyArmy = S.WhyCantRaise(p.Id);
        var ab = B("군단 징집 0.5", () =>
            {
                var a = S.RaiseArmy(p.Id);
                if (a == null) Toast(S.WhyCantRaise(p.Id), Art.Bad);
                else { Game.SelectedArmy = a; Sfx.Play("hunt"); }
                Game.Touched();
            },
            $"군단을 일으킵니다 ({Art.Num(S.ArmySize())}명 징집, 현재 군단 {S.Armies.Count(x => x.Owner == 0)}/{S.ArmyCap})." + (whyArmy.Length > 0 ? $"\n\n지금 불가: {whyArmy}" : ""),
            whyArmy.Length > 0, 14, "army");
        ab.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rowAct.AddChild(ab);
        _side.AddChild(rowAct);

        _side.AddChild(Head("build", "지방 건물 건설", 15));
        if (S.Realm.Stage == Stage.Tribe)
            _side.AddChild(L("지방 건물은 군장사회 단계부터 지을 수 있습니다. 도성 칸 건물은 건설(B) 탭을 여세요.", 13, Faint(0.7f), true, SideW));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        var availBlds = BuildingCatalog.All
            .Where(def => !p.Has(def.Kind) && def.Era <= S.Realm.Era + 1)
            .OrderBy(def => S.WhyCantBuild(p.Id, def.Kind).Length > 0 ? 1 : 0)
            .ToList();
        foreach (var def in availBlds)
        {
            string why = S.WhyCantBuild(p.Id, def.Kind);
            bool off = why.Length > 0;
            double cost = S.BuildCost(p, def.Kind);
            string tip = $"{def.Name}\n＋ 효과: {def.Gain}\n− 대가: {def.Price}\n건설비 국고 {cost:0.0} · 공사 {def.Seasons}계절 · 유지비 {def.Upkeep:0.00}/년" + (off ? $"\n\n지금 불가: {why}" : "");
            var kind = def.Kind;
            var b = new Button { TooltipText = tip, Disabled = off, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(156, 46) };
            b.AddThemeStyleboxOverride("normal", Box(off ? new Color(Ui.Surface, 0.35f) : Ui.Surface, off ? new Color(Ui.Rim, 0.2f) : new Color(Art.Gold, 0.65f), 1, 4, 8));
            b.AddThemeStyleboxOverride("hover", Box(Ui.Surface.Lightened(0.12f), Ui.Accent, 2, 4, 8));
            b.AddThemeStyleboxOverride("disabled", Box(new Color(Ui.Surface, 0.28f), new Color(Ui.Rim, 0.16f), 1, 4, 8));
            b.Pressed += () => Sfx.Play("click");
            b.Pressed += () =>
            {
                if (!S.Build(p.Id, kind)) Toast(S.WhyCantBuild(p.Id, kind), Art.Bad);
                else Sfx.Play("build");
                Game.Touched();
            };
            var bh = H(6);
            bh.MouseFilter = Control.MouseFilterEnum.Ignore;
            bh.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, 6);
            bh.AddChild(new BuildingGlyph(def.Kind, size: 30) { Modulate = off ? new Color(1, 1, 1, 0.4f) : Colors.White });
            var bt = V(-2);
            bt.MouseFilter = Control.MouseFilterEnum.Ignore;
            bt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            bt.Alignment = BoxContainer.AlignmentMode.Center;
            var bn = L(def.Name, 14, off ? Faint(0.45f) : Ink);
            bn.MouseFilter = Control.MouseFilterEnum.Ignore;
            bt.AddChild(bn);
            var bc = L(off ? why : $"국고 {cost:0.0} · {def.Seasons}철", 11, off ? Faint(0.45f) : new Color("e2b84f"));
            bc.MouseFilter = Control.MouseFilterEnum.Ignore;
            bc.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            bt.AddChild(bc);
            bh.AddChild(bt);
            b.AddChild(bh);
            grid.AddChild(b);
        }
        _side.AddChild(grid);
    }
}
