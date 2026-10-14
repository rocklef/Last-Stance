using System.Collections;
using UnityEngine;

// MOROS, THE SHARD MONK - the final boss after Brakka (replaces the Ashen Roc).
// A pale stone monk with ember eyes who tears the courtyard's own flagstones out of the floor and
// shapes them into weapons. He hovers out of reach inside a halo of shards (with a mandala of stone
// behind his head) and attacks with transformations: the swarm streams together into an object,
// strikes with it, and shatters back into shards.
//   EXECUTIONER'S AXE  a giant war axe: overhead chop down a red line (sidestep / parry), later a sweep (dash)
//   TITAN GRASP     a giant stone hand on a stone arm lunges, catches Kaito, crushes and slams him
//   LANCE STORM     a fan of stone spears fires one after another and sticks in the floor
//   SERPENT DRAGON  a shard serpent with a dragon's head coils and lunges (punch the head to break it)
//   STONE ROC       (phase 2) the bird returns - built from stone, it circles the temple and dives
//   SKYBREAKER      (phase 2) a colossal maul tracks Kaito from the sky and smashes the red ring
//   SHARD FLOOD     (phase 2) a breaking wave of stone rolls across the floor
// At half health he ascends: every shard in the courtyard builds a 10 m armoured stone titan around
// him (boots, plated legs, runed belt, harnessed chest plate, layered pauldrons, gauntlets, a bearded
// head with burning eyes) that walks after Kaito, slams, stomps, grabs him to its face and hurls him.
// After his powers he lands to finish Kaito by hand - the moment Kaito can punish him.
public partial class MonkBoss : MonoBehaviour
{
    public static MonkBoss Live;
    public bool Complete { get; private set; }
    public bool HeroWon { get; private set; }

    Fighter hero, proxy;
    GameObject model;
    Animator an;
    Transform hips, warnDisc, warnStrip, focus;
    readonly Transform[] eyes = new Transform[2];
    Light aura;
    Renderer[] body;
    MaterialPropertyBlock mpb;
    ShardSwarm swarm;

    public float maxHp = 340;
    float hp;
    Vector3 pos, posVel, goal;
    float heightVel, height;
    bool enraged, ascended, dying, invulnerable;
    string act = "intro";
    float t, camLift = 1, glow, warnPulse;
    int powerIndex;

    // punish window and weak points
    bool open, knockedBack, broken, breakable;
    int windowHits;
    Vector3 breakPoint;
    float breakRadius;

    // the object the swarm is currently shaped into
    enum Form { None, Axe, Hand, Lances, Roc, Hammer, Titan, Dragon, Gatling, Rockets, GreatDragon, Arsenal, Sphere, Cobra, Drill, Wall }
    Form form;
    Vector3 eyeL, eyeR;
    float eyeSize;
    Vector3 axGrip, axDir = Vector3.up, axEdge = Vector3.forward;
    const float AxLen = 4.4f, AxScale = 1.6f;
    Vector3 hWrist, hFingers = Vector3.up, hPalm = Vector3.forward, hArm;
    float hScale = 1.5f, hCurl;
    readonly Vector3[] lTail = new Vector3[12], lTip = new Vector3[12];
    int lCount;
    Vector3 rocC, rocF = Vector3.forward, rocU = Vector3.up;
    float rocFlap, rocFold, rocTalons;
    const float RocScale = 1.55f;
    Vector3 hmGrip, hmHandle = Vector3.up, hmAxis = Vector3.forward;
    const float HmScale = 1.9f;
    ShardForms.TitanPose tp;
    const float TitanScale = 1f;
    float walkPhase;
    float gape;

    static readonly Color Ember = new Color(1f, 0.3f, 0.12f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
    static readonly string[] Phase1 = { "axe", "grasp", "lances", "serpent", "sphere", "cobra" };

    int shot;
    float shotT, lastShot = -10;

    // the hunt through the town
    readonly System.Collections.Generic.List<Hunter> hunters = new System.Collections.Generic.List<Hunter>();
    Hunter caughtHunter;
    bool hunting, fled, delivered;
    int huntsDone, cycles;
    float farT, lastSpotCall = -10;
    Vector3 lastSeenHero;
    Material[] shardMats;
    Material eyeMat;
    BossDirector director;
    Brain huntSaved;
    float deathGlow = -1;   // the finale: his cracks burn brighter

    public void Begin(Fighter heroFighter, Fighter proxyFighter, GameObject prefab, Material[] shardMats, Material warnMat)
    {
        Live = this;
        hero = heroFighter;
        proxy = proxyFighter;
        hp = maxHp;
        pos = new Vector3(0, 0, 4.2f);
        goal = pos;

        model = Instantiate(prefab, pos, Quaternion.LookRotation(Vector3.back));
        model.name = "Moros";
        an = model.GetComponentInChildren<Animator>();
        hips = an ? an.GetBoneTransform(HumanBodyBones.Hips) : model.transform;
        eyeMat = warnMat;
        this.shardMats = shardMats;
        var rs = model.GetComponentsInChildren<Renderer>(true);
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (var r in rs)
        {
            if (r.name.StartsWith("Eye")) eyeMat = r.sharedMaterial;
            else list.Add(r);
        }
        body = list.ToArray();
        mpb = new MaterialPropertyBlock();
        model.SetActive(false);

        for (int i = 0; i < 2; i++)
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(e.GetComponent<Collider>());
            e.name = "ConstructEye";
            var er = e.GetComponent<Renderer>();
            er.sharedMaterial = eyeMat;
            er.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            eyes[i] = e.transform;
            e.SetActive(false);
        }

        var lightGo = new GameObject("MorosAura");
        aura = lightGo.AddComponent<Light>();
        aura.type = LightType.Point; aura.color = Ember; aura.range = 6; aura.intensity = 0; aura.shadows = LightShadows.None;

        Fighter.freeRoam = TownMap.I != null;   // the fight can spill out into the temple town
        var cam0 = GameManager.I.cam;
        director = cam0.GetComponent<BossDirector>() ?? cam0.gameObject.AddComponent<BossDirector>();
        director.hero = hero;
        cam0.director = director;

        swarm = new GameObject("MorosShards").AddComponent<ShardSwarm>();
        swarm.Init(shardMats, Application.isMobilePlatform ? 6000 : 12000, Vector3.zero, 11f);   // phones carry half the stones
        swarm.active = 3400;
        swarm.boundRadius = 110;
        swarm.builder = BuildForm;
        ammo = new GameObject("MorosAmmo").AddComponent<ShardProjectiles>();
        ammo.Init(shardMats, hero, (from, dmg, up, push, parry, block) => Strike(from, dmg, up, push, parry, block, "blast"));
        var powers = hero.GetComponent<HeroPowers>() ?? hero.gameObject.AddComponent<HeroPowers>();
        powers.Begin(this);
        swarm.mode = ShardSwarm.Mode.Free;

        warnDisc = MakeWarn(PrimitiveType.Cylinder, warnMat);
        warnStrip = MakeWarn(PrimitiveType.Cube, warnMat);
        focus = new GameObject("MorosFocus").transform;
        focus.position = pos + Vector3.up * 2;

        if (AutoCapture.Active) hero.bossDamageScale = 0.35f;   // let the autopilot survive long enough to see every form
        StartCoroutine(Run());
    }

    Transform MakeWarn(PrimitiveType type, Material m)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        g.SetActive(false);
        return g.transform;
    }

    void OnDestroy() { if (Live == this) Live = null; }

    // ================================================================== Kaito's strikes
    public static bool TryHit(Vector3 point, float radius, float damage, bool crush = false)
    {
        var b = Live;
        if (b == null || b.Complete || b.dying) return false;
        foreach (var h in b.hunters)
        {
            if (h == null || !(h.state == Hunter.State.Roam || h.state == Hunter.State.Chase || h.state == Hunter.State.Search)) continue;
            if ((h.Head - point).sqrMagnitude < (radius + 0.6f) * (radius + 0.6f))
            {
                h.Break(point);
                TimeFX.HitStop(0.06f);
                b.Announce("HUNTER SHATTERED!", "", 0.8f, Gold);
                return true;
            }
        }
        if (crush && !b.seized && b.CrushWeapon(point, radius)) return true;
        if (b.invulnerable) return false;
        if (b.breakable && (b.breakPoint - point).sqrMagnitude < (radius + b.breakRadius) * (radius + b.breakRadius))
        {
            b.broken = true;
            b.breakable = false;
            b.hp = Mathf.Max(1, b.hp - 8);
            GameManager.I.fx.Crack(point, 0.7f);
            TimeFX.HitStop(0.08f);
            return true;
        }
        Vector3 c = (b.hips ? b.hips.position : b.pos + Vector3.up) + Vector3.up * 0.2f;
        if ((c - point).sqrMagnitude > (radius + 0.42f) * (radius + 0.42f)) return false;
        b.TakeHit(point, damage);
        return true;
    }

    void TakeHit(Vector3 point, float damage)
    {
        float dmg = Mathf.Round(damage * (open ? 1.2f : 0.6f));
        hp = Mathf.Max(Mathf.Min(hp, HpFloor), hp - dmg);
        Vector3 dir = Flat(pos - hero.transform.position).normalized;
        var fx = GameManager.I.fx;
        fx.Crack(point, Random.Range(0.75f, 0.9f));
        fx.Splash(point, dir, 0.35f);
        TimeFX.HitStop(0.05f);
        FightCamera.Shake(0.18f);
        pos += dir * 0.1f;
        windowHits++;
        if (hp <= 0)
        {
            StopAllCoroutines();
            StartCoroutine(Death());
            return;
        }
        Play("Hit", 0.05f);
        if ((open || act == "melee") && windowHits >= 5) knockedBack = true;
    }

    // ================================================================== fight script
    IEnumerator Run()
    {
        yield return Intro();
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-titan") >= 0)
        {
            // test: straight to the titan, Kaito only runs (so the giant builds fully and gives chase)
            armed = true;
            hp = maxHp * 0.39f;
            var flee = hero.gameObject.GetComponent<FleeBrain>();
            if (flee == null) flee = hero.gameObject.AddComponent<FleeBrain>();
            hero.brain = flee;
        }
        if (ShowcaseMode) { showBusy = false; yield break; }   // the showcase director calls his powers
        yield return Loop();
    }

    // THE INTRO CUTSCENE: the floor tears itself apart around a low crane shot behind Kaito, Moros
    // bursts out of the stone tornado (push-in, focus pull), they trade words over each other's
    // shoulders, and the camera pulls out into the fight. Enter / click skips.
    IEnumerator Intro()
    {
        act = "rise";
        height = 0;
        swarm.mode = ShardSwarm.Mode.Twister;
        swarm.center = Flat(pos);
        swarm.twisterHeight = 7.5f; swarm.twisterWidth = 0.42f;
        GameManager.I.cam.SetRoc(focus);
        var hud = GameManager.I.hud;
        hud.Cinematic(true);
        Cine.Begin(true);
        Vector3 h = HeroFlat, m = Flat(pos);
        Vector3 ax = m - h;
        ax = ax.sqrMagnitude > 0.01f ? ax.normalized : Vector3.forward;
        Vector3 sd = Vector3.Cross(Vector3.up, ax);
        hero.transform.rotation = Quaternion.LookRotation(ax);
        hud.Dialogue("", Color.white, "The courtyard floor tears itself apart...", 3.2f);
        float nextShake = 0;
        yield return Cine.Shot(3.4f, h - ax * 3.4f + sd * 1.4f + Vector3.up * 0.35f, h - ax * 4.2f + sd * 1.8f + Vector3.up * 3.4f,
                               m + Vector3.up * 1.2f, m + Vector3.up * 4.8f, 50, 46, x =>
        {
            aura.intensity = x;
            focus.position = pos + Vector3.up * (1.5f + x);
            if (x > nextShake) { FightCamera.Shake(0.2f); nextShake += 0.5f; }
        });
        act = "reveal";
        model.SetActive(true);
        Play("Bow", 0.1f);
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.Burst(pos + Vector3.up * 2, 7, 3);
        swarm.twisterHeight = 4.6f; swarm.twisterWidth = 0.32f;
        GameManager.I.fx.Impact(pos, 5.5f, 0.7f);
        FightCamera.Shake(0.8f); FightCamera.Punch(1f);
        Title("MOROS", "THE SHARD MONK", 2.4f);
        Atmosphere.Focus(() => pos + Vector3.up * 1.6f, 2.6f);
        yield return Cine.Shot(2.6f, m - ax * 5.6f + sd * 2.4f + Vector3.up * 1.5f, m - ax * 3.9f + sd * 1.6f + Vector3.up * 2.0f,
                               m + Vector3.up * 1.8f, m + Vector3.up * 2.5f, 44, 38, x =>
        {
            height = Mathf.Lerp(0.2f, 1.3f, Mathf.Clamp01(x / 1.3f));
            focus.position = pos + Vector3.up * 1.8f;
            if (x > 1.3f && swarm.mode != ShardSwarm.Mode.Halo) swarm.mode = ShardSwarm.Mode.Halo;
        });
        height = 1.3f;
        swarm.mode = ShardSwarm.Mode.Halo;
        Shot("reveal");
        // Moros speaks - over Kaito's shoulder
        hud.Dialogue("MOROS", Ember, "Every stone of this temple answers to me.", 2.6f);
        Atmosphere.Focus(() => pos + Vector3.up * 2.0f, 2.7f);
        yield return Cine.Shot(2.7f, h - ax * 1.35f + sd * 0.75f + Vector3.up * 1.7f, h - ax * 1.1f + sd * 0.65f + Vector3.up * 1.75f,
                               m + Vector3.up * 2.4f, m + Vector3.up * 2.4f, 38, 34);
        // Kaito answers - a side close-up, Moros's halo glowing behind him
        hud.Dialogue("KAITO", hero.accent, "Then I'll break every stone.", 2.2f);
        Atmosphere.Focus(() => hero.Body + Vector3.up * 1.5f, 2.3f);
        yield return Cine.Shot(2.3f, h - sd * 2.6f - ax * 0.9f + Vector3.up * 1.6f, h - sd * 2.2f - ax * 0.7f + Vector3.up * 1.6f,
                               h + ax * 0.5f + Vector3.up * 1.5f, h + ax * 0.5f + Vector3.up * 1.55f, 38, 34);
        // pull out into the fight
        Vector3 mid = (h + m) * 0.5f;
        yield return Cine.Shot(1.5f, mid + sd * 4.5f + Vector3.up * 1.6f, mid + sd * 7.5f + Vector3.up * 2.6f, mid + Vector3.up * 1.3f, mid + Vector3.up * 1.2f, 48, 50);
        Cine.End();
        hud.Cinematic(false);
        aura.intensity = 2.4f;
        var cam = GameManager.I.cam;
        cam.b = proxy;
        cam.SetMode(FightCamera.Mode.Fight);
        hero.BeginFight();
        Announce("FINAL BOSS", "DASH THROUGH ATTACKS  -  TAP BLOCK TO PARRY  -  PUNISH HIM WHEN HE LANDS", 2.6f);
    }

    IEnumerator Hover(float seconds)
    {
        act = "hover";
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Halo;
        swarm.chipScale = 1;
        camLift = 1.2f;
        Play("Idle", 0.25f);
        for (float x = 0; x < seconds; x += Time.deltaTime)
        {
            goal = HoverSpot(4.3f);
            height = 1.5f;
            yield return null;
        }
        fled = TownMap.I != null && farT > 1.5f;
    }

    // ------------------------------------------------------------------ EXECUTIONER'S AXE
    IEnumerator Axe()
    {
        act = "axe";
        director?.Cut(BossDirector.Shot.Villain);
        form = Form.Axe;
        swarm.chipScale = 1.1f;
        Announce("EXECUTIONER'S AXE", enraged ? "SIDESTEP THE CHOP  -  DASH THE SWEEP" : "SIDESTEP OR PARRY THE CHOP", 1.4f);
        Play("Summon", 0.15f);
        GameManager.I.fx.Whoosh(0.5f);
        Vector3 f = ToHero();
        float alpha = 90;
        swarm.BeginForm(0.9f);
        for (float x = 0; x < 1.25f; x += Time.deltaTime)
        {
            goal = HoverSpot(4.2f);
            height = 0.9f;
            f = Vector3.Slerp(f, ToHero(), 1 - Mathf.Exp(-4 * Time.deltaTime));
            SetAxe(alpha, f);
            yield return null;
        }
        Shot("axe_raised");
        director?.Cut(BossDirector.Shot.Side);
        float reach = 0.55f + AxLen + 0.9f * AxScale;
        ShowStrip(Flat(pos), f, reach + 0.6f);
        for (float x = 0; x < 0.45f; x += Time.deltaTime)
        {
            alpha = Mathf.Lerp(90, 124, Smooth(x / 0.45f));
            SetAxe(alpha, f);
            yield return null;
        }
        Play("Cast", 0.08f);
        GameManager.I.fx.Whoosh(0.65f, 1f);
        float pivotY = pos.y + 2.1f;
        float alphaEnd = -Mathf.Asin(Mathf.Clamp01(pivotY / reach)) * Mathf.Rad2Deg;
        for (float x = 0; x < 0.18f; x += Time.deltaTime)
        {
            float e = x / 0.18f;
            SetAxe(Mathf.Lerp(124, alphaEnd, e * e), f);
            yield return null;
        }
        SetAxe(alphaEnd, f);
        Vector3 edge = ShardForms.AxeEdge(axGrip, axDir, axEdge, AxLen, AxScale);
        HideWarn();
        GameManager.I.fx.Impact(Flat(edge), 5f, 0.7f);
        FightCamera.Shake(0.7f); FightCamera.Punch(1f);
        {
            Vector3 rel = HeroFlat - Flat(pos);
            float along = Vector3.Dot(rel, f), lat = Mathf.Abs(Vector3.Dot(rel, Right(f)));
            if (lat < 0.95f && along > 0.2f && along < reach + 0.5f) Strike(pos, 17, 4.8f, 3f, true, true, "axe");
        }
        Shot("axe_chop");
        for (float x = 0; x < 0.4f && !broken; x += Time.deltaTime) { SetAxe(alphaEnd, f); yield return null; }

        if (enraged && !broken)
        {
            Announce("SWEEP!", "DASH THROUGH IT", 0.9f, Gold);
            Vector3 startDir = axDir, startGrip = axGrip;
            const float yaw0 = -125, yaw1 = 125;
            for (float x = 0; x < 0.4f; x += Time.deltaTime)
            {
                float e = Smooth(x / 0.4f);
                height = Mathf.Lerp(height, 0.3f, e);
                Vector3 d = Quaternion.AngleAxis(yaw0, Vector3.up) * f;
                axDir = Vector3.Slerp(startDir, d, e);
                axGrip = Vector3.Lerp(startGrip, Flat(pos) + Vector3.up * 1.0f + d * 0.55f, e);
                axEdge = Quaternion.AngleAxis(yaw0 + 90, Vector3.up) * f;
                yield return null;
            }
            GameManager.I.fx.Whoosh(0.55f, 1f);
            float prev = yaw0;
            bool hitDone = false;
            for (float x = 0; x < 0.42f; x += Time.deltaTime)
            {
                float yaw = Mathf.Lerp(yaw0, yaw1, Smooth(x / 0.42f));
                Vector3 d = Quaternion.AngleAxis(yaw, Vector3.up) * f;
                axDir = d;
                axGrip = Flat(pos) + Vector3.up * 1.0f + d * 0.55f;
                axEdge = Quaternion.AngleAxis(yaw + 90, Vector3.up) * f;
                float heroYaw = Vector3.SignedAngle(f, HeroFlat - Flat(pos), Vector3.up);
                float dist = Vector3.Distance(HeroFlat, Flat(pos));
                if (!hitDone && prev <= heroYaw && yaw >= heroYaw && dist > 0.4f && dist < reach + 0.8f)
                {
                    hitDone = true;
                    Strike(pos, 13, 3.8f, 4.5f, true, true, "sweep");
                }
                prev = yaw;
                yield return null;
            }
            Shot("axe_sweep");
        }
        yield return Dissolve(axGrip + axDir * AxLen * 0.6f, 3.5f);
        if (broken) yield return Broken("AXE SHATTERED!");
    }

    // alpha: the haft's angle above the horizontal, in the vertical plane toward Kaito
    void SetAxe(float alphaDeg, Vector3 f)
    {
        float a = alphaDeg * Mathf.Deg2Rad;
        Vector3 pivot = pos + Vector3.up * 2.1f;
        Vector3 hd = Mathf.Cos(a) * f + Mathf.Sin(a) * Vector3.up;
        axDir = hd;
        axGrip = pivot + hd * 0.55f;
        axEdge = Mathf.Sin(a) * f - Mathf.Cos(a) * Vector3.up;   // the blade leads the chop
    }

    // ------------------------------------------------------------------ TITAN GRASP
    IEnumerator Grasp()
    {
        act = "grasp";
        form = Form.Hand;
        swarm.chipScale = 1.35f;
        camLift = 2.4f;
        hScale = 1.5f;
        hCurl = 0;
        Announce("TITAN GRASP", "DASH OR SIDESTEP  -  PUNCH THE HAND TO BREAK IT", 1.4f);
        Play("Summon", 0.15f);
        Vector3 f = ToHero();
        swarm.BeginForm(1.0f);
        for (float x = 0; x < 1.3f; x += Time.deltaTime)
        {
            goal = HoverSpot(4.7f);
            height = 1.4f;
            f = Vector3.Slerp(f, ToHero(), 1 - Mathf.Exp(-4 * Time.deltaTime));
            hArm = pos + Vector3.up * 2.0f;
            hWrist = pos + Vector3.up * (3.1f + 0.15f * Mathf.Sin(t * 3)) + f * 0.5f + Right(f) * 0.8f;
            hFingers = Vector3.up;
            hPalm = f;
            yield return null;
        }
        Shot("grasp_rise");
        Vector3 w0 = hWrist;
        for (float x = 0; x < 0.3f; x += Time.deltaTime)
        {
            hArm = pos + Vector3.up * 2.0f;
            hWrist = Vector3.Lerp(w0, pos + Vector3.up * 3.9f - f * 0.4f + Right(f) * 0.6f, Smooth(x / 0.3f));
            yield return null;
        }

        Play("Cast", 0.08f);
        GameManager.I.fx.Whoosh(0.55f, 1f);
        Vector3 start = hWrist, aim = HeroChest;
        bool caught = false, evaded = false;
        breakable = true;
        const float dur = 0.5f;
        for (float x = 0; x < dur; x += Time.deltaTime)
        {
            aim = Vector3.Lerp(aim, HeroChest, 1 - Mathf.Exp(-3 * Time.deltaTime));
            hPalm = Flat(aim - pos).normalized;
            hFingers = Vector3.up;
            Vector3 wristTarget = aim - hPalm * 0.5f * hScale - hFingers * 0.55f * hScale;
            float e = Smooth(x / dur);
            hArm = pos + Vector3.up * 2.0f;
            hWrist = Bezier(start, (start + wristTarget) * 0.5f + Vector3.up * 1.2f, wristTarget, e);
            breakPoint = ShardForms.PalmCentre(hWrist, hFingers, hScale);
            breakRadius = 0.9f * hScale;
            if (broken) break;
            Vector3 grab = breakPoint + hPalm * 0.35f * hScale;
            if ((grab - HeroChest).magnitude < 1.0f)
            {
                if (hero.state == Fighter.State.Dash) { if (!evaded) { evaded = true; hero.SwarmStrike(pos, 0, 0, 0); } }   // evade callout
                else if (hero.BossGrab()) { caught = true; break; }
            }
            yield return null;
        }
        breakable = false;
        if (broken) { yield return Broken("HAND BROKEN!"); yield break; }
        if (!caught)
        {
            Vector3 crash = Flat(hWrist + hPalm * 1.3f);
            Vector3 w1 = hWrist;
            for (float x = 0; x < 0.18f; x += Time.deltaTime) { hWrist = Vector3.Lerp(w1, crash + Vector3.up * 0.15f, x / 0.18f); hArm = pos + Vector3.up * 2; yield return null; }
            GameManager.I.fx.Impact(crash, 3.5f, 0.8f);
            FightCamera.Shake(0.5f);
            yield return Dissolve(ShardForms.PalmCentre(hWrist, hFingers, hScale), 4f);
            yield break;
        }

        Announce("CAUGHT!", "", 0.8f, Gold);
        FightCamera.Punch(1f);
        GameManager.I.fx.Crack(HeroChest, 0.6f);
        for (float x = 0; x < 0.22f; x += Time.deltaTime) { hCurl = x / 0.22f; HoldHero(); yield return null; }
        hCurl = 1;
        Shot("grasp_caught");
        Vector3 wl = hWrist;
        Vector3 liftTo = Flat(pos) + ToHero() * 2.2f + Vector3.up * 3.2f;
        int squeezes = 0;
        for (float x = 0; x < 1.25f; x += Time.deltaTime)
        {
            hArm = pos + Vector3.up * 2.0f;
            hWrist = Vector3.Lerp(wl, liftTo, Smooth(Mathf.Clamp01(x / 0.5f)));
            hCurl = 1 - 0.08f * Mathf.Abs(Mathf.Sin(t * 20));
            HoldHero();
            if (x > 0.38f * (squeezes + 1) && squeezes < 3)
            {
                squeezes++;
                hero.BossSqueeze(4);
                GameManager.I.fx.Crack(HeroChest, 0.5f + squeezes * 0.06f);
                FightCamera.Shake(0.3f);
            }
            yield return null;
        }
        Vector3 f2 = Flat(hWrist - pos).normalized;
        Vector3 slamAt = Flat(pos) + f2 * 3.3f;
        Vector3 w2 = hWrist;
        GameManager.I.fx.Whoosh(0.5f, 1f);
        for (float x = 0; x < 0.2f; x += Time.deltaTime)
        {
            float e = x / 0.2f;
            hWrist = Vector3.Lerp(w2, slamAt + Vector3.up * 0.4f - f2 * 0.6f, e * e);
            hPalm = Vector3.Slerp(hPalm, (f2 - Vector3.up).normalized, e);
            HoldHero();
            yield return null;
        }
        hero.BossRelease(f2, 8, 2.5f, 3f);
        GameManager.I.fx.Impact(slamAt, 5f, 0.6f);
        FightCamera.Shake(0.9f); FightCamera.Punch(1.2f);
        Shot("grasp_slam");
        hCurl = 0.3f;
        for (float x = 0; x < 0.25f; x += Time.deltaTime) yield return null;
        yield return Dissolve(ShardForms.PalmCentre(hWrist, hFingers, hScale), 5f);
    }

    void HoldHero()
    {
        Vector3 c = ShardForms.PalmCentre(hWrist, hFingers, hScale) + hPalm * 0.42f * hScale;
        hero.BossHold(c - Vector3.up * 1.05f);
    }

    // ------------------------------------------------------------------ LANCE STORM
    IEnumerator Lances()
    {
        act = "lances";
        form = Form.Lances;
        lCount = enraged ? 12 : 8;
        swarm.chipScale = 0.95f;
        camLift = 2.1f;
        Announce("LANCE STORM", "KEEP MOVING  -  PARRY THE LANCES", 1.4f);
        Play("Summon", 0.15f);
        var dir = new Vector3[lCount];
        var flying = new bool[lCount];
        var stuck = new bool[lCount];
        var done = new bool[lCount];
        swarm.BeginForm(1.0f);
        for (float x = 0; x < 1.35f; x += Time.deltaTime)
        {
            goal = HoverSpot(4.8f);
            height = 1.6f;
            ArrangeLances(flying);
            yield return null;
        }
        Shot("lances_fan");
        var order = new int[lCount];
        for (int i = 0, lo = 0, hi = lCount - 1; i < lCount; i++) order[i] = i % 2 == 0 ? lo++ : hi--;
        int fired = 0;
        float next = 0, interval = enraged ? 0.1f : 0.15f;
        for (float x = 0; x < 4f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            ArrangeLances(flying);
            if (fired < lCount && x >= next)
            {
                int i = order[fired++];
                flying[i] = true;
                dir[i] = (HeroChest - lTip[i]).normalized;
                if (dir[i].y > -0.05f) dir[i] = (dir[i] + Vector3.down * 0.1f).normalized;
                GameManager.I.fx.Whoosh(Random.Range(1.1f, 1.4f), 0.45f);
                if (fired == 1) Play("Cast", 0.08f);
                next = x + interval;
            }
            bool anyFlying = false;
            for (int i = 0; i < lCount; i++)
            {
                if (!flying[i] || stuck[i]) continue;
                anyFlying = true;
                Vector3 prevTip = lTip[i];
                Vector3 step = dir[i] * 30f * dt;
                lTip[i] += step; lTail[i] += step;
                if (!done[i] && SegDist(HeroChest, prevTip, lTip[i]) < 0.5f)
                {
                    done[i] = true;
                    bool last = i == order[lCount - 1];
                    var r = Strike(prevTip - dir[i] * 2, 6, last ? 3.5f : 0, 1.5f, true, true, "lance");
                    if (r == Fighter.SwarmResult.Parried) dir[i] = (Vector3.up * 1.5f - dir[i]).normalized;
                }
                if (lTip[i].y <= 0.02f)
                {
                    float back = (0.02f - lTip[i].y) / Mathf.Max(0.05f, -dir[i].y);
                    lTip[i] += dir[i] * -back; lTail[i] += dir[i] * -back;
                    stuck[i] = true;
                    GameManager.I.fx.Crack(lTip[i], Random.Range(1.0f, 1.3f));
                }
                else if (Flat(lTip[i]).magnitude > 11.5f || lTip[i].y > 12) stuck[i] = true;
            }
            if (fired == lCount && !anyFlying) break;
            yield return null;
        }
        Shot("lances_stuck");
        for (float x = 0; x < 0.7f; x += Time.deltaTime) yield return null;
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        for (int i = 0; i < lCount; i++) swarm.Burst((lTip[i] + lTail[i]) * 0.5f, 1.6f, 1.2f, 1.5f);
        swarm.chipScale = 1;
        for (float x = 0; x < 0.3f; x += Time.deltaTime) yield return null;
    }

    void ArrangeLances(bool[] flying)
    {
        Vector3 f = ToHero(), r = Right(f);
        for (int i = 0; i < lCount; i++)
        {
            if (flying[i]) continue;
            float a = Mathf.Lerp(-80, 80, lCount > 1 ? i / (float)(lCount - 1) : 0.5f) * Mathf.Deg2Rad;
            Vector3 b = pos + Vector3.up * 2.3f - f * 0.4f + (r * Mathf.Sin(a) + Vector3.up * Mathf.Cos(a)) * (2.3f + 0.35f * (i % 2));
            Vector3 d = (HeroChest - b).normalized;
            lTail[i] = b - d * 1.3f;
            lTip[i] = b + d * 1.3f;
        }
    }

    // ------------------------------------------------------------------ SERPENT DRAGON
    IEnumerator Serpent()
    {
        act = "serpent";
        form = Form.Dragon;
        swarm.chipScale = 1.1f;
        camLift = 2.1f;
        gape = 5;
        Play("Cast", 0.15f);
        swarm.mode = ShardSwarm.Mode.Serpent;
        swarm.head = pos + Vector3.up * 1.4f;
        swarm.ResetTrail(swarm.head);
        Announce("SERPENT DRAGON", "DASH THROUGH IT OR PUNCH ITS HEAD", 1.3f);
        float speed = enraged ? 1.3f : 1f;
        float coil = 1.1f / speed, aim = 1.5f / speed, lunge = 2.3f / speed, back = 3.2f / speed;
        Vector3 from = swarm.head, to = swarm.head;
        bool struck = false;
        for (float x = 0; x < back; x += Time.deltaTime)
        {
            goal = HoverSpot(4.3f);
            height = 1.5f;
            Vector3 chest = HeroChest;
            if (x < coil)
            {
                float a = x / coil * Mathf.PI * 2.2f;
                swarm.head = pos + Vector3.up * (1.6f + x * 1.2f) + new Vector3(Mathf.Cos(a) * 1.8f, 0, Mathf.Sin(a) * 1.8f);
                gape = Mathf.Lerp(gape, 8, Time.deltaTime * 4);
            }
            else if (x < aim)
            {
                from = swarm.head;
                Vector3 rear = pos + Vector3.up * 3f + Flat(pos - hero.transform.position).normalized * 0.6f;
                swarm.head = Vector3.Lerp(swarm.head, rear, 1 - Mathf.Exp(-6 * Time.deltaTime));
                to = chest + (chest - swarm.head).normalized * 2.2f;
                gape = Mathf.Lerp(gape, 40, Time.deltaTime * 8);
                if (x + Time.deltaTime >= aim) Shot("serpent_aim");
            }
            else if (x < lunge)
            {
                float u = (x - aim) / (lunge - aim);
                swarm.head = Vector3.Lerp(from, to, Smooth(u));
                breakable = true;
                breakPoint = swarm.head;
                breakRadius = 0.7f;
                if (broken) break;
                if (!struck && (swarm.head - chest).sqrMagnitude < 0.75f * 0.75f)
                {
                    struck = true;
                    Strike(swarm.head, 14, 4.6f, 4f, true, true, "serpent");
                }
            }
            else
            {
                breakable = false;
                gape = Mathf.Lerp(gape, 6, Time.deltaTime * 6);
                swarm.head = Vector3.Lerp(swarm.head, pos + Vector3.up * 2.5f, 1 - Mathf.Exp(-5 * Time.deltaTime));
            }
            if (swarm.head.y < 0.3f) swarm.head = new Vector3(swarm.head.x, 0.3f, swarm.head.z);
            yield return null;
        }
        breakable = false;
        form = Form.None;
        if (broken)
        {
            swarm.mode = ShardSwarm.Mode.Free;
            yield return Broken("DRAGON BROKEN!");
        }
    }

    // ------------------------------------------------------------------ SHARD FLOOD
    IEnumerator Flood()
    {
        act = "flood";
        form = Form.None;
        camLift = 1.6f;
        Play("Summon", 0.15f);
        Vector3 f = ToHero();
        swarm.floodDir = f;
        swarm.floodOrigin = Flat(pos) - f * 0.5f;
        swarm.floodFront = -1;
        swarm.mode = ShardSwarm.Mode.Flood;
        Announce("SHARD FLOOD", "DASH THROUGH THE WAVE", 1.2f);
        bool struck = false;
        const float charge = 0.6f;
        for (float x = 0; swarm.floodFront < 14; x += Time.deltaTime)
        {
            if (x < charge) swarm.floodFront = Mathf.Lerp(-1, 0.6f, x / charge);
            else swarm.floodFront += Time.deltaTime * 10f;
            if (!struck && x > charge)
            {
                Vector3 rel = HeroFlat - swarm.floodOrigin;
                float along = Vector3.Dot(rel, f), lat = Vector3.Dot(rel, Right(f));
                if (Mathf.Abs(along - (swarm.floodFront - 0.5f)) < 0.6f && Mathf.Abs(lat) < 4.5f)
                {
                    struck = true;
                    Strike(pos, 11, 4.2f, 3.5f, false, true, "flood");
                    Shot("flood");
                }
            }
            yield return null;
        }
    }

    // ------------------------------------------------------------------ STONE ROC
    IEnumerator Roc()
    {
        act = "roc";
        director?.Cut(BossDirector.Shot.Low);
        form = Form.Roc;
        swarm.chipScale = 1.5f;
        camLift = 3.4f;
        rocFold = 0; rocTalons = 0;
        Announce("STONE ROC", "THE BIRD RETURNS  -  DASH WHEN IT DIVES", 1.6f);
        Play("Summon", 0.15f);
        Vector3 centre = HeroFlat;
        Vector3 off = Flat(pos - centre);
        float ang = Mathf.Atan2(off.z, off.x);
        rocC = pos + Vector3.up * 4f;
        rocF = Right(ToHero());
        rocU = Vector3.up;
        swarm.BeginForm(1.3f);
        bool screech = false;
        for (float x = 0; x < 3.0f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            ang += dt * 1.05f;
            centre = Vector3.ClampMagnitude(Vector3.Lerp(centre, HeroFlat, 1 - Mathf.Exp(-1 * dt)), 3.2f);   // keep its orbit over the courtyard
            Vector3 want = centre + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 5.2f + Vector3.up * 6.2f;
            rocC = Vector3.Lerp(rocC, want, 1 - Mathf.Exp(-2.2f * dt));
            Vector3 tangent = new Vector3(-Mathf.Sin(ang), 0, Mathf.Cos(ang));
            rocF = Vector3.Slerp(rocF, tangent, 1 - Mathf.Exp(-4 * dt)).normalized;
            rocU = (Vector3.up + Flat(centre - rocC).normalized * 0.45f).normalized;
            rocFlap = Mathf.Sin(t * 4.2f) * 36f;
            goal = HoverSpot(4.6f);
            height = 1.7f;
            if (x > 1.3f && !screech)
            {
                screech = true;
                GameManager.I.fx.Impact(Flat(rocC), 2.5f, 1.7f);
                FightCamera.Shake(0.35f);
                Shot("roc_soar");
            }
            yield return null;
        }
        int dives = enraged ? 2 : 1;
        for (int d = 0; d < dives; d++)
        {
            Announce("DIVE!", "DASH!", 0.7f, Gold);
            Vector3 p0 = rocC, p2 = HeroChest;
            Vector3 over = p2 + Flat(p2 - p0).normalized * 0.8f;
            Vector3 p1 = (p0 + p2) * 0.5f + Vector3.up * 2.5f;
            bool hit = false;
            GameManager.I.fx.Whoosh(0.45f, 1f);
            const float dur = 0.75f;
            for (float x = 0; x < dur; x += Time.deltaTime)
            {
                float e = Mathf.Pow(x / dur, 1.4f);
                Vector3 c = Bezier(p0, p1, over, e);
                Vector3 dv = c - rocC;
                if (dv.sqrMagnitude > 1e-5f) rocF = dv.normalized;
                rocC = c;
                rocU = Vector3.up;
                rocFold = Mathf.MoveTowards(rocFold, 1, Time.deltaTime * 4);
                rocTalons = Mathf.MoveTowards(rocTalons, 1, Time.deltaTime * 3);
                rocFlap = -10;
                if (!hit)
                {
                    Vector3 tal = ShardForms.RocTalons(rocC, rocF, rocU, RocScale, rocTalons);
                    if ((tal - HeroChest).magnitude < 1.25f)
                    {
                        hit = true;
                        Strike(rocC, 18, 5.5f, 4.5f, false, true, "roc");
                    }
                }
                yield return null;
            }
            Shot("roc_dive");
            Vector3 climb = Flat(rocF).normalized;
            if (climb.sqrMagnitude < 0.01f) climb = ToHero();
            for (float x = 0; x < 0.9f; x += Time.deltaTime)
            {
                float dt = Time.deltaTime;
                rocC += (climb * 11f + Vector3.up * 8f) * dt;
                rocF = Vector3.Slerp(rocF, (climb + Vector3.up * 0.8f).normalized, 1 - Mathf.Exp(-5 * dt));
                rocFold = Mathf.MoveTowards(rocFold, 0, dt * 3);
                rocTalons = Mathf.MoveTowards(rocTalons, 0, dt * 3);
                rocFlap = Mathf.Sin(t * 9) * 45;
                yield return null;
            }
            if (d < dives - 1)
            {
                for (float x = 0; x < 1.1f; x += Time.deltaTime)
                {
                    float dt = Time.deltaTime;
                    Vector3 want = Vector3.ClampMagnitude(HeroFlat, 3.2f) + Vector3.up * 6.2f + Flat(rocC - HeroFlat).normalized * 5f;
                    Vector3 to = want - rocC;
                    rocC = Vector3.Lerp(rocC, want, 1 - Mathf.Exp(-2.5f * dt));
                    if (to.sqrMagnitude > 0.01f) rocF = Vector3.Slerp(rocF, to.normalized, 1 - Mathf.Exp(-4 * dt));
                    rocU = Vector3.up;
                    rocFlap = Mathf.Sin(t * 5) * 38;
                    yield return null;
                }
            }
        }
        GameManager.I.fx.Impact(Flat(rocC), 6f, 1.3f);
        FightCamera.Shake(0.4f);
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.Burst(rocC, 6f, 1f, 8f);
        swarm.chipScale = 1;
        for (float x = 0; x < 0.4f; x += Time.deltaTime) yield return null;
    }

    // ------------------------------------------------------------------ SKYBREAKER
    IEnumerator Hammer()
    {
        act = "hammer";
        director?.Cut(BossDirector.Shot.Aerial);
        form = Form.Hammer;
        swarm.chipScale = 1.55f;
        camLift = 3.3f;
        Announce("SKYBREAKER", "GET OUT OF THE RED RING", 1.4f);
        Play("Uppercut", 0.15f);
        Vector3 target = HeroFlat;
        Vector3 f = ToHero();
        swarm.BeginForm(1.0f);
        for (float x = 0; x < 1.7f; x += Time.deltaTime)
        {
            target = Vector3.Lerp(target, HeroFlat, 1 - Mathf.Exp(-6 * Time.deltaTime));
            Vector3 fd = Flat(target - pos);
            if (fd.sqrMagnitude > 0.25f) f = fd.normalized;
            SetHammer(target, f, 90);
            ShowDisc(target, 2.0f);
            goal = HoverSpot(4.8f);
            height = 1.8f;
            yield return null;
        }
        Shot("hammer_raised");
        for (float x = 0; x < 0.35f; x += Time.deltaTime) { SetHammer(target, f, 90); ShowDisc(target, 2.0f, 3); yield return null; }
        Play("Cast", 0.08f);
        GameManager.I.fx.Whoosh(0.45f, 1f);
        for (float x = 0; x < 0.2f; x += Time.deltaTime)
        {
            float e = x / 0.2f;
            SetHammer(target, f, Mathf.Lerp(90, 0, e * e));
            yield return null;
        }
        SetHammer(target, f, 0);
        HideWarn();
        GameManager.I.fx.Impact(target, 6.5f, 0.55f);
        GameManager.I.fx.Chroma(1f);
        FightCamera.Shake(1f); FightCamera.Punch(1.4f);
        if (Flat(hero.transform.position - target).magnitude < 2.0f && hero.Height - hero.floorY < 1.5f)
            Strike(target, 20, 6.5f, 3f, false, false, "hammer");
        Shot("hammer_impact");
        for (float x = 0; x < 0.45f; x += Time.deltaTime) yield return null;
        yield return Dissolve(target + Vector3.up * 1.5f, 5f);
    }

    void SetHammer(Vector3 target, Vector3 f, float betaDeg)
    {
        float b = betaDeg * Mathf.Deg2Rad;
        hmGrip = target + Vector3.up * 0.95f * HmScale - f * 2.9f * HmScale;
        hmHandle = Mathf.Cos(b) * f + Mathf.Sin(b) * Vector3.up;
        hmAxis = Mathf.Cos(b) * Vector3.up - Mathf.Sin(b) * f;
    }

    // ------------------------------------------------------------------ ASCENSION: THE TITAN
    // ------------------------------------------------------------------ THE HUNT
    // Moros rises over the town and sends shard serpents through the streets to find Kaito. They
    // only see him in a clear line of sight; one that reaches him becomes a hand and drags him back.
    IEnumerator Hunt()
    {
        act = "hunt";
        hunting = true;
        fled = false;
        form = Form.None;
        caughtHunter = null;
        delivered = false;
        huntsDone++;
        lastSeenHero = HeroFlat;
        Announce("THE HUNT", "RUN!  BREAK LINE OF SIGHT  -  HIDE BEHIND THE HOUSES", 2.6f);
        Play("Casting", 0.15f);
        swarm.mode = ShardSwarm.Mode.Halo;
        director?.Cut(BossDirector.Shot.Aerial);
        // he rises over the rooftops first - Kaito's head start
        for (float x = 0; x < 1.3f; x += Time.deltaTime) { goal = Flat(pos); height = 7.5f; yield return null; }
        GameManager.I.fx.Impact(Flat(pos), 4f, 0.8f);
        int n = ascended ? 6 : 4;
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Hunter" + i);
            var h = go.AddComponent<Hunter>();
            Vector3 start = pos + Vector3.up * 2f + Quaternion.Euler(0, i * 360f / n, 0) * Vector3.forward * 1.2f;
            h.Init(this, hero, shardMats, eyeMat, start, 600, ascended ? 1.2f : 1f);
            hunters.Add(h);
        }
        huntSaved = hero.brain;
        if (AutoCapture.Active)
        {
            var flee = hero.gameObject.GetComponent<FleeBrain>() ?? hero.gameObject.AddComponent<FleeBrain>();
            hero.brain = flee;
        }
        Shot("hunt_start");
        float dur = ascended ? 22 : 18;
        for (float x = 0; x < dur && caughtHunter == null && !Complete; x += Time.deltaTime)
        {
            // Moros floats high over the rooftops, drifting toward where Kaito was last seen
            goal = Vector3.Lerp(Flat(pos), lastSeenHero, 0.35f);
            height = 7.5f;
            if (x > 2 && Mathf.Repeat(x, 2.4f) < Time.deltaTime) Shot("hunt");
            yield return null;
        }
        if (caughtHunter != null)
        {
            act = "hunt_carry";
            Announce("CAUGHT!", "THE SWARM DRAGS YOU TO MOROS", 1.4f, Gold);
            director?.Cut(BossDirector.Shot.Chase);
            for (float x = 0; x < 8f && !delivered && caughtHunter != null && caughtHunter.state == Hunter.State.Carry; x += Time.deltaTime)
            {
                height = 1.0f;
                goal = Flat(pos);
                yield return null;
            }
            if (delivered)
            {
                Play("Uppercut", 0.08f);
                for (float x = 0; x < 0.3f; x += Time.deltaTime) { height = 0.6f; yield return null; }
                Vector3 away = ToHero();
                hero.BossRelease(away, 12, 5f, 4f);
                GameManager.I.fx.Impact(HeroFlat, 4.5f, 0.7f);
                GameManager.I.fx.Splash(HeroChest, away, 0.8f);
                FightCamera.Shake(0.7f);
                Shot("hunt_punish");
            }
        }
        else
        {
            Announce("MOROS LOST YOUR TRAIL", "HE COMES DOWN TO FIND YOU", 1.6f, Gold);
        }
        foreach (var h in hunters) if (h) h.Dissolve();
        hunters.Clear();
        if (hero.brain is FleeBrain) hero.brain = huntSaved;
        hunting = false;
    }

    public Vector3 DropPoint => Flat(pos) + ToHero() * 1.4f;

    public void OnHunterSpotted(Hunter h)
    {
        lastSeenHero = HeroFlat;
        if (Time.time - lastSpotCall > 3f)
        {
            lastSpotCall = Time.time;
            Announce("SPOTTED!", "RUN!", 0.8f, Gold);
            GameManager.I.fx.Crack(h.Head, 1.3f);
            director?.Cut(BossDirector.Shot.Flee);
            Shot("hunt_spotted");
        }
    }

    public void OnHunterCaught(Hunter h)
    {
        if (caughtHunter == null) caughtHunter = h;
        Shot("hunt_caught");
    }

    public void OnHunterDelivered(Hunter h)
    {
        if (h == caughtHunter) delivered = true;
    }

    // ------------------------------------------------------------------ landing: melee + punish window
    IEnumerator Brawl()
    {
        act = "descend";
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.chipScale = 1;
        camLift = 1.2f;
        Play("Idle", 0.2f);
        Vector3 away = Flat(pos - HeroFlat).normalized;
        for (float x = 0; x < 0.75f; x += Time.deltaTime)
        {
            away = Flat(pos - HeroFlat).normalized;
            goal = HeroFlat + away * 1.05f;
            height = Mathf.Lerp(1.5f, 0, Mathf.Clamp01(x / 0.6f));
            yield return null;
        }
        act = "melee";
        windowHits = 0;
        knockedBack = false;
        Play("Cast", 0.08f);
        float dur = enraged ? 2.2f : 1.5f;
        bool s1 = false, s2 = false, s3 = false, s4 = false, s5 = false;
        for (float x = 0; x < dur && !knockedBack; x += Time.deltaTime)
        {
            height = 0;
            away = Flat(pos - HeroFlat).normalized;
            goal = HeroFlat + away * 1.0f;
            if (!s1 && x >= 0.33f) { s1 = true; Melee(8, 0); }
            if (!s2 && x >= 0.75f) { s2 = true; Play("Hook", 0.08f); }
            if (!s3 && x >= 1.08f) { s3 = true; Melee(8, 0); }
            if (enraged && !s4 && x >= 1.5f) { s4 = true; Play("Uppercut", 0.08f); }
            if (enraged && !s5 && x >= 1.85f) { s5 = true; Melee(10, 4f); }
            yield return null;
        }
        if (!knockedBack)
        {
            act = "open";
            open = true;
            windowHits = 0;
            Play("Idle", 0.2f);
            Announce("HE'S OPEN!", "STRIKE NOW", 1f, Gold);
            for (float x = 0; x < (enraged ? 2.4f : 3.0f) && !knockedBack; x += Time.deltaTime) { height = 0; yield return null; }
            open = false;
        }
        if (knockedBack) yield return Stagger();
    }

    IEnumerator Stagger()
    {
        knockedBack = false;
        open = false;
        act = "stagger";
        Play("Stagger", 0.05f);
        Vector3 away = Flat(pos - HeroFlat).normalized;
        for (float x = 0; x < 1.3f; x += Time.deltaTime)
        {
            goal = Flat(pos) + away * 2f;
            height = Mathf.Lerp(0, 1.5f, Mathf.Clamp01((x - 0.5f) / 0.8f));
            yield return null;
        }
    }

    IEnumerator Broken(string title)
    {
        act = "broken";
        form = Form.None;
        breakable = false;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.Burst(breakPoint, 7, 3, 4);
        swarm.chipScale = 1;
        GameManager.I.fx.Impact(breakPoint, 3f, 0.9f);
        FightCamera.Shake(0.45f);
        Announce(title, "HE'S FALLING  -  PUNISH HIM", 1.3f, Gold);
        Shot("broken");
        Play("Stagger", 0.05f);
        for (float x = 0; x < 0.5f; x += Time.deltaTime) yield return null;
    }

    IEnumerator Dissolve(Vector3 at, float power)
    {
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.Burst(at, power, 1.5f, 8f);
        swarm.chipScale = 1;
        for (float x = 0; x < 0.35f; x += Time.deltaTime) yield return null;
    }

    // THE FINALE CUTSCENE: the killing blow in slow motion, Moros kneels as the cracks in his stone
    // body burn brighter and he whispers his last words, then shatters into the sky while the
    // camera looks up from the flagstones - and Kaito answers him.
    IEnumerator Death()
    {
        dying = true;
        Cleanup();
        act = "dying";
        var hud = GameManager.I.hud;
        hud.Cinematic(true);
        Cine.Begin(false);
        swarm.mode = ShardSwarm.Mode.Free;
        Vector3 m = Flat(pos), hf = HeroFlat;
        Vector3 ax = m - hf;
        ax = ax.sqrMagnitude > 0.01f ? ax.normalized : Vector3.forward;
        Vector3 sd = Vector3.Cross(Vector3.up, ax);
        Vector3 mid = (hf + m) * 0.5f;
        Play("Stagger", 0.05f);
        TimeFX.slow = 0.25f;
        Title("K.O.", "", 1.4f, new Color(1f, 0.2f, 0.12f));
        yield return Cine.Shot(1.3f, mid + sd * 4.5f + Vector3.up * 1.3f, mid + sd * 3.8f + Vector3.up * 1.5f, m + Vector3.up * 1.4f, m + Vector3.up * 1.3f, 40, 36, x => height = 0);
        TimeFX.slow = 1f;
        Play("Fall", 0.1f);
        hud.Dialogue("MOROS", Ember, "The stones... remember...", 2.4f);
        Atmosphere.Focus(() => hips ? hips.position : pos + Vector3.up, 2.2f);
        deathGlow = 0.2f;
        yield return Cine.Shot(2.2f, m + sd * 3.6f - ax * 0.8f + Vector3.up * 1.3f, m + sd * 3.0f - ax * 0.6f + Vector3.up * 1.1f,
                               m - ax * 0.4f + Vector3.up * 0.8f, m - ax * 0.4f + Vector3.up * 0.6f, 44, 40, x =>
        {
            height = 0;
            deathGlow = Mathf.Lerp(0.2f, 1.6f, x / 2.2f) * (0.85f + 0.15f * Mathf.Sin(x * 40f));
        });
        // he shatters
        Vector3 c = hips ? hips.position : pos + Vector3.up;
        swarm.active = swarm.Count;
        swarm.Gather(c, 0.5f);
        swarm.Burst(c, 9, 4);
        model.SetActive(false);
        aura.intensity = 0;
        deathGlow = -1;
        GameManager.I.fx.Impact(Flat(pos), 6f, 0.6f);
        FightCamera.Shake(0.8f);
        Title("MOROS SHATTERS", "THE SWARM FALLS SILENT", 3f, Gold);
        Shot("shatter");
        TimeFX.slow = 0.4f;
        yield return Cine.Shot(2.4f, m + sd * 5f - ax * 1f + Vector3.up * 0.5f, m + sd * 8.5f - ax * 2f + Vector3.up * 3.5f, m + Vector3.up * 1.5f, m + Vector3.up * 4.5f, 55, 60);
        TimeFX.slow = 1f;
        // Kaito answers
        hud.Dialogue("KAITO", hero.accent, "Then let them remember this.", 2.4f);
        Vector3 hc = hero.Body;
        Atmosphere.Focus(() => hero.Body + Vector3.up * 1.4f, 2.4f);
        Vector3 hfwd = Flat(hero.transform.forward);
        hfwd = hfwd.sqrMagnitude > 0.01f ? hfwd.normalized : ax;
        Vector3 hsd = Vector3.Cross(Vector3.up, hfwd);
        yield return Cine.Shot(2.4f, hc + hfwd * 2.4f + hsd * 1.1f + Vector3.up * 1.55f, hc + hfwd * 1.9f + hsd * 0.9f + Vector3.up * 1.6f,
                               hc + Vector3.up * 1.5f, hc + Vector3.up * 1.55f, 36, 32);
        Cine.End();   // the letterbox stays up over the victory
        HeroWon = true;
        Complete = true;
    }

    IEnumerator Victory()
    {
        Cleanup();
        act = "victory";
        swarm.mode = ShardSwarm.Mode.Halo;
        Play("Victory", 0.2f);
        HeroWon = false;
        Complete = true;
        yield break;
    }

    void Cleanup()
    {
        if (hero.brain is FleeBrain && huntSaved != null) hero.brain = huntSaved;
        ammo?.Clear();
        foreach (var h in hunters) if (h) h.Dissolve();
        hunters.Clear();
        hunting = false;
        form = Form.None;
        breakable = false;
        open = false;
        HideWarn();
        CobraOff();
        formCracks = 0; crackForm = Form.None;   // a new formation starts whole
        sphCoreScale = 0;
        wallCore = false;
        if (hero.bossHeld) hero.BossRelease(Flat(hero.transform.position - pos), 0, 2f, 1f);
        swarm.chipScale = 1;
        camLift = 1;
    }

    // ================================================================== per-frame
    void BuildForm(ShardSwarm s)
    {
        switch (form)
        {
            case Form.Axe: ShardForms.Axe(s.parts, axGrip, axDir, axEdge, AxLen, AxScale); break;
            case Form.Hand: ShardForms.Hand(s.parts, hWrist, hFingers, hPalm, hScale, hCurl, hArm); break;
            case Form.Lances: for (int i = 0; i < lCount; i++) ShardForms.Spear(s.parts, lTail[i], lTip[i], 1.2f); break;
            case Form.Roc: ShardForms.Roc(s.parts, rocC, rocF, rocU, RocScale, rocFlap, rocFold, rocTalons, out eyeL, out eyeR); break;
            case Form.Hammer: ShardForms.Hammer(s.parts, hmGrip, hmHandle, hmAxis, HmScale); break;
            case Form.Titan: ShardForms.Titan(s.parts, tp, out eyeL, out eyeR); break;
            case Form.Dragon:
                if (s.mode == ShardSwarm.Mode.Serpent) ShardForms.DragonHead(s.headParts, s.head, s.HeadForward, gape, out eyeL, out eyeR);
                break;
            default: BuildArsenalForm(s); break;
        }
    }

    void Update()
    {
        if (hero == null) return;
        float dt = Time.deltaTime;
        t += dt;

        if (!dying && !Complete && hero.health <= 0)
        {
            StopAllCoroutines();
            StartCoroutine(Victory());
        }

        // body: glide toward the goal, bob while floating, face Kaito
        Vector3 g = Flat(goal);
        var town = TownMap.I;
        if (town != null) { g.x = Mathf.Clamp(g.x, town.min.x, town.max.x); g.z = Mathf.Clamp(g.z, town.min.y, town.max.y); }
        else if (g.magnitude > 7.2f) g = g.normalized * 7.2f;
        farT = Vector3.Distance(HeroFlat, Flat(pos)) > 15f ? farT + dt : 0;
        bool fast = act == "descend";
        Vector3 flat = Vector3.SmoothDamp(Flat(pos), g, ref posVel, fast ? 0.22f : 0.4f);
        if (form == Form.Titan) { flat = g; posVel = Vector3.zero; }
        float bob = height > 0.3f ? Mathf.Sin(t * 2.2f) * 0.08f : 0;
        float floorLift = form == Form.Titan || hunting ? 0 : Mathf.Clamp(hero.floorY, 0, 9f);
        float wantY = height + bob + floorLift;
        if (town != null && form != Form.Titan) wantY = Mathf.Max(wantY, town.HeightAt(flat, 0.8f) + 0.3f);   // never through a roof
        float y = Mathf.SmoothDamp(pos.y, wantY, ref heightVel, fast ? 0.14f : 0.3f);
        pos = new Vector3(flat.x, y, flat.z);
        if (model)
        {
            model.transform.position = pos;
            Vector3 look = Flat(hero.transform.position - pos);
            if (look.sqrMagnitude > 0.01f && !dying)
                model.transform.rotation = Quaternion.Slerp(model.transform.rotation, Quaternion.LookRotation(look), 1 - Mathf.Exp(-6 * dt));
            swarm.haloBack = -model.transform.forward;
        }
        swarm.center = pos;

        aura.transform.position = pos + Vector3.up * 1.2f;
        if (act != "rise" && !dying)
            aura.intensity = Mathf.Lerp(aura.intensity, (ascended ? 4.5f : 2.4f) + Mathf.Sin(t * 6) * 0.5f, dt * 3);
        // his stone body smoulders; after the ascension it burns
        glow = Mathf.Lerp(glow, ascended ? 0.55f + 0.35f * Mathf.Sin(t * 5) : 0.08f + 0.05f * Mathf.Sin(t * 2), dt * 3);
        if (deathGlow >= 0) glow = deathGlow;
        mpb.SetColor("_EmissionColor", Ember * glow * 2.2f);
        foreach (var r in body) if (r) r.SetPropertyBlock(mpb);

        if (warnDisc.gameObject.activeSelf || warnStrip.gameObject.activeSelf) warnPulse += dt;

        // Kaito's opponent anchor follows Moros so he faces, approaches and spaces against him
        if (proxy)
        {
            proxy.transform.position = Flat(pos);
            proxy.health = hp; proxy.maxHealth = maxHp; proxy.stamina = proxy.maxStamina;
        }
        var cam = GameManager.I.cam;
        float lift = Mathf.Max(camLift, Mathf.Clamp(pos.y, 0, 1.6f) * 0.8f);
        cam.lift = Mathf.Lerp(cam.lift, lift, 1 - Mathf.Exp(-2.5f * dt));

        if (AutoCapture.Active)
        {
            shotT += Clock.Dt;
            if (shotT > 1.6f) { shotT = 0; Shot(act); }
        }
    }

    void LateUpdate()
    {
        CobraRefresh();   // the King Cobra's flesh follows the pose its coroutine set this frame
        CoresLate();      // the stone ball's core, the wall's blocks
        TrackFormAge();
        // camera: keep Kaito, Moros and the whole weapon on screen (FightCamera runs after this)
        var cam = GameManager.I.cam;
        cam.bossFrame = !Complete && !dying && hero != null;
        if (cam.bossFrame) cam.frameBounds = FrameBounds();
        if (form != Form.Titan) swarm.formStretch = 1;   // long shards belong to the giant only
        // HUD tracker: where Moros is (and which hunting swarms are on Kaito's trail)
        var hud = GameManager.I.hud;
        hud.trackOn = cam.bossFrame;
        hud.trackPos = form == Form.Titan ? ShardForms.TitanHead(tp) + Vector3.up * 1.5f : pos + Vector3.up * 2.4f;
        hud.threats.Clear();
        foreach (var h in hunters)
            if (h != null && (h.state == Hunter.State.Chase || h.state == Hunter.State.Carry)) hud.threats.Add(h.Head);
        if (cobOn) hud.threats.Add(CobraHeadCentre + Vector3.up);
        if (director != null)
        {
            director.bounds = cam.frameBounds;
            bool titan = form == Form.Titan;
            bool cob = form == Form.Cobra && cobOn;
            bool sph = form == Form.Sphere, wal = form == Form.Wall;
            director.boss = titan ? tp.feet + Vector3.up * 7.0f * TitanScale : cob ? CobraHeadCentre - Vector3.up * 1.5f
                          : sph ? sphC : wal ? wallC + Vector3.up * WallH * 0.5f : pos + Vector3.up * 1.2f;
            director.bossHead = titan ? ShardForms.TitanHead(tp) : form == Form.GreatDragon ? drag[0] : form == Form.Cobra && cobOn ? CobraHeadCentre
                              : sph ? sphC + Vector3.up * SphR : wal ? wallC + Vector3.up * WallH : pos + Vector3.up * 1.9f;
            director.bossFacing = titan ? tp.fwd : cob ? Flat(cobHeadFwd) : wal ? wallF : sph ? ToHeroFrom(sphC) : Vector3.zero;
            director.bossClearance = titan ? 7.5f * TitanScale : cob ? 4.5f : sph ? SphR + 3f : wal ? 3f : 1.5f;
            director.blocker = cob ? cobraBlocks : sph || wal ? FormBlocks : null;
            director.bossChases = cob;
            director.context = act == "rise" || act == "reveal" || (act == "ascension" && form != Form.Titan) ? BossDirector.Context.Cinematic
                             : HeroIndoors ? BossDirector.Context.Indoor
                             : hunting ? BossDirector.Context.Hunt
                             : titan ? BossDirector.Context.Titan
                             : cob || sph || wal ? BossDirector.Context.Cobra
                             : BossDirector.Context.Fight;
        }

        bool show = form == Form.Roc || form == Form.Titan || form == Form.GreatDragon || form == Form.Cobra || (form == Form.Dragon && swarm.mode == ShardSwarm.Mode.Serpent);
        float want = !show ? 0 : form == Form.Titan ? 0.26f * TitanScale : form == Form.GreatDragon ? 0.34f : form == Form.Cobra ? 0.13f : form == Form.Roc ? 0.24f : 0.14f;
        eyeSize = Mathf.MoveTowards(eyeSize, want, Time.deltaTime * 0.6f);
        for (int i = 0; i < 2; i++)
        {
            bool on = eyeSize > 0.01f;
            if (eyes[i].gameObject.activeSelf != on) eyes[i].gameObject.SetActive(on);
            if (!on) continue;
            eyes[i].position = i == 0 ? eyeL : eyeR;
            eyes[i].localScale = new Vector3(eyeSize, eyeSize * 0.55f, eyeSize);
        }
    }

    Bounds FrameBounds()
    {
        var b = new Bounds(hero.Body + Vector3.up * 0.95f, new Vector3(1.4f, 2.2f, 1.4f));
        if (model && model.activeSelf) b.Encapsulate(new Bounds(pos + Vector3.up * 1.15f, new Vector3(1.4f, 2.5f, 1.4f)));
        else b.Encapsulate(Flat(pos) + Vector3.up * 1.5f);
        switch (form)
        {
            case Form.Axe:
                b.Encapsulate(axGrip - axDir * 0.9f);
                b.Encapsulate(axGrip + axDir * (AxLen + 1.2f));
                b.Encapsulate(ShardForms.AxeEdge(axGrip, axDir, axEdge, AxLen, AxScale));
                break;
            case Form.Hand:
                b.Encapsulate(hWrist);
                b.Encapsulate(hWrist + hFingers * 1.9f * hScale);
                b.Encapsulate(ShardForms.PalmCentre(hWrist, hFingers, hScale) + hPalm * 0.7f * hScale);
                b.Encapsulate(hArm);
                break;
            case Form.Lances:
                for (int i = 0; i < lCount; i++) { b.Encapsulate(lTip[i]); b.Encapsulate(lTail[i]); }
                break;
            case Form.Roc:
                b.Encapsulate(new Bounds(new Vector3(rocC.x, Mathf.Min(rocC.y, 8.5f), rocC.z), new Vector3(6f, 3f, 6f)));
                break;
            case Form.Hammer:
                b.Encapsulate(hmGrip);
                b.Encapsulate(new Bounds(hmGrip + hmHandle * 2.9f * HmScale, Vector3.one * 2.4f * HmScale));
                break;
            case Form.Titan:
                b.Encapsulate(new Bounds(tp.feet + Vector3.up * 5.4f * TitanScale, new Vector3(7f, 11f, 7f) * TitanScale));
                b.Encapsulate(tp.wristL);
                b.Encapsulate(tp.wristR);
                break;
            case Form.Dragon:
                b.Encapsulate(new Bounds(swarm.head, Vector3.one * 1.6f));
                break;
            default: FrameArsenal(ref b); break;
        }
        if (swarm.mode == ShardSwarm.Mode.Twister) b.Encapsulate(Flat(swarm.center) + Vector3.up * swarm.twisterHeight);
        foreach (var h in hunters) if (h != null && (h.state == Hunter.State.Chase || h.state == Hunter.State.Carry)) b.Encapsulate(h.Head);
        if (warnDisc.gameObject.activeSelf) b.Encapsulate(new Bounds(warnDisc.position, warnDisc.localScale));
        return b;
    }

    // ================================================================== helpers
    Fighter.SwarmResult Strike(Vector3 from, float damage, float up, float push, bool canParry, bool canBlock, string tag)
    {
        var r = hero.SwarmStrike(from, damage, up, push, canParry, canBlock);
        if (r == Fighter.SwarmResult.Hit)
        {
            GameManager.I.fx.Splash(HeroChest, Flat(hero.transform.position - from).normalized, Mathf.Clamp01(damage / 16f));
            TimeFX.HitStop(0.07f);
            FightCamera.Shake(0.35f);
            if (tag != null) Shot("hit_" + tag);
        }
        else if (r == Fighter.SwarmResult.Parried)
        {
            if (tag == "blade" || tag == "serpent" || tag == "sweep") broken = true;
            if (tag != null) Shot("parry_" + tag);
        }
        return r;
    }

    void Melee(float dmg, float up)
    {
        Vector3 d = HeroFlat - Flat(pos);
        if (d.magnitude < 1.6f && Vector3.Dot(model.transform.forward, d.normalized) > 0.3f)
            Strike(pos, dmg, up, 1.6f, true, true, null);
        swarm.Burst(pos + Vector3.up, 2, 1, 2);
    }

    void ShowDisc(Vector3 at, float radius, float rate = 1.5f)
    {
        warnDisc.gameObject.SetActive(true);
        float pulse = 1 + 0.08f * Mathf.Sin(warnPulse * 18 * rate);
        warnDisc.position = Flat(at) + Vector3.up * (0.03f + HeroFloor);
        warnDisc.localScale = new Vector3(radius * 2 * pulse, 0.005f, radius * 2 * pulse);
    }

    void ShowStrip(Vector3 from, Vector3 dir, float length)
    {
        warnStrip.gameObject.SetActive(true);
        warnStrip.position = Flat(from) + dir * length * 0.5f + Vector3.up * (0.03f + HeroFloor);
        warnStrip.rotation = Quaternion.LookRotation(dir);
        warnStrip.localScale = new Vector3(1.5f, 0.01f, length);
    }

    void HideWarn()
    {
        warnDisc.gameObject.SetActive(false);
        warnStrip.gameObject.SetActive(false);
    }

    Vector3 HoverSpot(float dist)
    {
        var map = TownMap.I;
        if (map != null)
        {
            int room = map.RoomAt(hero.transform.position);
            if (room >= 0)
            {
                var m = map.rooms[room];
                Vector3 door = map.NearestDoor(room, HeroFlat);
                Vector3 outDir = Flat(door - new Vector3(m.c.x, 0, m.c.y)).normalized;
                return door + outDir * 4.5f;
            }
        }
        Vector3 away = Flat(pos - HeroFlat);
        away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
        away = Quaternion.Euler(0, Mathf.Sin(t * 0.35f) * 25f * Time.deltaTime, 0) * away;
        Vector3 spot = HeroFlat + away * dist;
        // in the courtyard he keeps to its open middle; out in the town he follows Kaito
        if ((map == null || map.ZoneAt(hero.transform.position) == TownMap.Zone.Courtyard) && spot.magnitude > 7f) spot = spot.normalized * 7f;
        return spot;
    }

    Vector3 HeroFlat => Flat(hero.transform.position);
    bool HeroHidden => TownMap.I != null && (TownMap.Blocked(pos + Vector3.up * 1.5f, HeroChest) || Flat(hero.transform.position - pos).magnitude > 30f);
    Vector3 HeroChest => hero.Body + Vector3.up * 1.1f;
    Vector3 ToHero()
    {
        Vector3 d = Flat(hero.transform.position - pos);
        return d.sqrMagnitude > 0.01f ? d.normalized : (model ? model.transform.forward : Vector3.back);
    }

    static Vector3 Right(Vector3 f) => Vector3.Cross(Vector3.up, f).normalized;
    Vector3 ToHeroFrom(Vector3 p) { Vector3 d = Flat(hero.transform.position - p); return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward; }
    System.Func<Vector3, bool> formBlocksFn;
    System.Func<Vector3, bool> FormBlocks => formBlocksFn ??= p => NearFormation(p, 1.6f);
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u) => Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(b, c, u), u);
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float l = ab.sqrMagnitude;
        float u = l > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l) : 0;
        return Vector3.Distance(p, a + ab * u);
    }

    void Play(string state, float fade)
    {
        if (an && an.gameObject.activeInHierarchy && an.HasState(0, Animator.StringToHash(state))) an.CrossFadeInFixedTime(state, fade);
    }

    // attack calls go small and high (BossCall) so they never cover the action; cinematic beats stay big
    void Announce(string big, string small, float dur, Color? c = null) => GameManager.I.hud.BossCall(big, small, dur, c ?? Ember);
    void Title(string big, string small, float dur, Color? c = null) => GameManager.I.hud.Announce(big, small, dur, c ?? Ember);

    void Shot(string name)
    {
        if (!AutoCapture.Active || shot > 90 || Clock.Now - lastShot < 0.35f) return;
        lastShot = Clock.Now;
        string path = System.IO.Path.Combine(AutoCapture.Dir, $"boss_{shot++:00}_{name}.png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log($"[AUTO] boss shot {path} act={act} hp={hp} kaito={hero.health}");
    }
}

// Autopilot only (capture runs): during a hunt Kaito runs for distant streets away from Moros and
// dashes now and then, so the hunt can be tested without a player.
public class FleeBrain : Brain
{
    Vector3 target;
    float t, climbT = 2f;

    public override Intent Think(Fighter self, Fighter foe, float dt)
    {
        t -= dt;
        Vector3 p = self.transform.position;
        if (t <= 0 || (new Vector3(target.x - p.x, 0, target.z - p.z)).magnitude < 2f)
        {
            t = 5f;
            float best = -1;
            for (int k = 0; k < 8 && TownMap.I != null; k++)
            {
                var c = TownMap.I.RandomStreetPoint();
                float d = foe != null ? (c - foe.transform.position).magnitude : 0;
                if (d > best) { best = d; target = c; }
            }
        }
        var i = new Intent();
        Vector3 to = target - p; to.y = 0;
        i.move = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.zero;
        i.dash = Random.value < 0.006f;
        // parkour now and then: run up a wall in the way, leap from the rooftops
        climbT -= dt;
        if (climbT <= 0 && TownMap.I != null && self.state == Fighter.State.Neutral)
        {
            if (TownMap.I.ClimbTarget(p, i.move, self.Y, out _, out _)) { i.jump = true; climbT = 4f; }
            else if (self.OnRoof && Random.value < 0.02f) { i.jump = true; climbT = 1.5f; }
        }
        return i;
    }
}
