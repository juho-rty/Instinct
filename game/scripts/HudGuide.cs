using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>
/// 안내: 새 게임을 시작하면 화면의 각 부분을 차례로 짚어 주는 카드(처음 안내),
/// 그리고 나라가 서거나 침공이 오는 등 처음 겪는 일에 한 번씩 뜨는 도움말(상황 안내).
/// 카드가 떠 있는 동안 시간은 멈춘다. "다시 표시하지 않기"를 누르면 Prefs.ShowGuide가 꺼진다.
/// </summary>
public partial class Hud
{
    record GuideStep(string Key, string Title, string Body, Func<Rect2?>? Target);

    PanelContainer _guide = null!;
    Label _guideTitle = null!, _guideBody = null!, _guideCount = null!;
    Button _guidePrev = null!, _guideNext = null!, _guideSkip = null!;
    IconButton _guideMute = null!;
    GuideRing _ring = null!;
    readonly List<GuideStep> _guideQueue = new();
    readonly HashSet<string> _guideSeen = new();
    int _guideAt, _guideIntroLen;

    /// <summary>안내 카드가 떠 있는가. 떠 있으면 시간이 흐르지 않는다.</summary>
    public bool GuideOpen => _guide.Visible;
    /// <summary>처음 안내만 시간을 멈춘다. 상황별 도움말은 떠 있어도 시간이 흐른다.</summary>
    public bool GuideHolds => _guide.Visible && _guideAt < _guideIntroLen;

    void BuildGuide()
    {
        _ring = new GuideRing { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _ring.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_ring);

        _guide = new PanelContainer { Visible = false };
        var gs = Ui.GlassBox(22, 18, 0.96f);
        gs.BgColor = new Color("13262d", 0.98f);
        gs.BorderColor = new Color(Ui.Accent, 0.85f);
        gs.SetBorderWidthAll(2);
        gs.ShadowColor = new Color(0, 0, 0, 0.45f);
        gs.ShadowSize = 14;
        _guide.AddThemeStyleboxOverride("panel", gs);
        _root.AddChild(_guide);

        var v = V(10);
        _guide.AddChild(v);
        var head = H(8);
        head.AddChild(new Glyph("guide", 24, Ui.Accent));
        _guideTitle = L("", 20);
        _guideTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(_guideTitle);
        _guideCount = L("", 13, Faint(0.55f));
        head.AddChild(_guideCount);
        v.AddChild(head);
        _guideBody = L("", 15, null, true, 520);
        v.AddChild(_guideBody);
        v.AddChild(new HSeparator());

        var foot = H(6);
        _guideMute = (IconButton)B("다시 표시하지 않기", ToggleGuideMute, "체크하면 이 안내와 상황별 도움말이 더 이상 뜨지 않습니다. 설정에서 다시 켤 수 있습니다.", false, 14, "box");
        _guideMute.Flat = true;
        foot.AddChild(_guideMute);
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _guideSkip = B("건너뛰기", CloseGuide, "남은 안내를 모두 닫습니다 (Esc)", false, 14);
        foot.AddChild(_guideSkip);
        _guidePrev = B("이전", () => GuideGo(_guideAt - 1), "", false, 14, "back");
        foot.AddChild(_guidePrev);
        _guideNext = B("다음", () => GuideGo(_guideAt + 1), "", false, 15, "play1");
        _guideNext.CustomMinimumSize = new Vector2(96, 0);
        foot.AddChild(_guideNext);
        v.AddChild(foot);

        // 아래 단추 줄 바로 위, 가운데.
        _guide.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 82);
        _guide.GrowVertical = Control.GrowDirection.Begin;
        _guide.GrowHorizontal = Control.GrowDirection.Both;
    }

    void ResetGuide()
    {
        _guideQueue.Clear();
        _guideSeen.Clear();
        _guideAt = _guideIntroLen = 0;
        HideGuide();
    }

    void HideGuide()
    {
        _guide.Visible = false;
        _ring.Visible = false;
    }

    // ───────── 처음 안내 ─────────

    /// <summary>새 게임의 첫 안내. force면 설정이 꺼져 있어도 띄운다(설정의 "다시 보기").</summary>
    public void StartIntro(bool force = false)
    {
        if (!force && !Prefs.ShowGuide) return;
        _guideQueue.Clear();
        _guideQueue.AddRange(IntroSteps());
        _guideIntroLen = _guideQueue.Count;
        GuideGo(0);
    }

    IEnumerable<GuideStep> IntroSteps()
    {
        yield return new("intro", "Instinct에 오신 것을 환영합니다",
            "작은 무리에서 시작해 여러 세대에 걸쳐 문명을 키우는 게임입니다. 사람들은 저마다 공격성·탐욕·야심·공감 같은 본능을 지니고 스스로 살아갑니다.\n\n처음에는 사람들에게 일을 나누어 주고 건물을 지어 겨울을 나며, 무리가 커져 나라가 되면 법과 기술·군대로 역사의 방향을 이끕니다. 짧게 화면을 둘러보겠습니다.",
            null);
        yield return new("time", "시간",
            "시간은 계절 단위로 흐릅니다. 여기서 일시정지와 속도를 바꿉니다(Space, 숫자 1~4).\n안내가 떠 있는 동안에는 시간이 멈춰 있으니 천천히 읽으셔도 됩니다.",
            () => _speedBox.GetGlobalRect());
        if (S.Tribal)
            yield return new("tribe", "일 배치 · 건설 · 무리",
                "왼쪽 패널의 세 탭으로 무리를 다스립니다.\n· 일(J): 채집·사냥·벌목·채석·건설·정찰·경계에 사람을 나눕니다.\n· 건설(B): 나무와 돌로 움집·창고·사냥막·목책 등을 칸 위에 짓습니다.\n· 무리: 우두머리가 큰 사냥·잔치·선물·습격을 벌입니다.",
            () => _sidePanel.Visible ? _sidePanel.GetGlobalRect() : null);
        yield return new("research", "연구",
            "연구할 기술을 고르세요(T). 기술은 새 제도와 건물, 자원을 열어 줍니다. 연구가 멈춰 있으면 오른쪽 위에 알림이 뜹니다.",
            () => _research.GetGlobalRect());
        yield return new("stats", "상태와 자원",
            "위쪽 가운데에는 우두머리 지지와 단합(나라가 서면 영토·불만도 함께), 오른쪽 위에는 식량(창고/한도)·나무·돌·인구가 표시됩니다.\n\n위험한 수치는 붉게 바뀌며, 각 칸에 마우스를 올리면 자세한 원인과 겨울 대비 정보가 나옵니다.",
            StatRect);
        yield return new("tools", "창 열기와 저장",
            "법전(L)에서 제도를 도입하거나 폐지하고, 나라(R)에서 계층과 승리 조건·이웃 나라를 살펴봅니다. 사람들(P)에서는 한 사람씩 일을 정하거나 가족과 원한을 볼 수 있고, 연대기(C)에는 지금까지의 사건이 쌓입니다.\n\n오른쪽 끝 단추로 저장(F5)과 불러오기(F9)를 합니다.",
            () => _tools.GetGlobalRect());
        yield return new("map", "지도 조작",
            "좌클릭으로 지역을 선택하고, 드래그나 WASD로 지도를 움직이며, 휠로 확대·축소합니다. Esc는 선택 해제, 아무것도 고르지 않았을 때는 설정입니다.\n\n이제 시작해 보세요. 처음 겪는 일이 생기면 그때그때 짧게 알려 드립니다.",
            () => _hint.GetGlobalRect());
    }

    // ───────── 상황 안내 ─────────

    /// <summary>Refresh마다: 처음 겪는 일이면 도움말을 줄에 세우고, 카드가 비어 있으면 띄운다.</summary>
    void GuideCheck()
    {
        if (!Prefs.ShowGuide || Game.NoGuide || !S.Living.Any()) return;
        if (!S.Tribal)
            Tip("state", "나라가 세워졌습니다",
                "이제 무리 대신 나라를 다스립니다. 지도에서 우리 땅을 클릭하면 건물을 짓고 도로를 낼 수 있습니다. 도성이나 병영이 있는 땅에서는 군단을 징집할 수 있습니다.\n\n오른쪽 위의 국고와 위쪽 띠의 영토(다스릴 수 있는 한도)를 눈여겨보세요. 한도를 넘으면 단합이 깎이고 나라가 흔들립니다.",
                StatRect);
        if (Game.Selected >= 0 && Game.SelectedArmy == null)
            Tip("land", "지역 정보",
                "선택한 지역의 지형, 비옥도, 자원이 왼쪽에 표시됩니다. 나라가 세워지면 여기서 건물을 짓고 도로를 냅니다. Esc를 누르면 선택이 해제됩니다.",
                () => _sidePanel.Visible ? _sidePanel.GetGlobalRect() : null);
        if (Game.SelectedArmy != null)
            Tip("army", "군단 지휘",
                "군단을 선택한 상태에서 지도를 우클릭하면 그곳으로 이동합니다. 한 계절에 한 지역씩 움직입니다.\n\n패널에서 인접 지역의 승률을 확인하세요. 주인 없는 땅은 싸우지 않고 차지하고, 다른 나라 땅에 들어가면 전투가 벌어집니다.",
                () => _sidePanel.Visible ? _sidePanel.GetGlobalRect() : null);
        if (S.Pending.Count > 0 && !ModalOpen)
            Tip("decision", "결정을 기다리는 일",
                "나라에 중요한 일이 생기면 오른쪽에 결정 카드가 쌓입니다. 카드에서 바로 고르거나 '자세히'를 눌러 크게 볼 수 있으며, 기한 안에 정하지 않으면 마지막 선택지로 처리됩니다.",
                () => _decCards.GetGlobalRect());
        if (S.Armies.Any(a => a.Owner != 0 && a.Muster > 0 && a.Goal >= 0 && S.Map[a.Goal].Owner == 0))
            Tip("invasion", "적의 침공",
                "적이 군대를 모으고 있습니다. 출발까지 몇 계절이 남아 있습니다. 오른쪽 위 알림을 누르면 적이 노리는 땅으로 이동합니다.\n\n그곳에 군단을 보내 막으세요. 방어하는 쪽이 유리하고, 성이 있으면 더 유리합니다.",
                () => _badges.GetGlobalRect());

        if (!_guide.Visible && !ModalOpen && _guideAt < _guideQueue.Count) GuideGo(_guideAt);
    }

    void Tip(string key, string title, string body, Func<Rect2?> target)
    {
        if (!_guideSeen.Add(key)) return;
        _guideQueue.Add(new GuideStep(key, title, body, target));
    }

    // ───────── 카드 ─────────

    /// <summary>스크린샷용: 처음 안내의 i번째 카드를 띄운다.</summary>
    public void ShowIntroStep(int i)
    {
        StartIntro(true);
        GuideGo(Math.Clamp(i, 0, _guideIntroLen - 1));
    }

    void GuideGo(int i)
    {
        if (i < 0) i = 0;
        if (i >= _guideQueue.Count)
        {
            _guideAt = _guideQueue.Count;
            HideGuide();
            HintRefresh();
            return;
        }
        _guideAt = i;
        var st = _guideQueue[i];
        _guideSeen.Add(st.Key);
        bool intro = i < _guideIntroLen;
        _guideTitle.Text = st.Title;
        _guideBody.Text = st.Body;
        _guideCount.Text = intro ? $"{i + 1} / {_guideIntroLen}" : "도움말";
        _guidePrev.Visible = intro && i > 0;
        _guideSkip.Visible = intro && i < _guideIntroLen - 1;
        bool last = intro ? i == _guideIntroLen - 1 : true;
        _guideNext.Text = intro ? (last ? "시작하기" : "다음") : "확인";
        SyncMute();
        _guide.Visible = true;
        _guide.ResetSize();
        _ring.Target = st.Target;
        _ring.Visible = st.Target != null;
        HintRefresh();
        Sfx.Play("tick", 0.6f);
    }

    /// <summary>안내를 닫는다. 처음 안내 중이면 남은 처음 안내를 모두 건너뛴다.</summary>
    public void CloseGuide()
    {
        if (_guideAt < _guideIntroLen)
        {
            _guideQueue.RemoveRange(0, _guideIntroLen);
            _guideIntroLen = 0;
            _guideAt = 0;
        }
        else _guideAt = _guideQueue.Count;
        HideGuide();
        HintRefresh();
    }

    void ToggleGuideMute()
    {
        Prefs.ShowGuide = !Prefs.ShowGuide;
        Prefs.Save();
        SyncMute();
    }

    void SyncMute()
    {
        _guideMute.Kind = Prefs.ShowGuide ? "box" : "checked";
        _guideMute.QueueRedraw();
    }
}

/// <summary>안내가 짚는 자리 둘레에 숨 쉬듯 빛나는 테.</summary>
public partial class GuideRing : Control
{
    public Func<Rect2?>? Target;
    float _t;
    static readonly StyleBoxFlat RingBox = MakeRingBox();
    static StyleBoxFlat MakeRingBox()
    {
        var sb = new StyleBoxFlat { BgColor = new Color(Ui.Accent, 0.06f), AntiAliasing = true, ShadowSize = 10 };
        sb.SetBorderWidthAll(3);
        sb.SetCornerRadiusAll(16);
        return sb;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Target?.Invoke() is not { } g || g.Size.X < 1) return;
        // 전역(화면) 좌표 → 이 판의 좌표(판 전체가 UI 크기만큼 늘어나 있다).
        var inv = GetGlobalTransform().AffineInverse();
        var r = new Rect2(inv * g.Position, inv.BasisXform(g.Size)).Grow(6 + 2 * Mathf.Sin(_t * 4));
        float a = 0.55f + 0.35f * Mathf.Sin(_t * 4);
        RingBox.BorderColor = new Color(Ui.Accent.Lightened(0.15f), a);
        RingBox.ShadowColor = new Color(Ui.Accent, 0.35f * a);
        DrawStyleBox(RingBox, r);
    }
}
