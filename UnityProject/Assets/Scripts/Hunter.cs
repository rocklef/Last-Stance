using UnityEngine;

// One of Moros's hunting swarms: a serpent of stone chips with ember eyes that roams the temple
// town looking for Kaito. It flies along the streets and over the rooftops when a building is in
// the way, and it can only see him in a clear line of sight (TownMap solids block it) - so Kaito
// can break away down an alley or hide behind a house. Once it spots him it gives chase; when it
// reaches him it closes into a stone hand, grabs him and drags him back to Moros.
public class Hunter : MonoBehaviour
{
    public enum State { Roam, Chase, Search, Carry, Delivered, Broken }
    public State state = State.Roam;
    public Vector3 Head => head;
    public bool Seeing { get; private set; }

    ShardSwarm sw;
    MonkBoss boss;
    Fighter hero;
    Vector3 head, dir = Vector3.forward, waypoint, lastSeen;
    float stateT, lookT, speedMul = 1, lungeCooldown, phase, releaseT = 2.6f;
    Transform[] eyes = new Transform[2];
    // SEEKERS: small buzzing clusters Moros sends into buildings; they search room by room and drag
    // Kaito out through the nearest door
    bool seeker;
    int room = -1;
    bool pastDoor;
    // carry hand
    Vector3 hWrist, hPalm = Vector3.forward;
    float hCurl;
    const float HandScale = 1.4f;

    public void Init(MonkBoss owner, Fighter target, Material[] mats, Material eyeMat, Vector3 start, int shards, float speed)
    {
        boss = owner;
        hero = target;
        speedMul = speed;
        phase = Random.value * 10f;
        head = start;
        sw = gameObject.AddComponent<ShardSwarm>();
        sw.Init(mats, shards, start, 0.5f);
        sw.boundRadius = 110;
        sw.trailStep = 0.055f;
        sw.Gather(start, 0.6f);
        sw.mode = ShardSwarm.Mode.Serpent;
        sw.head = head;
        sw.ResetTrail(head);
        sw.builder = Build;
        sw.chipScale = 1.05f;
        for (int i = 0; i < 2; i++)
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(e.GetComponent<Collider>());
            e.GetComponent<Renderer>().sharedMaterial = eyeMat;
            e.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            e.transform.localScale = new Vector3(0.12f, 0.07f, 0.12f);
            eyes[i] = e.transform;
        }
        // fan out: the first waypoint is a street away from Moros
        waypoint = TownMap.I != null ? TownMap.I.RandomStreetPoint() : start + Random.insideUnitSphere * 12f;
        dir = (new Vector3(waypoint.x - start.x, 3f, waypoint.z - start.z)).normalized;
        stateT = 0;
    }

    public void InitSeeker(MonkBoss owner, Fighter target, Material[] mats, Material eyeMat, Vector3 start, int roomIndex, int shape)
    {
        seeker = true;
        room = roomIndex;
        boss = owner;
        hero = target;
        speedMul = 1;
        phase = Random.value * 10f;
        head = start;
        releaseT = 1.2f;
        sw = gameObject.AddComponent<ShardSwarm>();
        sw.Init(mats, 260, start, 0.4f);
        sw.boundRadius = 110;
        sw.Gather(start, 0.4f);
        sw.mode = ShardSwarm.Mode.Cluster;
        sw.clusterShape = shape;
        sw.clusterRadius = shape == 1 ? 0.55f : 0.45f;
        sw.head = head;
        sw.chipScale = 0.85f;
        for (int i = 0; i < 2; i++)
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(e.GetComponent<Collider>());
            e.GetComponent<Renderer>().sharedMaterial = eyeMat;
            e.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            eyes[i] = e.transform;
        }
        waypoint = RoomPoint();
        dir = (waypoint - start).normalized;
        stateT = 0;
    }

    Vector3 RoomPoint()
    {
        var map = TownMap.I;
        if (map == null || room < 0) return hero.transform.position;
        var m = map.rooms[room];
        for (int k = 0; k < 12; k++)
        {
            var p = new Vector3(m.c.x + Random.Range(-m.half.x + 1, m.half.x - 1), 0, m.c.y + Random.Range(-m.half.y + 1, m.half.y - 1));
            if (!map.InsideSolid(p + Vector3.up * 0.5f, 0.4f)) return p;
        }
        return new Vector3(m.c.x, 0, m.c.y);
    }

    void OnDestroy()
    {
        foreach (var e in eyes) if (e) Destroy(e.gameObject);
    }

    void Build(ShardSwarm s)
    {
        if (state == State.Carry) ShardForms.Hand(s.parts, hWrist, Vector3.up, hPalm, HandScale, hCurl, hWrist);
    }

    Vector3 HeroChest => hero.Body + Vector3.up * 1.1f;

    void PickWaypoint()
    {
        var map = TownMap.I;
        if (seeker && map != null && room >= 0)
        {
            // search the rooms; sometimes check near where Kaito might be
            waypoint = Random.value < 0.5f ? RoomPoint() : hero.transform.position + Random.insideUnitSphere * 4f;
            waypoint.y = 0;
            stateT = 0;
            return;
        }
        // mostly search around where Kaito could be, sometimes sweep a random street
        Vector3 around = hero.transform.position + Random.insideUnitSphere * 14f;
        around.y = 0;
        waypoint = map != null && (Random.value < 0.4f || !TownMap.Walkable(around)) ? map.RandomStreetPoint() : around;
        stateT = 0;
    }

    // showcase: this swarm spots Kaito now
    public void ForceChase()
    {
        if (state != State.Roam && state != State.Search) return;
        state = State.Chase;
        stateT = 0;
        releaseT = 0;
        lastSeen = hero.transform.position;
        Seeing = true;
        boss.OnHunterSpotted(this);
    }

    public void Break(Vector3 at)
    {
        if (state == State.Broken || state == State.Delivered) return;
        if (state == State.Carry && hero.bossHeld) hero.BossRelease(Vector3.zero, 0, 2f, 0);
        state = State.Broken;
        stateT = 0;
        sw.mode = ShardSwarm.Mode.Free;
        sw.Burst(at, 6, 2.5f, 4);
        GameManager.I.fx.Crack(at, 0.8f);
        foreach (var e in eyes) if (e) e.gameObject.SetActive(false);
    }

    public void Dissolve()
    {
        if (state == State.Carry && hero.bossHeld) hero.BossRelease(Vector3.zero, 0, 2f, 0);
        state = State.Broken;
        sw.mode = ShardSwarm.Mode.Free;
        sw.Burst(head, 2, 1, 5);
        foreach (var e in eyes) if (e) e.gameObject.SetActive(false);
        Destroy(gameObject, 6f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0 || hero == null) return;
        stateT += dt;
        lungeCooldown -= dt;
        Vector3 chest = HeroChest;
        switch (state)
        {
            case State.Roam:
            case State.Search:
            {
                Vector3 target = state == State.Search ? lastSeen : waypoint;
                target.y = 0;
                Vector3 flat = new Vector3(head.x, 0, head.z);
                bool blocked = TownMap.Blocked(head, target + Vector3.up * 2f);
                float cruise = seeker ? 1.7f + Mathf.Sin(Time.time * 2.3f + phase) * 0.5f
                             : blocked ? 10f : 2.4f + Mathf.Sin(Time.time * 1.7f + phase) * 0.8f;
                Vector3 to = new Vector3(target.x, cruise, target.z) - head;
                Steer(to, (seeker ? 5.5f : 6.5f) * speedMul, seeker ? 3.2f : 2.2f, dt);
                if ((flat - target).magnitude < 2.5f || stateT > 9f)
                {
                    if (state == State.Search) state = State.Roam;
                    PickWaypoint();
                }
                // look for Kaito (not while the swarms are still fanning out over the town)
                releaseT -= dt;
                lookT -= dt;
                if (lookT <= 0 && releaseT <= 0)
                {
                    lookT = 0.15f;
                    float d = Vector3.Distance(head, chest);
                    // up on the rooftops Kaito is hidden unless a swarm flies right over him
                    bool hidden = hero.OnRoof && d > 4f;
                    Seeing = d < (seeker ? 9f : 14f) && !hidden && !TownMap.Blocked(head, chest) && !hero.IsDown;
                    if (Seeing)
                    {
                        state = State.Chase;
                        stateT = 0;
                        lastSeen = hero.transform.position;
                        boss.OnHunterSpotted(this);
                    }
                }
                break;
            }
            case State.Chase:
            {
                lookT -= dt;
                if (lookT <= 0)
                {
                    lookT = 0.12f;
                    Seeing = !TownMap.Blocked(head, chest) && Vector3.Distance(head, chest) < 20f && !(hero.OnRoof && Vector3.Distance(head, chest) > 6f);
                    if (Seeing) lastSeen = hero.transform.position;
                }
                if (!Seeing && stateT > 0.6f)
                {
                    // lost him around a corner: go to where he was last seen
                    state = State.Search;
                    stateT = 0;
                    break;
                }
                Vector3 aim = chest + new Vector3(hero.transform.forward.x, 0, hero.transform.forward.z) * 0.4f;
                Steer(aim - head, (seeker ? 8.5f : 10.5f) * speedMul, seeker ? 6f : 5f, dt);
                if (lungeCooldown <= 0 && Vector3.Distance(head, chest) < 1.3f)
                {
                    if (hero.state == Fighter.State.Dash)
                    {
                        hero.SwarmStrike(head, 0, 0, 0);   // EVADE!
                        lungeCooldown = 1.4f;
                        dir = -dir;
                    }
                    else if (hero.BossGrab())
                    {
                        state = State.Carry;
                        stateT = 0;
                        hPalm = new Vector3(dir.x, 0, dir.z).normalized;
                        if (hPalm.sqrMagnitude < 0.01f) hPalm = Vector3.forward;
                        hWrist = chest - hPalm * 0.5f * HandScale - Vector3.up * 0.55f * HandScale;
                        hCurl = 0;
                        pastDoor = !seeker || TownMap.I == null || TownMap.I.RoomAt(hero.transform.position) < 0;
                        if (seeker) { sw.clusterShape = 0; sw.clusterRadius = 0.85f; hWrist = chest; }
                        else { sw.BeginForm(0.25f); sw.chipScale = 1.25f; }
                        boss.OnHunterCaught(this);
                    }
                    else lungeCooldown = 0.8f;
                }
                break;
            }
            case State.Carry when seeker:
            {
                // the cluster swallows him and drags him out through the door, then to Moros
                Vector3 goal = boss.DropPoint + Vector3.up * 1.6f;
                if (!pastDoor)
                {
                    int ri = TownMap.I.RoomAt(hWrist);
                    if (ri < 0) pastDoor = true;
                    else
                    {
                        Vector3 door = TownMap.I.NearestDoor(ri, hWrist);
                        var m = TownMap.I.rooms[ri];
                        Vector3 outDir = new Vector3(door.x - m.c.x, 0, door.z - m.c.y).normalized;
                        goal = door + outDir * 2.5f + Vector3.up * 1.4f;
                    }
                }
                float sp = stateT < 0.35f ? 0 : 6.5f;
                Vector3 to2 = goal - hWrist;
                if (to2.magnitude > 0.1f) hWrist += Vector3.ClampMagnitude(to2, sp * dt);
                hero.BossHold(hWrist - Vector3.up * 1.05f);
                head = hWrist;
                sw.head = hWrist;
                if (pastDoor && stateT > 0.6f && new Vector3(boss.DropPoint.x - hWrist.x, 0, boss.DropPoint.z - hWrist.z).magnitude < 1.4f)
                {
                    state = State.Delivered;
                    stateT = 0;
                    boss.OnHunterDelivered(this);
                }
                break;
            }
            case State.Carry:
            {
                // close the fist, lift him, and fly him back to Moros
                hCurl = Mathf.MoveTowards(hCurl, 1, dt * 4);
                Vector3 home = boss.DropPoint;
                Vector3 to = home + Vector3.up * 2.6f - hWrist;
                float sp = stateT < 0.4f ? 0 : 9f * speedMul;
                if (to.magnitude > 0.1f) hWrist += Vector3.ClampMagnitude(to, sp * dt);
                if (stateT < 0.4f) hWrist += Vector3.up * dt * 3f;
                // over rooftops if a building is in the way
                if (TownMap.Blocked(hWrist, home + Vector3.up * 2f) && hWrist.y < 9f) hWrist += Vector3.up * dt * 7f;
                Vector3 hold = ShardForms.PalmCentre(hWrist, Vector3.up, HandScale) + hPalm * 0.42f * HandScale;
                hero.BossHold(hold - Vector3.up * 1.05f);
                head = hold;
                if (stateT > 0.6f && new Vector3(to.x, 0, to.z).magnitude < 1.2f)
                {
                    state = State.Delivered;
                    stateT = 0;
                    boss.OnHunterDelivered(this);
                }
                break;
            }
            case State.Delivered:
            case State.Broken:
                break;
        }
        if (state != State.Carry && state != State.Broken && state != State.Delivered)
        {
            if (head.y < 0.6f) head.y = 0.6f;
            sw.head = head;
        }
        // ember eyes on the serpent's head
        bool eyesOn = state == State.Roam || state == State.Chase || state == State.Search;
        Vector3 f = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
        for (int i = 0; i < 2; i++)
        {
            if (eyes[i] == null) continue;
            if (eyes[i].gameObject.activeSelf != eyesOn) eyes[i].gameObject.SetActive(eyesOn);
            eyes[i].position = head + f * (seeker ? 0.25f : 0.3f) + Vector3.up * (seeker ? 0.08f : 0.18f) + r * (i == 0 ? -0.12f : 0.12f) * (seeker ? 0.8f : 1.5f);
            float glow = (state == State.Chase ? 0.18f : 0.12f) * (seeker ? 0.7f : 1f);
            eyes[i].localScale = new Vector3(glow, glow * 0.6f, glow);
        }
    }

    void Steer(Vector3 to, float speed, float turn, float dt)
    {
        if (to.sqrMagnitude > 1e-4f)
        {
            Vector3 want = to.normalized;
            dir = Vector3.Slerp(dir.sqrMagnitude > 1e-4f ? dir.normalized : want, want, 1 - Mathf.Exp(-turn * dt));
        }
        head += dir * speed * dt;
        var map = TownMap.I;
        if (map != null)
        {
            head.x = Mathf.Clamp(head.x, map.min.x - 2, map.max.x + 2);
            head.z = Mathf.Clamp(head.z, map.min.y - 2, map.max.y + 8);
        }
        head.y = Mathf.Clamp(head.y, 0.6f, 12f);
        if (seeker && map != null)
        {
            int r = map.RoomAt(new Vector3(head.x, 0.5f, head.z));
            if (r >= 0) head.y = Mathf.Min(head.y, map.rooms[r].ceiling - 0.6f);
        }
    }
}
