using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// Fighting-game HUD built at runtime: health bars with trailing damage, round pips, timer,
// combo counter with style rank, announcer text and a controls hint.
public class HUD : MonoBehaviour
{
    public Font display;   // Bangers
    public Font body;      // Anton

    class Side
    {
        public Fighter f;
        public Image hp, trail, stamina;
        public Text toast;
        public float toastT;
        public Image[] pips = new Image[2];
        public Text combo, hits, rank, dmg, moveName;
        public RectTransform comboRoot;
        public CanvasGroup comboGroup;
        public float comboShow, pop, trailValue = 1;
        public int lastCombo;
        public Text name, tag;
    }

    // Swap a bar over to a new opponent (story boss).
    public void Rebrand(int side, string displayName, string title, Color accent)
    {
        var s = sides[side];
        s.name.text = displayName;
        s.tag.text = title;
        s.tag.color = accent;
        s.trailValue = 1;
    }

    Side[] sides;
    Text timer, announce, sub, hint;
    RectTransform announceRt;
    float announceT, announceDur, announceScale;
    Canvas canvas;

    public void Build(Fighter a, Fighter b, string hintText)
    {
        var go = new GameObject("HUDCanvas");
        go.transform.SetParent(transform);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        rootRt = go.GetComponent<RectTransform>();
        // everything that is gameplay (bars, timer, powers, markers) fades away during cutscenes
        var gp = new GameObject("Gameplay", typeof(RectTransform), typeof(CanvasGroup));
        gameplayRt = gp.GetComponent<RectTransform>();
        gameplayRt.SetParent(go.transform, false);
        gameplayRt.anchorMin = Vector2.zero; gameplayRt.anchorMax = Vector2.one;
        gameplayRt.offsetMin = gameplayRt.offsetMax = Vector2.zero;
        gameplayGroup = gp.GetComponent<CanvasGroup>();

        sides = new[] { MakeSide(a, true), MakeSide(b, false) };

        var tbox = Img(gameplayRt, "TimerBox", new Color(0.05f, 0.03f, 0.03f, 0.85f), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(128, 96));
        Img(tbox.transform, "TimerEdge", new Color(0.85f, 0.65f, 0.25f, 1), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(128, 5));
        timer = Label(tbox.transform, "Timer", body, 66, TextAnchor.MiddleCenter, Color.white, Vector2.zero, new Vector2(128, 96), new Vector2(0.5f, 0.5f));

        announce = Label(go.transform, "Announce", display, 190, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f), new Vector2(0, 80), new Vector2(1600, 260), new Vector2(0.5f, 0.5f));
        Stroke(announce, new Color(0.35f, 0.02f, 0.01f), 5);
        announceRt = announce.rectTransform;
        sub = Label(go.transform, "Sub", body, 44, TextAnchor.MiddleCenter, Color.white, new Vector2(0, -70), new Vector2(1600, 80), new Vector2(0.5f, 0.5f));
        Stroke(sub, Color.black, 2);
        hint = Label(gameplayRt, "Hint", body, 24, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.75f), new Vector2(0, 30), new Vector2(1800, 40), new Vector2(0.5f, 0));
        Stroke(hint, new Color(0, 0, 0, 0.8f), 1);
        hint.text = hintText;
        announce.text = sub.text = "";

        Fighter.Hit += OnHit;
        Fighter.Callout += OnCallout;
        BuildTracker(gameplayRt);
        BuildCinematic();
    }

    // ------------------------------------------------------------------ cutscenes
    RectTransform rootRt, gameplayRt;
    CanvasGroup gameplayGroup;
    public float GameplayAlpha => gameplayGroup != null ? gameplayGroup.alpha : 1;
    Image barTop, barBottom;
    Text cineSpeaker, cineLine, cineSkip;
    float letterbox, letterboxWant, gameplayWant = 1, lineT;

    void BuildCinematic()
    {
        barTop = Img(rootRt, "LetterboxTop", Color.black, new Vector2(0.5f, 1), Vector2.zero, new Vector2(4000, 0), new Vector2(0.5f, 1));
        barBottom = Img(rootRt, "LetterboxBottom", Color.black, new Vector2(0.5f, 0), Vector2.zero, new Vector2(4000, 0), new Vector2(0.5f, 0));
        cineSpeaker = Label(rootRt, "CineSpeaker", display, 34, TextAnchor.MiddleCenter, Color.white, new Vector2(0, 104), new Vector2(1400, 44), new Vector2(0.5f, 0));
        Stroke(cineSpeaker, Color.black, 1.5f);
        cineLine = Label(rootRt, "CineLine", body, 38, TextAnchor.MiddleCenter, Color.white, new Vector2(0, 58), new Vector2(1600, 54), new Vector2(0.5f, 0));
        Stroke(cineLine, Color.black, 1.5f);
        cineSkip = Label(rootRt, "CineSkip", body, 22, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.55f), new Vector2(-40, 30), new Vector2(400, 30), new Vector2(1, 0), new Vector2(1, 0.5f));
        cineSpeaker.text = cineLine.text = cineSkip.text = "";
    }

    // letterbox bars in, gameplay HUD out (and back)
    public void Cinematic(bool on)
    {
        letterboxWant = on ? 1 : 0;
        gameplayWant = on ? 0 : 1;
    }

    public bool InCinematic => letterboxWant > 0.5f;
    public void SkipHint(bool on) { if (cineSkip) cineSkip.text = on ? (MobileControls.On ? "TAP  TO  SKIP" : "ENTER  SKIP") : ""; }

    // on a phone, keyboard keys in any on-screen text become the touch buttons
    public static string Phone(string s)
    {
        if (!MobileControls.On || string.IsNullOrEmpty(s)) return s;
        return s.Replace("JUMP (SPACE)", "JUMP").Replace("(SPACE)", "(JUMP)").Replace("PRESS ENTER", "TAP").Replace("ENTER", "TAP")
                .Replace("R-CTRL", "BLOCK").Replace("SHIFT", "DODGE").Replace("SPACE", "JUMP");
    }

    // a spoken line in the lower bar: the speaker's name in their colour, the line under it
    public void Dialogue(string speaker, Color c, string line, float dur)
    {
        if (cineLine == null) return;
        cineSpeaker.text = speaker;
        cineSpeaker.color = c;
        cineLine.text = line;
        lineT = dur;
    }

    void UpdateCinematic(float dt)
    {
        if (barTop == null) return;
        letterbox = Mathf.MoveTowards(letterbox, letterboxWant, dt * 2.2f);
        float h = 118f * Ease.InOut(letterbox);
        barTop.rectTransform.sizeDelta = new Vector2(4000, h);
        barBottom.rectTransform.sizeDelta = new Vector2(4000, h);
        gameplayGroup.alpha = Mathf.MoveTowards(gameplayGroup.alpha, gameplayWant, dt * 3f);
        lineT -= dt;
        float a = Mathf.Clamp01(lineT / 0.3f);
        cineLine.color = new Color(1, 1, 1, a);
        var sc = cineSpeaker.color; sc.a = a; cineSpeaker.color = sc;
    }

    // ------------------------------------------------------------------ hero power bar
    // Round icons along the bottom: a ring around each one fills back up while the power recharges
    // (no numbers - the ring is the timer); a ready power glows and pulses, a toggle shows ON.
    class PowerIcon
    {
        public RectTransform root;
        public Image disc, track, ring;
        public Text name, key, state;
        public Color color;
        public float flash;
    }
    readonly List<PowerIcon> powers = new List<PowerIcon>();
    RectTransform powerRoot;
    static Sprite ringSprite, discSprite;

    static Sprite MakeCircle(bool ring)
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x + 0.5f - N / 2f, dy = y + 0.5f - N / 2f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(N / 2f - 1 - r);                     // outer edge
                if (ring) a = Mathf.Min(a, Mathf.Clamp01(r - (N / 2f - 12)));  // inner edge
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100);
    }

    public void BuildPowers(string[] keys, string[] names, Color[] colors)
    {
        if (ringSprite == null) ringSprite = MakeCircle(true);
        if (discSprite == null) discSprite = MakeCircle(false);
        var go = new GameObject("PowerBar", typeof(RectTransform));
        powerRoot = go.GetComponent<RectTransform>();
        powerRoot.SetParent(canvasRt, false);
        powerRoot.anchorMin = powerRoot.anchorMax = new Vector2(0.5f, 0);
        powerRoot.anchoredPosition = new Vector2(0, 150);
        powerRoot.sizeDelta = Vector2.zero;
        int n = keys.Length;
        for (int i = 0; i < n; i++)
        {
            var ic = new PowerIcon { color = colors[i] };
            var r = new GameObject("Power" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(powerRoot, false);
            r.anchoredPosition = new Vector2((i - (n - 1) / 2f) * 150f, 0);
            r.sizeDelta = new Vector2(120, 120);
            ic.root = r;
            ic.disc = Img(r, "Disc", new Color(0.04f, 0.04f, 0.07f, 0.78f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104, 104));
            ic.disc.sprite = discSprite;
            ic.track = Img(r, "Track", new Color(1, 1, 1, 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118, 118));
            ic.track.sprite = ringSprite;
            ic.ring = Img(r, "Ring", colors[i], new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118, 118));
            ic.ring.sprite = ringSprite;
            ic.ring.type = Image.Type.Filled;
            ic.ring.fillMethod = Image.FillMethod.Radial360;
            ic.ring.fillOrigin = (int)Image.Origin360.Top;
            ic.ring.fillClockwise = true;
            ic.name = Label(r, "Name", display, 23, TextAnchor.MiddleCenter, Color.white, new Vector2(0, 4), new Vector2(110, 70), new Vector2(0.5f, 0.5f));
            ic.name.text = names[i];
            ic.name.lineSpacing = 0.8f;
            Stroke(ic.name, Color.black, 1.2f);
            var kb = Img(r, "KeyBox", new Color(0.05f, 0.05f, 0.08f, 0.9f), new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(Mathf.Max(34, keys[i].Length * 15 + 16), 30));
            ic.key = Label(kb.transform, "Key", body, 20, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.6f), Vector2.zero, kb.rectTransform.sizeDelta, new Vector2(0.5f, 0.5f));
            ic.key.text = keys[i];
            ic.state = Label(r, "State", body, 18, TextAnchor.MiddleCenter, colors[i], new Vector2(0, 66), new Vector2(120, 26), new Vector2(0.5f, 0.5f));
            Stroke(ic.state, Color.black, 1f);
            ic.state.text = "";
            powers.Add(ic);
        }
        go.SetActive(false);
    }

    public void ShowPowers(bool on) { if (powerRoot) powerRoot.gameObject.SetActive(on); }

    // ready01: how far the recharge ring has filled; active: a toggle that is switched on
    public void SetPower(int i, float ready01, bool active, string stateText = null)
    {
        if (i < 0 || i >= powers.Count) return;
        var ic = powers[i];
        bool ready = ready01 >= 0.999f;
        if (ready && ic.ring.fillAmount < 0.999f) ic.flash = 1;   // just recharged
        ic.ring.fillAmount = ready01;
        float pulse = ready ? 0.5f + 0.5f * Mathf.Sin(Clock.Now * 5f + i) : 0;
        var c = ic.color;
        ic.ring.color = ready ? Color.Lerp(c, Color.white, 0.25f * pulse + ic.flash * 0.6f) : new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.9f);
        ic.disc.color = active ? new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.9f) : new Color(0.04f, 0.04f, 0.07f, ready ? 0.82f : 0.6f);
        ic.name.color = ready || active ? Color.white : new Color(1, 1, 1, 0.45f);
        ic.root.localScale = Vector3.one * (1f + (ready ? 0.04f * pulse : 0) + ic.flash * 0.25f);
        ic.state.text = stateText ?? (active ? "ON" : "");
        ic.flash = Mathf.MoveTowards(ic.flash, 0, Clock.Dt * 2.5f);
    }

    // ------------------------------------------------------------------ narration subtitles (showcase)
    Text subtitle;
    Image subtitleBox;
    float subtitleT;

    public void Subtitle(string text, float dur)
    {
        text = Phone(text);
        if (subtitle == null)
        {
            subtitleBox = Img(rootRt, "SubtitleBox", new Color(0, 0, 0, 0.5f), new Vector2(0.5f, 0), new Vector2(0, 268), new Vector2(1400, 62));
            subtitle = Label(subtitleBox.transform, "Subtitle", body, 36, TextAnchor.MiddleCenter, Color.white, Vector2.zero, new Vector2(1380, 62), new Vector2(0.5f, 0.5f));
            Stroke(subtitle, Color.black, 1.5f);
        }
        subtitle.text = text;
        subtitleT = dur;
        subtitleBox.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(text.Length * 17f + 60, 300, 1600), 62);
        subtitle.rectTransform.sizeDelta = subtitleBox.rectTransform.sizeDelta;
    }

    void UpdateSubtitle(float dt)
    {
        if (subtitle == null) return;
        subtitleT -= dt;
        float a = Mathf.Clamp01(subtitleT / 0.35f);
        subtitle.color = new Color(1, 1, 1, a);
        subtitleBox.color = new Color(0, 0, 0, 0.5f * a);
    }

    // ------------------------------------------------------------------ boss tracker
    // A marker over Moros; when he is off screen it sticks to the screen edge and points to him,
    // with his distance. Hunting swarms that are chasing Kaito get smaller red edge markers.
    [System.NonSerialized] public bool trackOn;
    [System.NonSerialized] public Vector3 trackPos;
    [System.NonSerialized] public readonly List<Vector3> threats = new List<Vector3>();
    RectTransform canvasRt;
    Marker boss;
    readonly List<Marker> threatMarks = new List<Marker>();

    class Marker
    {
        public RectTransform root, arrow;
        public Image dot, tip;
        public Text label;
    }

    Marker MakeMarker(Transform parent, Color c, float size, bool labelled)
    {
        var m = new Marker();
        var go = new GameObject("Marker", typeof(RectTransform));
        m.root = go.GetComponent<RectTransform>();
        m.root.SetParent(parent, false);
        m.root.anchorMin = m.root.anchorMax = new Vector2(0.5f, 0.5f);
        m.root.sizeDelta = Vector2.one * size;
        m.dot = Img(m.root, "Dot", c, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * size);
        m.dot.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        Img(m.dot.transform, "Core", new Color(0.1f, 0.02f, 0.01f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * size * 0.45f);
        var ag = new GameObject("Arrow", typeof(RectTransform));
        m.arrow = ag.GetComponent<RectTransform>();
        m.arrow.SetParent(m.root, false);
        m.arrow.sizeDelta = Vector2.zero;
        // an arrow head: two bars meeting in a point, sitting just outside the diamond
        for (int k = -1; k <= 1; k += 2)
        {
            var bar = Img(m.arrow, "Head", c, new Vector2(0.5f, 0.5f), new Vector2(size * 0.95f, k * size * 0.22f), new Vector2(size * 0.62f, size * 0.2f), new Vector2(1f, 0.5f));
            bar.rectTransform.localRotation = Quaternion.Euler(0, 0, -k * 40);
            if (m.tip == null) m.tip = bar;
        }
        if (labelled)
        {
            m.label = Label(m.root, "Label", body, 26, TextAnchor.MiddleCenter, c, new Vector2(0, -size * 1.25f), new Vector2(260, 34), new Vector2(0.5f, 0.5f));
            Stroke(m.label, Color.black, 1.5f);
        }
        go.SetActive(false);
        return m;
    }

    void BuildTracker(Transform parent)
    {
        canvasRt = parent as RectTransform;
        boss = MakeMarker(parent, new Color(1f, 0.45f, 0.12f), 30, true);
    }

    // places a marker; returns false when it is not needed
    void Place(Marker m, Vector3 world, bool onScreenToo, string text)
    {
        var cam = Camera.main;
        if (cam == null) { m.root.gameObject.SetActive(false); return; }
        Vector3 sp = cam.WorldToScreenPoint(world);
        bool behind = sp.z < 0;
        if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
        // the edge band markers live in: clear of the health bars at the top and the hint at the bottom
        float px = Screen.height / 1080f, mx = 70f * px, mTop = 230f * px, mBot = 110f * px;
        Vector2 half = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f + (mBot - mTop) * 0.5f);
        Vector2 v = new Vector2(sp.x, sp.y) - half;
        Vector2 lim = new Vector2(Screen.width * 0.5f - mx, Screen.height * 0.5f - (mTop + mBot) * 0.5f);
        bool off = behind || Mathf.Abs(v.x) > lim.x || Mathf.Abs(v.y) > lim.y;
        if (!off && !onScreenToo) { m.root.gameObject.SetActive(false); return; }
        m.root.gameObject.SetActive(true);
        if (off)
        {
            if (behind && v.sqrMagnitude < 1f) v = Vector2.down;
            float s = Mathf.Min(lim.x / Mathf.Max(0.001f, Mathf.Abs(v.x)), lim.y / Mathf.Max(0.001f, Mathf.Abs(v.y)));
            if (behind || s < 1) v *= s;
        }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, half + v, null, out var local);
        m.root.anchoredPosition = local;
        m.arrow.gameObject.SetActive(off);
        if (off) m.arrow.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
        float pulse = off ? 1f + 0.18f * Mathf.Sin(Clock.Now * 8f) : 0.8f;
        m.dot.rectTransform.localScale = Vector3.one * pulse;
        if (m.label != null)
        {
            m.label.text = text;
            // keep the label inside the screen, on the side away from the edge
            m.label.rectTransform.anchoredPosition = off && v.y < -lim.y + 1 ? new Vector2(0, 40) : new Vector2(0, -40);
            var c = m.label.color; c.a = off ? 1f : 0.7f; m.label.color = c;
        }
    }

    void UpdateTracker()
    {
        if (boss == null) return;
        var cam = Camera.main;
        if (!trackOn || cam == null)
        {
            boss.root.gameObject.SetActive(false);
            foreach (var t in threatMarks) t.root.gameObject.SetActive(false);
            return;
        }
        Vector3 me = sides != null && sides[0].f != null ? sides[0].f.Body : cam.transform.position;
        float dist = Vector3.Distance(new Vector3(me.x, 0, me.z), new Vector3(trackPos.x, 0, trackPos.z));
        Place(boss, trackPos, true, "MOROS  " + Mathf.RoundToInt(dist) + "m");
        while (threatMarks.Count < threats.Count) threatMarks.Add(MakeMarker(canvasRt, new Color(1f, 0.15f, 0.1f), 20, false));
        for (int i = 0; i < threatMarks.Count; i++)
        {
            if (i < threats.Count) Place(threatMarks[i], threats[i], false, null);
            else threatMarks[i].root.gameObject.SetActive(false);
        }
    }

    void OnDestroy()
    {
        Fighter.Hit -= OnHit;
        Fighter.Callout -= OnCallout;
    }

    void OnCallout(Fighter f, string text, Color c)
    {
        foreach (var s in sides)
        {
            if (s.f != f) continue;
            s.toast.text = text;
            s.toast.color = c;
            s.toastT = 1.2f;
        }
    }

    Side MakeSide(Fighter f, bool left)
    {
        var s = new Side { f = f };
        float sx = left ? 1 : -1;
        var anchor = new Vector2(left ? 0 : 1, 1);
        Transform root = gameplayRt;

        var frame = Img(root, "BarFrame", new Color(0.04f, 0.02f, 0.02f, 0.88f), anchor, new Vector2(sx * 60, -38), new Vector2(760, 50), pivot: new Vector2(left ? 0 : 1, 1));
        Img(frame.transform, "BarEdge", new Color(0.85f, 0.65f, 0.25f, 1), new Vector2(0.5f, 0), Vector2.zero, new Vector2(760, 4));
        s.trail = Img(frame.transform, "Trail", new Color(1f, 0.95f, 0.85f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(744, 34));
        s.hp = Img(frame.transform, "HP", new Color(1f, 0.78f, 0.1f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(744, 34));
        foreach (var im in new[] { s.trail, s.hp })
        {
            im.type = Image.Type.Filled;
            im.fillMethod = Image.FillMethod.Horizontal;
            im.fillOrigin = left ? (int)Image.OriginHorizontal.Left : (int)Image.OriginHorizontal.Right;
            im.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
        }
        var shine = Img(frame.transform, "Shine", new Color(1, 1, 1, 0.18f), new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(744, 8));
        shine.raycastTarget = false;

        var sframe = Img(root, "StaminaFrame", new Color(0.04f, 0.02f, 0.02f, 0.85f), anchor, new Vector2(sx * 60, -92), new Vector2(420, 14), pivot: new Vector2(left ? 0 : 1, 1));
        s.stamina = Img(sframe.transform, "Stamina", new Color(0.3f, 0.9f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(412, 8));
        s.stamina.type = Image.Type.Filled;
        s.stamina.fillMethod = Image.FillMethod.Horizontal;
        s.stamina.fillOrigin = left ? (int)Image.OriginHorizontal.Left : (int)Image.OriginHorizontal.Right;
        s.stamina.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);

        var name = Label(root, "Name", body, 40, left ? TextAnchor.UpperLeft : TextAnchor.UpperRight, Color.white,
            new Vector2(sx * 64, -110), new Vector2(600, 60), anchor, new Vector2(left ? 0 : 1, 1));
        name.text = f.displayName.ToUpper();
        Stroke(name, Color.black, 2);
        var tag = Label(root, "Tag", body, 22, left ? TextAnchor.UpperLeft : TextAnchor.UpperRight, f.accent,
            new Vector2(sx * 66, -156), new Vector2(600, 40), anchor, new Vector2(left ? 0 : 1, 1));
        tag.text = f.isBrute ? "THE IRON FIST" : "THE BLADE HAND";
        Stroke(tag, Color.black, 1);
        s.name = name; s.tag = tag;

        for (int i = 0; i < 2; i++)
        {
            var pip = Img(root, "Pip", new Color(0.15f, 0.12f, 0.1f, 0.9f), new Vector2(0.5f, 1), new Vector2(-sx * (110 + i * 36), -110), new Vector2(22, 22));
            pip.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            s.pips[i] = pip;
        }

        var cgo = new GameObject("Combo", typeof(RectTransform), typeof(CanvasGroup));
        s.comboRoot = cgo.GetComponent<RectTransform>();
        s.comboRoot.SetParent(root, false);
        s.comboRoot.anchorMin = s.comboRoot.anchorMax = new Vector2(left ? 0 : 1, 0.5f);
        s.comboRoot.pivot = new Vector2(left ? 0 : 1, 0.5f);
        s.comboRoot.anchoredPosition = new Vector2(sx * 70, 150);
        s.comboRoot.sizeDelta = new Vector2(420, 300);
        s.comboGroup = cgo.GetComponent<CanvasGroup>();
        s.comboGroup.alpha = 0;
        var ta = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        var pv = new Vector2(left ? 0 : 1, 0.5f);
        var an = new Vector2(left ? 0 : 1, 0.5f);
        s.combo = Label(s.comboRoot, "Count", display, 130, ta, Color.white, new Vector2(0, 60), new Vector2(260, 140), an, pv);
        Stroke(s.combo, new Color(0.3f, 0.02f, 0.02f), 4);
        s.hits = Label(s.comboRoot, "Hits", body, 34, ta, new Color(1f, 0.85f, 0.4f), new Vector2(sx * 118, 30), new Vector2(200, 50), an, pv);
        s.hits.text = "HITS";
        Stroke(s.hits, Color.black, 2);
        s.rank = Label(s.comboRoot, "Rank", display, 96, ta, Color.white, new Vector2(sx * 205, 62), new Vector2(200, 130), an, pv);
        Stroke(s.rank, Color.black, 4);
        s.dmg = Label(s.comboRoot, "Dmg", body, 26, ta, new Color(1, 1, 1, 0.85f), new Vector2(0, -22), new Vector2(360, 40), an, pv);
        Stroke(s.dmg, Color.black, 1);
        s.moveName = Label(s.comboRoot, "MoveName", display, 46, ta, new Color(1f, 0.35f, 0.25f), new Vector2(0, -68), new Vector2(460, 60), an, pv);
        Stroke(s.moveName, Color.black, 3);
        s.toast = Label(root, "Toast", display, 78, left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, Color.white,
            new Vector2(sx * 70, -60), new Vector2(700, 110), new Vector2(left ? 0 : 1, 0.5f), new Vector2(left ? 0 : 1, 0.5f));
        Stroke(s.toast, Color.black, 4);
        s.toast.text = "";
        return s;
    }

    void OnHit(Fighter attacker, Fighter victim, Move m, float dmg, Vector3 p)
    {
        foreach (var s in sides)
        {
            if (s.f != attacker) continue;
            s.comboShow = 1.6f;
            if (attacker.combo >= 2)
            {
                s.combo.text = attacker.combo.ToString();
                var (letter, col) = Rank(attacker.combo);
                s.rank.text = letter;
                s.rank.color = col;
                s.dmg.text = $"{attacker.comboDamage:0} DAMAGE";
                s.pop = 1;
            }
            if (m.heavy || m.knockdown) s.moveName.text = m.name.ToUpper() + "!";
            else if (attacker.combo < 2) s.moveName.text = "";
        }
    }

    public static (string, Color) Rank(int hits)
    {
        if (hits >= 11) return ("SSS", new Color(1f, 0.15f, 0.12f));
        if (hits >= 9) return ("SS", new Color(1f, 0.45f, 0.1f));
        if (hits >= 7) return ("S", new Color(1f, 0.8f, 0.15f));
        if (hits >= 5) return ("A", new Color(0.8f, 0.4f, 1f));
        if (hits >= 4) return ("B", new Color(0.3f, 0.6f, 1f));
        if (hits >= 3) return ("C", new Color(0.3f, 0.9f, 0.4f));
        return ("D", new Color(0.75f, 0.75f, 0.75f));
    }

    public void SetWins(int a, int b)
    {
        for (int i = 0; i < 2; i++)
        {
            sides[0].pips[i].color = i < a ? new Color(1f, 0.75f, 0.2f) : new Color(0.15f, 0.12f, 0.1f, 0.9f);
            sides[1].pips[i].color = i < b ? new Color(1f, 0.75f, 0.2f) : new Color(0.15f, 0.12f, 0.1f, 0.9f);
        }
    }

    public void SetTimer(float t) => timer.text = Mathf.CeilToInt(Mathf.Max(0, t)).ToString();

    public void Announce(string big, string small = "", float dur = 1.4f, Color? color = null)
    {
        big = Phone(big); small = Phone(small);
        SetCompact(false);
        announce.text = big;
        sub.text = small;
        announce.color = color ?? new Color(1f, 0.85f, 0.3f);
        announceT = 0;
        announceDur = dur;
    }

    public void ShowHint(bool on) => hint.enabled = on;

    // Boss attack calls: the same announcer, but smaller and up under the health bars so it never
    // covers the action it is warning about.
    bool compact;
    public void BossCall(string big, string small, float dur, Color color)
    {
        big = Phone(big); small = Phone(small);
        Announce(big, small, dur, color);
        SetCompact(true);
    }

    void SetCompact(bool on)
    {
        compact = on;
        announce.fontSize = on ? 88 : 190;
        announceRt.anchoredPosition = on ? new Vector2(0, 300) : new Vector2(0, 80);
        sub.fontSize = on ? 30 : 44;
        sub.rectTransform.anchoredPosition = on ? new Vector2(0, 232) : new Vector2(0, -70);
    }

    void Update()
    {
        float dt = Clock.Dt;
        UpdateTracker();
        UpdateSubtitle(dt);
        UpdateCinematic(dt);
        foreach (var s in sides)
        {
            float v = s.f.health / s.f.maxHealth;
            s.hp.fillAmount = v;
            s.hp.color = v < 0.3f ? Color.Lerp(new Color(1f, 0.2f, 0.1f), new Color(1f, 0.55f, 0.1f), Mathf.PingPong(Clock.Now * 3, 1)) : new Color(1f, 0.78f, 0.1f);
            if (s.trailValue > v && !TimeFX.Stopped) s.trailValue = Mathf.MoveTowards(s.trailValue, v, dt * 0.45f);
            if (s.trailValue < v) s.trailValue = v;
            s.trail.fillAmount = s.trailValue;
            s.trail.color = Color.Lerp(new Color(1f, 0.2f, 0.15f), new Color(1f, 0.95f, 0.85f), Mathf.PingPong(Clock.Now * 6, 1));

            float st = s.f.stamina / s.f.maxStamina;
            s.stamina.fillAmount = st;
            s.stamina.color = s.f.Exhausted ? Color.Lerp(new Color(1f, 0.2f, 0.1f), new Color(0.4f, 0.1f, 0.1f), Mathf.PingPong(Clock.Now * 4, 1))
                            : st < 0.3f ? new Color(1f, 0.6f, 0.2f) : new Color(0.3f, 0.9f, 0.75f);

            s.toastT -= dt;
            float ta = Mathf.Clamp01(s.toastT / 0.3f);
            var tc = s.toast.color; tc.a = ta; s.toast.color = tc;
            s.toast.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.8f, Ease.In(Mathf.Clamp01((s.toastT - 1.05f) / 0.15f)));

            s.comboShow -= dt;
            bool showCount = s.f.combo >= 2;
            s.combo.enabled = s.hits.enabled = s.rank.enabled = s.dmg.enabled = showCount;
            s.comboGroup.alpha = Mathf.Clamp01(s.comboShow * 3);
            s.pop = Mathf.MoveTowards(s.pop, 0, dt * 6);
            s.comboRoot.localScale = Vector3.one * (1 + 0.25f * Ease.OutCubic(s.pop));
        }

        if (announceDur > 0)
        {
            announceT += dt;
            float k = announceT / 0.16f;
            announceRt.localScale = Vector3.one * Mathf.Lerp(compact ? 1.5f : 2.4f, 1f, Ease.OutBack(Mathf.Clamp01(k)));
            float a = announceT > announceDur ? 1 - (announceT - announceDur) / 0.3f : 1;
            var c = announce.color; c.a = Mathf.Clamp01(a); announce.color = c;
            var c2 = sub.color; c2.a = Mathf.Clamp01(a); sub.color = c2;
            if (a <= 0) { announce.text = sub.text = ""; announceDur = 0; }
        }
    }

    // ------------------------------------------------------------------ builders
    static Image Img(Transform parent, string name, Color c, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var im = go.GetComponent<Image>();
        im.color = c;
        return im;
    }

    static Text Label(Transform parent, string name, Font f, int size, TextAnchor align, Color c, Vector2 pos, Vector2 box, Vector2 anchor, Vector2? pivot = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        var t = go.GetComponent<Text>();
        t.font = f;
        t.fontSize = size;
        t.alignment = align;
        t.color = c;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void Stroke(Text t, Color c, float d)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = c;
        o.effectDistance = new Vector2(d, -d);
        var s = t.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0, 0, 0, 0.6f);
        s.effectDistance = new Vector2(d + 2, -d - 2);
    }
}
