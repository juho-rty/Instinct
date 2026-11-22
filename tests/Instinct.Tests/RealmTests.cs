using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 나라 층(Realm): 단계·시대의 진행, 몰락, 그리고 나라의 재앙이 이름 있는 사람에게 닿는 경로를
/// 상태를 직접 만들어 강제로 밟는다.
/// </summary>
public class RealmTests
{
    static bool Logged(Simulation sim, string part, int from = 0) =>
        sim.Log.Skip(from).Any(e => e.Text.Contains(part));

    /// <summary>한 철을 돌린 뒤, 어른을 채워 넣고 원하는 단계·시대의 나라로 만든다.</summary>
    static Simulation State(int seed, Stage stage, Era era, double pop, int territory)
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
        r.DynastyClan = sim.Chief!.ClanId;
        return sim;
    }

    // ───────── 단계 ─────────

    [Fact]
    public void 농경과_서른_명이면_군장사회가_된다()
    {
        var sim = State(1, Stage.Tribe, Era.Primitive, 0, 1);
        sim.RealmYear();
        Assert.Equal(Stage.Chiefdom, sim.Realm.Stage);
        Assert.True(sim.Realm.Pop > 100);
        Assert.True(Logged(sim, "【군장사회】"));
    }

    [Fact]
    public void 농경이_없으면_부족에_머문다()
    {
        var sim = State(1, Stage.Tribe, Era.Primitive, 0, 1);
        sim.Inst.Agriculture = false;
        sim.RealmYear();
        Assert.Equal(Stage.Tribe, sim.Realm.Stage);
    }

    [Fact]
    public void 조세와_백성이_차면_도시국가가_되고_이웃_무리는_사라진다()
    {
        var sim = State(1, Stage.Chiefdom, Era.Ancient, 3500, 1);
        sim.Inst.Taxation = true;
        Assert.NotEmpty(sim.Neighbors);
        sim.RealmYear();
        Assert.Equal(Stage.CityState, sim.Realm.Stage);
        Assert.Empty(sim.Neighbors);
        Assert.False(sim.Tribal);
    }

    [Fact]
    public void 조세가_없으면_도시국가가_되지_못한다()
    {
        var sim = State(1, Stage.Chiefdom, Era.Ancient, 3500, 1);
        sim.RealmYear();
        Assert.Equal(Stage.Chiefdom, sim.Realm.Stage);
    }

    [Fact]
    public void 땅과_백성이_차면_왕국과_제국이_된다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 30000, 5);
        sim.RealmYear();
        Assert.Equal(Stage.Kingdom, sim.Realm.Stage);

        sim = State(1, Stage.Kingdom, Era.Industrial, 300000, 10);
        sim.RealmYear();
        Assert.Equal(Stage.Empire, sim.Realm.Stage);
        Assert.Equal(Stage.Empire, sim.Realm.PeakStage);
    }

    // ───────── 시대 ─────────

    [Fact]
    public void 지식이_차도_조건_제도가_없으면_시대가_넘어가지_않는다()
    {
        var sim = State(1, Stage.CityState, Era.Ancient, 5000, 1);
        sim.Realm.Knowledge = 400;
        sim.RealmYear();
        Assert.Equal(Era.Ancient, sim.Realm.Era);
        Assert.Contains("문자", sim.NextEraNeeds());

        sim.Inst.Writing = true;
        sim.RealmYear();
        Assert.Equal(Era.Medieval, sim.Realm.Era);
        Assert.True(Logged(sim, "【시대】 중세"));
    }

    [Fact]
    public void 제도는_시대와_단계가_되어야_쓸_수_있다()
    {
        var sim = State(1, Stage.Chiefdom, Era.Ancient, 1000, 1);
        Assert.True(sim.Available(Catalog.Find("taxation")!));
        Assert.False(sim.Available(Catalog.Find("coinage")!));   // 도시국가부터
        Assert.False(sim.Available(Catalog.Find("factories")!)); // 근대부터
    }

    // ───────── 몰락 ─────────

    [Fact]
    public void 제국이_무너지면_두_단계_내려앉고_한동안_다시_커지지_못한다()
    {
        var sim = State(1, Stage.Empire, Era.Industrial, 400000, 9);
        double know = sim.Realm.Knowledge = 1500;
        sim.Collapse("시험");
        var r = sim.Realm;
        Assert.Equal(Stage.CityState, r.Stage);
        Assert.Equal(Stage.Empire, r.PeakStage);
        Assert.Equal(3, r.Territory);
        Assert.InRange(r.Pop, 400000 * 0.5 - 1, 400000 * 0.7 + 1);
        Assert.True(r.Knowledge < know);
        Assert.Equal(1, r.Collapses);
        Assert.True(Logged(sim, "【몰락】 제국이 무너졌다"));

        // 백성과 땅이 왕국의 조건을 넘지만 재건 기간에는 올라가지 못한다.
        for (int y = 0; y < 5; y++) sim.RealmYear();
        Assert.True(r.Stage <= Stage.CityState);
    }

    [Fact]
    public void 군장사회는_더_무너질_곳이_없다()
    {
        var sim = State(1, Stage.Chiefdom, Era.Ancient, 2000, 1);
        sim.Collapse("시험");
        Assert.Equal(Stage.Chiefdom, sim.Realm.Stage);
        Assert.Equal(0, sim.Realm.Collapses);
    }

    [Fact]
    public void 결속이_바닥나고_불만이_높으면_나라가_무너진다()
    {
        var sim = State(2, Stage.Kingdom, Era.Medieval, 60000, 4);
        var r = sim.Realm;
        for (int y = 0; y < 400 && r.Collapses == 0; y++)
        {
            r.Cohesion = 0.05;
            r.Unrest = 0.58; // 봉기 문턱(0.6) 바로 아래: 몰락 판정만 본다
            sim.RealmYear();
        }
        Assert.True(r.Collapses >= 1);
        Assert.True(Logged(sim, "【몰락】"));
    }

    // ───────── 나라의 재앙이 사람에게 닿는다 ─────────

    [Fact]
    public void 마녀사냥은_가장_미움받는_이를_불태우고_고발자는_원한을_품은_자다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        foreach (var p in sim.People) { p.Grudge.Clear(); p.GrudgeWhy.Clear(); }
        var adults = sim.Living.Where(p => p != sim.Chief && p.Age(sim.Tick) >= 20).ToList();
        var victim = adults.First(p => !p.IsMale);
        var accuser = adults.First(p => p != victim);
        accuser.Grudge[victim.Id] = 2;
        accuser.GrudgeWhy[victim.Id] = "남편을 빼앗긴 일로";

        sim.WitchHunt("역병");

        Assert.False(victim.Alive);
        Assert.Equal("화형", victim.DeathCause);
        Assert.True(Logged(sim, "남편을 빼앗긴 일로"));
        Assert.True(Logged(sim, accuser.Name));
        Assert.True(sim.Realm.Pop < 8000);
    }

    [Fact]
    public void 원한이_없으면_마녀사냥은_백성만_태운다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 8000, 1);
        foreach (var p in sim.People) p.Grudge.Clear();
        int before = sim.Living.Count();
        sim.WitchHunt("");
        Assert.Equal(before, sim.Living.Count());
        Assert.True(Logged(sim, "【마녀사냥】"));
    }

    [Fact]
    public void 역병은_백성과_지배_가문을_함께_데려간다()
    {
        var sim = State(1, Stage.CityState, Era.Medieval, 10000, 1);
        var r = sim.Realm;
        for (int k = 0; k < 200 && !sim.DeathsByCause.ContainsKey("역병"); k++) sim.Plague();
        Assert.True(r.Pop < 10000);
        Assert.True(sim.DeathsByCause.GetValueOrDefault("역병") > 0);
        Assert.True(Logged(sim, "【역병】"));
    }

    [Fact]
    public void 학살은_바깥_핏줄을_죽이고_기록에_남는다()
    {
        var sim = State(1, Stage.Kingdom, Era.Industrial, 80000, 4);
        sim.Inst.Nationalism = true;
        var outsiders = sim.Living.Where(p => p != sim.Chief).Take(12).ToList();
        foreach (var o in outsiders) o.Outsider = true;

        sim.Persecution(force: true);

        Assert.Equal(1, sim.Realm.Atrocities);
        Assert.True(sim.Realm.Pop < 80000);
        Assert.Contains(outsiders, o => o.DeathCause == "학살");
        Assert.DoesNotContain(sim.People, p => p.DeathCause == "학살" && !outsiders.Contains(p));
    }

    [Fact]
    public void 봉기가_성공하면_통치자가_처형되고_제도가_무너진다()
    {
        var sim = State(3, Stage.Kingdom, Era.Medieval, 60000, 4);
        var r = sim.Realm;
        var i = sim.Inst;
        var king = sim.Chief!;
        for (int k = 0; k < 500 && king.Alive; k++)
        {
            i.Serfdom = i.Feudalism = true;
            r.Unrest = 1; r.Inequality = 1; r.Legitimacy = 0; r.Cohesion = 0.8;
            sim.Uprising();
        }
        Assert.Equal("처형", king.DeathCause);
        Assert.False(i.Serfdom);
        Assert.False(i.Feudalism);
        Assert.True(Logged(sim, "성공했다"));
    }

    [Fact]
    public void 야심_있는_장수가_있으면_정변이_난다()
    {
        var sim = State(1, Stage.Kingdom, Era.Medieval, 60000, 4);
        var king = sim.Chief!;
        var general = sim.NewPerson(Sex.Male, 35, king.ClanId + 1 <= 3 ? king.ClanId + 1 : 1);
        general.Traits[(int)Trait.Ambition] = 1;
        general.Status = 50;

        sim.Coup();

        Assert.True(Logged(sim, "【정변"));
        Assert.True(sim.Chief == general || !general.Alive, "장수가 자리를 빼앗거나, 실패해 죽어야 한다");
    }

    [Fact]
    public void 나라가_서면_자리는_아들에게_내려간다()
    {
        var sim = State(1, Stage.Tribe, Era.Primitive, 0, 1);
        sim.RealmYear(); // 군장사회
        var chief = sim.Chief!;
        var son = sim.NewPerson(Sex.Male, 20, chief.ClanId);
        son.Father = chief;
        chief.Children.Add(son);
        var strong = sim.NewPerson(Sex.Male, 30, chief.ClanId);
        strong.Status = 99; // 지위로 뽑았다면 이 사내가 되었을 것이다
        chief.DeathTick = sim.Tick;
        chief.DeathCause = "병";

        sim.Step();

        Assert.Equal(son, sim.Chief);
        Assert.True(Logged(sim, "자리를 물려받았다"));
    }

    // ───────── 목록과 결정성 ─────────

    [Fact]
    public void 모든_제도는_목록에_하나씩_있고_얻는_것과_값이_적혀_있다()
    {
        var fields = typeof(Institutions).GetFields().Select(f => f.Name).OrderBy(x => x).ToList();
        Assert.Equal(fields, Catalog.All.Select(d => d.Field).OrderBy(x => x).ToList());
        Assert.Equal(Catalog.All.Count, Catalog.All.Select(d => d.Key).Distinct().Count());
        Assert.All(Catalog.All, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Gain));
            Assert.False(string.IsNullOrWhiteSpace(d.Cost));
            var inst = new Institutions();
            d.Set(inst, true);
            Assert.True(d.Get(inst));
        });
    }

    static (List<string> log, Stage peak) RunAll(int seed, int years)
    {
        var sim = new Simulation(seed);
        for (int t = 0; t < years * 4 && sim.Living.Any(); t++)
        {
            if (sim.Season == 0)
                foreach (var d in Catalog.All.Where(d => !d.Get(sim.Inst) && sim.Available(d)))
                    d.Set(sim.Inst, true);
            sim.Step();
        }
        return (sim.Log.Select(e => $"{e.Tick}|{e.Text}").ToList(), sim.Realm.PeakStage);
    }

    [Fact]
    public void 제도를_모두_켜고_나라까지_가도_같은_씨앗은_같은_역사를_낳는다()
    {
        var a = RunAll(5, 300);
        var b = RunAll(5, 300);
        Assert.Equal(a.log, b.log);
        Assert.True(a.peak >= Stage.Chiefdom, "300년이면 적어도 군장사회에는 닿아야 한다");
    }
}
