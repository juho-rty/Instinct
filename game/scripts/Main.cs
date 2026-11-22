using Godot;
using Instinct.Sim;
using FileAccess = Godot.FileAccess;

namespace InstinctGame;

/// <summary>
/// 판 전체. 시뮬레이션을 쥐고, 시간을 흘리고, 화면을 움직이고, 마우스와 자판을 받는다.
/// 실행 인자(-- 뒤): --seed N · --skip 해 · --shot 파일.png · --wait 초(찍기 전 보통 속도로 흘림) · --zoom 배율(본진 확대) · --open law|tech|log|realm|people|save|load|settings|land|army|decision|tribe|roundtrip|victory|gain|build|camp:탭|guide[N] (N번째 안내 카드)
/// 자판: 스페이스·1~4 빠르기 · L T R P C 창 · Home 도성 · F5 빠른 저장 · F9 빠른 불러오기 · F10 설정 · Esc
/// </summary>
public partial class Main : Node2D
{
    public Simulation S = null!;
    public TerrainLayer Terrain = null!;
    public OverlayLayer Overlay = null!;
    public ActorLayer Actors = null!;
    public FogLayer Fog = null!;
    public MarkLayer Marks = null!;
    /// <summary>짓기 모드: 고른 칸 건물 종류. null이면 보통 모드.</summary>
    public StructureKind? Placing;
    public List<int> PlaceOk = new();
    public int Hover = -1;
    public Hud Hud = null!;
    public Camera2D Cam = null!;
    CanvasModulate _tint = null!;

    /// <summary>0 멈춤 · 1~4 빠르기.</summary>
    public int Speed;
    int _resume = 1;
    public int Selected = -1;
    public Army? SelectedArmy;
    public int Seed;

    static readonly double[] SeasonSeconds = { 0, 6.0, 3.0, 1.2, 0.05 }; // 1배속은 한 철 6초(한 해 24초): 모으고 겨울을 나는 흐름이 보이게
    static readonly Color[] SeasonTint = { new(1, 1, 1), new(1f, 0.99f, 0.93f), new(1f, 0.92f, 0.82f), new(0.86f, 0.92f, 1f) };

    double _acc;
    int _logSeen, _battleSeen;
    readonly HashSet<int> _asked = new();
    bool _pressed, _dragging;
    Vector2 _pressAt;
    string _shot = "", _open = "";
    /// <summary>스크린샷을 찍는 중이면 안내를 띄우지 않는다(--open guide만 예외).</summary>
    public bool NoGuide => (_shot.Length > 0 && !_open.StartsWith("guide")) || _bench > 0;
    /// <summary>재기: 최고 속도로 이 초만큼 흘리며 틀마다 걸린 시간을 모아 찍고 끈다(--bench 초).</summary>
    double _bench, _benchClock;
    readonly List<double> _frameMs = new();
    double _stepMs, _refreshMs, _syncMs;
    int _benchSteps, _flushes;
    string _hide = "";
    int _shotFrames = -1;
    float _shotWait, _shotZoom;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        int seed = (int)(Time.GetUnixTimeFromSystem() % 9000) + 1, skip = 0;
        Difficulty? diff = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--seed") int.TryParse(args[i + 1], out seed);
            if (args[i] == "--skip") int.TryParse(args[i + 1], out skip);
            if (args[i] == "--shot") _shot = args[i + 1];
            if (args[i] == "--wait") float.TryParse(args[i + 1], out _shotWait);
            if (args[i] == "--zoom") float.TryParse(args[i + 1], out _shotZoom);
            if (args[i] == "--open") _open = args[i + 1];
            if (args[i] == "--bench") double.TryParse(args[i + 1], out _bench);
            if (args[i] == "--hide") _hide = args[i + 1];
            if (args[i] == "--difficulty") diff = Simulation.ParseDifficulty(args[i + 1]);
        }

        Prefs.Load();
        if (diff is { } dv) Prefs.Difficulty = dv; // 시험용: 설정 파일의 값을 이번 실행에서 덮는다
        AddChild(new Sfx());
        Prefs.Apply(_shot.Length == 0);

        Terrain = new TerrainLayer { TextureFilter = TextureFilterEnum.LinearWithMipmaps };
        AddChild(Terrain);
        Overlay = new OverlayLayer { Game = this };
        AddChild(Overlay);
        Actors = new ActorLayer { Game = this, TextureFilter = TextureFilterEnum.LinearWithMipmaps };
        AddChild(Actors);
        Fog = new FogLayer { Game = this };
        AddChild(Fog);
        Marks = new MarkLayer { Game = this };
        AddChild(Marks);
        _tint = new CanvasModulate();
        AddChild(_tint);
        Cam = new Camera2D();
        AddChild(Cam);
        Cam.MakeCurrent();
        Hud = new Hud { Game = this };
        AddChild(Hud);
        Hud.Init();

        NewGame(seed);
        if (skip > 0) FastForward(skip);
        if (_open.Length > 0) OpenForShot();
        if (_shot.Length > 0) _shotFrames = 40 + (int)(_shotWait * 60);
        if (_shot.Length > 0 && _shotWait > 0) SetSpeed(1);
        if (_shot.Length > 0 && _shotZoom > 0) { Cam.Zoom = new Vector2(_shotZoom, _shotZoom); Cam.Position = Cells.At(S.CampCell); }
        if (_bench > 0) SetSpeed(4);
        // 재기용: 층을 하나씩 꺼서 어디가 무거운지 본다(--hide terrain,overlay,actors,hud,glass).
        if (_hide.Contains("terrain")) Terrain.Visible = false;
        if (_hide.Contains("overlay")) Overlay.Visible = false;
        if (_hide.Contains("actors")) { Actors.Visible = false; Actors.ProcessMode = ProcessModeEnum.Disabled; }
        if (_hide.Contains("hud")) Hud.Visible = false;
        if (_hide.Contains("glass")) Ui.Glass.Shader = new Shader { Code = "shader_type canvas_item;" };

        // 띄우자마자 창을 앞으로 가져와 입력을 받게 한다: 그러지 않으면 첫 클릭이 창을 깨우는 데만 쓰이고 사라진다.
        if (_shot.Length == 0 && _bench <= 0)
            Callable.From(() =>
            {
                DisplayServer.WindowMoveToForeground();
                GetWindow().GrabFocus();
                _focusAt = Time.GetTicksMsec();
            }).CallDeferred();
    }

    /// <summary>창이 마지막으로 입력 초점을 얻은 때. 그 직후 눌림 없이 뗌만 오면(창을 깨운 클릭) 그것도 클릭으로 친다.</summary>
    ulong _focusAt;

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn || what == NotificationWMWindowFocusIn) _focusAt = Time.GetTicksMsec();
    }

    /// <summary>새 판. 난이도는 설정에서 마지막에 고른 것(Prefs.Difficulty).</summary>
    public void NewGame(int seed)
    {
        Begin(new Simulation(seed) { PlayerRules = true, Difficulty = Prefs.Difficulty });
        if (!NoGuide) Hud.StartIntro();
    }

    /// <summary>판을 화면에 올린다(새 판이든 불러온 판이든).</summary>
    void Begin(Simulation sim)
    {
        S = sim;
        Seed = sim.Seed;
        Selected = -1;
        SelectedArmy = null;
        Placing = null;
        PlaceOk.Clear();
        Hover = -1;
        Marks.QueueRedraw();
        _gFood = _gWood = _gStone = 0;
        _gainAt = _deliverAt = 0;
        _logSeen = S.Log.Count;
        _battleSeen = S.Battles.Count;
        _asked.Clear();
        _acc = 0;
        Speed = 1;
        Terrain.Build(S.Map, Seed);
        Actors.Reset();
        Overlay.Sync(true);
        Cam.Zoom = new Vector2(0.85f, 0.85f);
        Cam.Position = TerrainLayer.Center(S.Map[S.Map.Capital]);
        ClampCam();
        Hud.ResetGame();
        Hud.Refresh();
    }

    // ───────── 저장 ─────────

    public static readonly string[] Slots = { "auto", "quick", "1", "2", "3", "4", "5", "6" };
    static string SlotPath(string slot) => $"user://saves/{slot}.save";
    public static string SlotName(string slot) => slot switch { "auto" => "자동 저장", "quick" => "빠른 저장", _ => $"슬롯 {slot}" };

    public static string? ReadSlot(string slot)
    {
        string path = SlotPath(slot);
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f?.GetAsText();
    }

    public static string SlotTime(string slot)
    {
        string path = SlotPath(slot);
        if (!FileAccess.FileExists(path)) return "";
        var t = Time.GetDatetimeDictFromUnixTime((long)FileAccess.GetModifiedTime(path) + (long)Time.GetTimeZoneFromSystem()["bias"].AsInt32() * 60);
        return $"{t["month"]}월 {t["day"]}일 {t["hour"]:00}:{(int)t["minute"]:00}";
    }

    string SaveLabel() => $"{S.StageName} · {(S.Chief is { Alive: true } c ? $"{S.RulerTitle} {c.Name}" : "통치자 없음")}";

    public void SaveSlot(string slot, bool quiet = false)
    {
        DirAccess.MakeDirRecursiveAbsolute("user://saves");
        using (var f = FileAccess.Open(SlotPath(slot), FileAccess.ModeFlags.Write))
        {
            if (f == null) { Hud.Toast($"저장 실패: {FileAccess.GetOpenError()}", Art.Bad); return; }
            f.StoreString(SaveGame.Write(S, SaveLabel()));
        }
        if (!quiet)
        {
            Sfx.Play("save");
            Hud.Toast($"{SlotName(slot)}에 저장했습니다 — 제 {S.Year}년 {Simulation.SeasonNames[S.Season]}", Art.Good);
        }
    }

    bool _loading;

    /// <summary>저장을 불러온다. 다시 흘리는 데 시간이 걸리니 알림을 한 번 그린 뒤에 한다.</summary>
    public async void LoadSlot(string slot)
    {
        if (_loading) return;
        string? text = ReadSlot(slot);
        if (text == null) { Hud.Toast($"{SlotName(slot)}: 비어 있는 슬롯입니다.", Art.Bad); return; }
        _loading = true;
        Hud.CloseModal();
        Hud.Banner("불러오는 중…", 30);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            var sim = SaveGame.Read(text);
            bool drift = SaveGame.Drifted(sim, text);
            GD.Print($"불러오기 {slot}: 제 {sim.Year}년, 어긋남 {drift}");
            Begin(sim);
            Speed = 0;
            Hud.Refresh();
            Sfx.Play("save");
            Hud.Banner($"{SlotName(slot)} 불러오기 완료 — 제 {S.Year}년 {Simulation.SeasonNames[S.Season]}", 3);
            if (drift) Hud.Toast("다른 버전에서 만든 저장 파일이라 저장 당시와 조금 다를 수 있습니다.", Art.Bad);
        }
        catch (Exception e) // 깨진 글(FormatException)만이 아니라, 손댄 파일이 다시 내리기 중에 던지는 것도 받는다
        {
            Hud.Banner("불러오기 실패", 3);
            Hud.Toast($"{SlotName(slot)}: {e.Message}", Art.Bad);
        }
        finally { _loading = false; }
    }

    /// <summary>확인용: 사람 없이 여러 해를 흘린다. 건의는 받아들이고 살림은 자동 통치에 맡긴다.</summary>
    void FastForward(int years)
    {
        var policy = new Random(Seed);
        for (int t = 0; t < years * 4 && S.Living.Any(); t++)
        {
            S.Step();
            foreach (var d in S.Pending.ToList()) S.Decide(d.Id, 0);
            if (S.Season == 0) AutoPolicy.Year(S, policy);
            if (S.AtCrossroad && TechTree.All.FirstOrDefault(x => x.Crossroad && x.Course == S.Leaning() && S.CanResearch(x)) is { } fork) S.SetResearch(fork.Key);
        }
        _logSeen = S.Log.Count;
        _battleSeen = S.Battles.Count;
        Speed = 0;
        Overlay.Sync(true);
        Hud.Refresh();
    }

    void OpenForShot()
    {
        switch (_open)
        {
            case "gain": // 자원이 떠오르는 모습
                for (int t = 0; t < 2; t++) StepOnce();
                _gainAt = 0;
                _gFood = 12; _gWood = 4; _gStone = -2;
                FloatGains();
                Focus(S.Map.Capital);
                break;
            case "land":
                Select(S.Map.Capital);
                break;
            case "army":
                var a = S.RaiseArmy(S.Map.Capital) ?? S.Armies.FirstOrDefault(x => x.Owner == 0);
                if (a != null)
                {
                    SelectedArmy = a;
                    var target = S.Map.Provinces.Where(p => p.Owner != 0).OrderBy(p => S.Map.Distance(p, S.Map[a.At])).First();
                    S.OrderArmy(a.Id, target.Id);
                }
                Speed = 2;
                Hud.Refresh();
                break;
            case "build": // 확인용: 몇 채를 놓아 짓게 하고, 움집 짓기 모드로 둔다.
            {
                S.Wood += 30;
                S.Stone += 10;
                foreach (var k in new[] { StructureKind.Hut, StructureKind.Lumber, StructureKind.Lodge, StructureKind.Tower, StructureKind.Palisade })
                {
                    RefreshPlacing(k);
                    if (PlaceOk.Count > 0) S.Place(k, PlaceOk.OrderBy(c => Math.Abs(c % WorldMap.Width - S.CampCell % WorldMap.Width) + Math.Abs(c / WorldMap.Width - S.CampCell / WorldMap.Width)).ThenBy(c => c).First());
                }
                S.Assign(Work.Build, 1);
                S.Assign(Work.Build, 1);
                S.Assign(Work.Scout, 1);
                for (int t = 0; t < 3; t++) S.Step();
                foreach (var d in S.Pending.ToList()) S.Decide(d.Id, 0);
                _logSeen = S.Log.Count;
                Hud.OpenCampTab("build");
                StartPlacing(StructureKind.Hut);
                if (PlaceOk.Count > 0) Hover = PlaceOk[PlaceOk.Count / 2];
                Touched();
                Hud.CloseModal();
                SetSpeed(0);
                break;
            }
            case "decision":
                S.Proposals = true;
                for (int t = 0; t < 400 && S.Pending.Count == 0; t++) S.Step();
                _logSeen = S.Log.Count;
                Overlay.Sync(true);
                Hud.Refresh();
                if (S.Pending.Count > 0) Hud.ShowDecision(S.Pending[0]);
                break;
            case "roundtrip": // 확인용: 목록에 없는 칸에 저장했다가 바로 불러온다.
                SaveSlot("shot-test", true);
                LoadSlot("shot-test");
                break;
            case var g when g.StartsWith("guide"):
                Speed = 0;
                Hud.Refresh();
                Hud.ShowIntroStep(int.TryParse(g[5..], out int gi) ? gi - 1 : 0);
                break;
            case "victory": // 확인용: 이룬 승리가 없으면 교역의 승리 창을 띄워 본다
                Speed = 0;
                Hud.Refresh();
                Hud.ShowVictory(S.Won.Count > 0 ? S.Won[^1].Course : Course.Commerce);
                break;
            case "tribe":
                Speed = 0;
                Hud.Refresh();
                break;
            case var ct when ct.StartsWith("camp:"): // 확인용: 왼쪽 패널의 탭(tribe·work·build)
                Speed = 0;
                Hud.OpenCampTab(ct[5..]);
                Hud.Refresh();
                break;
            default:
                Hud.Open(_open);
                break;
        }
    }

    public void SetSpeed(int speed)
    {
        if (speed > 0) _resume = speed;
        Speed = speed;
        Hud.Refresh();
    }

    public void TogglePause() => SetSpeed(Speed == 0 ? _resume : 0);

    public void Select(int province)
    {
        Selected = province;
        SelectedArmy = null;
        Overlay.Sync();
        Hud.Refresh();
    }

    public void SelectArmy(Army? a)
    {
        SelectedArmy = a;
        if (a != null) Selected = -1;
        Overlay.Sync();
        Hud.Refresh();
    }

    public void Focus(int province)
    {
        Cam.Position = TerrainLayer.Center(S.Map[province]);
        ClampCam();
    }

    /// <summary>플레이어가 무언가를 한 뒤: 화면을 맞춘다.</summary>
    public void Touched()
    {
        Drain();
        Overlay.Sync(true);
        Fog.Sync();
        Actors.OnPlayerCommand();
        if (Placing is { } pk) RefreshPlacing(pk);
        Hud.Refresh();
    }

    bool Bench(double delta)
    {
        _benchClock += delta;
        if (_benchClock > 1) _frameMs.Add(delta * 1000); // 첫 1초는 데우기
        if (Hud.ModalOpen) Hud.CloseModal();
        if (Hud.GuideOpen) Hud.CloseGuide();
        if (Speed == 0) SetSpeed(4);
        if (_benchClock < _bench + 1) return false;
        _frameMs.Sort();
        double P(double q) => _frameMs.Count == 0 ? 0 : _frameMs[Math.Min(_frameMs.Count - 1, (int)(q * _frameMs.Count))];
        int n = Math.Max(1, _benchSteps);
        GD.Print($"BENCH frames {_frameMs.Count} avg {_frameMs.DefaultIfEmpty(0).Average():F2}ms p50 {P(0.5):F2} p95 {P(0.95):F2} p99 {P(0.99):F2} max {P(1):F2}");
        GD.Print($"BENCH steps {_benchSteps} → {S.Year}y {S.Realm.Stage}, step {_stepMs / n:F2}ms per season; refresh {_refreshMs / Math.Max(1, _flushes):F2}ms sync {_syncMs / Math.Max(1, _flushes):F2}ms per frame ({_flushes})");
        GD.Print($"BENCH window {GetViewport().GetVisibleRect().Size} x{DisplayServer.WindowGetSize()} draws {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} orphans {Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)} vram {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1e6:F0}MB gpu {RenderingServer.GetVideoAdapterName()}");
        GetTree().Quit();
        return true;
    }

    void StepOnce()
    {
        if (!S.Living.Any()) { Speed = 0; Hud.Refresh(); return; }
        double f0 = S.CommonStore, w0 = S.Wood, s0 = S.Stone;
        ulong t0 = Time.GetTicksUsec();
        S.Step();
        _gFood += S.CommonStore - f0; _gWood += S.Wood - w0; _gStone += S.Stone - s0;
        if (_bench > 0) { _stepMs += (Time.GetTicksUsec() - t0) / 1000.0; _benchSteps++; }
        Drain();
        if (S.Season == 0)
        {
            Sfx.Play("season", 0.5f);
            if (Prefs.AutosaveYears > 0 && S.Year > 1 && (S.Year - 1) % Prefs.AutosaveYears == 0) SaveSlot("auto", true);
        }
        if (SelectedArmy != null && !S.Armies.Contains(SelectedArmy)) SelectedArmy = null;
        _stepped = true; // 화면은 이 틀의 끝에 한 번만 맞춘다(FlushScreen)
    }

    bool _stepped;
    // 철마다 창고에 들어온 양(여러 철이 한 틀에 흐르면 모아 둔다)
    double _gFood, _gWood, _gStone;
    ulong _gainAt;

    /// <summary>쌓인 자원 증감을 지도 위 그 자원을 내는 자리에서 띄운다.</summary>
    void FloatGains()
    {
        ulong now = Time.GetTicksMsec();
        if (now - _gainAt < 1400) return; // 자주 띄우면 지도가 숫자로 덮인다: 모아서 띄운다
        _gainAt = now;
        Vector2 Where(StructureKind k, Vector2 off)
        {
            var st = S.Structures.Where(x => x.Kind == k && x.Built).OrderBy(x => x.Id).FirstOrDefault();
            return st != null ? Cells.At(st.Cell) : Cells.At(S.CampCell) + off;
        }
        void One(ref double g, string icon, Vector2 pos, Color col)
        {
            double v = g;
            g = 0;
            if (Math.Abs(v) < 1) return;
            Actors.Gain(pos, icon, v, v > 0 ? col : Art.Bad);
        }
        // 무리 시절: 늘어난 것은 일꾼이 등에 지고 와 창고에 내려놓을 때 뜬다(Deliver, Folk.cs).
        // 줄어든 것은 창고에서 바로 붉게, 한동안 아무도 가져오지 않으면(일꾼이 없거나 아주 빠를 때) 창고에서 그냥 띄운다.
        if (S.Tribal && S.Living.Any())
        {
            var store = Where(StructureKind.Storehouse, Vector2.Zero);
            bool stale = now - _deliverAt > 4500;
            if (_gFood < 0 || stale) One(ref _gFood, "food", store, FoodCol);
            if (_gWood < 0 || stale) One(ref _gWood, "wood", store + new Vector2(-40, 10), WoodCol);
            if (_gStone < 0 || stale) One(ref _gStone, "stone", store + new Vector2(40, 10), StoneCol);
            if (stale) _deliverAt = now;
            return;
        }
        One(ref _gFood, "food", Cells.At(S.CampCell), FoodCol);
        One(ref _gWood, "wood", Where(StructureKind.Lumber, new Vector2(-70, 40)), WoodCol);
        One(ref _gStone, "stone", Where(StructureKind.Quarry, new Vector2(70, 40)), StoneCol);
    }

    static readonly Color FoodCol = new("f4d27a"), WoodCol = new("b8e08a"), StoneCol = new("cfd8e4");
    ulong _deliverAt;

    /// <summary>일꾼 하나가 짐(food·wood·stone)을 창고에 내려놓았다: 그동안 쌓인 그 자원의 증가를 그 자리에서 띄운다.</summary>
    public void Deliver(string kind, Vector2 at)
    {
        ref double g = ref kind == "wood" ? ref _gWood : ref kind == "stone" ? ref _gStone : ref _gFood;
        if (g < 1) return;
        _deliverAt = Time.GetTicksMsec();
        Actors.Gain(at + new Vector2(0, -10), kind, g, kind == "wood" ? WoodCol : kind == "stone" ? StoneCol : FoodCol);
        g = 0;
    }


    /// <summary>한 틀에 여러 철이 흘러도 화면은 한 번만 맞춘다.</summary>
    void FlushScreen()
    {
        if (!_stepped) return;
        _stepped = false;
        ulong t1 = Time.GetTicksUsec();
        FloatGains();
        Overlay.Sync();
        ulong t2 = Time.GetTicksUsec();
        Hud.Refresh(tick: true);
        if (_bench > 0) { _syncMs += (t2 - t1) / 1000.0; _refreshMs += (Time.GetTicksUsec() - t2) / 1000.0; _flushes++; }
    }

    /// <summary>새로 쌓인 연대기·싸움·결정을 화면에 알린다.</summary>
    void Drain()
    {
        if (_logSeen > S.Log.Count) _logSeen = S.Log.Count;
        for (; _logSeen < S.Log.Count; _logSeen++)
        {
            var e = S.Log[_logSeen];
            if (Hud.OnLog(e) && Speed > 0) { _resume = Math.Min(Speed, 2); Speed = 0; }
        }

        if (_battleSeen > S.Battles.Count) _battleSeen = S.Battles.Count;
        for (; _battleSeen < S.Battles.Count; _battleSeen++)
        {
            var b = S.Battles[_battleSeen];
            Sfx.Play("battle", 0.8f);
            Actors.Burst(TerrainLayer.Center(S.Map[b.Province]) + new Vector2(0, 40), b.Won ? "승리" : "패배", b.Won ? Art.Good : Art.Bad);
        }

        _asked.RemoveWhere(id => S.Pending.All(d => d.Id != id));
        foreach (var d in S.Pending)
        {
            if (!_asked.Add(d.Id)) continue;
            Hud.DecisionArrived(d); // 시간을 멈추지 않는다: 오른쪽에 카드로 쌓이고 그 자리에서 고를 수 있다
        }
    }

    public Vector2 ToWorld(Vector2 screen) => Cam.Position + (screen - GetViewportRect().Size / 2) / Cam.Zoom.X;

    public void FocusCell(int cell)
    {
        Cam.Position = Cells.At(cell);
        ClampCam();
    }

    void ClampCam()
    {
        // 화면이 땅 밖을 비추지 않게 한다.
        var w = TerrainLayer.WorldSize;
        var view = GetViewportRect().Size;
        float min = Math.Max(view.X / w.X, view.Y / w.Y);
        if (Cam.Zoom.X < min) Cam.Zoom = new Vector2(min, min);
        var half = view / 2 / Cam.Zoom.X;
        Cam.Position = new Vector2(Mathf.Clamp(Cam.Position.X, half.X, Math.Max(half.X, w.X - half.X)), Mathf.Clamp(Cam.Position.Y, half.Y, Math.Max(half.Y, w.Y - half.Y)));
    }

    void ZoomAt(Vector2 screen, float factor)
    {
        var world = ToWorld(screen);
        float z = Mathf.Clamp(Cam.Zoom.X * factor, 0.3f, 2.4f);
        Cam.Zoom = new Vector2(z, z);
        Cam.Position = world - (screen - GetViewportRect().Size / 2) / z;
        ClampCam();
    }

    public override void _Process(double delta)
    {
        if (_bench > 0 && Bench(delta)) return;
        if (_shotFrames >= 0 && --_shotFrames < 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shot);
            GetTree().Quit();
            return;
        }

        var move = Vector2.Zero;
        if (!Hud.ModalOpen)
        {
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        }
        if (move != Vector2.Zero)
        {
            Cam.Position += move * (float)delta * 900 / Cam.Zoom.X;
            ClampCam();
        }

        _tint.Color = _tint.Color.Lerp(SeasonTint[S.Season], Math.Min(1f, (float)delta * 1.5f));

        if (Speed > 0 && !Hud.ModalOpen && !Hud.GuideHolds)
        {
            _acc += delta;
            // 나라가 서기 전에는 할 일이 적으니 더 빨리 흘린다(설정).
            double need = SeasonSeconds[Speed] / (S.Tribal && Speed < 4 ? Prefs.TribeHaste : 1);
            int guard = 0;
            while (_acc >= need && Speed > 0 && !Hud.ModalOpen && !Hud.GuideHolds && guard++ < 4)
            {
                _acc -= need;
                StepOnce();
            }
        }
        else _acc = 0;
        FlushScreen();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    if (Placing != null) StopPlacing();
                    else if (Hud.ModalOpen) Hud.CloseModal();
                    else if (Hud.GuideOpen) Hud.CloseGuide();
                    else if (Selected < 0 && SelectedArmy == null) Hud.Open("settings");
                    else { Selected = -1; SelectedArmy = null; Overlay.Sync(); Hud.Refresh(); }
                    break;
                case Key.P: Hud.Open("people"); break;
                case Key.F5: SaveSlot("quick"); break;
                case Key.F9: LoadSlot("quick"); break;
                case Key.F10: Hud.Open("settings"); break;
                case Key.Space: if (!Hud.ModalOpen) TogglePause(); break;
                case Key.Key1: SetSpeed(1); break;
                case Key.Key2: SetSpeed(2); break;
                case Key.Key3: SetSpeed(3); break;
                case Key.Key4: SetSpeed(4); break;
                case Key.L: Hud.Open("law"); break;
                case Key.J: if (S.Living.Any()) Hud.ShowCamp("work"); break;
                case Key.B: if (S.Living.Any()) Hud.ShowCamp("build"); break;
                case Key.T: Hud.Open("tech"); break;
                case Key.R: Hud.Open("realm"); break;
                case Key.C: Hud.Open("log"); break;
                case Key.Home: Focus(S.Map.Capital); break;
            }
            return;
        }
        if (Hud.ModalOpen) return;

        if (ev is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) ZoomAt(mb.Position, 1.12f);
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) ZoomAt(mb.Position, 1 / 1.12f);
            else if (mb.ButtonIndex is MouseButton.Left or MouseButton.Middle)
            {
                if (mb.Pressed)
                {
                    _pressed = true; _dragging = mb.ButtonIndex == MouseButton.Middle; _pressAt = mb.Position;
                }
                else
                {
                    // 창을 깨운 첫 클릭은 눌림이 먹히고 뗌만 올 수 있다: 초점을 얻은 직후라면 그대로 클릭으로 받는다.
                    bool wake = !_pressed && Time.GetTicksMsec() - _focusAt < 800;
                    if ((_pressed || wake) && !_dragging && mb.ButtonIndex == MouseButton.Left) { if (Placing != null) PlaceAt(ToWorld(mb.Position)); else Click(ToWorld(mb.Position)); }
                    _pressed = _dragging = false;
                }
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.Right) { if (Placing != null) StopPlacing(); else Order(ToWorld(mb.Position)); }
        }
        else if (ev is InputEventMouseMotion hov && Placing != null && !_pressed)
        {
            int h = Cells.CellAt(ToWorld(hov.Position));
            if (h != Hover) { Hover = h; Marks.QueueRedraw(); }
        }
        else if (ev is InputEventMouseMotion mm && _pressed)
        {
            if (!_dragging && mm.Position.DistanceTo(_pressAt) > 6) _dragging = true;
            if (_dragging)
            {
                Cam.Position -= mm.Relative / Cam.Zoom.X;
                ClampCam();
            }
        }
    }

    /// <summary>짓기 모드로 들어간다: 지을 수 있는 칸을 모아 초록으로 칠한다.</summary>
    public void StartPlacing(StructureKind k)
    {
        Placing = k;
        Selected = -1;
        SelectedArmy = null;
        RefreshPlacing(k);
        var def = StructureCatalog.Of(k);
        if (PlaceOk.Count == 0) Hud.Toast($"{def.Name}: 지을 수 있는 칸이 아직 없습니다. {def.Need}", Art.Bad);
        else Hud.Toast($"{def.Name}: 초록 칸을 누르면 짓습니다(Shift를 누른 채면 이어서 더 놓기). 오른쪽 클릭이나 Esc로 그만둡니다.", Ui.Accent);
        Hud.Refresh();
    }

    public void RefreshPlacing(StructureKind k)
    {
        PlaceOk.Clear();
        for (int c = 0; c < S.Map.Seen.Length; c++)
            if (S.Map.Seen[c] && S.WhyCantPlace(k, c).Length == 0) PlaceOk.Add(c);
        Marks.QueueRedraw();
    }

    public void StopPlacing()
    {
        Placing = null;
        PlaceOk.Clear();
        Hover = -1;
        Marks.QueueRedraw();
        Hud.Refresh();
    }

    void PlaceAt(Vector2 world)
    {
        if (Placing is not { } k) return;
        int c = Cells.CellAt(world);
        string why = c < 0 ? "지도 밖입니다." : S.WhyCantPlace(k, c);
        if (why.Length > 0) { Hud.Toast(why, Art.Bad); return; }
        var def = StructureCatalog.Of(k);
        if (!S.Place(k, c)) { Hud.Toast($"{def.Name}: 지을 수 없습니다.", Art.Bad); return; }
        Actors.Float(Cells.At(c), $"{def.Name} 터", Art.Good);
        // 들어간 나무·돌을 그 자리에서 그 자원의 아이콘으로 보인다(창고의 식량 증감과 헷갈리지 않게).
        if (def.Wood > 0) Actors.Gain(Cells.At(c) + new Vector2(-30, -20), "wood", -def.Wood, Art.Bad);
        if (def.Stone > 0) Actors.Gain(Cells.At(c) + new Vector2(30, -20), "stone", -def.Stone, Art.Bad);
        if (S.CountJob(Work.Build) == 0) Hud.Toast("일꾼 중 건설 일을 맡은 사람이 없습니다. 일 탭에서 건설에 사람을 붙이세요.", Art.Bad);
        // 다 놓았으면 짓기 모드를 끝낸다(Shift를 누른 채 놓으면 같은 건물을 이어서 놓는다).
        // 끝내지 않으면 방금 놓은 칸 위에 곧바로 "이미 건물이 있음"이 떠 실패한 것처럼 보인다.
        Hover = -1;
        bool canMore = S.Wood >= def.Wood - 1e-9 && S.Stone >= def.Stone - 1e-9;
        if (!Input.IsKeyPressed(Key.Shift) || !canMore) { Placing = null; PlaceOk.Clear(); }
        Marks.QueueRedraw();
        Touched();
    }

    void Click(Vector2 world)
    {
        var a = Actors.ArmyNear(world);
        if (a is { Owner: 0 }) { SelectArmy(a); return; }
        if (Cam.Zoom.X >= 1.3f && Actors.FolkIdNear(world) is int fid && fid >= 0)
        {
            Hud.ShowPerson(fid);
            return;
        }
        int c = Cells.CellAt(world);
        if (c < 0 || !S.Map.Seen[c]) { Select(-1); return; }
        Select(Terrain.ProvinceAt(world));
    }

    void Order(Vector2 world)
    {
        if (SelectedArmy == null) return;
        int p = Terrain.ProvinceAt(world);
        if (p < 0) { Hud.Toast("바다로는 이동할 수 없습니다.", Art.Bad); return; }
        if (S.OrderArmy(SelectedArmy.Id, p))
        {
            Actors.Float(TerrainLayer.Center(S.Map[p]), "이동", Art.Ours);
            if (Speed == 0) Hud.Toast($"{SelectedArmy.Name}: {Josa.Ro(S.Map[p].Name)} 이동 명령. 시간이 흐르면 출발합니다.", Ui.Accent);
        }
        else Hud.Toast("그곳으로 가는 길이 없습니다.", Art.Bad);
        Touched();
    }
}
