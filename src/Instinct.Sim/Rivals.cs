namespace Instinct.Sim;

/// <summary>이웃 나라. 도시국가부터 국경 너머에 실제로 있는 상대다: 싸우고, 장사하고, 믿음과 생각을 실어 보낸다.</summary>
public sealed class Rival
{
    public int Id;
    public string Name = "";
    /// <summary>군사력. 우리 군대와 맞서는 값(대략 0.2~2).</summary>
    public double Strength;
    /// <summary>우리를 향한 적의(0~1). 전쟁·교역·화친을 정한다.</summary>
    public double Hostility;
    /// <summary>믿음. 0이면 우리와 같고, 아니면 다른 믿음이다.</summary>
    public int Faith;
    /// <summary>그 나라의 지식. 우리보다 앞서면 교역로를 따라 문물이 들어온다.</summary>
    public double Advance;
    public bool Trade;
    public bool Allied;
    /// <summary>백성이 다스리는 이를 뽑는 나라. 그 소문이 교역로를 타고 온다.</summary>
    public bool Free;
    /// <summary>화친이 끝나는 해 (0 = 없음).</summary>
    public int TreatyUntil;
    /// <summary>도성이 있는 지방의 Id (-1 = 아직 지도에 자리가 없다).</summary>
    public int Seat = -1;
}

// 이웃 나라: 교역, 외교, 믿음과 문물의 전파, 전쟁.
public sealed partial class Simulation
{
    static readonly string[] RivalHeads = { "카르", "벨로", "오손", "타마", "이슈", "누메", "하란", "세다", "모르", "유한", "아크", "테살", "룬드", "자파", "미탄", "소그" };
    static readonly string[] RivalTails = { "단", "리아", "라", "누스", "르", "가르", "니아", "움" };
    static readonly string[][] RivalKinds =
    {
        new[] { "도시 동맹" }, new[] { "도시 동맹" },
        new[] { "도시 동맹", "부족 연맹", "항구 도시" },
        new[] { "왕국", "공국", "유목 연맹" },
        new[] { "제국", "왕국", "칸국" },
    };

    public readonly List<Rival> Rivals = new();
    int _nextRival = 1;
    bool _freeIdeasLogged;
    int _envoyTick = -1;

    /// <summary>플레이어가 이웃 나라에 사절(trade·truce·ally)을 보낼 수 없는 까닭. 보낼 수 있으면 빈 문자열.</summary>
    public string WhyCantEnvoy(int rivalId, string kind)
    {
        if (Realm.Stage < Stage.CityState) return "도시국가 단계부터 가능";
        if (Rivals.FirstOrDefault(x => x.Id == rivalId) is not { } v) return "없는 나라";
        if (_envoyTick == Tick) return "이번 계절에는 이미 사절을 보냄";
        switch (kind)
        {
            case "trade":
                if (v.Trade) return "이미 교역 중";
                if (Inst.Cannibalism) return "식인 풍습 때문에 상인들이 오가지 않음";
                if (!(Inst.Coinage || Inst.Guilds || Inst.Globalization)) return "화폐·길드·세계화 제도 중 하나가 필요";
                if (v.Hostility >= 0.6) return $"적대감이 너무 높음 ({v.Hostility:P0})";
                if (Realm.Treasury < 0.4) return "국고 부족 (0.4 필요)";
                return "";
            case "truce":
                if (v.Allied) return "이미 혼인 동맹 관계";
                if (Year < v.TreatyUntil) return $"화친 기간 남음 ({v.TreatyUntil - Year}년)";
                double truceCost = Inst.Parley ? 0.35 : 0.6;
                if (Realm.Treasury < truceCost) return $"국고 부족 ({truceCost:0.##} 필요)";
                return "";
            case "ally":
                if (v.Allied) return "이미 혼인 동맹 관계";
                if (!Inst.MarriageAlliance) return "혼인 동맹 제도 필요";
                if (Inst.Cannibalism) return "식인 풍습 때문에 혼담을 거부함";
                if (Rivals.Any(x => x.Allied)) return "이미 다른 나라와 혼인 동맹 중";
                if (Chief is not { Alive: true }) return "통치자 없음";
                if (v.Hostility >= 0.55) return $"적대감이 너무 높음 ({v.Hostility:P0})";
                if (!Living.Any(w => !w.IsMale && w.Master == null && w.Husband == null && w.Age(Tick) is >= 15 and <= 30)) return "혼기 미혼 여성 없음 (15~30세)";
                if (Realm.Treasury < 0.5) return "국고 부족 (0.5 필요)";
                return "";
            default:
                return "알 수 없는 사절";
        }
    }

    /// <summary>이웃 나라에 사절을 보내 교역·화친·혼인 동맹을 직접 맺는다(한 계절에 한 번).</summary>
    public bool SendEnvoy(int rivalId, string kind)
    {
        if (WhyCantEnvoy(rivalId, kind).Length > 0) return false;
        var v = Rivals.First(x => x.Id == rivalId);
        _envoyTick = Tick;
        _mute++;
        try
        {
            switch (kind)
            {
                case "trade":
                    Realm.Treasury -= 0.4;
                    v.Trade = true;
                    v.Hostility = Clamp01(v.Hostility - 0.08);
                    Say($"【교역】 {Josa.Iga(RulerTitle)} {v.Name}에 상단을 보내 장삿길을 열었다. 세입과 문물이 들어오고, 낯선 믿음과 병도 함께 따라올 것이다.");
                    break;
                case "truce":
                    double cost = Inst.Parley ? 0.35 : 0.6;
                    int years = Inst.Parley ? 12 : 8;
                    Realm.Treasury -= cost;
                    v.TreatyUntil = Year + years;
                    v.Hostility = Clamp01(v.Hostility - 0.22);
                    Realm.Grievance[(int)Estate.Nobles] = Math.Min(1, Realm.Grievance[(int)Estate.Nobles] + 0.06);
                    if (!Inst.Parley) Realm.LegitShock += 0.03;
                    int recalled = Armies.RemoveAll(a => a.Owner == v.Id && a.Muster > 0);
                    Say($"【화친】 {Josa.Iga(RulerTitle)} {v.Name}에 사절과 예물을 보내 {years}년의 화친을 맺었다."
                        + (recalled > 0 ? " 국경에 모이던 적군도 발길을 돌렸다." : "")
                        + " 싸움으로 공을 세우려던 귀족들은 불만을 품었다.");
                    break;
                case "ally":
                    if (!RoyalMarriage(v)) return false;
                    break;
            }
        }
        finally { _mute--; }
        Wrote("envoy", rivalId.ToString(), kind);
        return true;
    }

    Rival NewRival()
    {
        var r = Realm;
        string name;
        do name = RivalHeads[_rng.Next(RivalHeads.Length)] + RivalTails[_rng.Next(RivalTails.Length)];
        while (Rivals.Any(v => v.Name.StartsWith(name + " ")));
        var kinds = RivalKinds[(int)r.Stage];
        var v = new Rival
        {
            Id = _nextRival++,
            Name = $"{name} {kinds[_rng.Next(kinds.Length)]}",
            Strength = 0.5 + 0.1 * (int)r.Stage + 0.5 * Rnd(),
            Hostility = 0.25 + 0.4 * Rnd(),
            Faith = Rnd() < 0.3 ? 0 : 1 + _rng.Next(3),
            Advance = r.Knowledge * (0.7 + 0.6 * Rnd()),
        };
        Rivals.Add(v);
        return v;
    }

    /// <summary>도시국가가 되면 국경 너머의 나라들이 보이기 시작한다.</summary>
    void EnsureRivals()
    {
        if (Realm.Stage < Stage.CityState || Rivals.Count > 0) return;
        for (int k = 0; k < 3; k++) NewRival();
        Say($"【이웃 나라】 국경 너머에 {Josa.Iga(string.Join(", ", Rivals.Select(v => v.Name)))} 있다. 이제 싸움도 장사도 그들과 한다.");
    }

    internal void RivalsYear()
    {
        var r = Realm;
        var i = Inst;
        if (r.Stage < Stage.CityState) return;
        EnsureRivals();
        if (Rivals.Count < 4 && Rnd() < 0.03)
        {
            var n = NewRival();
            Say($"【새 세력】 국경 너머에 {Josa.Iga(n.Name)} 일어섰다.");
        }

        foreach (var v in Rivals.OrderBy(v => v.Id).ToList())
        {
            double target = 0.7 + 0.1 * (int)r.Stage + 0.03 * (int)r.Era;
            v.Strength = Math.Clamp(v.Strength + (target - v.Strength) * 0.03 + Noise(0.03), 0.15, 2);
            v.Advance += _knowledgeGain * (0.7 + 0.6 * Rnd());

            if (Rnd() < 0.006)
            {
                v.Strength *= 0.45;
                v.Hostility *= 0.6;
                Say($"【이웃의 내란】 {Josa.Iga(v.Name)} 내란으로 갈라졌다. 그쪽 국경은 한동안 조용할 것이다.");
            }
            if (!v.Free && r.Era >= Era.Industrial && Rnd() < 0.012)
            {
                v.Free = true;
                Say($"{v.Name}에서 왕이 쫓겨나고 백성이 다스릴 이를 뽑기 시작했다는 소식이 들려왔다.");
            }

            double ht = 0.35 + B(v.Faith != 0 && i.StateReligion && !i.Tolerance, 0.15) + B(i.Raiding, 0.12) + B(i.Nationalism, 0.1)
                        + B(i.Colonialism, 0.05) + 0.2 * (RulerT(Trait.Aggression) - 0.5)
                        - B(i.Parley, 0.1) - B(v.Trade, 0.15) - B(v.Allied, 0.25);
            v.Hostility = Clamp01(v.Hostility + (ht - v.Hostility) * 0.1 + Noise(0.03));

            if (i.Parley && Year >= v.TreatyUntil && !v.Allied && v.Hostility > 0.5 && r.Treasury > 0 && Rnd() < 0.2)
            {
                r.Treasury -= 0.4;
                v.TreatyUntil = Year + 8 + _rng.Next(10);
                v.Hostility = Clamp01(v.Hostility - 0.2);
                r.Grievance[(int)Estate.Nobles] += 0.04;
                Say($"【화친】 {Josa.Iga(RulerTitle)} {v.Name}에 사절과 선물을 보내 {v.TreatyUntil - Year}년의 평화를 샀다. 칼로 이름을 얻던 집안들은 못마땅해했다.");
            }

            if (v.Trade && v.Hostility > 0.65)
            {
                v.Trade = false;
                Say($"{Josa.Wa(v.Name)}의 교역로가 끊겼다.");
            }
            else if (!v.Trade && v.Hostility < 0.45 && (i.Coinage || i.Guilds || i.Globalization) && Rnd() < 0.15)
            {
                v.Trade = true;
                Say($"【교역】 {Josa.Wa(v.Name)} 장삿길이 열렸다. 물건과 함께 "
                    + (v.Faith != 0 ? "낯선 믿음과 " : "") + "소문과 병도 오갈 것이다.");
            }

            if (v.Allied && v.Hostility > 0.6)
            {
                v.Allied = false;
                Say($"{Josa.Wa(v.Name)}의 혼인 동맹이 깨졌다.");
            }
            else if (i.MarriageAlliance && !v.Allied && !Rivals.Any(x => x.Allied) && v.Hostility < 0.4 && Rnd() < 0.02)
                RoyalMarriage(v);
        }

        // 교역로를 따라 들어오는 것들: 문물, 믿음, 그리고 다른 세상의 소문.
        foreach (var v in Rivals.Where(v => v.Trade).OrderBy(v => v.Id))
        {
            if (v.Advance > r.Knowledge) r.Knowledge += Math.Min(3 + r.Knowledge * 0.01, (v.Advance - r.Knowledge) * 0.03);
            if (v.Faith != 0) r.Heresy = Math.Min(0.6, r.Heresy + 0.006 * (i.Printing ? 1.5 : 1) * (i.Inquisition ? 0.3 : 1));
            if (v.Free && !i.Suffrage)
            {
                r.Grievance[(int)Estate.Commoners] += 0.01;
                r.Grievance[(int)Estate.Workers] += 0.01;
                if (!_freeIdeasLogged)
                {
                    _freeIdeasLogged = true;
                    Say($"{v.Name}에서 온 장사꾼들이 그 나라에서는 백성이 통치자를 뽑는다고 전했다. 듣는 이들의 낯빛이 달라졌다.");
                }
            }
        }
        r.Heresy = Math.Max(0, r.Heresy - (i.Inquisition ? 0.01 : 0.002));
    }

    /// <summary>나라끼리의 혼인 동맹. 지배 가문의 딸 하나가 국경을 넘는다.</summary>
    internal bool RoyalMarriage(Rival v)
    {
        if (Chief is not { Alive: true } c) return false;
        var maidens = Living.Where(w => !w.IsMale && w.Master == null && w.Husband == null && w.Age(Tick) is >= 15 and <= 30).ToList();
        // 핏줄을 따지는 통치자는 제 딸 대신 남의 딸을 보낸다.
        bool spareOwn = c.T(Trait.KinBias) > 0.6;
        var bride = maidens.OrderByDescending(w => (w.Father == c) != spareOwn).ThenBy(w => w.Id).FirstOrDefault();
        if (bride == null) return false;
        string name = P(bride);
        foreach (var parent in new[] { bride.Father, bride.Mother })
            if (parent is { Alive: true } && parent != c)
                AddGrudge(parent, c, 0.5, $"딸 {Josa.Eul(bride.Name)} {v.Name}에 보낸 일로");
        Die(bride, "시집감");
        Realm.Treasury -= 0.5;
        v.Allied = true;
        v.Hostility = Clamp01(v.Hostility - 0.2);
        Say($"【혼인 동맹】 {Josa.Iga(name)} 지참금과 함께 {v.Name}의 궁정으로 시집갔다. 두 나라는 서로의 전쟁에 군사를 보내기로 했다.");
        return true;
    }
}
