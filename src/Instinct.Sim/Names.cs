namespace Instinct.Sim;

public sealed class Names
{
    static readonly string[] First =
        { "가", "나", "다", "마", "바", "사", "아", "자", "카", "타", "하", "루", "모", "누", "부", "소", "토", "라", "미", "우" };
    static readonly string[] Second =
        { "루", "라", "미", "노", "부", "시", "카", "토", "한", "란", "문", "솔", "린", "곤", "단", "무", "리", "나", "야", "로" };
    static readonly string[] Totems =
        { "곰", "늑대", "사슴", "매", "뱀", "수달", "까마귀", "멧돼지", "여우", "학", "거북", "범", "올빼미", "들소", "담비", "고래" };

    readonly Random _rng;
    readonly HashSet<string> _used = new();
    int _clanCount;

    public Names(Random rng) => _rng = rng;

    public string NextPerson()
    {
        for (int i = 0; i < 50; i++)
        {
            var n = First[_rng.Next(First.Length)] + Second[_rng.Next(Second.Length)];
            if (_used.Add(n)) return n;
        }
        while (true)
        {
            var n = First[_rng.Next(First.Length)] + Second[_rng.Next(Second.Length)] + Second[_rng.Next(Second.Length)];
            if (_used.Add(n)) return n;
        }
    }

    static readonly string[] Shades = { "", "흰", "검은", "붉은", "푸른", "누런", "잿빛", "큰", "작은", "외눈", "얼룩" };

    public string NextClan()
    {
        // 토템이 다 쓰이면 빛깔·생김새를 앞에 붙인다(흰늑대, 붉은매 …). 그것도 다 쓰이면 그때 가서 숫자를 붙인다.
        int i = _clanCount++;
        int round = i / Totems.Length;
        string name = Shades[round % Shades.Length] + Totems[i % Totems.Length];
        return round < Shades.Length ? name : $"{name}{round / Shades.Length + 1}";
    }
}

/// <summary>한국어 조사 처리.</summary>
public static class Josa
{
    static bool HasBatchim(string s)
    {
        if (s.Length == 0) return false;
        char c = s[^1];
        if (c < 0xAC00 || c > 0xD7A3) return false;
        return (c - 0xAC00) % 28 != 0;
    }

    public static string Iga(string s) => s + (HasBatchim(s) ? "이" : "가");
    public static string Eul(string s) => s + (HasBatchim(s) ? "을" : "를");
    public static string Eun(string s) => s + (HasBatchim(s) ? "은" : "는");
    public static string Wa(string s) => s + (HasBatchim(s) ? "과" : "와");
    /// <summary>"으로/로". 받침이 없거나 ㄹ 받침이면 "로".</summary>
    public static string Ro(string s) => s + (HasBatchim(s) && (s[^1] - 0xAC00) % 28 != 8 ? "으로" : "로");
}
