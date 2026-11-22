namespace Instinct.Sim;

/// <summary>결정에 딸린 선택지 하나.</summary>
public sealed record Choice(string Label, string Hint);

/// <summary>플레이어의 답을 기다리는 일. 답하지 않고 두면 기한이 지나 저절로 닫힌다(마지막 선택지를 고른 것으로 친다).</summary>
public sealed class Decision
{
    public int Id;
    /// <summary>adopt(제도 건의) · relief(기근 구휼) · conquest(빼앗은 땅의 처분) · tribute(침공 앞의 공물).</summary>
    public string Kind = "";
    /// <summary>무엇에 대한 결정인가: 제도 키, 지방 Id, 이웃 나라 Id.</summary>
    public string Subject = "";
    public string Title = "", Text = "";
    public IReadOnlyList<Choice> Choices = Array.Empty<Choice>();
    public int Tick, ExpiresTick;
}

/// <summary>연대기 한 줄의 무게와 갈래. 화면이 무엇을 크게 알리고 무엇을 접어 둘지 정하는 데 쓴다.</summary>
public static class Chronicle
{
    static readonly HashSet<string> Grave = new()
    {
        "시대", "군장사회", "도시국가", "왕국", "제국", "몰락", "왕조 교체", "정복", "침공", "함락", "총력전", "혁명", "핵전쟁", "도성 약탈", "승리",
    };

    static readonly HashSet<string> Light = new() { "제도", "입신", "개척", "건설", "교역", "발견", "화친", "공장", "빈민가", "파업", "새 세력", "이웃의 내란", "군대" };

    static readonly Dictionary<string, string> Kinds = new()
    {
        ["전쟁"] = "전쟁", ["총력전"] = "전쟁", ["정복"] = "전쟁", ["침공"] = "전쟁", ["전투"] = "전쟁", ["함락"] = "전쟁", ["계승 전쟁"] = "전쟁",
        ["종교 전쟁"] = "전쟁", ["도성 약탈"] = "전쟁", ["군대"] = "전쟁", ["핵전쟁"] = "전쟁",
        ["기근"] = "재앙", ["역병"] = "재앙", ["기후 재난"] = "재앙", ["공황"] = "재앙", ["석유 파동"] = "재앙", ["화폐 가치 절하"] = "재앙",
        ["기술"] = "기술", ["갈림길"] = "기술", ["발견"] = "기술", ["시대"] = "기술",
        ["건설"] = "살림", ["개척"] = "살림", ["교역"] = "살림", ["공장"] = "살림", ["빈민가"] = "살림",
        ["이웃 나라"] = "외교", ["새 세력"] = "외교", ["이웃의 내란"] = "외교", ["화친"] = "외교", ["혼인 동맹"] = "외교", ["독립"] = "외교",
    };

    /// <summary>문장 머리의 【꼬리표】. 없으면 빈 문자열.</summary>
    public static string Tag(string text)
    {
        if (!text.StartsWith('【')) return "";
        int end = text.IndexOf('】');
        return end > 1 ? text[1..end] : "";
    }

    /// <summary>무게: 3 나라의 운명이 바뀐 일, 2 큰일, 1 알아 둘 일, 0 자잘한 일.</summary>
    public static int Weight(LogEntry e)
    {
        if (e.Kind != LogKind.Major) return 0;
        string tag = Tag(e.Text);
        if (tag.Length == 0) return 1;
        if (Grave.Contains(tag)) return 3;
        return Light.Contains(tag) ? 1 : 2;
    }

    /// <summary>갈래: 전쟁 · 재앙 · 기술 · 살림 · 외교 · 정치(그 밖의 꼬리표) · 사람(꼬리표 없는 이야기).</summary>
    public static string Category(LogEntry e)
    {
        string tag = Tag(e.Text);
        if (tag.Length == 0) return "사람";
        return Kinds.TryGetValue(tag, out var k) ? k : "정치";
    }
}

// 결정: 신하들이 건의하고, 재앙과 승리가 물음을 던진다. 플레이어가 다스릴 때(PlayerRules)만 생긴다.
public sealed partial class Simulation
{
    /// <summary>
    /// 플레이어가 직접 다스리는가. 켜면 제도는 건의(Decision)로 올라오고, 이웃 나라의 침공은 지도 위의 군대로 벌어지며,
    /// 치는 전쟁은 플레이어가 군대를 움직여야만 일어난다. 끄면(시험·자동 실행) 나라가 알아서 싸운다.
    /// </summary>
    public bool PlayerRules;

    /// <summary>켜면 신하들이 해마다 들일 수 있는 제도를 건의(결정)로 올린다. 기본은 꺼짐: 제도는 법전에서 플레이어가 고른다.</summary>
    public bool Proposals;

    public readonly List<Decision> Pending = new();
    readonly Dictionary<string, int> _declinedUntil = new();
    int _nextDecision = 1;
    int _nextProposalYear;

    Decision Ask(string kind, string subject, string title, string text, int seasons, params Choice[] choices)
    {
        var d = new Decision
        {
            Id = _nextDecision++, Kind = kind, Subject = subject, Title = title, Text = text, Choices = choices,
            Tick = Tick, ExpiresTick = Tick + seasons,
        };
        Pending.Add(d);
        return d;
    }

    /// <summary>해마다: 들일 수 있게 된 제도 하나를 신하들이 건의한다. 한 번에 하나만, 답한 뒤 몇 해는 쉰다.</summary>
    internal void ProposeYear()
    {
        if (!PlayerRules || Year < _nextProposalYear || Pending.Any(d => d.Kind == "adopt")) return;
        // 식인 풍습은 굶주린 해에만 누군가 입에 올린다.
        var def = Catalog.All.FirstOrDefault(d => !d.Get(Inst) && Available(d) && _declinedUntil.GetValueOrDefault(d.Key) <= Year
                                                 && (d.Key != "cannibalism" || _famineLogged || Realm.Pop > 0 && _crisisThisYear));
        if (def == null) return;
        var pv = PreviewReform(def);
        string who = Realm.Stage == Stage.Tribe ? "어른들이 모닥불 앞에서 말을 꺼냈습니다" : "신하들이 건의를 올렸습니다";
        Ask("adopt", def.Key, $"{def.Name} — 도입할까요?",
            $"{who}: {Josa.Eul(def.Name)} 들이자고 합니다.\n\n얻는 것: {def.Gain}.\n치르는 값: {def.Cost}.",
            12,
            new Choice("도입한다", pv.Text),
            new Choice("물리친다", "15년 동안 다시 건의되지 않는다"));
    }

    /// <summary>결정에 답한다. 없는 결정이거나 없는 선택지면 false.</summary>
    public bool Decide(int id, int choice)
    {
        _mute++; // 결정 안에서 벌어지는 일(제도 도입)은 따로 적지 않는다
        bool ok;
        try { ok = DecideCore(id, choice); }
        finally { _mute--; }
        if (ok) Wrote("decide", id.ToString(), choice.ToString());
        return ok;
    }

    bool DecideCore(int id, int choice)
    {
        var d = Pending.FirstOrDefault(x => x.Id == id);
        if (d == null || choice < 0 || choice >= d.Choices.Count) return false;
        Pending.Remove(d);
        var r = Realm;
        switch (d.Kind)
        {
            case "adopt":
                _nextProposalYear = Year + 2;
                if (choice == 0 && Catalog.Find(d.Subject) is { } def) Enact(def, true);
                else _declinedUntil[d.Subject] = Year + 15;
                break;

            case "relief":
                if (choice == 0)
                {
                    r.Treasury -= 1;
                    r.Unrest = Clamp01(r.Unrest - 0.1);
                    r.LegitShock -= 0.06;
                    Say("나라의 곳간이 열렸다. 죽 한 그릇을 받아 든 이들은 그 손을 오래 기억할 것이다.");
                }
                else if (choice == 1)
                {
                    r.Unrest = Clamp01(r.Unrest - 0.12);
                    r.Inequality = Clamp01(r.Inequality - 0.03);
                    r.Grievance[(int)Estate.Nobles] = Math.Min(1, r.Grievance[(int)Estate.Nobles] + 0.25);
                    Say("굶는 이들을 먹이려고 귀족들의 곳간을 헐었다. 백성은 절했고, 곳간의 주인들은 그 일을 잊지 않았다.");
                }
                else
                {
                    r.Grievance[(int)Estate.Commoners] = Math.Min(1, r.Grievance[(int)Estate.Commoners] + 0.1);
                    Minor("굶는 이들에게 나라는 아무것도 주지 않았다.");
                }
                break;

            case "conquest":
                {
                    var land = Map[int.Parse(d.Subject)];
                    if (choice == 1)
                    {
                        r.Treasury += 0.8;
                        r.Atrocities++;
                        r.Cohesion = Clamp01(r.Cohesion - 0.02);
                        foreach (var v in Rivals) v.Hostility = Clamp01(v.Hostility + 0.08);
                        Say($"【학살】 {land.Name}의 곳간은 비워졌고 집들은 탔다. 군사들은 무거운 자루를 메고 돌아왔고, 이웃 나라들이 그 연기를 보았다.");
                    }
                    else if (choice == 2)
                    {
                        r.Treasury += 0.4;
                        r.Atrocities++;
                        r.Share[(int)Estate.Bound] = Math.Min(0.5, r.Share[(int)Estate.Bound] + 0.03);
                        r.Inequality = Clamp01(r.Inequality + 0.02);
                        Say($"{land.Name}의 사람들이 줄에 묶여 끌려왔다. 밭과 광산에 일손이 늘었고, 밤마다 우는 소리도 늘었다.");
                    }
                    else
                    {
                        r.Treasury -= 0.2;
                        Minor($"{land.Name}의 사람들을 백성으로 받아들였다.");
                    }
                    break;
                }

            case "tribute":
                {
                    var foe = Rivals.FirstOrDefault(v => v.Id == int.Parse(d.Subject));
                    if (foe == null) break;
                    if (choice == 0 && r.Treasury >= 1)
                    {
                        r.Treasury -= 1;
                        r.LegitShock += 0.04;
                        foe.Strength += 0.03;
                        foe.Hostility = Clamp01(foe.Hostility - 0.1);
                        foe.TreatyUntil = Math.Max(foe.TreatyUntil, Year + 3);
                        Say($"{Josa.Iga(foe.Name)} 국경에 군대를 모으자 {Josa.Eun(RulerTitle)} 공물을 바쳐 물렸다.");
                    }
                    else Invade(foe);
                    break;
                }
        }
        return true;
    }

    /// <summary>철마다: 기한이 지난 결정은 마지막 선택지(물리친다·내버려 둔다·맞선다)로 닫힌다.</summary>
    internal void ExpireDecisions()
    {
        foreach (var d in Pending.Where(d => Tick >= d.ExpiresTick).ToList())
        {
            if (d.Kind == "adopt")
            {
                Pending.Remove(d);
                _declinedUntil[d.Subject] = Year + 5;
                _nextProposalYear = Year + 1;
            }
            else Decide(d.Id, d.Kind == "conquest" ? 0 : d.Choices.Count - 1);
        }
    }

    /// <summary>기근이 든 해에 곳간을 열지 묻는다.</summary>
    internal void AskRelief(double dead)
    {
        if (!PlayerRules || Pending.Any(d => d.Kind == "relief")) return;
        Ask("relief", "", "기근 — 곳간을 열까요?",
            $"흉년으로 {Num(dead)}명이 굶어 죽었습니다. 성문 앞에 사람들이 모여 있습니다.",
            3,
            new Choice("나라의 곳간을 연다", "국고 −1.0 · 불만이 가라앉고 통치자 지지가 회복된다"),
            new Choice("귀족의 곳간을 헌다", "돈은 들지 않는다 · 귀족이 크게 반발한다"),
            new Choice("내버려 둔다", "평민의 분노가 쌓인다"));
    }

    void AskConquest(Province land)
    {
        if (!PlayerRules) return;
        var choices = new List<Choice>
        {
            new("백성으로 받아들인다", "국고 −0.2"),
            new("약탈한다", "국고 +0.8 · 학살로 기록된다 · 이웃 나라들의 적의가 오른다"),
        };
        if (Inst.Slavery) choices.Add(new Choice("노예로 끌고 온다", "국고 +0.4 · 예속민이 는다 · 학살로 기록된다"));
        Ask("conquest", land.Id.ToString(), $"{land.Name} — 빼앗은 땅을 어떻게 할까요?",
            $"{Josa.Iga(land.Name)} 우리 손에 들어왔습니다. 군사들이 명을 기다립니다.", 4, choices.ToArray());
    }
}
