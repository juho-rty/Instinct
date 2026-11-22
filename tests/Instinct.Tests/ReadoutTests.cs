using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 화면의 "왜 이 값인가" 읽기: 민심의 갈래(MoodParts)와 일의 소출 어림(Forecast).
/// 둘 다 판을 바꾸지 않아야 하고(난수를 쓰지 않음), 민심의 갈래는 실제로 민심을 움직이는 식과 같아야 한다.
/// </summary>
public class ReadoutTests
{
    static double Sum(List<Simulation.MoodPart> parts) => parts.Sum(p => p.Value);

    [Fact]
    public void 민심의_갈래가_제도를_따라_움직이고_합이_결속을_끈다()
    {
        var sim = EstateTests.State(7, Stage.Kingdom, Era.Medieval, 30000, 3);
        double before = Sum(sim.CohesionParts());
        sim.Inst.Nationalism = true;
        Assert.Equal(before + 0.2, Sum(sim.CohesionParts()), 9);
        Assert.Contains(sim.CohesionParts(), p => p.What == "민족주의" && p.Value == 0.2);
        Assert.Equal(sim.CohesionParts().Count, sim.CohesionParts().Select(p => p.What).Distinct().Count());

        // 결속은 갈래의 합(목표) 쪽으로 다가간다.
        sim.Realm.Cohesion = 0.05;
        double target = Math.Clamp(Sum(sim.CohesionParts()), 0, 1);
        Assert.True(target > 0.3);
        sim.Step(); sim.Step(); sim.Step(); sim.Step();
        Assert.True(sim.Realm.Cohesion > 0.05);
    }

    [Fact]
    public void 억누름은_불만의_갈래와_따로_보인다()
    {
        var sim = EstateTests.State(8, Stage.Kingdom, Era.Medieval, 30000, 3);
        Assert.Equal(0, Sum(sim.RepressParts()), 9);
        sim.Inst.Surveillance = true;
        Assert.Contains(sim.RepressParts(), p => p.What == "감시" && p.Value == 0.15);
        sim.Inst.Welfare = true;
        Assert.Contains(sim.UnrestParts(), p => p.What == "복지국가" && p.Value == -0.12);
        Assert.Contains(sim.LegitimacyParts(), p => p.What == "기본" && p.Value == 0.5);
    }

    [Fact]
    public void 소출_어림은_일하는_사람이_있어야_나오고_판을_바꾸지_않는다()
    {
        var a = new Simulation(4);
        var b = new Simulation(4);
        a.Step(); b.Step();
        var busy = new[] { Work.Gather, Work.Hunt, Work.Fish }.OrderByDescending(a.CountJob).First();
        Assert.True(a.CountJob(busy) > 0);
        Assert.True(a.Forecast(busy) > 0);
        Assert.Equal(a.Forecast(Work.Gather), a.Forecast(Work.None));
        if (a.CountJob(Work.Farm) == 0) Assert.Equal(0, a.Forecast(Work.Farm));
        Assert.Equal(0, a.Forecast(Work.Build));
        Assert.True(a.ForecastFood() >= a.Forecast(busy));
        // 몇 번 불러도 난수를 먹지 않는다: 부른 판과 안 부른 판이 똑같이 흘러간다.
        for (int i = 0; i < 8; i++) { a.ForecastFood(); a.Step(); b.Step(); }
        Assert.Equal(b.CommonStore, a.CommonStore);
        Assert.Equal(b.Living.Count(), a.Living.Count());
    }

    [Fact]
    public void 받침_칭호와_계층_창고_지명의_조사가_바르게_붙는다()
    {
        // 1. 무리 잔치: 공동 창고일 때 '창고를 헐어', 사유재산일 때 '제 곳간을 헐어'
        var band = new Simulation(2);
        for (int t = 0; t < 4; t++) band.Step();
        band.CommonStore = 60;
        Assert.True(band.TribeAct("feast", -1));
        Assert.Contains(band.Log, e => e.Text.Contains("창고를 헐어"));
        Assert.DoesNotContain(band.Log, e => e.Text.Contains("창고을 헐어"));

        // 2. 노동자 계층 반발: '노동자가 반발한다' ('노동자이'가 아님)
        var kingdom = EstateTests.State(5, Stage.Kingdom, Era.Modern, 80000, 4);
        Assert.Equal("왕", kingdom.RulerTitle);
        var workerLaw = Catalog.All.First(d => d.Losers.Count == 1 && d.Losers[0] == Estate.Workers && !d.Get(kingdom.Inst));
        kingdom.GrantTechs(Era.Modern);
        Assert.True(kingdom.Enact(workerLaw, true));
        Assert.Contains(kingdom.Log, e => e.Text.Contains("노동자가 반발한다."));
        Assert.DoesNotContain(kingdom.Log, e => e.Text.Contains("노동자이 반발한다"));

        // 3. 받침 있는 통치자 칭호('왕'): '왕이'·'왕은' ('왕가'·'왕는'이 아님)
        kingdom.RivalsYear();
        var foe = kingdom.Rivals[0];
        var cap = kingdom.Map[kingdom.Map.Capital];
        var adj = kingdom.Map[cap.Adjacent[0]];
        adj.Owner = foe.Id;
        foe.TreatyUntil = kingdom.Year + 5;
        int from = kingdom.Log.Count;
        kingdom.Armies.Add(new Army { Id = 98, Owner = 0, Name = "제1군단", At = cap.Id, Goal = adj.Id, Men = 500, FullMen = 500, Morale = 1 });
        kingdom.ArmiesMove();
        Assert.Contains(kingdom.Log.Skip(from), e => e.Text.Contains("왕이 "));
        Assert.DoesNotContain(kingdom.Log.Skip(from), e => e.Text.Contains("왕가 "));

        kingdom.Inst.Tribute = true;
        kingdom.PlayerRules = true;
        kingdom.Realm.Treasury = 5;
        foe.TreatyUntil = 0;
        foe.Hostility = 1;
        kingdom.Armies.Clear();
        kingdom.Pending.Clear();
        for (int k = 0; k < 200 && kingdom.Pending.Count == 0; k++) kingdom.InvasionYear();
        var ask = Assert.Single(kingdom.Pending, d => d.Kind == "tribute");
        from = kingdom.Log.Count;
        Assert.True(kingdom.Decide(ask.Id, 0));
        Assert.Contains(kingdom.Log.Skip(from), e => e.Text.Contains("왕은 공물을 바쳐 물렸다."));
        Assert.DoesNotContain(kingdom.Log.Skip(from), e => e.Text.Contains("왕는"));

        // 4. 받침 없는 지명('이슬재'): 전투 요약에 '이슬재를' ('이슬재을'이 아님)
        cap.Name = "이슬재";
        kingdom.Armies.Add(new Army { Id = 99, Owner = foe.Id, Name = foe.Name + "군", At = cap.Adjacent[0], Goal = cap.Id, Men = 50, FullMen = 50, Morale = 1, Muster = 0 });
        kingdom.ArmiesMove();
        Assert.Contains(kingdom.Battles, b => b.Text.Contains("이슬재를 "));
        Assert.DoesNotContain(kingdom.Battles, b => b.Text.Contains("이슬재을"));
    }
}
