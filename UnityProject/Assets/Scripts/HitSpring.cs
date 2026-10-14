using UnityEngine;

// Procedural hit reactions layered on top of the mocap, the way console fighters do it: a hit
// kicks a spring-damper on the spine, neck and head, which bends the body away from the point of
// impact and then settles back with a little overshoot, while the underlying animation (guard,
// walk, attack recovery) keeps playing. No clip switch, so nothing snaps or bounces.
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(Animator))]
public class HitSpring : MonoBehaviour
{
    [Tooltip("Spring stiffness; higher settles faster.")]
    public float stiffness = 340f;
    [Tooltip("Damping ratio: 1 = no overshoot, lower = more wobble.")]
    public float dampingRatio = 0.5f;
    public float maxAngle = 38f;

    Transform[] bones;
    float[] weights;
    Transform head, chest;
    Vector3 bend, bendVel;     // lean away from the hit (rotation vector, degrees)
    float twist, twistVel;     // yaw of the struck shoulder backwards
    float headKick, headVel;   // extra snap of the head for high hits
    Vector3 headAxis;

    void Awake()
    {
        var a = GetComponent<Animator>();
        var spec = new (HumanBodyBones b, float w)[]
        {
            (HumanBodyBones.Spine, 0.25f), (HumanBodyBones.Chest, 0.35f), (HumanBodyBones.UpperChest, 0.25f),
            (HumanBodyBones.Neck, 0.2f), (HumanBodyBones.Head, 0.25f),
        };
        var bl = new System.Collections.Generic.List<Transform>();
        var wl = new System.Collections.Generic.List<float>();
        foreach (var (b, w) in spec)
        {
            var t = a.GetBoneTransform(b);
            if (t != null) { bl.Add(t); wl.Add(w); }
        }
        bones = bl.ToArray();
        weights = wl.ToArray();
        head = a.GetBoneTransform(HumanBodyBones.Head);
        chest = a.GetBoneTransform(HumanBodyBones.Chest) ?? a.GetBoneTransform(HumanBodyBones.Spine);
    }

    // dir: horizontal direction the blow travels (attacker -> victim). strength ~1 for a jab, ~2 for a heavy.
    public void Kick(Vector3 dir, Vector3 point, float strength, bool high)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        Vector3 axis = Vector3.Cross(Vector3.up, dir);            // rotating about this tips the top of the body along dir
        float side = chest ? Mathf.Sign(Vector3.Dot(point - chest.position, transform.right)) : 0;
        // give an immediate displacement so the hit-stop freeze frame already shows the snap
        bend += axis * (9f * strength);
        bendVel += axis * (260f * strength);
        twist += -side * 5f * strength;
        twistVel += -side * 160f * strength;
        if (high)
        {
            headAxis = axis;
            headKick += 10f * strength;
            headVel += 320f * strength;
        }
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0 || bones == null) return;
        float c = 2f * dampingRatio * Mathf.Sqrt(stiffness);
        bendVel += (-stiffness * bend - c * bendVel) * dt;
        bend += bendVel * dt;
        twistVel += (-stiffness * twist - c * twistVel) * dt;
        twist += twistVel * dt;
        headVel += (-stiffness * 1.3f * headKick - c * 1.1f * headVel) * dt;
        headKick += headVel * dt;
        bend = Vector3.ClampMagnitude(bend, maxAngle);
        twist = Mathf.Clamp(twist, -maxAngle, maxAngle);
        headKick = Mathf.Clamp(headKick, -maxAngle, maxAngle);

        float mag = bend.magnitude;
        if (mag < 0.01f && Mathf.Abs(twist) < 0.01f && Mathf.Abs(headKick) < 0.01f) return;
        Vector3 ax = mag > 0.0001f ? bend / mag : Vector3.right;
        for (int i = 0; i < bones.Length; i++)
        {
            var r = Quaternion.AngleAxis(mag * weights[i], ax) * Quaternion.AngleAxis(twist * weights[i], Vector3.up);
            bones[i].rotation = r * bones[i].rotation;
        }
        if (head != null && Mathf.Abs(headKick) > 0.01f)
            head.rotation = Quaternion.AngleAxis(headKick, headAxis) * head.rotation;
    }
}
