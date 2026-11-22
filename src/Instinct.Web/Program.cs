using System.Diagnostics;
using Instinct.Sim;

// 간단한 화면. 이 PC 안에서만 도는 작은 서버가 시뮬레이션을 쥐고, 브라우저의 한 페이지가 그것을 보여 준다.
//   dotnet run --project src/Instinct.Web -c Release      (또는 play.cmd)
// 환경 변수 INSTINCT_NO_BROWSER=1 이면 브라우저를 열지 않는다. INSTINCT_PORT 로 포트를 바꾼다.

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
string port = Environment.GetEnvironmentVariable("INSTINCT_PORT") ?? "5177";
string url = $"http://localhost:{port}";
builder.WebHost.UseUrls(url);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var gate = new object();
int gameId = 1;
var game = new Game(1, gameId);

app.MapGet("/api/state", (int? since, int? hist) =>
{
    lock (gate) return Results.Json(game.State(since ?? 0, hist ?? 0));
});

app.MapPost("/api/step", (int? seasons, int? since, int? hist) =>
{
    lock (gate)
    {
        game.Step(Math.Clamp(seasons ?? 1, 1, 400));
        return Results.Json(game.State(since ?? 0, hist ?? 0));
    }
});

app.MapPost("/api/toggle", (string key, int? since, int? hist) =>
{
    lock (gate)
    {
        game.Toggle(key);
        return Results.Json(game.State(since ?? 0, hist ?? 0));
    }
});

app.MapPost("/api/auto", (bool on, int? since, int? hist) =>
{
    lock (gate)
    {
        game.Auto = on;
        return Results.Json(game.State(since ?? 0, hist ?? 0));
    }
});

app.MapPost("/api/research", (string key, int? since, int? hist) =>
{
    lock (gate)
    {
        game.Sim.SetResearch(key);
        return Results.Json(game.State(since ?? 0, hist ?? 0));
    }
});

// 지도의 생김새는 한 판 동안 바뀌지 않는다. 주인과 드러난 자원은 /api/state 가 싣는다.
app.MapGet("/api/map", () =>
{
    lock (gate) return Results.Json(game.MapShape());
});

app.MapPost("/api/new", (int? seed) =>
{
    lock (gate)
    {
        game = new Game(seed ?? 1, ++gameId) { Auto = game.Auto };
        return Results.Json(game.State(0, 0));
    }
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"Instinct 화면: {url}  (끄려면 Ctrl+C)");
    if (Environment.GetEnvironmentVariable("INSTINCT_NO_BROWSER") == "1") return;
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch { /* 브라우저를 못 열어도 주소는 위에 찍혀 있다 */ }
});

app.Run();

/// <summary>한 판. 시뮬레이션과, 화면에서 켜는 '제도 자동 도입'의 난수를 쥔다.</summary>
sealed class Game
{
    static readonly string[] TraitNames = { "공격성", "탐욕", "야심", "핏줄 편애", "성욕", "공감" };

    public readonly Simulation Sim;
    public readonly int Seed, Id;
    public bool Auto;
    readonly Random _policy; // 시뮬레이션의 난수와 섞지 않는다

    public Game(int seed, int id)
    {
        Seed = seed;
        Id = id;
        Sim = new Simulation(seed);
        _policy = new Random(seed * 7919 + 1);
    }


    public void Step(int seasons)
    {
        for (int k = 0; k < seasons && Sim.Living.Any(); k++)
        {
            if (Auto && Sim.Season == 0) AutoPolicy.Year(Sim, _policy);
            Sim.Step();
        }
    }

    public void Toggle(string key)
    {
        if (Catalog.Find(key) is not { } d) return;
        Sim.Enact(d, !d.Get(Sim.Inst)); // 값과 반발은 코어가 매긴다
    }

    public object MapShape()
    {
        var m = Sim.Map;
        return new
        {
            gameId = Id,
            w = WorldMap.Width,
            h = WorldMap.Height,
            cells = m.Cell,
            capital = m.Capital,
            provinces = m.Provinces.Select(p => new
            {
                id = p.Id, name = p.Name, terrain = (int)p.Terrain, x = p.X, y = p.Y, size = p.Size, coastal = p.Coastal,
                fertility = Math.Round(p.Fertility, 2), adj = p.Adjacent,
            }),
        };
    }

    public object State(int since, int hist)
    {
        var s = Sim;
        var r = s.Realm;
        since = Math.Clamp(since, 0, s.Log.Count);
        if (s.Log.Count - since > 800) since = s.Log.Count - 800; // 한꺼번에 너무 많이 보내지 않는다
        hist = Math.Clamp(hist, 0, s.History.Count);
        var living = s.Living.ToList();

        return new
        {
            gameId = Id,
            seed = Seed,
            auto = Auto,
            year = s.Year,
            season = Simulation.SeasonNames[s.Season],
            era = s.EraName,
            stage = s.StageName,
            stageIndex = (int)r.Stage,
            peakStage = Catalog.StageNames[(int)r.PeakStage],
            rulerTitle = s.RulerTitle,
            chief = s.Chief is { Alive: true } c ? s.P(c) : null,
            nextEra = s.NextEraNeeds(),
            nextStage = s.NextStageNeeds(),
            living = living.Count,
            peakLiving = s.PeakPopulation,
            births = s.TotalBirths,
            store = Math.Round(s.CommonStore, 1),
            realm = new
            {
                pop = Math.Round(r.Pop),
                r.Territory,
                admin = s.AdminCap,
                landCap = Math.Round(s.LandCapacity()),
                treasury = Math.Round(r.Treasury, 1),
                knowledge = Math.Round(r.Knowledge),
                r.Unrest, r.Suppressed, r.Legitimacy, r.Inequality, r.Corruption, r.Cohesion, r.Urban, r.Pollution,
                r.Collapses, r.Atrocities, r.Heresy,
                reformHeat = Math.Round(r.ReformHeat, 2),
                falls = r.Falls.Select(f => f.Split('|')).Select(f => new { year = int.Parse(f[0]), kind = f[1] }),
                dynasty = r.DynastyClan >= 0 ? s.ClanName(r.DynastyClan) : null,
                dynastyAge = s.Year - r.FoundedYear,
                stateAge = s.Year - r.StateFounded,
            },
            estates = Enum.GetValues<Estate>().Select(e => new
            {
                name = Catalog.EstateNames[(int)e],
                share = Math.Round(r.Share[(int)e], 3),
                anger = Math.Round(s.EstateAnger(e), 3),
                grievance = Math.Round(r.Grievance[(int)e], 3),
            }),
            rivals = s.Rivals.OrderBy(v => v.Id).Select(v => new
            {
                id = v.Id,
                seat = v.Seat,
                lands = s.Lands(v.Id).Count(),
                name = v.Name,
                strength = Math.Round(v.Strength, 2),
                hostility = Math.Round(v.Hostility, 3),
                otherFaith = v.Faith != 0,
                ahead = v.Advance > r.Knowledge,
                trade = v.Trade, allied = v.Allied, free = v.Free,
                treaty = Math.Max(0, v.TreatyUntil - s.Year),
            }),
            // 지방마다 [주인, 드러난 자원]. 알아볼 기술이 없는 자원은 싣지 않는다.
            lands = s.Map.Provinces.Select(p => new[] { p.Owner, s.Revealed(p.Deposit) ? (int)p.Deposit : 0 }),
            landPop = s.LandPops().Select(v => Math.Round(v)),
            eraIndex = (int)r.Era,
            seasonIndex = s.Season,
            bands = s.Tribal ? s.Neighbors.Select(n => new { name = n.Name, fighters = n.Fighters, hostility = Math.Round(n.Hostility, 2), allied = n.Allied }) : null,
            research = new
            {
                key = s.Research,
                progress = Math.Round(s.ResearchProgress, 1),
                rate = Math.Round(s.ResearchRate, 2),
                known = s.KnownCount,
                total = TechTree.All.Count,
            },
            techs = TechTree.All.Select(t => new
            {
                key = t.Key, name = t.Name, era = (int)t.Era, cost = t.Cost, requires = t.Requires, effect = t.Effect,
                known = s.Knows(t.Key), can = s.CanResearch(t),
                unlocks = TechTree.Unlocks(t.Key).Select(d => d.Name),
                reveals = ResourceCatalog.Name(t.Reveals),
            }),
            resources = ResourceCatalog.All.Select(d => new
            {
                kind = (int)d.Kind, name = d.Name, use = d.Use, era = Catalog.EraNames[(int)d.Era],
                tech = TechTree.Find(d.Tech)!.Name,
                revealed = s.Revealed(d.Kind), own = s.OwnDeposits(d.Kind), supply = s.Supply(d.Kind),
                holders = s.Revealed(d.Kind) ? s.Rivals.Where(v => s.Lands(v.Id).Any(p => p.Deposit == d.Kind)).OrderBy(v => v.Id).Select(v => v.Name) : Enumerable.Empty<string>(),
            }),
            stageNames = Catalog.StageNames,
            traitNames = TraitNames,
            deaths = s.DeathsByCause.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => new { cause = kv.Key, n = kv.Value }),
            logStart = since,
            logCount = s.Log.Count,
            log = s.Log.Skip(since).Select(e => new { t = e.Tick, k = (int)e.Kind, x = e.Text }),
            histStart = hist,
            history = s.History.Skip(hist).Select(h => new[]
            {
                h.Year, (int)h.Stage, (int)h.Era, Math.Round(h.Pop), h.Court, h.Territory, Math.Round(h.Treasury, 1), Math.Round(h.Knowledge),
                Math.Round(h.Unrest, 3), Math.Round(h.Suppressed, 3), Math.Round(h.Legitimacy, 3), Math.Round(h.Inequality, 3),
                Math.Round(h.Corruption, 3), Math.Round(h.Cohesion, 3),
            }),
            institutions = Catalog.All.Select(d => new
            {
                key = d.Key, name = d.Name, era = Catalog.EraNames[(int)d.Era], stage = Catalog.StageNames[(int)d.MinStage],
                gain = d.Gain, cost = d.Cost, on = d.Get(s.Inst), available = s.Available(d), why = s.WhyUnavailable(d),
                reform = s.PreviewReform(d).Text,
                winners = d.Winners.Select(e => Catalog.EstateNames[(int)e]),
                losers = d.Losers.Select(e => Catalog.EstateNames[(int)e]),
            }),
            people = living.OrderByDescending(p => p == s.Chief).ThenByDescending(p => p.Status).ThenBy(p => p.Id).Take(60).Select(p =>
            {
                string? grudge = null;
                var top = p.Grudge.Where(g => g.Value >= 0.3).OrderByDescending(g => g.Value).ThenBy(g => g.Key)
                    .Select(g => (who: s.People.FirstOrDefault(x => x.Id == g.Key), g.Key))
                    .FirstOrDefault(g => g.who is { Alive: true });
                if (top.who != null) grudge = $"{top.who.Name} — {p.GrudgeWhy.GetValueOrDefault(top.Key, "묵은 원한으로")}";
                return new
                {
                    id = p.Id,
                    clan = p.ClanId,
                    name = s.P(p),
                    male = p.IsMale,
                    age = (int)p.Age(s.Tick),
                    status = Math.Round(p.Status, 1),
                    chief = p == s.Chief,
                    thrall = p.Master != null,
                    outsider = p.Outsider,
                    traits = p.Traits.Select(t => (int)Math.Round(t * 100)),
                    grudge,
                };
            }),
        };
    }
}
