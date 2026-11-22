using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 이웃 나라: 교역·화친·혼인·믿음과 문물의 전파·정복·함락, 그리고 몰락의 갈래가 기록되는지를
/// 상태를 만들어 강제로 밟는다.
/// </summary>
public class RivalTests
{
    static bool Logged(Simulation sim, string part) => EstateTests.Logged(sim, part);

    /// <summary>도시국가 이상의 나라와, 그 이웃 가운데 하나만 남긴 상태.</summary>
    static (Simulation sim, Rival v) WithRival(Stage stage, Era era, int territory = 3)
    {
        var sim = EstateTests.State(1, stage, era, 30000, territory);
        sim.Inst.Taxation = true;
        sim.Realm.Treasury = 5;
        sim.RivalsYear();
        var v = sim.Rivals[0];
        sim.Rivals.RemoveAll(x => x != v);
        v.Trade = v.Allied = v.Free = false;
        v.TreatyUntil = 0;
        return (sim, v);
    }

    [Fact]
    public void 도시국가가_되면_국경_너머에_나라들이_보인다()
    {
        var chiefdom = EstateTests.State(1, Stage.Chiefdom, Era.Ancient, 2000, 1);
        chiefdom.RivalsYear();
        Assert.Empty(chiefdom.Rivals);

        var sim = EstateTests.State(1, Stage.CityState, Era.Ancient, 5000, 1);
        sim.RivalsYear();
        Assert.True(sim.Rivals.Count >= 3);
        Assert.Equal(sim.Rivals.Count, sim.Rivals.Select(v => v.Name).Distinct().Count());
        Assert.True(Logged(sim, "【이웃 나라】"));
    }

    [Fact]
    public void 적의가_낮고_화폐가_있으면_장삿길이_열리고_적의가_높으면_끊긴다()
    {
        var (sim, v) = WithRival(Stage.CityState, Era.Medieval);
        for (int k = 0; k < 60; k++) { v.Hostility = 0; sim.RivalsYear(); }
        Assert.False(v.Trade); // 화폐도 길드도 없으면 장사가 서지 않는다

        sim.Inst.Coinage = true;
        for (int k = 0; k < 200 && !v.Trade; k++) { v.Hostility = 0; sim.RivalsYear(); }
        Assert.True(v.Trade);
        Assert.True(Logged(sim, "【교역】"));

        v.Hostility = 1;
        sim.RivalsYear();
        Assert.False(v.Trade);
        Assert.True(Logged(sim, "교역로가 끊겼다"));
    }

    [Fact]
    public void 장삿길로_앞선_문물과_낯선_믿음이_들어온다()
    {
        var (sim, v) = WithRival(Stage.CityState, Era.Medieval);
        var r = sim.Realm;
        v.Trade = true;
        v.Hostility = 0;
        v.Faith = 1;
        v.Advance = r.Knowledge + 500;
        double know = r.Knowledge;

        sim.RivalsYear();

        Assert.True(r.Knowledge > know);
        Assert.True(r.Heresy > 0);

        // 종교재판은 낯선 믿음을 막는다(그 값은 다른 데서 치른다).
        var (sim2, v2) = WithRival(Stage.CityState, Era.Medieval);
        sim2.Inst.Inquisition = true;
        v2.Trade = true; v2.Hostility = 0; v2.Faith = 1;
        sim2.RivalsYear();
        Assert.True(sim2.Realm.Heresy < r.Heresy);
    }

    [Fact]
    public void 백성이_통치자를_뽑는_이웃의_소문은_평민과_노동자를_들썩이게_한다()
    {
        var (sim, v) = WithRival(Stage.Kingdom, Era.Industrial);
        var r = sim.Realm;
        v.Trade = true; v.Hostility = 0; v.Free = true;
        sim.RivalsYear();
        Assert.True(r.Grievance[(int)Estate.Commoners] > 0);
        Assert.True(r.Grievance[(int)Estate.Workers] > 0);
        Assert.True(Logged(sim, "백성이 통치자를 뽑는다"));

        var (sim2, v2) = WithRival(Stage.Kingdom, Era.Industrial);
        sim2.Inst.Suffrage = true;
        v2.Trade = true; v2.Hostility = 0; v2.Free = true;
        sim2.RivalsYear();
        Assert.Equal(0, sim2.Realm.Grievance[(int)Estate.Commoners]);
    }

    [Fact]
    public void 화친은_곳간으로_평화를_사고_귀족의_불만을_산다()
    {
        var (sim, v) = WithRival(Stage.Kingdom, Era.Medieval);
        var r = sim.Realm;
        sim.Inst.Parley = true;
        for (int k = 0; k < 200 && !Logged(sim, "【화친】"); k++)
        {
            v.Hostility = 0.9;
            r.Treasury = 5;
            sim.RivalsYear();
        }
        Assert.True(Logged(sim, "【화친】"));
        Assert.True(v.TreatyUntil > sim.Year);
        Assert.True(r.Treasury < 5);
        Assert.True(r.Grievance[(int)Estate.Nobles] > 0);
    }

    [Fact]
    public void 나라의_혼인_동맹은_딸을_보내고_핏줄을_따지는_통치자는_남의_딸을_보낸다()
    {
        var (sim, v) = WithRival(Stage.Kingdom, Era.Medieval);
        var chief = sim.Chief!;
        var daughter = sim.NewPerson(Sex.Female, 18, chief.ClanId);
        daughter.Father = chief;
        chief.Children.Add(daughter);
        chief.Traits[(int)Trait.KinBias] = 1;
        double treasury = sim.Realm.Treasury;

        Assert.True(sim.RoyalMarriage(v));

        Assert.True(v.Allied);
        Assert.True(daughter.Alive, "핏줄을 따지는 통치자는 제 딸을 아낀다");
        Assert.Equal(1, sim.DeathsByCause.GetValueOrDefault("시집감"));
        Assert.True(sim.Realm.Treasury < treasury);
        Assert.True(Logged(sim, "【혼인 동맹】"));

        // 핏줄을 따지지 않는 통치자는 제 딸을 보낸다.
        var (sim2, v2) = WithRival(Stage.Kingdom, Era.Medieval);
        var chief2 = sim2.Chief!;
        var daughter2 = sim2.NewPerson(Sex.Female, 18, chief2.ClanId);
        daughter2.Father = chief2;
        chief2.Children.Add(daughter2);
        chief2.Traits[(int)Trait.KinBias] = 0;
        Assert.True(sim2.RoyalMarriage(v2));
        Assert.Equal("시집감", daughter2.DeathCause);
    }

    [Fact]
    public void 약해진_이웃을_쳐서_이기면_그_나라는_지도에서_사라진다()
    {
        var (sim, v) = WithRival(Stage.Kingdom, Era.Medieval);
        var r = sim.Realm;
        sim.Inst.Raiding = sim.Inst.StandingArmy = true;
        for (int k = 0; k < 3000 && sim.Rivals.Contains(v); k++)
        {
            if (sim.Chief is { Alive: true } c)
            {
                c.Traits[(int)Trait.Aggression] = 1;
                c.Traits[(int)Trait.Ambition] = 1;
            }
            v.Strength = 0.35;
            v.Hostility = 1;
            r.Pop = 30000; r.Cohesion = 0.9; r.Treasury = 5;
            sim.RealmWar();
        }
        Assert.DoesNotContain(v, sim.Rivals);
        Assert.True(Logged(sim, "【정복】"));
        Assert.True(r.Atrocities >= 1);
        Assert.True(r.Territory > 3);
    }

    [Fact]
    public void 땅이_한_칸_남은_나라는_강한_이웃에게_지면_함락된다()
    {
        var (sim, v) = WithRival(Stage.Kingdom, Era.Medieval, territory: 1);
        var r = sim.Realm;
        for (int k = 0; k < 3000 && r.Collapses == 0; k++)
        {
            if (sim.Chief is { Alive: true } c) c.Traits[(int)Trait.Ambition] = 0;
            v.Strength = 2;
            v.Hostility = 1;
            v.TreatyUntil = 0;
            r.Pop = 30000; r.Cohesion = 0.1; r.Territory = 1;
            sim.RealmWar();
        }
        Assert.Equal(1, r.Collapses);
        Assert.EndsWith("|함락", r.Falls.Single());
        Assert.True(Logged(sim, "도성이 함락되었다"));
    }

    [Fact]
    public void 빚이_쌓인_나라는_파산으로_무너지고_그_갈래가_기록된다()
    {
        var sim = EstateTests.State(2, Stage.Kingdom, Era.Medieval, 60000, 4);
        var r = sim.Realm;
        sim.Inst.Taxation = true;
        for (int y = 0; y < 400 && r.Collapses == 0; y++)
        {
            r.Treasury = -6;
            r.Cohesion = 0.8; r.Unrest = 0.2; r.Legitimacy = 0.8; r.Territory = 4; r.Pop = 60000;
            foreach (var v in sim.Rivals) { v.Hostility = 0; v.TreatyUntil = sim.Year + 50; }
            sim.RealmYear();
        }
        Assert.Equal(1, r.Collapses);
        Assert.EndsWith("|파산", r.Falls.Single());
    }

    /// <summary>다른 갈래가 끼어들지 못하게 눌러 놓고, 한 갈래의 조건만 해마다 다시 세운다.</summary>
    static string FallBy(Action<Simulation> setup)
    {
        var sim = EstateTests.State(2, Stage.Kingdom, Era.Medieval, 60000, 4);
        var r = sim.Realm;
        sim.Inst.Taxation = true;
        for (int y = 0; y < 600 && r.Collapses == 0; y++)
        {
            r.Treasury = 5; r.Cohesion = 0.8; r.Unrest = 0.2; r.Pop = 60000; r.Territory = 4;
            foreach (var v in sim.Rivals) { v.Hostility = 0; v.TreatyUntil = sim.Year + 50; }
            setup(sim);
            sim.RealmYear();
        }
        Assert.Equal(1, r.Collapses);
        return r.Falls.Single().Split('|')[1];
    }

    [Fact]
    public void 다스릴_수_있는_것보다_넓은_땅은_정통성이_낮으면_끝에서부터_떨어져_나간다() =>
        Assert.Equal("과한 영토", FallBy(sim => { sim.Realm.Territory = 30; sim.Realm.Legitimacy = 0.2; }));

    [Fact]
    public void 봉건제_아래서_귀족이_분노하고_정통성이_낮으면_나라가_갈라진다() =>
        Assert.Equal("분열", FallBy(sim =>
        {
            sim.Inst.Feudalism = true;
            sim.Realm.Territory = 2;
            sim.Realm.Legitimacy = 0.2;
            sim.Realm.Grievance[(int)Estate.Nobles] = 1;
        }));

    [Fact]
    public void 몰락할_때마다_해와_갈래가_남는다()
    {
        var sim = EstateTests.State(1, Stage.Empire, Era.Industrial, 400000, 9);
        sim.Collapse("시험", kind: "분열");
        sim.Realm.Stage = Stage.Kingdom;
        sim.Realm.StateFounded -= Simulation.CollapseGrace; // 재건의 유예가 끝난 뒤
        sim.Collapse("시험");
        Assert.Equal(new[] { $"{sim.Year}|분열", $"{sim.Year}|기타" }, sim.Realm.Falls);
    }

    [Fact]
    public void 몰락_직후_유예기에는_흔들려도_무너지지_않는다()
    {
        var sim = EstateTests.State(1, Stage.Empire, Era.Industrial, 400000, 9);
        sim.Collapse("시험", kind: "분열");
        Assert.True(sim.InCollapseGrace);
        sim.Realm.Stage = Stage.Kingdom;
        double coh = sim.Realm.Cohesion;
        sim.Collapse("두 번째 시험", kind: "분열");
        Assert.Single(sim.Realm.Falls);
        Assert.Equal(Stage.Kingdom, sim.Realm.Stage);
        Assert.True(sim.Realm.Cohesion < coh);
        Assert.True(EstateTests.Logged(sim, "【위기】"));
        sim.Collapse("함락 시험", kind: "함락"); // 도성 함락은 유예가 막지 못한다
        Assert.Equal(2, sim.Realm.Falls.Count);
    }

    [Fact]
    public void 플레이어_사절로_교역_화친_국혼을_맺고_저장에_복원된다()
    {
        var (sim, v) = WithRival(Stage.Kingdom, Era.Medieval);
        sim.Inst.Coinage = true;
        sim.Inst.Parley = true;
        sim.Inst.MarriageAlliance = true;
        v.Hostility = 0.45;
        sim.Realm.Treasury = 5;

        // 1) 교역 사절
        Assert.True(sim.SendEnvoy(v.Id, "trade"));
        Assert.True(v.Trade);
        Assert.Equal("이번 계절에는 이미 사절을 보냄", sim.WhyCantEnvoy(v.Id, "truce"));

        sim.Step();

        // 2) 집결 중인 적군이 있을 때 화친 사절을 보내면 물러난다
        sim.Armies.Add(new Army { Id = 99, Name = "침공군", Owner = v.Id, At = 0, Goal = sim.Map.Capital, Men = 200, FullMen = 200, Muster = 2 });
        Assert.True(sim.SendEnvoy(v.Id, "truce"));
        Assert.True(v.TreatyUntil > sim.Year);
        Assert.DoesNotContain(sim.Armies, a => a.Owner == v.Id);
        Assert.True(sim.Realm.Grievance[(int)Estate.Nobles] > 0);

        // 3) 국고 예측은 난수를 건드리지 않는다
        string fpBefore = sim.Fingerprint();
        var bf = sim.ForecastTreasury();
        Assert.True(bf.Income > 0);
        Assert.Equal(fpBefore, sim.Fingerprint());

        // 4) 실제 진행한 판에서 사절 명령이 저장·불러오기로 똑같이 복원된다
        var real = new Simulation(3) { PlayerRules = true };
        var pol = new Random(104);
        for (int t = 0; t < 800 && (real.Rivals.Count == 0 || real.Realm.Treasury < 1.0); t++)
        {
            PlayerBot.Season(real, pol);
            real.Step();
        }
        var target = real.Rivals.First(r => real.WhyCantEnvoy(r.Id, "truce").Length == 0);
        Assert.True(real.SendEnvoy(target.Id, "truce"));
        for (int t = 0; t < 4; t++) real.Step();
        string saved = SaveGame.Write(real, "envoy-test");
        var loaded = SaveGame.Read(saved);
        Assert.False(SaveGame.Drifted(loaded, saved));
        Assert.Equal(real.Fingerprint(), loaded.Fingerprint());
    }
}