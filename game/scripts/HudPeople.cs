using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>사람 한 명의 전신 그림(블렌더 3D 스프라이트)을 작은 초상 틀 안에 그리는 칸.</summary>
public partial class PersonAvatar : Control
{
    public string Body = "man";
    public bool Alive = true;
    public bool Chief;
    public float AvatarScale = 1f;

    public PersonAvatar() { }
    public PersonAvatar(Simulation s, Person p, float size = 56)
    {
        Alive = p.Alive;
        Chief = p == s.Chief;
        int tick = p.Alive ? s.Tick : (p.DeathTick ?? s.Tick);
        double age = p.Age(tick);
        if (age < 10) Body = "child";
        else
        {
            if (age < 14) AvatarScale = 0.85f;
            if (Chief) Body = "chief";
            else if (p.Job == Work.Guard) Body = "soldier";
            else
            {
                bool alt = ((uint)p.Id * 2654435761u >> 7) % 2 == 0;
                Body = p.IsMale ? (p.Job == Work.Hunt || alt ? "hunter" : "man") : (alt ? "woman" : "woman2");
            }
        }
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public override void _Draw()
    {
        float r = Math.Min(Size.X, Size.Y) / 2f;
        var center = Size / 2f;
        DrawCircle(center, r - 1, new Color("112329", 0.95f));
        var rim = !Alive ? new Color(Ui.Text, 0.22f) : Chief ? Art.Gold : new Color(Ui.Rim, 0.75f);
        DrawArc(center, r - 1.5f, 0, Mathf.Tau, 32, rim, Chief ? 2.2f : 1.5f, true);
        var tint = Alive ? Colors.White : new Color(0.6f, 0.62f, 0.64f, 0.55f);
        float h = Size.Y * (Body == "child" ? 0.56f : 0.72f * AvatarScale);
        ActorLayer.DrawFolk(this, Body, "idle", new Vector2(0.3f, 1f), 0, new Vector2(center.X, Size.Y * 0.86f), h, tint);
    }
}

/// <summary>사람들 창: 왼쪽은 산 사람의 목록, 오른쪽은 고른 한 사람(몸·재주·본능·식구·원한).</summary>
public partial class Hud
{
    int _personId = -1;
    string _peopleFilter = "all";

    static readonly string[] TraitNames = { "공격성", "탐욕", "야심", "핏줄 편애", "성욕", "공감" };
    static readonly Color[] TraitColors = { new("a8442f"), new("b88a2a"), new("6a4f9a"), new("3f7a5a"), new("b0587a"), new("2f6f93") };

    public void ShowPerson(int id)
    {
        _personId = id;
        PeopleWindow();
    }

    void PeopleWindow()
    {
        var byId = S.People.ToDictionary(p => p.Id);
        var win = Window($"사람들 — 생존 {S.Living.Count()}명", out var body, false, "people", 1060, 620);
        var cols = H(16);
        cols.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(cols);

        // 왼쪽: 목록
        var left = V(6);
        left.CustomMinimumSize = new Vector2(360, 0);
        cols.AddChild(left);
        var filters = H(4);
        void F(string name, string key)
        {
            var bt = B(name, () => { _peopleFilter = key; PeopleWindow(); }, "", false, 13);
            bt.ToggleMode = true;
            bt.SetPressedNoSignal(_peopleFilter == key);
            filters.AddChild(bt);
        }
        F("모두", "all");
        F("가장", "head");
        F("원한", "grudge");
        F("종", "slave");
        F("사망자", "dead");
        left.AddChild(filters);

        IEnumerable<Person> list = _peopleFilter switch
        {
            "head" => S.Living.Where(p => S.Head(p) == p),
            "grudge" => S.Living.Where(p => p.Grudge.Values.Any(g => g > 0.3)),
            "slave" => S.Living.Where(p => p.Master != null),
            "dead" => S.People.Where(p => !p.Alive).OrderByDescending(p => p.DeathTick).ThenBy(p => p.Id).Take(200),
            _ => S.Living,
        };
        if (_peopleFilter != "dead") list = list.OrderByDescending(p => p == S.Chief).ThenByDescending(p => p.Status).ThenBy(p => p.Id);
        var rows = list.Take(400).ToList();

        var sc = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        left.AddChild(sc);
        var lv = V(3);
        lv.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sc.AddChild(lv);
        if (rows.Count == 0) lv.AddChild(L("해당하는 사람이 없습니다.", 14, Faint(0.6f)));
        foreach (var p in rows)
        {
            int id = p.Id;
            string tag = p == S.Chief ? " ★" : "";
            double age = p.Age(p.Alive ? S.Tick : (p.DeathTick ?? S.Tick));
            string sub = p.Alive ? $"{(p.IsMale ? "남" : "여")} {age:0}세" + (age >= 10 ? $" · {WorkCatalog.Name(p.Job)}" : "") : $"{p.DeathCause ?? "죽음"} · 제 {(p.DeathTick ?? 0) / 4 + 1}년";
            string ico = !p.Alive ? "skull" : age < 10 ? "child" : p.IsMale ? "people" : "woman";
            var b = B($"{S.P(p)}{tag}   {sub}", () => ShowPerson(id), "", false, 14, ico);
            b.ToggleMode = true;
            b.SetPressedNoSignal(id == _personId);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.ClipText = true;
            lv.AddChild(b);
        }
        if (list.Count() > rows.Count) lv.AddChild(L($"…외 {list.Count() - rows.Count}명", 13, Faint(0.55f)));

        cols.AddChild(new VSeparator());

        // 오른쪽: 한 사람
        var rsc = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        cols.AddChild(rsc);
        var right = V(8);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rsc.AddChild(right);
        if (!byId.TryGetValue(_personId, out var who)) who = rows.FirstOrDefault();
        if (who == null) right.AddChild(L("왼쪽 목록에서 사람을 선택하세요.", 15, Faint(0.6f)));
        else PersonDetail(right, who, byId);

        ShowModal(win, "people");
    }

    void PersonDetail(VBoxContainer v, Person p, Dictionary<int, Person> byId)
    {
        _personId = p.Id;
        var head = H(12);
        head.AddChild(new PersonAvatar(S, p, 58));
        var headInfo = V(3);
        headInfo.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var titleRow = H(8);
        titleRow.AddChild(L(S.P(p), 26));
        var tags = new List<string>();
        if (p == S.Chief) tags.Add(S.RulerTitle);
        if (p.Alive && S.Head(p) == p) tags.Add("가장");
        if (p.Master != null) tags.Add("종");
        if (p.Outsider) tags.Add("이주민");
        if (!p.Alive) tags.Add("사망");
        foreach (var t in tags)
        {
            var chip = new PanelContainer();
            chip.AddThemeStyleboxOverride("panel", Ui.Pill(t == "사망" ? new Color(0, 0, 0, 0.2f) : new Color(Ui.Accent, 0.16f), t == "사망" ? new Color(Ui.Text, 0.25f) : new Color(Ui.Accent, 0.6f), 4));
            chip.AddChild(L(t, 13, t == "사망" ? Faint(0.7f) : Ui.Accent.Lightened(0.18f)));
            chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            titleRow.AddChild(chip);
        }
        headInfo.AddChild(titleRow);

        string life = p.Alive
            ? $"{(p.IsMale ? "남자" : "여자")} · {p.Age(S.Tick):0}세 · {S.ClanName(p.ClanId)}씨족"
            : $"{(p.IsMale ? "남자" : "여자")} · {((p.DeathTick ?? S.Tick) - p.BirthTick) / 4}세에 사망 — {p.DeathCause ?? "원인 미상"}" + (p.KilledBy != null ? $" ({S.P(p.KilledBy)}에게 살해됨)" : "");
        headInfo.AddChild(L(life, 15, Faint(0.78f)));
        head.AddChild(headInfo);
        v.AddChild(head);
        if (p.Master != null) v.AddChild(Link("주인", p.Master));
        if (p.Alive && p.Age(S.Tick) >= 10) JobPicker(v, p);

        // 몸과 살림
        v.AddChild(L("상태", 17, Section));
        void Bar(string name, double val, double max, string text, Color col)
        {
            var row = H(10);
            row.AddChild(L(name, 14, null, false, 70));
            row.AddChild(Meter(val / max, 260, col, 10));
            row.AddChild(L(text, 14, Faint(0.75f)));
            v.AddChild(row);
        }
        Bar("건강", p.Health, 1, Pct(p.Health), p.Health < 0.4 ? Art.Bad : Art.Good);
        Bar("재주", p.Skill, 2, $"{p.Skill:0.00}", Ui.Accent);
        Bar("위신", Math.Max(0, p.Status), 12, $"{p.Status:0.00}", Art.Gold);
        Bar("보유 식량", Math.Max(0, p.Food), 20, $"{p.Food:0.0}", new Color("c99a52"));

        // 본능
        v.AddChild(L("본능", 17, Section));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 22);
        grid.AddThemeConstantOverride("v_separation", 4);
        for (int i = 0; i < 6; i++)
        {
            var row = H(8);
            row.AddChild(L(TraitNames[i], 14, null, false, 70));
            row.AddChild(Meter(p.Traits[i], 150, TraitColors[i], 10));
            row.AddChild(L($"{p.Traits[i]:0.00}", 13, Faint(0.65f)));
            grid.AddChild(row);
        }
        v.AddChild(grid);

        // 식구
        v.AddChild(L("가족", 17, Section));
        if (p.Father != null) v.AddChild(Link("아버지", p.Father));
        if (p.Mother != null) v.AddChild(Link("어머니", p.Mother));
        if (p.Husband != null) v.AddChild(Link("남편", p.Husband));
        foreach (var w in p.Wives.OrderBy(w => w.Id)) v.AddChild(Link("아내", w));
        var kids = p.Children.OrderBy(c => c.BirthTick).ThenBy(c => c.Id).ToList();
        if (kids.Count > 0)
        {
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 6);
            flow.AddChild(L($"자식 {kids.Count}명", 14, null, false, 70));
            foreach (var c in kids)
            {
                int id = c.Id;
                flow.AddChild(B(c.Name + (c.Alive ? "" : " †"), () => ShowPerson(id), $"{S.P(c)} · {(c.Alive ? $"{c.Age(S.Tick):0}세" : c.DeathCause ?? "죽음")}", false, 13));
            }
            v.AddChild(flow);
        }
        if (p.Father == null && p.Mother == null && p.Husband == null && p.Wives.Count == 0 && kids.Count == 0) v.AddChild(L("가족 관계가 없습니다.", 14, Faint(0.6f)));

        // 원한
        v.AddChild(L("원한", 17, Section));
        var mine = p.Grudge.Where(g => g.Value > 0.05 && byId.ContainsKey(g.Key)).OrderByDescending(g => g.Value).ThenBy(g => g.Key).Take(8).ToList();
        foreach (var g in mine)
        {
            var other = byId[g.Key];
            string why = p.GrudgeWhy.TryGetValue(g.Key, out var w) ? $" — {w}" : "";
            v.AddChild(Link("원한 대상", other, $"{g.Value:0.00}{why}", Art.Bad));
        }
        var theirs = S.Living.Where(o => o.Grudge.TryGetValue(p.Id, out var g) && g > 0.05).OrderByDescending(o => o.Grudge[p.Id]).ThenBy(o => o.Id).Take(8).ToList();
        foreach (var o in theirs)
        {
            string why = o.GrudgeWhy.TryGetValue(p.Id, out var w) ? $" — {w}" : "";
            v.AddChild(Link("원한 품은 이", o, $"{o.Grudge[p.Id]:0.00}{why}", new Color("df9b52")));
        }
        if (mine.Count == 0 && theirs.Count == 0) v.AddChild(L("얽힌 원한이 없습니다.", 14, Faint(0.6f)));

        if (S.Chief is { Alive: true } chief && chief != p && p.Alive)
            v.AddChild(L($"{Josa.Wa(S.RulerTitle)}의 혈연: {S.Related(chief, p) * 100:0}%", 13, Faint(0.6f)));
    }

    Control Link(string role, Person q, string note = "", Color? col = null)
    {
        var row = H(8);
        row.AddChild(L(role, 14, col, false, 70));
        int id = q.Id;
        var b = B(S.P(q) + (q.Alive ? "" : " †"), () => ShowPerson(id), q.Alive ? $"{q.Age(S.Tick):0}세" : q.DeathCause ?? "죽음", false, 14);
        row.AddChild(b);
        if (note.Length > 0) row.AddChild(L(note, 13, Faint(0.7f), true, 330));
        return row;
    }

    /// <summary>한 사람의 일을 고르는 단추 줄. 관리인이 있으면 관리인이 정한다.</summary>
    void JobPicker(VBoxContainer v, Person p)
    {
        v.AddChild(L("일", 17, Section));
        bool steward = S.Steward is { Alive: true };
        if (steward) v.AddChild(L($"관리인이 일을 나누고 있습니다 (지금: {WorkCatalog.Name(p.Job)}).", 13, Faint(0.65f)));
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        foreach (var d in WorkCatalog.All)
        {
            var w = d.Work;
            string why = S.WhyCantWork(w);
            int id = p.Id;
            var b = B(d.Name, () => { if (S.SetJob(id, w)) { Game.Touched(); PeopleWindow(); } }, why.Length > 0 ? why : $"얻는 것: {d.Gain}\n치르는 값: {d.Cost}", steward || why.Length > 0, 14, WorkIcon(w));
            b.ToggleMode = true;
            b.SetPressedNoSignal(p.Job == w);
            flow.AddChild(b);
        }
        v.AddChild(flow);
    }
}
