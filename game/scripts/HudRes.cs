using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>
/// 오른쪽 위 자원 줄(실시간 전략 게임처럼): 작은 아이콘 + 숫자 + 철마다 늘고 준 양을 한 줄로. 식량은 창고의 저장 한도와 채움 막대를 함께 보인다.
/// 늘면 초록, 줄면 빨강으로 잠깐 번쩍인다.
/// 그리고 위 띠 가운데의 상태 막대(영토·불만·지도자 지지·단합) — 이름만 보고도 뜻을 알 수 있게 쉬운 말과 막대로 보인다.
/// </summary>
public partial class Hud
{
    readonly Dictionary<string, (Label val, Label rate, Control cell)> _res = new();
    readonly Dictionary<string, double> _resLast = new();
    readonly Dictionary<string, float> _resFlash = new();
    readonly Dictionary<string, int> _resDir = new();
    // 철마다의 증감: 지난 철 끝의 값과 이번 철의 차이
    readonly Dictionary<string, double> _seasonBase = new(), _seasonRate = new();
    int _rateTick = int.MinValue;
    ProgressBar _storeBar = null!;

    static readonly Color ResInk = new(1f, 0.97f, 0.9f);
    static readonly Color UpCol = new("7ee07a"), DownCol = new("ff7d62");

    /// <summary>자원 줄 한 칸의 정의: 키, 아이콘, 설명.</summary>
    static readonly (string key, string icon, string tip)[] ResDefs =
    {
        ("food", "food", ""),
        ("wood", "wood", "나무 — 벌목으로 얻습니다.\n움집·창고·사냥막·목책 같은 건물을 짓는 데 씁니다."),
        ("stone", "stone", "돌 — 채석으로 얻습니다.\n곡창·제단·망루를 짓는 데 씁니다."),
        ("coin", "coin", "국고 — 나라의 돈. 건물·도로·군대·제도 개혁에 씁니다.\n1은 한 해 세입쯤 됩니다."),
        ("pop", "pop", "인구 — 살아 있는 사람 수.\n작은 글씨는 그중 일할 수 있는 사람(10살 이상)."),
    };

    /// <summary>위 띠 오른쪽에 붙는 자원 줄. 칸마다 [아이콘 숫자 +증감], 칸 사이에 가는 금.</summary>
    Control ResourceStrip()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var sb = new StyleBoxFlat { BgColor = new Color("0d1b20", 0.92f), BorderColor = new Color("82724d", 0.7f) };
        sb.SetCornerRadiusAll(8);
        sb.SetBorderWidthAll(1);
        sb.ContentMarginLeft = 10;
        sb.ContentMarginRight = 12;
        sb.ContentMarginTop = 4;
        sb.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", sb);
        var row = H(10);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(row);
        bool first = true;
        foreach (var (key, icon, tip) in ResDefs)
        {
            var cell = V(1);
            cell.TooltipText = tip;
            cell.MouseFilter = Control.MouseFilterEnum.Stop;
            cell.Alignment = BoxContainer.AlignmentMode.Center;
            var line = H(4);
            line.MouseFilter = Control.MouseFilterEnum.Ignore;
            if (!first)
            {
                line.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.08f), CustomMinimumSize = new Vector2(1, 22), MouseFilter = Control.MouseFilterEnum.Ignore });
                line.AddChild(new Control { CustomMinimumSize = new Vector2(4, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            first = false;
            line.AddChild(new Glyph(icon, 22, Colors.White) { MouseFilter = Control.MouseFilterEnum.Ignore });
            var v = new Label { Text = "0", MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
            v.AddThemeFontSizeOverride("font_size", 17);
            v.AddThemeColorOverride("font_color", ResInk);
            v.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            v.AddThemeConstantOverride("outline_size", 3);
            var rate = new Label { Text = "", MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
            rate.AddThemeFontSizeOverride("font_size", 12);
            line.AddChild(v);
            line.AddChild(rate);
            cell.AddChild(line);
            if (key == "food")
            {
                // 창고가 얼마나 찼는지: 가는 막대. 넘치면 붉게(넘친 것은 빨리 썩는다).
                _storeBar = Meter(0, 60, new Color("e2b84f"), 3);
                _storeBar.MouseFilter = Control.MouseFilterEnum.Ignore;
                _storeBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                cell.AddChild(_storeBar);
            }
            row.AddChild(cell);
            _res[key] = (v, rate, cell);
        }
        return panel;
    }

    /// <summary>겨울까지 남은 철(지금이 겨울이면 0).</summary>
    int SeasonsToWinter => (3 - S.Season + 4) % 4;

    static readonly Color WarnCol = new("ffc45a");

    /// <summary>지난 철에 배를 곯은 사람 수(굶주림은 쌓이면 죽음으로 간다).</summary>
    int HungryCount() => S.Living.Count(p => p.HungrySeasons > 0);

    /// <summary>식량 칸의 위험 정도: 0 괜찮음 · 1 창고가 넘침(썩는 중) · 2 모자람(굶는 사람이 있거나 한 철 먹을 것도 안 됨).</summary>
    int FoodLevel()
    {
        if (!S.Tribal) return 0;
        if (HungryCount() > 0 || S.CommonStore < 1 || S.CommonStore < S.SeasonNeed()) return 2;
        return S.CommonStore > S.StoreCap() + 0.5 ? 1 : 0;
    }

    /// <summary>창고와 겨울나기 설명(식량 칸의 툴팁).</summary>
    string FoodTip()
    {
        double cap = S.StoreCap(), need = S.SeasonNeed();
        int toWinter = SeasonsToWinter;
        string winter = toWinter == 0 ? "지금은 겨울: 들에서 나는 것이 거의 없어 창고에 모아 둔 것으로 버팁니다."
            : $"겨울까지 {toWinter}철. 겨울 한 철에 {need:0}쯤 먹습니다 — 그 전에 창고를 채워 두세요.";
        string warn = "";
        int hungry = HungryCount();
        if (hungry > 0) warn += $"⚠ 굶는 사람 {hungry}명 — 굶주림이 이어지면 약한 사람부터 죽습니다. 먹을 것을 내는 일에 사람을 더 붙이세요.\n";
        else if (S.CommonStore < 1) warn += "⚠ 창고가 비었습니다 — 이대로면 곧 굶는 사람이 나옵니다.\n";
        else if (S.CommonStore < need) warn += $"⚠ 창고에 한 철 먹을 것({need:0})도 없습니다.\n";
        if (S.CommonStore > cap + 0.5) warn += $"⚠ 한도를 {S.CommonStore - cap:0} 넘었습니다 — 넘친 것은 빨리 썩습니다. 창고를 더 지으세요.\n";
        if (warn.Length > 0) warn += "\n";
        return warn + "식량 — 공동 창고에 모아 둔 먹을거리.\n" +
               $"창고 {S.CommonStore:0} / 한도 {cap:0}. 한도를 넘친 것은 쌓아 둘 데가 없어 철마다 {StructureCatalog.ExcessRot:P0}씩 썩습니다.\n" +
               $"창고를 지으면 한도가 {StructureCatalog.StoreEach:0}씩 늘어납니다(건설 탭).\n\n{winter}\n" +
               "작은 숫자는 지난 철에 늘고 준 양(먹은 것까지 뺀 값)입니다.";
    }

    void RefreshResources()
    {
        if (_res.Count == 0) return;
        int folk = S.Living.Count();
        bool tribal = S.Tribal;
        double pop = tribal ? folk : S.Realm.Pop;
        var now = new Dictionary<string, double>
        {
            ["food"] = S.CommonStore, ["wood"] = S.Wood, ["stone"] = S.Stone, ["coin"] = S.Realm.Treasury, ["pop"] = pop,
        };
        // 철이 바뀌면 지난 철의 증감을 굳힌다.
        if (S.Tick != _rateTick)
        {
            bool fresh = _rateTick == int.MinValue || S.Tick < _rateTick || S.Tick - _rateTick > 1;
            foreach (var (k, v) in now)
            {
                _seasonRate[k] = fresh || !_seasonBase.TryGetValue(k, out var b) ? 0 : v - b;
                _seasonBase[k] = v;
            }
            _rateTick = S.Tick;
        }

        void Set(string k, string text, int bad, bool show, string sub = "")
        {
            var (val, rate, cell) = _res[k];
            cell.Visible = show;
            if (!show) return;
            double num = now[k];
            val.Text = text;
            if (_resLast.TryGetValue(k, out var last) && Math.Abs(num - last) >= (k == "coin" ? 0.05 : 0.5))
            {
                _resFlash[k] = 1;
                _resDir[k] = num > last ? 1 : -1;
            }
            _resLast[k] = num;
            PaintRes(k, bad);
            double d = _seasonRate.GetValueOrDefault(k);
            if (sub.Length > 0)
            {
                rate.Text = sub;
                rate.AddThemeColorOverride("font_color", new Color("a9bcb8"));
            }
            else if (Math.Abs(d) >= (k == "coin" ? 0.05 : 0.5))
            {
                rate.Text = (d > 0 ? "+" : "−") + (k == "coin" ? $"{Math.Abs(d):0.0}" : Art.Num(Math.Abs(d)));
                rate.AddThemeColorOverride("font_color", d > 0 ? UpCol : DownCol);
            }
            else rate.Text = "";
        }
        double cap = S.StoreCap(), need = S.SeasonNeed();
        int foodLv = FoodLevel(), hungry = tribal ? HungryCount() : 0;
        Set("food", $"{S.CommonStore:0}/{cap:0}" + (foodLv > 0 ? " !" : ""), foodLv, tribal);
        if (tribal)
        {
            if (hungry > 0)
            {
                // 굶는 사람이 있으면 증감 대신 그 수를 붉게
                var rl = _res["food"].rate;
                rl.Text = $"굶는 {hungry}명";
                rl.AddThemeColorOverride("font_color", DownCol);
            }
            _storeBar.Value = Math.Clamp(S.CommonStore / Math.Max(1, cap), 0, 1);
            var fill = S.CommonStore > cap ? Art.Bad : S.CommonStore < need ? new Color("ec6a50") : new Color("e2b84f");
            _storeBar.AddThemeStyleboxOverride("fill", Box(fill, new Color(0, 0, 0, 0), 0, 0, 999));
            _res["food"].cell.TooltipText = FoodTip();
        }
        Set("wood", $"{S.Wood:0}", 0, true);
        Set("stone", $"{S.Stone:0}", 0, true);
        var bf = !tribal ? S.ForecastTreasury() : default;
        string coinSub = !tribal
            ? (Math.Abs(bf.Net) is > 0.005 and < 0.1
                ? $"{(bf.Net >= 0 ? "+" : "−")}{Math.Abs(bf.Net):0.00}/년"
                : $"{(bf.Net >= 0 ? "+" : "−")}{Math.Abs(bf.Net):0.0}/년")
            : "";
        Set("coin", $"{S.Realm.Treasury:0.0}", S.Realm.Treasury < 0.5 ? 2 : 0, !tribal, coinSub);
        if (!tribal)
        {
            _res["coin"].rate.AddThemeColorOverride("font_color", bf.Net >= 0 ? UpCol : DownCol);
            _res["coin"].cell.TooltipText = CoinTip(bf);
        }
        Set("pop", tribal ? $"{folk}" : Art.Num(pop), 0, true, tribal ? $"일꾼 {S.Workers().Count}" : "");
    }

    string CoinTip(Simulation.BudgetForecast bf)
    {
        var sb = new System.Text.StringBuilder();
        if (S.Realm.Treasury < 0) sb.Append("⚠ 국고가 적자(빚) 상태입니다 — 군대가 약해지고 오래 이어지면 파산으로 몰락합니다.\n\n");
        else if (!S.Inst.Taxation) sb.Append("💡 아직 조세 제도가 없어 국고를 크게 쌓을 수 없습니다 (한도 3.0).\n\n");
        sb.Append($"국고 {S.Realm.Treasury:0.00} — 건물·도로·군단 징집·외교 사절·개혁에 씁니다.\n");
        sb.Append($"이번 해 겨울 예상 정산: {(bf.Net >= 0 ? "+" : "−")}{Math.Abs(bf.Net):0.00}/년\n");
        sb.Append($"  ＋ 세입·교역·자원: +{bf.Income:0.00}\n");
        sb.Append($"  − 행정·제도 지출: −{bf.BaseExpense:0.00}\n");
        sb.Append($"  − 건물·도로·군단 유지비: −{bf.Upkeep:0.00}");
        if (bf.Drain > 0.01) sb.Append($"\n  − 국고 과잉 유출(부패·사치, 3 초과분): −{bf.Drain:0.00}");
        return sb.ToString();
    }

    /// <summary>bad: 0 보통 · 1 주의(노랑) · 2 위험(빨강).</summary>
    void PaintRes(string k, int bad)
    {
        if (!_res.TryGetValue(k, out var r)) return;
        float f = _resFlash.GetValueOrDefault(k);
        var flash = _resDir.GetValueOrDefault(k) > 0 ? UpCol : DownCol;
        r.val.AddThemeColorOverride("font_color", (bad >= 2 ? DownCol : bad == 1 ? WarnCol : ResInk).Lerp(flash, f * 0.85f));
    }

    /// <summary>번쩍임이 서서히 빠진다.</summary>
    void FadeResourceFlash(float dt)
    {
        if (_resFlash.Count == 0) return;
        foreach (var k in _resFlash.Keys.ToList())
        {
            float f = Math.Max(0, _resFlash[k] - dt * 1.4f);
            _resFlash[k] = f;
            int bad = k == "food" ? FoodLevel() : k == "coin" && S.Realm.Treasury < 0.5 ? 2 : 0;
            PaintRes(k, bad);
            if (f <= 0) _resFlash.Remove(k);
        }
    }

    // ───────── 상태 막대: 쉬운 말 + 막대 ─────────

    readonly Dictionary<string, (Control chip, Label name, Label val, ProgressBar bar, Label delta)> _meter = new();

    /// <summary>상태 막대의 정의: 키, 아이콘. 이름과 설명은 단계에 따라 바뀐다(MeterText).</summary>
    static readonly (string key, string icon)[] MeterDefs = { ("land", "land"), ("unrest", "unrest"), ("legit", "legit"), ("cohesion", "cohesion") };

    (string name, string tip) MeterText(string key)
    {
        bool tribal = S.Tribal;
        string boss = tribal ? "우두머리" : "통치자";
        return key switch
        {
            "land" => ("영토", "우리 땅의 수 / 다스릴 수 있는 한도.\n한도를 넘으면 단합이 깎이고 돈이 더 듭니다. 기술과 제도가 한도를 늘립니다."),
            "unrest" => ("불만", "백성이 얼마나 성나 있는지.\n높으면 봉기와 혁명이 일어납니다.\n오르는 것: 굶주림, 무거운 세금, 불평등, 패전.\n내리는 것: 풍년, 잔치, 구휼, 공감 많은 통치자."),
            "legit" => ($"{boss} 지지", $"사람들이 지금의 {boss}를 얼마나 믿고 따르는지.\n낮아지면 자리 다툼과 정변이 일어나 {boss}가 바뀝니다.\n오르는 것: 승리, 풍년, 오래 다스림.\n깎이는 것: 패전, 기근, 잦은 제도 개혁."),
            _ => ("단합", tribal
                ? "무리가 '우리는 한편'이라고 느끼는 정도.\n낮아지면 무리가 갈라지고 사람들이 떠납니다.\n깎이는 것: 원한, 다툼, 굶주림."
                : "온 나라가 '우리는 한 나라'라고 느끼는 정도.\n낮아지면 지방이 떨어져 나가고 나라가 쪼개집니다.\n깎이는 것: 다스릴 한도를 넘은 영토, 박해, 불평등."),
        };
    }

    Control StatusMeter(string key, string icon)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        chip.AddThemeStyleboxOverride("panel", MeterBox(false));
        var row = H(7);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(row);
        row.AddChild(new Glyph(icon, 24, Colors.White) { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var col = V(1);
        col.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(col);
        var top = H(6);
        top.MouseFilter = Control.MouseFilterEnum.Ignore;
        var name = L("", 13, new Color("b9cbc7"));
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        var val = L("", 17);
        val.MouseFilter = Control.MouseFilterEnum.Ignore;
        var delta = L("", 12);
        delta.MouseFilter = Control.MouseFilterEnum.Ignore;
        delta.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        top.AddChild(name);
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        top.AddChild(val);
        top.AddChild(delta);
        col.AddChild(top);
        var bar = Meter(0, 86, Art.Good, 5);
        bar.MouseFilter = Control.MouseFilterEnum.Ignore;
        col.AddChild(bar);
        _meter[key] = (chip, name, val, bar, delta);
        return chip;
    }

    static StyleBoxFlat MeterBox(bool bad)
    {
        var sb = Box(bad ? new Color("4a2620") : new Color("12232a"), bad ? new Color(Art.Bad, 0.9f) : new Color("82724d", 0.55f), 1, 4, 6);
        sb.ContentMarginLeft = 8;
        sb.ContentMarginRight = 10;
        sb.ContentMarginTop = 3;
        sb.ContentMarginBottom = 4;
        return sb;
    }

    /// <summary>값(0~1)과 좋은 쪽(높을수록 좋은가)으로 막대 색을 정한다.</summary>
    static Color Level(double good) => good >= 0.6 ? new Color("6cc46a") : good >= 0.35 ? new Color("e2b84f") : new Color("ec6a50");

    void RefreshMeters()
    {
        if (_meter.Count == 0) return;
        var r = S.Realm;
        bool tribal = S.Tribal;
        int lands = S.Lands(0).Count();
        void Set(string k, string text, double fill, double good, bool show, double num, bool pct, bool upGood)
        {
            var (chip, name, val, bar, delta) = _meter[k];
            chip.Visible = show;
            if (!show) return;
            var (n, tip) = MeterText(k);
            name.Text = n;
            chip.TooltipText = tribal ? tip : tip + Why(k);
            val.Text = text;
            bool bad = good < 0.35;
            val.AddThemeColorOverride("font_color", bad ? new Color("ff9a80") : Ink);
            chip.AddThemeStyleboxOverride("panel", bad ? MeterBoxBad : MeterBoxOk);
            bar.Value = Math.Clamp(fill, 0, 1);
            bar.AddThemeStyleboxOverride("fill", Box(Level(good), new Color(0, 0, 0, 0), 0, 0, 999));
            if (!_yearBase.TryGetValue(k, out var b)) _yearBase[k] = b = num;
            double d = (num - b) * (pct ? 100 : 1);
            bool shown = Math.Abs(d) >= 1;
            delta.Visible = shown;
            if (!shown) return;
            delta.Text = (d > 0 ? "▲" : "▼") + $"{Math.Abs(d):0}";
            delta.TooltipText = "올해 들어 바뀐 양";
            delta.AddThemeColorOverride("font_color", (d > 0) == upGood ? UpCol : DownCol);
        }
        if (S.Year != _baseYear)
        {
            _baseYear = S.Year;
            _yearBase.Clear();
        }
        double over = S.AdminCap > 0 ? (double)lands / S.AdminCap : 1;
        Set("land", $"{lands}/{S.AdminCap}", over, lands > S.AdminCap ? 0.1 : 0.8, !tribal, lands, false, true);
        Set("unrest", Pct(r.Unrest), r.Unrest, 1 - r.Unrest * 1.3, !tribal, r.Unrest, true, false);
        Set("legit", Pct(r.Legitimacy), r.Legitimacy, r.Legitimacy, true, r.Legitimacy, true, true);
        Set("cohesion", Pct(r.Cohesion), r.Cohesion, r.Cohesion, true, r.Cohesion, true, true);
    }

    static readonly StyleBoxFlat MeterBoxOk = MeterBox(false), MeterBoxBad = MeterBox(true);

    /// <summary>
    /// "왜 이 값인가" — 스텔라리스·빅토리아의 툴팁처럼, 값이 향해 가는 목표와 그것을 미는 갈래를 큰 것부터 보인다.
    /// 나라 시절에만 있다(부족 시절의 결속·지지는 사건으로만 움직인다).
    /// </summary>
    string Why(string key)
    {
        var r = S.Realm;
        List<Simulation.MoodPart> parts;
        double now, rate;
        switch (key)
        {
            case "cohesion": parts = S.CohesionParts(); now = r.Cohesion; rate = 6; break;
            case "legit": parts = S.LegitimacyParts(); now = r.Legitimacy; rate = 15; break;
            case "unrest":
                parts = S.UnrestParts();
                parts.AddRange(S.RepressParts().Where(p => p.Value != 0).Select(p => new Simulation.MoodPart(p.What + "(억누름)", -p.Value)));
                now = r.Unrest; rate = 12; break;
            default:
                int lands = S.Lands(0).Count(), cap = S.AdminCap;
                return lands > cap ? $"\n\n지금 한도를 {lands - cap}칸 넘었습니다 — 단합 −{7 * (lands - cap)}%p, 불만 +{5 * (lands - cap)}%p." : "";
        }
        double target = Math.Clamp(parts.Sum(p => p.Value), 0, 1);
        var sb = new System.Text.StringBuilder();
        sb.Append($"\n\n지금 {now * 100:0}% → 향하는 값 {target * 100:0}%");
        sb.Append(Math.Abs(target - now) < 0.01 ? " (멈춤)" : target > now ? $" (해마다 남은 차이의 {rate}%씩 오름)" : $" (해마다 남은 차이의 {rate}%씩 내림)");
        bool upGood = key != "unrest";
        foreach (var p in parts.Where(p => p.What != "기본" && Math.Abs(p.Value) >= 0.005).OrderByDescending(p => Math.Abs(p.Value)).Take(8))
            sb.Append($"\n  {(p.Value > 0 ? "+" : "−")}{Math.Abs(p.Value) * 100,3:0}  {p.What}{((p.Value > 0) == upGood ? "" : "  ⚠")}");
        if (key == "unrest" && r.Suppressed > 0.01) sb.Append($"\n억눌려 쌓인 불만 {r.Suppressed * 100:0} — 억누름이 풀리면 한꺼번에 터집니다.");
        return sb.ToString();
    }

    Rect2? StatRect()
    {
        Rect2? r = null;
        foreach (var (chip, _, _, _, _) in _meter.Values)
        {
            if (!chip.Visible) continue;
            var g = chip.GetGlobalRect();
            r = r is { } a ? a.Merge(g) : g;
        }
        foreach (var (_, _, cell) in _res.Values)
        {
            if (!cell.Visible) continue;
            var g = cell.GetGlobalRect();
            r = r is { } a ? a.Merge(g) : g;
        }
        return r;
    }
}
