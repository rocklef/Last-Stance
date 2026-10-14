using UnityEngine;

// Tekken-style 2.5D camera: sits side-on to the line between the fighters, pulls back as they
// separate, orbits as they sidestep, and adds shake / zoom punches on big hits.
// Boss fights switch to a framing camera: it keeps Kaito, the boss and the boss's weapon (a 6 m
// sword, a stone colossus...) fully on screen, and never leaves the open courtyard - it widens the
// lens instead of backing up behind the hall, the walls, the trees or the bamboo.
[DefaultExecutionOrder(100)]
public class FightCamera : MonoBehaviour
{
    public Fighter a, b;
    public enum Mode { Intro, Fight, KO, Victory, Roc }
    public Mode mode = Mode.Intro;
    public Fighter focus;
    public Transform roc;
    [System.NonSerialized] public float lift;   // raise the fight framing for a floating boss
    [System.NonSerialized] public bool bossFrame;      // frame frameBounds instead of just the two fighters
    [System.NonSerialized] public Bounds frameBounds;  // everything that must stay on screen (set by MonkBoss)
    [System.NonSerialized] public BossDirector director; // auto-cutting boss camera (shots, occlusion)

    // open courtyard the camera may occupy: inside the ring of lanterns / trees / bamboo, in front
    // of the hall steps and behind the torii
    const float SafeRadius = 10.5f, HallZ = 11f, GateZ = -13f, BossFov = 50f;
    Vector3 bossSide;
    float fovNow = -1, fovWant = 50, fightFov = 38;

    Camera cam;
    Vector3 side = Vector3.back;
    Vector3 vel;
    float shakeAmp, shakeT;
    float punch;
    float modeT;
    float baseFov = 38;

    static FightCamera inst;
    public static float koDist = 3.2f, koHeight = 0.9f;   // the showcase pulls the knockout orbit back

    void Awake()
    {
        inst = this;
        cam = GetComponent<Camera>();
        baseFov = cam.fieldOfView;
    }

    // The boss fight is full of huge impacts; at full strength the shake made the action hard to
    // follow, so there it is scaled down and capped, and during a hero power it is almost still.
    static float calmUntil;
    public static void Calm(float seconds) => calmUntil = Mathf.Max(calmUntil, Clock.Now + seconds);
    static float ShakeScale => (inst == null ? 1 : (Clock.Now < calmUntil ? 0.15f : inst.bossFrame ? 0.4f : 1f)) * (MobileControls.On ? 0.5f : 1f);   // a phone in the hands shakes enough
    // cutscene shots (Cine.cs): while one is being fed, it owns the camera
    static Vector3 cinePos, cineLook;
    static float cineFov, cineUntil = -1;
    public static void Cine(Vector3 pos, Vector3 look, float fov)
    {
        cinePos = pos; cineLook = look; cineFov = fov;
        cineUntil = Clock.Now + 0.12f;
    }

    public static void Shake(float amp)
    {
        if (!inst) return;
        float k = ShakeScale;
        amp = Mathf.Min(amp * k, k < 1 ? 0.22f : 99f);
        inst.shakeAmp = Mathf.Max(inst.shakeAmp * Mathf.Exp(-inst.shakeT * 7f), amp);
        inst.shakeT = 0;
    }
    public static void Punch(float amount) { if (inst) inst.punch = Mathf.Max(inst.punch, amount * Mathf.Min(1f, ShakeScale * 1.25f)); }

    public void SetMode(Mode m, Fighter f = null)
    {
        mode = m;
        focus = f;
        modeT = 0;
    }

    public void SetRoc(Transform target)
    {
        roc = target;
        SetMode(Mode.Roc);
    }

    void LateUpdate()
    {
        if (Clock.Now < cineUntil)
        {
            // keep the shot out of walls and trees
            Vector3 p = cinePos, d = cinePos - cineLook;
            if (d.sqrMagnitude > 0.01f && Physics.SphereCast(cineLook, 0.25f, d.normalized, out var hit, d.magnitude, TownMap.SolidMask, QueryTriggerInteraction.Ignore))
                p = cineLook + d.normalized * Mathf.Max(0.8f, hit.distance - 0.2f);
            transform.position = p;
            if ((cineLook - p).sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(cineLook - p);
            fovNow = cineFov;
            cam.fieldOfView = cineFov;
            vel = Vector3.zero;
            shakeT += Clock.Dt;
            float a = shakeAmp * Mathf.Exp(-shakeT * 7f);
            if (a > 0.001f)
            {
                float st = Clock.Now * 38f;
                transform.position += transform.right * (Mathf.PerlinNoise(st, 0.3f) - 0.5f) * a + transform.up * (Mathf.PerlinNoise(0.7f, st) - 0.5f) * a;
            }
            return;
        }
        float dt = Clock.Dt;
        modeT += dt;
        Vector3 pa = a.Body, pb = b.Body;
        Vector3 mid = (pa + pb) * 0.5f;
        mid.y = 0;
        Vector3 axis = pb - pa; axis.y = 0;
        float sep = axis.magnitude;
        if (sep > 0.01f)
        {
            Vector3 n = Vector3.Cross(Vector3.up, axis / sep);
            if (Vector3.Dot(n, side) < 0) n = -n;   // stay on the same side, never flip
            side = Vector3.Slerp(side, n, 1 - Mathf.Exp(-4f * dt)).normalized;
        }

        Vector3 pos, look;
        bool boss = bossFrame && (mode == Mode.Fight || mode == Mode.Roc);
        if (boss && director != null) { director.Tick(dt); fovWant = director.fov; }
        else if (boss) BossCam(dt);
        else switch (mode)
        {
            case Mode.Intro:
            {
                // crane down from a high shot over the courtyard to the fighters
                float k = Ease.InOut(Mathf.Clamp01(modeT / 3.2f));
                Vector3 start = mid + side * 14f + Vector3.up * 7.5f + Vector3.Cross(side, Vector3.up) * 6f;
                Vector3 end = mid + side * 5.2f + Vector3.up * 1.55f;
                pos = Vector3.Lerp(start, end, k);
                look = Vector3.Lerp(mid + Vector3.up * 3f + Vector3.Cross(Vector3.up, side) * -4f, mid + Vector3.up * 1.15f, k);
                transform.position = pos;
                transform.rotation = Quaternion.LookRotation(look - pos);
                break;
            }
            case Mode.KO:
            {
                Vector3 f = focus ? focus.Body : mid;
                Vector3 orbit = Quaternion.AngleAxis(20 + modeT * 12f, Vector3.up) * side;
                pos = f + orbit * koDist + Vector3.up * koHeight;
                look = f + Vector3.up * 0.5f;
                transform.position = Vector3.SmoothDamp(transform.position, pos, ref vel, 0.25f, 50f, dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look - transform.position), 1 - Mathf.Exp(-8 * dt));
                break;
            }
            case Mode.Victory:
            {
                Vector3 f = focus ? focus.Body : mid;
                Vector3 fwd = focus ? focus.transform.forward : side;
                Vector3 orbit = Quaternion.AngleAxis(Mathf.Sin(modeT * 0.3f) * 25f, Vector3.up) * (fwd * 0.85f + side * 0.5f).normalized;
                pos = f + orbit * 3.0f + Vector3.up * 1.2f;
                look = f + Vector3.up * 1.3f;
                transform.position = Vector3.SmoothDamp(transform.position, pos, ref vel, 0.5f, 50f, dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look - transform.position), 1 - Mathf.Exp(-4 * dt));
                break;
            }
            case Mode.Roc:
            {
                Vector3 h = a != null ? a.Body : Vector3.zero;
                Vector3 r = roc ? roc.position : h + Vector3.up * 6f;
                Vector3 target = Vector3.Lerp(h + Vector3.up * 1.1f, r + Vector3.up * .7f, .62f);
                pos = h + side * 8.2f + Vector3.up * 3.1f;
                transform.position = Vector3.SmoothDamp(transform.position, pos, ref vel, .35f, 50f, dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(target - transform.position), 1 - Mathf.Exp(-5 * dt));
                break;
            }
            default:
            {
                float dist = Mathf.Clamp(3.4f + sep * 0.85f, 4.2f, 9.5f);
                float lift = Mathf.Max(Mathf.Max(a.Height, b.Height) * 0.5f, 0);
                dist += this.lift * 1.1f;
                pos = mid + side * dist + Vector3.up * (1.45f + dist * 0.06f + lift + this.lift * 0.45f);
                look = mid + Vector3.up * (1.05f + lift + this.lift * 0.75f);
                // near the courtyard edge the camera may not back out behind the walls / trees:
                // hold it inside and widen the lens so both fighters stay in shot
                Vector3 safe = ClampSafe(pos);
                fightFov = baseFov;
                if ((safe - pos).sqrMagnitude > 0.0004f)
                {
                    float dd = Mathf.Max(1f, Vector3.Distance(safe, look));
                    float h1 = 2 * Mathf.Atan((sep * 0.5f + 1.0f) / dd);
                    float v = 2 * Mathf.Atan(Mathf.Tan(h1 * 0.5f) / cam.aspect) * Mathf.Rad2Deg;
                    fightFov = Mathf.Clamp(Mathf.Max(v, 2 * Mathf.Atan(1.5f / dd) * Mathf.Rad2Deg), baseFov, 70f);
                    pos = safe;
                }
                transform.position = Vector3.SmoothDamp(transform.position, pos, ref vel, 0.18f, 60f, dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look - transform.position), 1 - Mathf.Exp(-10 * dt));
                break;
            }
        }

        // no mode may park the camera behind the buildings, walls, trees or bamboo
        if (!boss && !Fighter.freeRoam) transform.position = ClampSafe(transform.position, 0.4f);
        else if (!boss && focus != null)
        {
            // KO / victory shots out in the town: keep a clear view of the fighter
            Vector3 f0 = focus.Body + Vector3.up * 1.1f, d0 = transform.position - f0;
            if (d0.sqrMagnitude > 0.01f && Physics.SphereCast(f0, 0.3f, d0.normalized, out var hit0, d0.magnitude, TownMap.SolidMask, QueryTriggerInteraction.Ignore))
                transform.position = f0 + d0.normalized * Mathf.Max(1f, hit0.distance - 0.2f);
        }

        // shake (unscaled so it plays through hit-stop)
        shakeT += dt;
        float amp = shakeAmp * Mathf.Exp(-shakeT * 7f);
        if (amp > 0.001f)
        {
            float t = Clock.Now * 38f;
            transform.position += transform.right * (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2 * amp
                                + transform.up * (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2 * amp;
            transform.rotation *= Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, t) - 0.5f) * (bossFrame ? 4 : 12) * amp);
        }
        punch = Mathf.MoveTowards(punch, 0, dt * 2.2f);
        if (fovNow < 0) fovNow = baseFov;
        fovNow = Mathf.Lerp(fovNow, boss ? fovWant : mode == Mode.Fight ? fightFov : baseFov, 1 - Mathf.Exp(-3f * dt));
        cam.fieldOfView = fovNow - punch * 7f;
    }

    // ------------------------------------------------------------------ boss framing
    static Vector3 Elevated(Vector3 horizontal, float deg = 15f)
    {
        float r = deg * Mathf.Deg2Rad;
        return (horizontal * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r)).normalized;
    }

    static Vector3 ClampSafe(Vector3 p, float minY = 1.3f)
    {
        Vector3 f = new Vector3(p.x, 0, p.z);
        if (f.magnitude > SafeRadius) f = f.normalized * SafeRadius;
        f.z = Mathf.Clamp(f.z, GateZ, HallZ);
        return new Vector3(f.x, Mathf.Clamp(p.y, minY, 14f), f.z);
    }

    // distance the camera must stand back (along viewDir) to fit the bounds in a BossFov lens
    float Need(Bounds bb, Vector3 horizontalSide)
    {
        Vector3 e = bb.extents;
        Vector3 right = Vector3.Cross(Vector3.up, horizontalSide).normalized;
        float halfW = Mathf.Abs(right.x) * e.x + Mathf.Abs(right.z) * e.z;
        float depth = Mathf.Abs(horizontalSide.x) * e.x + Mathf.Abs(horizontalSide.z) * e.z;
        float vf = BossFov * 0.5f * Mathf.Deg2Rad;
        float hf = Mathf.Atan(Mathf.Tan(vf) * cam.aspect);
        return Mathf.Max(6f, Mathf.Max(e.y / Mathf.Tan(vf), halfW / Mathf.Tan(hf)) * 1.15f + depth);
    }

    void BossCam(float dt)
    {
        Bounds bb = frameBounds;
        Vector3 c = bb.center;
        if (bossSide.sqrMagnitude < 0.5f) bossSide = side;

        // stay on the fighters' perpendicular, but swing round to the other side of the action when
        // the courtyard behind the camera is too shallow (that is when it used to end up in the bamboo)
        Vector3 s = Vector3.Dot(side, bossSide) >= 0 ? side : -side;
        float need = Need(bb, s);
        float roomS = Vector3.Distance(ClampSafe(c + Elevated(s) * need), c);
        float roomO = Vector3.Distance(ClampSafe(c + Elevated(-s) * need), c);
        Vector3 target = roomS < need * 0.85f && roomO > roomS * 1.4f ? -s : s;
        float yaw = Mathf.Atan2(bossSide.x, bossSide.z) * Mathf.Rad2Deg;
        float yawT = Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg;
        yaw = Mathf.MoveTowardsAngle(yaw, yawT, 75f * dt);
        bossSide = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0, Mathf.Cos(yaw * Mathf.Deg2Rad));

        need = Need(bb, bossSide);
        Vector3 want = ClampSafe(c + Elevated(bossSide) * need);
        float got = Vector3.Distance(want, c);
        float fov = BossFov;
        if (got < need - 0.05f)
        {
            // could not back up that far without leaving the courtyard: widen the lens instead
            Vector3 e = bb.extents;
            Vector3 right = Vector3.Cross(Vector3.up, bossSide).normalized;
            float halfW = Mathf.Abs(right.x) * e.x + Mathf.Abs(right.z) * e.z;
            float depth = Mathf.Abs(bossSide.x) * e.x + Mathf.Abs(bossSide.z) * e.z;
            float dd = Mathf.Max(1.5f, got - depth);
            float v1 = 2 * Mathf.Atan(e.y * 1.15f / dd);
            float h1 = 2 * Mathf.Atan(halfW * 1.15f / dd);
            float v2 = 2 * Mathf.Atan(Mathf.Tan(h1 * 0.5f) / cam.aspect);
            fov = Mathf.Clamp(Mathf.Max(v1, v2) * Mathf.Rad2Deg, BossFov, 80f);
        }
        fovWant = fov;
        transform.position = Vector3.SmoothDamp(transform.position, want, ref vel, 0.32f, 60f, dt);
        Vector3 lookDir = c - transform.position;
        if (lookDir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), 1 - Mathf.Exp(-7 * dt));
    }
}
