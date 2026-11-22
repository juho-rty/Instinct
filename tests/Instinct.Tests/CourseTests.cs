using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 길과 승리(Courses.cs): 기술의 갈림길은 시대마다 여러 갈래를 고를 수 있되 더할수록 비싸지고, 고른 길이 나라를 바꾸며,
/// 길마다 승리가 하나씩 있다. 식인 풍습과 공포(Dread)도 여기서 강제로 밟는다.
/// </summary>
public class CourseTests
{
    static bool Logged(Simulation sim, string part, int from = 0) =>
        sim.Log.Skip(from).Any(e => e.Text.Contains(part));

    static TechDef T(string key) => TechTree.Find(key)!;

    // ───────── 갈림길 ─────────

    [Fact]
    public void 시대마다_네_길의_갈림길이_하나씩_있다()
    {
        foreach (var era in new[] { Era.Ancient, Era.Medieval, Era.EarlyModern, Era.Industrial, Era.Modern })
        {
            var forks = TechTree.All.Where(t => t.Crossroad && t.Era == era).ToList();
            Assert.Equal(4, forks.Count);
            Assert.Equal(new[] { Course.Conquest, Course.Commerce, Course.Terror, Course.Tyranny },
                         forks.Select(t => t.Course!.Value).OrderBy(c => c));
            Assert.All(forks, t => Assert.All(t.Requires, r => Assert.NotNull(TechTree.Find(r))));
        }
        Assert.Equal(20, TechTree.All.Count(t => t.Crossroad));
    }

    [Fact]
    public void 갈림길은_여러_갈래를_고를_수_있고_더할수록_비싸진다()
    {
        var sim = EstateTests.State(1, Stage.Kingdom, Era.Ancient, 20000, 3);
        Assert.True(sim.CanResearch(T("caravan")));
        Assert.Equal(T("phalanx").Cost, sim.CostOf(T("phalanx")));
        Assert.True(sim.SetResearch("caravan"));
        sim.ResearchYear(100);
        Assert.True(sim.Knows("caravan"));
        Assert.True(Logged(sim, "【갈림길】 대상의 길을 택했다"));
        Assert.Equal(1, sim.CourseLevel(Course.Commerce));
        Assert.Equal("caravan", sim.CrossroadChoice(Era.Ancient)!.Key);
        // 다른 갈래도 열려 있지만 하나를 익힐 때마다 값이 절반씩 붙는다.
        foreach (var k in new[] { "phalanx", "skulltower", "godking" })
        {
            Assert.True(sim.CanResearch(T(k)));
            Assert.Equal(T(k).Cost * 1.5, sim.CostOf(T(k)), 6);
        }
        Assert.True(sim.GrantCrossroad("phalanx"));
        Assert.False(sim.GrantCrossroad("phalanx"));
        Assert.Equal(T("godking").Cost * 2, sim.CostOf(T("godking")), 6);
        Assert.Equal(1, sim.CourseLevel(Course.Conquest));
        Assert.Null(sim.CrossroadChoice(Era.Medieval));
    }

    [Fact]
    public void 고르지_않으면_나라의_기질을_따라_갈림길을_간다()
    {
        var sim = EstateTests.State(2, Stage.Kingdom, Era.Ancient, 20000, 3);
        sim.Inst.Raiding = true;
        sim.Inst.Slavery = true;
        sim.Inst.Cannibalism = true;
        Assert.Equal(Course.Terror, sim.Leaning());
        sim.ResearchYear(100);
        Assert.True(sim.Knows("skulltower"));
        Assert.Equal(1, sim.CourseLevel(Course.Terror));

        var tyrant = EstateTests.State(3, Stage.Kingdom, Era.Ancient, 20000, 3);
        tyrant.Inst.Absolutism = tyrant.Inst.Inquisition = tyrant.Inst.Surveillance = true;
        Assert.Equal(Course.Tyranny, tyrant.Leaning());
        tyrant.ResearchYear(100);
        Assert.True(tyrant.Knows("godking"));
    }

    [Fact]
    public void 플레이어는_갈림길에서_멈추고_쌓은_지식을_그_값만큼_남겨_둔다()
    {
        var sim = EstateTests.State(4, Stage.Kingdom, Era.Ancient, 20000, 3);
        sim.PlayerRules = true;
        sim.ResearchYear(1000);
        Assert.True(sim.AtCrossroad);
        Assert.Null(sim.Research);
        Assert.Equal(0, TechTree.All.Count(t => t.Crossroad && sim.Knows(t.Key)));
        Assert.Equal(TechTree.All.Where(t => t.Crossroad && sim.CanResearch(t)).Max(t => t.Cost), sim.ResearchProgress);

        Assert.True(sim.SetResearch("phalanx"));
        sim.ResearchYear(0);
        Assert.True(sim.Knows("phalanx"));
        Assert.False(sim.AtCrossroad);
    }

    // ───────── 길이 미는 값 ─────────

    [Fact]
    public void 정복의_길은_군대와_다스릴_땅을_늘리고_교역의_길은_군대를_조금_깎는다()
    {
        var a = EstateTests.State(5, Stage.Kingdom, Era.Industrial, 30000, 3);
        var b = EstateTests.State(5, Stage.Kingdom, Era.Industrial, 30000, 3);
        var c = EstateTests.State(5, Stage.Kingdom, Era.Industrial, 30000, 3);
        foreach (var k in new[] { "phalanx", "chivalry", "drill", "generalstaff" }) Assert.True(b.GrantCrossroad(k));
        foreach (var k in new[] { "caravan", "fairs", "jointstock", "exchange" }) Assert.True(c.GrantCrossroad(k));
        Assert.Equal(4, b.CourseLevel(Course.Conquest));
        Assert.True(b.ArmyStrength() > a.ArmyStrength());
        Assert.True(b.AdminCapacity() > a.AdminCapacity());
        Assert.True(c.ArmyStrength() < a.ArmyStrength());
    }

    [Fact]
    public void 교역의_길은_곳간을_채운다()
    {
        double Treasury(params string[] forks)
        {
            var sim = EstateTests.State(6, Stage.Kingdom, Era.Industrial, 30000, 3);
            foreach (var k in forks) sim.GrantCrossroad(k);
            sim.Realm.Treasury = 0;
            sim.RealmYear();
            return sim.Realm.Treasury;
        }
        double plain = Treasury(), rich = Treasury("caravan", "fairs", "jointstock", "exchange");
        Assert.True(rich > plain, $"보통 {plain:F3} 교역 {rich:F3}");
    }

    // ───────── 공포 ─────────

    [Fact]
    public void 공포가_쌓이면_이웃이_덜_쳐들어온다()
    {
        int Invasions(double dread, int terror)
        {
            var sim = EstateTests.State(1, Stage.Kingdom, Era.Medieval, 30000, 3);
            sim.Inst.Nationalism = true;
            sim.RivalsYear();
            var v = sim.Rivals[0];
            sim.Rivals.RemoveAll(x => x != v);
            v.Trade = v.Allied = false;
            v.TreatyUntil = 0;
            v.Strength = 1;
            sim.SyncMap();
            sim.PlayerRules = true;
            foreach (var k in new[] { "skulltower", "reavers" }.Take(terror)) sim.GrantCrossroad(k);
            int n = 0;
            for (int k = 0; k < 3000; k++)
            {
                v.Hostility = 0.9;
                sim.Realm.Dread = dread;
                sim.InvasionYear();
                if (sim.Armies.FirstOrDefault(a => a.Owner == v.Id) is { } army) { n++; sim.Armies.Remove(army); }
            }
            return n;
        }
        int calm = Invasions(0, 0), feared = Invasions(100, 0), terror = Invasions(100, 2);
        Assert.True(feared < calm && terror < feared, $"침공 {calm}/{feared}/{terror}");
    }

    [Fact]
    public void 공포는_만행과_약탈로_쌓이고_해마다_옅어진다()
    {
        var sim = EstateTests.State(2, Stage.Kingdom, Era.Medieval, 30000, 3);
        sim.Realm.Dread = 50;
        sim.CourseYear();
        Assert.True(sim.Realm.Dread < 50); // 아무 짓도 안 하면 잊힌다
        double d = sim.Realm.Dread;
        sim.Realm.Atrocities += 2;
        sim.CourseYear();
        Assert.True(sim.Realm.Dread > d + 6);
        sim.Feared(2);
        Assert.True(sim.Realm.Dread > d + 8);
        sim.Realm.Dread = 149;
        sim.Feared(10);
        Assert.Equal(150, sim.Realm.Dread);
    }

    // ───────── 식인 풍습 ─────────

    [Fact]
    public void 식인_풍습은_얻는_것과_치르는_값이_있다()
    {
        var d = Catalog.Find("cannibalism")!;
        Assert.Equal("식인 풍습", d.Name);
        Assert.False(string.IsNullOrWhiteSpace(d.Gain));
        Assert.False(string.IsNullOrWhiteSpace(d.Cost));
    }

    [Fact]
    public void 굶어_죽은_이의_몸이_곳간으로_간다()
    {
        var sim = EstateTests.State(3, Stage.Tribe, Era.Primitive, 200, 1);
        double s0 = sim.CommonStore;
        sim.EatTheDead("굶주림");
        Assert.Equal(s0, sim.CommonStore); // 풍습이 없으면 먹지 않는다
        sim.Inst.Cannibalism = true;
        sim.EatTheDead("전쟁");
        Assert.Equal(s0, sim.CommonStore); // 굶주림·버려짐만
        sim.EatTheDead("굶주림");
        sim.EatTheDead("버려짐");
        Assert.Equal(s0 + 6, sim.CommonStore);
        int from = sim.Log.Count;
        sim.CourseYear();
        Assert.True(Logged(sim, "죽은 이 2명의 살이", from));
    }

    [Fact]
    public void 식인하는_무리는_이긴_약탈에서_쓰러진_적을_먹는다()
    {
        var sim = EstateTests.State(4, Stage.Tribe, Era.Primitive, 200, 1);
        sim.Inst.Cannibalism = true;
        var n = sim.Neighbors[0];
        sim.Neighbors.RemoveAll(x => x != n);
        bool ate = false;
        for (int k = 0; k < 300 && !ate; k++)
        {
            n.Fighters = n.MaxFighters = 20;
            n.Food = 0; // 빼앗을 식량이 없으니 곳간이 는 것은 살뿐이다
            sim.Inst.PrivateProperty = false;
            double s0 = sim.CommonStore, d0 = sim.Realm.Dread;
            int from = sim.Log.Count;
            sim.OurRaid(n, sanctioned: true);
            if (Logged(sim, "잔치 솥", from))
            {
                ate = true;
                Assert.True(sim.CommonStore > s0);
                Assert.True(sim.Realm.Dread > d0);
            }
            if (sim.Living.Count(p => p.Sex == Sex.Male) < 6)
                for (int j = 0; j < 6; j++) sim.NewPerson(Sex.Male, 25, sim.People[0].ClanId);
        }
        Assert.True(ate);
    }

    [Fact]
    public void 식인하는_무리는_떨림병에_걸리고_장삿길을_잃고_혼인_동맹을_못_맺는다()
    {
        var sim = EstateTests.State(5, Stage.Kingdom, Era.Medieval, 30000, 3);
        sim.RivalsYear();
        foreach (var v in sim.Rivals) v.Trade = true;
        int trading = sim.Rivals.Count;
        sim.Inst.Cannibalism = true;
        int from = sim.Log.Count;
        for (int k = 0; k < 60; k++) sim.CourseYear();
        Assert.True(Logged(sim, "떨림병", from));
        Assert.True(Logged(sim, "사람을 먹는 나라와는 장사하지 않겠다", from));
        Assert.True(sim.Rivals.Count(v => v.Trade) < trading);

        // 혼인 동맹: 식인 풍습이 있으면 아무리 시도해도 맺어지지 않는다.
        var tribe = EstateTests.State(6, Stage.Tribe, Era.Primitive, 200, 1);
        tribe.Inst.MarriageAlliance = true;
        tribe.Inst.Cannibalism = true;
        for (int k = 0; k < 400; k++)
        {
            foreach (var nb in tribe.Neighbors) nb.Hostility = 0;
            tribe.Step();
        }
        Assert.DoesNotContain(tribe.Neighbors, nb => nb.Allied);
    }

    // ───────── 승리 ─────────
    static void Years(Simulation sim, int n) { for (int k = 0; k < n; k++) sim.CourseYear(); }

    [Fact]
    public void 제국을_15년_지키면_제국_건설_승리()
    {
        var sim = EstateTests.State(1, Stage.Empire, Era.Medieval, 80000, 9);
        Assert.False(sim.VictoryMet(Course.Conquest)); // 정복의 길 2단계가 모자라다
        sim.GrantCrossroad("phalanx");
        sim.GrantCrossroad("chivalry");
        Assert.True(sim.VictoryMet(Course.Conquest));
        Years(sim, 14);
        Assert.Empty(sim.Won);
        Assert.Equal(14, sim.VictoryNeeds(Course.Conquest).streak);
        Years(sim, 1);
        Assert.Contains(sim.Won, w => w.Course == Course.Conquest);
        Assert.True(Logged(sim, "【승리】 제국 건설"));
        Assert.Equal(1, sim.VictoryProgress(Course.Conquest));
        Years(sim, 5);
        Assert.Single(sim.Won); // 한 번만
    }

    [Fact]
    public void 조건이_한_해라도_깨지면_다시_센다()
    {
        var sim = EstateTests.State(1, Stage.Empire, Era.Medieval, 80000, 9);
        sim.GrantCrossroad("phalanx");
        sim.GrantCrossroad("chivalry");
        Years(sim, 10);
        sim.Realm.Territory = 8;
        Years(sim, 1);
        Assert.Equal(0, sim.VictoryNeeds(Course.Conquest).streak);
        sim.Realm.Territory = 9;
        Years(sim, 14);
        Assert.Empty(sim.Won);
    }

    [Fact]
    public void 교역국과_곳간을_지키면_경제_승리()
    {
        var sim = EstateTests.State(2, Stage.Kingdom, Era.Industrial, 50000, 4);
        sim.RivalsYear();
        Assert.True(sim.Rivals.Count >= 3);
        sim.GrantCrossroad("caravan");
        sim.GrantCrossroad("fairs");
        foreach (var v in sim.Rivals.Take(3)) v.Trade = true;
        sim.Realm.Treasury = 6;
        Assert.False(sim.VictoryMet(Course.Commerce)); // 교역의 길이 아직 2단계
        sim.GrantCrossroad("jointstock");
        sim.Realm.Treasury = 0;
        Years(sim, 15);
        Assert.Empty(sim.Won); // 곳간이 비었다
        var needs = sim.VictoryNeeds(Course.Commerce).needs;
        Assert.Contains(needs, x => x.what.StartsWith("곳간") && !x.ok);
        foreach (var v in sim.Rivals.Take(3)) v.Trade = true;
        sim.Realm.Treasury = 6;
        Years(sim, 15);
        Assert.Contains(sim.Won, w => w.Course == Course.Commerce);
        Assert.True(Logged(sim, "【승리】 경제 승리"));
    }

    [Fact]
    public void 공포_80에_닿으면_공포의_군림()
    {
        var sim = EstateTests.State(3, Stage.Kingdom, Era.Medieval, 30000, 3);
        sim.GrantCrossroad("skulltower");
        sim.GrantCrossroad("reavers");
        sim.Realm.Dread = 60;
        Years(sim, 1);
        Assert.Empty(sim.Won);
        Assert.True(sim.VictoryProgress(Course.Terror) < 1);
        sim.Realm.Dread = 90;
        Years(sim, 1);
        Assert.Contains(sim.Won, w => w.Course == Course.Terror);
        Assert.True(Logged(sim, "【승리】 공포의 군림"));
    }

    [Fact]
    public void 의회_없이_정통성을_30년_지키면_영원한_독재()
    {
        var sim = EstateTests.State(4, Stage.Kingdom, Era.Medieval, 30000, 3);
        sim.GrantCrossroad("godking");
        sim.GrantCrossroad("divineright");
        sim.Inst.Absolutism = true;
        sim.Realm.Legitimacy = 0.8;
        sim.Inst.Parliament = true;
        Assert.False(sim.VictoryMet(Course.Tyranny));
        sim.Inst.Parliament = false;
        Assert.True(sim.VictoryMet(Course.Tyranny));
        Years(sim, 29);
        Assert.Empty(sim.Won);
        Years(sim, 1);
        Assert.Contains(sim.Won, w => w.Course == Course.Tyranny);
        Assert.True(Logged(sim, "【승리】 영원한 독재"));
    }

    [Fact]
    public void 근대에_지식과_의무교육이면_계몽()
    {
        var sim = EstateTests.State(5, Stage.Kingdom, Era.Industrial, 30000, 3);
        sim.Realm.Knowledge = 2500;
        Assert.False(sim.VictoryMet(Course.Learning));
        sim.Inst.PublicSchooling = true;
        Years(sim, 1);
        Assert.Contains(sim.Won, w => w.Course == Course.Learning);
        Assert.True(Logged(sim, "【승리】 계몽"));
    }

    [Fact]
    public void 승리_조건은_모두_보여_줄_설명이_있다()
    {
        var sim = EstateTests.State(6, Stage.Kingdom, Era.Medieval, 30000, 3);
        Assert.Equal(5, Simulation.Victories.Count);
        foreach (var v in Simulation.Victories)
        {
            var (needs, streak, hold) = sim.VictoryNeeds(v.Course);
            Assert.NotEmpty(needs);
            Assert.Equal(v.Hold, hold);
            Assert.Equal(0, streak);
            double p = sim.VictoryProgress(v.Course);
            Assert.InRange(p, 0, 0.99);
        }
    }

    // ───────── 길을 노리는 플레이어 ─────────

    static Simulation Played(int seed, int years, Course aim)
    {
        var sim = new Simulation(seed) { PlayerRules = true };
        var policy = new Random(seed * 31 + 7);
        for (int t = 0; t < years * 4 && sim.Living.Any(); t++)
        {
            PlayerBot.Season(sim, policy, aim);
            sim.Step();
        }
        return sim;
    }

    [Theory]
    [InlineData(Course.Terror)]
    [InlineData(Course.Tyranny)]
    public void 길을_노리는_판도_결정적이고_저장에서_되살아난다(Course aim)
    {
        var a = Played(3, 260, aim);
        var b = Played(3, 260, aim);
        Assert.Equal(a.Fingerprint(), b.Fingerprint());
        Assert.True(a.CourseLevel(aim) >= 1, $"{Simulation.CourseName(aim)} {a.CourseLevel(aim)}단계");
        string text = SaveGame.Write(a, "길");
        var c = SaveGame.Read(text);
        Assert.False(SaveGame.Drifted(c, text));
        Assert.Equal(a.Log.Select(e => e.Text), c.Log.Select(e => e.Text));
        Assert.Equal(a.Won, c.Won);
    }

    [Fact]
    public void 길_이름을_읽는다()
    {
        Assert.Equal(Course.Conquest, Simulation.ParseCourse("empire"));
        Assert.Equal(Course.Tyranny, Simulation.ParseCourse(" 독재 "));
        Assert.Null(Simulation.ParseCourse("?"));
        foreach (var c in Enum.GetValues<Course>())
        {
            Assert.Equal(c, Simulation.ParseCourse(Simulation.CourseKey(c)));
            Assert.Equal(c, Simulation.ParseCourse(Simulation.CourseName(c)));
        }
    }
}
