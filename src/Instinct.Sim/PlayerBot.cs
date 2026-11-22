namespace Instinct.Sim;

/// <summary>
/// 플레이어 흉내: <see cref="Simulation.PlayerRules"/>를 켠 판을 사람처럼 다스린다. 물음에 답하고, 남는 돈으로 짓고,
/// 군단을 일으켜 침공을 막고 빈 땅과 약한 이웃을 친다. 부족 단계에서는 사냥·잔치·선물을 벌인다.
/// 플레이어 규칙의 균형을 재는 데 쓴다(실행기의 --player). 제 난수(policy)만 쓰고 시뮬레이션의 난수는 건드리지 않는다.
/// </summary>
public static class PlayerBot
{
    /// <summary>한 철이 돌기 전에 부른다.</summary>
    /// <param name="aim">따라갈 길. 갈림길에서 그 갈래를 고르고, 그 길에 맞는 제도를 들이고 맞지 않는 것을 물리친다. null이면 나라의 기질(Leaning)을 따른다.</param>
    public static void Season(Simulation sim, Random policy, Course? aim = null)
    {
        if (!sim.Living.Any()) return;
        sim.Proposals = true; // 흉내는 신하의 건의(결정)를 받아 제도를 고른다. 사람이 하는 판은 기본으로 꺼져 있어 법전에서 직접 고른다.
        Answer(sim, aim);
        Steer(sim, aim);
        if (sim.Tribal) TribeLife(sim);
        if (sim.Season == 0) AutoPolicy.Works(sim, policy);
        if (sim.Realm.Stage >= Stage.CityState) Command(sim);
    }

    static void Answer(Simulation sim, Course? aim)
    {
        var r = sim.Realm;
        foreach (var d in sim.Pending.OrderBy(d => d.Id).ToList())
        {
            bool last = sim.Tick + 1 >= d.ExpiresTick;
            switch (d.Kind)
            {
                case "adopt":
                    if (Catalog.Find(d.Subject) is not { } def) { sim.Decide(d.Id, 1); break; }
                    bool afford = r.Stage == Stage.Tribe || r.Treasury - sim.PreviewReform(def).Cost >= -1;
                    if (Shuns(def.Key, aim)) sim.Decide(d.Id, 1);
                    else if (afford && r.ReformHeat < 0.5) sim.Decide(d.Id, 0);
                    else if (last) sim.Decide(d.Id, 1);
                    break;
                case "relief":
                    sim.Decide(d.Id, r.Treasury >= 2 ? 0 : r.Grievance[(int)Estate.Nobles] < 0.4 ? 1 : 2);
                    break;
                case "conquest":
                    sim.Decide(d.Id, r.Treasury >= 0 ? 0 : 1);
                    break;
                case "tribute":
                    // 지킬 군단이 서 있으면 맞서고, 없으면 값을 치른다.
                    sim.Decide(d.Id, sim.Armies.Any(a => a.Owner == 0 && a.Men >= a.FullMen * 0.7) ? 1 : 0);
                    break;
            }
        }
    }

    // 길마다 서둘러 들이는 제도와 물리치는 제도.
    static readonly Dictionary<Course, string[]> Wants = new()
    {
        [Course.Conquest] = new[] { "army", "feudalism", "nationalism", "conscription" },
        [Course.Commerce] = new[] { "coinage", "guilds", "parliament", "globalization" },
        [Course.Terror] = new[] { "raiding", "slavery", "cannibalism", "colonialism" },
        [Course.Tyranny] = new[] { "religion", "absolutism", "inquisition", "propaganda", "surveillance" },
        [Course.Learning] = new[] { "writing", "printing", "tolerance", "schooling" },
    };

    static bool Shuns(string key, Course? aim) => aim switch
    {
        Course.Terror => key is "parley" or "tribute",
        Course.Tyranny => key is "cannibalism" or "parliament" or "suffrage" or "tolerance",
        Course.Commerce => key is "cannibalism" or "inquisition",
        _ => key == "cannibalism",
    };

    /// <summary>길을 잡는다: 갈림길에서 고르고, 그 길의 제도를 들인다.</summary>
    static void Steer(Simulation sim, Course? aim)
    {
        if (sim.Season != 0) return;
        // 학문의 길에는 갈림길의 갈래가 없다: 장삿길이 생각도 실어 오니 교역을 고른다.
        var want = aim is null ? sim.Leaning() : aim == Course.Learning ? Course.Commerce : aim.Value;
        if (sim.Research == null || TechTree.Find(sim.Research) is { Crossroad: false } && aim != null)
        {
            var pick = TechTree.All.FirstOrDefault(t => t.Crossroad && t.Course == want && sim.CanResearch(t))
                       ?? (sim.AtCrossroad ? TechTree.All.First(t => t.Crossroad && sim.CanResearch(t)) : null);
            if (pick != null && sim.Research != pick.Key) sim.SetResearch(pick.Key);
        }
        if (aim == null) return;
        var r = sim.Realm;
        if (r.ReformHeat >= 0.5) return;
        foreach (var key in Wants[aim.Value])
        {
            if (Catalog.Find(key) is not { } d || d.Get(sim.Inst) || !sim.Available(d)) continue;
            if (r.Stage != Stage.Tribe && r.Treasury - sim.PreviewReform(d).Cost < -1) return;
            sim.Enact(d, true);
            return;
        }
    }

    static void TribeLife(Simulation sim)
    {
        int folk = sim.Living.Count();
        if (sim.Season <= 2 && sim.CommonStore < folk * 2 && sim.TribeAct("hunt")) return;
        for (int i = 0; i < sim.Neighbors.Count; i++)
            if (sim.Neighbors[i].Hostility > 1.5 && sim.CommonStore > folk * 2 && sim.TribeAct("gift", i)) return;
        if (sim.Season == 2 && sim.CommonStore > folk * 5) sim.TribeAct("feast");
    }

    static void Command(Simulation sim)
    {
        var r = sim.Realm;
        var map = sim.Map;
        var foes = sim.Armies.Where(a => a.Owner != 0).OrderBy(a => a.Id).ToList();
        int own = sim.Armies.Count(a => a.Owner == 0);

        // 일으킨다: 평시에는 하나, 침공을 받으면 둘까지.
        int want = Math.Min(sim.ArmyCap, foes.Count > 0 ? 2 : 1);
        if (own < want && (r.Treasury >= 1.5 || foes.Count > 0) && r.Pop > 600) sim.RaiseArmy(map.Capital);

        var mine = sim.Armies.Where(a => a.Owner == 0).OrderBy(a => a.Id).ToList();
        if (mine.Count == 0) return;

        // 막는다: 침공군이 노리는 우리 땅으로 가장 가까운 군단을 보낸다.
        var busy = new HashSet<int>();
        foreach (var f in foes)
        {
            if (f.Goal < 0 || map[f.Goal].Owner != 0) continue;
            var guard = mine.Where(a => !busy.Contains(a.Id))
                .OrderBy(a => a.At == f.Goal ? 0 : sim.Route(a.At, f.Goal).Count).ThenBy(a => a.Id).FirstOrDefault();
            if (guard == null) break;
            busy.Add(guard.Id);
            if (guard.At != f.Goal && guard.Goal != f.Goal) sim.OrderArmy(guard.Id, f.Goal);
        }
        if (foes.Count > 0) return;

        // 살림이 무너지면 군단부터 흩는다.
        if (r.Treasury < -2 && mine.Count > 0) { sim.DisbandArmy(mine[^1].Id); return; }

        // 친다: 여름에, 쉬고 있는 온전한 군단으로. 빈 땅이 먼저, 그다음이 이길 만한 이웃 땅.
        if (sim.Season != 1 || r.Treasury < 1 || r.Territory >= sim.AdminCap) return;
        foreach (var a in mine.Where(a => a.Goal < 0 && a.Men >= a.FullMen * 0.85 && a.Morale >= 0.9))
        {
            var edge = sim.Lands(0).SelectMany(p => p.Adjacent).Distinct().Select(id => map[id]).Where(p => p.Owner != 0).OrderBy(p => p.Id).ToList();
            var free = edge.Where(p => p.Owner == -1).OrderBy(p => sim.Route(a.At, p.Id).Count).ThenBy(p => p.Id).FirstOrDefault();
            if (free != null) { sim.OrderArmy(a.Id, free.Id); return; }
            var prey = edge.Where(p => p.Owner > 0 && sim.Rivals.FirstOrDefault(v => v.Id == p.Owner) is { Allied: false } v && sim.Year >= v.TreatyUntil)
                .Select(p => (p, odds: sim.AttackOdds(a.Id, p.Id))).Where(x => x.odds >= 0.62)
                .OrderByDescending(x => x.odds).ThenBy(x => x.p.Id).FirstOrDefault();
            if (prey.p != null) { sim.OrderArmy(a.Id, prey.p.Id); return; }
        }
    }
}
