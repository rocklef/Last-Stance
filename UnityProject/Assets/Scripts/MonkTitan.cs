using System.Collections;
using UnityEngine;

// MOROS, part four: THE TITAN in motion. The giant is not a statue - it has a body that moves:
//   GAIT        procedural walking and running: each foot plants and stays planted while the body
//               passes over it (no sliding), swings forward in an arc, and lands with a quake; the
//               pelvis bobs and sways, the torso leans into the run and twists against the legs, and
//               the arms pump in opposition. It runs faster than Kaito can sprint.
//   LEAPS       a building in the way? It crouches and leaps over the roofs after him.
//   ATTACKS     built for its body: a running grab (it bends down to snatch him), a leap smash, a
//               two-handed hammer fist, a low arm sweep (jump it), a stomp - and it can simply step
//               on him.
public partial class MonkBoss
{
    Vector3 tRoot, tVel;
    float tY, tPhase, tStagger, tBlocked, tStepHitCd;
    float tCrouchWant, tLeanWant, tLeanRate = 2.8f, tCrouchRate = 2.5f, tTwistWant;
    bool tAir, tTwistHold;
    readonly bool[] tArmBusy = new bool[2];
    int tFootOverride;
    Vector3 tFootTarget;
    readonly float[] tPrevU = new float[2];
    const float TitanWalk = 3.4f, TitanRun = 7.6f;

    Vector3 TitanForward => Flat(tp.fwd).normalized;

    void TitanBegin()
    {
        float s = TitanScale;
        tRoot = Flat(pos);
        tVel = Vector3.zero;
        tY = 0;
        tAir = false;
        tStagger = 0;
        tArmBusy[0] = tArmBusy[1] = false;
        tFootOverride = 0;
        tCrouchWant = tLeanWant = tTwistWant = 0;
        tLeanRate = 2.8f; tCrouchRate = 2.5f;
        Vector3 f = ToHero(), r = Right(f);
        tp = new ShardForms.TitanPose { feet = tRoot, fwd = f, scale = s };
        tp.footL = tRoot - r * 1.0f * s + Vector3.up * 0.6f * s;
        tp.footR = tRoot + r * 1.0f * s + Vector3.up * 0.6f * s;
        for (int sd = -1; sd <= 1; sd += 2)
            SetTitanHand(sd, ShardForms.TitanShoulder(tp, sd) + (-Vector3.up * 4.6f + f * 0.5f + r * sd * 0.6f) * s, Vector3.down, -r * sd, 0.4f, 1);
    }

    // compatibility: stand (or walk after him) with relaxed arms
    void TitanRest(bool follow = true, bool arms = true) => TitanMove(follow ? TitanWalk : 0, HeroFlat, 7f);

    // one frame of the titan's body: move toward target at up to speed, stop within stopDist
    void TitanMove(float speed, Vector3 target, float stopDist = 6f, bool face = true)
    {
        float dt = Time.deltaTime;
        if (dt <= 0) return;
        float s = TitanScale;
        tStepHitCd -= dt;
        Vector3 to = Flat(target) - tRoot;
        float dist = to.magnitude;
        Vector3 dir = dist > 0.1f ? to / dist : TitanForward;
        if (!tAir)
        {
            float want = dist > stopDist ? speed : 0;
            Vector3 vWant = dir * want;
            tVel = Vector3.MoveTowards(tVel, vWant, (want > tVel.magnitude ? 7f : 12f) * dt);
            Vector3 next = tRoot + tVel * dt;
            if (TownMap.I != null)
            {
                // it steps over walls of crates and lanterns; houses and town walls stop it
                Vector3 res = TownMap.I.Resolve(next, 1.1f * s, 3.2f);
                tBlocked = (res - next).sqrMagnitude > 1e-5f && want > 0 ? tBlocked + dt : 0;
                next = res;
            }
            tRoot = new Vector3(next.x, 0, next.z);
        }
        Vector3 faceDir = face ? dir : (tVel.sqrMagnitude > 0.5f ? tVel.normalized : TitanForward);
        tp.fwd = Vector3.RotateTowards(TitanForward, faceDir, 3.2f * dt, 0).normalized;
        Vector3 f = TitanForward, r = Right(f), u = Vector3.up;

        float sp = tAir ? 0 : tVel.magnitude;
        float run01 = Mathf.Clamp01((sp - 2.5f) / (TitanRun - 2.5f));
        float cadence = 0.5f + sp * 0.09f;
        float duty = Mathf.Lerp(0.6f, 0.38f, run01);
        if (sp > 0.2f) tPhase += cadence * dt;
        Vector3 mv = sp > 0.05f ? tVel / sp : f;
        float stride = sp * duty / cadence;
        float liftH = (0.6f + 1.6f * run01) * s;
        for (int sd = -1; sd <= 1; sd += 2)
        {
            int i = sd < 0 ? 0 : 1;
            float uu = Mathf.Repeat(tPhase + i * 0.5f, 1f);
            Vector3 home = tRoot + r * sd * 1.0f * s + u * (0.6f * s + tY);
            Vector3 foot;
            if (tAir) foot = home + u * 1.9f * s + f * sd * 0.35f * s;                 // legs tucked in a leap
            else if (sp < 0.2f) foot = home;
            else if (uu < duty) foot = home + mv * (stride * 0.5f - uu / duty * stride);
            else
            {
                float w = (uu - duty) / (1 - duty);
                foot = home + mv * (-stride * 0.5f + Smooth(w) * stride) + u * Mathf.Sin(w * Mathf.PI) * liftH;
            }
            if (tFootOverride == sd) foot = tFootTarget;
            if (!tAir && sp >= 0.2f && tFootOverride != sd && tPrevU[i] > 0.85f && uu < 0.15f) Footfall(foot, run01);
            tPrevU[i] = uu;
            float k = 1 - Mathf.Exp(-(tFootOverride == sd || tAir ? 18f : sp < 0.2f ? 6f : 30f) * dt);
            if (sd < 0) tp.footL = Vector3.Lerp(tp.footL, foot, k); else tp.footR = Vector3.Lerp(tp.footR, foot, k);
        }

        // the body over the legs
        float moving = Mathf.Min(1, sp / 2f);
        float bob = (0.1f + 0.3f * run01) * s * moving;
        tp.sway = u * (-bob * (0.5f + 0.5f * Mathf.Cos(4 * Mathf.PI * tPhase))) + r * (Mathf.Sin(2 * Mathf.PI * tPhase) * 0.2f * s * (1 - run01) * moving);
        tp.crouch = Mathf.MoveTowards(tp.crouch, (tAir ? 0.35f : tCrouchWant) + run01 * 0.1f, tCrouchRate * dt);
        tp.lean = Mathf.MoveTowards(tp.lean, tLeanWant + run01 * 0.35f - tStagger, tLeanRate * dt);
        float twist = tTwistHold ? tTwistWant : Mathf.Sin(2 * Mathf.PI * tPhase) * 0.16f * Mathf.Min(1, sp / 3f);
        tp.twist = Mathf.MoveTowards(tp.twist, twist, 4f * dt);
        tp.roll = Mathf.Sin(2 * Mathf.PI * tPhase) * 0.04f * Mathf.Min(1, sp / 3f);
        tp.breathe = Mathf.Sin(t * 1.3f);
        tp.scale = s;
        tStagger = Mathf.MoveTowards(tStagger, 0, dt * 1.2f);
        tp.feet = tRoot + u * tY;
        goal = tRoot;
        height = 6.3f + tY;

        // arms the attacks are not using swing with the stride, or reach up in a leap
        for (int sd = -1; sd <= 1; sd += 2)
        {
            if (tArmBusy[sd < 0 ? 0 : 1]) continue;
            Vector3 j = ShardForms.TitanShoulder(tp, sd);
            Vector3 w;
            Vector3 fing;
            float curl;
            if (tAir)
            {
                w = j + (u * 1.4f + f * 1.2f + r * sd * 1.6f) * s;
                fing = (u + f * 0.3f).normalized;
                curl = 0.6f;
            }
            else
            {
                float sw = Mathf.Cos(2 * Mathf.PI * tPhase) * (sd > 0 ? 1 : -1) * Mathf.Min(1, sp / 3f);
                Vector3 hang = j + (-u * 4.6f + f * (0.5f + sw * 1.4f) + r * sd * 0.6f) * s;
                Vector3 pump = j + (-u * 2.4f + f * (0.8f + sw * 1.8f) + r * sd * 0.4f) * s;
                w = Vector3.Lerp(hang, pump, run01);
                fing = Vector3.Slerp((-u + f * 0.25f).normalized, (f - u * 0.3f).normalized, run01);
                curl = Mathf.Lerp(0.4f, 1f, run01);
            }
            SetTitanHand(sd, w, fing, -r * sd, curl, 1 - Mathf.Exp(-12 * dt));
        }
    }

    // a foot lands: the ground shakes - and woe to anyone under it
    void Footfall(Vector3 foot, float run01)
    {
        var fx = GameManager.I.fx;
        Vector3 g = Flat(foot);
        fx.Shock(g, 3f + 3f * run01);
        fx.Puff(g + Vector3.up * 0.3f, 1.6f);
        FightCamera.Shake(0.06f + 0.14f * run01);
        SoundStage.At(AudioSynth.SubThump, g, 0.45f + 0.4f * run01, 0.8f);
        if (tStepHitCd <= 0 && hero.Height - hero.floorY < 0.5f && hero.floorY < 1f && Flat(hero.transform.position - g).magnitude < 1.8f)
        {
            tStepHitCd = 1f;
            Strike(g, 12, 3.5f, 3f, false, true, "stepped_on");
        }
    }

    // k: how far to move toward the new pose this frame (1 = snap)
    void SetTitanHand(int side, Vector3 wrist, Vector3 fingers, Vector3 palm, float curl, float k)
    {
        if (side < 0)
        {
            tp.wristL = Vector3.Lerp(tp.wristL == Vector3.zero ? wrist : tp.wristL, wrist, k);
            tp.fingersL = Vector3.Slerp(tp.fingersL == Vector3.zero ? fingers : tp.fingersL, fingers, k).normalized;
            tp.palmL = Vector3.Slerp(tp.palmL == Vector3.zero ? palm : tp.palmL, palm, k).normalized;
            tp.curlL = Mathf.Lerp(tp.curlL, curl, k);
        }
        else
        {
            tp.wristR = Vector3.Lerp(tp.wristR == Vector3.zero ? wrist : tp.wristR, wrist, k);
            tp.fingersR = Vector3.Slerp(tp.fingersR == Vector3.zero ? fingers : tp.fingersR, fingers, k).normalized;
            tp.palmR = Vector3.Slerp(tp.palmR == Vector3.zero ? palm : tp.palmR, palm, k).normalized;
            tp.curlR = Mathf.Lerp(tp.curlR, curl, k);
        }
    }

    Vector3 TitanWristNow(int side) => side < 0 ? tp.wristL : tp.wristR;

    void HoldInTitan(int side, Vector3 fingers, Vector3 palm, float hs)
    {
        Vector3 c = ShardForms.PalmCentre(TitanWristNow(side), fingers, hs) + palm * 0.42f * hs;
        hero.BossHold(c - Vector3.up * 1.05f);
    }

    void TitanRelax()
    {
        tArmBusy[0] = tArmBusy[1] = false;
        tFootOverride = 0;
        tCrouchWant = tLeanWant = 0;
        tLeanRate = 2.8f; tCrouchRate = 2.5f;
        tTwistHold = false;
    }

    // a blow from the Guardian or the Thunder Palm rocks it back
    void TitanKnock(Vector3 dir, float amount)
    {
        tStagger = Mathf.Max(tStagger, amount);
        tVel += Flat(dir).normalized * 5f * amount;
    }

    IEnumerator Shockwave(Vector3 at, float radius, float damage, float up)
    {
        bool waveHit = false;
        float nextRing = 0;
        for (float x = 0; x < 0.9f; x += Time.deltaTime)
        {
            float R = x / 0.9f * radius;
            if (!waveHit && hero.Height - hero.floorY < 0.35f && Mathf.Abs(hero.floorY - at.y) < 1f)
            {
                float d = Flat(hero.transform.position - at).magnitude;
                if (Mathf.Abs(d - R) < 0.5f)
                {
                    waveHit = true;
                    Strike(at, damage, up, 3.5f, false, true, "shockwave");
                }
            }
            if (x >= nextRing) { GameManager.I.fx.Shock(at, R * 2 + 1); nextRing += 0.13f; }
            yield return null;
        }
    }

    // the tallest roof between two points (how high a leap has to clear)
    float RoofsBetween(Vector3 a, Vector3 b)
    {
        if (TownMap.I == null) return 0;
        float h = 0;
        for (int k = 1; k < 10; k++) h = Mathf.Max(h, TownMap.I.HeightAt(Vector3.Lerp(a, b, k / 10f), 1f));
        return h;
    }

    // ================================================================== THE ASCENSION
    IEnumerator Ascension()
    {
        ascended = true;
        enraged = true;
        invulnerable = true;
        titanCracks = 0;
        powerIndex = 0;   // phase 3 opens with the dragon
        act = "ascension";
        Cleanup();
        swarm.active = swarm.Count;
        GameManager.I.hud.Rebrand(1, "MOROS ASCENDED", "THE LIVING TEMPLE", Ember);
        Announce("ENOUGH!", "", 1.3f);
        GameManager.I.fx.Chroma(0.7f);
        director?.Cut(BossDirector.Shot.Low);
        swarm.mode = ShardSwarm.Mode.Twister;
        swarm.center = Flat(pos);
        swarm.twisterHeight = 12f; swarm.twisterWidth = 0.5f;
        Play("PowerUp", 0.15f);
        {
            var hud = GameManager.I.hud;
            hud.Cinematic(true);
            Cine.Begin(false);
            TimeFX.slow = 0.5f;
            Vector3 m = Flat(pos), away = Flat(hero.transform.position - m);
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back;
            Vector3 sd = Vector3.Cross(Vector3.up, away);
            float nextShake = 0;
            yield return Cine.Shot(1.7f, m + away * 5f + sd * 2f + Vector3.up * 0.4f, m + away * 6.2f + sd * 2.6f + Vector3.up * 0.7f,
                                   m + Vector3.up * 2f, m + Vector3.up * 7f, 56, 62, x =>
            {
                goal = Flat(pos);
                height = 6.3f;
                if (x > nextShake) { FightCamera.Shake(0.15f); nextShake += 0.5f; }
            });
            TimeFX.slow = 1f;
            Cine.End();
            hud.Cinematic(false);
            for (float x = 0; x < 0.9f; x += Time.deltaTime) { goal = Flat(pos); height = 6.3f; yield return null; }
        }

        // the titan builds itself from the feet up WHILE it strides after him, the rest of its body
        // still a storm of shards; Kaito keeps full control
        TitanBegin();
        form = Form.Titan;
        swarm.chipScale = 2.6f;
        swarm.formStretch = 2.6f;
        swarm.reservoirStorm = true;
        swarm.stormCenter = tRoot;
        const float build = 6f;
        swarm.BeginForm(build, true);
        Announce("THE TITAN RISES", "RUN!", 1.6f);
        for (float x = 0; x < build + 0.6f; x += Time.deltaTime)
        {
            TitanMove(x > 1.4f ? TitanWalk : 0, HeroFlat, 7f);
            swarm.stormCenter = tRoot;
            swarm.stormBase = Mathf.Lerp(0.5f, 9f, x / build);
            if (x > build * 0.75f && model.activeSelf) model.SetActive(false);
            if (x > 2.6f && x - Time.deltaTime <= 2.6f) Shot("titan_building");
            yield return null;
        }
        swarm.reservoirStorm = false;
        model.SetActive(false);
        swarm.twisterHeight = 4.6f; swarm.twisterWidth = 0.32f;
        yield return TitanRoar();
        yield return TitanBattle();

        // the titan comes apart and drops him
        TitanRelax();
        Announce("THE TITAN CRUMBLES", "STRIKE HIM NOW!", 1.6f, Gold);
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.Burst(tRoot + Vector3.up * 5f, 4f, 1.5f);
        swarm.chipScale = 1f;
        swarm.active = 5600;
        GameManager.I.fx.Impact(tRoot, 7f, 0.5f);
        FightCamera.Shake(1f);
        pos = tRoot + Vector3.up * 3f;
        model.SetActive(true);
        invulnerable = false;
        Play("Stagger", 0.05f);
        height = 0;
        for (float x = 0; x < 0.9f; x += Time.deltaTime) yield return null;
        act = "open";
        open = true;
        windowHits = 0;
        knockedBack = false;
        Announce("HE'S OPEN!", "STRIKE NOW", 1f, Gold);
        for (float x = 0; x < 3.6f && !knockedBack; x += Time.deltaTime) { height = 0; yield return null; }
        open = false;
        if (knockedBack) yield return Stagger();
    }

    // the roar: it arches back, throws its arms wide and bellows - looking up past Kaito
    IEnumerator TitanRoar()
    {
        act = "titan_roar";
        GameManager.I.fx.Impact(tRoot, 9f, 0.38f);
        GameManager.I.fx.Chroma(1f);
        FightCamera.Shake(0.8f); FightCamera.Punch(1f);
        Announce("MOROS ASCENDED", "I AM THE TEMPLE.", 2.4f);
        Shot("titan");
        tArmBusy[0] = tArmBusy[1] = true;
        tLeanWant = -0.35f;
        var hud = GameManager.I.hud;
        hud.Cinematic(true);
        Cine.Begin(false);
        TimeFX.slow = 0.6f;
        yield return Cine.Track(1.8f, x =>
        {
            Vector3 hf = HeroFlat;
            Vector3 back = hf - tRoot;
            back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
            return hf + back * 3.2f + Vector3.Cross(Vector3.up, back) * 1.6f + Vector3.up * 0.5f;
        }, x => ShardForms.TitanHead(tp) - Vector3.up * 0.6f, 60, x =>
        {
            TitanMove(0, HeroFlat, 99f);
            Vector3 f = TitanForward, r = Right(f);
            for (int sd = -1; sd <= 1; sd += 2)
                SetTitanHand(sd, ShardForms.TitanShoulder(tp, sd) + (r * sd * 3.4f + Vector3.up * 1.6f + f * 0.6f) * TitanScale, (r * sd + Vector3.up).normalized, f, 0.2f, 1 - Mathf.Exp(-6 * Time.deltaTime));
        });
        TimeFX.slow = 1f;
        Cine.End();
        hud.Cinematic(false);
        TitanRelax();
    }

    // ================================================================== THE TITAN FIGHTS
    IEnumerator TitanBattle()
    {
        string[] seq = { "grab", "leap", "hammer", "sweep", "stomp", "grab" };
        for (int n = 0; n < 6 && !dying; n++)
        {
            yield return TitanChase(1.8f);
            float d = (HeroFlat - tRoot).magnitude;
            string pick = seq[n % seq.Length];
            if (d > 15f) pick = "leap";
            else if (hero.OnRoof) pick = n % 2 == 0 ? "grab" : "sweep";
            switch (pick)
            {
                case "grab": yield return TitanRunGrab(); break;
                case "leap": yield return TitanLeapSmash(); break;
                case "hammer": yield return TitanHammerFist(); break;
                case "sweep": yield return TitanSweep(); break;
                default: yield return TitanStompAttack(); break;
            }
        }
    }

    // it runs him down - and leaps any building in the way
    IEnumerator TitanChase(float maxT)
    {
        act = "titan_chase";
        for (float x = 0; x < maxT; x += Time.deltaTime)
        {
            float d = (HeroFlat - tRoot).magnitude;
            if (d < 6.5f) yield break;
            bool walled = tBlocked > 0.3f || (d > 12f && TownMap.I != null && TownMap.Blocked(tRoot + Vector3.up * 3f, HeroChest) && RoofsBetween(tRoot, HeroFlat) > 3f);
            if (walled)
            {
                Vector3 dir = (HeroFlat - tRoot).normalized;
                yield return TitanLeap(HeroFlat - dir * 4f, false);
                act = "titan_chase";
                continue;
            }
            TitanMove(TitanRun, HeroFlat, 5.5f);
            yield return null;
        }
    }

    // a leap: crouch, fly in an arc over whatever is in the way, land in a quake
    IEnumerator TitanLeap(Vector3 land, bool smash)
    {
        float s = TitanScale;
        land = Flat(land);
        if (TownMap.I != null) land = TownMap.I.Resolve(land, 1.6f * s, 3.2f);
        float apex = Mathf.Max(6f, RoofsBetween(tRoot, land) + 4.5f);
        tArmBusy[0] = tArmBusy[1] = true;
        tCrouchWant = 0.85f; tLeanWant = 0.35f; tCrouchRate = 4f;
        Vector3 f0 = TitanForward;
        for (float x = 0; x < 0.4f; x += Time.deltaTime)
        {
            TitanMove(0, land, 99f);
            Vector3 f = TitanForward, r = Right(f);
            for (int sd = -1; sd <= 1; sd += 2)
                SetTitanHand(sd, ShardForms.TitanShoulder(tp, sd) + (-Vector3.up * 3f - f * 2.2f + r * sd * 0.7f) * s, (-f - Vector3.up).normalized, -r * sd, 1, 1 - Mathf.Exp(-10 * Time.deltaTime));
            if (smash) ShowDisc(land, 4f);
            yield return null;
        }
        var fx = GameManager.I.fx;
        fx.Impact(tRoot, 5f, 0.5f);
        fx.Whoosh(0.3f, 1f);
        Vector3 from = tRoot;
        float dur = Mathf.Clamp(Vector3.Distance(from, land) / 11f, 0.8f, 1.5f);
        tAir = true;
        tVel = Vector3.zero;
        tCrouchWant = 0.3f; tLeanWant = 0.15f;
        if (!smash) tArmBusy[0] = tArmBusy[1] = false;   // arms fly up on their own
        Shot(smash ? "titan_leap" : "titan_jump");
        for (float x = 0; x < dur; x += Time.deltaTime)
        {
            float u = x / dur;
            tRoot = Vector3.Lerp(from, land, u);
            tY = 4 * apex * u * (1 - u);
            TitanMove(0, land, 99f);
            if (smash)
            {
                // both fists raised overhead, coming down as it falls
                Vector3 f = TitanForward, r = Right(f);
                Vector3 hi = ShardForms.TitanHead(tp) + (Vector3.up * 1.4f + f * 0.4f) * s;
                Vector3 lo = land + (f * 2f + Vector3.up * 0.9f) * s;
                for (int sd = -1; sd <= 1; sd += 2)
                    SetTitanHand(sd, Vector3.Lerp(hi + r * sd * 0.7f * s, lo + r * sd * 0.8f * s, Smooth((u - 0.55f) / 0.45f)), -Vector3.up, f, 1, 1 - Mathf.Exp(-14 * Time.deltaTime));
                ShowDisc(land, 4f);
            }
            yield return null;
        }
        tAir = false;
        tY = 0;
        tRoot = land;
        HideWarn();
        // the landing
        tCrouchWant = 0.9f; tLeanWant = smash ? 0.7f : 0.45f; tCrouchRate = 8f; tLeanRate = 8f;
        fx.Impact(land, smash ? 10f : 8f, 0.36f);
        fx.Chroma(0.8f);
        FightCamera.Shake(smash ? 1.2f : 0.8f); FightCamera.Punch(1f);
        if (Flat(hero.transform.position - land).magnitude < (smash ? 4f : 3f) && hero.floorY < 1f) Strike(land, smash ? 22 : 14, 6f, 5f, false, false, "titan_leap");
        StartCoroutine(Shockwave(land, smash ? 12f : 9f, 12, 4.5f));
        Shot(smash ? "titan_land" : "titan_landed");
        for (float x = 0; x < (smash ? 0.55f : 0.3f); x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
        TitanRelax();
        for (float x = 0; x < 0.3f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
    }

    IEnumerator TitanLeapSmash()
    {
        act = "titan_leap";
        Announce("LEAP SMASH", "GET OUT OF THE RED CIRCLE", 1.2f);
        director?.Cut(BossDirector.Shot.Low);
        Vector3 lead = hero.running ? Flat(hero.transform.forward) * 2.5f : Vector3.zero;
        yield return TitanLeap(HeroFlat + lead, true);
    }

    // it runs at him, bends down and snatches - caught, he is lifted to its face and thrown
    IEnumerator TitanRunGrab()
    {
        act = "titan_grab";
        Announce("TITAN GRAB", "DASH AWAY FROM THE HAND!", 1.2f);
        float s = TitanScale, hs = ShardForms.TitanHand * s;
        for (float x = 0; x < 2.4f && (HeroFlat - tRoot).magnitude > 6.8f; x += Time.deltaTime) { TitanMove(TitanRun, HeroFlat, 5f); yield return null; }
        int side = Vector3.Dot(HeroFlat - tRoot, Right(TitanForward)) >= 0 ? 1 : -1;
        tArmBusy[side < 0 ? 0 : 1] = true;
        bool low = HeroChest.y < 3.2f;
        tLeanWant = low ? 0.95f : 0.3f;
        tCrouchWant = low ? 0.45f : 0.05f;
        tLeanRate = 4.5f; tCrouchRate = 3.5f;
        Vector3 aim = HeroChest, fu = Vector3.down, palm = TitanForward;
        bool caught = false, evaded = false;
        for (float x = 0; x < 0.75f; x += Time.deltaTime)
        {
            TitanMove(TitanRun * 0.55f, HeroFlat, 3f);
            aim = Vector3.Lerp(aim, HeroChest, 1 - Mathf.Exp(-6 * Time.deltaTime));
            palm = Flat(aim - tRoot).normalized;
            if (palm.sqrMagnitude < 0.01f) palm = TitanForward;
            fu = (palm * 0.55f - Vector3.up * 0.8f).normalized;
            Vector3 target = aim - palm * 0.5f * hs - fu * 0.55f * hs;
            SetTitanHand(side, Vector3.Lerp(TitanWristNow(side), target, 1 - Mathf.Exp(-9 * Time.deltaTime)), fu, palm, 0, 1f);
            Vector3 pc = ShardForms.PalmCentre(TitanWristNow(side), fu, hs) + palm * 0.35f * hs;
            if ((pc - HeroChest).magnitude < 1.6f && x > 0.15f)
            {
                if (hero.state == Fighter.State.Dash) { if (!evaded) { evaded = true; hero.SwarmStrike(pos, 0, 0, 0); } }
                else if (hero.BossGrab()) { caught = true; break; }
            }
            yield return null;
        }
        if (!caught)
        {
            // the hand slams into the ground where he was
            Vector3 w0 = TitanWristNow(side);
            Vector3 crash = Flat(w0) + Vector3.up * 0.9f * s;
            for (float x = 0; x < 0.18f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); SetTitanHand(side, Vector3.Lerp(w0, crash, x / 0.18f), -Vector3.up, TitanForward, 1, 1); yield return null; }
            GameManager.I.fx.Impact(Flat(crash), 5f, 0.5f);
            FightCamera.Shake(0.5f);
            for (float x = 0; x < 0.45f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
            TitanRelax();
            yield break;
        }

        Announce("CAUGHT!", "", 0.8f, Gold);
        director?.Cut(BossDirector.Shot.Villain);
        FightCamera.Punch(1f);
        GameManager.I.fx.Crack(HeroChest, 0.5f);
        Vector3 pn = palm;
        for (float x = 0; x < 0.2f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); SetTitanHand(side, TitanWristNow(side), fu, pn, x / 0.2f, 1); HoldInTitan(side, fu, pn, hs); yield return null; }
        Shot("titan_caught");
        // it straightens and lifts him to its face, squeezing
        tLeanWant = -0.1f; tCrouchWant = 0;
        Vector3 w1 = TitanWristNow(side);
        int squeezes = 0;
        bool roared = false;
        for (float x = 0; x < 1.5f; x += Time.deltaTime)
        {
            TitanMove(0, tRoot + TitanForward * 10f, 99f);
            Vector3 face = ShardForms.TitanHead(tp) + (TitanForward * 2.0f - Vector3.up * 1.3f) * s;
            pn = Vector3.Slerp(pn, -TitanForward, Time.deltaTime * 3).normalized;
            fu = Vector3.Slerp(fu, Vector3.up, Time.deltaTime * 3).normalized;
            SetTitanHand(side, Vector3.Lerp(w1, face, Smooth(x / 0.6f)), fu, pn, 1 - 0.06f * Mathf.Abs(Mathf.Sin(t * 18)), 1);
            HoldInTitan(side, fu, pn, hs);
            if (x > 0.7f && !roared)
            {
                roared = true;
                tLeanWant = -0.3f;
                GameManager.I.fx.Impact(tRoot, 5f, 0.33f);
                FightCamera.Shake(0.6f);
                Shot("titan_roar");
            }
            if (x > 0.45f + 0.3f * squeezes && squeezes < 3)
            {
                squeezes++;
                hero.BossSqueeze(5);
                GameManager.I.fx.Crack(HeroChest, 0.45f + squeezes * 0.06f);
            }
            yield return null;
        }
        Vector3 f2 = TitanForward, r2 = Right(f2);
        if (Random.value < 0.5f)
        {
            // hurl him across the town
            tLeanWant = -0.2f;
            Vector3 back = ShardForms.TitanShoulder(tp, side) + (Vector3.up * 1.4f - f2 * 1.0f + r2 * side * 1.2f) * s;
            Vector3 w2 = TitanWristNow(side);
            for (float x = 0; x < 0.32f; x += Time.deltaTime) { TitanMove(0, tRoot + f2 * 10f, 99f); SetTitanHand(side, Vector3.Lerp(w2, back, Smooth(x / 0.32f)), fu, pn, 1, 1); HoldInTitan(side, fu, pn, hs); yield return null; }
            GameManager.I.fx.Whoosh(0.3f, 1f);
            tLeanWant = 0.5f; tLeanRate = 10f;
            Vector3 fling = ShardForms.TitanShoulder(tp, side) + (f2 * 3.8f + Vector3.up * 0.2f - r2 * side * 0.6f) * s;
            Vector3 w3 = TitanWristNow(side);
            for (float x = 0; x < 0.16f; x += Time.deltaTime) { TitanMove(0, tRoot + f2 * 10f, 99f); SetTitanHand(side, Vector3.Lerp(w3, fling, x / 0.16f), fu, pn, 1, 1); HoldInTitan(side, fu, pn, hs); yield return null; }
            hero.BossRelease(Quaternion.AngleAxis(-side * 25f, Vector3.up) * f2, 12, 6.5f, 11f);
            Shot("titan_throw");
        }
        else
        {
            // raise him overhead and smash him into the street
            Vector3 up = ShardForms.TitanHead(tp) + (Vector3.up * 1.6f + f2 * 0.6f) * s;
            Vector3 w2 = TitanWristNow(side);
            for (float x = 0; x < 0.35f; x += Time.deltaTime) { TitanMove(0, tRoot + f2 * 10f, 99f); SetTitanHand(side, Vector3.Lerp(w2, up, Smooth(x / 0.35f)), Vector3.up, f2, 1, 1); HoldInTitan(side, Vector3.up, f2, hs); yield return null; }
            GameManager.I.fx.Whoosh(0.28f, 1f);
            tLeanWant = 1.0f; tCrouchWant = 0.5f; tLeanRate = 10f; tCrouchRate = 8f;
            Vector3 slam = tRoot + (f2 * 3.4f + Vector3.up * 1.1f) * s;
            Vector3 w3 = TitanWristNow(side);
            for (float x = 0; x < 0.2f; x += Time.deltaTime) { TitanMove(0, tRoot + f2 * 10f, 99f); SetTitanHand(side, Vector3.Lerp(w3, slam, x * x / 0.04f), -Vector3.up, f2, 1, 1); HoldInTitan(side, -Vector3.up, f2, hs); yield return null; }
            hero.BossRelease(f2, 18, 1.2f, 3f);
            GameManager.I.fx.Impact(Flat(slam), 8f, 0.4f);
            FightCamera.Shake(1f);
            Shot("titan_body_slam");
        }
        FightCamera.Shake(0.6f);
        for (float x = 0; x < 0.6f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
        TitanRelax();
    }

    // both fists raised high over its head and brought down together
    IEnumerator TitanHammerFist()
    {
        act = "titan_hammer";
        Announce("HAMMER FIST", "DASH THROUGH THE SHOCKWAVE", 1.1f);
        director?.Cut(BossDirector.Shot.Side);
        float s = TitanScale;
        for (float x = 0; x < 2f && (HeroFlat - tRoot).magnitude > 6f; x += Time.deltaTime) { TitanMove(TitanRun, HeroFlat, 5f); yield return null; }
        tArmBusy[0] = tArmBusy[1] = true;
        tLeanWant = -0.3f; tCrouchWant = 0.1f;
        Vector3 impact = HeroFlat;
        for (float x = 0; x < 0.75f; x += Time.deltaTime)
        {
            TitanMove(0, HeroFlat, 99f);
            Vector3 f = TitanForward, r = Right(f);
            Vector3 top = ShardForms.TitanHead(tp) + (Vector3.up * 1.6f - f * 0.5f) * s;
            for (int sd = -1; sd <= 1; sd += 2)
                SetTitanHand(sd, top + r * sd * 0.3f * s, Vector3.up, -r * sd, 1, 1 - Mathf.Exp(-8 * Time.deltaTime));
            Vector3 toH = HeroFlat - tRoot;
            impact = tRoot + Vector3.ClampMagnitude(toH, 4.6f * s);
            ShowDisc(impact, 2.6f);
            yield return null;
        }
        GameManager.I.fx.Whoosh(0.3f, 1f);
        tLeanWant = 1.0f; tCrouchWant = 0.55f; tLeanRate = 12f; tCrouchRate = 8f;
        Vector3 wl = tp.wristL, wr = tp.wristR;
        for (float x = 0; x < 0.2f; x += Time.deltaTime)
        {
            TitanMove(0, impact, 99f);
            float e = x / 0.2f;
            Vector3 r = Right(TitanForward);
            SetTitanHand(-1, Vector3.Lerp(wl, impact + (Vector3.up * 0.9f - r * 0.5f) * s, e * e), -Vector3.up, TitanForward, 1, 1);
            SetTitanHand(1, Vector3.Lerp(wr, impact + (Vector3.up * 0.9f + r * 0.5f) * s, e * e), -Vector3.up, TitanForward, 1, 1);
            yield return null;
        }
        HideWarn();
        GameManager.I.fx.Impact(impact, 9f, 0.4f);
        GameManager.I.fx.Chroma(1f);
        FightCamera.Shake(1.1f); FightCamera.Punch(1.2f);
        if (Flat(hero.transform.position - impact).magnitude < 2.6f && hero.floorY < 1f) Strike(impact, 20, 6f, 3f, false, false, "titan_hammer");
        StartCoroutine(Shockwave(impact, 10f, 12, 4f));
        Shot("titan_hammer");
        for (float x = 0; x < 0.5f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
        TitanRelax();
        for (float x = 0; x < 0.3f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
    }

    // it drops low and sweeps an arm across the ground in a wide arc - jump it or dash through
    IEnumerator TitanSweep()
    {
        act = "titan_sweep";
        Announce("TITAN SWEEP", "JUMP (SPACE) OR DASH!", 1.1f);
        float s = TitanScale;
        for (float x = 0; x < 2f && (HeroFlat - tRoot).magnitude > 5.5f; x += Time.deltaTime) { TitanMove(TitanRun, HeroFlat, 4.5f); yield return null; }
        int side = 1;
        tArmBusy[1] = true;
        bool high = hero.OnRoof;
        float armY = high ? hero.floorY + 1.1f : 1.0f * s;
        tCrouchWant = high ? 0.1f : 0.75f; tLeanWant = high ? 0.2f : 0.55f;
        tTwistHold = true; tTwistWant = side * 0.6f;
        for (float x = 0; x < 0.55f; x += Time.deltaTime)
        {
            TitanMove(0, HeroFlat, 99f);
            Vector3 f = TitanForward, r = Right(f);
            SetTitanHand(side, tRoot + (r * side * 4.4f - f * 1.4f) * s + Vector3.up * armY, (r * side).normalized, -f, 0.2f, 1 - Mathf.Exp(-9 * Time.deltaTime));
            ShowDisc(tRoot, 5.6f);
            yield return null;
        }
        GameManager.I.fx.Whoosh(0.35f, 1f);
        Vector3 f0 = TitanForward;
        bool hit = false;
        for (float x = 0; x < 0.45f; x += Time.deltaTime)
        {
            float e = Smooth(x / 0.45f);
            float ang = Mathf.Lerp(side * 110f, -side * 85f, e);
            Vector3 d = Quaternion.AngleAxis(ang, Vector3.up) * f0;
            Vector3 w = tRoot + d * 5.0f * s + Vector3.up * armY;
            tTwistWant = -ang * Mathf.Deg2Rad * 0.45f;
            TitanMove(0, tRoot + f0 * 10f, 99f);
            SetTitanHand(side, w, d, Vector3.Cross(Vector3.up, d) * -side, 0.2f, 1);
            Vector3 toH = Flat(hero.transform.position - tRoot);
            float hAng = Vector3.SignedAngle(f0, toH, Vector3.up);
            bool inArc = side > 0 ? hAng <= ang + 10 && hAng >= ang - 25 : hAng >= ang - 10 && hAng <= ang + 25;
            if (!hit && toH.magnitude < 6.2f * s && inArc && Mathf.Abs(hero.Height - armY + 0.5f) < 1.4f)
            {
                hit = true;
                Strike(w, 14, 4.5f, 7f, false, true, "titan_sweep");
            }
            if (Random.value < 0.4f) GameManager.I.fx.Puff(Flat(w) + Vector3.up * 0.3f, 1.2f);
            yield return null;
        }
        HideWarn();
        Shot("titan_sweep");
        for (float x = 0; x < 0.45f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
        TitanRelax();
    }

    // a knee raised high, then the foot comes down on him
    IEnumerator TitanStompAttack()
    {
        act = "titan_stomp";
        Announce("TITAN STOMP", "DASH THROUGH THE SHOCKWAVE", 1.0f);
        director?.Cut(BossDirector.Shot.Low);
        float s = TitanScale;
        for (float x = 0; x < 2f && (HeroFlat - tRoot).magnitude > 4.5f; x += Time.deltaTime) { TitanMove(TitanRun, HeroFlat, 3.8f); yield return null; }
        int side = Vector3.Dot(HeroFlat - tRoot, Right(TitanForward)) >= 0 ? 1 : -1;
        Vector3 start = side < 0 ? tp.footL : tp.footR;
        tFootOverride = side;
        tLeanWant = -0.12f;
        Vector3 at = HeroFlat;
        for (float x = 0; x < 0.6f; x += Time.deltaTime)
        {
            TitanMove(0, HeroFlat, 99f);
            Vector3 hipG = tRoot + Right(TitanForward) * side * 1.0f * s;
            at = hipG + Vector3.ClampMagnitude(HeroFlat - hipG, 3.6f * s);
            tFootTarget = Vector3.Lerp(start, Flat(start) + (Vector3.up * 3.4f + TitanForward * 0.9f) * s, Smooth(x / 0.6f));
            ShowDisc(at, 2.6f);
            yield return null;
        }
        GameManager.I.fx.Whoosh(0.3f, 1f);
        tLeanWant = 0.3f; tLeanRate = 8f;
        Vector3 up = tFootTarget;
        for (float x = 0; x < 0.14f; x += Time.deltaTime)
        {
            TitanMove(0, HeroFlat, 99f);
            float e = x / 0.14f;
            tFootTarget = Vector3.Lerp(up, at + Vector3.up * 0.6f * s, e * e);
            yield return null;
        }
        HideWarn();
        GameManager.I.fx.Impact(at, 9.5f, 0.36f);
        GameManager.I.fx.Chroma(1f);
        FightCamera.Shake(1.2f); FightCamera.Punch(1.2f);
        if (Flat(hero.transform.position - at).magnitude < 2.6f && hero.floorY < 1f) Strike(at, 20, 6f, 3f, false, false, "titan_stomp");
        StartCoroutine(Shockwave(at, 12f, 12, 4.5f));
        Shot("titan_stomp");
        for (float x = 0; x < 0.4f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
        TitanRelax();
        for (float x = 0; x < 0.3f; x += Time.deltaTime) { TitanMove(0, HeroFlat, 99f); yield return null; }
    }
}
