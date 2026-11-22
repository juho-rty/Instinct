namespace Instinct.Sim;

/// <summary>사람 하나가 맡는 일. None은 아직 정해지지 않은 것(다음 철 첫머리에 우두머리나 관리인이 정한다).</summary>
public enum Work { None, Gather, Hunt, Fish, Farm, Wood, Stone, Build, Scout, Guard }

/// <summary>일 하나의 설명. Food면 식량을 내고, Mult·Season은 정원 안에서의 한 사람 몫(채집의 철 몫이 옛 기본 수확과 같다).</summary>
public sealed record WorkDef(Work Work, string Name, bool Food, double Mult, double[] Season, string Gain, string Cost);

/// <summary>관리인이 일을 나누는 방침.</summary>
public enum StewardPolicy { Balanced, Food, Build, War }

/// <summary>칸 위에 짓는 것. 원시 시대부터 지을 수 있다.</summary>
public enum StructureKind { Hut, Lodge, Weir, Field, Granary, Lumber, Quarry, Palisade, WarHut, Tower, Shrine, Storehouse }

/// <summary>칸 건물 하나의 설명. 나무·돌을 내고 일손(Work)을 들여 짓는다. Need는 필요한 제도의 키.</summary>
public sealed record StructureDef(StructureKind Kind, string Name, double Wood, double Stone, double Work, string Need, string Gain, string Cost, string Rule);

/// <summary>칸 위에 선(또는 짓는 중인) 건물.</summary>
public sealed class Structure
{
    public int Id;
    public StructureKind Kind;
    public int Cell;
    /// <summary>들인 일손. Work에 닿으면 다 지은 것이다.</summary>
    public double Done;
    public double Work;
    public bool Built => Done >= Work;
}

public static class WorkCatalog
{
    public static readonly IReadOnlyList<WorkDef> All = new[]
    {
        new WorkDef(Work.Gather, "채집", true, 1.0, new[] { 1.0, 1.3, 1.75, 0.25 }, "열매와 뿌리. 어디서나 할 수 있다", "겨울에는 거의 없다 · 열매 덤불이 적으면 정원이 작다"),
        new WorkDef(Work.Hunt, "사냥", true, 1.15, new[] { 1.0, 0.95, 1.45, 0.4 }, "가을 짐승이 살지고 겨울에도 조금은 잡힌다", "사냥감 칸이 정원을 정한다 · 다치는 이가 나온다"),
        new WorkDef(Work.Fish, "고기잡이", true, 1.1, new[] { 0.9, 1.35, 1.35, 0.4 }, "물가의 꾸준한 먹을 것", "물고기 칸이 있어야 한다"),
        new WorkDef(Work.Farm, "농사", true, 1.55, new[] { 0.25, 1.0, 2.6, 0.0 }, "가을걷이가 가장 크다", "농경 제도와 밭이 있어야 한다 · 겨울에는 놀린다"),
        new WorkDef(Work.Wood, "벌목", false, 0.55, new[] { 1.0, 1.0, 1.0, 0.7 }, "나무: 짓는 데 쓴다", "먹을 것은 나오지 않는다"),
        new WorkDef(Work.Stone, "채석", false, 0.4, new[] { 1.0, 1.0, 1.0, 0.6 }, "돌: 곡창·제단·망루에 쓴다", "돌 칸이 있어야 한다 · 먹을 것은 나오지 않는다"),
        new WorkDef(Work.Build, "건설", false, 1.0, new[] { 1.0, 1.0, 1.0, 0.6 }, "놓아 둔 건물을 차례로 짓는다", "먹을 것은 나오지 않는다"),
        new WorkDef(Work.Scout, "정찰", false, 1.0, new[] { 1.0, 1.0, 1.0, 1.0 }, "안개를 걷어 새 땅을 찾는다", "먹을 것은 나오지 않는다"),
        new WorkDef(Work.Guard, "경계", false, 1.0, new[] { 1.0, 1.0, 1.0, 1.0 }, "습격을 막고 도둑질을 줄인다", "먹을 것은 나오지 않는다 · 싸움이 나면 맨 앞에 선다"),
    };

    public static WorkDef Of(Work w) => All.First(d => d.Work == w);
    public static string Name(Work w) => w == Work.None ? "미정" : Of(w).Name;
    public static readonly string[] PolicyNames = { "고르게", "식량 먼저", "건설 먼저", "전쟁 대비" };
}

public static class StructureCatalog
{
    public static readonly IReadOnlyList<StructureDef> All = new[]
    {
        new StructureDef(StructureKind.Hut, "움집", 3, 0, 3, "", "땅이 먹여 살릴 수 있는 사람 +5", "나무 3", "풀밭·마른 땅·모래·언덕"),
        new StructureDef(StructureKind.Storehouse, "창고", 5, 0, 4, "", $"공동 창고에 식량을 {StoreEach}만큼 더 쌓아 둔다(넘치는 것은 철마다 {ExcessRot:P0}씩 썩는다)", "나무 5", "무리의 터에서 3칸 안, 풀밭·마른 땅·모래·언덕"),
        new StructureDef(StructureKind.Lodge, "사냥막", 4, 0, 4, "", "둘레 3칸 사냥감 한 칸마다 사냥 정원 +2", "나무 4", "둘레 3칸에 사냥감"),
        new StructureDef(StructureKind.Weir, "어살", 4, 0, 4, "", "둘레 2칸 물고기 한 칸마다 고기잡이 정원 +2", "나무 4", "물가(바다·강)에 붙어 있고 둘레 2칸에 물고기"),
        new StructureDef(StructureKind.Field, "밭", 1, 0, 3, "agriculture", "농사 정원 +2 (기름진 흙·강가면 +3)", "나무 1", "풀밭·마른 땅·늪, 농경 제도"),
        new StructureDef(StructureKind.Granary, "곡창", 6, 4, 6, "agriculture", $"공동 창고의 식량이 절반만 썩고 {GranaryEach}만큼 더 쌓아 둔다", "나무 6, 돌 4", "풀밭·마른 땅·언덕·모래, 농경 제도"),
        new StructureDef(StructureKind.Lumber, "벌목장", 2, 0, 3, "", "둘레 2칸 숲 한 칸마다 벌목 정원 +1 (최대 +6)", "나무 2", "둘레 2칸에 숲 세 칸 이상"),
        new StructureDef(StructureKind.Quarry, "채석장", 3, 0, 4, "", "둘레 2칸 돌 한 칸마다 채석 정원 +2", "나무 3", "바로 옆이나 그 칸에 돌"),
        new StructureDef(StructureKind.Palisade, "목책", 8, 0, 6, "", "습격을 막는 힘 ×1.35", "나무 8", "무리의 터에서 4칸 안"),
        new StructureDef(StructureKind.WarHut, "전사의 집", 6, 2, 5, "", "사내들의 싸움 힘 ×1.15", "나무 6, 돌 2 · 철마다 식량 1을 더 먹는다 · 다툼도 거칠어진다", "산꼭대기가 아닌 곳"),
        new StructureDef(StructureKind.Tower, "망루", 5, 2, 4, "", "둘레 8칸(언덕이면 10칸)의 안개를 걷고 습격에 미리 대비한다(×1.1)", "나무 5, 돌 2", "어디든"),
        new StructureDef(StructureKind.Shrine, "제단", 0, 6, 5, "", "원한이 더 빨리 풀린다", "돌 6 · 철마다 식량 1을 바친다", "산꼭대기가 아닌 곳"),
    };

    public static StructureDef Of(StructureKind k) => All.First(d => d.Kind == k);

    /// <summary>창고 한 채·곡창 한 채가 늘리는 저장 한도, 무리의 터(구덩이·바구니)가 기본으로 담는 양과 한 사람당 더 담는 양.</summary>
    public const double StoreEach = 60, GranaryEach = 80, StoreBase = 30, StorePerHead = 1.5;
    /// <summary>저장 한도를 넘친 식량이 한 철에 썩는 몫.</summary>
    public const double ExcessRot = 0.3;
}

// 일과 칸 건물과 안개: 사람마다 일을 맡기고, 칸 위에 건물을 짓고, 정찰로 땅을 밝힌다.
public sealed partial class Simulation
{
    /// <summary>쌓아 둔 나무와 돌. 칸 건물을 짓는 데 쓴다.</summary>
    public double Wood = 6, Stone;
    public readonly List<Structure> Structures = new();
    int _nextStructure = 1;
    /// <summary>관리인: 사람이 많아지면 일 나누기를 맡긴다. 없으면 플레이어가 직접 나눈다.</summary>
    public Person? Steward;
    public StewardPolicy Policy;
    /// <summary>관리인을 둘 수 있는 어른(14살 이상) 수. 군장사회부터는 언제나 둘 수 있다.</summary>
    public const int StewardThreshold = 20;

    static readonly int[] Dx = { 1, -1, 0, 0 }, Dy = { 0, 0, 1, -1 };
    const int W = WorldMap.Width, H = WorldMap.Height;

    public int CampCell => CapitalLand.Y * W + CapitalLand.X;

    /// <summary>일하는 사람: 열 살 넘은 산 사람(종 포함), Id 순.</summary>
    public List<Person> Workers() => Living.Where(p => p.Age(Tick) >= 10).OrderBy(p => p.Id).ToList();

    public int CountJob(Work w) => Living.Count(p => p.Age(Tick) >= 10 && p.Job == w);

    public bool HasBuilt(StructureKind k) => Structures.Any(s => s.Kind == k && s.Built);
    public int BuiltCount(StructureKind k) => Structures.Count(s => s.Kind == k && s.Built);
    public Structure? StructureAt(int cell) => Structures.FirstOrDefault(s => s.Cell == cell);

    bool Ours(int cell) => Map.Cell[cell] >= 0 && Map[Map.Cell[cell]].Owner == 0;

    int CountNear(int cell, int r, Func<int, bool> pred) => Map.Disc(cell % W, cell / W, r).Count(pred);

    bool ByWater(int cell)
    {
        int x = cell % W, y = cell / W;
        if (Map.RiverCell[cell]) return true;
        for (int d = 0; d < 4; d++)
        {
            int nx = x + Dx[d], ny = y + Dy[d];
            if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
            int j = ny * W + nx;
            if (Map.Tile[j] == Ground.Shallows || Map.RiverCell[j]) return true;
        }
        return false;
    }

    // ───────── 정원 ─────────

    (int tick, int sig, double[] cap) _capCache = (-1, 0, Array.Empty<double>());

    /// <summary>일마다의 정원: 이만큼까지는 한 사람이 제 몫을 다 낸다. 넘치면 넘친 사람은 40%만 낸다.</summary>
    public double Capacity(Work w)
    {
        int sig = Structures.Count * 31 + Structures.Count(s => s.Built) + Lands(0).Count() * 1009 + (Steward != null ? 7 : 0);
        if (_capCache.tick != Tick || _capCache.sig != sig) _capCache = (Tick, sig, ComputeCapacity());
        return _capCache.cap[(int)w];
    }

    double[] ComputeCapacity()
    {
        var cap = new double[Enum.GetValues<Work>().Length];
        double berries = 0, open = 0, game = 0, fish = 0, woods = 0, stone = 0;
        for (int i = 0; i < W * H; i++)
        {
            if (Map.Cell[i] < 0)
            {
                // 우리 땅에 붙은 얕은 물의 물고기
                if (Map.Feat[i] == Feature.Fish)
                {
                    int x = i % W, y = i / W;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + Dx[d], ny = y + Dy[d];
                        if (nx >= 0 && ny >= 0 && nx < W && ny < H && Ours(ny * W + nx)) { fish++; break; }
                    }
                }
                continue;
            }
            if (!Ours(i)) continue;
            switch (Map.Feat[i])
            {
                case Feature.Berries: berries++; break;
                case Feature.Game: game++; break;
                case Feature.Fish: fish++; break;
                case Feature.Stone: stone++; break;
            }
            if (Map.Tile[i] is Ground.Grass or Ground.Woods or Ground.Marsh or Ground.Dry) open++;
            if (Map.Tile[i] == Ground.Woods) woods++;
        }
        cap[(int)Work.Gather] = 8 + 2.5 * berries + 0.15 * open;
        cap[(int)Work.Hunt] = 1.5 * game;
        cap[(int)Work.Fish] = 1.5 * fish;
        cap[(int)Work.Wood] = 1 + 0.2 * woods;
        cap[(int)Work.Stone] = stone;
        foreach (var s in Structures.Where(s => s.Built).OrderBy(s => s.Id))
        {
            int x = s.Cell % W, y = s.Cell / W;
            switch (s.Kind)
            {
                case StructureKind.Lodge: cap[(int)Work.Hunt] += 2 * CountNear(s.Cell, 3, j => Map.Feat[j] == Feature.Game); break;
                case StructureKind.Weir: cap[(int)Work.Fish] += 2 * CountNear(s.Cell, 2, j => Map.Feat[j] == Feature.Fish); break;
                case StructureKind.Field: cap[(int)Work.Farm] += Map.Feat[s.Cell] == Feature.Fertile || ByWater(s.Cell) ? 3 : 2; break;
                case StructureKind.Lumber: cap[(int)Work.Wood] += Math.Min(6, CountNear(s.Cell, 2, j => Map.Tile[j] == Ground.Woods)); break;
                case StructureKind.Quarry: cap[(int)Work.Stone] += 2 * Math.Max(1, CountNear(s.Cell, 2, j => Map.Feat[j] == Feature.Stone)); break;
            }
        }
        // 관리인은 일손을 빈틈없이 짠다: 먹을 것을 내는 일의 정원이 는다.
        if (Steward is { Alive: true } st)
            foreach (var w in new[] { Work.Gather, Work.Hunt, Work.Fish, Work.Farm })
                cap[(int)w] *= 1.05 + 0.1 * st.T(Trait.Ambition);
        foreach (var w in new[] { Work.Build, Work.Scout, Work.Guard }) cap[(int)w] = 1e9;
        return cap;
    }

    /// <summary>n명이 그 일을 할 때 한 사람의 몫(정원을 넘으면 준다).</summary>
    public double Efficiency(Work w, double n)
    {
        double c = Capacity(w);
        return n <= c || n <= 0 ? 1 : (c + 0.7 * (n - c)) / n;
    }

    /// <summary>그 일을 맡을 수 없는 까닭. 맡을 수 있으면 빈 문자열.</summary>
    public string WhyCantWork(Work w)
    {
        if (w == Work.None) return "일을 골라야 함";
        if (w == Work.Farm && !Inst.Agriculture) return "농경 제도가 있어야 함";
        return "";
    }

    // ───────── 한 철의 일 ─────────

    /// <summary>
    /// 한 철 첫머리: 안개를 걷고, 일이 없는 사람에게 일을 주고(관리인이 있으면 모두 다시 나눈다),
    /// 정찰꾼이 새 땅을 밝히고, 일꾼들이 건물을 짓는다. 먹을 것은 Produce에서 낸다.
    /// </summary>
    internal void Labor()
    {
        if (Steward is { } s0 && (!s0.Alive || s0 == Chief || s0.Master != null))
        {
            Minor(s0.Alive ? $"{Josa.Eun(s0.Name)} 더는 관리인 일을 보지 않는다." : $"관리인 {Josa.Iga(s0.Name)} 세상을 떠나 일 나누기가 우두머리에게 돌아왔다.");
            Steward = null;
        }
        UpdateSight();
        var ws = Workers();
        foreach (var p in ws.Where(p => p.Job != Work.None && WhyCantWork(p.Job).Length > 0)) p.Job = Work.None;
        if (Steward != null) Rebalance(ws);
        else
        {
            foreach (var p in ws.Where(p => p.Job == Work.None))
                p.Job = BestFoodJob(ws);
            AutoBuilders(ws);
        }

        if (Steward is { } st && CommonStore > 1)
        {
            // 관리인의 값: 창고에서 제 몫을 떼고, 위신이 쌓여 우두머리 자리를 넘본다.
            double skim = CommonStore * (0.01 + 0.03 * st.T(Trait.Greed));
            CommonStore -= skim;
            Head(st).Food += skim;
            st.Status += 0.03;
        }
        foreach (var k in new[] { StructureKind.WarHut, StructureKind.Shrine })
            if (HasBuilt(k)) CommonStore = Math.Max(0, CommonStore - 1);

        Scouting(ws.Where(p => p.Job == Work.Scout).ToList());
        BuildStructures(ws.Where(p => p.Job == Work.Build).ToList());
    }

    /// <summary>
    /// 관리인이 없을 때의 건설 일손: 새로 놓은 터(아직 손대지 않은 것)에 짓는 사람이 없고 한 철 먹을 것이 있으면
    /// 가장 알맞은 일손 하나가 붙는다. 지을 것이 다 끝나면 짓던 사람은 먹을 것 일로 돌아간다.
    /// </summary>
    void AutoBuilders(List<Person> ws)
    {
        var builders = ws.Where(p => p.Job == Work.Build).OrderBy(p => p.Id).ToList();
        if (!Structures.Any(s => !s.Built))
        {
            foreach (var p in builders) p.Job = BestFoodJob(ws);
            if (builders.Count > 0) Minor($"지을 것이 다 끝나 짓던 {builders.Count}명이 먹을 것을 구하러 돌아갔다.");
            return;
        }
        if (builders.Count > 0 || ws.Count < 4 || CommonStore < Living.Count() || !Structures.Any(s => !s.Built && s.Done <= 0)) return;
        var pick = ws.Where(p => WorkCatalog.Of(p.Job == Work.None ? Work.Gather : p.Job).Food)
            .OrderByDescending(p => Suit(p, Work.Build)).ThenBy(p => p.Id).FirstOrDefault();
        if (pick == null) return;
        pick.Job = Work.Build;
        Minor($"놓아 둔 터에 짓는 사람이 없어 {Josa.Iga(pick.Name)} 일손을 보탰다.");
    }

    double SeasonAvg(Work w) => WorkCatalog.Of(w).Season.Average();

    /// <summary>지금 사람들을 그대로 두고 한 명을 더 넣을 때 가장 많이 내는 먹을 것 일.</summary>
    Work BestFoodJob(List<Person> ws)
    {
        Work best = Work.Gather;
        double bestGain = double.MinValue;
        foreach (var d in WorkCatalog.All.Where(d => d.Food && WhyCantWork(d.Work).Length == 0))
        {
            int n = ws.Count(p => p.Job == d.Work);
            double gain = d.Mult * SeasonAvg(d.Work) * ((n + 1) * Efficiency(d.Work, n + 1) - n * Efficiency(d.Work, n));
            if (d.Work == Work.Farm && Capacity(Work.Farm) <= 0) gain *= 0.5;
            if (gain > bestGain + 1e-9) { bestGain = gain; best = d.Work; }
        }
        return best;
    }

    /// <summary>그 사람이 그 일에 맞는 정도(배치할 사람을 고를 때).</summary>
    double Suit(Person p, Work w) => w switch
    {
        Work.Hunt or Work.Guard => (p.IsMale ? 1 : 0.4) + p.T(Trait.Aggression),
        Work.Scout => (p.IsMale ? 0.8 : 0.5) + p.T(Trait.Ambition),
        Work.Build or Work.Wood or Work.Stone => (p.IsMale ? 0.9 : 0.6) + 0.3 * p.Skill,
        _ => 1 - 0.3 * p.T(Trait.Aggression),
    } * Math.Max(0.1, p.Health) * (p.Age(Tick) is >= 14 and < 60 ? 1 : 0.5);

    /// <summary>관리인이 방침대로 모두의 일을 다시 나눈다. 지금 맡은 일을 되도록 그대로 둔다.</summary>
    void Rebalance(List<Person> ws)
    {
        int n = ws.Count;
        if (n == 0) return;
        var target = new Dictionary<Work, int>();
        int pending = Structures.Count(s => !s.Built);
        bool hostile = Tribal && Neighbors.Any(x => !x.Allied && x.Hostility > 0.5);
        target[Work.Build] = pending == 0 ? 0 : Policy switch
        {
            StewardPolicy.Build => Math.Max(2, n * 30 / 100), StewardPolicy.Food => 1, _ => Math.Max(1, n * 15 / 100),
        };
        target[Work.Scout] = Policy is StewardPolicy.Balanced or StewardPolicy.War && HasFogEdge() ? 1 : 0;
        target[Work.Guard] = Policy == StewardPolicy.War ? Math.Max(1, n * 20 / 100) : Policy == StewardPolicy.Balanced && hostile ? Math.Max(1, n * 5 / 100) : 0;
        target[Work.Wood] = Wood < 12 ? Policy == StewardPolicy.Build ? 3 : Policy == StewardPolicy.Food ? 0 : 1 : 0;
        target[Work.Stone] = Stone < 8 && Capacity(Work.Stone) > 0 && (Policy == StewardPolicy.Build || pending > 0) ? 1 : 0;
        // 먹을 것을 내는 사람이 적어도 열에 여섯은 되게 한다.
        int other = target.Values.Sum(), limit = n * 4 / 10;
        foreach (var w in new[] { Work.Guard, Work.Stone, Work.Wood, Work.Scout, Work.Build })
            while (other > limit && target[w] > 0) { target[w]--; other--; }
        var food = new Dictionary<Work, int>();
        foreach (var d in WorkCatalog.All.Where(d => d.Food)) food[d.Work] = 0;
        for (int k = other; k < n; k++)
        {
            Work best = Work.Gather;
            double bestGain = double.MinValue;
            foreach (var w in food.Keys.Where(w => WhyCantWork(w).Length == 0))
            {
                int c = food[w];
                var d = WorkCatalog.Of(w);
                double gain = d.Mult * SeasonAvg(w) * ((c + 1) * Efficiency(w, c + 1) - c * Efficiency(w, c));
                if (gain > bestGain + 1e-9) { bestGain = gain; best = w; }
            }
            food[best]++;
        }
        foreach (var (w, c) in food) target[w] = c;

        // 넘치는 일에서 덜 맞는 사람부터 빼고, 모자란 일에 더 맞는 사람부터 넣는다.
        var loose = new List<Person>();
        foreach (var w in Enum.GetValues<Work>())
        {
            var on = ws.Where(p => p.Job == w).OrderBy(p => Suit(p, w)).ThenByDescending(p => p.Id).ToList();
            int keep = w == Work.None ? 0 : target.GetValueOrDefault(w);
            loose.AddRange(on.Take(Math.Max(0, on.Count - keep)));
        }
        foreach (var w in target.Keys.OrderBy(w => w))
        {
            int need = target[w] - ws.Count(p => p.Job == w && !loose.Contains(p));
            foreach (var p in loose.OrderByDescending(p => Suit(p, w)).ThenBy(p => p.Id).Take(Math.Max(0, need)).ToList())
            {
                p.Job = w;
                loose.Remove(p);
            }
        }
        foreach (var p in loose) p.Job = Work.Gather;
    }

    /// <summary>이 철에 그 사람이 낸 식량에 곱할 값(일의 몫 × 철 × 정원). 먹을 것 일이 아니면 0.</summary>
    double FoodFactor(Work w, Dictionary<Work, int> counts)
    {
        if (w == Work.None) w = Work.Gather;
        var d = WorkCatalog.Of(w);
        if (!d.Food) return 0;
        if (w == Work.Farm && Capacity(Work.Farm) <= 0) return d.Mult * d.Season[Season] * 0.3; // 밭 없이 흩뿌린 씨
        return d.Mult * d.Season[Season] * Efficiency(w, counts.GetValueOrDefault(w));
    }

    /// <summary>농경은 같은 땅에 더 많은 입을 붙이고, 나라가 커지면 백성이 지배 가문을 먹인다.</summary>
    double Crowd(int living) => 1.0 / (1 + living / (Cfg.Capacity * (Inst.Agriculture ? 1.25 : 1) + HutCapacity())) * (1 + 0.05 * (int)Realm.Stage);

    Dictionary<Work, int> JobCounts(List<Person> living) =>
        living.Where(p => p.Age(Tick) >= 10).GroupBy(p => p.Job == Work.None ? Work.Gather : p.Job).ToDictionary(g => g.Key, g => g.Count());

    static double AgeF(double age) => age < 14 || age >= 60 ? 0.4 : 1.0;

    /// <summary>운을 뺀 한 사람의 이번 철 식량(운은 0.85~1.15를 곱한다).</summary>
    double ExpectedFood(Person p, double ageF, Dictionary<Work, int> counts, double crowd)
    {
        // 공동 분배에서는 탐욕스러운 이가 게으름을 피우고, 사유재산에서는 야심이 일을 시킨다.
        double effort = Inst.PrivateProperty ? 1 + 0.15 * p.T(Trait.Ambition) : 1 - 0.3 * p.T(Trait.Greed);
        return Cfg.BaseYield * YieldMul * p.Skill * FoodFactor(p.Job, counts) * _yearFactor * crowd * ageF * effort
               * (0.5 + 0.5 * Math.Max(0, p.Health));
    }

    /// <summary>
    /// 이 일이 이번 철에 낼 것의 어림(운을 뺀 기댓값): 먹을 것 일이면 식량, 나무·돌이면 그것. 그 밖의 일은 0.
    /// 화면이 "+N/철"로 보인다. 난수를 쓰지 않으므로 몇 번 불러도 판이 바뀌지 않는다.
    /// </summary>
    public double Forecast(Work w)
    {
        if (w == Work.None) w = Work.Gather;
        var living = Living.ToList();
        var counts = JobCounts(living);
        double crowd = Crowd(living.Count), sum = 0;
        var d = WorkCatalog.Of(w);
        foreach (var p in living)
        {
            double age = p.Age(Tick);
            if (age < 10 || (p.Job == Work.None ? Work.Gather : p.Job) != w) continue;
            double ageF = AgeF(age);
            if (d.Food) sum += ExpectedFood(p, ageF, counts, crowd) * (p.Master != null ? 0.8 : 1);
            else if (w is Work.Wood or Work.Stone)
                sum += d.Mult * d.Season[Season] * p.Skill * ageF * (0.5 + 0.5 * Math.Max(0, p.Health)) * Efficiency(w, counts.GetValueOrDefault(w));
        }
        return sum;
    }

    /// <summary>먹을 것 일을 모두 더한 이번 철 식량 어림.</summary>
    public double ForecastFood() => WorkCatalog.All.Where(d => d.Food).Sum(d => Forecast(d.Work));

    /// <summary>먹을 것이 아닌 일의 몫을 쌓는다(나무·돌). 건설과 정찰은 Labor에서 따로 셈한다.</summary>
    void Gather(Person p, Work w, double each, Dictionary<Work, int> counts)
    {
        var d = WorkCatalog.Of(w);
        double v = d.Mult * d.Season[Season] * each * Efficiency(w, counts.GetValueOrDefault(w));
        if (w == Work.Wood) Wood += v;
        else if (w == Work.Stone) Stone += v;
    }

    // ───────── 안개와 정찰 ─────────

    /// <summary>우리 땅, 망루, 우리 군단 둘레를 밝힌다.</summary>
    internal void UpdateSight()
    {
        for (int i = 0; i < W * H; i++)
            if (!Map.Seen[i] && Map.Cell[i] >= 0 && Map[Map.Cell[i]].Owner == 0)
                Map.Reveal(i % W, i / W, 1);
        foreach (var s in Structures.Where(s => s.Kind == StructureKind.Tower && s.Built).OrderBy(s => s.Id))
            Map.Reveal(s.Cell % W, s.Cell / W, Map.Tile[s.Cell] == Ground.Hill ? 10 : 8);
        foreach (var a in Armies.Where(a => a.Owner == 0).OrderBy(a => a.Id))
            Map.Reveal(Map[a.At].X, Map[a.At].Y, 4);
    }

    /// <summary>안개의 가장자리 칸이 하나라도 남아 있는가(정렬 없이 바로 확인).</summary>
    bool HasFogEdge()
    {
        for (int i = 0; i < W * H; i++)
        {
            if (!Map.Seen[i] || Map.Tile[i] == Ground.Deep) continue;
            int x = i % W, y = i / W;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if (nx >= 0 && ny >= 0 && nx < W && ny < H && !Map.Seen[ny * W + nx]) return true;
            }
        }
        return false;
    }

    /// <summary>안개의 가장자리: 본 칸 가운데 안 본 칸에 붙은 것, 도성에서 가까운 순.</summary>
    public List<int> FogEdge()
    {
        var cap = CapitalLand;
        var list = new List<int>();
        for (int i = 0; i < W * H; i++)
        {
            if (!Map.Seen[i] || Map.Tile[i] == Ground.Deep) continue;
            int x = i % W, y = i / W;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if (nx < 0 || ny < 0 || nx >= W || ny >= H || Map.Seen[ny * W + nx]) continue;
                list.Add(i);
                break;
            }
        }
        return list.OrderBy(i => (i % W - cap.X) * (i % W - cap.X) + (i / W - cap.Y) * (i / W - cap.Y))
            .ThenBy(i => (i * 2654435761u) & 0xFFFF).ThenBy(i => i).ToList();
    }

    void Scouting(List<Person> scouts)
    {
        if (scouts.Count == 0) return;
        var before = Map.Provinces.Where(Map.Known).Select(p => p.Id).ToHashSet();
        foreach (var s in scouts)
        {
            var edge = FogEdge();
            if (edge.Count == 0) break;
            // 저마다 다른 쪽으로 간다: 가까운 가장자리 가운데 사람마다 다른 자리.
            int pick = edge[(int)((s.Id * 7919L + Tick * 31L) % Math.Min(edge.Count, 24))];
            Map.Reveal(pick % W, pick / W, 3 + (s.Age(Tick) is >= 16 and <= 40 ? 1 : 0));
        }
        var found = Map.Provinces.Where(p => !before.Contains(p.Id) && Map.Known(p)).OrderBy(p => p.Id).Select(p => p.Name).ToList();
        if (found.Count > 0) Minor($"【정찰】 정찰꾼이 새 땅을 보았다: {string.Join(", ", found)}.");
    }

    // ───────── 칸 건물 ─────────

    void BuildStructures(List<Person> builders)
    {
        double pts = builders.Sum(p => p.Skill * Math.Max(0.2, p.Health) * (p.Age(Tick) is >= 14 and < 60 ? 1 : 0.5)) * (Season == 3 ? 0.6 : 1);
        foreach (var s in Structures.Where(s => !s.Built).OrderBy(s => s.Id))
        {
            if (pts <= 0) break;
            double use = Math.Min(pts, s.Work - s.Done);
            s.Done += use;
            pts -= use;
            if (!s.Built) continue;
            var def = StructureCatalog.Of(s.Kind);
            Say($"【건설】 {Josa.Iga(def.Name)} 다 지어졌다. {def.Gain}.");
            if (s.Kind == StructureKind.Tower) UpdateSight();
        }
    }

    /// <summary>그 칸에 그 건물을 놓을 수 없는 까닭. 놓을 수 있으면 빈 문자열.</summary>
    public string WhyCantPlace(StructureKind k, int cell)
    {
        var def = StructureCatalog.Of(k);
        if (cell < 0 || cell >= W * H || Map.Cell[cell] < 0) return "물 위에는 지을 수 없음";
        if (!Map.Seen[cell]) return "아직 보지 못한 땅";
        if (!Ours(cell)) return "우리 땅이 아님";
        if (cell == CampCell) return "무리의 터(마을 한가운데)";
        if (StructureAt(cell) != null) return "이미 건물이 있음";
        if (def.Need.Length > 0 && Catalog.Find(def.Need) is { } inst && !inst.Get(Inst)) return $"{inst.Name} 제도가 있어야 함";
        var t = Map.Tile[cell];
        var f = Map.Feat[cell];
        if (f is Feature.Game or Feature.Berries or Feature.Fish && k != StructureKind.Weir) return "먹을 것이 나는 칸은 비워 둔다";
        if (f == Feature.Stone && k != StructureKind.Quarry && k != StructureKind.Tower) return "돌 칸에는 채석장이나 망루만";
        if (t == Ground.Peak && k is not (StructureKind.Quarry or StructureKind.Tower)) return "산꼭대기에는 채석장·망루만";
        bool ok = k switch
        {
            StructureKind.Hut => t is Ground.Grass or Ground.Dry or Ground.Sand or Ground.Hill,
            StructureKind.Lodge => CountNear(cell, 3, j => Map.Feat[j] == Feature.Game) > 0,
            StructureKind.Weir => ByWater(cell) && CountNear(cell, 2, j => Map.Feat[j] == Feature.Fish) > 0,
            StructureKind.Field => t is Ground.Grass or Ground.Dry or Ground.Marsh,
            StructureKind.Granary => t is Ground.Grass or Ground.Dry or Ground.Hill or Ground.Sand,
            StructureKind.Lumber => CountNear(cell, 2, j => Map.Tile[j] == Ground.Woods) >= 3,
            StructureKind.Quarry => f == Feature.Stone || CountNear(cell, 1, j => Map.Feat[j] == Feature.Stone) > 0,
            StructureKind.Palisade => CountNear(cell, 4, j => j == CampCell) > 0,
            StructureKind.Storehouse => CountNear(cell, 3, j => j == CampCell) > 0 && t is Ground.Grass or Ground.Dry or Ground.Sand or Ground.Hill,
            _ => true,
        };
        if (!ok) return $"자리 조건: {def.Rule}";
        if (Wood < def.Wood - 1e-9) return $"나무 부족 ({def.Wood:0} 필요, {Wood:0.#} 있음)";
        if (Stone < def.Stone - 1e-9) return $"돌 부족 ({def.Stone:0} 필요, {Stone:0.#} 있음)";
        return "";
    }

    /// <summary>그 칸에 지으면 무엇이 나아지는지(자리에 따라 다르다).</summary>
    public string PlacePreview(StructureKind k, int cell)
    {
        if (cell < 0 || cell >= W * H) return "";
        return k switch
        {
            StructureKind.Lodge => $"사냥감 {CountNear(cell, 3, j => Map.Feat[j] == Feature.Game)}칸 → 사냥 정원 +{2 * CountNear(cell, 3, j => Map.Feat[j] == Feature.Game)}",
            StructureKind.Weir => $"물고기 {CountNear(cell, 2, j => Map.Feat[j] == Feature.Fish)}칸 → 고기잡이 정원 +{2 * CountNear(cell, 2, j => Map.Feat[j] == Feature.Fish)}",
            StructureKind.Field => Map.Feat[cell] == Feature.Fertile || ByWater(cell) ? "기름진 자리 → 농사 정원 +3" : "농사 정원 +2",
            StructureKind.Lumber => $"숲 {CountNear(cell, 2, j => Map.Tile[j] == Ground.Woods)}칸 → 벌목 정원 +{Math.Min(6, CountNear(cell, 2, j => Map.Tile[j] == Ground.Woods))}",
            StructureKind.Quarry => $"돌 {CountNear(cell, 2, j => Map.Feat[j] == Feature.Stone)}칸 → 채석 정원 +{2 * Math.Max(1, CountNear(cell, 2, j => Map.Feat[j] == Feature.Stone))}",
            StructureKind.Tower => Map.Tile[cell] == Ground.Hill ? "언덕 위: 10칸까지 보인다" : "8칸까지 보인다",
            _ => StructureCatalog.Of(k).Gain,
        };
    }

    /// <summary>칸 위에 건물을 놓는다. 나무·돌은 곧바로 내고, 건설 일꾼이 차례로 짓는다.</summary>
    public bool Place(StructureKind k, int cell)
    {
        if (WhyCantPlace(k, cell).Length > 0) return false;
        var def = StructureCatalog.Of(k);
        Wood -= def.Wood;
        Stone -= def.Stone;
        Structures.Add(new Structure { Id = _nextStructure++, Kind = k, Cell = cell, Work = def.Work });
        Wrote("place", ((int)k).ToString(), cell.ToString());
        return true;
    }

    /// <summary>칸 위의 건물을 헌다(짓던 것이면 들인 나무·돌의 절반을 돌려받는다).</summary>
    public bool Raze(int cell)
    {
        if (StructureAt(cell) is not { } s) return false;
        if (!s.Built)
        {
            var def = StructureCatalog.Of(s.Kind);
            Wood += def.Wood / 2;
            Stone += def.Stone / 2;
        }
        Structures.Remove(s);
        Wrote("raze", cell.ToString());
        return true;
    }

    // ───────── 일 맡기기 ─────────

    /// <summary>한 사람에게 일을 맡긴다. 관리인이 있으면 관리인이 나누므로 받아들이지 않는다.</summary>
    public bool SetJob(int personId, Work w)
    {
        if (Steward != null || WhyCantWork(w).Length > 0) return false;
        var p = Living.FirstOrDefault(x => x.Id == personId);
        if (p == null || p.Age(Tick) < 10 || p.Job == w) return false;
        p.Job = w;
        Wrote("job", personId.ToString(), ((int)w).ToString());
        return true;
    }

    /// <summary>그 일의 사람 수를 하나 늘리거나(채집하던 이를 데려온다) 줄인다(채집으로 돌린다).</summary>
    public bool Assign(Work w, int delta)
    {
        if (Steward != null || w == Work.None || delta == 0 || (delta > 0 && WhyCantWork(w).Length > 0)) return false;
        var ws = Workers();
        Person? pick;
        if (delta > 0)
        {
            var from = ws.Where(p => p.Job is Work.Gather or Work.None).ToList();
            if (w == Work.Gather || from.Count == 0)
                from = ws.Where(p => p.Job != w).GroupBy(p => p.Job).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault()?.ToList() ?? new();
            pick = from.OrderByDescending(p => Suit(p, w)).ThenBy(p => p.Id).FirstOrDefault();
            if (pick == null) return false;
            pick.Job = w;
        }
        else
        {
            if (w == Work.Gather) return false;
            pick = ws.Where(p => p.Job == w).OrderBy(p => Suit(p, w)).ThenByDescending(p => p.Id).FirstOrDefault();
            if (pick == null) return false;
            pick.Job = Work.Gather;
        }
        Wrote("assign", ((int)w).ToString(), delta.ToString());
        return true;
    }

    public string WhyCantSteward(int personId)
    {
        if (personId < 0) return Steward == null ? "관리인이 없음" : "";
        int adults = Living.Count(p => p.Age(Tick) >= 14);
        if (Realm.Stage < Stage.Chiefdom && adults < StewardThreshold) return $"어른이 {StewardThreshold}명은 되어야 함 (지금 {adults}명)";
        var p = Living.FirstOrDefault(x => x.Id == personId);
        if (p == null) return "없는 사람";
        if (p.Age(Tick) < 16) return "너무 어림";
        if (p.Master != null) return "종은 맡을 수 없음";
        if (p == Chief) return "우두머리는 맡을 수 없음";
        return "";
    }

    /// <summary>관리인을 세우거나(personId) 내보낸다(-1). 방침도 함께 정한다.</summary>
    public bool AppointSteward(int personId, StewardPolicy policy)
    {
        if (personId >= 0 && Steward?.Id == personId)
        {
            if (Policy == policy) return false;
            Policy = policy;
            Wrote("steward", personId.ToString(), ((int)policy).ToString());
            return true;
        }
        if (WhyCantSteward(personId).Length > 0) return false;
        Steward = personId < 0 ? null : Living.First(x => x.Id == personId);
        Policy = policy;
        if (Steward != null) Say($"{Josa.Iga(P(Steward))} 관리인이 되어 사람들의 일을 나누기 시작했다 ({WorkCatalog.PolicyNames[(int)policy]}).");
        Wrote("steward", personId.ToString(), ((int)policy).ToString());
        return true;
    }

    /// <summary>관리인으로 세울 만한 사람: 야심과 솜씨가 있는 어른, 우두머리가 아닌 이.</summary>
    public Person? StewardCandidate() => Living.Where(p => WhyCantSteward(p.Id).Length == 0)
        .OrderByDescending(p => p.T(Trait.Ambition) + 0.5 * p.Skill - 0.5 * p.T(Trait.Greed)).ThenBy(p => p.Id).FirstOrDefault();

    // ───────── 다른 일에 닿는 값 ─────────

    /// <summary>경계를 서는 사람들.</summary>
    internal List<Person> Guards() => Living.Where(p => p.Job == Work.Guard && p.Age(Tick) >= 14).OrderBy(p => p.Id).ToList();

    /// <summary>습격을 막는 힘에 곱하는 값(목책·망루).</summary>
    internal double DefenseMul() => (HasBuilt(StructureKind.Palisade) ? 1.35 : 1) * (HasBuilt(StructureKind.Tower) ? 1.1 : 1);

    /// <summary>공동 창고의 저장 한도: 무리의 터가 담는 양 + 사람마다 조금 + 창고·곡창. 넘치는 식량은 빨리 썩는다.</summary>
    public double StoreCap() => StructureCatalog.StoreBase + StructureCatalog.StorePerHead * Living.Count()
                                + StructureCatalog.StoreEach * BuiltCount(StructureKind.Storehouse) + StructureCatalog.GranaryEach * BuiltCount(StructureKind.Granary);

    /// <summary>한 철에 공동 창고에서 먹는 양(종은 주인이 먹이므로 빼고, 아이는 절반).</summary>
    public double SeasonNeed() => Living.Where(p => p.Master == null).Sum(p => Need(p, Tick));

    /// <summary>움집이 늘린 땅의 수용력.</summary>
    internal double HutCapacity() => 5 * BuiltCount(StructureKind.Hut);
}
