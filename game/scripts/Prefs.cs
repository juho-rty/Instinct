using Godot;

namespace InstinctGame;

/// <summary>설정. user://settings.cfg 에 남는다.</summary>
public static class Prefs
{
    const string Path = "user://settings.cfg";

    public static float Master = 0.8f, Music = 0.55f, Effects = 0.8f;
    public static bool Fullscreen = false;
    /// <summary>화면 글과 단추의 크기(1 = 기본).</summary>
    public static float UiScale = 1f;
    /// <summary>몇 해마다 저절로 저장하나(0 = 하지 않는다).</summary>
    public static int AutosaveYears = 10;
    /// <summary>침공·몰락 같은 큰일이 나면 시간을 멈춘다.</summary>
    public static bool PauseOnCrisis = false;
    /// <summary>나라가 서기 전(부족·군장사회)에는 시간을 이만큼 빨리 흘린다.</summary>
    public static float TribeHaste = 1f;
    /// <summary>처음 안내와 상황별 도움말을 띄운다. 안내 카드의 '다시 표시하지 않기'가 끈다.</summary>
    public static bool ShowGuide = true;
    /// <summary>새 게임의 난이도(마지막에 고른 것). 진행 중인 판의 난이도는 바꾸지 않는다.</summary>
    public static Instinct.Sim.Difficulty Difficulty = Instinct.Sim.Difficulty.Normal;

    public static readonly float[] Scales = { 0.9f, 1f, 1.15f, 1.3f };
    public static readonly int[] Autosaves = { 0, 5, 10, 25, 50 };
    public static readonly float[] Hastes = { 1f, 1.5f, 2f, 3f };

    public static void Load()
    {
        var cf = new ConfigFile();
        if (cf.Load(Path) != Error.Ok) return;
        Master = (float)cf.GetValue("sound", "master", Master);
        Music = (float)cf.GetValue("sound", "music", Music);
        Effects = (float)cf.GetValue("sound", "effects", Effects);
        Fullscreen = (bool)cf.GetValue("screen", "fullscreen", Fullscreen);
        UiScale = (float)cf.GetValue("screen", "ui_scale", UiScale);
        AutosaveYears = (int)cf.GetValue("play", "autosave_years", AutosaveYears);
        PauseOnCrisis = (bool)cf.GetValue("play", "pause_on_crisis2", PauseOnCrisis);
        TribeHaste = (float)cf.GetValue("play", "tribe_pace", TribeHaste);
        ShowGuide = (bool)cf.GetValue("play", "show_guide", ShowGuide);
        Difficulty = Instinct.Sim.Simulation.ParseDifficulty((string)cf.GetValue("play", "difficulty", "normal")) ?? Instinct.Sim.Difficulty.Normal;
    }

    public static void Save()
    {
        var cf = new ConfigFile();
        cf.SetValue("sound", "master", Master);
        cf.SetValue("sound", "music", Music);
        cf.SetValue("sound", "effects", Effects);
        cf.SetValue("screen", "fullscreen", Fullscreen);
        cf.SetValue("screen", "ui_scale", UiScale);
        cf.SetValue("play", "autosave_years", AutosaveYears);
        cf.SetValue("play", "pause_on_crisis2", PauseOnCrisis);
        cf.SetValue("play", "tribe_pace", TribeHaste);
        cf.SetValue("play", "show_guide", ShowGuide);
        cf.SetValue("play", "difficulty", Instinct.Sim.Simulation.DifficultyKey(Difficulty));
        cf.Save(Path);
    }

    /// <summary>소리 크기를 지금 값으로 맞춘다. window면 창 모드도 바꾼다.</summary>
    public static void Apply(bool window)
    {
        Sfx.ApplyVolumes();
        if (!window || DisplayServer.GetName() == "headless") return;
        var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Maximized;
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
    }
}
