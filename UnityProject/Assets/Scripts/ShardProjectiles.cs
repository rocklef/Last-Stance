using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The ammunition of Moros's arsenal, made of the same stone chips as his swarm and drawn with GPU
// instancing: siege-cannon balls (a tumbling sphere of stones on a ballistic arc), gatling rounds
// (tiny chip bursts) and homing rockets (a stone warhead with an ember core and a fire trail).
// Buildings, walls and trees stop everything (hide behind them), explosions hurt in a radius,
// Kaito's Thunder Palm wipes projectiles out of the air.
public class ShardProjectiles : MonoBehaviour
{
    public enum Kind { Ball, Rocket, Bullet }

    class Shot
    {
        public Kind kind;
        public Vector3 pos, vel;
        public float age, life, damage, blast, spin;
        public bool alive = true;
        public int seed;
    }

    readonly List<Shot> shots = new List<Shot>();
    Mesh mesh;
    Material[] mats;
    Matrix4x4[][] batch;
    int[] counts;
    Fighter hero;
    System.Func<Vector3, float, float, float, bool, bool, Fighter.SwarmResult> strike;
    public int Live => shots.Count;
    int gunCount;

    const float Gravity = 14f;

    public void Init(Material[] materials, Fighter target, System.Func<Vector3, float, float, float, bool, bool, Fighter.SwarmResult> onStrike)
    {
        mats = materials;
        hero = target;
        strike = onStrike;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        batch = new Matrix4x4[mats.Length][];
        counts = new int[mats.Length];
        for (int m = 0; m < mats.Length; m++) batch[m] = new Matrix4x4[1023];
    }

    // cannonball: flies a ballistic arc from `from` so it lands on `target` after `flight` seconds
    public void FireBall(Vector3 from, Vector3 target, float flight, float damage, float blast)
    {
        Vector3 v = (target - from - 0.5f * Vector3.down * Gravity * flight * flight) / flight;
        shots.Add(new Shot { kind = Kind.Ball, pos = from, vel = v, life = flight + 1.5f, damage = damage, blast = blast, seed = Random.Range(0, 9999), spin = Random.Range(3f, 6f) });
    }

    public void FireRocket(Vector3 from, Vector3 dir, float speed, float damage, float blast)
    {
        shots.Add(new Shot { kind = Kind.Rocket, pos = from, vel = dir.normalized * speed, life = 5.5f, damage = damage, blast = blast, seed = Random.Range(0, 9999) });
        SoundStage.At(AudioSynth.Rocket, from, 0.55f, Random.Range(0.9f, 1.2f));
    }

    public void FireBullet(Vector3 from, Vector3 dir, float speed, float damage)
    {
        shots.Add(new Shot { kind = Kind.Bullet, pos = from, vel = dir.normalized * speed, life = 1.6f, damage = damage, seed = Random.Range(0, 9999) });
        if (++gunCount % 2 == 0) SoundStage.At(AudioSynth.GunTick, from, 0.5f, Random.Range(0.85f, 1.15f));
    }

    public void Clear() => shots.Clear();

    // Thunder Palm: everything within the shockwave turns to dust
    public int ClearWithin(Vector3 c, float radius)
    {
        int n = 0;
        foreach (var s in shots)
            if (s.alive && (s.pos - c).sqrMagnitude < radius * radius)
            {
                s.alive = false;
                n++;
                if (GameManager.I) GameManager.I.fx.Energy(s.pos, Vector3.up, 8, new Color(0.6f, 0.85f, 1f), Color.white, 2f, 6f);
            }
        shots.RemoveAll(s => !s.alive);
        return n;
    }

    Vector3 HeroChest => hero.Body + Vector3.up * 1.1f;

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0 || hero == null) { Draw(); return; }
        var fx = GameManager.I ? GameManager.I.fx : null;
        for (int i = 0; i < shots.Count; i++)
        {
            var s = shots[i];
            if (!s.alive) continue;
            s.age += dt;
            Vector3 prev = s.pos;
            switch (s.kind)
            {
                case Kind.Ball:
                    s.vel += Vector3.down * Gravity * dt;
                    break;
                case Kind.Rocket:
                {
                    // boost out of the tube, then home in on Kaito with a limited turn rate
                    float speed = Mathf.Min(24f, s.vel.magnitude + 10f * dt);
                    if (s.age > 0.45f)
                    {
                        Vector3 to = HeroChest - s.pos;
                        Vector3 want = to.sqrMagnitude > 0.01f ? to.normalized : s.vel.normalized;
                        s.vel = Vector3.RotateTowards(s.vel.normalized, want, 2.3f * dt, 0) * speed;
                    }
                    else s.vel = s.vel.normalized * speed;
                    if (fx && Random.value < 0.8f) fx.Embers(s.pos - s.vel.normalized * 0.45f, -s.vel * 0.15f, 1, 0.14f);
                    break;
                }
            }
            s.pos += s.vel * dt;

            // buildings / walls / trees are cover
            if (Physics.Linecast(prev, s.pos, out var hit, TownMap.SolidMask, QueryTriggerInteraction.Ignore))
            {
                s.pos = hit.point;
                Explode(s);
                continue;
            }
            // Kaito
            float hr = s.kind == Kind.Bullet ? 0.45f : s.kind == Kind.Rocket ? 0.8f : 0.95f;
            if ((s.pos - HeroChest).sqrMagnitude < hr * hr)
            {
                if (s.kind == Kind.Bullet)
                {
                    hero.BulletHit(s.pos - s.vel.normalized * 2f, s.damage);
                    if (fx) fx.Energy(s.pos, -s.vel.normalized, 4, new Color(1f, 0.8f, 0.5f), Color.white, 2f, 5f);
                    s.alive = false;
                }
                else Explode(s);
                continue;
            }
            // the ground (or a roof)
            float floor = TownMap.I != null ? Mathf.Max(0, TownMap.I.GroundAt(s.pos, s.pos.y)) : 0;
            if (s.pos.y <= floor + 0.1f || s.age > s.life)
            {
                if (s.kind == Kind.Bullet) { if (fx && Random.value < 0.3f) fx.Energy(s.pos, Vector3.up, 3, new Color(0.8f, 0.75f, 0.7f), Color.white, 1f, 3f); s.alive = false; }
                else Explode(s);
            }
        }
        shots.RemoveAll(s => !s.alive);
        Draw();
    }

    void Explode(Shot s)
    {
        s.alive = false;
        if (s.kind == Kind.Bullet) return;
        SoundStage.Explosion(s.pos, s.blast);
        var fx = GameManager.I ? GameManager.I.fx : null;
        if (fx)
        {
            fx.Impact(s.pos, s.blast * 1.6f, s.kind == Kind.Ball ? 0.5f : 0.75f);
            fx.Embers(s.pos + Vector3.up * 0.3f, Vector3.up * 2f, s.kind == Kind.Ball ? 26 : 14, 0.35f);
            fx.Pulse(s.pos + Vector3.up * 0.1f, s.blast * 2.4f, new Color(1f, 0.55f, 0.25f, 0.9f));
            for (int k = 0; k < 4; k++) fx.Puff(s.pos + Random.insideUnitSphere * s.blast * 0.5f, 1.2f);
        }
        FightCamera.Shake(s.kind == Kind.Ball ? 0.55f : 0.25f);
        // blast damage (walls between the blast and Kaito shield him)
        float d = Vector3.Distance(s.pos, HeroChest);
        if (d < s.blast + 0.6f && !TownMap.Blocked(s.pos + Vector3.up * 0.3f, HeroChest))
        {
            float k = Mathf.Clamp01(1.2f - d / (s.blast + 0.6f));
            strike?.Invoke(s.pos, s.damage * k, s.kind == Kind.Ball ? 5f : 3f, s.kind == Kind.Ball ? 5f : 3f, false, true);
        }
    }

    // ------------------------------------------------------------------ drawing
    void Add(int m, Vector3 p, Quaternion q, Vector3 scale)
    {
        if (m >= counts.Length || counts[m] >= 1023) return;
        batch[m][counts[m]++] = Matrix4x4.TRS(p, q, scale);
    }

    void Draw()
    {
        if (mats == null) return;
        for (int m = 0; m < counts.Length; m++) counts[m] = 0;
        float t = Time.time;
        foreach (var s in shots)
        {
            if (!s.alive) continue;
            var rng = new System.Random(s.seed);
            float R() => (float)rng.NextDouble();
            Vector3 f = s.vel.sqrMagnitude > 1e-4f ? s.vel.normalized : Vector3.forward;
            switch (s.kind)
            {
                case Kind.Ball:
                {
                    // a tumbling sphere of stones with an ember heart
                    var spin = Quaternion.AngleAxis(t * s.spin * 60f, Vector3.Cross(f, Vector3.up).normalized + Vector3.up * 0.01f);
                    for (int k = 0; k < 90; k++)
                    {
                        Vector3 n = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1).normalized;
                        float sz = 0.14f + 0.12f * R();
                        Add(R() < 0.8f ? 0 : 3, s.pos + spin * n * 0.62f, spin * Quaternion.LookRotation(n), new Vector3(sz, sz * 0.6f, sz * 1.2f));
                    }
                    Add(2, s.pos, Quaternion.identity, Vector3.one * 0.55f);
                    break;
                }
                case Kind.Rocket:
                {
                    var q = Quaternion.LookRotation(f);
                    for (int k = 0; k < 22; k++)
                    {
                        float u = R();
                        float rad = Mathf.Lerp(0.2f, 0.05f, Mathf.Pow(u, 2));
                        float a = R() * Mathf.PI * 2;
                        Vector3 local = new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, (u - 0.4f) * 1.0f);
                        Add(u > 0.85f ? 1 : 3, s.pos + q * local, q, new Vector3(0.09f, 0.06f, 0.22f));
                    }
                    Add(2, s.pos - f * 0.55f, q, new Vector3(0.18f, 0.18f, 0.35f + 0.15f * Mathf.Sin(t * 40)));
                    break;
                }
                default:
                {
                    var q = Quaternion.LookRotation(f);
                    for (int k = 0; k < 3; k++) Add(k == 0 ? 2 : 0, s.pos - f * k * 0.18f, q, new Vector3(0.05f, 0.05f, 0.22f));
                    break;
                }
            }
        }
        for (int m = 0; m < mats.Length; m++)
        {
            if (counts[m] == 0 || mats[m] == null) continue;
            var rp = new RenderParams(mats[m])
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 320),
                shadowCastingMode = m == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On,
                receiveShadows = true,
            };
            Graphics.RenderMeshInstanced(rp, mesh, 0, batch[m], counts[m]);
        }
    }
}
