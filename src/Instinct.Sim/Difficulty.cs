namespace Instinct.Sim;

/// <summary>난이도. 보통은 원래 규칙 그대로다(곱하는 값이 모두 1, 더하는 값이 모두 0).</summary>
public enum Difficulty { Easy, Normal, Hard }

// 난이도: 위협(습격·침공의 잦기와 적군의 수), 나라 살림(지출), 민심(불만의 기준), 수확을 조금씩 민다.
// 난수를 더 뽑거나 덜 뽑지 않는다 — 문턱값만 바꾼다. 그래서 같은 씨앗·같은 난이도면 언제나 같은 판이 나온다.
public sealed partial class Simulation
{
    /// <summary>이 판의 난이도. 판을 만든 직후(첫 철 전)에만 정한다. 저장 파일의 머리말에 함께 적힌다.</summary>
    public Difficulty Difficulty = Difficulty.Normal;

    /// <summary>습격·침공이 일어날 확률에 곱한다.</summary>
    internal double ThreatMul => Difficulty switch { Difficulty.Easy => 0.6, Difficulty.Hard => 1.4, _ => 1 };

    /// <summary>쳐들어오는 군대와 습격하는 무리의 수에 곱한다.</summary>
    internal double FoeMul => Difficulty switch { Difficulty.Easy => 0.8, Difficulty.Hard => 1.2, _ => 1 };

    /// <summary>나라의 해마다 지출에 곱한다.</summary>
    internal double ExpenseMul => Difficulty switch { Difficulty.Easy => 0.85, Difficulty.Hard => 1.15, _ => 1 };

    /// <summary>불만이 향해 가는 기준값에 더한다.</summary>
    internal double UnrestBias => Difficulty switch { Difficulty.Easy => -0.04, Difficulty.Hard => 0.05, _ => 0 };

    /// <summary>결속과 정통성이 향해 가는 기준값에 더한다. 어려우면 나라가 늘 덜 묶이고 덜 믿긴다.</summary>
    internal double StateBias => Difficulty switch { Difficulty.Easy => 0.05, Difficulty.Hard => -0.06, _ => 0 };

    /// <summary>봉기와 나라가 무너질(몰락) 확률에 곱한다.</summary>
    internal double CrisisMul => Difficulty switch { Difficulty.Easy => 0.6, Difficulty.Hard => 1.3, _ => 1 };

    /// <summary>수확에 곱한다.</summary>
    internal double YieldMul => Difficulty switch { Difficulty.Easy => 1.08, Difficulty.Hard => 0.94, _ => 1 };

    public static string DifficultyName(Difficulty d) => d switch { Difficulty.Easy => "쉬움", Difficulty.Hard => "어려움", _ => "보통" };

    /// <summary>"easy"/"normal"/"hard"(또는 한국어 이름) → 난이도. 모르면 null.</summary>
    public static Difficulty? ParseDifficulty(string s) => s.Trim().ToLowerInvariant() switch
    {
        "easy" or "쉬움" or "0" => Difficulty.Easy,
        "normal" or "보통" or "1" => Difficulty.Normal,
        "hard" or "어려움" or "2" => Difficulty.Hard,
        _ => null,
    };

    public static string DifficultyKey(Difficulty d) => d switch { Difficulty.Easy => "easy", Difficulty.Hard => "hard", _ => "normal" };
}
