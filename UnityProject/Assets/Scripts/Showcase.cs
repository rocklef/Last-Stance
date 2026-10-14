using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// SHOWCASE - a scripted trailer of the Moros fight, recorded straight to video.
//   LastStance.exe -showcase <out.mp4> -lines <lines.json> [-durations <durations.json>] [-ffmpeg <ffmpeg.exe>]
// The game runs at a fixed 30 frames per second of game time (Time.captureFramerate), every frame is
// piped into ffmpeg, and every sound effect / narration cue is logged with its frame number
// (<out>.events.tsv) so the soundtrack can be mixed in afterwards in perfect sync.
// The director below stages each scene: it places Kaito and Moros, calls Moros's powers one by one,
// drives Kaito with a scripted brain (runs, wall runs, leaps, dodges, powers), frames every moment
// with a steady camera, and shows the narration as subtitles.
[DefaultExecutionOrder(-90)]
public class Showcase : MonoBehaviour
{
    public static bool Active { get; private set; }
    static string outPath, linesPath, durPath, ffmpegPath = "ffmpeg";
    // -finale: the final battle played live in real time (for screen recording) - every formation
    // in about two and a half minutes, directed camera, no narration
    public static bool Finale { get; private set; }
    bool live;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        var args = System.Environment.GetCommandLineArgs();
        if (System.Array.IndexOf(args, "-finale") >= 0)
        {
            Active = true;
            Finale = true;
            MonkBoss.ShowcaseMode = true;
            var fgo = new GameObject("Showcase");
            DontDestroyOnLoad(fgo);
            fgo.AddComponent<Showcase>();
            return;
        }
        int i = System.Array.IndexOf(args, "-showcase");
        if (i < 0 || i + 1 >= args.Length) return;
        Active = true;
        MonkBoss.ShowcaseMode = true;
        outPath = args[i + 1];
        int j = System.Array.IndexOf(args, "-lines"); if (j >= 0 && j + 1 < args.Length) linesPath = args[j + 1];
        j = System.Array.IndexOf(args, "-durations"); if (j >= 0 && j + 1 < args.Length) durPath = args[j + 1];
        j = System.Array.IndexOf(args, "-ffmpeg"); if (j >= 0 && j + 1 < args.Length) ffmpegPath = args[j + 1];
        var go = new GameObject("Showcase");
        DontDestroyOnLoad(go);
        go.AddComponent<Showcase>();
    }

    // ================================================================== recording
    System.Diagnostics.Process ff;
    Stream ffIn;
    byte[] buf;
    int frame;
    bool recording;
    StreamWriter events;
    // the game's own audio output, rendered in step with the video (Unity AudioRenderer)
    FileStream audioOut;
    int channels;

    void StartRecording()
    {
        Time.captureFramerate = 30;
        int w = Screen.width & ~1, h = Screen.height & ~1;
        var psi = new System.Diagnostics.ProcessStartInfo(ffmpegPath,
            $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {w}x{h} -r 30 -i - -vf vflip -c:v libx264 -preset medium -crf 17 -pix_fmt yuv420p \"{outPath}\"")
        { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
        ff = System.Diagnostics.Process.Start(psi);
        ffIn = ff.StandardInput.BaseStream;
        events = new StreamWriter(outPath + ".events.tsv");
        events.WriteLine("frame\tkind\tname\tvol\tpitch");
        FX.Played += (c, v, p) => Log("SFX", c.name, v, p);
        channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
        audioOut = new FileStream(outPath + ".audio.f32", FileMode.Create);
        AudioRenderer.Start();
        File.WriteAllText(outPath + ".audio.txt", $"{AudioSettings.outputSampleRate} {channels}");
        recording = true;
        StartCoroutine(Capture());
        Debug.Log($"[SHOWCASE] recording {w}x{h} -> {outPath}");
    }

    void Log(string kind, string name, float vol = 1, float pitch = 1)
    {
        if (events != null) events.WriteLine($"{frame}\t{kind}\t{name}\t{vol:0.###}\t{pitch:0.###}");
    }

    IEnumerator Capture()
    {
        var eof = new WaitForEndOfFrame();
        int w = Screen.width & ~1, h = Screen.height & ~1;
        while (recording)
        {
            yield return eof;
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex.width != w || tex.height != h)
            {
                // the window was resized: scale into the recording size
                var rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var t2 = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t2.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                t2.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Destroy(tex);
                tex = t2;
            }
            var data = tex.GetRawTextureData<byte>();
            if (buf == null || buf.Length != data.Length) buf = new byte[data.Length];
            data.CopyTo(buf);
            ffIn.Write(buf, 0, buf.Length);
            Destroy(tex);
            // this frame's slice of the game's audio
            int n = AudioRenderer.GetSampleCountForCaptureFrame();
            if (n > 0)
            {
                using (var a = new Unity.Collections.NativeArray<float>(n * channels, Unity.Collections.Allocator.Temp))
                {
                    AudioRenderer.Render(a);
                    var bytes = new byte[a.Length * 4];
                    System.Buffer.BlockCopy(a.ToArray(), 0, bytes, 0, bytes.Length);
                    audioOut.Write(bytes, 0, bytes.Length);
                }
            }
            frame++;
            if (frame % 300 == 0) Debug.Log($"[SHOWCASE] frame {frame} ({frame / 30f:0}s) unscaledDt={Time.unscaledDeltaTime:0.0000}");
        }
    }

    IEnumerator StopRecording()
    {
        Log("END", "end");
        recording = false;
        yield return null;
        yield return null;
        ffIn.Flush();
        ffIn.Close();
        ff.WaitForExit(120000);
        events.Flush();
        events.Close();
        AudioRenderer.Stop();
        audioOut.Flush();
        audioOut.Close();
        Debug.Log($"[SHOWCASE] done: {frame} frames");
        Time.captureFramerate = 0;
        Application.Quit();
    }

    // ================================================================== narration
    readonly Dictionary<string, string> lines = new Dictionary<string, string>();
    readonly Dictionary<string, float> durs = new Dictionary<string, float>();

    [System.Serializable] class Line { public string id, text; public float dur; }
    [System.Serializable] class LineFile { public Line[] lines; }

    void LoadLines()
    {
        foreach (var path in new[] { linesPath, durPath })
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
            var f = JsonUtility.FromJson<LineFile>(File.ReadAllText(path));
            if (f?.lines == null) continue;
            foreach (var l in f.lines)
            {
                if (!string.IsNullOrEmpty(l.text)) lines[l.id] = l.text;
                if (l.dur > 0) durs[l.id] = l.dur;
            }
        }
    }

    float Dur(string id) => durs.TryGetValue(id, out var d) ? d : (lines.TryGetValue(id, out var t) ? t.Split(' ').Length / 2.2f + 0.6f : 2f);

    // lines queue up so the narrator never talks over himself
    readonly Queue<string> sayQueue = new Queue<string>();
    float voiceFreeAt;
    void Say(string id) => sayQueue.Enqueue(id);

    void Narrate()
    {
        if (sayQueue.Count == 0 || Clock.Now < voiceFreeAt) return;
        string id = sayQueue.Dequeue();
        if (!lines.TryGetValue(id, out var text)) text = id;
        hud.Subtitle(text, Dur(id) + 0.5f);
        Log("SAY", id);
        voiceFreeAt = Clock.Now + Dur(id) + 0.35f;
    }

    // ================================================================== staging
    MonkBoss boss;
    Fighter hero;
    HeroPowers powers;
    BossDirector dir;
    FightCamera fc;
    HUD hud;
    ShowcaseBrain brain;

    IEnumerator Start()
    {
        if (!Finale) LoadLines();
        while (MonkBoss.Live == null || GameManager.I == null || GameManager.I.cam.director == null) yield return null;
        boss = MonkBoss.Live;
        hero = GameManager.I.p1;
        hud = GameManager.I.hud;
        fc = GameManager.I.cam;
        dir = fc.director;
        powers = hero.GetComponent<HeroPowers>();
        hud.ShowHint(false);
        Fighter.demoShield = true;
        hero.bossDamageScale = 0;
        FightCamera.koDist = 8f;
        FightCamera.koHeight = 3f;
        brain = hero.gameObject.AddComponent<ShowcaseBrain>();
        hero.brain = brain;
        if (Finale)
        {
            live = true;
            yield return FinaleScript();
            yield break;
        }
        StartRecording();
        yield return Script();
        yield return StopRecording();
    }

    void Update()
    {
        if ((recording || live) && hero != null && boss != null)
        {
            hero.health = hero.maxHealth;   // the hero of the trailer never falls
            if (recording) Narrate();
            CamUpdate();
        }
    }

    static Vector3 V(float x, float z) => new Vector3(x, 0, z);
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    Vector3 HeroFlat => Flat(hero.transform.position);
    Vector3 HeroChest => hero.Body + Vector3.up * 1.1f;

    IEnumerator W(float gameSeconds) { for (float x = 0; x < gameSeconds; x += Time.deltaTime) yield return null; }
    IEnumerator WV(float videoSeconds) { int n = Mathf.RoundToInt(videoSeconds * 30); for (int k = 0; k < n; k++) yield return null; }
    IEnumerator WaitBoss(float max) { for (float x = 0; x < max && boss.ShowBusy && !boss.Ended; x += Time.deltaTime) yield return null; }
    IEnumerator WaitUntil(System.Func<bool> cond, float max) { for (float x = 0; x < max && !cond(); x += Time.deltaTime) yield return null; }
    IEnumerator WaitHeroReady(float max) { yield return WaitUntil(() => hero.CanAct, max); }

    void Chapter(string big, string small)
    {
        hud.Announce(big, small, 2.4f, new Color(1f, 0.82f, 0.35f));
        Log("CHAPTER", big);
    }

    void Cut(Vector3 heroAt, Vector3 heroFace, Vector3 bossAt, float bossH)
    {
        boss.Place(bossAt, bossH);
        hero.Teleport(heroAt, heroFace);
        brain.Stop();
        dir.SnapOverride();
    }

    // ================================================================== camera
    enum Cam { Frame, Follow, Chase, Front, Aerial, Orbit, Fixed, Low, HeroClose }
    Cam cam = Cam.Orbit;
    Vector3 camOffset, fixedPos, fixedLook;
    bool fixedLookHero;
    float frameSide = 1, orbitAng, orbitSpeed, orbitR, orbitH, orbitLookH;
    Vector3 orbitC;
    bool orbitOnBoss;

    void CamFrame(float side) { cam = Cam.Frame; frameSide = side; }
    void CamFollow(Vector3 offset) { cam = Cam.Follow; camOffset = offset; }
    void CamFixed(Vector3 p, Vector3 look, bool lookHero = false) { cam = Cam.Fixed; fixedPos = p; fixedLook = look; fixedLookHero = lookHero; }
    void CamOrbit(Vector3 c, float r, float h, float lookH, float degPerSec, float startDeg, bool onBoss = false)
    {
        cam = Cam.Orbit; orbitC = c; orbitR = r; orbitH = h; orbitLookH = lookH; orbitSpeed = degPerSec; orbitAng = startDeg; orbitOnBoss = onBoss;
    }

    Vector3 HeroDir()
    {
        Vector3 v = Flat(hero.transform.forward);
        return v.sqrMagnitude > 0.01f ? v.normalized : Vector3.forward;
    }

    void CamUpdate()
    {
        if (powers != null && powers.OwnsCamera) return;   // the Guardian / Awakening stage their own shots
        if (boss.Ended) return;
        float dt = Time.deltaTime;
        Vector3 h = HeroChest;
        Vector3 v = HeroDir();
        Vector3 side = Vector3.Cross(Vector3.up, v);
        Vector3 pos, look;
        float fov = 50;
        switch (cam)
        {
            case Cam.Frame:
            {
                var b = fc.frameBounds;
                Vector3 axis = Flat(boss.Pos - hero.transform.position);
                if (axis.sqrMagnitude < 0.01f) axis = v;
                Vector3 n = Vector3.Cross(Vector3.up, axis.normalized) * frameSide;
                Vector3 e = b.extents;
                float cam0 = Camera.main ? Camera.main.aspect : 1.78f;
                float r = Mathf.Max(e.y, Mathf.Max(e.x, e.z) / cam0);
                float need = Mathf.Max(7.5f, r / Mathf.Tan(25 * Mathf.Deg2Rad) * 1.1f + Mathf.Max(e.x, e.z) * 0.4f);
                Vector3 d = (n * Mathf.Cos(16 * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(16 * Mathf.Deg2Rad)).normalized;
                pos = b.center + d * need;
                look = b.center;
                break;
            }
            case Cam.Follow: pos = h + camOffset; look = h; break;
            case Cam.Chase: pos = h - v * 6f + Vector3.up * 2.2f + side * 1.0f; look = h + v * 4f; fov = 58; break;
            case Cam.Front:
            {
                pos = h + v * 6.5f + Vector3.up * 1.7f + side * 1.3f;
                look = Vector3.Lerp(h, boss.HeadPoint, 0.3f);
                fov = 58;
                break;
            }
            case Cam.Aerial: pos = h - v * 8f + side * 5f + Vector3.up * 17f; look = h + v * 3f; fov = 55; break;
            case Cam.Orbit:
            {
                orbitAng += orbitSpeed * dt;
                Vector3 c = orbitOnBoss ? Flat(boss.Pos) : orbitC;
                float a = orbitAng * Mathf.Deg2Rad;
                pos = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * orbitR + Vector3.up * orbitH;
                look = c + Vector3.up * orbitLookH;
                break;
            }
            case Cam.Low:
            {
                Vector3 m = boss.HeadPoint;
                Vector3 away = Flat(h - m);
                away = away.sqrMagnitude > 0.01f ? away.normalized : -v;
                Vector3 s2 = Vector3.Cross(Vector3.up, away);
                pos = h + away * 4.5f + s2 * 1.8f - Vector3.up * 0.4f;
                pos.y = Mathf.Max(pos.y, hero.floorY + 0.45f);
                look = Vector3.Lerp(h, m, 0.6f);
                fov = 62;
                break;
            }
            case Cam.HeroClose: pos = h + v * 2.8f + side * 1.0f + Vector3.up * 0.2f; look = h; fov = 42; break;
            default: pos = fixedPos; look = fixedLookHero ? h : fixedLook; break;
        }
        dir.Override(pos, look, fov, 0.25f);
    }

    // ================================================================== the trailer
    IEnumerator Script()
    {
        // ---------------------------------------------------------------- PROLOGUE: Moros rises
        CamOrbit(V(0, 4.2f), 13f, 4.5f, 2.6f, 7f, -90f);
        dir.SnapOverride();
        yield return WV(0.8f);
        Say("open1");
        yield return WV(Dur("open1") + 0.8f);
        Say("open2");
        yield return WaitBoss(20f);
        yield return WV(0.5f);

        // ---------------------------------------------------------------- I. THE SHARD MONK
        Cut(V(0, -2.5f), Vector3.forward, V(0, 2.5f), 1.5f);
        CamFrame(1);
        Chapter("I. THE SHARD MONK", "EVERY STONE IS A WEAPON");
        yield return WV(2.0f);
        Say("axe");
        boss.Perform("axe");
        yield return W(1.6f);
        brain.Dash(Vector3.right);
        yield return WaitBoss(6f);

        cam = Cam.HeroClose;
        yield return WV(0.6f);
        Say("gauntlet");
        brain.Power(4);
        yield return WV(Longer(Dur("gauntlet"), 2.6f));

        CamFrame(-1);
        boss.Perform("serpent");
        yield return W(1.5f);
        for (int k = 0; k < 3; k++) { brain.Light(); yield return W(0.22f); }
        if (boss.Act == "serpent") boss.Interrupt("WEAPON SHATTERED!", boss.WeaponPos);
        Say("serpent");
        yield return W(0.5f);
        brain.Go(Flat(boss.Pos));
        yield return WaitUntil(() => (HeroFlat - Flat(boss.Pos)).magnitude < 1.3f, 1.6f);
        brain.Stop();
        for (int k = 0; k < 4; k++) { brain.Light(); yield return W(0.3f); }
        yield return WaitBoss(6f);

        CamFrame(1);
        Fighter.demoAllowGrab = true;
        boss.Perform("grasp");
        yield return WaitUntil(() => hero.bossHeld, 3.5f);
        Fighter.demoAllowGrab = false;
        if (hero.bossHeld)
        {
            yield return W(0.5f);
            Say("grasp");
            powers.ResetCooldowns();
            brain.Power(1);
        }
        yield return WaitBoss(7f);
        yield return WaitHeroReady(4f);

        CamFrame(1);
        Say("lances");
        boss.Perform("lances");
        yield return Evade(new[] { V(-6, -5), V(5, -7), V(6, 1), V(-4, -2) }, 1.5f, 7f);

        CamFrame(-1);
        Say("roc");
        boss.Perform("roc");
        yield return Evade(new[] { V(-7, -3), V(0, -8), V(7, -3), V(3, 5), V(-5, 4) }, 1.6f, 12f);

        CamFrame(1);
        Say("hammer");
        boss.Perform("hammer");
        yield return Evade(new[] { V(6, -2), V(0, -7), V(-6, -2), V(-3, 5), V(4, 5) }, 1.1f, 9f);

        CamFrame(-1);
        Say("flood");
        boss.Perform("flood");
        yield return W(0.75f);
        brain.Dash(Flat(boss.Pos - hero.transform.position));
        yield return WaitBoss(5f);
        brain.Stop();
        yield return WV(0.8f);

        // ---------------------------------------------------------------- II. THE HUNT
        Cut(V(14.5f, 0), Vector3.right, V(3, 2), 1.5f);
        cam = Cam.Aerial;
        Chapter("II. THE HUNT", "RUN, AND HE SENDS THE STONES AFTER YOU");
        yield return WV(1.6f);
        boss.Perform("hunt");
        Say("hunt1");
        brain.Go(V(29.5f, 3.4f));
        yield return W(1.5f);
        cam = Cam.Chase;
        yield return WaitUntil(() => (HeroFlat - V(29.5f, 3.4f)).magnitude < 0.8f, 4f);
        // up the wall of the townhouse
        CamFollow(new Vector3(-5f, 1.4f, -6.5f));
        brain.Go(V(29.5f, 9f));
        yield return W(0.5f);
        brain.Jump();
        yield return W(1.7f);
        Say("hunt2");
        cam = Cam.Aerial;
        brain.Go(V(33.4f, 9f));
        yield return WaitUntil(() => hero.transform.position.x > 32.9f, 2.5f);
        // and up onto the taller roof next door
        brain.Go(V(38f, 9f));
        yield return W(0.3f);
        brain.Jump();
        yield return W(1.6f);
        brain.Go(V(40.8f, 9f));
        yield return WaitUntil(() => hero.transform.position.x > 40.2f, 2.5f);
        // drop back into the street
        CamFollow(new Vector3(-4f, 2f, -7f));
        brain.Go(V(41.2f, 2.5f));
        yield return WaitUntil(() => !hero.OnRoof && hero.CanAct, 3.5f);
        Say("hunt3");
        boss.ShowcaseSpot();
        cam = Cam.Front;
        brain.Go(V(22f, 0f));
        yield return W(2.2f);
        yield return WaitUntil(() => boss.AnyHunterNear(6f), 4f);
        brain.Stop();
        CamFrame(1);
        powers.ResetCooldowns();
        brain.Power(1);
        Say("hunt4");
        yield return WaitBoss(7f);
        yield return WV(0.6f);

        // ---------------------------------------------------------------- III. THE ARSENAL
        boss.SetPhase(true, false, false);
        boss.Hp = boss.MaxHp * 0.66f;
        Cut(V(20f, -1f), Vector3.right, V(27f, 1f), 2f);
        CamFrame(1);
        Chapter("III. THE ARSENAL", "HE ARMS HIMSELF");
        yield return WV(1.6f);
        boss.Perform("arsenal");
        Say("arsenal1");
        yield return WaitBoss(8f);
        // gatlings: sprint down the market street
        CamFollow(new Vector3(-1f, 3.2f, -9.5f));
        boss.Perform("gatling");
        Say("gatling");
        brain.Go(V(40f, -1.5f));
        yield return WaitBoss(7f);
        // rockets: out through the east gate and up the brewery wall
        boss.Perform("rockets");
        Say("rockets");
        cam = Cam.Chase;
        brain.Go(V(48f, -2f));
        yield return WaitUntil(() => hero.transform.position.x > 47.5f, 3f);
        CamFollow(new Vector3(-6.5f, 2.4f, -5.5f));
        brain.Go(V(54f, -3.8f));
        yield return WaitUntil(() => hero.transform.position.x > 51.2f, 1.5f);
        yield return W(0.15f);
        brain.Jump();
        yield return W(1.7f);
        cam = Cam.Aerial;
        brain.Go(V(58.5f, -3.8f));
        yield return WaitUntil(() => hero.transform.position.x > 58f, 2.5f);
        // leap across the lane to the townhouse roof
        CamFollow(new Vector3(7.5f, 3f, 2f));
        brain.Go(V(58.5f, -15f));
        yield return W(0.12f);
        brain.Jump();
        yield return W(1.8f);
        yield return WaitBoss(4f);
        // cannon: red circles on the rooftops
        CamFrame(-1);
        boss.Perform("rockets");
        brain.Go(V(56f, -12.5f));
        yield return W(2.2f);
        brain.Go(V(61f, -11.8f));
        yield return W(2.0f);
        brain.Go(V(56.5f, -13f));
        yield return WaitBoss(8f);
        brain.Stop();
        yield return WV(0.6f);

        // ---------------------------------------------------------------- IV. NOWHERE TO HIDE
        Cut(V(0f, 34.5f), Vector3.forward, V(0f, 29f), 1.5f);
        CamFixed(new Vector3(6.6f, 3.1f, 47.5f), new Vector3(-0.5f, 1.5f, 39f), false);
        Chapter("IV. NOWHERE TO HIDE", "INSIDE, THE STONES COME AFTER HIM");
        brain.Go(V(0f, 44f));
        yield return WaitUntil(() => hero.transform.position.z > 42.5f, 3.5f);
        brain.Stop();
        boss.Perform("gale");
        Say("indoor1");
        yield return W(1.0f);
        brain.block = true;
        yield return W(3.2f);
        Say("indoor2");
        yield return W(1.3f);
        brain.block = false;
        yield return null;
        powers.ResetCooldowns();
        brain.Power(1);
        yield return WaitBoss(6f);
        yield return WaitHeroReady(3f);
        // the seekers search the hall; Kaito hides behind the altar
        CamFixed(new Vector3(-6.6f, 3.1f, 47.5f), new Vector3(1f, 1.3f, 49f), false);
        brain.Go(V(1.8f, 49.8f), 0.7f);
        yield return WV(0.6f);
        boss.Perform("seekers");
        Say("seekers");
        yield return W(4.5f);
        Fighter.demoAllowGrab = true;
        boss.ShowcaseSpot();
        yield return WaitUntil(() => hero.bossHeld, 6f);
        if (hero.bossHeld) CamFixed(new Vector3(7.5f, 3.2f, 44f), new Vector3(0, 1.5f, 40f), true);
        yield return WaitBoss(12f);
        Fighter.demoAllowGrab = false;
        CamFrame(1);
        yield return WaitHeroReady(4f);
        yield return WV(0.5f);

        // ---------------------------------------------------------------- V. THE BLADE HAND
        Cut(V(3.5f, 30f), Vector3.left, V(-2.5f, 30f), 1.4f);
        CamFrame(1);
        Chapter("V. THE BLADE HAND", "KAITO AWAKENS");
        yield return WV(1.8f);
        boss.Perform("axe");
        yield return W(1.2f);
        powers.ResetCooldowns();
        brain.Power(3);
        Say("awaken");
        yield return WaitUntil(() => !powers.Busy, 12f);
        yield return WaitBoss(6f);
        yield return WV(0.4f);

        // ---------------------------------------------------------------- VI. THE GREAT DRAGON
        boss.SetPhase(true, true, true);
        Cut(V(0f, -22f), Vector3.back, V(0f, -15f), 2f);
        CamOrbit(V(0f, -25f), 20f, 9f, 6f, 6f, 20f);
        Chapter("VI. THE GREAT DRAGON", "");
        yield return WV(1.4f);
        boss.Perform("dragon");
        Say("dragon");
        brain.Go(V(0f, -33f), 0.7f);
        yield return W(5.0f);
        CamFrame(1);
        yield return W(1.0f);
        // out of the fire line
        {
            Vector3 run = Flat(hero.transform.position - boss.HeadPoint);
            run = run.sqrMagnitude > 0.01f ? run.normalized : Vector3.back;
            Vector3 sd = Vector3.Cross(Vector3.up, run);
            if (hero.transform.position.x + sd.x * 3f > 3f || hero.transform.position.x + sd.x * 3f < -3f) sd = -sd;
            brain.Dash(sd);
            brain.Go(HeroFlat + sd * 3.2f);
        }
        yield return W(2.6f);
        brain.Go(V(0f, -41f));
        yield return W(1.1f);
        brain.Dash(Vector3.back);
        yield return WaitBoss(6f);
        brain.Stop();
        yield return WV(0.5f);

        // ---------------------------------------------------------------- VII. THE LIVING TEMPLE
        boss.SetPhase(true, false, true);
        boss.Hp = boss.MaxHp * 0.3f;
        Cut(V(0f, -3.5f), Vector3.forward, V(0f, 4.5f), 1.5f);
        CamOrbit(V(0f, 1f), 17f, 5f, 4f, -6f, -60f);
        Chapter("VII. THE LIVING TEMPLE", "");
        yield return WV(2.4f);
        TimeFX.slow = 0.55f;
        boss.Perform("titan");
        Say("final1");
        yield return WV(Dur("final1") + 0.6f);
        CamOrbit(V(0, 1f), 19f, 8f, 5f, -7f, -80f);
        Say("final2");
        yield return W(1.5f);
        // Kaito runs; the half-built titan strides after him through its own storm
        brain.Go(V(-7f, -12f));
        yield return WV(Dur("final2") + 0.4f);
        CamFrame(1);
        Say("final3");
        yield return W(1.5f);
        brain.Go(V(7f, -11f));
        yield return WaitBoss(14f);
        TimeFX.slow = 1f;
        brain.Stop();
        CamOrbit(V(0, 0f), 20f, 6f, 6f, 8f, -100f);
        yield return W(1.2f);
        CamFrame(1);
        boss.Perform("titan_slam");
        yield return W(1.0f);
        brain.Dash(Vector3.Cross(Vector3.up, Flat(boss.Pos - hero.transform.position).normalized));
        yield return WaitBoss(4f);
        yield return W(0.6f);
        Say("final4");
        boss.Hp = 30;
        powers.ResetCooldowns();
        brain.Power(2);
        yield return WaitUntil(() => boss.Ended, 14f);
        yield return WV(1.6f);
        Say("final5");
        yield return WV(Mathf.Max(6.5f, Dur("final5") + 1.5f));
        hud.Announce("LAST STANCE", "", 4.5f, new Color(1f, 0.82f, 0.35f));
        Say("end");
        yield return WV(4.2f);
    }

    static float Longer(float a, float b) => Mathf.Max(a, b);

    // ================================================================== -finale: the whole fight live
    IEnumerator Act(string power, float max)
    {
        boss.Perform(power);
        yield return W(0.3f);
        yield return WaitBoss(max);
        if (boss.ShowBusy && boss.Act != "titan") boss.Interrupt("WEAPON SHATTERED!", boss.WeaponPos);   // keep the pace
        yield return WaitBoss(3f);
    }

    IEnumerator FinaleScript()
    {
        // the rise: Moros bursts out of the stone storm
        CamOrbit(V(0, 4.2f), 13f, 4.5f, 2.6f, 7f, -90f);
        dir.SnapOverride();
        yield return WaitBoss(16f);
        yield return W(0.3f);

        // ---------------------------------------------------------------- I. THE SHARD MONK
        Cut(V(0, -2.5f), Vector3.forward, V(0, 2.5f), 1.5f);
        CamFrame(1);
        Chapter("I. THE SHARD MONK", "EVERY STONE IS A WEAPON");
        yield return W(1.6f);
        boss.Perform("axe");
        yield return W(1.6f);
        brain.Dash(Vector3.right);
        yield return WaitBoss(5f);
        CamFrame(-1);
        Fighter.demoAllowGrab = true;
        boss.Perform("grasp");
        yield return WaitUntil(() => hero.bossHeld, 3.5f);
        Fighter.demoAllowGrab = false;
        if (hero.bossHeld) { yield return W(0.6f); powers.ResetCooldowns(); brain.Power(1); }
        yield return WaitBoss(6f);
        yield return WaitHeroReady(3f);
        CamFrame(1);
        boss.Perform("lances");
        yield return Evade(new[] { V(-6, -5), V(5, -7), V(6, 1), V(-4, -2) }, 1.4f, 6f);
        CamFrame(-1);
        boss.Perform("serpent");
        yield return Evade(new[] { V(5, -4), V(-5, -6), V(-6, 2) }, 1.3f, 5f);
        CamFrame(1);
        boss.Perform("roc");
        yield return Evade(new[] { V(-7, -3), V(0, -8), V(7, -3), V(3, 5) }, 1.5f, 8f);
        CamFrame(-1);
        boss.Perform("hammer");
        yield return Evade(new[] { V(6, -2), V(0, -7), V(-6, -2) }, 1.1f, 6f);
        CamFrame(1);
        boss.Perform("flood");
        yield return W(0.75f);
        brain.Dash(Flat(boss.Pos - hero.transform.position));
        yield return WaitBoss(4f);
        brain.Stop();

        // ---------------------------------------------------------------- II. THE ARSENAL
        boss.SetPhase(true, false, true);
        Cut(V(0, -3f), Vector3.forward, V(0, 3f), 2f);
        CamOrbit(V(0, 1f), 13f, 4f, 3f, 9f, -70f);
        Chapter("II. THE ARSENAL", "HE ARMS HIMSELF WITH THE TEMPLE");
        yield return W(1.2f);
        boss.Perform("arsenal");
        yield return WaitBoss(7f);
        CamFrame(1);
        boss.Perform("gatling");
        yield return Evade(new[] { V(-6, -4), V(6, -6), V(5, 3) }, 1.4f, 6f);
        CamFrame(-1);
        boss.Perform("rockets");
        yield return Evade(new[] { V(5, -5), V(-6, -6), V(-5, 3) }, 1.3f, 6f);
        Cut(V(0f, -22f), Vector3.back, V(0f, -15f), 2f);
        CamOrbit(V(0f, -25f), 20f, 9f, 6f, 6f, 20f);
        yield return W(0.8f);
        boss.Perform("dragon");
        brain.Go(V(0f, -33f), 0.7f);
        yield return W(4.5f);
        CamFrame(1);
        yield return WaitBoss(7f);
        brain.Stop();

        // ---------------------------------------------------------------- III. THE ENTHIRAN FORMS
        Cut(V(0, -4f), Vector3.forward, V(0, 4f), 2f);
        CamFrame(1);
        Chapter("III. THE ENTHIRAN FORMS", "THE WHOLE TOWN BECOMES HIS WEAPON");
        yield return W(1.6f);
        boss.Perform("sphere");
        yield return Evade(new[] { V(-7, -5), V(6, -8), V(8, 2), V(-6, 3), V(0, -9) }, 1.5f, 13f);
        yield return WaitBoss(2f);
        CamFrame(-1);
        Fighter.demoAllowGrab = true;   // let the cobra swallow him
        brain.Stop();
        boss.Perform("cobra");
        yield return W(0.3f);
        yield return WaitBoss(22f);
        Fighter.demoAllowGrab = false;
        yield return WaitHeroReady(4f);
        CamFrame(1);
        boss.Perform("drill");
        yield return Evade(new[] { V(-5, -4), V(5, -6), V(6, 2), V(-4, 4) }, 1.6f, 9f);
        if (boss.ShowBusy) boss.Interrupt("WEAPON SHATTERED!", boss.WeaponPos);
        yield return WaitBoss(3f);
        CamFrame(-1);
        boss.Perform("wall");
        yield return W(3.2f);
        brain.Go(V(8f, -2f));
        yield return WaitBoss(9f);
        brain.Stop();
        yield return WaitHeroReady(3f);

        // ---------------------------------------------------------------- IV. THE TITAN
        boss.SetPhase(true, false, true);
        boss.Hp = boss.MaxHp * 0.3f;
        Cut(V(0f, -3.5f), Vector3.forward, V(0f, 4.5f), 1.5f);
        CamOrbit(V(0f, 1f), 17f, 5f, 4f, -6f, -60f);
        Chapter("IV. THE LIVING TEMPLE", "");
        yield return W(1.4f);
        boss.Perform("titan");
        yield return W(2.5f);
        CamOrbit(V(0, 1f), 19f, 8f, 5f, -7f, -80f);
        brain.Go(V(-7f, -12f));
        yield return W(3f);
        CamFrame(1);
        brain.Go(V(7f, -11f));
        yield return WaitBoss(10f);
        brain.Stop();
        CamOrbit(V(0, 0f), 20f, 6f, 6f, 8f, -100f);
        yield return W(0.8f);
        CamFrame(1);
        boss.Perform("titan_slam");
        yield return W(1.0f);
        brain.Dash(Vector3.Cross(Vector3.up, Flat(boss.Pos - hero.transform.position).normalized));
        yield return WaitBoss(4f);
        yield return W(0.4f);
        boss.Hp = 30;
        powers.ResetCooldowns();
        brain.Power(2);   // the Guardian Spirit ends it
        yield return WaitUntil(() => boss.Ended, 14f);
        yield return W(2f);
        hud.Announce("LAST STANCE", "", 5f, new Color(1f, 0.82f, 0.35f));
    }

    // run a path while a weapon is out; dash away whenever it gets close
    IEnumerator Evade(Vector3[] path, float legTime, float max)
    {
        int k = 0;
        float leg = 0, dashCd = 0;
        for (float x = 0; x < max && boss.ShowBusy && !boss.Ended; x += Time.deltaTime)
        {
            leg -= Time.deltaTime;
            dashCd -= Time.deltaTime;
            if (leg <= 0) { brain.Go(path[k++ % path.Length]); leg = legTime; }
            if (dashCd <= 0 && boss.WeaponOut && (boss.WeaponPos - HeroChest).magnitude < 3.2f)
            {
                Vector3 away = Flat(HeroChest - boss.WeaponPos);
                if (away.sqrMagnitude < 0.01f) away = HeroDir();
                brain.Dash(Vector3.Cross(Vector3.up, away.normalized) + away.normalized * 0.5f);
                dashCd = 1.1f;
            }
            yield return null;
        }
        brain.Stop();
    }
}

// The trailer's stunt double: a brain the director drives - run to a point, jump, dash, punch,
// block, use a power.
public class ShowcaseBrain : Brain
{
    Vector3? target;
    float speed = 1;
    public bool block;
    bool jump, dash, light, heavy;
    int power;
    Vector3 dashDir;

    public void Go(Vector3 t, float spd = 1) { target = t; speed = spd; }
    public void Stop() => target = null;
    public void Jump() => jump = true;
    public void Dash(Vector3 d) { dash = true; dashDir = d; }
    public void Light() => light = true;
    public void Heavy() => heavy = true;
    public void Power(int n) => power = n;

    public override Intent Think(Fighter self, Fighter foe, float dt)
    {
        var i = new Intent();
        if (target.HasValue)
        {
            Vector3 to = target.Value - self.transform.position;
            to.y = 0;
            if (to.magnitude > 0.3f) i.move = to.normalized * speed;
        }
        if (dash) { i.dash = true; if (dashDir.sqrMagnitude > 0.01f) i.move = new Vector3(dashDir.x, 0, dashDir.z).normalized; dash = false; }
        i.jump = jump; jump = false;
        i.light = light; light = false;
        i.heavy = heavy; heavy = false;
        i.power = power; power = 0;
        i.block = block;
        return i;
    }
}
