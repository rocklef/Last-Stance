using System.Collections.Generic;
using UnityEngine;

// THE KING COBRA's flesh: one mesh rebuilt every frame from the pose MonkBoss gives it -
//   a scaled body that follows the spine (flat belly, tapering to a needle tail, a lump where
//   something swallowed is sliding down), a hood that spreads from the neck into a wide cupped
//   shield, and a detailed head: a flat-topped skull with brow ridges over the eyes and cheek
//   muscles, lips, a hinged lower jaw, a mouth lined in flesh that glows toward the throat, fangs
//   that swing down as it gapes, rows of teeth, a forked tongue and glowing eyes.
// Pure geometry (vector maths only - no engine calls), so it also runs outside Unity to preview
// the model in Blender.
public class CobraMesh
{
    public const int Body = 0, Hood = 1, Head = 2, Mouth = 3, Fang = 4, Tongue = 5, Eye = 6, SubCount = 7;
    public const float HeadLen = 1.15f;       // head length, in cobra units (s metres each)
    const int NB = 28, NH = 64;               // vertices around the body / the hood

    // ---------------------------------------------------------------- pose
    // the spine, from the back of the head (0) to the tail tip; MonkBoss fills it with BuildSpine
    public readonly List<Vector3> spine = new List<Vector3>(512);
    public Vector3 headFwd = Vector3.forward, headUp = Vector3.up;
    public Vector3 back = Vector3.back;       // the side of the reared neck facing away from its prey
    public float s = 2.2f;                    // metres per cobra unit
    public float bodyLength = 42f;            // metres, head to tail tip
    public float hood = 1;                    // 0 folded .. 1 spread .. 1.3 flared in fury
    public float gape;                        // jaw opening, degrees
    public float tongue;                      // 0 in .. 1 fully out
    public float flick;                       // tongue flicker clock
    public float bulge = -1;                  // arc (m from the head) of a swallowed lump; -1 none
    public float bulgeSize = 1;
    public float reveal = 1;                  // how much of it is built, from the tail tip forward

    // ---------------------------------------------------------------- output
    public readonly List<Vector3> V = new List<Vector3>(16384), N = new List<Vector3>(16384);
    public readonly List<Vector4> Tn = new List<Vector4>(16384);
    public readonly List<Vector2> UV = new List<Vector2>(16384);
    public readonly List<int>[] tris = new List<int>[SubCount];
    public Vector3 eyeL, eyeR, snout, mouthCentre, throat;
    public float length;                      // the spine's actual length
    public bool headShown;
    public float frontArc;                    // the growth front while it is being built

    readonly List<float> arc = new List<float>(512);
    Vector3[] gp = new Vector3[8192];
    Vector2[] guv = new Vector2[8192];
    readonly Vector3[] tp = new Vector3[64];
    readonly float[] tr = new float[64];
    float[] vTab = new float[1024];
    float vTab0, vTabStep = 0.1f;
    int vTabN;
    Vector3 O, F, U, R;                       // the head's frame

    public CobraMesh()
    {
        for (int i = 0; i < SubCount; i++) tris[i] = new List<int>(24576);
    }

    // ================================================================== the spine
    // head: the back of the skull; fwd/up: the head's frame; B: where the neck leaves the ground;
    // ground: the body's path behind B (B first, spaced evenly, on the ground plane).
    public void BuildSpine(Vector3 head, Vector3 fwd, Vector3 up, Vector3 B, List<Vector3> ground, int groundCount)
    {
        spine.Clear();
        Vector3 f = fwd.normalized;
        Vector3 dirB = groundCount > 1 ? B - ground[1] : f;
        dirB.y = 0;
        dirB = dirB.sqrMagnitude > 1e-6f ? dirB.normalized : new Vector3(f.x, 0, f.z).normalized;
        float h = head.y - B.y;
        // the neck: an S - it leaves the ground along the body's path, rises in a tall column and
        // bends over into the skull (both bends smooth, like a shepherd's crook)
        float k1 = Mathf.Clamp(h * 0.45f, 0.35f * s, 1.2f * s), k2 = Mathf.Clamp(h * 0.42f, 0.3f * s, 1.1f * s);
        Vector3 p0 = head, p1 = head - f * k1, p2 = B + dirB * k2, p3 = B;
        const int neckN = 26;
        float neckLen = 0;
        Vector3 prev = p0;
        for (int i = 0; i < neckN; i++)
        {
            float t = i / (float)(neckN - 1);
            float u = 1 - t;
            Vector3 p = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
            if (i > 0) neckLen += Vector3.Distance(prev, p);
            spine.Add(p);
            prev = p;
        }
        // the body lies along the path, its belly on the ground, until the length runs out
        float acc = neckLen;
        for (int k = 1; k < groundCount && acc < bodyLength; k++)
        {
            Vector3 g = ground[k];
            float d = Vector3.Distance(new Vector3(prev.x, 0, prev.z), new Vector3(g.x, 0, g.z));
            if (acc + d > bodyLength)
            {
                g = Vector3.Lerp(new Vector3(prev.x, 0, prev.z), new Vector3(g.x, 0, g.z), (bodyLength - acc) / Mathf.Max(1e-4f, d));
                d = bodyLength - acc;
            }
            acc += d;
            g.y = Belly(acc);
            spine.Add(g);
            prev = g;
        }
        if (acc < bodyLength - 0.05f && spine.Count > 1)
        {
            // the path is too short (just formed): the rest of the tail trails straight back
            Vector3 dir = spine[spine.Count - 1] - spine[spine.Count - 2];
            dir.y = 0;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : -dirB;
            while (acc < bodyLength - 0.01f)
            {
                float d = Mathf.Min(0.4f, bodyLength - acc);
                acc += d;
                Vector3 g = prev + dir * d;
                g.y = Belly(acc);
                spine.Add(g);
                prev = g;
            }
        }
    }

    // ================================================================== profile
    public float BaseRadius(float a)
    {
        float u = a / s;   // cobra units behind the head
        float r = u < 0.45f ? Mathf.Lerp(0.155f, 0.23f, Smooth((u + 0.3f) / 0.75f))
                : u < 3.8f ? Mathf.Lerp(0.23f, 0.27f, Smooth((u - 0.45f) / 3.35f))
                : Mathf.Lerp(0.27f, 0.33f, Smooth((u - 3.8f) / 5f));
        float L = bodyLength, tail0 = L * 0.5f;
        if (a > tail0) r *= 0.05f + 0.95f * Mathf.Pow(Mathf.Clamp01(1 - (a - tail0) / (L - tail0)), 0.9f);
        return r * s;
    }

    public float Radius(float a)
    {
        float r = BaseRadius(a);
        if (bulge >= 0)
        {
            float d = (a - bulge) / (0.65f * s);
            r *= 1 + 0.75f * bulgeSize * Mathf.Exp(-d * d);
        }
        return r;
    }

    // how far below the spine the belly lies (to rest the body on the ground)
    public float Belly(float a) => BaseRadius(a) * 0.96f * 0.79f;

    // head profiles along z (0 = back of the skull, 1 = snout tip), in cobra units
    static readonly float[] kz = { -0.08f, 0f, 0.15f, 0.32f, 0.5f, 0.68f, 0.82f, 0.92f, 1f };
    static readonly float[] kw = { 0.19f, 0.25f, 0.29f, 0.3f, 0.285f, 0.25f, 0.21f, 0.175f, 0.15f };      // half width
    static readonly float[] kh = { 0.15f, 0.2f, 0.235f, 0.24f, 0.225f, 0.2f, 0.175f, 0.15f, 0.13f };      // skull above the lips
    static readonly float[] kj = { 0.14f, 0.175f, 0.165f, 0.15f, 0.13f, 0.11f, 0.092f, 0.075f, 0.06f };   // jaw depth

    // the blunt, rounded snout and chin
    static float CapU(float z) => z > 0.86f ? Mathf.Sqrt(Mathf.Max(0, 1 - Sq((z - 0.86f) / 0.14f))) : 1;
    static float CapJ(float z) => z > 0.83f ? Mathf.Sqrt(Mathf.Max(0, 1 - Sq((z - 0.83f) / 0.14f))) : 1;
    static float Tuck(float z) => z < 0 ? 1 - 0.3f * (-z / 0.08f) : 1;
    public static float HeadW(float z) => Knot(z, kz, kw) * CapU(z) * Tuck(z);
    public static float HeadH(float z) => Knot(z, kz, kh) * CapU(z) * Tuck(z);
    public static float JawW(float z) => 0.93f * Knot(z, kz, kw) * CapJ(z) * Tuck(z);
    public static float JawD(float z) => Knot(z, kz, kj) * CapJ(z) * Tuck(z);
    public static float Lip(float z) => 0.02f - 0.03f * Sq(1 - z);
    public float HoodStart => 0.12f * s;
    public float HoodEnd => 2.25f * s;
    public static float HoodWarp(float t) => 2 * Mathf.PI * t - 0.3f * Mathf.Sin(4 * Mathf.PI * t);

    // ================================================================== build
    public void Build()
    {
        V.Clear(); N.Clear(); Tn.Clear(); UV.Clear();
        for (int i = 0; i < SubCount; i++) tris[i].Clear();
        headShown = false;
        int n = spine.Count;
        if (n < 2) return;
        arc.Clear();
        arc.Add(0);
        float acc = 0;
        for (int i = 1; i < n; i++) { acc += Vector3.Distance(spine[i - 1], spine[i]); arc.Add(acc); }
        length = acc;
        F = headFwd.sqrMagnitude > 1e-6f ? headFwd.normalized : Vector3.forward;
        U = headUp - F * Vector3.Dot(headUp, F);
        U = U.sqrMagnitude > 1e-6f ? U.normalized : Perp(F);
        R = Vector3.Cross(U, F);
        O = spine[0];
        back.y = 0;
        back = back.sqrMagnitude > 1e-6f ? back.normalized : -new Vector3(F.x, 0, F.z).normalized;

        float a0 = -0.3f * s;
        float aMin = Mathf.Lerp(length, a0, Mathf.Clamp01(reveal));
        frontArc = aMin;
        BuildVTable(a0);
        BodyTube(a0, aMin);
        if (hood > 0.02f) HoodTube(aMin);
        if (reveal >= 0.999f) { headShown = true; HeadParts(); }
    }

    // ---------------------------------------------------------------- spine sampling
    Vector3 At(float a)
    {
        int n = spine.Count;
        if (a <= 0) return spine[0] - (spine[1] - spine[0]).normalized * a;   // on into the skull
        if (a >= length) return spine[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (arc[mid] <= a) lo = mid; else hi = mid; }
        float t = (a - arc[lo]) / Mathf.Max(1e-5f, arc[hi] - arc[lo]);
        Vector3 p0 = spine[Mathf.Max(lo - 1, 0)], p1 = spine[lo], p2 = spine[hi], p3 = spine[Mathf.Min(hi + 1, n - 1)];
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
    }

    public Vector3 Point(float a) => spine.Count > 1 ? At(a) : Vector3.zero;
    public void FrameAt(float a, out Vector3 p, out Vector3 t, out Vector3 d, out Vector3 x) => Frame(a, out p, out t, out d, out x);

    // the hood's half width (m) at arc a
    public float HoodHalfWidth(float a)
    {
        float uh = Mathf.Clamp01((a - HoodStart) / (HoodEnd - HoodStart));
        float prof = uh < 0.34f ? Mathf.Pow(Mathf.Sin(uh / 0.34f * Mathf.PI * 0.5f), 0.6f)
                                : Mathf.Pow(Mathf.Cos((uh - 0.34f) / 0.66f * Mathf.PI * 0.5f), 1.15f);
        float rw = Radius(a) * 1.04f;
        return rw + hood * (1.27f * s - rw) * prof;
    }

    // centre, forward (toward the head), dorsal and right-hand axes at arc a
    void Frame(float a, out Vector3 p, out Vector3 t, out Vector3 d, out Vector3 x)
    {
        p = At(a);
        Vector3 tt = At(a - 0.35f) - At(a + 0.35f);
        t = tt.sqrMagnitude > 1e-8f ? tt.normalized : F;
        // on the ground the back faces up; up the reared neck it faces away from the prey
        float vert = Mathf.Abs(t.y);
        Vector3 rf = Vector3.up + back * (3f * Smooth((vert - 0.35f) / 0.55f));
        Vector3 dd = rf - t * Vector3.Dot(t, rf);
        if (dd.sqrMagnitude < 1e-6f) dd = back - t * Vector3.Dot(t, back);
        d = dd.normalized;
        x = Vector3.Cross(d, t);
    }

    // texture v along the body: scales shrink with the body, so v runs faster where it is thin
    void BuildVTable(float a0)
    {
        vTab0 = a0;
        vTabN = Mathf.Min(vTab.Length, Mathf.CeilToInt((bodyLength - a0) / vTabStep) + 2);
        vTab[0] = 0;
        for (int k = 1; k < vTabN; k++)
            vTab[k] = vTab[k - 1] + vTabStep / (Mathf.Max(0.02f * s, BaseRadius(a0 + k * vTabStep)) * 5.5f);
    }

    float VAt(float a)
    {
        float f = (a - vTab0) / vTabStep;
        int k = Mathf.Clamp(Mathf.FloorToInt(f), 0, vTabN - 2);
        return Mathf.Lerp(vTab[k], vTab[k + 1], Mathf.Clamp01(f - k));
    }

    // ================================================================== body
    void BodyTube(float a0, float aMin)
    {
        float start = Mathf.Max(a0, aMin), end = length;
        if (end - start < 0.05f) return;
        int rings = Mathf.Clamp(Mathf.CeilToInt((end - start) / 0.26f) + 1, 3, 260);
        float step = (end - start) / (rings - 1);
        int cols = NB + 1;
        bool growing = aMin > a0 + 0.01f;
        for (int i = 0; i < rings; i++)
        {
            float a = start + i * step;
            Frame(a, out var p, out _, out var d, out var x);
            float r = i == rings - 1 ? 0 : Radius(a);
            if (growing) r *= Mathf.Sqrt(Smooth((a - aMin) / 0.9f));   // the rounded growth front
            float rw = r * 1.04f, rh = r * 0.96f, v = VAt(a);
            for (int j = 0; j < cols; j++)
            {
                float ph = j * (2 * Mathf.PI / NB);
                float yy = Mathf.Cos(ph) * rh, xx = Mathf.Sin(ph) * rw;
                if (yy < -0.72f * rh) yy = -0.72f * rh + (yy + 0.72f * rh) * 0.25f;   // the flat belly
                gp[i * cols + j] = p + x * xx + d * yy;
                guv[i * cols + j] = new Vector2(j / (float)NB, v);
            }
        }
        Grid(Body, rings, cols, true, false);
    }

    // ================================================================== hood
    void HoodTube(float aMin)
    {
        float h0 = HoodStart, h1 = HoodEnd;
        float start = Mathf.Max(h0, aMin);
        if (h1 - start < 0.1f) return;
        int rings = Mathf.Clamp(Mathf.CeilToInt((h1 - start) / (h1 - h0) * 40), 3, 40);
        int cols = NH + 1;
        float hw = 1.27f * s, amt = hood;
        for (int i = 0; i < rings; i++)
        {
            float a = Mathf.Lerp(start, h1, i / (float)(rings - 1));
            float uh = (a - h0) / (h1 - h0);
            Frame(a, out var p, out _, out var d, out var x);
            float prof = uh < 0.34f ? Mathf.Pow(Mathf.Sin(uh / 0.34f * Mathf.PI * 0.5f), 0.6f)
                                    : Mathf.Pow(Mathf.Cos((uh - 0.34f) / 0.66f * Mathf.PI * 0.5f), 1.15f);
            float r = Radius(a), rw = r * 1.04f, rh = r * 0.96f;
            float A = rw + amt * (hw - rw) * prof;
            float tw = (0.05f + 0.035f * (1 - uh)) * s;
            float cup = (0.3f + 0.5f * Mathf.Min(1, amt)) * prof;
            float k = 0.35f * tw;
            for (int j = 0; j < cols; j++)
            {
                float tt = j / (float)NH;
                float ph = HoodWarp(tt);
                float sx = Mathf.Sin(ph), cy = Mathf.Cos(ph);
                float xx = A * sx;
                float qn = xx / rw, yn = qn * qn < 1 ? rh * Mathf.Sqrt(1 - qn * qn) : 0;
                float qw = xx / A, yw = tw * Mathf.Sqrt(Mathf.Max(0, 1 - qw * qw));
                float yh = 0.5f * (yn + yw + Mathf.Sqrt((yn - yw) * (yn - yw) + k * k)) * 1.02f;
                float yy = Sgn(cy) * yh * Mathf.Pow(Mathf.Abs(cy), 0.12f);
                if (yy < -0.72f * rh) yy = -0.72f * rh + (yy + 0.72f * rh) * 0.25f;
                yy -= cup * qw * qw * A * 0.28f;   // the wings cup forward around the prey
                gp[i * cols + j] = p + x * xx + d * yy;
                guv[i * cols + j] = new Vector2(tt, uh);
            }
        }
        Grid(Hood, rings, cols, true, false);
    }

    // ================================================================== head
    Vector3 HL(float x, float y, float z) => O + (R * x + U * y + F * (z * HeadLen)) * s;

    // a point of the lower jaw (head coordinates), swung open about the hinge
    Vector3 JL(float x, float y, float z)
    {
        const float hz0 = 0.06f;
        float hy = Lip(hz0), hz = hz0 * HeadLen;
        float dy = y - hy, dz = z * HeadLen - hz;
        float b = gape * Mathf.Deg2Rad, cb = Mathf.Cos(b), sb = Mathf.Sin(b);
        float y2 = hy + dy * cb - dz * sb, z2 = hz + dy * sb + dz * cb;
        return O + (R * x + U * y2 + F * z2) * s;
    }

    Vector3 LocalDir(Vector3 v) => (R * v.x + U * v.y + F * v.z).normalized;

    void HeadParts()
    {
        // ------------------------------------------------ the skull
        const int RZ = 36, CT = 30;
        int cols = CT + 1;
        for (int i = 0; i <= RZ; i++)
        {
            float f = i / (float)RZ;
            float z = -0.08f + 1.08f * (1 - Mathf.Pow(1 - f, 1.3f));
            float w = HeadW(z), ht = HeadH(z), m = Lip(z);
            float n = 2.2f + 0.4f * G(z, 0.45f, 0.35f);
            for (int j = 0; j < cols; j++)
            {
                float th = Mathf.PI * j / CT;
                float c = Mathf.Cos(th), sn = Mathf.Sin(th);
                float x = w * Sgn(c) * Mathf.Pow(Mathf.Abs(c), 2f / n);
                float y = m + ht * Mathf.Pow(Mathf.Max(0, sn), 2f / n);
                float q = th < Mathf.PI * 0.5f ? th : Mathf.PI - th;          // up from the lip
                float brow = 0.03f * G(z, 0.63f, 0.12f) * G(q, 1.12f, 0.24f);  // ridge over the eye
                float cheek = 0.02f * G(z, 0.2f, 0.13f) * G(q, 0.3f, 0.35f);   // jaw muscles
                float crown = 0.012f * G(z, 0.25f, 0.2f) * G(q, 1.57f, 0.35f); // the domed crown
                x += Sgn(c) * (brow * 0.9f + cheek);
                y += brow * 0.4f + crown;
                gp[i * cols + j] = HL(x, y, z);
                guv[i * cols + j] = new Vector2(0.5f * th / Mathf.PI, (z + 0.08f) / 1.08f);
            }
        }
        Grid(Head, RZ + 1, cols, false, false);

        // ------------------------------------------------ the roof of the mouth
        const int RP = 24, CP = 12;
        cols = CP + 1;
        for (int i = 0; i <= RP; i++)
        {
            float z = Mathf.Lerp(0.06f, 0.985f, 1 - Mathf.Pow(1 - i / (float)RP, 1.3f));
            float w = HeadW(z) * 0.985f, ht = HeadH(z), m = Lip(z);
            for (int j = 0; j < cols; j++)
            {
                float fx = -1 + 2f * j / CP;
                gp[i * cols + j] = HL(w * fx, m + 0.07f * ht * (1 - fx * fx), z);
                guv[i * cols + j] = new Vector2(j / (float)CP, (z - 0.06f) / 0.93f);
            }
        }
        Grid(Mouth, RP + 1, cols, false, false);

        // ------------------------------------------------ the lower jaw and the floor of the mouth
        const int RJ = 30, CJ = 24;
        cols = CJ + 1;
        for (int i = 0; i <= RJ; i++)
        {
            float z = Mathf.Lerp(0.0f, 0.97f, 1 - Mathf.Pow(1 - i / (float)RJ, 1.3f));
            float w = JawW(z), dj = JawD(z), m = Lip(z);
            for (int j = 0; j < cols; j++)
            {
                float th = Mathf.PI * j / CJ;
                float c = Mathf.Cos(th), sn = Mathf.Sin(th);
                float x = w * Sgn(c) * Mathf.Pow(Mathf.Abs(c), 2f / 2.2f);
                float y = m - dj * Mathf.Pow(Mathf.Max(0, sn), 2f / 2.2f);
                gp[i * cols + j] = JL(x, y, z);
                guv[i * cols + j] = new Vector2(0.5f + 0.5f * th / Mathf.PI, z / 0.97f);
            }
        }
        Grid(Head, RJ + 1, cols, false, true);
        cols = CP + 1;
        for (int i = 0; i <= RP; i++)
        {
            float z = Mathf.Lerp(0.06f, 0.965f, 1 - Mathf.Pow(1 - i / (float)RP, 1.3f));
            float w = JawW(z) * 0.97f, dj = JawD(z), m = Lip(z);
            for (int j = 0; j < cols; j++)
            {
                float fx = -1 + 2f * j / CP;
                gp[i * cols + j] = JL(w * fx, m - 0.004f - 0.25f * dj * (1 - fx * fx), z);
                guv[i * cols + j] = new Vector2(j / (float)CP, (z - 0.06f) / 0.93f);
            }
        }
        Grid(Mouth, RP + 1, cols, false, true);

        // ------------------------------------------------ fangs (they swing down as it gapes) and teeth
        float open = Smooth(gape / 32f);
        for (int sd = -1; sd <= 1; sd += 2)
        {
            float zf = 0.83f;
            Vector3 root = new Vector3(sd * 0.115f, Lip(zf) + 0.03f, zf);
            Vector3 dir = new Vector3(0, -1, -0.28f).normalized;
            float len = 0.06f + 0.13f * open;
            for (int k = 0; k < 7; k++)
            {
                float t = k / 6f;
                Vector3 l = root + dir * len * t + new Vector3(0, 0, -0.06f) * t * t * len / 0.19f;
                tp[k] = HL(l.x, l.y, l.z);
                tr[k] = (0.024f * Mathf.Pow(1 - t, 0.85f) + 0.0015f) * s;
            }
            Tube(Fang, 7, 6, F);
            for (int k = 0; k < 6; k++)
            {
                float zt = 0.36f + 0.075f * k;
                Tooth(HL(sd * 0.82f * HeadW(zt), Lip(zt) + 0.01f, zt), LocalDir(new Vector3(0, -1, -0.4f)), 0.035f * s, 0.009f * s);
                float zl = 0.34f + 0.08f * k;
                Vector3 a = JL(sd * 0.8f * JawW(zl), Lip(zl) - 0.008f, zl);
                Vector3 b = JL(sd * 0.8f * JawW(zl), Lip(zl) + 0.03f, zl - 0.012f);
                Tooth(a, (b - a).normalized, 0.032f * s, 0.008f * s);
            }
        }

        // ------------------------------------------------ the forked tongue
        if (tongue > 0.01f)
        {
            float ext = tongue * 1.0f;
            const int shaft = 12;
            for (int k = 0; k < shaft; k++)
            {
                float t = k / (float)(shaft - 1);
                float z = 0.6f + (0.4f + ext * 0.78f) * t;
                float y = Mathf.Lerp(Lip(0.6f) - 0.03f, Lip(1f) + 0.004f, Smooth(t * 1.6f));
                y += 0.05f * Mathf.Sin(flick * 31f - t * 7f) * t * t * tongue;
                tp[k] = JL(0.004f * Mathf.Sin(flick * 17f + t * 5f), y, z);
                tr[k] = Mathf.Lerp(0.022f, 0.013f, t) * s;
            }
            Vector3 fork = tp[shaft - 1];
            Tube(Tongue, shaft, 8, U);
            Vector3 fwdT = (tp[shaft - 1] - tp[shaft - 2]).normalized;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float spread = 0.07f + 0.03f * Mathf.Sin(flick * 23f);
                for (int k = 0; k < 7; k++)
                {
                    float t = k / 6f;
                    Vector3 side = R * (sd * spread * t * t * 1.6f + sd * 0.006f);
                    tp[k] = fork + (fwdT * (0.26f * ext + 0.04f) * t + side + U * 0.02f * Mathf.Sin(flick * 40f + sd) * t) * s;
                    tr[k] = Mathf.Lerp(0.012f, 0.0015f, t) * s;
                }
                Tube(Tongue, 7, 6, U);
            }
        }

        // ------------------------------------------------ eyes
        for (int sd = -1; sd <= 1; sd += 2)
        {
            const float ze = 0.64f;
            Vector3 c = HL(sd * 0.86f * HeadW(ze), Lip(ze) + 0.1f, ze);
            Vector3 look = LocalDir(new Vector3(sd * 0.9f, 0.1f, 0.42f));
            EyeBall(c, look, 0.058f * s);
            if (sd < 0) eyeL = c + look * 0.045f * s; else eyeR = c + look * 0.045f * s;
        }
        snout = HL(0, Lip(1f), 1f);
        mouthCentre = Vector3.Lerp(HL(0, Lip(0.5f), 0.5f), JL(0, Lip(0.5f), 0.5f), 0.5f);
        throat = HL(0, 0, 0.1f);
    }

    // ================================================================== primitives
    // a tapered tube through tp[0..count) with radii tr[]
    void Tube(int sub, int count, int sides, Vector3 upHint)
    {
        int cols = sides + 1;
        for (int i = 0; i < count; i++)
        {
            Vector3 t = tp[Mathf.Min(i + 1, count - 1)] - tp[Mathf.Max(i - 1, 0)];
            t = t.sqrMagnitude > 1e-10f ? t.normalized : F;
            Vector3 d = upHint - t * Vector3.Dot(upHint, t);
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Perp(t);
            Vector3 x = Vector3.Cross(t, d);
            for (int j = 0; j < cols; j++)
            {
                float ph = j * 2 * Mathf.PI / sides;
                gp[i * cols + j] = tp[i] + (d * Mathf.Cos(ph) + x * Mathf.Sin(ph)) * tr[i];
                guv[i * cols + j] = new Vector2(j / (float)sides, i / (float)(count - 1));
            }
        }
        Grid(sub, count, cols, true, false);
    }

    void Tooth(Vector3 root, Vector3 dir, float len, float rad)
    {
        for (int k = 0; k < 3; k++) { tp[k] = root + dir * len * k * 0.5f; tr[k] = rad * (1 - k * 0.5f) + (k == 2 ? 0.0005f : 0); }
        Tube(Fang, 3, 5, F);
    }

    void EyeBall(Vector3 c, Vector3 look, float r)
    {
        const int rows = 13, sides = 18;
        int cols = sides + 1;
        Vector3 a = look, e1 = Perp(a), e2 = Vector3.Cross(a, e1);
        for (int i = 0; i < rows; i++)
        {
            float psi = Mathf.PI * (1 - i / (float)(rows - 1));
            for (int j = 0; j < cols; j++)
            {
                float ph = 2 * Mathf.PI * j / sides;
                Vector3 nn = a * Mathf.Cos(psi) + (e1 * Mathf.Cos(ph) + e2 * Mathf.Sin(ph)) * Mathf.Sin(psi);
                gp[i * cols + j] = c + nn * r;
                guv[i * cols + j] = new Vector2(j / (float)sides, i / (float)(rows - 1));
            }
        }
        Grid(Eye, rows, cols, true, false);
    }

    // a rows x cols grid of gp/guv: smooth normals from the neighbours, tangents along +u,
    // two triangles per cell (front faces follow cross(d/dcol, d/drow), or its reverse when flipped)
    void Grid(int sub, int rows, int cols, bool wrap, bool flip)
    {
        int b = V.Count;
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                int jp = j + 1, jm = j - 1;
                if (wrap) { if (jp >= cols) jp = 1; if (jm < 0) jm = cols - 2; }
                else { if (jp >= cols) jp = cols - 1; if (jm < 0) jm = 0; }
                int ip = i + 1 < rows ? i + 1 : rows - 1, im = i > 0 ? i - 1 : 0;
                Vector3 dj = gp[i * cols + jp] - gp[i * cols + jm];
                Vector3 di = gp[ip * cols + j] - gp[im * cols + j];
                Vector3 n = Vector3.Cross(dj, di);
                if (flip) n = -n;
                float nm = n.magnitude;
                n = nm > 1e-12f ? n / nm : Vector3.zero;
                float tm = dj.magnitude;
                Vector3 tg = tm > 1e-12f ? dj / tm : Vector3.zero;
                float w = Vector3.Dot(Vector3.Cross(n, tg), di) >= 0 ? 1 : -1;
                V.Add(gp[i * cols + j]);
                N.Add(n);
                Tn.Add(new Vector4(tg.x, tg.y, tg.z, w));
                UV.Add(guv[i * cols + j]);
            }
        // a pinched row (snout tip, tail tip, eye poles) borrows the averaged normal of its neighbour
        for (int i = 0; i < rows; i++)
        {
            Vector3 p0 = gp[i * cols];
            if ((p0 - gp[i * cols + cols / 2]).sqrMagnitude > 1e-12f || (p0 - gp[i * cols + cols / 4]).sqrMagnitude > 1e-12f) continue;
            int nb = i == 0 ? 1 : i - 1;
            if (nb >= rows) continue;
            Vector3 sum = Vector3.zero;
            for (int j = 0; j < cols - (wrap ? 1 : 0); j++) sum += N[b + nb * cols + j];
            sum = sum.sqrMagnitude > 1e-12f ? sum.normalized : Vector3.up;
            for (int j = 0; j < cols; j++) { N[b + i * cols + j] = sum; Tn[b + i * cols + j] = Tn[b + nb * cols + j]; }
        }
        var T = tris[sub];
        for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < cols - 1; j++)
            {
                int A = b + i * cols + j, B = A + 1, C = A + cols, D = C + 1;
                if (!flip) { T.Add(A); T.Add(B); T.Add(C); T.Add(B); T.Add(D); T.Add(C); }
                else { T.Add(A); T.Add(C); T.Add(B); T.Add(B); T.Add(C); T.Add(D); }
            }
    }

    // ================================================================== maths
    static float Sq(float x) => x * x;
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
    static float G(float x, float c, float w) { float d = (x - c) / w; return Mathf.Exp(-d * d); }
    static float Sgn(float x) => x < 0 ? -1 : 1;
    static Vector3 Perp(Vector3 d) => Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;

    static float Knot(float z, float[] k, float[] v)
    {
        int n = k.Length;
        if (z <= k[0]) return v[0];
        if (z >= k[n - 1]) return v[n - 1];
        int i = 0;
        while (z > k[i + 1]) i++;
        float h = k[i + 1] - k[i], t = (z - k[i]) / h;
        float m0 = i > 0 ? (v[i + 1] - v[i - 1]) / (k[i + 1] - k[i - 1]) : (v[1] - v[0]) / (k[1] - k[0]);
        float m1 = i + 2 < n ? (v[i + 2] - v[i]) / (k[i + 2] - k[i]) : (v[n - 1] - v[n - 2]) / (k[n - 1] - k[n - 2]);
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * v[i] + (t3 - 2 * t2 + t) * h * m0 + (-2 * t3 + 3 * t2) * v[i + 1] + (t3 - t2) * h * m1;
    }
}
