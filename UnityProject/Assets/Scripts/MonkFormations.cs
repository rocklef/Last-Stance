using System.Collections;
using UnityEngine;

// MOROS, part five: the formations of the Enthiran climax, built from EVERY stone in the town -
// the whole floor of rubble rises and streams into them.
//   THE KING COBRA    (MonkCobra.cs)
//   THE STONE SPHERE  the rubble whirls up into a vortex, balls into a spiked, armoured sphere with
//                     burning seams, slams down, and runs him down through the streets - bounding,
//                     bouncing off the houses - before it bursts apart in a storm of stone
//   THE DRILL         spins up, plunges into the ground, tunnels toward him and bursts up under him
//   THE WALL          a rampart rises course by course out of the street and closes in: it turns to
//                     face him and slides to keep him in front, its ends curl forward to trap him,
//                     then the ends slam shut and it topples onto him
public partial class MonkBoss
{
    const int AllShardsBack = 5600;   // how many stay in play after a grand formation

    // ---------------------------------------------------------------- the sphere
    Vector3 sphC;
    Quaternion sphRot = Quaternion.identity;
    const float SphR = 4.2f;

    // ---------------------------------------------------------------- the drill
    Vector3 drTip, drAxis = Vector3.down;
    float drSpin;
    const float DrLen = 5.5f, DrRad = 1.4f;

    // ---------------------------------------------------------------- the wall
    Vector3 wallC, wallF = Vector3.forward;
    float wallTilt, wallBend;
    const float WallW = 24f, WallH = 7f;

    // ---------------------------------------------------------------- solid cores the stones cover
    GameObject sphCore;
    Mesh cubeMesh;
    Material plateMat;
    readonly Matrix4x4[] wallBlocks = new Matrix4x4[ShardForms.WallCols * ShardForms.WallRows];
    float sphCoreScale, wallBuild;
    bool wallCore;

    Material PlateMat()
    {
        if (plateMat == null)
        {
            plateMat = Resources.Load<Material>("Cobra/StonePlates");
            if (plateMat == null)
            {
                plateMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                plateMat.SetColor("_BaseColor", new Color(0.3f, 0.28f, 0.26f));
            }
        }
        return plateMat;
    }

    // from LateUpdate: the stone ball's core turns with it; the wall's blocks rise course by course
    void CoresLate()
    {
        bool sphOn = form == Form.Sphere && sphCoreScale > 0.01f;
        if (sphOn && sphCore == null)
        {
            sphCore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphCore.name = "StoneSphereCore";
            Destroy(sphCore.GetComponent<Collider>());
            sphCore.GetComponent<Renderer>().sharedMaterial = PlateMat();
        }
        if (sphCore != null)
        {
            if (sphCore.activeSelf != sphOn) sphCore.SetActive(sphOn);
            if (sphOn)
            {
                sphCore.transform.SetPositionAndRotation(sphC, sphRot);
                sphCore.transform.localScale = Vector3.one * (SphR * 1.94f * sphCoreScale);
            }
        }
        if (form != Form.Wall || !wallCore) return;
        if (cubeMesh == null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeMesh = g.GetComponent<MeshFilter>().sharedMesh;
            Destroy(g);
        }
        int n = 0;
        for (int row = 0; row < ShardForms.WallRows; row++)
        {
            float grow = Mathf.Clamp01((wallBuild - 0.55f - row / 7f * 2.2f) / 0.45f);
            if (grow <= 0) continue;
            for (int col = 0; col < ShardForms.WallCols; col++)
            {
                ShardForms.WallBlock(wallC, wallF, WallW, WallH, wallTilt, wallBend, row, col, out var c, out var q, out var half);
                Vector3 up = q * Vector3.up;
                wallBlocks[n++] = Matrix4x4.TRS(c - up * (half.y * (1 - grow)), q, new Vector3(half.x * 1.9f, half.y * 1.86f * grow, half.z * 1.8f));
            }
        }
        if (n > 0)
        {
            var rp = new RenderParams(PlateMat())
            {
                worldBounds = new Bounds(wallC, Vector3.one * 80f),
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                receiveShadows = true,
            };
            Graphics.RenderMeshInstanced(rp, cubeMesh, 0, wallBlocks, n);
        }
    }

    static readonly bool EnthiranTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-enthiran") >= 0;
    static readonly string[] EnthiranSeq = { "cobra", "sphere", "wall", "drill" };

    void AllShardsIn(float chip)
    {
        swarm.active = swarm.Count;
        swarm.chipScale = chip;
    }

    void AllShardsOut()
    {
        swarm.active = AllShardsBack;
        swarm.chipScale = 1f;
    }

    // ================================================================== THE STONE SPHERE
    IEnumerator RollingSphere()
    {
        act = "sphere";
        camLift = 2f;
        AllShardsIn(1.35f);
        Announce("THE STONE SPHERE", "IT RUNS YOU DOWN  -  CLIMB A ROOF (SPACE)", 1.6f);
        Play("PowerUp", 0.15f);
        director?.Cut(BossDirector.Shot.Side);
        var fx = GameManager.I.fx;
        // every stone in the town whirls up into a vortex around him
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Twister;
        swarm.twisterHeight = 11f; swarm.twisterWidth = 0.6f;
        float rumble = 0;
        for (float x = 0; x < 1.5f; x += Time.deltaTime)
        {
            goal = Flat(pos); height = 3f;
            rumble -= Time.deltaTime;
            if (rumble <= 0) { rumble = 0.3f; FightCamera.Shake(0.12f); SoundStage.At(AudioSynth.Rumble, pos, 0.5f, 0.9f); }
            yield return null;
        }
        swarm.twisterHeight = 4.6f; swarm.twisterWidth = 0.32f;
        // which balls up into the sphere, spinning high above him
        form = Form.Sphere;
        sphRot = Quaternion.identity;
        sphCoreScale = 0;
        sphC = Flat(pos) + ToHero() * 3f + Vector3.up * (SphR + 6f);
        swarm.BeginForm(1.5f);
        for (float x = 0; x < 1.9f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            goal = HoverSpot(9f); height = 3f;
            sphRot = Quaternion.AngleAxis(Mathf.Lerp(60f, 420f, x / 1.9f) * dt, (Vector3.up + Vector3.right * 0.3f).normalized) * sphRot;
            sphCoreScale = Smooth((x - 0.45f) / 1.2f);
            sphC += Vector3.up * Mathf.Sin(x * 3f) * 0.6f * dt;
            if (x > 1.2f && x - dt <= 1.2f) Shot("sphere_form");
            yield return null;
        }
        // it slams down
        Vector3 top = sphC, land = Flat(sphC);
        if (TownMap.I != null) land = TownMap.I.Resolve(land, SphR * 0.85f, 2.2f);
        for (float x = 0; x < 0.4f; x += Time.deltaTime)
        {
            float u = x / 0.4f;
            sphC = Vector3.Lerp(top, land + Vector3.up * SphR, u * u);
            yield return null;
        }
        fx.Impact(land, 10f, 0.4f);
        fx.Chroma(0.8f);
        FightCamera.Shake(1f);
        StartCoroutine(Shockwave(land, 9f, 12, 4f));
        Shot("sphere_drop");
        // and runs him down
        Vector3 v = ToHero() * 4f;
        float y = SphR, vy = 0, hitCd = 0, dustT = 0, bounceCd = 0;
        int bounces = 0;
        rumble = 0;
        for (float x = 0; x < 9f && bounces < 9 && !dying; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            goal = HoverSpot(9f); height = 3f;
            hitCd -= dt; dustT -= dt; bounceCd -= dt; rumble -= dt;
            Vector3 to = HeroFlat - Flat(sphC);
            Vector3 want = to.sqrMagnitude > 0.01f ? to.normalized * 13f : v;
            v = Vector3.MoveTowards(v, want, 6f * dt);   // it steers, but it has momentum
            Vector3 next = Flat(sphC) + v * dt;
            if (TownMap.I != null)
            {
                Vector3 res = TownMap.I.Resolve(next, SphR * 0.85f, 2.2f);
                Vector3 push = Flat(res - next);
                if (push.sqrMagnitude > 1e-5f)
                {
                    Vector3 n = push.normalized;
                    if (Vector3.Dot(v, n) < 0)
                    {
                        v = Vector3.Reflect(v, n) * 0.75f;
                        if (bounceCd <= 0)
                        {
                            bounceCd = 0.3f;
                            bounces++;
                            vy = Mathf.Max(vy, 5f);   // it bounds off the wall
                            fx.Impact(Flat(sphC) - n * SphR + Vector3.up * y * 0.5f, 6f, 0.55f);
                            FightCamera.Shake(0.5f);
                        }
                    }
                    next = res;
                }
            }
            // bounding: now and then it leaps, and every landing quakes
            if (y <= SphR + 0.01f && Random.value < 0.006f) vy = 7f;
            if (y > SphR || vy > 0)
            {
                vy -= 22f * dt;
                y += vy * dt;
                if (y <= SphR && vy < 0)
                {
                    if (vy < -6f)
                    {
                        fx.Impact(next, 6f, 0.5f);
                        FightCamera.Shake(0.45f);
                        StartCoroutine(Shockwave(next, 6f, 8, 3f));
                    }
                    y = SphR;
                    vy = 0;
                }
            }
            float spd = v.magnitude;
            if (spd > 0.05f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, v / spd);
                sphRot = Quaternion.AngleAxis(spd * dt / SphR * Mathf.Rad2Deg, axis) * sphRot;
            }
            sphC = next + Vector3.up * y;
            if (dustT <= 0 && spd > 1f && y < SphR + 0.3f)
            {
                dustT = 0.06f;
                Vector3 back = next - v / spd * SphR * 0.8f;
                fx.Puff(back + Vector3.up * 0.3f + Random.insideUnitSphere * 1.2f, 1.8f);
                if (Random.value < 0.5f) fx.Energy(next + Random.insideUnitSphere * SphR * 0.5f, Vector3.up, 3, new Color(1f, 0.7f, 0.4f), Color.white, 2f, 6f);
                FightCamera.Shake(0.05f);
            }
            if (rumble <= 0) { rumble = 0.28f; SoundStage.At(AudioSynth.Rumble, next, 0.5f, 1.1f); }
            Vector3 rel = Flat(hero.transform.position - Flat(sphC));
            if (hitCd <= 0 && rel.magnitude < SphR + 0.5f && hero.Height < y + SphR * 0.5f)
            {
                hitCd = 1.2f;
                Strike(sphC, 22, 10f, 8f, false, true, "sphere");   // run over and flung into the air
            }
            if (x > 1.5f && x - dt <= 1.5f) Shot("sphere_roll");
            if (x > 4.5f && x - dt <= 4.5f) Shot("sphere_chase");
            yield return null;
        }
        // it bursts apart in a storm of stone
        fx.Impact(Flat(sphC), 10f, 0.4f);
        fx.Chroma(0.8f);
        FightCamera.Shake(1f);
        Shot("sphere_shatter");
        sphCoreScale = 0;
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.Burst(sphC, 14f, 6f);
        AllShardsOut();
        StartCoroutine(Shockwave(Flat(sphC), 9f, 10, 4f));
        for (float x = 0; x < 0.6f; x += Time.deltaTime) { goal = HoverSpot(8f); height = 2f; yield return null; }
    }

    // ================================================================== THE DRILL
    IEnumerator DrillAttack()
    {
        act = "drill";
        form = Form.Drill;
        swarm.chipScale = 1.1f;
        camLift = 1.6f;
        Announce("THE DRILL", "IT BURROWS UNDER YOU  -  KEEP MOVING", 1.5f);
        Play("Cast", 0.15f);
        director?.Cut(BossDirector.Shot.Side);
        var fx = GameManager.I.fx;
        Vector3 spot = Flat(pos) + ToHero() * 2.5f;
        if (TownMap.I != null) spot = TownMap.I.Resolve(spot, 1.5f, 2.2f);
        drAxis = Vector3.down;
        drTip = spot + Vector3.up * 2.5f;
        swarm.BeginForm(1.0f);
        for (float x = 0; x < 1.3f; x += Time.deltaTime)
        {
            goal = HoverSpot(8f);
            height = 2.5f;
            drSpin += Mathf.Lerp(2f, 18f, x / 1.3f) * Time.deltaTime;
            yield return null;
        }
        Vector3 top = drTip;
        for (float x = 0; x < 0.5f; x += Time.deltaTime)
        {
            drSpin += 18f * Time.deltaTime;
            drTip = Vector3.Lerp(top, spot - Vector3.up * DrLen * 0.9f, Smooth(x / 0.5f));
            if (Random.value < 0.5f) fx.Puff(spot + Vector3.up * 0.2f + Random.insideUnitSphere * 1.2f, 1.5f);
            yield return null;
        }
        fx.Impact(spot, 5f, 0.6f);
        Shot("drill_plunge");
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        Vector3 burrow = spot;
        for (int k = 0; k < 3 && !dying; k++)
        {
            Vector3 target = HeroFlat;
            float rumble = 0;
            for (float x = 0; x < 2.2f && Flat(target - burrow).magnitude > 0.6f; x += Time.deltaTime)
            {
                float dt = Time.deltaTime;
                target = Vector3.Lerp(target, HeroFlat, 1 - Mathf.Exp(-2 * dt));
                burrow = Vector3.MoveTowards(burrow, Flat(target), 14f * dt);
                if (Random.value < 0.6f) fx.Puff(burrow + Vector3.up * 0.2f + Random.insideUnitSphere * 0.6f, 1.2f);
                if (Random.value < 0.25f) fx.Energy(burrow, Vector3.up, 3, new Color(0.8f, 0.72f, 0.62f), Color.white, 2f, 5f);
                FightCamera.Shake(0.05f);
                rumble -= dt;
                if (rumble <= 0) { rumble = 0.25f; SoundStage.At(AudioSynth.Rumble, burrow, 0.35f, 1.2f); }
                goal = HoverSpot(8f);
                height = 2.5f;
                yield return null;
            }
            for (float x = 0; x < 0.55f; x += Time.deltaTime)
            {
                ShowDisc(burrow, 2.4f);
                if (Random.value < 0.5f) fx.Puff(burrow + Random.insideUnitSphere * 1.5f, 1.3f);
                goal = HoverSpot(8f);
                height = 2.5f;
                yield return null;
            }
            HideWarn();
            swarm.GatherActive(burrow - Vector3.up * 0.5f, 1.2f);
            form = Form.Drill;
            drAxis = Vector3.up;
            swarm.BeginForm(0.12f);
            fx.Impact(burrow, 7f, 0.45f);
            FightCamera.Shake(0.7f);
            if (Flat(hero.transform.position - burrow).magnitude < 2.6f && hero.Height - hero.floorY < 1.5f && hero.floorY < 1f)
                Strike(burrow, 16, 9f, 2f, false, false, "drill");
            Shot("drill_erupt");
            Vector3 low = burrow - Vector3.up * DrLen * 0.6f, high = burrow + Vector3.up * DrLen * 0.8f;
            for (float x = 0; x < 0.35f; x += Time.deltaTime) { drSpin += 20f * Time.deltaTime; drTip = Vector3.Lerp(low, high, Smooth(x / 0.35f)); yield return null; }
            for (float x = 0; x < 0.45f; x += Time.deltaTime) { drSpin += 20f * Time.deltaTime; yield return null; }
            for (float x = 0; x < 0.35f; x += Time.deltaTime) { drSpin += 20f * Time.deltaTime; drTip = Vector3.Lerp(high, burrow - Vector3.up * DrLen, Smooth(x / 0.35f)); yield return null; }
            form = Form.None;
            swarm.mode = ShardSwarm.Mode.Free;
        }
        yield return Dissolve(burrow + Vector3.up, 3f);
    }

    // ================================================================== THE WALL
    // where Kaito is relative to the curved front of the wall
    void WallRel(out float xw, out float along)
    {
        Vector3 rel = Flat(hero.transform.position - wallC);
        xw = Vector3.Dot(rel, Right(wallF));
        float k = xw / (WallW * 0.5f);
        along = Vector3.Dot(rel, wallF) - wallBend * k * k * WallW * 0.35f;
    }

    IEnumerator WallAttack()
    {
        act = "wall";
        camLift = 2.2f;
        AllShardsIn(1.4f);
        Announce("THE WALL", "IT CLOSES IN  -  CLIMB A ROOF (SPACE) OR GET PAST ITS EDGE", 1.8f);
        Play("PowerUp", 0.15f);
        director?.Cut(BossDirector.Shot.Side);
        var fx = GameManager.I.fx;
        wallF = ToHero();
        wallC = Flat(pos) + wallF * 4f;
        if (TownMap.I != null) wallC = TownMap.I.Resolve(wallC, 1f, 2.2f);
        wallTilt = 0;
        wallBend = 0;
        form = Form.Wall;
        wallBuild = 0;
        wallCore = true;
        swarm.BeginForm(2.2f, true);   // course by course, out of the street
        float rumble = 0;
        for (float x = 0; x < 2.5f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            wallBuild += dt;
            goal = wallC - wallF * 3f; height = WallH + 2f;
            Vector3 b = ShardForms.WallBase(wallC, wallF, WallW, wallBend, Random.Range(-WallW, WallW) * 0.5f);
            if (Random.value < 0.8f) fx.Puff(b + Vector3.up * 0.3f, 1.8f);
            rumble -= dt;
            if (rumble <= 0) { rumble = 0.3f; FightCamera.Shake(0.12f); SoundStage.At(AudioSynth.Rumble, wallC, 0.55f, 0.9f); }
            if (x > 1.6f && x - dt <= 1.6f) Shot("wall_rise");
            yield return null;
        }
        // it advances - turning to face him, sliding to keep him in front, its ends curling in
        const float speed = 6.5f;
        float hitCd = 0;
        for (float x = 0; x < 6f && !dying; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            hitCd -= dt;
            Vector3 to = Flat(HeroFlat - wallC);
            if (to.sqrMagnitude > 0.01f) wallF = Vector3.RotateTowards(wallF, to.normalized, 0.7f * dt, 0);
            wallF = Flat(wallF).normalized;
            Vector3 rgt = Right(wallF);
            float lat = Vector3.Dot(to, rgt);
            Vector3 next = wallC + (wallF * speed + rgt * Mathf.Clamp(lat, -3f, 3f) * 1.2f) * dt;
            if (TownMap.I != null && TownMap.I.InsideSolid(next + Vector3.up, 0.2f)) break;   // it runs into a building
            wallC = next;
            wallBend = Mathf.MoveTowards(wallBend, 0.9f, 0.2f * dt);
            goal = wallC - wallF * 3f; height = WallH + 2f;
            if (Random.value < 0.7f) fx.Puff(ShardForms.WallBase(wallC, wallF, WallW, wallBend, Random.Range(-WallW, WallW) * 0.5f) + wallF * 0.7f + Vector3.up * 0.2f, 1.5f);
            rumble -= dt;
            if (rumble <= 0) { rumble = 0.3f; FightCamera.Shake(0.1f); SoundStage.At(AudioSynth.Rumble, wallC, 0.5f, 0.95f); }
            WallRel(out float xw, out float along);
            if (Mathf.Abs(xw) < WallW * 0.5f + 0.3f && along > -0.8f && along < 1.3f && hero.Height < WallH - 0.4f && hero.state != Fighter.State.Dash)
            {
                hero.WindForce((wallF - rgt * Mathf.Sign(xw) * 0.3f) * (speed + 2f), dt);   // shoved along, toward the middle
                if (hitCd <= 0) { hitCd = 1f; Strike(wallC, 8, 0, 3f, false, true, "wall"); }
            }
            if (x > 2f && x - dt <= 2f) Shot("wall_close");
            yield return null;
        }
        // the ends slam shut...
        fx.Whoosh(0.3f, 1f);
        for (float x = 0; x < 0.5f; x += Time.deltaTime) { wallBend = Mathf.Lerp(wallBend, 1.5f, 1 - Mathf.Exp(-6 * Time.deltaTime)); yield return null; }
        FightCamera.Shake(0.5f);
        Shot("wall_trap");
        // ...and it topples onto him
        for (float x = 0; x < 0.65f; x += Time.deltaTime)
        {
            float e = x / 0.65f;
            wallTilt = Mathf.Lerp(0, 88f, e * e);
            yield return null;
        }
        for (int k = -3; k <= 3; k++)
            fx.Impact(ShardForms.WallBase(wallC, wallF, WallW, wallBend, k * WallW / 7f) + wallF * WallH * 0.55f, 6f, 0.45f);
        fx.Chroma(0.8f);
        FightCamera.Shake(1.2f);
        {
            WallRel(out float xw, out float along);
            if (along > 0 && along < WallH + 0.5f && Mathf.Abs(xw) < WallW * 0.5f && hero.floorY < 1f)
                Strike(wallC + wallF * WallH * 0.5f, 18, 5f, 4f, false, false, "wall_fall");
        }
        Shot("wall_fall");
        wallCore = false;
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.Burst(wallC + wallF * WallH * 0.5f + Vector3.up, 7f, 3f, WallW);
        AllShardsOut();
        StartCoroutine(Shockwave(wallC + wallF * WallH * 0.5f, 10f, 10, 4f));
        for (float x = 0; x < 0.6f; x += Time.deltaTime) { goal = HoverSpot(8f); height = 2f; yield return null; }
    }

    // ================================================================== shapes, reach and framing
    void BuildFormation(ShardSwarm s)
    {
        switch (form)
        {
            case Form.Sphere: ShardForms.StoneSphere(s.parts, sphC, SphR, sphRot); break;
            case Form.Cobra: CobraParts(s.parts); break;
            case Form.Drill: ShardForms.Drill(s.parts, drTip, drAxis, DrLen, DrRad, drSpin); break;
            case Form.Wall: ShardForms.ShardWall(s.parts, wallC, wallF, WallW, WallH, wallTilt, wallBend); break;
        }
    }

    bool NearFormation(Vector3 p, float r)
    {
        switch (form)
        {
            case Form.Sphere: return (p - sphC).magnitude < SphR + r + 0.5f;
            case Form.Cobra: return (p - CobraHeadCentre).magnitude < r + 1.8f;
            case Form.Drill: return SegDist(p, drTip, drTip - drAxis * DrLen) < r + DrRad + 0.3f;
            case Form.Wall:
            {
                Vector3 rel = Flat(p - wallC);
                float xw = Vector3.Dot(rel, Right(wallF)), k = xw / (WallW * 0.5f);
                float along = Vector3.Dot(rel, wallF) - wallBend * k * k * WallW * 0.35f;
                return Mathf.Abs(along) < 1.3f && Mathf.Abs(xw) < WallW * 0.5f && p.y < WallH + 1f;
            }
        }
        return false;
    }

    Vector3 FormationPoint()
    {
        switch (form)
        {
            case Form.Sphere: return sphC;
            case Form.Cobra: return CobraHeadCentre;
            case Form.Drill: return drTip;
            default: return wallC + Vector3.up * WallH * 0.5f;
        }
    }

    void FrameFormation(ref Bounds b)
    {
        switch (form)
        {
            case Form.Sphere: b.Encapsulate(new Bounds(sphC, Vector3.one * SphR * 2.8f)); break;
            case Form.Cobra: FrameCobra(ref b); break;
            case Form.Drill: b.Encapsulate(drTip); b.Encapsulate(drTip - drAxis * DrLen); break;
            case Form.Wall:
                b.Encapsulate(ShardForms.WallBase(wallC, wallF, WallW, wallBend, WallW * 0.5f) + Vector3.up * WallH);
                b.Encapsulate(ShardForms.WallBase(wallC, wallF, WallW, wallBend, -WallW * 0.5f));
                break;
        }
    }
}
