using System.Globalization;

namespace Instinct.Sim;

/// <summary>정치 체제의 크기. 조건이 차면 올라가고, 무너지면 내려간다.</summary>
public enum Stage { Tribe, Chiefdom, CityState, Kingdom, Empire }

/// <summary>지식이 쌓여 넘어가는 시대. 시대가 새 제도와 새 재앙을 연다.</summary>
public enum Era { Primitive, Ancient, Medieval, EarlyModern, Industrial, Modern }

/// <summary>
/// 나라 단위의 상태. 군장사회부터는 이름 있는 사람들(People)이 지배 가문이 되고,
/// 그 아래 백성은 머릿수(Pop)와 감정(Unrest 등)으로만 그린다.
/// </summary>
public sealed class Realm
{
    public Stage Stage;
    public Stage PeakStage;
    public Era Era;

    /// <summary>백성의 수. 부족 단계에서는 쓰지 않는다.</summary>
    public double Pop;
    public int Territory = 1;
    /// <summary>나라 곳간. 한 해 세입을 1로 본 단위. 음수는 빚.</summary>
    public double Treasury;
    public double Knowledge;

    /// <summary>겉으로 드러난 민심 이반(0~1). 높으면 봉기가 난다.</summary>
    public double Unrest = 0.1;
    /// <summary>억눌러 숨긴 불만. 위기가 오면 한꺼번에 터진다.</summary>
    public double Suppressed;
    public double Legitimacy = 0.6;
    public double Inequality = 0.15;
    public double Corruption = 0.1;
    /// <summary>결속. 왕조가 늙고 땅이 넓어질수록 닳는다.</summary>
    public double Cohesion = 0.8;
    public double Urban = 0.02;
    public double Pollution;
    /// <summary>정통성에 남은 최근의 충격(패전, 기근, 찬탈). 해마다 옅어진다.</summary>
    public double LegitShock;

    /// <summary>지금 왕조(지배 씨족)가 선 해와 그 씨족.</summary>
    public int FoundedYear = 1;
    public int DynastyClan = -1;
    /// <summary>지금의 나라가 선 해. 나라가 늙을수록 결속이 닳는다.</summary>
    public int StateFounded = 1;
    /// <summary>몰락 뒤 이 해까지는 다시 커지지 못한다.</summary>
    public int RebuildUntil;
    /// <summary>이 해까지는 암흑기: 지식이 더디게 쌓인다.</summary>
    public int DarkAgeUntil;
    public int Collapses;
    /// <summary>나라의 이름으로 저지른 학살의 횟수. 지워지지 않는다.</summary>
    public int Atrocities;
    /// <summary>공포: 약탈·식인·학살로 쌓인 이름값(0~150). 이웃이 덜 쳐들어오고 더 미워한다. 해마다 옅어진다.</summary>
    public double Dread;

    /// <summary>계층의 비율(귀족·평민·예속민·노동자). 합은 1.</summary>
    public readonly double[] Share = { 0.03, 0.97, 0, 0 };
    /// <summary>개혁으로 계층마다 쌓인 분노. 해마다 옅어진다.</summary>
    public readonly double[] Grievance = new double[4];
    /// <summary>최근에 제도를 얼마나 자주 고쳤나. 높으면 다음 개혁의 값과 반발이 커진다.</summary>
    public double ReformHeat;
    /// <summary>교역로를 타고 들어온 다른 믿음을 따르는 백성의 비율.</summary>
    public double Heresy;
    /// <summary>몰락의 기록: "해|까닭".</summary>
    public readonly List<string> Falls = new();
}

public readonly record struct RealmSnapshot(
    int Year, Stage Stage, Era Era, double Pop, int Court, int Territory, double Treasury, double Knowledge,
    double Unrest, double Suppressed, double Legitimacy, double Inequality, double Corruption, double Cohesion);

// 부족 너머: 군장사회 → 도시국가 → 왕국 → 제국, 그리고 몰락.
public sealed partial class Simulation
{
    static readonly double[] EraKnowledge = { 0, 40, 220, 600, 1300, 2600 };
    static readonly double[] LandCap = { 600, 4000, 9000, 22000, 70000, 200000 };
    static readonly double[] UrbanBase = { 0.02, 0.06, 0.08, 0.14, 0.40, 0.75 };

    public readonly Realm Realm = new();
    public readonly List<RealmSnapshot> History = new();
    int _rulerId = -1;
    bool _crisisThisYear;
    double _knowledgeGain;
    int _debtYears;
    int _lastDebase = -100;

    /// <summary>우리 땅이 먹여 살릴 수 있는 사람 수.</summary>
    public double LandCapacity()
    {
        var r = Realm;
        var i = Inst;
        // 근대부터는 먹을 것을 사 올 수 있다: 땅의 넓이가 덜 중요해진다.
        double land = r.Era >= Era.Industrial ? 1.4 * Math.Pow(r.Territory, 0.7) : r.Territory;
        double cap = land * LandCap[(int)r.Era] * Fertility() * (1 - 0.3 * r.Pollution);
        if (i.Serfdom) cap *= 1.1;
        if (i.Colonialism) cap *= 1.15;
        if (i.Factories) cap *= 1 + 0.3 * (0.5 + 0.5 * Supply(Resource.Coal)); // 석탄이 없으면 공장은 절반만 돈다
        if (i.Globalization) cap *= 1.15;
        cap *= 1 + TB("pottery", 0.05) + TB("plow", 0.08) + TB("electricity", 0.1)
               + 0.04 * Supply(Resource.Livestock) + 0.08 * Supply(Resource.Iron);
        return cap;
    }

    public string StageName => Catalog.StageNames[(int)Realm.Stage];
    public string EraName => Catalog.EraNames[(int)Realm.Era];
    public string RulerTitle => Inst.Suffrage && Realm.Stage >= Stage.CityState ? "수반" : Catalog.RulerTitles[(int)Realm.Stage];

    /// <summary>이웃 무리와의 약탈·화친이 작동하는 규모인가. 도시국가부터는 나라의 전쟁이 대신한다.</summary>
    public bool Tribal => Realm.Stage < Stage.CityState;

    /// <summary>제도를 들일 수 있는가: 시대와 나라의 크기, 그것을 여는 기술, 그리고 핵무장은 우라늄.</summary>
    public bool Available(InstitutionDef d) =>
        Realm.Era >= d.Era && Realm.PeakStage >= d.MinStage && TechAllows(d) && (d.Key != "nuclear" || Supply(Resource.Uranium) > 0);

    /// <summary>들일 수 없는 까닭(화면에 보여 준다). 들일 수 있으면 빈 문자열.</summary>
    public string WhyUnavailable(InstitutionDef d)
    {
        if (Realm.Era < d.Era) return $"{Catalog.EraNames[(int)d.Era]}부터 가능";
        if (Realm.PeakStage < d.MinStage) return $"{Catalog.StageNames[(int)d.MinStage]} 단계부터 가능";
        if (!TechAllows(d)) return $"기술 필요: {TechTree.Find(TechTree.Gate[d.Key])!.Name}";
        if (d.Key == "nuclear" && Supply(Resource.Uranium) <= 0) return "우라늄 없음";
        return "";
    }

    static string Num(double x) => Math.Round(x).ToString("N0", CultureInfo.InvariantCulture);
    static double Clamp01(double x) => Math.Clamp(x, 0, 1);
    static double B(bool on, double v) => on ? v : 0;

    double RulerT(Trait t) => Chief is { Alive: true } c ? c.T(t) : 0.5;

    List<Person> Court() => Living.Where(p => p.Master == null && p.Age(Tick) >= 16).ToList();

    double CourtAvg(Trait t)
    {
        var court = Court();
        return court.Count == 0 ? 0.5 : court.Average(p => p.T(t));
    }

    /// <summary>나라의 재앙이 이름 있는 사람들에게도 미친다.</summary>
    List<Person> CourtDeaths(IEnumerable<Person> pool, double p, string cause)
    {
        var dead = pool.Where(x => x.Alive && Rnd() < p).ToList();
        foreach (var d in dead) Die(d, cause);
        return dead;
    }

    public int AdminCapacity()
    {
        var i = Inst;
        // 나라가 클수록, 그리고 글·법·길·철도 같은 다스림의 도구가 많을수록 더 넓은 땅에 명령이 닿는다.
        // 정복의 길은 넓은 땅을 다스리는 법을 안다.
        int conquest = (L(Course.Conquest) + 1) / 2;
        int tools = (i.Writing ? 1 : 0) + (i.LawCode ? 1 : 0) + (i.Feudalism ? 2 : 0) + (i.Absolutism ? 1 : 0) + (i.Printing ? 1 : 0)
                    + (i.Nationalism ? 1 : 0) + (Knows("wheel") ? 1 : 0) + (Knows("rail") ? 2 : 0);
        // 플레이어가 닦은 길: 도성에서 이어진 지방 셋마다 다스릴 수 있는 땅이 하나 는다.
        return 2 + (int)Realm.Stage + tools / 3 + RoadCount / 3 + conquest;
    }

    public double ArmyStrength()
    {
        var i = Inst;
        double a = 0.3 + 0.05 * (int)Realm.Stage + B(i.StandingArmy, 0.3) + B(i.Feudalism, 0.2)
                   + B(i.Conscription, 0.35) + B(i.Nationalism, 0.1) - B(i.Parliament, 0.05)
                   + 0.07 * L(Course.Conquest) - 0.03 * L(Course.Commerce);
        // 무엇으로 싸우는가: 말, 청동(철이 나오면 밀려난다), 쇠, 화약, 기름.
        a += 0.04 * Supply(Resource.Livestock) + (Knows("iron") ? 0.1 * Supply(Resource.Iron) : 0.06 * Supply(Resource.Copper))
             + TB("gunpowder", 0.06) + TB("engine", 0.12 * Supply(Resource.Oil)) + BE(Building.Barracks, 0.03, 4);
        return a * (Realm.Treasury < -1 ? 0.7 : 1);
    }

    // ───────────────────────── 다음 단계의 조건 (화면에 보여 준다) ─────────────────────────

    public string NextEraNeeds()
    {
        var r = Realm;
        if (r.Era == Era.Modern) return "";
        int next = (int)r.Era + 1;
        var needs = new List<string> { $"지식 {r.Knowledge:F0}/{EraKnowledge[next]:F0}" };
        var i = Inst;
        switch ((Era)next)
        {
            case Era.Ancient: if (r.Stage < Stage.Chiefdom) needs.Add("군장사회"); break;
            case Era.Medieval:
                if (!i.Writing) needs.Add("문자");
                if (r.PeakStage < Stage.CityState) needs.Add("도시국가");
                break;
            case Era.EarlyModern:
                if (!i.Coinage) needs.Add("화폐");
                if (r.PeakStage < Stage.Kingdom) needs.Add("왕국");
                break;
            case Era.Industrial: if (!i.Printing) needs.Add("인쇄술"); break;
            case Era.Modern:
                if (!i.Factories) needs.Add("공장제");
                if (!i.PublicSchooling) needs.Add("의무교육");
                break;
        }
        return $"{Catalog.EraNames[next]}: " + string.Join(", ", needs);
    }

    public string NextStageNeeds()
    {
        var r = Realm;
        var needs = new List<string>();
        switch (r.Stage)
        {
            case Stage.Tribe:
                if (!Inst.Agriculture) needs.Add("농경");
                needs.Add($"인구 {Living.Count()}/30");
                break;
            case Stage.Chiefdom:
                needs.Add($"백성 {Num(r.Pop)}/2,500");
                if (r.Era < Era.Ancient) needs.Add("고대");
                if (!Inst.Taxation) needs.Add("조세");
                break;
            case Stage.CityState:
                needs.Add($"백성 {Num(r.Pop)}/15,000");
                needs.Add($"영토 {r.Territory}/3");
                break;
            case Stage.Kingdom:
                needs.Add($"백성 {Num(r.Pop)}/120,000");
                needs.Add($"영토 {r.Territory}/7");
                break;
            default: return "";
        }
        return $"{Catalog.StageNames[(int)r.Stage + 1]}: " + string.Join(", ", needs);
    }

    public readonly record struct MilestoneNeed(string Text, bool Met, string? LawKey = null, string? TechKey = null);
    public readonly record struct BudgetForecast(double Income, double BaseExpense, double Upkeep, double Drain, double Net);

    /// <summary>다음 정치 단계의 조건을 항목별로 돌려준다(화면의 클릭 가능한 나침반용, 난수 없음).</summary>
    public (string Target, List<MilestoneNeed> Needs) NextStageChecks()
    {
        var r = Realm;
        var n = new List<MilestoneNeed>();
        switch (r.Stage)
        {
            case Stage.Tribe:
                n.Add(new("농경", Inst.Agriculture, "agriculture", "farming"));
                int folk = Living.Count();
                n.Add(new($"인구 {folk}/30", folk >= 30));
                return ("군장사회", n);
            case Stage.Chiefdom:
                n.Add(new($"백성 {Num(r.Pop)}/2,500", r.Pop >= 2500));
                n.Add(new("고대 시대", r.Era >= Era.Ancient, null, "pottery"));
                n.Add(new("조세", Inst.Taxation, "taxation", "counting"));
                return ("도시국가", n);
            case Stage.CityState:
                n.Add(new($"백성 {Num(r.Pop)}/15,000", r.Pop >= 15000));
                n.Add(new($"영토 {r.Territory}/3", r.Territory >= 3));
                return ("왕국", n);
            case Stage.Kingdom:
                n.Add(new($"백성 {Num(r.Pop)}/120,000", r.Pop >= 120000));
                n.Add(new($"영토 {r.Territory}/7", r.Territory >= 7));
                return ("제국", n);
            default:
                return ("", n);
        }
    }

    /// <summary>다음 시대의 조건을 항목별로 돌려준다(화면의 클릭 가능한 나침반용, 난수 없음).</summary>
    public (string Target, List<MilestoneNeed> Needs) NextEraChecks()
    {
        var r = Realm;
        var i = Inst;
        var n = new List<MilestoneNeed>();
        if (r.Era == Era.Modern) return ("", n);
        int next = (int)r.Era + 1;
        double goal = EraKnowledge[next];
        n.Add(new($"지식 {r.Knowledge:F0}/{goal:F0}", r.Knowledge >= goal, null, ""));
        switch ((Era)next)
        {
            case Era.Ancient:
                n.Add(new("군장사회", r.Stage >= Stage.Chiefdom, "agriculture", "farming"));
                break;
            case Era.Medieval:
                n.Add(new("문자", i.Writing, "writing", "script"));
                n.Add(new("도시국가", r.PeakStage >= Stage.CityState));
                break;
            case Era.EarlyModern:
                n.Add(new("화폐", i.Coinage, "coinage", "currency"));
                n.Add(new("왕국", r.PeakStage >= Stage.Kingdom));
                break;
            case Era.Industrial:
                n.Add(new("인쇄술", i.Printing, "printing", "press"));
                break;
            case Era.Modern:
                n.Add(new("공장제", i.Factories, "factories", "steam"));
                n.Add(new("의무교육", i.PublicSchooling, "schooling", "press"));
                break;
        }
        return (Catalog.EraNames[next], n);
    }

    /// <summary>이번 해 겨울 정산 때 예상되는 국고 수지(난수 없음, 화면 표시용).</summary>
    public BudgetForecast ForecastTreasury()
    {
        var r = Realm;
        var i = Inst;
        if (r.Stage == Stage.Tribe) return default;
        double income = (i.Taxation ? 0.9 : 0.5) * (1 - r.Corruption)
                        * (1 + B(i.Writing, 0.15) + B(i.Coinage, 0.2) + B(i.Serfdom, 0.2) + B(i.Slavery, 0.1) + B(i.Colonialism, 0.5)
                           + B(i.Factories, 0.5) + B(i.Globalization, 0.3) + B(i.PrivateProperty, 0.1)
                           + 0.08 * Rivals.Count(v => v.Trade) * (i.Globalization ? 1.5 : 1)
                           + 0.15 * Supply(Resource.Spice) + TB("banking", 0.1)
                           + B(i.Factories, 0.06 * Supply(Resource.Coal)) + TB("engine", 0.1 * Supply(Resource.Oil))
                           + BE(Building.Market, 0.03, 6) + BE(Building.Mine, 0.05, 4) + B(i.Factories, BE(Building.Factory, 0.06, 5))
                           + BE(Building.Harbor, 0.02, 3) * Rivals.Count(v => v.Trade) + 0.01 * Math.Min(10, RoadCount)
                           + 0.08 * L(Course.Commerce) + 0.02 * L(Course.Commerce) * Rivals.Count(v => v.Trade)
                           - B(i.Feudalism, 0.2) - B(i.LaborLaws, 0.1) - B(i.GreenLaw, 0.08));
        int over = Math.Max(0, r.Territory - AdminCapacity());
        double rawUpkeep = Upkeep();
        double rawBase = 0.15 + 0.05 * (int)r.Stage + B(i.StandingArmy, 0.3) + B(i.Conscription, 0.15) + B(i.Welfare, 0.3) + B(i.PublicSchooling, 0.15)
                         + B(i.Surveillance, 0.15) + B(i.Inquisition, 0.05) + 0.08 * over + 0.03 * r.Territory + 0.06 * (int)r.Stage
                         + 0.04 * L(Course.Conquest) + 0.02 * (int)r.Era;
        double baseExp = rawBase * ExpenseMul;
        double upkeep = rawUpkeep * ExpenseMul;
        double after = Math.Clamp(r.Treasury + income - (baseExp + upkeep), i.Taxation ? -6 : -1, i.Taxation ? 12 : 3);
        double drain = after > 3 ? (after - 3) * (0.1 + 0.3 * r.Corruption + 0.1 * RulerT(Trait.Greed)) : 0;
        return new BudgetForecast(income, baseExp, upkeep, drain, after - drain - r.Treasury);
    }

    // ───────────────────────── 한 해 ─────────────────────────

    /// <summary>겨울마다 나라의 한 해를 셈한다.</summary>
    internal void RealmYear()
    {
        var r = Realm;
        _crisisThisYear = false;
        GrowKnowledge();
        AdvanceEra();
        CourseYear();
        if (r.Stage == Stage.Tribe)
        {
            r.ReformHeat *= 0.6;
            if (Inst.Agriculture && Chief is { Alive: true } c && Living.Count() >= 30) BecomeChiefdom(c);
        }
        else
        {
            RulerCheck();
            RivalsYear();
            RealmEconomy();
            EstatesYear();
            RealmMood();
            BuildingsYear();
            RealmEvents();
            CourtYear();
            AdvanceStage();
            if (r.Stage >= Stage.CityState) CheckCollapse();
        }
        SyncMap();
        if (Proposals) ProposeYear();
        History.Add(new RealmSnapshot(Year, r.Stage, r.Era, r.Stage == Stage.Tribe ? Living.Count() : r.Pop, Living.Count(),
            r.Territory, r.Treasury, r.Knowledge, r.Unrest, r.Suppressed, r.Legitimacy, r.Inequality, r.Corruption, r.Cohesion));
    }

    /// <summary>지식이 쌓이는 빠르기의 배수(판의 템포). 시대와 기술이 모두 이 빠르기를 따른다.</summary>
    public const double KnowledgePace = 1.3;

    void GrowKnowledge()
    {
        var r = Realm;
        var i = Inst;
        double people = r.Stage == Stage.Tribe ? Living.Count() : r.Pop;
        double k = KnowledgePace * (0.25 + 0.35 * Math.Log10(people + 10) * (1 + 2 * r.Urban));
        if (i.Writing) k *= 1.4;
        if (i.Guilds) k *= 1.1;
        if (i.Printing) k *= 1.5;
        if (i.Tolerance) k *= 1.15;
        if (i.PublicSchooling) k *= 1.4;
        if (i.Globalization) k *= 1.2;
        if (i.StateReligion) k *= 0.9;
        if (i.Inquisition) k *= 0.7;
        if (i.Surveillance) k *= 0.85;
        if (i.Propaganda) k *= 0.95;
        if (i.Tolerance) k *= 1 + 0.2 * r.Heresy; // 다른 믿음과 함께 다른 생각도 들어온다
        if (Knows("theology")) k *= 1.05;
        if (Knows("computing")) k *= 1.15;
        k *= 1 - 0.05 * L(Course.Tyranny); // 묻지 않는 나라는 덜 배운다
        k *= 1 + BE(Building.Library, 0.05, 5) + BE(Building.University, 0.08, 3) - BE(Building.Temple, 0.02, 4);
        _knowledgeGain = k; // 이웃 나라들도 이만큼씩 나아간다 (암흑기는 우리만의 것)
        if (Year < r.DarkAgeUntil) k *= 0.5;
        r.Knowledge += k;
        ResearchYear(k);
    }

    void AdvanceEra()
    {
        var r = Realm;
        var i = Inst;
        if (r.Era == Era.Modern) return;
        var next = r.Era + 1;
        if (r.Knowledge < EraKnowledge[(int)next]) return;
        bool ok = next switch
        {
            Era.Ancient => r.Stage >= Stage.Chiefdom,
            Era.Medieval => i.Writing && r.PeakStage >= Stage.CityState,
            Era.EarlyModern => i.Coinage && r.PeakStage >= Stage.Kingdom,
            Era.Industrial => i.Printing,
            _ => i.Factories && i.PublicSchooling,
        };
        if (!ok) return;
        r.Era = next;
        Say($"【시대】 {Catalog.EraNames[(int)next]}가 열렸다. " + next switch
        {
            Era.Ancient => "청동과 곡식 창고, 그리고 그것을 지키는 자들의 시대.",
            Era.Medieval => "성과 사원, 땅에 묶인 사람들의 시대.",
            Era.EarlyModern => "돛과 화약, 인쇄된 말의 시대.",
            Era.Industrial => "증기와 굴뚝, 공장 종소리의 시대.",
            _ => "전파와 원자, 숫자로 세는 사람들의 시대.",
        });
    }

    void BecomeChiefdom(Person c)
    {
        var r = Realm;
        r.Stage = r.PeakStage = Stage.Chiefdom;
        r.Pop = Living.Count() * 6;
        r.DynastyClan = c.ClanId;
        r.FoundedYear = r.StateFounded = Year;
        r.Share[(int)Estate.Bound] = Inst.Slavery ? 0.1 : 0;
        r.Share[(int)Estate.Commoners] = 0.97 - r.Share[(int)Estate.Bound];
        _rulerId = c.Id;
        Say($"【군장사회】 곡식이 쌓이자 가까운 무리들이 {P(c)} 아래로 모였다. 이제 이름이 남는 것은 다스리는 집안들이고, 그 아래 {Num(r.Pop)}명의 백성이 밭을 간다.");
    }

    void AdvanceStage()
    {
        var r = Realm;
        if (Year < r.RebuildUntil) return;
        switch (r.Stage)
        {
            case Stage.Chiefdom when r.Pop >= 2500 && r.Era >= Era.Ancient && Inst.Taxation:
                r.Stage = Stage.CityState;
                Neighbors.Clear();
                Say($"【도시국가】 성벽이 올라가고 장이 섰다. 이웃 무리들은 성 안으로 흡수되거나 쫓겨났다. 백성 {Num(r.Pop)}명.");
                EnsureRivals();
                break;
            case Stage.CityState when r.Pop >= 15000 && r.Territory >= 3:
                r.Stage = Stage.Kingdom;
                Say($"【왕국】 {(Chief != null ? Iga(Chief) : "통치자가")} 여러 도시를 아울러 왕을 칭했다. 백성 {Num(r.Pop)}명, 영토 {r.Territory}.");
                break;
            case Stage.Kingdom when r.Pop >= 120000 && r.Territory >= 7:
                r.Stage = Stage.Empire;
                Say($"【제국】 서로 말이 다른 땅들이 한 깃발 아래 놓였다. 백성 {Num(r.Pop)}명, 영토 {r.Territory}. 이제 넓이가 곧 짐이다.");
                break;
            default: return;
        }
        if (r.Stage > r.PeakStage) r.PeakStage = r.Stage;
    }

    // ───────────────────────── 통치자와 왕조 ─────────────────────────

    void RulerCheck()
    {
        var r = Realm;
        if (Chief is not { Alive: true } c || c.Id == _rulerId) return;
        var prev = _rulerId >= 0 ? _byId[_rulerId] : null;
        _rulerId = c.Id;
        if (prev == null) return;

        bool usurped = prev.Alive || prev.DeathCause is "살해" or "처형";
        if (usurped) r.LegitShock += 0.15;
        ReleaseSuppressed("통치자가 바뀌는 틈에");
        // 혼인으로 맺은 동맹은 사돈이 죽으면 흐려진다.
        foreach (var v in Rivals.Where(v => v.Allied).OrderBy(v => v.Id).ToList())
            if (Rnd() < 0.12)
            {
                v.Allied = false;
                Say($"자리의 주인이 바뀌자 {Josa.Wa(v.Name)} 맺은 혼인 동맹도 옛일이 되었다.");
            }

        if (c.ClanId != r.DynastyClan)
        {
            r.DynastyClan = c.ClanId;
            r.FoundedYear = Year;
            r.LegitShock += 0.05;
            if (r.Stage >= Stage.CityState)
                Say($"【왕조 교체】 {ClanName(prev.ClanId)}족의 시대가 끝나고 {ClanName(c.ClanId)}족 {c.Name}의 집안이 자리에 올랐다.");
        }

        // 왕국부터는 자리가 비면 칼이 먼저 나선다.
        if (r.Stage >= Stage.Kingdom && Rnd() < (usurped ? 0.5 : 0.15) * (Inst.Suffrage ? 0.2 : 1) * (Inst.LawCode ? 0.7 : 1))
        {
            double loss = r.Pop * (0.02 + 0.04 * Rnd());
            r.Pop -= loss;
            r.LegitShock += 0.1;
            _crisisThisYear = true;
            var fallen = CourtDeaths(Warriors().Where(m => m != c), 0.1, "전쟁");
            Say($"【계승 전쟁】 {P(c)}의 자리를 인정하지 않는 자들이 군사를 일으켰다. 백성 {Num(loss)}명이 죽었다. 쓰러진 이: {NamesOr(fallen.Select(P), "없음")}.");
        }
    }

    // ───────────────────────── 살림: 인구와 재정 ─────────────────────────

    void RealmEconomy()
    {
        var r = Realm;
        var i = Inst;

        double cap = LandCapacity();

        double harvest = cap * (0.6 + 0.4 * _yearFactor);
        if (r.Pop > harvest && _yearFactor < 0.9)
        {
            // 풍년에 불어난 입을 흉년이 거둔다.
            double soften = i.Globalization ? 0.3 : 1;
            double dead = ((r.Pop - harvest) * 0.6 + r.Pop * 0.02) * soften * (1 - BE(Building.Granary, 0.12, 4)) * (i.Cannibalism ? 0.75 : 1);
            r.Pop -= dead;
            r.Unrest = Clamp01(r.Unrest + 0.15 * soften);
            r.LegitShock += 0.08;
            _crisisThisYear = true;
            Say($"【기근】 흉년이 들어 백성 {Num(dead)}명이 굶어 죽었다.");
            ReleaseSuppressed("굶주림 앞에서");
            AskRelief(dead);
            if (i.Inquisition && Rnd() < 0.5) WitchHunt("흉년");
        }
        else
        {
            double g = r.Era == Era.Modern ? 0.02 : 0.03;
            if (r.Era == Era.Modern && r.Urban > 0.6) g -= 0.006; // 도시의 아이는 비싸다
            if (i.Welfare) g -= 0.004;
            if (r.Pop < 0.4 * cap) g *= 2; // 빈 땅은 빨리 찬다
            r.Pop += r.Pop * g * (1 - r.Pop / Math.Max(1, cap));
        }
        r.Pop = Math.Max(50, r.Pop);
        Settle(cap);

        double urbanTarget = UrbanBase[(int)r.Era] + B(i.Guilds, 0.04) + B(i.Coinage, 0.02) + B(i.Factories, 0.1);
        r.Urban += (urbanTarget - r.Urban) * 0.03;

        double income = (i.Taxation ? 0.9 : 0.5) * (1 - r.Corruption)
                        * (1 + B(i.Writing, 0.15) + B(i.Coinage, 0.2) + B(i.Serfdom, 0.2) + B(i.Slavery, 0.1) + B(i.Colonialism, 0.5)
                           + B(i.Factories, 0.5) + B(i.Globalization, 0.3) + B(i.PrivateProperty, 0.1)
                           + 0.08 * Rivals.Count(v => v.Trade) * (i.Globalization ? 1.5 : 1)
                           + 0.15 * Supply(Resource.Spice) + TB("banking", 0.1)
                           + B(i.Factories, 0.06 * Supply(Resource.Coal)) + TB("engine", 0.1 * Supply(Resource.Oil))
                           + BE(Building.Market, 0.03, 6) + BE(Building.Mine, 0.05, 4) + B(i.Factories, BE(Building.Factory, 0.06, 5))
                           + BE(Building.Harbor, 0.02, 3) * Rivals.Count(v => v.Trade) + 0.01 * Math.Min(10, RoadCount)
                           + 0.08 * L(Course.Commerce) + 0.02 * L(Course.Commerce) * Rivals.Count(v => v.Trade)
                           - B(i.Feudalism, 0.2) - B(i.LaborLaws, 0.1) - B(i.GreenLaw, 0.08));
        int over = Math.Max(0, r.Territory - AdminCapacity());
        double expense = 0.15 + 0.05 * (int)r.Stage + B(i.StandingArmy, 0.3) + B(i.Conscription, 0.15) + B(i.Welfare, 0.3) + B(i.PublicSchooling, 0.15)
                         + B(i.Surveillance, 0.15) + B(i.Inquisition, 0.05) + 0.08 * over + 0.03 * r.Territory + 0.06 * (int)r.Stage
                         + 0.04 * L(Course.Conquest) // 정복의 길: 군영과 성채를 먹여 살린다
                         + 0.02 * (int)r.Era // 시대가 갈수록 나라가 할 일이 는다
                         + Upkeep();
        expense *= ExpenseMul;
        // 조세가 없으면 곳간이라 할 것도, 빚을 낼 곳도 없다.
        r.Treasury = Math.Clamp(r.Treasury + income - expense, i.Taxation ? -6 : -1, i.Taxation ? 12 : 3);
        // 남는 돈은 곳간에 머물지 않는다: 궁정의 사치와 새는 돈.
        if (r.Treasury > 3) r.Treasury -= (r.Treasury - 3) * (0.1 + 0.3 * r.Corruption + 0.1 * RulerT(Trait.Greed));

        r.Pollution = Clamp01(r.Pollution + B(i.Factories, 0.008) + B(i.Globalization, 0.003) - B(i.GreenLaw, 0.012) - 0.001);

        if (r.Treasury >= -2) _debtYears = 0;
        else
        {
            _crisisThisYear = true;
            _debtYears++;
            if (i.Coinage && Year - _lastDebase >= 8)
            {
                // 한 번 깎은 돈은 한동안 다시 깎지 못한다: 아무도 그 돈을 믿지 않는다.
                _lastDebase = Year;
                r.Treasury += 2.5;
                r.Unrest = Clamp01(r.Unrest + 0.08);
                r.Inequality = Clamp01(r.Inequality + 0.03);
                Say($"【화폐 가치 절하】 빚에 몰린 {Josa.Iga(RulerTitle)} 돈에 섞는 구리를 늘렸다. 값이 뛰고, 품삯으로 사는 이들이 먼저 무너졌다.");
            }
            else if (!i.Coinage && Rnd() < 0.3)
            {
                r.Unrest = Clamp01(r.Unrest + 0.08);
                r.Treasury += 1;
                Say($"곳간이 비자 {Josa.Eun(RulerTitle)} 세금을 한 번 더 거두었다.");
            }
        }
    }

    // ───────────────────────── 민심: 불평등·부패·결속·정통성·불만 ─────────────────────────

    /// <summary>민심 값 하나를 미는 것 하나: 이름과 크기(+면 올리고 −면 내린다). 화면의 툴팁이 "왜 이 값인가"를 보인다.</summary>
    public sealed record MoodPart(string What, double Value);

    static double Total(List<MoodPart> parts)
    {
        double sum = 0;
        foreach (var p in parts) sum += p.Value;
        return sum;
    }

    int OverCap => Math.Max(0, Realm.Territory - AdminCapacity());

    /// <summary>결속(단합)이 향해 가는 값의 갈래. 합이 목표이고, 결속은 해마다 그쪽으로 6%씩 다가간다.</summary>
    public List<MoodPart> CohesionParts()
    {
        var r = Realm; var i = Inst;
        int stateAge = Year - r.StateFounded;
        return new()
        {
            new("기본", 0.68), new("국교", B(i.StateReligion, 0.1)), new("종교재판", B(i.Inquisition, 0.05)), new("민족주의", B(i.Nationalism, 0.2)),
            new("의무교육", B(i.PublicSchooling, 0.06)), new("선전", B(i.Propaganda, 0.08)), new("종교 관용", -B(i.Tolerance, 0.06)),
            new("세계화", -B(i.Globalization, 0.08)), new("정복의 길", -0.02 * L(Course.Conquest)),
            new("현대의 보통선거(갈라진 여론)", -B(i.Suffrage && r.Era == Era.Modern, 0.08)),
            new("낯선 믿음(국교, 관용 없음)", -B(i.StateReligion && !i.Tolerance, 0.25 * r.Heresy)),
            new("불평등", -0.2 * r.Inequality), new("부패", -0.2 * r.Corruption), new("다스릴 한도를 넘은 땅", -0.07 * OverCap),
            new("나라의 나이", -Math.Min(0.25, stateAge * 0.0008)), new("나라의 크기", -0.05 * Math.Max(0, (int)r.Stage - 2)),
            new("난이도", StateBias),
        };
    }

    /// <summary>정통성(통치자 지지)이 향해 가는 값의 갈래. 해마다 15%씩 다가간다.</summary>
    public List<MoodPart> LegitimacyParts()
    {
        var r = Realm; var i = Inst;
        return new()
        {
            new("기본", 0.5), new("국교", B(i.StateReligion, 0.1)), new("절대왕정", B(i.Absolutism, 0.1)), new("의회", B(i.Parliament, 0.05)),
            new("보통선거", B(i.Suffrage, 0.15)), new("선전", B(i.Propaganda, 0.1)), new("성문법", B(i.LawCode, 0.05)),
            new("통치자의 위신", 0.1 * Math.Min(1, (Chief?.Status ?? 0) / 6)), new("전제의 길", 0.04 * L(Course.Tyranny)),
            new("공포의 길", -0.03 * L(Course.Terror)), new("식인 풍습", -B(i.Cannibalism, 0.06)),
            new("부패", -0.3 * r.Corruption), new("최근의 충격(패전·기근·개혁)", -r.LegitShock), new("잔학 행위", -0.02 * Math.Min(5, r.Atrocities)),
            new("난이도", StateBias),
        };
    }

    /// <summary>불만이 향해 가는 값의 갈래(억누르기 전). 해마다 12%씩 다가간다.</summary>
    public List<MoodPart> UnrestParts()
    {
        var r = Realm; var i = Inst;
        return new()
        {
            new("기본", 0.08), new("불평등", 0.5 * r.Inequality), new("부패", 0.3 * r.Corruption), new("조세", B(i.Taxation, 0.06)),
            new("농노제", B(i.Serfdom, 0.1)), new("노예제", B(i.Slavery, 0.06)), new("노동법 없는 공장", B(i.Factories && !i.LaborLaws, 0.1)),
            new("징병제", B(i.Conscription, 0.03)), new("세계화", B(i.Globalization, 0.04)), new("다스릴 한도를 넘은 땅", 0.05 * OverCap),
            new("통치자 지지", -0.25 * (r.Legitimacy - 0.5)), new("국교", -B(i.StateReligion, 0.06)), new("의회", -B(i.Parliament, 0.06)),
            new("보통선거", -B(i.Suffrage, 0.1)), new("복지국가", -B(i.Welfare, 0.12)), new("통치자의 공감", -0.08 * RulerT(Trait.Empathy)),
            new("성난 계층", 0.35 * (r.Share[1] * r.Grievance[1] + r.Share[2] * r.Grievance[2] + r.Share[3] * r.Grievance[3])),
            new("난이도", UnrestBias),
        };
    }

    /// <summary>불만을 겉에서 지우는 억누름(값은 양수). 지운 만큼 억눌린 불만으로 쌓인다.</summary>
    public List<MoodPart> RepressParts()
    {
        var i = Inst;
        return new()
        {
            new("종교재판", B(i.Inquisition, 0.08)), new("절대왕정", B(i.Absolutism, 0.05)), new("선전", B(i.Propaganda, 0.08)),
            new("감시", B(i.Surveillance, 0.15)), new("전제의 길", 0.04 * L(Course.Tyranny)),
        };
    }

    void RealmMood()
    {
        var r = Realm;
        var i = Inst;
        int age = Year - r.FoundedYear, stateAge = Year - r.StateFounded;
        int over = Math.Max(0, r.Territory - AdminCapacity());

        double ineq = 0.2 + B(i.PrivateProperty, 0.08) + B(i.Slavery, 0.1) + B(i.Agriculture, 0.04) + B(i.Writing, 0.03) + B(i.LawCode, 0.03)
                      + B(i.Coinage, 0.06) + B(i.Feudalism, 0.05) + B(i.Serfdom, 0.12) + B(i.Guilds, 0.03) + B(i.Colonialism, 0.05)
                      + B(i.Factories, 0.15) + B(i.Globalization, 0.08) + 0.1 * RulerT(Trait.Greed) + 0.025 * L(Course.Commerce)
                      - B(i.LaborLaws, 0.1) - B(i.Suffrage, 0.05) - B(i.PublicSchooling, 0.03) - B(i.Welfare, 0.15);
        r.Inequality = Clamp01(r.Inequality + (Clamp01(ineq) - r.Inequality) * 0.05);

        double corr = 0.1 + 0.25 * CourtAvg(Trait.Greed) + 0.15 * RulerT(Trait.KinBias) + Math.Min(0.2, age / 500.0)
                      + B(i.Absolutism, 0.12) + B(i.Feudalism, 0.05) + B(i.Surveillance, 0.08) + B(i.Colonialism, 0.04) + 0.03 * L(Course.Tyranny)
                      - B(i.LawCode, 0.1) - B(i.Parliament, 0.12) - B(i.Suffrage, 0.08) - B(i.Printing, 0.04);
        r.Corruption = Clamp01(r.Corruption + (Clamp01(corr) - r.Corruption) * 0.05);

        double coh = Total(CohesionParts());
        r.Cohesion = Clamp01(r.Cohesion + (Clamp01(coh) - r.Cohesion) * 0.06);

        r.LegitShock = Math.Clamp(r.LegitShock * 0.8, -0.2, 0.6);
        double legit = Total(LegitimacyParts());
        r.Legitimacy = Clamp01(r.Legitimacy + (Clamp01(legit) - r.Legitimacy) * 0.15);

        double target = Total(UnrestParts());
        // 억누르면 보이지 않을 뿐 사라지지 않는다.
        double repress = Total(RepressParts());
        r.Unrest = Clamp01(r.Unrest + (Clamp01(target - repress) - r.Unrest) * 0.12);
        r.Suppressed = Clamp01(r.Suppressed + (repress > 0 ? repress * 0.25 * Clamp01(target * 2) : -0.03));
    }

    void ReleaseSuppressed(string when)
    {
        var r = Realm;
        if (r.Suppressed < 0.25) return;
        r.Unrest = Clamp01(r.Unrest + r.Suppressed * 0.8);
        r.Suppressed = 0;
        Say($"{when} 오래 억눌렸던 분노가 한꺼번에 터져 나왔다.");
    }

    // ───────────────────────── 사건 ─────────────────────────

    void RealmEvents()
    {
        var r = Realm;
        var i = Inst;

        double pPlague = (0.006 + 0.06 * r.Urban + B(i.Globalization, 0.015) + B(i.Colonialism, 0.008) + B(i.Agriculture, 0.004)
                          + 0.004 * Rivals.Count(v => v.Trade) + 0.004 * L(Course.Commerce) + B(i.Cannibalism, 0.006))
                         * (r.Era >= Era.Modern ? 0.3 : r.Era == Era.Industrial ? 0.6 : 1);
        if (Rnd() < pPlague * (Knows("medicine") ? 0.5 : 1)) Plague();

        RealmWar();

        if (i.Inquisition && Rnd() < 0.02 + 0.1 * r.Heresy) WitchHunt("");
        if (i.StandingArmy && (r.Treasury < -1 || r.Legitimacy < 0.35) && Rnd() < 0.08 * (1 - 0.15 * L(Course.Tyranny))) Coup();
        if (i.Feudalism && !i.Absolutism && r.Legitimacy < 0.55 && Rnd() < 0.1) NobleRevolt();
        if (i.StateReligion && !i.Tolerance && r.Era <= Era.Industrial && Rnd() < B(i.Printing, 0.03) + 0.25 * r.Heresy) ReligiousWar();
        if (i.Colonialism) ColonyEvents();
        if (i.Factories) FactoryEvents();
        if (i.Nationalism) Persecution();
        if (i.Propaganda && i.Surveillance && Rnd() < 0.15) Purge();
        if (r.Era == Era.Modern) ModernEvents();
        if (Rivals.OrderBy(v => v.Id).FirstOrDefault(v => v.Allied) is { } ally && Rnd() < 0.06) AllyWar(ally);
        EstateEvents();
        ResourceEvents();

        if (r.Unrest > 0.6 && Rnd() < (r.Unrest - 0.5) * 0.35 * CrisisMul) Uprising();
    }

    internal void Plague()
    {
        var r = Realm;
        bool great = r.Era is Era.Medieval or Era.EarlyModern && Rnd() < 0.35;
        double sev = great ? 0.25 + 0.15 * Rnd() : 0.04 + 0.1 * Rnd();
        if (r.Era >= Era.Industrial) sev *= 0.5;
        double dead = r.Pop * sev;
        r.Pop -= dead;
        _crisisThisYear = true;
        var court = CourtDeaths(Living.ToList(), sev * 0.6, "역병");
        // 일손이 귀해지면 살아남은 자의 품삯이 오른다.
        r.Inequality = Clamp01(r.Inequality - sev * 0.3);
        r.LegitShock += sev * 0.3;
        Say($"【역병】 {(great ? "큰 돌림병이 온 나라를 휩쓸었다" : "돌림병이 돌았다")}. 백성 {Num(dead)}명이 죽었다."
            + (court.Count > 0 ? $" 지배 가문에서도 {court.Count}명이 죽었다." : ""));
        if (great && Inst.Serfdom)
        {
            r.Unrest = Clamp01(r.Unrest + 0.15);
            Say("일손이 모자라자 살아남은 농노들이 품삯과 자유를 요구하기 시작했다.");
        }
        if (Inst.Inquisition || (Inst.StateReligion && Rnd() < 0.4)) WitchHunt("역병");
    }

    /// <summary>재앙에는 탓할 사람이 필요하다. 가장 미움받는 이가 불려 나온다.</summary>
    internal void WitchHunt(string after)
    {
        var r = Realm;
        var adults = Living.Where(p => p.Age(Tick) >= 14 && p != Chief).ToList();
        Person? victim = null, accuser = null;
        double worst = 0;
        foreach (var v in adults)
        {
            double hate = 0, top = 0;
            Person? who = null;
            foreach (var k in adults)
            {
                if (!k.Grudge.TryGetValue(v.Id, out var g)) continue;
                hate += g;
                if (g > top) { top = g; who = k; }
            }
            hate *= (v.IsMale ? 0.6 : 1.3) * (v.Outsider ? 1.5 : 1) * (v.Master != null ? 0.7 : 1);
            if (hate > worst) { worst = hate; victim = v; accuser = who; }
        }
        double mass = r.Pop * (0.0005 + 0.002 * Rnd());
        r.Pop -= mass;
        r.Knowledge *= 0.997;
        r.Suppressed = Clamp01(r.Suppressed + 0.05);
        string lead = after == "" ? "" : $"{after}의 탓을 찾던 사람들이 ";
        if (victim == null || accuser == null || worst < 0.3)
        {
            Say($"【마녀사냥】 {lead}이웃을 고발하기 시작했다. {Num(mass)}명이 불에 탔다.");
            return;
        }
        string why = accuser.GrudgeWhy.GetValueOrDefault(victim.Id, "묵은 원한으로");
        string name = P(victim);
        Die(victim, "화형");
        Say($"【마녀사냥】 {lead}{Josa.Eul(name)} 마녀로 몰아 불태웠다. 고발한 이는 {P(accuser)} — 실은 {why} 품은 원한이었다. 나라 곳곳에서 {Num(mass)}명이 같은 길을 갔다.");
    }


    internal void RealmWar()
    {
        var r = Realm;
        var i = Inst;
        if (r.Stage < Stage.CityState) return; // 그 아래는 이웃 무리와의 약탈이 전쟁이다
        if (PlayerRules) { InvasionYear(); return; } // 플레이어가 다스리면 전쟁은 지도 위에서 벌어진다
        EnsureRivals();
        var open = Rivals.Where(v => !v.Allied).OrderBy(v => v.Id).ToList();
        if (open.Count == 0) return;
        double p = (0.03 + 0.05 * RulerT(Trait.Aggression) + B(i.Raiding, 0.04) + B(i.Nationalism, 0.04) + B(i.Colonialism, 0.02)
                    + B(i.StandingArmy, 0.01) - B(i.Parley, 0.025)) * (0.4 + 1.2 * open.Max(v => v.Hostility));
        if (i.NuclearArms) p *= 0.35;
        p *= DreadFactor;
        p *= ThreatMul;
        if (Rnd() >= p) return;

        bool offensive = Rnd() < 0.2 + 0.5 * RulerT(Trait.Ambition) + B(i.Raiding, 0.15);
        // 치러 갈 때는 밉고 약한 쪽을, 맞을 때는 우리를 미워하는 강한 쪽을 만난다.
        var foeV = offensive
            ? open.OrderByDescending(v => v.Hostility - 0.5 * v.Strength + (Coveted(v) != Resource.None ? 0.3 : 0)).ThenBy(v => v.Id).First()
            : open.Where(v => Year >= v.TreatyUntil).OrderByDescending(v => v.Hostility * v.Strength).ThenBy(v => v.Id).FirstOrDefault();
        if (foeV == null) return; // 모두와 화친 중이다
        string foe = foeV.Name;
        // 그 나라 땅에서 나는 것이 탐나서 치는 전쟁도 있다.
        var want = offensive ? Coveted(foeV) : Resource.None;
        if (offensive && Year < foeV.TreatyUntil)
        {
            foeV.TreatyUntil = 0;
            r.LegitShock += 0.05;
            foreach (var v in Rivals) v.Hostility = Clamp01(v.Hostility + 0.1);
            Say($"{Josa.Iga(RulerTitle)} {Josa.Wa(foe)} 맺은 화친을 제 손으로 깼다. 다른 이웃들이 그것을 지켜보았다.");
        }
        if (!offensive && i.Tribute && r.Treasury > 0.5 && Rnd() < 0.7)
        {
            r.Treasury -= 1;
            r.LegitShock += 0.04;
            foeV.Strength += 0.03;
            foeV.Hostility = Clamp01(foeV.Hostility - 0.1);
            Say($"{Josa.Iga(foe)} 국경에 군대를 모으자 {Josa.Eun(RulerTitle)} 공물을 바쳐 물렸다.");
            return;
        }
        foeV.Trade = false;
        foeV.Hostility = Clamp01(foeV.Hostility + 0.2);

        bool allied = Rivals.Any(v => v.Allied);
        double s = ArmyStrength() * (0.5 + r.Cohesion) * (offensive ? 0.9 : 1.15) + B(allied && !offensive, 0.15)
                   + (offensive ? 0 : BE(Building.Castle, 0.03, 4));
        double e = foeV.Strength * (0.6 + 0.5 * Rnd()) + (offensive ? 0.025 * r.Territory : 0); // 멀리 뻗을수록 이기기 어렵다
        bool won = Rnd() < s / (s + e);
        bool total = i.Conscription && r.Era >= Era.Industrial;
        double dead = r.Pop * (total ? 0.04 + 0.06 * Rnd() : 0.005 + 0.02 * Rnd()) * (won ? 0.7 : 1.3);
        r.Pop -= dead;
        r.Treasury -= (total ? 1.5 : 0.5) + 0.08 * (int)r.Stage;
        if (total) r.Inequality *= 0.85; // 총력전은 재산도 태운다
        var fallen = CourtDeaths(Warriors(), won ? 0.05 : 0.12, "전쟁");
        _crisisThisYear = true;

        string head = total ? "【총력전】" : "【전쟁】";
        string start = !offensive ? $"{Josa.Iga(foe)} 쳐들어왔다"
            : want != Resource.None ? $"{Josa.Iga(RulerTitle)} {Josa.Eul(ResourceCatalog.Name(want))} 노리고 {Josa.Eul(foe)} 쳤다"
            : $"{Josa.Iga(RulerTitle)} {Josa.Eul(foe)} 쳤다";
        string tail;
        if (won)
        {
            var land = offensive ? GainLand(foeV, want) : null;
            r.Treasury += 0.8;
            r.Treasury += 0.3 * L(Course.Terror); // 공포의 길: 이긴 땅을 턴다
            Feared(offensive ? 2 : 1);
            r.LegitShock -= 0.1;
            r.Cohesion = Clamp01(r.Cohesion + 0.03);
            foeV.Strength -= offensive ? 0.2 : 0.1;
            tail = !offensive ? "막아냈다"
                : land == null ? $"이겨서 땅을 넓혔다 (영토 {r.Territory})"
                : $"이겨서 {Josa.Eul(land.Name)} 차지했다 (영토 {r.Territory})"
                  + (land.Deposit != Resource.None && Revealed(land.Deposit) ? $". 그 땅에서는 {Josa.Iga(ResourceCatalog.Name(land.Deposit))} 난다" : "");
        }
        else
        {
            foeV.Strength += 0.1;
            bool lostLand = r.Territory > 1 && (!offensive || Rnd() < 0.3);
            var land = lostLand ? LoseLand(foeV) : null;
            r.LegitShock += 0.15;
            r.Unrest = Clamp01(r.Unrest + 0.08);
            tail = !lostLand ? "졌다" : land == null ? $"지고 땅을 잃었다 (영토 {r.Territory})" : $"지고 {Josa.Eul(land.Name)} 잃었다 (영토 {r.Territory})";
        }
        Say($"{head} {start}. {tail}. 백성 {Num(dead)}명이 죽었다. 지배 가문에서 쓰러진 이: {NamesOr(fallen.Select(P), "없음")}.");
        if (won && offensive && foeV.Strength < 0.3)
        {
            Rivals.Remove(foeV);
            r.Treasury += 1;
            r.Atrocities++;
            Say($"【정복】 {Josa.Iga(foe)} 지도에서 사라졌다. 그 도성의 곳간은 비워졌고, 사람들은 팔리거나 흩어졌다.");
        }
        if (!won)
        {
            ReleaseSuppressed("패전의 소식에");
            // 땅이 얼마 남지 않은 나라는 한 번의 패전으로 끝날 수 있다.
            if (!offensive && r.Territory <= 3 && (r.Cohesion < 0.5 || foeV.Strength > 1.0) && Rnd() < (r.Territory == 1 ? 0.5 : 0.25))
                Collapse($"{foe}에게 도성이 함락되었다", kind: "함락");
        }
    }

    void AllyWar(Rival ally)
    {
        var fallen = CourtDeaths(Warriors().Where(m => m != Chief), 0.06, "전쟁");
        Realm.Treasury -= 0.3;
        Say($"사돈인 {Josa.Iga(ally.Name)} 제 전쟁에 원군을 청했다. 군사를 보냈고, 돌아오지 못한 이: {NamesOr(fallen.Select(P), "없음")}.");
    }

    internal void Coup()
    {
        if (Chief is not { Alive: true } c) return;
        var general = Living.Where(m => m != c && m.IsMale && m.Master == null && m.Age(Tick) is >= 20 and <= 55)
            .OrderByDescending(m => m.T(Trait.Ambition) * (1 + m.Status)).ThenBy(m => m.Id).FirstOrDefault();
        if (general == null || general.T(Trait.Ambition) < 0.5) return;
        _crisisThisYear = true;
        if (Rnd() < 0.5 + 0.3 * (general.T(Trait.Aggression) - c.T(Trait.Aggression)))
        {
            Say($"【정변】 급료가 밀린 군대를 등에 업고 {Iga(general)} {RulerTitle} {Eul(c)} 몰아냈다.");
            general.Status = Math.Max(general.Status, c.Status) + 1;
            Chief = general;
            if (Rnd() < 0.6) Kill(general, c, "정변");
            else { c.Status -= 2; AddGrudge(c, general, 0.9, "자리를 빼앗긴 일로"); }
        }
        else
        {
            Say($"【정변 실패】 {Iga(general)} 군사를 일으켰으나 꺾였다.");
            Kill(c, general, "반역", announce: false, selfDefense: true);
            Realm.Suppressed = Clamp01(Realm.Suppressed + 0.05);
        }
    }

    internal void NobleRevolt()
    {
        var r = Realm;
        double dead = r.Pop * (0.01 + 0.03 * Rnd());
        r.Pop -= dead;
        _crisisThisYear = true;
        var fallen = CourtDeaths(Warriors().Where(m => m != Chief), 0.08, "전쟁");
        bool lost = r.Territory > 1 && Rnd() < 0.4;
        var land = lost ? LoseLand(null) : null;
        r.LegitShock += 0.08;
        Say($"【귀족 반란】 제 땅에서 왕 노릇을 하던 영주들이 등을 돌렸다. 백성 {Num(dead)}명이 죽었다."
            + (lost ? $" {(land == null ? "한 지방이" : Josa.Iga(land.Name))} 떨어져 나갔다 (영토 {r.Territory})." : "")
            + $" 쓰러진 이: {NamesOr(fallen.Select(P), "없음")}.");
        // 묶어 둘 힘이 없으면 한 지방으로 끝나지 않는다.
        if (r.Stage >= Stage.Kingdom && r.Cohesion < 0.4 && Rnd() < 0.2) Collapse("영주들이 저마다 제 땅의 왕을 칭했다", kind: "분열");
    }

    void ReligiousWar()
    {
        var r = Realm;
        double dead = r.Pop * (0.04 + 0.08 * Rnd());
        r.Pop -= dead;
        r.Cohesion = Clamp01(r.Cohesion - 0.1);
        r.LegitShock += 0.1;
        r.Heresy *= 0.6;
        _crisisThisYear = true;
        var fallen = CourtDeaths(Court(), 0.05, "전쟁");
        Say($"【종교 전쟁】 {(Inst.Printing ? "인쇄된 말이" : "장삿길로 들어온 낯선 믿음이")} 믿음을 둘로 갈랐다. 이웃이 이웃을 죽였고 백성 {Num(dead)}명이 죽었다."
            + (fallen.Count > 0 ? $" 지배 가문에서 죽은 이: {NamesOr(fallen.Select(P), "없음")}." : ""));
    }

    void ColonyEvents()
    {
        var r = Realm;
        double x = Rnd();
        if (r.Era == Era.Modern && x < 0.06)
        {
            Inst.Colonialism = false;
            r.Treasury -= 1;
            r.LegitShock += 0.05;
            Say("【독립】 식민지들이 차례로 깃발을 내리게 했다. 배에 실려 오던 부가 끊겼다.");
        }
        else if (x < 0.05)
        {
            r.Treasury += 1;
            r.Atrocities++;
            Say("【식민지 수탈】 바다 건너에서 은과 사람을 실은 배가 들어왔다. 그 땅의 마을들이 어떻게 비었는지는 장부에 적히지 않았다.");
        }
        else if (x < 0.09)
        {
            r.Treasury -= 0.8;
            var fallen = CourtDeaths(Warriors().Where(m => m != Chief), 0.04, "전쟁");
            Say($"【식민지 봉기】 바다 건너 땅이 들고일어났다. 진압에 돈과 사람이 들었다. 돌아오지 못한 이: {NamesOr(fallen.Select(P), "없음")}.");
        }
    }

    void FactoryEvents()
    {
        var r = Realm;
        if (Inst.LaborLaws || Rnd() >= 0.1) return;
        switch (_rng.Next(3))
        {
            case 0:
                r.Unrest = Clamp01(r.Unrest + 0.05);
                Say("【공장】 방직 공장에서 불이 났다. 문은 밖에서 잠겨 있었고, 죽은 이의 절반은 열두 살이 못 되었다.");
                break;
            case 1:
                double dead = r.Pop * (0.01 + 0.02 * Rnd());
                r.Pop -= dead;
                Say($"【빈민가】 공장 둘레의 판잣집에 돌림병이 돌아 {Num(dead)}명이 죽었다. 물은 한 우물에서 길었다.");
                break;
            default:
                r.Unrest = Clamp01(r.Unrest + 0.08);
                r.Suppressed = Clamp01(r.Suppressed + 0.05);
                Say("【파업】 품삯을 올려 달라며 멈춘 공장에 군대가 들어갔다. 광장에 주검이 남았다.");
                break;
        }
    }

    /// <summary>민족의 이름으로 '우리'를 좁히면 누군가는 밖에 놓인다.</summary>
    internal void Persecution(bool force = false)
    {
        var r = Realm;
        var i = Inst;
        double p = (_crisisThisYear || r.Unrest > 0.5 || r.Treasury < 0 ? 0.04 : 0.008)
                   * (i.Propaganda ? 2 : 1) * (i.Tolerance ? 0.3 : 1) * (i.Suffrage && !i.Propaganda ? 0.4 : 1)
                   * (0.5 + CourtAvg(Trait.KinBias));
        if (!force && Rnd() >= p) return;
        double dead = r.Pop * (0.02 + 0.06 * Rnd());
        r.Pop -= dead;
        r.Atrocities++;
        r.Knowledge *= 0.98;
        r.Cohesion = Clamp01(r.Cohesion + 0.04);
        r.Suppressed = Clamp01(r.Suppressed + r.Unrest * 0.5);
        r.Unrest *= 0.6;
        var killed = CourtDeaths(Living.Where(x => x.Outsider && x != Chief).ToList(), 0.7, "학살");
        Say($"【학살】 나라가 어려워지자 '우리가 아닌 자들'이 그 까닭으로 지목되었다. 이웃이던 사람들 {Num(dead)}명이 끌려가 돌아오지 않았다."
            + (killed.Count > 0 ? $" 바깥에서 들어온 핏줄이라는 이유로 지배 가문에서도 죽은 이: {NamesOr(killed.Select(P), "없음")}." : "")
            + " 남은 이들은 더 굳게 뭉쳤고, 아무도 그 일을 입에 올리지 않았다.");
    }

    void Purge()
    {
        if (Chief is not { Alive: true } c) return;
        Person? target = null;
        double g = 0.3;
        foreach (var (id, v) in c.Grudge)
        {
            var t = _byId[id];
            if (t.Alive && v > g) { g = v; target = t; }
        }
        target ??= Court().Where(x => x != c && x.IsMale).OrderByDescending(x => x.Status).ThenBy(x => x.Id).FirstOrDefault();
        if (target == null) return;
        string why = c.GrudgeWhy.GetValueOrDefault(target.Id, "자리를 넘볼 만하다는 이유로");
        string name = P(target);
        foreach (var k in Living.Where(k => k != c && Related(k, target) >= 0.25).ToList())
            AddGrudge(k, c, 0.6, $"{Josa.Eul(target.Name)} 숙청한 일로");
        Die(target, "숙청");
        Realm.Suppressed = Clamp01(Realm.Suppressed + 0.04);
        Say($"【숙청】 {Josa.Iga(name)} 한밤에 끌려가 돌아오지 않았다. 죄목은 반역이었으나 {RulerTitle} {c.Name}의 속은 달랐다 — {why}.");
    }

    void ModernEvents()
    {
        var r = Realm;
        var i = Inst;
        if ((i.Coinage || i.Globalization) && r.Inequality > 0.45 && Rnd() < 0.03)
        {
            r.Treasury -= 2;
            r.Unrest = Clamp01(r.Unrest + 0.2);
            _crisisThisYear = true;
            Say("【공황】 빚으로 쌓아 올린 값이 하루아침에 꺼졌다. 일자리를 잃은 이들이 거리에 줄을 섰다.");
        }
        if (r.Pollution > 0.5 && Rnd() < 0.08)
        {
            double dead = r.Pop * (0.01 + 0.03 * Rnd());
            r.Pop -= dead;
            r.Unrest = Clamp01(r.Unrest + 0.06);
            _crisisThisYear = true;
            Say($"【기후 재난】 더워진 하늘이 가뭄과 큰물을 번갈아 보냈다. {Num(dead)}명이 죽고 더 많은 이가 살던 곳을 떠났다.");
        }
        if (i.NuclearArms && Rnd() < 0.0004 * (1 + 2 * RulerT(Trait.Aggression)) * (_crisisThisYear ? 3 : 1))
        {
            double dead = r.Pop * 0.8;
            r.Pop -= dead;
            CourtDeaths(Living.ToList(), 0.7, "핵전쟁");
            r.DarkAgeUntil = Year + 80;
            Collapse($"단추가 눌렸다. 하루 만에 {Num(dead)}명이 죽었다", toChiefdom: true, kind: "핵전쟁");
        }
    }

    // ───────────────────────── 봉기와 혁명 ─────────────────────────

    internal void Uprising(Estate? lead = null)
    {
        var r = Realm;
        var i = Inst;
        // 누가 일어서는가: 수가 많고 분노가 큰 계층.
        if (lead == null)
        {
            double wc = r.Share[1] * (0.1 + EstateAnger(Estate.Commoners)), wb = r.Share[2] * (0.1 + EstateAnger(Estate.Bound)) * 1.5,
                   ww = r.Share[3] * (0.1 + EstateAnger(Estate.Workers)) * 1.5;
            double x = Rnd() * (wc + wb + ww);
            lead = x < wb ? Estate.Bound : x < wb + ww ? Estate.Workers : Estate.Commoners;
        }
        string kind = lead == Estate.Bound ? (i.Slavery && (!i.Serfdom || Rnd() < 0.4) ? "노예 반란" : "농민 반란")
            : r.Era >= Era.Industrial ? "혁명"
            : i.Serfdom ? "농민 반란" : "민란";
        _crisisThisYear = true;
        r.Unrest = Clamp01(r.Unrest + r.Suppressed * 0.8);
        r.Suppressed = 0;

        double rebels = r.Unrest * (1 + r.Inequality) * (i.Absolutism ? 1.2 : 1);
        double state = ArmyStrength() * (0.4 + r.Legitimacy) * 5.0;
        if (Rnd() >= rebels / (rebels + state))
        {
            double dead = r.Pop * (0.01 + 0.03 * Rnd());
            r.Pop -= dead;
            r.Unrest = 0.35;
            r.Suppressed = 0.2;
            r.LegitShock += 0.05;
            Say($"【{kind}】 들고일어난 이들을 군대가 짓밟았다. {Num(dead)}명이 죽었고, 살아남은 이들은 입을 닫았다.");
            return;
        }

        // 성공: 위가 뒤집힌다.
        double loss = r.Pop * (0.02 + 0.03 * Rnd());
        r.Pop -= loss;
        var toppled = new List<string>();
        void Topple(InstitutionDef d) { if (d.Get(i)) { d.Set(i, false); toppled.Add(d.Name); } }
        if (kind == "노예 반란") Topple(Catalog.Find("slavery")!);
        else
        {
            foreach (var key in new[] { "serfdom", "absolutism", "inquisition", "feudalism" }) Topple(Catalog.Find(key)!);
            if (r.Era >= Era.Industrial) Topple(Catalog.Find("slavery")!);
        }

        var executed = new List<string>();
        if (Chief is { Alive: true } c)
        {
            executed.Add(P(c));
            Die(c, "처형");
        }
        if (kind == "혁명")
            foreach (var x in Court().Where(x => x.IsMale).OrderByDescending(x => x.Status + x.Food * 0.2).ThenBy(x => x.Id).Take(3).ToList())
            {
                executed.Add(P(x));
                Die(x, "처형");
            }

        r.Inequality *= 0.6;
        r.Corruption *= 0.6;
        r.Legitimacy = 0.5;
        r.LegitShock = 0;
        r.Unrest = 0.2;
        r.Cohesion = Clamp01(r.Cohesion - 0.08);
        for (int k = 1; k < 4; k++) r.Grievance[k] = 0;

        string after = "";
        // 봉기를 이끈 이름 없는 사람이 빈자리에 앉기도 한다. 백성에게서 솟은 새 왕조다.
        if (Rnd() < 0.6)
        {
            var leader = RiseFromCommons(26, 8);
            Chief = leader;
            _rulerId = leader.Id;
            r.DynastyClan = leader.ClanId;
            r.FoundedYear = Year;
            foreach (var k in Court().Where(k => k != leader && k.T(Trait.KinBias) > 0.6).OrderByDescending(k => k.Status).ThenBy(k => k.Id).Take(5))
                AddGrudge(k, leader, 0.5, "미천한 자가 자리를 차지한 일로");
            after = $" 봉기를 이끈 {(kind == "노예 반란" ? "종이었던 " : "")}{Josa.Iga(leader.Name)} 빈자리에 앉았고, 사람들은 그 집안을 {ClanName(leader.ClanId)}족이라 불렀다.";
        }
        if (kind == "혁명")
        {
            if (!i.Suffrage && Rnd() < 0.5)
            {
                i.Suffrage = true;
                after += " 거리의 사람들은 제 손으로 다스릴 이를 뽑을 권리를 얻어 냈다.";
            }
            else if (r.Era == Era.Modern && Rnd() < 0.6)
            {
                i.Propaganda = i.Surveillance = true;
                after += " 그러나 혁명은 새 주인을 낳았다. 광장에는 초상화가 걸렸고 이웃이 이웃을 지켜보기 시작했다.";
            }
        }
        Say($"【{kind}】 성공했다. {Num(loss)}명이 죽었다. 처형된 이: {NamesOr(executed, "없음")}."
            + (toppled.Count > 0 ? $" 무너진 제도: {string.Join(", ", toppled)}." : "") + after);

        if (r.Stage >= Stage.CityState && r.Cohesion < 0.3 && Rnd() < 0.3) Collapse("뒤집힌 나라를 다시 묶을 힘이 아무에게도 없었다", kind: "봉기");
    }

    // ───────────────────────── 몰락 ─────────────────────────

    void CheckCollapse()
    {
        var r = Realm;
        int over = Math.Max(0, r.Territory - AdminCapacity());
        // 넘치게 넓은 나라는 결속이 닳아 무너지기 전에 변방부터 떨어져 나간다.
        if (over >= 1 && (r.Legitimacy < 0.6 || r.Cohesion < 0.35) && Rnd() < 0.08 * over * CrisisMul) Collapse("넓어진 땅의 끝에서부터 명령이 닿지 않았다", kind: "과한 영토");
        else if (r.Cohesion < 0.3 && r.Unrest > 0.45 && Rnd() < 0.04 * CrisisMul) Collapse("서로를 한 나라 사람이라 여기는 이가 더는 없었다", kind: "결속");
        else if ((r.Treasury <= -5 || _debtYears >= 8) && Rnd() < 0.1 * CrisisMul) Collapse("빚을 갚을 길이 없었다. 군대에 줄 돈이 끊기자 국경이 먼저 사라졌다", kind: "파산");
        else if (Inst.Feudalism && EstateAnger(Estate.Nobles) > 0.3 && r.Legitimacy < 0.55 && Rnd() < 0.05 * CrisisMul)
            Collapse("영주들이 저마다 제 땅의 왕을 칭했다", kind: "분열");
    }

    /// <summary>몰락 뒤 다시 무너지지 않고 버티는 햇수.</summary>
    public const int CollapseGrace = 40;

    /// <summary>몰락 뒤 CollapseGrace년이 지나지 않았나(첫 나라는 해당하지 않는다).</summary>
    public bool InCollapseGrace => Realm.Collapses > 0 && Year - Realm.StateFounded < CollapseGrace;

    /// <summary>나라가 무너진다. 한두 단계 내려앉고, 사람과 땅과 지식을 잃는다. 새 결속은 폐허에서 다시 자란다.</summary>
    internal void Collapse(string reason, bool toChiefdom = false, string kind = "기타")
    {
        var r = Realm;
        if (r.Stage <= Stage.Chiefdom) return;
        // 무너졌다 다시 선 지 얼마 안 된 나라는 아직 새 결속으로 버틴다. 도성이 함락되거나 핵이 떨어진 것은 막지 못한다.
        if (InCollapseGrace && kind is not ("함락" or "핵전쟁"))
        {
            r.LegitShock += 0.05;
            r.Cohesion = Clamp01(r.Cohesion - 0.05);
            Say($"【위기】 {Josa.Iga(StageName)} 크게 흔들렸다 — {reason}. 그러나 폐허에서 다시 선 지 {Year - r.StateFounded}년, 아직은 서로를 붙든 손이 놓이지 않았다.");
            return;
        }
        r.Falls.Add($"{Year}|{kind}");
        _debtYears = 0;
        if (toChiefdom) Rivals.Clear();
        for (int k = 0; k < 4; k++) r.Grievance[k] = 0;
        string was = StageName;
        r.Stage = toChiefdom ? Stage.Chiefdom : (Stage)Math.Max(1, (int)r.Stage - (r.Stage == Stage.Empire ? 2 : 1));
        double lost = r.Pop * (0.3 + 0.2 * Rnd());
        r.Pop -= lost;
        r.Territory = Math.Max(1, r.Territory / 3);
        foreach (var a in Armies.Where(a => a.Owner == 0).ToList()) { Armies.Remove(a); r.Pop += a.Men * 0.5; } // 군단은 흩어진다
        if (Map.Provinces.Any(p => p.Buildings.Count > 0)) RuinBuildings(0.35);
        SyncMap(); // 떨어져 나간 땅은 주인 없는 땅이 된다
        r.Knowledge *= 0.92;
        r.DarkAgeUntil = Math.Max(r.DarkAgeUntil, Year + 15);
        r.Treasury = 0;
        r.Inequality *= 0.6;
        r.Corruption *= 0.4;
        r.Unrest = 0.25;
        r.Suppressed = 0;
        r.Legitimacy = 0.45;
        r.LegitShock = 0;
        r.Cohesion = 0.7;
        r.StateFounded = Year;
        r.RebuildUntil = Year + 30;
        r.Collapses++;
        _crisisThisYear = true;
        Say($"【몰락】 {Josa.Iga(was)} 무너졌다 — {reason}. {Num(lost)}명이 죽거나 흩어졌고, 남은 것은 {StageName}다 (백성 {Num(r.Pop)}명). 글을 읽는 이가 줄어 한동안 지식이 더디게 쌓일 것이다.");
    }

    string RealmSummary()
    {
        var r = Realm;
        if (r.Stage == Stage.Tribe) return $" | {EraName}·{StageName}, 지식 {r.Knowledge:F0}";
        return $" | {EraName}·{StageName}: 백성 {Num(r.Pop)}, 영토 {r.Territory}, 곳간 {r.Treasury:F1}, 지식 {r.Knowledge:F0}, " +
               $"불만 {r.Unrest:P0}(억눌림 {r.Suppressed:P0}), 통치자 지지 {r.Legitimacy:P0}, 불평등 {r.Inequality:P0}, 부패 {r.Corruption:P0}, 단합 {r.Cohesion:P0}";
    }
}
