using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 땅(지도), 기술 나무, 자원: 지도가 씨앗으로만 정해지는지, 땅을 얻고 잃는 일이 지도와 맞는지,
/// 기술이 제도와 자원을 여는지, 자원이 모자라면 무슨 일이 나는지를 상태를 만들어 강제로 밟는다.
/// </summary>
public class MapTechTests
{
    static bool Logged(Simulation sim, string part) => EstateTests.Logged(sim, part);
    static InstitutionDef Def(string key) => Catalog.All.First(d => d.Key == key);

    /// <summary>원시의 기술만 아는 채로 시대만 앞서 간 나라(기술의 문턱을 보기 위한 상태).</summary>
    static Simulation Untaught(Stage stage, Era era)
    {
        var sim = EstateTests.State(1, stage, Era.Primitive, 20000, 2);
        sim.Realm.Era = era;
        return sim;
    }

    // ───────── 지도 ─────────

    [Fact]
    public void 지도는_씨앗만으로_정해지고_모든_땅이_이어져_있다()
    {
        foreach (int seed in new[] { 1, 2, 3, 7, 42 })
        {
            var a = WorldMap.Generate(seed);
            var b = WorldMap.Generate(seed);
            Assert.Equal(a.Cell, b.Cell);
            Assert.Equal(a.Provinces.Select(p => p.Name + p.Terrain + p.Deposit), b.Provinces.Select(p => p.Name + p.Terrain + p.Deposit));
            Assert.Equal(a.Capital, b.Capital);

            Assert.True(a.Provinces.Count >= 8, $"씨앗 {seed}: 땅이 너무 적다 ({a.Provinces.Count})");
            Assert.Equal(0, a[a.Capital].Owner);
            Assert.Equal(Resource.None, a[a.Capital].Deposit);
            Assert.All(a.Provinces.Where(p => p.Id != a.Capital), p => Assert.Equal(-1, p.Owner));
            Assert.Equal(a.Provinces.Count, a.Provinces.Select(p => p.Name).Distinct().Count());

            // 도성에서 걸어서(맞닿은 땅을 따라) 모든 땅에 닿는다.
            var seen = new HashSet<int> { a.Capital };
            var queue = new Queue<int>(seen);
            while (queue.Count > 0)
                foreach (int n in a[queue.Dequeue()].Adjacent)
                    if (seen.Add(n)) queue.Enqueue(n);
            Assert.Equal(a.Provinces.Count, seen.Count);

            // 시대마다의 자원이 어딘가에는 묻혀 있다.
            foreach (var d in ResourceCatalog.All)
                Assert.Contains(a.Provinces, p => p.Deposit == d.Kind);
        }
        Assert.NotEqual(WorldMap.Generate(1).Cell, WorldMap.Generate(2).Cell);
    }

    [Fact]
    public void 땅을_얻고_잃으면_영토_수와_지도가_함께_움직이고_도성은_잃지_않는다()
    {
        var sim = EstateTests.State(1, Stage.CityState, Era.Ancient, 5000, 1);
        sim.SyncMap();
        Assert.Single(sim.Lands(0));

        var got = sim.GainLand(null);
        Assert.NotNull(got);
        Assert.Equal(2, sim.Realm.Territory);
        Assert.Equal(2, sim.Lands(0).Count());
        Assert.Contains(sim.Map.Capital, got!.Adjacent);

        var lost = sim.LoseLand(null);
        Assert.Equal(got.Id, lost!.Id);
        Assert.Equal(-1, lost.Owner);
        Assert.Equal(1, sim.Realm.Territory);

        Assert.Null(sim.LoseLand(null));
        Assert.Equal(0, sim.Map[sim.Map.Capital].Owner);
        Assert.Equal(1, sim.Realm.Territory);
    }

    [Fact]
    public void 지도는_해마다_나라의_상태에_맞춰진다()
    {
        var sim = EstateTests.State(1, Stage.Kingdom, Era.Medieval, 30000, 5);
        sim.RivalsYear();
        sim.SyncMap();
        Assert.Equal(5, sim.Lands(0).Count());
        Assert.NotEmpty(sim.Rivals);
        Assert.All(sim.Rivals, v =>
        {
            Assert.True(v.Seat >= 0);
            Assert.Equal(v.Id, sim.Map[v.Seat].Owner);
        });
        Assert.Equal(sim.Rivals.Count, sim.Rivals.Select(v => v.Seat).Distinct().Count());

        // 이웃 나라는 힘만큼 해마다 한 조각씩 넓힌다.
        var strong = sim.Rivals[0];
        strong.Strength = 1.5;
        for (int k = 0; k < 6; k++) sim.SyncMap();
        Assert.True(sim.Lands(strong.Id).Count() >= 3);

        // 사라진 나라의 땅은 주인 없는 땅으로 돌아간다.
        sim.Rivals.Remove(strong);
        sim.SyncMap();
        Assert.Empty(sim.Lands(strong.Id));

        // 몰락하면 영토가 줄고, 지도도 따라 준다.
        sim.Realm.Territory = 9;
        sim.SyncMap();
        Assert.Equal(9, sim.Lands(0).Count());
        sim.Collapse("시험", kind: "결속");
        Assert.Equal(sim.Realm.Territory, sim.Lands(0).Count());
        Assert.True(sim.Realm.Territory < 9);
        Assert.Equal(0, sim.Map[sim.Map.Capital].Owner);
    }

    [Fact]
    public void 사람이_차면_맞닿은_빈_땅을_개척한다()
    {
        var sim = EstateTests.State(1, Stage.CityState, Era.Ancient, 5000, 1);
        sim.SyncMap();
        double cap = sim.LandCapacity();

        sim.Realm.Pop = 0.2 * cap; // 땅이 남으면 옮겨 가지 않는다
        for (int k = 0; k < 300; k++) sim.Settle(cap);
        Assert.Equal(1, sim.Realm.Territory);

        sim.Realm.Pop = 0.9 * cap;
        for (int k = 0; k < 300 && sim.Realm.Territory == 1; k++) sim.Settle(cap);
        Assert.Equal(2, sim.Realm.Territory);
        Assert.Equal(2, sim.Lands(0).Count());
        Assert.True(Logged(sim, "【개척】"));
    }

    // ───────── 기술 ─────────

    [Fact]
    public void 기술_나무는_앞뒤가_맞는다()
    {
        var keys = TechTree.All.Select(t => t.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var t in TechTree.All)
            foreach (string need in t.Requires)
            {
                var pre = TechTree.Find(need);
                Assert.NotNull(pre);
                Assert.True(pre!.Era <= t.Era, $"{t.Name}의 앞선 기술 {pre.Name}이 더 늦은 시대에 있다");
            }
        // 제도를 여는 기술은 그 제도보다 늦은 시대의 것이 아니다.
        foreach (var (inst, tech) in TechTree.Gate)
        {
            var d = Def(inst);
            Assert.True(TechTree.Find(tech)!.Era <= d.Era, $"{d.Name}을 여는 기술이 제도보다 늦다");
        }
        // 자원을 알아보는 기술이 자원마다 있다.
        foreach (var r in ResourceCatalog.All)
            Assert.Equal(r.Kind, TechTree.Find(r.Tech)!.Reveals);
    }

    [Fact]
    public void 기술을_모르면_제도를_들일_수_없고_익히면_열린다()
    {
        var sim = Untaught(Stage.CityState, Era.Ancient);
        var tax = Def("taxation");
        Assert.False(sim.TechAllows(tax));
        Assert.False(sim.Available(tax));
        Assert.Contains("셈과 저울", sim.WhyUnavailable(tax));

        Assert.False(sim.SetResearch("steam"));  // 시대가 멀다
        Assert.False(sim.SetResearch("script")); // 앞선 기술(셈)이 없다
        Assert.True(sim.SetResearch("counting"));

        sim.ResearchYear(5);
        Assert.False(sim.Knows("counting"));
        Assert.Equal(5, sim.ResearchProgress, 6);
        sim.ResearchYear(8);
        Assert.True(sim.Knows("counting"));
        Assert.True(Logged(sim, "【기술】 셈과 저울"));
        Assert.True(sim.Available(tax));
        Assert.Equal("", sim.WhyUnavailable(tax));
    }

    [Fact]
    public void 파고들던_기술을_접으면_쌓은_것의_절반을_잃는다()
    {
        var sim = Untaught(Stage.CityState, Era.Ancient);
        Assert.True(sim.SetResearch("bronze"));
        sim.ResearchYear(10);
        Assert.Equal(10, sim.ResearchProgress, 6);
        Assert.True(sim.SetResearch("bronze")); // 같은 것을 다시 고르면 그대로다
        Assert.Equal(10, sim.ResearchProgress, 6);
        Assert.True(sim.SetResearch("counting"));
        Assert.Equal(5, sim.ResearchProgress, 6);
        Assert.Equal("counting", sim.Research);
    }

    [Fact]
    public void 고르지_않아도_가장_쉬운_기술부터_익히고_시대의_것을_다_익히면_멈춘다()
    {
        var sim = Untaught(Stage.CityState, Era.Ancient);
        for (int k = 0; k < 40; k++) sim.ResearchYear(5);
        Assert.All(TechTree.All.Where(t => t.Era <= Era.Ancient && !t.Crossroad), t => Assert.True(sim.Knows(t.Key), t.Name));
        Assert.Single(TechTree.All, t => t.Era == Era.Ancient && t.Crossroad && sim.Knows(t.Key)); // 저절로는 시대마다 한 갈래만 고른다
        Assert.All(TechTree.All.Where(t => t.Era > Era.Ancient), t => Assert.False(sim.Knows(t.Key), t.Name));
        Assert.Null(sim.Research);
        Assert.Equal(0, sim.ResearchProgress);
    }

    // ───────── 자원 ─────────

    [Fact]
    public void 자원은_기술이_있어야_알아보고_제_땅에서_나면_온전히_장삿길로는_절반만_쓴다()
    {
        var sim = Untaught(Stage.CityState, Era.Ancient);
        sim.RivalsYear();
        sim.SyncMap();
        var v = sim.Rivals[0];
        foreach (var p in sim.Map.Provinces.Where(p => p.Deposit == Resource.Iron)) p.Owner = -1;
        var mine = sim.Map.Provinces.First(p => p.Deposit == Resource.Iron);

        mine.Owner = 0;
        Assert.False(sim.Revealed(Resource.Iron));
        Assert.Equal(0, sim.Supply(Resource.Iron)); // 깔고 앉아 있어도 모르면 못 쓴다

        sim.GrantTechs(Era.Ancient);
        Assert.Equal(1, sim.Supply(Resource.Iron));
        sim.Discover(Resource.Iron);
        Assert.True(Logged(sim, $"【발견】 우리 땅 {mine.Name}에서"));

        mine.Owner = v.Id;
        v.Trade = false;
        foreach (var x in sim.Rivals) if (x != v) x.Trade = false;
        Assert.Equal(0, sim.Supply(Resource.Iron));
        Assert.Equal(Resource.Iron, sim.Coveted(v)); // 살 길이 없으면 탐낸다
        v.Trade = true;
        Assert.Equal(0.5, sim.Supply(Resource.Iron));
        Assert.Equal(Resource.None, sim.Coveted(v)); // 사서 쓰는 동안에는 탐내지 않는다
        sim.Discover(Resource.Iron);
        Assert.True(Logged(sim, "우리 땅에서는 나지 않는다"));
        Assert.True(Logged(sim, $"가진 곳은 {v.Name}"));
    }

    [Fact]
    public void 석유를_사서_쓰는_나라는_석유_파동을_맞는다()
    {
        var own = EstateTests.State(1, Stage.Empire, Era.Modern, 200000, 4);
        own.SyncMap();
        own.Map.Provinces.First(p => p.Deposit == Resource.Oil).Owner = 0;
        for (int k = 0; k < 400; k++) own.ResourceEvents();
        Assert.False(Logged(own, "【석유 파동】"));

        var sim = EstateTests.State(1, Stage.Empire, Era.Modern, 200000, 4);
        sim.SyncMap();
        foreach (var p in sim.Map.Provinces.Where(p => p.Deposit == Resource.Oil)) p.Owner = -1;
        sim.Realm.Treasury = 5;
        double before = sim.Realm.Unrest;
        for (int k = 0; k < 400 && !Logged(sim, "【석유 파동】"); k++) sim.ResourceEvents();
        Assert.True(Logged(sim, "【석유 파동】"));
        Assert.Equal(4, sim.Realm.Treasury, 6);
        Assert.True(sim.Realm.Unrest > before);
    }

    [Fact]
    public void 우라늄이_없으면_핵무장을_하지_못한다()
    {
        var sim = EstateTests.State(1, Stage.Empire, Era.Modern, 200000, 4);
        sim.SyncMap();
        var nuclear = Def("nuclear");
        foreach (var p in sim.Map.Provinces.Where(p => p.Deposit == Resource.Uranium)) p.Owner = -1;
        Assert.False(sim.Available(nuclear));
        Assert.Equal("우라늄 없음", sim.WhyUnavailable(nuclear));

        sim.Map.Provinces.First(p => p.Deposit == Resource.Uranium).Owner = 0;
        Assert.True(sim.Available(nuclear));
    }

    [Fact]
    public void 아쉬운_자원을_가진_이웃에게서_이기면_그_땅을_빼앗는다()
    {
        var sim = EstateTests.State(1, Stage.Kingdom, Era.Medieval, 30000, 2);
        sim.RivalsYear();
        sim.SyncMap();
        var v = sim.Rivals[0];
        foreach (var p in sim.Map.Provinces.Where(p => p.Deposit == Resource.Iron)) p.Owner = -1;
        // 우리 땅과 맞닿은 곳에 철이 나는 그들의 땅을 만든다.
        var mineIds = sim.Lands(0).Select(p => p.Id).ToHashSet();
        var target = sim.Map.Provinces.First(p => p.Owner != 0 && p.Id != v.Seat && sim.Rivals.All(x => x.Seat != p.Id) && p.Adjacent.Any(mineIds.Contains));
        target.Deposit = Resource.Iron;
        target.Owner = v.Id;

        var got = sim.GainLand(v, Resource.Iron);
        Assert.Equal(target.Id, got!.Id);
        Assert.Equal(0, target.Owner);
        Assert.Equal(1, sim.OwnDeposits(Resource.Iron));
        Assert.Equal(1, sim.Supply(Resource.Iron));
    }

    // ───────── 균형을 고치며 생긴 갈래 ─────────

    [Fact]
    public void 한_번_깎은_돈은_한동안_다시_깎지_못한다()
    {
        var sim = EstateTests.State(1, Stage.Kingdom, Era.Medieval, 30000, 3);
        sim.Inst.Taxation = sim.Inst.Coinage = true;
        for (int k = 0; k < 3; k++)
        {
            sim.Realm.Treasury = -20;
            sim.RealmYear();
        }
        Assert.Equal(1, sim.Log.Count(e => e.Text.Contains("【화폐 가치 절하】")));
    }

    [Fact]
    public void 묶어_둘_힘이_없는_나라는_귀족_반란으로_갈라진다()
    {
        bool split = false;
        for (int seed = 1; seed <= 40 && !split; seed++)
        {
            var sim = EstateTests.State(seed, Stage.Kingdom, Era.Medieval, 30000, 4);
            sim.SyncMap();
            sim.Realm.Cohesion = 0.2;
            sim.NobleRevolt();
            Assert.True(Logged(sim, "【귀족 반란】"));
            if (sim.Realm.Falls.Count == 0) continue;
            split = true;
            Assert.EndsWith("|분열", sim.Realm.Falls.Single());
            Assert.Equal(sim.Realm.Territory, sim.Lands(0).Count());
        }
        Assert.True(split, "마흔 판 가운데 한 번도 갈라지지 않았다");

        // 결속이 단단하면 한 지방으로 끝난다.
        for (int seed = 1; seed <= 20; seed++)
        {
            var firm = EstateTests.State(seed, Stage.Kingdom, Era.Medieval, 30000, 4);
            firm.Realm.Cohesion = 0.8;
            firm.NobleRevolt();
            Assert.Empty(firm.Realm.Falls);
        }
    }

    [Fact]
    public void 백성은_땅마다_나뉘어_살고_도시화하면_도성으로_몰린다()
    {
        var sim = EstateTests.State(1, Stage.Kingdom, Era.Medieval, 30000, 4);
        sim.SyncMap();
        var pops = sim.LandPops();
        Assert.Equal(sim.Map.Provinces.Count, pops.Length);
        Assert.Equal(sim.Realm.Pop, pops.Sum(), 3);
        Assert.All(sim.Map.Provinces, p => Assert.Equal(p.Owner == 0, pops[p.Id] > 0));

        double before = pops[sim.Map.Capital];
        sim.Realm.Urban = 0.6;
        Assert.True(sim.LandPops()[sim.Map.Capital] > before);

        // 부족은 아직 백성이라 할 것이 없다.
        Assert.All(new Simulation(1).LandPops(), v => Assert.Equal(0, v));
        Assert.True(sim.Map.Provinces.Count >= 20, $"땅이 {sim.Map.Provinces.Count}개뿐이다");
    }

    [Fact]
    public void 조사_로는_받침에_따라_붙는다()
    {
        Assert.Equal("솔터로", Josa.Ro("솔터"));
        Assert.Equal("붉은벌로", Josa.Ro("붉은벌"));
        Assert.Equal("샘밭으로", Josa.Ro("샘밭"));
    }
}
