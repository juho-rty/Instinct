using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 계층과 개혁: 제도를 들이고 없애는 값과 반발, 분노한 계층의 움직임,
/// 백성에서 솟는 인물과 밀려나는 가문을 상태를 만들어 강제로 밟는다.
/// </summary>
public class EstateTests
{
    internal static bool Logged(Simulation sim, string part, int from = 0) =>
        sim.Log.Skip(from).Any(e => e.Text.Contains(part));

    /// <summary>한 철을 돌린 뒤, 어른을 채워 넣고 원하는 단계·시대의 나라로 만든다.</summary>
    internal static Simulation State(int seed, Stage stage, Era era, double pop, int territory)
    {
        var sim = new Simulation(seed);
        sim.Step();
        int clan = sim.People[0].ClanId;
        for (int k = 0; k < 15; k++)
        {
            sim.NewPerson(Sex.Male, 25 + k, clan);
            sim.NewPerson(Sex.Female, 22 + k, clan);
        }
        sim.Inst.Agriculture = true;
        var r = sim.Realm;
        r.Stage = r.PeakStage = stage;
        r.Era = era;
        sim.GrantTechs(era);
        r.Pop = pop;
        r.Territory = territory;
        if (sim.Chief == null) sim.Step(); // 첫 철에 우두머리가 죽었으면 다음 철에 새로 선다
        r.DynastyClan = sim.Chief!.ClanId;
        return sim;
    }

    static InstitutionDef D(string key) => Catalog.Find(key)!;

    // ───────── 개혁의 값과 반발 ─────────

    [Fact]
    public void 제도를_들이면_곳간과_정통성을_치르고_잃는_계층이_분노한다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        var r = sim.Realm;
        sim.Inst.Taxation = true;
        r.Treasury = 5;
        var pv = sim.PreviewReform(D("serfdom"));
        Assert.True(pv.Adopt);
        Assert.True(pv.Cost > 0);
        Assert.Equal(new[] { Estate.Bound, Estate.Commoners }, pv.Angered);
        Assert.Contains("국고", pv.Text);
        Assert.Contains("예속민", pv.Text);

        double shock = r.LegitShock;
        Assert.True(sim.Enact(D("serfdom"), true));

        Assert.True(sim.Inst.Serfdom);
        Assert.Equal(5 - pv.Cost, r.Treasury, 6);
        Assert.Equal(shock + pv.LegitHit, r.LegitShock, 6);
        Assert.True(r.Grievance[(int)Estate.Bound] > 0);
        Assert.True(r.Grievance[(int)Estate.Commoners] > 0);
        Assert.Equal(0, r.Grievance[(int)Estate.Nobles]);
        Assert.True(Logged(sim, "【제도】 농노제"));
        Assert.True(Logged(sim, "반발한다"));
    }

    [Fact]
    public void 제도를_없애면_누리던_계층이_더_크게_반발한다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        var r = sim.Realm;
        sim.Inst.Taxation = sim.Inst.Feudalism = true;
        r.Treasury = 5;
        double adoptGrief = sim.PreviewReform(D("serfdom")).Grievance;

        var pv = sim.PreviewReform(D("feudalism"));
        Assert.False(pv.Adopt);
        Assert.Equal(new[] { Estate.Nobles }, pv.Angered);
        Assert.True(pv.Grievance > adoptGrief, "가진 것을 빼앗길 때가 더 아프다");

        Assert.True(sim.Enact(D("feudalism"), false));
        Assert.False(sim.Inst.Feudalism);
        Assert.True(r.Grievance[(int)Estate.Nobles] > 0.3);
        Assert.True(sim.EstateAnger(Estate.Nobles) > 0.3);
        Assert.True(Logged(sim, "봉건제를 폐지했다"));
    }

    [Fact]
    public void 바뀌는_것이_없거나_아직_열리지_않은_제도는_값도_치르지_않는다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        sim.Inst.Taxation = true;
        sim.Realm.Treasury = 5;
        Assert.False(sim.Enact(D("factories"), true)); // 근대부터
        Assert.False(sim.Enact(D("serfdom"), false));  // 없는 것을 없앨 수는 없다
        Assert.False(sim.Enact(D("taxation"), true));  // 이미 있다
        Assert.Equal(5, sim.Realm.Treasury);
        Assert.Equal(0, sim.Realm.ReformHeat);
        Assert.False(sim.Inst.Factories);
    }

    [Fact]
    public void 잦은_개혁은_다음_개혁의_값을_올리고_해가_가면_식는다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        sim.Inst.Taxation = true;
        sim.Realm.Treasury = 8;
        var before = sim.PreviewReform(D("guilds"));
        sim.Enact(D("serfdom"), true);
        var after = sim.PreviewReform(D("guilds"));
        Assert.True(after.Cost > before.Cost);
        Assert.True(after.LegitHit > before.LegitHit);
        Assert.Contains("잦은 개혁", after.Text);

        double heat = sim.Realm.ReformHeat;
        sim.RealmYear();
        Assert.True(sim.Realm.ReformHeat < heat);
    }

    [Fact]
    public void 조세가_서기_전에는_곳간_대신_위신으로_치른다()
    {
        var sim = State(1, Stage.Chiefdom, Era.Ancient, 1500, 1);
        var r = sim.Realm;
        r.Treasury = 0;
        var pv = sim.PreviewReform(D("army"));
        Assert.Equal(0, pv.Cost);
        Assert.True(pv.LegitHit > 0);
        Assert.Contains("위신", pv.Text);
        double shock = r.LegitShock;
        Assert.True(sim.Enact(D("army"), true));
        Assert.Equal(0, r.Treasury);
        Assert.True(r.LegitShock > shock);

        // 같은 개혁도 조세가 있으면 곳간에서 나가고, 위신은 덜 깎인다.
        var taxed = State(1, Stage.Chiefdom, Era.Ancient, 1500, 1);
        taxed.Inst.Taxation = true;
        var pv2 = taxed.PreviewReform(D("army"));
        Assert.True(pv2.Cost > 0);
        Assert.True(pv2.LegitHit < pv.LegitHit);
    }

    [Fact]
    public void 부족에서는_우두머리의_지위로_치른다()
    {
        var sim = State(1, Stage.Tribe, Era.Primitive, 0, 1);
        var chief = sim.Chief!;
        chief.Status = 5;
        Assert.True(sim.Enact(D("monogamy"), true));
        Assert.Equal(4.7, chief.Status, 6);
        Assert.Equal(0, sim.Realm.Treasury);
        Assert.Equal(0, sim.Realm.Grievance.Sum());
    }

    [Fact]
    public void 귀족이_잃는_개혁에는_욕심_많은_윗사람이_통치자에게_원한을_품는다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        sim.Inst.Taxation = true;
        sim.Realm.Treasury = 5;
        var chief = sim.Chief!;
        foreach (var p in sim.People) { p.Grudge.Clear(); p.GrudgeWhy.Clear(); }
        var lord = sim.Living.First(p => p != chief && p.IsMale && p.Age(sim.Tick) >= 20);
        lord.Traits[(int)Trait.Greed] = 1;
        lord.Traits[(int)Trait.Ambition] = 1;
        lord.Status = 99;

        sim.Enact(D("lawcode"), true); // 성문법: 평민이 얻고 귀족이 잃는다

        Assert.True(lord.Grudge.GetValueOrDefault(chief.Id) > 0);
        Assert.Contains("성문법", lord.GrudgeWhy[chief.Id]);
        Assert.True(Logged(sim, "눈빛이 달라졌다"));
    }

    [Fact]
    public void 값을_치를_것이_없는_제도_하나를_빼면_모든_제도에_얻는_계층이나_잃는_계층이_있다()
    {
        Assert.All(Catalog.All.Where(d => d.Key != "nuclear"), d =>
            Assert.True(d.Winners.Count + d.Losers.Count > 0, $"{d.Key}: 누가 얻고 누가 잃는지 적혀 있지 않다"));
    }

    // ───────── 분노한 계층의 움직임 ─────────

    [Fact]
    public void 분노한_귀족은_정변이나_반란으로_움직인다()
    {
        var sim = State(1, Stage.Kingdom, Era.Medieval, 60000, 4);
        var r = sim.Realm;
        sim.Inst.Feudalism = true;
        for (int k = 0; k < 200 && !Logged(sim, "귀족들이 움직이기 시작했다"); k++)
        {
            r.Grievance[(int)Estate.Nobles] = 1;
            sim.EstateEvents();
        }
        Assert.True(Logged(sim, "귀족들이 움직이기 시작했다"));
        Assert.True(Logged(sim, "【정변") || Logged(sim, "【귀족 반란】"));
        Assert.True(r.Grievance[(int)Estate.Nobles] < 1);
    }

    [Fact]
    public void 분노한_노동자는_총파업으로_곳간을_멈춘다()
    {
        var sim = State(1, Stage.Kingdom, Era.Industrial, 60000, 4);
        var r = sim.Realm;
        sim.Inst.Factories = true;
        r.Legitimacy = 0.9;
        r.Share[(int)Estate.Workers] = 0.6;
        r.Treasury = 5;
        for (int k = 0; k < 200 && !Logged(sim, "【총파업】"); k++)
        {
            r.Grievance[(int)Estate.Workers] = 1;
            sim.EstateEvents();
        }
        Assert.True(Logged(sim, "【총파업】"));
        Assert.True(r.Treasury < 5);
    }

    [Fact]
    public void 분노한_예속민은_반란을_일으킨다()
    {
        var sim = State(1, Stage.Kingdom, Era.Medieval, 60000, 4);
        var r = sim.Realm;
        sim.Inst.Serfdom = true;
        r.Legitimacy = 0.9;
        r.Share[(int)Estate.Bound] = 0.4;
        for (int k = 0; k < 200 && !Logged(sim, "【농민 반란】"); k++)
        {
            sim.Inst.Serfdom = true;
            r.Grievance[(int)Estate.Bound] = 1;
            sim.EstateEvents();
        }
        Assert.True(Logged(sim, "【농민 반란】"));
    }

    // ───────── 솟는 사람, 밀려나는 가문 ─────────

    [Fact]
    public void 입신한_백성은_새_가문을_열고_오랜_집안의_미움을_산다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        var clans = sim.People.Select(p => p.ClanId).ToHashSet();
        var old = sim.Living.First(p => p != sim.Chief && p.Age(sim.Tick) >= 20);
        old.Traits[(int)Trait.KinBias] = 1;
        old.Status = 50;
        int before = sim.Living.Count();

        var riser = sim.Ascend();

        Assert.True(riser.Alive);
        Assert.DoesNotContain(riser.ClanId, clans);
        Assert.Equal(before + 2, sim.Living.Count()); // 아내와 함께 온다
        Assert.True(old.Grudge.GetValueOrDefault(riser.Id) > 0);
        Assert.Contains("미천한 핏줄", old.GrudgeWhy[riser.Id]);
        Assert.True(Logged(sim, "【입신】"));
    }

    [Fact]
    public void 윗자리가_넘치면_낮은_방계부터_백성_속으로_내려앉는다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        var chief = sim.Chief!;
        int other = sim.People.Select(p => p.ClanId).First(c => c != chief.ClanId);
        for (int k = 0; k < 130; k++) sim.NewPerson(Sex.Male, 25 + k % 20, other).Status = 0;
        Assert.True(sim.Living.Count() > 120);

        int gone = sim.Decline();
        Assert.True(gone > 0);
        Assert.Equal(gone, sim.DeathsByCause.GetValueOrDefault("낙향"));
        for (int k = 0; k < 60 && sim.Living.Count() > 120; k++) sim.Decline();

        Assert.True(sim.Living.Count() <= 120);
        Assert.True(chief.Alive);
        Assert.Equal(0, sim.Decline()); // 넘치지 않으면 아무도 밀려나지 않는다
    }

    [Fact]
    public void 봉기가_성공하면_백성_출신_지도자가_새_왕조를_열_수_있다()
    {
        var sim = State(3, Stage.Kingdom, Era.Medieval, 60000, 4);
        var r = sim.Realm;
        var known = sim.People.ToHashSet();
        for (int k = 0; k < 500 && !Logged(sim, "빈자리에 앉았고"); k++)
        {
            sim.Inst.Serfdom = true;
            r.Pop = 60000;
            r.Unrest = 1; r.Inequality = 1; r.Legitimacy = 0; r.Cohesion = 0.8;
            sim.Uprising();
        }
        Assert.True(Logged(sim, "빈자리에 앉았고"));
        // 마지막 봉기가 그를 앉혔으니, 지금 통치자는 처음에 없던 사람이고 왕조는 그의 집안이다.
        var leader = sim.Chief!;
        Assert.DoesNotContain(leader, known);
        Assert.DoesNotContain(leader.ClanId, known.Select(p => p.ClanId));
        Assert.Equal(leader.ClanId, r.DynastyClan);
        Assert.Equal(sim.Year, r.FoundedYear);
    }

    // ───────── 자동 도입 ─────────

    static (List<string> log, Simulation sim) RunAuto(int seed, int years)
    {
        var sim = new Simulation(seed);
        var policy = new Random(seed * 7919 + 1);
        for (int t = 0; t < years * 4 && sim.Living.Any(); t++)
        {
            if (sim.Season == 0) AutoPolicy.Year(sim, policy);
            sim.Step();
        }
        return (sim.Log.Select(e => $"{e.Tick}|{e.Text}").ToList(), sim);
    }

    [Fact]
    public void 자동_도입으로_돌려도_같은_씨앗은_같은_역사를_낳는다()
    {
        var a = RunAuto(5, 300);
        var b = RunAuto(5, 300);
        Assert.Equal(a.log, b.log);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void 자동_도입은_조세_전의_빚에_갇히지_않고_도시국가에_닿는다(int seed)
    {
        var (_, sim) = RunAuto(seed, 450);
        Assert.True(sim.Realm.PeakStage >= Stage.CityState, $"씨앗 {seed}: {sim.StageName}에서 멈췄다 (곳간 {sim.Realm.Treasury:F1})");
        Assert.NotEmpty(sim.Rivals);
    }
}