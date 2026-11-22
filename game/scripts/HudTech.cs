using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>
/// 기술 나무 그림: 시대마다 한 열. 위에는 줄기 기술, 아래에는 갈림길(여러 갈래를 골라도 되지만 더할수록 비싸다).
/// 선행 기술은 선으로 잇고, 카드 색으로 상태(익힘·연구 중·연구 가능·잠김·닫힘)를 보인다. 연구 가능한 카드를 누르면 그것을 연구한다.
/// </summary>
public partial class TechTreeView : Control
{
    public readonly Dictionary<string, Rect2> Cards = new();
    public readonly List<(string from, string to, bool lit)> Links = new();
    public readonly List<(float x, float w, string title, bool now, bool past)> Columns = new();
    public float ForkY;

    public override void _Draw()
    {
        var ink = Ui.Text;
        foreach (var (x, w, title, now, past) in Columns)
        {
            var bg = now ? new Color(Ui.Accent, 0.07f) : past ? new Color(Art.Good, 0.05f) : new Color(ink, 0.025f);
            DrawRect(new Rect2(x - 8, 0, w + 16, Size.Y), bg);
            if (now) DrawRect(new Rect2(x - 8, 0, w + 16, Size.Y), new Color(Ui.Accent, 0.5f), false, 2);
            Art.Text(this, new Vector2(x + w / 2, 26), title, 18, now ? Ui.Accent : new Color(ink, past ? 0.7f : 0.45f), true, 0);
            DrawDashedLine(new Vector2(x, ForkY), new Vector2(x + w, ForkY), new Color(Art.Bad, 0.45f), 1.5f, 6);
            Art.Text(this, new Vector2(x + w / 2, ForkY + 18), "갈림길 · 여럿 가능", 13, new Color(Art.Bad, 0.8f), true, 0);
        }
        foreach (var (from, to, lit) in Links)
        {
            if (!Cards.TryGetValue(from, out var a) || !Cards.TryGetValue(to, out var b)) continue;
            var col = lit ? new Color(Art.Good.Darkened(0.2f), 0.85f) : new Color(ink, 0.22f);
            float wdt = lit ? 2.5f : 1.5f;
            if (b.Position.X > a.End.X)
            {
                // 다른 시대로: 오른쪽 가장자리에서 왼쪽 가장자리로 굽은 선
                var p0 = new Vector2(a.End.X, a.GetCenter().Y);
                var p3 = new Vector2(b.Position.X, b.GetCenter().Y);
                float dx = (p3.X - p0.X) * 0.5f;
                var pts = new Vector2[17];
                for (int i = 0; i <= 16; i++)
                {
                    float t = i / 16f, u = 1 - t;
                    pts[i] = u * u * u * p0 + 3 * u * u * t * (p0 + new Vector2(dx, 0)) + 3 * u * t * t * (p3 - new Vector2(dx, 0)) + t * t * t * p3;
                }
                DrawPolyline(pts, col, wdt, true);
                DrawCircle(p3, 3.5f, col);
            }
            else
            {
                // 같은 시대 안: 카드 왼쪽으로 돌아 내려가되, 줄마다 조금씩 비켜 겹치지 않게 한다
                int lane = (int)(a.Position.Y / 50) % 3;
                float lx = Math.Min(a.Position.X, b.Position.X) - 4 - lane * 3;
                var pts = new[] { new Vector2(a.Position.X, a.GetCenter().Y), new Vector2(lx, a.GetCenter().Y), new Vector2(lx, b.GetCenter().Y), new Vector2(b.Position.X, b.GetCenter().Y) };
                DrawPolyline(pts, col, wdt, true);
                DrawCircle(pts[3], 3, col);
            }
        }
    }
}

public partial class Hud
{
    static readonly Color[] CourseCols = { new("e7987c"), new("e6bd72"), new("c09bb9"), new("b2a0db"), new("83c1da") };
    string _techHighlight = "";

    public void OpenTech(string key = "")
    {
        _techHighlight = key;
        TechWindow();
    }

    void TechWindow()
    {
        var win = Window($"기술 — 습득 {S.KnownCount}/{TechTree.All.Count}", out var body, false, "tech", 1440, 660);

        // 위: 지금 연구 + 범례
        var cur = S.Research != null ? TechTree.Find(S.Research) : null;
        var top = H(14);
        if (cur != null)
        {
            double left = Math.Max(0, S.CostOf(cur) - S.ResearchProgress);
            // 지식은 해가 끝날 때 한꺼번에 쌓인다(ResearchRate는 지난해 값). 첫 해가 끝나기 전에는 0이니 사람 수로 어림한다.
            bool guess = S.ResearchRate <= 0.001 && S.Year <= 1;
            double rate = guess ? KnowledgeGuess() : S.ResearchRate;
            string eta = rate > 0.001 ? $"약 {Math.Max(1, Math.Ceiling(left / rate))}년 남음" : "진척 없음";
            top.AddChild(Head("tech", $"연구 중: {cur.Name}", 18, Ui.Accent));
            top.AddChild(Meter(S.ResearchProgress / Math.Max(0.001, S.CostOf(cur)), 220, Ui.Accent, 12));
            var rl = L(guess ? $"{eta} · 해마다 지식 약 {rate:0.0} (어림 — 첫 해가 끝나면 계산)" : $"{eta} · 해마다 지식 {rate:0.00}", 14, Faint(0.7f));
            rl.TooltipText = "지식은 해마다 겨울이 끝날 때 한꺼번에 쌓입니다. 사람이 많고 도시가 클수록, 글과 학교가 있을수록 빨라집니다.";
            rl.MouseFilter = Control.MouseFilterEnum.Pass;
            top.AddChild(rl);
        }
        else
        {
            string nextNeeds = S.NextEraNeeds();
            if (S.Realm.Era == Era.Primitive && S.Realm.Stage < Stage.Chiefdom)
                nextNeeds += $" ({S.NextStageNeeds()})";
            top.AddChild(Head("tech", S.AtCrossroad ? "갈림길입니다 — 아래 칸에서 나라가 갈 길을 고르세요(여러 갈래도 됩니다)"
                : TechTree.All.Any(S.CanResearch) ? "연구 중인 것이 없습니다 — 밝은 카드를 누르세요"
                : nextNeeds.Length > 0 ? $"이 시대의 기술을 모두 익혔습니다 — 다음 시대 조건: {nextNeeds}"
                : "모든 시대의 기술을 익혔습니다",
                17, Art.Bad));
        }
        // 잠긴 카드를 눌렀을 때 까닭을 띄우는 자리(알림 띠는 창 뒤에 가려진다)
        _techMsg = L("", 14, Art.Bad, false);
        _techMsg.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _techMsg.HorizontalAlignment = HorizontalAlignment.Right;
        _techMsg.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        top.AddChild(_techMsg);
        foreach (var (txt, bg, br) in new[]
                 {
                     ("익힘", new Color(Art.Good, 0.28f), Art.Good.Darkened(0.2f)), ("연구 중", new Color(Ui.Accent, 0.2f), Ui.Accent),
                     ("연구 가능", Ui.Surface, new Color(Ui.Text, 0.55f)), ("잠김", new Color(Ui.Surface, 0.28f), new Color(Ui.Text, 0.12f)),
                 })
        {
            var chip = new PanelContainer();
            chip.AddThemeStyleboxOverride("panel", Box(bg, br, 2, 5, 6));
            chip.AddChild(L(txt, 12));
            top.AddChild(chip);
        }
        body.AddChild(top);

        // 나무
        const float cw = 204, ch = 44, gap = 6, colGap = 44, head = 44;
        var view = new TechTreeView { MouseFilter = Control.MouseFilterEnum.Pass };
        var eras = Enum.GetValues<Era>();
        int maxTrunk = eras.Max(e => TechTree.All.Count(t => t.Era == e && !t.Crossroad));
        int maxFork = eras.Max(e => TechTree.All.Count(t => t.Era == e && t.Crossroad));
        view.ForkY = head + maxTrunk * (ch + gap) + 6;
        float forkTop = view.ForkY + 30;
        view.CustomMinimumSize = new Vector2(eras.Length * (cw + colGap) + 10, forkTop + maxFork * (ch + gap) + 6);
        float nowX = 0;
        for (int ei = 0; ei < eras.Length; ei++)
        {
            var era = eras[ei];
            float x = 14 + ei * (cw + colGap);
            bool now = era == S.Realm.Era;
            if (now) nowX = x;
            view.Columns.Add((x, cw, Catalog.EraNames[ei] + (now ? " · 지금" : era > S.Realm.Era ? " · 잠김" : ""), now, era < S.Realm.Era));
            // 줄기: 같은 시대 안의 선행 깊이 → 값 순서로 위에서 아래로
            int Depth(TechDef t) => t.Requires.Select(TechTree.Find).Where(r => r != null && r.Era == era).Select(r => 1 + Depth(r!)).DefaultIfEmpty(0).Max();
            var trunk = TechTree.All.Where(t => t.Era == era && !t.Crossroad).OrderBy(Depth).ThenBy(t => t.Cost).ToList();
            for (int i = 0; i < trunk.Count; i++) AddCard(view, trunk[i], new Rect2(x, head + i * (ch + gap), cw, ch));
            var forks = TechTree.All.Where(t => t.Era == era && t.Crossroad).ToList();
            for (int i = 0; i < forks.Count; i++) AddCard(view, forks[i], new Rect2(x, forkTop + i * (ch + gap), cw, ch));
        }
        foreach (var t in TechTree.All)
            foreach (var r in t.Requires)
                view.Links.Add((r, t.Key, S.Knows(r)));

        var sc = new ScrollContainer { CustomMinimumSize = new Vector2(1440, 600), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        sc.AddChild(view);
        body.AddChild(sc);
        if (_techHighlight.Length > 0 && view.Cards.TryGetValue(_techHighlight, out var hiRect))
        {
            nowX = hiRect.Position.X;
            if (TechTree.Find(_techHighlight) is { } ht && !S.Knows(ht.Key) && !S.CanResearch(ht) && _techMsg != null)
                _techMsg.Text = WhyCantResearch(ht);
        }
        float scrollTo = Math.Max(0, nowX - (cw + colGap) - 20);
        Callable.From(() => sc.ScrollHorizontal = (int)scrollTo).CallDeferred();

        var taken = TechTree.All.Where(t => t.Crossroad && S.Knows(t.Key)).ToList();
        body.AddChild(L((taken.Count > 0 ? "걸어온 길: " + string.Join(" → ", taken.Select(t => $"{t.Name}({Simulation.CourseName(t.Course!.Value)})")) + "   ·   " : "")
                        + string.Join(" · ", Enumerable.Range(0, 4).Select(k => $"{Simulation.CourseNames[k]} {S.CourseLevel((Course)k)}단계"))
                        + "   ·   다음 시대의 기술은 나라가 그 시대에 들어서야 연구할 수 있습니다.", 13, Faint(0.65f)));
        ShowModal(win, "tech");
    }

    Label? _techMsg;

    /// <summary>첫 해가 끝나기 전의 한 해 지식 어림(시뮬레이션의 GrowKnowledge 바탕식만: 사람 수와 도시 비율).</summary>
    double KnowledgeGuess()
    {
        double people = S.Tribal ? S.Living.Count() : S.Realm.Pop;
        return 0.25 + 0.35 * Math.Log10(people + 10) * (1 + 2 * S.Realm.Urban);
    }

    /// <summary>아직 연구할 수 없는 기술을 눌렀을 때: 왜 안 되는지.</summary>
    string WhyCantResearch(TechDef t)
    {
        if (t.Era > S.Realm.Era) return $"{t.Name}: {Catalog.EraNames[(int)t.Era]} 시대에 들어서야 연구할 수 있습니다.";
        var missing = t.Requires.Where(k => !S.Knows(k)).Select(k => TechTree.Find(k)?.Name ?? k).ToList();
        if (missing.Count > 0) return $"{t.Name}: 먼저 익힐 것 — {string.Join(", ", missing)}";
        return $"{t.Name}: 지금은 연구할 수 없습니다.";
    }

    void AddCard(TechTreeView view, TechDef t, Rect2 r)
    {
        view.Cards[t.Key] = r;
        bool known = S.Knows(t.Key), now = S.Research == t.Key, can = S.CanResearch(t);
        bool eraLocked = t.Era > S.Realm.Era;
        Color? course = t.Crossroad ? CourseCols[(int)t.Course!.Value] : null;

        var unlocks = TechTree.Unlocks(t.Key).Select(d => d.Name).ToList();
        string tip = $"{t.Name} · {Catalog.EraNames[(int)t.Era]} · 지식 {S.CostOf(t):0}\n{t.Effect}";
        if (unlocks.Count > 0) tip += "\n여는 제도: " + string.Join(", ", unlocks);
        if (t.Reveals != Instinct.Sim.Resource.None) tip += "\n알아보는 자원: " + ResourceCatalog.Name(t.Reveals);
        if (t.Requires.Length > 0) tip += "\n먼저 익힐 것: " + string.Join(", ", t.Requires.Select(k => (S.Knows(k) ? "✓ " : "") + (TechTree.Find(k)?.Name ?? k)));
        if (t.Crossroad) tip += $"\n\n{Simulation.CourseName(t.Course!.Value)}의 길 (지금 {S.CourseLevel(t.Course!.Value)}단계) — 승리: {Simulation.Victories[(int)t.Course!.Value].Name}\n같은 시대의 갈림길을 여러 개 익힐 수 있습니다. 다만 하나 더할 때마다 값이 50%씩 오릅니다{(S.CostOf(t) > t.Cost + 0.01 ? $"(지금 {S.CostOf(t) / t.Cost:0.#}배)" : "")}.";
        tip += known ? "\n\n익힌 기술" : now ? "\n\n지금 연구 중" : can ? "\n\n누르면 이것을 연구합니다" + (S.Research != null ? " (하던 연구는 쌓은 것의 절반을 잃습니다)" : "")
            : eraLocked ? $"\n\n{Catalog.EraNames[(int)t.Era]} 시대에 들어서야 연구할 수 있습니다" : "\n\n먼저 익힐 기술이 남았습니다";

        var b = new Button
        {
            Position = r.Position, Size = r.Size, CustomMinimumSize = r.Size, TooltipText = tip, FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = can && !now ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow,
        };
        Color bg, br;
        int bw = 1;
        bool picked = t.Key == _techHighlight;
        if (known) { bg = new Color(Art.Good, 0.26f); br = Art.Good.Darkened(0.2f); bw = 2; }
        else if (now) { bg = new Color(Ui.Accent, 0.18f); br = Ui.Accent; bw = 3; }
        else if (can) { bg = Ui.Surface; br = course ?? new Color(Ui.Text, 0.55f); bw = 2; }
        else { bg = new Color(Ui.Surface, eraLocked ? 0.16f : 0.28f); br = new Color(course ?? Ui.Text, eraLocked ? 0.1f : 0.15f); }
        if (picked) { br = Art.Gold; bw = 3; bg = bg.Lightened(0.12f); }
        var box = Box(bg, br, bw, 6, 8);
        if (course != null) box.BorderWidthLeft = 6;
        if (course != null && !known && !now) box.BorderColor = new Color(course.Value, can ? 1 : 0.35f);
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("pressed", box);
        b.AddThemeStyleboxOverride("disabled", box);
        b.AddThemeStyleboxOverride("hover", can && !now ? Box(new Color(Ui.Accent, 0.12f), Ui.Accent, 3, 6, 8) : box);
        if (can && !now)
        {
            var key = t.Key;
            b.Pressed += () => Sfx.Play("click");
            b.Pressed += () => { if (S.SetResearch(key)) { Game.Touched(); TechWindow(); } };
        }
        else if (!known && !now)
        {
            // 잠긴 카드: 눌러도 연구되지 않으니 모자란 선행 기술을 창 위쪽에 알린다.
            var td = t;
            b.Pressed += () =>
            {
                Sfx.Play("click");
                if (_techMsg != null && IsInstanceValid(_techMsg)) _techMsg.Text = WhyCantResearch(td);
                else Toast(WhyCantResearch(td), Art.Bad);
            };
        }

        float dim = known || now || can ? 1 : eraLocked ? 0.38f : 0.5f;
        var v = V(-4);
        v.MouseFilter = Control.MouseFilterEnum.Ignore;
        v.Position = new Vector2(42, 2);
        v.Size = new Vector2(r.Size.X - 50, r.Size.Y - 8);
        b.AddChild(v);
        string art = t.Key;
        b.AddChild(new Glyph(art, 28) { Position = new Vector2(8, 8), Modulate = new Color(1, 1, 1, dim) });
        var name = L((known ? "✓ " : "") + t.Name, 15, new Color(known ? new Color("a9d995") : Ui.Text, dim));
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.ClipText = true;
        name.CustomMinimumSize = new Vector2(r.Size.X - 50, 0);
        v.AddChild(name);
        string sub = known ? (t.Crossroad ? $"{Simulation.CourseName(t.Course!.Value)}의 길" : "익힘")
            : now ? $"{Math.Min(1, S.ResearchProgress / S.CostOf(t)) * 100:0}% · 지식 {S.CostOf(t):0}"
            : (t.Crossroad ? $"{Simulation.CourseName(t.Course!.Value)} · " : "") + $"지식 {S.CostOf(t):0}" + (S.CostOf(t) > t.Cost + 0.01 ? "↑" : "") + (unlocks.Count > 0 ? " · 제도 " + unlocks.Count : "");
        var sl = L(sub, 12, new Color(now ? Ui.Accent : course ?? Ui.Text, dim * (now ? 1 : 0.75f)));
        sl.MouseFilter = Control.MouseFilterEnum.Ignore;
        v.AddChild(sl);
        if (now)
        {
            var m = Meter(S.ResearchProgress / Math.Max(0.001, S.CostOf(t)), r.Size.X - 16, Ui.Accent, 3);
            m.Position = new Vector2(8, r.Size.Y - 5);
            m.MouseFilter = Control.MouseFilterEnum.Ignore;
            b.AddChild(m);
        }
        view.AddChild(b);
    }
}
