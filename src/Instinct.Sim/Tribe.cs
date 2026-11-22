namespace Instinct.Sim;

/// <summary>무리의 일 하나: 우두머리가 사람들을 움직여 벌이는 일. 얻는 것과 치르는 값이 함께 있다.</summary>
public sealed record TribeActionDef(string Key, string Name, bool NeedsNeighbor, string Gain, string Cost);

public static class TribeActions
{
    public static readonly IReadOnlyList<TribeActionDef> All = new[]
    {
        new TribeActionDef("hunt", "큰 사냥", false, "사내들이 큰 짐승을 몰아 식량을 한꺼번에 얻는다", "다치는 이가 나오고, 드물게 돌아오지 못한다 · 한 해에 한 번"),
        new TribeActionDef("feast", "잔치", false, "묵은 원한이 풀리고 우두머리의 위신이 선다", "창고의 식량이 한꺼번에 나간다 · 욕심 많은 이들은 못마땅해한다 · 한 해에 한 번"),
        new TribeActionDef("gift", "선물 보내기", true, "그 무리의 적의가 가라앉는다", "식량 4 · 그들에게 피붙이를 잃은 이들이 우두머리를 원망한다 · 한 해에 한 번"),
        new TribeActionDef("raid", "습격 명령", true, "이기면 식량과 사람을 빼앗아 온다", "사내들이 죽고, 그 무리의 적의가 크게 오른다 · 약탈이 관습이 아니면 위신이 깎인다"),
    };

    public static TribeActionDef? Find(string key) => All.FirstOrDefault(a => a.Key == key);
}

// 무리의 일: 나라가 서기 전(부족·군장사회), 플레이어가 우두머리의 손으로 직접 벌이는 일.
public sealed partial class Simulation
{
    int _tribeActTick = -1;
    int _huntYear, _feastYear, _giftYear;

    double FeastCost() => Math.Max(4, 0.8 * Living.Count());

    /// <summary>그 일을 벌일 수 없는 까닭. 벌일 수 있으면 빈 문자열.</summary>
    public string WhyCantTribe(string key, int neighbor = -1)
    {
        if (TribeActions.Find(key) is not { } def) return "없는 행동";
        if (!Tribal) return "나라가 세워진 뒤에는 군대와 국고로 대신함";
        if (Chief is not { Alive: true } c) return "우두머리 없음";
        if (_tribeActTick == Tick) return "이번 계절에는 이미 행동함";
        if (def.NeedsNeighbor && (neighbor < 0 || neighbor >= Neighbors.Count)) return "대상 무리를 골라야 함";
        switch (key)
        {
            case "hunt":
                if (_huntYear == Year) return "올해는 이미 큰 사냥을 다녀옴";
                if (Warriors().Count(m => m.Health > 0.3) < 3) return "나설 전사가 3명 이상 필요";
                break;
            case "feast":
                if (_feastYear == Year) return "올해는 이미 잔치를 벌임";
                double have = Inst.PrivateProperty ? Head(c).Food : CommonStore;
                if (have < FeastCost()) return $"식량 부족 ({FeastCost():F0} 필요)";
                break;
            case "gift":
                if (_giftYear == Year) return "올해는 이미 선물을 보냄";
                if (Neighbors[neighbor].Allied) return "이미 혼인 동맹 관계";
                break;
            case "raid":
                if (Season == 3) return "겨울에는 출정 불가";
                if (_raidedThisYear) return "올해는 이미 습격함";
                if (Warriors().Count < 3) return "나설 전사가 3명 이상 필요";
                break;
        }
        return "";
    }

    /// <summary>무리의 일을 벌인다. 한 철에 하나만. 벌이지 못했으면 false.</summary>
    public bool TribeAct(string key, int neighbor = -1)
    {
        if (WhyCantTribe(key, neighbor).Length > 0) return false;
        var c = Chief!;
        _tribeActTick = Tick;
        _mute++;
        try
        {
            switch (key)
            {
                case "hunt": GreatHunt(c); break;
                case "feast": Feast(c); break;
                case "gift": SendGift(Neighbors[neighbor], c); break;
                case "raid": OrderRaid(Neighbors[neighbor], c); break;
            }
        }
        finally { _mute--; }
        Wrote("tribe", key, neighbor.ToString());
        return true;
    }

    void GreatHunt(Person c)
    {
        _huntYear = Year;
        var party = Warriors().Where(m => m.Health > 0.3).OrderBy(m => m.Id).ToList();
        double food = 0;
        var hurt = new List<Person>();
        var dead = new List<Person>();
        Person? best = null;
        double bestGot = 0;
        foreach (var m in party)
        {
            double got = (1.2 + 1.8 * Rnd()) * m.Skill * _yearFactor * (Season == 3 ? 0.5 : 1);
            food += got;
            if (got > bestGot) { bestGot = got; best = m; }
            double x = Rnd();
            if (x < 0.012) dead.Add(m);
            else if (x < 0.08) hurt.Add(m);
        }
        foreach (var m in hurt) m.Health -= 0.35;
        var deadNames = dead.Select(P).ToList();
        foreach (var m in dead) Die(m, "사냥");
        if (Inst.PrivateProperty)
        {
            // 제 몫은 제 곳간으로. 이끈 이가 두 몫을 갖는다.
            var back = party.Where(m => m.Alive).ToList();
            double share = food / (back.Count + 1);
            foreach (var m in back) Head(m).Food += share * (m == c ? 2 : 1);
            if (!back.Contains(c)) CommonStore += share;
        }
        else CommonStore += food;
        if (best is { Alive: true }) best.Status += 0.3;
        Say($"{Iga(c)} 사내 {party.Count}명을 이끌고 큰 사냥을 나갔다. 식량 {food:F0}을 메고 돌아왔다."
            + (best is { Alive: true } ? $" 가장 큰 짐승은 {best.Name}의 창에 쓰러졌다." : "")
            + (hurt.Count > 0 ? $" 다친 이: {NamesOr(hurt.Select(P), "")}." : "")
            + (dead.Count > 0 ? $" 돌아오지 못한 이: {NamesOr(deadNames, "")}." : ""));
    }

    void Feast(Person c)
    {
        _feastYear = Year;
        double cost = FeastCost();
        var adults = Living.Where(p => p.Age(Tick) >= 14).OrderBy(p => p.Id).ToList();
        if (Inst.PrivateProperty) Head(c).Food -= cost;
        else
        {
            CommonStore -= cost;
            // 함께 모은 것을 하룻밤에 태웠다: 욕심 많은 이들은 그것을 센다.
            foreach (var p in adults.Where(p => p != c && p.T(Trait.Greed) > 0.7))
                AddGrudge(p, c, 0.15, "창고를 헐어 잔치를 벌인 일로");
        }
        int eased = 0;
        foreach (var p in adults)
            foreach (var id in p.Grudge.Keys.OrderBy(id => id).ToList())
            {
                if (p.GrudgeWhy.GetValueOrDefault(id) == "창고를 헐어 잔치를 벌인 일로") continue;
                p.Grudge[id] *= 0.7;
                eased++;
            }
        c.Status += 0.4;
        Say($"{Iga(c)} {(Inst.PrivateProperty ? "제 곳간을" : "창고를")} 헐어 잔치를 벌였다 (식량 {cost:F0}). 밤새 불가에서 고기를 나누었고, "
            + (eased > 0 ? "묵은 말들이 얼마쯤 풀렸다." : "풀 원한도 없는 조용한 밤이었다."));
    }

    void SendGift(Neighbor n, Person c)
    {
        _giftYear = Year;
        double gift = TakeFood(4, out bool sparedKin);
        if (gift < 2)
        {
            CommonStore += gift;
            c.Status = Math.Max(0, c.Status - 0.2);
            Say($"{Iga(c)} {n.Name}에 선물을 보내려 했으나 거둘 식량이 없었다.");
            return;
        }
        n.Food += gift;
        n.Hostility = Math.Max(0, n.Hostility * 0.6 - 0.2);
        int angry = ResentPeace(n, c, "원수에게 선물을 보낸 일로");
        Say($"{Iga(c)} {n.Name}에 식량 {gift:F0}을 선물로 보냈다. 그쪽의 눈빛이 누그러졌다."
            + (sparedKin ? " 제 핏줄의 곳간은 건드리지 않았다." : "")
            + (angry > 0 ? $" 그들에게 피붙이를 잃은 {angry}명은 그 짐을 지고 가는 뒷모습을 노려보았다." : ""));
    }

    void OrderRaid(Neighbor n, Person c)
    {
        if (!Inst.Raiding) c.Status = Math.Max(0, c.Status - 0.3); // 관습에 없는 싸움을 시킨 값
        OurRaid(n, sanctioned: true);
        if (_raidedThisYear) return;
        // 사내들은 우두머리의 말보다 제 피를 따른다.
        _raidedThisYear = true;
        c.Status = Math.Max(0, c.Status - 0.3);
        Say($"{Iga(c)} {Josa.Eul(n.Name)} 치자고 했으나 따라나서는 사내가 모자랐다. 우두머리의 말이 가벼워졌다.");
    }
}
