using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>칸 건물 그림 하나를 단추·목록 옆에 그리는 작은 칸.</summary>
public partial class StructIcon : Control
{
    public StructureKind Kind;
    public float Done = 1;

    public StructIcon() { }
    public StructIcon(StructureKind k, float done = 1)
    {
        Kind = k;
        Done = done;
        CustomMinimumSize = new Vector2(46, 46);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float s = Math.Min(Size.X, Size.Y) / 46f * 0.78f;
        DrawSetTransform(Size / 2 + new Vector2(0, 2 * s), 0, new Vector2(s, s));
        Cells.Structure(this, Kind, Vector2.Zero, Done, false);
    }
}

/// <summary>
/// 아무것도 고르지 않았을 때 왼쪽 패널:
/// · 항상 맨 위에 발전 나침반과 현재 상황의 열린 선택지를 보여 주어 자유도를 해치지 않으면서 무엇을 할 수 있는지 직관적으로 안내한다.
/// · 무리 시절: 무리(우두머리의 행동) · 일(사람 배치와 관리인) · 건설(칸 건물).
/// · 나라 시절: 국정(승리 길·예산·영토·군단 요약) · 일족(도성 일손) · 건설(도성 칸 건물).
/// </summary>
public partial class Hud
{
    string _campTab = "work";
    bool _wasTribal = true;

    public void OpenCampTab(string tab) { _campTab = tab; RefreshSide(); }

    /// <summary>아래 단추 줄의 "일"·"건설": 고른 땅·군단을 놓고 왼쪽 패널의 그 탭을 연다.</summary>
    public void ShowCamp(string tab)
    {
        _campTab = tab;
        CloseModal();
        if (Game.Selected >= 0 || Game.SelectedArmy != null) Game.Select(-1);
        else RefreshSide();
    }

    /// <summary>왼쪽 패널의 안쪽 폭. 글자 깨짐 없이 넉넉하면서도 지도를 가리지 않는 폭.</summary>
    public const float SideW = 320;

    static readonly (Work w, string icon)[] WorkIcons =
    {
        (Work.Gather, "gather"), (Work.Hunt, "hunt"), (Work.Fish, "fish"), (Work.Farm, "farm"), (Work.Wood, "wood"),
        (Work.Stone, "stone"), (Work.Build, "build"), (Work.Scout, "scout"), (Work.Guard, "guard"),
    };

    public static string WorkIcon(Work w) => WorkIcons.FirstOrDefault(x => x.w == w).icon ?? "info";

    void CampPanel()
    {
        bool tribal = S.Tribal;
        if (_wasTribal && !tribal) _campTab = "overview";
        _wasTribal = tribal;
        if (!tribal && _campTab == "tribe") _campTab = "overview";
        if (tribal && _campTab == "overview") _campTab = "work";

        var tabs = H(4);
        void Tab(string key, string text, string icon, string tip)
        {
            var b = B(text, () => { _campTab = key; RefreshSide(); }, tip, false, 14, icon);
            b.ToggleMode = true;
            b.ButtonPressed = _campTab == key;
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.CustomMinimumSize = new Vector2(0, 32);
            foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed" })
            {
                bool on = st is "pressed" or "hover_pressed" || _campTab == key;
                b.AddThemeStyleboxOverride(st, Ui.Pill(on ? new Color("2a5968") : st == "hover" ? Ui.Surface.Lightened(0.12f) : new Color("152830"),
                    on ? Ui.Accent : new Color(Ui.Rim, 0.45f), 5, 8, 8));
            }
            tabs.AddChild(b);
        }
        if (tribal)
        {
            Tab("work", "일", "gather", "누가 무슨 일을 할지 정합니다 (J)");
            Tab("build", "건설", "build", "칸 위에 건물을 짓습니다 (B)");
            Tab("tribe", "무리", "people", "우두머리가 이번 계절에 벌일 행동(사냥·잔치·선물·습격)");
        }
        else
        {
            Tab("overview", "국정", "realm", "나라의 목표·예산·영토·군단 한눈에 보기");
            Tab("work", "일족", "gather", "도성 지배 가문 사람들의 일손 배치 (J)");
            Tab("build", "터 건설", "build", "도성 주변 칸 건물 짓기 (B)");
        }
        _side.AddChild(tabs);

        CompassBox();

        switch (_campTab)
        {
            case "tribe": TribePanel(); break;
            case "build": BuildTab(); break;
            case "overview": RealmQuickTab(); break;
            default: WorkTab(); break;
        }
    }

    // ───────── 발전 나침반 & 상황별 열린 선택지 ─────────

    void CompassBox()
    {
        var card = new PanelContainer();
        var sb = Box(new Color("12252d", 0.94f), new Color(Ui.Rim, 0.5f), 1, 8, 10);
        sb.BorderWidthLeft = 3;
        sb.BorderColor = new Color(Art.Gold, 0.7f);
        card.AddThemeStyleboxOverride("panel", sb);
        var v = V(5);
        card.AddChild(v);

        var (stTarget, stNeeds) = S.NextStageChecks();
        var (eraTarget, eraNeeds) = S.NextEraChecks();

        if (stTarget.Length > 0 || eraTarget.Length > 0)
        {
            var head = H(6);
            head.AddChild(new Glyph("legit", 16, Art.Gold));
            string goalTitle = stTarget.Length > 0 ? $"다음 단계: {stTarget}" : $"다음 시대: {eraTarget}";
            var gl = L(goalTitle, 13, Art.Gold);
            gl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(gl);
            if (!S.Tribal && stTarget.Length > 0 && eraTarget.Length > 0)
                head.AddChild(L($"시대 → {eraTarget}", 11, Faint(0.6f)));
            v.AddChild(head);

            var chips = new HFlowContainer { CustomMinimumSize = new Vector2(SideW - 18, 0) };
            chips.AddThemeConstantOverride("h_separation", 5);
            chips.AddThemeConstantOverride("v_separation", 4);
            var showNeeds = stTarget.Length > 0 ? stNeeds : eraNeeds;
            if (!S.Tribal && stTarget.Length > 0 && eraTarget.Length > 0)
                showNeeds = stNeeds.Concat(eraNeeds.Where(e => showNeeds.All(s => s.Text != e.Text))).Take(5).ToList();
            foreach (var n in showNeeds)
                chips.AddChild(MilestoneChip(n));
            v.AddChild(chips);
        }

        var opps = GatherOpportunities();
        if (opps.Count > 0)
        {
            var oppCol = V(3);
            foreach (var (icon, text, col, act, tip) in opps.Take(2))
            {
                var bt = B(text, act, tip, false, 12, icon);
                bt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                bt.CustomMinimumSize = new Vector2(0, 26);
                bt.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                bt.AddThemeStyleboxOverride("normal", Ui.Pill(new Color(col, 0.14f), new Color(col, 0.65f), 4, 7, 8));
                bt.AddThemeStyleboxOverride("hover", Ui.Pill(new Color(col, 0.26f), col, 4, 7, 8));
                oppCol.AddChild(bt);
            }
            v.AddChild(oppCol);
        }

        if (v.GetChildCount() > 0) _side.AddChild(card);
    }

    Control MilestoneChip(Simulation.MilestoneNeed n)
    {
        if (n.Met)
        {
            var done = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            var dsb = Box(new Color(Art.Good, 0.18f), new Color(Art.Good, 0.6f), 1, 3, 6);
            dsb.ContentMarginLeft = dsb.ContentMarginRight = 7;
            done.AddThemeStyleboxOverride("panel", dsb);
            done.AddChild(L("✓ " + n.Text, 11, new Color("a8e296")));
            return done;
        }

        Action? onClick = null;
        string label = "○ " + n.Text;
        string tip = $"목표 달성 조건: {n.Text}";
        Color rim = new(Ui.Rim, 0.5f);
        Color bg = new("182d36");

        if (n.LawKey is { } lk && Catalog.Find(lk) is { } inst)
        {
            if (S.WhyUnavailable(inst).Length == 0)
            {
                label = $"+ {n.Text} 도입 가능";
                tip = $"{inst.Name} 제도를 지금 바로 도입할 수 있습니다.\n누르면 법전을 엽니다.";
                rim = Ui.Accent;
                bg = new Color(Ui.Accent, 0.2f);
                onClick = () => OpenLaw(lk);
            }
            else if (n.TechKey is { Length: > 0 } tk && !S.Knows(tk) && TechTree.Find(tk) is { } td)
            {
                label = $"○ {n.Text} (기술: {td.Name})";
                tip = $"{inst.Name} 제도를 열려면 먼저 「{td.Name}」 기술을 연구해야 합니다.\n누르면 기술 나무에서 해당 위치를 보여 줍니다.";
                rim = new Color(Art.Gold, 0.7f);
                onClick = () => OpenTech(tk);
            }
            else
            {
                tip = $"{inst.Name} 필요: {S.WhyUnavailable(inst)}\n누르면 법전을 엽니다.";
                onClick = () => OpenLaw(lk);
            }
        }
        else if (n.TechKey != null)
        {
            string tk = n.TechKey;
            tip = "지식은 해마다 쌓이며 글·도서관·대학이 속도를 높입니다.\n누르면 기술 창을 엽니다.";
            onClick = () => OpenTech(tk);
        }
        else if (n.Text.StartsWith("영토"))
        {
            var free = AdjacentUnowned();
            if (free != null)
            {
                tip = $"군단을 일으켜 인접한 빈 땅({free.Name} 등)이나 이웃 나라 땅을 차지하면 영토가 늘어납니다.\n누르면 개척 가능한 인접 땅으로 이동합니다.";
                onClick = () => { Game.Focus(free.Id); Game.Select(free.Id); };
            }
        }

        if (onClick == null)
        {
            var p = new PanelContainer { TooltipText = tip, MouseFilter = Control.MouseFilterEnum.Stop };
            var psb = Box(bg, rim, 1, 3, 6);
            psb.ContentMarginLeft = psb.ContentMarginRight = 7;
            p.AddThemeStyleboxOverride("panel", psb);
            p.AddChild(L(label, 11, Faint(0.8f)));
            return p;
        }

        var b = B(label, onClick, tip, false, 11);
        b.AddThemeStyleboxOverride("normal", Ui.Pill(bg, rim, 3, 6, 7));
        b.AddThemeStyleboxOverride("hover", Ui.Pill(bg.Lightened(0.14f), Ui.Accent, 3, 6, 7));
        return b;
    }

    Province? AdjacentUnowned() =>
        S.Map.Provinces.Where(p => p.Owner < 0 && S.Map.Known(p) && p.Adjacent.Any(a => S.Map[a].Owner == 0))
            .OrderByDescending(p => p.Fertility).ThenBy(p => p.Id).FirstOrDefault();

    List<(string icon, string text, Color col, Action act, string tip)> GatherOpportunities()
    {
        var list = new List<(string, string, Color, Action, string)>();
        bool steward = S.Steward is { Alive: true };

        if (S.Structures.Any(x => !x.Built) && S.CountJob(Work.Build) == 0 && !steward)
        {
            int sites = S.Structures.Count(x => !x.Built);
            list.Add(("build", $"공사 중인 건물 {sites}곳 멈춤 → 건설 일꾼 +1 배치", Art.Bad,
                () => { if (S.Assign(Work.Build, 1)) Game.Touched(); }, "클릭하면 채집 인원 1명을 즉시 건설에 투입합니다."));
        }
        if (S.Research == null && TechTree.All.Any(S.CanResearch))
        {
            list.Add(("tech", S.AtCrossroad ? "기술 갈림길 선택하기 (나라의 길 결정)" : "비어 있는 기술 연구 과제 고르기", Ui.Accent,
                () => Open("tech"), "연구를 골라 두어야 해마다 쌓이는 지식이 새 기술로 이어집니다."));
        }
        if (S.Tribal)
        {
            double need = S.SeasonNeed();
            if (SeasonsToWinter <= 1 && S.CommonStore < need * 1.2)
            {
                bool canHunt = S.WhyCantTribe("hunt").Length == 0;
                list.Add(("food", canHunt ? $"겨울 식량 부족 ({S.CommonStore:0}/{need:0}) → 큰 사냥 가능" : $"겨울 대비: 식량 일손 늘리기 ({S.CommonStore:0}/{need:0})",
                    Art.Bad, () => OpenCampTab(canHunt ? "tribe" : "work"), "겨울에는 들에서 식량이 거의 나지 않으므로 가을까지 창고를 채워야 합니다."));
            }
            if (S.CommonStore > S.StoreCap() + 2)
            {
                bool canStore = S.Wood >= 5;
                list.Add(("build", canStore ? "창고 한도 초과 — 클릭해 창고 짓기 (나무 5)" : "창고 한도 초과 — 벌목 후 창고 증설 추천",
                    WarnCol, () => { if (canStore) Game.StartPlacing(StructureKind.Storehouse); else OpenCampTab("work"); },
                    "저장 한도를 넘긴 식량은 계절마다 30%씩 썩습니다."));
            }
            if (S.BuiltCount(StructureKind.Hut) == 0 && S.Wood >= 3 && S.Living.Count() >= 14)
            {
                list.Add(("home", "나무 여유 — 움집 지어 수용 인구 늘리기 (나무 3)", Art.Good,
                    () => Game.StartPlacing(StructureKind.Hut), "인구가 늘어날수록 1인당 수확 효율이 줄어듭니다. 움집은 수용력을 +5 늘립니다."));
            }
            if (S.CountJob(Work.Scout) == 0 && S.Workers().Count >= 8 && !steward && S.FogEdge().Count > 0)
            {
                list.Add(("scout", "정찰꾼 1명 파견해 주변 사냥터·자원 밝히기", Ui.Accent,
                    () => { if (S.Assign(Work.Scout, 1)) Game.Touched(); }, "클릭하면 정찰에 1명을 배정해 안개를 걷고 새 자원을 찾습니다."));
            }
        }
        else
        {
            if (S.Armies.Count(a => a.Owner == 0) == 0 && S.WhyCantRaise(S.Map.Capital).Length == 0 && (AdjacentUnowned() != null || S.Rivals.Any(v => v.Hostility > 0.55)))
            {
                list.Add(("army", "도성에서 군단 징집하기 (빈 땅 개척 · 국경 방어)", Ui.Accent,
                    () => { if (S.RaiseArmy(S.Map.Capital) is { } a) { Game.SelectedArmy = a; Sfx.Play("hunt"); Game.Touched(); } },
                    "국고 0.5로 도성에서 군단을 일으킵니다. 주인 없는 인접 땅을 개척하거나 침공을 막을 수 있습니다."));
            }
            else if (S.Armies.FirstOrDefault(a => a.Owner == 0 && a.Goal < 0) is { } idle && AdjacentUnowned() is { } free && S.Realm.Treasury >= 0.3 && S.Lands(0).Count() < S.AdminCap)
            {
                list.Add(("land", $"인접 빈 땅 「{free.Name}」 개척 가능 (통치 한도 여유)", Art.Good,
                    () => { Game.Focus(free.Id); Game.Select(free.Id); }, "클릭하면 해당 지역을 비추며 바로 군단을 보내 개척할 수 있습니다."));
            }
            if (S.Rivals.FirstOrDefault(v => S.WhyCantEnvoy(v.Id, "trade").Length == 0) is { } tr)
            {
                list.Add(("coin", $"{tr.Name}에 교역 사절 파견 가능 (세입·문물↑)", Art.Gold,
                    () => Open("realm"), "나라 창이나 해당 국가 영토에서 사절을 보내 즉시 장삿길을 열 수 있습니다."));
            }
        }
        return list;
    }

    // ───────── 나라 시절의 국정 요약 탭 ─────────

    void RealmQuickTab()
    {
        var r = S.Realm;
        var bf = S.ForecastTreasury();

        // 1) 연간 국고 수지 카드
        var bCard = new PanelContainer { TooltipText = CoinTip(bf), MouseFilter = Control.MouseFilterEnum.Stop };
        bCard.AddThemeStyleboxOverride("panel", Box(new Color(bf.Net < 0 ? Art.Bad : Art.Good, 0.12f), new Color(bf.Net < 0 ? Art.Bad : Art.Good, 0.6f), 1, 7, 8));
        var bRow = H(6);
        bRow.MouseFilter = Control.MouseFilterEnum.Ignore;
        bRow.AddChild(new Glyph("coin", 20, bf.Net < 0 ? DownCol : UpCol));
        var bMid = V(0);
        bMid.MouseFilter = Control.MouseFilterEnum.Ignore;
        bMid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        double outTotal = bf.BaseExpense + bf.Upkeep + bf.Drain;
        bMid.AddChild(L($"국고 {r.Treasury:0.00}  (세입 +{bf.Income:0.00} / 지출 −{outTotal:0.00})", 13));
        bRow.AddChild(bMid);
        bRow.AddChild(L($"{(bf.Net >= 0 ? "+" : "−")}{Math.Abs(bf.Net):0.00}/년", 14, bf.Net >= 0 ? UpCol : DownCol));
        bCard.AddChild(bRow);
        _side.AddChild(bCard);

        // 2) 가장 가까운 승리 길 진척
        var bestVic = Simulation.Victories.OrderByDescending(vd => S.VictoryProgress(vd.Course)).First();
        double vp = S.VictoryProgress(bestVic.Course);
        var (vNeeds, streak, hold) = S.VictoryNeeds(bestVic.Course);
        var vCard = new PanelContainer();
        vCard.AddThemeStyleboxOverride("panel", Box(new Color(Ui.Surface, 0.7f), new Color(Ui.Rim, 0.4f), 1, 7, 8));
        var vv = V(4);
        vCard.AddChild(vv);
        var vHead = H(6);
        vHead.AddChild(L($"유력 승리: {bestVic.Name} ({Simulation.CourseName(bestVic.Course)})", 13, Ui.Accent));
        vHead.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        vHead.AddChild(L(hold > 1 && streak > 0 ? $"{streak}/{hold}년" : $"{vp * 100:0}%", 12, Faint(0.75f)));
        vv.AddChild(vHead);
        vv.AddChild(Meter(vp, SideW - 16, Ui.Accent, 6));
        string rem = string.Join(" · ", vNeeds.Select(x => (x.ok ? "✓" : "○") + x.what));
        var remL = L(rem, 11, Faint(0.7f), true, SideW - 16);
        vv.AddChild(remL);
        _side.AddChild(vCard);

        // 3) 우리 영토 빠른 선택 (건물·도로·징집)
        var lands = S.Lands(0).OrderByDescending(p => p.Id == S.Map.Capital).ThenBy(p => p.Id).ToList();
        var lHead = H(6);
        lHead.AddChild(Head("land", $"우리 영토 ({lands.Count}/{S.AdminCap})", 14));
        lHead.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        lHead.AddChild(L("클릭해 건설·징집", 11, Faint(0.55f)));
        _side.AddChild(lHead);
        var lFlow = new HFlowContainer { CustomMinimumSize = new Vector2(SideW, 0) };
        lFlow.AddThemeConstantOverride("h_separation", 5);
        lFlow.AddThemeConstantOverride("v_separation", 5);
        foreach (var lp in lands.Take(10))
        {
            int pid = lp.Id;
            bool cap = pid == S.Map.Capital;
            string tag = (cap ? "★ " : "") + lp.Name + (lp.Buildings.Count > 0 ? $" [{lp.Buildings.Count}]" : "");
            var lb = B(tag, () => { Game.Focus(pid); Game.Select(pid); },
                $"{lp.Name} — 건물 {lp.Buildings.Count}채" + (lp.Road || cap ? " · 도로 연결됨" : " · 도로 미연결") + "\n클릭하면 이 지역 건설·군단 창을 엽니다.", false, 12);
            lb.AddThemeStyleboxOverride("normal", Ui.Pill(Ui.Surface, new Color(cap ? Art.Gold : Ui.Rim, 0.55f), 4, 7, 8));
            lb.AddThemeStyleboxOverride("hover", Ui.Pill(Ui.Surface.Lightened(0.12f), Ui.Accent, 4, 7, 8));
            lFlow.AddChild(lb);
        }
        _side.AddChild(lFlow);

        // 4) 군단 빠른 지휘
        var myArmies = S.Armies.Where(a => a.Owner == 0).OrderBy(a => a.Id).ToList();
        var aHead = H(6);
        aHead.AddChild(Head("army", $"군단 ({myArmies.Count}/{S.ArmyCap})", 14));
        aHead.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        string whyRaise = S.WhyCantRaise(S.Map.Capital);
        aHead.AddChild(B("+ 징집", () =>
        {
            if (S.RaiseArmy(S.Map.Capital) is { } na) { Game.SelectedArmy = na; Sfx.Play("hunt"); }
            else Toast(whyRaise, Art.Bad);
            Game.Touched();
        }, whyRaise.Length > 0 ? $"지금 불가: {whyRaise}" : "도성에서 새 군단을 일으킵니다 (국고 0.5).", whyRaise.Length > 0, 12, "army"));
        _side.AddChild(aHead);
        if (myArmies.Count == 0)
            _side.AddChild(L("운용 중인 군단이 없습니다. 군단을 징집해 빈 땅 개척과 방어에 나서세요.", 12, Faint(0.6f), true, SideW));
        else
            foreach (var ma in myArmies)
            {
                var armyRef = ma;
                string state = ma.Goal >= 0 ? $"→ {S.Map[ma.Goal].Name}" : $"{S.Map[ma.At].Name} 대기";
                var ab = B($"{ma.Name} ({Art.Num(ma.Men)}명) · {state}", () => { Game.Focus(armyRef.At); Game.SelectArmy(armyRef); },
                    "클릭해 군단을 선택하고 우클릭으로 목적지를 지정합니다.", false, 12, "army");
                ab.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _side.AddChild(ab);
            }
    }

    /// <summary>작은 타일 바탕.</summary>
    static StyleBoxFlat TileBox(bool on, bool off, bool bad = false) =>
        Box(on ? new Color(Art.Good, 0.2f) : bad ? new Color(Art.Bad, 0.14f) : new Color(Ui.Surface, off ? 0.35f : 0.92f),
            on ? new Color(Art.Good, 0.85f) : bad ? new Color(Art.Bad, 0.7f) : new Color(Ui.Rim, off ? 0.14f : 0.35f), on || bad ? 2 : 1, 4, 8);

    /// <summary>사람이 없는 일 줄에: 이번 철이 그 일에 좋은 철인지 한 마디로.</summary>
    string SeasonWord(WorkDef def)
    {
        double top = Math.Max(0.01, def.Season.Max()), now = def.Season[S.Season] / top;
        return now >= 0.85 ? "제철" : now >= 0.5 ? "보통" : now > 0.05 ? "비수기" : "쉬는 철";
    }

    /// <summary>네 철의 소출을 작은 막대 넷으로(지금 철은 밝게).</summary>
    Control SeasonBars(WorkDef def, bool dim)
    {
        var box = H(2);
        box.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.CustomMinimumSize = new Vector2(0, 13);
        double top = Math.Max(0.01, def.Season.Max());
        for (int i = 0; i < 4; i++)
        {
            bool now = i == S.Season;
            var c = now ? (dim ? Faint(0.4f) : SeasonInk[i]) : new Color(SeasonInk[i], dim ? 0.15f : 0.35f);
            float h = (float)Math.Max(2, 13 * def.Season[i] / top);
            box.AddChild(new ColorRect { Color = c, CustomMinimumSize = new Vector2(6, h), SizeFlagsVertical = Control.SizeFlags.ShrinkEnd, MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        return box;
    }

    static Button StepperBtn(string label, Action on, string tip, bool disabled)
    {
        var b = new Button
        {
            Text = label,
            TooltipText = tip,
            Disabled = disabled,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = disabled ? Control.CursorShape.Arrow : Control.CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(26, 26),
        };
        b.AddThemeFontSizeOverride("font_size", 15);
        b.AddThemeStyleboxOverride("normal", Ui.Pill(new Color("25434e"), new Color(Ui.Rim, 0.6f), 3, 6, 0));
        b.AddThemeStyleboxOverride("hover", Ui.Pill(new Color("315c6b"), Ui.Accent, 3, 6, 0));
        b.AddThemeStyleboxOverride("pressed", Ui.Pill(Ui.Accent.Darkened(0.25f), Ui.Accent, 3, 6, 0));
        b.AddThemeStyleboxOverride("disabled", Ui.Pill(new Color("16262c"), new Color(Ui.Rim, 0.16f), 3, 6, 0));
        b.Pressed += () => Sfx.Play("click");
        b.Pressed += on;
        return b;
    }

    void WorkTab()
    {
        bool steward = S.Steward is { Alive: true };
        int workers = S.Workers().Count, idle = S.CountJob(Work.Gather);

        double inFood = S.ForecastFood(), eat = S.SeasonNeed(), diff = inFood - eat;
        bool short_ = diff < -0.05;
        var sum = new PanelContainer
        {
            TooltipText = "운을 뺀 이번 계절 예상치입니다. 실제 수확은 해마다 조금씩 흔들리며 가뭄·풍년의 영향을 받습니다.",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var sbx = Box(new Color(short_ ? Art.Bad : Art.Good, 0.14f), new Color(short_ ? Art.Bad : Art.Good, 0.7f), 1, 6, 8);
        sbx.ContentMarginLeft = sbx.ContentMarginRight = 10;
        sum.AddThemeStyleboxOverride("panel", sbx);
        var sumRow = H(6);
        sumRow.MouseFilter = Control.MouseFilterEnum.Ignore;
        sumRow.AddChild(new Glyph("food", 18, short_ ? DownCol : UpCol));
        var sumLeft = L($"이번 철 +{inFood:0} 생산 / −{eat:0} 소비", 13, Ink);
        sumLeft.MouseFilter = Control.MouseFilterEnum.Ignore;
        sumLeft.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sumRow.AddChild(sumLeft);
        var sumRight = L(short_ ? $"−{Math.Abs(diff):0} 부족" : $"+{diff:0} 여유", 14, short_ ? DownCol : UpCol);
        sumRight.MouseFilter = Control.MouseFilterEnum.Ignore;
        sumRow.AddChild(sumRight);
        sum.AddChild(sumRow);
        _side.AddChild(sum);

        var list = V(3);
        foreach (var (w, icon) in WorkIcons)
        {
            var def = WorkCatalog.Of(w);
            string why = S.WhyCantWork(w);
            bool blocked = why.Length > 0;
            int n = S.CountJob(w);
            double cap = S.Capacity(w);
            bool open = cap > 1e8;
            bool over = !open && n > cap + 1e-9;
            bool dim = blocked && n == 0;

            string buildHint = "";
            bool buildBad = false;
            if (w == Work.Build)
            {
                int sites = S.Structures.Count(x => !x.Built);
                if (sites > 0 && n == 0) { buildHint = $"공사 {sites}곳 멈춤 — 인원 배정 필요"; buildBad = true; }
                else if (sites == 0 && n > 0) buildHint = "공사 없음 — 빼서 다른 일로";
            }
            string tip = $"{def.Name}\n＋ 얻는 것: {def.Gain}\n− 치르는 값: {def.Cost}"
                         + (buildHint.Length > 0 ? $"\n\n{buildHint}" : "")
                         + (def.Food || w is Work.Wood or Work.Stone ? $"\n계절별 소출(봄·여름·가을·겨울): {string.Join(" · ", def.Season.Select(x => $"{x * def.Mult:0.0}"))}" : "")
                         + (blocked ? $"\n지금 불가: {why}" : "")
                         + (open ? "" : $"\n정원 {cap:0.#}명 (초과 인원은 효율 70%)");
            var row = new PanelContainer { TooltipText = tip, MouseFilter = Control.MouseFilterEnum.Stop };
            var rb = buildHint.Length > 0
                ? Box(new Color(buildBad ? Art.Bad : WarnCol, 0.14f), new Color(buildBad ? Art.Bad : WarnCol, 0.8f), 2, 4, 8)
                : TileBox(n > 0 && !over, dim, over);
            rb.ContentMarginTop = rb.ContentMarginBottom = 3;
            rb.ContentMarginLeft = rb.ContentMarginRight = 7;
            row.AddThemeStyleboxOverride("panel", rb);
            var h = H(6);
            h.MouseFilter = Control.MouseFilterEnum.Ignore;
            row.AddChild(h);
            h.AddChild(new Glyph(icon, 22, dim ? Faint(0.3f) : Colors.White) { MouseFilter = Control.MouseFilterEnum.Ignore });
            var mid = V(0);
            mid.MouseFilter = Control.MouseFilterEnum.Ignore;
            mid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var nm = L(def.Name, 14, dim ? Faint(0.4f) : Ink);
            nm.MouseFilter = Control.MouseFilterEnum.Ignore;
            mid.AddChild(nm);
            if (buildHint.Length > 0)
            {
                var bl = L(buildHint, 11, buildBad ? DownCol : WarnCol, false);
                bl.MouseFilter = Control.MouseFilterEnum.Ignore;
                bl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                mid.AddChild(bl);
            }
            else if (blocked && n == 0)
            {
                string shortWhy = w == Work.Farm ? "농경 제도 도입 필요" : why;
                var wl = L(shortWhy, 11, Faint(0.45f), false);
                wl.MouseFilter = Control.MouseFilterEnum.Ignore;
                wl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                mid.AddChild(wl);
            }
            else if (def.Food || w is Work.Wood or Work.Stone)
            {
                var line = H(6);
                line.MouseFilter = Control.MouseFilterEnum.Ignore;
                line.AddChild(SeasonBars(def, dim));
                double f = n > 0 ? S.Forecast(w) : 0;
                string unit = def.Food ? "" : w == Work.Wood ? " 나무" : " 돌";
                var fl = L(n > 0 ? $"+{f:0.#}{unit}/철" : SeasonWord(def), 11, n > 0 ? (def.Food ? UpCol : Ink) : Faint(0.45f), false);
                fl.MouseFilter = Control.MouseFilterEnum.Ignore;
                line.AddChild(fl);
                mid.AddChild(line);
            }
            else
            {
                string shortGain = w switch
                {
                    Work.Build => "놓아 둔 건물 공사",
                    Work.Scout => "안개 걷어 새 땅·자원 탐색",
                    Work.Guard => "습격 방어 · 도둑질 억제",
                    _ => def.Gain,
                };
                var sl = L(shortGain, 11, Faint(0.55f), false);
                sl.MouseFilter = Control.MouseFilterEnum.Ignore;
                sl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                mid.AddChild(sl);
            }
            h.AddChild(mid);
            var cnt = L(open ? $"{n}명" : $"{n}/{cap:0}", 15, over ? Art.Bad : n > 0 ? Ink : Faint(0.45f));
            cnt.MouseFilter = Control.MouseFilterEnum.Ignore;
            cnt.HorizontalAlignment = HorizontalAlignment.Right;
            cnt.CustomMinimumSize = new Vector2(46, 0);
            h.AddChild(cnt);
            string plusTip = blocked ? why
                : n >= workers ? "모든 일꾼이 이미 이 일을 맡고 있습니다"
                : idle > 0 && w != Work.Gather ? $"{def.Name}에 1명 배정 (채집에서 이동)"
                : $"{def.Name}에 1명 배정 (가장 많은 일에서 이동)";
            h.AddChild(StepperBtn("−", () => { if (S.Assign(w, -1)) Game.Touched(); }, $"{def.Name}에서 1명 빼기 (채집으로 복귀)", steward || n == 0 || w == Work.Gather));
            h.AddChild(StepperBtn("+", () => { if (S.Assign(w, 1)) Game.Touched(); }, plusTip, steward || blocked || n >= workers));
            list.AddChild(row);
        }
        _side.AddChild(list);
        StewardBox();
    }

    void StewardBox()
    {
        if (S.Steward is { Alive: true } st)
        {
            var head = H(4);
            head.AddChild(new Glyph("steward", 18, Ui.Accent));
            var nm = L($"관리인 {st.Name} (자동 배분 중)", 13);
            nm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            nm.TooltipText = $"{S.P(st)}\n＋ 얻는 것: 식량 일 정원이 늘고 계절마다 방침대로 자동 배분합니다.\n− 치르는 값: 창고에서 조금씩 제 몫을 챙기며 위신이 오릅니다.";
            nm.MouseFilter = Control.MouseFilterEnum.Pass;
            head.AddChild(nm);
            head.AddChild(B("해임", () => { if (S.AppointSteward(-1, S.Policy)) Game.Touched(); }, "관리인을 내보내고 다시 직접 인원을 나눕니다", false, 12));
            _side.AddChild(head);
            _side.AddChild(PolicyRow(st.Id, true));
            return;
        }
        var cand = S.StewardCandidate();
        if (cand == null)
        {
            int adults = S.Living.Count(p => p.Age(S.Tick) >= 14);
            var h = H(4);
            h.TooltipText = "인구가 늘어나면 일손 배분을 자동화하는 관리인을 임명할 수 있습니다.";
            h.MouseFilter = Control.MouseFilterEnum.Pass;
            h.AddChild(new Glyph("steward", 16, Faint(0.35f)));
            h.AddChild(L($"관리인 자동 배분: 어른 {adults}/{Simulation.StewardThreshold}명부터 가능", 12, Faint(0.55f)));
            _side.AddChild(h);
            return;
        }
        var head2 = H(4);
        head2.AddChild(new Glyph("steward", 18, Ui.Accent));
        var l = L($"관리인 임명 가능: {cand.Name} (방침 클릭)", 13);
        l.TooltipText = $"후보 {S.P(cand)}\n방침을 고르면 계절마다 일손을 자동 배분합니다 (대가: 창고에서 소량 착복).";
        l.MouseFilter = Control.MouseFilterEnum.Pass;
        head2.AddChild(l);
        _side.AddChild(head2);
        _side.AddChild(PolicyRow(cand.Id, false));
    }

    Control PolicyRow(int who, bool sitting)
    {
        var row = H(4);
        for (int i = 0; i < WorkCatalog.PolicyNames.Length; i++)
        {
            var pol = (StewardPolicy)i;
            bool active = sitting && S.Policy == pol;
            var b = B(WorkCatalog.PolicyNames[i], () => { if (S.AppointSteward(who, pol)) Game.Touched(); }, sitting ? "관리인의 배분 방침 변경" : "이 방침으로 관리인 임명", false, 12);
            b.ToggleMode = sitting;
            if (sitting) b.ButtonPressed = active;
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.CustomMinimumSize = new Vector2(0, 26);
            foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed" })
            {
                bool on = st is "pressed" or "hover_pressed" || active;
                b.AddThemeStyleboxOverride(st, Ui.Pill(on ? new Color("2d5e6e") : st == "hover" ? Ui.Surface.Lightened(0.12f) : Ui.Surface,
                    on || st == "hover" ? Ui.Accent : new Color(Ui.Rim, 0.5f), 4, 7, 5));
            }
            row.AddChild(b);
        }
        return row;
    }

    string WhyCantStart(StructureDef d)
    {
        if (d.Need.Length > 0 && Catalog.Find(d.Need) is { } inst && !inst.Get(S.Inst)) return $"{inst.Name} 제도 필요";
        if (S.Wood < d.Wood - 1e-9) return $"나무 부족 ({S.Wood:0.#}/{d.Wood:0})";
        if (S.Stone < d.Stone - 1e-9) return $"돌 부족 ({S.Stone:0.#}/{d.Stone:0})";
        return "";
    }

    void BuildTab()
    {
        var going = S.Structures.Where(x => !x.Built).OrderBy(x => x.Id).ToList();
        if (going.Count > 0)
        {
            int builders = S.CountJob(Work.Build);
            var head = H(4);
            head.AddChild(new Glyph("build", 16, builders == 0 ? Art.Bad : Ui.Accent));
            var hl = L(builders == 0 ? $"공사 중 {going.Count}곳 — 건설 일꾼 없음!" : $"공사 중 {going.Count}곳 · 건설 일꾼 {builders}명", 13,
                builders == 0 ? Art.Bad : Faint(0.8f));
            hl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(hl);
            if (builders == 0 && S.Steward == null)
                head.AddChild(B("+1 배정", () => { if (S.Assign(Work.Build, 1)) Game.Touched(); }, "채집하던 사람을 건설로 옮깁니다", false, 12, "build"));
            _side.AddChild(head);
            foreach (var st in going.Take(3))
            {
                var row = H(5);
                var s = st;
                row.AddChild(new StructIcon(st.Kind, (float)(st.Done / st.Work)) { CustomMinimumSize = new Vector2(26, 26) });
                var nm = L(StructureCatalog.Of(st.Kind).Name, 13);
                nm.CustomMinimumSize = new Vector2(64, 0);
                row.AddChild(nm);
                var m = Meter(st.Done / st.Work, 100, Art.Gold, 6);
                m.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(m);
                row.AddChild(B("", () => Game.FocusCell(s.Cell), "공사 위치 보기", false, 12, "eye"));
                row.AddChild(B("", () => { if (S.Raze(s.Cell)) Game.Touched(); }, "공사 취소 (자재 절반 반환)", false, 12, "close"));
                _side.AddChild(row);
            }
            _side.AddChild(new HSeparator());
        }

        bool placing = Game.Placing != null;
        _side.AddChild(L(Game.Placing is { } pk
                ? $"▶ {StructureCatalog.Of(pk).Name} 배치: 초록 칸 클릭 (Shift 연속 · 우클릭 취소)"
                : "건물을 고르면 지을 수 있는 초록 칸이 지도에 표시됩니다.",
            12, placing ? Art.Good : Faint(0.7f), true, SideW));

        // 3열 카드 배치(102×82): 그림과 이름·자재 값이 시원하고 또렷하게 보인다.
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var d in StructureCatalog.All)
        {
            var def = d;
            string why = WhyCantStart(d);
            bool on = Game.Placing == d.Kind;
            bool off = why.Length > 0 && !on;
            string cost = (d.Wood > 0 ? $"나무 {d.Wood:0}" : "") + (d.Wood > 0 && d.Stone > 0 ? " · " : "") + (d.Stone > 0 ? $"돌 {d.Stone:0}" : "");
            int built = S.BuiltCount(d.Kind);
            string tip = d.Name + (built > 0 ? $" (보유 {built}채)" : "") + $"\n자재: {cost} · 일손 {d.Work:0}\n＋ 효과: {d.Gain}\n− 대가: {d.Cost}\n조건: {d.Rule}" + (why.Length > 0 ? $"\n\n지금 불가: {why}" : "");
            var tile = new Button
            {
                TooltipText = tip,
                CustomMinimumSize = new Vector2(102, 82),
                FocusMode = Control.FocusModeEnum.None,
                Disabled = off,
                MouseDefaultCursorShape = off ? Control.CursorShape.Arrow : Control.CursorShape.PointingHand,
            };
            var bg = TileBox(on, off);
            tile.AddThemeStyleboxOverride("normal", bg);
            tile.AddThemeStyleboxOverride("disabled", bg);
            tile.AddThemeStyleboxOverride("hover", Box(new Color(Ui.Accent, 0.16f), Ui.Accent, 2, 4, 8));
            tile.AddThemeStyleboxOverride("pressed", bg);
            tile.Pressed += () => Sfx.Play("click");
            tile.Pressed += () => { if (Game.Placing == def.Kind) Game.StopPlacing(); else Game.StartPlacing(def.Kind); };

            var col = V(-2);
            col.MouseFilter = Control.MouseFilterEnum.Ignore;
            col.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, 3);
            tile.AddChild(col);
            col.AddChild(new StructIcon(d.Kind) { Modulate = off ? new Color(1, 1, 1, 0.38f) : Colors.White, CustomMinimumSize = new Vector2(94, 44) });
            var name = L(d.Name, 13, off ? Faint(0.42f) : Ink);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            col.AddChild(name);
            bool needInst = why.EndsWith("제도 필요");
            string subCost = needInst && !on ? why : cost;
            var c2 = L(subCost, 11, on ? Art.Good : why.Length > 0 ? Art.Bad : new Color("d8c596"));
            c2.HorizontalAlignment = HorizontalAlignment.Center;
            c2.MouseFilter = Control.MouseFilterEnum.Ignore;
            c2.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            col.AddChild(c2);

            if (built > 0)
            {
                var badge = L($"{built}", 11, Colors.White);
                var bsb = Box(new Color(Ui.Accent, 0.92f), new Color(0, 0, 0, 0), 0, 2, 99);
                bsb.ContentMarginLeft = bsb.ContentMarginRight = 5;
                badge.AddThemeStyleboxOverride("normal", bsb);
                badge.Position = new Vector2(76, 4);
                badge.MouseFilter = Control.MouseFilterEnum.Ignore;
                tile.AddChild(badge);
            }
            grid.AddChild(tile);
        }
        _side.AddChild(grid);
    }
}
