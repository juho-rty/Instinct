using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>난이도: 보통은 원래 규칙 그대로, 쉬움·어려움은 수확·위협을 민다. 저장에 함께 적힌다.</summary>
public class DifficultyTests
{
    static Simulation Played(int seed, int years, Difficulty d)
    {
        var sim = new Simulation(seed) { PlayerRules = true, Difficulty = d };
        var policy = new Random(seed * 31 + 7);
        for (int t = 0; t < years * 4 && sim.Living.Any(); t++)
        {
            PlayerBot.Season(sim, policy);
            sim.Step();
        }
        return sim;
    }

    [Fact]
    public void 보통은_기본값이고_원래_규칙과_같다()
    {
        Assert.Equal(Difficulty.Normal, new Simulation(1).Difficulty);
        var a = new Simulation(5);
        var b = new Simulation(5) { Difficulty = Difficulty.Normal };
        for (int t = 0; t < 400; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.Fingerprint(), b.Fingerprint());
    }

    [Fact]
    public void 쉬울수록_첫_수확이_많다()
    {
        double Store(Difficulty d)
        {
            var sim = new Simulation(4) { Difficulty = d };
            sim.Step(); // 난수는 같은 수만큼 뽑으니 첫 철의 차이는 수확의 차이다
            return sim.CommonStore + sim.Living.Sum(p => p.Food);
        }
        double easy = Store(Difficulty.Easy), normal = Store(Difficulty.Normal), hard = Store(Difficulty.Hard);
        Assert.True(easy > normal && normal > hard, $"쉬움 {easy:F2} 보통 {normal:F2} 어려움 {hard:F2}");
    }

    [Fact]
    public void 어려울수록_자주_침공당하고_적군이_많다()
    {
        (int count, double men) Invasions(Difficulty d)
        {
            var sim = EstateTests.State(1, Stage.Kingdom, Era.Medieval, 30000, 3);
            sim.Inst.Raiding = true;
            sim.Inst.Nationalism = true;
            sim.RivalsYear();
            var v = sim.Rivals[0];
            sim.Rivals.RemoveAll(x => x != v);
            v.Trade = v.Allied = false;
            v.TreatyUntil = 0;
            v.Strength = 1;
            sim.SyncMap();
            sim.PlayerRules = true;
            sim.Difficulty = d;
            int n = 0;
            double men = 0;
            for (int k = 0; k < 3000; k++)
            {
                v.Hostility = 0.9;
                sim.InvasionYear();
                if (sim.Armies.FirstOrDefault(a => a.Owner == v.Id) is { } army)
                {
                    n++;
                    men += army.Men;
                    sim.Armies.Remove(army);
                }
            }
            return (n, n > 0 ? men / n : 0);
        }
        var easy = Invasions(Difficulty.Easy);
        var normal = Invasions(Difficulty.Normal);
        var hard = Invasions(Difficulty.Hard);
        Assert.True(easy.count < normal.count && normal.count < hard.count, $"침공 {easy.count}/{normal.count}/{hard.count}");
        Assert.True(easy.men < normal.men && normal.men < hard.men, $"적군 {easy.men:F0}/{normal.men:F0}/{hard.men:F0}");
    }

    [Fact]
    public void 어려울수록_나라가_덜_묶이고_덜_믿긴다()
    {
        (double coh, double legit) After(Difficulty d)
        {
            var sim = EstateTests.State(2, Stage.Kingdom, Era.Medieval, 30000, 3);
            sim.Difficulty = d;
            sim.Realm.Cohesion = 0.6;
            sim.Realm.Legitimacy = 0.6;
            sim.RealmYear();
            return (sim.Realm.Cohesion, sim.Realm.Legitimacy);
        }
        var easy = After(Difficulty.Easy);
        var normal = After(Difficulty.Normal);
        var hard = After(Difficulty.Hard);
        Assert.True(easy.coh > normal.coh && normal.coh > hard.coh, $"결속 {easy.coh:F3}/{normal.coh:F3}/{hard.coh:F3}");
        Assert.True(easy.legit > normal.legit && normal.legit > hard.legit, $"정통성 {easy.legit:F3}/{normal.legit:F3}/{hard.legit:F3}");
    }

    [Fact]
    public void 난이도는_저장에_적히고_불러와도_같은_판이_된다()
    {
        var a = Played(6, 220, Difficulty.Hard);
        string text = SaveGame.Write(a, "어려움");
        Assert.Contains("diff\thard", text);
        Assert.Equal(Difficulty.Hard, SaveGame.Peek(text)!.Difficulty);
        var b = SaveGame.Read(text);
        Assert.Equal(Difficulty.Hard, b.Difficulty);
        Assert.False(SaveGame.Drifted(b, text));
        Assert.Equal(a.Log.Select(e => e.Text), b.Log.Select(e => e.Text));

        // 같은 명령이라도 난이도가 다르면 다른 판이다: 머리말에서 난이도를 지우면 지문이 어긋난다.
        string asNormal = text.Replace("diff\thard\n", "");
        Assert.Equal(Difficulty.Normal, SaveGame.Peek(asNormal)!.Difficulty); // 난이도 줄이 없는 옛 저장은 보통
        Assert.True(SaveGame.Drifted(SaveGame.Read(asNormal), asNormal) || SaveGame.Read(asNormal).Fingerprint() != a.Fingerprint());

        Assert.Null(SaveGame.Peek(text.Replace("diff\thard", "diff\tnightmare")));
    }

    [Fact]
    public void 난이도_이름을_읽는다()
    {
        Assert.Equal(Difficulty.Easy, Simulation.ParseDifficulty("쉬움"));
        Assert.Equal(Difficulty.Hard, Simulation.ParseDifficulty(" HARD "));
        Assert.Null(Simulation.ParseDifficulty("?"));
        foreach (var d in Enum.GetValues<Difficulty>())
            Assert.Equal(d, Simulation.ParseDifficulty(Simulation.DifficultyKey(d)));
        Assert.Equal("어려움", Simulation.DifficultyName(Difficulty.Hard));
    }
}
