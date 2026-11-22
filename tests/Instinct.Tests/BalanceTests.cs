using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 균형 손질로 새로 생긴 경로: 넘치는 곳간의 강제 나눔, 아이 부양 부담의 출산 제동, 작은 무리에 대한 습격 자비,
/// 공물 상한, 관리인 없는 건설 일손, 죽음 원인의 갈래, 승리 목표.
/// </summary>
public class BalanceTests
{
    static bool Logged(Simulation sim, string part) => EstateTests.Logged(sim, part);

    [Fact]
    public void 굶는_이웃이_있으면_우두머리가_넘치는_곳간을_풀게_한다()
    {
        var sim = new Simulation(3);
        sim.Step();
        Assert.NotNull(sim.Chief);
        var living = sim.Living.OrderBy(p => p.Id).ToList();
        var heads = living.Select(sim.Head).Distinct().OrderBy(h => h.Id).ToList();
        Assert.True(heads.Count >= 2);
        var rich = heads.First();
        foreach (var p in living) p.Food = 0;
        sim.CommonStore = 0;
        rich.Food = 1000;
        sim.Feed();
        Assert.True(rich.Food < 1000 - living.Count(p => sim.Head(p) == rich) * 0.1);
        Assert.True(Logged(sim, "곳간이 넘치는 집들에 굶는 이웃을 먹이게 했다"));
    }

    [Fact]
    public void 넘치는_집이_없으면_나눔도_없다()
    {
        var sim = new Simulation(3);
        sim.Step();
        foreach (var p in sim.Living) p.Food = 0;
        sim.CommonStore = 0;
        sim.Feed();
        Assert.False(Logged(sim, "곳간이 넘치는 집들"));
    }

    [Fact]
    public void 아이가_일손보다_많으면_출산이_준다()
    {
        Assert.Equal(1, Simulation.DependencyBrake(10, 5));
        Assert.Equal(1, Simulation.DependencyBrake(10, 8));
        Assert.Equal(0.8, Simulation.DependencyBrake(10, 10), 6);
        Assert.Equal(0.4, Simulation.DependencyBrake(10, 14), 6);
        Assert.Equal(0.4, Simulation.DependencyBrake(10, 40), 6);
        Assert.Equal(0.4, Simulation.DependencyBrake(0, 3), 6); // 일손이 없으면 바닥
    }

    [Fact]
    public void 작은_무리는_덜_습격당하고_끌려가는_사람에_상한이_있다()
    {
        Assert.Equal(0.2, Simulation.SmallBandMercy(2), 6);
        Assert.Equal(0.4, Simulation.SmallBandMercy(6), 6);
        Assert.Equal(1, Simulation.SmallBandMercy(15));
        Assert.Equal(1, Simulation.SmallBandMercy(60));
        Assert.Equal(0, Simulation.MaxAbducted(9));
        Assert.Equal(2, Simulation.MaxAbducted(10));
        Assert.Equal(10, Simulation.MaxAbducted(40));
    }

    [Fact]
    public void 공물은_한해_상한을_넘지_않고_작은_무리는_바치지_않는다()
    {
        Assert.Equal(25, Simulation.TributeBudget(100, 5));
        Assert.Equal(0, Simulation.TributeBudget(0, 5));
        Assert.Equal(10, Simulation.TributeBudget(0, 20));

        // 큰 무리: 적대하는 이웃 모두에게 바쳐도 한 해 상한 안이다.
        var sim = new Simulation(5);
        sim.Step();
        Assert.NotEmpty(sim.Neighbors);
        sim.Inst.Tribute = true;
        foreach (var n in sim.Neighbors) { n.Hostility = 1; n.Fighters = 500; n.TributeYears = 10; }
        sim.CommonStore = 200;
        double Stores() => sim.CommonStore + sim.Living.Sum(p => p.Food);
        double before = Stores(), cap = Simulation.TributeBudget(before, sim.Living.Count());
        sim.PayTribute();
        Assert.True(before - Stores() <= cap + 1e-6);

        // 일곱 명 남은 무리: 받아 낼 것이 없다.
        foreach (var p in sim.Living.OrderBy(p => p.Id).Skip(7).ToList()) p.DeathTick = sim.Tick;
        Assert.Equal(7, sim.Living.Count());
        foreach (var n in sim.Neighbors) n.TributeYears = 3;
        before = Stores();
        sim.PayTribute();
        Assert.Equal(before, Stores(), 6);
        Assert.All(sim.Neighbors, n => Assert.Equal(0, n.TributeYears));
    }

    [Fact]
    public void 관리인이_없어도_놓은_터에는_일손이_붙고_다_지으면_돌아간다()
    {
        var sim = new Simulation(2);
        sim.Step();
        sim.Steward = null;
        int cell = Enumerable.Range(0, WorldMap.Width * WorldMap.Height).First(c => sim.WhyCantPlace(StructureKind.Hut, c).Length == 0);
        Assert.True(sim.Place(StructureKind.Hut, cell));
        foreach (var p in sim.Workers()) p.Job = Work.Gather;
        sim.CommonStore = 1000;
        sim.Labor();
        Assert.Single(sim.Workers(), p => p.Job == Work.Build);
        Assert.True(Logged(sim, "놓아 둔 터에 짓는 사람이 없어"));

        foreach (var s in sim.Structures) s.Done = s.Work;
        sim.Labor();
        Assert.DoesNotContain(sim.Workers(), p => p.Job == Work.Build);
        Assert.True(Logged(sim, "지을 것이 다 끝나 짓던 1명이"));
    }

    [Fact]
    public void 죽음은_노환_병_추위_사고로_갈린다()
    {
        Func<double> low = () => 0.1, high = () => 0.9;
        Assert.Equal("노환", Simulation.DeathCause(60, 1, true, low));
        Assert.Equal("병", Simulation.DeathCause(2, 1, true, low));
        Assert.Equal("병", Simulation.DeathCause(30, 0.4, true, low));
        Assert.Equal("추위", Simulation.DeathCause(30, 1, true, low));
        Assert.Equal("사고", Simulation.DeathCause(30, 1, false, low));
        Assert.Equal("병", Simulation.DeathCause(30, 1, false, high));
        Assert.Equal("물에 빠져", Simulation.AccidentText(Work.Fish));
        Assert.Equal("독사에 물려", Simulation.AccidentText(Work.Gather));
    }

    [Fact]
    public void 계몽은_지식_2200을_요구하고_지식은_빨리_쌓인다()
    {
        Assert.Equal(1.3, Simulation.KnowledgePace);
        var sim = EstateTests.State(6, Stage.Kingdom, Era.Industrial, 50000, 4);
        sim.Realm.Knowledge = 2199;
        sim.Inst.PublicSchooling = true;
        Assert.False(sim.VictoryMet(Course.Learning));
        Assert.Contains(sim.VictoryNeeds(Course.Learning).needs, x => x.what.Contains("2,200"));
        sim.Realm.Knowledge = 2200;
        Assert.True(sim.VictoryMet(Course.Learning));
    }
}
