namespace Instinct.Sim;

/// <summary>백성의 계층. 제도마다 얻는 계층과 잃는 계층이 있고, 잃는 쪽은 가만있지 않는다.</summary>
public enum Estate { Nobles, Commoners, Bound, Workers }

/// <summary>제도를 들이거나 없앨 때 치를 값의 미리보기.</summary>
public sealed record ReformPreview(bool Adopt, double Cost, double LegitHit, double Grievance, IReadOnlyList<Estate> Angered, string Text);

// 계층(귀족·평민·예속민·노동자), 개혁의 값과 반발, 백성에서 솟는 인물과 밀려나는 가문.
public sealed partial class Simulation
{
    /// <summary>한 해에 고친 제도가 많을수록 다음 개혁의 값과 반발이 커진다.</summary>
    double ReformScale => 1 + 0.5 * Realm.ReformHeat;

    /// <summary>계층이 쥔 힘. 귀족은 수가 적어도 칼과 땅을 쥐고 있다.</summary>
    public double EstatePower(Estate e)
    {
        var i = Inst;
        if (e != Estate.Nobles) return Realm.Share[(int)e];
        return Math.Clamp(0.4 + B(i.Feudalism, 0.2) - B(i.Absolutism, 0.15) - B(i.Suffrage, 0.15), 0.1, 0.7);
    }

    /// <summary>계층의 분노(0~1): 처지에서 오는 것 + 개혁으로 쌓인 것(Grievance).</summary>
    public double EstateAnger(Estate e)
    {
        var r = Realm;
        var i = Inst;
        if (r.Stage == Stage.Tribe || r.Share[(int)e] < 0.005) return 0;
        double a = e switch
        {
            Estate.Nobles => 0.1 + B(i.Absolutism, 0.12) + B(i.Suffrage, 0.08) + B(i.Welfare, 0.05) + B(i.LaborLaws, 0.04)
                             - B(i.Feudalism, 0.08) + 0.3 * (0.5 - r.Legitimacy),
            Estate.Commoners => r.Unrest,
            Estate.Bound => 0.3 + 0.3 * r.Inequality + 0.3 * r.Unrest,
            _ => 0.2 + B(i.Factories && !i.LaborLaws, 0.25) + 0.3 * r.Inequality - B(i.Welfare, 0.15) - B(i.Suffrage, 0.05),
        };
        return Clamp01(a + r.Grievance[(int)e]);
    }

    /// <summary>지금 이 제도를 뒤집으면(없으면 들이고, 있으면 없애고) 무엇을 치르나.</summary>
    public ReformPreview PreviewReform(InstitutionDef d)
    {
        bool adopt = !d.Get(Inst);
        // 들일 때는 잃는 계층이, 없앨 때는 누리던 계층이 들고일어난다. 가진 것을 빼앗길 때가 더 아프다.
        var angered = (adopt ? d.Losers : d.Winners).ToList();
        double scale = ReformScale;
        if (Realm.Stage == Stage.Tribe)
        {
            string who = angered.Count == 0 ? "" : angered.Contains(Estate.Nobles) ? " · 반대하는 쪽: 윗자리 사람들" : " · 반대하는 쪽: 아랫자리 사람들";
            return new(adopt, 0, 0, 0, angered, "우두머리의 위신이 깎인다" + who);
        }
        // 조세가 서기 전에는 치를 곳간이 없다: 값은 통치자의 위신으로 낸다.
        bool coin = Inst.Taxation;
        double cost = coin ? (adopt ? 0.3 + 0.2 * (int)d.Era : 0.15 + 0.1 * (int)d.Era) * scale : 0;
        double legit = (0.02 + 0.03 * angered.Count) * scale * (coin ? 1 : 2);
        double grief = (adopt ? 0.25 : 0.35) * scale;
        string text = (coin ? $"국고 −{cost:F1}" : "통치자의 위신이 깎인다") + (angered.Count > 0 ? " · 반대하는 쪽: " + string.Join(", ", angered.Select(e => Catalog.EstateNames[(int)e])) : "")
                      + (Realm.ReformHeat >= 0.5 ? " · 잦은 개혁으로 값이 올랐다" : "");
        return new(adopt, cost, legit, grief, angered, text);
    }

    /// <summary>
    /// 제도를 들이거나 없앤다. 값(곳간·정통성)을 치르고, 잃는 계층의 분노가 쌓이며,
    /// 이름 있는 사람 가운데 잃는 자들이 통치자에게 원한을 품는다. 바뀐 것이 없으면 false.
    /// </summary>
    public bool Enact(InstitutionDef d, bool on)
    {
        if (d.Get(Inst) == on) return false;
        if (on && !Available(d)) return false;
        var r = Realm;
        var pv = PreviewReform(d);
        d.Set(Inst, on);

        if (r.Stage != Stage.Tribe)
        {
            r.Treasury = Math.Max(-6, r.Treasury - pv.Cost);
            r.LegitShock += pv.LegitHit;
            foreach (var e in pv.Angered) r.Grievance[(int)e] = Math.Min(1, r.Grievance[(int)e] + pv.Grievance);
        }
        else if (Chief is { Alive: true } ch) ch.Status = Math.Max(0, ch.Status - 0.3);
        r.ReformHeat += 1;

        // 이름 있는 사람들의 반응: 윗자리가 잃으면 욕심·야심 큰 윗사람이, 아랫자리가 잃으면 성마른 아랫사람이 원한을 품는다.
        var resent = new List<Person>();
        if (Chief is { Alive: true } c && pv.Angered.Count > 0)
        {
            var court = Court().Where(p => p != c);
            bool top = pv.Angered.Contains(Estate.Nobles);
            if (top || r.Stage == Stage.Tribe)
            {
                resent = (top
                        ? court.Where(p => p.T(Trait.Greed) + p.T(Trait.Ambition) > 1.0).OrderByDescending(p => p.Status)
                        : court.Where(p => p.T(Trait.Aggression) > 0.5).OrderBy(p => p.Status))
                    .ThenBy(p => p.Id).Take(3).ToList();
                string why = $"{Josa.Eul(d.Name)} {(on ? "들인" : "없앤")} 일로";
                foreach (var p in resent) AddGrudge(p, c, 0.4 * ReformScale, why);
            }
        }

        string cry = pv.Angered.Count == 0 ? ""
            : r.Stage == Stage.Tribe ? ""
            : $" {Josa.Iga(string.Join("·", pv.Angered.Select(e => Catalog.EstateNames[(int)e])))} 반발한다.";
        string grudge = resent.Count > 0 ? $" 이 일로 {NamesOr(resent.Select(P), "")}의 눈빛이 달라졌다." : "";
        Say(on
            ? $"【제도】 {d.Name}: {d.Gain}. 그 값: {d.Cost}.{cry}{grudge}"
            : $"【제도】 {Josa.Eul(d.Name)} 폐지했다.{cry}{grudge}");
        Wrote("enact", d.Key, on ? "1" : "0");
        return true;
    }

    // ───────────────────────── 계층의 한 해 ─────────────────────────

    void EstatesYear()
    {
        var r = Realm;
        var i = Inst;
        double nobles = 0.03 + B(i.Feudalism, 0.02);
        double bound = Math.Min(0.5, B(i.Slavery, 0.10) + B(i.Serfdom, 0.35));
        double workers = r.Urban * (i.Factories ? 0.7 : 0.15);
        double commoners = Math.Max(0.1, 1 - nobles - bound - workers);
        double sum = nobles + bound + workers + commoners;
        var target = new[] { nobles / sum, commoners / sum, bound / sum, workers / sum };
        for (int k = 0; k < 4; k++)
        {
            r.Share[k] += (target[k] - r.Share[k]) * 0.08;
            r.Grievance[k] *= 0.9;
        }
        r.ReformHeat *= 0.6;
    }

    /// <summary>분노한 계층이 저마다의 방식으로 움직인다.</summary>
    internal void EstateEvents()
    {
        var r = Realm;
        var i = Inst;
        double n = EstateAnger(Estate.Nobles);
        if (n > 0.55 && Rnd() < (n - 0.45) * 0.5)
        {
            Say("빼앗긴 것을 세던 귀족들이 움직이기 시작했다.");
            if (i.StandingArmy && Rnd() < 0.5) Coup(); else NobleRevolt();
            r.Grievance[(int)Estate.Nobles] *= 0.4;
        }

        double b = EstateAnger(Estate.Bound);
        if (r.Share[(int)Estate.Bound] > 0.05 && b > 0.6 && Rnd() < (b - 0.5) * 0.4)
        {
            Uprising(Estate.Bound);
            r.Grievance[(int)Estate.Bound] *= 0.4;
        }

        double w = EstateAnger(Estate.Workers);
        if (r.Share[(int)Estate.Workers] > 0.1 && w > 0.45 && Rnd() < (w - 0.3) * 0.5)
        {
            r.Treasury -= 0.4;
            r.Unrest = Clamp01(r.Unrest + 0.06);
            r.Grievance[(int)Estate.Workers] *= 0.6;
            Say("【총파업】 굴뚝이 식고 항구가 멈췄다. 일하는 이들이 제 수를 처음으로 세어 보았다.");
        }
    }

    // ───────────────────────── 솟는 사람, 밀려나는 가문 ─────────────────────────

    /// <summary>백성 가운데 한 사람이 새 가문의 이름을 얻어 윗자리로 올라온다.</summary>
    internal Person RiseFromCommons(double minAge, double status)
    {
        int clan = NewClan();
        var p = NewPerson(Sex.Male, minAge + _rng.Next(12), clan);
        p.Traits[(int)Trait.Ambition] = 0.6 + 0.4 * Rnd();
        p.Status = status;
        p.Food = 4;
        var wife = NewPerson(Sex.Female, 18 + _rng.Next(10), clan);
        Wed(p, wife);
        return p;
    }

    /// <summary>입신: 장사·칼·글로 이름을 얻은 백성이 지배 가문의 끝자리에 앉는다. 오래된 집안은 그것을 곱게 보지 않는다.</summary>
    internal Person Ascend()
    {
        var i = Inst;
        var roles = new List<string> { "용사" };
        if (i.Coinage || i.Guilds) roles.Add("거상");
        if (i.StandingArmy) roles.Add("장수");
        if (i.Writing) roles.Add("서기");
        if (i.PublicSchooling) roles.Add("학자");
        if (i.Factories) roles.Add("공장주");
        if (i.Suffrage) roles.Add("연설가");
        string role = roles[_rng.Next(roles.Count)];

        var olds = Court().Where(k => k.T(Trait.KinBias) > 0.65).OrderByDescending(k => k.Status).ThenBy(k => k.Id).Take(4).ToList();
        var p = RiseFromCommons(24, 2 + Rnd());
        foreach (var k in olds) AddGrudge(k, p, 0.3, "미천한 핏줄이 윗자리에 오른 일로");
        Say($"【입신】 백성 출신 {role} {Josa.Iga(p.Name)} 이름을 얻어 윗자리에 올랐다. 사람들은 그 집안을 {ClanName(p.ClanId)}족이라 불렀다."
            + (olds.Count > 0 ? $" {olds[0].Name} 같은 오랜 집안 사람들은 자리를 내주며 이를 갈았다." : ""));
        return p;
    }

    /// <summary>윗자리는 넓지 않다. 가장 낮은 방계부터 백성 속으로 내려앉는다.</summary>
    internal int Decline(int keep = 120)
    {
        int excess = Living.Count() - keep;
        if (excess <= 0) return 0;
        var chief = Chief;
        var heads = Living.Where(p => p.IsMale && p != chief && p.Master == null && p.Age(Tick) >= 20
                                      && (chief == null || Related(p, chief) < 0.25))
            .OrderBy(p => p.Status).ThenBy(p => p.Id).Take(8).ToList();
        int gone = 0;
        var names = new List<string>();
        foreach (var h in heads)
        {
            if (gone >= excess) break;
            if (!h.Alive) continue;
            var house = new List<Person> { h };
            house.AddRange(h.Wives.Where(w => w.Alive));
            house.AddRange(house.ToList().SelectMany(a => a.Children).Where(k => k.Alive && k.Age(Tick) < 14 && k.Master == null).Distinct());
            names.Add(P(h));
            foreach (var x in house.Distinct().ToList())
            {
                if (!x.Alive) continue;
                Die(x, "낙향");
                gone++;
            }
        }
        if (gone > 0) Minor($"윗자리에서 밀려난 집안들이 백성 속으로 흩어졌다 ({gone}명): {string.Join(", ", names)}.");
        return gone;
    }

    void CourtYear()
    {
        var i = Inst;
        double p = 0.02 + B(i.Coinage, 0.01) + B(i.Guilds, 0.015) + B(i.StandingArmy, 0.01) + B(i.PublicSchooling, 0.025)
                   + B(i.Factories, 0.015) + B(i.Suffrage, 0.02) - B(i.Feudalism, 0.01) - B(i.Serfdom, 0.01);
        if (Rnd() < p) Ascend();
        Decline();
    }
}

/// <summary>'제도 자동 도입': 쓸 수 있는 제도를 해마다 하나쯤, 값을 치를 수 있을 때만 들인다. 실행기와 화면이 함께 쓴다.</summary>
public static class AutoPolicy
{
    public static void Year(Simulation sim, Random policy)
    {
        Works(sim, policy);
        Reform(sim, policy);
    }

    /// <summary>곳간에 여유가 있으면 길 하나나 건물 하나를 짓는다.</summary>
    internal static void Works(Simulation sim, Random policy)
    {
        if (sim.Realm.Stage == Stage.Tribe || sim.Realm.Treasury < 3.5 || sim.Upkeep() > 0.12 || sim.Sites.Count >= 2 || policy.NextDouble() >= 0.5) return;
        var mine = sim.Lands(0).OrderBy(p => p.Id).ToList();
        var roads = mine.Where(p => sim.WhyCantRoad(p.Id).Length == 0).ToList();
        if (roads.Count > 0 && policy.NextDouble() < 0.4)
        {
            sim.BuildRoad(roads[policy.Next(roads.Count)].Id);
            return;
        }
        var open = (from p in mine from d in BuildingCatalog.All
                    where sim.Realm.Treasury - sim.BuildCost(p, d.Kind) >= 2.5 && sim.WhyCantBuild(p.Id, d.Kind).Length == 0
                    select (p, d)).ToList();
        if (open.Count == 0) return;
        var (land, def) = open[policy.Next(open.Count)];
        sim.Build(land.Id, def.Kind);
    }

    static void Reform(Simulation sim, Random policy)
    {
        if (sim.Year < 20 || sim.Realm.ReformHeat >= 0.5 || policy.NextDouble() >= 0.6) return;
        // 식인 풍습은 저절로 들이지 않는다: 그 길은 다스리는 이가 골라야 한다.
        var open = Catalog.All.Where(d => !d.Get(sim.Inst) && sim.Available(d) && d.Key != "cannibalism").ToList();
        if (open.Count == 0) return;
        var d = open[policy.Next(open.Count)];
        if (sim.Realm.Stage != Stage.Tribe && sim.Realm.Treasury - sim.PreviewReform(d).Cost < -1) return; // 빚을 조금 지는 것까지는 감수한다
        sim.Enact(d, true);
    }
}
