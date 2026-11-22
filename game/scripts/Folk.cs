using Godot;
using Instinct.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InstinctGame;

/// <summary>
/// 도성 일족 사람들. 화면의 한 사람은 시뮬레이션의 한 사람(<see cref="Person.Id"/>)이고, 맡은 일(<see cref="Person.Job"/>)을 따른다:
/// 플레이어가 일을 바꾸거나 건물을 놓으면 즉시 반응해 머리 위에 일 아이콘 말풍선을 띄우고 새 일터로 달려간다.
/// 일터(열매 덤불·사냥감·물가·숲·돌·밭·공사장·안개 경계·초소) 둘레에 겹치지 않게 자리 잡아 제 몸짓으로 일하고,
/// 거둔 것을 앞/어깨에 지고 창고(또는 벌목장·채석장)로 날라 내려놓는다(그때 "+n"이 뜬다, <see cref="Main.Deliver"/>).
/// 그림은 tools/blender/people.py가 블렌더에서 찍은 몸 일곱 가지의 장(units/people_*.png, people.json).
/// </summary>
public partial class ActorLayer
{
    // ───────── 그림 장 ─────────

    sealed class Sheet
    {
        public Texture2D Tex = null!;
        public Dictionary<string, (int Row, int N)> Rows = new();
    }

    static Dictionary<string, Sheet>? _sheets;
    static int _frame = 128, _ground = 108, _cols = 8;
    static readonly string[] DirNames = { "S", "SE", "E", "NE", "N" };
    /// <summary>사람 키(화면 단위). 틀 한 장에서 몸이 차지하는 높이는 약 72/128.</summary>
    public const float FolkHeight = 27f;

    static Sheet? SheetOf(string body)
    {
        if (_sheets == null)
        {
            _sheets = new();
            const string meta = "res://assets/units/people.json";
            if (Godot.FileAccess.FileExists(meta))
            {
                var root = Json.ParseString(Godot.FileAccess.GetFileAsString(meta)).AsGodotDictionary();
                _frame = (int)root["frame"];
                _ground = (int)root["ground"];
                _cols = (int)root["frames"];
                foreach (var (name, rowsV) in root["bodies"].AsGodotDictionary())
                {
                    string file = $"res://assets/units/people_{name}.png";
                    if (!Godot.FileAccess.FileExists(file)) continue;
                    var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(file));
                    if (img == null) continue;
                    img.GenerateMipmaps();
                    var sh = new Sheet { Tex = ImageTexture.CreateFromImage(img) };
                    foreach (var (key, v) in rowsV.AsGodotDictionary())
                    {
                        var a = v.AsGodotArray();
                        sh.Rows[(string)key] = ((int)a[0], (int)a[1]);
                    }
                    _sheets[(string)name] = sh;
                }
            }
        }
        return _sheets.TryGetValue(body, out var s) ? s : null;
    }

    /// <summary>
    /// 사람 하나를 그린다. anim은 idle·walk·carry_food·chop…, face는 바라보는 화면 방향,
    /// t는 몸짓의 진행(1이 한 바퀴). 발밑이 pos. 그림이 없으면 false.
    /// </summary>
    public static bool DrawFolk(CanvasItem c, string body, string anim, Vector2 face, float t, Vector2 pos, float height = FolkHeight, Color? tint = null)
    {
        var sh = SheetOf(body);
        if (sh == null) return false;
        if (face.LengthSquared() < 1e-6f) face = new Vector2(0, 1);
        float deg = Mathf.RadToDeg(Mathf.Atan2(face.X, face.Y)); // 0 = 아래(S), 90 = 오른쪽(E)
        int idx = (int)Mathf.Round(deg / 45f);
        bool flip = idx < 0;
        idx = Math.Min(4, Math.Abs(idx));
        if (!sh.Rows.TryGetValue($"{anim}_{DirNames[idx]}", out var row))
        {
            // 일 동작은 SE·E·NE 세 방향, 놀이 동작은 S·SE·E 세 방향이 있다: 가장 가까운 방향에 맞춰 자연스럽게 고른다.
            int near = idx == 0 ? 1 : idx == 4 ? 3 : idx;
            bool ok = sh.Rows.TryGetValue($"{anim}_{DirNames[near]}", out row)
                      || sh.Rows.TryGetValue($"{anim}_E", out row)
                      || sh.Rows.TryGetValue($"{anim}_SE", out row);
            if (!ok)
            {
                if (!sh.Rows.TryGetValue($"idle_{DirNames[idx]}", out row)) return false;
            }
            else flip = face.X < 0;
        }
        int f = row.N <= 1 ? 0 : (int)Mathf.PosMod(Mathf.Floor(t * row.N), row.N);
        float size = height * _frame / 72f;
        float k = size / _frame;
        var dst = new Rect2(pos.X - size / 2, pos.Y - _ground * k, size, size);
        if (flip) dst = new Rect2(dst.Position.X + size, dst.Position.Y, -size, size);
        c.DrawTextureRectRegion(sh.Tex, dst, new Rect2(f * _frame, row.Row * _frame, _frame, _frame), tint ?? Colors.White);
        return true;
    }

    // ───────── 사람과 현장 효과 ─────────

    enum Mode { Idle, Going, Working, Hauling }

    sealed class Folk
    {
        public int Id;
        public string Name = "";
        public string Body = "man";
        public float Scale = 1f;
        public Vector2 Pos, Vel, Face = new(0, 1), TargetFace = new(0, 1), Spot, Look;
        public readonly List<Vector2> Path = new();
        public int Next;
        public Work Job = (Work)(-1);
        public bool Child;
        public Mode Mode;
        public float Timer, Anim;
        public string Carry = "";
        public string Act = "idle";
        public int WorkCell = -1;
        public int CycleCount;
        public string BubbleIcon = "";
        public float BubbleTimer;
    }

    sealed class WorkFx
    {
        public Vector2 Start, End, Pos, Vel;
        public float Age, Life;
        public Color Color;
        public string Kind = "chip"; // "arrow", "chip", "ripple"
    }

    readonly Dictionary<int, Folk> _folk = new();
    readonly List<WorkFx> _workFx = new();
    float _folkSync;
    string _unfinishedSig = "";
    int _hoverFolkId = -1;
    static readonly StyleBoxFlat _pillBox = MakePillBox();

    static StyleBoxFlat MakePillBox()
    {
        var sb = new StyleBoxFlat { BgColor = new Color("102229", 0.92f), BorderColor = new Color(Ui.Rim, 0.85f), AntiAliasing = true };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(10);
        return sb;
    }

    static int Hash(int a, int b) => (int)(Art.Hash(a, b, 77) * 100000);

    static bool Passable(Simulation s, int cell) =>
        cell >= 0 && cell < s.Map.Tile.Length && s.Map.Seen[cell] && s.Map.Tile[cell] is not (Ground.Deep or Ground.Peak);

    static float Cost(Ground g) => g switch
    {
        Ground.Shallows => 5f, Ground.Marsh => 2.5f, Ground.Woods => 1.5f, Ground.Hill => 1.7f, _ => 1f,
    };

    static bool OursOrFish(Simulation s, int cell)
    {
        if (cell < 0 || cell >= s.Map.Tile.Length || !s.Map.Seen[cell]) return false;
        if (s.Map.Cell[cell] >= 0 && s.Map[s.Map.Cell[cell]].Owner == 0) return true;
        if (s.Map.Feat[cell] != Feature.Fish) return false;
        int w = WorldMap.Width, x = cell % w, y = cell / w;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= WorldMap.Height) continue;
                int j = ny * w + nx;
                if (s.Map.Cell[j] >= 0 && s.Map[s.Map.Cell[j]].Owner == 0) return true;
            }
        return false;
    }

    /// <summary>칸 사이의 길(A*, 여덟 방향, 물·봉우리·안개는 돌아간다). 못 찾으면 곧장.</summary>
    static List<int> FindPath(Simulation s, int from, int to)
    {
        int w = WorldMap.Width;
        var result = new List<int>();
        if (from == to || !Passable(s, to)) { result.Add(to); return result; }
        var open = new PriorityQueue<int, float>();
        var g = new Dictionary<int, float> { [from] = 0 };
        var came = new Dictionary<int, int>();
        float H(int c) => MathF.Sqrt(MathF.Pow(c % w - to % w, 2) + MathF.Pow(c / w - to / w, 2));
        open.Enqueue(from, H(from));
        int seen = 0;
        while (open.Count > 0 && seen++ < 5000)
        {
            int c = open.Dequeue();
            if (c == to) break;
            int cx = c % w, cy = c / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= WorldMap.Height) continue;
                    int n = y * w + x;
                    if (!Passable(s, n)) continue;
                    if (dx != 0 && dy != 0 && (!Passable(s, cy * w + x) || !Passable(s, y * w + cx))) continue;
                    float ng = g[c] + Cost(s.Map.Tile[n]) * (dx != 0 && dy != 0 ? 1.414f : 1f);
                    if (g.TryGetValue(n, out var old) && old <= ng) continue;
                    g[n] = ng;
                    came[n] = c;
                    open.Enqueue(n, ng + H(n));
                }
        }
        if (!came.ContainsKey(to)) { result.Add(to); return result; }
        for (int c = to; c != from; c = came[c]) result.Add(c);
        result.Reverse();
        return result;
    }

    /// <summary>터에서 가까운 우리 땅(또는 물고기 물가) 가운데 조건에 맞는 것(가까운 순, 같으면 번호 순).</summary>
    static List<int> Near(Simulation s, Func<int, bool> ok, int reach = 14)
    {
        int w = WorldMap.Width, cx = s.CampCell % w, cy = s.CampCell / w;
        var list = new List<(int c, int d)>();
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= w || y >= WorldMap.Height) continue;
                int c = y * w + x;
                if (OursOrFish(s, c) && ok(c)) list.Add((c, dx * dx + dy * dy));
            }
        return list.OrderBy(v => v.d).ThenBy(v => v.c).Select(v => v.c).ToList();
    }

    static bool NearAny(int cell, List<int> anchors, int maxDistSq)
    {
        if (anchors.Count == 0) return false;
        int w = WorldMap.Width, x = cell % w, y = cell / w;
        foreach (int a in anchors)
        {
            int dx = x - a % w, dy = y - a / w;
            if (dx * dx + dy * dy <= maxDistSq) return true;
        }
        return false;
    }

    List<int> Built(StructureKind k) => Game.S.Structures.Where(x => x.Kind == k && x.Built).OrderBy(x => x.Id).Select(x => x.Cell).ToList();

    /// <summary>일마다의 일터 칸들(가까운 것부터)과 그 일의 몸짓·지고 오는 짐.</summary>
    (List<int> cells, string act, string carry) Sites(Work job)
    {
        var s = Game.S;
        List<int> Or(List<int> a, Func<List<int>> b) => a.Count > 0 ? a : b();
        switch (job)
        {
            case Work.Gather:
            {
                var berries = Near(s, c => s.Map.Feat[c] == Feature.Berries);
                if (berries.Count > 0) return (berries, "gather", "food");
                var wild = Near(s, c => c != s.CampCell && s.Map.Tile[c] is Ground.Woods or Ground.Grass, 7);
                return (Or(wild, () => Built(StructureKind.Hut)), "gather", "food");
            }
            case Work.Hunt:
            {
                var lodges = Built(StructureKind.Lodge);
                var game = Near(s, c => s.Map.Feat[c] == Feature.Game);
                if (game.Count > 0)
                {
                    if (lodges.Count > 0) game = game.OrderBy(c => NearAny(c, lodges, 16) ? 0 : 1).ToList();
                    return (game, "bow", "food");
                }
                return (lodges, "bow", "food");
            }
            case Work.Fish:
                return (Or(Built(StructureKind.Weir), () => Near(s, c => s.Map.Feat[c] == Feature.Fish)), "fish", "food");
            case Work.Farm:
            {
                if (s.Season == 3) return (new List<int>(), "idle", ""); // 겨울에는 밭을 쉬고 마을 안에 머문다
                return (Or(Built(StructureKind.Field), () => Near(s, c => s.Map.Feat[c] == Feature.Fertile)), "hoe", "food");
            }
            case Work.Wood:
            {
                var lumbers = Built(StructureKind.Lumber);
                var woods = Near(s, c => s.Map.Tile[c] == Ground.Woods);
                if (woods.Count > 0)
                {
                    if (lumbers.Count > 0) woods = woods.OrderBy(c => NearAny(c, lumbers, 9) ? 0 : 1).ToList();
                    return (woods, "chop", "wood");
                }
                return (lumbers, "chop", "wood");
            }
            case Work.Stone:
            {
                var quarries = Built(StructureKind.Quarry);
                var stones = Near(s, c => s.Map.Feat[c] == Feature.Stone);
                if (stones.Count > 0)
                {
                    if (quarries.Count > 0) stones = stones.OrderBy(c => NearAny(c, quarries, 9) ? 0 : 1).ToList();
                    return (stones, "pick", "stone");
                }
                return (Or(quarries, () => Near(s, c => s.Map.Tile[c] == Ground.Hill)), "pick", "stone");
            }
            case Work.Build:
                return (s.Structures.Where(x => !x.Built).OrderBy(x => x.Id).Select(x => x.Cell).ToList(), "hammer", "");
            case Work.Scout:
                return (s.FogEdge(), "scout", "");
            case Work.Guard:
            {
                var posts = new List<int>();
                posts.AddRange(Built(StructureKind.Tower));
                posts.AddRange(Built(StructureKind.Palisade));
                posts.AddRange(Built(StructureKind.WarHut));
                int w = WorldMap.Width, cx = s.CampCell % w, cy = s.CampCell / w;
                foreach (var (dx, dy) in new[] { (0, -3), (3, 0), (0, 3), (-3, 0) })
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= WorldMap.Height) continue;
                    int c = ny * w + nx;
                    if (Passable(s, c) && OursOrFish(s, c) && !posts.Contains(c)) posts.Add(c);
                }
                return (posts, "guard", "");
            }
            default:
                return (new List<int>(), "idle", "");
        }
    }

    /// <summary>일터 둘레의 겹치지 않는 작업 자리와 바라볼 곳을 고른다.</summary>
    (Vector2 spot, Vector2 look) StandBy(int cell, Work job, int slotRank, int salt)
    {
        var s = Game.S;
        var center = Cells.At(cell);
        int w = WorldMap.Width;
        if (!Passable(s, cell))
        {
            var adj = new List<int>();
            var camp = Cells.At(s.CampCell);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cell % w + dx, y = cell / w + dy;
                    if (x < 0 || y < 0 || x >= w || y >= WorldMap.Height) continue;
                    int n = y * w + x;
                    if (Passable(s, n)) adj.Add(n);
                }
            if (adj.Count == 0) return (center, center);
            adj = adj.OrderBy(n => Cells.At(n).DistanceTo(camp)).ThenBy(n => n).ToList();
            int best = adj[slotRank % adj.Count];
            float tang = (slotRank / adj.Count - 0.5f) * 12f + (Art.Hash(salt, 1, 5) - 0.5f) * 8f;
            var dir = (center - Cells.At(best)).Normalized();
            var ortho = dir.Orthogonal();
            var edge = Cells.At(best).Lerp(center, 0.44f) + ortho * tang;
            return (edge, center);
        }

        if (job == Work.Scout)
        {
            int cx = cell % w, cy = cell / w;
            var fogDir = Vector2.Zero;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx >= 0 && ny >= 0 && nx < w && ny < WorldMap.Height && !s.Map.Seen[ny * w + nx])
                    fogDir += new Vector2(nx - cx, ny - cy);
            }
            if (fogDir.LengthSquared() < 0.01f) fogDir = (center - Cells.At(s.CampCell)).Normalized();
            var spot = center + new Vector2((Art.Hash(salt, 1, 5) - 0.5f) * 14, (Art.Hash(salt, 2, 5) - 0.5f) * 10);
            return (spot, spot + fogDir.Normalized() * 48);
        }

        if (job == Work.Guard)
        {
            float ga = slotRank * 2.39996f + Art.Hash(cell, 4, 21) * Mathf.Tau;
            var spot = center + new Vector2(Mathf.Cos(ga) * 18, Mathf.Sin(ga) * 14);
            if (!Passable(s, Cells.CellAt(spot))) spot = center;
            var outDir = center - Cells.At(s.CampCell);
            if (outDir.LengthSquared() < 1) outDir = new Vector2(0, 1);
            return (spot, spot + outDir.Normalized() * 48);
        }

        var (rx, ry) = job switch
        {
            Work.Hunt => (52f, 38f),
            Work.Farm => (18f, 13f),
            _ => (25f, 18f),
        };
        float ang = slotRank * 2.39996f + Art.Hash(cell, 3, 19) * Mathf.Tau;
        for (int tries = 0; tries < 8; tries++)
        {
            float a = ang + tries * (Mathf.Tau / 8f);
            float scale = tries < 4 ? 1f : 0.65f;
            var cand = center + new Vector2(Mathf.Cos(a) * rx * scale, Mathf.Sin(a) * ry * scale);
            if (Passable(s, Cells.CellAt(cand))) return (cand, center);
        }
        return (center + new Vector2((Art.Hash(salt, 1, 5) - 0.5f) * 18, (Art.Hash(salt, 2, 5) - 0.5f) * 14), center);
    }

    Vector2 Store(string carry)
    {
        var s = Game.S;
        if (carry == "wood")
        {
            var lum = s.Structures.Where(x => x.Kind == StructureKind.Lumber && x.Built).OrderBy(x => x.Id).FirstOrDefault();
            if (lum != null) return Cells.At(lum.Cell) + new Vector2(0, 12);
        }
        if (carry == "stone")
        {
            var q = s.Structures.Where(x => x.Kind == StructureKind.Quarry && x.Built).OrderBy(x => x.Id).FirstOrDefault();
            if (q != null) return Cells.At(q.Cell) + new Vector2(0, 12);
        }
        var st = s.Structures.Where(x => x.Kind == StructureKind.Storehouse && x.Built).OrderBy(x => x.Id).FirstOrDefault()
                 ?? (carry == "food" ? s.Structures.Where(x => x.Kind == StructureKind.Granary && x.Built).OrderBy(x => x.Id).FirstOrDefault() : null);
        return st != null ? Cells.At(st.Cell) + new Vector2(0, 14) : Cells.At(s.CampCell) + new Vector2(0, s.Tribal ? 40 : 58);
    }

    void Route(Folk f, Vector2 to)
    {
        var s = Game.S;
        f.Path.Clear();
        f.Next = 0;
        var cells = FindPath(s, Cells.CellAt(f.Pos), Cells.CellAt(to));
        float ox = (Art.Hash(f.Id, 7, 1) - 0.5f) * 20, oy = (Art.Hash(f.Id, 8, 1) - 0.5f) * 14;
        for (int i = 0; i < cells.Count - 1; i++) f.Path.Add(Cells.At(cells[i]) + new Vector2(ox, oy));
        f.Path.Add(to);
        if (f.Path.Count > 0)
        {
            var d = f.Path[0] - f.Pos;
            if (d.LengthSquared() > 4) f.TargetFace = d.Normalized();
        }
    }

    /// <summary>맡은 일에 맞춰 다음 할 일을 정한다: 일터로 가거나, 일이 없으면 터 둘레를 거닌다.</summary>
    void Plan(Folk f, bool immediate = false)
    {
        var s = Game.S;
        f.Carry = "";
        f.CycleCount = 0;
        var (cells, act, _) = f.Child ? (new List<int>(), "idle", "") : Sites(f.Job);
        if (cells.Count == 0)
        {
            bool play = f.Child && _rnd.NextDouble() < 0.35;
            if (play)
            {
                f.Mode = Mode.Idle;
                f.Act = "play";
                f.Path.Clear();
                f.Next = 0;
                f.Timer = 2.5f + 2f * (float)_rnd.NextDouble();
                f.WorkCell = -1;
                return;
            }
            float r = (f.Child ? 62 : 84) + 28 * (float)_rnd.NextDouble();
            float ang = (float)_rnd.NextDouble() * Mathf.Tau;
            var to = Cells.At(s.CampCell) + new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * 0.7f + 14);
            if (!Passable(s, Cells.CellAt(to))) to = Cells.At(s.CampCell) + new Vector2(0, s.Tribal ? 30 : 58);
            f.Mode = Mode.Idle;
            f.Act = "idle";
            f.WorkCell = -1;
            Route(f, to);
            if (immediate) f.Face = f.TargetFace;
            return;
        }
        var mates = _folk.Values.Where(o => o.Job == f.Job && !o.Child).Select(o => o.Id).OrderBy(i => i).ToList();
        int rank = Math.Max(0, mates.IndexOf(f.Id));
        int pick;
        if (f.Job == Work.Build)
        {
            // 첫 번째 공사장에 최대 4명까지 먼저 모이고, 넘으면 다음 공사장으로 나뉜다
            pick = Math.Min(cells.Count - 1, rank / 4);
        }
        else if (f.Job == Work.Scout)
        {
            pick = Hash(f.Id, (int)_time + 1) % Math.Min(cells.Count, 24);
        }
        else
        {
            int spread = Math.Min(cells.Count, 8);
            pick = rank % Math.Max(1, spread);
        }
        int cell = cells[Math.Min(pick, cells.Count - 1)];
        int cellRank = _folk.Values.Count(o => o.Id < f.Id && o.Job == f.Job && o.WorkCell == cell);
        var (spot, look) = StandBy(cell, f.Job, cellRank, f.Id * 31 + rank);
        f.WorkCell = cell;
        f.Spot = spot;
        f.Look = look;
        f.Act = act;
        f.Mode = Mode.Going;
        Route(f, spot);
        if (immediate) f.Face = f.TargetFace;
    }

    static (string body, float scale) BodyFor(Simulation s, Person p, Work job)
    {
        double age = p.Age(s.Tick);
        if (age < 10) return ("child", 1f);
        if (p == s.Chief) return ("chief", 1f);
        if (job == Work.Guard) return ("soldier", 1f);
        float sc = age < 14 ? 0.85f : 1f;
        bool alt = ((uint)p.Id * 2654435761u >> 7) % 2 == 0;
        string body = p.IsMale ? (job == Work.Hunt || alt ? "hunter" : "man") : (alt ? "woman" : "woman2");
        return (body, sc);
    }

    string UnfinishedSignature(Simulation s) =>
        string.Join(",", s.Structures.Where(x => !x.Built).OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Cell}"));

    /// <summary>플레이어가 일을 바꾸거나 건물을 놓은 직후: 기다리지 않고 곧장 반영한다.</summary>
    public void OnPlayerCommand()
    {
        if (!Game.S.Living.Any()) return;
        SyncFolk(0f, playerTriggered: true);
        QueueRedraw();
    }

    void SyncFolk(float dt, bool playerTriggered = false)
    {
        var s = Game.S;
        _folkSync -= dt;
        if (!playerTriggered && _folkSync > 0 && _folk.Count > 0) return;
        _folkSync = 0.25f;

        string unfinSig = UnfinishedSignature(s);
        bool sitesChanged = unfinSig != _unfinishedSig;
        _unfinishedSig = unfinSig;

        int maxFolk = s.Tribal ? 48 : 20;
        var living = s.Living.OrderBy(p => p.Id).ToList();
        if (living.Count > maxFolk)
        {
            var keep = new List<Person>();
            for (int i = 0; i < maxFolk; i++) keep.Add(living[i * living.Count / maxFolk]);
            living = keep;
        }
        var ids = living.Select(p => p.Id).ToHashSet();
        foreach (var id in _folk.Keys.Where(id => !ids.Contains(id)).ToList()) _folk.Remove(id);

        var camp = Cells.At(s.CampCell);
        bool hadAny = _folk.Count > 0;
        var changed = new List<Folk>();
        foreach (var p in living)
        {
            bool child = p.Age(s.Tick) < 10;
            var job = child ? Work.None : p.Job == Work.None ? Work.Gather : p.Job;
            bool created = false;
            if (!_folk.TryGetValue(p.Id, out var f))
            {
                float a = Art.Hash(p.Id, 3, 3) * Mathf.Tau;
                float spawnR = s.Tribal ? 36f : 64f;
                f = new Folk { Id = p.Id, Pos = camp + new Vector2(Mathf.Cos(a) * spawnR, Mathf.Sin(a) * spawnR * 0.68f + (s.Tribal ? 18f : 26f)), Anim = Art.Hash(p.Id, 4, 4) };
                _folk[p.Id] = f;
                created = true;
            }
            f.Name = p.Name;
            var (body, scale) = BodyFor(s, p, job);
            f.Body = body;
            f.Scale = scale;
            if (f.Job != job || f.Child != child)
            {
                bool jobSwitched = hadAny && !created && f.Job != job && !child;
                f.Job = job;
                f.Child = child;
                changed.Add(f);
                if (jobSwitched && playerTriggered)
                {
                    f.BubbleIcon = Hud.WorkIcon(job);
                    f.BubbleTimer = 1.6f;
                }
            }
            else if (sitesChanged && !child && job == Work.Build)
            {
                bool targetDone = f.WorkCell < 0 || !s.Structures.Any(x => !x.Built && x.Cell == f.WorkCell);
                if (f.Mode == Mode.Idle || targetDone)
                {
                    changed.Add(f);
                    if (playerTriggered && s.Structures.Any(x => !x.Built))
                    {
                        f.BubbleIcon = "build";
                        f.BubbleTimer = 1.6f;
                    }
                }
            }
        }
        foreach (var f in changed)
        {
            Plan(f, immediate: playerTriggered || !hadAny);
            if (!hadAny) ScatterInitial(f);
        }
    }

    void ScatterInitial(Folk f)
    {
        if (f.Mode == Mode.Going && f.Path.Count > 0)
        {
            float u = Art.Hash(f.Id, 11, 7);
            if (u < 0.5f)
            {
                f.Pos = f.Spot;
                Arrive(f);
                f.Anim = Art.Hash(f.Id, 12, 7);
                f.Face = f.TargetFace;
            }
            else if (u < 0.8f)
            {
                int idx = Math.Clamp((int)(Art.Hash(f.Id, 13, 7) * f.Path.Count), 0, f.Path.Count - 1);
                var p0 = idx == 0 ? f.Pos : f.Path[idx - 1];
                f.Pos = p0.Lerp(f.Path[idx], 0.5f);
                f.Next = idx;
                var d = f.Path[idx] - f.Pos;
                if (d.LengthSquared() > 1) f.Face = f.TargetFace = d.Normalized();
            }
            else
            {
                var (_, _, carry) = Sites(f.Job);
                if (carry.Length > 0)
                {
                    f.Pos = f.Spot;
                    f.Carry = carry;
                    f.Mode = Mode.Hauling;
                    Route(f, Store(carry) + new Vector2((Art.Hash(f.Id, 9, 9) - 0.5f) * 26, (Art.Hash(f.Id, 10, 9) - 0.5f) * 10));
                    if (f.Path.Count > 0)
                    {
                        int hi = Math.Min(f.Path.Count - 1, f.Path.Count / 3);
                        var p0 = hi == 0 ? f.Spot : f.Path[hi - 1];
                        f.Pos = p0.Lerp(f.Path[hi], 0.5f);
                        f.Next = hi;
                        var d = f.Path[hi] - f.Pos;
                        if (d.LengthSquared() > 1) f.Face = f.TargetFace = d.Normalized();
                    }
                }
            }
        }
        else if (f.Mode == Mode.Idle && f.Path.Count > 0)
        {
            f.Pos = f.Path[^1];
            f.Path.Clear();
            f.Next = 0;
        }
    }

    void TriggerWorkFx(Folk f, float prevPhase, float curPhase)
    {
        bool Crossed(float mark) => prevPhase < mark && curPhase >= mark;
        switch (f.Act)
        {
            case "bow":
                if (Crossed(0.56f))
                {
                    var from = f.Pos + new Vector2(0, -14 * f.Scale);
                    var to = f.Look + new Vector2((float)(_rnd.NextDouble() - 0.5) * 10, -8 + (float)(_rnd.NextDouble() - 0.5) * 6);
                    _workFx.Add(new WorkFx { Kind = "arrow", Start = from, End = to, Pos = from, Life = 0.22f, Color = new Color("f0e2c0") });
                }
                break;
            case "fish":
                if (Crossed(0.54f))
                    _workFx.Add(new WorkFx { Kind = "ripple", Pos = f.Pos.Lerp(f.Look, 0.72f), Life = 0.75f, Color = new Color("bfe8f2") });
                break;
            case "chop":
            case "pick":
            case "hoe":
                if (Crossed(0.52f)) SpawnChips(f, f.Act == "chop" ? new Color("d6b27c") : f.Act == "pick" ? new Color("cfd6dc") : new Color("8c6b46"), 3);
                break;
            case "hammer":
                if (Crossed(0.28f) || Crossed(0.60f)) SpawnChips(f, new Color("e2c792"), 2);
                break;
        }
    }

    void SpawnChips(Folk f, Color col, int n)
    {
        var origin = f.Pos.Lerp(f.Look, 0.45f) + new Vector2(0, -6);
        for (int i = 0; i < n; i++)
        {
            float a = (float)(_rnd.NextDouble() * Mathf.Tau);
            float sp = 18f + (float)_rnd.NextDouble() * 20f;
            _workFx.Add(new WorkFx
            {
                Kind = "chip", Pos = origin, Vel = new Vector2(Mathf.Cos(a) * sp, -22f - (float)_rnd.NextDouble() * 16f),
                Life = 0.35f, Color = col,
            });
        }
    }

    void StepFolk(float dt)
    {
        var s = Game.S;
        if (!s.Living.Any()) { _folk.Clear(); _workFx.Clear(); return; }
        SyncFolk(dt);
        UpdateHoverFolk();

        // 말풍선과 현장 이펙트는 일시정지 중에도 부드럽게 가라앉는다
        foreach (var f in _folk.Values)
            if (f.BubbleTimer > 0) f.BubbleTimer = Math.Max(0, f.BubbleTimer - dt);
        for (int i = _workFx.Count - 1; i >= 0; i--)
        {
            var fx = _workFx[i];
            fx.Age += dt;
            if (fx.Age >= fx.Life) { _workFx.RemoveAt(i); continue; }
            if (fx.Kind == "chip")
            {
                fx.Vel.Y += 120f * dt;
                fx.Pos += fx.Vel * dt;
            }
            else if (fx.Kind == "arrow")
            {
                float u = fx.Age / fx.Life;
                fx.Pos = fx.Start.Lerp(fx.End, u) + new Vector2(0, -14f * 4f * u * (1f - u));
            }
        }

        if (Game.Speed == 0) return;
        float pace = Math.Min(2.6f, 0.75f + 0.45f * Game.Speed);
        float step = dt * pace;
        var list = _folk.Values.OrderBy(f => f.Id).ToList();
        foreach (var f in list)
        {
            // 방향 전환을 부드럽게 보간해 각도 깜빡임을 막는다
            if (f.TargetFace.LengthSquared() > 0.01f)
            {
                var nextF = f.Face.Lerp(f.TargetFace, 1 - Mathf.Exp(-10f * dt));
                if (nextF.LengthSquared() > 0.01f) f.Face = nextF.Normalized();
            }

            switch (f.Mode)
            {
                case Mode.Working:
                {
                    f.Timer -= step;
                    float rate = f.Act == "gather" ? 1.35f : f.Act is "scout" or "guard" ? 1.6f : 1.05f;
                    float prev = Mathf.PosMod(f.Anim, 1f);
                    f.Anim += step / rate;
                    float cur = Mathf.PosMod(f.Anim, 1f);
                    if (cur < prev) f.CycleCount++;
                    TriggerWorkFx(f, prev, cur);
                    f.Vel = Vector2.Zero;
                    var lookDir = f.Look - f.Pos;
                    if (lookDir.LengthSquared() > 1) f.TargetFace = lookDir.Normalized();

                    // 농사는 2회 괭이질마다 옆 이랑으로 한 걸음 옮긴다
                    if (f.Job == Work.Farm && f.CycleCount >= 2 && f.Timer > 1.2f && f.WorkCell >= 0)
                    {
                        f.CycleCount = 0;
                        var center = Cells.At(f.WorkCell);
                        var nextSpot = center + new Vector2(((float)_rnd.NextDouble() - 0.5f) * 32, ((float)_rnd.NextDouble() - 0.5f) * 22);
                        if (Passable(s, Cells.CellAt(nextSpot)))
                        {
                            f.Spot = nextSpot;
                            f.Look = center;
                            f.Mode = Mode.Going;
                            Route(f, nextSpot);
                            continue;
                        }
                    }

                    if (f.Timer <= 0)
                    {
                        var (_, _, carry) = Sites(f.Job);
                        if (carry.Length > 0)
                        {
                            f.Carry = carry;
                            f.Mode = Mode.Hauling;
                            Route(f, Store(carry) + new Vector2((Art.Hash(f.Id, 9, 9) - 0.5f) * 26, (Art.Hash(f.Id, 10, 9) - 0.5f) * 10));
                        }
                        else Plan(f);
                    }
                    continue;
                }
                case Mode.Idle when f.Next >= f.Path.Count:
                    f.Timer -= step;
                    if (f.Act == "play") f.Anim += step * 1.3f;
                    else f.Anim += step * 0.45f; // 서 있을 때도 잔잔히 숨을 쉰다
                    f.Vel = Vector2.Zero;
                    if (f.Timer <= 0) Plan(f);
                    continue;
            }
            if (f.Next >= f.Path.Count) { Arrive(f); continue; }
            var target = f.Path[f.Next];
            var to = target - f.Pos;
            float d = to.Length();
            bool last = f.Next == f.Path.Count - 1;
            if (d < (last ? 3f : 20f)) { f.Next++; if (f.Next >= f.Path.Count) Arrive(f); continue; }
            float speed = (f.Child ? 20f : f.Carry.Length > 0 ? 22f : f.Job == Work.Scout ? 33f : 27f) * (0.9f + 0.2f * Art.Hash(f.Id, 5, 5));
            if (last) speed *= Mathf.Clamp(d / 18f, 0.35f, 1f);
            var want = to / d * speed;
            // 마주 오는 사람과 겹치지 않게 밀어내고 살짝 비켜 걷는다
            foreach (var o in list)
            {
                if (o == f) continue;
                var away = f.Pos - o.Pos;
                float dd = away.Length();
                if (dd > 0.01f && dd < 16f)
                {
                    var dir = away / dd;
                    want += dir * (16f - dd) * 2.6f;
                    if (dd < 11f) want += dir.Orthogonal() * (11f - dd) * 1.4f;
                }
            }
            f.Vel = f.Vel.Lerp(want, 1 - Mathf.Exp(-7f * dt));
            f.Pos += f.Vel * step;
            if (f.Vel.LengthSquared() > 4) f.TargetFace = f.Vel.Normalized();
            f.Anim += f.Vel.Length() * step / 24f;
        }
    }

    void Arrive(Folk f)
    {
        f.Path.Clear();
        f.Next = 0;
        f.Vel = Vector2.Zero;
        switch (f.Mode)
        {
            case Mode.Going:
                f.Mode = Mode.Working;
                f.CycleCount = 0;
                f.Timer = f.Job == Work.Scout ? 1.8f + 1.5f * (float)_rnd.NextDouble()
                    : f.Job == Work.Guard ? 3.5f + 2f * (float)_rnd.NextDouble()
                    : 4.5f + 3f * (float)_rnd.NextDouble();
                var look = f.Look - f.Pos;
                f.TargetFace = look.LengthSquared() > 1 ? look.Normalized() : new Vector2(1, 0);
                f.Anim = 0;
                break;
            case Mode.Hauling:
                Game.Deliver(f.Carry, f.Pos);
                f.Carry = "";
                Plan(f);
                break;
            default:
                f.Mode = Mode.Idle;
                f.Timer = 1f + 3f * (float)_rnd.NextDouble();
                break;
        }
    }

    void UpdateHoverFolk()
    {
        _hoverFolkId = -1;
        if (Game.Placing != null || Game.Hud.ModalOpen || Game.Cam.Zoom.X < 0.75f) return;
        if (GetViewport().GuiGetHoveredControl() != null) return;
        var vpMouse = GetViewport().GetMousePosition();
        if (vpMouse.X <= 1 || vpMouse.Y <= 1 || !GetViewportRect().HasPoint(vpMouse)) return;
        var world = Game.ToWorld(vpMouse);
        var f = FolkNear(world, 18f);
        if (f != null) _hoverFolkId = f.Id;
    }

    Folk? FolkNear(Vector2 world, float reach)
    {
        Folk? best = null;
        float bestD = reach;
        foreach (var f in _folk.Values)
        {
            float d = f.Pos.DistanceTo(world + new Vector2(0, 11 * f.Scale));
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    /// <summary>그 자리 가까이에 있는 주민 Id(확대 클릭용).</summary>
    public int FolkIdNear(Vector2 world, float reach = 14f) => FolkNear(world, reach)?.Id ?? -1;

    string FolkStatusText(Folk f)
    {
        if (f.Child) return f.Act == "play" ? "뛰노는 중" : "마을 거니는 중";
        if (f.Mode == Mode.Hauling) return f.Carry switch
        {
            "wood" => "통나무 나르는 중",
            "stone" => "돌 나르는 중",
            _ => "식량 나르는 중",
        };
        if (f.Mode == Mode.Working) return f.Job switch
        {
            Work.Gather => "채집 중",
            Work.Hunt => "사냥 중",
            Work.Fish => "고기잡이 중",
            Work.Farm => "밭일 중",
            Work.Wood => "벌목 중",
            Work.Stone => "채석 중",
            Work.Build => "건설 중",
            Work.Scout => "정찰 중",
            Work.Guard => "경계 중",
            _ => "일하는 중",
        };
        if (f.Mode == Mode.Going) return $"{WorkCatalog.Name(f.Job)} 일터로 이동 중";
        return f.Job == Work.Farm && Game.S.Season == 3 ? "겨울 휴식 중" : "쉬는 중";
    }

    void DrawFolkAll()
    {
        var s = Game.S;
        foreach (var f in _folk.Values.OrderBy(f => f.Pos.Y).ThenBy(f => f.Id))
        {
            if (f.Id == _hoverFolkId || f.BubbleTimer > 0)
            {
                float a = f.Id == _hoverFolkId ? 0.85f : Math.Clamp(f.BubbleTimer, 0, 1) * 0.75f;
                DrawArc(f.Pos + new Vector2(0, -1), 11 * f.Scale, 0, Mathf.Tau, 24, new Color(Ui.Accent, a), 1.8f, true);
            }
            bool walking = f.Mode != Mode.Working && f.Path.Count > 0 && f.Vel.LengthSquared() > 4;
            string anim = f.Mode == Mode.Working ? f.Act
                : walking ? (f.Carry.Length > 0 ? "carry_" + f.Carry : "walk")
                : f.Act == "play" ? "play" : "idle";
            float t = f.Anim;
            if (!DrawFolk(this, f.Body, anim, f.Face, t, f.Pos, FolkHeight * f.Scale))
                Art.Walker(this, f.Pos, t * Mathf.Tau, f.Face.X >= 0 ? 1 : -1, Tunics[f.Id % Tunics.Length], 0, walking && Game.Speed > 0, f.Scale);
        }

        // 현장 이펙트(화살·나무/돌 조각·물결)
        foreach (var fx in _workFx)
        {
            float u = fx.Age / fx.Life;
            if (fx.Kind == "arrow")
            {
                var dir = (fx.End - fx.Start).Normalized();
                var tail = fx.Pos - dir * 10;
                DrawLine(tail, fx.Pos, Art.Ink, 2.6f, true);
                DrawLine(tail, fx.Pos, fx.Color, 1.4f, true);
            }
            else if (fx.Kind == "ripple")
            {
                DrawArc(fx.Pos, 4 + 14 * u, 0, Mathf.Tau, 20, new Color(fx.Color, (1 - u) * 0.7f), 1.6f, true);
            }
            else
            {
                DrawCircle(fx.Pos, 1.8f * (1 - u * 0.5f), new Color(fx.Color, 1 - u));
            }
        }

        // 배정 말풍선
        foreach (var f in _folk.Values)
        {
            if (f.BubbleTimer <= 0 || f.BubbleIcon.Length == 0) continue;
            float a = Math.Clamp(f.BubbleTimer * 2.5f, 0, 1);
            float pop = f.BubbleTimer > 1.4f ? (1.6f - f.BubbleTimer) / 0.2f : 1f;
            float lift = Mathf.Sin(Math.Min(1f, (1.6f - f.BubbleTimer) * 3.5f) * Mathf.Pi) * 4f;
            var center = f.Pos + new Vector2(0, -34 * f.Scale - lift);
            var box = new Rect2(center - new Vector2(12, 12), new Vector2(24, 24));
            _pillBox.BgColor = new Color("102229", 0.92f * a);
            _pillBox.BorderColor = new Color(Ui.Accent, 0.9f * a);
            DrawStyleBox(_pillBox, box);
            Ui.Icon(this, f.BubbleIcon, new Rect2(center - new Vector2(8.5f * pop, 8.5f * pop), new Vector2(17 * pop, 17 * pop)), new Color(1, 1, 1, a));
        }

        // 호버 주민 상태표
        if (_hoverFolkId >= 0 && _folk.TryGetValue(_hoverFolkId, out var hf))
        {
            string label = $"{hf.Name} · {FolkStatusText(hf)}";
            const int fs = 13;
            float tw = Art.Font.GetStringSize(label, HorizontalAlignment.Left, -1, fs).X;
            bool hasIcon = !hf.Child && hf.Job != Work.None;
            float icoW = hasIcon ? 18f : 0f;
            float w = tw + icoW + 16f;
            var at = hf.Pos + new Vector2(-w / 2, -48 * hf.Scale);
            var box = new Rect2(at, new Vector2(w, 22));
            _pillBox.BgColor = new Color("102229", 0.94f);
            _pillBox.BorderColor = new Color(Ui.Rim, 0.85f);
            DrawStyleBox(_pillBox, box);
            if (hasIcon) Ui.Icon(this, Hud.WorkIcon(hf.Job), new Rect2(at + new Vector2(6, 3), new Vector2(16, 16)), Colors.White);
            DrawString(Art.Font, new Vector2(at.X + 7 + icoW, at.Y + 16), label, HorizontalAlignment.Left, -1, fs, Ui.Text);
        }
    }
}
