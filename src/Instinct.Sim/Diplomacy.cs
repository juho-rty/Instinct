namespace Instinct.Sim;

// 전쟁의 고리를 끊는 수단: 화친, 공물, 혼인 동맹.
// 셋 다 싸움을 멈추지만 값을 치른다 — 그리고 그 값은 대개 우두머리가 아닌 사람이 낸다.
public sealed partial class Simulation
{
    bool AtPeace(Neighbor n) => n.Allied || Tick < n.PeaceUntil;

    /// <summary>
    /// 공동 창고에서 먼저, 모자라면 집집의 곳간에서 거둔다.
    /// 핏줄을 따지는 우두머리는 제 집안을 빼고 거둔다.
    /// </summary>
    double TakeFood(double amount, out bool sparedKin)
    {
        sparedKin = false;
        double taken = Math.Min(CommonStore, amount);
        CommonStore -= taken;
        double rest = amount - taken;
        if (rest <= 0.01 || Chief is not { } c) return taken;

        var payers = Living.Where(h => h.Food > 1 && h.Master == null).ToList();
        var levied = c.T(Trait.KinBias) > 0.6 ? payers.Where(h => Related(c, h) < 0.25).ToList() : payers;
        sparedKin = levied.Count < payers.Count;
        double pool = levied.Sum(h => h.Food - 1);
        if (pool <= 0) return taken;

        double frac = Math.Min(0.6, rest / pool);
        foreach (var h in levied)
        {
            double cut = (h.Food - 1) * frac;
            h.Food -= cut;
            taken += cut;
            if (h != c && cut > 0.5 && h.T(Trait.Greed) > 0.6) AddGrudge(h, c, 0.12, "곳간의 곡식을 거둬 간 일로");
        }
        return taken;
    }

    /// <summary>그 무리에게 피붙이를 잃은 이들은 평화를 배신으로 여긴다. 원한을 품은 사람 수를 돌려준다.</summary>
    int ResentPeace(Neighbor n, Person chief, string why)
    {
        int angry = 0;
        foreach (var (id, loss) in n.Mourners)
        {
            var k = _byId[id];
            if (!k.Alive || k == chief || loss < 0.15) continue;
            AddGrudge(k, chief, 0.2 + 0.6 * loss, why);
            angry++;
        }
        return angry;
    }

    void BreakPeace(Neighbor n, Person leader)
    {
        string what = n.Allied ? "혼인 동맹" : "화친";
        n.Allied = false;
        n.Bride = null;
        n.PeaceUntil = 0;
        n.Hostility += 1.5;
        if (Chief is { } c && c != leader) c.Status -= 0.5;
        Say($"{P(leader)}의 무리가 {what}을 깨고 {Josa.Eul(n.Name)} 쳤다. {Josa.Eun(n.Name)} 이 배신을 잊지 않을 것이다.");
    }

    /// <summary>해마다 봄, 이웃별 관계 점검.</summary>
    void DiplomacyYear(Neighbor n)
    {
        if (n.PeaceUntil > 0 && Tick >= n.PeaceUntil)
        {
            n.PeaceUntil = 0;
            Say($"{Josa.Wa(n.Name)} 맺은 화친의 기한이 다했다.");
        }
        if (n.Allied && n.Bride is { Alive: false } b)
        {
            n.Allied = false;
            n.Bride = null;
            if (b.DeathCause == "살해")
            {
                n.Hostility += 2;
                Say($"시집온 {Josa.Iga(b.Name)} 살해당했다는 소식에 {Josa.Eun(n.Name)} 동맹을 끊고 피를 갚겠다 했다.");
            }
            else
            {
                Say($"시집온 {Josa.Iga(b.Name)} 더는 없으니 {Josa.Wa(n.Name)} 맺은 혼인 동맹도 힘을 잃었다.");
            }
        }
        // 사돈은 다섯 해에 한 번꼴로 제 싸움에 우리를 부른다.
        if (n.Allied && Rnd() < 0.2) AidCall(n);
        foreach (var id in n.Mourners.Keys.ToList())
        {
            double v = n.Mourners[id] * 0.85;
            if (v < 0.1 || !_byId[id].Alive) n.Mourners.Remove(id);
            else n.Mourners[id] = v;
        }
    }

    void Diplomacy()
    {
        if (Chief is not { } c) return;
        foreach (var n in Neighbors)
        {
            if (n.Allied || Tick < n.NextTalkTick) continue;
            if (Inst.MarriageAlliance && !Inst.Cannibalism && n.Hostility < 1.0 && Tick >= n.NextAllianceTick && TryAlliance(n, c)) continue;
            if (Inst.Parley && Tick >= n.PeaceUntil && (n.Hostility > 0.8 || n.Vengeance > 0.8)) TryTruce(n, c);
        }
    }

    // ───────────────────────── 화친 ─────────────────────────

    void TryTruce(Neighbor n, Person c)
    {
        n.NextTalkTick = Tick + 12;
        double gift = TakeFood(6, out _);
        if (gift < 3)
        {
            CommonStore += gift;
            Say($"{Iga(c)} {n.Name}에 화친을 청하려 했으나 들고 갈 선물이 없었다.");
            return;
        }
        n.Food += gift;
        if (Rnd() >= Math.Clamp(0.8 - 0.15 * n.Hostility, 0.15, 0.9))
        {
            Say($"{Josa.Eun(n.Name)} {P(c)}의 선물만 받고 화친을 거절했다.");
            return;
        }
        int years = 6 + _rng.Next(7);
        n.PeaceUntil = Tick + years * 4;
        n.Hostility *= 0.4;
        int angry = ResentPeace(n, c, "원수와 손잡은 일로");
        Say($"{Iga(c)} 선물을 들고 {Josa.Eul(n.Name)} 찾아가 {years}년의 화친을 맺었다."
            + (angry > 0 ? $" 그들에게 피붙이를 잃은 {angry}명은 이를 갈았다." : ""));
    }

    // ───────────────────────── 공물 ─────────────────────────

    internal void PayTribute()
    {
        if (!Inst.Tribute || Chief is not { } c) return;
        int ours = Warriors().Count, folk = Living.Count();
        double stores = CommonStore + Living.Sum(p => p.Food);
        double budget = TributeBudget(stores, folk); // 한 해에 바치는 공물을 다 합쳐도 이만큼을 넘지 않는다
        foreach (var n in Neighbors.OrderByDescending(n => n.Hostility).ThenBy(n => n.Name))
        {
            // 너무 작은 무리에게는 받아 낼 것이 없다.
            if (folk < 8 || AtPeace(n) || !(n.Hostility > 0.6 || n.Fighters > 1.3 * ours))
            {
                n.TributeYears = 0;
                continue;
            }
            // 한번 바치기 시작하면 요구는 해마다 는다.
            double demand = Math.Min(budget, (0.2 * folk + 0.08 * stores) * Math.Min(2, 1 + 0.1 * n.TributeYears));
            if (demand < 1) { n.TributeYears = 0; continue; }
            budget -= demand;
            double paid = TakeFood(demand, out bool sparedKin);
            string extra = "";

            if (paid < demand * 0.7)
            {
                var thrall = Inst.Slavery ? Living.Where(t => t.Master != null).OrderBy(t => t.Id).FirstOrDefault() : null;
                if (thrall == null)
                {
                    CommonStore += paid;
                    n.TributeYears = 0;
                    Say($"{n.Name}에 바칠 공물을 채우지 못했다.");
                    continue;
                }
                extra = $" 식량이 모자라 {P(thrall.Master!)}의 종 {Josa.Eul(thrall.Name)} 대신 넘겼다.";
                Die(thrall, "공물");
            }

            n.Food += paid;
            n.PaidTick = Tick;
            n.Hostility *= 0.85;
            n.TributeYears++;
            c.Status -= 0.15;
            // 자존심 센 사내들은 무릎 꿇은 우두머리를 곱게 보지 않는다.
            foreach (var k in Living.Where(k => k != c && k.IsMale && k.Master == null && k.Age(Tick) is >= 16 and <= 45
                                                && k.T(Trait.Aggression) + k.T(Trait.Ambition) > 1.2).ToList())
                AddGrudge(k, c, 0.12, "이웃에 무릎 꿇고 공물을 바친 일로");

            string text = $"{Iga(c)} {n.Name}에 공물을 바쳤다 (식량 {paid:F0}, {n.TributeYears}년째)."
                          + (sparedKin ? " 우두머리의 집안 곳간은 건드리지 않았다." : "") + extra;
            if (n.TributeYears == 1 || n.TributeYears % 5 == 0 || extra != "") Say(text); else Minor(text);
        }
    }

    /// <summary>한 해 공물의 상한: 쌓아 둔 식량의 4분의 1(무리 사람 수의 절반보다는 적게 바치지 않는다).</summary>
    internal static double TributeBudget(double stores, int folk) => Math.Max(0.25 * stores, folk < 8 ? 0 : 0.5 * folk);

    // ───────────────────────── 혼인 동맹 ─────────────────────────

    /// <summary>시도했으면 true(성사 여부와 무관).</summary>
    internal bool TryAlliance(Neighbor n, Person c)
    {
        var maidens = Living.Where(w => !w.IsMale && w.Master == null && w.Husband == null && !w.Outsider
                                        && w.Age(Tick) is >= 15 and <= 25).ToList();
        var grooms = Living.Where(m => m.IsMale && m.Master == null && m.Wives.Count == 0
                                       && m.Age(Tick) is >= 17 and <= 40).ToList();
        if (maidens.Count == 0 || grooms.Count == 0) return false;

        // 지참 예물: 딸만 보내서는 사돈이 되지 못한다. 곳간이 클수록, 미움이 깊을수록 더 부른다.
        double stores = CommonStore + Living.Sum(p => p.Food);
        double dowry = (8 + 0.1 * stores) * (1 + 0.5 * n.Hostility);
        double reachable = CommonStore + 0.6 * Living.Where(h => h.Master == null && h.Food > 1).Sum(h => h.Food - 1);
        if (reachable < dowry)
        {
            n.NextAllianceTick = Tick + 12;
            Say($"{Iga(c)} {n.Name}에 혼인을 청하려 했으나 예물(식량 {dowry:F0})을 마련할 길이 없었다.");
            return false; // 혼담은 막혔어도 화친은 청해 볼 수 있다
        }
        n.NextTalkTick = Tick + 12;

        if (Rnd() >= Math.Clamp(0.55 - 0.25 * n.Hostility + (Tick < n.PeaceUntil ? 0.2 : 0), 0.05, 0.85))
        {
            Say($"{Iga(c)} {n.Name}에 혼인을 청했으나 거절당했다.");
            return true;
        }

        double paid = TakeFood(dowry, out bool sparedKin);
        if (paid < dowry * 0.8)
        {
            CommonStore += paid;
            n.NextAllianceTick = Tick + 12;
            Say($"{Josa.Eun(n.Name)} 혼인을 받아들였으나 {P(c)}의 무리는 약속한 예물(식량 {dowry:F0})을 채우지 못해 혼담이 깨졌다.");
            return true;
        }
        n.Food += paid;

        // 핏줄을 따지는 우두머리는 제 딸을 아끼고 남의 딸을 보낸다. 며느리는 제 집안이 받는다.
        bool hasOwn = maidens.Any(w => Related(c, w) >= 0.25);
        var maiden = c.T(Trait.KinBias) > 0.5
            ? maidens.OrderBy(w => Related(c, w)).ThenBy(w => w.Father?.Status ?? 0).ThenBy(w => w.Id).First()
            : maidens.OrderByDescending(w => Related(c, w)).ThenBy(w => w.Id).First();
        var groom = grooms.OrderByDescending(m => Related(c, m)).ThenByDescending(m => m.Status).ThenBy(m => m.Id).First();
        bool ownKin = Related(c, maiden) >= 0.25;

        if (!ownKin)
            foreach (var parent in new[] { maiden.Father, maiden.Mother })
                if (parent is { Alive: true } && parent != c)
                    AddGrudge(parent, c, 0.25 + 0.4 * parent.T(Trait.Empathy), $"딸 {Josa.Eul(maiden.Name)} 남의 무리에 보낸 일로");
        var jilted = grooms.Where(m => m != groom && m != c && Related(m, maiden) < 0.2)
            .OrderByDescending(m => m.T(Trait.Libido)).ThenBy(m => m.Id).FirstOrDefault();
        if (jilted != null) AddGrudge(jilted, c, 0.25, "짝이 될 여자를 내준 일로");

        string sent = P(maiden);
        Die(maiden, "시집감");

        var bride = NewPerson(Sex.Female, 15 + _rng.Next(8), n.ClanId);
        bride.Outsider = true;
        Wed(groom, bride);
        foreach (var k in Living.Where(k => k != groom && k.Age(Tick) >= 14 && k.T(Trait.KinBias) > 0.65).ToList())
            AddGrudge(k, bride, 0.2, "이방인이 못마땅해");

        n.Allied = true;
        n.Bride = bride;
        n.PeaceUntil = 0;
        n.Hostility *= 0.2;
        if (ownKin) c.Status += 0.5;
        int angry = ResentPeace(n, c, "원수와 혼인을 맺은 일로");

        Say($"{Iga(c)} {Josa.Wa(n.Name)} 혼인으로 동맹을 맺었다. {Josa.Iga(sent)} 그쪽으로 시집가고, {Josa.Iga(bride.Name)} {P(groom)}의 아내로 왔다. 예물로 식량 {paid:F0}을 실어 보냈다."
            + (sparedKin ? " 우두머리의 집안 곳간은 건드리지 않았다." : "")
            + (!ownKin && hasOwn ? " 우두머리는 제 집안 딸 대신 남의 딸을 보냈다." : "")
            + (angry > 0 ? $" 그들에게 피붙이를 잃은 {angry}명은 이를 갈았다." : ""));
        return true;
    }

    /// <summary>
    /// 사돈이 제 싸움에 원군을 청한다. 동맹은 공짜가 아니다 — 우리 사내가 남의 싸움에서 죽는다.
    /// 보낼 사내가 없으면 약속을 저버린 것이 되어 동맹이 깨진다.
    /// </summary>
    internal void AidCall(Neighbor n)
    {
        if (!n.Allied) return;
        var men = Warriors();
        if (men.Count < 4)
        {
            n.Allied = false;
            n.Bride = null;
            n.Hostility += 0.8;
            Say($"사돈 {Josa.Iga(n.Name)} 원군을 청했으나 보낼 사내가 없었다. {Josa.Eun(n.Name)} 약속을 저버렸다며 동맹을 끊었다.");
            return;
        }

        var c = Chief is { Alive: true } chief ? chief : null;
        int need = Math.Max(2, men.Count / 3);
        // 핏줄을 따지는 우두머리는 제 집안 사내를 아끼고 남의 아들부터 내보낸다.
        bool spare = c != null && c.T(Trait.KinBias) > 0.6;
        var sent = men
            .OrderBy(m => spare && Related(c!, m) >= 0.25 ? 1 : 0)
            .ThenByDescending(EffAggression).ThenBy(m => m.Id)
            .Take(need).ToList();
        bool sparedKin = spare && men.Any(m => Related(c!, m) >= 0.25) && sent.All(m => Related(c!, m) < 0.25);

        bool won = Rnd() < 0.5;
        var fallen = sent.Where(_ => Rnd() < (won ? 0.08 : 0.2)).ToList();
        var fallenNames = fallen.Select(P).ToList();
        foreach (var d in fallen)
        {
            if (c != null && c != d)
                foreach (var k in Living.Where(k => k != d && k != c && k.Age(Tick) >= 12 && Related(k, d) >= 0.25).ToList())
                    AddGrudge(k, c, 0.3 + 0.4 * k.T(Trait.KinBias), $"{Josa.Eul(d.Name)} 사돈의 싸움에 내보내 죽게 한 일로");
            Die(d, "전쟁");
        }
        var back = sent.Where(m => m.Alive).ToList();

        string tail;
        if (won)
        {
            double loot = 1.5 * back.Count;
            if (Inst.PrivateProperty) foreach (var m in back) Head(m).Food += 1.5;
            else CommonStore += loot;
            foreach (var m in back) m.Status += 0.3;
            n.Hostility *= 0.7;
            tail = $"싸움은 이겼고 살아 돌아온 이들이 전리품(식량 {loot:F0})을 나눠 받았다.";
        }
        else
        {
            n.Fighters = Math.Max(2, n.Fighters * 0.85);
            tail = "싸움은 졌고 빈손으로 돌아왔다.";
        }
        Say($"사돈 {Josa.Iga(n.Name)} 제 싸움에 원군을 청해 사내 {sent.Count}명이 떠났다. {tail} 돌아오지 못한 이: {NamesOr(fallenNames, "없음")}."
            + (sparedKin ? " 우두머리의 집안 사내는 한 명도 가지 않았다." : ""));
    }
}
