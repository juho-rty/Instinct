namespace Instinct.Sim;

/// <summary>지도 위의 군대. 우리 군단은 플레이어가 움직이고, 이웃 나라의 군대는 침공해 온다.</summary>
public sealed class Army
{
    public int Id;
    public string Name = "";
    /// <summary>0 우리, 그 밖은 이웃 나라의 Id.</summary>
    public int Owner;
    /// <summary>지금 있는 지방과 바로 앞서 있던 지방.</summary>
    public int At, From;
    /// <summary>가려는 지방(-1 = 머문다).</summary>
    public int Goal = -1;
    public double Men, FullMen;
    /// <summary>사기(0.3~1.2). 지면 꺾이고, 쉬면 돌아온다.</summary>
    public double Morale = 1;
    /// <summary>침공군이 빼앗은 땅의 수. 둘을 빼앗으면 돌아간다.</summary>
    public int Taken;
    /// <summary>침공군이 국경에서 모이는 남은 철. 0이 되어야 움직인다 — 그 사이가 막을 채비를 할 틈이다.</summary>
    public int Muster;
}

/// <summary>지도 위에서 벌어진 싸움의 기록(화면이 그 자리에 그린다).</summary>
public readonly record struct BattleMark(int Tick, int Province, bool Won, string Text);

// 군대: 일으키고, 길을 따라 움직이고, 맞붙는다.
public sealed partial class Simulation
{
    public readonly List<Army> Armies = new();
    public readonly List<BattleMark> Battles = new();
    int _nextArmy = 1;

    /// <summary>거느릴 수 있는 군단의 수: 하나에 병영마다 하나씩.</summary>
    public int ArmyCap => 1 + Math.Min(4, Built(Building.Barracks));

    /// <summary>군단 하나의 정원. 백성의 수에서 나온다.</summary>
    public double ArmySize() => Math.Max(40, Realm.Pop * 0.012);

    /// <summary>우리 군대 한 사람의 값어치(무기와 제도에서 나온다).</summary>
    double Quality() => ArmyStrength() / 0.6 * (0.6 + 0.5 * Realm.Cohesion);

    public string WhyCantRaise(int province)
    {
        if (province < 0 || province >= Map.Provinces.Count) return "없는 지역";
        var p = Map[province];
        if (Realm.Stage < Stage.CityState) return "도시국가 단계부터 가능";
        if (p.Owner != 0) return "우리 영토가 아님";
        if (p.Id != Map.Capital && !p.Has(Building.Barracks)) return "도성이나 병영이 있는 지역에서만 가능";
        if (Armies.Count(a => a.Owner == 0) >= ArmyCap) return $"군단은 최대 {ArmyCap}개 (병영을 지으면 늘어남)";
        if (Realm.Treasury < 0.5) return "국고 부족 (0.5 필요)";
        return "";
    }

    /// <summary>군단을 일으킨다. 백성에서 사내를 뽑고 곳간에서 값을 치른다.</summary>
    public Army? RaiseArmy(int province)
    {
        if (WhyCantRaise(province).Length > 0) return null;
        var p = Map[province];
        double men = ArmySize();
        Realm.Pop -= men;
        Realm.Treasury -= 0.5;
        int n = _nextArmy++;
        var a = new Army { Id = n, Name = $"{p.Name} 군단", Owner = 0, At = p.Id, From = p.Id, Men = men, FullMen = men };
        Armies.Add(a);
        Say($"【군대】 {p.Name}에서 {Num(men)}명을 뽑아 {Josa.Eul(a.Name)} 일으켰다. 밭에서는 그만큼의 손이 사라졌다.");
        Wrote("raise", province.ToString());
        return a;
    }

    /// <summary>군단을 흩는다. 살아남은 이들은 밭으로 돌아간다.</summary>
    public bool DisbandArmy(int id)
    {
        var a = Armies.FirstOrDefault(x => x.Id == id && x.Owner == 0);
        if (a == null) return false;
        Armies.Remove(a);
        Realm.Pop += a.Men;
        Minor($"{Josa.Iga(a.Name)} 흩어졌다. {Num(a.Men)}명이 집으로 돌아갔다.");
        Wrote("disband", id.ToString());
        return true;
    }

    /// <summary>from에서 to까지 맞닿은 지방을 따라가는 가장 짧은 길(from 빼고). 길이 없으면 빈 목록.</summary>
    public List<int> Route(int from, int to)
    {
        var path = new List<int>();
        if (from == to || from < 0 || to < 0) return path;
        var prev = new Dictionary<int, int> { [from] = -1 };
        var q = new Queue<int>();
        q.Enqueue(from);
        while (q.Count > 0 && !prev.ContainsKey(to))
        {
            int cur = q.Dequeue();
            foreach (int next in Map[cur].Adjacent)
                if (prev.TryAdd(next, cur)) q.Enqueue(next);
        }
        if (!prev.ContainsKey(to)) return path;
        for (int at = to; at != from; at = prev[at]) path.Add(at);
        path.Reverse();
        return path;
    }

    /// <summary>군단에 갈 곳을 준다. 남의 땅으로 보내면 그것이 곧 전쟁이다.</summary>
    public bool OrderArmy(int id, int goal)
    {
        var a = Armies.FirstOrDefault(x => x.Id == id && x.Owner == 0);
        if (a == null || goal < 0 || goal >= Map.Provinces.Count) return false;
        if (goal != a.At && Route(a.At, goal).Count == 0) return false;
        a.Goal = goal == a.At ? -1 : goal;
        Wrote("order", id.ToString(), goal.ToString());
        return true;
    }

    /// <summary>그 군단이 그 땅에 들어서면 이길 셈(0~1). 싸울 상대가 없으면 1.</summary>
    public double AttackOdds(int armyId, int province)
    {
        var a = Armies.FirstOrDefault(x => x.Id == armyId && x.Owner == 0);
        if (a == null || province < 0 || province >= Map.Provinces.Count) return 0;
        var to = Map[province];
        var foes = Armies.Where(x => x.Owner != 0 && x.At == to.Id).ToList();
        var lord = to.Owner > 0 ? Rivals.FirstOrDefault(v => v.Id == to.Owner) : null;
        if (lord == null && foes.Count == 0) return 1;
        double ours = a.Men * a.Morale * Quality() * 0.9;
        double garrison = lord == null ? 0 : RivalMen(lord) * (lord.Seat == to.Id ? 0.9 : 0.5) * (to.Has(Building.Castle) ? 1.5 : 1);
        double theirs = (garrison + foes.Sum(x => x.Men * x.Morale)) * Footing(to);
        return ours * ours / (ours * ours + theirs * theirs);
    }

    double RivalMen(Rival v) => v.Strength * Math.Max(150, Realm.Pop * 0.016);

    static double Footing(Province p) => p.Terrain switch { Terrain.Mountain => 1.35, Terrain.Hills => 1.2, Terrain.Forest => 1.1, _ => 1.0 };

    void Mark(Province where, bool won, string text) => Battles.Add(new BattleMark(Tick, where.Id, won, text));

    /// <summary>이웃 나라가 군대를 일으켜 우리 땅으로 온다.</summary>
    internal Army? Invade(Rival v)
    {
        if (Armies.Any(a => a.Owner == v.Id)) return null;
        var mine = Lands(0).ToList();
        var from = Lands(v.Id).OrderBy(p => mine.Min(m => Map.Distance(p, m))).ThenBy(p => p.Id).FirstOrDefault();
        if (from == null) return null;
        var target = mine.OrderBy(m => m.Id == Map.Capital && mine.Count > 1 ? 1 : 0).ThenBy(m => Map.Distance(m, from)).ThenBy(m => m.Id).First();
        double men = RivalMen(v) * FoeMul * (0.7 + 0.4 * Rnd());
        var a = new Army { Id = _nextArmy++, Name = $"{v.Name}의 군대", Owner = v.Id, At = from.Id, From = from.Id, Goal = target.Id, Men = men, FullMen = men, Muster = 2 };
        Armies.Add(a);
        v.Trade = false;
        v.Hostility = Clamp01(v.Hostility + 0.2);
        _crisisThisYear = true;
        Say($"【침공】 {Josa.Iga(v.Name)} {Num(men)}명의 군대를 {from.Name}에 모으고 있다. 두 철 뒤면 {Josa.Ro(target.Name)} 넘어온다.");
        return a;
    }

    /// <summary>플레이어가 다스릴 때의 전쟁: 이웃 나라가 쳐들어올지를 해마다 본다. 치는 전쟁은 플레이어의 몫이다.</summary>
    internal void InvasionYear()
    {
        var r = Realm;
        var i = Inst;
        if (r.Stage < Stage.CityState) return;
        EnsureRivals();
        var open = Rivals.Where(v => !v.Allied && Year >= v.TreatyUntil && Armies.All(a => a.Owner != v.Id)).OrderBy(v => v.Id).ToList();
        if (open.Count == 0 || Armies.Count(a => a.Owner != 0) >= 2) return;
        double p = 0.75 * (0.02 + 0.03 * RulerT(Trait.Aggression) + B(i.Raiding, 0.03) + B(i.Nationalism, 0.03) + B(i.Colonialism, 0.015) - B(i.Parley, 0.015))
                   * (0.4 + 1.2 * open.Max(v => v.Hostility));
        if (i.NuclearArms) p *= 0.35;
        p *= DreadFactor;
        p *= ThreatMul;
        if (Rnd() >= p) return;
        var foe = open.OrderByDescending(v => v.Hostility * v.Strength).ThenBy(v => v.Id).First();
        if (i.Tribute && r.Treasury >= 1)
            Ask("tribute", foe.Id.ToString(), $"{foe.Name} — 공물을 바칠까요?",
                $"{Josa.Iga(foe.Name)} 국경에 군대를 모으고 있습니다. 사신은 값을 치르면 물러가겠다고 합니다.", 2,
                new Choice("공물을 바친다", "국고 −1.0 · 위신이 꺾인다 · 세 해의 평화"),
                new Choice("맞서 싸운다", "그들의 군대가 국경을 넘는다"));
        else Invade(foe);
    }

    /// <summary>철마다: 군대가 한 걸음씩 움직이고, 닿은 곳에서 싸운다.</summary>
    internal void ArmiesMove()
    {
        if (Armies.Count == 0) return;
        foreach (var a in Armies.OrderBy(a => a.Owner == 0 ? 0 : 1).ThenBy(a => a.Id).ToList())
        {
            if (!Armies.Contains(a)) continue;
            if (a.Owner != 0 && Rivals.All(v => v.Id != a.Owner)) { Armies.Remove(a); continue; }
            a.Morale = Math.Min(1, a.Morale + 0.08);
            if (a.Muster > 0) { a.Muster--; continue; }
            if (a.Goal < 0 || a.Goal == a.At)
            {
                a.Goal = -1;
                if (a.Owner == 0) Rest(a);
                else Armies.Remove(a); // 돌아갈 곳에 닿은 침공군은 흩어진다
                continue;
            }
            // 길이 닿은 땅 사이에서는 한 철에 두 걸음을 간다.
            int steps = a.Owner == 0 && (Map[a.At].Road || a.At == Map.Capital) ? 2 : 1;
            for (int k = 0; k < steps && Armies.Contains(a) && a.Goal >= 0 && a.Goal != a.At; k++)
            {
                var route = Route(a.At, a.Goal);
                if (route.Count == 0) { a.Goal = -1; break; }
                var next = Map[route[0]];
                if (k == 1 && !(next.Owner == 0 && (next.Road || next.Id == Map.Capital))) break;
                if (!(a.Owner == 0 ? OurArrival(a, next) : EnemyArrival(a, next))) break;
            }
        }
        if (Battles.Count > 60) Battles.RemoveRange(0, Battles.Count - 60);
    }

    /// <summary>우리 땅에서 쉬는 군단은 빈자리를 채운다.</summary>
    void Rest(Army a)
    {
        if (Map[a.At].Owner != 0 || a.Men >= a.FullMen || Season != 0) return;
        double add = Math.Min(a.FullMen - a.Men, Realm.Pop * 0.004);
        a.Men += add;
        Realm.Pop -= add;
    }

    void Advance(Army a, Province to)
    {
        a.From = a.At;
        a.At = to.Id;
        if (a.Goal == to.Id) a.Goal = -1;
    }

    /// <summary>싸움 한 판. 이긴 쪽도 피를 흘린다. 우리가 이겼으면 true.</summary>
    bool Fight(double ours, double theirs, out double ourLoss, out double theirLoss)
    {
        // 수가 많은 쪽이 수의 비보다 더 자주 이긴다(제곱 셈): 군단을 모으고 성을 쌓은 보람이 있다.
        bool won = Rnd() < ours * ours / (ours * ours + theirs * theirs);
        double edge = Math.Clamp(ours / (ours + theirs), 0.15, 0.85);
        ourLoss = won ? 0.08 + 0.2 * (1 - edge) * Rnd() : 0.3 + 0.25 * Rnd();
        theirLoss = won ? 0.35 + 0.25 * Rnd() : 0.08 + 0.2 * edge * Rnd();
        return won;
    }

    /// <summary>우리 군단이 다음 땅에 들어선다. 계속 나아갈 수 있으면 true.</summary>
    bool OurArrival(Army a, Province to)
    {
        var r = Realm;
        var foes = Armies.Where(x => x.Owner != 0 && x.At == to.Id).OrderBy(x => x.Id).ToList();
        var lord = to.Owner > 0 ? Rivals.FirstOrDefault(v => v.Id == to.Owner) : null;

        if (lord == null && foes.Count == 0)
        {
            Advance(a, to);
            if (to.Owner == -1 && to.Adjacent.Any(x => Map[x].Owner == 0) && r.Treasury >= 0.3)
            {
                to.Owner = 0;
                r.Territory++;
                r.Treasury -= 0.3;
                Say($"【개척】 {Josa.Iga(a.Name)} {Josa.Eul(to.Name)} 차지하고 깃발을 꽂았다 (영토 {r.Territory}).");
                a.Goal = -1;
                return false;
            }
            return true;
        }

        // 남의 땅에 들어서는 것이 곧 전쟁이다: 화친도 장삿길도 사돈도 거기서 끝난다.
        if (lord != null)
        {
            if (Year < lord.TreatyUntil)
            {
                lord.TreatyUntil = 0;
                r.LegitShock += 0.05;
                foreach (var v in Rivals) v.Hostility = Clamp01(v.Hostility + 0.1);
                Say($"{Josa.Iga(RulerTitle)} {Josa.Wa(lord.Name)} 맺은 화친을 제 손으로 깼다. 다른 이웃들이 그것을 지켜보았다.");
            }
            if (lord.Allied)
            {
                lord.Allied = false;
                r.LegitShock += 0.08;
                Say($"{Josa.Iga(RulerTitle)} 사돈의 나라 {Josa.Eul(lord.Name)} 쳤다. 시집간 딸의 소식은 끊겼다.");
            }
            lord.Trade = false;
            lord.Hostility = Clamp01(lord.Hostility + 0.3);
        }

        var allies = Armies.Where(x => x.Owner == 0 && x.At == a.At && x != a && x.Goal == a.Goal).ToList();
        double ours = (a.Men * a.Morale + allies.Sum(x => x.Men * x.Morale) * 0.5) * Quality() * 0.9;
        double garrison = lord == null ? 0 : RivalMen(lord) * (lord.Seat == to.Id ? 0.9 : 0.5) * (to.Has(Building.Castle) ? 1.5 : 1);
        double theirs = (garrison + foes.Sum(x => x.Men * x.Morale)) * Footing(to);
        bool won = Fight(ours, theirs, out double ourLoss, out double theirLoss);
        double dead = a.Men * ourLoss;
        a.Men -= dead;
        foreach (var f in foes) f.Men *= 1 - theirLoss;
        _crisisThisYear = true;
        var fallen = CourtDeaths(Warriors().Where(m => m != Chief), won ? 0.03 : 0.08, "전쟁");
        r.Treasury -= 0.2;
        string who = lord?.Name ?? foes[0].Name;

        if (!won)
        {
            a.Morale = Math.Max(0.3, a.Morale - 0.3);
            a.Goal = -1;
            r.LegitShock += 0.08;
            if (lord != null) lord.Strength += 0.05;
            Mark(to, false, $"{to.Name} 싸움에서 졌다");
            Say($"【전투】 {Josa.Iga(a.Name)} {to.Name}에서 {Josa.Wa(who)} 맞붙어 졌다. {Num(dead)}명이 돌아오지 못했다. 쓰러진 이: {NamesOr(fallen.Select(P), "없음")}.");
            if (a.Men < 15) { Armies.Remove(a); Say($"{Josa.Eun(a.Name)} 그 들판에서 사라졌다."); }
            return false;
        }

        foreach (var f in foes) { f.Morale = 0.3; Retreat(f); }
        a.Morale = Math.Min(1.2, a.Morale + 0.1);
        Advance(a, to);
        a.Goal = -1;
        Mark(to, true, $"{to.Name} 싸움에서 이겼다");
        if (lord == null)
        {
            Feared(1);
            Say($"【전투】 {Josa.Iga(a.Name)} {to.Name}에서 {Josa.Eul(who)} 꺾었다. 우리 쪽 {Num(dead)}명이 죽었다.");
            return false;
        }

        if (lord.Seat == to.Id) lord.Seat = -1;
        to.Owner = 0;
        r.Territory++;
        r.Treasury += 0.5;
        r.Treasury += 0.3 * L(Course.Terror); // 공포의 길: 이긴 땅을 턴다
        Feared(2);
        r.LegitShock -= 0.08;
        r.Cohesion = Clamp01(r.Cohesion + 0.02);
        lord.Strength = Math.Max(0.1, lord.Strength - 0.18);
        Say($"【전쟁】 {Josa.Iga(a.Name)} {Josa.Eul(lord.Name)} 꺾고 {Josa.Eul(to.Name)} 차지했다 (영토 {r.Territory}). 우리 쪽 {Num(dead)}명이 죽었다. 쓰러진 이: {NamesOr(fallen.Select(P), "없음")}."
            + (to.Deposit != Resource.None && Revealed(to.Deposit) ? $" 그 땅에서는 {Josa.Iga(ResourceCatalog.Name(to.Deposit))} 난다." : ""));
        if (!Lands(lord.Id).Any() || lord.Strength < 0.25)
        {
            Rivals.Remove(lord);
            Armies.RemoveAll(x => x.Owner == lord.Id);
            foreach (var p in Lands(lord.Id).ToList()) p.Owner = -1;
            r.Treasury += 1;
            Say($"【정복】 {Josa.Iga(lord.Name)} 지도에서 사라졌다. 그 도성의 곳간은 비워졌고, 깃발은 내려졌다.");
        }
        else if (Armies.All(x => x.Owner != lord.Id) && Rnd() < 0.5) Invade(lord); // 되갚으러 온다
        AskConquest(to);
        return false;
    }

    void Retreat(Army a)
    {
        if (a.Men < 15) { Armies.Remove(a); return; }
        if (a.Owner != 0) { Armies.Remove(a); return; } // 진 침공군은 국경 너머로 흩어진다
        var back = Map[a.At].Adjacent.Select(x => Map[x]).Where(p => p.Owner == 0).OrderBy(p => p.Id == a.From ? 0 : 1).ThenBy(p => p.Id).FirstOrDefault();
        if (back == null) { Armies.Remove(a); Say($"{Josa.Eun(a.Name)} 물러날 곳이 없어 그 자리에서 무너졌다."); return; }
        a.From = a.At;
        a.At = back.Id;
        a.Goal = -1;
    }

    /// <summary>침공군이 다음 땅에 들어선다. 계속 나아갈 수 있으면 true.</summary>
    bool EnemyArrival(Army a, Province to)
    {
        var r = Realm;
        var lord = Rivals.First(v => v.Id == a.Owner);
        if (to.Owner != 0) { Advance(a, to); return true; }

        var defenders = Armies.Where(x => x.Owner == 0 && x.At == to.Id).OrderBy(x => x.Id).ToList();
        // 군단이 없어도 성벽과 그 땅의 사내들이 막아선다.
        double militia = Math.Max(20, LandPops()[to.Id] * 0.006) * (to.Has(Building.Castle) ? 3 : 1) * (to.Id == Map.Capital ? 2 : 1);
        double ours = (defenders.Sum(x => x.Men * x.Morale) * (to.Has(Building.Castle) ? 1.8 : 1.4) + militia) * Quality() * Footing(to);
        double theirs = a.Men * a.Morale;
        bool won = Fight(ours, theirs, out double ourLoss, out double theirLoss);
        double dead = 0;
        foreach (var d in defenders) { dead += d.Men * ourLoss; d.Men *= 1 - ourLoss; }
        double folk = militia * ourLoss + (won ? 0 : LandPops()[to.Id] * 0.03);
        r.Pop = Math.Max(50, r.Pop - folk);
        a.Men *= 1 - theirLoss;
        _crisisThisYear = true;
        var fallen = CourtDeaths(Warriors().Where(m => m != Chief), won ? 0.03 : 0.08, "전쟁");

        if (won)
        {
            Mark(to, true, $"{Josa.Eul(to.Name)} 지켜냈다");
            r.LegitShock -= 0.08;
            r.Cohesion = Clamp01(r.Cohesion + 0.03);
            lord.Strength = Math.Max(0.1, lord.Strength - 0.1);
            Say($"【전투】 {Josa.Iga(lord.Name)} {Josa.Eul(to.Name)} 쳤으나 막아냈다. {Num(dead + folk)}명이 죽었다. 쓰러진 이: {NamesOr(fallen.Select(P), "없음")}.");
            Armies.Remove(a);
            return false;
        }

        foreach (var d in defenders.ToList()) { d.Morale = Math.Max(0.3, d.Morale - 0.3); Retreat(d); }
        Mark(to, false, $"{Josa.Eul(to.Name)} 잃었다");
        r.LegitShock += 0.15;
        r.Unrest = Clamp01(r.Unrest + 0.08);
        lord.Strength += 0.08;
        Advance(a, to);
        if (to.Id == Map.Capital)
        {
            // 도성은 빼앗기지 않으나 털린다.
            double looted = Math.Max(0, Math.Min(2, r.Treasury));
            r.Treasury -= looted + 0.5;
            Say($"【도성 약탈】 {Josa.Iga(lord.Name)} 도성의 문을 부쉈다. 곳간이 털리고 {Num(dead + folk)}명이 죽었다. 쓰러진 이: {NamesOr(fallen.Select(P), "없음")}.");
            Armies.Remove(a);
            ReleaseSuppressed("도성이 불타는 틈에");
            if (r.Territory <= 3 && (r.Cohesion < 0.5 || lord.Strength > 1.0) && Rnd() < (r.Territory == 1 ? 0.5 : 0.25))
            {
                Armies.RemoveAll(x => x.Owner == 0);
                Collapse($"{lord.Name}에게 도성이 함락되었다", kind: "함락");
            }
            return false;
        }

        to.Owner = lord.Id;
        r.Territory = Math.Max(1, r.Territory - 1);
        Say($"【전쟁】 {Josa.Iga(lord.Name)} 쳐들어와 {Josa.Eul(to.Name)} 빼앗았다 (영토 {r.Territory}). {Num(dead + folk)}명이 죽었다. 쓰러진 이: {NamesOr(fallen.Select(P), "없음")}.");
        ReleaseSuppressed("패전의 소식에");
        // 하나를 빼앗고 기세가 남았으면 다음 땅으로, 아니면 돌아간다.
        var nextTarget = ++a.Taken < 2 && a.Men > a.FullMen * 0.5
            ? to.Adjacent.Select(x => Map[x]).Where(p => p.Owner == 0).OrderBy(p => p.Id == Map.Capital ? 1 : 0).ThenBy(p => p.Id).FirstOrDefault()
            : null;
        if (nextTarget == null) Armies.Remove(a);
        else a.Goal = nextTarget.Id;
        return false;
    }
}
