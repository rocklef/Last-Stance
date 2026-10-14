using UnityEngine;

// Foot locking with two-bone IK, after Daniel Holden's "Inverse Kinematics and Foot Locking"
// (theorangeduck.com): a foot whose toe is near the floor and barely moving is in contact; its toe
// position is locked to the floor and the hip/knee are re-solved so the planted foot stays put
// instead of skating or twisting while the body strikes, turns and reels. Locks fade in and out
// smoothly, the knee keeps the animation's bend direction, extension is soft-clamped so the leg never
// snaps straight, and no foot is ever pushed below the floor.
[DefaultExecutionOrder(60)]
[RequireComponent(typeof(Animator))]
public class FootLock : MonoBehaviour
{
    public float contactSpeed = 0.35f;     // toe speed (m/s) under which the foot counts as planted
    public float contactHeight = 0.09f;    // toe height (m) under which the foot can plant
    public float unlockDistance = 0.28f;   // animation pulled the toe this far from its lock: let go
    public float blendTime = 0.12f;
    public bool active = true;

    class Leg
    {
        public Transform hip, knee, ankle, toe;
        public float l1, l2;
        public Vector3 lastToe;
        public bool locked;
        public Vector3 lockPos;
        public float weight;
        public float ankleRest;
    }

    Leg left, right;
    Animator anim;
    // A strike should only constrain the weight-bearing leg.  Letting the IK solver grab the
    // kicking leg on its first planted frame is what made high kicks and knees look rubbery.
    bool allowLeft = true, allowRight = true;

    void Awake()
    {
        anim = GetComponent<Animator>();
        left = Make(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
        right = Make(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
    }

    Leg Make(HumanBodyBones h, HumanBodyBones k, HumanBodyBones a, HumanBodyBones t)
    {
        var l = new Leg { hip = anim.GetBoneTransform(h), knee = anim.GetBoneTransform(k), ankle = anim.GetBoneTransform(a), toe = anim.GetBoneTransform(t) };
        if (l.hip == null || l.knee == null || l.ankle == null) return null;
        l.l1 = Vector3.Distance(l.hip.position, l.knee.position);
        l.l2 = Vector3.Distance(l.knee.position, l.ankle.position);
        l.ankleRest = Mathf.Max(0.06f, l.ankle.position.y - transform.position.y);
        l.lastToe = Toe(l);
        return l;
    }

    static Vector3 Toe(Leg l) => l.toe != null ? l.toe.position : l.ankle.position + l.ankle.forward * 0.12f;

    // Drop all locks immediately (knockdowns, throws, teleports).
    public void ReleaseAll()
    {
        foreach (var l in new[] { left, right }) if (l != null) { l.locked = false; l.weight = 0; l.lastToe = Toe(l); }
    }

    // Called by Fighter when an action starts.  Hand strikes keep both feet grounded; a kick or
    // knee keeps only its support leg planted.  This is deliberately a contact policy rather than
    // a hard animation override, so the authored mocap still owns the airborne part of a stunt.
    public void SetSupport(bool leftSupported, bool rightSupported)
    {
        allowLeft = leftSupported;
        allowRight = rightSupported;
        if (!allowLeft && left != null) { left.locked = false; left.weight = 0; }
        if (!allowRight && right != null) { right.locked = false; right.weight = 0; }
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0) return;
        Solve(left, dt, allowLeft);
        Solve(right, dt, allowRight);
    }

    void Solve(Leg l, float dt, bool allowed)
    {
        if (l == null) return;
        Vector3 toe = Toe(l);
        float floor = transform.position.y;
        float speed = new Vector3(toe.x - l.lastToe.x, 0, toe.z - l.lastToe.z).magnitude / dt;
        l.lastToe = toe;

        if (!active || !allowed)
        {
            l.locked = false;
        }
        else
        {
            bool contact = toe.y - floor < contactHeight && speed < contactSpeed;
            if (!l.locked && contact)
            {
                l.locked = true;
                l.lockPos = new Vector3(toe.x, toe.y, toe.z);
            }
            else if (l.locked)
            {
                float drift = new Vector3(toe.x - l.lockPos.x, 0, toe.z - l.lockPos.z).magnitude;
                if (drift > unlockDistance || toe.y - floor > contactHeight + 0.06f) l.locked = false;
            }
        }
        float goal = l.locked ? 1 : 0;
        l.weight = Mathf.MoveTowards(l.weight, goal, dt / blendTime);
        float w = l.weight * l.weight * (3 - 2 * l.weight);

        // target for the ankle: where the ankle must be so the toe sits on its lock
        Vector3 ankleToToe = toe - l.ankle.position;
        Vector3 ankleTarget = l.ankle.position;
        if (w > 0.001f) ankleTarget = Vector3.Lerp(l.ankle.position, l.lockPos - ankleToToe, w);
        // never below the floor
        float minY = floor + l.ankleRest * 0.85f;
        if (ankleTarget.y < minY) ankleTarget.y = minY;
        if ((ankleTarget - l.ankle.position).sqrMagnitude < 1e-6f) return;

        var footRot = l.ankle.rotation;
        TwoBone(l.hip, l.knee, l.ankle, ankleTarget, l.l1, l.l2);
        l.ankle.rotation = footRot;   // keep the animated foot orientation
    }

    // Analytic two-bone IK (cosine rule), keeping the animation's bend plane for the knee.
    static void TwoBone(Transform a, Transform b, Transform c, Vector3 t, float lab, float lcb)
    {
        Vector3 pa = a.position, pb = b.position, pc = c.position;
        float maxExt = (lab + lcb) * 0.995f;
        float soft = 0.03f;
        float lat = Vector3.Distance(pa, t);
        // soft clamp so a stretched leg eases toward straight instead of popping
        if (lat > maxExt - soft) lat = maxExt - soft * Mathf.Exp(-(lat - (maxExt - soft)) / soft);
        lat = Mathf.Max(lat, 0.01f);

        float acab0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pc - pa).normalized, (pb - pa).normalized), -1, 1));
        float babc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pa - pb).normalized, (pc - pb).normalized), -1, 1));
        float acat0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((pc - pa).normalized, (t - pa).normalized), -1, 1));
        float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2 * lab * lat), -1, 1));
        float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2 * lab * lcb), -1, 1));

        Vector3 axis0 = Vector3.Cross(pc - pa, pb - pa);
        if (axis0.sqrMagnitude < 1e-8f) axis0 = a.right;
        axis0.Normalize();
        Vector3 axis1 = Vector3.Cross(pc - pa, t - pa);
        if (axis1.sqrMagnitude < 1e-8f) axis1 = axis0;
        axis1.Normalize();

        var r0 = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis0);
        var r1 = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis0);
        var r2 = Quaternion.AngleAxis(acat0 * Mathf.Rad2Deg, axis1);
        var ra = a.rotation;
        var rb = b.rotation;
        b.rotation = r1 * rb;
        a.rotation = r2 * r0 * ra;
    }
}
