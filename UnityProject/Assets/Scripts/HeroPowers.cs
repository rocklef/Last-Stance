using System.Collections;
using UnityEngine;

// Kaito's powers in the fight with Moros. Wall runs and the Stone Gauntlets are always available;
// the three great powers recharge for a minute after each use (the ring around the icon fills back
// up - there is no number on screen).
//   SPACE  WALL RUN / ROOFTOP LEAP   run up a wall onto the roofs and leap across them (Fighter)
//   1      THUNDER PALM             a charged palm into the ground: a lightning shockwave that
//                                   shatters every shard formation, hunter and projectile around him
//                                   (it also breaks him free of a grab)
//   2      GUARDIAN SPIRIT          a giant blue spirit of light rises behind him and punches Moros
//                                   three times - giant against giant; the last punch crumbles the titan
//   3      BLADE HAND AWAKENING     hands ignite blue, time slows, and he tears into a pinned Moros with
//                                   a cinematic strike sequence (flying knee, hurricane kick, capoeira
//                                   kicks, uppercut) that sends him flying
//   4      STONE GAUNTLETS          stones wrap his fists: +60% damage, and a punch that lands on one of
//                                   Moros's weapons shatters it (and cracks the titan's legs)
[DefaultExecutionOrder(10)]
public class HeroPowers : MonoBehaviour
{
    public const float Recharge = 20f;
    static readonly Color Blue = new Color(0.45f, 0.75f, 1f);
    static readonly Color Pale = new Color(0.8f, 0.92f, 1f);

    Fighter hero;
    MonkBoss boss;
    HUD hud;
    FX fx;
    BossDirector director;
    readonly float[] cd = new float[4];
    bool busy, gauntlets;
    ShardSwarm stones;
    Transform handL, handR, knuckL, knuckR;
    readonly GameObject[] glow = new GameObject[2];
    float autoT = 6f, liveT;

    public bool Busy => busy;
    public static HeroPowers Active { get; private set; }
    // 0 just used .. 1 ready (powers 1-3)
    public float Ready(int i) => i >= 1 && i <= 3 ? 1 - Mathf.Clamp01(cd[i] / Recharge) : 1;
    float fistsT = 2f;
    public bool OwnsCamera { get; private set; }   // the Guardian and the Awakening stage their own shots
    public void ResetCooldowns() { for (int i = 0; i < cd.Length; i++) cd[i] = 0; }
    public bool GauntletsOn => gauntlets;

    public void Begin(MonkBoss b)
    {
        Active = this;
        boss = b;
        hero = GetComponent<Fighter>();
        hud = GameManager.I.hud;
        fx = GameManager.I.fx;
        director = GameManager.I.cam.director;
        var an = hero.animator;
        handL = an.GetBoneTransform(HumanBodyBones.LeftHand);
        handR = an.GetBoneTransform(HumanBodyBones.RightHand);
        knuckL = an.GetBoneTransform(HumanBodyBones.LeftMiddleProximal) ?? handL;
        knuckR = an.GetBoneTransform(HumanBodyBones.RightMiddleProximal) ?? handR;
        hud.BuildPowers(new[] { "SPACE", "1", "2", "3", "4" },
                        new[] { "WALL\nRUN", "THUNDER\nPALM", "GUARDIAN\nSPIRIT", "BLADE\nHAND", "STONE\nFISTS" },
                        new[] { new Color(0.55f, 0.85f, 1f), new Color(0.5f, 0.8f, 1f), new Color(0.4f, 0.65f, 1f), new Color(0.35f, 0.9f, 1f), new Color(0.85f, 0.75f, 0.6f) });
        hud.ShowPowers(!MobileControls.On);   // phones show the powers on their own buttons

        stones = new GameObject("StoneGauntlets").AddComponent<ShardSwarm>();
        stones.Init(GameManager.I.shardMats, 340, hero.transform.position, 1.2f);
        stones.boundRadius = 110;
        stones.chipScale = 0.42f;
        stones.dust = 0;
        stones.mode = ShardSwarm.Mode.Free;
        stones.builder = s =>
        {
            if (!gauntlets) return;
            ShardForms.Gauntlet(s.parts, handL.position, knuckL.position, hero.transform.up);
            ShardForms.Gauntlet(s.parts, handR.position, knuckR.position, hero.transform.up);
        };
        MakeGlow();
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        if (stones) Destroy(stones.gameObject);
    }

    void Update()
    {
        if (boss == null || hero == null) return;
        float dt = Time.deltaTime;
        // phones: the Stone Fists are simply on (one button fewer)
        if (MobileControls.On && !gauntlets && !busy && !hero.inputLocked && (fistsT -= dt) <= 0) ToggleGauntlets();
        liveT += dt;
        for (int i = 1; i < cd.Length; i++) cd[i] = Mathf.Max(0, cd[i] - dt);
        int p = hero.lastIntent.power;
        if (GameManager.I.demo) p = 0;   // demo mode fights with fists and feet only - no powers
        if (p != 0 && !busy && !boss.Ended && !hero.inputLocked && hero.health > 0) TryUse(p);

        hud.SetPower(0, 1, false, hero.OnRoof ? "ROOFTOPS" : "");
        for (int i = 1; i <= 3; i++) hud.SetPower(i, 1 - cd[i] / Recharge, false);
        hud.SetPower(4, 1, gauntlets);
    }

    void TryUse(int p)
    {
        switch (p)
        {
            case 1:
                if (cd[1] <= 0 && (hero.CanAct || hero.bossHeld || hero.state == Fighter.State.Hitstun)) StartCoroutine(Palm());
                break;
            case 2:
                if (cd[2] <= 0 && hero.CanAct && boss.CanBeInterrupted) StartCoroutine(Guardian());
                break;
            case 3:
                if (cd[3] <= 0 && hero.CanAct && boss.CanBeInterrupted) StartCoroutine(Awakening());
                break;
            case 4:
                ToggleGauntlets();
                break;
        }
    }

    // capture runs: use every power when it makes sense so the screenshots show them
    int AutoPick(float dt)
    {
        autoT -= dt;
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-titan") >= 0) return 0;
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-enthiran") >= 0) return 0;
        if (autoT > 0 || busy || !hero.CanAct) return 0;
        if (!gauntlets && liveT > 4) { autoT = 2; return 4; }
        bool holdGuardian = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-titan") >= 0;
        if (cd[2] <= 0 && boss.IsTitan && !holdGuardian) { autoT = 5; return 2; }
        if (cd[1] <= 0 && boss.WeaponOut) { autoT = 5; return 1; }
        if (cd[3] <= 0 && liveT > 40 && boss.WeaponOut) { autoT = 5; return 3; }
        if (cd[2] <= 0 && liveT > 70) { autoT = 5; return 2; }
        return 0;
    }

    Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    Vector3 Feet => new Vector3(hero.transform.position.x, hero.floorY, hero.transform.position.z);
    Vector3 Chest => hero.Body + Vector3.up * 1.2f;

    Vector3 ToBoss()
    {
        Vector3 d = Flat(boss.BodyPoint - hero.transform.position);
        return d.sqrMagnitude > 0.01f ? d.normalized : Flat(hero.transform.forward).normalized;
    }

    float ClipStartFor(string clip, float speed, float lead)
    {
        var e = hero.anim.library[clip];
        return e == null ? 0 : Mathf.Max(0, e.impact - lead * speed);
    }

    // ------------------------------------------------------------------ THUNDER PALM
    IEnumerator Palm()
    {
        busy = true;
        cd[1] = Recharge;
        FightCamera.Calm(1.2f);
        SoundStage.At(AudioSynth.Zap, Chest, 0.5f, 1.3f);
        bool held = hero.bossHeld;
        const float charge = 0.42f, speed = 1.35f;
        hud.BossCall("THUNDER PALM!", "", 1.1f, Blue);
        if (held)
        {
            // the palm blasts the grip apart
            hero.BossRelease(Vector3.zero, 0, 1.8f, 0);
        }
        else
        {
            hero.EnterPower();
            hero.transform.rotation = Quaternion.LookRotation(ToBoss());
            if (hero.anim.Has("ThunderPalm")) hero.PlayClip("ThunderPalm", 0.08f, ClipStartFor("ThunderPalm", speed, charge), speed);
            TimeFX.SlowFor(0.5f, 0.45f);
            for (float x = 0; x < charge; x += Time.deltaTime)
            {
                // lightning gathers in the palms
                foreach (var h in new[] { handL, handR })
                    fx.Energy(h.position + Random.insideUnitSphere * 0.5f, (h.position - Chest).normalized, 3, Blue, Color.white, 1f, 3f);
                yield return null;
            }
        }
        Vector3 c = Feet + Vector3.up * 0.1f;
        SoundStage.At(AudioSynth.Zap, c, 1f, 0.8f);
        fx.Impact(c, 6f, 0.6f);
        ActionFX.DustRing(c, 3.5f, 24, 2.2f);   // the blast tears up the courtyard
        ActionFX.DustRing(c, 7f, 20, 2.8f);
        fx.Pulse(c, 8f, Blue);
        fx.Pulse(c, 16f, Blue, 0.5f);
        fx.Pulse(c, 26f, new Color(0.6f, 0.85f, 1f, 0.7f), 0.7f);
        for (int k = 0; k < 18; k++)
        {
            float a = k / 18f * Mathf.PI * 2;
            Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            fx.Energy(c + d * Random.Range(1f, 11f) + Vector3.up * 0.2f, d + Vector3.up, 7, Blue, Color.white, 3f, 9f);
        }
        fx.Chroma(0.6f);
        FightCamera.Shake(0.5f);
        FightCamera.Punch(0.4f);
        boss.ThunderPalm(c, 13f);
        boss.CaptureShot("power_thunder_palm");
        for (float x = 0; x < 0.45f; x += Time.deltaTime)
        {
            if (Random.value < 0.5f)
            {
                float a = Random.value * Mathf.PI * 2;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                fx.Energy(c + d * (x / 0.45f * 13f), Vector3.up, 5, Blue, Color.white, 2f, 7f);
            }
            yield return null;
        }
        if (!held) hero.ExitPower();
        busy = false;
    }

    // ------------------------------------------------------------------ GUARDIAN SPIRIT
    IEnumerator Guardian()
    {
        var prefab = GameManager.I.guardianPrefab;
        if (prefab == null) yield break;
        busy = true;
        OwnsCamera = true;
        cd[2] = Recharge;
        hero.EnterPower();
        Vector3 toBoss = ToBoss();
        hero.transform.rotation = Quaternion.LookRotation(toBoss);
        if (hero.anim.Has("SpiritSummon")) hero.PlayClip("SpiritSummon", 0.1f, 0, 1.1f);
        hud.BossCall("GUARDIAN SPIRIT", "", 1.6f, Blue);
        bool titan = boss.IsTitan;
        float scale = titan ? 7.2f : 5.4f;
        Vector3 basePos = Feet - toBoss * (titan ? 3.5f : 2.8f);
        var spirit = Instantiate(prefab, basePos + Vector3.down * 12f, Quaternion.LookRotation(toBoss));
        spirit.transform.localScale = Vector3.one * scale;
        var drv = spirit.GetComponent<AnimDriver>();
        var an = spirit.GetComponent<Animator>();
        if (drv) drv.Play("FightIdle", 0.1f, 0, 1, loop: true);
        var rs = spirit.GetComponentsInChildren<Renderer>();
        var mpb = new MaterialPropertyBlock();
        var baseCol = new Color(0.12f, 0.38f, 1.25f, 0.32f);
        var lightGo = new GameObject("SpiritLight");
        var lt = lightGo.AddComponent<Light>();
        lt.type = LightType.Point; lt.color = Blue; lt.range = scale * 3f; lt.intensity = 0; lt.shadows = LightShadows.None;
        void Alpha(float a)
        {
            mpb.SetColor("_BaseColor", new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * a));
            foreach (var r in rs) if (r) r.SetPropertyBlock(mpb);
            lt.intensity = 6f * a;
            lightGo.transform.position = spirit.transform.position + Vector3.up * scale;
        }
        Alpha(0);
        Vector3 right = Vector3.Cross(Vector3.up, toBoss);
        FightCamera.Calm(7f);
        Vector3 moros0 = boss.IsTitan ? boss.TitanChest : boss.BodyPoint;
        Vector3 camAnchor = Feet - toBoss * (scale * 1.25f + 4f) + right * (scale * 0.9f + 2f) + Vector3.up * (scale * 1.3f + 3f);
        Vector3 lookPt = Vector3.Lerp(basePos + Vector3.up * scale, moros0, 0.35f);
        // rise out of the ground behind Kaito
        SoundStage.At(AudioSynth.Spirit, basePos + Vector3.up * scale, 1f, 1f);
        fx.Impact(basePos, 5f, 0.4f);
        ActionFX.DustRing(basePos, 3f, 22, 2.4f);
        fx.Pulse(basePos + Vector3.up * 0.1f, 10f, Blue, 0.6f);
        for (float x = 0; x < 1.4f; x += Time.deltaTime)
        {
            float u = Mathf.Clamp01(x / 1.3f);
            spirit.transform.position = basePos + Vector3.down * 12f * (1 - u * u * (3 - 2 * u));
            Alpha(u);
            fx.Energy(basePos + Random.insideUnitSphere * scale * 0.6f + Vector3.up * scale * 0.4f, Vector3.up, 3, Blue, Color.white, 2f, 6f);
            lookPt = Vector3.Lerp(lookPt, Vector3.Lerp(spirit.transform.position + Vector3.up * scale * 1.05f, moros0, 0.35f), 1 - Mathf.Exp(-1.5f * Time.deltaTime));
            director?.Override(camAnchor, lookPt, 62f, 0.3f);
            if (x > 1.0f && x - Time.deltaTime <= 1.0f) boss.CaptureShot("power_guardian_rise");
            yield return null;
        }
        hero.ExitPower();   // Kaito can fight alongside it
        string[] clips = { "Cross", "Hook", "Uppercut" };
        float[] dmg = { 10, 10, 18 };
        const float spd = 0.8f;
        for (int k = 0; k < 3 && !boss.Ended; k++)
        {
            var e = hero.anim.library[clips[k]];
            if (e == null || drv == null) continue;
            drv.Play(clips[k], 0.1f, 0, spd);
            float hitAt = e.impact / spd;
            bool hit = false;
            for (float x = 0; x < hitAt + 0.35f; x += Time.deltaTime)
            {
                Vector3 target = boss.IsTitan ? boss.TitanChest : boss.BodyPoint;
                Vector3 fl = Flat(target - spirit.transform.position);
                Vector3 fd = fl.sqrMagnitude > 0.01f ? fl.normalized : toBoss;
                float reach = scale * 0.72f;
                Vector3 stand = Flat(target) - fd * reach + Vector3.up * basePos.y;
                spirit.transform.position = Vector3.MoveTowards(spirit.transform.position, stand, 9f * Time.deltaTime);
                spirit.transform.rotation = Quaternion.Slerp(spirit.transform.rotation, Quaternion.LookRotation(fd), 1 - Mathf.Exp(-8 * Time.deltaTime));
                lookPt = Vector3.Lerp(lookPt, Vector3.Lerp(spirit.transform.position + Vector3.up * scale * 1.0f, target, 0.5f), 1 - Mathf.Exp(-1.5f * Time.deltaTime));
                director?.Override(camAnchor, lookPt, 62f, 0.3f);
                lightGo.transform.position = spirit.transform.position + Vector3.up * scale;
                if (!hit && x >= hitAt)
                {
                    hit = true;
                    Vector3 fist = target;
                    if (an)
                    {
                        var hl = an.GetBoneTransform(HumanBodyBones.LeftHand);
                        var hr = an.GetBoneTransform(HumanBodyBones.RightHand);
                        if (hl && hr) fist = Vector3.Dot(hl.position - hr.position, fd) > 0 ? hl.position : hr.position;
                    }
                    boss.SpiritPunch(Vector3.Lerp(fist, target, 0.5f), dmg[k], k == 2);
                    Vector3 hitGround = new Vector3(target.x, 0.05f, target.z);
                    ActionFX.DustRing(hitGround, k == 2 ? 3.5f : 2f, k == 2 ? 24 : 14, k == 2 ? 2.4f : 1.6f);
                    if (k == 2) { TimeFX.SlowFor(0.25f, 0.35f); FightCamera.Punch(1f); }
                    boss.CaptureShot("power_guardian_punch" + k);
                }
                yield return null;
            }
        }
        // the spirit fades back into light
        Vector3 at = spirit.transform.position;
        for (float x = 0; x < 0.9f; x += Time.deltaTime)
        {
            float u = x / 0.9f;
            Alpha(1 - u);
            spirit.transform.position = at + Vector3.up * u * 2f;
            fx.Energy(at + Random.insideUnitSphere * scale * 0.5f + Vector3.up * scale, Vector3.up, 4, Blue, Color.white, 2f, 7f);
            yield return null;
        }
        Destroy(spirit);
        Destroy(lightGo);
        OwnsCamera = false;
        busy = false;
    }

    // ------------------------------------------------------------------ BLADE HAND AWAKENING
    IEnumerator Awakening()
    {
        busy = true;
        cd[3] = Recharge;
        FightCamera.Calm(6f);
        orbitAng = 0;
        // pin him first: his weapon collapses to the ground before the close-up
        OwnsCamera = true;
        if (!boss.Seize())
        {
            busy = false;
            OwnsCamera = false;
            cd[3] = 0;
            yield break;
        }
        hero.EnterPower();
        hero.transform.rotation = Quaternion.LookRotation(ToBoss());
        hud.BossCall("BLADE HAND AWAKENING", "", 1.8f, Blue);
        SetGlow(true);
        fx.Chroma(0.6f);
        fx.Pulse(Chest, 5f, Blue);
        if (hero.anim.Has("PowerUp")) hero.PlayClip("PowerUp", 0.1f, 0.2f, 1.6f);
        TimeFX.slow = 0.35f;
        Atmosphere.Focus(() => hero.Body + Vector3.up * 1.2f, 1.1f);
        Vector3 f0 = Flat(hero.transform.forward).normalized, r0 = Vector3.Cross(Vector3.up, f0);
        director?.SnapOverride();   // cut straight to the close-up (no glide) so the focus pull lands on him
        for (float x = 0; x < 1.0f; x += Clock.Dt)
        {
            // side profile: his burning hands in front, Moros waiting in the background
            director?.Override(hero.Body - f0 * 1.0f + r0 * 3.4f + Vector3.up * 1.6f, hero.Body + f0 * 1.2f + Vector3.up * 1.25f, 46f, 0.3f);
            foreach (var h in new[] { handL, handR }) fx.Energy(h.position, Vector3.up, 2, Blue, Color.white, 0.5f, 2f);
            if (x > 0.6f && x - Clock.Dt <= 0.6f) boss.CaptureShot("power_awakening");
            yield return null;
        }
        TimeFX.slow = 1;
        // flash-step in
        Vector3 from = Feet;
        Vector3 dir = ToBoss();
        fx.Whoosh(0.6f, 1f);
        for (float x = 0; x < 0.25f; x += Time.deltaTime)
        {
            Vector3 stand = Flat(boss.BodyPoint) - dir * 1.05f;
            Vector3 p = Vector3.Lerp(from, stand, x / 0.25f);
            hero.transform.position = new Vector3(p.x, hero.floorY, p.z);
            hero.transform.rotation = Quaternion.LookRotation(dir);
            fx.Energy(hero.Body + Vector3.up * 1f, -dir, 4, Blue, Color.white, 1f, 3f);
            yield return null;
        }
        (string clip, float speed, float dmg)[] seq =
        {
            ("FlyingKneeCombo", 1.35f, 7), ("HurricaneKick", 1.3f, 8), ("Martelo", 1.45f, 7), ("SideKick", 1.45f, 7), ("Uppercut", 1.3f, 10),
        };
        Vector3 last = boss.BodyPoint;
        for (int k = 0; k < seq.Length && !boss.Ended; k++)
        {
            var e = hero.anim.library[seq[k].clip];
            if (e == null) continue;
            // the hurricane kick's analysed impact is its first elbow chamber; the spinning kick lands later
            float impact = seq[k].clip == "HurricaneKick" ? e.length * 0.58f : e.impact;
            float start = Mathf.Max(0, impact - 0.32f * seq[k].speed);
            hero.PlayClip(seq[k].clip, 0.06f, start, seq[k].speed);
            float hitAt = (impact - start) / seq[k].speed;
            bool hit = false;
            for (float x = 0; x < hitAt + 0.16f; x += Time.deltaTime)
            {
                Vector3 target = boss.BodyPoint;
                Vector3 stand = Flat(target) - dir * 1.05f;
                hero.transform.position = Vector3.MoveTowards(hero.transform.position, new Vector3(stand.x, hero.floorY, stand.z), 6f * Time.deltaTime);
                hero.transform.rotation = Quaternion.Slerp(hero.transform.rotation, Quaternion.LookRotation(dir), 1 - Mathf.Exp(-12 * Time.deltaTime));
                OrbitCam(target, dir);
                if (!hit && x >= hitAt)
                {
                    hit = true;
                    last = target + Vector3.up * 0.2f;
                    boss.AwakeningHit(last, seq[k].dmg, dir);
                    SoundStage.At(AudioSynth.Zap, last, 0.45f, 1.4f + k * 0.08f);
                    fx.Pulse(last, 3f, Blue);
                    if (k == 2) boss.CaptureShot("power_awakening_strike");
                }
                yield return null;
            }
        }
        if (!boss.Ended)
        {
            fx.Impact(last, 6f, 0.5f);
            ActionFX.DustRing(new Vector3(last.x, 0.05f, last.z), 3f, 22, 2.2f);
            fx.Pulse(last, 9f, Blue, 0.6f);
            fx.Energy(last, dir, 60, Blue, Color.white, 6f, 16f);
            TimeFX.SlowFor(0.3f, 0.6f);
            FightCamera.Shake(0.4f);
            hud.BossCall("SHATTERED!", "", 1.2f, Blue);
            boss.ReleaseSeize(dir);
            boss.CaptureShot("power_awakening_finish");
        }
        SetGlow(false);
        hero.ExitPower();
        OwnsCamera = false;
        busy = false;
    }

    // one slow, steady orbit around the two of them for the whole sequence
    float orbitAng;
    void OrbitCam(Vector3 target, Vector3 dir)
    {
        Vector3 mid = (hero.Body + Vector3.up * 1.1f + target) * 0.5f;
        orbitAng += 14f * Clock.Dt;
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Vector3 off = Quaternion.AngleAxis(orbitAng, Vector3.up) * side;
        Vector3 cam = mid + off * 4.8f + Vector3.up * 1.2f;
        cam.y = Mathf.Max(cam.y, hero.floorY + 0.5f);
        director?.Override(cam, mid, 44f, 0.3f);
    }

    // ------------------------------------------------------------------ STONE GAUNTLETS
    void ToggleGauntlets()
    {
        gauntlets = !gauntlets;
        hero.strikeMul = gauntlets ? 1.6f : 1f;
        if (gauntlets)
        {
            // the stones rise out of the ground and wrap his fists
            stones.Gather(Feet + Vector3.up * 0.1f, 1.2f);
            stones.BeginForm(0.7f);
            hud.BossCall("STONE GAUNTLETS", "YOUR PUNCHES SHATTER HIS WEAPONS", 1.3f, new Color(0.85f, 0.75f, 0.6f));
            fx.Crack(Feet, 0.8f);
            boss.CaptureShot("power_gauntlets");
        }
        else
        {
            stones.mode = ShardSwarm.Mode.Free;
            stones.Burst(Chest, 2.5f, 1f, 3f);
        }
    }

    // ------------------------------------------------------------------ glowing hands
    void MakeGlow()
    {
        var mat = GameManager.I.glowMat;
        for (int i = 0; i < 2; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "AwakenGlow";
            var r = g.GetComponent<Renderer>();
            if (mat) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var glowBlock = new MaterialPropertyBlock();
            glowBlock.SetColor("_BaseColor", new Color(0.25f, 0.5f, 1.1f, 0.35f));   // bright, but not a white blob under bloom
            r.SetPropertyBlock(glowBlock);
            g.transform.SetParent(i == 0 ? handL : handR, false);
            g.transform.localPosition = Vector3.zero;
            g.transform.localScale = Vector3.one * 0.035f / Mathf.Max(0.01f, (i == 0 ? handL : handR).lossyScale.x);
            var tr = g.AddComponent<TrailRenderer>();
            tr.time = 0.28f;
            tr.startWidth = 0.14f;
            tr.endWidth = 0f;
            if (mat) tr.sharedMaterial = mat;
            tr.startColor = new Color(0.6f, 0.9f, 1f, 1f);
            tr.endColor = new Color(0.2f, 0.5f, 1f, 0f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point; l.color = Blue; l.range = 1.8f; l.intensity = 1.2f; l.shadows = LightShadows.None;
            g.SetActive(false);
            glow[i] = g;
        }
    }

    void SetGlow(bool on)
    {
        foreach (var g in glow)
        {
            if (!g) continue;
            g.SetActive(on);
            var tr = g.GetComponent<TrailRenderer>();
            if (tr) tr.Clear();
        }
    }
}
