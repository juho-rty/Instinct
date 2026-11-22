using System.Reflection;

namespace Instinct.Sim;

/// <summary>제도 한 가지의 설명. 실행기·화면·구조도가 모두 이 목록을 읽는다.</summary>
public sealed record InstitutionDef(string Key, string Field, string Name, Era Era, Stage MinStage, string Gain, string Cost)
{
    readonly FieldInfo _field = typeof(Institutions).GetField(Field)
                                ?? throw new InvalidOperationException($"Institutions.{Field} 없음");

    /// <summary>이 제도로 얻는 계층과 잃는 계층. 들이면 잃는 쪽이, 없애면 얻던 쪽이 반발한다.</summary>
    public IReadOnlyList<Estate> Winners => Catalog.Stance(Key).win;
    public IReadOnlyList<Estate> Losers => Catalog.Stance(Key).lose;

    public bool Get(Institutions i) => (bool)_field.GetValue(i)!;
    public void Set(Institutions i, bool on) => _field.SetValue(i, on);
}

public static class Catalog
{
    public static readonly string[] StageNames = { "부족", "군장사회", "도시국가", "왕국", "제국" };
    public static readonly string[] EraNames = { "원시", "고대", "중세", "근세", "근대", "현대" };
    public static readonly string[] RulerTitles = { "우두머리", "군장", "통치자", "왕", "황제" };
    public static readonly string[] EstateNames = { "귀족", "평민", "예속민", "노동자" };

    // 제도 키 → "얻는 계층/잃는 계층". N 귀족, C 평민, B 예속민(노예·농노), W 노동자.
    static readonly Dictionary<string, string> Stances = new()
    {
        ["private"] = "N/C", ["monogamy"] = "C/N", ["bloodmoney"] = "C/N", ["raiding"] = "N/C", ["slavery"] = "N/B",
        ["parley"] = "C/N", ["tribute"] = "C/N", ["alliance"] = "N/C", ["cannibalism"] = "N/C", ["agriculture"] = "C/",
        ["writing"] = "N/", ["taxation"] = "N/C", ["lawcode"] = "C/N", ["army"] = "N/C", ["religion"] = "N/", ["coinage"] = "C/",
        ["feudalism"] = "N/C", ["serfdom"] = "N/BC", ["guilds"] = "C/W", ["inquisition"] = "N/C",
        ["printing"] = "C/N", ["colonialism"] = "N/", ["absolutism"] = "/N", ["parliament"] = "NC/", ["tolerance"] = "C/N",
        ["factories"] = "N/W", ["laborlaws"] = "W/N", ["schooling"] = "CW/N", ["nationalism"] = "C/", ["conscription"] = "N/CW",
        ["suffrage"] = "CW/N", ["welfare"] = "CW/N", ["propaganda"] = "N/", ["surveillance"] = "N/CW", ["nuclear"] = "/",
        ["globalization"] = "N/W", ["greenlaw"] = "C/N",
    };

    static Estate[] Parse(string s) => s.Select(ch => ch switch
    {
        'N' => Estate.Nobles, 'C' => Estate.Commoners, 'B' => Estate.Bound, _ => Estate.Workers,
    }).ToArray();

    static readonly Dictionary<string, (Estate[] win, Estate[] lose)> StanceCache =
        Stances.ToDictionary(kv => kv.Key, kv => (Parse(kv.Value.Split('/')[0]), Parse(kv.Value.Split('/')[1])));

    public static (Estate[] win, Estate[] lose) Stance(string key) =>
        StanceCache.TryGetValue(key, out var s) ? s : (Array.Empty<Estate>(), Array.Empty<Estate>());

    static InstitutionDef D(string key, string field, string name, Era era, Stage stage, string gain, string cost) =>
        new(key, field, name, era, stage, gain, cost);

    public static readonly IReadOnlyList<InstitutionDef> All = new[]
    {
        // ── 원시: 무리 안의 본능을 다루는 제도 ──
        D("private", "PrivateProperty", "사유재산", Era.Primitive, Stage.Tribe, "일한 만큼 갖는다. 야심이 일을 시킨다", "빈부 차, 시샘 도둑질, 유산 다툼. 불평등이 자란다"),
        D("monogamy", "Monogamy", "일부일처", Era.Primitive, Stage.Tribe, "짝 없는 사내가 줄어 폭력이 준다", "힘센 자의 불만. 불륜은 그대로"),
        D("bloodmoney", "BloodMoney", "피값", Era.Primitive, Stage.Tribe, "살인이 복수의 사슬로 번지지 않는다", "못 갚는 자는 (노예제가 있으면) 종이 된다"),
        D("raiding", "Raiding", "약탈", Era.Primitive, Stage.Tribe, "식량·여자·종을 빼앗아 온다", "사내가 죽고 되갚음을 부른다. 나라가 되면 전쟁이 잦다"),
        D("slavery", "Slavery", "노예제", Era.Primitive, Stage.Tribe, "포로와 빚진 자의 노동", "종의 원한과 도망, 노예 반란, 불평등"),
        D("parley", "Parley", "화친", Era.Primitive, Stage.Tribe, "선물로 싸움을 멈춘다. 나라가 되면 전쟁이 준다", "선물 값, 피붙이 잃은 이들의 원한"),
        D("tribute", "Tribute", "공물", Era.Primitive, Stage.Tribe, "바쳐서 침략을 물린다", "해마다 느는 요구, 꺾이는 위신"),
        D("alliance", "MarriageAlliance", "혼인 동맹", Era.Primitive, Stage.Tribe, "사돈이 싸움을 돕는다", "지참 예물, 딸을 보냄, 남의 싸움에 끌려감"),
        D("agriculture", "Agriculture", "농경", Era.Primitive, Stage.Tribe, "땅이 더 많은 사람을 먹인다. 군장사회로 가는 문", "흉년에 무너지는 인구, 역병, 쌓이는 잉여가 낳는 불평등"),
        D("cannibalism", "Cannibalism", "식인 풍습", Era.Primitive, Stage.Tribe, "굶어 죽은 이와 쓰러진 적이 식량이 된다. 공포가 쌓여 이웃이 덜 쳐들어온다", "떨림병, 혼인 동맹이 끊기고 이웃의 미움이 자란다. 나라가 되면 통치자 지지와 장삿길을 잃는다"),

        // ── 고대 ──
        D("writing", "Writing", "문자", Era.Ancient, Stage.Chiefdom, "지식이 쌓이고 세금을 셀 수 있다", "글을 아는 계층이 따로 선다"),
        D("taxation", "Taxation", "조세", Era.Ancient, Stage.Chiefdom, "나라 살림이 생긴다. 도시국가로 가는 문", "거두는 만큼 민심이 떠난다"),
        D("lawcode", "LawCode", "성문법", Era.Ancient, Stage.Chiefdom, "부패가 줄고 더 넓은 땅을 다스린다", "법은 가진 자의 편이다. 신분이 굳는다"),
        D("army", "StandingArmy", "상비군", Era.Ancient, Stage.Chiefdom, "강한 군대, 반란 진압", "급료, 그리고 야심 있는 장수의 정변"),
        D("religion", "StateReligion", "국교", Era.Ancient, Stage.Chiefdom, "단합과 통치자 지지", "지식이 더디고 이단을 박해한다"),
        D("coinage", "Coinage", "화폐", Era.Ancient, Stage.CityState, "장사와 세입이 는다", "부가 쏠리고, 재정이 궁하면 돈의 값을 깎는다"),

        // ── 중세 ──
        D("feudalism", "Feudalism", "봉건제", Era.Medieval, Stage.CityState, "싼값에 군대와 넓은 영토", "세입을 귀족과 나눈다. 귀족 반란"),
        D("serfdom", "Serfdom", "농노제", Era.Medieval, Stage.CityState, "땅이 더 많이 낸다", "깊은 불평등, 농민 반란"),
        D("guilds", "Guilds", "길드", Era.Medieval, Stage.CityState, "도시와 기술이 자란다", "독점과 텃세로 부가 쏠린다"),
        D("inquisition", "Inquisition", "종교재판", Era.Medieval, Stage.CityState, "불만이 겉으로 드러나지 않고 단합이 는다", "마녀사냥, 지식의 후퇴, 억눌린 분노"),

        // ── 근세 ──
        D("printing", "Printing", "인쇄술", Era.EarlyModern, Stage.CityState, "지식이 빠르게 퍼진다", "선동과 종교 분열도 같이 퍼진다"),
        D("colonialism", "Colonialism", "식민지", Era.EarlyModern, Stage.Kingdom, "막대한 부", "수탈과 학살, 식민지 봉기, 잦은 전쟁"),
        D("absolutism", "Absolutism", "절대왕정", Era.EarlyModern, Stage.Kingdom, "통치자 지지가 굳고 귀족을 누른다", "부패, 억눌린 불만, 혁명이 나면 더 크게 난다"),
        D("parliament", "Parliament", "의회", Era.EarlyModern, Stage.CityState, "부패와 불만이 준다", "더딘 결정으로 군대가 약해진다"),
        D("tolerance", "Tolerance", "종교 관용", Era.EarlyModern, Stage.CityState, "지식이 자라고 종교 전쟁이 없다", "단합이 느슨해진다"),

        // ── 근대 ──
        D("factories", "Factories", "공장제", Era.Industrial, Stage.CityState, "생산과 인구 부양력이 뛴다", "아동 노동, 빈민가, 오염, 벌어지는 격차"),
        D("laborlaws", "LaborLaws", "노동법", Era.Industrial, Stage.CityState, "격차와 불만이 준다", "세입이 조금 준다"),
        D("schooling", "PublicSchooling", "의무교육", Era.Industrial, Stage.CityState, "지식과 국민의 단합", "돈이 든다"),
        D("nationalism", "Nationalism", "민족주의", Era.Industrial, Stage.CityState, "강한 단합과 군대", "전쟁, 그리고 소수 집단 박해와 학살"),
        D("conscription", "Conscription", "징병제", Era.Industrial, Stage.CityState, "압도적인 군대", "총력전: 전쟁이 나면 백성이 떼로 죽는다"),
        D("suffrage", "Suffrage", "보통선거", Era.Industrial, Stage.CityState, "통치자 지지, 혁명 대신 선거", "현대에는 갈라진 여론이 단합을 깎는다"),

        // ── 현대 ──
        D("welfare", "Welfare", "복지국가", Era.Modern, Stage.CityState, "격차와 불만이 준다", "큰 지출, 낮아지는 출산"),
        D("propaganda", "Propaganda", "선전", Era.Modern, Stage.CityState, "통치자 지지와 단합", "억눌린 불만, 학살을 부추긴다"),
        D("surveillance", "Surveillance", "감시", Era.Modern, Stage.CityState, "불만과 정변을 누른다", "숙청, 지식의 후퇴, 억눌린 분노"),
        D("nuclear", "NuclearArms", "핵무장", Era.Modern, Stage.CityState, "큰 전쟁이 드물어진다", "아주 낮은 확률로 모든 것이 끝난다"),
        D("globalization", "Globalization", "세계화", Era.Modern, Stage.CityState, "생산과 지식", "격차, 느슨한 단합, 빠르게 번지는 역병"),
        D("greenlaw", "GreenLaw", "환경 규제", Era.Modern, Stage.CityState, "오염이 줄어든다", "세입이 조금 준다"),
    };

    public static InstitutionDef? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
