using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// 명령 기록과 저장·불러오기, 무리의 일(큰 사냥·잔치·선물·습격), 플레이어 흉내(PlayerBot), 침공군의 집결.
/// </summary>
public class SaveTribeTests
{
    static bool Logged(Simulation sim, string part, int from = 0) => EstateTests.Logged(sim, part, from);

    static Simulation Played(int seed, int years)
    {
        var sim = new Simulation(seed) { PlayerRules = true };
        var policy = new Random(seed * 31 + 7);
        for (int t = 0; t < years * 4 && sim.Living.Any(); t++)
        {
            PlayerBot.Season(sim, policy);
            sim.Step();
        }
        return sim;
    }

    // ───────── 저장과 불러오기 ─────────

    [Fact]
    public void 저장한_판을_불러오면_같은_판이_된다()
    {
        var a = Played(3, 320);
        Assert.True(a.Journal.Count > 20, "플레이어 흉내가 명령을 거의 내리지 않았다");
        string text = SaveGame.Write(a, "시험");
        var b = SaveGame.Read(text);

        Assert.Equal(a.Tick, b.Tick);
        Assert.False(SaveGame.Drifted(b, text));
        Assert.Equal(a.Log.Select(e => e.Text), b.Log.Select(e => e.Text));
        Assert.Equal(a.Journal, b.Journal);
        Assert.Equal(SaveGame.Write(a, "시험"), SaveGame.Write(b, "시험"));

        // 불러온 뒤에도 같은 길을 간다.
        for (int t = 0; t < 80; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.Fingerprint(), b.Fingerprint());
        Assert.Equal(a.Log.Count, b.Log.Count);
    }

    [Fact]
    public void 저장_머리말을_읽고_깨진_글은_거절한다()
    {
        var sim = new Simulation(9) { PlayerRules = true };
        for (int t = 0; t < 10; t++) sim.Step();
        sim.Note("탭\t과 줄\n바꿈이 든 말");
        string text = SaveGame.Write(sim, "이름\t하나");
        var info = SaveGame.Peek(text);
        Assert.NotNull(info);
        Assert.Equal(9, info!.Seed);
        Assert.Equal(10, info.Tick);
        Assert.True(info.PlayerRules);
        Assert.Equal("이름\t하나", info.Label);
        var back = SaveGame.Read(text);
        Assert.True(Logged(back, "탭\t과 줄\n바꿈이 든 말"));

        Assert.Null(SaveGame.Peek("아무 글"));
        Assert.Throws<FormatException>(() => SaveGame.Read("아무 글"));
        Assert.Throws<FormatException>(() => SaveGame.Read("INSTINCT-SAVE 1\nseed\t3\n"));
    }

    [Fact]
    public void 한_철이_도는_동안_나라가_한_일은_명령으로_적지_않는다()
    {
        var (sim, _) = RuledState();
        var def = Catalog.All.First(d => !d.Get(sim.Inst) && sim.Available(d));
        sim.Realm.ReformHeat = 0;
        sim.ProposeYear();
        var ask = Assert.Single(sim.Pending, d => d.Kind == "adopt");
        int before = sim.Journal.Count;
        Assert.True(sim.Decide(ask.Id, 0));
        // 결정 하나만 적힌다: 그 안에서 벌어진 제도 도입은 따로 적히지 않는다.
        Assert.Equal(before + 1, sim.Journal.Count);
        Assert.Equal("decide", sim.Journal[^1].Kind);
        Assert.True(Catalog.Find(ask.Subject)!.Get(sim.Inst));

        sim.AskRelief(100);
        before = sim.Journal.Count;
        for (int t = 0; t < 6; t++) sim.Step(); // 기한이 지나 스스로 닫힌다
        Assert.DoesNotContain(sim.Pending, d => d.Kind == "relief");
        Assert.Equal(before, sim.Journal.Count);
        _ = def;
    }

    static (Simulation sim, Rival v) RuledState()
    {
        var sim = EstateTests.State(1, Stage.Kingdom, Era.Medieval, 30000, 3);
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

    // ───────── 무리의 일 ─────────

    /// <summary>우두머리와 이웃이 있는 무리(몇 해 묵힌 판).</summary>
    static Simulation Band(int seed = 2)
    {
        var sim = new Simulation(seed);
        for (int t = 0; t < 8; t++) sim.Step();
        Assert.NotNull(sim.Chief);
        Assert.True(sim.Neighbors.Count > 0);
        return sim;
    }

    [Fact]
    public void 큰_사냥은_식량을_얻고_한_해에_한_번이다()
    {
        var sim = Band();
        double before = sim.CommonStore + sim.Living.Sum(p => p.Food);
        Assert.Equal("", sim.WhyCantTribe("hunt"));
        Assert.True(sim.TribeAct("hunt"));
        Assert.True(sim.CommonStore + sim.Living.Sum(p => p.Food) > before);
        Assert.True(Logged(sim, "큰 사냥"));
        Assert.Equal("tribe", sim.Journal[^1].Kind);
        // 같은 철에는 다른 일도 못 벌이고, 다음 철에도 사냥은 올해 끝났다.
        Assert.False(sim.TribeAct("feast"));
        sim.Step();
        Assert.Contains("올해", sim.WhyCantTribe("hunt"));
    }

    [Fact]
    public void 큰_사냥에서_사람이_다치고_죽을_수_있다()
    {
        bool hurt = false, died = false;
        for (int seed = 1; seed <= 400 && !(hurt && died); seed++)
        {
            var sim = new Simulation(seed);
            for (int t = 0; t < 4; t++) sim.Step();
            if (sim.WhyCantTribe("hunt").Length > 0) continue;
            sim.TribeAct("hunt");
            hurt |= Logged(sim, "다친 이:");
            if (Logged(sim, "돌아오지 못한 이:"))
            {
                died = true;
                Assert.True(sim.DeathsByCause.GetValueOrDefault("사냥") > 0);
            }
        }
        Assert.True(hurt, "사냥에서 다친 이가 한 번도 나오지 않았다");
        Assert.True(died, "사냥에서 죽은 이가 한 번도 나오지 않았다");
    }

    [Fact]
    public void 잔치는_창고를_헐어_원한을_푼다()
    {
        var sim = Band();
        var c = sim.Chief!;
        var adults = sim.Living.Where(p => p != c && p.Age(sim.Tick) >= 14).ToList();
        var holder = adults[0];
        var target = adults[1];
        holder.Grudge[target.Id] = 1.0;
        holder.GrudgeWhy[target.Id] = "시험";
        var greedy = adults[2];
        greedy.Traits[(int)Trait.Greed] = 0.95;

        sim.CommonStore = 1;
        Assert.Contains("부족", sim.WhyCantTribe("feast"));
        sim.CommonStore = 60;
        double status = c.Status;
        Assert.True(sim.TribeAct("feast"));
        Assert.True(sim.CommonStore < 60);
        Assert.Equal(0.7, holder.Grudge[target.Id], 3);
        Assert.True(c.Status > status);
        Assert.True(greedy.Grudge.GetValueOrDefault(c.Id) > 0, "욕심 많은 이가 잔치를 못마땅해하지 않았다");
        Assert.True(Logged(sim, "잔치를 벌였다"));
    }

    [Fact]
    public void 선물은_적의를_낮추고_피붙이를_잃은_이의_원망을_산다()
    {
        var sim = Band();
        var c = sim.Chief!;
        var n = sim.Neighbors[0];
        n.Hostility = 2;
        var mourner = sim.Living.First(p => p != c && p.Age(sim.Tick) >= 14);
        n.Mourners[mourner.Id] = 1.0;
        sim.CommonStore = 30;
        Assert.Contains("골라야", sim.WhyCantTribe("gift"));
        Assert.True(sim.TribeAct("gift", 0));
        Assert.True(n.Hostility < 1.1);
        Assert.True(sim.CommonStore < 30);
        Assert.True(mourner.Grudge.GetValueOrDefault(c.Id) > 0);
        Assert.True(Logged(sim, "선물로 보냈다"));
    }

    [Fact]
    public void 식량이_없으면_선물은_빈손으로_끝난다()
    {
        var sim = Band();
        sim.CommonStore = 0;
        foreach (var p in sim.Living) p.Food = 0;
        double before = sim.Neighbors[0].Hostility = 2;
        Assert.True(sim.TribeAct("gift", 0));
        Assert.Equal(before, sim.Neighbors[0].Hostility);
        Assert.True(Logged(sim, "거둘 식량이 없었다"));
    }

    [Fact]
    public void 습격_명령은_싸움을_일으키거나_우두머리의_말을_가볍게_한다()
    {
        bool fought = false, refused = false;
        for (int seed = 1; seed <= 200 && !(fought && refused); seed++)
        {
            var sim = new Simulation(seed);
            for (int t = 0; t < 8; t++) sim.Step();
            if (sim.WhyCantTribe("raid", 0).Length > 0) continue;
            int from = sim.Log.Count;
            Assert.True(sim.TribeAct("raid", 0));
            Assert.True(sim._raidedThisYear);
            Assert.Contains("올해는 이미", sim.WhyCantTribe("raid", 0) + (sim.WhyCantTribe("raid", 0).Contains("이번 계절") ? "올해는 이미" : ""));
            fought |= Logged(sim, "치러 떠났다", from);
            refused |= Logged(sim, "따라나서는 사내가 모자랐다", from);
        }
        Assert.True(fought, "습격이 한 번도 벌어지지 않았다");
        Assert.True(refused, "사내들이 따르지 않는 경우가 한 번도 없었다");
    }

    [Fact]
    public void 나라가_서면_무리의_일은_닫힌다()
    {
        var (sim, _) = RuledState();
        Assert.Contains("나라가 세워진 뒤", sim.WhyCantTribe("hunt"));
        Assert.False(sim.TribeAct("hunt"));
        Assert.Equal("없는 행동", sim.WhyCantTribe("없는일"));
        foreach (var a in TribeActions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Gain));
            Assert.False(string.IsNullOrWhiteSpace(a.Cost));
        }
    }

    // ───────── 침공군의 집결과 승산 ─────────

    [Fact]
    public void 침공군은_두_철을_모인_뒤에야_움직인다()
    {
        var (sim, v) = RuledState();
        var inv = sim.Invade(v)!;
        Assert.Equal(2, inv.Muster);
        int at = inv.At;
        sim.ArmiesMove();
        sim.ArmiesMove();
        Assert.Equal(at, inv.At);
        Assert.Equal(0, inv.Muster);
        sim.ArmiesMove();
        Assert.True(!sim.Armies.Contains(inv) || inv.At != at, "모이기를 마친 침공군이 움직이지 않았다");
    }

    [Fact]
    public void 승산은_빈_땅에서_1이고_군사가_많을수록_오른다()
    {
        var (sim, v) = RuledState();
        var a = sim.RaiseArmy(sim.Map.Capital)!;
        var free = sim.Map.Provinces.First(p => p.Owner == -1);
        Assert.Equal(1, sim.AttackOdds(a.Id, free.Id));
        var theirs = sim.Lands(v.Id).First();
        double small = sim.AttackOdds(a.Id, theirs.Id);
        a.Men *= 4;
        double big = sim.AttackOdds(a.Id, theirs.Id);
        Assert.InRange(small, 0.0001, 0.9999);
        Assert.True(big > small);
        Assert.Equal(0, sim.AttackOdds(999, theirs.Id));
    }

    // ───────── 플레이어 흉내 ─────────

    [Fact]
    public void 플레이어_흉내는_결정적이고_물음에_답하며_군단을_움직인다()
    {
        var a = Played(5, 420);
        var b = Played(5, 420);
        Assert.Equal(a.Fingerprint(), b.Fingerprint());
        Assert.Equal(a.Journal, b.Journal);
        var kinds = a.Journal.Select(c => c.Kind).ToHashSet();
        Assert.Contains("decide", kinds);
        Assert.Contains("tribe", kinds);
        Assert.Contains("raise", kinds);
        Assert.Contains("order", kinds);
        Assert.True(a.Realm.PeakStage >= Stage.CityState);
    }
}
