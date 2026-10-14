using System.Collections;
using UnityEngine;

// MOROS, part two: THE ARSENAL and the hunt through the buildings.
//   At 70% health he arms himself - the swarm becomes a siege cannon, twin gatlings and a rocket pod
//   circling him - and from then on he also fights at range:
//     SHARD GATLINGS   two six-barrel guns spray stone rounds (break line of sight, block, dash)
//     SIEGE CANNON     a banded cannon lobs stone balls onto red circles
//     ROCKET BARRAGE   a pod launches homing rockets (they explode on buildings - use them as cover)
//   After the ascension the whole swarm can become THE GREAT DRAGON: a winged stone dragon the size
//   of a street that circles, breathes fire along a street and dives into Kaito.
// His tactics follow Kaito around the town: close weapons in the courtyard, guns and rockets at a
// hero on the rooftops, and indoors - where he cannot follow - the SHARD GALE (a storm of stones
// through the doorway that shoves Kaito, then drags him out) and SEEKER SWARMS (small clusters that
// search the rooms and haul him out to Moros).
// Kaito's powers hook in here too: Thunder Palm, the Guardian Spirit, Blade Hand Awakening and the
// Stone Gauntlets can shatter his weapons, crumble the titan and pin him down.
public partial class MonkBoss
{
    static readonly string[] Phase2 = { "gatling", "axe", "grasp", "rockets", "lances", "serpent", "sphere", "cobra", "drill", "wall" };
    static readonly string[] Phase3 = { "dragon", "gatling", "roc", "hammer", "rockets", "axe", "grasp", "lances", "serpent", "flood", "sphere", "cobra", "drill", "wall" };
    static readonly string[] RoofP1 = { "lances", "serpent", "grasp" };
    static readonly string[] RoofP2 = { "gatling", "rockets", "lances", "cobra" };
    static readonly string[] RoofP3 = { "dragon", "rockets", "roc", "gatling", "cobra" };

    bool armed, seized, indoorFlip;
    int titanCracks;
    ShardProjectiles ammo;

    // arsenal poses
    Vector3 gunL, gunR, gunDir = Vector3.forward;
    float gunSpin;
    const float GunScale = 1.15f;
    Vector3 podC, podDir = Vector3.up;
    int podLoaded;
    const float PodScale = 1.25f;
    float arsenalAngle;
    // the celestial dragon's spine (0 = head): every link follows the exact path the head flew,
    // so the long body snakes through the air in S-curves
    readonly Vector3[] drag = new Vector3[30];
    readonly Vector3[] dragTrail = new Vector3[1200];
    int dragTrailHead, dragTrailCount;
    Vector3 dragLast, dragMove = Vector3.forward;
    float dragGape;
    const float DragonScale = 1.25f, DragonLink = 1.3f, DragonStep = 0.15f;

    static readonly Color Spirit = new Color(0.45f, 0.75f, 1f);

    public bool Hunting => hunting;
    TownMap.Zone HeroZone => TownMap.I != null ? TownMap.I.ZoneAt(hero.transform.position) : TownMap.Zone.Courtyard;
    bool HeroIndoors => TownMap.I != null && TownMap.I.RoomAt(hero.transform.position) >= 0;
    float HeroFloor => hero != null ? hero.floorY : 0;

    public void CaptureShot(string name) => Shot(name);

    // every phase shows its whole arsenal, in order, before he can be pushed on (or killed):
    // his health holds just above the phase line until the last formation of the list has been used
    int roofIndex;
    bool PhaseDone => powerIndex >= (ascended ? Phase3 : armed ? Phase2 : Phase1).Length;
    float HpFloor => ShowcaseMode ? 0 : !armed ? (PhaseDone ? maxHp * 0.4f + 1 : maxHp * 0.7f + 1)
                   : !ascended ? (PhaseDone ? 1 : maxHp * 0.4f + 1)
                   : PhaseDone ? 0 : 1;

    // ================================================================== the fight loop
    IEnumerator Loop()
    {
        while (true)
        {
            if (!armed && hp <= maxHp * 0.7f && PhaseDone) { yield return Arsenal(); continue; }
            if (armed && !ascended && hp <= maxHp * 0.4f && PhaseDone) { yield return Ascension(); continue; }
            yield return Hover(enraged ? 0.7f : 1.2f);
            cycles++;
            if (HeroIndoors) { yield return NextPower(); yield return Hover(0.5f); continue; }
            // Kaito ran off into the town - or it is simply time to hunt him
            // hunt only when Kaito has really got away: far off AND out of sight behind the houses
            if (!EnthiranTest && fled && HeroHidden) { yield return Hunt(); continue; }
            yield return NextPower();
            if (enraged && !broken) { yield return Hover(0.35f); yield return NextPower(); }
            yield return Brawl();
        }
    }

    IEnumerator NextPower()
    {
        broken = false;
        var zone = HeroZone;
        if (zone == TownMap.Zone.Indoors)
        {
            indoorFlip = !indoorFlip;
            return indoorFlip ? Gale() : Seekers();
        }
        string[] seq = zone == TownMap.Zone.Rooftops ? (ascended ? RoofP3 : armed ? RoofP2 : RoofP1)
                                                     : (ascended ? Phase3 : armed ? Phase2 : Phase1);
        if (EnthiranTest) seq = EnthiranSeq;
        // the rooftops have their own short list, so the main arsenal keeps its order
        bool roof = zone == TownMap.Zone.Rooftops && !EnthiranTest;
        string pick = roof ? seq[roofIndex++ % seq.Length] : seq[powerIndex++ % seq.Length];
        if (AutoCapture.Active) Debug.Log($"[AUTO] power {pick} phase={(ascended ? 3 : armed ? 2 : 1)} hp={hp}");
        switch (pick)
        {
            case "axe": return Axe();
            case "grasp": return Grasp();
            case "lances": return Lances();
            case "serpent": return Serpent();
            case "roc": return Roc();
            case "hammer": return Hammer();
            case "gatling": return Gatling();
            case "rockets": return Rockets();
            case "dragon": return GreatDragon();
            case "sphere": return RollingSphere();
            case "cobra": return CobraStrike();
            case "drill": return DrillAttack();
            case "wall": return WallAttack();
            default: return Flood();
        }
    }

    // ================================================================== THE ARSENAL (70%)
    IEnumerator Arsenal()
    {
        armed = true;
        powerIndex = 0;
        act = "arsenal";
        Cleanup();
        form = Form.Arsenal;
        swarm.active = Mathf.Max(swarm.active, 7000);
        swarm.chipScale = 1.2f;
        Announce("MOROS ARMS HIMSELF", "THE TEMPLE BECOMES AN ARSENAL", 2.4f);
        Play("PowerUp", 0.15f);
        director?.Cut(BossDirector.Shot.Orbit);
        GameManager.I.fx.Chroma(0.8f);
        swarm.BeginForm(1.6f);
        for (float x = 0; x < 3.4f; x += Time.deltaTime)
        {
            goal = HoverSpot(8f);
            height = 2.6f;
            arsenalAngle += 0.9f * Time.deltaTime;
            PlaceArsenal(Time.deltaTime);
            if (x > 1.6f && x - Time.deltaTime <= 1.6f) Shot("arsenal");
            yield return null;
        }
        // a warning volley
        Announce("ROCKET BARRAGE", "RUN BEHIND THE BUILDINGS", 1.1f);
        for (int k = 0; k < 4; k++)
        {
            PlaceArsenal(Time.deltaTime);
            Vector3 muzzle = ShardForms.RocketTube(podC, podDir, PodScale, k);
            ammo.FireRocket(muzzle, (podDir + Random.insideUnitSphere * 0.3f).normalized, 9f, 8f, 2.2f);
            for (float x = 0; x < 0.2f; x += Time.deltaTime) { goal = HoverSpot(8f); height = 2.6f; arsenalAngle += 0.9f * Time.deltaTime; PlaceArsenal(Time.deltaTime); yield return null; }
        }
        for (float x = 0; x < 1.4f; x += Time.deltaTime) { goal = HoverSpot(8f); height = 2.6f; arsenalAngle += 0.9f * Time.deltaTime; PlaceArsenal(Time.deltaTime); yield return null; }
        HideWarn();
        yield return Dissolve(pos + Vector3.up, 2f);
        swarm.active = 5200;
    }

    void PlaceArsenal(float dt)
    {
        Vector3 f = ToHero(), r = Right(f);
        var rot = Quaternion.AngleAxis(arsenalAngle * Mathf.Rad2Deg, Vector3.up);
        gunL = pos + rot * (-r * 2.4f) + Vector3.up * 0.6f;
        gunR = pos + rot * (-f * 2.4f) + Vector3.up * 1.4f;
        gunDir = Vector3.Slerp(gunDir, (HeroChest - (gunL + gunR) * 0.5f).normalized, 1 - Mathf.Exp(-5 * dt));
        podC = pos + Vector3.up * 2.9f;
        podDir = Vector3.Slerp(podDir, (Vector3.up * 0.9f + f * 0.45f).normalized, 1 - Mathf.Exp(-5 * dt));
        podLoaded = 9;
        gunSpin += dt * 6;
    }

    Vector3 HeroTarget() => new Vector3(hero.transform.position.x, HeroFloor, hero.transform.position.z);

    // ------------------------------------------------------------------ SHARD GATLINGS
    IEnumerator Gatling()
    {
        act = "gatling";
        form = Form.Gatling;
        swarm.chipScale = 1.05f;
        camLift = 1.5f;
        Announce("SHARD GATLINGS", "BREAK LINE OF SIGHT  -  BLOCK  -  DASH", 1.3f);
        Play("Summon", 0.15f);
        director?.Cut(BossDirector.Shot.Side);
        swarm.BeginForm(0.8f);
        float spinV = 0, next = 0;
        int barrel = 0;
        float fire = enraged ? 3.4f : 2.6f;
        var fx = GameManager.I.fx;
        for (float x = 0; x < 1.1f + fire + 0.5f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            goal = HoverSpot(7.5f);
            height = 1.6f;
            Vector3 f = ToHero(), r = Right(f);
            gunL = pos - r * 1.5f + Vector3.up * 0.35f + f * 0.3f;
            gunR = pos + r * 1.5f + Vector3.up * 0.35f + f * 0.3f;
            gunDir = Vector3.Slerp(gunDir, (HeroChest - (gunL + gunR) * 0.5f).normalized, 1 - Mathf.Exp(-5 * dt));
            if (x < 1.1f)
            {
                spinV = Mathf.Lerp(0, 26, x / 1.1f);
                ShowStrip(Flat(pos), f, Mathf.Min(14f, Flat(hero.transform.position - pos).magnitude));
            }
            else if (x < 1.1f + fire)
            {
                HideWarn();
                spinV = 26;
                next -= dt;
                while (next <= 0)
                {
                    next += 0.045f;
                    Vector3 c = barrel++ % 2 == 0 ? gunL : gunR;
                    Vector3 muzzle = ShardForms.GatlingMuzzle(c, gunDir, GunScale);
                    Vector3 aim = HeroChest + new Vector3(hero.transform.forward.x, 0, hero.transform.forward.z) * 0.3f + Random.insideUnitSphere * 0.55f;
                    ammo.FireBullet(muzzle, aim - muzzle, 40f, 2.2f);
                    if (barrel % 2 == 0) fx.Energy(muzzle, gunDir, 3, new Color(1f, 0.85f, 0.5f), Color.white, 3f, 8f);
                }
                if (Random.value < 0.3f) FightCamera.Shake(0.05f);
                if (x - dt < 1.6f && x >= 1.6f) Shot("gatling_fire");
            }
            else spinV = Mathf.MoveTowards(spinV, 0, dt * 50);
            gunSpin += spinV * dt;
            yield return null;
        }
        HideWarn();
        yield return Dissolve(pos + Vector3.up, 2f);
    }

    // ------------------------------------------------------------------ ROCKET BARRAGE
    IEnumerator Rockets()
    {
        act = "rockets";
        form = Form.Rockets;
        swarm.chipScale = 1.15f;
        camLift = 2f;
        Announce("ROCKET BARRAGE", "RUN BEHIND THE BUILDINGS", 1.3f);
        Play("Cast", 0.15f);
        director?.Cut(BossDirector.Shot.Low);
        swarm.BeginForm(0.9f);
        podLoaded = 9;
        for (float x = 0; x < 1.1f; x += Time.deltaTime) { goal = HoverSpot(8f); height = 2f; PlacePod(Time.deltaTime); yield return null; }
        int n = enraged ? 12 : 8;
        var fx = GameManager.I.fx;
        for (int k = 0; k < n; k++)
        {
            PlacePod(Time.deltaTime);
            Vector3 muzzle = ShardForms.RocketTube(podC, podDir, PodScale, k % 9);
            Vector3 d = (podDir + Random.insideUnitSphere * 0.35f).normalized;
            ammo.FireRocket(muzzle, d, 9f, 9f, 2.4f);
            podLoaded = Mathf.Max(0, 8 - k % 9);
            fx.Embers(muzzle, -podDir * 3f, 6, 0.25f);
            fx.Puff(muzzle - podDir * 0.4f, 0.9f);
            if (k == 2) Shot("rockets_launch");
            for (float x = 0; x < 0.13f; x += Time.deltaTime) { goal = HoverSpot(8f); height = 2f; PlacePod(Time.deltaTime); yield return null; }
        }
        for (float x = 0; x < 3.5f && ammo.Live > 0; x += Time.deltaTime) { goal = HoverSpot(8f); height = 2f; PlacePod(Time.deltaTime); if (x > 1f && x - Time.deltaTime <= 1f) Shot("rockets_hit"); yield return null; }
        yield return Dissolve(podC, 2f);
    }

    void PlacePod(float dt)
    {
        Vector3 f = ToHero();
        podC = pos + Vector3.up * 2.4f - f * 0.3f;
        podDir = Vector3.Slerp(podDir, (Vector3.up * 0.9f + f * 0.5f).normalized, 1 - Mathf.Exp(-5 * dt));
    }

    // ------------------------------------------------------------------ THE GREAT DRAGON
    Vector3 DragonTrailAt(float idx)
    {
        idx = Mathf.Clamp(idx, 0, dragTrailCount - 1.001f);
        int a = Mathf.FloorToInt(idx);
        float f = idx - a;
        int ia = (dragTrailHead - a + dragTrail.Length) % dragTrail.Length;
        int ib = (ia - 1 + dragTrail.Length) % dragTrail.Length;
        return Vector3.Lerp(dragTrail[ia], dragTrail[ib], f);
    }

    void DragonPush(Vector3 p)
    {
        dragTrailHead = (dragTrailHead + 1) % dragTrail.Length;
        dragTrail[dragTrailHead] = p;
        dragTrailCount = Mathf.Min(dragTrailCount + 1, dragTrail.Length);
    }

    // start coiled around a point (the body spirals up out of the swarm)
    void DragonCoil(Vector3 c)
    {
        dragTrailCount = 0;
        int n = dragTrail.Length;
        for (int k = n - 1; k >= 0; k--)
        {
            float a = k * 0.06f;
            DragonPush(c + new Vector3(Mathf.Cos(a) * 4f, 1.2f + (n - k) * 0.004f, Mathf.Sin(a) * 4f));
        }
        dragLast = dragTrail[dragTrailHead];
    }

    // the head flies to p; wave adds the serpentine side-to-side and up-and-down undulation
    void DragonPath(Vector3 p, float wave = 1f)
    {
        Vector3 mv = p - dragLast;
        if (mv.sqrMagnitude > 1e-4f) dragMove = Vector3.Slerp(dragMove, mv.normalized, 0.2f);
        Vector3 side = Vector3.Cross(Vector3.up, dragMove);
        side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
        Vector3 head = p + (side * Mathf.Sin(t * 2.3f) * 2.6f + Vector3.up * Mathf.Sin(t * 1.6f) * 1.1f) * wave;
        if (head.y < 1f) head.y = 1f;
        // lay trail points every DragonStep so the body follows the same curve
        Vector3 from = dragTrail[dragTrailHead];
        float d = Vector3.Distance(from, head);
        int steps = Mathf.Min(40, Mathf.FloorToInt(d / DragonStep));
        for (int k = 1; k <= steps; k++) DragonPush(Vector3.Lerp(from, head, k * DragonStep / d));
        dragLast = p;
        drag[0] = head;
        float link = DragonLink * DragonScale / DragonStep;
        for (int i = 1; i < drag.Length; i++)
        {
            drag[i] = DragonTrailAt(i * link);
            if (drag[i].y < 0.8f) drag[i].y = 0.8f;
        }
    }

    IEnumerator GreatDragon()
    {
        act = "dragon";
        form = Form.GreatDragon;
        swarm.active = swarm.Count;
        swarm.chipScale = 2.0f;
        camLift = 3f;
        Announce("THE CELESTIAL DRAGON", "FIND COVER  -  WATCH THE FIRE  -  DASH THE DIVE", 2.0f);
        Play("PowerUp", 0.15f);
        director?.Cut(BossDirector.Shot.Low);
        DragonCoil(Flat(pos));
        DragonPath(dragTrail[dragTrailHead], 0);
        swarm.BeginForm(2.2f, true);
        var fx = GameManager.I.fx;
        Vector3 head = drag[0];
        // rise and roar
        for (float x = 0; x < 2.6f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            goal = HoverSpot(9f);
            height = 3f;
            float sa = x * 2.4f;
            Vector3 want = Flat(pos) + new Vector3(Mathf.Cos(sa) * 5f, 3f + x * 3.2f, Mathf.Sin(sa) * 5f);
            head = Vector3.Lerp(head, want, 1 - Mathf.Exp(-4f * dt));
            DragonPath(head, 0.4f);
            dragGape = Mathf.Lerp(dragGape, x > 2f ? 38 : 8, dt * 5);
            if (x > 2.2f && x - dt <= 2.2f)
            {
                fx.Impact(head, 7f, 0.35f);
                fx.Chroma(1f);
                FightCamera.Shake(0.9f);
                Shot("dragon_roar");
            }
            yield return null;
        }
        // circle over Kaito
        float ang = Mathf.Atan2(head.z - hero.transform.position.z, head.x - hero.transform.position.x);
        director?.Cut(BossDirector.Shot.Aerial);
        for (float x = 0; x < 2.4f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            goal = HoverSpot(9f);
            height = 4f;
            ang += 1.2f * dt;
            Vector3 c = HeroFlat + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 13f + Vector3.up * (11f + HeroFloor);
            head = Vector3.MoveTowards(head, c, 24f * dt);
            DragonPath(head);
            dragGape = Mathf.Lerp(dragGape, 8, dt * 4);
            yield return null;
        }
        // fire run along a street through Kaito
        Vector3 run = Flat(HeroFlat - Flat(head));
        run = run.sqrMagnitude > 0.1f ? run.normalized : ToHero();
        Vector3 start = HeroFlat - run * 18f + Vector3.up * (6f + HeroFloor);
        Vector3 end = HeroFlat + run * 18f + Vector3.up * (6f + HeroFloor);
        Announce("DRAGON FIRE", "GET OFF THE RED LINE", 1.0f);
        director?.Cut(BossDirector.Shot.Side);
        for (float x = 0; x < 1.2f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            ShowStrip(start, run, 36f);
            head = Vector3.MoveTowards(head, start, 30f * dt);
            DragonPath(head);
            yield return null;
        }
        bool burned = false;
        for (float x = 0; x < 2.3f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            float u = x / 2.3f;
            head = Vector3.Lerp(start, end, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * 1.5f;
            DragonPath(head, 0.25f);
            head = drag[0];
            dragGape = 48;
            Vector3 mouth = head + run * 1.6f * DragonScale - Vector3.up * 0.4f;
            Vector3 flame = new Vector3(mouth.x, HeroFloor, mouth.z) + run * 3f;
            fx.Embers(mouth, (run * 7f + Vector3.down * 9f), 7, 0.4f);
            if (Random.value < 0.4f) fx.Embers(flame, Vector3.up * 3f, 4, 0.5f);
            if (!burned && Flat(hero.transform.position - flame).magnitude < 2.7f && !TownMap.Blocked(mouth, HeroChest))
            {
                burned = true;
                Strike(flame, 16, 3.5f, 3.5f, false, true, "dragon_fire");
            }
            if (x > 1.0f && x - dt <= 1.0f) Shot("dragon_fire");
            yield return null;
        }
        HideWarn();
        // climb and dive
        Vector3 target = HeroTarget();
        director?.Cut(BossDirector.Shot.Low);
        for (float x = 0; x < 1.1f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            target = Vector3.Lerp(target, HeroTarget(), 1 - Mathf.Exp(-4 * dt));
            ShowDisc(target, 4.5f);
            head = Vector3.MoveTowards(head, target + Vector3.up * 16f, 26f * dt);
            DragonPath(head);
            dragGape = 30;
            yield return null;
        }
        Vector3 from = head;
        for (float x = 0; x < 0.5f; x += Time.deltaTime)
        {
            float u = x / 0.5f;
            head = Vector3.Lerp(from, target + Vector3.up * 0.8f, u * u);
            DragonPath(head, 0);
            yield return null;
        }
        HideWarn();
        fx.Impact(target, 9f, 0.35f);
        fx.Embers(target + Vector3.up * 0.5f, Vector3.up * 4f, 40, 0.6f);
        FightCamera.Shake(1.1f);
        FightCamera.Punch(1f);
        Shot("dragon_dive");
        form = Form.None;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.Burst(target, 9f, 4f);
        swarm.chipScale = 1;
        yield return Shockwave(target, 5f, 18, 5f);
        swarm.active = 5600;
    }

    // ================================================================== INDOORS
    // Moros cannot follow Kaito inside, so the stones come in after him.
    bool RoomFrame(out int room, out Vector3 door, out Vector3 inward, out float depth, out float across)
    {
        room = -1; door = inward = Vector3.zero; depth = across = 0;
        var map = TownMap.I;
        if (map == null) return false;
        room = map.RoomAt(hero.transform.position);
        if (room < 0) return false;
        var m = map.rooms[room];
        Vector3 rc = new Vector3(m.c.x, 0, m.c.y);
        door = map.NearestDoor(room, HeroFlat);
        inward = Flat(rc - door).normalized;
        bool alongX = Mathf.Abs(inward.x) > 0.5f;
        depth = (alongX ? m.half.x : m.half.y) * 2;
        across = alongX ? m.half.y : m.half.x;
        return true;
    }

    // ------------------------------------------------------------------ SHARD GALE
    IEnumerator Gale()
    {
        if (!RoomFrame(out int room, out Vector3 door, out Vector3 inward, out float depth, out float across)) yield break;
        var map = TownMap.I;
        act = "gale";
        form = Form.None;
        Announce("SHARD GALE", "HOLD BLOCK  -  RUN AGAINST THE WIND", 1.4f);
        Play("Summon", 0.15f);
        Vector3 station = door - inward * 4.5f;
        for (float x = 0; x < 1.0f; x += Time.deltaTime) { goal = station; height = 1.2f; yield return null; }
        swarm.active = Mathf.Max(swarm.active, 5600);
        swarm.windDoor = door + Vector3.up * 1.4f;
        swarm.windTo = door + inward * (depth - 0.8f) + Vector3.up * 0.3f;
        swarm.windWidth = Mathf.Clamp(across - 0.6f, 1.5f, 6f);
        swarm.windSpeed = 1f;
        swarm.windFrom = pos + Vector3.up * 1.2f;
        swarm.mode = ShardSwarm.Mode.Wind;
        var fx = GameManager.I.fx;
        for (float x = 0; x < 3.2f; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            goal = station; height = 1.2f;
            swarm.windFrom = pos + Vector3.up * 1.2f;
            if (map.RoomAt(hero.transform.position) == room)
            {
                hero.WindForce(inward * (hero.Blocking ? 1.3f : 3.6f), dt);
                if (!hero.Blocking) hero.Chip(1.4f * dt);
                if (Random.value < 0.25f) fx.Puff(HeroChest - Vector3.up * 0.8f, 0.7f);
            }
            if (x > 1.2f && x - dt <= 1.2f) Shot("gale_push");
            yield return null;
        }
        Announce("THE GALE REVERSES", "DON'T GET DRAGGED OUT!", 1.2f, Gold);
        swarm.windSpeed = -1.5f;
        bool caught = false;
        for (float x = 0; x < 3.2f && !caught; x += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            goal = station; height = 1.2f;
            swarm.windFrom = pos + Vector3.up * 1.2f;
            Vector3 toDoor = Flat(door - hero.transform.position);
            if (map.RoomAt(hero.transform.position) == room || toDoor.magnitude < 3f)
                hero.WindForce(toDoor.normalized * (hero.Blocking ? 2f : 4.3f), dt);
            if (toDoor.magnitude < 1.4f && hero.BossGrab()) caught = true;
            if (x > 1.0f && x - dt <= 1.0f) Shot("gale_pull");
            yield return null;
        }
        swarm.mode = ShardSwarm.Mode.Halo;
        if (caught)
        {
            Announce("DRAGGED OUT!", "", 1.0f, Gold);
            Vector3 a = door + Vector3.up * 0.2f;
            for (float x = 0; x < 0.6f; x += Time.deltaTime)
            {
                Vector3 b = pos + ToHero() * 1.2f - Vector3.up * 0.6f;
                hero.BossHold(Vector3.Lerp(a, b, x / 0.6f));
                yield return null;
            }
            yield return Punish(12);
        }
    }

    // ------------------------------------------------------------------ SEEKER SWARMS
    IEnumerator Seekers()
    {
        if (!RoomFrame(out int room, out Vector3 door, out Vector3 inward, out _, out _)) yield break;
        act = "seekers";
        form = Form.None;
        hunting = true;
        caughtHunter = null;
        delivered = false;
        Announce("SEEKER SWARMS", "THEY SEARCH EVERY ROOM  -  HIDE, OR SMASH THEM", 1.6f);
        Play("Casting", 0.15f);
        Vector3 station = door - inward * 4.5f;
        for (float x = 0; x < 0.9f; x += Time.deltaTime) { goal = station; height = 1.2f; yield return null; }
        int n = ascended ? 8 : 6;
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Seeker" + i);
            var h = go.AddComponent<Hunter>();
            Vector3 start = door + inward * 0.8f + Vector3.up * 1.6f + Random.insideUnitSphere * 0.5f;
            h.InitSeeker(this, hero, shardMats, eyeMat, start, room, i % 3);
            hunters.Add(h);
        }
        Shot("seekers");
        float dur = ascended ? 16 : 14;
        for (float x = 0; x < dur && caughtHunter == null && !Complete; x += Time.deltaTime)
        {
            goal = station; height = 1.2f;
            if (x > 3f && Mathf.Repeat(x, 3f) < Time.deltaTime) Shot("seekers_search");
            yield return null;
        }
        if (caughtHunter != null)
        {
            act = "hunt_carry";
            Announce("CAUGHT!", "THE SEEKERS HAUL YOU OUT", 1.4f, Gold);
            for (float x = 0; x < 9f && !delivered && caughtHunter != null && caughtHunter.state == Hunter.State.Carry; x += Time.deltaTime)
            {
                height = 1.0f;
                goal = Flat(pos);
                yield return null;
            }
            if (delivered) yield return Punish(12);
        }
        foreach (var h in hunters) if (h) h.Dissolve();
        hunters.Clear();
        hunting = false;
    }

    // Kaito is delivered into Moros's reach: an uppercut launches him
    IEnumerator Punish(float damage)
    {
        Play("Uppercut", 0.08f);
        for (float x = 0; x < 0.3f; x += Time.deltaTime) { height = 0.6f; yield return null; }
        Vector3 away = ToHero();
        hero.BossRelease(away, damage, 5f, 4f);
        GameManager.I.fx.Impact(HeroFlat, 4.5f, 0.7f);
        GameManager.I.fx.Splash(HeroChest, away, 0.8f);
        FightCamera.Shake(0.7f);
        Shot("punish");
    }

    // ================================================================== Kaito's powers
    public bool IsTitan => form == Form.Titan;
    public bool WeaponOut => form != Form.None && form != Form.Titan && form != Form.Arsenal;
    public bool Ended => dying || Complete;
    public bool Seized => seized;
    public Vector3 BodyPoint => form == Form.Titan ? tp.feet + Vector3.up * 5.5f * TitanScale : (hips ? hips.position : pos + Vector3.up);
    public Vector3 TitanChest => ShardForms.TitanChestPoint(tp);
    bool InCinematic => act == "rise" || act == "reveal" || (act == "ascension" && form != Form.Titan);
    public bool CanBeInterrupted => !dying && !Complete && !InCinematic;

    void Kill()
    {
        StopAllCoroutines();
        EndForms();
        StartCoroutine(Death());
    }

    // everything he was doing falls apart; the titan releases him
    void EndForms()
    {
        bool wasTitan = form == Form.Titan;
        Cleanup();
        ammo?.Clear();
        swarm.reservoirStorm = false;
        swarm.formStretch = 1;
        swarm.mode = ShardSwarm.Mode.Free;
        swarm.chipScale = 1;
        if (wasTitan || !model.activeSelf) { model.SetActive(true); swarm.active = 5600; }
        invulnerable = false;
        seized = false;
        if (hero.inputLocked && !Complete)
        {
            hero.inputLocked = false;
            var cam = GameManager.I.cam;
            cam.b = proxy;
            cam.SetMode(FightCamera.Mode.Fight);
        }
        TimeFX.slow = 1;
    }

    public void Interrupt(string title, Vector3 at)
    {
        if (!CanBeInterrupted) return;
        StopAllCoroutines();
        showBusy = true;
        StartCoroutine(Interrupted(title, at));
    }

    IEnumerator Interrupted(string title, Vector3 at)
    {
        EndForms();
        swarm.Burst(at, 8, 3, 8);
        GameManager.I.fx.Impact(at, 5f, 0.6f);
        FightCamera.Shake(0.6f);
        Announce(title, "HE'S OPEN  -  PUNISH HIM", 1.4f, Gold);
        Shot("interrupted");
        act = "stagger";
        Play("Stagger", 0.05f);
        for (float x = 0; x < 0.7f; x += Time.deltaTime) { goal = Flat(pos); height = 0; yield return null; }
        act = "open";
        open = true;
        windowHits = 0;
        knockedBack = false;
        Play("Idle", 0.2f);
        for (float x = 0; x < 2.8f && !knockedBack; x += Time.deltaTime) { height = 0; yield return null; }
        open = false;
        if (knockedBack) yield return Stagger();
        if (ShowcaseMode) showBusy = false; else yield return Loop();
    }

    // THUNDER PALM: a lightning shockwave through the ground around Kaito
    public void ThunderPalm(Vector3 c, float radius)
    {
        if (Ended) return;
        int broke = 0;
        foreach (var h in hunters)
            if (h != null && h.state != Hunter.State.Broken && h.state != Hunter.State.Delivered && Flat(h.Head - c).magnitude < radius)
            {
                h.Break(h.Head);
                broke++;
            }
        int shots = ammo != null ? ammo.ClearWithin(c, radius + 2) : 0;
        var fx = GameManager.I.fx;
        float d = Flat(BodyPoint - c).magnitude;
        if (form == Form.Titan)
        {
            if (d < radius + 5)
            {
                hp = Mathf.Max(Mathf.Min(hp, HpFloor), hp - 14);
                TitanKnock(Flat(tp.feet - c), 0.6f);
                fx.Crack(TitanChest, 0.6f);
                Announce("THE TITAN REELS", "", 0.9f, Gold);
                if (hp <= 0) Kill();
            }
            return;
        }
        bool weapon = NearWeapon(c, radius);
        if (d < radius) hp = Mathf.Max(Mathf.Min(hp, HpFloor), hp - Mathf.Round(12 * (open ? 1.5f : 1f)));
        if (hp <= 0) { Kill(); return; }
        bool busy = act != "hover" && act != "open" && act != "stagger" && act != "descend" && act != "melee";
        if (weapon || (d < radius && busy) || (broke > 0 && hunting) || act == "gale")
            Interrupt(broke > 0 ? "SWARMS SHATTERED!" : weapon ? "WEAPON SHATTERED!" : "THUNDER PALM!", weapon ? WeaponPoint() : BodyPoint);
        else if (d < radius) { Play("Hit", 0.05f); fx.Crack(BodyPoint, 0.9f); }
        else if (shots > 0) Announce("SHOTS DESTROYED", "", 0.8f, Gold);
    }

    // GUARDIAN SPIRIT: each of the giant's punches
    public void SpiritPunch(Vector3 fist, float dmg, bool final)
    {
        if (Ended) return;
        hp = Mathf.Max(Mathf.Min(hp, HpFloor), hp - dmg);
        var fx = GameManager.I.fx;
        fx.Impact(fist, 5.5f, 0.5f);
        fx.Pulse(fist, 6f, Spirit);
        fx.Energy(fist, Vector3.up, 40, Spirit, Color.white, 4f, 14f);
        TimeFX.HitStop(0.09f);
        FightCamera.Shake(0.3f);
        FightCamera.Punch(0.25f);
        if (form == Form.Titan)
        {
            TitanKnock(Flat(tp.feet - HeroFlat), 0.8f);
        }
        else
        {
            swarm.Burst(fist, 6, 2, 6);
            Play("Hit", 0.03f);
            goal = Flat(pos) + Flat(pos - HeroFlat).normalized * 0.8f;
        }
        if (hp <= 0) { Kill(); return; }
        if (final) Interrupt(form == Form.Titan ? "THE TITAN CRUMBLES!" : "SPIRIT'S JUDGEMENT!", fist);
    }

    // BLADE HAND AWAKENING: pin him, take the beating, send him flying
    public bool Seize()
    {
        if (!CanBeInterrupted) return false;
        StopAllCoroutines();
        EndForms();
        swarm.Burst(BodyPoint, 1.5f, 0.5f, 8);   // the weapon just drops
        seized = true;
        showBusy = true;
        act = "seized";
        open = false;
        goal = Flat(pos);
        height = 0;
        Play("Stagger", 0.05f);
        return true;
    }

    public void AwakeningHit(Vector3 point, float dmg, Vector3 dir)
    {
        if (Ended) return;
        hp = Mathf.Max(Mathf.Min(hp, HpFloor), hp - dmg);
        var fx = GameManager.I.fx;
        fx.Crack(point, 1.1f);
        fx.Energy(point, dir, 26, Spirit, Color.white, 4f, 12f);
        fx.Pulse(point, 2.4f, Spirit);
        fx.Splash(point, dir, 0.3f);
        TimeFX.HitStop(0.06f);
        FightCamera.Shake(0.1f);
        Play("Hit", 0.03f);
        goal = Flat(pos) + Flat(dir) * 0.18f;
        if (hp <= 0) Kill();
    }

    public void ReleaseSeize(Vector3 dir)
    {
        if (!seized) return;
        seized = false;
        if (Ended) return;
        StopAllCoroutines();
        StartCoroutine(AfterAwakening(dir));
    }

    IEnumerator AfterAwakening(Vector3 dir)
    {
        act = "stagger";
        Play("Fall", 0.05f);
        Vector3 to = Flat(pos) + Flat(dir).normalized * 6f;
        if (TownMap.I != null) to = TownMap.I.Resolve(to, 0.8f);
        for (float x = 0; x < 0.9f; x += Time.deltaTime) { goal = to; height = Mathf.Sin(x / 0.9f * Mathf.PI) * 2.2f; yield return null; }
        GameManager.I.fx.Impact(Flat(pos), 4f, 0.6f);
        FightCamera.Shake(0.5f);
        for (float x = 0; x < 0.8f; x += Time.deltaTime) { height = 0; yield return null; }
        act = "open";
        open = true;
        windowHits = 0;
        knockedBack = false;
        Play("Idle", 0.2f);
        Announce("HE'S OPEN!", "STRIKE NOW", 1f, Gold);
        for (float x = 0; x < 2.2f && !knockedBack; x += Time.deltaTime) { height = 0; yield return null; }
        open = false;
        if (knockedBack) yield return Stagger();
        if (ShowcaseMode) showBusy = false; else yield return Loop();
    }

    // STONE GAUNTLETS: a punch that lands on a weapon shatters it; on the titan's legs it cracks it
    Form ageForm = Form.None;
    float formStartT;
    float FormAge => ageForm == form ? t - formStartT : 0;
    void TrackFormAge() { if (form != ageForm) { ageForm = form; formStartT = t; } }
    Form crackForm = Form.None;
    int formCracks;

    bool CrushWeapon(Vector3 p, float r)
    {
        if (form == Form.Titan)
        {
            if (Flat(p - tp.feet).magnitude < 2.8f && p.y < 4.5f)
            {
                hp = Mathf.Max(Mathf.Min(hp, HpFloor), hp - 5);
                titanCracks++;
                GameManager.I.fx.Crack(p, 0.7f);
                GameManager.I.fx.Energy(p, Vector3.up, 16, new Color(0.8f, 0.75f, 0.7f), Color.white, 3f, 8f);
                TimeFX.HitStop(0.05f);
                if (titanCracks == 1) Announce("THE TITAN CRACKS", "KEEP SMASHING ITS LEGS", 1.1f, Gold);
                if (hp <= 0) Kill();
                else if (titanCracks >= 8) { titanCracks = 0; Interrupt("THE TITAN CRUMBLES!", p); }
                return true;
            }
            return false;
        }
        if (form == Form.None || form == Form.Arsenal) return false;
        if (!NearWeapon(p, r + 0.3f)) return false;
        hp = Mathf.Max(Mathf.Min(hp, HpFloor), hp - 6);
        if (hp <= 0) { Kill(); return true; }
        // the grand formations take a beating before they break: each Stone Fist punch cracks them
        bool grand = form == Form.Cobra || form == Form.Sphere || form == Form.Wall || form == Form.Drill || form == Form.GreatDragon;
        if (grand && FormAge < 5f)
        {
            // still assembling: the stones just spark off it
            GameManager.I.fx.Energy(p, Vector3.up, 8, new Color(0.8f, 0.75f, 0.7f), Color.white, 2f, 6f);
            return true;
        }
        if (grand)
        {
            if (crackForm != form) { crackForm = form; formCracks = 0; }
            formCracks++;
            GameManager.I.fx.Crack(p, 0.7f);
            GameManager.I.fx.Energy(p, Vector3.up, 18, new Color(0.8f, 0.75f, 0.7f), Color.white, 3f, 9f);
            ActionFX.DustRing(new Vector3(p.x, 0.05f, p.z), 1.2f, 8, 1.2f);
            TimeFX.HitStop(0.05f);
            if (formCracks == 1) Announce("IT CRACKS", "KEEP SMASHING IT", 1f, Gold);
            if (formCracks < 6) return true;
            formCracks = 0;
        }
        Interrupt("WEAPON SHATTERED!", p);
        return true;
    }

    bool NearWeapon(Vector3 p, float r)
    {
        switch (form)
        {
            case Form.Axe: return SegDist(p, axGrip - axDir * 0.5f, axGrip + axDir * (AxLen + 0.5f)) < r + 0.9f;
            case Form.Hand: return SegDist(p, hArm, hWrist + hFingers * 1.9f * hScale) < r + 0.9f;
            case Form.Lances:
                for (int i = 0; i < lCount; i++) if (SegDist(p, lTail[i], lTip[i]) < r + 0.5f) return true;
                return false;
            case Form.Hammer: return SegDist(p, hmGrip, hmGrip + hmHandle * 3.4f * HmScale) < r + 1.4f;
            case Form.Roc: return (p - rocC).magnitude < r + 3f;
            case Form.Dragon: return (p - swarm.head).magnitude < r + 1.2f;
            case Form.Gatling: return (p - gunL).magnitude < r + 1.4f || (p - gunR).magnitude < r + 1.4f;
            case Form.Rockets: return (p - podC).magnitude < r + 1.6f;
            case Form.GreatDragon:
                for (int i = 0; i < drag.Length; i++) if ((p - drag[i]).magnitude < r + 1.6f) return true;
                return false;
            case Form.Sphere: case Form.Cobra: case Form.Drill: case Form.Wall: return NearFormation(p, r);
        }
        return false;
    }

    Vector3 WeaponPoint()
    {
        switch (form)
        {
            case Form.Axe: return axGrip + axDir * AxLen * 0.7f;
            case Form.Hand: return hWrist;
            case Form.Lances: return lCount > 0 ? lTip[0] : pos;
            case Form.Hammer: return hmGrip + hmHandle * 2.9f * HmScale;
            case Form.Roc: return rocC;
            case Form.Dragon: return swarm.head;
            case Form.Gatling: return (gunL + gunR) * 0.5f;
            case Form.Rockets: return podC;
            case Form.GreatDragon: return drag[0];
            case Form.Sphere: case Form.Cobra: case Form.Drill: case Form.Wall: return FormationPoint();
        }
        return BodyPoint;
    }

    // ================================================================== per-frame helpers for the new forms
    void BuildArsenalForm(ShardSwarm s)
    {
        switch (form)
        {
            case Form.Gatling:
                ShardForms.Gatling(s.parts, gunL, gunDir, GunScale, gunSpin);
                ShardForms.Gatling(s.parts, gunR, gunDir, GunScale, -gunSpin);
                break;
            case Form.Rockets: ShardForms.RocketPod(s.parts, podC, podDir, PodScale, podLoaded); break;
            case Form.GreatDragon: ShardForms.ChineseDragon(s.parts, drag, drag.Length, DragonScale, dragGape, t, out eyeL, out eyeR); break;
            case Form.Sphere: case Form.Cobra: case Form.Drill: case Form.Wall: BuildFormation(s); break;
            case Form.Arsenal:
                ShardForms.Gatling(s.parts, gunL, gunDir, GunScale, gunSpin);
                ShardForms.Gatling(s.parts, gunR, gunDir, GunScale, -gunSpin);
                ShardForms.RocketPod(s.parts, podC, podDir, PodScale, podLoaded);
                break;
        }
    }

    void FrameArsenal(ref Bounds b)
    {
        switch (form)
        {
            case Form.Gatling:
                b.Encapsulate(gunL); b.Encapsulate(gunR);
                b.Encapsulate(ShardForms.GatlingMuzzle(gunL, gunDir, GunScale));
                break;
            case Form.Rockets: b.Encapsulate(new Bounds(podC, Vector3.one * 2.5f)); break;
            case Form.GreatDragon:
                b.Encapsulate(new Bounds(drag[0], Vector3.one * 4f));
                b.Encapsulate(drag[8]);
                break;
            case Form.Sphere: case Form.Cobra: case Form.Drill: case Form.Wall: FrameFormation(ref b); break;
            case Form.Arsenal:
                b.Encapsulate(gunL); b.Encapsulate(gunR); b.Encapsulate(podC + Vector3.up);
                break;
        }
    }
}
