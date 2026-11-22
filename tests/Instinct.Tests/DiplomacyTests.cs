using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 긴 실행에서도 좀처럼 터지지 않는 경로를 상태를 직접 만들어 강제로 밟는다.
/// 이웃은 첫 철에 생기므로 모든 테스트는 한 철을 돌린 뒤 시작한다.
/// </summary>
public class DiplomacyTests
{
    const int Tries = 3000;

    static (Simulation sim, Neighbor n) OneNeighbor(int seed)
    {
        var sim = new Simulation(seed);
        sim.Step();
        var n = sim.Neighbors[0];
        sim.Neighbors.RemoveAll(x => x != n);
        n.Hostility = 0;
        n.Vengeance = 0;
        return (sim, n);
    }

    static bool Logged(Simulation sim, string part, int from = 0) =>
        sim.Log.Skip(from).Any(e => e.Text.Contains(part));

    // ───────── 경로 1: 이웃이 흉년에 화친을 깬다 ─────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 이웃은_흉년에_화친을_깨고_쳐들어온다(int seed)
    {
        var (sim, n) = OneNeighbor(seed);
        n.PeaceUntil = sim.Tick + 400;
        n.Hostility = 3;
        sim._yearFactor = 0.5;
        int from = sim.Log.Count;

        for (int i = 0; i < Tries && n.PeaceUntil != 0; i++) sim.EnemyRaids();

        Assert.Equal(0, n.PeaceUntil);
        Assert.True(Logged(sim, "흉년을 못 이겨 화친을 깼다", from));
        Assert.True(Logged(sim, "마을을 덮", from), "화친을 깬 바로 그 철에 습격이 이어져야 한다");
    }

    [Fact]
    public void 풍년에는_화친이_깨지지_않는다()
    {
        var (sim, n) = OneNeighbor(1);
        n.PeaceUntil = sim.Tick + 400;
        n.Hostility = 3;
        sim._yearFactor = 1.0;
        int from = sim.Log.Count;

        for (int i = 0; i < Tries; i++) sim.EnemyRaids();

        Assert.Equal(sim.Tick + 400, n.PeaceUntil);
        Assert.False(Logged(sim, "마을을 덮", from));
    }

    // ───────── 경로 2: 우리 사내들이 평화를 깨고 복수하러 간다 ─────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 복수심이_차면_사내들이_화친을_깨고_친다(int seed)
    {
        var (sim, n) = OneNeighbor(seed);
        Assert.False(sim.Inst.Raiding);
        n.PeaceUntil = sim.Tick + 400;
        n.Vengeance = 5;
        double before = n.Hostility;
        int from = sim.Log.Count;

        for (int i = 0; i < Tries && n.PeaceUntil != 0; i++)
        {
            sim._raidedThisYear = false;
            n.Vengeance = 5;
            sim.MaybeRaid();
        }

        Assert.Equal(0, n.PeaceUntil);
        Assert.True(Logged(sim, "피를 갚으러 떠났다", from));
        Assert.True(Logged(sim, "화친을 깨고", from));
        Assert.True(n.Hostility >= before + 1.5, "배신당한 이웃의 적의가 올라야 한다");
    }

    [Fact]
    public void 복수심이_차면_사내들이_혼인_동맹도_깬다()
    {
        var (sim, n) = OneNeighbor(1);
        var bride = sim.NewPerson(Sex.Female, 20, n.ClanId);
        n.Allied = true;
        n.Bride = bride;
        n.Vengeance = 5;
        int from = sim.Log.Count;

        for (int i = 0; i < Tries && n.Allied; i++)
        {
            sim._raidedThisYear = false;
            n.Vengeance = 5;
            sim.MaybeRaid();
        }

        Assert.False(n.Allied);
        Assert.Null(n.Bride);
        Assert.True(Logged(sim, "혼인 동맹을 깨고", from));
    }

    [Fact]
    public void 복수심이_낮으면_평화를_깨지_않는다()
    {
        var (sim, n) = OneNeighbor(1);
        n.PeaceUntil = sim.Tick + 400;
        n.Vengeance = 1.0;

        for (int i = 0; i < Tries; i++)
        {
            sim._raidedThisYear = false;
            sim.MaybeRaid();
        }

        Assert.Equal(sim.Tick + 400, n.PeaceUntil);
    }

    // ───────── 혼인 동맹의 값: 지참 예물 ─────────

    static (Person maiden, Person groom) AddPair(Simulation sim)
    {
        int clan = sim.Living.First().ClanId;
        return (sim.NewPerson(Sex.Female, 18, clan), sim.NewPerson(Sex.Male, 22, clan));
    }

    [Fact]
    public void 예물을_못_마련하면_혼인_동맹은_성사되지_않는다()
    {
        var (sim, n) = OneNeighbor(1);
        var (maiden, _) = AddPair(sim);
        sim.CommonStore = 0;
        foreach (var p in sim.Living) p.Food = 0;
        int from = sim.Log.Count;

        bool tried = sim.TryAlliance(n, sim.Chief!);

        Assert.False(tried);
        Assert.False(n.Allied);
        Assert.True(maiden.Alive);
        Assert.True(n.NextAllianceTick > sim.Tick, "한동안 다시 청하지 못해야 한다");
        Assert.True(Logged(sim, "예물", from));
    }

    [Fact]
    public void 혼인_동맹은_예물을_치르고_딸을_보내야_맺어진다()
    {
        var (sim, n) = OneNeighbor(1);
        var (maiden, groom) = AddPair(sim);
        sim.CommonStore = 200;
        double storeBefore = sim.CommonStore, theirsBefore = n.Food;
        int from = sim.Log.Count;

        for (int i = 0; i < 200 && !n.Allied; i++) sim.TryAlliance(n, sim.Chief!);

        Assert.True(n.Allied);
        Assert.False(maiden.Alive);
        Assert.Equal("시집감", maiden.DeathCause);
        Assert.NotNull(n.Bride);
        Assert.Same(groom, n.Bride!.Husband);
        double paid = storeBefore - sim.CommonStore;
        Assert.True(paid >= 8, $"예물이 빠져나가야 한다 (실제 {paid:F1})");
        Assert.Equal(paid, n.Food - theirsBefore, 6);
        Assert.True(Logged(sim, "예물로 식량", from));
    }

    [Fact]
    public void 적의가_깊을수록_예물이_커진다()
    {
        double Paid(double hostility)
        {
            var (sim, n) = OneNeighbor(1);
            AddPair(sim);
            sim.CommonStore = 500;
            n.Hostility = hostility;
            n.PeaceUntil = sim.Tick + 400; // 화친 중이면 성사 확률이 올라 시도 횟수가 줄 뿐, 예물 크기에는 영향이 없다
            for (int i = 0; i < 2000 && !n.Allied; i++) { n.Hostility = hostility; sim.TryAlliance(n, sim.Chief!); }
            Assert.True(n.Allied);
            return 500 - sim.CommonStore;
        }

        Assert.True(Paid(0.9) > Paid(0) * 1.3);
    }

    // ───────── 혼인 동맹의 값: 원군 ─────────

    [Fact]
    public void 사돈이_원군을_청하면_우리_사내가_나가_죽는다()
    {
        var (sim, n) = OneNeighbor(1);
        n.Allied = true;
        n.Bride = sim.NewPerson(Sex.Female, 20, n.ClanId);
        int clan = sim.Living.First().ClanId;
        // 여러 번 불려 나가도 사내가 남도록 넉넉히 넣는다. 한 어머니의 아들들이라 죽으면 슬퍼할 피붙이가 있다.
        var mother = sim.Living.First(p => !p.IsMale && p != sim.Chief && p != n.Bride);
        for (int i = 0; i < 40; i++) sim.NewPerson(Sex.Male, 20, clan).Mother = mother;
        int from = sim.Log.Count;

        for (int i = 0; i < 20 && n.Allied; i++) sim.AidCall(n);

        Assert.True(sim.DeathsByCause.GetValueOrDefault("전쟁") > 0, "원군으로 나간 사내 가운데 죽는 이가 있어야 한다");
        Assert.True(Logged(sim, "원군을 청해", from));
        Assert.True(n.Allied, "원군을 보냈으면 동맹은 유지된다");
        // 죽은 이의 피붙이는 보낸 우두머리를 원망한다.
        Assert.Contains(sim.Living, p => p.GrudgeWhy.Values.Any(w => w.Contains("사돈의 싸움에 내보내")));
    }

    [Fact]
    public void 보낼_사내가_없으면_동맹이_깨진다()
    {
        var (sim, n) = OneNeighbor(1);
        n.Allied = true;
        n.Bride = sim.NewPerson(Sex.Female, 20, n.ClanId);
        // 사내 셋만 남기고 나머지는 늙힌다.
        foreach (var m in sim.Living.Where(p => p.IsMale).Skip(3)) m.BirthTick = sim.Tick - 70 * 4;
        double before = n.Hostility;

        sim.AidCall(n);

        Assert.False(n.Allied);
        Assert.True(n.Hostility > before);
        Assert.True(Logged(sim, "약속을 저버렸다"));
    }

    // ───────── 결정성 ─────────

    [Fact]
    public void 같은_씨앗은_같은_연대기를_낳는다()
    {
        static List<string> Run()
        {
            var sim = new Simulation(7);
            var i = sim.Inst;
            i.PrivateProperty = i.Monogamy = i.BloodMoney = i.Raiding = i.Slavery = i.Parley = i.Tribute = i.MarriageAlliance = true;
            for (int t = 0; t < 80 * 4; t++) sim.Step();
            return sim.Log.Select(e => $"{e.Tick}:{e.Text}").ToList();
        }

        Assert.Equal(Run(), Run());
    }
}
