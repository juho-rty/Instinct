using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 일과 칸 건물(Labor.cs): 일 맡기기, 정원, 관리인, 칸 위에 짓기, 정찰과 안개.
/// </summary>
public class LaborTests
{
    const int W = WorldMap.Width;

    static int FirstPlaceable(Simulation sim, StructureKind k) =>
        Enumerable.Range(0, W * WorldMap.Height).First(c => sim.WhyCantPlace(k, c).Length == 0);

    static void Finish(Structure s) => s.Done = s.Work;

    [Fact]
    public void 같은_씨앗은_같은_먹을거리와_같은_첫_시야()
    {
        var a = new Simulation(3);
        var b = new Simulation(3);
        Assert.Equal(a.Map.Feat, b.Map.Feat);
        Assert.Equal(a.Map.Seen, b.Map.Seen);
        int seen = a.Map.Seen.Count(x => x);
        Assert.InRange(seen, 50, W * WorldMap.Height / 3); // 처음에는 터 둘레만 보인다
        var cap = a.Map[a.Map.Capital];
        var near = a.Map.Disc(cap.X, cap.Y, 5).ToList();
        Assert.True(near.Count(c => a.Map.Feat[c] == Feature.Berries) >= 3);
        Assert.True(near.Count(c => a.Map.Feat[c] == Feature.Game) >= 3);
        Assert.Contains(near, c => a.Map.Feat[c] == Feature.Stone);
    }

    [Fact]
    public void 일이_없는_사람은_다음_철에_먹을_것_일을_맡는다()
    {
        var sim = new Simulation(1);
        sim.Step();
        Assert.All(sim.Workers(), p => Assert.NotEqual(Work.None, p.Job));
        Assert.All(sim.Workers(), p => Assert.True(WorkCatalog.Of(p.Job).Food));
    }

    [Fact]
    public void 정원을_넘으면_한_사람의_몫이_준다()
    {
        var sim = new Simulation(1);
        double c = sim.Capacity(Work.Hunt);
        Assert.True(c > 0);
        Assert.Equal(1, sim.Efficiency(Work.Hunt, Math.Floor(c)));
        Assert.True(sim.Efficiency(Work.Hunt, c * 3) < 0.9);
        Assert.Equal(0, sim.Capacity(Work.Farm)); // 밭이 없으면 농사 정원도 없다
        Assert.False(sim.SetJob(sim.Workers()[0].Id, Work.Farm)); // 농경 없이는 농사를 못 맡긴다
    }

    [Fact]
    public void 칸_건물은_나무를_내고_건설_일꾼이_지어야_선다()
    {
        var sim = new Simulation(2);
        int cell = FirstPlaceable(sim, StructureKind.Hut);
        double wood = sim.Wood;
        Assert.True(sim.Place(StructureKind.Hut, cell));
        Assert.Equal(wood - 3, sim.Wood, 6);
        Assert.False(sim.HasBuilt(StructureKind.Hut));
        Assert.NotEqual("", sim.WhyCantPlace(StructureKind.Hut, cell)); // 한 칸에 하나
        foreach (var p in sim.Workers().Take(4)) sim.SetJob(p.Id, Work.Build);
        for (int i = 0; i < 4 && !sim.HasBuilt(StructureKind.Hut); i++) sim.Step();
        Assert.True(sim.HasBuilt(StructureKind.Hut));
        Assert.Contains(sim.Log, e => e.Text.Contains("【건설】"));
        Assert.Equal(5, sim.HutCapacity());
    }

    [Fact]
    public void 놓을_수_없는_자리에는_까닭이_있다()
    {
        var sim = new Simulation(2);
        Assert.Equal("무리의 터(마을 한가운데)", sim.WhyCantPlace(StructureKind.Hut, sim.CampCell));
        int unseen = Enumerable.Range(0, W * WorldMap.Height).First(c => sim.Map.Cell[c] >= 0 && !sim.Map.Seen[c]);
        Assert.Equal("아직 보지 못한 땅", sim.WhyCantPlace(StructureKind.Tower, unseen));
        int foreign = Enumerable.Range(0, W * WorldMap.Height).FirstOrDefault(c => sim.Map.Cell[c] >= 0 && sim.Map.Seen[c] && sim.Map[sim.Map.Cell[c]].Owner != 0, -1);
        if (foreign >= 0) Assert.Equal("우리 땅이 아님", sim.WhyCantPlace(StructureKind.Tower, foreign));
        sim.Wood = 0;
        Assert.StartsWith("나무 부족", sim.WhyCantPlace(StructureKind.Tower, FirstPlaceableIgnoringCost(sim)));
        Assert.Contains("농경", sim.WhyCantPlace(StructureKind.Granary, FirstPlaceableIgnoringCost(sim)));
    }

    static int FirstPlaceableIgnoringCost(Simulation sim)
    {
        double w = sim.Wood, s = sim.Stone;
        sim.Wood = sim.Stone = 100;
        int c = FirstPlaceable(sim, StructureKind.Tower);
        sim.Wood = w; sim.Stone = s;
        return c;
    }

    [Fact]
    public void 곡창은_창고를_덜_썩히고_목책과_망루는_막는_힘을_키운다()
    {
        var plain = new Simulation(4);
        var stored = new Simulation(4);
        stored.Inst.Agriculture = true;
        stored.Wood = stored.Stone = 100;
        Assert.True(stored.Place(StructureKind.Granary, FirstPlaceable(stored, StructureKind.Granary)));
        Finish(stored.Structures[0]);
        plain.CommonStore = stored.CommonStore = 1000;
        plain.Step();
        stored.Step();
        Assert.True(stored.CommonStore > plain.CommonStore);

        Assert.Equal(1, stored.DefenseMul());
        Assert.True(stored.Place(StructureKind.Palisade, FirstPlaceable(stored, StructureKind.Palisade)));
        Assert.True(stored.Place(StructureKind.Tower, FirstPlaceable(stored, StructureKind.Tower)));
        foreach (var s in stored.Structures) Finish(s);
        Assert.Equal(1.35 * 1.1, stored.DefenseMul(), 6);
    }

    [Fact]
    public void 정찰꾼은_안개를_걷고_새_땅을_알린다()
    {
        var sim = new Simulation(5);
        sim.Step();
        int before = sim.Map.Seen.Count(x => x);
        foreach (var p in sim.Workers().Take(3)) sim.SetJob(p.Id, Work.Scout);
        for (int i = 0; i < 8; i++) sim.Step();
        Assert.True(sim.Map.Seen.Count(x => x) > before + 20);
        Assert.Contains(sim.Log, e => e.Text.Contains("【정찰】"));
    }

    [Fact]
    public void 관리인은_어른이_차야_세우고_일을_다시_나누며_창고에서_떼어_간다()
    {
        var sim = new Simulation(1);
        sim.Step();
        var cand = sim.Living.First(p => p.Age(sim.Tick) >= 16 && p != sim.Chief && p.Master == null);
        Assert.StartsWith("어른이", sim.WhyCantSteward(cand.Id));
        Assert.False(sim.AppointSteward(cand.Id, StewardPolicy.Balanced));

        int clan = sim.People[0].ClanId;
        for (int k = 0; k < 12; k++) { sim.NewPerson(Sex.Male, 20 + k, clan); sim.NewPerson(Sex.Female, 20 + k, clan); }
        Assert.Equal("", sim.WhyCantSteward(cand.Id));
        sim.Wood = 100;
        sim.Place(StructureKind.Hut, FirstPlaceable(sim, StructureKind.Hut));
        Assert.True(sim.AppointSteward(cand.Id, StewardPolicy.Build));
        Assert.False(sim.SetJob(sim.Workers()[0].Id, Work.Wood)); // 관리인이 있으면 손으로 못 나눈다
        sim.CommonStore = 200;
        double status = cand.Status;
        sim.Step();
        Assert.True(sim.CountJob(Work.Build) >= 2); // 건설 먼저
        int food = sim.Workers().Count(p => WorkCatalog.Of(p.Job).Food);
        Assert.True(food * 10 >= sim.Workers().Count * 6 - 10);
        Assert.True(cand.Status > status);
        Assert.True(sim.AppointSteward(-1, StewardPolicy.Balanced));
        Assert.Null(sim.Steward);
    }

    [Fact]
    public void 일과_칸_건물_명령은_저장에서_되살아난다()
    {
        var sim = new Simulation(6);
        sim.Step();
        sim.SetJob(sim.Workers()[0].Id, Work.Wood);
        sim.Assign(Work.Scout, 1);
        sim.Place(StructureKind.Hut, FirstPlaceable(sim, StructureKind.Hut));
        sim.Assign(Work.Build, 1);
        for (int i = 0; i < 6; i++) sim.Step();
        var back = SaveGame.Read(SaveGame.Write(sim));
        Assert.Equal(sim.Fingerprint(), back.Fingerprint());
        Assert.Equal(sim.Structures.Select(s => (s.Kind, s.Cell, s.Built)), back.Structures.Select(s => (s.Kind, s.Cell, s.Built)));
        Assert.Equal(sim.Map.Seen, back.Map.Seen);
        Assert.Equal(sim.Wood, back.Wood, 6);
    }

    [Fact]
    public void 판을_열면_일꾼마다_먹을_것_일이_있다()
    {
        var sim = new Simulation(2);
        var ws = sim.Workers();
        Assert.NotEmpty(ws);
        Assert.All(ws, p => Assert.True(WorkCatalog.Of(p.Job).Food));
    }

    [Fact]
    public void 창고는_터_가까이에만_서고_저장_한도를_늘린다()
    {
        var sim = new Simulation(4);
        sim.Wood = 100;
        double cap = sim.StoreCap();
        int cell = FirstPlaceable(sim, StructureKind.Storehouse);
        Assert.True(cell >= 0);
        int w = WorldMap.Width;
        Assert.True(Math.Max(Math.Abs(cell % w - sim.CampCell % w), Math.Abs(cell / w - sim.CampCell / w)) <= 3);
        Assert.True(sim.Place(StructureKind.Storehouse, cell));
        Assert.Equal(cap, sim.StoreCap(), 6); // 다 짓기 전에는 그대로
        Finish(sim.Structures.Single(s => s.Kind == StructureKind.Storehouse));
        Assert.Equal(cap + StructureCatalog.StoreEach, sim.StoreCap(), 6);
    }

    [Fact]
    public void 저장_한도를_넘친_식량은_더_빨리_썩는다()
    {
        var a = new Simulation(4);
        var b = new Simulation(4);
        double cap = a.StoreCap();
        a.CommonStore = cap;
        b.CommonStore = cap + 100;
        a.Step();
        b.Step();
        // 넘친 100이 그대로 남지 않는다: 썩는 비율이 보통보다 크다.
        double extra = b.CommonStore - a.CommonStore;
        Assert.True(extra > 0);
        Assert.True(extra <= 100 * (1 - StructureCatalog.ExcessRot) + 1e-6, $"넘친 몫이 {extra:0.0}만큼 남음");
    }

    [Fact]
    public void 제도_건의는_기본으로_꺼져_있고_켜면_올라온다()
    {
        Simulation Run(bool on)
        {
            var sim = new Simulation(7) { PlayerRules = true, Proposals = on };
            for (int i = 0; i < 4 * 12 && sim.Living.Any(); i++)
            {
                sim.Step();
                if (on && sim.Pending.Any(d => d.Kind == "adopt")) break;
            }
            return sim;
        }
        Assert.False(new Simulation(7).Proposals);
        var off = Run(false);
        Assert.DoesNotContain(off.Pending, d => d.Kind == "adopt");
        Assert.Contains(Run(true).Pending, d => d.Kind == "adopt");

        var saved = new Simulation(7) { PlayerRules = true, Proposals = true };
        saved.Step();
        Assert.True(SaveGame.Read(SaveGame.Write(saved)).Proposals);
        Assert.False(SaveGame.Read(SaveGame.Write(new Simulation(7) { PlayerRules = true })).Proposals);
    }

    [Fact]
    public void 겨울에는_창고가_덜_썩는다()
    {
        double Left(int tick)
        {
            var a = new Simulation(4) { Tick = tick };
            var b = new Simulation(4) { Tick = tick };
            a.CommonStore = a.StoreCap() / 2;
            b.CommonStore = a.CommonStore + 10;
            a.Step();
            b.Step();
            return b.CommonStore - a.CommonStore;
        }
        double spring = Left(4), winter = Left(7);
        var cfg = new Config();
        Assert.True(winter > spring, $"겨울 {winter:0.00} / 봄 {spring:0.00}");
        Assert.True(winter >= 10 * (1 - (1 - cfg.Keep) * cfg.WinterRot) - 1e-6, $"겨울에 10 가운데 {winter:0.00}만 남음");
    }

    [Fact]
    public void 가을에_모이고_겨울에_줄며_손놓은_무리는_겨울에_굶는다()
    {
        // 아무것도 하지 않는 무리 여섯을 10년 돌린다: 가을에 창고가 불고 겨울에 줄어야 한다.
        var net = new double[4];
        int starved = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            var sim = new Simulation(seed) { PlayerRules = true };
            for (int t = 0; t < 40 && sim.Living.Any() && sim.Tribal; t++)
            {
                foreach (var d in sim.Pending.ToList()) sim.Decide(d.Id, 0);
                double before = sim.CommonStore;
                int s = sim.Season;
                sim.Step();
                net[s] += sim.CommonStore - before;
            }
            Assert.True(sim.Living.Any(), $"씨앗 {seed}의 무리가 사라졌다");
            starved += sim.People.Count(p => !p.Alive && p.DeathCause == "굶주림");
        }
        Assert.True(net[2] > 0, $"가을 순증감 {net[2]:0.0}");
        Assert.True(net[3] < 0, $"겨울 순증감 {net[3]:0.0}");
        Assert.True(-net[3] > Math.Abs(net[0]) * 2, $"겨울 {net[3]:0.0} / 봄 {net[0]:0.0}");
        Assert.True(starved > 0, "손놓은 무리에서 굶어 죽은 이가 없다 — 겨울이 위협이 아니다");
    }

    [Fact]
    public void 지방_시야는_가장자리_한_칸만_보여도_알고_안개가_다_걷히면_관리인은_정찰을_뺀다()
    {
        var sim = new Simulation(3);
        sim.Step();
        var cap = sim.Map[sim.Map.Capital];
        Array.Fill(sim.Map.Seen, false);
        Assert.False(sim.Map.Known(cap));
        int edgeCell = Enumerable.Range(0, W * WorldMap.Height).First(i => sim.Map.Cell[i] == cap.Id && i != cap.Y * W + cap.X);
        sim.Map.Seen[edgeCell] = true;
        Assert.True(sim.Map.Known(cap));

        // 어른을 채워 관리인을 세운 뒤 지도를 모두 밝히면 정찰꾼을 더 두지 않는다.
        sim.Map.Reveal(cap.X, cap.Y, 7);
        int clan = sim.People[0].ClanId;
        for (int k = 0; k < 12; k++) { sim.NewPerson(Sex.Male, 22 + k, clan); sim.NewPerson(Sex.Female, 22 + k, clan); }
        var cand = sim.Living.First(p => p.Age(sim.Tick) >= 16 && p != sim.Chief && p.Master == null);
        Assert.True(sim.AppointSteward(cand.Id, StewardPolicy.Balanced));
        Array.Fill(sim.Map.Seen, true);
        sim.Step();
        Assert.Equal(0, sim.CountJob(Work.Scout));
    }
}
