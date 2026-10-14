using UnityEngine;
using UnityEngine.InputSystem;

// The boss fight's camera director. Instead of one fixed view it cuts between shots on its own,
// choosing ones that suit the moment:
//   SIDE      the readable fighting-game side view, framing Kaito, Moros and his weapon
//   VILLAIN   over Moros's shoulder, looking down at Kaito (before his big strikes)
//   LOW       from the ground at Kaito's side, looking up at the titan / the monk (terrifying)
//   ORBIT     a slow circle around the action (reveals and transformations)
//   CHASE     behind Kaito as he runs through the town
//   FLEE      in front of Kaito, looking back at what is chasing him
//   AERIAL    high over the streets, showing the hunting swarms searching for him
//   (COBRA)   while the King Cobra hunts: three-quarter views of its reared front, over its hood,
//             Kaito fleeing toward the lens with the snake behind him, and high over the streets
//   MANUAL    mouse look: moving the mouse takes the camera over (orbit around Kaito, wheel zooms,
//             middle click turns to face Moros); the director takes back over after a few idle seconds
// Every shot keeps a clear line of sight to Kaito: the town's solid colliders are sphere-cast and
// the camera is pulled in front of any house, wall or tree - it never ends up behind a building.
[DefaultExecutionOrder(90)]
public class BossDirector : MonoBehaviour
{
    public enum Shot { Side, Villain, Low, Orbit, Chase, Flee, Aerial, Manual }
    public enum Context { Fight, Hunt, Titan, Cinematic, Indoor, Cobra }

    [System.NonSerialized] public Context context = Context.Cinematic;
    [System.NonSerialized] public Fighter hero;
    [System.NonSerialized] public Vector3 boss, bossHead;
    [System.NonSerialized] public Vector3 bossFacing;   // the titan's forward (frame its front, not its edge)
    [System.NonSerialized] public float bossClearance = 1.5f;   // never put the camera closer than this to the boss (titan: its arm reach)
    [System.NonSerialized] public Bounds bounds;
    [System.NonSerialized] public System.Func<Vector3, bool> blocker;
    [System.NonSerialized] public bool bossChases;   // the big thing races after Kaito (the cobra), not just looms   // a big body the camera must not sit inside (the cobra's coils)
    [System.NonSerialized] public float fov = 50;
    public Shot shot = Shot.Side;
    public float ShotTime => shotT;

    Camera cam;
    float shotT, hold = 4;
    Vector3 vel, side = Vector3.back;
    float orbitYaw;
    Shot? request;
    bool cut = true;
    float blendT;          // > 0 while gliding from the old shot to the new one
    const float MinShot = 3f;   // Moros's attacks ask for cuts; never switch faster than this

    // mouse look
    public static bool Manual { get; private set; }
    const float MouseSens = 0.11f, ManualHold = 6f;
    float manualT, mYaw, mPitch = 16, mDist = 6.5f;
    Shot autoShot = Shot.Side;

    void Awake() => cam = GetComponent<Camera>();

    public void Cut(Shot s) => request = s;

    // hero powers stage their own shots (the Guardian rising, the Awakening's strikes); call every frame
    Vector3 ovPos, ovLook;
    float ovFov, ovT;
    public void Override(Vector3 camPos, Vector3 look, float fovDeg, float hold = 0.15f)
    {
        ovPos = camPos; ovLook = look; ovFov = fovDeg; ovT = hold;
    }
    bool snapOv;
    public void SnapOverride() => snapOv = true;   // the next staged shot jumps straight into place (a scene cut)

    Vector3 HeroChest => hero.Body + Vector3.up * 1.2f;

    static bool Clear(Vector3 a, Vector3 b) => !Physics.Linecast(a, b, TownMap.SolidMask, QueryTriggerInteraction.Ignore);

    public void Tick(float dt)
    {
        if (hero == null) return;
        if (ovT > 0)
        {
            ovT -= dt;
            Vector3 want0 = Unblock(ovLook, ovPos);
            if (snapOv)
            {
                snapOv = false;
                transform.position = want0;
                vel = Vector3.zero;
                if ((ovLook - want0).sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(ovLook - want0);
                fov = ovFov;
                return;
            }
            transform.position = Vector3.SmoothDamp(transform.position, want0, ref vel, 0.5f, 40f, dt);
            Vector3 d0 = ovLook - transform.position;
            if (d0.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d0), 1 - Mathf.Exp(-4 * dt));
            fov = ovFov;
            shotT = hold + 1;   // choose a fresh shot when the power ends
            return;
        }
        if (MobileControls.On && context != Context.Cinematic) { MobileFollow(dt); return; }
        shotT += dt;
        MouseLook(dt);
        if (Manual) { request = null; shot = Shot.Manual; }
        else if (request.HasValue && (shotT > MinShot || context == Context.Cinematic)) { Switch(request.Value); request = null; }
        else if (shotT > hold) { request = null; Switch(Pick()); }
        else if (shotT > 1.2f && (!Clear(transform.position, HeroChest) || TooClose(transform.position) || Cramped(transform.position))) Switch(Pick());

        Compute(shot, out var want, out var look, out float wantFov);
        want = Unblock(HeroChest, want);
        if (shot == Shot.Manual)
        {
            // the player is steering: follow tightly, look exactly where the mouse points
            transform.position = Vector3.SmoothDamp(transform.position, want, ref vel, 0.05f, 200f, dt);
            transform.rotation = Quaternion.Euler(mPitch, mYaw, 0);
            fov = wantFov;
            return;
        }
        if (cut)
        {
            transform.position = want;
            transform.rotation = Quaternion.LookRotation(look - want);
            vel = Vector3.zero;
            cut = false;
        }
        else
        {
            // a new shot glides in over about a second instead of jumping
            blendT = Mathf.Max(0, blendT - dt);
            float smooth = blendT > 0 ? 0.55f : shot == Shot.Chase || shot == Shot.Flee ? 0.22f : 0.35f;
            transform.position = Vector3.SmoothDamp(transform.position, want, ref vel, smooth, 60f, dt);
            Vector3 d = look - transform.position;
            if (d.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1 - Mathf.Exp(-(blendT > 0 ? 3.5f : 5.5f) * dt));
        }
        fov = wantFov;
    }

    // PHONES: one steady camera instead of cuts - behind Kaito and a little over his shoulder,
    // turned toward whatever threatens him (Moros, the titan, the cobra, the ball...) so both stay
    // in frame; it pulls back and looks up for the big ones. A swipe looks around; a second or two
    // after the thumb lifts it eases back behind him. Scripted moments still stage their own shots.
    float fYaw = float.NaN, fPitch = 14f, fIdle = 99f, fDist = 7f;
    void MobileFollow(float dt)
    {
        Vector3 h = HeroChest;
        Vector3 tb = boss - h; tb.y = 0;
        float away = tb.magnitude;
        float heroYaw = hero.transform.eulerAngles.y;
        float wantYaw = away > 1.5f ? Mathf.Atan2(tb.x, tb.z) * Mathf.Rad2Deg : (float.IsNaN(fYaw) ? heroYaw : fYaw);
        float tall = Mathf.Max(0f, bossHead.y - h.y);
        float basePitch = Mathf.Clamp(10f + tall * 1.1f, 10f, 26f);
        if (float.IsNaN(fYaw)) { fYaw = wantYaw; fPitch = basePitch; }
        Vector2 look = MobileControls.Look;
        if (look.sqrMagnitude > 0.5f)
        {
            fYaw += look.x * 0.14f;
            fPitch = Mathf.Clamp(fPitch - look.y * 0.1f, -4f, 55f);
            fIdle = 0;
        }
        else fIdle += dt;
        if (fIdle > 1.6f)
        {
            float k = 1 - Mathf.Exp(-1.8f * dt);
            fYaw = Mathf.LerpAngle(fYaw, wantYaw, k);
            fPitch = Mathf.Lerp(fPitch, basePitch, k);
        }
        // big things need room: step back for them, and for a distant threat
        float wantDist = Mathf.Clamp(6.2f + tall * 0.5f + Mathf.Min(away, 24f) * 0.12f, 6.2f, 15f);
        // a big formation: step back far enough to see all of it
        float fit = Fit(bounds, 60) * 0.8f;
        if (bounds.size.magnitude > 6f) wantDist = Mathf.Clamp(Mathf.Max(wantDist, fit), 6.2f, 26f);
        fDist = Mathf.Lerp(fDist, wantDist, 1 - Mathf.Exp(-2f * dt));
        var q = Quaternion.Euler(fPitch, fYaw, 0);
        Vector3 pivot = h + Vector3.up * 0.35f;
        Vector3 want = pivot - q * Vector3.forward * fDist + Quaternion.Euler(0, fYaw, 0) * Vector3.right * 0.8f;
        want = Unblock(HeroChest, want);
        if (want.y < 0.5f) want.y = 0.5f;
        Vector3 flatFwd = Quaternion.Euler(0, fYaw, 0) * Vector3.forward;
        Vector3 lookAt = pivot + flatFwd * 5f + Vector3.up * Mathf.Clamp(tall * 0.3f, 0f, 3.5f) - Vector3.up * Mathf.Tan(fPitch * Mathf.Deg2Rad) * 2f;
        if (bounds.size.magnitude > 6f) lookAt = Vector3.Lerp(lookAt, bounds.center, 0.4f);   // lean toward the formation
        transform.position = Vector3.SmoothDamp(transform.position, want, ref vel, 0.16f, 90f, dt);
        Vector3 d = lookAt - transform.position;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1 - Mathf.Exp(-9f * dt));
        fov = 60;
    }

    void MouseLook(float dt)
    {
        var mouse = Mouse.current;
        bool touch = MobileControls.On;
        bool live = !TimeFX.paused && (touch || (mouse != null && Cursor.lockState == CursorLockMode.Locked));
        if (live)
        {
            // on a phone a finger dragging the empty screen is the mouse; FACE is the middle button
            Vector2 d = touch ? MobileControls.Look : mouse.delta.ReadValue();
            float wheel = touch || mouse == null ? 0 : mouse.scroll.ReadValue().y;
            bool snap = touch ? MobileControls.Face : mouse.middleButton.wasPressedThisFrame;
            if (d.sqrMagnitude > 2f || wheel != 0 || snap)
            {
                if (!Manual)
                {
                    // take over from the current view without a jump
                    Vector3 e = transform.eulerAngles;
                    mYaw = e.y;
                    mPitch = Mathf.Clamp(Mathf.DeltaAngle(0, e.x), -8f, 60f);
                    Manual = true;
                }
                manualT = 0;
                mYaw += d.x * MouseSens;
                mPitch = Mathf.Clamp(mPitch - d.y * MouseSens, -8f, 70f);
                if (wheel != 0) mDist = Mathf.Clamp(mDist - Mathf.Sign(wheel) * 0.9f, 3.2f, 16f);
                if (snap)
                {
                    // turn round to face Moros
                    Vector3 tb = boss - HeroChest; tb.y = 0;
                    if (tb.sqrMagnitude > 0.01f) mYaw = Mathf.Atan2(tb.x, tb.z) * Mathf.Rad2Deg;
                    mPitch = Mathf.Clamp(12f + bossHead.y * 1.2f, 12f, 30f);
                }
            }
        }
        if (Manual)
        {
            manualT += dt;
            if (manualT > ManualHold)
            {
                Manual = false;
                shot = autoShot;
                Switch(Pick());
            }
        }
    }

    void OnDisable() => Manual = false;

    void Switch(Shot s)
    {
        if (s == Shot.Manual) return;
        autoShot = s;
        if (s != shot)
        {
            // glide there unless a wall is in the way (then a clean cut is better than flying through it)
            Compute(s, out var p, out _, out _);
            p = Unblock(HeroChest, p);
            bool through = Physics.Linecast(transform.position, p, TownMap.SolidMask, QueryTriggerInteraction.Ignore);
            if (through || (p - transform.position).sqrMagnitude > 30f * 30f) cut = true;
            else blendT = 1.1f;
        }
        shot = s;
        shotT = 0;
        switch (context)
        {
            case Context.Hunt: hold = Random.Range(5f, 6.5f); break;
            case Context.Indoor: hold = Random.Range(5f, 6.5f); break;
            case Context.Titan: hold = s == Shot.Side ? Random.Range(6f, 8f) : Random.Range(4.5f, 5.5f); break;
            case Context.Cobra: hold = s == Shot.Side ? Random.Range(5f, 6.5f) : Random.Range(3.5f, 4.5f); break;
            case Context.Cinematic: hold = Random.Range(3.5f, 4.5f); break;
            default: hold = s == Shot.Side ? Random.Range(8f, 11f) : Random.Range(4f, 5f); break;
        }
        if (s == Shot.Orbit) orbitYaw = Mathf.Atan2(transform.position.x - bounds.center.x, transform.position.z - bounds.center.z) * Mathf.Rad2Deg;
    }

    // weighted choice of the next shot for the current situation, skipping ones that cannot see
    Shot Pick()
    {
        Shot[] opts; float[] w;
        bool run = hero.running;
        switch (context)
        {
            case Context.Hunt:
                opts = new[] { Shot.Aerial, Shot.Chase, Shot.Flee, Shot.Side };
                w = new[] { 0.35f, run ? 0.3f : 0.1f, run ? 0.25f : 0.1f, 0.2f };
                break;
            case Context.Titan:
                opts = new[] { Shot.Low, Shot.Side, Shot.Villain, Shot.Orbit };
                w = new[] { 0.35f, 0.35f, 0.15f, 0.15f };
                break;
            case Context.Cobra:
            {
                // racing after him: show the chase; circling him: frame the reared snake
                bool chasing = bossChases && new Vector3(boss.x - hero.transform.position.x, 0, boss.z - hero.transform.position.z).magnitude > 16f;
                opts = new[] { Shot.Side, Shot.Villain, Shot.Flee, Shot.Aerial, Shot.Low };
                w = chasing ? new[] { 0.2f, 0.05f, 0.4f, 0.35f, 0.0f } : new[] { 0.45f, 0.25f, 0.05f, 0.1f, 0.15f };
                break;
            }
            case Context.Cinematic:
                opts = new[] { Shot.Orbit, Shot.Low, Shot.Side };
                w = new[] { 0.45f, 0.35f, 0.2f };
                break;
            case Context.Indoor:
                // inside a building: follow Kaito through the rooms; now and then Moros's view through the door
                opts = new[] { Shot.Chase, Shot.Flee, Shot.Villain, Shot.Side };
                w = new[] { 0.4f, 0.3f, 0.15f, 0.15f };
                break;
            default:
                opts = new[] { Shot.Side, Shot.Villain, Shot.Low, Shot.Orbit, Shot.Chase };
                w = new[] { 0.5f, 0.15f, 0.12f, 0.1f, run ? 0.25f : 0.02f };
                break;
        }
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float total = 0;
            for (int i = 0; i < opts.Length; i++) if (opts[i] != shot || opts.Length == 1) total += w[i];
            float r = Random.value * total;
            Shot pick = opts[0];
            for (int i = 0; i < opts.Length; i++)
            {
                if (opts[i] == shot && opts.Length > 1) continue;
                r -= w[i];
                if (r <= 0) { pick = opts[i]; break; }
            }
            Compute(pick, out var p, out _, out _);
            p = Unblock(HeroChest, p);
            if (Clear(p, HeroChest) && !TooClose(p) && !Cramped(p)) return pick;
        }
        return Shot.Side;
    }

    void Compute(Shot s, out Vector3 pos, out Vector3 look, out float f)
    {
        Vector3 h = HeroChest;
        Vector3 toBoss = boss - h; toBoss.y = 0;
        Vector3 tb = toBoss.sqrMagnitude > 0.01f ? toBoss.normalized : hero.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, tb).normalized;
        Vector3 move = hero.running ? new Vector3(hero.transform.forward.x, 0, hero.transform.forward.z).normalized : -tb;
        f = 50;
        switch (s)
        {
            case Shot.Villain:
            {
                if (context == Context.Cobra)
                {
                    // over the hood, behind the snake's head, looking down its strike line at Kaito
                    Vector3 f0 = bossFacing.sqrMagnitude > 0.01f ? bossFacing.normalized : tb;
                    pos = bossHead - f0 * 6.5f + Vector3.Cross(Vector3.up, f0) * 3.2f + Vector3.up * 3.2f;
                    look = Vector3.Lerp(h, bossHead, 0.25f);
                    f = 56;
                }
                else if (context == Context.Titan)
                {
                    // high over the titan's shoulder, clear of its pauldrons, looking down at Kaito
                    pos = bossHead - tb * 4.5f + right * 6.5f + Vector3.up * 2.5f;
                    look = h;
                    f = 52;
                }
                else
                {
                    pos = boss - tb * 3.2f + Vector3.up * 1.6f + right * 1.4f;
                    look = h + Vector3.up * 0.2f;
                    f = 48;
                }
                break;
            }
            case Shot.Low:
            {
                // from the ground behind Kaito, far enough back that the whole boss towers over him
                Vector3 feet = new Vector3(boss.x, 0, boss.z);
                Vector3 away = new Vector3(h.x - feet.x, 0, h.z - feet.z);
                if (away.sqrMagnitude < 0.25f) away = -tb;
                float tall = Mathf.Max(2.5f, bossHead.y);
                float dist = Mathf.Max(away.magnitude + 3.2f, tall * 1.15f + 1.5f);
                pos = feet + away.normalized * dist + right * (tall * 0.2f + 1.2f) + Vector3.up * 0.7f;
                look = Vector3.Lerp(h, bossHead, 0.62f);
                f = 62;
                break;
            }
            case Shot.Orbit:
            {
                orbitYaw += 14f * Clock.Dt;
                float fit = Fit(bounds, 50);
                Vector3 dir = Quaternion.Euler(22, orbitYaw, 0) * Vector3.back;
                pos = bounds.center - dir * fit;
                look = bounds.center;
                f = 50;
                break;
            }
            case Shot.Chase:
            {
                pos = h - move * 5.2f + Vector3.up * 1.9f + Vector3.Cross(Vector3.up, move) * 0.7f;
                look = h + move * 6f + Vector3.up * 0.2f;
                f = 58;
                break;
            }
            case Shot.Flee:
            {
                pos = h + move * 5.5f + Vector3.up * 1.5f;
                look = Vector3.Lerp(h, boss, 0.25f);
                f = 55;
                break;
            }
            case Shot.Manual:
            {
                Quaternion q = Quaternion.Euler(mPitch, mYaw, 0);
                Vector3 pivot = h + Vector3.up * 0.35f;
                // a titan fills the screen: stand further back
                float d = mDist + (bossHead.y > 5f ? 3f : 0f);
                pos = pivot - q * Vector3.forward * d;
                look = pivot;
                f = 60;
                break;
            }
            case Shot.Aerial:
            {
                if (context == Context.Cobra)
                {
                    // high over the streets: the whole snake winding after him
                    pos = h - tb * 9f + right * 5f + Vector3.up * 22f;
                    look = Vector3.Lerp(h, boss, 0.4f);
                    f = 58;
                    break;
                }
                pos = h - tb * 5f + right * 3f + Vector3.up * 15f;
                look = Vector3.Lerp(h, boss, 0.25f);
                f = 55;
                break;
            }
            default:
            {
                // side view: perpendicular to Kaito-Moros, on the side with more open space
                Vector3 n = right;
                if (Vector3.Dot(n, side) < 0) n = -n;
                if ((context == Context.Titan || context == Context.Cobra) && bossFacing.sqrMagnitude > 0.01f)
                    n = (n + new Vector3(bossFacing.x, 0, bossFacing.z).normalized * 1.3f).normalized;   // front three-quarter
                float need = Fit(bounds, 50);
                float roomA = Room(bounds.center, n, need), roomB = Room(bounds.center, -n, need);
                if (roomA < need * 0.8f && roomB > roomA * 1.4f) n = -n;
                side = Vector3.Slerp(side, n, 1 - Mathf.Exp(-3 * Clock.Dt)).normalized;
                Vector3 dir = (side * Mathf.Cos(15 * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(15 * Mathf.Deg2Rad)).normalized;
                float got = Mathf.Min(need, Room(bounds.center, side, need));
                pos = bounds.center + dir * got;
                look = bounds.center;
                f = got < need - 0.05f ? Mathf.Clamp(50 * need / Mathf.Max(1f, got), 50, 80) : 50;
                break;
            }
        }
        if (pos.y < 0.35f) pos.y = 0.35f;
    }

    // a wall pushed the camera right up against Kaito (his face fills the screen)
    bool Cramped(Vector3 p) => (p - HeroChest).sqrMagnitude < 3.2f * 3.2f;

    bool TooClose(Vector3 p)
    {
        if (blocker != null && blocker(p)) return true;
        Vector3 d = new Vector3(p.x - boss.x, 0, p.z - boss.z);
        return d.magnitude < bossClearance && p.y < bossHead.y + 1.5f;
    }

    // distance needed to fit the bounds in a lens of the given vertical fov
    float Fit(Bounds b, float fovDeg)
    {
        Vector3 e = b.extents;
        float r = Mathf.Max(e.y, Mathf.Max(e.x, e.z) / Mathf.Max(0.5f, cam.aspect));
        return Mathf.Max(6f, r / Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad) * 1.15f + Mathf.Max(e.x, e.z) * 0.5f);
    }

    // how far the camera can back away from centre along dir before a solid gets in the way
    static float Room(Vector3 centre, Vector3 dir, float max)
    {
        Vector3 d = (dir + Vector3.up * 0.27f).normalized;
        if (Physics.SphereCast(centre, 0.4f, d, out var hit, max, TownMap.SolidMask, QueryTriggerInteraction.Ignore))
            return Mathf.Max(2f, hit.distance - 0.3f);
        return max;
    }

    // pull the camera in front of anything between it and the target
    static Vector3 Unblock(Vector3 target, Vector3 pos)
    {
        Vector3 d = pos - target;
        float dist = d.magnitude;
        if (dist < 0.01f) return pos;
        if (Physics.SphereCast(target, 0.35f, d / dist, out var hit, dist, TownMap.SolidMask, QueryTriggerInteraction.Ignore))
            pos = target + d / dist * Mathf.Max(1.2f, hit.distance - 0.25f);
        return pos;
    }
}
