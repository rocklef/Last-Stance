using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

// Moros's weapon: thousands of courtyard stone chips, roof-tile shards and embers flying as one
// living swarm, drawn with GPU instancing (no GameObject per shard).
//
// Every shard chases its own target point. Loose modes (halo, serpent, flood, twister, wind,
// cluster) use under-damped springs with per-shard stiffness so the swarm smears and trails like a
// flock. FORM mode is the transformation: MonkBoss describes an object (axe, hand, spears, bird,
// hammer, titan, dragon, cannon...) as world-space primitives each frame, every shard is assigned
// a spot on it, streams over from wherever it is (staggered, so you watch the object assemble),
// then locks on with velocity feed-forward so the object stays crisp while it swings and strikes.
//
// The per-shard simulation runs as a parallel job across every CPU core, which is what lets the
// swarm carry many more pieces. Each shard is drawn as one of several faceted rock chunks with a
// stone texture, and fast-moving shards kick up dust (embers leave sparks).
[DefaultExecutionOrder(20)]
public class ShardSwarm : MonoBehaviour
{
    public enum Mode { Halo, Serpent, Flood, Twister, Form, Free, Wind, Cluster }
    public Mode mode = Mode.Free;

    // written by MonkBoss
    [HideInInspector] public Vector3 center;           // halo / twister centre (Moros)
    [HideInInspector] public Vector3 haloBack = Vector3.back;
    [HideInInspector] public Vector3 head;              // serpent head
    [HideInInspector] public Vector3 floodOrigin, floodDir;
    [HideInInspector] public float floodFront;
    [HideInInspector] public int active;               // shards in play (the rest lie on the floor)
    [HideInInspector] public float twisterHeight = 4.6f, twisterWidth = 0.32f;
    [HideInInspector] public float chipScale = 1;       // shard size multiplier (big forms use chunkier stones)
    [HideInInspector] public float boundRadius = 12f;   // shards never leave this radius (the town is bigger)
    [HideInInspector] public float trailStep = 0.08f;   // serpent body spacing (shorter = tighter serpent)
    // WIND: a gale streaming from windFrom through a doorway (windDoor) across a room to windTo;
    // windSpeed < 0 runs it backwards (the gale that drags Kaito out)
    [HideInInspector] public Vector3 windFrom, windDoor, windTo;
    [HideInInspector] public float windWidth = 4f, windSpeed = 1f;
    // CLUSTER: a small buzzing swarm around head - 0 ball, 1 spinning ring, 2 whirling column
    [HideInInspector] public float clusterRadius = 0.6f;
    [HideInInspector] public int clusterShape;
    // while a big form assembles, the shards not yet built into it swirl in a storm around it
    // (from stormBase up) instead of waiting in the halo
    [HideInInspector] public bool reservoirStorm;
    [HideInInspector] public Vector3 stormCenter;
    [HideInInspector] public float stormBase;
    [HideInInspector] public float dust = 1;           // dust / ember trail amount (0 = none)
    // shards locked into a form are stretched along it (the giant's long steel shards); 1 = off
    [HideInInspector] public float formStretch = 1;
    // parts marked sink: the shards land on them, then fuse into the solid body that takes their
    // place (the King Cobra's skin) - they shrink away and ride along hidden until this is cleared
    [HideInInspector] public bool formSink;
    public System.Action<ShardSwarm> builder;           // fills parts / headParts every frame
    public readonly List<ShardPart> parts = new List<ShardPart>();
    public readonly List<ShardPart> headParts = new List<ShardPart>();
    public int Count => count;
    public Vector3 HeadForward { get; private set; } = Vector3.forward;

    const int Variants = 5;
    static Mesh[] rocks;
    Material[] mats;
    int count;
    // per-shard state (shared with the simulation job)
    NativeArray<Vector3> pos, vel, scale, axis, prevTarget;
    NativeArray<Quaternion> rot;
    NativeArray<float> k, r1, r2, r3, r4, r5, spin;
    NativeArray<int> matIndex, drawMat;
    NativeArray<Matrix4x4> xform;
    // formation data handed to the job each frame
    NativeArray<ShardPart> nParts, nHead;
    NativeArray<float> nCum, nHeadCum;
    NativeArray<Vector3> trail;
    Matrix4x4[][] batches;
    int[] batchCount;
    float chip = 1;
    int dustCursor;

    // formation state
    float formAge, formBuild = 0.9f;
    bool orderedBuild;

    const int TrailLen = 140;
    int trailHead, trailCount;

    public void Init(Material[] materials, int shardCount, Vector3 scatterCentre, float scatterRadius)
    {
        mats = materials;
        count = shardCount;
        active = count;
        if (rocks == null) rocks = MakeRocks();

        var P = Allocator.Persistent;
        pos = new NativeArray<Vector3>(count, P); vel = new NativeArray<Vector3>(count, P);
        scale = new NativeArray<Vector3>(count, P); axis = new NativeArray<Vector3>(count, P);
        prevTarget = new NativeArray<Vector3>(count, P);
        rot = new NativeArray<Quaternion>(count, P);
        k = new NativeArray<float>(count, P); r1 = new NativeArray<float>(count, P); r2 = new NativeArray<float>(count, P);
        r3 = new NativeArray<float>(count, P); r4 = new NativeArray<float>(count, P); r5 = new NativeArray<float>(count, P);
        spin = new NativeArray<float>(count, P);
        matIndex = new NativeArray<int>(count, P); drawMat = new NativeArray<int>(count, P);
        xform = new NativeArray<Matrix4x4>(count, P);
        nParts = new NativeArray<ShardPart>(64, P); nHead = new NativeArray<ShardPart>(16, P);
        nCum = new NativeArray<float>(64, P); nHeadCum = new NativeArray<float>(16, P);
        trail = new NativeArray<Vector3>(TrailLen, P);

        var rng = new System.Random(11);
        float R() => (float)rng.NextDouble();
        for (int i = 0; i < count; i++)
        {
            r1[i] = R(); r2[i] = R(); r3[i] = R(); r4[i] = R(); r5[i] = R();
            k[i] = Mathf.Lerp(0.55f, 1.45f, R());
            float sz = Mathf.Lerp(0.7f, 1.4f, R() * R()) * 1.15f;   // rocks are rounder than cubes: a little bigger
            // flat, broken-flagstone chips
            scale[i] = new Vector3(0.06f + 0.1f * R(), 0.03f + 0.035f * R(), 0.06f + 0.12f * R()) * sz;
            int m = R() < 0.8f ? 0 : (mats.Length > 1 ? 1 : 0);
            if (mats.Length > 2 && R() < 0.05f) m = 2;      // glowing embers
            matIndex[i] = m;
            axis[i] = Random.onUnitSphere;
            spin[i] = Mathf.Lerp(120, 720, R());
            rot[i] = Random.rotation;
            var c = Random.insideUnitCircle * scatterRadius;
            pos[i] = scatterCentre + new Vector3(c.x, scale[i].y * 0.5f, c.y);
        }
        batches = new Matrix4x4[mats.Length * Variants][];
        batchCount = new int[mats.Length * Variants];
        for (int b = 0; b < batches.Length; b++) batches[b] = new Matrix4x4[count / Variants + 1];
        ResetTrail(scatterCentre);
    }

    void OnDestroy()
    {
        if (!pos.IsCreated) return;
        pos.Dispose(); vel.Dispose(); scale.Dispose(); axis.Dispose(); prevTarget.Dispose(); rot.Dispose();
        k.Dispose(); r1.Dispose(); r2.Dispose(); r3.Dispose(); r4.Dispose(); r5.Dispose(); spin.Dispose();
        matIndex.Dispose(); drawMat.Dispose(); xform.Dispose();
        nParts.Dispose(); nHead.Dispose(); nCum.Dispose(); nHeadCum.Dispose(); trail.Dispose();
    }

    // Start assembling a new object. buildTime spreads the shards' arrival; ordered builds the
    // parts in the order they were listed (a titan rises from its feet).
    public void BeginForm(float buildTime, bool ordered = false)
    {
        mode = Mode.Form;
        formAge = 0;
        formBuild = buildTime;
        orderedBuild = ordered;
        for (int i = 0; i < count; i++) prevTarget[i] = pos[i];
    }

    public void ResetTrail(Vector3 p)
    {
        for (int i = 0; i < TrailLen; i++) PushTrail(p);
        trailCount = TrailLen;
    }

    void PushTrail(Vector3 p)
    {
        trailHead = (trailHead + 1) % TrailLen;
        trail[trailHead] = p;
        trailCount = Mathf.Min(trailCount + 1, TrailLen);
    }

    static Vector3 TrailAt(NativeArray<Vector3> trail, int head, int n, float idx)
    {
        idx = Mathf.Clamp(idx, 0, n - 1.001f);
        int a = Mathf.FloorToInt(idx);
        float f = idx - a;
        int ia = (head - a + TrailLen) % TrailLen;
        int ib = (ia - 1 + TrailLen) % TrailLen;
        return Vector3.Lerp(trail[ia], trail[ib], f);
    }

    // Blow shards outward from a point (weapons shattering, impacts, Moros breaking apart).
    public void Burst(Vector3 from, float power, float lift, float radius = 99)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 d = pos[i] - from;
            float m = d.magnitude;
            if (m > radius) continue;
            Vector3 n = m > 0.01f ? d / m : Random.onUnitSphere;
            vel[i] += (n + Random.insideUnitSphere * 0.6f) * power * Mathf.Lerp(0.4f, 1.2f, r2[i]) + Vector3.up * lift * r3[i];
        }
        dustBoost = 0.6f;
    }

    // Pull only the shards in play to a point (the drill bursting out of the ground)
    public void GatherActive(Vector3 p, float radius)
    {
        for (int i = 0; i < active && i < count; i++) { pos[i] = p + Random.insideUnitSphere * radius; vel[i] = Vector3.zero; }
    }

    // Pull every shard into a point (Moros's body) - used right before he shatters.
    public void Gather(Vector3 p, float radius)
    {
        for (int i = 0; i < count; i++) { pos[i] = p + Random.insideUnitSphere * radius; vel[i] = Vector3.zero; }
    }

    static float BuildCum(List<ShardPart> list, ref NativeArray<ShardPart> dst, ref NativeArray<float> cum)
    {
        if (dst.Length < list.Count)
        {
            dst.Dispose(); cum.Dispose();
            dst = new NativeArray<ShardPart>(list.Count * 2, Allocator.Persistent);
            cum = new NativeArray<float>(list.Count * 2, Allocator.Persistent);
        }
        float total = 0;
        for (int p = 0; p < list.Count; p++)
        {
            dst[p] = list[p];
            total += Mathf.Max(1e-4f, list[p].weight);
            cum[p] = total;
        }
        return total;
    }

    float dustBoost;

    void Update()
    {
        if (!pos.IsCreated) return;
        float dt = Mathf.Min(Time.deltaTime, 1 / 30f);
        if (dt <= 0) { Draw(); return; }
        float t = Time.time;
        formAge += dt;
        chip = Mathf.MoveTowards(chip, chipScale, dt * 1.6f);

        parts.Clear();
        headParts.Clear();
        builder?.Invoke(this);

        if (mode == Mode.Serpent && (head - trail[trailHead]).sqrMagnitude > trailStep * trailStep) PushTrail(head);
        if (mode == Mode.Serpent)
        {
            Vector3 hf = TrailAt(trail, trailHead, trailCount, 0) - TrailAt(trail, trailHead, trailCount, 4);
            if (hf.sqrMagnitude > 1e-4f) HeadForward = hf.normalized;
        }

        float total = parts.Count > 0 ? BuildCum(parts, ref nParts, ref nCum) : 0;
        float headTotal = headParts.Count > 0 ? BuildCum(headParts, ref nHead, ref nHeadCum) : 0;
        bool form = mode == Mode.Form && parts.Count > 0;
        Mode eff = mode == Mode.Form && !form ? Mode.Halo : mode;   // no object described yet: wait in the halo

        float stiff = 14, damp = 5;
        switch (eff)
        {
            case Mode.Halo: stiff = 9; damp = 3.6f; break;
            case Mode.Serpent: stiff = 26; damp = 7; break;
            case Mode.Flood: stiff = 30; damp = 8; break;
            case Mode.Twister: stiff = 14; damp = 4; break;
            case Mode.Wind: stiff = 26; damp = 6.5f; break;
            case Mode.Cluster: stiff = 42; damp = 8; break;
        }

        var job = new SimJob
        {
            pos = pos, vel = vel, scale = scale, axis = axis, prevTarget = prevTarget, rot = rot,
            k = k, r1 = r1, r2 = r2, r3 = r3, r4 = r4, r5 = r5, spin = spin,
            matIndex = matIndex, drawMat = drawMat, xform = xform,
            parts = nParts, headParts = nHead, cum = nCum, headCum = nHeadCum, trail = trail,
            partCount = parts.Count, headCount = headParts.Count, total = total, headTotal = headTotal,
            trailHead = trailHead, trailCount = trailCount,
            eff = (int)eff, form = form, active = active, matCount = mats.Length,
            dt = dt, t = t, chip = chip, stiff = stiff, damp = damp, boundRadius = boundRadius,
            formAge = formAge, formBuild = formBuild, orderedBuild = orderedBuild,
            center = center, haloBack = haloBack, head = head,
            floodOrigin = floodOrigin, floodDir = floodDir, floodFront = floodFront,
            twisterHeight = twisterHeight, twisterWidth = twisterWidth,
            windFrom = windFrom, windDoor = windDoor, windTo = windTo, windWidth = windWidth, windSpeed = windSpeed,
            clusterRadius = clusterRadius, clusterShape = clusterShape,
            reservoirStorm = reservoirStorm, stormCenter = stormCenter, stormBase = stormBase,
            formStretch = formStretch, formSink = formSink,
        };
        job.Schedule(count, 256).Complete();

        EmitDust(dt);
        Draw();
    }

    // fast shards kick up dust; embers leave sparks
    void EmitDust(float dt)
    {
        if (dust <= 0 || GameManager.I == null) return;
        var fx = GameManager.I.fx;
        int budget = Mathf.RoundToInt((10 + 40 * dustBoost) * dust);
        dustBoost = Mathf.MoveTowards(dustBoost, 0, dt);
        int n = Mathf.Min(count, 600);
        for (int s = 0; s < n && budget > 0; s++)
        {
            int i = (dustCursor + s * 7) % count;
            Vector3 v = vel[i];
            float sp = v.sqrMagnitude;
            if (sp < 49f || i >= active) continue;   // only shards flying faster than 7 m/s
            budget--;
            if (drawMat[i] == 2) fx.Embers(pos[i], -v * 0.08f, 1, 0.07f);
            else ShardDust.Emit(pos[i], v * 0.05f, 0.25f + scale[i].x * 3f);
        }
        dustCursor = (dustCursor + 97) % count;
    }

    void Draw()
    {
        for (int b = 0; b < batchCount.Length; b++) batchCount[b] = 0;
        for (int i = 0; i < count; i++)
        {
            int b = drawMat[i] * Variants + (i % Variants);
            var arr = batches[b];
            if (batchCount[b] >= arr.Length) { System.Array.Resize(ref batches[b], arr.Length * 2); arr = batches[b]; }
            arr[batchCount[b]++] = xform[i];
        }
        for (int b = 0; b < batches.Length; b++)
        {
            int m = b / Variants;
            if (batchCount[b] == 0 || mats[m] == null) continue;
            var rp = new RenderParams(mats[m])
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 320),
                shadowCastingMode = m == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On,
                receiveShadows = true,
            };
            for (int s = 0; s < batchCount[b]; s += 1000)
                Graphics.RenderMeshInstanced(rp, rocks[b % Variants], 0, batches[b], Mathf.Min(1000, batchCount[b] - s), s);
        }
    }

    // ------------------------------------------------------------------ rock chunks
    // Faceted, flat-shaded chunks: a jittered icosahedron, each face with its own texture patch,
    // fitted to a unit box so the per-shard scale keeps the old chip sizes.
    static Mesh[] MakeRocks()
    {
        var list = new Mesh[Variants];
        float g = (1 + Mathf.Sqrt(5)) / 2;
        Vector3[] ico =
        {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
        };
        int[] tri = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                      3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        for (int v = 0; v < Variants; v++)
        {
            var rng = new System.Random(101 + v * 17);
            float R() => (float)rng.NextDouble();
            var pts = new Vector3[ico.Length];
            for (int i = 0; i < ico.Length; i++)
            {
                Vector3 p = ico[i].normalized * Mathf.Lerp(0.72f, 1.18f, R());
                // a few variants are sheared wedges, others chunky
                p.y *= v % 2 == 0 ? Mathf.Lerp(0.7f, 1.1f, R()) : 1;
                p.x += (v == 3 ? 0.25f : 0) * p.y;
                pts[i] = p;
            }
            // fit into a unit box around the origin
            Vector3 mn = pts[0], mx = pts[0];
            foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            Vector3 c = (mn + mx) * 0.5f, ext = mx - mn;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 p = pts[i] - c;
                pts[i] = new Vector3(p.x / ext.x, p.y / ext.y, p.z / ext.z);
            }
            var verts = new Vector3[tri.Length];
            var norms = new Vector3[tri.Length];
            var uvs = new Vector2[tri.Length];
            var idx = new int[tri.Length];
            for (int f = 0; f < tri.Length; f += 3)
            {
                Vector3 a = pts[tri[f]], b = pts[tri[f + 1]], d = pts[tri[f + 2]];
                Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                if (Vector3.Dot(n, a + b + d) < 0) { var tmp = b; b = d; d = tmp; n = -n; }
                Vector3 ta = (b - a).normalized, tb = Vector3.Cross(n, ta);
                Vector2 off = new Vector2(R(), R());
                Vector3[] fv = { a, b, d };
                for (int j = 0; j < 3; j++)
                {
                    verts[f + j] = fv[j];
                    norms[f + j] = n;
                    uvs[f + j] = off + new Vector2(Vector3.Dot(fv[j] - a, ta), Vector3.Dot(fv[j] - a, tb)) * 0.6f;
                    idx[f + j] = f + j;
                }
            }
            var m = new Mesh { name = "ShardRock" + v };
            m.vertices = verts;
            m.normals = norms;
            m.uv = uvs;
            m.triangles = idx;
            m.RecalculateTangents();
            m.RecalculateBounds();
            list[v] = m;
        }
        return list;
    }

    // ------------------------------------------------------------------ the simulation job
    struct SimJob : IJobParallelFor
    {
        public NativeArray<Vector3> pos, vel, prevTarget;
        public NativeArray<Quaternion> rot;
        public NativeArray<int> drawMat;
        public NativeArray<Matrix4x4> xform;
        [ReadOnly] public NativeArray<Vector3> scale, axis;
        [ReadOnly] public NativeArray<float> k, r1, r2, r3, r4, r5, spin;
        [ReadOnly] public NativeArray<int> matIndex;
        [ReadOnly] public NativeArray<ShardPart> parts, headParts;
        [ReadOnly] public NativeArray<float> cum, headCum;
        [ReadOnly] public NativeArray<Vector3> trail;
        public int partCount, headCount, trailHead, trailCount, eff, active, matCount, clusterShape;
        public float total, headTotal, dt, t, chip, stiff, damp, boundRadius, formAge, formBuild;
        public bool form, orderedBuild, reservoirStorm;
        public Vector3 center, haloBack, head, floodOrigin, floodDir, windFrom, windDoor, windTo, stormCenter;
        public float floodFront, twisterHeight, twisterWidth, windWidth, windSpeed, clusterRadius, stormBase, formStretch;
        public bool formSink;

        static int FindPart(NativeArray<float> c, int n, float key)
        {
            int lo = 0, hi = n - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (c[mid] < key) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        Vector3 Eval(in ShardPart p, int i, out Quaternion q)
        {
            float u = r2[i], v = r3[i], w = r4[i];
            switch (p.type)
            {
                case 0:
                {
                    Vector3 h = p.b;
                    Vector3 l = new Vector3((u * 2 - 1) * h.x, (v * 2 - 1) * h.y, (w * 2 - 1) * h.z);
                    q = p.rot;
                    if (p.shell)
                    {
                        float ax = h.y * h.z, ay = h.x * h.z, az = h.x * h.y;
                        float pick = r5[i] * (ax + ay + az);
                        float sgn = (r5[i] * 7.31f) % 1f > 0.5f ? 1 : -1;
                        if (pick < ax) { l.x = sgn * h.x; q = p.rot * Quaternion.Euler(0, 0, 90); }
                        else if (pick < ax + ay) l.y = sgn * h.y;
                        else { l.z = sgn * h.z; q = p.rot * Quaternion.Euler(90, 0, 0); }
                    }
                    return p.a + p.rot * l;
                }
                case 1:
                {
                    Vector3 d = p.b - p.a;
                    float len = d.magnitude;
                    Vector3 dn = len > 1e-4f ? d / len : Vector3.up;
                    Vector3 n1 = Vector3.Cross(dn, Mathf.Abs(dn.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                    Vector3 n2 = Vector3.Cross(dn, n1);
                    float ph = v * Mathf.PI * 2;
                    Vector3 radial = n1 * Mathf.Cos(ph) + n2 * Mathf.Sin(ph);
                    q = Quaternion.LookRotation(dn, radial);
                    return p.a + d * u + radial * p.r;
                }
                case 2:
                {
                    float z = u * 2 - 1, ph = v * Mathf.PI * 2, s = Mathf.Sqrt(Mathf.Max(0, 1 - z * z));
                    Vector3 n = new Vector3(s * Mathf.Cos(ph), z, s * Mathf.Sin(ph));
                    if (p.rot.x != 0 || p.rot.y != 0 || p.rot.z != 0 || p.rot.w != 0) n = p.rot * n;   // a rolling sphere
                    q = Quaternion.FromToRotation(Vector3.up, n);
                    return p.a + n * p.b.x;
                }
                default:
                {
                    float th = u * Mathf.PI * 2, ph = v * Mathf.PI * 2;
                    Vector3 X = p.rot * Vector3.right, Y = p.rot * Vector3.up, Z = p.rot * Vector3.forward;
                    Vector3 radial = X * Mathf.Cos(th) + Z * Mathf.Sin(th);
                    Vector3 n = radial * Mathf.Cos(ph) + Y * Mathf.Sin(ph);
                    Vector3 tan = -X * Mathf.Sin(th) + Z * Mathf.Cos(th);
                    q = Quaternion.LookRotation(tan, n);
                    return p.a + radial * p.b.x + n * p.b.y;
                }
            }
        }

        // shards joining a painted part take its colour; a few embers keep glowing through
        int TintMat(int i, byte tint)
        {
            if (tint == ShardForms.Natural || tint >= matCount) return matIndex[i];
            if (matIndex[i] == 2 && r4[i] < (tint == ShardForms.Steel ? 0.08f : 0.35f)) return 2;   // the giant is almost all steel
            return tint;
        }

        Vector3 HaloTarget(int i)
        {
            float band = r1[i];
            float ang = r2[i] * Mathf.PI * 2;
            if (band < 0.3f)   // spinning disc under his feet
            {
                ang += t * 2.6f;
                float rr = 0.35f + 0.75f * r3[i];
                return center + new Vector3(Mathf.Cos(ang) * rr, -0.15f + 0.08f * Mathf.Sin(ang * 3), Mathf.Sin(ang) * rr);
            }
            if (band < 0.72f)  // two tilted orbit rings
            {
                bool a = band < 0.51f;
                ang += t * (a ? -1.7f : 1.9f);
                float rad = 1.05f + 0.45f * r3[i];
                var q = Quaternion.Euler(a ? 22 : -18, t * 20 + (a ? 0 : 90), a ? 12 : -25);
                return center + Vector3.up * 1.05f + q * new Vector3(Mathf.Cos(ang) * rad, 0.15f * Mathf.Sin(t * 2 + i), Mathf.Sin(ang) * rad);
            }
            // mandala behind his head: two counter-rotating vertical rings with spokes
            Vector3 back = haloBack.sqrMagnitude > 0.01f ? haloBack.normalized : Vector3.back;
            Vector3 side = Vector3.Cross(Vector3.up, back).normalized;
            Vector3 hc = center + Vector3.up * 2.05f + back * 0.4f;
            float sub = (band - 0.72f) / 0.28f;
            float radius;
            if (sub < 0.45f) { radius = 0.95f; ang += t * 0.6f; }
            else if (sub < 0.75f) { radius = 0.62f; ang -= t * 0.9f; }
            else { radius = 0.2f + 0.75f * r3[i]; ang = Mathf.Round(ang / (Mathf.PI / 6)) * (Mathf.PI / 6) + t * 0.6f; }
            return hc + (side * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang)) * radius;
        }

        Vector3 StormTarget(int i)
        {
            float h = Mathf.Lerp(stormBase, 12f, r1[i]);
            float rad = 2.4f + 2.6f * r3[i] + 0.7f * Mathf.Sin(t * 2f + i);
            float ang = r2[i] * Mathf.PI * 2 + t * (1.4f + r4[i] * 1.3f);
            return stormCenter + new Vector3(Mathf.Cos(ang) * rad, h + 0.4f * Mathf.Sin(t * 3f + i * 0.7f), Mathf.Sin(ang) * rad);
        }

        public void Execute(int i)
        {
            Vector3 p = pos[i], v = vel[i];
            Quaternion ro = rot[i];
            int dm = matIndex[i];
            float lock01 = 0, size = 1;
            bool free = eff == (int)Mode.Free || i >= active;
            if (free)
            {
                v += Vector3.down * 16 * dt;
                v *= 1 - 0.3f * dt;
            }
            else if (form)
            {
                int pi = FindPart(cum, partCount, r1[i] * total);
                float delay = orderedBuild
                    ? (pi > 0 ? cum[pi - 1] / total : 0) * formBuild + r5[i] * 0.3f
                    : r5[i] * formBuild * 0.75f;
                float w = Mathf.Clamp01((formAge - delay) / 0.45f);
                if (w <= 0)
                {
                    // still waiting in the reservoir around Moros (or swirling in the storm)
                    Vector3 tg = reservoirStorm ? StormTarget(i) : HaloTarget(i);
                    v += ((tg - p) * 9 * k[i] - v * 3.6f * Mathf.Sqrt(k[i])) * dt;
                    prevTarget[i] = p;
                }
                else
                {
                    Vector3 target = Eval(parts[pi], i, out var q);
                    dm = TintMat(i, parts[pi].tint);
                    lock01 = w;
                    size = 1 - parts[pi].shrink;
                    if (formSink && parts[pi].sink != 0) size *= 1 - Mathf.Clamp01((formAge - delay - 0.55f) / 0.4f);
                    Vector3 tv = (target - prevTarget[i]) / dt;
                    if (tv.sqrMagnitude > 70 * 70 || (target - prevTarget[i]).sqrMagnitude > 2.5f) tv = Vector3.zero;
                    prevTarget[i] = target;
                    if (w < 1)
                    {
                        float kk = (16 + 60 * w) * k[i];
                        v += ((target - p) * kk - v * (5 + 7 * w)) * dt;
                    }
                    else
                    {
                        Vector3 desired = tv * 0.9f + (target - p) * (18 * k[i]);
                        v = Vector3.Lerp(v, desired, 1 - Mathf.Exp(-28 * dt));
                    }
                    ro = Quaternion.Slerp(ro, q, 1 - Mathf.Exp(-(4 + 16 * w) * dt));
                }
            }
            else
            {
                Vector3 target = p;
                bool wrapped = false;
                Vector3 flow = Vector3.zero;
                switch ((Mode)eff)
                {
                    case Mode.Halo:
                        target = HaloTarget(i);
                        break;
                    case Mode.Serpent:
                    {
                        const float headFrac = 0.16f;
                        float bodyLen = trailCount - 1;
                        float u = (float)i / active;
                        if (u < headFrac && headCount > 0)
                        {
                            int hp = FindPart(headCum, headCount, (u / headFrac) * headTotal);
                            target = Eval(headParts[hp], i, out var hq);
                            dm = TintMat(i, headParts[hp].tint);
                            ro = Quaternion.Slerp(ro, hq, 1 - Mathf.Exp(-14 * dt));
                            break;
                        }
                        float uu = headCount > 0 ? (u - headFrac) / (1 - headFrac) : u;
                        float idx = 3 + uu * (bodyLen - 3);
                        Vector3 c = TrailAt(trail, trailHead, trailCount, idx);
                        Vector3 tan = TrailAt(trail, trailHead, trailCount, idx - 1) - TrailAt(trail, trailHead, trailCount, idx + 1);
                        if (tan.sqrMagnitude < 1e-6f) tan = Vector3.forward;
                        tan.Normalize();
                        Vector3 sd = Vector3.Cross(tan, Vector3.up);
                        if (sd.sqrMagnitude < 1e-4f) sd = Vector3.Cross(tan, Vector3.right);
                        sd.Normalize();
                        Vector3 up2 = Vector3.Cross(sd, tan);
                        float radius = Mathf.Lerp(0.55f, 0.08f, Mathf.Pow(uu, 0.7f)) * Mathf.Sqrt(r1[i]);
                        if (r3[i] > 0.94f) radius += 0.5f * r2[i];
                        float ang = r2[i] * Mathf.PI * 2 + t * 4f;
                        target = c + (sd * Mathf.Cos(ang) + up2 * Mathf.Sin(ang)) * radius;
                        break;
                    }
                    case Mode.Flood:
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, floodDir);
                        float depth = r1[i];
                        float lat = (r2[i] - 0.5f) * 9f;
                        float crest = Mathf.Max(0, 1 - depth * 1.3f);
                        float h = 0.05f + r3[i] * 1.6f * crest + 0.3f * Mathf.Sin(lat * 1.3f + t * 6) * crest;
                        target = floodOrigin + floodDir * (floodFront - depth * 1.6f) + side * lat + Vector3.up * h;
                        break;
                    }
                    case Mode.Wind:
                    {
                        float wsp = windSpeed * 0.42f;
                        float u = Mathf.Repeat(r1[i] + t * wsp, 1f);
                        float u0 = Mathf.Repeat(r1[i] + (t - dt) * wsp, 1f);
                        wrapped = wsp > 0 ? u < u0 - 0.5f : u > u0 + 0.5f;
                        const float split = 0.3f;
                        Vector3 a0, a1;
                        float wk;
                        if (u < split) { a0 = windFrom; a1 = windDoor; wk = u / split; }
                        else { a0 = windDoor; a1 = windTo; wk = (u - split) / (1 - split); }
                        Vector3 c = Vector3.Lerp(a0, a1, wk);
                        Vector3 wd = a1 - a0; wd.y = 0;
                        wd = wd.sqrMagnitude > 1e-4f ? wd.normalized : Vector3.forward;
                        Vector3 sd = Vector3.Cross(Vector3.up, wd);
                        float spread = u < split ? Mathf.Lerp(0.5f, 1.0f, wk) : Mathf.Lerp(1.0f, windWidth, Mathf.Sqrt(wk));
                        float swirl = Mathf.Sin(t * 5f + r4[i] * 20f) * 0.35f;
                        target = c + sd * ((r2[i] * 2 - 1) * spread + swirl) + Vector3.up * (0.25f + r3[i] * 2.4f + 0.3f * Mathf.Sin(t * 3 + i));
                        flow = wd * Mathf.Sign(wsp);
                        break;
                    }
                    case Mode.Cluster:
                    {
                        float ang = r2[i] * Mathf.PI * 2 + t * (2.5f + r4[i] * 2f) * (r5[i] > 0.5f ? 1 : -1);
                        float rr = clusterRadius * (0.35f + 0.65f * Mathf.Sqrt(r1[i]));
                        if (clusterShape == 1)
                            target = head + new Vector3(Mathf.Cos(ang) * clusterRadius, (r3[i] - 0.5f) * 0.25f, Mathf.Sin(ang) * clusterRadius);
                        else if (clusterShape == 2)
                            target = head + new Vector3(Mathf.Cos(ang) * rr * 0.5f, (r3[i] - 0.5f) * clusterRadius * 2.4f, Mathf.Sin(ang) * rr * 0.5f);
                        else
                        {
                            float el = (r3[i] - 0.5f) * Mathf.PI;
                            target = head + new Vector3(Mathf.Cos(ang) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(ang) * Mathf.Cos(el)) * rr;
                        }
                        break;
                    }
                    case Mode.Twister:
                    {
                        float h = Mathf.Pow(r1[i], 1.15f) * twisterHeight;
                        float wv = 5.2f - h * 0.35f * (4.6f / twisterHeight);
                        float rad = 0.22f + h * twisterWidth + 0.12f * Mathf.Sin(t * 3 + i);
                        float ang = r2[i] * Mathf.PI * 2 + t * wv;
                        target = center + new Vector3(Mathf.Cos(ang) * rad, h, Mathf.Sin(ang) * rad);
                        break;
                    }
                }
                float kk2 = stiff * k[i];
                v += ((target - p) * kk2 - v * damp * Mathf.Sqrt(k[i])) * dt;
                v += new Vector3(Mathf.Sin(t * 1.3f + i), Mathf.Sin(t * 1.7f + i * 1.3f), Mathf.Cos(t * 1.1f + i * 0.7f)) * (1.2f * dt);
                if (wrapped) { p = target; v = flow * 12f; }   // the gale's end feeds back in at its start
            }

            float sp = v.magnitude;
            float cap = form ? 80 : 40;
            if (sp > cap) { v *= cap / sp; sp = cap; }
            p += v * dt;
            float floor = scale[i].y * 0.5f * chip;
            if (p.y < floor)
            {
                p.y = floor;
                if (v.y < 0) v.y = -v.y * 0.25f;
                v.x *= 0.82f; v.z *= 0.82f;
                if (free && Mathf.Abs(v.y) < 0.4f) { v.y = 0; ro = Quaternion.Slerp(ro, Quaternion.Euler(0, r2[i] * 360, 0), 10 * dt); }
            }
            Vector3 flat = new Vector3(p.x, 0, p.z);
            if (flat.sqrMagnitude > boundRadius * boundRadius) { p -= flat.normalized * (flat.magnitude - boundRadius); v.x *= -0.3f; v.z *= -0.3f; }

            bool resting = free && p.y <= floor + 0.001f && sp < 0.3f;
            bool locked = form || (eff == (int)Mode.Serpent && headCount > 0 && (float)i / active < 0.16f);
            if (!resting && !locked) ro = Quaternion.AngleAxis(spin[i] * dt * Mathf.Clamp(sp / 6, 0.15f, 1.8f), axis[i]) * ro;

            pos[i] = p;
            vel[i] = v;
            rot[i] = ro;
            drawMat[i] = dm;
            Vector3 sc = scale[i] * (chip * size);
            if (lock01 > 0 && formStretch != 1)
            {
                float st = Mathf.Lerp(1, formStretch, lock01);
                sc = new Vector3(sc.x * Mathf.Lerp(1, 0.72f, lock01), sc.y, sc.z * st);
            }
            xform[i] = Matrix4x4.TRS(p, ro, sc);
        }
    }
}

// One shared dust system for every swarm: soft puffs kicked up by fast-flying stones.
public static class ShardDust
{
    static ParticleSystem ps;

    static void Make()
    {
        var go = new GameObject("ShardDust");
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = 3000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.02f;
        var emission = ps.emission;
        emission.enabled = false;
        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.5f, 1, 1.8f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                     new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
        col.color = grad;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (GameManager.I && GameManager.I.fx) r.sharedMaterial = GameManager.I.fx.dustMat;
        ps.Play();
    }

    public static void Emit(Vector3 p, Vector3 v, float size)
    {
        if (ps == null) Make();
        var e = new ParticleSystem.EmitParams
        {
            position = p,
            velocity = v + Random.insideUnitSphere * 0.3f,
            startSize = size,
            startLifetime = Random.Range(0.5f, 1.0f),
            startColor = new Color(0.78f, 0.7f, 0.62f, 0.28f),
        };
        ps.Emit(e, 1);
    }
}
