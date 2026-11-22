using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>가운데에 뜨는 창: 결정, 법전, 기술, 나라, 연대기, 저장·불러오기, 설정, 새 판.</summary>
public partial class Hud
{
    static Color Section => Ui.Accent.Darkened(0.15f);

    void ShowModal(Control content, string window)
    {
        if (Game.Placing != null) Game.StopPlacing();
        HideSideTip();
        Clear(_modalPanel);
        _modalPanel.AddChild(content);
        _modalPanel.ResetSize();
        _modalHost.Visible = true;
        _window = window;
    }

    public void CloseModal()
    {
        _modalHost.Visible = false;
        _window = "";
        Clear(_modalPanel);
    }

    VBoxContainer Window(string title, out VBoxContainer body, bool scroll = true, string icon = "", float width = 980, float height = 600)
    {
        var v = V(10);
        var head = H(10);
        if (icon.Length > 0) head.AddChild(new Glyph(icon, 26, Ui.Accent));
        head.AddChild(L(title, 24));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = B("", CloseModal, "닫기 (Esc)", false, 16, "close");
        close.CustomMinimumSize = new Vector2(38, 38);
        head.AddChild(close);
        v.AddChild(head);
        v.AddChild(new HSeparator());
        body = V(8);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (scroll)
        {
            var sc = new ScrollContainer { CustomMinimumSize = new Vector2(width, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            sc.AddChild(body);
            v.AddChild(sc);
        }
        else
        {
            body.CustomMinimumSize = new Vector2(width, height);
            v.AddChild(body);
        }
        return v;
    }

    public void Open(string which)
    {
        switch (which)
        {
            case "law": LawWindow(); break;
            case "tech": TechWindow(); break;
            case "realm": RealmWindow(); break;
            case "log": LogWindow(); break;
            case "people": PeopleWindow(); break;
            case "save": SaveWindow(true); break;
            case "load": SaveWindow(false); break;
            case "settings": SettingsWindow(); break;
            case "new": AskNewGame(); break;
        }
    }

    /// <summary>승리를 이뤘다. 판은 이어진다.</summary>
    public void ShowVictory(Course c)
    {
        Sfx.Play("bell");
        var vd = Simulation.Victories[(int)c];
        var v = V(12);
        v.AddChild(Head("realm", $"승리 — {Simulation.CourseName(c)}의 길", 15, Art.Gold));
        v.AddChild(L(vd.Name, 30));
        string line = S.Log.Select(e => e.Text).LastOrDefault(t => t.StartsWith($"【승리】 {vd.Name}")) ?? vd.Goal;
        v.AddChild(L(line.Replace($"【승리】 {vd.Name}: ", ""), 17, null, true, 560));
        v.AddChild(L($"제 {S.Year}년 · {S.StageName}. 이룬 승리 {S.Won.Count}/{Simulation.Victories.Count}", 14, Faint(0.7f)));
        v.AddChild(new HSeparator());
        v.AddChild(L("승리한 뒤에도 역사는 이어집니다. 다른 길의 승리도 노릴 수 있습니다.", 14, Faint(0.72f), true, 560));
        var foot = H();
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foot.AddChild(B("나라 보기", () => { CloseModal(); RealmWindow(); }, "", false, 16, "realm"));
        foot.AddChild(B("계속하기", CloseModal, "", false, 16, "checked"));
        v.AddChild(foot);
        ShowModal(v, "victory");
    }

    string _lawFilter = "all";
    string _lawSel = "";

    /// <summary>제도의 상태: 0 시행 중, 1 지금 들일 수 있음, 2 잠김.</summary>
    int LawState(InstitutionDef d) => d.Get(S.Inst) ? 0 : S.WhyUnavailable(d).Length == 0 ? 1 : 2;


    /// <summary>
    /// 법전: 왼쪽은 시대별 열로 늘어선 제도 나무(초록 = 시행 중, 파랑 = 지금 들일 수 있음, 회색 = 잠김),
    /// 오른쪽은 고른 제도의 얻는 것·치르는 값·좋아하고 반발하는 계층과 도입 단추.
    /// </summary>
    public void OpenLaw(string key) { _lawSel = key; LawWindow(); }

    void LawWindow()
    {
        var win = Window($"법전 — {S.StageName}, {S.EraName}", out var body, false, "law", 1180, 600);
        var shown = Catalog.All.Where(d => d.Era <= S.Realm.Era + 1).ToList();
        int nOn = shown.Count(d => LawState(d) == 0), nCan = shown.Count(d => LawState(d) == 1);
        if (Catalog.Find(_lawSel) is not { } sel || !shown.Contains(sel))
            sel = shown.FirstOrDefault(d => LawState(d) == 1) ?? shown.First();
        _lawSel = sel.Key;
        // 법전을 열어 본 것은 알린 것으로 친다(새 제도 배지가 사라진다).
        bool sawNew = false;
        foreach (var d in shown) if (LawState(d) == 1 && _lawSeen.Add(d.Key)) sawNew = true;
        if (sawNew) Refresh();

        // 위: 요약과 거르개
        var top = H(10);
        top.AddChild(Tag($"● 시행 중 {nOn}", LawOn, LawOnRim));
        top.AddChild(Tag($"● 도입 가능 {nCan}", LawCan, LawCanRim));
        top.AddChild(Tag($"● 잠김 {shown.Count - nOn - nCan}", LawLocked, LawLockedRim));
        top.AddChild(L("  제도를 누르면 오른쪽에 자세히 나옵니다.", 14, Faint(0.6f)));
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foreach (var (key, name) in new[] { ("all", "전체"), ("on", "시행 중"), ("can", "도입 가능") })
        {
            var k = key;
            var f = B(name, () => { _lawFilter = k; LawWindow(); }, "", false, 14);
            f.ToggleMode = true;
            f.SetPressedNoSignal(_lawFilter == key);
            top.AddChild(f);
        }
        body.AddChild(top);

        var split = H(14);
        split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(split);

        // 왼쪽: 시대별 열
        var sc = new ScrollContainer { CustomMinimumSize = new Vector2(760, 540), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        split.AddChild(sc);
        var cols = H(6);
        sc.AddChild(cols);
        bool firstCol = true;
        foreach (var group in shown.GroupBy(d => d.Era).OrderBy(g => g.Key))
        {
            var list = group.Where(d => _lawFilter switch
            {
                "on" => LawState(d) == 0,
                "can" => LawState(d) == 1,
                _ => true,
            }).ToList();
            if (!firstCol)
            {
                var arrow = new CenterContainer { CustomMinimumSize = new Vector2(16, 0) };
                arrow.AddChild(L("›", 26, Faint(0.35f)));
                cols.AddChild(arrow);
            }
            firstCol = false;
            var col = V(5);
            col.CustomMinimumSize = new Vector2(196, 0);
            var head = V(0);
            head.AddChild(L($"{Catalog.EraNames[(int)group.Key]} 시대", 17, group.Key > S.Realm.Era ? Faint(0.5f) : Section));
            head.AddChild(L(group.Key > S.Realm.Era ? "다음 시대" : $"{group.Count(d => LawState(d) == 0)}/{group.Count()} 시행", 12, Faint(0.55f)));
            col.AddChild(head);
            col.AddChild(new HSeparator());
            foreach (var d in list) col.AddChild(LawChip(d, d.Key == _lawSel));
            cols.AddChild(col);
        }

        // 오른쪽: 고른 제도
        var detail = new PanelContainer { CustomMinimumSize = new Vector2(380, 540) };
        detail.AddThemeStyleboxOverride("panel", Box(new Color("10232a", 0.95f), new Color(1, 1, 1, 0.08f), 1, 16, 12));
        detail.AddChild(LawDetail(sel));
        split.AddChild(detail);
        ShowModal(win, "law");
    }

    static readonly Color LawOn = new("2e463e"), LawOnRim = new("a1c587");
    static readonly Color LawCan = new("263e46"), LawCanRim = new("78c5cf");
    static readonly Color LawLocked = new("1b3038"), LawLockedRim = new("40575b");

    static PanelContainer Tag(string text, Color bg, Color rim)
    {
        var p = new PanelContainer();
        var sb = Box(bg, rim, 1, 4, 999);
        sb.ContentMarginLeft = sb.ContentMarginRight = 12;
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(L(text, 14));
        return p;
    }

    /// <summary>나무의 마디 하나: 상태 색 테두리, 이름, 상태 점. 누르면 오른쪽에 자세히.</summary>
    Button LawChip(InstitutionDef d, bool picked)
    {
        int st = LawState(d);
        var (bg, rim) = st switch { 0 => (LawOn, LawOnRim), 1 => (LawCan, LawCanRim), _ => (LawLocked, LawLockedRim) };
        string mark = st switch { 0 => "● ", 1 => "+ ", _ => "" };
        // 문명 6의 정책 카드처럼: 누르지 않아도 마우스만 올리면 얻는 것과 치르는 값이 함께 보인다.
        string tip = $"{d.Name}\n＋ 얻는 것: {d.Gain}\n－ 치르는 값: {d.Cost}" + (st == 2 ? $"\n\n잠김 — 필요: {S.WhyUnavailable(d)}" : "");
        var b = B(mark + d.Name, () => { _lawSel = d.Key; LawWindow(); }, tip, false, 15);
        b.Alignment = HorizontalAlignment.Left;
        b.CustomMinimumSize = new Vector2(196, 34);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
        {
            var sb = Box(state == "hover" ? bg.Lightened(0.12f) : bg, picked ? new Color("f3d58b") : rim, picked ? 2 : 1, 6, 8);
            sb.BorderWidthLeft = 5;
            sb.ContentMarginLeft = 10;
            b.AddThemeStyleboxOverride(state, sb);
        }
        b.AddThemeColorOverride("font_color", st == 2 ? Faint(0.5f) : Ink);
        return b;
    }

    /// <summary>고른 제도의 자세한 설명: 얻는 것(초록)·치르는 값(빨강)·계층의 편·조건·단추.</summary>
    VBoxContainer LawDetail(InstitutionDef d)
    {
        int st = LawState(d);
        var v = V(8);
        var head = H(8);
        head.AddChild(new Glyph("law", 30, st == 0 ? LawOnRim : st == 1 ? LawCanRim : Faint(0.5f)));
        var name = L(d.Name, 24, Ink);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(name);
        head.AddChild(Tag(st switch { 0 => "시행 중", 1 => "도입 가능", _ => "잠김" }, st == 0 ? LawOn : st == 1 ? LawCan : LawLocked, st == 0 ? LawOnRim : st == 1 ? LawCanRim : LawLockedRim));
        v.AddChild(head);
        v.AddChild(L($"{Catalog.EraNames[(int)d.Era]} 시대 · {Catalog.StageNames[(int)d.MinStage]}부터", 13, Faint(0.55f)));

        void Part(string title, string text, Color col, string sign)
        {
            v.AddChild(L(title, 15, col));
            foreach (var s in Bullets(text))
                v.AddChild(L($"{sign}  {s}", 15, Ink, true, 340));
        }
        Part("얻는 것", d.Gain, Art.Good, "+");
        Part("치르는 값", d.Cost, Art.Bad, "−");

        if (d.Winners.Any() || d.Losers.Any())
        {
            v.AddChild(new HSeparator());
            if (d.Winners.Any()) v.AddChild(L("▲ 좋아하는 계층: " + string.Join(", ", d.Winners.Select(e => Catalog.EstateNames[(int)e])), 14, Art.Good.Darkened(0.1f), true, 340));
            if (d.Losers.Any()) v.AddChild(L("▼ 반발하는 계층: " + string.Join(", ", d.Losers.Select(e => Catalog.EstateNames[(int)e])), 14, Art.Bad, true, 340));
        }
        if (TechTree.Gate.TryGetValue(d.Key, out var tk) && TechTree.Find(tk) is { } t)
            v.AddChild(L($"여는 기술: {t.Name}" + (S.Knows(tk) ? " (앎)" : " (아직 모름)"), 13, Faint(0.6f)));

        v.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        v.AddChild(new HSeparator());
        if (st == 2)
            v.AddChild(L("아직 들일 수 없습니다 — " + S.WhyUnavailable(d), 14, Faint(0.7f), true, 340));
        else
        {
            // 이 줄은 제도 자체의 값(위)이 아니라 바꾸는 순간 한 번 치르는 값이다.
            v.AddChild(L((st == 0 ? "폐지하면 곧바로: " : "도입하면 곧바로: ") + S.PreviewReform(d).Text, 13, Faint(0.7f), true, 340));
            var b = B(st == 0 ? "폐지하기" : "도입하기", () => AskEnact(d, st != 0), "", false, 18, st == 0 ? "close" : "law");
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(b);
        }
        return v;
    }

    void AskEnact(InstitutionDef d, bool on)
    {
        var pre = S.PreviewReform(d);
        var v = V(12);
        v.AddChild(L(on ? $"{Josa.Eul(d.Name)} 도입할까요?" : $"{Josa.Eul(d.Name)} 폐지할까요?", 24));
        v.AddChild(L($"효과: {d.Gain}\n대가: {d.Cost}", 15, null, true, 560));
        if (d.Winners.Any()) v.AddChild(L("지지하는 계층: " + string.Join(", ", d.Winners.Select(e => Catalog.EstateNames[(int)e])), 14, Art.Good.Darkened(0.2f)));
        if (d.Losers.Any()) v.AddChild(L("반발하는 계층: " + string.Join(", ", d.Losers.Select(e => Catalog.EstateNames[(int)e])), 14, Art.Bad));
        v.AddChild(new HSeparator());
        v.AddChild(L((on ? "도입하면 곧바로: " : "폐지하면 곧바로: ") + pre.Text, 15, null, true, 560));
        var row = H();
        row.AddChild(B(on ? "도입" : "폐지", () =>
        {
            bool ok = S.Enact(d, on);
            if (!ok) Toast($"{d.Name}: 지금은 할 수 없습니다. {S.WhyUnavailable(d)}", Art.Bad);
            else Sfx.Play("coin");
            Game.Touched();
            LawWindow();
        }, "", false, 18, "law"));
        row.AddChild(B("취소", LawWindow, "", false, 18, "back"));
        v.AddChild(row);
        ShowModal(v, "ask");
    }

    void RealmWindow()
    {
        var r = S.Realm;
        var win = Window($"나라 — {S.StageName} · 난이도 {Simulation.DifficultyName(S.Difficulty)}", out var body, true, "realm");
        var cols = H(24);
        body.AddChild(cols);
        var a = V(5);
        a.CustomMinimumSize = new Vector2(440, 0);
        var b = V(5);
        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cols.AddChild(a);
        cols.AddChild(b);

        a.AddChild(L("현황", 18, Section));
        string popStr = S.Tribal ? $"{S.Living.Count()}명" : Art.Num(r.Pop);
        var bf = !S.Tribal ? S.ForecastTreasury() : default;
        double outTotal = bf.BaseExpense + bf.Upkeep + bf.Drain;
        string budgetLine = S.Tribal
            ? $"공동 창고 {S.CommonStore:0}/{S.StoreCap():0} · 나무 {S.Wood:0} · 돌 {S.Stone:0}\n"
            : $"국고 {r.Treasury:0.00} (연간 예상 {(bf.Net >= 0 ? "+" : "−")}{Math.Abs(bf.Net):0.00} = 세입 {bf.Income:0.00} − 지출 {outTotal:0.00})\n";
        a.AddChild(L($"인구 {popStr} · 영토 {S.Lands(0).Count()} (통치 한도 {S.AdminCap})\n"
                     + budgetLine
                     + $"지식 {r.Knowledge:0} · 도시 인구 {Pct(r.Urban)} · 오염 {Pct(r.Pollution)}\n"
                     + $"불만 {Pct(r.Unrest)} · 억눌린 불만 {Pct(r.Suppressed)}\n"
                     + $"{(S.Tribal ? "우두머리" : "통치자")} 지지 {Pct(r.Legitimacy)} · 단합 {Pct(r.Cohesion)}\n"
                     + $"불평등 {Pct(r.Inequality)} · 부패 {Pct(r.Corruption)}\n"
                     + $"군사력 {S.ArmyStrength():0.00} · 군단 {S.Armies.Count(x => x.Owner == 0)}/{S.ArmyCap}", 15));

        var knownRes = ResourceCatalog.All.Where(d => S.Revealed(d.Kind)).ToList();
        if (knownRes.Count > 0)
        {
            a.AddChild(L("전략 자원", 18, Section));
            var resFlow = new HFlowContainer { CustomMinimumSize = new Vector2(430, 0) };
            resFlow.AddThemeConstantOverride("h_separation", 6);
            resFlow.AddThemeConstantOverride("v_separation", 6);
            foreach (var rd in knownRes)
            {
                double sup = S.Supply(rd.Kind);
                int own = S.OwnDeposits(rd.Kind);
                string status = own > 0 ? $"자급 ({own}곳)" : sup > 0 ? "교역 수입" : "없음";
                var col = own > 0 ? Art.Good : sup > 0 ? Art.Gold : Art.Bad;
                var chip = new PanelContainer { TooltipText = $"{rd.Name} — {status}\n효과: {rd.Use}", MouseFilter = Control.MouseFilterEnum.Stop };
                var csb = Box(new Color(col, 0.14f), new Color(col, 0.65f), 1, 4, 7);
                csb.ContentMarginLeft = 7; csb.ContentMarginRight = 9;
                chip.AddThemeStyleboxOverride("panel", csb);
                var ch = H(5);
                ch.MouseFilter = Control.MouseFilterEnum.Ignore;
                ch.AddChild(new Glyph(Art.ResourceIcon(rd.Kind), 18, Colors.White));
                var cl = L($"{rd.Name} · {status}", 13, Ink);
                cl.MouseFilter = Control.MouseFilterEnum.Ignore;
                ch.AddChild(cl);
                chip.AddChild(ch);
                resFlow.AddChild(chip);
            }
            a.AddChild(resFlow);
        }

        a.AddChild(L("계층", 18, Section));
        if (S.Tribal)
            a.AddChild(L("아직 계층이 나뉘지 않은 공동체입니다. 나라가 세워지면 귀족·평민·예속민·노동자의 비중과 분노가 여기에 표시됩니다.", 14, Faint(0.7f), true, 430));
        else
            foreach (var e in Enum.GetValues<Estate>())
            {
                double anger = S.EstateAnger(e);
                var row = H();
                row.AddChild(L(Catalog.EstateNames[(int)e], 15, null, false, 80));
                row.AddChild(L($"비중 {Pct(r.Share[(int)e])}", 14, null, false, 80));
                row.AddChild(Meter(anger, 170, anger > 0.6 ? Art.Bad : Art.Gold, 12));
                row.AddChild(L($"분노 {Pct(anger)}", 14, anger > 0.6 ? Art.Bad : null));
                a.AddChild(row);
            }

        a.AddChild(L("발전 조건", 18, Section));
        string stage = S.NextStageNeeds(), era = S.NextEraNeeds();
        string nextNeeds = (stage.Length > 0 ? $"다음 단계: {stage}\n" : "") + (era.Length > 0 ? $"다음 시대: {era}" : "");
        a.AddChild(L(nextNeeds.Length > 0 ? nextNeeds.TrimEnd() : "최고 단계(제국)와 마지막 시대(현대)에 도달했습니다.", 14, null, true, 430));
        if (r.Falls.Count > 0)
        {
            a.AddChild(L("몰락의 기록", 18, Section));
            a.AddChild(L(string.Join("\n", r.Falls.Select(f => f.Split('|')).Select(f => $"제 {f[0]}년 — {f[1]}")), 14));
        }

        b.AddChild(L("길과 승리", 18, Section));
        b.AddChild(L(string.Join(" · ", Enumerable.Range(0, 4).Select(k => $"{Simulation.CourseNames[k]} {S.CourseLevel((Course)k)}단계"))
                     + $"   기질: {Simulation.CourseName(S.Leaning())}   공포 {r.Dread:0}", 14, null, true, 440));
        static string CourseIcon(Course c) => c switch
        {
            Course.Conquest => "conquest",
            Course.Commerce => "commerce",
            Course.Terror => "terror",
            Course.Tyranny => "tyranny",
            _ => "tech",
        };
        foreach (var vd in Simulation.Victories)
        {
            var (needs, streak, hold) = S.VictoryNeeds(vd.Course);
            double p = S.VictoryProgress(vd.Course);
            var won = S.Won.FirstOrDefault(w => w.Course == vd.Course);
            var row = H(8);
            row.AddChild(new Glyph(CourseIcon(vd.Course), 20, Colors.White));
            row.AddChild(L(vd.Name, 15, won.Year > 0 ? Art.Good.Darkened(0.15f) : null, false, 96));
            row.AddChild(Meter(p, 114, won.Year > 0 ? Art.Good : Ui.Accent, 10));
            row.AddChild(L(won.Year > 0 ? $"제 {won.Year}년에 이룸" : hold > 1 ? $"{streak}/{hold}년" : $"{p * 100:0}%", 13, Faint(0.7f)));
            b.AddChild(row);
            if (won.Year == 0)
            {
                // 문명 6의 승리 화면처럼: 조건마다 체크 칸 하나 — 이룬 것은 초록 ✓, 남은 것은 흐린 ○.
                var flow = new HFlowContainer { CustomMinimumSize = new Vector2(440, 0) };
                flow.AddThemeConstantOverride("h_separation", 12);
                flow.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
                foreach (var n in needs)
                    flow.AddChild(L((n.ok ? "✓ " : "○ ") + n.what, 12, n.ok ? new Color("8fd47e") : Faint(0.55f)));
                b.AddChild(flow);
            }
        }

        b.AddChild(L("이웃 나라", 18, Section));
        if (S.Rivals.Count == 0) b.AddChild(L(S.Tribal ? "아직 주변에 나라가 없습니다. 이웃 무리는 왼쪽 무리 패널에서 볼 수 있습니다." : "알려진 이웃 나라가 없습니다.", 14, null, true, 440));
        foreach (var v in S.Rivals.OrderBy(v => v.Id))
        {
            var rCard = new PanelContainer();
            rCard.AddThemeStyleboxOverride("panel", Box(new Color(Ui.Surface, 0.55f), new Color(Art.OwnerColor(v.Id), 0.65f), 1, 8, 8));
            var rv = V(5);
            rCard.AddChild(rv);
            var row = H(8);
            var chip = new Panel { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            chip.AddThemeStyleboxOverride("panel", Box(Art.OwnerColor(v.Id), new Color(1, 1, 1, 0.9f), 2, 0, 999));
            row.AddChild(chip);
            string flags = (v.Allied ? " · 동맹" : "") + (v.Trade ? " · 교역" : "") + (v.Faith != 0 ? " · 다른 신앙" : "") + (v.Advance > r.Knowledge ? " · 기술 우위" : "")
                           + (v.TreatyUntil > S.Year ? $" · 화친 {v.TreatyUntil - S.Year}년" : "");
            var rl = L($"{v.Name}  영토 {S.Lands(v.Id).Count()} · 국력 {v.Strength:0.00} · 적대감 {Pct(v.Hostility)}{flags}", 14, v.Hostility > 0.6 ? Art.Bad : null, true, 410);
            rl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(rl);
            int seat = v.Seat;
            bool canSee = seat >= 0 && seat < S.Map.Provinces.Count && S.Map.Known(S.Map[seat]);
            row.AddChild(B("", () => { CloseModal(); Game.Focus(seat); Game.Select(seat); },
                canSee ? "지도에서 보기" : seat < 0 ? "아직 지도에 자리가 없습니다" : "아직 탐사하지 못한 지역입니다", !canSee, 13, "eye"));
            rv.AddChild(row);
            rv.AddChild(RivalEnvoyRow(v, RealmWindow));
            b.AddChild(rCard);
        }
        ShowModal(win, "realm");
    }

    /// <summary>
    /// 연대기 창의 거르개 무게. Chronicle.Weight는 꼬리표 없는 큰일(굶어 죽음, 살인, 도둑질이 들킴 같은 사람들의 이야기)을 1로 두어
    /// "주요 사건"에서 빠뜨린다. 창에서는 시뮬레이션이 큰일(Major)로 적은 것은 모두 주요 사건으로 본다.
    /// (알림·멈춤을 정하는 OnLog는 그대로 Chronicle.Weight를 쓴다.)
    /// </summary>
    static int LogWeight(LogEntry e)
    {
        int w = Chronicle.Weight(e);
        return w == 1 && Chronicle.Tag(e.Text).Length == 0 ? 2 : w;
    }

    void LogWindow()
    {
        var win = Window("연대기", out var body, false, "log");
        static string CatIcon(string cat) => cat switch
        {
            "전쟁" => "raid",
            "재앙" => "cat_disaster",
            "기술" => "tech",
            "살림" => "food",
            "외교" => "cat_diplomacy",
            "정치" => "law",
            "사람" => "people",
            _ => "",
        };
        var filters = H(4);
        void Cat(string name, string key)
        {
            var bt = B(name, () => { _logCat = key; LogWindow(); }, "", false, 14, CatIcon(key));
            bt.ToggleMode = true;
            bt.SetPressedNoSignal(_logCat == key);
            filters.AddChild(bt);
        }
        Cat("모두", "");
        foreach (string c in new[] { "전쟁", "재앙", "기술", "살림", "외교", "정치", "사람" }) Cat(c, c);
        filters.AddChild(new VSeparator());
        void Min(string name, int w)
        {
            var bt = B(name, () => { _logMin = w; LogWindow(); }, "", false, 14);
            bt.ToggleMode = true;
            bt.SetPressedNoSignal(_logMin == w);
            filters.AddChild(bt);
        }
        Min("중대 사건만", 3);
        Min("주요 사건", 2);
        Min("사소한 일까지", 1);
        Min("전부", 0);
        body.AddChild(filters);

        var rows = S.Log.Where(e => LogWeight(e) >= _logMin && (_logCat.Length == 0 || Chronicle.Category(e) == _logCat)).ToList();
        var sb = new System.Text.StringBuilder();
        foreach (var e in Enumerable.Reverse(rows).Take(300))
        {
            int w = LogWeight(e);
            string text = e.Text.Replace("[", "[lb]");
            string when = $"[color=#8fa8b2]제 {e.Tick / 4 + 1}년 {Simulation.SeasonNames[e.Tick % 4]}[/color]  ";
            sb.Append(when);
            sb.Append(w >= 3 ? $"[b][color=#ff9e80]{text}[/color][/b]" : w <= 1 ? $"[color=#a5b5b0]{text}[/color]" : text);
            sb.Append("\n\n");
        }
        if (rows.Count == 0) sb.Append("이 분류에는 아직 기록이 없습니다.");
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true, Text = sb.ToString(), SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(980, 540),
        };
        rich.AddThemeFontSizeOverride("normal_font_size", 15);
        rich.AddThemeFontSizeOverride("bold_font_size", 15);
        body.AddChild(rich);
        body.AddChild(L($"{rows.Count}건 중 최근 {Math.Min(300, rows.Count)}건 표시 · 전체 기록 {S.Log.Count}건", 13, Faint(0.6f)));
        ShowModal(win, "log");
    }

    // ───────── 저장과 불러오기 ─────────

    void SaveWindow(bool saving)
    {
        var win = Window(saving ? "저장" : "불러오기", out var body, true, saving ? "save" : "load", 760, 520);
        body.AddChild(L(saving
            ? "슬롯을 골라 현재 게임을 저장합니다. 저장 파일에는 맵 시드와 지금까지 내린 명령이 기록되며, 불러올 때 처음부터 다시 진행해 같은 순간으로 돌아옵니다."
            : "저장한 게임을 불러옵니다. 오래 진행한 게임일수록 몇 초 더 걸립니다.", 14, Faint(0.72f), true, 740));
        foreach (string slot in Main.Slots)
        {
            string? text = Main.ReadSlot(slot);
            var info = text != null ? SaveGame.Peek(text) : null;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", Box(new Color(Ui.Surface, info != null ? 1f : 0.5f), new Color(Ui.Rim, info != null ? 0.65f : 0.25f), 1, 10, 16));
            var h = H(12);
            row.AddChild(h);
            h.AddChild(new Glyph(slot == "auto" ? "load" : slot == "quick" ? "bolt" : "save", 22, info != null ? Ui.Accent : Faint(0.35f)));
            var t = V(1);
            t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            t.AddChild(L(Main.SlotName(slot), 16));
            if (info != null)
            {
                string when = Main.SlotTime(slot);
                t.AddChild(L($"{(info.Label.Length > 0 ? info.Label + " · " : "")}제 {info.Tick / 4 + 1}년 {Simulation.SeasonNames[info.Tick % 4]} · {Simulation.DifficultyName(info.Difficulty)} · 맵 시드 {info.Seed}{(when.Length > 0 ? " · " + when : "")}", 13, Faint(0.7f)));
            }
            else t.AddChild(L(text != null ? "읽을 수 없는 파일" : "빈 슬롯", 13, Faint(0.5f)));
            h.AddChild(t);
            string s = slot;
            if (saving)
            {
                if (slot != "auto")
                    h.AddChild(B(info != null ? "덮어쓰기" : "저장", () => { Game.SaveSlot(s); SaveWindow(true); }, "", false, 15, "save"));
            }
            else h.AddChild(B("불러오기", () => Game.LoadSlot(s), "", info == null, 15, "load"));
            body.AddChild(row);
        }
        ShowModal(win, saving ? "save" : "load");
    }

    // ───────── 설정 ─────────

    void SettingsWindow()
    {
        var win = Window("설정", out var body, true, "gear", 640, 560);

        void Slider(string name, string icon, float value, Action<float> set)
        {
            var row = H(12);
            row.AddChild(new Glyph(icon, 20, Ui.Accent));
            row.AddChild(L(name, 16, null, false, 150));
            var sl = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(300, 24), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None };
            var num = L(Pct(value), 15, Faint(0.7f), false, 56);
            sl.ValueChanged += v => { set((float)v); num.Text = Pct(v); Prefs.Apply(false); };
            sl.DragEnded += _ => { Prefs.Save(); Sfx.Play("click"); };
            row.AddChild(sl);
            row.AddChild(num);
            body.AddChild(row);
        }

        void Choice<T>(string name, string icon, T[] options, T now, Func<T, string> label, Action<T> set, bool window = false)
        {
            var row = H(12);
            row.AddChild(new Glyph(icon, 20, Ui.Accent));
            row.AddChild(L(name, 16, null, false, 150));
            var group = H(4);
            foreach (var o in options)
            {
                var opt = o;
                var bt = B(label(o), () =>
                {
                    set(opt);
                    Prefs.Save();
                    Prefs.Apply(window);
                    SettingsWindow();
                }, "", false, 14);
                bt.ToggleMode = true;
                bt.SetPressedNoSignal(EqualityComparer<T>.Default.Equals(o, now));
                group.AddChild(bt);
            }
            row.AddChild(group);
            body.AddChild(row);
        }

        body.AddChild(L("소리", 18, Section));
        Slider("전체 음량", "sound", Prefs.Master, v => Prefs.Master = v);
        Slider("음악", "sound", Prefs.Music, v => Prefs.Music = v);
        Slider("효과음", "sound", Prefs.Effects, v => Prefs.Effects = v);

        body.AddChild(L("화면", 18, Section));
        Choice("화면 모드", "eye", new[] { false, true }, Prefs.Fullscreen, f => f ? "전체 화면" : "창", f => Prefs.Fullscreen = f, true);
        Choice("UI 크기", "info", Prefs.Scales, Prefs.UiScale, s => $"{s * 100:0}%", s => Prefs.UiScale = s);

        body.AddChild(L("진행", 18, Section));
        Choice("자동 저장", "save", Prefs.Autosaves, Prefs.AutosaveYears, y => y == 0 ? "끄기" : $"{y}년마다", y => Prefs.AutosaveYears = y);
        Choice("위기 시 일시정지", "pause", new[] { true, false }, Prefs.PauseOnCrisis, b => b ? "켜기" : "끄기", b => Prefs.PauseOnCrisis = b);
        Choice("부족 시대 속도", "bolt", Prefs.Hastes, Prefs.TribeHaste, h => $"{h:0.#}배", h => Prefs.TribeHaste = h);
        Choice("안내", "guide", new[] { true, false }, Prefs.ShowGuide, b => b ? "켜기" : "끄기", b => Prefs.ShowGuide = b);
        Choice("새 게임 난이도", "legit", new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard }, Prefs.Difficulty, Simulation.DifficultyName, d => Prefs.Difficulty = d);
        var again = H(12);
        again.AddChild(new Control { CustomMinimumSize = new Vector2(20 + 150 + 12, 0) });
        again.AddChild(B("처음 안내 다시 보기", () => { CloseModal(); StartIntro(true); }, "새 게임 시작 때 나오는 화면 안내를 지금 다시 봅니다.", false, 14, "guide"));
        body.AddChild(again);
        body.AddChild(L("나라가 세워지기 전에는 할 일이 적어 시간이 이만큼 더 빨리 흐릅니다. 위기 시 일시정지는 침공·몰락 같은 큰 사건을 놓치지 않게 해 줍니다.", 13, Faint(0.65f), true, 620));

        body.AddChild(L("단축키", 18, Section));
        body.AddChild(L("Space 일시정지 · 1~4 속도 · J 일 · B 건설 · L 법전 · T 기술 · R 나라 · P 사람들 · C 연대기 · Home 도성\nF5 빠른 저장 · F9 빠른 불러오기 · F10 설정 · Esc 닫기/선택 해제", 14, Faint(0.75f), true, 620));
        ShowModal(win, "settings");
    }

    void AskNewGame()
    {
        var v = V(12);
        v.AddChild(Head("new", "새 게임을 시작할까요?", 24));
        v.AddChild(L("저장하지 않으면 지금 게임은 사라집니다.", 15));
        v.AddChild(DifficultyPicker(AskNewGame));
        var row = H();
        row.AddChild(B("저장 후 새 게임", () => { Game.SaveSlot("quick"); Game.NewGame(new Random().Next(1, 9999)); }, "빠른 저장 슬롯에 저장합니다.", false, 17, "save"));
        row.AddChild(B("새 맵으로 시작", () => Game.NewGame(new Random().Next(1, 9999)), "", false, 17, "new"));
        row.AddChild(B("같은 맵으로 다시", () => Game.NewGame(Game.Seed), "", false, 17, "back"));
        row.AddChild(B("취소", CloseModal, "", false, 17, "close"));
        v.AddChild(row);
        ShowModal(v, "ask");
    }

    static readonly (Difficulty d, string tip)[] Difficulties =
    {
        (Difficulty.Easy, "수확이 넉넉하고 침공이 드뭅니다. 나라 살림과 민심도 너그러워, 무너지는 일이 적습니다."),
        (Difficulty.Normal, "본래의 규칙입니다."),
        (Difficulty.Hard, "흉년이 잦고 이웃이 더 자주, 더 큰 군대로 쳐들어옵니다. 살림이 빠듯하고 민심이 사나워 나라가 쉽게 무너집니다."),
    };

    /// <summary>새 게임의 난이도 고르기. 고르면 설정에 남고 again으로 창을 다시 그린다.</summary>
    Control DifficultyPicker(Action again)
    {
        var box = V(6);
        var row = H(10);
        row.AddChild(new Glyph("legit", 20, Ui.Accent));
        row.AddChild(L("난이도", 16, null, false, 80));
        var group = H(4);
        string tip = "";
        foreach (var (d, t) in Difficulties)
        {
            var opt = d;
            var bt = B(Simulation.DifficultyName(d), () =>
            {
                Prefs.Difficulty = opt;
                Prefs.Save();
                again();
            }, t, false, 15);
            bt.ToggleMode = true;
            bt.SetPressedNoSignal(d == Prefs.Difficulty);
            if (d == Prefs.Difficulty) tip = t;
            group.AddChild(bt);
        }
        row.AddChild(group);
        box.AddChild(row);
        box.AddChild(L(tip, 13, Faint(0.7f), true, 520));
        return box;
    }
}
