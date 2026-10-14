using System;
using UnityEngine;

// What a brain (player or AI) wants the fighter to do this frame.
public struct Intent
{
    public Vector3 move;      // world-space, magnitude <= 1
    public bool light, heavy, dash, grab;
    public bool block;        // held
    public bool jump;         // wall-run / leap (boss fight) - a dash in a normal match
    public int power;         // hero power pressed this frame (1 palm, 2 spirit, 3 awakening, 4 gauntlets)
}

public abstract class Brain : MonoBehaviour
{
    public abstract Intent Think(Fighter self, Fighter foe, float dt);
}

// A fighter driven by motion-captured clips: movement, attack strings, blocking/parrying, dodges,
// paired throws, stamina, hit reactions, launches and juggles. Hit windows come from each clip's
// analysed impact frame, strikes are tested against the real bone positions, and all timing runs
// on scaled time so hit-stop and slow motion freeze everything together.
[RequireComponent(typeof(AnimDriver))]
public class Fighter : MonoBehaviour
{
    public enum State { Intro, Neutral, Attack, Dash, Block, Blockstun, Parried, Hitstun, Throwing, Thrown, Air, Down, GetUp, KO, Victory, Leap, WallRun, Power }

    [Header("Identity")]
    public string displayName = "Kaito";
    public Color accent = new Color(0.2f, 0.5f, 1f);
    public bool isBrute;
    public string idleClip = "BouncingIdle", introClip = "Bow", victoryClip = "VictoryBoxing";

    [Header("Stats")]
    public float maxHealth = 100;
    public float walkSpeed = 2.3f;
    public float attackSpeed = 1f;
    public float damageMul = 1f;
    public float weight = 1f;          // scales knockback taken
    public float bodyRadius = 0.24f;   // hurt capsule radius
    public float maxStamina = 100;

    [NonSerialized] public Brain brain;
    [NonSerialized] public Fighter foe;
    [NonSerialized] public AnimDriver anim;
    [NonSerialized] public Animator animator;
    [NonSerialized] public HitSpring spring;
    [NonSerialized] public FootLock feet;
    [NonSerialized] public float health, stamina;
    [NonSerialized] public State state = State.Intro;
    [NonSerialized] public Move move;
    [NonSerialized] public int moveId;
    [NonSerialized] public int combo;
    [NonSerialized] public float comboDamage;
    [NonSerialized] public int juggles;
    [NonSerialized] public bool inputLocked = true;

    public static event Action<Fighter, Fighter, Move, float, Vector3> Hit;
    public static event Action<Fighter, Fighter, Move, Vector3> Blocked;
    public static event Action<Fighter, Fighter, Vector3> Parry;
    public static event Action<Fighter> GuardBroken;
    public static event Action<Fighter, Fighter> Evaded;
    public static event Action<Fighter, Fighter> Grabbed;
    public static event Action<Fighter, Fighter, float> ThrowLanded;
    public static event Action<Fighter, Fighter> ThrowBroken;
    public static event Action<Fighter> Knocked;
    public static event Action<Fighter> KnockedOut;
    public static event Action<Fighter, Move> Swing;
    public static event Action<Fighter, string, Color> Callout;

    // GOD MODE (testing): launch with -god (or press F2 in game) and Kaito's health never drops
    public static bool God = System.Array.IndexOf(Environment.GetCommandLineArgs(), "-god") >= 0;
    public static bool BossInvincible;   // the Moros fight: only Moros's health goes down
    bool Protected => (God || BossInvincible) && GameManager.I != null && this == GameManager.I.p1;
    float Hurt(float dmg) => Protected ? 0 : dmg;

    public const float ArenaRadius = 9f;
    // boss chase: the fight spills out of the courtyard into the temple town (TownMap)
    public static bool freeRoam;
    public float runSpeed = 6.4f;
    [NonSerialized] public bool running;
    // what Kaito stands on: the street (0), a roof, a wall top, a crate stack
    [NonSerialized] public float floorY;
    public bool OnRoof => floorY > 1.2f;
    [NonSerialized] public Intent lastIntent;
    [NonSerialized] public float strikeMul = 1;     // Stone Gauntlets
    public float Y { get => y; set => y = value; }
    // parkour
    Vector3 leapVel, wallDir, wallLand, flipFrom;
    float wallTop, parkT, fallFrom;
    int parkPhase;
    readonly System.Collections.Generic.Dictionary<string, float> clipSpeed = new System.Collections.Generic.Dictionary<string, float>();
    const float ParryWindow = 0.12f;
    const float DashCost = 18, ThrowCost = 10;
    const int MaxJuggles = 5;

    float stateT, moveT;
    bool moveHit, moveEvaded;
    Move queued;
    Btn? buffered;
    float bufferT;
    bool grabBuffered;
    float grabBufferT;
    Vector3 knock;
    float y, vy, airT, airDur;
    Vector3 lastWalk;
    float hitDur;
    float dashCooldown;
    Vector3 dashDir;
    float flash;
    Color flashColor = new Color(0.55f, 0.04f, 0.02f);
    Renderer[] renderers;
    MaterialPropertyBlock mpb;
    float blockPressedAt = -10, lastSpend = -10;
    bool blockHeld;
    float getUpDur = 1.2f;
    // throws
    Fighter grabbed, thrower;
    bool grabConnected;
    float breakPressedAt = -10;

    public bool Hurtable
    {
        get
        {
            switch (state)
            {
                case State.Neutral: case State.Attack: case State.Block: case State.Blockstun:
                case State.Parried: case State.Hitstun:
                    return true;
                case State.Throwing: return !grabConnected;
                case State.Dash: return stateT < 0.02f || stateT > 0.2f;
                case State.Air: return health > 0 && juggles < MaxJuggles;
                case State.Leap: return true;
                default: return false;
            }
        }
    }
    public bool CanAct => state == State.Neutral || state == State.Block;
    public bool IsDown => state == State.Down || state == State.KO || state == State.Air || state == State.Thrown;
    public bool Exhausted => stamina < 1;
    public float Height => y;
    // Where the body actually is (paired clips can carry the body away from the root).
    public Vector3 Body
    {
        get
        {
            var h = animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            return h != null ? new Vector3(h.position.x, transform.position.y, h.position.z) : transform.position;
        }
    }
    public float MoveTime => moveT;
    public bool Blocking => state == State.Block || state == State.Blockstun;
    AnimLibrary Lib => anim.library;

    void Awake()
    {
        anim = GetComponent<AnimDriver>();
        animator = GetComponent<Animator>();
        spring = GetComponent<HitSpring>();
        if (spring == null) spring = gameObject.AddComponent<HitSpring>();
        feet = GetComponent<FootLock>();
        if (feet == null) feet = gameObject.AddComponent<FootLock>();
        if (System.Array.IndexOf(Environment.GetCommandLineArgs(), "-nofootlock") >= 0) feet.enabled = false;   // A/B testing
        renderers = GetComponentsInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
        health = maxHealth;
        stamina = maxStamina;
    }

    public void ResetForRound(Vector3 pos, Vector3 facing)
    {
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(facing);
        health = maxHealth;
        stamina = maxStamina;
        move = null; queued = null; buffered = null; grabbed = null; thrower = null;
        knock = Vector3.zero; y = 0; vy = 0; juggles = 0;
        bossHeld = false;
        combo = 0; comboDamage = 0;
        inputLocked = true;
        state = State.Intro;
        stateT = 0;
        anim.Play(introClip, 0.05f, 0, 1);
        if (feet != null) feet.ReleaseAll();
    }

    public void BeginFight() { Enter(State.Neutral); inputLocked = false; }

    public void Celebrate()
    {
        inputLocked = true;
        if (state != State.KO && state != State.Down) Enter(State.Victory);
    }

    void Enter(State s)
    {
        state = s;
        stateT = 0;
        switch (s)
        {
            case State.Neutral:
                if (feet != null) feet.SetSupport(true, true);
                anim.Play(idleClip, 0.14f, 0, 1, loop: true, keepIfSame: true);
                break;
            case State.Block:
                if (feet != null) feet.SetSupport(true, true);
                anim.Hold("Block", Lib["Block"].impact, 0.08f);
                break;
            case State.Victory: anim.Play(victoryClip, 0.3f, 0, 1); break;
        }
    }

    void Spend(float amount)
    {
        stamina = Mathf.Max(0, stamina - amount);
        lastSpend = Time.time;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0) return;
        stateT += dt;
        dashCooldown -= dt;
        bufferT -= dt;
        grabBufferT -= dt;
        if (bufferT <= 0) buffered = null;
        if (grabBufferT <= 0) grabBuffered = false;

        var intent = (brain != null && !inputLocked && foe != null) ? brain.Think(this, foe, dt) : default;
        lastIntent = intent;
        floorY = GroundHere();
        if (intent.grab) { grabBuffered = true; grabBufferT = 0.2f; breakPressedAt = Time.time; }
        else
        {
            if (intent.light) { buffered = Btn.Light; bufferT = 0.22f; }
            if (intent.heavy) { buffered = Btn.Heavy; bufferT = 0.22f; }
        }
        if (intent.block && !blockHeld) blockPressedAt = Time.time;
        blockHeld = intent.block;

        if (Time.time - lastSpend > 0.55f && state != State.Attack)
            stamina = Mathf.Min(maxStamina, stamina + (Blocking ? 9f : 30f) * dt);

        switch (state)
        {
            case State.Intro:
                if (anim.Finished && anim.Current == introClip) anim.Play(idleClip, 0.3f, 0, 1, loop: true);
                break;
            case State.Neutral: Neutral(intent, dt); break;
            case State.Block: BlockUpdate(intent, dt); break;
            case State.Blockstun:
                if (stateT >= hitDur) { if (blockHeld) Enter(State.Block); else Enter(State.Neutral); }
                break;
            case State.Dash: DashUpdate(dt); break;
            case State.Attack: AttackUpdate(dt); break;
            case State.Throwing: ThrowUpdate(dt); break;
            case State.Thrown: ThrownUpdate(); break;
            case State.Parried:
            case State.Hitstun:
                if (stateT >= hitDur) Enter(State.Neutral);
                break;
            case State.Air: AirUpdate(dt); break;
            case State.Leap: LeapUpdate(intent, dt); break;
            case State.WallRun: WallRunUpdate(dt); break;
            case State.Power: break;   // HeroPowers drives the body
            case State.Down:
                if (stateT > 0.75f) StartGetUp();
                break;
            case State.GetUp:
                if (stateT > getUpDur) { state = State.Neutral; stateT = 0; anim.Play(idleClip, 0.15f, 0, 1, loop: true); }
                break;
        }

        if (knock.sqrMagnitude > 0.0001f && state != State.Thrown)
        {
            transform.position += knock * dt;
            float fr = y > 0.01f ? 0.5f : 9f;
            knock = Vector3.MoveTowards(knock, Vector3.zero, fr * dt);
        }

        Separate();
        if (state != State.WallRun) ClampToArena();
        FaceFoe(dt);
        // stand on whatever is under the feet; step off a roof edge and fall
        bool airborne = state == State.Air || state == State.Leap || state == State.WallRun || state == State.Thrown || state == State.Power;
        if (!airborne)
        {
            float g = GroundHere();
            bool canFall = state == State.Neutral || state == State.Block || state == State.Blockstun || state == State.Hitstun ||
                           state == State.Parried || state == State.Dash || state == State.Attack;
            if (y - g > 0.45f && freeRoam && canFall) BeginAir(running ? lastWalk : Vector3.zero, 0, "FallingIdle");
            else y = g;
            floorY = g;
        }
        bool standing = state == State.Neutral || state == State.Attack || state == State.Block || state == State.Blockstun ||
                        state == State.Hitstun || state == State.Parried || state == State.Throwing || state == State.Intro || state == State.Victory;
        // no foot locking while sprinting: a planted foot pinned to the street while the body runs on
        // is what stretched and folded the leg like rubber
        if ((!standing || running) && feet.active) feet.ReleaseAll();
        feet.active = standing && !running;
        var p = transform.position;
        transform.position = new Vector3(p.x, y, p.z);
        UpdateFlash();
    }

    // Root motion from the mocap carries attacks forward (never through the opponent).
    void OnAnimatorMove()
    {
        if (animator == null || state != State.Attack) return;
        // skip the start-offset jump of a freshly started clip, and never move faster than a lunge
        if (Time.frameCount - anim.StartFrame <= 1) return;
        var d = animator.deltaPosition;
        d.y = 0;
        d = Vector3.ClampMagnitude(d, 3.5f * Time.deltaTime);
        if (foe != null)
        {
            Vector3 to = Flat(foe.transform.position - transform.position);
            if (Vector3.Dot(d, to) > 0 && to.magnitude < 0.8f) d -= Vector3.Project(d, to.normalized);
        }
        transform.position += d;
    }

    // ------------------------------------------------------------------ neutral / movement
    void Neutral(Intent intent, float dt)
    {
        if (intent.jump && TryParkour(intent, freeRoam)) return;
        if (TryStartAction(intent)) return;
        if (intent.block) { Enter(State.Block); return; }
        Walk(intent.move, 1f, dt);
    }

    bool TryStartAction(Intent intent)
    {
        if (grabBuffered)
        {
            grabBuffered = false;
            StartThrow();
            return true;
        }
        if (buffered.HasValue)
        {
            var b = buffered.Value;
            buffered = null;
            bool towards = Vector3.Dot(intent.move, transform.forward) > 0.55f;
            StartMove(b == Btn.Heavy && towards ? Moves.RisingKnee : Moves.Start(b));
            return true;
        }
        if (intent.dash && dashCooldown <= 0 && stamina >= DashCost)
        {
            dashDir = intent.move.sqrMagnitude > 0.1f ? intent.move.normalized : -transform.forward;
            Spend(DashCost);
            state = State.Dash;
            stateT = 0;
            float side = Vector3.Dot(dashDir, transform.right), fwd = Vector3.Dot(dashDir, transform.forward);
            string clip = Mathf.Abs(side) > Mathf.Abs(fwd) ? (side > 0 ? "DodgeRight" : "DodgeLeft") : fwd > 0 ? "DodgeAdvance" : "DodgeRetreat";
            anim.Play(clip, 0.05f, 0, 2f);
            dashCooldown = 0.4f;
            return true;
        }
        return false;
    }

    void Walk(Vector3 input, float speedMul, float dt)
    {
        var m = Vector3.ClampMagnitude(new Vector3(input.x, 0, input.z), 1);
        // in the town, away from the foe, Kaito turns and sprints instead of shuffling in stance
        bool far = foe == null || Flat(foe.transform.position - transform.position).magnitude > 3.2f;
        running = freeRoam && state == State.Neutral && speedMul >= 1 && far && m.sqrMagnitude > 0.25f;
        if (running)
        {
            Vector3 rv = m.normalized * runSpeed * Mathf.Clamp01(m.magnitude * 1.3f);
            transform.position += rv * dt;
            lastWalk = Vector3.Lerp(lastWalk, rv, 1 - Mathf.Exp(-10 * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(m), 1 - Mathf.Exp(-12 * dt));
            string rc = RunClip;
            anim.Play(rc, 0.18f, 0, Mathf.Clamp(rv.magnitude / ClipSpeed(rc), 0.7f, 1.6f), loop: true, keepIfSame: true);
            return;
        }
        float fwd = Vector3.Dot(m, transform.forward);
        float speed = walkSpeed * speedMul * (fwd < -0.3f ? 0.8f : 1f);
        Vector3 v = m * speed;
        transform.position += v * dt;
        lastWalk = Vector3.Lerp(lastWalk, v, 1 - Mathf.Exp(-10 * dt));
        if (state != State.Neutral) return;
        float mag = lastWalk.magnitude;
        if (mag < 0.35f) { anim.Play(idleClip, 0.25f, 0, 1, loop: true, keepIfSame: true); return; }
        float lf = Vector3.Dot(lastWalk, transform.forward), ls = Vector3.Dot(lastWalk, transform.right);
        string clip = Mathf.Abs(lf) >= Mathf.Abs(ls) ? (lf > 0 ? "StepForward" : "StepBackward") : (ls > 0 ? "StrafeRight" : "StrafeLeft");
        anim.Play(clip, 0.2f, 0, Mathf.Clamp(mag / 1.6f, 0.8f, 1.6f), loop: true, keepIfSame: true);
    }

    void BlockUpdate(Intent intent, float dt)
    {
        if (!intent.block) { Enter(State.Neutral); return; }
        if (TryStartAction(intent)) return;
        Walk(intent.move, 0.4f, dt);
    }

    void DashUpdate(float dt)
    {
        const float dur = 0.3f;
        float k = 1 - stateT / dur;
        transform.position += dashDir * (8.5f * Mathf.Max(0, k)) * dt;
        if (stateT >= dur) Enter(State.Neutral);
    }

    // ------------------------------------------------------------------ attacks
    float Speed => attackSpeed * (Exhausted ? 0.78f : 1f);

    void StartMove(Move m)
    {
        move = m;
        moveT = m.clipStart;
        moveHit = false;
        moveEvaded = false;
        queued = null;
        moveId++;
        Spend(m.heavy || m.limb == Limb.FootR || m.limb == Limb.FootL || m.limb == Limb.KneeR ? 9 : 4);
        state = State.Attack;
        stateT = 0;
        // The humanoid clip's own foot IK is sufficient while striking.  Disable the post-animation
        // foot-lock solver for the whole stunt: even a locked support leg can otherwise pull the hip
        // and make the free leg look like soft rubber on high kicks, knees and spins.
        if (feet != null)
        {
            feet.SetSupport(false, false);
        }
        anim.Play(m.clip, 0.07f, m.clipStart, m.speed * Speed);
        Swing?.Invoke(this, m);
    }

    void AttackUpdate(float dt)
    {
        anim.SetSpeed(move.speed * Speed);
        moveT = anim.Time;

        // a little extra drive toward the opponent before the strike lands
        if (moveT < move.activeEnd && foe != null)
        {
            float dist = Vector3.Distance(Flat(transform.position), Flat(foe.transform.position));
            if (dist > 0.85f && dist < move.reach + 0.8f)
                transform.position += transform.forward * (move.lunge * 0.8f / Mathf.Max(0.1f, move.activeEnd - move.clipStart)) * move.speed * Speed * dt;
        }

        if (!moveHit && moveT >= move.activeStart && moveT <= move.activeEnd)
        {
            CheckHit();
            if (state != State.Attack) return;
        }

        if (buffered.HasValue && queued == null)
        {
            var n = Moves.Next(move, buffered.Value, isBrute);
            if (n != null) { queued = n; buffered = null; }
        }
        if (queued != null && moveT >= move.cancelFrom) { StartMove(queued); return; }
        if (moveT >= move.duration || anim.Current != move.clip)
        {
            move = null;
            if (feet != null) feet.SetSupport(true, true);
            if (blockHeld) Enter(State.Block); else Enter(State.Neutral);
        }
    }

    void CheckHit()
    {
        var p = LimbPoint(move.limb);
        bool leg = move.limb == Limb.FootR || move.limb == Limb.FootL || move.limb == Limb.KneeR || move.limb == Limb.KneeL;
        float r = leg ? 0.14f : 0.12f;
        // Story constructs are independent targets rather than a disguised second humanoid.
        // This keeps Kaito's real mocap strikes useful after Brakka is defeated.
        if (MonkBoss.TryHit(p, r, move.damage * damageMul * strikeMul, strikeMul > 1))
        {
            moveHit = true;
            return;
        }
        if (RocConstruct.TryHit(p, r + 0.18f, move.damage * damageMul))
        {
            moveHit = true;
            return;
        }
        if (foe == null) return;
        if (foe.state == State.Dash && !foe.Hurtable)
        {
            if (!moveEvaded && foe.Overlaps(p, r + 0.15f))
            {
                moveEvaded = true;
                Evaded?.Invoke(this, foe);
                Callout?.Invoke(foe, "EVADE!", new Color(0.4f, 0.85f, 1f));
            }
            return;
        }
        if (!foe.Hurtable) return;
        if (foe.Overlaps(p, r))
        {
            moveHit = true;
            foe.TakeHit(this, move, p);
        }
    }

    Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b);

    public Vector3 LimbPoint(Limb l)
    {
        switch (l)
        {
            case Limb.HandL: return Tip(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal);
            case Limb.HandR: return Tip(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal);
            case Limb.FootL: return Tip(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            case Limb.FootR: return Tip(HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            case Limb.KneeL: return Bone(HumanBodyBones.LeftLowerLeg).position;
            case Limb.KneeR: return Bone(HumanBodyBones.RightLowerLeg).position;
            case Limb.ElbowL: return Bone(HumanBodyBones.LeftLowerArm).position;
            case Limb.ElbowR: return Bone(HumanBodyBones.RightLowerArm).position;
            default: return Bone(HumanBodyBones.Head).position + Vector3.up * 0.08f;
        }
    }

    // Most of the way from a joint to its child (knuckles / ball of the foot) when the child exists.
    Vector3 Tip(HumanBodyBones joint, HumanBodyBones child)
    {
        var a = Bone(joint);
        var b = Bone(child);
        return b != null ? Vector3.Lerp(a.position, b.position, 0.7f) : a.position;
    }

    // Hurt capsule from the hips through the top of the head (down through the legs while airborne).
    public bool Overlaps(Vector3 p, float r)
    {
        Vector3 a = Bone(HumanBodyBones.Hips).position + Vector3.down * (state == State.Air ? 0.5f : 0.15f);
        Vector3 b = Bone(HumanBodyBones.Head).position + Vector3.up * 0.14f;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return (a + ab * t - p).sqrMagnitude < (bodyRadius + r) * (bodyRadius + r);
    }

    bool Facing(Fighter other)
    {
        Vector3 d = Flat(other.transform.position - transform.position).normalized;
        return Vector3.Dot(transform.forward, d) > 0.2f;
    }

    public void TakeHit(Fighter attacker, Move m, Vector3 point)
    {
        Vector3 dir = Flat(transform.position - attacker.transform.position).normalized;
        if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;

        bool guarding = (state == State.Block || state == State.Blockstun) && Facing(attacker);
        if (guarding && Time.time - blockPressedAt <= ParryWindow)
        {
            attacker.GetParried(this);
            spring.Kick(dir, point, 0.35f, false);
            attacker.spring.Kick(-dir, point, 1.4f, true);
            stamina = Mathf.Min(maxStamina, stamina + 20);
            Enter(State.Block);
            Flash(new Color(0.6f, 0.8f, 1f));
            Parry?.Invoke(attacker, this, point);
            Callout?.Invoke(this, "PARRY!", new Color(0.5f, 0.9f, 1f));
            return;
        }
        if (guarding)
        {
            attacker.combo = 0;
            Spend(m.damage * attacker.damageMul * 2.2f);
            if (m.heavy) health = Mathf.Max(1, health - Hurt(Mathf.Round(m.damage * attacker.damageMul * 0.12f)));
            knock = dir * (Mathf.Min(m.push * 0.5f + 0.5f, 1.8f) / weight);
            spring.Kick(dir, point, m.heavy ? 0.7f : 0.4f, false);
            if (stamina <= 0)
            {
                React(State.Hitstun, "StomachHit", 0.8f, 1.2f);
                Flash(new Color(0.7f, 0.5f, 0.1f));
                GuardBroken?.Invoke(this);
                Callout?.Invoke(attacker, "GUARD BREAK!", new Color(1f, 0.7f, 0.2f));
                return;
            }
            state = State.Blockstun;
            stateT = 0;
            hitDur = Mathf.Max(0.12f, m.hitstun * 0.45f);
            Blocked?.Invoke(attacker, this, m, point);
            return;
        }

        bool comboing = state == State.Hitstun || state == State.Air || state == State.Parried;
        attacker.combo = comboing ? attacker.combo + 1 : 1;
        if (!comboing) attacker.comboDamage = 0;
        float scale = Mathf.Max(0.45f, 1 - 0.08f * (attacker.combo - 1));
        if (state == State.Parried) scale *= 1.25f;
        float dmg = Mathf.Round(m.damage * attacker.damageMul * scale);
        health = Mathf.Max(0, health - Hurt(dmg));
        attacker.comboDamage += dmg;
        bool wasAir = state == State.Air;
        move = null; queued = null;
        Flash(new Color(0.55f, 0.04f, 0.02f));
        Hit?.Invoke(attacker, this, m, dmg, point);

        if (health <= 0)
        {
            spring.Kick(dir, point, 2.4f, true);
            knock = dir * (5.5f / weight);
            Launch(6.5f);
            KnockedOut?.Invoke(this);
            return;
        }

        if (m.knockdown || wasAir)
        {
            juggles = wasAir ? juggles + 1 : 0;
            float up = m.launch > 0 ? m.launch : 4.2f;
            if (juggles > 0) up = Mathf.Max(2.8f, Mathf.Min(up, 4.6f) - (juggles - 1) * 0.5f);
            knock = dir * ((wasAir ? Mathf.Min(m.push, 1.2f) : m.push) / weight);
            Launch(up);
            if (juggles >= 2) Callout?.Invoke(attacker, $"AIR x{juggles}", new Color(1f, 0.55f, 0.9f));
            return;
        }

        spring.Kick(dir, point, m.heavy ? 0.8f : 0.4f, m.high);
        if (m.heavy)
        {
            knock = dir * (Mathf.Min(m.push, 2.6f) / weight);
            React(State.Hitstun, "ReactLargeFront", Mathf.Max(m.hitstun, 0.45f), 1.35f);
        }
        else
        {
            // a real mocap flinch that matches where the blow landed (head: which side; body: gut or ribs)
            knock = dir * (Mathf.Min(m.push, 1.4f) / weight);
            float side = Vector3.Dot(point - Body, transform.right);
            string clip = m.high ? (UnityEngine.Random.value < 0.4f ? "HeadHit" : side > 0 ? "HitHeadRight" : side < -0.05f ? "HitHeadLeft" : "HitHeadStraight")
                                 : (UnityEngine.Random.value < 0.45f ? "BodyHit" : side > 0 ? "KidneyHit" : "StomachHit");
            if (!anim.Has(clip)) clip = m.high ? "HitHeadStraight" : "StomachHit";
            state = State.Hitstun;
            stateT = 0;
            hitDur = Mathf.Max(0.28f, m.hitstun * 0.75f);
            if (feet != null) feet.SetSupport(true, true);
            anim.Play(clip, 0.05f, 0.04f, 1.3f);
        }
    }

    void React(State s, string clip, float dur, float speed)
    {
        state = s;
        stateT = 0;
        hitDur = dur;
        move = null;
        if (feet != null) feet.SetSupport(true, true);
        anim.Play(clip, 0.05f, 0.03f, speed);
    }

    void Launch(float up)
    {
        vy = up;
        y = Mathf.Max(y, floorY + 0.02f);
        airT = 0;
        airDur = Mathf.Max(0.3f, 2 * up / 20f);
        state = State.Air;
        stateT = 0;
        var fb = Lib["FallingBack"];
        anim.Play("FallingBack", 0.06f, fb.impact * 0.3f, 0);
    }

    // Reserved for authored story beats.  Uses the same airborne state and mocap tumble as a
    // combat launcher, so cinematic impacts never bypass the animation/physics safety rules.
    public void CinematicLaunch(Vector3 direction, float up, float horizontalSpeed)
    {
        inputLocked = true;
        move = null;
        queued = null;
        if (feet != null) feet.ReleaseAll();
        direction.y = 0;
        knock = direction.sqrMagnitude > 0.001f ? direction.normalized * horizontalSpeed : Vector3.zero;
        Launch(up);
    }

    // Moros's swarm weapons and stone fists. Dashing through them is invulnerable; tapping block
    // just before a parryable weapon lands deflects it; holding guard chips; otherwise the hit
    // launches (up > 0) or staggers (up == 0) and can KO.
    public enum SwarmResult { Miss, Evaded, Parried, Blocked, Hit }
    // showcase recording: the scripted hero dodges by choreography, so hits never knock him off his
    // marks (grabs only land when the script asks for one)
    public static bool demoShield, demoAllowGrab;
    [NonSerialized] public float bossDamageScale = 1;
    [NonSerialized] public bool bossHeld;

    public SwarmResult SwarmStrike(Vector3 from, float damage, float up, float push, bool canParry = true, bool canBlock = true)
    {
        if (state == State.Dash)
        {
            if (foe != null) Evaded?.Invoke(foe, this);
            Callout?.Invoke(this, "EVADE!", new Color(0.4f, 0.85f, 1f));
            return SwarmResult.Evaded;
        }
        if (demoShield) return SwarmResult.Miss;
        if (!Hurtable) return SwarmResult.Miss;
        damage *= bossDamageScale;
        Vector3 dir = Flat(transform.position - from);
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : -transform.forward;
        Vector3 point = Body + Vector3.up * 1.1f;
        bool guarding = (state == State.Block || state == State.Blockstun) && Vector3.Dot(transform.forward, -dir) > 0.2f;
        if (guarding && canParry && Time.time - blockPressedAt <= 0.22f)
        {
            stamina = Mathf.Min(maxStamina, stamina + 20);
            spring.Kick(dir, point, 0.5f, false);
            Flash(new Color(0.6f, 0.8f, 1f));
            Enter(State.Block);
            if (foe != null) Parry?.Invoke(foe, this, point);
            Callout?.Invoke(this, "PARRY!", new Color(0.5f, 0.9f, 1f));
            return SwarmResult.Parried;
        }
        if (guarding && canBlock)
        {
            health = Mathf.Max(1, health - Hurt(Mathf.Round(damage * 0.15f)));
            Spend(damage * 1.6f);
            knock = dir * Mathf.Min(push * 0.5f, 1.4f);
            spring.Kick(dir, point, 0.8f, false);
            state = State.Blockstun; stateT = 0; hitDur = 0.25f;
            return SwarmResult.Blocked;
        }
        health = Mathf.Max(0, health - Hurt(damage));
        move = null; queued = null; buffered = null;
        Flash(new Color(0.55f, 0.04f, 0.02f));
        spring.Kick(dir, point, 0.8f, true);
        knock = dir * push / weight;
        if (health <= 0)
        {
            if (feet != null) feet.ReleaseAll();
            Launch(6.5f);
            KnockedOut?.Invoke(this);
            return SwarmResult.Hit;
        }
        if (up > 0)
        {
            if (feet != null) feet.ReleaseAll();
            Launch(up);
        }
        else React(State.Hitstun, "ReactLargeFront", 0.45f, 1.35f);
        return SwarmResult.Hit;
    }

    // gatling rounds: chip damage that never stunlocks - only every fifth round makes him flinch
    int bulletCount;
    public void BulletHit(Vector3 from, float damage)
    {
        if (state == State.Dash || demoShield) return;
        if (!Hurtable) return;
        Vector3 dir = Flat(transform.position - from);
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : -transform.forward;
        damage *= bossDamageScale;
        Vector3 point = Body + Vector3.up * 1.15f;
        bool guarding = (state == State.Block || state == State.Blockstun) && Vector3.Dot(transform.forward, -dir) > 0.2f;
        if (guarding) { Spend(damage * 1.2f); spring.Kick(dir, point, 0.15f, false); return; }
        health = Mathf.Max(0, health - Hurt(damage));
        Flash(new Color(0.55f, 0.04f, 0.02f));
        spring.Kick(dir, point, 0.25f, true);
        knock += dir * 0.3f;
        if (health <= 0)
        {
            if (feet != null) feet.ReleaseAll();
            Launch(5f);
            KnockedOut?.Invoke(this);
            return;
        }
        if (++bulletCount % 5 == 0 && (state == State.Neutral || state == State.Hitstun))
        {
            state = State.Hitstun;
            stateT = 0;
            hitDur = 0.22f;
            anim.Play(anim.Has("HeadHit") ? "HeadHit" : "HitHeadStraight", 0.05f, 0.05f, 1.5f);
        }
    }

    // Moros's gale inside a building shoves Kaito across the room / drags him to the door
    public void WindForce(Vector3 v, float dt)
    {
        if (state == State.Power || state == State.Thrown || state == State.WallRun || IsDown) return;
        transform.position += v * dt;
    }

    public void Chip(float dmg)
    {
        if (!Hurtable || demoShield) return;
        health = Mathf.Max(1, health - Hurt(dmg * bossDamageScale));
    }

    // Moros's giant stone hand: catch Kaito (grabs beat guard; only a dash escapes), hold him in
    // the fist while it squeezes, then release him into a slam / launch.
    public bool BossGrab()
    {
        if (state == State.Dash || !Hurtable || bossHeld) return false;
        if (demoShield && !demoAllowGrab) return false;
        move = null; queued = null; buffered = null;
        knock = Vector3.zero;
        if (feet != null) feet.ReleaseAll();
        thrower = null; grabbed = null;
        bossHeld = true;
        state = State.Thrown;
        stateT = 0;
        anim.Play("BigUppercutReact", 0.06f, 0.1f, 0.6f);
        Flash(new Color(0.55f, 0.04f, 0.02f));
        return true;
    }

    // swallowed whole: hidden inside the King Cobra until it spits him out
    public bool Hidden
    {
        set { if (renderers != null) foreach (var r in renderers) if (r) r.enabled = !value; }
    }

    public void BossHold(Vector3 p)
    {
        if (!bossHeld) return;
        transform.position = new Vector3(p.x, Mathf.Max(0, p.y), p.z);
        y = Mathf.Max(0, p.y);
    }

    public void BossSqueeze(float damage)
    {
        if (!bossHeld) return;
        health = Mathf.Max(1, health - Hurt(damage * bossDamageScale));
        Flash(new Color(0.55f, 0.04f, 0.02f));
        spring.Kick(transform.forward, Body + Vector3.up * 1.1f, 1.2f, true);
    }

    public void BossRelease(Vector3 dir, float damage, float up, float push)
    {
        if (!bossHeld) return;
        bossHeld = false;
        health = Mathf.Max(0, health - Hurt(damage * bossDamageScale));
        dir.y = 0;
        knock = dir.sqrMagnitude > 0.001f ? dir.normalized * push / weight : Vector3.zero;
        Flash(new Color(0.55f, 0.04f, 0.02f));
        Launch(health <= 0 ? 6.5f : Mathf.Max(0.5f, up));
        if (health <= 0) KnockedOut?.Invoke(this);
    }

    // Roc constructs can launch the player while leaving manual control available after landing.
    public void RocStrike(Vector3 direction, float up, float horizontalSpeed)
    {
        if (state == State.KO || state == State.Down || state == State.GetUp) return;
        health = Mathf.Max(1, health - Hurt(8f));
        move = null; queued = null; buffered = null;
        if (feet != null) feet.ReleaseAll();
        direction.y = 0;
        knock = direction.sqrMagnitude > 0.001f ? direction.normalized * horizontalSpeed : Vector3.zero;
        inputLocked = false;
        Launch(up);
    }

    void GetParried(Fighter by)
    {
        move = null; queued = null; buffered = null;
        knock = Flat(transform.position - by.transform.position).normalized * 1.2f;
        combo = 0;
        Spend(15);
        React(State.Parried, "ReactLargeFront", isBrute ? 0.55f : 0.62f, 1.5f);
    }

    // ------------------------------------------------------------------ throws (grab and slam)
    // The thrower plays the grab-and-slam mocap; the victim is held in front of them in a stagger
    // and, on the slam frame, driven into the floor as a knockdown. Both bodies stay on their own
    // roots the whole time, so nothing drifts or teleports.
    const float ThrowSpeed = 1.7f, ReachLead = 0.22f;             // clip seconds of reaching before contact
    const float GrabAt = ReachLead / ThrowSpeed;
    bool slammed;

    void StartThrow()
    {
        move = null;
        grabbed = null;
        grabConnected = false;
        slammed = false;
        Spend(ThrowCost);
        state = State.Throwing;
        stateT = 0;
        var e = Lib["GrabSlam"];
        anim.Play("GrabSlam", 0.06f, Mathf.Max(0, e.contact - ReachLead), ThrowSpeed);
        Swing?.Invoke(this, Moves.Jab);
    }

    void ThrowUpdate(float dt)
    {
        var e = Lib["GrabSlam"];
        if (!grabConnected)
        {
            if (stateT >= GrabAt && stateT - dt < GrabAt && foe != null)
            {
                float dist = Vector3.Distance(Flat(transform.position), Flat(foe.transform.position));
                bool grabbable = foe.state == State.Neutral || foe.state == State.Block || foe.state == State.Blockstun ||
                                 foe.state == State.Hitstun || foe.state == State.Parried ||
                                 (foe.state == State.Attack && foe.moveT < foe.move.activeStart);
                if (dist < 1.15f && grabbable && Facing(foe))
                {
                    grabConnected = true;
                    grabbed = foe;
                    foe.GetGrabbed(this);
                    Grabbed?.Invoke(this, foe);
                }
            }
            if (!grabConnected && stateT >= 0.45f) Enter(State.Neutral);   // whiffed grab: punishable recovery
            return;
        }

        if (grabbed != null && grabbed.state == State.Thrown)
        {
            // haul the victim off his feet and swing him over in an arc into the slam point
            float span = Mathf.Max(0.05f, e.impact - e.contact);
            float lift = Mathf.Clamp01((anim.Time - e.contact) / span);
            var hold = transform.position + transform.forward * Mathf.Lerp(0.62f, 1.3f, Mathf.SmoothStep(0, 1, lift));
            float k = 1 - Mathf.Exp(-16f * dt);
            var gp = grabbed.transform.position;
            grabbed.transform.position = Vector3.Lerp(new Vector3(gp.x, 0, gp.z), new Vector3(hold.x, 0, hold.z), k);
            grabbed.transform.rotation = Quaternion.Slerp(grabbed.transform.rotation, Quaternion.LookRotation(-transform.forward), k);
            grabbed.y = grabbed.floorY + Mathf.Sin(lift * Mathf.PI) * 1.1f;
            if (lift > 0.15f && grabbed.anim.Current != "FallingBack" && grabbed.anim.Has("FallingBack"))
            {
                // his fall starts in the air and hits its impact frame exactly on the slam
                var fb = Lib["FallingBack"];
                float left = (1 - lift) * span / ThrowSpeed;          // real seconds until the slam
                const float fallSpeed = 1.4f;
                grabbed.anim.Play("FallingBack", 0.1f, Mathf.Max(0, fb.impact - left * fallSpeed), fallSpeed);
            }
            if (stateT < GrabAt + 0.25f && Time.time - grabbed.breakPressedAt < 0.05f) { BreakThrow(); return; }
        }
        if (!slammed && anim.Time >= e.impact)
        {
            slammed = true;
            if (grabbed != null && grabbed.state == State.Thrown) grabbed.Slammed(this);
        }
        if ((slammed && anim.Time >= e.impact + 0.35f * ThrowSpeed) || anim.Finished)
        {
            grabbed = null;
            Enter(State.Neutral);
        }
    }

    void GetGrabbed(Fighter by)
    {
        move = null; queued = null; buffered = null;
        knock = Vector3.zero;
        thrower = by;
        state = State.Thrown;
        stateT = 0;
        anim.Play("BigUppercutReact", 0.06f, 0.1f, 0.6f);
    }

    void ThrownUpdate()
    {
        if (bossHeld) return;   // held in Moros's stone fist
        // never stay held if the thrower was interrupted
        if (thrower == null || thrower.state != State.Throwing) { thrower = null; Enter(State.Neutral); }
    }

    void Slammed(Fighter by)
    {
        float dmg = Mathf.Round(14 * by.damageMul);
        health = Mathf.Max(0, health - Hurt(dmg));
        by.combo = 1;
        by.comboDamage = dmg;
        thrower = null;
        Flash(new Color(0.55f, 0.04f, 0.02f));
        ThrowLanded?.Invoke(by, this, dmg);
        knock = by.transform.forward * 1.5f;
        juggles = MaxJuggles;   // a slam can't be juggled
        LandOnBack(anim.Current == "FallingBack");
        if (health <= 0) KnockedOut?.Invoke(this);
        else Callout?.Invoke(by, "SLAM!", new Color(1f, 0.6f, 0.3f));
    }

    void BreakThrow()
    {
        var v = grabbed;
        grabbed = null;
        knock = -transform.forward * 3f;
        v.knock = transform.forward * 3f;
        v.thrower = null;
        React(State.Hitstun, "ReactLargeFront", 0.3f, 1.8f);
        v.React(State.Hitstun, "ReactLargeFront", 0.3f, 1.8f);
        ThrowBroken?.Invoke(this, v);
        Callout?.Invoke(v, "THROW BREAK", Color.white);
    }

    // ------------------------------------------------------------------ parkour (boss fight in the town)
    float GroundHere() => freeRoam && TownMap.I != null ? TownMap.I.GroundAt(transform.position, y) : 0;

    string RunClip => anim.Has("FastRun") ? "FastRun" : "Run";

    // the clip's own ground speed, so the sprint plays at a speed that matches the body's (no skating)
    float ClipSpeed(string clip)
    {
        if (clipSpeed.TryGetValue(clip, out var v)) return v;
        var e = Lib[clip];
        float sp = e != null && e.clip != null ? new Vector3(e.clip.averageSpeed.x, 0, e.clip.averageSpeed.z).magnitude : 0;
        if (sp < 0.5f) sp = runSpeed;
        clipSpeed[clip] = sp;
        return sp;
    }

    // Space: run up a wall onto a roof when one is in front, otherwise leap (across the rooftops)
    bool TryParkour(Intent intent, bool canClimb = true)
    {
        Vector3 dir = intent.move.sqrMagnitude > 0.1f ? Flat(intent.move).normalized : Flat(transform.forward).normalized;
        if (canClimb && TownMap.I != null && TownMap.I.ClimbTarget(transform.position, dir, y, out var land, out var edge))
        {
            StartWallRun(dir, land, edge);
            return true;
        }
        if (stamina < 6) return false;
        Spend(6);
        bool roof = OnRoof;
        float speed = intent.move.sqrMagnitude > 0.1f ? runSpeed * (roof ? 1.3f : 1.05f) : 1.2f;
        BeginAir(dir * speed, roof ? 7.6f : 6.4f, "RunningJump");
        transform.rotation = Quaternion.LookRotation(dir);
        if (roof) Callout?.Invoke(this, "LEAP!", new Color(0.5f, 0.85f, 1f));
        return true;
    }

    public void BeginAir(Vector3 horizontal, float up, string clip)
    {
        move = null; queued = null;
        state = State.Leap;
        stateT = 0;
        parkT = 0;
        leapVel = Flat(horizontal);
        vy = up;
        fallFrom = y;
        running = false;
        if (feet != null) feet.ReleaseAll();
        if (anim.Has(clip))
        {
            var e = Lib[clip];
            // the sprint jump is short: take off straight from its first stride
            float start = clip == "RunningJump" ? e.length * 0.1f : 0;
            anim.Play(clip, 0.1f, start, clip == "RunningJump" ? 1.1f : 1f, loop: clip == "FallingIdle");
        }
    }

    void LeapUpdate(Intent intent, float dt)
    {
        parkT += dt;
        // a little air control
        Vector3 want = Flat(intent.move);
        if (want.sqrMagnitude > 0.04f)
            leapVel = Vector3.MoveTowards(leapVel, want.normalized * Mathf.Max(leapVel.magnitude, runSpeed * 0.6f), 7f * dt);
        transform.position += leapVel * dt;
        if (leapVel.sqrMagnitude > 0.3f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(leapVel), 1 - Mathf.Exp(-8 * dt));
        float g = GroundHere();
        vy -= 17f * dt;
        y += vy * dt;
        fallFrom = Mathf.Max(fallFrom, y);
        if (vy < -2f && anim.Current != "FallingIdle" && y - g > 1.4f && anim.Has("FallingIdle"))
            anim.Play("FallingIdle", 0.3f, 0, 1, loop: true);
        if (vy <= 0 && y <= g)
        {
            y = g;
            floorY = g;
            float drop = fallFrom - g;
            if (drop > 3.2f && anim.Has("HardLanding"))
            {
                // heavy landing from a roof: absorb it, dust, a short recovery
                state = State.Hitstun;
                stateT = 0;
                hitDur = 0.36f;
                var e = Lib["HardLanding"];
                anim.Play("HardLanding", 0.05f, Mathf.Max(0, e.impact - 0.1f), 1.35f);
                FightCamera.Shake(0.18f);
                Knocked?.Invoke(this);
            }
            else Enter(State.Neutral);
        }
    }

    void StartWallRun(Vector3 dir, Vector3 land, float edgeY)
    {
        move = null; queued = null;
        state = State.WallRun;
        stateT = 0;
        parkT = 0;
        parkPhase = 0;
        wallDir = dir;
        wallLand = land;
        wallTop = edgeY;
        running = false;
        knock = Vector3.zero;
        if (feet != null) feet.ReleaseAll();
        anim.Play(RunClip, 0.08f, 0, 1.45f, loop: true);
        Callout?.Invoke(this, "WALL RUN!", new Color(0.5f, 0.85f, 1f));
    }

    void WallRunUpdate(float dt)
    {
        if (parkPhase == 0)
        {
            // sprint straight up the wall face, body flat out from the wall
            y = Mathf.MoveTowards(y, wallTop - 0.9f, 11f * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(Vector3.up, -wallDir), 1 - Mathf.Exp(-20 * dt));
            if (y >= wallTop - 0.92f || stateT > 1.4f)
            {
                parkPhase = 1;
                parkT = 0;
                flipFrom = transform.position;
                if (anim.Has("FrontFlip"))
                {
                    var e = Lib["FrontFlip"];
                    anim.Play("FrontFlip", 0.06f, e.length * 0.18f, e.length * 0.7f / 0.6f);
                }
            }
            return;
        }
        // flip over the eave and land on the roof
        parkT += dt;
        float u = Mathf.Clamp01(parkT / 0.6f);
        float k = u * u * (3 - 2 * u);
        var p = Vector3.Lerp(Flat(flipFrom), Flat(wallLand), k);
        transform.position = new Vector3(p.x, transform.position.y, p.z);
        y = Mathf.Lerp(wallTop - 0.9f, wallLand.y, k) + Mathf.Sin(u * Mathf.PI) * 1.5f;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(wallDir), 1 - Mathf.Exp(-14 * dt));
        if (u >= 1)
        {
            y = wallLand.y;
            floorY = y;
            Enter(State.Neutral);
        }
    }

    // cut to a new scene (showcase)
    public void Teleport(Vector3 p, Vector3 face)
    {
        move = null; queued = null; buffered = null;
        knock = Vector3.zero; grabbed = null; thrower = null; bossHeld = false;
        y = p.y; floorY = p.y; vy = 0;
        transform.position = p;
        face.y = 0;
        if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);
        if (feet != null) feet.ReleaseAll();
        state = State.Hitstun;   // so Enter below restarts the idle
        Enter(State.Neutral);
    }

    // scripted hero powers (HeroPowers) take the body over
    public void EnterPower()
    {
        move = null; queued = null; buffered = null;
        knock = Vector3.zero;
        running = false;
        state = State.Power;
        stateT = 0;
        if (feet != null) feet.ReleaseAll();
    }

    public void ExitPower()
    {
        if (state != State.Power) return;
        floorY = GroundHere();
        if (y - floorY > 0.3f) BeginAir(Vector3.zero, 0, "FallingIdle");
        else { y = floorY; Enter(State.Neutral); }
    }

    public void PlayClip(string clip, float fade, float start, float speed, bool loop = false) => anim.Play(clip, fade, start, speed, loop);

    // ------------------------------------------------------------------ air / down / get up
    void AirUpdate(float dt)
    {
        airT += dt;
        vy -= (juggles > 0 || vy > 0 ? 19f : 22f) * dt;
        y += vy * dt;
        var fb = Lib["FallingBack"];
        // hold the body in the tumbling part of the fall while airborne
        anim.SetTime(Mathf.Lerp(fb.impact * 0.3f, fb.impact * 0.75f, Mathf.Clamp01(airT / airDur)));
        float g = GroundHere();
        if (y <= g && vy < 0)
        {
            y = g;
            knock *= 0.3f;
            juggles = 0;
            Knocked?.Invoke(this);
            LandOnBack();
        }
    }

    // Finish the fall onto the back and lie there.
    void LandOnBack(bool alreadyFalling = false)
    {
        y = GroundHere();
        floorY = y;
        vy = 0;
        state = health <= 0 ? State.KO : State.Down;
        stateT = 0;
        if (alreadyFalling) return;   // thrown: the fall is already playing through its landing
        var fb = Lib["FallingBack"];
        anim.Play("FallingBack", 0.08f, fb.impact * 0.8f, 1.6f);
    }

    void StartGetUp()
    {
        state = State.GetUp;
        stateT = 0;
        var gu = Lib["GettingUp"];
        const float spd = 2.4f;
        float from = Mathf.Max(0, gu.impact - 0.15f);
        float to = gu.recover > gu.impact ? gu.recover : gu.length;
        getUpDur = (to - from) / spd;
        anim.Play("GettingUp", 0.12f, from, spd);
    }

    // Keep lying / falling bodies above the floor: lift the whole character if any bone dips under it.
    static readonly HumanBodyBones[] GroundBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
    };
    float groundLift;

    void LateUpdate()
    {
        if (animator == null) return;
        bool lowPose = state == State.Air || state == State.Down || state == State.KO || state == State.GetUp || state == State.Thrown;
        float want = 0;
        if (lowPose)
        {
            // lowest bone height (the root is back at its true height here); bones sit ~6 cm inside the skin
            float min = float.MaxValue;
            foreach (var b in GroundBones)
            {
                var t = animator.GetBoneTransform(b);
                if (t) min = Mathf.Min(min, t.position.y);
            }
            want = Mathf.Max(0, floorY + 0.07f - min);
        }
        groundLift = Mathf.Min(Mathf.MoveTowards(groundLift, want, Time.deltaTime * 4f), 0.6f);
        if (groundLift > 0.0001f) transform.position = new Vector3(transform.position.x, y + groundLift, transform.position.z);
    }

    // ------------------------------------------------------------------ spacing
    void Separate()
    {
        if (foe == null || state == State.Thrown || state == State.Throwing && grabConnected || state == State.Power) return;
        if (Mathf.Abs(transform.position.y - foe.transform.position.y) > 1.2f) return;
        if (foe.state == State.Thrown || foe.state == State.Throwing && foe.grabConnected) return;
        Vector3 d = Flat(transform.position - foe.transform.position);
        float min = 0.55f + (bodyRadius + foe.bodyRadius) * 0.6f;
        if (IsDown || foe.IsDown) min = 0.35f;
        float dist = d.magnitude;
        if (dist < min && dist > 0.0001f)
            transform.position += d / dist * (min - dist) * 0.5f;
    }

    void ClampToArena()
    {
        if (TownMap.I != null)
        {
            // buildings, walls, trees and lanterns are solid
            var q = TownMap.I.Resolve(transform.position, bodyRadius + 0.08f, y);
            if (!freeRoam)
            {
                var f = Flat(q);
                if (f.magnitude > ArenaRadius) { f = f.normalized * ArenaRadius; q = new Vector3(f.x, q.y, f.z); }
            }
            if ((q - transform.position).sqrMagnitude > 1e-8f)
            {
                transform.position = new Vector3(q.x, transform.position.y, q.z);
                if (!freeRoam) knock = Vector3.zero;
            }
            return;
        }
        var p = Flat(transform.position);
        if (p.magnitude > ArenaRadius)
        {
            p = p.normalized * ArenaRadius;
            transform.position = new Vector3(p.x, transform.position.y, p.z);
            knock = Vector3.zero;
        }
    }

    void FaceFoe(float dt)
    {
        if (foe == null || running) return;
        bool track = state == State.Neutral || state == State.Dash || state == State.Hitstun || state == State.Intro ||
                     state == State.Block || state == State.Blockstun ||
                     (state == State.Throwing && !grabConnected && stateT < GrabAt) ||
                     (state == State.Attack && moveT < move.activeStart - 0.05f);
        if (!track) return;
        Vector3 d = Flat(foe.transform.position - transform.position);
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1 - Mathf.Exp(-14f * dt));
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    // ------------------------------------------------------------------ hit flash
    void Flash(Color c) { flash = 1; flashColor = c; }

    void UpdateFlash()
    {
        if (flash <= 0) return;
        flash = Mathf.Max(0, flash - Clock.Dt * 8f);
        var c = Color.Lerp(Color.black, flashColor, flash * flash);
        mpb.SetColor("_EmissionColor", c);
        foreach (var r in renderers) r.SetPropertyBlock(mpb);
    }
}
