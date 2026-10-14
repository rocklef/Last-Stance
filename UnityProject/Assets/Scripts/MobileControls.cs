using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Phone controls, designed for two thumbs:
//   left thumb   a floating joystick wherever it lands on the left side
//   right thumb  ATTACK (big): tap for combos, hold for a heavy finisher
//                DODGE: a quick dash that slips through attacks
//                BLOCK: hold to guard, tap to parry; BLOCK held + ATTACK = THROW
//                CLIMB / LEAP: only appears when there is a wall to run up or a roof edge to jump
//                PALM / SPIRIT / BLADE: the hero powers (Moros fight only), arced round ATTACK with
//                cooldown sweeps; the Stone Fists come on by themselves
//   swipe        anywhere free on the right turns the camera (it eases back behind Kaito)
//   II           pause
// Nothing shows on menus or during cutscenes. Hits taken buzz the phone. PlayerBrain,
// BossDirector and GameManager read the static state. Active on phones, or on PC with -touch.
[DefaultExecutionOrder(-100)]
public class MobileControls : MonoBehaviour
{
    public static bool On { get; private set; }
    public static Vector2 Move;
    public static Vector2 Look;
    public static bool Light, Heavy, Dash, Jump, Grab, Block, Face, Pause, Tap;
    public static int Power;

    static bool mouseAsTouch;

    enum Kind { Attack, Dodge, Block, Jump, P1, P2, P3, Pause }

    class Btn
    {
        public Kind kind;
        public RectTransform rt;
        public Image disc, ring, sweep, art;
        public Sprite artMain, artAlt, artClimb;
        // the power loader: a ring that fills with light as it recharges, glitter riding its edge
        public Image loaderTrack, loaderFill;
        public RectTransform loaderRt;
        public Image[] sparks;
        public Vector2[] sparkPos, sparkVel;
        public float[] sparkLife, sparkSeed;
        public bool wasReady = true;
        public float flashT;
        public Text label;
        public float radius;
        public int finger = -1;
        public bool held, pressed, visible = true;
        public float heldT;
        public bool heavyFired;
    }

    readonly List<Btn> buttons = new List<Btn>();
    RectTransform root, safe, stickBase, stickKnob;
    Image baseImg, knobImg;
    CanvasGroup group, pauseGroup;
    int stickFinger = -1, lookFinger = -1;
    Vector2 stickOrigin, lookLast;
    Vector2 stickHome = new Vector2(320, 290);
    const float StickRadius = 150, HoldForHeavy = 0.3f;
    float scale = 1, lastHp = -1, pulseT;
    Rect lastSafe;
    static Sprite disc, ring, thickRing, star;
    const int Sparks = 10;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        mouseAsTouch = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-touch") >= 0;
        if (!Application.isMobilePlatform && !mouseAsTouch) return;
        if (FindAnyObjectByType<MobileControls>() != null) return;
        var go = new GameObject("MobileControls");
        DontDestroyOnLoad(go);
        go.AddComponent<MobileControls>();
        On = true;
    }

    void Start()
    {
        MakeSprites();
        var cgo = new GameObject("TouchCanvas");
        cgo.transform.SetParent(transform);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;   // sized by the screen height: the same thumb size on every phone
        root = cgo.GetComponent<RectTransform>();
        group = cgo.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0;

        // everything thumb-sized sits inside the safe area (clear of the camera hole and rounded corners)
        var sgo = new GameObject("Safe", typeof(RectTransform));
        sgo.transform.SetParent(root, false);
        safe = sgo.GetComponent<RectTransform>();
        ApplySafeArea();

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var hud = GameManager.I != null ? GameManager.I.hud : null;
        if (hud != null && hud.display != null) font = hud.display;

        // painted art (tools/ui/make_ui.py -> Resources/UI); plain circles if it is missing
        var sb = Art("stick_base"); var sk = Art("stick_knob");
        stickBase = Circle("StickBase", sb ?? ring, new Color(1, 1, 1, 0.3f), sb ? 2 * StickRadius / 0.9f : 2 * StickRadius, Vector2.zero, out baseImg, root);
        stickKnob = Circle("StickKnob", sk ?? disc, new Color(1, 1, 1, 0.4f), sk ? 2 * 68 / 0.62f : 130, Vector2.zero, out knobImg, root);
        painted = sb != null;

        var red = new Color(1f, 0.42f, 0.22f);
        var blue = new Color(0.45f, 0.78f, 1f);
        var pale = new Color(0.92f, 0.92f, 0.95f);
        Vector2 atk = new Vector2(-265, 245);
        Add(Kind.Attack, "ATTACK", atk, 128, red, font, new Vector2(1, 0));
        Add(Kind.Dodge, "DODGE", new Vector2(-92, 100), 76, blue, font, new Vector2(1, 0));
        Add(Kind.Block, "BLOCK", new Vector2(-530, 118), 84, pale, font, new Vector2(1, 0));
        Add(Kind.Jump, "JUMP", new Vector2(-105, 440), 72, new Color(0.55f, 1f, 0.7f), font, new Vector2(1, 0));
        // the powers arc round the attack button, within reach of the same thumb
        string[] names = { "PALM", "SPIRIT", "BLADE" };
        float[] ang = { 90f, 130f, 170f };
        for (int k = 0; k < 3; k++)
        {
            float a = ang[k] * Mathf.Deg2Rad;
            Add(Kind.P1 + k, names[k], atk + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 222f, 60, blue, font, new Vector2(1, 0));
        }
        // pause: small, top centre under the timer (clear of both health bars)
        Add(Kind.Pause, "II", new Vector2(0, -150), 40, pale, font, new Vector2(0.5f, 1));
        var pb = buttons[buttons.Count - 1];
        var pcgo = new GameObject("PauseCanvas");
        pcgo.transform.SetParent(transform);
        var pc = pcgo.AddComponent<Canvas>();
        pc.renderMode = RenderMode.ScreenSpaceOverlay;
        pc.sortingOrder = 31;
        var ps = pcgo.AddComponent<CanvasScaler>();
        ps.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2(1920, 1080);
        ps.matchWidthOrHeight = 1f;
        pb.rt.SetParent(pcgo.transform, false);   // its own canvas: it stays up while the game is paused
        pauseGroup = pcgo.AddComponent<CanvasGroup>();
        pauseGroup.blocksRaycasts = false;
        pauseGroup.alpha = 0;
    }

    void ApplySafeArea()
    {
        Rect sa = Screen.safeArea;
        if (sa == lastSafe || Screen.width <= 0) return;
        lastSafe = sa;
        safe.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
        safe.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
        safe.offsetMin = safe.offsetMax = Vector2.zero;
    }

    bool painted;

    static Sprite Art(string name)
    {
        var t = Resources.Load<Texture2D>("UI/" + name);
        return t != null ? Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f)) : null;
    }

    static string ArtName(Kind k) => k switch
    {
        Kind.Attack => "btn_attack", Kind.Dodge => "btn_dodge", Kind.Block => "btn_block", Kind.Jump => "btn_jump",
        Kind.P1 => "btn_palm", Kind.P2 => "btn_spirit", Kind.P3 => "btn_blade", _ => "btn_pause",
    };

    void Add(Kind k, string label, Vector2 pos, float r, Color c, Font font, Vector2 anchor)
    {
        var b = new Btn { kind = k, radius = r };
        b.rt = Circle(k.ToString(), disc, new Color(0.04f, 0.04f, 0.07f, 0.45f), 2 * r, anchor, out b.disc, safe);
        b.rt.anchoredPosition = pos;
        b.artMain = Art(ArtName(k));
        if (k == Kind.Jump) { b.artAlt = Art("btn_leap"); b.artClimb = Art("btn_climb"); }
        if (b.artMain != null)
        {
            // the painted stone: its disc fills 80% of the sprite, the rest is the ember glow
            var ar = Circle("Art", b.artMain, Color.white, 2 * r / 0.8f, new Vector2(0.5f, 0.5f), out b.art, b.rt);
            ar.anchoredPosition = Vector2.zero;
            b.disc.color = Color.clear;
        }
        if (k >= Kind.P1 && k <= Kind.P3)
        {
            float lr = r * 2.5f;
            b.loaderRt = Circle("Loader", thickRing, new Color(0.3f, 0.6f, 1f, 0.22f), lr, new Vector2(0.5f, 0.5f), out b.loaderTrack, b.rt);
            b.loaderRt.anchoredPosition = Vector2.zero;
            var lf = Circle("Fill", thickRing, Color.white, lr, new Vector2(0.5f, 0.5f), out b.loaderFill, b.loaderRt);
            lf.anchoredPosition = Vector2.zero;
            b.loaderFill.type = Image.Type.Filled;
            b.loaderFill.fillMethod = Image.FillMethod.Radial360;
            b.loaderFill.fillOrigin = (int)Image.Origin360.Top;
            b.loaderFill.fillClockwise = true;
            b.sparks = new Image[Sparks];
            b.sparkPos = new Vector2[Sparks];
            b.sparkVel = new Vector2[Sparks];
            b.sparkLife = new float[Sparks];
            b.sparkSeed = new float[Sparks];
            for (int i = 0; i < Sparks; i++)
            {
                var sp = Circle("Spark", star, Color.white, 26, new Vector2(0.5f, 0.5f), out b.sparks[i], b.loaderRt);
                b.sparkSeed[i] = Random.value * 10f;
                b.sparks[i].enabled = false;
            }
        }
        if (k >= Kind.P1 && k <= Kind.P3)
        {
            // the cooldown: a dark sweep that unwinds as the power recharges
            var sw = Circle("Sweep", disc, new Color(0, 0, 0, 0.45f), 2 * r, new Vector2(0.5f, 0.5f), out b.sweep, b.rt);
            sw.anchoredPosition = Vector2.zero;
            b.sweep.type = Image.Type.Filled;
            b.sweep.fillMethod = Image.FillMethod.Radial360;
            b.sweep.fillOrigin = (int)Image.Origin360.Top;
            b.sweep.fillClockwise = false;
            b.sweep.fillAmount = 0;
        }
        var rg = Circle("Ring", ring, new Color(c.r, c.g, c.b, 0.9f), 2 * r, new Vector2(0.5f, 0.5f), out b.ring, b.rt);
        rg.anchoredPosition = Vector2.zero;
        if (b.art != null) rg.gameObject.SetActive(false);
        var tgo = new GameObject("Label", typeof(RectTransform));
        tgo.transform.SetParent(b.rt, false);
        var t = tgo.AddComponent<Text>();
        t.font = font;
        t.text = label;
        t.alignment = TextAnchor.MiddleCenter;
        t.fontSize = Mathf.RoundToInt(Mathf.Clamp(r * 0.36f, 20, 44));
        t.color = new Color(1, 1, 1, 0.95f);
        t.raycastTarget = false;
        var ol = tgo.AddComponent<Outline>();
        ol.effectColor = new Color(0, 0, 0, 0.7f);
        ol.effectDistance = new Vector2(1.5f, -1.5f);
        var trt = tgo.GetComponent<RectTransform>();
        trt.sizeDelta = new Vector2(2 * r, 2 * r);
        trt.anchoredPosition = Vector2.zero;
        b.label = t;
        if (b.art != null) tgo.SetActive(false);   // the name is painted into the art
        buttons.Add(b);
    }

    RectTransform Circle(string name, Sprite s, Color c, float size, Vector2 anchor, out Image img, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        img = go.AddComponent<Image>();
        img.sprite = s;
        img.color = c;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        return rt;
    }

    static void MakeSprites()
    {
        if (disc != null) return;
        disc = Sprite.Create(Tex(false), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
        ring = Sprite.Create(Tex(true), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
        thickRing = Sprite.Create(RingTex(256, 0.86f, 0.98f), new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f));
        star = Sprite.Create(StarTex(64), new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
    }

    static Texture2D RingTex(int n, float inner, float outer)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float c = n / 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c)) / c;
                float a = Mathf.Clamp01((d - inner) * n * 0.5f) * Mathf.Clamp01((outer - d) * n * 0.5f);
                float core = 1 - Mathf.Abs(d - (inner + outer) * 0.5f) / ((outer - inner) * 0.5f);   // bright centre line
                byte v = (byte)(200 + 55 * Mathf.Clamp01(core));
                px[y * n + x] = new Color32(v, v, v, (byte)(a * 255));
            }
        t.SetPixels32(px);
        t.Apply();
        return t;
    }

    // a four-point twinkle with a soft halo
    static Texture2D StarTex(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float c = n / 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - c) / c, dy = Mathf.Abs(y + 0.5f - c) / c;
                float cross = Mathf.Clamp01(1 - dx * 8) * Mathf.Clamp01(1 - dy) + Mathf.Clamp01(1 - dy * 8) * Mathf.Clamp01(1 - dx);
                float halo = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy) * 2.2f);
                float a = Mathf.Clamp01(cross * 1.2f + halo * halo * 0.8f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        t.SetPixels32(px);
        t.Apply();
        return t;
    }

    static Texture2D Tex(bool isRing)
    {
        var t = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[128 * 128];
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64));
                float a = isRing ? Mathf.Clamp01(1 - Mathf.Abs(d - 59f) / 3.5f) : Mathf.Clamp01(62f - d);
                px[y * 128 + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        t.SetPixels32(px);
        t.Apply();
        return t;
    }

    struct Finger { public int id; public Vector2 pos; public bool began, down; }
    readonly List<Finger> fingers = new List<Finger>(10);

    void ReadFingers()
    {
        fingers.Clear();
        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var t in ts.touches)
            {
                bool down = t.press.isPressed;
                if (!down && !t.press.wasReleasedThisFrame) continue;
                fingers.Add(new Finger { id = t.touchId.ReadValue(), pos = t.position.ReadValue(), began = t.press.wasPressedThisFrame, down = down });
            }
        }
        if (mouseAsTouch && Mouse.current != null)
        {
            var m = Mouse.current;
            if (m.leftButton.isPressed || m.leftButton.wasReleasedThisFrame)
                fingers.Add(new Finger { id = 999, pos = m.position.ReadValue(), began = m.leftButton.wasPressedThisFrame, down = m.leftButton.isPressed });
        }
    }

    Vector2 StickCentreScreen => stickBase != null ? CentreOnScreen(stickBase) : stickHome * scale;
    static Vector2 CentreOnScreen(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.position);

    // is Kaito in the player's hands right now (not a menu, an intro, a KO or a cutscene)?
    static Fighter Hero => GameManager.I != null ? GameManager.I.p1 : null;
    static bool Live
    {
        get
        {
            var h = Hero;
            return h != null && h.gameObject.activeInHierarchy && !h.inputLocked && !TimeFX.paused;
        }
    }

    void Update()
    {
        if (root == null) return;
        ApplySafeArea();
        scale = root.localScale.x;
        Light = Heavy = Dash = Jump = Grab = Face = Pause = Tap = false;
        Power = 0;
        Look = Vector2.zero;
        ReadFingers();
        bool live = Live;
        var hud = GameManager.I != null ? GameManager.I.hud : null;
        float alpha = live ? (hud != null ? hud.GameplayAlpha : 1) : 0;
        group.alpha = Mathf.MoveTowards(group.alpha, alpha, Time.unscaledDeltaTime * 4f);
        pauseGroup.alpha = live || TimeFX.paused ? 1 : 0;

        // which buttons exist right now
        var powers = HeroPowers.Active;
        bool bossFight = powers != null && powers.isActiveAndEnabled;
        string ctx = bossFight && live ? ClimbContext() : null;
        foreach (var b in buttons)
        {
            bool vis = b.kind switch
            {
                Kind.Jump => true,
                Kind.P1 or Kind.P2 or Kind.P3 => bossFight,
                _ => true,
            };
            if (b.kind == Kind.Jump)
            {
                b.label.text = ctx ?? "JUMP";
                if (b.art != null)
                    b.art.sprite = ctx == "LEAP" && b.artAlt != null ? b.artAlt : ctx == "CLIMB" && b.artClimb != null ? b.artClimb : b.artMain;
            }
            if (vis != b.visible) { b.visible = vis; b.rt.gameObject.SetActive(vis); if (!vis) { b.held = false; b.finger = -1; } }
            b.pressed = false;
        }

        foreach (var f in fingers)
        {
            if (f.began)
            {
                Tap = true;
                Btn hit = null;
                float best = float.MaxValue;
                foreach (var b in buttons)
                {
                    bool usable = b.kind == Kind.Pause ? live || TimeFX.paused : live;
                    if (!b.visible || !usable) continue;
                    float d = Vector2.Distance(f.pos, CentreOnScreen(b.rt));
                    float reach = b.radius * scale * 1.2f;   // forgiving hit areas
                    if (d < reach && d < best) { best = d; hit = b; }
                }
                if (hit != null) { hit.finger = f.id; hit.held = true; hit.pressed = true; hit.heldT = 0; hit.heavyFired = false; continue; }
                if (!live) continue;
                if (f.pos.x < Screen.width * 0.45f && stickFinger < 0) { stickFinger = f.id; stickOrigin = StickCentreScreen; continue; }
                if (lookFinger < 0) { lookFinger = f.id; lookLast = f.pos; }
                continue;
            }
            if (f.id == stickFinger)
            {
                if (!f.down) { stickFinger = -1; continue; }
                Vector2 d = (f.pos - StickCentreScreen) / (StickRadius * scale);
                if (d.magnitude > 1) d = d.normalized;   // the stick never moves; the knob stops at its rim
                Move = d.magnitude < 0.12f ? Vector2.zero : d;   // a small dead zone: resting thumbs don't creep
                continue;
            }
            if (f.id == lookFinger)
            {
                if (!f.down) { lookFinger = -1; continue; }
                Look += (f.pos - lookLast) / Mathf.Max(0.01f, scale);
                lookLast = f.pos;
                continue;
            }
            foreach (var b in buttons)
                if (b.finger == f.id && !f.down) { b.finger = -1; b.held = false; }
        }
        if (stickFinger >= 0 && !Present(stickFinger)) stickFinger = -1;
        if (lookFinger >= 0 && !Present(lookFinger)) lookFinger = -1;
        foreach (var b in buttons) if (b.finger >= 0 && !Present(b.finger)) { b.finger = -1; b.held = false; }
        if (stickFinger < 0 || !live) Move = Vector2.zero;
        if (!live) { stickFinger = lookFinger = -1; }

        Block = false;
        bool blockHeld = false;
        foreach (var b in buttons) if (b.kind == Kind.Block && b.held) blockHeld = true;
        foreach (var b in buttons)
        {
            if (b.kind == Kind.Pause) { if (b.pressed) Pause = true; continue; }
            if (!live) continue;
            switch (b.kind)
            {
                case Kind.Attack:
                    if (b.pressed) { if (blockHeld) Grab = true; else Light = true; }
                    if (b.held && !blockHeld)
                    {
                        b.heldT += Time.unscaledDeltaTime;
                        if (!b.heavyFired && b.heldT >= HoldForHeavy) { Heavy = true; b.heavyFired = true; }   // hold: the heavy finisher
                    }
                    break;
                case Kind.Dodge: if (b.pressed) Dash = true; break;
                case Kind.Block: Block = b.held; break;
                case Kind.Jump: if (b.pressed) Jump = true; break;
                case Kind.P1: if (b.pressed) Power = 1; break;
                case Kind.P2: if (b.pressed) Power = 2; break;
                case Kind.P3: if (b.pressed) Power = 3; break;
            }
        }

        foreach (var b in buttons) if (b.loaderRt != null && b.visible && powers != null) Loader(b, powers.Ready(b.kind - Kind.P1 + 1));
        // cooldown sweeps; a ready power's ring glows
        foreach (var b in buttons)
        {
            if (b.sweep == null || !b.visible || powers == null) continue;
            float ready = powers.Ready(b.kind - Kind.P1 + 1);
            b.sweep.fillAmount = 1 - ready;
            float glowA = ready >= 1 ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f) : 0.35f;
            var c = b.ring.color; c.a = glowA; b.ring.color = c;
            if (b.art != null)
            {
                // recharging: dimmed stone; ready: it breathes
                float v = ready >= 1 ? 1f : 0.55f;
                b.art.color = new Color(v, v, v, 1);
                if (ready >= 1 && !b.held) b.art.rectTransform.localScale = Vector3.one * (1 + 0.035f * Mathf.Sin(Time.unscaledTime * 4f));
                else b.art.rectTransform.localScale = Vector3.one;
            }
        }

        // the stick sits under the thumb while it is held
        Vector2 basePos = stickHome;
        stickBase.anchoredPosition = basePos;
        stickKnob.anchoredPosition = basePos + Move * StickRadius;
        baseImg.color = new Color(1, 1, 1, painted ? (stickFinger >= 0 ? 0.95f : 0.55f) : stickFinger >= 0 ? 0.5f : 0.25f);
        knobImg.color = new Color(1, 1, 1, painted ? (stickFinger >= 0 ? 1f : 0.7f) : stickFinger >= 0 ? 0.65f : 0.35f);
        foreach (var b in buttons)
        {
            if (b.art == null) b.disc.color = b.held ? new Color(0.3f, 0.3f, 0.36f, 0.6f) : new Color(0.04f, 0.04f, 0.07f, 0.45f);
            else if (b.sweep == null) b.art.color = b.held ? Color.white : new Color(0.9f, 0.9f, 0.9f, 1);
            b.rt.localScale = Vector3.one * (b.held ? 0.9f : 1f);   // pressed: the stone sinks
        }

        Rumble(live);
    }

    // RECHARGING: the ring fills clockwise in spirit-blue light that shimmers toward white, glitter
    // riding its leading edge and twinkling along what is filled. READY: a white flash and a burst of
    // sparkles, then one glint keeps circling the ring.
    void Loader(Btn b, float ready)
    {
        float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
        float lr = b.radius * 1.25f * 0.92f;   // the ring's centre line, in the button's local pixels
        bool full = ready >= 1f;
        if (full && !b.wasReady)
        {
            b.flashT = 1f;
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2 + Random.value * 0.3f;
                b.sparkPos[i] = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * lr;
                b.sparkVel[i] = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Random.Range(70f, 120f);
                b.sparkLife[i] = Random.Range(0.35f, 0.5f);
            }
        }
        b.wasReady = full;
        b.flashT = Mathf.Max(0, b.flashT - dt * 2.2f);
        float shimmer = 0.5f + 0.5f * Mathf.Sin(t * 7f + b.radius);
        Color blue = new Color(0.35f, 0.72f, 1f), gold = new Color(1f, 0.85f, 0.45f);
        b.loaderFill.fillAmount = full ? 1 : ready;
        Color fc = full ? Color.Lerp(blue, Color.white, 0.25f + 0.2f * shimmer) : Color.Lerp(blue, Color.white, 0.15f + 0.45f * shimmer);
        fc = Color.Lerp(fc, Color.white, b.flashT);
        fc.a = full ? 0.55f + 0.2f * shimmer + 0.45f * b.flashT : 0.85f;
        b.loaderFill.color = fc;
        b.loaderTrack.color = new Color(0.3f, 0.6f, 1f, full ? 0.1f : 0.22f);
        b.loaderRt.localScale = Vector3.one * (1f + 0.12f * b.flashT);
        float head = ready * Mathf.PI * 2;
        for (int i = 0; i < Sparks; i++)
        {
            var sp = b.sparks[i];
            float seed = b.sparkSeed[i];
            if (b.sparkLife[i] > 0)
            {
                // burst sparkles fly out and fade
                b.sparkLife[i] -= dt;
                b.sparkPos[i] += b.sparkVel[i] * dt;
                b.sparkVel[i] *= 1 - 2.5f * dt;
                sp.enabled = true;
                sp.rectTransform.anchoredPosition = b.sparkPos[i];
                float k = Mathf.Clamp01(b.sparkLife[i] / 0.5f);
                sp.rectTransform.localScale = Vector3.one * (0.4f + 0.5f * k);
                sp.color = new Color(1f, 0.95f, 0.8f, k);
                sp.rectTransform.localRotation = Quaternion.Euler(0, 0, t * 200f + seed * 50);
                continue;
            }
            float ang, rad = lr, alpha, size;
            Color col;
            if (!full)
            {
                if (i >= 3) { sp.enabled = false; continue; }   // just a touch of glitter
                if (i < 2)
                {
                    // glitter crowding the leading edge of the fill
                    ang = head - (i * 0.09f + 0.04f * Mathf.Sin(t * 13f + seed)) ;
                    rad = lr + Mathf.Sin(t * 17f + seed * 3f) * b.radius * 0.07f;
                    alpha = 0.7f + 0.3f * Mathf.Sin(t * 20f + seed);
                    size = (i == 0 ? 0.85f : 0.5f) * (0.85f + 0.15f * Mathf.Sin(t * 15f + seed));
                    alpha *= i == 0 ? 0.9f : 0.5f;
                    col = Color.Lerp(Color.white, gold, i * 0.25f);
                }
                else
                {
                    // twinkles scattered along the filled arc, each blinking on its own clock
                    float phase = Mathf.Repeat(t * 0.9f + seed, 1f);
                    ang = Mathf.Repeat(seed * 7.3f + Mathf.Floor(t * 0.9f + seed) * 2.1f, 1f) * head;
                    alpha = Mathf.Sin(phase * Mathf.PI) * 0.55f;
                    size = 0.35f + 0.3f * alpha;
                    col = Color.Lerp(Color.white, blue, 0.3f);
                    if (ready < 0.05f) alpha = 0;
                }
            }
            else
            {
                // ready: one bright glint circling, a couple of faint followers
                if (i > 0) { sp.enabled = false; continue; }
                ang = t * 1.8f;
                alpha = 0.7f;
                size = 0.65f;
                col = Color.Lerp(Color.white, gold, 0.35f);
            }
            sp.enabled = alpha > 0.02f;
            sp.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * rad;
            sp.rectTransform.localScale = Vector3.one * size;
            sp.rectTransform.localRotation = Quaternion.Euler(0, 0, t * 90f + seed * 40);
            col.a = Mathf.Clamp01(alpha);
            sp.color = col;
        }
    }

    // a wall ahead to run up, or a roof edge to leap from
    static string ClimbContext()
    {
        var h = Hero;
        var town = TownMap.I;
        if (h == null || town == null || !h.CanAct) return null;
        Vector3 dir = h.transform.forward;
        if (Move.sqrMagnitude > 0.04f && Camera.main != null)
        {
            var q = Quaternion.Euler(0, Camera.main.transform.eulerAngles.y, 0);
            dir = (q * new Vector3(Move.x, 0, Move.y)).normalized;
        }
        if (town.ClimbTarget(h.transform.position, dir, h.Y, out _, out _)) return "CLIMB";
        if (h.OnRoof) return "LEAP";
        return null;
    }

    // the phone buzzes when Kaito is hurt: harder for bigger hits
    void Rumble(bool live)
    {
        var h = Hero;
        pulseT -= Time.unscaledDeltaTime;
        if (h == null || !live) { lastHp = h != null ? h.health : -1; return; }
        if (lastHp >= 0 && h.health < lastHp - 0.5f && pulseT <= 0)
        {
            float lost = lastHp - h.health;
            Haptics.Pulse(lost >= 12 ? 70 : lost >= 5 ? 40 : 22, lost >= 12 ? 255 : 150);
            pulseT = 0.12f;
        }
        lastHp = h.health;
    }

    bool Present(int id)
    {
        foreach (var f in fingers) if (f.id == id) return true;
        return false;
    }
}

// short, sharp vibrations (Android's VibrationEffect); nothing on other platforms
public static class Haptics
{
    static AndroidJavaObject vib;
    static bool tried;

    public static void Pulse(long ms, int amplitude = 160)
    {
#if UNITY_ANDROID || UNITY_IOS
        if (Time.frameCount < 0) Handheld.Vibrate();   // never runs: tells the Android build to request the vibrate permission
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (!tried)
            {
                tried = true;
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                vib = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            if (vib == null) return;
            using var effect = new AndroidJavaClass("android.os.VibrationEffect");
            var shot = effect.CallStatic<AndroidJavaObject>("createOneShot", ms, Mathf.Clamp(amplitude, 1, 255));
            vib.Call("vibrate", shot);
        }
        catch (System.Exception) { vib = null; }
#endif
    }
}
