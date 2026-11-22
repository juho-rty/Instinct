using System.Text;
using Instinct.Sim;

// 사용법:
//   dotnet run --project src/Instinct.Runner -- [--seed N] [--years N] [--verbose] [--quiet] [--auto] [--list]
//                                               [--제도키[@년]] ...
// --difficulty easy/normal/hard 는 난이도(기본 normal).
// --aim conquest/commerce/terror/tyranny/learning 은 --player 의 흉내가 따라갈 길(갈림길과 제도를 그 길로 고른다).
// --player 는 플레이어 규칙(PlayerRules)을 켜고 PlayerBot이 사람처럼 다스린다(물음에 답하고, 짓고, 군단을 움직인다). 균형을 재는 용도.
// 제도 키는 --list 로 본다 (예: --private@30 --agriculture@40). @년을 붙이면 그 해 봄에 도입한다.
// --auto 는 쓸 수 있게 된 제도를 값을 치를 수 있을 때 해마다 하나쯤 알아서 도입한다(끝까지 돌려 보는 용도).

Console.OutputEncoding = Encoding.UTF8;

int seed = 1, years = 100;
bool verbose = false, quiet = false, auto = false, player = false;
var difficulty = Difficulty.Normal;
Course? aim = null;
var schedule = new List<(int year, InstitutionDef def)>();

for (int i = 0; i < args.Length; i++)
{
    var a = args[i];
    switch (a)
    {
        case "--seed": seed = int.Parse(args[++i]); break;
        case "--years": years = int.Parse(args[++i]); break;
        case "--verbose": verbose = true; break;
        case "--quiet": quiet = true; break;
        case "--auto": auto = true; break;
        case "--player": player = true; break;
        case "--aim":
            if (Simulation.ParseCourse(args[++i]) is not { } cv) { Console.Error.WriteLine($"알 수 없는 길: {args[i]} (conquest/commerce/terror/tyranny/learning)"); return 1; }
            aim = cv;
            player = true;
            break;
        case "--difficulty":
            if (Simulation.ParseDifficulty(args[++i]) is not { } dv) { Console.Error.WriteLine($"알 수 없는 난이도: {args[i]} (easy/normal/hard)"); return 1; }
            difficulty = dv;
            break;
        case "--list":
            foreach (var d in Catalog.All)
                Console.WriteLine($"--{d.Key,-14} {d.Name} [{Catalog.EraNames[(int)d.Era]}·{Catalog.StageNames[(int)d.MinStage]}부터]  얻는 것: {d.Gain} / 값: {d.Cost}");
            return 0;
        default:
            var parts = a.TrimStart('-').Split('@');
            if (Catalog.Find(parts[0]) is not { } def)
            {
                Console.Error.WriteLine($"알 수 없는 옵션: {a} (--list 로 제도 목록을 본다)");
                return 1;
            }
            schedule.Add((parts.Length > 1 ? int.Parse(parts[1]) : 1, def));
            break;
    }
}

var sim = new Simulation(seed) { PlayerRules = player, Difficulty = difficulty };
var policy = new Random(seed * 7919 + 1); // 자동 도입용. 시뮬레이션의 난수와 섞지 않는다.
int printed = 0;


void Flush()
{
    for (; printed < sim.Log.Count; printed++)
    {
        var e = sim.Log[printed];
        if (e.Kind == LogKind.Minor && !verbose) continue;
        if (e.Kind != LogKind.Summary && quiet) continue;
        if (e.Kind == LogKind.Summary)
            Console.WriteLine($"── {e.Text}");
        else
            Console.WriteLine($"[{e.Tick / 4 + 1}년 {Simulation.SeasonNames[e.Tick % 4]}] {e.Text}");
    }
}

for (int t = 0; t < years * 4; t++)
{
    if (sim.Season == 0)
    {
        // 정해 둔 해가 되었어도 아직 그 시대가 아니면 쓸 수 있게 되는 해에 도입한다.
        foreach (var s in schedule.Where(s => s.year <= sim.Year && !s.def.Get(sim.Inst) && sim.Available(s.def)).ToList())
        {
            sim.Enact(s.def, true);
            schedule.Remove(s);
        }
        if (auto) AutoPolicy.Year(sim, policy);
    }

    if (player) PlayerBot.Season(sim, policy, aim);
    sim.Step();
    Flush();
    if (!sim.Living.Any()) break;
}

var r = sim.Realm;
Console.WriteLine();
Console.WriteLine($"═ 씨앗 {seed}, {sim.Year - (sim.Season == 0 ? 1 : 0)}년 경과 ═");
Console.WriteLine($"{sim.EraName}·{sim.StageName} (최고 {Catalog.StageNames[(int)r.PeakStage]}), 몰락 {r.Collapses}회, 백성 {Math.Round(r.Pop):N0}명, 영토 {r.Territory}, 지식 {r.Knowledge:F0}");
if (player)
{
    int Count(string s) => sim.Log.Count(e => e.Text.Contains(s));
    Console.WriteLine($"플레이어 규칙: 명령 {sim.Journal.Count}개, 개척 {Count("【개척】")}, 빼앗은 땅 {Count("꺾고")}, 침공당함 {Count("【침공】")}, 막아냄 {Count("쳤으나 막아냈다")}, 빼앗긴 땅 {Count("쳐들어와")}, 도성 약탈 {Count("【도성 약탈】")}, 원정 패배 {Count("맞붙어 졌다")}, 곳간 {r.Treasury:F1}");
}
Console.WriteLine($"길: {string.Join(", ", Enum.GetValues<Course>().Take(4).Select(c => $"{Simulation.CourseName(c)} {sim.CourseLevel(c)}"))}, 공포 {r.Dread:F0}"
    + (sim.Won.Count > 0 ? $" · 승리: {string.Join(", ", sim.Won.Select(w => $"{Simulation.Victories[(int)w.Course].Name}({w.Year}년)"))}" : " · 승리 없음"));
if (aim is { } goal) { var (needs, streak, hold) = sim.VictoryNeeds(goal); Console.WriteLine($"{Simulation.Victories[(int)goal].Name}: {string.Join(", ", needs.Select(n => (n.ok ? "✓" : "✗") + n.what))}, 내리 {streak}/{hold}년"); }
if (r.Falls.Count > 0) Console.WriteLine("몰락: " + string.Join(", ", r.Falls.Select(f => f.Replace("|", "년 "))));
if (sim.Rivals.Count > 0) Console.WriteLine("이웃 나라: " + string.Join(", ", sim.Rivals.Select(v => $"{v.Name}(힘 {v.Strength:F1}, 적의 {v.Hostility:P0}{(v.Trade ? ", 교역" : "")}{(v.Allied ? ", 동맹" : "")})")));
Console.WriteLine("계층: " + string.Join(", ", Enumerable.Range(0, 4).Select(k => $"{Catalog.EstateNames[k]} {r.Share[k]:P0}(분노 {sim.EstateAnger((Estate)k):P0})")));
Console.WriteLine($"살아남은 사람 {sim.Living.Count()}명 (최대 {sim.PeakPopulation}명), 태어난 사람 {sim.TotalBirths}명");
Console.WriteLine("죽음·이탈: " + string.Join(", ", sim.DeathsByCause.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
return 0;
