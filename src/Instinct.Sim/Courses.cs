namespace Instinct.Sim;

/// <summary>나라가 걷는 길. 기술의 갈림길이 앞의 넷으로 갈리고, 길마다 이기는 방법(승리)이 하나씩 있다.</summary>
public enum Course { Conquest, Commerce, Terror, Tyranny, Learning }

/// <summary>승리 하나. Hold는 조건을 몇 해 내리 지켜야 하는가(1이면 닿는 순간).</summary>
public sealed record VictoryDef(Course Course, string Name, string Goal, int Hold);

// 길과 승리: 갈림길에서 고른 기술이 나라의 성격을 정하고(정복·교역·공포·전제), 그 끝에 각자의 승리가 있다.
// 공포(Dread)는 약탈·식인·학살로 쌓이는 이름값이다: 이웃은 덜 쳐들어오지만 더 미워한다.
// 난수는 식인 풍습이 켜졌을 때만 더 뽑는다(그 제도가 꺼진 판의 역사는 그대로다).
public sealed partial class Simulation
{
    public static readonly string[] CourseNames = { "정복", "교역", "공포", "전제", "학문" };
    public static string CourseName(Course c) => CourseNames[(int)c];
    public static string CourseKey(Course c) => c.ToString().ToLowerInvariant();

    /// <summary>"conquest"/"commerce"/"terror"/"tyranny"/"learning"(또는 한국어 이름) → 길. 모르면 null.</summary>
    public static Course? ParseCourse(string s) => s.Trim().ToLowerInvariant() switch
    {
        "conquest" or "정복" or "empire" => Course.Conquest,
        "commerce" or "교역" or "economy" => Course.Commerce,
        "terror" or "공포" or "raid" => Course.Terror,
        "tyranny" or "전제" or "독재" => Course.Tyranny,
        "learning" or "학문" or "science" => Course.Learning,
        _ => null,
    };


    public static readonly IReadOnlyList<VictoryDef> Victories = new[]
    {
        new VictoryDef(Course.Conquest, "제국 건설", "제국으로 영토 9 이상을 15년 동안 지킨다 (정복의 갈림길 2단계)", 15),
        new VictoryDef(Course.Commerce, "경제 승리", "근대에 교역국 3곳 이상, 곳간 5 이상을 15년 동안 지킨다 (교역의 갈림길 3단계)", 15),
        new VictoryDef(Course.Terror, "공포의 군림", "왕국 이상에서 공포 80에 닿는다 (공포의 갈림길 2단계)", 1),
        new VictoryDef(Course.Tyranny, "영원한 독재", "왕국 이상에서 의회·보통선거 없이 절대왕정이나 감시로 통치자 지지 60% 이상을 30년 동안 지킨다 (전제의 갈림길 2단계)", 30),
        new VictoryDef(Course.Learning, "계몽", "근대에 지식 2,200에 닿고 의무교육을 편다", 1),
    };

    /// <summary>계몽 승리에 필요한 지식.</summary>
    public const double LearningGoal = 2200;

    /// <summary>이룬 승리와 그 해. 승리한 뒤에도 판은 이어진다.</summary>
    public readonly List<(Course Course, int Year)> Won = new();
    readonly int[] _victoryStreak = new int[5];
    int _atrocitiesSeen;
    int _eatenThisYear;

    /// <summary>그 길의 갈림길 기술을 몇 개 익혔나(0~5).</summary>
    public int CourseLevel(Course c)
    {
        int n = 0;
        foreach (var t in TechTree.All)
            if (t.Course == c && _known.Contains(t.Key)) n++;
        return n;
    }

    int L(Course c) => CourseLevel(c);

    /// <summary>나라의 기질: 갈림길을 플레이어가 고르지 않을 때 이 길을 따른다.</summary>
    public Course Leaning()
    {
        var i = Inst;
        var r = Realm;
        double[] s =
        {
            B(i.StandingArmy, 1) + B(i.Feudalism, 1) + B(i.Conscription, 1) + B(i.Colonialism, 0.5) + RulerT(Trait.Ambition),
            B(i.Coinage, 1) + B(i.Guilds, 1) + B(i.Globalization, 1) + B(i.PrivateProperty, 0.5) + 0.5 * Rivals.Count(v => v.Trade),
            B(i.Raiding, 1) + B(i.Slavery, 0.5) + B(i.Cannibalism, 2) + 0.3 * Math.Min(5, r.Atrocities) + RulerT(Trait.Aggression),
            B(i.Absolutism, 1) + B(i.Inquisition, 1) + B(i.Surveillance, 1) + B(i.Propaganda, 0.5) + B(i.StateReligion, 0.5) + RulerT(Trait.Greed),
        };
        for (int k = 0; k < 4; k++) s[k] += 0.6 * L((Course)k);
        int best = 0;
        for (int k = 1; k < 4; k++) if (s[k] > s[best] + 1e-9) best = k;
        return (Course)best;
    }

    // ───────────────────────── 길이 미는 값 ─────────────────────────

    /// <summary>쳐들어오는 이가 망설이는 정도: 공포의 길과 쌓인 공포가 습격·침공의 확률을 깎는다.</summary>
    internal double DreadFactor => Math.Max(0.4, 1 - 0.3 * Math.Min(1, Realm.Dread / 100) - 0.08 * L(Course.Terror));

    /// <summary>이긴 싸움 하나가 남기는 이름값.</summary>
    internal void Feared(double v)
    {
        double add = v * (1 + 0.5 * L(Course.Terror)) + B(Inst.Cannibalism, 1);
        Realm.Dread = Math.Min(150, Realm.Dread + add);
    }

    /// <summary>해마다(겨울): 공포가 쌓이고 옅어지며, 식인 풍습이 값을 치르게 하고, 승리를 센다.</summary>
    internal void CourseYear()
    {
        var r = Realm;
        var i = Inst;

        int newAtrocities = r.Atrocities - _atrocitiesSeen;
        _atrocitiesSeen = r.Atrocities;
        r.Dread = Math.Clamp(r.Dread * 0.97 + B(i.Cannibalism, 1) + B(i.Raiding, 0.2) + B(i.Slavery, 0.2)
                             + 0.5 * L(Course.Terror) + 4 * Math.Max(0, newAtrocities), 0, 150);

        if (i.Cannibalism) CannibalYear();
        else _eatenThisYear = 0;

        // 공포의 이름은 미움을 부른다: 이웃 나라의 적의가 해마다 조금씩 오른다.
        double hate = 0.01 * L(Course.Terror) + B(i.Cannibalism, 0.02);
        if (hate > 0)
            foreach (var v in Rivals) v.Hostility = Clamp01(v.Hostility + hate);

        VictoryYear();
    }

    void CannibalYear()
    {
        if (_eatenThisYear > 0)
            Say($"굶주린 해, 죽은 이 {_eatenThisYear}명의 살이 산 자들을 먹였다. 아무도 그 이야기를 입에 올리지 않는다.");
        _eatenThisYear = 0;
        // 떨림병: 사람의 살을 먹는 무리에 도는 병. 웃는 듯 떨다가 죽는다.
        if (Rnd() < 0.2)
        {
            var sick = Living.Where(p => p.Age(Tick) >= 14).OrderBy(p => p.Id).ToList();
            if (sick.Count > 0)
            {
                var p = sick[_rng.Next(sick.Count)];
                p.Health -= 0.6;
                if (p.Health <= 0) { Die(p, "병"); Say($"{Iga(p)} 떨림병으로 웃는 듯 떨다가 죽었다 ({AgeOf(p)}세)."); }
                else Say($"{Iga(p)} 떨림병에 걸려 손이 멈추지 않는다.");
            }
        }
        // 이웃 나라는 식인하는 나라와 장사하지 않으려 한다.
        if (Realm.Stage >= Stage.CityState && Rivals.Where(v => v.Trade).OrderBy(v => v.Id).FirstOrDefault() is { } t && Rnd() < 0.15)
        {
            t.Trade = false;
            Say($"【교역】 {Josa.Iga(t.Name)} 사람을 먹는 나라와는 장사하지 않겠다며 장삿길을 닫았다.");
        }
    }

    /// <summary>굶주림 끝에 죽은 이의 몸이 곳간으로 간다(식인 풍습).</summary>
    internal void EatTheDead(string cause)
    {
        if (!Inst.Cannibalism || cause is not ("굶주림" or "버려짐")) return;
        CommonStore += 3;
        _eatenThisYear++;
    }

    // ───────────────────────── 승리 ─────────────────────────

    /// <summary>지금 조건을 채우는가(몇 해 내리 지켜야 하는지는 따로 센다).</summary>
    public bool VictoryMet(Course c)
    {
        var r = Realm;
        var i = Inst;
        return c switch
        {
            Course.Conquest => r.Stage == Stage.Empire && r.Territory >= 9 && L(c) >= 2,
            Course.Commerce => r.Era >= Era.Industrial && Rivals.Count(v => v.Trade) >= 3 && r.Treasury >= 5 && L(c) >= 3,
            Course.Terror => r.Stage >= Stage.Kingdom && r.Dread >= 80 && L(c) >= 2,
            Course.Tyranny => r.Stage >= Stage.Kingdom && (i.Absolutism || i.Surveillance) && !i.Parliament && !i.Suffrage
                              && r.Legitimacy >= 0.6 && L(c) >= 2,
            _ => r.Era >= Era.Industrial && r.Knowledge >= LearningGoal && i.PublicSchooling,
        };
    }

    /// <summary>승리까지 남은 것(화면에 보여 준다): 조건마다 (설명, 채웠나), 그리고 내리 지킨 해.</summary>
    public (List<(string what, bool ok)> needs, int streak, int hold) VictoryNeeds(Course c)
    {
        var r = Realm;
        var i = Inst;
        int lv = L(c);
        var n = new List<(string, bool)>();
        switch (c)
        {
            case Course.Conquest:
                n.Add(("제국", r.Stage == Stage.Empire));
                n.Add(($"영토 {r.Territory}/9", r.Territory >= 9));
                n.Add(($"정복의 길 {lv}/2", lv >= 2));
                break;
            case Course.Commerce:
                int trade = Rivals.Count(v => v.Trade);
                n.Add(("근대", r.Era >= Era.Industrial));
                n.Add(($"교역국 {trade}/3", trade >= 3));
                n.Add(($"곳간 {r.Treasury:F1}/5", r.Treasury >= 5));
                n.Add(($"교역의 길 {lv}/3", lv >= 3));
                break;
            case Course.Terror:
                n.Add(("왕국", r.Stage >= Stage.Kingdom));
                n.Add(($"공포 {r.Dread:F0}/80", r.Dread >= 80));
                n.Add(($"공포의 길 {lv}/2", lv >= 2));
                break;
            case Course.Tyranny:
                n.Add(("왕국", r.Stage >= Stage.Kingdom));
                n.Add(("절대왕정 또는 감시", i.Absolutism || i.Surveillance));
                n.Add(("의회·보통선거 없음", !i.Parliament && !i.Suffrage));
                n.Add(($"통치자 지지 {r.Legitimacy:P0}/60%", r.Legitimacy >= 0.6));
                n.Add(($"전제의 길 {lv}/2", lv >= 2));
                break;
            default:
                n.Add(("근대", r.Era >= Era.Industrial));
                n.Add(($"지식 {r.Knowledge:F0}/{LearningGoal:N0}", r.Knowledge >= LearningGoal));
                n.Add(("의무교육", i.PublicSchooling));
                break;
        }
        return (n, _victoryStreak[(int)c], Victories[(int)c].Hold);
    }

    /// <summary>승리까지 얼마나 왔나(0~1): 채운 조건의 비율과 내리 지킨 해.</summary>
    public double VictoryProgress(Course c)
    {
        if (Won.Any(w => w.Course == c)) return 1;
        var (needs, streak, hold) = VictoryNeeds(c);
        double met = needs.Count(x => x.ok) / (double)needs.Count;
        return hold <= 1 ? met * 0.99 : 0.7 * met + 0.3 * Math.Min(1, streak / (double)hold);
    }

    void VictoryYear()
    {
        foreach (var v in Victories)
        {
            int k = (int)v.Course;
            _victoryStreak[k] = VictoryMet(v.Course) ? _victoryStreak[k] + 1 : 0;
            if (_victoryStreak[k] < v.Hold || Won.Any(w => w.Course == v.Course)) continue;
            Won.Add((v.Course, Year));
            Say($"【승리】 {v.Name}: " + v.Course switch
            {
                Course.Conquest => $"해가 지지 않는 땅. {RulerTitle}의 깃발이 영토 {Realm.Territory}곳에 꽂혀 있다.",
                Course.Commerce => "온 세상의 돈이 우리 항구를 거쳐 간다. 칼 대신 장부가 이겼다.",
                Course.Terror => "이웃들은 우리 이름만 들어도 문을 걸어 잠근다. 아무도 맞서려 하지 않는다.",
                Course.Tyranny => $"한 사람의 뜻이 곧 법인 나라가 반세기를 버텼다. 누구도 {RulerTitle}에게 묻지 않는다.",
                _ => "별과 원자의 이치가 우리 손에 있다. 가장 멀리 본 나라가 되었다.",
            });
        }
    }
}
