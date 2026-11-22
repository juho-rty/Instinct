namespace Instinct.Sim;

/// <summary>땅의 생김새. 먹여 살릴 수 있는 사람 수와 묻힌 자원을 정한다.</summary>
public enum Terrain { Sea, Plains, Forest, Hills, Mountain, Steppe }

/// <summary>칸 하나의 땅결. 지방의 생김새(Terrain) 안에서도 칸마다 숲·언덕·모래톱이 섞인다. 화면이 그대로 그린다.</summary>
public enum Ground { Deep, Shallows, Sand, Grass, Woods, Hill, Peak, Dry, Marsh }

/// <summary>칸 하나에 있는 것: 사냥감, 열매, 물고기, 돌, 기름진 흙. 일의 정원과 건물의 자리를 정한다(Labor.cs).</summary>
public enum Feature { None, Game, Berries, Fish, Stone, Fertile }

/// <summary>지방 하나. 영토는 이 조각들의 수다.</summary>
public sealed class Province
{
    public int Id;
    public string Name = "";
    public Terrain Terrain;
    /// <summary>이 땅에서 나는 자원. 알아볼 기술이 생기기 전에는 있어도 모른다.</summary>
    public Resource Deposit;
    /// <summary>주인. -1 주인 없는 땅, 0 우리, 그 밖은 이웃 나라의 Id.</summary>
    public int Owner = -1;
    /// <summary>지도에서의 자리(가운데 칸).</summary>
    public int X, Y;
    public int Size;
    public bool Coastal;
    /// <summary>강이 지나는 땅. 조금 더 기름지다.</summary>
    public bool River;
    public readonly List<int> Adjacent = new();
    /// <summary>이 땅에 선 건물들(지은 순서).</summary>
    public readonly List<Building> Buildings = new();
    /// <summary>길이 닿았는가. 도성은 처음부터 길의 뿌리다.</summary>
    public bool Road;
    /// <summary>길이 이어진 이웃 지방(도성 쪽). 없으면 -1.</summary>
    public int RoadTo = -1;

    public bool Has(Building b) => Buildings.Contains(b);

    public double Fertility => (Terrain switch
    {
        Terrain.Plains => 1.1, Terrain.Forest => 0.95, Terrain.Hills => 0.9, Terrain.Steppe => 0.85, _ => 0.7,
    }) + (Coastal ? 0.05 : 0) + (River ? 0.04 : 0);
}

/// <summary>
/// 세계 지도. 씨앗에서 해시로만 만들어지므로 시뮬레이션의 난수(_rng)를 쓰지 않는다:
/// 같은 씨앗이면 같은 땅이고, 지도를 넣어도 사람들의 역사는 달라지지 않는다.
/// </summary>
public sealed class WorldMap
{
    public const int Width = 96, Height = 60;
    /// <summary>칸마다의 지방 Id. 바다는 -1.</summary>
    public readonly int[] Cell = new int[Width * Height];
    /// <summary>칸마다의 땅결.</summary>
    public readonly Ground[] Tile = new Ground[Width * Height];
    /// <summary>칸마다 있는 것(사냥감·열매·물고기·돌·기름진 흙).</summary>
    public readonly Feature[] Feat = new Feature[Width * Height];
    /// <summary>우리가 본 칸. 처음에는 도성 둘레만 보이고, 정찰·영토·망루·군단이 넓힌다. 지도 생성이 아니라 판의 상태다.</summary>
    public readonly bool[] Seen = new bool[Width * Height];
    /// <summary>칸마다의 높이(0~1쯤). 강이 흐르는 방향과 그림자에 쓴다.</summary>
    public readonly double[] Elevation = new double[Width * Height];
    /// <summary>강이 지나는 칸.</summary>
    public readonly bool[] RiverCell = new bool[Width * Height];
    /// <summary>강줄기들. 저마다 샘에서 바다까지의 칸 번호.</summary>
    public readonly List<List<int>> Rivers = new();
    public readonly List<Province> Provinces = new();
    /// <summary>우리가 처음 자리 잡은 지방. 끝까지 도성이다.</summary>
    public int Capital;

    static readonly string[] Heads =
    {
        "가람", "누리", "다솜", "마루", "바람", "새벽", "아라", "한울", "미르", "솔", "달", "별", "샘", "벼리", "너울", "도담",
        "보람", "시내", "여울", "이슬", "하늬", "노을", "구름", "소리", "거친", "푸른", "검은", "붉은", "높은", "깊은", "너른", "고운",
    };

    static readonly string[][] Tails =
    {
        new[] { "섬" },
        new[] { "벌", "들", "고을" },
        new[] { "숲", "골", "말" },
        new[] { "재", "언덕", "골" },
        new[] { "뫼", "봉", "재" },
        new[] { "벌판", "들", "터" },
    };

    static double Hash(int seed, int a, int b)
    {
        unchecked
        {
            uint h = (uint)seed * 374761393u + (uint)a * 668265263u + (uint)b * 2246822519u + 0x9E3779B9u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            h *= 2654435761u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    static double Smooth(int seed, double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        double a = Hash(seed, x0, y0), b = Hash(seed, x0 + 1, y0), c = Hash(seed, x0, y0 + 1), d = Hash(seed, x0 + 1, y0 + 1);
        return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
    }

    public Province this[int id] => Provinces[id];
    public double Distance(Province a, Province b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>(x, y)에서 r칸 안의 칸 번호들(지도 안만).</summary>
    public IEnumerable<int> Disc(int x, int y, int r)
    {
        for (int yy = Math.Max(0, y - r); yy <= Math.Min(Height - 1, y + r); yy++)
            for (int xx = Math.Max(0, x - r); xx <= Math.Min(Width - 1, x + r); xx++)
                if ((xx - x) * (xx - x) + (yy - y) * (yy - y) <= r * r + r) yield return yy * Width + xx;
    }

    /// <summary>r칸 안을 밝힌다(안개를 걷는다). 새로 밝힌 칸 수를 돌려준다.</summary>
    public int Reveal(int x, int y, int r)
    {
        int n = 0;
        foreach (int i in Disc(x, y, r))
            if (!Seen[i]) { Seen[i] = true; n++; }
        return n;
    }

    /// <summary>그 지방에 본 칸이 하나라도 있는가.</summary>
    public bool Known(Province p)
    {
        if (Seen[p.Y * Width + p.X]) return true;
        for (int i = 0; i < Cell.Length; i++)
            if (Cell[i] == p.Id && Seen[i]) return true;
        return false;
    }

    public static WorldMap Generate(int seed)
    {
        var map = new WorldMap();
        int w = Width, h = Height;
        var height = new double[w * h];
        var land = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double n = 0.5 * Smooth(seed, x / 16.0, y / 16.0) + 0.27 * Smooth(seed + 11, x / 7.5, y / 7.5) + 0.15 * Smooth(seed + 23, x / 3.5, y / 3.5) + 0.08 * Smooth(seed + 29, x / 1.7, y / 1.7);
                double dx = (x - (w - 1) / 2.0) / (w / 2.0), dy = (y - (h - 1) / 2.0) / (h / 2.0);
                double e = n - 0.5 * Math.Pow(dx * dx + dy * dy, 1.4);
                height[y * w + x] = e;
                land[y * w + x] = e > 0.3 && x > 0 && y > 0 && x < w - 1 && y < h - 1;
            }

        // 지방의 씨: 뭍의 칸을 해시 순서로 훑으며 서로 충분히 떨어진 것만 고른다.
        var cand = Enumerable.Range(0, w * h).Where(i => land[i]).OrderBy(i => Hash(seed + 37, i % w, i / w)).ThenBy(i => i).ToList();
        var seeds = new List<int>();
        foreach (int i in cand)
        {
            int x = i % w, y = i / w;
            if (seeds.All(s => (s % w - x) * (s % w - x) + (s / w - y) * (s / w - y) >= 44)) seeds.Add(i);
            if (seeds.Count >= 90) break;
        }

        // 씨에서 동시에 번져 나가 칸을 나눈다. 닿지 않는 작은 섬은 바다로 친다.
        Array.Fill(map.Cell, -1);
        var queue = new Queue<int>();
        for (int k = 0; k < seeds.Count; k++)
        {
            map.Cell[seeds[k]] = k;
            queue.Enqueue(seeds[k]);
            map.Provinces.Add(new Province { Id = k, X = seeds[k] % w, Y = seeds[k] / w });
        }
        int[] dxs = { 1, -1, 0, 0 }, dys = { 0, 0, 1, -1 };
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = i % w + dxs[d], ny = i / w + dys[d];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int j = ny * w + nx;
                if (!land[j] || map.Cell[j] >= 0) continue;
                map.Cell[j] = map.Cell[i];
                queue.Enqueue(j);
            }
        }

        var sumH = new double[seeds.Count];
        for (int i = 0; i < w * h; i++)
        {
            int id = map.Cell[i];
            if (id < 0) continue;
            var p = map.Provinces[id];
            p.Size++;
            sumH[id] += height[i];
            for (int d = 0; d < 4; d++)
            {
                int nx = i % w + dxs[d], ny = i / w + dys[d];
                int other = nx < 0 || ny < 0 || nx >= w || ny >= h ? -1 : map.Cell[ny * w + nx];
                if (other < 0) p.Coastal = true;
                else if (other != id && !p.Adjacent.Contains(other)) p.Adjacent.Add(other);
            }
        }

        // 땅의 생김새: 높은 순으로 산과 언덕, 나머지는 물기에 따라 숲·들·초원.
        var byHeight = map.Provinces.OrderByDescending(p => sumH[p.Id] / p.Size).ThenBy(p => p.Id).ToList();
        int n1 = Math.Max(2, byHeight.Count * 12 / 100), n2 = Math.Max(3, byHeight.Count * 20 / 100);
        for (int k = 0; k < byHeight.Count; k++)
            byHeight[k].Terrain = k < n1 ? Terrain.Mountain : k < n1 + n2 ? Terrain.Hills : Terrain.Plains;
        var low = byHeight.Skip(n1 + n2).OrderByDescending(p => Smooth(seed + 51, p.X / 9.0, p.Y / 9.0)).ThenBy(p => p.Id).ToList();
        for (int k = 0; k < low.Count; k++)
            low[k].Terrain = k < low.Count * 30 / 100 ? Terrain.Forest : k >= low.Count * 78 / 100 ? Terrain.Steppe : Terrain.Plains;

        // 도성: 지도 한가운데에 가장 가까운 들.
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
        var capital = map.Provinces.OrderBy(p => p.Terrain == Terrain.Plains ? 0 : 1)
            .ThenBy(p => (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)).ThenBy(p => p.Id).First();
        capital.Terrain = Terrain.Plains;
        capital.Owner = 0;
        map.Capital = capital.Id;

        // 바다로 끊긴 땅은 뱃길로 잇는다: 도성에서 닿지 않는 지방이 없게 한다.
        while (true)
        {
            var seen = new HashSet<int> { capital.Id };
            var q = new Queue<int>();
            q.Enqueue(capital.Id);
            while (q.Count > 0)
                foreach (int a in map.Provinces[q.Dequeue()].Adjacent)
                    if (seen.Add(a)) q.Enqueue(a);
            if (seen.Count == map.Provinces.Count) break;
            var best = (from a in map.Provinces where seen.Contains(a.Id)
                        from b in map.Provinces where !seen.Contains(b.Id)
                        orderby map.Distance(a, b), a.Id, b.Id
                        select (a, b)).First();
            best.a.Adjacent.Add(best.b.Id);
            best.b.Adjacent.Add(best.a.Id);
        }
        foreach (var p in map.Provinces) p.Adjacent.Sort();

        // 묻힌 것들. 먼저 자원마다 한 곳씩은 반드시 놓고(어느 지도에나 모든 자원이 있다),
        // 남는 땅의 일곱 중 셋쯤은 비워 둔 채 드문 것부터 어울리는 땅에 더 놓는다. 도성 밑에는 아무것도 없다.
        int Place(Resource r, int count, params Terrain[] fits)
        {
            var spots = map.Provinces.Where(p => p.Deposit == Resource.None && p.Id != capital.Id)
                .OrderBy(p => Array.IndexOf(fits, p.Terrain) >= 0 ? 0 : 1).ThenBy(p => Hash(seed + 71 + (int)r, p.X, p.Y)).ThenBy(p => p.Id)
                .Take(Math.Max(0, count)).ToList();
            foreach (var p in spots) p.Deposit = r;
            return spots.Count;
        }
        var plan = new (Resource r, int more, Terrain[] fits)[]
        {
            (Resource.Uranium, 1, new[] { Terrain.Mountain, Terrain.Hills }),
            (Resource.Oil, 2, new[] { Terrain.Steppe }),
            (Resource.Copper, 3, new[] { Terrain.Hills, Terrain.Mountain }),
            (Resource.Iron, 3, new[] { Terrain.Hills, Terrain.Mountain }),
            (Resource.Coal, 3, new[] { Terrain.Forest, Terrain.Hills }),
            (Resource.Spice, 2, new[] { Terrain.Forest, Terrain.Plains }),
            (Resource.Livestock, 4, new[] { Terrain.Steppe, Terrain.Plains }),
        };
        foreach (var (r, _, fits) in plan) Place(r, 1, fits);
        int spare = (int)((map.Provinces.Count - 1) * 0.6) - plan.Length;
        for (int round = 0; round < 4 && spare > 0; round++)
            foreach (var (r, more, fits) in plan)
                if (round < more && spare > 0) spare -= Place(r, 1, fits);

        // 강: 높은 땅의 샘에서 낮은 쪽으로 흘러 바다에 닿는다.
        Array.Copy(height, map.Elevation, height.Length);
        var springs = Enumerable.Range(0, w * h).Where(i => land[i] && map.Cell[i] >= 0 && map.Provinces[map.Cell[i]].Terrain is Terrain.Mountain or Terrain.Hills)
            .OrderByDescending(i => height[i] + 0.2 * Hash(seed + 113, i % w, i / w)).ThenBy(i => i).ToList();
        var heads = new List<int>();
        foreach (int i in springs)
        {
            int x = i % w, y = i / w;
            if (heads.All(s => Math.Abs(s % w - x) + Math.Abs(s / w - y) >= 13)) heads.Add(i);
            if (heads.Count >= 11) break;
        }
        foreach (int head in heads)
        {
            var path = new List<int> { head };
            var seen = new HashSet<int> { head };
            int cur = head;
            for (int step = 0; step < 220; step++)
            {
                int best = -1;
                double bestH = double.MaxValue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cur % w + dxs[d], ny = cur / w + dys[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (seen.Contains(j)) continue;
                    double hh = height[j] + 0.03 * Hash(seed + 127, nx, ny);
                    if (hh < bestH) { bestH = hh; best = j; }
                }
                if (best < 0) break;
                path.Add(best);
                seen.Add(best);
                cur = best;
                if (map.Cell[best] < 0) break; // 바다에 닿았다
                if (map.RiverCell[best]) break; // 다른 강에 합쳐졌다
            }
            if (path.Count < 4) continue;
            foreach (int i in path)
                if (map.Cell[i] >= 0)
                {
                    map.RiverCell[i] = true;
                    map.Provinces[map.Cell[i]].River = true;
                }
            map.Rivers.Add(path);
        }

        // 칸마다의 땅결: 지방의 생김새를 바탕으로 숲 덤불, 언덕, 모래톱, 늪이 섞인다.
        for (int i = 0; i < w * h; i++)
        {
            int x = i % w, y = i / w;
            bool nearLand = false, nearSea = false;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + dxs[d], ny = y + dys[d];
                bool sea = nx < 0 || ny < 0 || nx >= w || ny >= h || map.Cell[ny * w + nx] < 0;
                if (sea) nearSea = true; else nearLand = true;
            }
            if (map.Cell[i] < 0) { map.Tile[i] = nearLand ? Ground.Shallows : Ground.Deep; continue; }
            double rough = Smooth(seed + 101, x / 3.0, y / 3.0), wet = Smooth(seed + 51, x / 9.0, y / 9.0), grit = Hash(seed + 131, x, y);
            map.Tile[i] = map.Provinces[map.Cell[i]].Terrain switch
            {
                Terrain.Mountain => rough > 0.42 ? Ground.Peak : Ground.Hill,
                Terrain.Hills => rough > 0.5 ? Ground.Hill : wet > 0.55 ? Ground.Woods : Ground.Grass,
                Terrain.Forest => rough > 0.28 ? Ground.Woods : map.RiverCell[i] && grit < 0.5 ? Ground.Marsh : Ground.Grass,
                Terrain.Steppe => rough > 0.72 ? Ground.Grass : Ground.Dry,
                _ => rough > 0.7 && wet > 0.45 ? Ground.Woods : rough < 0.14 ? Ground.Hill : Ground.Grass,
            };
            if (nearSea && grit < 0.45 && map.Tile[i] is Ground.Grass or Ground.Dry) map.Tile[i] = Ground.Sand;
        }
        map.Tile[capital.Y * w + capital.X] = Ground.Grass;

        // 칸마다 있는 것: 땅결을 보고 해시로 놓는다. 강에는 물고기, 강가의 들에는 기름진 흙.
        for (int i = 0; i < w * h; i++)
        {
            int x = i % w, y = i / w;
            double f = Hash(seed + 151, x, y);
            bool byRiver = map.RiverCell[i] || Enumerable.Range(0, 4).Any(d =>
            {
                int nx = x + dxs[d], ny = y + dys[d];
                return nx >= 0 && ny >= 0 && nx < w && ny < h && map.RiverCell[ny * w + nx] && map.Cell[ny * w + nx] >= 0;
            });
            map.Feat[i] = map.Tile[i] switch
            {
                Ground.Shallows => f < 0.22 ? Feature.Fish : Feature.None,
                Ground.Woods => f < 0.14 ? Feature.Game : f < 0.27 ? Feature.Berries : Feature.None,
                Ground.Grass => map.RiverCell[i] && f < 0.3 ? Feature.Fish : byRiver && f < 0.45 ? Feature.Fertile
                    : f < 0.06 ? Feature.Game : f < 0.12 ? Feature.Berries : f < 0.16 ? Feature.Fertile : Feature.None,
                Ground.Dry => f < 0.09 ? Feature.Game : Feature.None,
                Ground.Hill => f < 0.2 ? Feature.Stone : f < 0.28 ? Feature.Game : Feature.None,
                Ground.Peak => f < 0.32 ? Feature.Stone : Feature.None,
                Ground.Marsh => f < 0.25 ? Feature.Berries : f < 0.4 ? Feature.Fish : Feature.None,
                _ => Feature.None,
            };
        }

        // 첫 자리 둘레에는 먹고살 거리가 조금은 있어야 한다: 모자라면 가까운 칸부터 채운다.
        int capCell = capital.Y * w + capital.X;
        map.Feat[capCell] = Feature.None;
        var near = map.Disc(capital.X, capital.Y, 5).Where(i => i != capCell && map.Cell[i] == capital.Id)
            .OrderBy(i => (i % w - capital.X) * (i % w - capital.X) + (i / w - capital.Y) * (i / w - capital.Y)).ThenBy(i => Hash(seed + 157, i % w, i / w)).ThenBy(i => i).ToList();
        foreach (var (feat, want) in new[] { (Feature.Berries, 3), (Feature.Game, 3), (Feature.Stone, 1), (Feature.Fertile, 2) })
        {
            int have = near.Count(i => map.Feat[i] == feat);
            foreach (int i in near.Where(i => map.Feat[i] == Feature.None).Skip(feat == Feature.Game ? 3 : feat == Feature.Stone ? 6 : 1))
            {
                if (have >= want) break;
                map.Feat[i] = feat;
                if (feat == Feature.Stone) map.Tile[i] = Ground.Hill;
                have++;
            }
        }

        // 이름.
        var used = new HashSet<string>();
        foreach (var p in map.Provinces)
        {
            var tails = Tails[(int)p.Terrain];
            int hi = (int)(Hash(seed + 91, p.X, p.Y) * Heads.Length), ti = (int)(Hash(seed + 93, p.X, p.Y) * tails.Length);
            string name;
            int tries = 0;
            do name = Heads[(hi + tries / tails.Length) % Heads.Length] + tails[(ti + tries++) % tails.Length];
            while (!used.Add(name) && tries < Heads.Length * tails.Length);
            p.Name = name;
        }
        return map;
    }
}

// 영토: 지방을 얻고 잃는 일, 그리고 나라의 영토 수(Realm.Territory)와 지도를 맞추는 일.
public sealed partial class Simulation
{
    public WorldMap Map { get; private set; } = null!;

    public IEnumerable<Province> Lands(int owner) => Map.Provinces.Where(p => p.Owner == owner);
    Province CapitalLand => Map[Map.Capital];

    /// <summary>우리 땅의 기름짐(평균). 들과 바닷가는 많이, 산과 초원은 적게 먹인다.</summary>
    public double Fertility()
    {
        var mine = Lands(0).ToList();
        return mine.Count == 0 ? 1 : mine.Average(p => p.Fertility);
    }

    /// <summary>owner의 땅과 맞닿은, from이 가진 지방들. 가까운 순.</summary>
    List<Province> Frontier(int owner, int from, Province home)
    {
        var mine = Lands(owner).Select(p => p.Id).ToHashSet();
        return Map.Provinces.Where(p => p.Owner == from && p.Adjacent.Any(mine.Contains))
            .OrderBy(p => Map.Distance(p, home)).ThenBy(p => p.Id).ToList();
    }

    /// <summary>
    /// 지방마다 사는 백성 수(지방 Id 순, 우리 땅이 아니면 0). 나라의 백성을 땅의 넓이와 기름짐대로 나누고,
    /// 도시화한 만큼은 도성으로 몰린다.
    /// </summary>
    public double[] LandPops()
    {
        var pops = new double[Map.Provinces.Count];
        var mine = Lands(0).ToList();
        if (mine.Count == 0 || Realm.Stage == Stage.Tribe) return pops;
        double W(Province p) => p.Size * p.Fertility * (p.Id == Map.Capital ? 1.5 + 6 * Realm.Urban : 1);
        double sum = mine.Sum(W);
        foreach (var p in mine) pops[p.Id] = Realm.Pop * W(p) / sum;
        return pops;
    }

    bool IsSeat(Province p) => p.Id == Map.Capital || Rivals.Any(v => v.Seat == p.Id);

    /// <summary>땅 한 조각을 얻는다. from이 있으면 그 나라에게서 빼앗고, 없으면 주인 없는 땅을 차지한다.</summary>
    internal Province? GainLand(Rival? from, Resource want = Resource.None)
    {
        Realm.Territory++;
        int owner = from?.Id ?? -1;
        var pick = Frontier(0, owner, CapitalLand).OrderBy(p => IsSeat(p) ? 1 : 0)
            .ThenBy(p => want != Resource.None && p.Deposit == want ? 0 : 1).FirstOrDefault();
        // 맞닿은 곳이 없으면 그 나라의 땅 가운데 우리와 가장 가까운 곳(바다 건너 땅)을 얻는다.
        pick ??= Lands(owner).OrderBy(p => IsSeat(p) ? 1 : 0).ThenBy(p => Map.Distance(p, CapitalLand)).ThenBy(p => p.Id).FirstOrDefault();
        pick ??= Frontier(0, -1, CapitalLand).FirstOrDefault();
        if (pick == null) return null;
        if (from != null && from.Seat == pick.Id) from.Seat = -1;
        pick.Owner = 0;
        return pick;
    }

    /// <summary>땅 한 조각을 잃는다. to가 있으면 그 나라로, 없으면 주인 없는 땅으로 돌아간다. 도성은 잃지 않는다.</summary>
    internal Province? LoseLand(Rival? to)
    {
        if (Realm.Territory <= 1) return null;
        Realm.Territory--;
        var mine = Lands(0).Where(p => p.Id != Map.Capital).ToList();
        if (mine.Count == 0) return null;
        var theirs = to == null ? new HashSet<int>() : Lands(to.Id).Select(p => p.Id).ToHashSet();
        var pick = mine.OrderBy(p => p.Adjacent.Any(theirs.Contains) ? 0 : 1)
            .ThenByDescending(p => Map.Distance(p, CapitalLand)).ThenBy(p => p.Id).First();
        pick.Owner = to?.Id ?? -1;
        return pick;
    }

    /// <summary>이웃 나라가 도성으로 삼을 빈 땅. 우리와도, 다른 도성과도 떨어진 곳.</summary>
    Province? FreeSeat()
    {
        var seats = Rivals.Where(v => v.Seat >= 0).Select(v => Map[v.Seat]).Append(CapitalLand).ToList();
        return Map.Provinces.Where(p => p.Owner == -1)
            .OrderByDescending(p => seats.Min(s => Map.Distance(p, s)) - 0.4 * Map.Distance(p, CapitalLand))
            .ThenBy(p => p.Id).FirstOrDefault();
    }

    /// <summary>
    /// 지도를 나라의 상태에 맞춘다. 영토 수가 지도와 어긋나면(몰락, 시험에서 직접 고친 값) 가장자리부터 얻거나 내놓고,
    /// 이웃 나라는 힘만큼 땅을 넓히거나 잃는다. 해마다 한 번 부른다.
    /// </summary>
    internal void SyncMap()
    {
        var r = Realm;
        var alive = Rivals.Select(v => v.Id).ToHashSet();
        foreach (var p in Map.Provinces)
            if (p.Owner > 0 && !alive.Contains(p.Owner)) p.Owner = -1;
        var capital = CapitalLand;
        foreach (var v in Rivals.Where(v => v.Seat == capital.Id)) v.Seat = -1;
        capital.Owner = 0;
        PruneRoads();

        int mine = Lands(0).Count();
        while (mine < r.Territory)
        {
            var pick = Frontier(0, -1, capital).FirstOrDefault()
                       ?? Map.Provinces.Where(p => p.Owner > 0 && !IsSeat(p) && p.Adjacent.Any(a => Map[a].Owner == 0))
                           .OrderBy(p => Map.Distance(p, capital)).ThenBy(p => p.Id).FirstOrDefault()
                       ?? Map.Provinces.Where(p => p.Owner != 0 && !IsSeat(p)).OrderBy(p => Map.Distance(p, capital)).ThenBy(p => p.Id).FirstOrDefault();
            if (pick == null) break; // 지도보다 큰 영토는 지도에 다 그리지 못한다
            pick.Owner = 0;
            mine++;
        }
        while (mine > Math.Max(1, r.Territory))
        {
            var pick = Lands(0).Where(p => p.Id != capital.Id).OrderByDescending(p => Map.Distance(p, capital)).ThenBy(p => p.Id).First();
            pick.Owner = -1;
            mine--;
        }

        foreach (var v in Rivals.OrderBy(v => v.Id))
        {
            if (v.Seat < 0 || Map[v.Seat].Owner != v.Id)
            {
                // 도성을 잃었으면 남은 땅으로 옮기고, 땅이 하나도 없으면 빈 땅에 새로 선다.
                var seat = Lands(v.Id).OrderBy(p => p.Id).FirstOrDefault() ?? FreeSeat();
                if (seat == null) { v.Seat = -1; continue; }
                seat.Owner = v.Id;
                v.Seat = seat.Id;
            }
            var home = Map[v.Seat];
            int want = Math.Clamp((int)Math.Round(1 + 2.2 * v.Strength), 1, 6), has = Lands(v.Id).Count();
            if (has < want && Frontier(v.Id, -1, home).FirstOrDefault() is { } grow) grow.Owner = v.Id;
            else if (has > want + 1)
                Lands(v.Id).Where(p => p.Id != v.Seat).OrderByDescending(p => Map.Distance(p, home)).ThenBy(p => p.Id).First().Owner = -1;
        }
    }

    /// <summary>개척: 사람이 차면 맞닿은 빈 땅으로 옮겨 가 밭을 일군다.</summary>
    internal void Settle(double cap)
    {
        var r = Realm;
        if (Year < r.RebuildUntil || r.Pop < 0.5 * cap || r.Territory >= (r.Stage == Stage.Chiefdom ? 2 : WorldMap.Width)) return;
        if (Lands(0).Count() != r.Territory) return; // 지도와 어긋난 해에는 움직이지 않는다
        if (Frontier(0, -1, CapitalLand).Count == 0 || Rnd() >= (r.Territory < AdminCapacity() ? 0.1 : 0.04)) return;
        var land = GainLand(null)!;
        r.Treasury -= Inst.Taxation ? 0.3 : 0;
        Say($"【개척】 땅이 좁아지자 사람들이 {Josa.Ro(land.Name)} 옮겨 가 밭을 일구었다 (영토 {r.Territory}).");
    }
}
