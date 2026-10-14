using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// MOROS, part six: THE KING COBRA - a forty-metre stone cobra (CobraBody / CobraMesh draw it).
//   FORMATION  every stone in the town whirls up into a storm, pours into a coil on the street and
//              builds the snake from the tip of its tail to its head - where the stones land they
//              fuse into its scales. It rises out of its coils, hood spreading, tongue flicking,
//              and a mandala of stone wheels up behind its hood.
//   THE HUNT   it chases Kaito through the whole town: the streets are mapped into a distance field
//              so it winds around the houses instead of getting stuck, its body following the exact
//              path its neck took, in S-curves. Far away it races with its head low and hood
//              folded; close in it rears, spreads its hood, sways and circles him - then strikes.
//              Its coils shove him aside; it snaps at him as it passes.
//   SWALLOWED  caught, he is gulped down (a lump slides down its neck), brought back up and spat
//              high into the sky. (Dash through the strike, or punch its head as it lunges.)
//   THE END    its skin cracks back into thousands of stones that blow apart.
public partial class MonkBoss
{
    CobraBody cobra;
    readonly Vector3[] cobTrail = new Vector3[3000];   // the ground path the neck base has taken
    readonly List<Vector3> cobPath = new List<Vector3>(160);
    int cobTrailHead, cobCount;
    Vector3 cobB;                                      // where the neck leaves the ground
    Vector3 cobDir = Vector3.forward;                  // heading along the ground
    Vector3 cobHeadPos, cobHeadFwd = Vector3.forward;  // the back of the skull, and where it looks
    float cobHood, cobGape, cobTongue, cobBulge = -1, cobRear, cobReach = 2.4f, cobPhase, cobHalo, cobGlow = 1;
    float cobCircle = 1, cobSway = 1;
    bool cobCaught, cobOn;
    const float CobraScale = 2.2f, CobraLen = 42f, CobStep = 0.2f;

    Vector3 CobraHeadCentre => cobHeadPos + cobHeadFwd * (0.5f * CobraMesh.HeadLen * CobraScale);
    System.Func<Vector3, bool> cobraBlocks;

    // is p inside (or right against) the snake? keeps the camera out of its coils
    bool CobraNear(Vector3 p)
    {
        if (!cobOn || cobra == null) return false;
        var c = cobra.shape;
        if (c.spine.Count < 2) return false;
        if ((p - CobraHeadCentre).sqrMagnitude < 3.6f * 3.6f) return true;
        for (float a = 0; a < c.length; a += 2f)
        {
            float r = c.Radius(a) + 1.8f;
            if ((p - c.Point(a)).sqrMagnitude < r * r) return true;
        }
        return false;
    }

    // Moros floats above and behind the spread hood, like a snake charmer riding his naga
    void CobraRide()
    {
        goal = cobB - cobDir * 3.5f;
        height = Mathf.Max(4f, cobRear + 3.4f);
    }

    // a staged shot of the snake: dist in front of its face, side to its right, up above the focus
    void CobraCam(float dist, float side, float up, float lookUp, float fov, float along = 0.55f)
    {
        if (director == null) return;
        Vector3 f = Flat(cobHeadFwd).sqrMagnitude > 0.01f ? Flat(cobHeadFwd).normalized : cobDir;
        Vector3 focus = Vector3.Lerp(cobB, cobHeadPos, along);
        Vector3 cp = focus + f * dist + Right(f) * side + Vector3.up * up;
        cp.y = Mathf.Max(0.7f, cp.y);
        director.Override(cp, focus + Vector3.up * lookUp, fov, 0.25f);
    }
    Vector3 CobraMouth => cobHeadPos + cobHeadFwd * (0.62f * CobraMesh.HeadLen * CobraScale);

    // ================================================================== the trail
    Vector3 CobTrailAt(float idx)
    {
        idx = Mathf.Clamp(idx, 0, cobCount - 1.001f);
        int a = Mathf.FloorToInt(idx);
        float f = idx - a;
        int ia = (cobTrailHead - a + cobTrail.Length) % cobTrail.Length;
        int ib = (ia - 1 + cobTrail.Length) % cobTrail.Length;
        return Vector3.Lerp(cobTrail[ia], cobTrail[ib], f);
    }

    void CobPush(Vector3 p)
    {
        cobTrailHead = (cobTrailHead + 1) % cobTrail.Length;
        cobTrail[cobTrailHead] = p;
        cobCount = Mathf.Min(cobCount + 1, cobTrail.Length);
    }

    // slide the neck base to 'to', laying trail every CobStep so the body follows the same path
    void CobMove(Vector3 to)
    {
        to.y = 0;
        cobB = to;
        Vector3 from = cobTrail[cobTrailHead];
        float d = Vector3.Distance(from, to);
        int steps = Mathf.Min(80, Mathf.FloorToInt(d / CobStep));
        for (int k = 1; k <= steps; k++) CobPush(Vector3.Lerp(from, to, k * CobStep / d));
    }

    // a coil of stone on the ground: an inward spiral (the tail outside) ending at c, heading along dir
    void CobraCoil(Vector3 c, Vector3 dir)
    {
        cobCount = 0;
        const float rEnd = 1.7f, rOut = 7.2f, tmax = Mathf.PI * 2f * 2.2f;
        float dr = (rOut - rEnd) / tmax;
        Vector2 endT = new Vector2(-dr, -rEnd).normalized;
        float rot = Mathf.Atan2(dir.z, dir.x) - Mathf.Atan2(endT.y, endT.x);
        float cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
        for (float th = tmax; th > 0;)
        {
            float r = rEnd + dr * th;
            float px = r * Mathf.Cos(th) - rEnd, py = r * Mathf.Sin(th);
            CobPush(c + new Vector3(px * cr - py * sr, 0, px * sr + py * cr));
            th -= CobStep / r;
        }
        CobPush(c);
        cobB = c;
    }

    // ================================================================== the streets (distance field)
    bool[] navOpen;
    int[] navDist, navQ;
    int navW, navH, navGoal = -1;
    float navAt = -9;
    Vector2 navMin;
    const float NavCell = 1f;

    void NavBuild()
    {
        if (navOpen != null) return;
        var town = TownMap.I;
        navMin = town != null ? town.min : new Vector2(-60, -60);
        Vector2 max = town != null ? town.max : new Vector2(60, 60);
        navW = Mathf.CeilToInt((max.x - navMin.x) / NavCell);
        navH = Mathf.CeilToInt((max.y - navMin.y) / NavCell);
        int n = navW * navH;
        navOpen = new bool[n];
        navDist = new int[n];
        navQ = new int[n];
        for (int y = 0; y < navH; y++)
            for (int x = 0; x < navW; x++)
            {
                var p = new Vector3(navMin.x + (x + 0.5f) * NavCell, 0.5f, navMin.y + (y + 0.5f) * NavCell);
                navOpen[y * navW + x] = town == null || (TownMap.Walkable(p) && !town.InsideSolid(p, 1.0f));
            }
    }

    int NavNearestOpen(Vector3 p)
    {
        int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - navMin.x) / NavCell), 0, navW - 1);
        int cy = Mathf.Clamp(Mathf.FloorToInt((p.z - navMin.y) / NavCell), 0, navH - 1);
        int c0 = cy * navW + cx;
        if (navOpen[c0]) return c0;
        for (int r = 1; r < 30; r++)
        {
            int best = -1, bd = int.MaxValue;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= navW || y >= navH) continue;
                    int c = y * navW + x;
                    if (!navOpen[c] || dx * dx + dy * dy >= bd) continue;
                    bd = dx * dx + dy * dy;
                    best = c;
                }
            if (best >= 0) return best;
        }
        return -1;
    }

    Vector3 NavCentre(int c) => new Vector3(navMin.x + (c % navW + 0.5f) * NavCell, 0, navMin.y + (c / navW + 0.5f) * NavCell);

    // steps from every open cell to the goal, refreshed a few times a second
    void NavField(Vector3 goal)
    {
        int g = NavNearestOpen(goal);
        if (g < 0 || (g == navGoal && Time.time - navAt < 0.25f)) return;
        navGoal = g;
        navAt = Time.time;
        for (int i = 0; i < navDist.Length; i++) navDist[i] = int.MaxValue;
        int head = 0, tail = 0;
        navDist[g] = 0;
        navQ[tail++] = g;
        while (head < tail)
        {
            int c = navQ[head++];
            int cx = c % navW, cy = c / navW, d = navDist[c] + 1;
            for (int k = 0; k < 4; k++)
            {
                int x = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), y = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (x < 0 || y < 0 || x >= navW || y >= navH) continue;
                int nb = y * navW + x;
                if (!navOpen[nb] || navDist[nb] <= d) continue;
                navDist[nb] = d;
                navQ[tail++] = nb;
            }
        }
    }

    // which way to slither from p toward the goal: straight at it when in plain sight, otherwise
    // down the distance field a few cells ahead (smoothly around the corners)
    Vector3 NavDir(Vector3 p, Vector3 goal)
    {
        Vector3 straight = Flat(goal - p);
        straight = straight.sqrMagnitude > 1e-4f ? straight.normalized : cobDir;
        if (navOpen == null || navGoal < 0 || !TownMap.Blocked(p + Vector3.up * 0.8f, Flat(goal) + Vector3.up * 0.8f)) return straight;
        int cur = NavNearestOpen(p);
        if (cur < 0 || navDist[cur] == int.MaxValue) return straight;
        for (int step = 0; step < 7; step++)
        {
            int cx = cur % navW, cy = cur / navW, best = cur, bd = navDist[cur];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= navW || y >= navH) continue;
                    int nb = y * navW + x;
                    if (navOpen[nb] && navDist[nb] < bd) { bd = navDist[nb]; best = nb; }
                }
            if (best == cur) break;
            cur = best;
        }
        Vector3 d = Flat(NavCentre(cur) - p);
        return d.sqrMagnitude > 0.04f ? d.normalized : straight;
    }

    // ================================================================== pose -> mesh
    void CobraOn()
    {
        if (cobra == null) cobra = CobraBody.Create();
        cobraBlocks ??= CobraNear;
        cobra.gameObject.SetActive(true);
        cobra.Visible = true;
        cobOn = true;
    }

    void CobraOff()
    {
        cobOn = false;
        if (cobra != null) cobra.gameObject.SetActive(false);
        swarm.formSink = false;
        if (hero != null) hero.Hidden = false;
        cobBulge = -1;
    }

    // the head: held out in front of the neck base, high when reared, swaying; it watches Kaito
    void CobraHeadPose(float track, float follow = 10f)
    {
        float dt = Time.deltaTime;
        Vector3 side = Right(cobDir);
        float sway = Mathf.Sin(t * 1.8f) * 0.5f * Mathf.Clamp01(cobRear / 4f) * cobSway;
        float lift = cobra.shape.Belly(cobRear * 1.1f + cobReach * 0.5f) + 0.28f * CobraScale;
        Vector3 want = cobB + cobDir * cobReach + Vector3.up * (lift + cobRear) + side * sway;
        cobHeadPos = Vector3.Lerp(cobHeadPos, want, 1 - Mathf.Exp(-follow * dt));
        Vector3 look = HeroChest - (cobHeadPos + cobHeadFwd * 1.2f);
        Vector3 fl = Flat(look);
        if (fl.sqrMagnitude < 0.01f) fl = cobDir;
        float pitch = Mathf.Clamp(Mathf.Atan2(look.y, fl.magnitude), -0.65f, 0.45f);
        Vector3 f = fl.normalized * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
        cobHeadFwd = Vector3.Slerp(cobHeadFwd, f, 1 - Mathf.Exp(-track * dt)).normalized;
    }

    // called from LateUpdate: rebuild the snake's flesh from the pose the coroutines set this frame
    void CobraRefresh()
    {
        if (!cobOn || cobra == null) return;
        var c = cobra.shape;
        c.s = CobraScale;
        c.bodyLength = CobraLen;
        c.hood = cobHood;
        c.gape = cobGape;
        c.tongue = cobTongue;
        c.flick = t;
        c.bulge = cobBulge;
        c.bulgeSize = 1.35f;
        c.headFwd = cobHeadFwd;
        c.headUp = Vector3.up;
        Vector3 back = -Flat(cobHeadFwd);
        c.back = back.sqrMagnitude > 0.01f ? back.normalized : -cobDir;
        cobPath.Clear();
        Vector3 B = cobB;
        B.y = c.Belly(cobRear * 1.1f + cobReach * 0.5f);
        cobPath.Add(B);
        for (int k = 1; k < 150; k++) cobPath.Add(CobTrailAt(k * 0.35f / CobStep));
        c.BuildSpine(cobHeadPos, cobHeadFwd, Vector3.up, B, cobPath, cobPath.Count);
        cobra.glow = cobGlow;
        cobra.Apply();
        if (c.headShown) { eyeL = c.eyeL; eyeR = c.eyeR; }
    }

    // the stones inside it: the body (tail first, so the build runs tail to head), the hood, the head,
    // and the stone mandala behind the hood
    void CobraParts(List<ShardPart> L)
    {
        if (cobra == null) return;
        var c = cobra.shape;
        if (c.spine.Count < 2) return;
        float len = c.length;
        const int segs = 36;
        for (int k = 0; k < segs; k++)
        {
            float a1 = len * (1 - k / (float)segs), a2 = Mathf.Max(0, len * (1 - (k + 1) / (float)segs));
            L.Add(new ShardPart
            {
                type = 1, a = c.Point(a1), b = c.Point(a2), r = c.Radius((a1 + a2) * 0.5f) * 0.95f,
                shell = true, weight = a1 - a2, tint = ShardForms.Natural, sink = 1,
            });
        }
        for (int k = 0; k < 5; k++)
        {
            float a = Mathf.Lerp(c.HoodEnd, c.HoodStart, (k + 0.5f) / 5f);
            c.FrameAt(a, out var p, out var tt, out var d, out _);
            L.Add(new ShardPart
            {
                type = 0, a = p, b = new Vector3(c.HoodHalfWidth(a), 0.1f * CobraScale, (c.HoodEnd - c.HoodStart) * 0.1f),
                rot = Quaternion.LookRotation(tt, d), shell = true, weight = 1.2f, tint = ShardForms.Natural, sink = 1,
            });
        }
        L.Add(new ShardPart
        {
            type = 0, a = CobraHeadCentre, b = new Vector3(0.28f, 0.2f, 0.55f) * CobraScale,
            rot = Quaternion.LookRotation(cobHeadFwd, Vector3.up), shell = true, weight = 3f, tint = ShardForms.Natural, sink = 1,
        });
        // the mandala: two wheels of stone and embers with spokes, turning behind the spread hood
        float wa = c.HoodStart + 0.34f * (c.HoodEnd - c.HoodStart);
        c.FrameAt(wa, out var hp, out var ht, out var hd, out _);
        Vector3 up2 = Vector3.ProjectOnPlane(Vector3.up, hd);
        up2 = up2.sqrMagnitude > 0.01f ? up2.normalized : ht;
        Vector3 hc = hp + hd * 1.4f + up2 * 0.6f;
        var q = Quaternion.AngleAxis(t * 16f, hd) * Quaternion.LookRotation(up2, hd);
        float shrink = 1 - Mathf.Clamp01(cobHalo);
        L.Add(new ShardPart { type = 3, a = hc, b = new Vector3(4.6f, 0.17f, 0), rot = q, shell = true, weight = 5f, tint = ShardForms.Stone, shrink = shrink });
        L.Add(new ShardPart { type = 3, a = hc, b = new Vector3(3.6f, 0.11f, 0), rot = q, shell = true, weight = 3f, tint = ShardForms.Ember, shrink = shrink });
        for (int k = 0; k < 12; k++)
        {
            Vector3 dir = q * (Quaternion.Euler(0, k * 30f, 0) * Vector3.forward);
            L.Add(new ShardPart { type = 1, a = hc + dir * 1.5f, b = hc + dir * 4.5f, r = 0.08f, shell = true, weight = 0.35f, tint = ShardForms.Stone, shrink = shrink });
        }
    }

    void FrameCobra(ref Bounds b)
    {
        if (cobra == null || !cobOn) return;
        b.Encapsulate(cobHeadPos + Vector3.up * 1.6f);
        b.Encapsulate(CobraMouth);
        b.Encapsulate(cobB);
        if (cobra.shape.spine.Count > 1) b.Encapsulate(cobra.shape.Point(12f));
    }

    // its coils shove Kaito aside; it snaps at him as it races past
    float cobBiteCd;
    void CobraContact()
    {
        var c = cobra.shape;
        if (c.spine.Count < 2 || hero.bossHeld || hero.floorY > 1.2f) return;
        float dt = Time.deltaTime;
        Vector3 hp = hero.transform.position;
        for (float a = 3f; a < c.length - 1f; a += 1f)
        {
            Vector3 p = c.Point(a);
            float r = c.Radius(a) + 0.45f;
            Vector3 d = Flat(hp - p);
            float m = d.magnitude;
            if (m >= r || hp.y > p.y + r + 0.6f) continue;
            Vector3 n = m > 0.01f ? d / m : Right(cobDir);
            hero.WindForce(n * ((r - m) / Mathf.Max(dt, 1e-3f)), dt);
            hp += n * (r - m);
        }
        cobBiteCd -= dt;
        if (cobBiteCd <= 0 && (CobraMouth - HeroChest).magnitude < 1.9f && hero.state != Fighter.State.Dash)
        {
            cobBiteCd = 1.4f;
            cobGape = 45f;
            Strike(CobraMouth, 9, 3f, 7f, false, true, "cobra_bite");
            GameManager.I.fx.Crack(CobraMouth, 0.9f);
        }
    }

    // ================================================================== THE KING COBRA
    IEnumerator CobraStrike()
    {
        act = "cobra";
        camLift = 3f;
        NavBuild();
        CobraOn();
        AllShardsIn(1.15f);
        Announce("THE KING COBRA", "IT HUNTS YOU THROUGH THE STREETS  -  DASH ITS STRIKES  -  PUNCH ITS HEAD", 2f);
        Play("PowerUp", 0.15f);
        director?.Cut(BossDirector.Shot.Side);
        var fx = GameManager.I.fx;
        // the coil: on open street between Moros and Kaito
        Vector3 toH = HeroFlat - Flat(pos);
        float d0 = toH.magnitude;
        Vector3 f = d0 > 0.1f ? toH / d0 : ToHero();
        Vector3 C = Flat(pos) + f * Mathf.Min(9f, d0 * 0.5f);
        if (TownMap.I != null) C = TownMap.I.Resolve(C, 3.5f, 0);
        C.y = 0;
        CobraCoil(C, f);
        cobDir = f;
        cobHeadFwd = f;
        cobRear = 0; cobReach = 1.3f; cobHood = 0.15f; cobGape = 4; cobTongue = 0; cobBulge = -1; cobHalo = 0; cobGlow = 1.6f;
        cobCaught = false;
        cobHeadPos = C + f * cobReach + Vector3.up * 1f;
        cobra.shape.reveal = 0;
        CobraRefresh();
        form = Form.Cobra;
        swarm.formSink = true;
        swarm.reservoirStorm = true;
        swarm.stormCenter = C;
        swarm.stormBase = 0.2f;
        const float build = 3.0f;
        swarm.BeginForm(build, true);   // tail first, the head last, the mandala after it

        // ---------------------------------------------------------------- the stones build it
        float rumble = 0, emberT = 0;
        for (float x = 0; x < build + 0.9f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            cobra.shape.reveal = Mathf.Clamp01((x - 0.6f) / (0.72f * build));
            CobraHeadPose(2f, 6f);
            goal = C; height = 6.5f;
            if (director != null)
            {
                float oa = Mathf.Atan2(f.x, f.z) + 0.9f + x * 0.22f;
                Vector3 cp = C + new Vector3(Mathf.Sin(oa), 0, Mathf.Cos(oa)) * 17f + Vector3.up * 7.5f;
                director.Override(cp, C + Vector3.up * 1.8f, 55f, 0.25f);
            }
            rumble -= dt; emberT -= dt;
            if (rumble <= 0) { rumble = 0.3f; FightCamera.Shake(0.12f); SoundStage.At(AudioSynth.Rumble, C, 0.55f, 0.9f); }
            if (emberT <= 0 && cobra.shape.reveal < 1)
            {
                // the growth front burns where the stones fuse
                emberT = 0.04f;
                Vector3 fp = cobra.shape.Point(cobra.shape.frontArc);
                fx.Embers(fp + Random.insideUnitSphere * 0.6f, Vector3.up * 2f + Random.insideUnitSphere * 2f, 3, 0.16f);
                fx.Puff(Flat(fp) + Vector3.up * 0.2f + Random.insideUnitSphere * 0.8f, 1.4f);
            }
            if (x > 1.6f && x - dt <= 1.6f) Shot("cobra_build");
            yield return null;
        }
        swarm.reservoirStorm = false;

        // ---------------------------------------------------------------- it rises out of its coils
        SoundStage.At(AudioSynth.Rocket, cobHeadPos, 0.9f, 0.5f);   // the hiss
        director?.Cut(BossDirector.Shot.Low);
        for (float x = 0; x < 1.8f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime, e = Smooth(x / 1.4f);
            cobRear = 5.6f * e;
            cobReach = Mathf.Lerp(1.3f, 2.4f, e);
            cobHood = Mathf.Lerp(0.15f, 1.15f, Smooth((x - 0.5f) / 1.0f));
            cobHalo = Smooth((x - 0.8f) / 1.0f);
            cobGape = x > 1.1f && x < 1.6f ? 30f : Mathf.Lerp(cobGape, 6f, dt * 4);
            cobTongue = x > 0.7f && Mathf.Sin(t * 9f) > 0.1f ? 1 : 0;
            cobGlow = Mathf.Lerp(1.6f, 1.0f, e);
            CobraHeadPose(3f, 8f);
            CobraRide();
            CobraCam(Mathf.Lerp(15f, 12f, e), 6.5f, -1.2f, 1.2f, 52f);
            if (x > 1.25f && x - dt <= 1.25f) { Shot("cobra_rise"); FightCamera.Shake(0.4f); }
            yield return null;
        }

        // ---------------------------------------------------------------- the hunt, all over town
        int strikes = 0;
        float strikeCd = 4.5f, dustT = 0, flickT = 0, hissT = 2f;   // it hunts and circles before the first strike
        rumble = 0;
        // test runs: Kaito flees across town for a while, so the chase gets exercised
        Brain cobSaved = null;
        if (AutoCapture.Active && !Showcase.Active)
        {
            cobSaved = hero.brain;
            hero.brain = hero.gameObject.GetComponent<FleeBrain>() ?? hero.gameObject.AddComponent<FleeBrain>();
        }
        for (float x = 0; x < 20f && strikes < 4 && !cobCaught && !broken && !dying; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            strikeCd -= dt; dustT -= dt; flickT -= dt; hissT -= dt; rumble -= dt;
            if (cobSaved != null && x > 9f) { hero.brain = cobSaved; cobSaved = null; }
            Vector3 hero0 = HeroFlat;
            float dist = Flat(hero0 - cobB).magnitude;
            bool far = dist > 17f;
            // far: race after him; near: circle him until it is ready to strike
            Vector3 aim = hero0;
            if (!far && strikeCd > 0)
            {
                Vector3 rel = Flat(cobB - hero0);
                if (rel.sqrMagnitude < 0.01f) rel = -cobDir;
                aim = hero0 + Quaternion.AngleAxis(38f * cobCircle, Vector3.up) * rel.normalized * 9f;
            }
            NavField(aim);
            Vector3 nav = NavDir(cobB, aim);
            float speed = far ? 13.5f : strikeCd > 0 ? 7f : 5f;
            cobPhase += speed * dt * (2 * Mathf.PI / 10f);
            Vector3 want = Quaternion.AngleAxis(Mathf.Sin(cobPhase) * (far ? 34f : 26f), Vector3.up) * nav;
            cobDir = Vector3.RotateTowards(cobDir, want, (far ? 2.6f : 3.2f) * dt, 0);
            cobDir = Flat(cobDir).normalized;
            Vector3 next = cobB + cobDir * speed * dt;
            if (TownMap.I != null) next = TownMap.I.Resolve(next, 0.9f, 0);
            if (Flat(hero0 - next).magnitude > 3.4f) CobMove(next);   // never onto him
            cobRear = Mathf.MoveTowards(cobRear, far ? 1.7f : hero.OnRoof ? 7.6f : 5.6f, (far ? 5f : 3.5f) * dt);
            cobReach = Mathf.MoveTowards(cobReach, far ? 3.4f : 2.4f, 3f * dt);
            cobHood = Mathf.MoveTowards(cobHood, far ? 0.25f : 1.1f, 1.6f * dt);
            cobHalo = Mathf.MoveTowards(cobHalo, far ? 0f : 1f, 1.2f * dt);
            cobGape = Mathf.Lerp(cobGape, far ? 4f : 9f, dt * 3f);
            cobSway = far ? 0.3f : 1f;
            if (flickT <= 0) flickT = Random.Range(0.9f, 1.8f);
            cobTongue = flickT > 0.55f ? 0 : Mathf.Clamp01(Mathf.Sin(flickT / 0.55f * Mathf.PI) * 1.4f);
            CobraHeadPose(far ? 4f : 3f);
            CobraContact();
            if (dustT <= 0)
            {
                dustT = far ? 0.03f : 0.07f;
                var c = cobra.shape;
                Vector3 p = c.Point(Random.Range(6f, c.length - 3f));
                fx.Puff(Flat(p) + Vector3.up * 0.25f + Random.insideUnitSphere * 0.7f, far ? 1.9f : 1.3f);
            }
            if (rumble <= 0) { rumble = far ? 0.22f : 0.4f; SoundStage.At(AudioSynth.Rumble, cobB, far ? 0.5f : 0.3f, 1.2f); if (far) FightCamera.Shake(0.06f); }
            if (hissT <= 0 && !far) { hissT = Random.Range(2.5f, 4f); SoundStage.At(AudioSynth.Rocket, CobraMouth, 0.6f, 0.45f); }
            CobraRide();
            if (x > 1.2f && x - dt <= 1.2f) Shot("cobra_hunt");
            if (x > 6f && x - dt <= 6f) Shot("cobra_chase");
            if (x > 11f && x - dt <= 11f) Shot("cobra_circle");
            if (strikeCd <= 0 && !far && (HeroChest - CobraMouth).magnitude < 10.5f)
            {
                yield return CobraLunge();
                strikes++;
                strikeCd = 3f;
                cobCircle = -cobCircle;
            }
            yield return null;
        }
        if (cobSaved != null) hero.brain = cobSaved;
        if (broken)
        {
            CobraShatter(CobraHeadCentre);
            yield return Broken("COBRA BROKEN!");
            AllShardsOut();
            yield break;
        }
        if (cobCaught) yield return CobraSwallow();
        yield return CobraCollapse();
    }

    // rear back, hood flared, and strike - the mouth drives at his chest
    IEnumerator CobraLunge()
    {
        var fx = GameManager.I.fx;
        float s = CobraScale;
        for (float x = 0; x < 0.42f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            cobReach = Mathf.MoveTowards(cobReach, 1.1f, 6f * dt);
            cobRear = Mathf.MoveTowards(cobRear, hero.OnRoof ? 8.6f : 6.6f, 5f * dt);
            cobHood = Mathf.MoveTowards(cobHood, 1.3f, 3f * dt);
            cobGape = Mathf.Lerp(cobGape, 38f, 1 - Mathf.Exp(-8 * dt));
            cobTongue = 0;
            cobGlow = Mathf.MoveTowards(cobGlow, 2.4f, 5f * dt);
            CobraHeadPose(8f);
            CobraRide();
            yield return null;
        }
        Vector3 from = cobHeadPos;
        Vector3 aimDir = (HeroChest - (from + cobHeadFwd * 1.4f)).normalized;
        Vector3 to = HeroChest + aimDir * 1.2f - aimDir * (0.62f * CobraMesh.HeadLen * s);
        Vector3 reach = to - Flat(cobB);
        if (reach.magnitude > 13f) to = Flat(cobB) + reach.normalized * 13f;
        bool evaded = false;
        breakable = true;
        breakRadius = 2.0f;
        fx.Whoosh(0.4f, 1f);
        SoundStage.At(AudioSynth.Rocket, from, 1f, 0.7f);
        Vector3 mouth = CobraMouth;
        for (float x = 0; x < 0.26f; x += Time.deltaTime)
        {
            cobHeadPos = Vector3.Lerp(from, to, Smooth(x / 0.26f));
            cobHeadFwd = Vector3.Slerp(cobHeadFwd, aimDir, 0.5f).normalized;
            cobGape = 64f;
            mouth = CobraMouth;
            breakPoint = mouth;
            if (broken) break;
            if ((mouth - HeroChest).magnitude < 2.3f)
            {
                if (hero.state == Fighter.State.Dash) { if (!evaded) { evaded = true; hero.SwarmStrike(mouth, 0, 0, 0); } }
                else if (hero.BossGrab()) { cobCaught = true; break; }
            }
            yield return null;
        }
        breakable = false;
        cobGlow = 1.3f;
        if (cobCaught || broken) yield break;
        // missed: its jaws slam into the street
        Vector3 slam = Flat(mouth);
        fx.Impact(slam, 7f, 0.5f);
        fx.Crack(slam, 0.7f);
        for (int k = 0; k < 6; k++) fx.Puff(slam + Random.insideUnitSphere * 1.5f + Vector3.up * 0.3f, 2f);
        FightCamera.Shake(0.6f);
        if (!evaded && Flat(hero.transform.position - slam).magnitude < 3f && hero.floorY < 1f) Strike(slam, 12, 4f, 5f, false, true, "cobra");
        Shot("cobra_strike");
        for (float x = 0; x < 0.55f; x += Time.deltaTime)
        {
            cobGape = Mathf.Lerp(cobGape, 10f, Time.deltaTime * 6f);
            cobReach = Mathf.MoveTowards(cobReach, 2.4f, 4f * Time.deltaTime);
            CobraHeadPose(3f, 5f);
            CobraRide();
            yield return null;
        }
    }

    // caught: gulped down - a lump slides down its neck - brought back up, and spat into the sky
    IEnumerator CobraSwallow()
    {
        var fx = GameManager.I.fx;
        Announce("SWALLOWED!", "", 1.2f, Gold);
        FightCamera.Punch(1f);
        CobraCam(3f, 17f, 1f, 1.6f, 55f, 0.55f);
        director?.SnapOverride();   // cut straight to the side view of its neck
        // the jaws snap shut on him
        hero.Hidden = true;
        fx.Crack(CobraMouth, 0.6f);
        fx.Impact(CobraMouth, 3f, 0.8f);
        for (int k = 0; k < 10; k++) fx.Puff(CobraMouth + Random.insideUnitSphere * 1.2f, 1.2f);
        Shot("cobra_swallow");
        Vector3 aimUp = (Flat(cobHeadFwd).normalized + Vector3.up * 0.8f).normalized;
        int squeezes = 0;
        for (float x = 0; x < 1.5f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            cobRear = Mathf.MoveTowards(cobRear, 7f, 5f * dt);
            cobReach = Mathf.MoveTowards(cobReach, 1.6f, 4f * dt);
            cobGape = Mathf.Lerp(cobGape, 3f, dt * 10f);
            cobHood = Mathf.MoveTowards(cobHood, 1.2f, dt);
            CobraHeadPose(0f, 6f);
            cobHeadFwd = Vector3.Slerp(cobHeadFwd, aimUp, 1 - Mathf.Exp(-4 * dt)).normalized;   // head thrown back to gulp
            cobBulge = Mathf.Lerp(0.6f, 6.2f, Smooth(x / 1.5f));
            cobHeadPos += Vector3.up * Mathf.Sin(x * 13f) * 0.06f;   // gulp, gulp
            hero.BossHold(cobra.shape.Point(cobBulge) - Vector3.up * 0.9f);
            CobraCam(3f, 17f, 1f, 1.6f, 55f, 0.55f);
            if (x > 0.35f + 0.38f * squeezes && squeezes < 3)
            {
                squeezes++;
                hero.BossSqueeze(4);
                fx.Crack(cobra.shape.Point(cobBulge), 0.4f + squeezes * 0.05f);
                FightCamera.Shake(0.3f);
            }
            CobraRide();
            if (x > 0.9f && x - dt <= 0.9f) Shot("cobra_gulp");
            yield return null;
        }
        // ...it heaves him back up...
        Vector3 aimOut = (Flat(cobHeadFwd).normalized + Vector3.up * 0.35f).normalized;
        for (float x = 0; x < 0.55f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            cobBulge = Mathf.Lerp(6.2f, 0.4f, Smooth(x / 0.55f));
            cobGape = Mathf.Lerp(3f, 78f, Smooth((x - 0.3f) / 0.25f));
            CobraHeadPose(0f, 6f);
            cobHeadFwd = Vector3.Slerp(cobHeadFwd, aimOut, 1 - Mathf.Exp(-6 * dt)).normalized;
            hero.BossHold(cobra.shape.Point(cobBulge) - Vector3.up * 0.9f);
            CobraCam(3f, 17f, 1f, 1.8f, 55f, 0.6f);
            CobraRide();
            yield return null;
        }
        // ...and spits him high into the air
        cobBulge = -1;
        hero.BossHold(CobraMouth - Vector3.up * 0.6f);
        hero.Hidden = false;
        hero.BossRelease(Flat(cobHeadFwd), 14, 13f, 7f);
        fx.Impact(CobraMouth, 5f, 0.6f);
        fx.Embers(CobraMouth, cobHeadFwd * 8f + Vector3.up * 4f, 40, 0.2f);
        fx.Energy(CobraMouth, cobHeadFwd, 30, new Color(1f, 0.5f, 0.15f), Color.white, 6f, 16f);
        fx.Whoosh(0.35f, 1f);
        FightCamera.Shake(0.7f);
        Shot("cobra_spit");
        Vector3 spitCam = CobraMouth + Right(Flat(cobHeadFwd).normalized) * 11f - Flat(cobHeadFwd).normalized * 3f + Vector3.up * 1.5f;
        for (float x = 0; x < 1.2f; x += Time.deltaTime)
        {
            cobGape = Mathf.Lerp(cobGape, 14f, Time.deltaTime * 3f);
            CobraHeadPose(3f, 5f);
            CobraRide();
            if (x < 1.0f) director?.Override(spitCam, Vector3.Lerp(CobraMouth, HeroChest, 0.7f), 60f, 0.25f);
            yield return null;
        }
    }

    // its skin cracks back into stone: the fused shards reappear on its body and blow apart
    IEnumerator CobraCollapse()
    {
        var fx = GameManager.I.fx;
        Shot("cobra_collapse");
        swarm.formSink = false;
        cobGlow = 3f;
        yield return null;   // one frame with the stones back on its skin
        var c = cobra.shape;
        float len = c.length;
        Vector3 head = CobraHeadCentre;
        Vector3 mid = c.Point(len * 0.22f);
        Vector3 fw = Flat(cobHeadFwd).sqrMagnitude > 0.01f ? Flat(cobHeadFwd).normalized : cobDir;
        Vector3 wide = mid + Right(fw) * 21f - fw * 5f + Vector3.up * 9f;
        CobraOff();
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        for (int k = 0; k <= 12; k++) swarm.Burst(c.Point(len * k / 12f), 5f, 2.5f, 3.6f);
        swarm.Burst(head, 7f, 3f, 3f);
        fx.Impact(Flat(head), 8f, 0.45f);
        fx.Chroma(0.6f);
        FightCamera.Shake(0.9f);
        AllShardsOut();
        StartCoroutine(Shockwave(Flat(head), 8f, 8, 3f));
        for (float x = 0; x < 1.1f; x += Time.deltaTime)
        {
            goal = HoverSpot(8f); height = 2f;
            if (x < 0.9f) director?.Override(wide, mid + Vector3.up * 1.5f, 58f, 0.25f);
            yield return null;
        }
    }

    // punched apart mid-strike: the whole snake falls as rubble
    void CobraShatter(Vector3 at)
    {
        swarm.formSink = false;
        CobraOff();
        swarm.Burst(at, 8f, 3f, 5f);
    }
}
