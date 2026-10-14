using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Each breath of the Roc's ash is a living weapon made from separate arrowhead shards. The
// pieces travel together, reform into recognizable shapes, attack the player, and can be punched
// apart with the existing Kaito strike hitboxes.
public class RocConstruct : MonoBehaviour
{
    static readonly List<RocConstruct> live = new List<RocConstruct>();
    static Mesh sharedArrow;
    public static int LiveCount => live.Count;
    public static Mesh ArrowMesh => sharedArrow ? sharedArrow : (sharedArrow = BuildArrowMesh());

    Fighter hero;
    Transform[] shards;
    Material material;
    int form, health = 3;
    float age, nextAttack;
    bool attacking;

    public static bool TryHit(Vector3 point, float radius, float damage)
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var c = live[i];
            if (!c) { live.RemoveAt(i); continue; }
            for (int s = 0; s < c.shards.Length; s++)
            {
                var part = c.shards[s];
                if (!part || !part.gameObject.activeInHierarchy) continue;
                if ((part.position - point).sqrMagnitude > (radius + .3f) * (radius + .3f)) continue;
                part.gameObject.SetActive(false);
                c.health--;
                FightCamera.Shake(.12f);
                if (c.health <= 0) c.BreakApart(point);
                return true;
            }
        }
        return false;
    }

    public void Begin(Fighter target, int shape, Material sharedMaterial)
    {
        hero = target; form = shape; live.Add(this);
        material = new Material(sharedMaterial);
        shards = new Transform[30];
        for (int i = 0; i < shards.Length; i++)
        {
            var g = new GameObject("AshArrow_" + i);
            g.transform.SetParent(transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = ArrowMesh;
            var renderer = g.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            shards[i] = g.transform;
            float u = i / (float)(shards.Length - 1);
            g.transform.localPosition = new Vector3(Random.Range(-.25f, .25f), u * 1.4f, Random.Range(-.3f, .3f));
        }
        nextAttack = Time.time + 1.1f;
    }

    void Update()
    {
        if (!hero) return;
        age += Time.deltaTime;
        Vector3 delta = hero.transform.position + Vector3.up * 1.05f - transform.position;
        Vector3 flat = new Vector3(delta.x, 0, delta.z);
        float distance = flat.magnitude;
        if (!attacking)
        {
            Vector3 desired = hero.transform.position - flat.normalized * 2.1f + hero.transform.right * Mathf.Sin(age * 1.3f + form) * 1.2f;
            desired.y = Mathf.Clamp(hero.transform.position.y + .8f + Mathf.Sin(age * 2f + form) * .8f, .7f, 4.2f);
            transform.position = Vector3.MoveTowards(transform.position, desired, Time.deltaTime * (form == 0 ? 4.1f : 3.2f));
            transform.rotation = Quaternion.LookRotation(flat.sqrMagnitude > .01f ? flat : Vector3.forward);
            ArrangeShape();
            if (Time.time >= nextAttack && distance < 7.5f)
            {
                attacking = true;
                nextAttack = Time.time + Random.Range(2.1f, 2.8f);
            }
        }
        else
        {
            transform.position = Vector3.MoveTowards(transform.position, hero.transform.position + Vector3.up * .8f, Time.deltaTime * 11f);
            for (int i = 0; i < shards.Length; i++) if (shards[i] && shards[i].gameObject.activeSelf) shards[i].localPosition = Vector3.up * (i / 30f - .5f) * 1.5f;
            if (distance < 1.05f)
            {
                Vector3 away = (hero.transform.position - transform.position).normalized;
                GameManager.I.hud.Announce(FormName() + " STRIKE", "DODGE OR BREAK THE SHARDS", .75f, new Color(1f, .38f, .12f));
                hero.RocStrike(away, form == 0 ? 3.8f : 2.9f, form == 0 ? 5.5f : 4.2f);
                FightCamera.Shake(.34f);
                attacking = false;
            }
        }
    }

    string FormName() { return form switch { 0 => "ASH SERPENT", 1 => "WING BLADE", 2 => "CORKSCREW PIKE", 3 => "ARROW FAN", _ => "ASH SPIRAL" }; }

    void ArrangeShape()
    {
        for (int i = 0; i < shards.Length; i++)
        {
            var p = shards[i]; if (!p || !p.gameObject.activeSelf) continue;
            float u = i / (float)(shards.Length - 1), a = u * Mathf.PI * 2;
            Vector3 pos, tangent;
            switch (form)
            {
                case 0: // A moving S-shaped segmented serpent
                    pos = new Vector3((u - .5f) * 4.8f, Mathf.Sin(u * 12f + age * 3f) * .72f, Mathf.Cos(u * 12f + age * 3f) * .45f);
                    tangent = new Vector3(1, Mathf.Cos(u * 12f + age * 3f) * 2.2f, -Mathf.Sin(u * 12f + age * 3f)); break;
                case 1: // Twin crescent / wings of arrow blades
                    float side = i % 2 == 0 ? -1 : 1; float v = (i / 2) / 14f;
                    pos = new Vector3(side * (.25f + v * 2.9f), Mathf.Sin(v * Mathf.PI) * 1.25f, Mathf.Cos(v * Mathf.PI) * .32f);
                    tangent = new Vector3(side * .55f, .62f, 0); break;
                case 2: // Long drill-like lance, spiralling around its axis
                    pos = new Vector3(Mathf.Cos(a + age * 5f) * (.22f + u * .42f), (u - .5f) * 4.1f, Mathf.Sin(a + age * 5f) * (.22f + u * .42f));
                    tangent = new Vector3(-Mathf.Sin(a + age * 5f), 1, Mathf.Cos(a + age * 5f)); break;
                case 3: // A fan of falling arrows
                    float fan = (u - .5f) * 2.1f;
                    pos = new Vector3(fan * 2.5f, 1.2f - Mathf.Abs(fan) * .8f, Mathf.Sin(u * Mathf.PI) * .45f);
                    tangent = new Vector3(fan * .3f, -1, 0); break;
                default: // Dense coil forming a spinning halo
                    float theta = u * Mathf.PI * 5f + age * 2.6f;
                    pos = new Vector3(Mathf.Cos(theta) * 1.65f, (u - .5f) * 3.3f, Mathf.Sin(theta) * 1.25f);
                    tangent = new Vector3(-Mathf.Sin(theta), .55f, Mathf.Cos(theta)); break;
            }
            p.localPosition = Vector3.Lerp(p.localPosition, pos, 1 - Mathf.Exp(-12 * Time.deltaTime));
            p.localRotation = Quaternion.Slerp(p.localRotation, Quaternion.FromToRotation(Vector3.up, tangent), 1 - Mathf.Exp(-12 * Time.deltaTime));
            float taper = i < 2 ? 1.25f : .85f + .25f * Mathf.Sin(u * Mathf.PI);
            p.localScale = Vector3.one * (.38f * taper);
        }
    }

    void BreakApart(Vector3 hit)
    {
        GameManager.I.hud.Announce(FormName() + " BROKEN", "THE SWARM LOSES ITS SHAPE", .9f, new Color(.95f, .7f, .25f));
        foreach (var p in shards)
        {
            if (!p) continue;
            p.SetParent(null, true);
            var rb = p.gameObject.AddComponent<Rigidbody>(); rb.mass = .035f; rb.useGravity = true;
            rb.AddExplosionForce(2.2f, hit, 3.2f, .35f, ForceMode.Impulse);
            Destroy(p.gameObject, 2.4f);
        }
        Destroy(gameObject);
    }

    static Mesh BuildArrowMesh()
    {
        var mesh = new Mesh { name = "RocArrowSegment" };
        mesh.vertices = new[] {
            new Vector3(-.055f,-.5f,-.035f), new Vector3(.055f,-.5f,-.035f), new Vector3(.055f,.12f,-.035f), new Vector3(.2f,.1f,-.035f), new Vector3(0,.5f,-.035f), new Vector3(-.2f,.1f,-.035f), new Vector3(-.055f,.12f,-.035f),
            new Vector3(-.055f,-.5f,.035f), new Vector3(.055f,-.5f,.035f), new Vector3(.055f,.12f,.035f), new Vector3(.2f,.1f,.035f), new Vector3(0,.5f,.035f), new Vector3(-.2f,.1f,.035f), new Vector3(-.055f,.12f,.035f)
        };
        var tris = new List<int>();
        for (int i = 1; i < 6; i++) { tris.AddRange(new[] { 0, i, i + 1 }); tris.AddRange(new[] { 7, 7 + i + 1, 7 + i }); }
        for (int i = 0; i < 7; i++) { int j = (i + 1) % 7; tris.AddRange(new[] { i, j, 7 + j, i, 7 + j, 7 + i }); }
        mesh.triangles = tris.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }

    void OnDestroy() { live.Remove(this); if (material) Destroy(material); }
}
