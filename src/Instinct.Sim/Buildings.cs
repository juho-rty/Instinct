namespace Instinct.Sim;

/// <summary>플레이어가 땅에 짓는 건물. 지방마다 종류별로 하나씩 선다.</summary>
public enum Building { Granary, Market, Temple, Library, Barracks, Castle, Mine, Harbor, University, Factory }

/// <summary>건물 하나의 설명. 값(Cost)은 곳간에서 한 번, 유지비(Upkeep)는 해마다 나간다.</summary>
public sealed record BuildingDef(Building Kind, string Key, string Name, Era Era, string Tech, double Cost, double Upkeep, int Seasons, string Gain, string Price);

public static class BuildingCatalog
{
    public static readonly IReadOnlyList<BuildingDef> All = new[]
    {
        new BuildingDef(Building.Granary, "granary", "곳간", Era.Primitive, "pottery", 0.6, 0.01, 4, "흉년에 굶어 죽는 이가 준다", "유지비. 쌓인 곡식은 지키는 자의 것이 된다(불평등)"),
        new BuildingDef(Building.Temple, "temple", "사원", Era.Ancient, "", 1.0, 0.015, 8, "통치자 지지의 상처가 빨리 아물고 단합이 는다", "유지비. 지식이 조금 더디게 쌓인다"),
        new BuildingDef(Building.Barracks, "barracks", "병영", Era.Ancient, "bronze", 1.0, 0.03, 6, "군대가 강해지고 군단을 하나 더 거느린다", "유지비. 칼을 쥔 자가 늘수록 부패가 자란다"),
        new BuildingDef(Building.Market, "market", "장터", Era.Ancient, "currency", 1.0, 0.01, 6, "세입이 는다", "유지비. 부가 쏠린다(불평등)"),
        new BuildingDef(Building.Library, "library", "도서관", Era.Ancient, "script", 1.2, 0.02, 8, "지식이 빨리 쌓인다", "유지비"),
        new BuildingDef(Building.Mine, "mine", "광산", Era.Ancient, "bronze", 1.2, 0.015, 8, "묻힌 것을 캐어 세입이 는다(자원이 드러난 땅에만)", "유지비. 땅과 물이 더러워진다"),
        new BuildingDef(Building.Castle, "castle", "성", Era.Medieval, "masonry", 2.5, 0.04, 12, "이 땅을 치는 적이 성벽에 막힌다", "큰 유지비. 성을 쥔 귀족의 목소리가 커진다"),
        new BuildingDef(Building.University, "university", "대학", Era.Medieval, "theology", 2.5, 0.04, 12, "지식이 훨씬 빨리 쌓인다", "큰 유지비. 배운 이들이 먼저 불만을 말한다"),
        new BuildingDef(Building.Harbor, "harbor", "항구", Era.EarlyModern, "navigation", 1.5, 0.02, 8, "장삿길마다 세입이 는다(바닷가에만)", "유지비. 배는 병도 싣고 온다"),
        new BuildingDef(Building.Factory, "factory", "공장", Era.Industrial, "steam", 3.0, 0.03, 8, "세입이 크게 는다(공장제가 있어야)", "유지비. 굴뚝이 하늘을 덮는다(오염)"),
    };

    public static BuildingDef Of(Building b) => All.First(d => d.Kind == b);
    public static BuildingDef? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}

/// <summary>짓고 있는 것. 건물이거나 길이다.</summary>
public sealed class Site
{
    public int Province;
    public Building Kind;
    /// <summary>true면 건물이 아니라 길을 낸다.</summary>
    public bool Road;
    public int SeasonsLeft, Total;
}

// 건물과 길: 플레이어가 곳간을 헐어 땅에 남기는 것들.
public sealed partial class Simulation
{
    public readonly List<Site> Sites = new();

    /// <summary>우리 땅에 선 그 건물의 수.</summary>
    public int Built(Building b) => Lands(0).Count(p => p.Has(b));

    /// <summary>건물 효과: 하나에 per씩, cap개까지만 친다(많이 지을수록 덜 는다).</summary>
    double BE(Building b, double per, int cap) => per * Math.Min(cap, Built(b));

    /// <summary>도성까지 길이 이어진 우리 지방의 수(도성 빼고).</summary>
    public int RoadCount => Lands(0).Count(p => p.Road && p.Id != Map.Capital);

    static double RoughCost(Province p) => p.Terrain switch { Terrain.Mountain => 2.0, Terrain.Hills or Terrain.Forest => 1.4, _ => 1.0 };

    /// <summary>건물의 값. 험한 땅에서는 더 든다.</summary>
    public double BuildCost(Province p, Building b) => Math.Round(BuildingCatalog.Of(b).Cost * (b == Building.Mine ? 1 : 0.8 + 0.2 * RoughCost(p)), 2);

    /// <summary>길의 값.</summary>
    public double RoadCost(Province p) => Math.Round(0.4 * RoughCost(p), 2);

    /// <summary>해마다 나가는 건물·길·군대의 유지비.</summary>
    public double Upkeep() =>
        Lands(0).Sum(p => p.Buildings.Sum(b => BuildingCatalog.Of(b).Upkeep) + (p.Road && p.Id != Map.Capital ? 0.01 : 0))
        + Armies.Count(a => a.Owner == 0) * (Inst.StandingArmy ? 0.08 : 0.12);

    /// <summary>지을 수 없는 까닭. 지을 수 있으면 빈 문자열.</summary>
    public string WhyCantBuild(int province, Building b)
    {
        if (province < 0 || province >= Map.Provinces.Count) return "없는 지역";
        var p = Map[province];
        var d = BuildingCatalog.Of(b);
        if (Realm.Stage == Stage.Tribe) return "군장사회 단계부터 가능";
        if (p.Owner != 0) return "우리 영토가 아님";
        if (p.Has(b)) return "이미 있음";
        if (Sites.Any(s => s.Province == province && !s.Road && s.Kind == b)) return "건설 중";
        if (Realm.Era < d.Era) return $"{Catalog.EraNames[(int)d.Era]}부터 가능";
        if (d.Tech.Length > 0 && !Knows(d.Tech)) return $"기술 필요: {TechTree.Find(d.Tech)!.Name}";
        if (b == Building.Harbor && !p.Coastal) return "해안 지역이 아님";
        if (b == Building.Mine && (p.Deposit is Resource.None or Resource.Livestock or Resource.Spice || !Revealed(p.Deposit))) return "채굴할 자원이 발견되지 않음";
        if (b == Building.Factory && !Inst.Factories) return "공장제 필요";
        if (Realm.Treasury < BuildCost(p, b)) return $"국고 부족 ({BuildCost(p, b):F1} 필요)";
        return "";
    }

    /// <summary>건물을 짓기 시작한다. 값은 바로 치르고, 몇 철 뒤에 선다.</summary>
    public bool Build(int province, Building b)
    {
        if (WhyCantBuild(province, b).Length > 0) return false;
        var p = Map[province];
        var d = BuildingCatalog.Of(b);
        Realm.Treasury -= BuildCost(p, b);
        Sites.Add(new Site { Province = province, Kind = b, SeasonsLeft = d.Seasons, Total = d.Seasons });
        Minor($"{p.Name}에 {Josa.Eul(d.Name)} 짓기 시작했다.");
        Wrote("build", province.ToString(), d.Key);
        return true;
    }

    /// <summary>길을 이을 이웃: 길이 닿은(또는 도성인) 맞닿은 우리 땅 가운데 도성에 가장 가까운 곳.</summary>
    Province? RoadParent(Province p) =>
        p.Adjacent.Select(a => Map[a]).Where(a => a.Owner == 0 && (a.Road || a.Id == Map.Capital))
            .OrderBy(a => Map.Distance(a, CapitalLand)).ThenBy(a => a.Id).FirstOrDefault();

    public string WhyCantRoad(int province)
    {
        if (province < 0 || province >= Map.Provinces.Count) return "없는 지역";
        var p = Map[province];
        if (Realm.Stage == Stage.Tribe) return "군장사회 단계부터 가능";
        if (p.Owner != 0) return "우리 영토가 아님";
        if (p.Id == Map.Capital || p.Road) return "이미 도로가 있음";
        if (Sites.Any(s => s.Province == province && s.Road)) return "건설 중";
        if (!Knows("wheel")) return $"기술 필요: {TechTree.Find("wheel")!.Name}";
        if (RoadParent(p) == null) return "도로가 있는 땅과 맞닿아야 함";
        if (Realm.Treasury < RoadCost(p)) return $"국고 부족 ({RoadCost(p):F1} 필요)";
        return "";
    }

    /// <summary>도성에서 뻗은 길을 이 지방까지 잇는다.</summary>
    public bool BuildRoad(int province)
    {
        if (WhyCantRoad(province).Length > 0) return false;
        var p = Map[province];
        Realm.Treasury -= RoadCost(p);
        int seasons = (int)Math.Round(4 * RoughCost(p));
        Sites.Add(new Site { Province = province, Road = true, SeasonsLeft = seasons, Total = seasons });
        Minor($"{Josa.Ro(p.Name)} 가는 길을 닦기 시작했다.");
        Wrote("road", province.ToString());
        return true;
    }

    /// <summary>철마다: 짓던 것이 한 철만큼 올라간다. 땅을 잃으면 공사도 버려진다.</summary>
    internal void Construction()
    {
        foreach (var s in Sites.ToList())
        {
            var p = Map[s.Province];
            if (p.Owner != 0) { Sites.Remove(s); continue; }
            if (--s.SeasonsLeft > 0) continue;
            Sites.Remove(s);
            if (s.Road)
            {
                var parent = RoadParent(p);
                if (parent == null) continue; // 이을 곳을 잃었다
                p.Road = true;
                p.RoadTo = parent.Id;
                Say($"【건설】 {parent.Name}에서 {Josa.Ro(p.Name)} 가는 길이 열렸다. 수레와 세리와 군대가 그 길로 다닌다.");
            }
            else if (!p.Has(s.Kind))
            {
                p.Buildings.Add(s.Kind);
                var d = BuildingCatalog.Of(s.Kind);
                Say($"【건설】 {p.Name}에 {Josa.Iga(d.Name)} 섰다. {d.Gain}.");
            }
        }
    }

    /// <summary>해마다: 건물이 나라의 결에 남기는 것들(얻는 것은 저마다의 셈에 들어 있고, 여기서는 치르는 값).</summary>
    internal void BuildingsYear()
    {
        var r = Realm;
        r.Inequality = Clamp01(r.Inequality + 0.001 * Built(Building.Market) + 0.0005 * Built(Building.Granary));
        r.Corruption = Clamp01(r.Corruption + 0.001 * Built(Building.Barracks));
        r.Pollution = Clamp01(r.Pollution + 0.002 * Built(Building.Mine) + 0.004 * Built(Building.Factory));
        r.Grievance[(int)Estate.Commoners] = Math.Min(1, r.Grievance[(int)Estate.Commoners] + 0.003 * Built(Building.Castle));
        if (!Inst.Suffrage && r.Legitimacy < 0.45) r.Unrest = Clamp01(r.Unrest + 0.004 * Built(Building.University));
        if (Built(Building.Temple) > 0)
        {
            if (r.LegitShock > 0) r.LegitShock = Math.Max(0, r.LegitShock - BE(Building.Temple, 0.01, 4));
            r.Cohesion = Clamp01(r.Cohesion + BE(Building.Temple, 0.003, 4));
        }
    }

    /// <summary>나라가 무너지면 세운 것들도 함께 무너진다.</summary>
    internal void RuinBuildings(double p)
    {
        foreach (var land in Map.Provinces.OrderBy(x => x.Id))
        {
            if (land.Buildings.Count == 0) continue;
            land.Buildings.RemoveAll(_ => Rnd() < p);
        }
        Sites.Clear();
    }

    /// <summary>길이 끊긴 땅을 정리한다: 길은 도성까지 우리 땅으로 이어져 있어야 길이다.</summary>
    void PruneRoads()
    {
        var capital = CapitalLand;
        capital.Road = false;
        capital.RoadTo = -1;
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var p in Map.Provinces)
            {
                if (!p.Road) continue;
                var to = p.RoadTo >= 0 ? Map[p.RoadTo] : null;
                bool ok = p.Owner == 0 && to != null && to.Owner == 0 && (to.Id == capital.Id || to.Road);
                if (ok) continue;
                p.Road = false;
                p.RoadTo = -1;
                changed = true;
            }
        }
    }
}
