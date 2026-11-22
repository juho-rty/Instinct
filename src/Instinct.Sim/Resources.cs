namespace Instinct.Sim;

/// <summary>땅에서 나는 자원. 시대마다 새것이 중요해지고, 없는 나라는 장사로 사거나 칼로 빼앗는다.</summary>
public enum Resource { None, Livestock, Copper, Iron, Spice, Coal, Oil, Uranium }

/// <summary>자원 하나의 설명.</summary>
public sealed record ResourceDef(Resource Kind, string Name, string Tech, Era Era, string Use);

public static class ResourceCatalog
{
    public static readonly IReadOnlyList<ResourceDef> All = new[]
    {
        new ResourceDef(Resource.Livestock, "가축", "herding", Era.Primitive, "말과 소: 군대가 조금 강해지고 땅이 조금 더 먹인다"),
        new ResourceDef(Resource.Copper, "구리", "bronze", Era.Ancient, "청동 무기: 군대가 강해진다(철을 쓰게 되면 밀려난다)"),
        new ResourceDef(Resource.Iron, "철", "iron", Era.Ancient, "쇠 칼과 쇠 보습: 군대가 강해지고 땅이 더 먹인다"),
        new ResourceDef(Resource.Spice, "향료", "navigation", Era.EarlyModern, "비싼 장삿거리: 세입이 는다"),
        new ResourceDef(Resource.Coal, "석탄", "steam", Era.Industrial, "공장의 밥: 없으면 공장제가 절반만 돌아간다"),
        new ResourceDef(Resource.Oil, "석유", "engine", Era.Modern, "군대와 살림의 피: 사서 쓰는 나라는 석유 파동을 맞는다"),
        new ResourceDef(Resource.Uranium, "우라늄", "fission", Era.Modern, "없으면 핵무장을 하지 못한다"),
    };

    public static ResourceDef Of(Resource r) => All.First(d => d.Kind == r);
    public static string Name(Resource r) => r == Resource.None ? "" : Of(r).Name;
}

// 자원: 누가 가졌는가(지도), 얼마나 쓸 수 있는가(자급 1, 사서 쓰면 0.5, 없으면 0).
public sealed partial class Simulation
{
    /// <summary>그 자원을 알아보는 기술이 있는가.</summary>
    public bool Revealed(Resource x) => x != Resource.None && Knows(ResourceCatalog.Of(x).Tech);

    /// <summary>우리 땅 가운데 그 자원이 나는 지방의 수.</summary>
    public int OwnDeposits(Resource x) => Lands(0).Count(p => p.Deposit == x);

    /// <summary>자원을 쓸 수 있는 정도: 우리 땅에서 나면 1, 장삿길(또는 식민지·세계 시장)로 사 오면 0.5, 없으면 0.</summary>
    public double Supply(Resource x)
    {
        if (!Revealed(x)) return 0;
        if (OwnDeposits(x) > 0) return 1;
        if (x == Resource.Spice && Inst.Colonialism) return 1; // 바다 건너에서 실어 온다
        if (Inst.Globalization) return 0.5;
        return Rivals.Any(v => v.Trade && Lands(v.Id).Any(p => p.Deposit == x)) ? 0.5 : 0;
    }

    /// <summary>지금 아쉬운 자원: 알아보지만 우리 땅에서 나지도 않고 사 올 길도 없는 것. 장삿길로 사서 쓰는 동안에는 탐내지 않는다.</summary>
    List<Resource> Wanted() =>
        ResourceCatalog.All.Where(d => Revealed(d.Kind) && Supply(d.Kind) <= 0 && !(d.Kind == Resource.Copper && Knows("iron")))
            .Select(d => d.Kind).ToList();

    /// <summary>이웃 나라가 가진 것 가운데 우리가 아쉬워하는 자원.</summary>
    internal Resource Coveted(Rival v)
    {
        var want = Wanted();
        return Lands(v.Id).OrderBy(p => p.Id).Select(p => p.Deposit).FirstOrDefault(want.Contains);
    }

    internal void Discover(Resource x)
    {
        string name = ResourceCatalog.Name(x);
        var mine = Lands(0).Where(p => p.Deposit == x).Select(p => p.Name).ToList();
        if (mine.Count > 0)
        {
            Say($"【발견】 우리 땅 {string.Join(", ", mine)}에서 {Josa.Iga(name)} 난다.");
            return;
        }
        var holders = Rivals.Where(v => Lands(v.Id).Any(p => p.Deposit == x)).OrderBy(v => v.Id).Select(v => v.Name).ToList();
        Say($"【발견】 {Josa.Eul(name)} 알아보게 되었으나 우리 땅에서는 나지 않는다."
            + (holders.Count > 0 ? $" 가진 곳은 {string.Join(", ", holders)}다." : " 국경 너머 어딘가에 묻혀 있을 것이다."));
    }

    /// <summary>자원이 낳는 사건: 사서 쓰는 석유는 값이 뛰면 나라를 흔든다.</summary>
    internal void ResourceEvents()
    {
        var r = Realm;
        if (r.Era == Era.Modern && Knows("engine") && Supply(Resource.Oil) < 1 && Rnd() < 0.04)
        {
            r.Treasury -= 1;
            r.Unrest = Clamp01(r.Unrest + 0.08);
            _crisisThisYear = true;
            Say("【석유 파동】 기름을 파는 나라들이 꼭지를 잠갔다. 값이 몇 곱절로 뛰었고, 공장과 차가 멈춰 섰다.");
        }
    }
}
