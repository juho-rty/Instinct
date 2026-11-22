using Godot;

namespace InstinctGame;

/// <summary>
/// 붓 묶음: 선과 면을 삼각형으로 모아 메시 하나로 만든다. 수천 개의 나무·봉우리를 그리기 호출 한 번에 그린다.
/// 선의 가장자리에는 투명하게 사라지는 띠(Feather)를 붙여 CanvasItem의 안티에일리어싱 선처럼 매끈하게 보이게 한다.
/// </summary>
public sealed class Pen
{
    readonly List<Vector2> _v = new();
    readonly List<Color> _c = new();
    readonly List<int> _i = new();

    /// <summary>선 가장자리가 흐려지는 너비(세계 단위).</summary>
    public float Feather = 0.9f;

    public int Vertices => _v.Count;

    int Add(Vector2 p, Color c)
    {
        _v.Add(p);
        _c.Add(c);
        return _v.Count - 1;
    }

    void Tri(int a, int b, int c)
    {
        _i.Add(a);
        _i.Add(b);
        _i.Add(c);
    }

    /// <summary>채운 다각형(오목해도 된다).</summary>
    public void Fill(Vector2[] pts, Color col)
    {
        var idx = Geometry2D.TriangulatePolygon(pts);
        if (idx.Length == 0) return;
        int b = _v.Count;
        foreach (var p in pts) Add(p, col);
        foreach (int k in idx) _i.Add(b + k);
    }

    /// <summary>채운 원(가장자리를 흐린다).</summary>
    public void Circle(Vector2 c, float r, Color col, int n = 20)
    {
        int center = Add(c, col);
        var clear = new Color(col, 0);
        int ring = _v.Count;
        for (int k = 0; k < n; k++)
        {
            var d = Vector2.FromAngle(Mathf.Tau * k / n);
            Add(c + d * r, col);
            Add(c + d * (r + Feather), clear);
        }
        for (int k = 0; k < n; k++)
        {
            int a = ring + k * 2, b = ring + (k + 1) % n * 2;
            Tri(center, a, b);
            Tri(a, a + 1, b + 1);
            Tri(a, b + 1, b);
        }
    }

    public void Line(Vector2 a, Vector2 b, Color col, float width) => Stroke(new[] { a, b }, col, width);

    /// <summary>호(라디안, CanvasItem.DrawArc와 같은 뜻).</summary>
    public void Arc(Vector2 c, float r, float from, float to, int n, Color col, float width)
    {
        var pts = new Vector2[n + 1];
        for (int k = 0; k <= n; k++) pts[k] = c + Vector2.FromAngle(Mathf.Lerp(from, to, k / (float)n)) * r;
        Stroke(pts, col, width, Mathf.IsEqualApprox(Math.Abs(to - from), Mathf.Tau));
    }

    /// <summary>꺾인 선. 꺾이는 곳은 맞붙여(miter) 겹치지 않게 한다. closed면 처음과 끝을 잇는다.</summary>
    public void Stroke(IReadOnlyList<Vector2> raw, Color col, float width, bool closed = false)
    {
        var pts = new List<Vector2>(raw.Count + 1);
        foreach (var p in raw)
            if (pts.Count == 0 || pts[^1].DistanceSquaredTo(p) > 1e-6f) pts.Add(p);
        if (closed && pts.Count > 2 && pts[0].DistanceSquaredTo(pts[^1]) < 1e-6f) pts.RemoveAt(pts.Count - 1);
        int n = pts.Count;
        if (n < 2) return;

        float h = width / 2, f = h + Feather;
        var clear = new Color(col, 0);
        int b = _v.Count;
        for (int k = 0; k < n; k++)
        {
            bool hasPrev = closed || k > 0, hasNext = closed || k < n - 1;
            var prev = hasPrev ? (pts[k] - pts[(k - 1 + n) % n]).Normalized() : Vector2.Zero;
            var next = hasNext ? (pts[(k + 1) % n] - pts[k]).Normalized() : Vector2.Zero;
            var dir = hasPrev && hasNext ? (prev + next) : hasPrev ? prev : next;
            if (dir.LengthSquared() < 1e-8f) dir = hasNext ? next : prev;
            dir = dir.Normalized();
            var nrm = new Vector2(-dir.Y, dir.X);
            var seg = hasNext ? next : prev;
            float miter = 1 / Math.Max(0.3f, Math.Abs(nrm.Dot(new Vector2(-seg.Y, seg.X))));
            var p = pts[k];
            Add(p + nrm * f * miter, clear);
            Add(p + nrm * h * miter, col);
            Add(p - nrm * h * miter, col);
            Add(p - nrm * f * miter, clear);
        }
        int segs = closed ? n : n - 1;
        for (int k = 0; k < segs; k++)
        {
            int a = b + k * 4, c = b + (k + 1) % n * 4;
            for (int j = 0; j < 3; j++)
            {
                Tri(a + j, a + j + 1, c + j + 1);
                Tri(a + j, c + j + 1, c + j);
            }
        }
    }

    /// <summary>모은 것을 메시 하나로. 아무것도 없으면 null.</summary>
    public ArrayMesh? Build()
    {
        if (_i.Count == 0) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _i.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
