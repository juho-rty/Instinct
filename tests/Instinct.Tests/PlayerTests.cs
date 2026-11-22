using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 플레이어가 손대는 것들: 건물과 길, 결정(건의·구휼·정복·공물), 지도 위의 군대.
/// 드물게 터지는 경로는 상태를 만들어 강제로 밟는다.
/// </summary>
public class PlayerTests
{
    static bool Logged(Simulation sim, string part, int from = 0) => EstateTests.Logged(sim, part, from);

    /// <summary>플레이어가 다스리는 왕국. 이웃 나라 하나, 넉넉한 곳간.</summary>
    static (Simulation sim, Rival v) Ruled(int territory = 3)
    {
        var sim = EstateTests.State(1, Stage.Kingdom, Era.Medieval, 30000, territory);
        sim.Inst.Taxation = true;
        sim.Realm.Treasury = 8;
        sim.RivalsYear();
        var v = sim.Rivals[0];
        sim.Rivals.RemoveAll(x => x != v);
        v.Trade = v.Allied = v.Free = false;
        v.TreatyUntil = 0;
        sim.SyncMap();
        sim.PlayerRules = true;
        return (sim, v);
    }

    static void Seasons(Simulation sim, int n)
    {
        for (int k = 0; k < n; k++) sim.Construction();
    }

    // ───────── 지도의 결 ─────────

    [Fact]
    public void 강과_땅의_결은_씨앗만으로_정해진다()
    {
        int rivers = 0;
        foreach (int seed in new[] { 1, 2, 3, 7, 42 })
        {
            var a = WorldMap.Generate(seed);
            var b = WorldMap.Generate(seed);
            Assert.Equal(a.Cell.Length, a.Tile.Length);
            Assert.Equal(a.Tile, b.Tile);
            Assert.Equal(a.RiverCell, b.RiverCell);
            foreach (var river in a.Rivers)
            {
                Assert.True(river.Count >= 2);
                Assert.All(river.Where(c => a.Cell[c] >= 0), c => Assert.True(a.RiverCell[c])); // 바다에 닿은 끝 칸은 강이 아니다
            }
            rivers += a.Rivers.Count;
            // 강이 지나는 땅만 강을 낀 땅이다.
            foreach (var p in a.Provinces.Where(p => p.River))
                Assert.Contains(Enumerable.Range(0, a.Cell.Length), c => a.Cell[c] == p.Id && a.RiverCell[c]);
            Assert.Contains(a.Tile, t => t == Ground.Deep || t == Ground.Shallows);
            Assert.Contains(a.Tile, t => t == Ground.Grass);
        }
        Assert.True(rivers > 0, "어느 씨앗에도 강이 없다");
    }

    // ───────── 건물 ─────────

    [Fact]
    public void 건물은_값을_치르고_몇_철_뒤에_서며_해마다_유지비가_나간다()
    {
        var (sim, _) = Ruled();
        int cap = sim.Map.Capital;
        var def = BuildingCatalog.Of(Building.Granary);
        double before = sim.Realm.Treasury, cost = sim.BuildCost(sim.Map[cap], Building.Granary);
        double upkeep = sim.Upkeep();

        Assert.Equal("", sim.WhyCantBuild(cap, Building.Granary));
        Assert.True(sim.Build(cap, Building.Granary));
        Assert.Equal(before - cost, sim.Realm.Treasury, 6);
        Assert.Equal("건설 중", sim.WhyCantBuild(cap, Building.Granary));

        Seasons(sim, def.Seasons - 1);
        Assert.False(sim.Map[cap].Has(Building.Granary));
        Seasons(sim, 1);
        Assert.True(sim.Map[cap].Has(Building.Granary));
        Assert.Empty(sim.Sites);
        Assert.True(Logged(sim, "【건설】"));
        Assert.Equal(1, sim.Built(Building.Granary));
        Assert.Equal(upkeep + def.Upkeep, sim.Upkeep(), 6);
        Assert.Equal("이미 있음", sim.WhyCantBuild(cap, Building.Granary));
    }

    [Fact]
    public void 지을_수_없는_까닭을_말해_준다()
    {
        var (sim, v) = Ruled();
        int cap = sim.Map.Capital;
        Assert.Contains("부터", sim.WhyCantBuild(cap, Building.Factory));          // 시대가 이르다
        Assert.Equal("우리 영토가 아님", sim.WhyCantBuild(sim.Lands(v.Id).First().Id, Building.Temple));
        Assert.Equal("없는 지역", sim.WhyCantBuild(-1, Building.Temple));
        var inland = sim.Lands(0).FirstOrDefault(p => !p.Coastal);
        if (inland != null)
        {
            sim.Realm.Era = Era.EarlyModern;
            sim.GrantTechs(Era.EarlyModern);
            Assert.Equal("해안 지역이 아님", sim.WhyCantBuild(inland.Id, Building.Harbor));
        }
        sim.Realm.Treasury = 0.1;
        Assert.Contains("국고 부족", sim.WhyCantBuild(cap, Building.Temple));
        Assert.False(sim.Build(cap, Building.Temple));
        sim.Realm.Stage = Stage.Tribe;
        Assert.Equal("군장사회 단계부터 가능", sim.WhyCantBuild(cap, Building.Granary));
    }

    [Fact]
    public void 건물은_저마다_얻는_것과_치르는_값이_있다()
    {
        Assert.Equal(Enum.GetValues<Building>().Length, BuildingCatalog.All.Count);
        Assert.All(BuildingCatalog.All, d =>
        {
            Assert.True(d.Cost > 0 && d.Upkeep > 0 && d.Seasons > 0);
            Assert.NotEmpty(d.Gain);
            Assert.NotEmpty(d.Price);
        });

        var (sim, _) = Ruled();
        var cap = sim.Map[sim.Map.Capital];
        double army = sim.ArmyStrength();
        cap.Buildings.Add(Building.Barracks);
        Assert.True(sim.ArmyStrength() > army);
        Assert.Equal(2, sim.ArmyCap);

        cap.Buildings.Add(Building.Castle);
        double g = sim.Realm.Grievance[(int)Estate.Commoners];
        sim.BuildingsYear();
        Assert.True(sim.Realm.Grievance[(int)Estate.Commoners] > g); // 성은 평민의 등에 선다
        Assert.True(sim.Realm.Corruption > 0);
    }

    [Fact]
    public void 나라가_무너지면_건물도_무너지고_군단은_흩어진다()
    {
        var (sim, _) = Ruled();
        var cap = sim.Map[sim.Map.Capital];
        cap.Buildings.AddRange(new[] { Building.Granary, Building.Temple, Building.Market });
        sim.Build(cap.Id, Building.Library);
        sim.RuinBuildings(1);
        Assert.Empty(cap.Buildings);
        Assert.Empty(sim.Sites);

        Assert.NotNull(sim.RaiseArmy(cap.Id));
        sim.Collapse("시험");
        Assert.DoesNotContain(sim.Armies, a => a.Owner == 0);
    }

    // ───────── 길 ─────────

    [Fact]
    public void 길은_도성에서_맞닿은_우리_땅으로_뻗고_땅을_잃으면_끊긴다()
    {
        var (sim, _) = Ruled();
        var cap = sim.Map[sim.Map.Capital];
        var near = sim.Lands(0).First(p => p.Id != cap.Id && p.Adjacent.Contains(cap.Id));
        Assert.Equal("이미 도로가 있음", sim.WhyCantRoad(cap.Id));
        Assert.Equal("", sim.WhyCantRoad(near.Id));

        int admin = sim.AdminCapacity();
        Assert.True(sim.BuildRoad(near.Id));
        Assert.Equal("건설 중", sim.WhyCantRoad(near.Id));
        Seasons(sim, 12);
        Assert.True(near.Road);
        Assert.Equal(cap.Id, near.RoadTo);
        Assert.Equal(1, sim.RoadCount);
        Assert.True(Logged(sim, "길이 열렸다"));
        Assert.True(sim.AdminCapacity() >= admin);

        // 길이 닿지 않은 땅과만 맞닿은 곳에는 길을 낼 수 없다.
        var far = sim.Map.Provinces.FirstOrDefault(p => p.Owner != 0 && !p.Adjacent.Any(a => sim.Map[a].Owner == 0));
        if (far != null)
        {
            far.Owner = 0;
            Assert.Equal("도로가 있는 땅과 맞닿아야 함", sim.WhyCantRoad(far.Id));
            far.Owner = -1;
        }

        near.Owner = -1;
        sim.Realm.Territory--;
        sim.SyncMap();
        Assert.False(near.Road);
        Assert.Equal(0, sim.RoadCount);
    }

    [Fact]
    public void 바퀴를_모르면_길을_내지_못한다()
    {
        var sim = EstateTests.State(1, Stage.Chiefdom, Era.Primitive, 3000, 2);
        sim.Realm.Treasury = 3;
        sim.SyncMap();
        var near = sim.Lands(0).First(p => p.Id != sim.Map.Capital);
        if (!sim.Knows("wheel")) Assert.Contains("기술 필요", sim.WhyCantRoad(near.Id));
    }

    // ───────── 결정 ─────────

    [Fact]
    public void 제도는_건의로_올라오고_도입하면_들어선다()
    {
        var (sim, _) = Ruled();
        sim.ProposeYear();
        var d = Assert.Single(sim.Pending);
        Assert.Equal("adopt", d.Kind);
        Assert.Contains("도입할까요?", d.Title);
        Assert.Equal(2, d.Choices.Count);
        var def = Catalog.Find(d.Subject)!;
        Assert.False(def.Get(sim.Inst));

        sim.ProposeYear();
        Assert.Single(sim.Pending); // 한 번에 하나만

        Assert.False(sim.Decide(d.Id, 5));
        Assert.True(sim.Decide(d.Id, 0));
        Assert.True(def.Get(sim.Inst));
        Assert.Empty(sim.Pending);
        Assert.False(sim.Decide(d.Id, 0));
    }

    [Fact]
    public void 물리친_건의는_한동안_다시_올라오지_않고_답하지_않으면_닫힌다()
    {
        var (sim, _) = Ruled();
        sim.ProposeYear();
        var d = sim.Pending[0];
        Assert.True(sim.Decide(d.Id, 1));
        Assert.False(Catalog.Find(d.Subject)!.Get(sim.Inst));

        sim.Tick += 12;
        sim.ProposeYear();
        var next = Assert.Single(sim.Pending);
        Assert.NotEqual(d.Subject, next.Subject);

        next.ExpiresTick = sim.Tick;
        sim.ExpireDecisions();
        Assert.Empty(sim.Pending);
        Assert.False(Catalog.Find(next.Subject)!.Get(sim.Inst));
    }

    [Fact]
    public void 플레이어가_다스리지_않으면_건의도_물음도_없다()
    {
        var (sim, _) = Ruled();
        sim.PlayerRules = false;
        sim.ProposeYear();
        sim.AskRelief(100);
        Assert.Empty(sim.Pending);
    }

    [Fact]
    public void 기근의_구휼은_세_갈래다()
    {
        var (sim, _) = Ruled();
        sim.AskRelief(300);
        sim.AskRelief(300);
        var d = Assert.Single(sim.Pending);
        Assert.Equal("relief", d.Kind);
        double t = sim.Realm.Treasury;
        sim.Decide(d.Id, 0);
        Assert.Equal(t - 1, sim.Realm.Treasury, 6);

        sim.AskRelief(300);
        double nobles = sim.Realm.Grievance[(int)Estate.Nobles];
        sim.Decide(sim.Pending[0].Id, 1);
        Assert.True(sim.Realm.Grievance[(int)Estate.Nobles] > nobles);

        // 답하지 않으면 내버려 둔 것이 된다.
        sim.AskRelief(300);
        double commoners = sim.Realm.Grievance[(int)Estate.Commoners];
        sim.Pending[0].ExpiresTick = sim.Tick;
        sim.ExpireDecisions();
        Assert.Empty(sim.Pending);
        Assert.True(sim.Realm.Grievance[(int)Estate.Commoners] > commoners);
    }

    [Fact]
    public void 연대기는_무게와_갈래로_나뉜다()
    {
        var (sim, v) = Ruled();
        Assert.Equal("침공", Chronicle.Tag("【침공】 온다"));
        Assert.Equal("", Chronicle.Tag("그냥 이야기"));

        int cap = sim.Map.Capital;
        sim.Build(cap, Building.Granary);
        Seasons(sim, 4);
        var built = sim.Log.Last(e => e.Text.StartsWith("【건설】"));
        Assert.Equal(1, Chronicle.Weight(built));
        Assert.Equal("살림", Chronicle.Category(built));

        Assert.NotNull(sim.Invade(v));
        var war = sim.Log.Last(e => e.Text.StartsWith("【침공】"));
        Assert.Equal(3, Chronicle.Weight(war));
        Assert.Equal("전쟁", Chronicle.Category(war));

        var minor = sim.Log.First(e => e.Kind != LogKind.Major);
        Assert.Equal(0, Chronicle.Weight(minor));
    }

    // ───────── 군대 ─────────

    [Fact]
    public void 군단은_백성에서_뽑고_흩으면_돌아간다()
    {
        var (sim, v) = Ruled();
        int cap = sim.Map.Capital;
        double pop = sim.Realm.Pop, t = sim.Realm.Treasury;
        var a = sim.RaiseArmy(cap);
        Assert.NotNull(a);
        Assert.Equal(pop - a!.Men, sim.Realm.Pop, 6);
        Assert.Equal(t - 0.5, sim.Realm.Treasury, 6);
        Assert.True(sim.Upkeep() > 0);
        Assert.Contains("군단은 최대 1개", sim.WhyCantRaise(cap));
        Assert.Null(sim.RaiseArmy(cap));
        Assert.Equal("우리 영토가 아님", sim.WhyCantRaise(sim.Lands(v.Id).First().Id));

        var other = sim.Lands(0).First(p => p.Id != cap);
        sim.Armies.Clear();
        Assert.Equal("도성이나 병영이 있는 지역에서만 가능", sim.WhyCantRaise(other.Id));
        other.Buildings.Add(Building.Barracks);
        Assert.Equal("", sim.WhyCantRaise(other.Id));

        sim.Armies.Add(a);
        Assert.True(sim.DisbandArmy(a.Id));
        Assert.Equal(pop, sim.Realm.Pop, 6);
        Assert.False(sim.DisbandArmy(a.Id));

        sim.Realm.Stage = Stage.Chiefdom;
        Assert.Equal("도시국가 단계부터 가능", sim.WhyCantRaise(cap));
    }

    [Fact]
    public void 군단은_맞닿은_땅을_따라_가고_빈_땅에_깃발을_꽂는다()
    {
        var (sim, _) = Ruled();
        var a = sim.RaiseArmy(sim.Map.Capital)!;
        var empty = sim.Map.Provinces.First(p => p.Owner == -1 && p.Adjacent.Any(x => sim.Map[x].Owner == 0));
        var from = sim.Map[empty.Adjacent.First(x => sim.Map[x].Owner == 0)];
        a.At = a.From = from.Id;

        Assert.Equal(new[] { empty.Id }, sim.Route(from.Id, empty.Id));
        Assert.Empty(sim.Route(from.Id, from.Id));
        Assert.False(sim.OrderArmy(a.Id, -3));
        Assert.False(sim.OrderArmy(999, empty.Id));
        Assert.True(sim.OrderArmy(a.Id, empty.Id));

        int land = sim.Realm.Territory;
        sim.ArmiesMove();
        Assert.Equal(empty.Id, a.At);
        Assert.Equal(0, empty.Owner);
        Assert.Equal(land + 1, sim.Realm.Territory);
        Assert.True(Logged(sim, "【개척】"));
        Assert.Equal(-1, a.Goal);
    }

    [Fact]
    public void 남의_땅으로_군단을_보내면_전쟁이고_이기면_그_땅의_처분을_묻는다()
    {
        var (sim, v) = Ruled();
        var a = sim.RaiseArmy(sim.Map.Capital)!;
        a.Men = a.FullMen = 1e9;
        var target = sim.Lands(v.Id).OrderBy(p => p.Id == v.Seat ? 1 : 0).ThenBy(p => p.Id).First();
        a.At = a.From = target.Adjacent[0];
        v.TreatyUntil = sim.Year + 5;
        v.Trade = true;
        Assert.True(sim.OrderArmy(a.Id, target.Id));

        int land = sim.Realm.Territory;
        sim.ArmiesMove();
        Assert.Equal(0, target.Owner);
        Assert.Equal(land + 1, sim.Realm.Territory);
        Assert.True(Logged(sim, "화친을 제 손으로 깼다"));
        Assert.False(v.Trade);
        Assert.Contains(sim.Battles, b => b.Province == target.Id && b.Won);

        var d = Assert.Single(sim.Pending, x => x.Kind == "conquest");
        int atrocities = sim.Realm.Atrocities;
        double t = sim.Realm.Treasury;
        Assert.True(sim.Decide(d.Id, 1));
        Assert.Equal(atrocities + 1, sim.Realm.Atrocities);
        Assert.True(sim.Realm.Treasury > t);
        Assert.True(Logged(sim, "【학살】"));
    }

    [Fact]
    public void 진_군단은_꺾이고_나아가지_못한다()
    {
        var (sim, v) = Ruled();
        var a = sim.RaiseArmy(sim.Map.Capital)!;
        a.Men = a.FullMen = 20;
        v.Strength = 2;
        var target = sim.Lands(v.Id).First();
        int start = target.Adjacent[0];
        a.At = a.From = start;
        sim.OrderArmy(a.Id, target.Id);
        sim.ArmiesMove();
        Assert.Equal(v.Id, target.Owner);
        Assert.True(Logged(sim, "맞붙어 졌다"));
        Assert.Contains(sim.Battles, b => b.Province == target.Id && !b.Won);
    }

    [Fact]
    public void 침공군은_땅을_빼앗고_막아내면_물러간다()
    {
        var (sim, v) = Ruled();
        var inv = sim.Invade(v);
        Assert.NotNull(inv);
        Assert.Null(sim.Invade(v)); // 한 나라에 한 군대
        Assert.Equal(0, sim.Map[inv!.Goal].Owner);
        Assert.NotEqual(sim.Map.Capital, inv.Goal); // 변방부터 친다
        inv.Men = inv.FullMen = 1e9;
        int land = sim.Realm.Territory;
        for (int k = 0; k < 40 && sim.Realm.Territory == land; k++) sim.ArmiesMove();
        Assert.Equal(land - 1, sim.Realm.Territory);
        Assert.True(Logged(sim, "빼앗았다"));
        Assert.Contains(sim.Lands(v.Id), p => sim.Battles.Any(b => b.Province == p.Id && !b.Won));

        sim.Armies.Clear();
        var weak = sim.Invade(v)!;
        weak.Men = 0.001;
        land = sim.Realm.Territory;
        for (int k = 0; k < 40 && sim.Armies.Contains(weak); k++) sim.ArmiesMove();
        Assert.Equal(land, sim.Realm.Territory);
        Assert.True(Logged(sim, "막아냈다"));
    }

    [Fact]
    public void 땅이_도성뿐이면_도성이_털린다()
    {
        var (sim, v) = Ruled(1);
        Assert.Single(sim.Lands(0));
        var inv = sim.Invade(v)!;
        Assert.Equal(sim.Map.Capital, inv.Goal);
        inv.Men = inv.FullMen = 1e9;
        for (int k = 0; k < 40 && sim.Armies.Contains(inv); k++) sim.ArmiesMove();
        Assert.True(Logged(sim, "【도성 약탈】"));
        Assert.Equal(0, sim.Map[sim.Map.Capital].Owner); // 도성은 빼앗기지 않는다
    }

    [Fact]
    public void 공물_제도가_있으면_침공_앞에서_값을_치를지_묻는다()
    {
        var (sim, v) = Ruled();
        sim.Inst.Tribute = true;
        v.Hostility = 1;
        for (int k = 0; k < 5000 && sim.Pending.Count == 0; k++) sim.InvasionYear();
        var d = Assert.Single(sim.Pending);
        Assert.Equal("tribute", d.Kind);
        Assert.Empty(sim.Armies);

        double t = sim.Realm.Treasury;
        sim.Decide(d.Id, 0);
        Assert.Equal(t - 1, sim.Realm.Treasury, 6);
        Assert.True(v.TreatyUntil > sim.Year);
        Assert.Empty(sim.Armies);

        // 맞서면 군대가 온다. 공물이 없는 나라에는 묻지 않고 온다.
        v.TreatyUntil = 0;
        for (int k = 0; k < 5000 && sim.Pending.Count == 0; k++) sim.InvasionYear();
        sim.Decide(sim.Pending[0].Id, 1);
        Assert.Contains(sim.Armies, a => a.Owner == v.Id);

        sim.Armies.Clear();
        sim.Inst.Tribute = false;
        for (int k = 0; k < 5000 && sim.Armies.Count == 0; k++) sim.InvasionYear();
        Assert.Contains(sim.Armies, a => a.Owner == v.Id);
        Assert.Empty(sim.Pending);
    }

    [Fact]
    public void 플레이어가_다스리는_나라도_수백_년을_결정적으로_돈다()
    {
        string Run()
        {
            var sim = new Simulation(5) { PlayerRules = true };
            for (int k = 0; k < 400 * 4; k++)
            {
                sim.Step();
                // 올라온 물음에는 늘 첫 선택지로 답한다.
                foreach (var d in sim.Pending.ToList()) sim.Decide(d.Id, 0);
            }
            return $"{sim.Year} {sim.Realm.Stage} {sim.Realm.Pop:F3} {sim.Log.Count} {sim.Armies.Count}";
        }
        Assert.Equal(Run(), Run());
    }
}
