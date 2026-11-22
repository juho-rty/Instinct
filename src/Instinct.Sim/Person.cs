namespace Instinct.Sim;

public enum Sex { Male, Female }

/// <summary>타고나는 본능. 0~1, 부모에게서 물려받는다.</summary>
public enum Trait { Aggression, Greed, Ambition, KinBias, Libido, Empathy }

public sealed class Person
{
    public int Id;
    public string Name = "";
    public Sex Sex;
    public int BirthTick;
    public int? DeathTick;
    public string? DeathCause;
    public Person? KilledBy;

    /// <summary>사회적으로 인정된 아버지. 혈연 계산은 이쪽을 따른다(사람들이 믿는 것).</summary>
    public Person? Father;
    /// <summary>실제 아버지. 불륜으로 태어났으면 Father와 다르다.</summary>
    public Person? BioFather;
    public Person? Mother;
    public Person? Husband;
    public readonly List<Person> Wives = new();
    public readonly List<Person> Children = new();

    /// <summary>종이면 그 주인. 종은 주인의 가구에 속하고 가장 나중에 먹는다.</summary>
    public Person? Master;

    public int ClanId;
    public bool Outsider;

    public readonly double[] Traits = new double[6];
    public double Skill = 1;
    public double Health = 1;
    /// <summary>사적으로 가진 식량. 가구의 것은 가장에게 모인다.</summary>
    public double Food;
    /// <summary>맡은 일(Labor.cs). None이면 다음 철에 정해진다.</summary>
    public Work Job;
    public double Status;
    public int HungrySeasons;
    public int LastBirthTick = -100;
    public Person? PendingLover;
    public bool PaternityChecked;

    /// <summary>상대 Id → 원한의 크기.</summary>
    public readonly Dictionary<int, double> Grudge = new();
    /// <summary>상대 Id → 그 원한이 맺힌 까닭.</summary>
    public readonly Dictionary<int, string> GrudgeWhy = new();
    public bool CaughtHoarding;

    public bool Alive => DeathTick is null;
    public bool IsMale => Sex == Sex.Male;
    public double T(Trait t) => Traits[(int)t];
    public double Age(int tick) => (tick - BirthTick) / 4.0;
}
