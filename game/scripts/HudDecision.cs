using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>
/// 결정(건의·기근·점령지·공물). 시간을 멈추지 않는다: 오른쪽에 카드로 쌓이고, 카드에서 바로 고르거나
/// "자세히"로 큰 창을 연다. 큰 창은 얻는 것(초록)·치르는 값(빨강)을 상자로 나누고, 선택지를 큰 카드 단추로 보인다.
/// </summary>
public partial class Hud
{
    VBoxContainer _decCards = null!;
    string _decSig = "";
    readonly Dictionary<int, Label> _decLeft = new();

    static readonly Color GainCol = new("7fd67a"), CostCol = new("ff8a70");

    /// <summary>새 결정이 올라왔다. 시간은 흐르는 채로 종만 울리고 카드를 세운다.</summary>
    public void DecisionArrived(Decision d)
    {
        Sfx.Play("bell", 0.45f);
        _decSig = "";
        RefreshDecisions();
    }

    static string KindName(string kind) => kind switch
    {
        "adopt" => "건의",
        "relief" => "기근",
        "conquest" => "점령지",
        "tribute" => "외교 사절",
        _ => "결정",
    };

    static string KindIcon(string kind) => kind switch
    {
        "adopt" => "law",
        "relief" => "food",
        "conquest" => "army",
        "tribute" => "coin",
        _ => "info",
    };

    /// <summary>남은 계절을 사람이 읽는 말로.</summary>
    static string LeftText(int seasons) => seasons <= 0 ? "곧 마감" : seasons < 4 ? $"{seasons}계절 남음" : $"{seasons}계절(약 {Math.Round(seasons / 4.0)}년) 남음";

    /// <summary>결정 글을 머리말·얻는 것·치르는 값으로 나눈다. "얻는 것: …" "치르는 값: …" 줄이 없으면 머리말만.</summary>
    static (string intro, List<string> gains, List<string> costs) Parts(Decision d)
    {
        var intro = new List<string>();
        var gains = new List<string>();
        var costs = new List<string>();
        foreach (var raw in d.Text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("얻는 것:")) gains.AddRange(Bullets(line["얻는 것:".Length..]));
            else if (line.StartsWith("치르는 값:")) costs.AddRange(Bullets(line["치르는 값:".Length..]));
            else if (line.Length > 0) intro.Add(line);
        }
        return (string.Join("\n", intro), gains, costs);
    }

    /// <summary>"가, 나. 다" 같은 한 줄을 쉼표·마침표로 끊어 항목으로.</summary>
    static IEnumerable<string> Bullets(string s)
    {
        foreach (var part in s.Split(new[] { ". ", ", " }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Trim().TrimEnd('.');
            if (p.Length > 0) yield return p;
        }
    }

    /// <summary>Refresh마다: 결정 목록이 바뀌었으면 카드를 다시 짓고, 아니면 남은 기한만 고친다.</summary>
    void RefreshDecisions()
    {
        if (_decCards == null) return;
        string sig = string.Join(",", S.Pending.Select(d => d.Id));
        if (sig != _decSig)
        {
            _decSig = sig;
            Clear(_decCards);
            _decLeft.Clear();
            foreach (var d in S.Pending) _decCards.AddChild(DecisionCard(d));
        }
        foreach (var d in S.Pending)
            if (_decLeft.TryGetValue(d.Id, out var l))
            {
                int left = Math.Max(0, d.ExpiresTick - S.Tick);
                l.Text = LeftText(left);
                l.AddThemeColorOverride("font_color", left <= 2 ? CostCol : Faint(0.75f));
            }
    }

    void Choose(Decision d, int k)
    {
        if (_window == "decision") CloseModal();
        S.Decide(d.Id, k);
        Game.Touched();
        _decSig = "";
        RefreshDecisions();
    }

    static StyleBoxFlat CardBox(Color rim)
    {
        var sb = Box(new Color("101e24", 0.96f), rim, 1, 12, 10);
        sb.BorderWidthLeft = 4;
        sb.ContentMarginLeft = 14;
        sb.ShadowColor = new Color(0, 0, 0, 0.35f);
        sb.ShadowSize = 6;
        return sb;
    }

    /// <summary>오른쪽에 쌓이는 결정 카드. 시간은 흐르고, 여기서 바로 고를 수 있다.</summary>
    Control DecisionCard(Decision d)
    {
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(390, 0) };
        card.AddThemeStyleboxOverride("panel", CardBox(new Color("e2b84f")));
        var v = V(6);
        card.AddChild(v);

        var head = H(6);
        head.AddChild(KindChip(d.Kind));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var left = L("", 13, Faint(0.75f));
        _decLeft[d.Id] = left;
        head.AddChild(left);
        v.AddChild(head);

        v.AddChild(L(d.Title, 18, null, true, 360));
        var (intro, gains, costs) = Parts(d);
        if (gains.Count + costs.Count > 0)
        {
            if (gains.Count > 0) v.AddChild(L("＋ " + string.Join(", ", gains), 14, GainCol, true, 360));
            if (costs.Count > 0) v.AddChild(L("－ " + string.Join(", ", costs), 14, CostCol, true, 360));
        }
        else if (intro.Length > 0) v.AddChild(L(intro, 14, Faint(0.8f), true, 360));

        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddThemeConstantOverride("v_separation", 6);
        for (int i = 0; i < d.Choices.Count; i++)
        {
            int k = i;
            var ch = d.Choices[i];
            var b = B(ch.Label, () => Choose(d, k), ch.Hint, false, 15);
            b.CustomMinimumSize = new Vector2(0, 34);
            b.AddThemeStyleboxOverride("normal", ChoiceBox(i == 0, false));
            b.AddThemeStyleboxOverride("hover", ChoiceBox(i == 0, true));
            row.AddChild(b);
        }
        var more = B("자세히", () => ShowDecision(d), "얻는 것과 치르는 값을 크게 봅니다", false, 14, "info");
        more.CustomMinimumSize = new Vector2(0, 34);
        row.AddChild(more);
        v.AddChild(row);
        return card;
    }

    static StyleBoxFlat ChoiceBox(bool first, bool hover)
    {
        var bg = first ? new Color("2c5560") : new Color("26343a");
        if (hover) bg = bg.Lightened(0.15f);
        var sb = Box(bg, first ? new Color(Ui.Accent, 0.8f) : new Color(1, 1, 1, 0.12f), 1, 8, 7);
        sb.ContentMarginLeft = sb.ContentMarginRight = 12;
        return sb;
    }

    static Control KindChip(string kind)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var sb = Box(new Color("e2b84f", 0.18f), new Color("e2b84f", 0.7f), 1, 3, 999);
        sb.ContentMarginLeft = 8;
        sb.ContentMarginRight = 10;
        chip.AddThemeStyleboxOverride("panel", sb);
        var h = H(5);
        h.MouseFilter = Control.MouseFilterEnum.Ignore;
        h.AddChild(new Glyph(KindIcon(kind), 16, Colors.White) { MouseFilter = Control.MouseFilterEnum.Ignore });
        var l = L(KindName(kind), 13, new Color("f1d48a"));
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        h.AddChild(l);
        chip.AddChild(h);
        return chip;
    }

    /// <summary>얻는 것/치르는 값 상자.</summary>
    static Control ListBox(string title, List<string> items, Color col, string mark)
    {
        var p = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var sb = Box(new Color(col, 0.10f), new Color(col, 0.55f), 1, 12, 8);
        p.AddThemeStyleboxOverride("panel", sb);
        var v = V(4);
        p.AddChild(v);
        v.AddChild(L(title, 15, col));
        foreach (var it in items) v.AddChild(L($"{mark} {it}", 16, Ink, true, 250));
        return p;
    }

    /// <summary>결정의 큰 창. 시간은 계속 흐른다(창이 떠 있는 동안만 멈춘다).</summary>
    public void ShowDecision(Decision d)
    {
        const float W = 600;
        var v = V(12);
        v.CustomMinimumSize = new Vector2(W, 0);
        var head = H(8);
        head.AddChild(KindChip(d.Kind));
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        int left = Math.Max(0, d.ExpiresTick - S.Tick);
        head.AddChild(L(LeftText(left), 14, left <= 2 ? CostCol : Faint(0.75f)));
        v.AddChild(head);
        if (Art.Tex($"events/{d.Kind}.png") is { } banner)
        {
            var artBox = new PanelContainer { ClipChildren = CanvasItem.ClipChildrenMode.AndDraw, CustomMinimumSize = new Vector2(W, 184) };
            var asb = Box(Colors.Black, new Color(Ui.Rim, 0.65f), 1, 0, 8);
            artBox.AddThemeStyleboxOverride("panel", asb);
            artBox.AddChild(new TextureRect { Texture = banner, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(W, 184), MouseFilter = Control.MouseFilterEnum.Ignore });
            v.AddChild(artBox);
        }
        v.AddChild(L(d.Title, 28, null, true, W));
        var (intro, gains, costs) = Parts(d);
        if (intro.Length > 0) v.AddChild(L(intro, 16, Faint(0.78f), true, W));
        if (gains.Count + costs.Count > 0)
        {
            var two = H(10);
            if (gains.Count > 0) two.AddChild(ListBox("얻는 것", gains, GainCol, "＋"));
            if (costs.Count > 0) two.AddChild(ListBox("치르는 값", costs, CostCol, "－"));
            v.AddChild(two);
        }
        v.AddChild(L("어떻게 할까요?", 15, Faint(0.6f)));
        for (int i = 0; i < d.Choices.Count; i++)
        {
            int k = i;
            var ch = d.Choices[i];
            var b = new Button { FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(W, ch.Hint.Length > 0 ? 64 : 46) };
            b.AddThemeStyleboxOverride("normal", ChoiceBox(i == 0, false));
            b.AddThemeStyleboxOverride("hover", ChoiceBox(i == 0, true));
            b.AddThemeStyleboxOverride("pressed", ChoiceBox(i == 0, true));
            b.Pressed += () => Sfx.Play("click");
            b.Pressed += () => Choose(d, k);
            var inner = V(2);
            inner.MouseFilter = Control.MouseFilterEnum.Ignore;
            inner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            inner.OffsetLeft = 16;
            inner.OffsetRight = -16;
            inner.Alignment = BoxContainer.AlignmentMode.Center;
            var lab = L(ch.Label, 19);
            lab.MouseFilter = Control.MouseFilterEnum.Ignore;
            inner.AddChild(lab);
            if (ch.Hint.Length > 0)
            {
                var hint = L(ch.Hint, 14, Faint(0.72f), true);
                hint.MouseFilter = Control.MouseFilterEnum.Ignore;
                inner.AddChild(hint);
            }
            b.AddChild(inner);
            v.AddChild(b);
        }
        var foot = H();
        string last = d.Choices.Count > 0 ? d.Choices[^1].Label : "";
        foot.AddChild(L($"기한 안에 고르지 않으면 「{last}」로 정해집니다.", 13, Faint(0.6f)));
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foot.AddChild(B("나중에", CloseModal, "오른쪽 카드에서 다시 고를 수 있습니다.", false, 14, "back"));
        v.AddChild(foot);
        ShowModal(v, "decision");
    }
}
