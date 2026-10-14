using UnityEngine;

// Makes blows land like an action film. Every heavy hit kicks up a ring of dust and flying stone,
// punches the camera in and throws a shock ring; a parry snaps time into slow motion and opens a
// COUNTER window - the next hit lands as a slow-motion counter blow with a flash; finishers
// (knockouts, knockdowns, long combos) get a heartbeat of slow motion; throws slam into the ground.
// Listens to the fighters' events, so it works in the Brakka round and the Moros fight alike.
public class ActionFX : MonoBehaviour
{
    float counterUntil, slowReady;
    static readonly Color Gold = new Color(1f, 0.82f, 0.4f), Stone = new Color(0.72f, 0.66f, 0.58f), DarkStone = new Color(0.35f, 0.31f, 0.28f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init() => new GameObject("ActionFX").AddComponent<ActionFX>();

    void OnEnable()
    {
        Fighter.Hit += OnHit;
        Fighter.Parry += OnParry;
        Fighter.ThrowLanded += OnThrow;
        Fighter.Knocked += OnKnocked;
    }

    void OnDisable()
    {
        Fighter.Hit -= OnHit;
        Fighter.Parry -= OnParry;
        Fighter.ThrowLanded -= OnThrow;
        Fighter.Knocked -= OnKnocked;
    }

    static FX Fx => GameManager.I != null ? GameManager.I.fx : null;
    static Fighter Hero => GameManager.I != null ? GameManager.I.p1 : null;
    static Vector3 Feet(Fighter f) => new Vector3(f.transform.position.x, f.floorY + 0.05f, f.transform.position.z);

    // a ring of dust and flying stone around a point on the ground
    public static void DustRing(Vector3 c, float radius, int puffs, float size)
    {
        var fx = Fx;
        if (fx == null) return;
        for (int k = 0; k < puffs; k++)
        {
            float a = k / (float)puffs * Mathf.PI * 2 + Random.value * 0.4f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            fx.Puff(c + d * radius * Random.Range(0.6f, 1.1f) + Vector3.up * 0.15f, size * Random.Range(0.8f, 1.3f));
        }
        fx.Energy(c + Vector3.up * 0.2f, Vector3.up, puffs, Stone, DarkStone, 2.5f, 6f + radius);
        fx.Pulse(c, radius * 3.2f, new Color(0.85f, 0.78f, 0.68f, 0.55f), 0.35f);
    }

    void OnParry(Fighter attacker, Fighter parrier, Vector3 p)
    {
        var fx = Fx;
        if (fx == null) return;
        fx.Energy(p, Vector3.up, 16, Gold, Color.white, 3f, 9f);
        fx.Pulse(p, 3.5f, Gold, 0.22f);
        if (parrier == Hero)
        {
            counterUntil = Clock.Now + 1.0f;   // the counter window
            TimeFX.SlowFor(0.3f, 0.3f);
            FightCamera.Punch(0.6f);
        }
    }

    void OnHit(Fighter a, Fighter v, Move m, float dmg, Vector3 p)
    {
        var fx = Fx;
        if (fx == null || v == null) return;
        bool big = m.heavy || m.knockdown || a.combo >= 4;
        if (big)
        {
            DustRing(Feet(v), 0.9f, 10, 1.1f);
            Vector3 dir = (v.transform.position - a.transform.position);
            dir.y = 0;
            fx.Energy(p, (dir.normalized + Vector3.up * 0.6f).normalized, 12, Stone, Color.white, 4f, 10f);
            FightCamera.Punch(0.35f);
        }
        bool counter = a == Hero && Clock.Now < counterUntil;
        bool finisher = v.health <= 0 || m.knockdown || a.combo >= 5;
        if ((counter || finisher) && Clock.Now >= slowReady)
        {
            // the cinematic beat
            slowReady = Clock.Now + 1.4f;
            TimeFX.SlowFor(0.22f, counter ? 0.45f : 0.32f);
            fx.Chroma(0.8f);
            fx.Pulse(p, 6f, Color.white, 0.25f);
            fx.Pulse(p, 11f, counter ? Gold : new Color(1f, 0.6f, 0.3f, 0.8f), 0.4f);
            DustRing(Feet(v), 1.6f, 16, 1.6f);
            FightCamera.Punch(1f);
            FightCamera.Shake(0.35f);
            if (counter) counterUntil = 0;
            if (MobileControls.On && a == Hero) Haptics.Pulse(45, 200);
        }
    }

    void OnThrow(Fighter thrower, Fighter victim, float dmg)
    {
        var fx = Fx;
        if (fx == null || victim == null) return;
        DustRing(Feet(victim), 1.4f, 16, 1.5f);
        fx.Crack(Feet(victim), 0.8f);
        fx.Impact(Feet(victim), 2.5f, 0.9f);
        TimeFX.SlowFor(0.35f, 0.25f);   // the slam lands in slow motion
        FightCamera.Shake(0.4f);
        FightCamera.Punch(0.6f);
    }

    void OnKnocked(Fighter f)
    {
        if (f != null) DustRing(Feet(f), 1.0f, 8, 1.2f);
    }
}
