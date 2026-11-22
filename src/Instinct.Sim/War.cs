namespace Instinct.Sim;

/// <summary>이웃 무리. 개인까지 그리지 않고 머릿수와 감정만 가진다.</summary>
public sealed class Neighbor
{
    public string Name = "";
    public int ClanId;
    public double Fighters;
    public double MaxFighters;
    public double Food;
    /// <summary>그들이 우리에게 품은 적의.</summary>
    public double Hostility;
    /// <summary>우리가 그들에게 품은 복수심. 죽거나 끌려간 이의 혈육이 키운다.</summary>
    public double Vengeance;

    /// <summary>화친이 유지되는 마지막 틱. 0이면 화친 없음.</summary>
    public int PeaceUntil;
    /// <summary>혼인 동맹. 시집온 여자가 살아 있는 동안 이어진다.</summary>
    public bool Allied;
    public Person? Bride;
    public int NextTalkTick;
    /// <summary>예물을 못 마련해 혼담이 막힌 뒤 다시 청할 수 있는 틱.</summary>
    public int NextAllianceTick;
    public int PaidTick = -100;
    public int TributeYears;
    /// <summary>이 무리에게 피붙이를 잃은 사람들(Id → 상실의 크기). 화친을 가장 미워한다.</summary>
    public readonly Dictionary<int, double> Mourners = new();
}

// 이웃 무리와의 약탈·전쟁, 그리고 종살이.
public sealed partial class Simulation
{
    static readonly string[] NeighborNames = { "강건너", "검은숲", "붉은언덕", "갈대늪", "바람고개", "소금벌" };

    public readonly List<Neighbor> Neighbors = new();
    int _neighborCount;
    internal bool _raidedThisYear;

    Neighbor AddNeighbor()
    {
        int i = _neighborCount++;
        string name = NeighborNames[i % NeighborNames.Length] + (i >= NeighborNames.Length ? $"{i / NeighborNames.Length + 1}" : "");
        int clan = _nextClan++;
        _clanNames[clan] = name;
        var n = new Neighbor { Name = name + "족", ClanId = clan, MaxFighters = 10 + _rng.Next(12) };
        n.Fighters = n.MaxFighters * (0.5 + 0.3 * Rnd());
        n.Food = n.Fighters * 3;
        Neighbors.Add(n);
        return n;
    }

    List<Person> Warriors() =>
        Living.Where(p => p.IsMale && p.Master == null && p.Age(Tick) is >= 15 and <= 50).ToList();

    void NeighborYear()
    {
        _raidedThisYear = false;
        if (Tick == 0)
        {
            var a = AddNeighbor();
            var b = AddNeighbor();
            Say($"가까이에 다른 무리가 산다: {a.Name}, {b.Name}.");
        }
        foreach (var n in Neighbors.ToList())
        {
            if (n.Fighters < 2)
            {
                Neighbors.Remove(n);
                Say($"{Josa.Eun(n.Name)} 싸울 사내를 잃고 흩어져 사라졌다.");
                continue;
            }
            n.Fighters += (n.MaxFighters - n.Fighters) * 0.15 * _yearFactor;
            n.Food = n.Fighters * 3 * _yearFactor;
            DiplomacyYear(n);
        }
        if (Tick > 0 && Neighbors.Count < 2 && Rnd() < 0.08)
        {
            var n = AddNeighbor();
            Say($"낯선 무리 {Josa.Iga(n.Name)} 가까운 땅에 자리를 잡았다.");
        }
    }

    void NeighborDecay()
    {
        foreach (var n in Neighbors)
        {
            n.Hostility *= 0.97;
            n.Vengeance *= 0.96;
        }
    }

    string WarSummary()
    {
        int thralls = Living.Count(p => p.Master != null);
        var parts = new List<string>();
        if (thralls > 0) parts.Add($"종 {thralls}");
        if (Neighbors.Count > 0)
            parts.Add("이웃 " + string.Join(", ", Neighbors.Select(n => $"{n.Name}({(n.Allied ? "동맹, " : Tick < n.PeaceUntil ? "화친, " : Tick - n.PaidTick < 4 ? "공물, " : "")}전사 {n.Fighters:F0}, 적의 {n.Hostility:F1}, 복수심 {n.Vengeance:F1})")));
        return parts.Count == 0 ? "" : " | " + string.Join(" | ", parts);
    }

    /// <summary>싸움에서 죽거나 끌려간 이의 혈육이 무리 전체의 복수심을 키운다.</summary>
    void Bereave(Person lost, Neighbor n, double scale)
    {
        double kin = 0;
        foreach (var k in Living.Where(k => k != lost && k.Age(Tick) >= 12))
        {
            double v = Related(k, lost) * k.T(Trait.KinBias);
            kin += v;
            if (v >= 0.1) n.Mourners[k.Id] = Math.Min(2, n.Mourners.GetValueOrDefault(k.Id) + v * scale);
        }
        n.Vengeance = Math.Min(5, n.Vengeance + scale * (0.3 + 0.25 * kin));
    }

    static string NamesOr(IEnumerable<string> names, string none)
    {
        var list = names.ToList();
        return list.Count == 0 ? none : string.Join(", ", list);
    }

    // ───────────────────────── 그들이 우리를 친다 ─────────────────────────

    internal void EnemyRaids()
    {
        bool lean = _yearFactor < 0.85;
        foreach (var n in Neighbors.ToList())
        {
            if (n.Allied) continue;
            double pr = (0.02 + 0.045 * n.Hostility + (lean ? 0.06 : 0)) * ThreatMul * DreadFactor;
            if (Tick - n.PaidTick < 4) pr *= 0.15; // 공물을 받은 해에는 굳이 오지 않는다
            pr *= SmallBandMercy(Living.Count()); // 털 것도 없는 작은 무리는 굳이 치지 않는다
            if (Tick < n.PeaceUntil)
            {
                // 화친은 배고픔 앞에서 깨지기도 한다.
                if (!lean || Rnd() >= 0.04 * n.Hostility) continue;
                n.PeaceUntil = 0;
                Say($"{Josa.Eun(n.Name)} 흉년을 못 이겨 화친을 깼다.");
            }
            else if (Rnd() >= pr) continue;
            EnemyRaid(n);
            return;
        }
    }

    internal void EnemyRaid(Neighbor n)
    {
        int raiders = Math.Max(3, (int)(n.Fighters * FoeMul * (0.4 + 0.4 * Rnd())));
        var defenders = Warriors();
        defenders.AddRange(Guards().Where(g => !defenders.Contains(g)));
        double dp = defenders.Sum(Power) * 1.2 * DefenseMul(); // 제 집을 지키는 쪽이 유리하다(목책·망루면 더)
        var allies = Neighbors.Where(a => a != n && a.Allied).ToList();
        dp += allies.Sum(a => a.Fighters * 0.25);
        if (allies.Count > 0) Say($"{n.Name}의 습격 소식에 사돈 {string.Join(", ", allies.Select(a => a.Name))}의 사내들이 도우러 달려왔다.");
        double ap = raiders * (0.9 + 0.4 * Rnd());
        double r = dp / (dp + ap);
        bool repelled = Rnd() < r;

        var fallen = new List<Person>();
        foreach (var d in defenders)
        {
            double x = Rnd(), die = 0.02 + 0.14 * (1 - r);
            if (x < die) fallen.Add(d);
            else if (x < die + 0.25 * (1 - r)) d.Health -= 0.3;
        }
        var fallenNames = fallen.Select(P).ToList();
        foreach (var d in fallen)
        {
            Bereave(d, n, 1);
            Die(d, "전쟁");
        }
        int theirLoss = Math.Min(raiders, (int)Math.Round(raiders * (0.1 + 0.5 * r) * (0.5 + Rnd())));
        n.Fighters -= theirLoss;
        var survivors = defenders.Where(d => d.Alive).ToList();

        if (repelled)
        {
            Say($"{n.Name} {raiders}명이 마을을 덮쳤으나 사내들이 물리쳤다. 적 {theirLoss}명을 쓰러뜨렸다. 죽은 이: {NamesOr(fallenNames, "없음")}.");
            foreach (var d in survivors) d.Status += 0.3;
            n.Hostility += 0.1 * theirLoss;
            if (Inst.Slavery && theirLoss > 0 && survivors.Count > 0 && Rnd() < 0.5)
            {
                var master = survivors.OrderByDescending(d => d.Status).ThenBy(d => d.Id).First();
                var captive = MakeCaptive(n, Sex.Male, master);
                Say($"쓰러진 적 가운데 숨이 붙은 {Josa.Eun(captive.Name)} {master.Name}의 종이 되었다.");
            }
            return;
        }

        // 졌다: 곳간이 털리고 사람이 끌려간다.
        double loot = CommonStore * 0.5;
        CommonStore -= loot;
        foreach (var h in Living.Where(h => h.Food > 1).ToList())
        {
            if (Rnd() >= 0.5) continue;
            loot += h.Food * 0.5;
            h.Food *= 0.5;
        }
        var taken = Shuffle(Living.Where(p => (!p.IsMale && p.Age(Tick) is >= 14 and <= 35) || p.Age(Tick) is >= 4 and < 14))
            .Take(Math.Max(1, raiders / 3)).ToList();
        if (Rnd() < 0.3) taken.Clear();
        // 무리가 작으면 끌고 갈 사람을 줄인다: 열 명 아래면 아무도, 그 위로는 넷 중 하나까지.
        int pop = Living.Count();
        if (taken.Count > MaxAbducted(pop)) taken = taken.Take(MaxAbducted(pop)).ToList();
        var takenNames = taken.Select(P).ToList();
        foreach (var p in taken)
        {
            Bereave(p, n, 0.8);
            Die(p, "납치");
        }
        n.Food += loot;
        n.Hostility *= 0.8;
        Say($"{n.Name} {raiders}명이 마을을 덮쳐 곳간을 털어 갔다 (식량 {loot:F0}). 죽은 이: {NamesOr(fallenNames, "없음")}. 끌려간 이: {NamesOr(takenNames, "없음")}.");
    }

    /// <summary>작은 무리를 노리는 습격 확률의 배수: 열다섯 명이면 온전히, 그 아래로는 줄어든다(최소 0.2).</summary>
    internal static double SmallBandMercy(int pop) => Math.Clamp(pop / 15.0, 0.2, 1);

    /// <summary>습격에 진 뒤 끌려갈 수 있는 사람의 수: 열 명 아래면 0, 그 위로는 무리의 4분의 1까지.</summary>
    internal static int MaxAbducted(int pop) => pop < 10 ? 0 : pop / 4;

    // ───────────────────────── 우리가 그들을 친다 ─────────────────────────

    internal void MaybeRaid()
    {
        if (Neighbors.Count == 0 || _raidedThisYear) return;
        var men = Warriors();
        if (men.Count < 3) return;
        var target = Neighbors.OrderByDescending(n => n.Vengeance).First();

        if (Inst.Raiding && Neighbors.Any(n => !AtPeace(n)))
        {
            target = Neighbors.Where(n => !AtPeace(n)).OrderByDescending(n => n.Vengeance).First();
            bool hungry = _famineLogged;
            int wifeless = men.Count(m => m.Wives.Count == 0 && m.Age(Tick) >= 20);
            double pr = 0.01 + 0.07 * target.Vengeance + (hungry ? 0.2 : 0) + 0.01 * wifeless
                        + 0.03 * (Chief?.T(Trait.Ambition) ?? 0);
            if (Rnd() >= pr) return;
            // 원한이 없으면 가장 배부른 이웃을 고른다.
            if (target.Vengeance < 0.5) target = Neighbors.Where(n => !AtPeace(n)).OrderByDescending(n => n.Food).First();
            OurRaid(target, sanctioned: true);
        }
        else if (target.Vengeance > 1.5 && Rnd() < (AtPeace(target) ? 0.06 : 0.12))
        {
            // 제도가 막아도 피붙이를 잃은 자들은 간다.
            OurRaid(target, sanctioned: false);
        }
    }

    internal void OurRaid(Neighbor n, bool sanctioned)
    {
        var men = Warriors();
        var party = sanctioned
            ? men.Where(m => Rnd() < 0.25 + 0.5 * EffAggression(m)).ToList()
            : men.OrderByDescending(m => n.Mourners.GetValueOrDefault(m.Id) + 0.5 * EffAggression(m)).ThenBy(m => m.Id).Take(Math.Min(4, men.Count)).ToList();
        if (party.Count < 3) return;
        _raidedThisYear = true;
        bool betrayal = AtPeace(n);

        var leader = Chief != null && party.Contains(Chief)
            ? Chief
            : party.OrderByDescending(m => m.Status).ThenBy(m => m.Id).First();
        Say(sanctioned
            ? $"{Iga(leader)} 사내 {party.Count}명을 이끌고 {Josa.Eul(n.Name)} 치러 떠났다."
            : $"말리는 소리를 뿌리치고 {P(leader)} 등 {party.Count}명이 {n.Name}에게 피를 갚으러 떠났다.");

        if (betrayal) BreakPeace(n, leader);

        double ap = party.Sum(Power);
        double dp = n.Fighters * (0.5 + 0.3 * Rnd()) + 1;
        double r = ap / (ap + dp);

        var fallen = party.Where(_ => Rnd() < 0.03 + 0.15 * (1 - r)).ToList();
        var fallenNames = fallen.Select(P).ToList();
        foreach (var d in fallen)
        {
            Bereave(d, n, 1);
            Die(d, "전쟁");
        }
        int theirLoss = (int)Math.Min(n.Fighters, Math.Round(n.Fighters * 0.3 * r * (0.5 + Rnd())));
        n.Fighters -= theirLoss;
        var back = party.Where(m => m.Alive).ToList();

        if (back.Count == 0 || Rnd() >= r)
        {
            Say($"{n.Name}에게 쫓겨 돌아왔다. 돌아오지 못한 이: {NamesOr(fallenNames, "없음")}.");
            if (leader.Alive) leader.Status -= 0.5;
            n.Hostility += 0.4;
            return;
        }

        // 이겼다: 식량, 여자, 종.
        double loot = Math.Min(n.Food, 2.0 * back.Count * (0.5 + Rnd()));
        // 식인 풍습: 쓰러진 적의 몸도 가져온다.
        double flesh = Inst.Cannibalism ? 1.5 * theirLoss : 0;
        n.Food -= loot;
        if (Inst.PrivateProperty)
        {
            // 우두머리가 두 몫을 갖는다.
            double share = loot / (back.Count + (leader.Alive ? 1 : 0));
            foreach (var m in back) Head(m).Food += share * (m == leader ? 2 : 1);
            CommonStore += flesh;
        }
        else
        {
            CommonStore += loot + flesh;
        }

        var brides = new List<string>();
        foreach (var m in back.Where(m => m.Wives.Count == 0).OrderByDescending(m => m.Status).Take(Math.Max(0, back.Count / 3)))
        {
            var w = NewPerson(Sex.Female, 15 + _rng.Next(10), n.ClanId);
            w.Outsider = true;
            Wed(m, w);
            AddGrudge(w, m, 0.8, "끌려와 아내가 된 일로");
            brides.Add($"{w.Name}(→{m.Name})");
        }

        var thralls = new List<string>();
        if (Inst.Slavery)
        {
            int count = 1 + _rng.Next(1 + back.Count / 3);
            var masters = back.OrderByDescending(m => m == leader ? double.MaxValue : m.Status).ThenBy(m => m.Id).ToList();
            for (int i = 0; i < count; i++)
            {
                var master = masters[i % masters.Count];
                var t = MakeCaptive(n, _rng.Next(3) == 0 ? Sex.Female : Sex.Male, master);
                thralls.Add($"{t.Name}(→{master.Name})");
            }
        }

        foreach (var m in back) m.Status += m == leader ? 0.8 : 0.4;
        n.Hostility += Inst.Cannibalism ? 1.5 : 1.0;
        Feared(2);
        n.Vengeance *= 0.3;
        Say($"{Josa.Eul(n.Name)} 쳐서 이겼다. 적 {theirLoss}명을 죽이고 식량 {loot:F0}을 빼앗았다. 돌아오지 못한 이: {NamesOr(fallenNames, "없음")}."
            + (brides.Count > 0 ? $" 끌고 와 아내로 삼은 여자: {string.Join(", ", brides)}." : "")
            + (thralls.Count > 0 ? $" 종으로 끌고 온 이: {string.Join(", ", thralls)}." : "")
            + (flesh > 0 ? $" 쓰러진 적의 몸은 잔치 솥으로 갔다 (식량 {flesh:F0})." : ""));
    }

    // ───────────────────────── 종살이 ─────────────────────────

    Person MakeCaptive(Neighbor n, Sex sex, Person master)
    {
        var t = NewPerson(sex, 14 + _rng.Next(16), n.ClanId);
        t.Outsider = true;
        t.Master = master;
        AddGrudge(t, master, 0.5, "종으로 끌려온 일로");
        return t;
    }

    /// <summary>피값을 못 치른 살인자가 죽은 자의 집안에 종으로 넘어간다.</summary>
    void Enslave(Person p, Person master)
    {
        foreach (var t in Living.Where(t => t.Master == p).ToList()) t.Master = master;
        p.Master = master;
        p.Status = 0;
        if (Chief == p) Chief = null;
        AddGrudge(p, master, 0.3, "종으로 떨어진 일로");
    }

    void InheritThralls(Person dead, Person? heir)
    {
        if (heir is { Master: not null }) heir = null;
        foreach (var t in People.Where(t => t.Alive && t.Master == dead).ToList())
        {
            t.Master = heir;
            if (heir == null) Say($"주인 {Josa.Iga(dead.Name)} 죽자 종 {Josa.Eun(t.Name)} 풀려났다.");
        }
    }

    void ThrallLife()
    {
        foreach (var t in Living.Where(t => t.Master != null).ToList())
        {
            var m = t.Master!;
            if (!m.Alive) { t.Master = null; continue; }
            bool hungry = t.HungrySeasons > 0;
            // 평소에는 참는다. 굶기면 원한이 끓는다.
            AddGrudge(t, m, 0.03 + (hungry ? 0.3 : 0), hungry ? "굶기며 부린 일로" : "종살이의 원한으로");
            m.Status += 0.02;

            if (Rnd() < 0.015 + (hungry ? 0.12 : 0))
            {
                Die(t, "도망");
                Say($"{P(m)}의 종 {Josa.Iga(t.Name)} 달아났다.");
            }
            else if (Rnd() < 0.012 * m.T(Trait.Empathy))
            {
                t.Master = null;
                t.Grudge.Remove(m.Id);
                t.GrudgeWhy.Remove(m.Id);
                Say($"{Iga(m)} 종 {Josa.Eul(t.Name)} 풀어 주었다.");
            }
        }
    }
}
