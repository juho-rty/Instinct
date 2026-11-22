namespace Instinct.Sim;

public sealed class Config
{
    /// <summary>일꾼 한 명이 한 철에 얻는 기본 식량.</summary>
    public double BaseYield = 2.4;
    /// <summary>땅의 수용력. 인구가 이 값에 가까워질수록 1인당 수확이 준다.</summary>
    public double Capacity = 80;
    /// <summary>피값(살인 배상) 제도에서 치러야 하는 식량.</summary>
    public double BloodPrice = 4;
    /// <summary>1인당 비축이 이만큼이면 마음 놓고 아이를 낳는다.</summary>
    public double ComfortStores = 1.5;
    /// <summary>한 철에 남는 식량의 비율(나머지는 썩는다).</summary>
    public double Keep = 0.94;
    /// <summary>겨울 한 철은 추워서 썩는 몫이 이만큼으로 줄어든다(가을에 모은 것이 겨울을 넘긴다).</summary>
    public double WinterRot = 0.35;
}

/// <summary>플레이어가 고르는 제도. 본능을 없애지 못하고 방향만 바꾼다.</summary>
public sealed class Institutions
{
    public bool PrivateProperty;
    public bool Monogamy;
    public bool BloodMoney;
    /// <summary>약탈: 굶주리거나 복수심이 차면 이웃 무리를 친다.</summary>
    public bool Raiding;
    /// <summary>노예제: 사로잡은 자와 피값을 못 치른 자를 종으로 부린다.</summary>
    public bool Slavery;
    /// <summary>화친: 우두머리가 선물을 들고 가 싸움을 멈춘다. 피붙이를 잃은 자들은 그것을 배신으로 여긴다.</summary>
    public bool Parley;
    /// <summary>공물: 해마다 식량을 바쳐 습격을 산다. 요구는 해마다 는다.</summary>
    public bool Tribute;
    /// <summary>혼인 동맹: 딸을 보내고 며느리를 받아 사돈이 된다.</summary>
    public bool MarriageAlliance;
    /// <summary>식인 풍습: 굶주림에 죽은 이와 쓰러진 적을 먹는다. 이웃이 두려워하고 미워한다.</summary>
    public bool Cannibalism;

    // 아래는 나라 단위(Realm)에서 작동한다. 얻는 것과 치르는 값은 Catalog에 적혀 있다.
    public bool Agriculture;
    public bool Writing, Taxation, LawCode, StandingArmy, StateReligion, Coinage;
    public bool Feudalism, Serfdom, Guilds, Inquisition;
    public bool Printing, Colonialism, Absolutism, Parliament, Tolerance;
    public bool Factories, LaborLaws, PublicSchooling, Nationalism, Conscription, Suffrage;
    public bool Welfare, Propaganda, Surveillance, NuclearArms, Globalization, GreenLaw;
}

public enum LogKind { Major, Minor, Summary }
public readonly record struct LogEntry(int Tick, LogKind Kind, string Text);

/// <summary>한 틱 = 한 철. 엔진과 무관한 순수 로직이라 화면 없이 수백 년을 돌릴 수 있다.</summary>
public sealed partial class Simulation
{
    static readonly double[] SeasonYield = { 1.0, 1.3, 1.5, 0.4 };
    public static readonly string[] SeasonNames = { "봄", "여름", "가을", "겨울" };

    public readonly Config Cfg;
    public readonly Institutions Inst = new();
    public readonly List<Person> People = new();
    public readonly List<LogEntry> Log = new();
    public readonly Dictionary<string, int> DeathsByCause = new();
    public int Tick { get; internal set; }
    public double CommonStore = 20;
    public Person? Chief;
    public int PeakPopulation;
    public int TotalBirths;

    readonly Random _rng;
    readonly Names _names;
    readonly Dictionary<int, Person> _byId = new();
    readonly Dictionary<int, string> _clanNames = new();
    readonly List<Person> _murdered = new();
    readonly Dictionary<string, int> _yearDeaths = new();
    int _yearBirths;
    int _nextId = 1;
    int _nextClan = 1;
    internal double _yearFactor = 1;
    bool _famineLogged;
    bool _favoritismLogged;
    bool _sharingLogged;

    public IEnumerable<Person> Living => People.Where(p => p.Alive);
    public int Year => Tick / 4 + 1;
    public int Season => Tick % 4;

    public Simulation(int seed, Config? cfg = null)
    {
        Cfg = cfg ?? new Config();
        Seed = seed;
        _rng = new Random(seed);
        _names = new Names(_rng);
        Map = WorldMap.Generate(seed); // 씨앗의 해시로만 만든다: _rng를 건드리지 않는다
        Map.Reveal(CapitalLand.X, CapitalLand.Y, 7); // 처음에는 터 둘레만 보인다
        UpdateSight();

        // 세 씨족, 씨족마다 남자 둘 여자 둘. 여자는 이웃 씨족 남자와 짝을 이룬 채 시작한다.
        var clans = new[] { NewClan(), NewClan(), NewClan() };
        var men = new List<Person>[3];
        var women = new List<Person>[3];
        for (int c = 0; c < 3; c++)
        {
            men[c] = new() { NewPerson(Sex.Male, 18 + _rng.Next(12), clans[c]), NewPerson(Sex.Male, 18 + _rng.Next(12), clans[c]) };
            women[c] = new() { NewPerson(Sex.Female, 16 + _rng.Next(10), clans[c]), NewPerson(Sex.Female, 16 + _rng.Next(10), clans[c]) };
        }
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < 2; i++)
                Wed(men[c][i], women[(c + 1) % 3][i]);
        // 처음부터 일을 나눠 둔다: 첫 화면에서 누가 무엇을 하는지 보인다.
        var first = Workers();
        foreach (var p in first) p.Job = BestFoodJob(first);

        Say($"세 씨족({string.Join(", ", clans.Select(ClanName))})의 열두 사람이 강가에 자리를 잡았다.");
    }

    // ───────────────────────── 기본 도구 ─────────────────────────

    double Rnd() => _rng.NextDouble();
    double Noise(double sd) => (Rnd() + Rnd() + Rnd() - 1.5) * 2 * sd;
    List<T> Shuffle<T>(IEnumerable<T> xs) => xs.OrderBy(_ => _rng.Next()).ToList();

    void Say(string text) => Log.Add(new LogEntry(Tick, LogKind.Major, text));
    void Minor(string text) => Log.Add(new LogEntry(Tick, LogKind.Minor, text));

    /// <summary>플레이어 개입이나 외부 설명을 연대기에 남긴다.</summary>
    public void Note(string text)
    {
        Say(text);
        Wrote("note", text);
    }

    public string ClanName(int clanId) => _clanNames[clanId];
    public string P(Person p) => p.Master != null ? $"종 {p.Name}" : $"{_clanNames[p.ClanId]}족 {p.Name}";
    string Iga(Person p) => Josa.Iga(P(p));
    string Eul(Person p) => Josa.Eul(P(p));
    string Eun(Person p) => Josa.Eun(P(p));
    string Wa(Person p) => Josa.Wa(P(p));
    int AgeOf(Person p) => (int)p.Age(Tick);

    int NewClan()
    {
        int id = _nextClan++;
        _clanNames[id] = _names.NextClan();
        return id;
    }

    internal Person NewPerson(Sex sex, double ageYears, int clan)
    {
        var p = new Person
        {
            Id = _nextId++,
            Name = _names.NextPerson(),
            Sex = sex,
            BirthTick = Tick - (int)(ageYears * 4),
            ClanId = clan,
            Skill = 0.7 + 0.6 * Rnd(),
        };
        for (int i = 0; i < p.Traits.Length; i++) p.Traits[i] = Rnd();
        People.Add(p);
        _byId[p.Id] = p;
        return p;
    }

    void Wed(Person man, Person woman)
    {
        woman.Husband = man;
        man.Wives.Add(woman);
    }

    void AddGrudge(Person holder, Person target, double amount, string why)
    {
        if (holder == target || amount <= 0) return;
        holder.Grudge.TryGetValue(target.Id, out var cur);
        holder.Grudge[target.Id] = Math.Min(3, cur + amount);
        // 이유는 가장 크게 맺힌 일을 기억한다.
        if (amount >= cur || !holder.GrudgeWhy.ContainsKey(target.Id)) holder.GrudgeWhy[target.Id] = why;
    }

    /// <summary>사람들이 믿는 혈연의 가까움. 편애와 복수의 크기를 정한다.</summary>
    public double Related(Person a, Person b)
    {
        if (a == b) return 1;
        if (a.Father == b || a.Mother == b || b.Father == a || b.Mother == a) return 0.5;
        if ((a.Mother != null && a.Mother == b.Mother) || (a.Father != null && a.Father == b.Father)) return 0.5;
        if (a.Husband == b || b.Husband == a) return 0.4;
        if (IsGrandparent(a, b) || IsGrandparent(b, a)) return 0.25;
        return a.ClanId == b.ClanId ? 0.15 : 0;
    }

    static bool IsGrandparent(Person elder, Person young) =>
        (young.Father != null && (young.Father.Father == elder || young.Father.Mother == elder)) ||
        (young.Mother != null && (young.Mother.Father == elder || young.Mother.Mother == elder));

    /// <summary>가구의 식량을 쥔 사람. 아이는 부모, 아내는 남편, 늙은이는 장성한 아들을 따른다.</summary>
    public Person Head(Person p)
    {
        if (p.Master is { Alive: true } master) return Head(master);
        double age = p.Age(Tick);
        if (age < 14)
        {
            if (p.Father is { Alive: true } f) return Head(f);
            if (p.Mother is { Alive: true } m) return Head(m);
            return p;
        }
        if (!p.IsMale && p.Husband is { Alive: true } h) return Head(h);
        if (age >= 60)
        {
            var son = p.Children.FirstOrDefault(c => c.Alive && c.IsMale && c.Master == null && c.Age(Tick) >= 14 && c.Age(Tick) < 60);
            if (son != null) return son;
        }
        return p;
    }

    double EffAggression(Person p) =>
        p.T(Trait.Aggression) + (p.IsMale && p.Age(Tick) >= 22 && p.Wives.Count == 0 ? 0.2 : 0);

    double Power(Person p)
    {
        double age = p.Age(Tick);
        double ageF = age is >= 15 and <= 45 ? 1 : 0.6;
        return Math.Max(0.05, p.Health) * (0.6 + p.T(Trait.Aggression)) * (p.IsMale ? 1.0 : 0.7) * ageF * (p.IsMale && HasBuilt(StructureKind.WarHut) ? 1.15 : 1) * (p.Job == Work.Guard ? 1.2 : 1) + Rnd() * 0.3;
    }

    // ───────────────────────── 한 철 ─────────────────────────

    public void Step()
    {
        if (!Living.Any()) return;
        _mute++; // 한 철이 도는 동안 나라가 스스로 한 일은 명령으로 적지 않는다
        if (Season == 0) YearStart();
        EnsureChief();
        if (Season == 0 && Tribal) Diplomacy();
        Labor();
        Produce();
        Feed();
        ThrallLife();
        if (Season <= 2 && Tribal) MaybeRaid();
        if (Season == 2 && Tribal) PayTribute();
        if (Season >= 2 && Tribal) EnemyRaids();
        if (Season <= 1) Courtship();
        Affairs();
        PaternityDoubts();
        if (Season == 0) StatusContest();
        EnvyTheft();
        Violence();
        Births();
        Mortality();
        Decay();
        NeighborDecay();
        Construction();
        ArmiesMove();
        ExpireDecisions();
        if (Season == 3) RealmYear();
        if (Season == 3) YearEnd();
        _mute--;
        Tick++;
    }

    void YearStart()
    {
        _famineLogged = false;
        _favoritismLogged = false;
        _sharingLogged = false;
        _yearBirths = 0;
        _yearDeaths.Clear();

        _yearFactor = 0.75 + 0.5 * Rnd();
        if (Rnd() < 0.08)
        {
            _yearFactor = 0.45;
            Say("가뭄이 들었다. 강이 마르고 짐승이 떠났다.");
        }

        if (Tribal) NeighborYear();
        int pop = Living.Count();
        if (pop > 0 && pop < Cfg.Capacity * 1.2 && Rnd() < 0.12) Arrivals();
    }

    void Arrivals()
    {
        int clan = NewClan();
        int n = 1 + _rng.Next(3);
        var locals = Living.Where(p => p.Age(Tick) >= 14).ToList();
        var comers = new List<Person>();
        for (int i = 0; i < n; i++)
        {
            var o = NewPerson(_rng.Next(2) == 0 ? Sex.Male : Sex.Female, 16 + _rng.Next(15), clan);
            o.Outsider = true;
            comers.Add(o);
        }
        Say($"낯선 이들이 찾아와 무리에 받아들여졌다: {string.Join(", ", comers.Select(P))}.");
        // 내집단 본능: 핏줄을 따지는 이들은 이방인을 처음부터 곱게 보지 않는다.
        foreach (var k in locals.Where(k => k.T(Trait.KinBias) > 0.65))
            foreach (var o in comers)
                AddGrudge(k, o, 0.2, "이방인이 못마땅해");
    }

    void EnsureChief()
    {
        if (Chief is { Alive: true }) return;
        var pool = Living.Where(p => p.Master == null && p.Age(Tick) >= 16).ToList();
        Person? next = null;
        // 나라가 서면 자리는 핏줄을 따라 내려간다 (선거가 생기기 전까지).
        if (Realm.Stage >= Stage.Chiefdom && !Inst.Suffrage && _rulerId >= 0 && _byId[_rulerId] is { Alive: false, DeathCause: not "처형" } prev)
        {
            var sons = pool.Where(p => p.IsMale && p.Father == prev).OrderBy(p => p.BirthTick).ThenBy(p => p.Id).ToList();
            next = sons.FirstOrDefault()
                   ?? pool.Where(p => p.IsMale && p.ClanId == prev.ClanId).OrderByDescending(p => p.Status).ThenBy(p => p.Id).FirstOrDefault();
            if (next != null)
            {
                Chief = next;
                next.Status = Math.Max(next.Status, 3) + 1;
                foreach (var bro in sons.Skip(1).Where(b => b.T(Trait.Ambition) > 0.6))
                    AddGrudge(bro, next, 0.5, "형이 자리를 물려받은 일로");
                Say($"{prev.Name}의 뒤를 이어 {Iga(next)} {RulerTitle}의 자리를 물려받았다.");
                return;
            }
        }
        next = pool.OrderByDescending(p => p.Status).ThenBy(p => p.Id).FirstOrDefault();
        if (next == null) { Chief = null; return; }
        Chief = next;
        next.Status += 1;
        Say(Realm.Stage == Stage.Tribe ? $"{Iga(next)} 무리의 우두머리가 되었다." : $"{Iga(next)} 새 {RulerTitle}의 자리에 올랐다.");
    }

    void Produce()
    {
        var living = Living.ToList();
        // 농경은 같은 땅에 더 많은 입을 붙이고, 나라가 커지면 백성이 지배 가문을 먹인다.
        double crowd = Crowd(living.Count);
        var counts = JobCounts(living);
        Person? top = null;
        double topYield = 0;

        foreach (var p in living)
        {
            double age = p.Age(Tick);
            if (age < 10) continue;
            double ageF = AgeF(age);
            double y = ExpectedFood(p, ageF, counts, crowd) * (0.85 + 0.3 * Rnd());
            if (p.Job is not (Work.None or Work.Gather or Work.Hunt or Work.Fish or Work.Farm))
            {
                // 먹을 것을 내지 않는 일: 나무·돌을 쌓는다(건설·정찰·경계는 Labor와 싸움에서 셈한다).
                Gather(p, p.Job, p.Skill * ageF * (0.5 + 0.5 * Math.Max(0, p.Health)), counts);
                continue;
            }
            if (p.Job == Work.Hunt && age >= 14 && Rnd() < 0.012)
            {
                p.Health = Math.Max(0.05, p.Health - 0.3);
                Minor($"{Iga(p)} 사냥을 나갔다가 짐승에게 다쳤다.");
            }
            if (p.Master != null)
            {
                // 종이 얻은 것은 주인의 것이다. 공동 분배 사회에서도 그렇다.
                Head(p).Food += y * 0.8;
                continue;
            }
            if (y > topYield) { topYield = y; top = p; }

            if (Inst.PrivateProperty)
            {
                Head(p).Food += y;
                continue;
            }

            double hidden = p.T(Trait.Greed) > 0.6 ? y * 0.35 * p.T(Trait.Greed) : 0;
            Head(p).Food += hidden;
            CommonStore += y - hidden;
            if (hidden > 0 && age >= 14 && Rnd() < 0.06)
            {
                p.Status -= 0.5;
                if (Chief != null) AddGrudge(Chief, p, 0.3, "식량을 빼돌린 일로");
                // 처음 들켰을 때만 큰 사건이다. 상습범은 다들 그러려니 한다.
                var text = $"{Iga(p)} 잡은 것을 공동 창고에 내놓지 않고 빼돌리다 들켰다.";
                if (p.CaughtHoarding) Minor(text); else Say(text);
                p.CaughtHoarding = true;
            }
        }
        if (top != null) top.Status += 0.1;
    }

    static double Need(Person p, int tick) => p.Age(tick) < 12 ? 0.5 : 1.0;

    int FeedPriority(Person p)
    {
        if (p.Master != null) return 4;
        double age = p.Age(Tick);
        if (age is >= 14 and <= 55) return 0;
        if (age is >= 5 and < 14) return 1;
        return age < 5 ? 2 : 3;
    }

    internal void Feed()
    {
        var living = Living.ToList();
        var unfed = new HashSet<Person>(living);

        // 1) 공동 창고. 모자라면 우두머리가 나눈다 — 제 핏줄부터.
        if (!Inst.PrivateProperty)
        {
            // 종은 공동 창고의 몫이 없다. 주인이 먹인다.
            var commoners = living.Where(p => p.Master == null).ToList();
            double total = commoners.Sum(p => Need(p, Tick));
            if (CommonStore >= total)
            {
                CommonStore -= total;
                unfed.ExceptWith(commoners);
            }
            else if (Chief is { } c)
            {
                double Weight(Person p)
                {
                    double age = p.Age(Tick);
                    return 1 + 2 * c.T(Trait.KinBias) * Related(c, p) + 0.05 * p.Status
                           + (age is >= 14 and <= 55 ? 0.3 : 0) - (p.Outsider ? 0.5 : 0);
                }
                bool kinFed = false, othersStarved = false;
                foreach (var p in commoners.OrderByDescending(Weight).ThenBy(p => p.Id))
                {
                    double n = Need(p, Tick);
                    if (CommonStore >= n)
                    {
                        CommonStore -= n;
                        unfed.Remove(p);
                        if (p != c && Related(c, p) >= 0.25) kinFed = true;
                    }
                    else if (Related(c, p) < 0.15) othersStarved = true;
                }
                if (kinFed && othersStarved && c.T(Trait.KinBias) > 0.5)
                {
                    if (!_favoritismLogged)
                    {
                        _favoritismLogged = true;
                        Say($"우두머리 {Eun(c)} 모자란 식량을 제 핏줄에게 먼저 돌렸다. 굶은 이들이 그것을 보았다.");
                    }
                    foreach (var p in unfed.Where(p => p.Age(Tick) >= 14 && Related(c, p) < 0.15))
                        AddGrudge(p, c, 0.25 * c.T(Trait.KinBias), "굶을 때 외면당한 일로");
                }
            }
        }

        // 2) 가구가 따로 쥔 식량. 일꾼, 큰 아이, 젖먹이, 늙은이 순으로 먹인다.
        void FeedHousehold(Person head)
        {
            foreach (var p in unfed.Where(p => Head(p) == head).OrderBy(FeedPriority).ThenBy(p => p.Id).ToList())
            {
                double n = Need(p, Tick);
                if (head.Food >= n) { head.Food -= n; unfed.Remove(p); }
            }
        }
        foreach (var head in unfed.Select(Head).Distinct().OrderBy(h => h.Id).ToList())
            FeedHousehold(head);

        // 2b) 남이 굶는데 제 곳간이 넘치는 집이 있으면 우두머리가 내놓게 한다(집안 몫 세 철치는 남긴다).
        //     사유재산 아래에서는 절반만 거둔다. 억지로 내놓은 욕심 많은 가장은 우두머리를 원망한다.
        if (Chief is { Alive: true } chief && unfed.Any(u => u.Master == null))
        {
            double shared = 0;
            foreach (var h in living.Where(h => h.Alive && h.Master == null && Head(h) == h && h.Food > 3)
                         .OrderByDescending(h => h.Food).ThenBy(h => h.Id).ToList())
            {
                double keep = 3 * living.Where(p => p.Alive && Head(p) == h).Sum(p => Need(p, Tick));
                double spare = (h.Food - keep) * (Inst.PrivateProperty ? 0.5 : 1);
                if (spare < 0.5) continue;
                double given = 0;
                foreach (var u in unfed.Where(u => u.Master == null && Head(u) != h).OrderBy(FeedPriority).ThenBy(u => u.Id).ToList())
                {
                    double n = Need(u, Tick);
                    if (spare - given < n) break;
                    given += n;
                    unfed.Remove(u);
                }
                if (given <= 0) continue;
                h.Food -= given;
                shared += given;
                if (h != chief && h.T(Trait.Greed) > 0.5)
                    AddGrudge(h, chief, 0.1 + 0.2 * h.T(Trait.Greed), "곳간을 억지로 풀게 한 일로");
            }
            if (shared > 0)
            {
                chief.Status += 0.05;
                var text = $"우두머리 {Iga(chief)} 곳간이 넘치는 집들에 굶는 이웃을 먹이게 했다 (식량 {shared:F0}).";
                if (_sharingLogged) Minor(text); else Say(text);
                _sharingLogged = true;
            }
        }

        // 3) 굶는 이에게 남는 것을 나눠 줄지는 핏줄과 동정심이 정한다.
        foreach (var u in Shuffle(unfed.ToList()))
        {
            if (u.Master != null) continue;
            var uh = Head(u);
            double n = Need(u, Tick);
            Person? best = null;
            double bestScore = 0;
            foreach (var h in living)
            {
                if (!h.Alive || h == uh || h.Food < n + 2) continue;
                double s = Related(h, u) * h.T(Trait.KinBias) * 1.5 + h.T(Trait.Empathy) * 0.35 - (u.Outsider ? 0.2 : 0);
                if (s > bestScore) { bestScore = s; best = h; }
            }
            if (best != null && Rnd() < bestScore)
            {
                best.Food -= n;
                best.Status += 0.05;
                unfed.Remove(u);
            }
        }

        // 4) 그래도 굶으면 훔친다.
        foreach (var u in Shuffle(unfed.Where(p => p.Age(Tick) >= 12).ToList()))
        {
            if (!unfed.Contains(u)) continue;
            double pr = 0.35 + 0.3 * u.T(Trait.Greed) + 0.2 * u.T(Trait.Aggression) - 0.3 * u.T(Trait.Empathy);
            if (Rnd() < pr && TrySteal(u, 2.5, "굶주림을 못 이겨")) FeedHousehold(Head(u));
        }

        // 5) 굶주림.
        if (unfed.Count >= Math.Max(3, living.Count / 5)) Famine();
        foreach (var p in living)
        {
            if (!p.Alive) continue;
            if (!unfed.Contains(p))
            {
                p.HungrySeasons = 0;
                p.Health = Math.Min(1, p.Health + 0.15);
                continue;
            }
            double age = p.Age(Tick);
            p.HungrySeasons++;
            p.Health -= age < 5 || age > 55 ? 0.45 : 0.3;

            var h = Head(p);
            if (h != p && (age < 2 || age > 58) && p.HungrySeasons >= 2 && h.T(Trait.Empathy) < 0.45 && Rnd() < 0.6)
            {
                Abandon(h, p);
                continue;
            }
            if (p.Health <= 0)
            {
                Die(p, "굶주림");
                Say($"{Iga(p)} 굶어 죽었다 ({AgeOf(p)}세).");
            }
        }
    }

    void Famine()
    {
        if (_famineLogged) return;
        _famineLogged = true;
        Say("기근이 들었다. 먹을 것이 모자라다.");

        var outsiders = Living.Where(p => p.Outsider && p.Age(Tick) >= 14).ToList();
        if (outsiders.Count == 0) return;
        bool any = false;
        foreach (var k in Living.Where(k => !k.Outsider && k.Age(Tick) >= 14 && k.T(Trait.KinBias) > 0.5 && k.T(Trait.Empathy) < 0.5).ToList())
        {
            AddGrudge(k, outsiders[_rng.Next(outsiders.Count)], 0.25, "기근을 이방인 탓으로 여겨");
            any = true;
        }
        if (any) Say("굶주린 사람들 사이에서 이 모든 게 이방인들 탓이라는 말이 돌았다.");
    }

    void Abandon(Person head, Person victim)
    {
        bool infant = victim.Age(Tick) < 2;
        Die(victim, "버려짐");
        Say(infant
            ? $"굶주림 끝에 {Eun(head)} 갓난아이 {JosaEul(victim.Name)} 숲에 두고 왔다."
            : $"굶주림 끝에 {Eun(head)} 늙은 {JosaEul(victim.Name)} 산에 두고 왔다 ({AgeOf(victim)}세).");
        // 남은 혈육 중 마음 여린 이는 그 일을 잊지 않는다.
        foreach (var k in Living.Where(k => k != head && k.Age(Tick) >= 12 && Related(k, victim) >= 0.5 && k.T(Trait.Empathy) > 0.5).ToList())
            AddGrudge(k, head, 0.4, $"{Josa.Eul(victim.Name)} 버린 일로");
    }

    static string JosaEul(string name) => Josa.Eul(name);

    bool TrySteal(Person thief, double amount, string motive)
    {
        var th = Head(thief);
        Person? victim = null;
        double best = 0.5;
        foreach (var h in Living)
        {
            if (h == th || h.Food < 0.5) continue;
            double s = h.Food * (1 - Related(thief, h));
            if (s > best) { best = s; victim = h; }
        }
        if (victim == null) return false;

        double take = Math.Min(victim.Food, amount);
        victim.Food -= take;
        th.Food += take;
        if (Rnd() < 0.45)
        {
            AddGrudge(victim, thief, 0.6, "식량을 도둑맞은 일로");
            thief.Status -= 0.5;
            Say($"{Iga(thief)} {motive} {P(victim)}의 식량을 훔치다 들켰다.");
        }
        return true;
    }

    void EnvyTheft()
    {
        if (!Inst.PrivateProperty) return;
        var living = Living.ToList();
        if (living.Count == 0) return;
        double richest = living.Max(p => p.Food);
        foreach (var p in Shuffle(living.Where(p => p.Age(Tick) >= 14 && p.T(Trait.Greed) > 0.7)))
        {
            if (Rnd() >= 0.05 / (1 + 0.5 * Guards().Count)) continue;
            if (richest > 4 * Head(p).Food + 5) TrySteal(p, 3, "남의 곳간이 탐나");
        }
    }

    void Courtship()
    {
        var living = Living.ToList();
        var adultMen = living.Where(p => p.IsMale && p.Master == null && p.Age(Tick) >= 17 && p.Age(Tick) < 60).ToList();
        if (adultMen.Count == 0) return;
        var statuses = adultMen.Select(m => m.Status).OrderByDescending(s => s).ToList();
        double topQuartile = statuses[Math.Min(statuses.Count - 1, statuses.Count / 4)];

        bool Eligible(Person m) =>
            m.Alive && (m.Wives.Count == 0 ||
                        (!Inst.Monogamy && m.Wives.Count < 3 && m.Status >= topQuartile && Head(m).Food > 6));

        foreach (var w in Shuffle(living.Where(p => !p.IsMale && p.Master == null && p.Husband == null && p.Age(Tick) is >= 15 and <= 45)))
        {
            if (Rnd() > 0.5) continue;
            var suitors = adultMen.Where(m => Eligible(m) && Related(m, w) < 0.2).ToList();
            if (suitors.Count == 0) continue;

            double Score(Person m) =>
                m.Status + 0.3 * Head(m).Food + m.Health - (m.Outsider ? 0.5 : 0) - 0.5 * m.Wives.Count
                - (m.ClanId == w.ClanId ? 1 : 0) - Math.Abs(m.Age(Tick) - w.Age(Tick)) / 15;
            var ranked = suitors.Select(m => (m, s: Score(m) + Rnd())).OrderByDescending(x => x.s).ToList();
            var groom = ranked[0].m;
            Wed(groom, w);
            Say(groom.Wives.Count > 1
                ? $"{Iga(groom)} {Eul(w)} {groom.Wives.Count}번째 아내로 들였다."
                : $"{Iga(groom)} {Eul(w)} 아내로 맞았다.");

            // 짝짓기 경쟁: 밀려난 사내는 원한을 품는다.
            var rival = ranked.Skip(1).Select(x => x.m).FirstOrDefault(m => m.Wives.Count == 0);
            if (rival != null && Rnd() < 0.3 * (rival.T(Trait.Libido) + rival.T(Trait.Aggression)))
            {
                AddGrudge(rival, groom, groom.Wives.Count > 1 ? 0.6 : 0.45, $"{Josa.Eul(w.Name)} 빼앗긴 일로");
                Say($"{Eun(rival)} {JosaEul(w.Name)} {P(groom)}에게 빼앗기고 이를 갈았다.");
            }
        }
    }

    void Affairs()
    {
        var living = Living.ToList();
        foreach (var m in Shuffle(living.Where(p => p.IsMale && p.Age(Tick) is >= 17 and <= 50)))
        {
            if (!m.Alive || Rnd() >= 0.1 * m.T(Trait.Libido)) continue;
            var cands = living.Where(w => w.Alive && !w.IsMale && w.Husband is { Alive: true } && w.Husband != m
                                          && w.Age(Tick) is >= 15 and <= 40 && Related(m, w) < 0.2).ToList();
            if (cands.Count == 0) continue;
            var w = cands[_rng.Next(cands.Count)];
            var h = w.Husband!;
            double accept = 0.5 * w.T(Trait.Libido) + (h.Wives.Count > 1 ? 0.15 : 0) - 0.03 * h.Status + 0.03 * m.Status;
            if (Rnd() >= accept) continue;

            w.PendingLover = m;
            if (Rnd() < 0.35)
            {
                AddGrudge(h, m, 0.9, "아내와 정을 통한 일로");
                if (Inst.Monogamy) { m.Status -= 1; w.Status -= 1; }
                Say($"{Eun(h)} 아내 {Wa(w)} {P(m)}의 사이를 알아챘다.");
            }
        }
    }

    void PaternityDoubts()
    {
        foreach (var c in Living.Where(c => !c.PaternityChecked && c.BioFather != c.Father && c.Age(Tick) >= 6).ToList())
        {
            c.PaternityChecked = true;
            if (c.Father is not { Alive: true } f || c.BioFather is not { Alive: true } bio || Rnd() >= 0.4) continue;

            AddGrudge(f, bio, 0.9, "아내와 정을 통한 일로");
            if (f.T(Trait.Empathy) < 0.5)
            {
                f.Children.Remove(c);
                bio.Children.Add(c);
                c.Father = bio;
                c.ClanId = bio.ClanId;
                Say($"자라는 얼굴을 보고 {Eun(f)} {c.Name}의 아비가 {P(bio)}임을 알았다. 아이를 제 핏줄에서 내쳤다.");
            }
            else
            {
                Say($"자라는 얼굴을 보고 {Eun(f)} {c.Name}의 아비가 {P(bio)}임을 알았다. 그래도 아이는 제 자식으로 품었다.");
            }
        }
    }

    void StatusContest()
    {
        if (Chief is not { Alive: true } c) return;
        var ch = Living
            .Where(p => p != c && p.IsMale && p.Master == null && p.Age(Tick) is >= 18 and <= 45 && p.T(Trait.Ambition) > 0.65
                        && p.Status >= c.Status - 2.5)
            .OrderByDescending(p => p.Status).ThenBy(p => p.Id)
            .FirstOrDefault(p => Rnd() < 0.2 * p.T(Trait.Ambition));
        if (ch == null) return;

        int Allies(Person x) => Living.Count(k => k.IsMale && k.Age(Tick) >= 15 && Related(x, k) >= 0.15);
        double pc = Power(ch) + 0.25 * Allies(ch) + 0.2 * ch.Status;
        double pd = Power(c) + 0.25 * Allies(c) + 0.2 * c.Status;

        if (Rnd() < pc / (pc + pd))
        {
            Say($"{Iga(ch)} 우두머리 {Eul(c)} 꺾고 새 우두머리가 되었다.");
            ch.Status = Math.Max(c.Status, ch.Status) + 1;
            c.Status -= 1.5;
            c.Health -= 0.25;
            AddGrudge(c, ch, 0.6, "우두머리 자리를 빼앗긴 일로");
            Chief = ch;
            if (Rnd() < 0.12) Kill(ch, c, "우두머리 다툼");
        }
        else
        {
            Say($"{Iga(ch)} 우두머리 {P(c)}에게 도전했다가 꺾였다.");
            ch.Health -= 0.25;
            ch.Status -= 1;
            AddGrudge(ch, c, 0.5, "우두머리 자리를 두고");
            if (Rnd() < 0.1) Kill(c, ch, "우두머리 다툼");
        }
    }

    void Violence()
    {
        // 여러 아내를 거느린 자가 있으면 짝 없는 사내들의 원한이 조금씩 쌓인다.
        if (!Inst.Monogamy)
        {
            var hoarders = Living.Where(p => p.Wives.Count > 1).ToList();
            if (hoarders.Count > 0)
                foreach (var s in Living.Where(p => p.IsMale && p.Age(Tick) >= 20 && p.Wives.Count == 0).ToList())
                    AddGrudge(s, hoarders[_rng.Next(hoarders.Count)], 0.06, "여자를 독차지한 데 앙심을 품고");
        }

        foreach (var p in Shuffle(Living.Where(p => p.Age(Tick) >= 15)))
        {
            if (!p.Alive || p.Grudge.Count == 0) continue;
            Person? target = null;
            double g = 0;
            foreach (var (id, v) in p.Grudge)
            {
                var t = _byId[id];
                if (t.Alive && (v > g || (v == g && target != null && id < target.Id))) { g = v; target = t; }
            }
            if (target == null || g < 0.6) continue;

            double prob = (g - 0.4) * (0.2 + EffAggression(p)) * 0.35 * (p.IsMale ? 1 : 0.4);
            if (Rnd() < prob) Attack(p, target);
        }
    }

    void Attack(Person a, Person t)
    {
        var avenged = _murdered.FirstOrDefault(d => d.KilledBy == t && Related(a, d) >= 0.15);
        string why = avenged != null ? $"{avenged.Name}의 복수" : a.GrudgeWhy.GetValueOrDefault(t.Id, "묵은 원한");

        double pa = Power(a) * 1.25, pd = Power(t);
        if (Rnd() < pa / (pa + pd))
        {
            if (Rnd() < 0.4)
            {
                Kill(a, t, why);
            }
            else
            {
                t.Health -= 0.4;
                a.Status += 0.3;
                AddGrudge(t, a, 0.5, "습격당한 일로");
                Say($"{Iga(a)} {Eul(t)} 습격해 크게 다치게 했다 ({why}).");
                if (t.Health <= 0) Kill(a, t, why, announce: false);
            }
        }
        else
        {
            a.Health -= 0.3;
            Say($"{Iga(a)} {Eul(t)} 덮쳤으나 되레 당했다 ({why}).");
            // 주인에게 대든 종은 대개 살아남지 못한다.
            if (Rnd() < (a.Master == t ? 0.7 : 0.2) || a.Health <= 0) Kill(t, a, "습격을 받아치다", selfDefense: true);
        }
        if (a.Alive && a.Grudge.ContainsKey(t.Id)) a.Grudge[t.Id] *= 0.4;
    }

    void Kill(Person killer, Person victim, string why, bool announce = true, bool selfDefense = false)
    {
        if (!victim.Alive) return;
        // 죽기 전에 혈육을 모아 둔다 (죽으면 혼인 관계가 끊긴다).
        var kin = Living.Where(k => k != killer && k != victim && k.Age(Tick) >= 12 && Related(k, victim) >= 0.15)
            .Select(k => (k, r: Related(k, victim))).ToList();

        victim.KilledBy = killer;
        _murdered.Add(victim);
        Die(victim, "살해");
        killer.Status += 0.5;
        if (announce) Say($"{Iga(killer)} {Eul(victim)} 죽였다 ({why}).");
        else Say($"{Eun(victim)} {P(killer)}에게 입은 상처로 죽었다.");

        bool settled = false;
        if (Inst.BloodMoney && !selfDefense && victim.Master == null && kin.Count > 0)
        {
            var kh = Head(killer);
            var vh = Head(kin.OrderByDescending(x => x.r).ThenBy(x => x.k.Id).First().k);
            if (vh != kh && kh.Food >= Cfg.BloodPrice)
            {
                kh.Food -= Cfg.BloodPrice;
                vh.Food += Cfg.BloodPrice;
                settled = true;
                Say($"{P(killer)}의 집안이 {P(vh)}의 집안에 피값을 치렀다.");
            }
            else if (vh != kh && Inst.Slavery && killer.Master == null && vh.Master == null && Head(vh) == vh && Related(killer, vh) < 0.15)
            {
                Enslave(killer, vh);
                settled = true;
                Say($"피값을 치를 식량이 없어 {Josa.Eun(killer.Name)} {P(vh)}의 종이 되었다.");
            }
            else if (vh != kh)
            {
                Say($"{P(killer)}의 집안에는 피값을 치를 식량이 없었다. 법은 가난한 자의 피를 막지 못한다.");
            }
        }

        var killerKin = Living.Where(r => r != killer && r.IsMale && r.Age(Tick) >= 15 && Related(r, killer) >= 0.5).ToList();
        foreach (var (k, r) in kin)
        {
            double amt = (0.3 + 1.6 * r * k.T(Trait.KinBias)) * (settled ? 0.25 : 1);
            if (Related(k, killer) >= 0.5) amt *= 0.3;
            AddGrudge(k, killer, amt, $"{victim.Name}의 복수");
            // 피의 복수는 살인자 한 사람에게서 멈추지 않고 그의 아비, 형제, 아들에게 번진다.
            if (settled) continue;
            foreach (var kk in killerKin)
                if (Related(k, kk) < 0.5) AddGrudge(k, kk, amt * 0.4, $"{victim.Name}의 복수");
        }
    }

    void Births()
    {
        // 곳간이 비면 아이도 덜 태어난다.
        int pop = Living.Count();
        double perHead = pop == 0 ? 0 : (CommonStore + Living.Sum(p => p.Food)) / pop;
        double fertility = Math.Clamp(perHead / Cfg.ComfortStores, 0.2, 1);
        // 먹일 입이 일할 손보다 많아지면 아이를 미룬다(젖먹이를 업고는 일을 못 한다).
        int hands = Living.Count(p => p.Age(Tick) is >= 14 and < 60), mouths = Living.Count(p => p.Age(Tick) < 14);
        fertility *= DependencyBrake(hands, mouths);
        foreach (var w in Living.Where(p => !p.IsMale && p.Husband is { Alive: true } && p.Age(Tick) is >= 15 and <= 42).ToList())
        {
            if (!w.Alive || Tick - w.LastBirthTick < 6 || w.Health <= 0.5 || w.HungrySeasons > 0 || Rnd() >= 0.13 * fertility) continue;
            var h = w.Husband!;
            var bio = w.PendingLover is { Alive: true } l && Rnd() < 0.5 ? l : h;

            var c = NewPerson(_rng.Next(2) == 0 ? Sex.Male : Sex.Female, 0, h.ClanId);
            c.Father = h;
            c.BioFather = bio;
            c.Mother = w;
            for (int i = 0; i < c.Traits.Length; i++)
                c.Traits[i] = Math.Clamp((w.Traits[i] + bio.Traits[i]) / 2 + Noise(0.12), 0, 1);
            c.Skill = Math.Clamp((w.Skill + bio.Skill) / 2 + Noise(0.1), 0.5, 1.5);
            w.Children.Add(c);
            h.Children.Add(c);
            w.LastBirthTick = Tick;
            _yearBirths++;
            TotalBirths++;
            Minor($"{Wa(w)} {P(h)} 사이에서 {JosaIga(c.Name)} 태어났다.");

            if (Rnd() < 0.02)
            {
                Die(w, "출산");
                Say($"{Iga(w)} 아이를 낳다 죽었다 ({AgeOf(w)}세).");
            }
        }
    }

    static string JosaIga(string name) => Josa.Iga(name);

    /// <summary>아이(14세 미만)가 일손(14~59세)의 0.8배를 넘으면 출산이 줄어든다. 1.4배면 바닥(0.4)이다.</summary>
    internal static double DependencyBrake(int hands, int mouths) =>
        Math.Clamp(1.8 - (double)mouths / Math.Max(1, hands), 0.4, 1);

    void Mortality()
    {
        foreach (var p in Living.ToList())
        {
            double age = p.Age(Tick);
            double pr = 0.003;
            if (age < 5) pr += 0.012;
            if (age > 50) pr += (age - 50) * 0.004;
            pr *= 2 - Math.Clamp(p.Health, 0, 1);
            if (Rnd() >= pr) continue;

            // 늙음, 앓던 몸, 겨울 추위, 일터의 사고를 가른다. 건강한 어른의 갑작스러운 죽음은 대개 병이 아니다.
            string cause = DeathCause(age, p.Health, Season == 3, Rnd);
            Die(p, cause);
            var text = cause switch
            {
                "노환" => $"{Iga(p)} 늙어 죽었다 ({AgeOf(p)}세).",
                "추위" => $"{Iga(p)} 겨울 추위를 넘기지 못했다 ({AgeOf(p)}세).",
                "사고" => $"{Iga(p)} {AccidentText(p.Job)} 죽었다 ({AgeOf(p)}세).",
                _ => $"{Iga(p)} 병으로 죽었다 ({AgeOf(p)}세).",
            };
            if (age < 5) Minor(text); else Say(text);
        }
    }

    /// <summary>자연사의 갈래: 55세 넘으면 노환, 젖먹이나 앓던 몸은 병, 건강한 어른은 겨울 추위·일터의 사고·병으로 갈린다.</summary>
    internal static string DeathCause(double age, double health, bool winter, Func<double> roll) =>
        age >= 55 ? "노환" : age < 5 || health < 0.6 ? "병" : winter && roll() < 0.5 ? "추위" : roll() < 0.5 ? "사고" : "병";

    internal static string AccidentText(Work job) => job switch
    {
        Work.Fish => "물에 빠져",
        Work.Hunt => "짐승에게 받혀",
        Work.Wood => "쓰러지는 나무에 깔려",
        Work.Stone => "무너진 돌더미에 깔려",
        Work.Build => "짓던 것이 무너져",
        Work.Scout => "길을 잃고 벼랑에서 떨어져",
        _ => "독사에 물려",
    };

    void Die(Person p, string cause)
    {
        if (!p.Alive) return;
        // 가장이 쥔 식량과 원한은 죽은 뒤에도 남는다.
        var wives = p.Wives.Where(w => w.Alive).ToList();
        var sons = p.Children.Where(c => c.Alive && c.IsMale && c.Age(Tick) >= 14).OrderBy(c => c.BirthTick).ToList();
        var heirsOfGrudge = p.Children.Where(c => c.Alive && c.Age(Tick) >= 10).ToList();

        p.DeathTick = Tick;
        p.DeathCause = cause;
        DeathsByCause[cause] = DeathsByCause.GetValueOrDefault(cause) + 1;
        EatTheDead(cause);
        _yearDeaths[cause] = _yearDeaths.GetValueOrDefault(cause) + 1;

        if (p.Husband != null) { p.Husband.Wives.Remove(p); p.Husband = null; }
        foreach (var w in p.Wives) w.Husband = null;
        p.Wives.Clear();
        if (Chief == p) Chief = null;
        InheritThralls(p, sons.FirstOrDefault() ?? wives.FirstOrDefault());

        if (p.Food > 0)
        {
            if (sons.Count > 0)
            {
                var heir = sons[0];
                heir.Food += p.Food;
                // 맏이가 다 가져간다. 욕심 많은 아우는 그것을 잊지 않는다.
                if (Inst.PrivateProperty && p.Food >= 4)
                    foreach (var younger in sons.Skip(1).Where(s => s.T(Trait.Greed) > 0.6))
                    {
                        AddGrudge(younger, heir, 0.5, "유산을 독차지한 일로");
                        Say($"{P(p)}의 곳간은 맏이 {heir.Name}에게 넘어갔다. 아우 {JosaEun(younger.Name)} 빈손으로 남았다.");
                    }
            }
            else if (wives.Count > 0) wives[0].Food += p.Food;
            else CommonStore += p.Food;
            p.Food = 0;
        }

        foreach (var child in heirsOfGrudge)
            foreach (var (id, v) in p.Grudge)
            {
                if (v < 0.4) continue;
                var t = _byId[id];
                if (t.Alive && Related(child, t) < 0.5) AddGrudge(child, t, v * 0.5, p.GrudgeWhy.GetValueOrDefault(id, "부모 대의 원한"));
            }
    }

    static string JosaEun(string name) => Josa.Eun(name);

    void Decay()
    {
        // 한도 안의 식량은 조금씩 썩고(곡창이 있으면 절반만, 겨울엔 추워서 WinterRot배만), 한도를 넘친 것은 쌓아 둘 데가 없어 빨리 썩는다.
        double cold = Season == 3 ? Cfg.WinterRot : 1;
        double rot = (1 - Cfg.Keep) * cold * (HasBuilt(StructureKind.Granary) ? 0.5 : 1);
        double cap = StoreCap(), over = Math.Max(0, CommonStore - cap);
        CommonStore = (CommonStore - over) * (1 - rot) + over * (1 - StructureCatalog.ExcessRot);
        foreach (var p in Living)
        {
            p.Food *= 1 - (1 - Cfg.Keep) * cold;
            p.PendingLover = null;
            if (p.Age(Tick) >= 14) p.Status += p == Chief ? 0.05 : 0.02;
            if (p.Grudge.Count == 0) continue;
            foreach (var id in p.Grudge.Keys.ToList())
            {
                double v = p.Grudge[id] * (HasBuilt(StructureKind.Shrine) ? 0.92 : 0.94);
                if (v < 0.05 || !_byId[id].Alive) { p.Grudge.Remove(id); p.GrudgeWhy.Remove(id); }
                else p.Grudge[id] = v;
            }
        }
    }

    void YearEnd()
    {
        var living = Living.ToList();
        PeakPopulation = Math.Max(PeakPopulation, living.Count);
        if (living.Count == 0)
        {
            Log.Add(new LogEntry(Tick, LogKind.Summary, $"{Year}년 끝: 아무도 남지 않았다."));
            return;
        }
        var clans = living.GroupBy(p => p.ClanId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => $"{ClanName(g.Key)} {g.Count()}");
        string deaths = _yearDeaths.Count == 0
            ? "사망·이탈 0"
            : $"사망·이탈 {_yearDeaths.Values.Sum()}({string.Join(", ", _yearDeaths.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))})";
        double privateFood = living.Sum(p => p.Food);
        string wealth = "";
        if (Inst.PrivateProperty && privateFood > 1)
        {
            int topN = Math.Max(1, living.Count(p => p.Food > 0) / 10);
            double topShare = living.OrderByDescending(p => p.Food).Take(topN).Sum(p => p.Food) / privateFood;
            wealth = $" | 상위 10% 가구가 식량의 {topShare:P0}";
        }
        Log.Add(new LogEntry(Tick, LogKind.Summary,
            $"{Year}년 끝: 인구 {living.Count} ({string.Join(", ", clans)}) | 출생 {_yearBirths}, {deaths} | " +
            $"공동 창고 {CommonStore:F0}, 사적 식량 {privateFood:F0}{wealth} | 우두머리 {(Chief != null ? P(Chief) : "없음")}{WarSummary()}{RealmSummary()}"));
    }
}
