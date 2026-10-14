using System.Collections.Generic;
using UnityEngine;

// One primitive of a swarm formation, in world space. Shards are spread over formations in
// proportion to each part's surface area, so a sword, a hand or a bird is literally assembled
// out of flying flagstone chips.
public struct ShardPart
{
    public byte type;        // 0 box, 1 segment (capsule), 2 sphere, 3 ring (torus)
    public Vector3 a, b;     // box: centre, half-size | segment: ends | sphere: centre, (radius) | ring: centre, (radius, tube)
    public Quaternion rot;   // box / ring orientation
    public float r;          // segment radius
    public bool shell;       // surface only (reads crisper) or filled
    public float weight;
    public byte tint;        // 255 = each shard keeps its own stone; otherwise the colour of this part
    public byte sink;        // 1 = once locked on, the shard fuses into a solid body (shrinks away) while formSink is on
    public float shrink;     // 0 full size .. 1 hidden (a part fading in and out)
}

// Moros's arsenal as shard formations. Every builder works in world space so the boss can swing,
// throw and fly the shapes freely; ShardSwarm samples points on them and flies shards there.
public static class ShardForms
{
    // colour the next parts are painted with (index into the shard materials); 255 = natural stone mix
    public const byte Natural = 255, Stone = 0, Red = 1, Ember = 2, Iron = 3, Steel = 4;
    public static byte Tint = Natural;

    public static void Box(List<ShardPart> L, Vector3 c, Quaternion rot, Vector3 half, float wMul = 1, bool shell = true)
    {
        float area = 8 * (half.x * half.y + half.y * half.z + half.x * half.z);
        L.Add(new ShardPart { type = 0, a = c, b = half, rot = rot, shell = shell, weight = area * wMul, tint = Tint });
    }

    public static void Seg(List<ShardPart> L, Vector3 a, Vector3 b, float r, float wMul = 1)
    {
        float len = Vector3.Distance(a, b);
        L.Add(new ShardPart { type = 1, a = a, b = b, r = r, shell = true, weight = (2 * Mathf.PI * r * len + 2 * Mathf.PI * r * r) * wMul, tint = Tint });
    }

    public static void Sphere(List<ShardPart> L, Vector3 c, float r, float wMul = 1)
    {
        L.Add(new ShardPart { type = 2, a = c, b = new Vector3(r, 0, 0), rot = Quaternion.identity, shell = true, weight = 4 * Mathf.PI * r * r * wMul, tint = Tint });
    }

    public static void Ring(List<ShardPart> L, Vector3 c, Quaternion rot, float radius, float tube, float wMul = 1)
    {
        L.Add(new ShardPart { type = 3, a = c, b = new Vector3(radius, tube, 0), rot = rot, shell = true, weight = 4 * Mathf.PI * Mathf.PI * radius * tube * wMul, tint = Tint });
    }

    static Vector3 Perp(Vector3 d)
    {
        var p = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right);
        return p.normalized;
    }

    // ------------------------------------------------------------------ SHARD BLADE
    // A greatsword: grip, pommel, crossguard, a three-step tapering blade and a diamond tip.
    // bladeDir points from the hilt to the tip; flatNormal is the direction the flat faces.
    public static void Sword(List<ShardPart> L, Vector3 hilt, Vector3 bladeDir, Vector3 flatNormal, float length, float s)
    {
        Vector3 bd = bladeDir.normalized;
        Vector3 fn = Vector3.ProjectOnPlane(flatNormal, bd);
        fn = fn.sqrMagnitude > 1e-4f ? fn.normalized : Perp(bd);
        var q = Quaternion.LookRotation(fn, bd);   // x = width, y = length, z = thickness
        Seg(L, hilt - bd * 0.6f * s, hilt, 0.075f * s, 0.9f);
        Sphere(L, hilt - bd * 0.7f * s, 0.14f * s, 0.9f);
        Box(L, hilt + bd * 0.08f * s, q, new Vector3(0.9f, 0.09f, 0.13f) * s);
        Box(L, hilt + bd * 0.08f * s + q * Vector3.right * 0.95f * s, q * Quaternion.Euler(0, 0, 45), Vector3.one * 0.12f * s, 0.8f);
        Box(L, hilt + bd * 0.08f * s - q * Vector3.right * 0.95f * s, q * Quaternion.Euler(0, 0, 45), Vector3.one * 0.12f * s, 0.8f);
        Vector3 b0 = hilt + bd * 0.18f * s;
        float[] w = { 0.3f, 0.26f, 0.2f };
        float sec = length * 0.3f;
        for (int k = 0; k < 3; k++)
            Box(L, b0 + bd * (sec * (k + 0.5f)), q, new Vector3(w[k] * s, sec * 0.5f, 0.04f * s));
        Box(L, b0 + bd * (sec * 3 + 0.12f * s), q * Quaternion.Euler(0, 0, 45), new Vector3(0.2f, 0.2f, 0.04f) * s);
        Seg(L, b0, b0 + bd * sec * 2.9f, 0.035f * s, 0.35f);
    }

    public static Vector3 SwordTip(Vector3 hilt, Vector3 bladeDir, float length, float s) =>
        hilt + bladeDir.normalized * (0.18f * s + length * 0.9f + 0.3f * s);

    // ------------------------------------------------------------------ TITAN GRASP
    // A giant stone hand on a jointed stone arm. fingerDir runs from wrist to fingertips, palmNormal
    // faces what it grabs; curl 0 = open claw, 1 = closed fist around whatever is in front of the palm.
    public static void Hand(List<ShardPart> L, Vector3 wrist, Vector3 fingerDir, Vector3 palmNormal, float s, float curl, Vector3 armRoot)
    {
        Vector3 fu = fingerDir.normalized;
        Vector3 pn = Vector3.ProjectOnPlane(palmNormal, fu);
        pn = pn.sqrMagnitude > 1e-4f ? pn.normalized : Perp(fu);
        Vector3 hw = Vector3.Cross(fu, pn);
        var q = Quaternion.LookRotation(pn, fu);
        Box(L, wrist + fu * 0.55f * s, q, new Vector3(0.56f, 0.52f, 0.17f) * s);
        float[] xs = { -0.41f, -0.14f, 0.14f, 0.41f };
        float[] fl = { 0.82f, 1f, 0.95f, 0.76f };
        float[] len = { 0.44f, 0.37f, 0.31f };
        float[] bend = { 38, 60, 62 };
        for (int f = 0; f < 4; f++)
        {
            Vector3 j = wrist + fu * 1.07f * s + hw * xs[f] * s;
            float ang = 0;
            for (int k = 0; k < 3; k++)
            {
                ang += curl * bend[k] * Mathf.Deg2Rad;
                Vector3 d = (Mathf.Cos(ang) * fu + Mathf.Sin(ang) * pn + hw * xs[f] * 0.35f * (1 - curl)).normalized;
                Vector3 j2 = j + d * len[k] * fl[f] * s;
                Seg(L, j, j2, (0.1f - 0.012f * k) * s);
                j = j2;
            }
            // claw tip
            Box(L, j, Quaternion.LookRotation(pn, fu), new Vector3(0.07f, 0.07f, 0.07f) * s, 0.5f);
        }
        // thumb
        Vector3 tj = wrist + fu * 0.35f * s + hw * 0.55f * s;
        Vector3 open = (fu * 0.55f + hw * 0.75f + pn * 0.3f).normalized;
        Vector3 shut = (pn * 0.75f - hw * 0.55f + fu * 0.3f).normalized;
        Vector3 td = Vector3.Slerp(open, shut, curl);
        Vector3 t1 = tj + td * 0.42f * s;
        Seg(L, tj, t1, 0.11f * s);
        Seg(L, t1, t1 + Vector3.Slerp(td, (pn - hw).normalized, curl * 0.6f) * 0.35f * s, 0.095f * s);
        // wrist cuff and the long stone arm back to Moros
        Ring(L, wrist + fu * 0.02f * s, Quaternion.FromToRotation(Vector3.up, fu), 0.42f * s, 0.07f * s, 0.7f);
        if ((armRoot - wrist).sqrMagnitude < 0.01f) return;   // attached to a body (the titan): no arm
        float reach = Vector3.Distance(wrist, armRoot);
        Vector3 elbow = Vector3.Lerp(armRoot, wrist, 0.5f) + Vector3.up * reach * 0.22f;
        Seg(L, wrist - fu * 0.05f * s, elbow, 0.26f * s, 0.8f);
        Sphere(L, elbow, 0.34f * s, 0.8f);
        Seg(L, elbow, armRoot, 0.22f * s, 0.7f);
    }

    public static Vector3 PalmCentre(Vector3 wrist, Vector3 fingerDir, float s) => wrist + fingerDir.normalized * 0.55f * s;

    // ------------------------------------------------------------------ LANCE STORM
    public static void Spear(List<ShardPart> L, Vector3 tail, Vector3 tip, float s)
    {
        Vector3 d = tip - tail;
        float len = d.magnitude;
        if (len < 0.01f) return;
        d /= len;
        Vector3 fn = Perp(d);
        var q = Quaternion.LookRotation(fn, d);
        Seg(L, tail, tip - d * 0.55f * s, 0.05f * s);
        Box(L, tip - d * 0.3f * s, q, new Vector3(0.16f, 0.3f, 0.03f) * s);
        Box(L, tip - d * 0.06f * s, q * Quaternion.Euler(0, 0, 45), new Vector3(0.09f, 0.09f, 0.03f) * s, 0.6f);
        Box(L, tip - d * 0.62f * s, q, new Vector3(0.22f, 0.04f, 0.05f) * s, 0.6f);
    }

    // ------------------------------------------------------------------ STONE ROC
    // A giant bird of stone: body, chest, neck, blocky head with beak and horn crest, a fan tail,
    // talons, and two long wings whose feathers are rows of stone slabs (as in the reference).
    // flap is the wing angle in degrees (+ up), fold 0..1 sweeps the wings back for a dive.
    public static void Roc(List<ShardPart> L, Vector3 c, Vector3 fwd, Vector3 upHint, float s, float flap, float fold, float talons,
                           out Vector3 eyeL, out Vector3 eyeR)
    {
        Vector3 f = fwd.normalized;
        Vector3 u = Vector3.ProjectOnPlane(upHint, f);
        u = u.sqrMagnitude > 1e-4f ? u.normalized : Vector3.up;
        Vector3 r = Vector3.Cross(u, f);
        var bq = Quaternion.LookRotation(f, u);

        Seg(L, c - f * 1.1f * s, c + f * 0.8f * s, 0.42f * s);
        Sphere(L, c + f * 0.55f * s + u * 0.05f * s, 0.52f * s);
        Vector3 neckTop = c + f * 1.35f * s + u * 0.3f * s;
        Seg(L, c + f * 0.8f * s, neckTop, 0.2f * s);
        Vector3 head = neckTop + f * 0.25f * s + u * 0.05f * s;
        Box(L, head, bq, new Vector3(0.27f, 0.24f, 0.33f) * s);
        Box(L, head + f * 0.46f * s - u * 0.08f * s, bq * Quaternion.Euler(18, 0, 0), new Vector3(0.1f, 0.09f, 0.28f) * s, 0.8f);
        Seg(L, head - f * 0.2f * s + u * 0.2f * s + r * 0.14f * s, head - f * 0.8f * s + u * 0.6f * s + r * 0.3f * s, 0.055f * s, 0.6f);
        Seg(L, head - f * 0.2f * s + u * 0.2f * s - r * 0.14f * s, head - f * 0.8f * s + u * 0.6f * s - r * 0.3f * s, 0.055f * s, 0.6f);
        eyeL = head + f * 0.24f * s + u * 0.07f * s - r * 0.2f * s;
        eyeR = head + f * 0.24f * s + u * 0.07f * s + r * 0.2f * s;

        Vector3 tb = c - f * 1.05f * s;
        for (int k = -2; k <= 2; k++)
        {
            Vector3 td = Quaternion.AngleAxis(k * 13f, u) * -f;
            Box(L, tb + td * 0.75f * s, Quaternion.LookRotation(td, u), new Vector3(0.15f, 0.03f, 0.75f) * s);
        }

        float a = flap * Mathf.Deg2Rad;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 sh = c + f * 0.35f * s + r * side * 0.4f * s + u * 0.15f * s;
            Vector3 wd = (r * side * Mathf.Cos(a) + u * Mathf.Sin(a)).normalized;
            Vector3 swept = (-f * 0.85f + r * side * 0.4f - u * 0.1f).normalized;
            wd = Vector3.Slerp(wd, swept, fold * 0.8f);
            Vector3 elbow = sh + wd * 1.7f * s;
            float a2 = a * 1.35f;
            Vector3 wd2 = (r * side * Mathf.Cos(a2) + u * Mathf.Sin(a2) - f * 0.25f).normalized;
            wd2 = Vector3.Slerp(wd2, swept, fold * 0.9f);
            Vector3 tipP = elbow + wd2 * 1.9f * s;
            Seg(L, sh, elbow, 0.14f * s);
            Sphere(L, elbow, 0.17f * s, 0.5f);
            Seg(L, elbow, tipP, 0.1f * s);
            for (int k = 0; k < 9; k++)
            {
                float t = (k + 0.5f) / 9f;
                Vector3 at = t < 0.47f ? Vector3.Lerp(sh, elbow, t / 0.47f) : Vector3.Lerp(elbow, tipP, (t - 0.47f) / 0.53f);
                Vector3 bone = t < 0.47f ? wd : wd2;
                Vector3 fd = (-f + bone * (0.12f + 0.55f * t)).normalized;
                fd = Vector3.Slerp(fd, -f, fold * 0.5f);
                Vector3 n = Vector3.Cross(bone, fd);
                if (Vector3.Dot(n, u) < 0) n = -n;
                float fl = (0.85f + 1.15f * t) * s;
                Box(L, at + fd * fl * 0.5f, Quaternion.LookRotation(fd, n), new Vector3(0.17f * s, 0.035f * s, fl * 0.5f));
            }
            // talons
            Vector3 hip = c - u * 0.3f * s + r * side * 0.22f * s;
            Vector3 foot = hip - u * 0.65f * s + f * 0.55f * s * talons;
            Seg(L, hip, foot, 0.08f * s, 0.6f);
            for (int k = -1; k <= 1; k++)
                Seg(L, foot, foot + (f * 0.28f - u * 0.12f * (1 - talons) + r * k * 0.1f) * s, 0.035f * s, 0.4f);
        }
    }

    public static Vector3 RocTalons(Vector3 c, Vector3 fwd, Vector3 upHint, float s, float talons)
    {
        Vector3 f = fwd.normalized;
        Vector3 u = Vector3.ProjectOnPlane(upHint, f).normalized;
        return c - u * 0.95f * s + f * 0.55f * s * talons;
    }

    // ------------------------------------------------------------------ SKYBREAKER
    // A giant maul. handleDir runs from the grip to the head; headAxis is the long axis of the head
    // (its -headAxis face is the striking face).
    public static void Hammer(List<ShardPart> L, Vector3 grip, Vector3 handleDir, Vector3 headAxis, float s)
    {
        Vector3 hd = handleDir.normalized;
        Vector3 ha = Vector3.ProjectOnPlane(headAxis, hd);
        ha = ha.sqrMagnitude > 1e-4f ? ha.normalized : Perp(hd);
        Vector3 hc = grip + hd * 2.9f * s;
        var q = Quaternion.LookRotation(ha, hd);
        Seg(L, grip - hd * 0.45f * s, hc, 0.09f * s);
        Sphere(L, grip - hd * 0.55f * s, 0.15f * s, 0.6f);
        Ring(L, grip + hd * 0.4f * s, Quaternion.FromToRotation(Vector3.up, hd), 0.14f * s, 0.04f * s, 0.5f);
        Box(L, hc, q, new Vector3(0.55f, 0.5f, 0.95f) * s);
        Box(L, hc + ha * 0.88f * s, q, new Vector3(0.63f, 0.58f, 0.07f) * s, 0.6f);
        Box(L, hc - ha * 0.88f * s, q, new Vector3(0.63f, 0.58f, 0.07f) * s, 0.6f);
        Seg(L, hc + hd * 0.5f * s, hc + hd * 1.25f * s, 0.12f * s, 0.6f);
    }

    public static Vector3 HammerFace(Vector3 grip, Vector3 handleDir, Vector3 headAxis, float s)
    {
        Vector3 hd = handleDir.normalized;
        Vector3 ha = Vector3.ProjectOnPlane(headAxis, hd).normalized;
        return grip + hd * 2.9f * s - ha * 0.95f * s;
    }

    // ------------------------------------------------------------------ SERPENT DRAGON HEAD
    public static void DragonHead(List<ShardPart> L, Vector3 h, Vector3 fwd, float gape, out Vector3 eyeL, out Vector3 eyeR, float k = 1)
    {
        Vector3 f = fwd.normalized;
        Vector3 u = Vector3.ProjectOnPlane(Vector3.up, f);
        u = u.sqrMagnitude > 1e-4f ? u.normalized : Perp(f);
        Vector3 r = Vector3.Cross(u, f);
        var q = Quaternion.LookRotation(f, u);
        Box(L, h - f * 0.12f * k, q, new Vector3(0.3f, 0.24f, 0.4f) * k);
        Box(L, h + (f * 0.42f + u * 0.03f) * k, q, new Vector3(0.21f, 0.12f, 0.3f) * k);
        Vector3 hinge = h + (f * 0.05f - u * 0.16f) * k;
        Vector3 jd = Quaternion.AngleAxis(gape, r) * f;
        Box(L, hinge + jd * 0.38f * k, Quaternion.LookRotation(jd, Vector3.Cross(jd, r) * -1), new Vector3(0.18f, 0.06f, 0.36f) * k);
        Seg(L, h + (-f * 0.3f + u * 0.2f + r * 0.18f) * k, h + (-f * 1.0f + u * 0.5f + r * 0.28f) * k, 0.05f * k, 0.6f);
        Seg(L, h + (-f * 0.3f + u * 0.2f - r * 0.18f) * k, h + (-f * 1.0f + u * 0.5f - r * 0.28f) * k, 0.05f * k, 0.6f);
        eyeL = h + (f * 0.14f + u * 0.15f - r * 0.22f) * k;
        eyeR = h + (f * 0.14f + u * 0.15f + r * 0.22f) * k;
    }

    // ------------------------------------------------------------------ COLOSSUS
    // A 7 m stone giant with an open rib-cage chest (Moros floats inside it). Parts are listed from
    // the feet up so an ordered build assembles it bottom to top. fistL / fistR are where the fists
    // should be; the arms solve a two-bone reach toward them.
    public const float ShoulderY = 5.1f, UpperArm = 3.0f, Forearm = 3.3f;

    public static Vector3 Shoulder(Vector3 m, Vector3 fwd, int side)
    {
        Vector3 f = fwd.normalized, r = Vector3.Cross(Vector3.up, f);
        return m + Vector3.up * ShoulderY + r * side * 1.95f;
    }

    public static void Colossus(List<ShardPart> L, Vector3 m, Vector3 fwd, Vector3 fistL, Vector3 fistR, out Vector3 eyeL, out Vector3 eyeR)
    {
        Vector3 f = fwd.normalized, u = Vector3.up, r = Vector3.Cross(u, f);
        var q = Quaternion.LookRotation(f, u);
        for (int side = -1; side <= 1; side += 2)
        {
            Box(L, m + r * side * 0.8f + u * 0.2f + f * 0.2f, q, new Vector3(0.5f, 0.2f, 0.7f));
            Box(L, m + r * side * 0.8f + u * 1.4f, q, new Vector3(0.42f, 1.05f, 0.45f));
        }
        Box(L, m + u * 2.75f, q, new Vector3(1.15f, 0.42f, 0.62f));
        Box(L, m + u * 3.95f - f * 0.72f, q, new Vector3(1.25f, 1.15f, 0.13f));
        for (int side = -1; side <= 1; side += 2)
            Box(L, m + u * 3.95f + r * side * 1.18f, q, new Vector3(0.13f, 1.15f, 0.72f));
        for (int k = 0; k < 3; k++)
            Seg(L, m + u * (3.2f + k * 0.58f) + f * 0.66f - r * 1.15f, m + u * (3.2f + k * 0.58f) + f * 0.66f + r * 1.15f, 0.1f);
        Box(L, m + u * ShoulderY, q, new Vector3(1.75f, 0.32f, 0.78f));
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 sh = m + u * ShoulderY + r * side * 1.95f;
            Sphere(L, sh, 0.8f);
            Vector3 target = side < 0 ? fistL : fistR;
            Vector3 d = target - sh;
            float dist = Mathf.Clamp(d.magnitude, 0.5f, UpperArm + Forearm - 0.05f);
            Vector3 dn = d.normalized;
            float aa = (UpperArm * UpperArm - Forearm * Forearm + dist * dist) / (2 * dist);
            float hh = Mathf.Sqrt(Mathf.Max(0, UpperArm * UpperArm - aa * aa));
            Vector3 bend = Vector3.ProjectOnPlane(r * side * 0.8f - f * 0.5f + u * 0.2f, dn).normalized;
            Vector3 elbow = sh + dn * aa + bend * hh;
            Vector3 fist = sh + dn * dist;
            Seg(L, sh, elbow, 0.46f);
            Sphere(L, elbow, 0.5f, 0.6f);
            Seg(L, elbow, fist, 0.5f);
            Box(L, fist + (fist - elbow).normalized * 0.35f, Quaternion.LookRotation(f, (fist - elbow).normalized), new Vector3(0.62f, 0.55f, 0.62f));
        }
        Seg(L, m + u * 5.4f, m + u * 5.75f, 0.35f, 0.5f);
        Vector3 head = m + u * 6.3f + f * 0.1f;
        Box(L, head, q, new Vector3(0.58f, 0.52f, 0.52f));
        Box(L, head - u * 0.6f + f * 0.12f, q, new Vector3(0.45f, 0.14f, 0.45f), 0.7f);
        for (int k = -1; k <= 1; k++)
            Seg(L, head + u * 0.5f + r * k * 0.35f, head + u * (1.15f - Mathf.Abs(k) * 0.25f) + r * k * 0.5f, 0.08f, 0.5f);
        eyeL = head + f * 0.54f + u * 0.08f - r * 0.24f;
        eyeR = head + f * 0.54f + u * 0.08f + r * 0.24f;
    }

    public static Vector3 ColossusFist(Vector3 m, Vector3 fwd, int side, Vector3 target)
    {
        Vector3 sh = Shoulder(m, fwd, side);
        Vector3 d = target - sh;
        float dist = Mathf.Clamp(d.magnitude, 0.5f, UpperArm + Forearm - 0.05f);
        return sh + d.normalized * dist;
    }

    // ------------------------------------------------------------------ EXECUTIONER'S AXE
    // A giant bearded war axe: haft, pommel, socket, a crescent blade facing edgeDir, a back spike
    // and a top spike. haftDir runs from the grip to the head; the flats face across both.
    public static void Axe(List<ShardPart> L, Vector3 grip, Vector3 haftDir, Vector3 edgeDir, float length, float s)
    {
        Vector3 Y = haftDir.normalized;
        Vector3 X = Vector3.ProjectOnPlane(edgeDir, Y);
        X = X.sqrMagnitude > 1e-4f ? X.normalized : Perp(Y);
        Vector3 fn = Vector3.Cross(X, Y);
        byte t0 = Tint;
        Tint = t0 == Natural ? Iron : t0;
        Seg(L, grip - Y * 0.5f * s, grip + Y * length, 0.075f * s);
        Sphere(L, grip - Y * 0.6f * s, 0.15f * s, 0.6f);
        for (int k = 0; k < 3; k++)
            Ring(L, grip + Y * (0.1f + k * 0.35f) * s, Quaternion.FromToRotation(Vector3.up, Y), 0.12f * s, 0.035f * s, 0.4f);
        Vector3 hc = grip + Y * length;
        Box(L, hc, Quaternion.LookRotation(fn, Y), new Vector3(0.2f, 0.36f, 0.16f) * s);
        Tint = t0 == Natural ? Stone : t0;
        Box(L, hc + X * 0.55f * s, Quaternion.LookRotation(fn, Y), new Vector3(0.5f, 0.5f, 0.05f) * s);
        // crescent edge: a band of plates around an arc
        Vector3 c = hc + X * 0.12f * s;
        const int n = 8;
        float R = 1.4f * s;
        float seg = R * Mathf.Sin(136f / (n - 1) * 0.5f * Mathf.Deg2Rad) * 1.15f;
        for (int k = 0; k < n; k++)
        {
            float a = Mathf.Lerp(-68f, 68f, k / (float)(n - 1)) * Mathf.Deg2Rad;
            Vector3 radial = X * Mathf.Cos(a) + Y * Mathf.Sin(a);
            Vector3 tan = -X * Mathf.Sin(a) + Y * Mathf.Cos(a);
            Box(L, c + radial * (R - 0.3f * s), Quaternion.LookRotation(fn, tan), new Vector3(0.34f * s, seg, 0.05f * s));
        }
        Tint = t0 == Natural ? Red : t0;
        for (int k = 0; k < n - 1; k++)   // glowing honed edge along the crescent
        {
            float a0 = Mathf.Lerp(-68f, 68f, k / (float)(n - 1)) * Mathf.Deg2Rad, a1 = Mathf.Lerp(-68f, 68f, (k + 1) / (float)(n - 1)) * Mathf.Deg2Rad;
            Seg(L, c + (X * Mathf.Cos(a0) + Y * Mathf.Sin(a0)) * R, c + (X * Mathf.Cos(a1) + Y * Mathf.Sin(a1)) * R, 0.05f * s, 0.8f);
        }
        Tint = t0 == Natural ? Iron : t0;
        Seg(L, hc - X * 0.18f * s, hc - X * 0.85f * s + Y * 0.12f * s, 0.09f * s, 0.7f);  // back spike
        Seg(L, hc + Y * 0.32f * s, hc + Y * 1.0f * s, 0.08f * s, 0.6f);                   // top spike
        Tint = t0;
    }

    public static Vector3 AxeEdge(Vector3 grip, Vector3 haftDir, Vector3 edgeDir, float length, float s)
    {
        Vector3 Y = haftDir.normalized;
        Vector3 X = Vector3.ProjectOnPlane(edgeDir, Y).normalized;
        return grip + Y * length + X * 1.55f * s;
    }

    // ------------------------------------------------------------------ THE TITAN
    // Moros's ascended form, after the giant of a thousand pieces: a lean, athletic humanoid built
    // from gunmetal shards laid along its muscles. It is a real body: a pelvis that bobs and sways,
    // a spine that bends forward, arches back and twists, legs solved with two-bone IK to wherever
    // its feet are planted (so it can run, crouch and leap), and arms of human proportion.
    // Parts are listed from the feet up so an ordered build raises it out of the ground.
    // scale 1 = 10 m tall.
    public struct TitanPose
    {
        public Vector3 feet, fwd;           // root: the ground point under the pelvis (y rises in a leap), heading
        public float scale, lean, breathe;  // lean: forward bend at the hips in radians (negative arches back)
        public float crouch, twist, roll;   // crouch 0..1 drops the pelvis; torso twist / roll in radians
        public Vector3 sway;                // pelvis bob and sway (world offset)
        public Vector3 footL, footR;        // ankle targets (world)
        public Vector3 wristL, wristR, fingersL, fingersR, palmL, palmR;
        public float curlL, curlR;
    }

    public const float TitanUpper = 2.55f, TitanFore = 2.65f, TitanHand = 1.35f, TitanThigh = 2.4f, TitanShin = 2.35f;
    const float HipY = 5.05f;

    static Vector3 TR(in TitanPose p) => Vector3.Cross(Vector3.up, p.fwd).normalized;

    public static Vector3 TitanPelvis(in TitanPose p) => p.feet + Vector3.up * (HipY - 1.9f * p.crouch) * p.scale + p.sway;

    public static Quaternion TitanTorso(in TitanPose p)
    {
        Vector3 f = p.fwd.normalized, r = TR(p);
        return Quaternion.AngleAxis(p.twist * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(p.lean * Mathf.Rad2Deg, r) * Quaternion.AngleAxis(p.roll * Mathf.Rad2Deg, f);
    }

    // a point on the torso (titan units, y measured from the ground at rest), bent with the spine
    public static Vector3 TitanTorsoPoint(in TitanPose p, float x, float y, float z)
    {
        Vector3 f = p.fwd.normalized, r = TR(p);
        return TitanPelvis(p) + TitanTorso(p) * ((r * x + Vector3.up * (y - HipY) + f * z) * p.scale);
    }

    public static Vector3 TitanShoulder(in TitanPose p, int side) => TitanTorsoPoint(p, side * 2.3f, 8.0f, -0.05f);
    public static Vector3 TitanHead(in TitanPose p) => TitanTorsoPoint(p, 0, 9.6f, 0.2f);
    public static Vector3 TitanChestPoint(in TitanPose p) => TitanTorsoPoint(p, 0, 7.3f, 0.7f);
    public static float TitanReach(float s) => (TitanUpper + TitanFore) * s - 0.05f;

    public static Vector3 TitanWrist(in TitanPose p, int side, Vector3 target)
    {
        Vector3 j = TitanShoulder(p, side);
        return j + Vector3.ClampMagnitude(target - j, TitanReach(p.scale));
    }

    // a muscle: a chain of segments through the given points with the given radii
    static void Muscle(List<ShardPart> L, float w, params (Vector3 p, float r)[] pts)
    {
        for (int k = 0; k + 1 < pts.Length; k++)
            Seg(L, pts[k].p, pts[k + 1].p, (pts[k].r + pts[k + 1].r) * 0.5f, w);
    }

    // two-bone IK: the middle joint of a limb from root a to end c, bending toward hint
    static Vector3 Bend(Vector3 a, ref Vector3 c, float l1, float l2, Vector3 hint)
    {
        Vector3 d = c - a;
        float dist = Mathf.Clamp(d.magnitude, 0.2f, (l1 + l2) * 0.999f);
        Vector3 dn = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
        c = a + dn * dist;
        float aa = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist);
        float hh = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - aa * aa));
        Vector3 bend = Vector3.ProjectOnPlane(hint, dn);
        bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Perp(dn);
        return a + dn * aa + bend * hh;
    }

    public static void Titan(List<ShardPart> L, in TitanPose pose, out Vector3 eyeL, out Vector3 eyeR)
    {
        TitanPose p = pose;   // a copy the local helpers below can use
        float s = p.scale;
        Vector3 f = p.fwd.normalized, u = Vector3.up, r = TR(p);
        var tq = TitanTorso(p);
        Vector3 tf = tq * f, tu = tq * u, tr = tq * r;
        var qu = Quaternion.LookRotation(tf, tu);
        Vector3 pel = TitanPelvis(p);
        Vector3 P(float x, float y, float z) => pel + (r * x + u * (y - HipY) + f * z) * s;   // pelvis frame (heading only)
        Vector3 U(float x, float y, float z) => TitanTorsoPoint(p, x, y, z);                  // torso frame (bends)
        Tint = Steel;

        // ---------------------------------------------------------------- legs (IK to the planted feet)
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Vector3 hip = P(sd * 0.92f, HipY, 0);
            Vector3 ankle = sd < 0 ? p.footL : p.footR;
            Vector3 knee = Bend(hip, ref ankle, TitanThigh * s, TitanShin * s, f + u * 0.05f + r * sd * 0.08f);
            Vector3 front = Vector3.ProjectOnPlane(f, (knee - hip).normalized).normalized;
            Vector3 shinFront = Vector3.ProjectOnPlane(f, (ankle - knee).normalized).normalized;
            // foot: a long arch to splayed toes
            Vector3 heel = ankle + (-f * 0.45f - u * 0.38f) * s, toe = ankle + (f * 1.25f - u * 0.42f) * s;
            Seg(L, heel, toe, 0.26f * s);
            for (int k = -1; k <= 1; k++) Seg(L, ankle + (f * 0.95f + r * k * 0.2f - u * 0.4f) * s, ankle + (f * 1.5f + r * k * 0.28f - u * 0.44f) * s, 0.1f * s, 0.5f);
            Seg(L, ankle, Vector3.Lerp(heel, toe, 0.45f), 0.3f * s);
            // shin and the swelling calf behind it
            Muscle(L, 1f, (ankle, 0.3f * s), (Vector3.Lerp(ankle, knee, 0.55f) - shinFront * 0.1f * s, 0.46f * s), (knee, 0.44f * s));
            Muscle(L, 0.8f, (Vector3.Lerp(ankle, knee, 0.35f) - shinFront * 0.28f * s, 0.26f * s), (Vector3.Lerp(ankle, knee, 0.68f) - shinFront * 0.34f * s, 0.46f * s), (knee - shinFront * 0.2f * s, 0.34f * s));
            Sphere(L, knee + front * 0.12f * s, 0.42f * s, 0.6f);
            // thigh: the quad bulging forward, the hamstring behind
            Muscle(L, 1f, (knee, 0.46f * s), (Vector3.Lerp(knee, hip, 0.5f) + front * 0.18f * s, 0.74f * s), (hip, 0.72f * s));
            Muscle(L, 0.7f, (knee - front * 0.1f * s, 0.3f * s), (Vector3.Lerp(knee, hip, 0.55f) - front * 0.28f * s, 0.55f * s), (hip - f * 0.3f * s, 0.5f * s));
        }
        // ---------------------------------------------------------------- pelvis and a narrow waist
        Muscle(L, 1f, (P(-0.95f, HipY, 0), 0.7f * s), (P(0.95f, HipY, 0), 0.7f * s));
        for (int sd = -1; sd <= 1; sd += 2) Sphere(L, P(sd * 0.55f, 5.2f, -0.42f), 0.58f * s, 0.7f);   // glutes
        Muscle(L, 1f, (U(0, 5.3f, 0.02f), 0.8f * s), (U(0, 6.1f, 0.05f), 0.66f * s), (U(0, 6.7f, 0.05f), 0.86f * s));
        for (int k = 0; k < 4; k++)   // ribbed abs
            Seg(L, U(-0.42f, 5.55f + 0.34f * k, 0.66f), U(0.42f, 5.55f + 0.34f * k, 0.66f), 0.15f * s, 0.55f);
        Seg(L, U(0, 5.45f, 0.72f), U(0, 6.75f, 0.74f), 0.07f * s, 0.3f);
        for (int sd = -1; sd <= 1; sd += 2)
            Muscle(L, 0.7f, (U(sd * 0.7f, 5.3f, 0.2f), 0.32f * s), (U(sd * 0.85f, 6.3f, 0.25f), 0.36f * s), (U(sd * 1.0f, 6.9f, 0.2f), 0.3f * s));
        // ---------------------------------------------------------------- ribcage, pecs, lats, traps
        float br = p.breathe * 0.06f;
        Muscle(L, 1.1f, (U(0, 6.7f, 0.05f), 0.95f * s), (U(0, 7.45f + br, 0.05f), 1.18f * s), (U(0, 8.2f, 0f), 1.0f * s));
        for (int k = 0; k < 5; k++)
        {
            float y = 6.75f + 0.28f * k, wd = 1.02f + 0.06f * k;
            for (int sd = -1; sd <= 1; sd += 2)
                Seg(L, U(sd * 0.25f, y + 0.08f, 0.93f + 0.05f * k), U(sd * wd, y - 0.1f, 0.4f), 0.11f * s, 0.35f);
        }
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Vector3 S = TitanShoulder(p, sd);
            Muscle(L, 0.9f, (U(sd * 0.12f, 7.55f + br, 0.93f), 0.42f * s), (U(sd * 0.8f, 7.7f + br, 0.83f), 0.5f * s), (S + (tf * 0.3f - tu * 0.2f) * s, 0.36f * s));
            Muscle(L, 0.8f, (U(sd * 0.75f, 6.1f, -0.25f), 0.3f * s), (U(sd * 1.3f, 7.1f, -0.2f), 0.48f * s), (S + (-tf * 0.35f - tu * 0.5f) * s, 0.4f * s));
            Muscle(L, 0.8f, (U(sd * 0.25f, 9.0f, -0.12f), 0.3f * s), (U(sd * 1.15f, 8.6f, -0.15f), 0.4f * s), (S + (tu * 0.25f - tf * 0.1f) * s, 0.38f * s));
            Sphere(L, S + tu * 0.1f * s, 0.62f * s, 0.9f);   // deltoid
        }
        Muscle(L, 0.8f, (U(0, 8.35f, 0f), 0.5f * s), (U(0, 9.25f, 0.1f), 0.34f * s));
        for (int sd = -1; sd <= 1; sd += 2) Seg(L, U(sd * 0.28f, 8.45f, 0.3f), U(sd * 0.12f, 9.2f, 0.3f), 0.09f * s, 0.4f);

        // ---------------------------------------------------------------- head: narrow, featureless, a burning slit
        Vector3 H = TitanHead(p);
        Seg(L, H + (-tu * 0.42f + tf * 0.05f) * s, H + (tu * 0.38f - tf * 0.05f) * s, 0.46f * s);
        Box(L, H + (tf * 0.38f - tu * 0.05f) * s, qu, new Vector3(0.32f, 0.5f, 0.05f) * s, 0.8f);
        Seg(L, H + (tf * 0.32f - tu * 0.5f) * s, H + (tf * 0.1f - tu * 0.66f) * s, 0.16f * s, 0.5f);
        for (int sd = -1; sd <= 1; sd += 2)
            Seg(L, H + (tr * sd * 0.3f + tf * 0.2f + tu * 0.3f) * s, H + (tr * sd * 0.36f - tf * 0.3f - tu * 0.2f) * s, 0.12f * s, 0.5f);
        Seg(L, H + (tu * 0.35f + tf * 0.1f) * s, H + (tu * 0.25f - tf * 0.6f) * s, 0.2f * s, 0.6f);
        Tint = Iron;
        Box(L, H + (tu * 0.1f + tf * 0.43f) * s, qu, new Vector3(0.3f, 0.045f, 0.03f) * s, 0.4f);
        Tint = Steel;
        eyeL = H + (tu * 0.1f + tf * 0.45f - tr * 0.15f) * s;
        eyeR = H + (tu * 0.1f + tf * 0.45f + tr * 0.15f) * s;

        // ---------------------------------------------------------------- arms (two-bone IK) and hands
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Vector3 j = TitanShoulder(p, sd);
            Vector3 w = TitanWrist(p, sd, sd < 0 ? p.wristL : p.wristR);
            Vector3 elbow = Bend(j, ref w, TitanUpper * s, TitanFore * s, tr * sd * 0.5f - tf * 0.8f - tu * 0.1f);
            Vector3 upDir = (elbow - j).normalized, front = Vector3.ProjectOnPlane(tf, upDir).normalized;
            Muscle(L, 1f, (j, 0.5f * s), (Vector3.Lerp(j, elbow, 0.5f), 0.46f * s), (elbow, 0.34f * s));
            Muscle(L, 0.7f, (Vector3.Lerp(j, elbow, 0.2f) + front * 0.18f * s, 0.28f * s), (Vector3.Lerp(j, elbow, 0.55f) + front * 0.3f * s, 0.4f * s), (Vector3.Lerp(j, elbow, 0.9f) + front * 0.14f * s, 0.24f * s));
            Muscle(L, 0.6f, (Vector3.Lerp(j, elbow, 0.2f) - front * 0.2f * s, 0.28f * s), (Vector3.Lerp(j, elbow, 0.6f) - front * 0.28f * s, 0.36f * s), (elbow - front * 0.18f * s, 0.22f * s));
            Sphere(L, elbow, 0.32f * s, 0.5f);
            Muscle(L, 1f, (elbow, 0.34f * s), (Vector3.Lerp(elbow, w, 0.3f), 0.43f * s), (Vector3.Lerp(elbow, w, 0.75f), 0.3f * s), (w, 0.25f * s));
            Hand(L, w, sd < 0 ? p.fingersL : p.fingersR, sd < 0 ? p.palmL : p.palmR, TitanHand * s, sd < 0 ? p.curlL : p.curlR, w);
        }
        Tint = Natural;
    }

    // ================================================================== THE ARSENAL
    static void Frame(Vector3 dir, out Vector3 d, out Vector3 side, out Vector3 up)
    {
        d = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        side = Vector3.Cross(Vector3.up, d);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side.Normalize();
        up = Vector3.Cross(d, side);
    }

    // SHARD GATLING: six spinning barrels in a banded housing with an ammunition drum.
    public static void Gatling(List<ShardPart> L, Vector3 c, Vector3 dir, float s, float spin)
    {
        Frame(dir, out var d, out var side, out var up);
        var hoop = Quaternion.FromToRotation(Vector3.up, d);
        Tint = Iron;
        Box(L, c - d * 0.4f * s, Quaternion.LookRotation(d, up), new Vector3(0.34f, 0.34f, 0.5f) * s);
        Sphere(L, c - d * 0.45f * s - up * 0.5f * s, 0.32f * s, 0.7f);
        Tint = Stone;
        for (int k = 0; k < 6; k++)
        {
            float a = spin + k * Mathf.PI / 3;
            Vector3 off = (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.2f * s;
            Seg(L, c + off, c + off + d * 1.9f * s, 0.065f * s, 0.8f);
        }
        Tint = Red;
        Ring(L, c + d * 0.45f * s, hoop, 0.28f * s, 0.05f * s, 0.6f);
        Ring(L, c + d * 1.75f * s, hoop, 0.28f * s, 0.05f * s, 0.6f);
        Tint = Natural;
    }

    public static Vector3 GatlingMuzzle(Vector3 c, Vector3 dir, float s) => c + dir.normalized * 1.95f * s;

    // ROCKET POD: an iron launcher with a 3x3 grid of tubes; loaded tubes show glowing warheads.
    public static void RocketPod(List<ShardPart> L, Vector3 c, Vector3 dir, float s, int loaded)
    {
        Frame(dir, out var d, out var side, out var up);
        var q = Quaternion.LookRotation(d, up);
        var hoop = Quaternion.FromToRotation(Vector3.up, d);
        Tint = Iron;
        Box(L, c, q, new Vector3(0.9f, 0.72f, 0.8f) * s);
        Tint = Red;
        Box(L, c + up * 0.74f * s, q, new Vector3(0.95f, 0.05f, 0.85f) * s, 0.5f);
        int idx = 0;
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++, idx++)
            {
                Vector3 m = RocketTube(c, dir, s, idx);
                Tint = Stone;
                Ring(L, m, hoop, 0.17f * s, 0.045f * s, 0.5f);
                if (idx < loaded) { Tint = Ember; Seg(L, m, m + d * 0.4f * s, 0.1f * s, 0.4f); }
            }
        Tint = Natural;
    }

    public static Vector3 RocketTube(Vector3 c, Vector3 dir, float s, int idx)
    {
        Frame(dir, out var d, out var side, out var up);
        int i = idx / 3 - 1, j = idx % 3 - 1;
        return c + d * 0.82f * s + side * i * 0.55f * s + up * j * 0.42f * s;
    }

    // THE CELESTIAL DRAGON: a Chinese dragon of red stone - a long serpentine body with cream belly
    // plates and golden dorsal fins, four short clawed legs, a flowing mane, branching golden antlers,
    // long whiskers that stream back from the snout, an open jaw with fangs, and a tail tuft.
    // spine[0] is the head, the rest follow the path it flew. t animates the whiskers and mane.
    public static void ChineseDragon(List<ShardPart> L, Vector3[] spine, int n, float s, float gape, float t, out Vector3 eyeL, out Vector3 eyeR)
    {
        Vector3 H = spine[0];
        Vector3 f = spine[0] - spine[1];
        f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        Vector3 u = Vector3.ProjectOnPlane(Vector3.up, f);
        u = u.sqrMagnitude > 1e-4f ? u.normalized : Perp(f);
        Vector3 r = Vector3.Cross(u, f);
        var q = Quaternion.LookRotation(f, u);

        // ---------------------------------------------------------------- head
        Tint = Red;
        Box(L, H, q, new Vector3(0.52f, 0.42f, 0.62f) * s);                                          // skull
        Box(L, H + (f * 1.0f - u * 0.08f) * s, q, new Vector3(0.36f, 0.26f, 0.62f) * s);              // long snout
        Sphere(L, H + (f * 1.62f + u * 0.02f) * s, 0.24f * s, 0.7f);                                  // nose
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Seg(L, H + (f * 0.15f + u * 0.42f + r * sd * 0.3f) * s, H + (f * 0.75f + u * 0.3f + r * sd * 0.22f) * s, 0.12f * s, 0.6f);   // brow ridges
            Seg(L, H + (-f * 0.2f - u * 0.1f + r * sd * 0.5f) * s, H + (-f * 0.7f + u * 0.2f + r * sd * 0.62f) * s, 0.14f * s, 0.5f);    // cheek frills
        }
        Vector3 hinge = H + (f * 0.1f - u * 0.3f) * s;
        Vector3 jd = Quaternion.AngleAxis(gape, r) * f;
        var jq = Quaternion.LookRotation(jd, Vector3.Cross(jd, r) * -1);
        Box(L, hinge + jd * 0.8f * s, jq, new Vector3(0.3f, 0.1f, 0.72f) * s);                        // lower jaw
        Tint = Stone;
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Seg(L, H + (f * 1.3f - u * 0.3f + r * sd * 0.2f) * s, H + (f * 1.25f - u * 0.62f + r * sd * 0.18f) * s, 0.05f * s, 0.4f);  // fangs
            Seg(L, hinge + (jd * 1.25f + r * sd * 0.18f) * s, hinge + (jd * 1.2f + u * 0.25f + r * sd * 0.16f) * s, 0.045f * s, 0.3f);
        }
        // beard under the chin
        for (int k = -1; k <= 1; k++)
            Seg(L, hinge + (jd * 0.4f + r * k * 0.15f) * s, hinge + (jd * 0.2f - u * (0.8f + 0.1f * Mathf.Sin(t * 3 + k)) + r * k * 0.2f - f * 0.3f) * s, 0.07f * s, 0.4f);
        // golden antlers: a main beam sweeping back with two tines
        Tint = Ember;
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Vector3 a0 = H + (u * 0.4f - f * 0.15f + r * sd * 0.22f) * s;
            Vector3 a1 = a0 + (u * 0.7f - f * 0.55f + r * sd * 0.28f) * s;
            Vector3 a2 = a1 + (u * 0.45f - f * 0.75f + r * sd * 0.18f) * s;
            Seg(L, a0, a1, 0.07f * s, 0.45f);
            Seg(L, a1, a2, 0.055f * s, 0.4f);
            Seg(L, Vector3.Lerp(a0, a1, 0.6f), Vector3.Lerp(a0, a1, 0.6f) + (u * 0.5f + f * 0.15f + r * sd * 0.1f) * s, 0.045f * s, 0.3f);
            Seg(L, a1, a1 + (u * 0.45f + f * 0.1f + r * sd * 0.2f) * s, 0.04f * s, 0.3f);
            // whiskers streaming back and rippling
            Vector3 w0 = H + (f * 1.45f - u * 0.05f + r * sd * 0.26f) * s;
            for (int k = 0; k < 5; k++)
            {
                float a = t * 4f - k * 0.9f;
                Vector3 w1 = w0 + (-f * 0.85f + r * sd * 0.22f + u * (0.28f * Mathf.Sin(a) - 0.08f)) * s;
                Seg(L, w0, w1, (0.05f - k * 0.007f) * s, 0.35f);
                w0 = w1;
            }
        }
        eyeL = H + (f * 0.45f + u * 0.3f - r * 0.3f) * s;
        eyeR = H + (f * 0.45f + u * 0.3f + r * 0.3f) * s;

        // ---------------------------------------------------------------- body
        int legA = Mathf.RoundToInt(n * 0.2f), legB = Mathf.RoundToInt(n * 0.55f);
        for (int i = 1; i < n; i++)
        {
            float uu = (float)i / (n - 1);
            float rad = (uu < 0.12f ? Mathf.Lerp(0.42f, 0.66f, uu / 0.12f) : uu < 0.7f ? 0.66f : Mathf.Lerp(0.66f, 0.12f, (uu - 0.7f) / 0.3f)) * s;
            Vector3 a = spine[i - 1], b = spine[i];
            Vector3 bt = a - b;
            bt = bt.sqrMagnitude > 1e-6f ? bt.normalized : f;
            Vector3 bu = Vector3.ProjectOnPlane(Vector3.up, bt);
            bu = bu.sqrMagnitude > 1e-4f ? bu.normalized : Perp(bt);
            Vector3 bR = Vector3.Cross(bu, bt);
            Tint = Red;
            Seg(L, a, b, rad);
            Tint = Stone;
            Seg(L, a - bu * rad * 0.62f, b - bu * rad * 0.62f, rad * 0.5f, 0.45f);                   // belly plates
            // the mane along the neck, dorsal fins along the back
            if (i < n * 0.16f)
            {
                Tint = Stone;
                for (int sd = -1; sd <= 1; sd += 2)
                    Seg(L, b + (bu * rad * 0.8f + bR * sd * rad * 0.4f), b + bu * rad * 0.8f + (bu * 0.55f - bt * 1.0f + bR * sd * 0.35f) * s + bu * 0.15f * Mathf.Sin(t * 5 + i) * s, 0.08f * s, 0.5f);
            }
            else if (i % 2 == 0 && uu < 0.95f)
            {
                Tint = Ember;
                Box(L, b + bu * (rad + 0.18f * s), Quaternion.LookRotation(bt, bu), new Vector3(0.035f, 0.22f, 0.26f) * s * (1.2f - uu), 0.35f);
            }
            // four short legs with three claws each
            if (i == legA || i == legB)
            {
                Tint = Red;
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 hip = b + (bR * sd * rad * 0.8f - bu * rad * 0.3f);
                    Vector3 knee = hip + (bR * sd * 0.5f - bu * 0.55f - bt * 0.35f) * s;
                    Vector3 paw = knee + (-bu * 0.6f + bt * 0.25f + bR * sd * 0.1f) * s;
                    Seg(L, hip, knee, 0.2f * s, 0.7f);
                    Seg(L, knee, paw, 0.14f * s, 0.6f);
                    Tint = Stone;
                    for (int c = -1; c <= 1; c++)
                        Seg(L, paw, paw + (bt * 0.35f - bu * 0.12f + bR * (c * 0.12f + sd * 0.05f)) * s, 0.04f * s, 0.3f);
                    Tint = Red;
                }
            }
        }
        // tail tuft
        Vector3 tail = spine[n - 1], tdir = (spine[n - 1] - spine[n - 2]).normalized;
        Tint = Stone;
        for (int k = -2; k <= 2; k++)
        {
            Vector3 side = Vector3.Cross(Vector3.up, tdir).normalized;
            Seg(L, tail, tail + (tdir * 1.1f + side * k * 0.28f + Vector3.up * 0.2f * Mathf.Sin(t * 4 + k)) * s, 0.07f * s, 0.4f);
        }
        Tint = Natural;
    }

    // THE STONE SPHERE: a great rolling ball of steel shards - iron hoops, armour plates, glowing
    // ember seams and a crown of spikes. rot turns with the roll so you can see it spin.
    public static void StoneSphere(List<ShardPart> L, Vector3 c, float radius, Quaternion rot)
    {
        Tint = Steel;
        L.Add(new ShardPart { type = 2, a = c, b = new Vector3(radius, 0, 0), rot = rot, shell = true, weight = 4 * Mathf.PI * radius * radius, tint = Tint });
        Tint = Iron;
        Ring(L, c, rot, radius * 1.02f, radius * 0.07f, 0.45f);
        Ring(L, c, rot * Quaternion.Euler(0, 0, 90), radius * 1.02f, radius * 0.07f, 0.45f);
        Ring(L, c, rot * Quaternion.Euler(90, 0, 0), radius * 1.02f, radius * 0.07f, 0.45f);
        Tint = Ember;   // seams of fire between the plates
        Ring(L, c, rot * Quaternion.Euler(45, 0, 45), radius * 1.0f, radius * 0.025f, 0.2f);
        Ring(L, c, rot * Quaternion.Euler(-45, 0, 45), radius * 1.0f, radius * 0.025f, 0.2f);
        for (int k = 0; k < 22; k++)
        {
            float z = 1 - 2 * (k + 0.5f) / 22f, ph = k * 2.399963f, sr = Mathf.Sqrt(1 - z * z);
            Vector3 n = rot * new Vector3(sr * Mathf.Cos(ph), z, sr * Mathf.Sin(ph));
            Tint = Iron;   // an armour plate
            Box(L, c + n * radius * 1.02f, Quaternion.LookRotation(n, Perp(n)), new Vector3(radius * 0.2f, radius * 0.2f, radius * 0.04f), 0.35f);
            Tint = Stone;  // and its spike
            Seg(L, c + n * radius * 1.04f, c + n * radius * 1.38f, radius * 0.06f, 0.3f);
        }
        Tint = Natural;
    }

    // THE KING COBRA: a giant steel cobra. sp[0] is the head, sp[1..neckN] the reared neck, the rest
    // lies on the ground. Scaled belly plates, dorsal scutes, iron bands, a ribbed hood with a pale
    // throat and the spectacle mark, brow ridges, nostrils, fangs and a flicking forked tongue; bulge
    // (a spine index, -1 = none) swells where something is being swallowed. Parts run from the tail
    // to the head, so an ordered build assembles it tail first.
    public static void Cobra(List<ShardPart> L, Vector3[] sp, int n, int neckN, float s, float hood, float gape, float tongue, float bulge, out Vector3 eyeL, out Vector3 eyeR)
    {
        for (int i = n - 1; i >= 1; i--)
        {
            float rad = CobraRadius(i, n) * s;
            Vector3 a = sp[i], b = sp[i - 1];
            Vector3 bt = b - a;
            bt = bt.sqrMagnitude > 1e-6f ? bt.normalized : Vector3.forward;
            Vector3 bu = Vector3.ProjectOnPlane(Vector3.up, bt);
            bu = bu.sqrMagnitude > 1e-4f ? bu.normalized : Perp(bt);
            Tint = Steel;
            Seg(L, a, b, rad);
            Tint = Stone;
            Seg(L, a - bu * rad * 0.62f, b - bu * rad * 0.62f, rad * 0.5f, 0.45f);
            if (i % 2 == 0) { Tint = Iron; Box(L, b + bu * rad * 0.92f, Quaternion.LookRotation(bt, bu), new Vector3(rad * 0.35f, 0.06f * s, rad * 0.5f), 0.3f); }
            if (i % 3 == 0) { Tint = Iron; Ring(L, b, Quaternion.FromToRotation(Vector3.up, bt), rad * 1.02f, rad * 0.08f, 0.25f); }
            if (bulge >= 0 && Mathf.Abs(i - bulge) < 0.5f) { Tint = Steel; Sphere(L, Vector3.Lerp(b, a, bulge - Mathf.Floor(bulge)), rad * 1.6f, 0.8f); }
        }

        // ---------------------------------------------------------------- the hood
        Vector3 H = sp[0];
        Vector3 f = sp[0] - sp[1];
        f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        int hi = Mathf.Min(2, n - 1), ho = Mathf.Clamp(neckN - 2, hi + 1, n - 1);
        Vector3 hc = Vector3.Lerp(sp[hi], sp[ho], 0.4f);
        Vector3 nd = sp[hi] - sp[ho];
        nd = nd.sqrMagnitude > 1e-6f ? nd.normalized : Vector3.up;
        Vector3 hf = Vector3.ProjectOnPlane(f, nd);
        hf = hf.sqrMagnitude > 1e-4f ? hf.normalized : Perp(nd);
        Vector3 hr = Vector3.Cross(nd, hf).normalized;
        float W = (0.7f + 1.3f * hood) * s, Hh = Vector3.Distance(sp[hi], sp[ho]) * 0.7f + 0.2f * s;
        var hq = Quaternion.LookRotation(hf, nd);
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Tint = Steel;
            Box(L, hc + hr * sd * W * 0.5f - hf * 0.12f * s, hq, new Vector3(W * 0.5f, Hh, 0.05f * s), 0.9f);
            Tint = Iron;
            for (int k = 0; k < 6; k++)   // the hood's ribs
            {
                float a = Mathf.Lerp(-65f, 65f, k / 5f) * Mathf.Deg2Rad;
                Vector3 dir = hr * sd * Mathf.Cos(a) + nd * Mathf.Sin(a);
                Seg(L, hc, hc + dir * W * (0.95f + 0.25f * Mathf.Cos(a)) - hf * 0.15f * s * Mathf.Abs(Mathf.Sin(a)), 0.07f * s, 0.35f);
            }
            Tint = Red;   // the spectacle mark on its back
            Ring(L, hc - hf * 0.2f * s + hr * sd * W * 0.42f, Quaternion.LookRotation(nd, -hf), 0.28f * s, 0.06f * s, 0.35f);
        }
        Tint = Stone;   // the pale throat
        Box(L, hc + hf * 0.12f * s, hq, new Vector3(W * 0.33f, Hh * 0.85f, 0.04f * s), 0.5f);

        // ---------------------------------------------------------------- the head
        Vector3 u = Vector3.ProjectOnPlane(Vector3.up, f);
        u = u.sqrMagnitude > 1e-4f ? u.normalized : Perp(f);
        Vector3 r = Vector3.Cross(u, f);
        var q = Quaternion.LookRotation(f, u);
        Tint = Steel;
        Box(L, H, q, new Vector3(0.55f, 0.26f, 0.62f) * s);
        Box(L, H + (f * 0.62f - u * 0.03f) * s, q, new Vector3(0.4f, 0.2f, 0.3f) * s, 0.8f);
        for (int sd = -1; sd <= 1; sd += 2)
            Seg(L, H + (f * 0.05f + u * 0.24f + r * sd * 0.3f) * s, H + (f * 0.55f + u * 0.2f + r * sd * 0.22f) * s, 0.09f * s, 0.5f);
        Tint = Iron;
        for (int sd = -1; sd <= 1; sd += 2) Sphere(L, H + (f * 0.86f + u * 0.08f + r * sd * 0.12f) * s, 0.05f * s, 0.2f);
        Vector3 hinge = H + (f * 0.05f - u * 0.2f) * s;
        Vector3 jd = Quaternion.AngleAxis(gape, r) * f;
        var jq = Quaternion.LookRotation(jd, Vector3.Cross(jd, r) * -1);
        Tint = Steel;
        Box(L, hinge + jd * 0.5f * s, jq, new Vector3(0.44f, 0.07f, 0.55f) * s);
        Tint = Red;   // the mouth
        Box(L, hinge + (jd * 0.32f + u * 0.06f) * s, jq, new Vector3(0.34f, 0.02f, 0.4f) * s, 0.35f);
        Tint = Stone;
        for (int sd = -1; sd <= 1; sd += 2)
            Seg(L, H + (f * 0.72f - u * 0.18f + r * sd * 0.22f) * s, H + (f * 0.78f - u * (0.5f + gape * 0.004f) + r * sd * 0.2f) * s, 0.05f * s, 0.5f);
        if (tongue > 0.05f)
        {
            Tint = Red;   // the forked tongue
            Vector3 t0 = hinge + jd * 0.7f * s, t1 = t0 + f * tongue * 1.1f * s;
            Seg(L, t0, t1, 0.035f * s, 0.3f);
            for (int sd = -1; sd <= 1; sd += 2) Seg(L, t1, t1 + (f * 0.3f + r * sd * 0.14f) * s * tongue, 0.025f * s, 0.2f);
        }
        eyeL = H + (f * 0.3f + u * 0.2f - r * 0.34f) * s;
        eyeR = H + (f * 0.3f + u * 0.2f + r * 0.34f) * s;
        Tint = Natural;
    }

    // the cobra's thickness along its body (0 = head .. n-1 = tail tip), in cobra units
    public static float CobraRadius(int i, int n)
    {
        float u = i / (float)(n - 1);
        return u < 0.12f ? Mathf.Lerp(0.42f, 0.62f, u / 0.12f) : u < 0.55f ? 0.62f : Mathf.Lerp(0.62f, 0.1f, (u - 0.55f) / 0.45f);
    }

    // THE DRILL: a spinning drill bit of steel - a fluted cone with a banded collar. tip is the point,
    // axis runs from the collar to the tip.
    public static void Drill(List<ShardPart> L, Vector3 tip, Vector3 axis, float length, float radius, float spin)
    {
        Vector3 a = axis.normalized, bse = tip - a * length;
        Vector3 x = Perp(a), y = Vector3.Cross(a, x);
        Tint = Steel;
        for (int k = 0; k < 6; k++)
        {
            float u0 = k / 6f, u1 = (k + 1) / 6f;
            Seg(L, Vector3.Lerp(bse, tip, u0), Vector3.Lerp(bse, tip, u1), radius * (1 - u0 * 0.92f) * 0.75f, 0.8f);
        }
        Tint = Iron;
        for (int fl = 0; fl < 4; fl++)   // spiral flutes turning with the spin
        {
            Vector3 prev = bse;
            for (int k = 0; k <= 10; k++)
            {
                float uu = k / 10f;
                float ang = spin + fl * Mathf.PI * 0.5f + uu * Mathf.PI * 3f;
                float rr = radius * (1 - uu * 0.95f);
                Vector3 pnt = Vector3.Lerp(bse, tip, uu) + (x * Mathf.Cos(ang) + y * Mathf.Sin(ang)) * rr;
                if (k > 0) Seg(L, prev, pnt, radius * 0.1f * (1.1f - uu * 0.7f), 0.5f);
                prev = pnt;
            }
        }
        Tint = Stone;
        Ring(L, bse, Quaternion.FromToRotation(Vector3.up, a), radius * 1.05f, radius * 0.16f, 0.6f);
        Seg(L, bse, bse - a * length * 0.18f, radius * 0.55f, 0.6f);
        Tint = Natural;
    }

    // THE WALL: a great rampart of stone blocks laid in courses, iron-banded, with a jagged crest.
    // bend curls its ends forward (a trap closing), tilt topples it forward. Blocks are listed course
    // by course from the ground up, so an ordered build raises it out of the street.
    public static Vector3 WallBase(Vector3 centre, Vector3 fwd, float width, float bend, float x)
    {
        Vector3 f = new Vector3(fwd.x, 0, fwd.z).normalized, rgt = Vector3.Cross(Vector3.up, f);
        float half = width * 0.5f, k = x / half;
        return centre + rgt * x + f * (bend * k * k * width * 0.35f);
    }

    public const int WallCols = 14, WallRows = 6;

    // one block of the wall (row from the ground up): its centre, orientation and half size
    public static void WallBlock(Vector3 centre, Vector3 fwd, float width, float height, float tilt, float bend, int row, int col,
                                 out Vector3 c, out Quaternion q, out Vector3 halfSize)
    {
        Vector3 f = new Vector3(fwd.x, 0, fwd.z).normalized, rgt = Vector3.Cross(Vector3.up, f);
        float half = width * 0.5f, colW = width / WallCols, rowH = height / WallRows;
        float x = -half + (col + 0.5f) * colW;
        Vector3 b = WallBase(centre, f, width, bend, x);
        Vector3 tang = (rgt + f * (bend * 2 * x / (half * half) * width * 0.35f)).normalized;
        Vector3 nrm = Vector3.Cross(tang, Vector3.up).normalized;
        q = Quaternion.AngleAxis(tilt, tang) * Quaternion.LookRotation(nrm, Vector3.up);
        c = b + q * Vector3.up * ((row + 0.5f) * rowH);
        halfSize = new Vector3((row % 2 == 0 ? 0.49f : 0.46f) * colW, rowH * 0.47f, 0.55f);
    }

    public static void ShardWall(List<ShardPart> L, Vector3 centre, Vector3 fwd, float width, float height, float tilt, float bend)
    {
        Vector3 f = new Vector3(fwd.x, 0, fwd.z).normalized, rgt = Vector3.Cross(Vector3.up, f);
        const int cols = 14, rows = 6;
        float half = width * 0.5f, colW = width / cols, rowH = height / rows, thick = 1.1f;
        for (int row = 0; row <= rows; row++)
            for (int col = 0; col < cols; col++)
            {
                float x = -half + (col + 0.5f) * colW;
                Vector3 b = WallBase(centre, f, width, bend, x);
                Vector3 tang = (rgt + f * (bend * 2 * x / (half * half) * width * 0.35f)).normalized;
                Vector3 nrm = Vector3.Cross(tang, Vector3.up).normalized;
                var q = Quaternion.AngleAxis(tilt, tang) * Quaternion.LookRotation(nrm, Vector3.up);
                Vector3 up = q * Vector3.up;
                if (row == rows)
                {
                    // the jagged crest
                    Tint = Steel;
                    Seg(L, b + up * height, b + up * (height + 0.8f + 0.6f * ((col * 37) % 3)) + nrm * 0.2f, 0.2f, 0.35f);
                    continue;
                }
                Tint = row == 1 || row == 4 ? Iron : Stone;
                float w = (row % 2 == 0 ? 0.49f : 0.46f) * colW;
                Box(L, b + up * (row + 0.5f) * rowH, q, new Vector3(w, rowH * 0.47f, thick * 0.5f), 1f);
            }
        Tint = Natural;
    }

    // ================================================================== KAITO'S STONE GAUNTLETS
    // Stones wrapped around each fist: a knuckle block, an iron band, three spikes and a bracer.
    public static void Gauntlet(List<ShardPart> L, Vector3 wrist, Vector3 knuckles, Vector3 upHint)
    {
        Vector3 f = knuckles - wrist;
        if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
        f.Normalize();
        Vector3 u = Vector3.ProjectOnPlane(upHint, f);
        u = u.sqrMagnitude > 1e-4f ? u.normalized : Perp(f);
        Vector3 r = Vector3.Cross(u, f);
        var q = Quaternion.LookRotation(f, u);
        Tint = Stone;
        Box(L, knuckles, q, new Vector3(0.1f, 0.075f, 0.09f), 1.2f, false);
        Tint = Iron;
        Ring(L, Vector3.Lerp(wrist, knuckles, 0.35f), Quaternion.FromToRotation(Vector3.up, f), 0.075f, 0.022f, 0.8f);
        Box(L, wrist - f * 0.1f, q, new Vector3(0.065f, 0.065f, 0.11f), 0.8f);
        Tint = Stone;
        for (int k = -1; k <= 1; k++) Seg(L, knuckles + r * k * 0.055f + f * 0.06f, knuckles + r * k * 0.055f + f * 0.15f + u * 0.02f, 0.017f, 0.4f);
        Tint = Natural;
    }
}
