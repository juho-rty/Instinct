namespace Instinct.Sim;

/// <summary>기술 하나. 앞선 기술을 알아야 익힐 수 있고, 제도와 자원을 연다.
/// Course가 있으면 갈림길의 기술이다: 같은 시대의 갈래를 여럿 익힐 수 있지만, 하나 더 익힐 때마다 값이 붙는다(CostOf).</summary>
public sealed record TechDef(string Key, string Name, Era Era, double Cost, string[] Requires, string Effect, Resource Reveals = Resource.None, Course? Course = null)
{
    public bool Crossroad => Course != null;
}

/// <summary>기술 나무. 실행기·화면·구조도가 모두 이 목록을 읽는다.</summary>
public static class TechTree
{
    static TechDef T(string key, string name, Era era, double cost, string requires, string effect, Resource reveals = Resource.None) =>
        new(key, name, era, cost, requires.Length == 0 ? Array.Empty<string>() : requires.Split(','), effect, reveals);

    // 갈림길: 시대마다 네 갈래(정복·교역·공포·전제). 여럿을 섞을 수 있고, 갈래를 더할수록 비싸진다. 같은 갈래를 거듭 고를수록 그 나라의 성격이 짙어진다.
    static TechDef X(string key, string name, Era era, double cost, string requires, Course course, string effect) =>
        new(key, name, era, cost, requires.Split(','), effect, Resource.None, course);

    public static readonly IReadOnlyList<TechDef> All = new[]
    {
        // ── 원시 ──
        T("fire", "불과 석기", Era.Primitive, 3, "", "모든 것의 시작"),
        T("farming", "농사", Era.Primitive, 5, "fire", "씨를 뿌려 거둔다. 농경을 연다"),
        T("herding", "목축", Era.Primitive, 5, "fire", "짐승을 길들인다. 가축이 나는 땅을 알아본다", Resource.Livestock),
        T("pottery", "토기와 저장", Era.Primitive, 7, "farming", "남는 곡식을 쌓아 둔다. 땅이 조금 더 많은 사람을 먹인다"),

        // ── 고대 ──
        T("counting", "셈과 저울", Era.Ancient, 12, "pottery", "곡식을 세고 단다. 조세를 연다"),
        T("bronze", "청동", Era.Ancient, 15, "pottery", "구리로 날을 벼린다. 상비군을 열고 구리가 나는 땅을 알아본다", Resource.Copper),
        T("script", "글자", Era.Ancient, 18, "counting", "말을 남긴다. 문자와 성문법을 연다"),
        T("wheel", "바퀴와 길", Era.Ancient, 15, "herding", "더 먼 땅까지 명령이 닿는다(다스릴 수 있는 땅 +1)"),
        T("iron", "철", Era.Ancient, 20, "bronze", "쇠 보습과 쇠 칼. 철이 나는 땅을 알아본다", Resource.Iron),
        T("currency", "주화", Era.Ancient, 15, "counting,bronze", "쇠붙이에 값을 새긴다. 화폐를 연다"),

        // ── 중세 ──
        T("plow", "무거운 쟁기", Era.Medieval, 45, "iron", "깊이 갈아 더 거둔다. 농노제를 연다"),
        T("masonry", "석조와 성", Era.Medieval, 50, "iron,wheel", "성벽이 침략을 막는다. 봉건제를 연다"),
        T("crafts", "수공업", Era.Medieval, 45, "currency", "장인의 손. 길드를 연다"),
        T("theology", "신학과 대학", Era.Medieval, 60, "script", "지식이 조금 빨리 쌓인다. 종교재판을 연다"),

        // ── 근세 ──
        T("press", "활판 인쇄", Era.EarlyModern, 90, "theology,crafts", "인쇄술·종교 관용·의무교육을 연다"),
        T("gunpowder", "화약", Era.EarlyModern, 90, "masonry", "성벽이 무너진다. 군대가 강해지고 절대왕정을 연다"),
        T("navigation", "항해술", Era.EarlyModern, 100, "crafts", "먼바다로 나간다. 식민지를 열고 향료가 나는 땅을 알아본다", Resource.Spice),
        T("banking", "은행과 부기", Era.EarlyModern, 100, "currency,crafts", "나라가 빚을 굴린다. 세입이 늘고 의회를 연다"),

        // ── 근대 ──
        T("steam", "증기기관", Era.Industrial, 180, "press,banking", "공장제와 노동법을 열고 석탄이 나는 땅을 알아본다", Resource.Coal),
        T("rail", "철도와 전신", Era.Industrial, 220, "steam", "다스릴 수 있는 땅 +2. 민족주의·징병제·보통선거를 연다"),
        T("medicine", "의학과 위생", Era.Industrial, 200, "press", "돌림병이 절반으로 준다"),
        T("electricity", "전기", Era.Industrial, 240, "steam", "밤이 밝아진다. 땅이 더 많은 사람을 먹인다"),

        // ── 현대 ──
        T("engine", "내연기관", Era.Modern, 520, "electricity", "기름으로 달리는 군대. 석유가 나는 땅을 알아본다", Resource.Oil),
        T("broadcast", "방송", Era.Modern, 450, "electricity", "한 목소리가 온 나라에 닿는다. 선전을 연다"),
        T("fission", "핵분열", Era.Modern, 700, "electricity", "핵무장을 열고 우라늄이 나는 땅을 알아본다", Resource.Uranium),
        T("computing", "전산", Era.Modern, 750, "electricity", "지식이 빨리 쌓인다. 감시와 세계화를 연다"),
        T("ecology", "생태학", Era.Modern, 520, "medicine", "환경 규제와 복지국가를 연다"),

        // ── 갈림길 (시대마다 넷, 섞을 수 있다) ──
        X("phalanx", "밀집 대형", Era.Ancient, 16, "bronze", Course.Conquest, "방패를 맞댄 줄이 무너지지 않는다"),
        X("caravan", "대상의 길", Era.Ancient, 16, "wheel", Course.Commerce, "낙타 행렬이 먼 장터를 잇는다"),
        X("skulltower", "해골 탑", Era.Ancient, 16, "bronze", Course.Terror, "꺾은 적의 머리를 성문 앞에 쌓는다"),
        X("godking", "신이 된 왕", Era.Ancient, 16, "script", Course.Tyranny, "왕은 사람이 아니라 신의 몸이다"),

        X("chivalry", "기사도", Era.Medieval, 50, "masonry", Course.Conquest, "말 탄 귀족이 땅을 받고 싸움을 바친다"),
        X("fairs", "장시와 어음", Era.Medieval, 50, "crafts", Course.Commerce, "종이 한 장이 금화 자루를 대신한다"),
        X("reavers", "노략 선단", Era.Medieval, 50, "iron", Course.Terror, "얕은 배가 강을 거슬러 마을을 턴다"),
        X("divineright", "왕권신수", Era.Medieval, 50, "theology", Course.Tyranny, "왕을 거스르는 것은 신을 거스르는 것이다"),

        X("drill", "제식 훈련", Era.EarlyModern, 95, "gunpowder", Course.Conquest, "북소리에 맞춰 쏘고 장전한다"),
        X("jointstock", "주식회사", Era.EarlyModern, 95, "banking", Course.Commerce, "남의 돈을 모아 배를 띄운다"),
        X("slavecoast", "노예 해안", Era.EarlyModern, 95, "navigation", Course.Terror, "사람을 사고파는 항구가 선다"),
        X("spycourt", "밀정의 궁정", Era.EarlyModern, 95, "gunpowder", Course.Tyranny, "벽에도 귀가 있다"),

        X("generalstaff", "참모본부", Era.Industrial, 210, "rail", Course.Conquest, "철도 시간표로 전쟁을 짠다"),
        X("exchange", "증권거래소", Era.Industrial, 210, "steam", Course.Commerce, "온 나라의 돈이 한 건물에서 값을 매긴다"),
        X("camps", "수용소", Era.Industrial, 210, "rail", Course.Terror, "철조망 너머로 사라진 사람들"),
        X("partystate", "일당 국가", Era.Industrial, 210, "steam", Course.Tyranny, "당이 곧 나라다"),

        X("missiles", "유도탄", Era.Modern, 560, "engine", Course.Conquest, "국경 너머까지 닿는 팔"),
        X("offshore", "국제 금융", Era.Modern, 560, "computing", Course.Commerce, "돈에는 국경이 없다"),
        X("chemwar", "화학전", Era.Modern, 560, "engine", Course.Terror, "바람이 무기다"),
        X("personality", "개인숭배", Era.Modern, 560, "broadcast", Course.Tyranny, "모든 벽에 같은 얼굴"),
    };

    /// <summary>제도 키 → 그 제도를 여는 기술 키. 여기 없는 제도(원시의 관습들, 국교)는 기술 없이도 들일 수 있다.</summary>
    public static readonly IReadOnlyDictionary<string, string> Gate = new Dictionary<string, string>
    {
        ["agriculture"] = "farming",
        ["taxation"] = "counting", ["army"] = "bronze", ["writing"] = "script", ["lawcode"] = "script", ["coinage"] = "currency",
        ["serfdom"] = "plow", ["feudalism"] = "masonry", ["guilds"] = "crafts", ["inquisition"] = "theology",
        ["printing"] = "press", ["tolerance"] = "press", ["absolutism"] = "gunpowder", ["colonialism"] = "navigation", ["parliament"] = "banking",
        ["factories"] = "steam", ["laborlaws"] = "steam", ["schooling"] = "press",
        ["nationalism"] = "rail", ["conscription"] = "rail", ["suffrage"] = "rail",
        ["propaganda"] = "broadcast", ["nuclear"] = "fission", ["surveillance"] = "computing", ["globalization"] = "computing",
        ["greenlaw"] = "ecology", ["welfare"] = "ecology",
    };

    public static TechDef? Find(string key) => All.FirstOrDefault(t => t.Key == key);

    /// <summary>이 기술이 여는 제도들.</summary>
    public static IEnumerable<InstitutionDef> Unlocks(string key) =>
        Catalog.All.Where(d => Gate.TryGetValue(d.Key, out var k) && k == key);
}

// 기술: 해마다 쌓이는 지식이 지금 파고드는 기술 하나로 흘러 들어간다.
public sealed partial class Simulation
{
    readonly HashSet<string> _known = new();

    /// <summary>지금 파고드는 기술의 키. 고르지 않으면 가장 쉬운 것을 알아서 잡는다.</summary>
    public string? Research { get; private set; }
    /// <summary>그 기술에 쌓인 지식.</summary>
    public double ResearchProgress { get; private set; }

    /// <summary>지난해에 기술로 흘러간 지식(화면에 보여 준다).</summary>
    public double ResearchRate { get; private set; }
    /// <summary>다스릴 수 있는 땅의 수. 영토가 이를 넘으면 값이 들고 결속이 닳는다.</summary>
    public int AdminCap => AdminCapacity();

    public bool Knows(string key) => _known.Contains(key);
    public int KnownCount => _known.Count;
    double TB(string key, double v) => _known.Contains(key) ? v : 0;

    public bool CanResearch(TechDef t) => !Knows(t.Key) && Realm.Era >= t.Era && t.Requires.All(Knows);

    /// <summary>
    /// 그 기술을 익히는 데 드는 지식. 갈림길은 닫히지 않는 대신, 같은 시대에서 이미 익힌 갈래 하나마다 값이 절반씩 더 붙는다
    /// (둘째 갈래 ×1.5, 셋째 ×2, 넷째 ×2.5). 여러 길을 섞을 수 있지만 그만큼 늦어진다.
    /// </summary>
    public double CostOf(TechDef t) =>
        t.Crossroad ? t.Cost * (1 + 0.5 * TechTree.All.Count(o => o.Crossroad && o.Era == t.Era && o.Key != t.Key && _known.Contains(o.Key))) : t.Cost;

    /// <summary>그 시대의 갈림길에서 고른 기술 가운데 하나(아직 고르지 않았으면 null).</summary>
    public TechDef? CrossroadChoice(Era era) => TechTree.All.FirstOrDefault(t => t.Crossroad && t.Era == era && _known.Contains(t.Key));

    /// <summary>제도를 여는 기술을 알고 있는가.</summary>
    public bool TechAllows(InstitutionDef d) => !TechTree.Gate.TryGetValue(d.Key, out var k) || Knows(k);

    /// <summary>파고들 기술을 고른다. 아직 익힐 수 없는 것이면 false.</summary>
    public bool SetResearch(string key)
    {
        if (TechTree.Find(key) is not { } t || !CanResearch(t)) return false;
        if (Research != null && Research != key) ResearchProgress *= 0.5; // 하던 것을 접으면 쌓은 것의 절반은 버려진다(갈림길에서 기다리던 것은 온전히 쓴다)
        Research = key;
        Wrote("research", key);
        return true;
    }

    TechDef? CurrentResearch()
    {
        if (Research != null && TechTree.Find(Research) is { } cur && CanResearch(cur)) return cur;
        // 저절로는 갈림길이 아닌 기술부터 익힌다. 남은 것이 갈림길뿐이면: 플레이어는 직접 고르고, 아니면 나라의 기질을 따른다(시대마다 한 갈래).
        var next = TechTree.All.Where(t => !t.Crossroad && CanResearch(t)).OrderBy(t => t.Cost).FirstOrDefault();
        if (next == null && !PlayerRules)
        {
            var lean = Leaning();
            next = TechTree.All.FirstOrDefault(t => t.Crossroad && t.Course == lean && CanResearch(t) && CrossroadChoice(t.Era) == null);
        }
        Research = next?.Key;
        return next;
    }

    /// <summary>
    /// 아직 한 갈래도 고르지 않은 시대의 갈림길이 남아 플레이어의 선택을 기다리는가.
    /// 한 갈래를 고른 뒤의 나머지 갈래는 원할 때 더 익히는 것이라 기다리지 않는다.
    /// </summary>
    public bool AtCrossroad => Research == null && TechTree.All.Any(t => t.Crossroad && CanResearch(t) && CrossroadChoice(t.Era) == null)
                               && !TechTree.All.Any(t => !t.Crossroad && CanResearch(t));

    /// <summary>한 해의 지식이 기술로 쌓인다.</summary>
    internal void ResearchYear(double gain)
    {
        ResearchRate = gain;
        ResearchProgress += gain;
        while (CurrentResearch() is { } t)
        {
            double cost = CostOf(t);
            if (ResearchProgress < cost) return;
            ResearchProgress -= cost;
            Learn(t);
        }
        // 파고들 것이 없다. 갈림길이 기다리거나 플레이어가 더 고를 갈래가 남았으면, 쌓은 것을 그 값만큼은 남겨 둔다.
        var open = TechTree.All.Where(CanResearch).ToList();
        double hold = (AtCrossroad || PlayerRules) && open.Count > 0 ? open.Max(CostOf) : 0;
        ResearchProgress = Math.Min(ResearchProgress, hold);
    }

    void Learn(TechDef t)
    {
        _known.Add(t.Key);
        if (Research == t.Key) Research = null;
        if (t.Crossroad)
        {
            Say($"【갈림길】 {Josa.Eul(t.Name)} 택했다: {t.Effect}. 나라가 {CourseName(t.Course!.Value)}의 길로 한 걸음 더 들어섰다"
                + $" ({CourseName(t.Course!.Value)} {CourseLevel(t.Course!.Value)}단계).");
            return;
        }
        var opens = TechTree.Unlocks(t.Key).Select(d => d.Name).ToList();
        Say($"【기술】 {t.Name}: {t.Effect}." + (opens.Count > 0 ? $" 이제 들일 수 있는 제도: {string.Join(", ", opens)}." : ""));
        if (t.Reveals != Resource.None) Discover(t.Reveals);
    }

    /// <summary>그 시대까지의 기술을 모두 아는 상태로 만든다(시험과 상태 만들기용). 갈림길은 고르지 않는다. 연대기에는 남기지 않는다.</summary>
    internal void GrantTechs(Era upTo)
    {
        foreach (var t in TechTree.All.Where(t => t.Era <= upTo && !t.Crossroad)) _known.Add(t.Key);
        Research = null;
        ResearchProgress = 0;
    }

    /// <summary>갈림길 기술을 하나 익힌 상태로 만든다(시험용). 이미 알면 false.</summary>
    internal bool GrantCrossroad(string key) => TechTree.Find(key) is { Crossroad: true } && _known.Add(key);
}
