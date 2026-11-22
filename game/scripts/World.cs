using Godot;
using Instinct.Sim;

namespace InstinctGame;

/// <summary>
/// 땅. 시뮬레이션의 48×30칸을 칸마다 32점으로 잘게 나누고, 경계를 잡음으로 휘고 흐려 손으로 그린 지도처럼 만든다.
/// 한 판에 한 번만 그린다.
/// </summary>
public partial class TerrainLayer : Node2D
{
    public const int PX = 32;
    public const int W = WorldMap.Width * PX, H = WorldMap.Height * PX;
    public static readonly Vector2 WorldSize = new(WorldMap.Width * Art.Cell, WorldMap.Height * Art.Cell);

    /// <summary>점마다의 지방 Id(바다 −1)와 땅결.</summary>
    public int[] PixProv = new int[W * H];
    public Ground[] PixGround = new Ground[W * H];

    WorldMap _map = null!;
    int _seed;
    ImageTexture? _tex;
    readonly List<(Vector2 pos, int kind, float s, float tone)> _decor = new();
    readonly List<Vector2[]> _rivers = new();
    ArrayMesh? _mesh, _spriteMesh;

    public int ProvinceAt(Vector2 world)
    {
        int x = (int)(world.X / Art.Cell * PX), y = (int)(world.Y / Art.Cell * PX);
        return x < 0 || y < 0 || x >= W || y >= H ? -1 : PixProv[y * W + x];
    }

    public Ground GroundAt(Vector2 world)
    {
        int x = (int)(world.X / Art.Cell * PX), y = (int)(world.Y / Art.Cell * PX);
        return x < 0 || y < 0 || x >= W || y >= H ? Ground.Deep : PixGround[y * W + x];
    }

    public static Vector2 Center(Province p) => new((p.X + 0.5f) * Art.Cell, (p.Y + 0.5f) * Art.Cell);

    public void Build(WorldMap map, int seed)
    {
        _map = map;
        _seed = seed;
        var elev = new float[W * H];
        var mask = new float[W * H];
        var cr = new float[W * H];
        var cg = new float[W * H];
        var cb = new float[W * H];

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // 경계는 잡음으로 휘되, 점마다 흩뜨리지는 않는다(흩뜨리면 확대했을 때 톱니가 된다).
                float u = (x + 0.5f) / PX, v = (y + 0.5f) / PX;
                float wu = u + (Art.Fbm(u * 0.9f, v * 0.9f, seed + 11) - 0.5f) * 1.1f + (Art.Noise(u * 3.3f, v * 3.3f, seed + 31) - 0.5f) * 0.42f;
                float wv = v + (Art.Fbm(u * 0.9f, v * 0.9f, seed + 23) - 0.5f) * 1.1f + (Art.Noise(u * 3.3f, v * 3.3f, seed + 37) - 0.5f) * 0.42f;
                int cx = Mathf.FloorToInt(wu), cy = Mathf.FloorToInt(wv);
                int i = y * W + x;
                if (cx < 0 || cy < 0 || cx >= WorldMap.Width || cy >= WorldMap.Height)
                {
                    PixProv[i] = -1;
                    PixGround[i] = Ground.Deep;
                }
                else
                {
                    int cell = cy * WorldMap.Width + cx;
                    PixProv[i] = map.Cell[cell];
                    PixGround[i] = map.Tile[cell];
                    if (PixProv[i] < 0 && PixGround[i] > Ground.Shallows) PixGround[i] = Ground.Shallows;
                    elev[i] = (float)map.Elevation[cell];
                }
                mask[i] = PixProv[i] >= 0 ? 1 : 0;
                var g0 = Art.GroundColors[(int)PixGround[i]];
                cr[i] = g0.R; cg[i] = g0.G; cb[i] = g0.B;
            }

        // 물가까지의 거리(땅은 +, 바다는 −). 흐려서 물가의 선이 점의 모서리를 따라 꺾이지 않게 한다.
        var sd = CoastDistance(mask);
        Art.Blur(sd, W, H, 2);
        Art.Blur(sd, W, H, 2);
        // 땅결의 빛깔도 흐려서 물감이 번지듯 넘어가게 한다.
        foreach (var ch in new[] { cr, cg, cb })
        {
            Art.Blur(ch, W, H, 3);
            Art.Blur(ch, W, H, 3);
        }
        // 높이를 흐려서 비탈의 그늘에 쓴다.
        Art.Blur(elev, W, H, 8);
        Art.Blur(elev, W, H, 8);

        var sand = Art.GroundColors[(int)Ground.Sand];
        var data = new byte[W * H * 3];
        var foam = new Color("d5e2d8");
        static float Band(float d, float at, float w) => 1 - Mathf.SmoothStep(0, w, Math.Abs(d - at));
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float d = sd[i];
                var col = new Color(cr[i], cg[i], cb[i]);
                float u = (x + 0.5f) / PX, v = (y + 0.5f) / PX;
                float wash = Art.Fbm(u * 1.7f, v * 1.7f, seed + 41) - 0.5f;   // 물감이 번진 얼룩
                float grain = Art.Hash(x, y, seed + 77) - 0.5f;               // 종이의 결
                col = wash > 0 ? col.Lightened(wash * 0.22f) : col.Darkened(-wash * 0.2f);
                col = grain > 0 ? col.Lightened(grain * 0.035f) : col.Darkened(-grain * 0.035f);

                if (d > 0)
                {
                    int xa = Math.Max(0, x - 6), ya = Math.Max(0, y - 6), xb = Math.Min(W - 1, x + 6), yb = Math.Min(H - 1, y + 6);
                    float slope = (elev[ya * W + xa] - elev[yb * W + xb]) * 9f; // 빛은 왼쪽 위에서 온다
                    col = slope > 0 ? col.Darkened(Math.Min(0.28f, slope)) : col.Lightened(Math.Min(0.2f, -slope));
                    col = col.Lerp(sand, 0.8f * (1 - Mathf.SmoothStep(2f, 13f, d)));
                }
                else
                {
                    float w = -d;
                    float f = 0.8f * (1 - Mathf.SmoothStep(1.5f, 11f, w)) + 0.3f * Band(w, 20, 2.2f) + 0.16f * Band(w, 32, 2.2f); // 거품과 물결선
                    col = col.Lerp(foam, Math.Min(0.85f, f));
                }
                col = col.Lerp(Art.Ink, 0.62f * (1 - Mathf.SmoothStep(0.5f, 2.4f, Math.Abs(d - 0.6f))));

                data[i * 3] = (byte)(Mathf.Clamp(col.R, 0, 1) * 255);
                data[i * 3 + 1] = (byte)(Mathf.Clamp(col.G, 0, 1) * 255);
                data[i * 3 + 2] = (byte)(Mathf.Clamp(col.B, 0, 1) * 255);
            }
        _tex = ImageTexture.CreateFromImage(Image.CreateFromData(W, H, false, Image.Format.Rgb8, data));

        // 강
        _rivers.Clear();
        foreach (var river in map.Rivers)
        {
            var raw = river.Select(c => new Vector2(
                (c % WorldMap.Width + 0.5f + (Art.Hash(c, 1, seed) - 0.5f) * 0.5f) * Art.Cell,
                (c / WorldMap.Width + 0.5f + (Art.Hash(c, 2, seed) - 0.5f) * 0.5f) * Art.Cell)).ToList();
            var pts = new List<Vector2>();
            for (int k = 0; k < raw.Count - 1; k++)
            {
                var p0 = raw[Math.Max(0, k - 1)];
                var p1 = raw[k];
                var p2 = raw[k + 1];
                var p3 = raw[Math.Min(raw.Count - 1, k + 2)];
                for (int t = 0; t < 6; t++) pts.Add(p1.CubicInterpolate(p2, p0, p3, t / 6f));
            }
            pts.Add(raw[^1]);
            _rivers.Add(pts.ToArray());
        }

        // 나무·봉우리·풀: 흩뿌린 점마다 그 자리의 땅결을 보고 놓는다.
        _decor.Clear();
        var centers = map.Provinces.Select(Center).ToList();
        const float step = 21f;
        for (int gy = 0; gy * step < WorldSize.Y; gy++)
            for (int gx = 0; gx * step < WorldSize.X; gx++)
            {
                var pos = new Vector2((gx + Art.Hash(gx, gy, seed + 101)) * step, (gy + Art.Hash(gx, gy, seed + 102)) * step);
                var g = GroundAt(pos);
                if (g <= Ground.Sand) continue;
                if (centers.Any(c => c.DistanceSquaredTo(pos) < 70 * 70)) continue;
                float h = Art.Hash(gx, gy, seed + 103), tone = Art.Hash(gx, gy, seed + 104);
                float s = 0.75f + Art.Hash(gx, gy, seed + 105) * 0.5f;
                int kind = g switch
                {
                    Ground.Woods => h < 0.8f ? (GroundAt(pos + new Vector2(0, -40)) >= Ground.Hill && tone < 0.7f ? 1 : tone < 0.3f ? 1 : 0) : -1,
                    Ground.Peak => h < 0.2f ? 2 : h < 0.3f ? 3 : -1,
                    Ground.Hill => h < 0.22f ? 3 : h < 0.3f ? 0 : -1,
                    Ground.Grass => h < 0.008f ? 0 : h < 0.14f ? 4 : -1, // 풀밭에는 나무를 거의 두지 않는다(밭 자리가 숲처럼 보이지 않게)
                    Ground.Dry => h < 0.12f ? 4 : -1,
                    Ground.Marsh => h < 0.4f ? 5 : -1,
                    _ => -1,
                };
                if (kind >= 0) _decor.Add((pos, kind, s, tone));
            }
        _decor.Sort((a, b) => a.pos.Y.CompareTo(b.pos.Y));
        BakeDecor();
        QueueRedraw();
    }

    /// <summary>물가까지의 거리(점 단위). 땅은 +, 바다는 −.</summary>
    static float[] CoastDistance(float[] mask)
    {
        const float far = 1e6f, diag = 1.41421f;
        var d = new float[W * H];
        // 물가에 닿은 점에서 시작해 두 번 훑는다(앞으로, 뒤로).
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                bool land = mask[i] > 0.5f;
                bool shore = (x > 0 && mask[i - 1] > 0.5f != land) || (x < W - 1 && mask[i + 1] > 0.5f != land)
                             || (y > 0 && mask[i - W] > 0.5f != land) || (y < H - 1 && mask[i + W] > 0.5f != land);
                d[i] = shore ? 0.5f : far;
            }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float m = d[i];
                if (x > 0) m = Math.Min(m, d[i - 1] + 1);
                if (y > 0)
                {
                    m = Math.Min(m, d[i - W] + 1);
                    if (x > 0) m = Math.Min(m, d[i - W - 1] + diag);
                    if (x < W - 1) m = Math.Min(m, d[i - W + 1] + diag);
                }
                d[i] = m;
            }
        for (int y = H - 1; y >= 0; y--)
            for (int x = W - 1; x >= 0; x--)
            {
                int i = y * W + x;
                float m = d[i];
                if (x < W - 1) m = Math.Min(m, d[i + 1] + 1);
                if (y < H - 1)
                {
                    m = Math.Min(m, d[i + W] + 1);
                    if (x < W - 1) m = Math.Min(m, d[i + W + 1] + diag);
                    if (x > 0) m = Math.Min(m, d[i + W - 1] + diag);
                }
                d[i] = Math.Min(m, 200);
            }
        for (int i = 0; i < d.Length; i++) if (mask[i] <= 0.5f) d[i] = -d[i];
        return d;
    }

    /// <summary>강과 장식을 붓 묶음 하나로 모은다(한 판에 한 번). 그림 파일이 있는 장식은 아틀라스 메시 하나로 묶는다.</summary>
    void BakeDecor()
    {
        var pen = new Pen();
        var sVerts = new List<Vector2>(_decor.Count * 4);
        var sUvs = new List<Vector2>(_decor.Count * 4);
        var sIdx = new List<int>(_decor.Count * 6);
        var inkWater = new Color(Art.Ink, 0.75f);
        var water = new Color("9cc0bd");
        foreach (var pts in _rivers)
        {
            if (pts.Length < 2) continue;
            // 아래로 갈수록 넓어진다
            var lower = pts.Skip(pts.Length / 2).ToArray();
            pen.Stroke(pts, inkWater, 7f);
            pen.Stroke(lower, inkWater, 10f);
            pen.Stroke(pts, water, 4f);
            pen.Stroke(lower, water, 7f);
        }

        foreach (var (pos, kind, s, tone) in _decor)
        {
            float size = kind == 2 ? s * 1.1f : s;
            if (Art.DecorQuad(kind, size, tone, pos) is var (uv, rect))
            {
                int b = sVerts.Count;
                sVerts.Add(rect.Position);
                sVerts.Add(new Vector2(rect.End.X, rect.Position.Y));
                sVerts.Add(rect.End);
                sVerts.Add(new Vector2(rect.Position.X, rect.End.Y));
                sUvs.Add(uv.Position);
                sUvs.Add(new Vector2(uv.End.X, uv.Position.Y));
                sUvs.Add(uv.End);
                sUvs.Add(new Vector2(uv.Position.X, uv.End.Y));
                sIdx.Add(b); sIdx.Add(b + 1); sIdx.Add(b + 2);
                sIdx.Add(b); sIdx.Add(b + 2); sIdx.Add(b + 3);
                continue;
            }
            switch (kind)
            {
                case 0: Art.Tree(pen, pos, size, tone); break;
                case 1: Art.Pine(pen, pos, size, tone); break;
                case 2: Art.Peak(pen, pos, size, tone); break;
                case 3: Art.Hill(pen, pos, size, tone); break;
                case 4: Art.Tuft(pen, pos, size, new Color(Art.Ink, 0.35f)); break;
                case 5: Art.Tuft(pen, pos, size * 1.3f, new Color("5f7a52")); break;
            }
        }
        _mesh = pen.Build();
        if (sIdx.Count > 0)
        {
            var arr = new Godot.Collections.Array();
            arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = sVerts.ToArray();
            arr[(int)Mesh.ArrayType.TexUV] = sUvs.ToArray();
            arr[(int)Mesh.ArrayType.Index] = sIdx.ToArray();
            _spriteMesh = new ArrayMesh();
            _spriteMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        }
        else _spriteMesh = null;
    }

    public override void _Draw()
    {
        if (_tex == null) return;
        DrawTextureRect(_tex, new Rect2(Vector2.Zero, WorldSize), false);
        if (_mesh != null) DrawMesh(_mesh, null);
        if (_spriteMesh != null) DrawMesh(_spriteMesh, Art.DecorAtlas());
    }
}

/// <summary>사람이 땅에 남긴 것: 국경, 길, 마을과 도성, 건물, 공사, 자원, 이름. 상태가 바뀔 때만 다시 그린다.</summary>
public partial class OverlayLayer : Node2D
{
    public Main Game = null!;
    ImageTexture? _borders, _selection;
    Rect2 _selectionRect;
    string _ownersSig = "", _stateSig = "";
    int _selected = -2;
    Simulation? _sim;
    /// <summary>국경 그림은 무거워서(한 장에 0.1초쯤) 시간이 흐르는 중에는 뒤에서 만든다.</summary>
    Task<Image>? _borderJob;
    int[]? _borderAgain;

    public void Sync(bool force = false)
    {
        var s = Game.S;
        var ownerArr = s.Map.Provinces.Select(p => p.Owner).ToArray();
        string owners = string.Join(",", ownerArr);
        bool fresh = _sim != s; // 새 판이나 불러온 판: 땅 모양이 다르니 바로 만든다
        if (fresh || owners != _ownersSig)
        {
            _sim = s;
            _ownersSig = owners;
            if (fresh)
            {
                _borderJob = null;
                _borderAgain = null;
                _borders = ImageTexture.CreateFromImage(BorderImage(Game.Terrain.PixProv, ownerArr));
            }
            else if (_borderJob != null) _borderAgain = ownerArr;
            else StartBorders(ownerArr);
        }
        if (Game.Selected != _selected || force || fresh)
        {
            _selected = Game.Selected;
            _selection = _selected >= 0 ? SelectionTexture(_selected, out _selectionRect) : null;
        }
        string sig = owners + "|" + _selected + "|" + (int)s.Realm.Stage + (int)s.Realm.Era + "|" + s.Sites.Count + "|" + s.Tick / 2
                     + string.Join(",", s.Map.Provinces.Select(p => p.Buildings.Count + (p.Road ? 100 : 0)))
                     + "|" + Cells.CountSeen(s.Map.Seen) + "|" + string.Join(",", s.Structures.Select(x => x.Cell * 100 + (int)(x.Done / Math.Max(0.01, x.Work) * 20)));
        if (sig != _stateSig || force)
        {
            _stateSig = sig;
            QueueRedraw();
        }
    }

    void StartBorders(int[] owners)
    {
        var prov = Game.Terrain.PixProv;
        _borderJob = Task.Run(() => BorderImage(prov, owners));
    }

    /// <summary>뒤에서 만든 국경 그림이 다 되었으면 바꿔 단다.</summary>
    public override void _Process(double delta)
    {
        if (_borderJob is not { IsCompleted: true } job) return;
        _borderJob = null;
        if (job.IsCompletedSuccessfully) _borders = ImageTexture.CreateFromImage(job.Result);
        else GD.PushError($"국경 그림 실패: {job.Exception?.GetBaseException().Message}");
        QueueRedraw();
        if (_borderAgain is { } again)
        {
            _borderAgain = null;
            StartBorders(again);
        }
    }

    /// <summary>색과 짙기를 흐린 뒤 그림으로 만든다. 경계가 점의 모서리를 따라 꺾이지 않는다.</summary>
    static Image Soft(float[] r, float[] g, float[] b, float[] a, int W, int H)
    {
        // 짙기를 곱한 채로 흐려야 빈 곳의 빛깔이 번져 들어오지 않는다.
        for (int i = 0; i < a.Length; i++) { r[i] *= a[i]; g[i] *= a[i]; b[i] *= a[i]; }
        foreach (var ch in new[] { r, g, b, a })
        {
            Art.Blur(ch, W, H, 1);
            Art.Blur(ch, W, H, 1);
        }
        var data = new byte[W * H * 4];
        for (int i = 0; i < a.Length; i++)
        {
            float al = a[i];
            if (al < 0.004f) continue;
            data[i * 4] = (byte)(Mathf.Clamp(r[i] / al, 0, 1) * 255);
            data[i * 4 + 1] = (byte)(Mathf.Clamp(g[i] / al, 0, 1) * 255);
            data[i * 4 + 2] = (byte)(Mathf.Clamp(b[i] / al, 0, 1) * 255);
            data[i * 4 + 3] = (byte)(Mathf.Clamp(al, 0, 1) * 255);
        }
        return Image.CreateFromData(W, H, false, Image.Format.Rgba8, data);
    }

    /// <summary>국경 그림. 시뮬레이션을 읽지 않고(주인 표 owners만 쓴다) 뒤에서 돌릴 수 있다.</summary>
    static Image BorderImage(int[] prov, int[] owners)
    {
        const int W = TerrainLayer.W, H = TerrainLayer.H;
        var own = new int[W * H];
        for (int i = 0; i < own.Length; i++) own[i] = prov[i] < 0 ? -9 : owners[prov[i]];
        float[] cr = new float[W * H], cg = new float[W * H], cb = new float[W * H], ca = new float[W * H];
        int Owner(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? -9 : own[y * W + x];
        bool Other(int x, int y, int p)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return false;
            int q = prov[y * W + x];
            return q >= 0 && q != p;
        }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                int p = prov[i];
                if (p < 0) continue;
                int o = own[i];
                Color col;
                float a;
                bool inner = Other(x + 1, y, p) || Other(x + 2, y, p) || Other(x, y + 1, p) || Other(x, y + 2, p);
                if (o < 0)
                {
                    if (!inner) continue;
                    col = Art.Ink;
                    a = (x + y) % 10 < 6 ? 0.26f : 0; // 주인 없는 땅의 경계는 점선
                }
                else
                {
                    col = Art.OwnerColor(o);
                    bool edge = Owner(x + 4, y) != o || Owner(x - 4, y) != o || Owner(x, y + 4) != o || Owner(x, y - 4) != o
                                || Owner(x + 3, y + 3) != o || Owner(x - 3, y - 3) != o || Owner(x + 3, y - 3) != o || Owner(x - 3, y + 3) != o;
                    bool band = edge || Owner(x + 12, y) != o || Owner(x - 12, y) != o || Owner(x, y + 12) != o || Owner(x, y - 12) != o
                                || Owner(x + 8, y + 8) != o || Owner(x - 8, y - 8) != o || Owner(x + 8, y - 8) != o || Owner(x - 8, y + 8) != o;
                    a = edge ? 0.9f : band ? 0.42f : 0.1f;
                    if (!edge && inner) { col = Art.Ink; a = 0.24f; }
                }
                cr[i] = col.R; cg[i] = col.G; cb[i] = col.B; ca[i] = a;
            }
        return Soft(cr, cg, cb, ca, W, H);
    }

    /// <summary>고른 땅의 테두리. 그 땅을 둘러싼 네모 안만 만든다(rect: 세계 좌표).</summary>
    ImageTexture? SelectionTexture(int id, out Rect2 rect)
    {
        const int W = TerrainLayer.W, H = TerrainLayer.H, M = 8;
        var prov = Game.Terrain.PixProv;
        int x0 = W, y0 = H, x1 = -1, y1 = -1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (prov[y * W + x] == id)
                {
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
        rect = default;
        if (x1 < 0) return null;
        x0 = Math.Max(0, x0 - M); y0 = Math.Max(0, y0 - M); x1 = Math.Min(W - 1, x1 + M); y1 = Math.Min(H - 1, y1 + M);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        float[] cr = new float[w * h], cg = new float[w * h], cb = new float[w * h], ca = new float[w * h];
        bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && prov[y * W + x] == id;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (prov[y * W + x] != id) continue;
                int i = (y - y0) * w + (x - x0);
                bool edge = !In(x + 4, y) || !In(x - 4, y) || !In(x, y + 4) || !In(x, y - 4)
                            || !In(x + 3, y + 3) || !In(x - 3, y - 3) || !In(x + 3, y - 3) || !In(x - 3, y + 3);
                cr[i] = 1; cg[i] = 0.97f; cb[i] = 0.86f;
                ca[i] = edge ? 0.92f : 0.18f;
            }
        float k = Art.Cell / TerrainLayer.PX;
        rect = new Rect2(x0 * k, y0 * k, w * k, h * k);
        return ImageTexture.CreateFromImage(Soft(cr, cg, cb, ca, w, h));
    }

    /// <summary>지방 안에서 건물이 서는 자리.</summary>
    public static Vector2 Slot(Province p, int k)
    {
        float a = -Mathf.Pi * 0.85f + k * 0.62f;
        float r = 62 + (k % 2) * 16;
        return TerrainLayer.Center(p) + new Vector2(Mathf.Cos(a) * r * 1.25f, Mathf.Sin(a) * r * 0.8f);
    }

    public override void _Draw()
    {
        var s = Game.S;
        var map = s.Map;
        var world = new Rect2(Vector2.Zero, TerrainLayer.WorldSize);
        if (_borders != null) DrawTextureRect(_borders, world, false);
        if (_selection != null) DrawTextureRect(_selection, _selectionRect, false);
        Cells.DrawAll(this, s);

        // 길
        foreach (var p in map.Provinces.Where(p => p.Road && p.RoadTo >= 0 && (map.Known(p) || map.Known(map[p.RoadTo]))))
        {
            var pts = Art.RoadCurve(TerrainLayer.Center(map[p.RoadTo]), TerrainLayer.Center(p), p.Id);
            DrawPolyline(pts, new Color(Art.Ink, 0.8f), 7f, true);
            DrawPolyline(pts, new Color("dccb9c"), 4f, true);
        }
        foreach (var site in s.Sites.Where(x => x.Road))
        {
            var p = map[site.Province];
            if (!map.Known(p)) continue;
            var parent = p.Adjacent.Select(a => map[a]).Where(a => a.Owner == 0 && (a.Road || a.Id == map.Capital))
                .OrderBy(a => map.Distance(a, map[map.Capital])).ThenBy(a => a.Id).FirstOrDefault();
            if (parent == null) continue;
            var pts = Art.RoadCurve(TerrainLayer.Center(parent), TerrainLayer.Center(p), p.Id);
            float done = 1 - site.SeasonsLeft / (float)site.Total;
            for (int k = 0; k < pts.Length - 1; k += 2)
                DrawLine(pts[k], pts[k + 1], k / (float)pts.Length < done ? new Color("dccb9c") : new Color(Art.Ink, 0.45f), 3.5f, true);
        }

        var pops = s.LandPops();
        int era = (int)s.Realm.Era, stage = (int)s.Realm.Stage;
        foreach (var p in map.Provinces.OrderBy(p => p.Y).ThenBy(p => p.Id))
        {
            if (!map.Known(p)) continue;
            var c = TerrainLayer.Center(p);
            var rival = p.Owner > 0 ? s.Rivals.FirstOrDefault(v => v.Id == p.Owner) : null;

            if (p.Id == map.Capital) Art.Town(this, c, stage, era, Art.Ours);
            else if (rival != null && rival.Seat == p.Id) Art.Town(this, c, Math.Clamp(stage, 2, 3), era, Art.OwnerColor(p.Owner));
            else if (p.Owner >= 0)
            {
                int n = p.Owner == 0 ? Math.Clamp((int)(Math.Log10(pops[p.Id] + 10) * 1.6), 1, 7) : 3;
                for (int k = 0; k < n; k++)
                {
                    var off = new Vector2((Art.Hash(p.Id, k, 31) - 0.5f) * 70, (Art.Hash(p.Id, k, 32) - 0.5f) * 36);
                    Art.House(this, c + off, 1f, era, Art.Hash(p.Id, k, 33));
                }
            }

            for (int k = 0; k < p.Buildings.Count; k++) Art.BuildingIcon(this, p.Buildings[k], Slot(p, k), 1.25f);
            int slot = p.Buildings.Count;
            foreach (var site in s.Sites.Where(x => x.Province == p.Id && !x.Road))
                Art.Scaffold(this, Slot(p, slot++), 1.2f, 1 - site.SeasonsLeft / (float)site.Total);

            if (s.Revealed(p.Deposit))
            {
                var at = c + new Vector2(-50, 30);
                DrawCircle(at, 13, new Color("13282e", 0.92f));
                DrawArc(at, 13, 0, Mathf.Tau, 28, DepositColor(p.Deposit).Lightened(0.35f), 2f, true);
                Ui.Icon(this, Art.ResourceIcon(p.Deposit), new Rect2(at - new Vector2(10, 10), new Vector2(20, 20)), Colors.White);
            }

            bool seat = p.Id == map.Capital || (rival != null && rival.Seat == p.Id);
            string name = rival != null && rival.Seat == p.Id ? $"{rival.Name} · {p.Name}" : p.Name;
            // 우리 터의 이름은 사람들 그림 위에 오도록 ActorLayer가 그린다(CampLabel).
            if (p.Id != map.Capital)
                Art.Text(this, c + new Vector2(0, seat ? 52 : 40), name, seat ? 22 : 18, p.Owner >= 0 ? Art.OwnerColor(p.Owner).Darkened(0.5f) : new Color(Art.Ink, 0.9f), true, 6);
        }

        // 싸움이 있었던 자리
        foreach (var b in s.Battles.Where(b => s.Tick - b.Tick < 12 && map.Known(map[b.Province])))
        {
            var c = TerrainLayer.Center(map[b.Province]) + new Vector2(46, -30);
            var col = b.Won ? Art.Good : Art.Bad;
            DrawLine(c + new Vector2(-9, -9), c + new Vector2(9, 9), Art.Ink, 4, true);
            DrawLine(c + new Vector2(9, -9), c + new Vector2(-9, 9), Art.Ink, 4, true);
            DrawLine(c + new Vector2(-8, -8), c + new Vector2(8, 8), col, 2, true);
            DrawLine(c + new Vector2(8, -8), c + new Vector2(-8, 8), col, 2, true);
        }
    }

    static Color DepositColor(Instinct.Sim.Resource r) => r switch
    {
        Instinct.Sim.Resource.Livestock => new Color("9a6a3a"),
        Instinct.Sim.Resource.Copper => new Color("b5683a"),
        Instinct.Sim.Resource.Iron => new Color("6a6a72"),
        Instinct.Sim.Resource.Spice => new Color("a8452f"),
        Instinct.Sim.Resource.Coal => new Color("3a3632"),
        Instinct.Sim.Resource.Oil => new Color("2f2a3a"),
        _ => new Color("5f8a3a"),
    };
}

/// <summary>움직이는 것들: 걸어 다니는 백성, 길 위의 수레, 군대, 싸움의 먼지. 프레임마다 그린다.</summary>
public partial class ActorLayer : Node2D
{
    public Main Game = null!;

    sealed class Walker
    {
        public int Province, Kind, Dir = 1;
        public Vector2 Pos, Target;
        public float Phase, Wait, Speed;
        public Color Tunic;
    }

    sealed class Effect
    {
        public Vector2 Pos;
        public float Age, Life;
        public string Text = "";
        public Color Color;
        public bool Dust;
        public string Icon = "";
        public double Value;
    }

    static readonly StyleBoxFlat _gainBox = MakeGainBox();
    static StyleBoxFlat MakeGainBox()
    {
        var b = new StyleBoxFlat { BgColor = new Color(0.09f, 0.08f, 0.07f, 0.74f), BorderColor = new Color(1, 1, 1, 0.25f) };
        b.SetCornerRadiusAll(10);
        b.SetBorderWidthAll(1);
        return b;
    }

    /// <summary>
    /// 자원이 들고 날 때: 아이콘과 숫자가 그 자리에서 떠오른다.
    /// 막 뜬 같은 자원·같은 방향의 판이 가까이 있으면 새로 띄우지 않고 그 판의 숫자에 더한다(자원마다 한 판).
    /// 다른 판과 겹치면 위로 비켜 띄운다.
    /// </summary>
    public void Gain(Vector2 pos, string icon, double value, Color color)
    {
        const float life = 1.5f;
        foreach (var e in _effects)
        {
            if (e.Icon != icon || Math.Sign(e.Value) != Math.Sign(value) || e.Age > life * 0.5f || e.Pos.DistanceTo(pos) > 160) continue;
            e.Value += value;
            e.Text = GainText(e.Value);
            e.Age = Math.Min(e.Age, 0.15f);
            return;
        }
        var at = pos;
        for (int k = 0; k < 4 && _effects.Any(e => e.Icon.Length > 0 && e.Age < life * 0.7f && Math.Abs(e.Pos.X - at.X) < 70 && Math.Abs(e.Pos.Y - at.Y) < 30); k++)
            at += new Vector2(0, -32);
        _effects.Add(new Effect { Pos = at, Life = life, Text = GainText(value), Value = value, Color = color, Icon = icon });
    }

    static string GainText(double v) => v > 0 ? $"+{v:0}" : $"−{-v:0}";

    readonly List<Walker> _walkers = new();
    readonly Dictionary<int, Vector2> _armyPos = new();
    readonly Dictionary<int, int> _armyDir = new();
    readonly List<Effect> _effects = new();
    readonly Random _rnd = new(12345); // 화면만의 난수. 시뮬레이션과 섞지 않는다.
    string _sig = "";
    float _time, _simTime;

    static readonly Color[] Tunics = { new("b9a27a"), new("8a9a7a"), new("a8785a"), new("7a8a9a"), new("c2b48a"), new("9a7a8a") };

    public void Reset()
    {
        _walkers.Clear();
        _armyPos.Clear();
        _armyDir.Clear();
        _effects.Clear();
        _folk.Clear();
        _workFx.Clear();
        _unfinishedSig = "";
        _hoverFolkId = -1;
        _folkSync = 0;
        _sig = "";
        _simTime = 0;
    }

    public void Burst(Vector2 pos, string text, Color color) =>
        _effects.Add(new Effect { Pos = pos, Life = 3.2f, Text = text, Color = color, Dust = true });

    public void Float(Vector2 pos, string text, Color color) =>
        _effects.Add(new Effect { Pos = pos, Life = 2.6f, Text = text, Color = color });

    Vector2 Wander(Province p, float reach)
    {
        var c = TerrainLayer.Center(p);
        for (int k = 0; k < 8; k++)
        {
            var q = c + new Vector2((float)(_rnd.NextDouble() - 0.5) * 2 * reach, (float)(_rnd.NextDouble() - 0.5) * reach * 1.3f);
            if (Game.Terrain.ProvinceAt(q) == p.Id) return q;
        }
        return c + new Vector2((float)(_rnd.NextDouble() - 0.5) * 30, 20);
    }

    void SyncWalkers()
    {
        var s = Game.S;
        var pops = s.LandPops();
        var want = new Dictionary<int, int>();
        foreach (var p in s.Lands(0))
        {
            if (p.Id == s.Map.Capital) continue; // 본거지(수도) 주민은 Folk.cs가 전 시대에 걸쳐 직접 그린다
            int n = s.Realm.Stage == Stage.Tribe ? Math.Min(12, s.Living.Count()) : Math.Clamp((int)(Math.Log10(pops[p.Id] + 10) * 2.4), 2, 8);
            if (s.Season == 3) n = Math.Max(1, n / 2); // 겨울에는 집 안에 든다
            want[p.Id] = n;
        }
        string sig = string.Join(",", want.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + kv.Value));
        if (sig == _sig) return;
        _sig = sig;
        _walkers.RemoveAll(w => !want.ContainsKey(w.Province));
        foreach (var (id, n) in want)
        {
            var mine = _walkers.Where(w => w.Province == id).ToList();
            for (int k = n; k < mine.Count; k++) _walkers.Remove(mine[k]);
            var p = s.Map[id];
            for (int k = mine.Count; k < n; k++)
            {
                var pos = Wander(p, 90);
                _walkers.Add(new Walker
                {
                    Province = id, Pos = pos, Target = pos, Kind = _rnd.Next(3), Speed = 14 + (float)_rnd.NextDouble() * 10,
                    Tunic = Tunics[_rnd.Next(Tunics.Length)], Wait = (float)_rnd.NextDouble() * 3,
                });
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        var s = Game.S;
        SyncWalkers();
        StepFolk(dt);
        float pace = Game.Speed == 0 ? 0 : 0.7f + 0.35f * Game.Speed;
        _simTime += dt * pace;

        foreach (var w in _walkers)
        {
            if (w.Wait > 0) { w.Wait -= dt * pace; continue; }
            var to = w.Target - w.Pos;
            float d = to.Length();
            if (d < 2)
            {
                w.Wait = 0.5f + (float)_rnd.NextDouble() * 3.5f;
                w.Target = Wander(s.Map[w.Province], 110);
                continue;
            }
            w.Pos += to / d * w.Speed * pace * dt;
            w.Dir = to.X >= 0 ? 1 : -1;
            w.Phase += dt * pace * 9;
        }

        // 군대는 있는 지방의 가운데 옆으로 미끄러져 간다.
        foreach (var a in s.Armies)
        {
            var goal = ArmyHome(a);
            if (!_armyPos.TryGetValue(a.Id, out var pos)) pos = goal;
            var to = goal - pos;
            float d = to.Length();
            if (d > 1)
            {
                pos += to / d * Math.Min(d, Math.Max(60, d * 1.8f) * Math.Max(0.6f, pace) * dt);
                _armyDir[a.Id] = to.X >= 0 ? 1 : -1;
            }
            _armyPos[a.Id] = pos;
        }
        foreach (int id in _armyPos.Keys.Where(id => s.Armies.All(a => a.Id != id)).ToList()) _armyPos.Remove(id);

        foreach (var e in _effects) e.Age += dt;
        _effects.RemoveAll(e => e.Age >= e.Life);
        QueueRedraw();
    }

    Vector2 ArmyHome(Army a)
    {
        var p = Game.S.Map[a.At];
        return TerrainLayer.Center(p) + (a.Owner == 0 ? new Vector2(-58, 44) : new Vector2(58, 44)) + new Vector2(0, (a.Id % 3) * 14);
    }

    public Vector2 ArmyPos(Army a) => _armyPos.TryGetValue(a.Id, out var p) ? p : ArmyHome(a);

    /// <summary>그 자리 가까이에 있는 우리 군단.</summary>
    public Army? ArmyNear(Vector2 world, float reach = 42)
    {
        return Game.S.Armies.Where(a => ArmyPos(a).DistanceTo(world + new Vector2(0, 12)) < reach)
            .OrderBy(a => a.Owner == 0 ? 0 : 1).ThenBy(a => ArmyPos(a).DistanceTo(world)).FirstOrDefault();
    }

    public override void _Draw()
    {
        var s = Game.S;
        var map = s.Map;

        // 길 위의 수레
        foreach (var p in map.Provinces)
        {
            if (!p.Road || p.RoadTo < 0 || (!map.Known(p) && !map.Known(map[p.RoadTo]))) continue;
            var pts = Art.RoadCurve(TerrainLayer.Center(map[p.RoadTo]), TerrainLayer.Center(p), p.Id);
            float t = (_simTime * 0.045f + Art.Hash(p.Id, 9, 9) * 2f) % 2f;
            bool back = t > 1;
            float u = back ? 2 - t : t;
            var at = Art.Along(pts, 0.12f + u * 0.76f);
            var ahead = Art.Along(pts, 0.12f + Mathf.Clamp(u + (back ? -0.05f : 0.05f), 0, 1) * 0.76f);
            Art.Cart(this, at, _simTime * 6, ahead.X >= at.X ? 1 : -1);
        }

        foreach (var w in _walkers.OrderBy(w => w.Pos.Y))
            Art.Walker(this, w.Pos, w.Wait <= 0 || Game.Speed == 0 ? w.Phase : _time * 1.6f + w.Tunic.R * 6f, w.Dir, w.Tunic, w.Kind, w.Wait <= 0 && Game.Speed > 0);
        DrawFolkAll();
        // 우리 터(도성)의 이름: 걸어 다니는 사람들에 가리지 않게 그들 위에 그린다.
        var cap = map[map.Capital];
        Art.Text(this, TerrainLayer.Center(cap) + new Vector2(0, 52), cap.Name, 22, cap.Owner >= 0 ? Art.OwnerColor(cap.Owner).Darkened(0.5f) : new Color(Art.Ink, 0.9f), true, 7);

        // 고른 군단의 갈 길
        if (Game.SelectedArmy is { } sel && s.Armies.Contains(sel))
        {
            var at = ArmyPos(sel);
            DrawArc(at + new Vector2(0, -4), 30, 0, Mathf.Tau, 32, new Color("fff3c8"), 3, true);
            if (sel.Goal >= 0)
            {
                var route = s.Route(sel.At, sel.Goal);
                var prev = at;
                foreach (int id in route)
                {
                    var next = TerrainLayer.Center(map[id]) + new Vector2(-58, 44);
                    DrawDashedLine(prev, next, new Color("fff3c8"), 3, 10);
                    prev = next;
                }
                DrawCircle(prev, 6, new Color("fff3c8"));
            }
        }

        foreach (var a in s.Armies.OrderBy(a => ArmyPos(a).Y))
        {
            if (a.Owner != 0 && !map.Known(map[a.At])) continue;
            var pos = ArmyPos(a);
            bool moving = pos.DistanceTo(ArmyHome(a)) > 2;
            int dir = _armyDir.GetValueOrDefault(a.Id, a.Owner == 0 ? 1 : -1);
            var col = a.Owner == 0 ? Art.Ours : Art.OwnerColor(a.Owner);
            string armyTex = a.Owner == 0 ? "units/army_own.png" : "units/army_enemy.png";
            if (!Art.Sprite(this, armyTex, pos + new Vector2(0, 10), 58))
            {
                int n = Math.Clamp((int)Math.Ceiling(a.Men / Math.Max(1, a.FullMen) * 6), 2, 6);
                Art.Flag(this, pos + new Vector2(-dir * 16, -20), col, _time * 4 + a.Id, 1.3f);
                for (int k = 0; k < n; k++)
                {
                    var off = new Vector2((k % 3 - 1) * 11 + (k / 3) * 4, (k / 3) * 9 - 4);
                    Art.Walker(this, pos + off, _time * 9 + k, dir, col.Lightened(0.25f), 3, moving, 1.15f);
                }
            }
            // 병력 감소 막대(피해를 입었을 때만)
            if (a.FullMen > 0 && a.Men < a.FullMen * 0.98)
            {
                float ratio = (float)Math.Clamp(a.Men / a.FullMen, 0, 1);
                DrawRect(new Rect2(pos.X - 18, pos.Y + 11, 36, 4), new Color(0.08f, 0.08f, 0.08f, 0.78f));
                DrawRect(new Rect2(pos.X - 17, pos.Y + 12, 34 * ratio, 2), a.Owner == 0 ? Art.Good : Art.Bad);
            }
            Art.Text(this, pos + new Vector2(0, 26), $"{a.Name} {Art.Num(a.Men)}", 16, col.Darkened(0.4f), true, 5);
        }

        foreach (var e in _effects)
        {
            float t = e.Age / e.Life;
            if (e.Dust)
            {
                for (int k = 0; k < 7; k++)
                {
                    float ang = k * 0.9f + e.Life;
                    var q = e.Pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.5f) * (12 + 46 * t);
                    DrawCircle(q + new Vector2(0, -10 * t), 9 + 10 * t, new Color(0.85f, 0.8f, 0.68f, 0.55f * (1 - t)));
                }
                float clash = Mathf.Sin(e.Age * 14) * 6;
                DrawLine(e.Pos + new Vector2(-14 - clash, -22), e.Pos + new Vector2(6 - clash, 2), new Color(Art.Ink, 1 - t), 3, true);
                DrawLine(e.Pos + new Vector2(14 + clash, -22), e.Pos + new Vector2(-6 + clash, 2), new Color(Art.Ink, 1 - t), 3, true);
            }
            if (e.Icon.Length > 0)
            {
                // 작은 판: 아이콘과 숫자를 한 판에 묶어 떠오르며 빨리 옅어진다(아이콘과 숫자가 다른 판과 섞이지 않게).
                const int fs = 21;
                float a = Mathf.Clamp(2f - 2f * t, 0, 1);
                var at = e.Pos + new Vector2(0, -22 - 30 * Mathf.Sqrt(t));
                float w = Art.Font.GetStringSize(e.Text, HorizontalAlignment.Left, -1, fs).X;
                var box = new Rect2(at + new Vector2(-w / 2 - 15, -22), new Vector2(w + 34, 29));
                _gainBox.BgColor = new Color(0.09f, 0.08f, 0.07f, 0.74f * a);
                _gainBox.BorderColor = new Color(1, 1, 1, 0.25f * a);
                DrawStyleBox(_gainBox, box);
                Ui.Icon(this, e.Icon, new Rect2(box.Position + new Vector2(5, 5), new Vector2(19, 19)), new Color(e.Color, a));
                var tp = new Vector2(box.Position.X + 27, at.Y);
                DrawStringOutline(Art.Font, tp, e.Text, HorizontalAlignment.Left, -1, fs, 4, new Color(0.08f, 0.07f, 0.06f, a));
                DrawString(Art.Font, tp, e.Text, HorizontalAlignment.Left, -1, fs, new Color(e.Color, a));
            }
            else if (e.Text.Length > 0)
                Art.Text(this, e.Pos + new Vector2(0, -34 - 30 * t), e.Text, 20, new Color(e.Color, Mathf.Clamp(2 - 2 * t, 0, 1)));
        }
    }
}
