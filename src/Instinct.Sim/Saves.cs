using System.Globalization;
using System.Text;

namespace Instinct.Sim;

/// <summary>밖에서 내린 명령 하나(언제, 무엇을, 무엇에). 이것만 모아 두면 판 전체를 다시 만들 수 있다.</summary>
public readonly record struct Command(int Tick, string Kind, string A, string B);

// 명령 기록: 시뮬레이션은 씨앗과 명령만으로 정해진다. 저장은 상태를 통째로 쓰지 않고 씨앗과 명령을 쓴다.
public sealed partial class Simulation
{
    /// <summary>이 판의 씨앗.</summary>
    public int Seed { get; }

    /// <summary>밖에서 내려 받아들여진 명령들(짓기·길·군대·결정·제도·연구·무리의 일·적어 둔 말), 내린 순서대로.</summary>
    public readonly List<Command> Journal = new();

    /// <summary>0보다 크면 적지 않는다: 한 철이 도는 중이거나, 명령 안에서 불린 명령이다.</summary>
    int _mute;

    void Wrote(string kind, string a, string b = "")
    {
        if (_mute == 0) Journal.Add(new Command(Tick, kind, a, b));
    }

    /// <summary>적어 둔 명령 하나를 다시 내린다. 알 수 없는 명령이거나 받아들여지지 않으면 false.</summary>
    internal bool Replay(Command c)
    {
        _mute++;
        try
        {
            int A() => int.Parse(c.A, CultureInfo.InvariantCulture);
            int B() => int.Parse(c.B, CultureInfo.InvariantCulture);
            switch (c.Kind)
            {
                case "build": return BuildingCatalog.Find(c.B) is { } bd && Build(A(), bd.Kind);
                case "road": return BuildRoad(A());
                case "raise": return RaiseArmy(A()) != null;
                case "disband": return DisbandArmy(A());
                case "order": return OrderArmy(A(), B());
                case "decide": return Decide(A(), B());
                case "enact": return Catalog.Find(c.A) is { } def && Enact(def, c.B == "1");
                case "research": return SetResearch(c.A);
                case "tribe": return TribeAct(c.A, B());
                case "note": Note(c.A); return true;
                case "job": return SetJob(A(), (Work)B());
                case "assign": return Assign((Work)A(), B());
                case "steward": return AppointSteward(A(), (StewardPolicy)B());
                case "place": return Place((StructureKind)A(), B());
                case "raze": return Raze(A());
                case "envoy": return SendEnvoy(A(), c.B);
                default: return false;
            }
        }
        finally { _mute--; }
    }

    /// <summary>판의 지문. 불러온 판이 저장한 판과 같은지 견준다.</summary>
    public string Fingerprint() =>
        string.Create(CultureInfo.InvariantCulture, $"{Log.Count}|{People.Count}|{Living.Count()}|{Realm.Pop:F3}|{Realm.Treasury:F3}|{Realm.Territory}|{KnownCount}");
}

/// <summary>저장 파일의 머리말(목록에 보여 줄 것).</summary>
public sealed record SaveInfo(int Seed, int Tick, bool PlayerRules, string Label, Difficulty Difficulty = Difficulty.Normal);

/// <summary>
/// 저장과 불러오기. 저장 파일은 씨앗·흐른 철·명령 기록을 담은 글이다. 불러올 때는 같은 씨앗으로 판을 새로 열어
/// 명령을 그 철에 다시 내리며 그때까지 돌린다. 규칙이 바뀐 판본에서 만든 저장은 지문이 어긋난다(<see cref="Drifted"/>).
/// </summary>
public static class SaveGame
{
    const string Magic = "INSTINCT-SAVE 1";

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "");

    static string Unesc(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            char n = s[++i];
            sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n);
        }
        return sb.ToString();
    }

    public static string Write(Simulation sim, string label = "")
    {
        var sb = new StringBuilder();
        sb.Append(Magic).Append('\n');
        sb.Append("seed\t").Append(sim.Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("rules\t").Append(sim.PlayerRules ? "1" : "0").Append('\n');
        if (sim.Proposals) sb.Append("proposals\t1\n");
        sb.Append("diff\t").Append(Simulation.DifficultyKey(sim.Difficulty)).Append('\n');
        sb.Append("tick\t").Append(sim.Tick.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("label\t").Append(Esc(label)).Append('\n');
        sb.Append("check\t").Append(sim.Fingerprint()).Append('\n');
        foreach (var c in sim.Journal)
            sb.Append("cmd\t").Append(c.Tick.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(Esc(c.Kind)).Append('\t').Append(Esc(c.A)).Append('\t').Append(Esc(c.B)).Append('\n');
        return sb.ToString();
    }

    sealed class Parsed
    {
        public int Seed, Tick;
        public bool Rules, Proposals;
        public Difficulty Diff = Difficulty.Normal; // 난이도가 생기기 전의 저장은 보통
        public string Label = "", Check = "";
        public readonly List<Command> Commands = new();
    }

    static Parsed Parse(string text, bool commands)
    {
        var lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != Magic) throw new FormatException("저장 파일이 아니다.");
        var p = new Parsed();
        bool seed = false, tick = false;
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            switch (f[0])
            {
                case "seed" when f.Length >= 2: p.Seed = int.Parse(f[1], CultureInfo.InvariantCulture); seed = true; break;
                case "tick" when f.Length >= 2: p.Tick = int.Parse(f[1], CultureInfo.InvariantCulture); tick = true; break;
                case "rules" when f.Length >= 2: p.Rules = f[1] == "1"; break;
                case "proposals" when f.Length >= 2: p.Proposals = f[1] == "1"; break;
                case "diff" when f.Length >= 2: p.Diff = Simulation.ParseDifficulty(f[1]) ?? throw new FormatException("모르는 난이도."); break;
                case "label" when f.Length >= 2: p.Label = Unesc(f[1]); break;
                case "check" when f.Length >= 2: p.Check = f[1]; break;
                case "cmd" when f.Length >= 5:
                    if (commands) p.Commands.Add(new Command(int.Parse(f[1], CultureInfo.InvariantCulture), Unesc(f[2]), Unesc(f[3]), Unesc(f[4])));
                    break;
            }
        }
        if (!seed || !tick || p.Tick < 0) throw new FormatException("저장 파일의 머리말이 깨졌다.");
        return p;
    }

    /// <summary>머리말만 읽는다(저장 목록용). 저장 파일이 아니면 null.</summary>
    public static SaveInfo? Peek(string text)
    {
        try
        {
            var p = Parse(text, commands: false);
            return new SaveInfo(p.Seed, p.Tick, p.Rules, p.Label, p.Diff);
        }
        catch (Exception e) when (e is FormatException or OverflowException) { return null; }
    }

    /// <summary>불러온 판이 저장한 때와 어긋났는가(다른 판본의 규칙으로 만든 저장).</summary>
    public static bool Drifted(Simulation sim, string text) => Parse(text, commands: false).Check is { Length: > 0 } c && c != sim.Fingerprint();

    /// <summary>저장한 판을 다시 만든다. 글이 저장 파일이 아니면 <see cref="FormatException"/>.</summary>
    public static Simulation Read(string text)
    {
        var p = Parse(text, commands: true);
        var sim = new Simulation(p.Seed) { PlayerRules = p.Rules, Proposals = p.Proposals, Difficulty = p.Diff };
        int next = 0;
        var cmds = p.Commands;
        while (true)
        {
            while (next < cmds.Count && cmds[next].Tick <= sim.Tick)
            {
                var c = cmds[next++];
                // 받아들여지지 않는 명령(다른 판본의 저장)도 기록에는 남겨 둔다: 다시 저장해도 잃지 않는다.
                sim.Replay(c);
                sim.Journal.Add(c);
            }
            if (sim.Tick >= p.Tick || !sim.Living.Any()) break;
            sim.Step();
        }
        return sim;
    }
}
