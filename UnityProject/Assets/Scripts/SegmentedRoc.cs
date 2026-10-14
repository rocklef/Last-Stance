using UnityEngine;

// Drives the authored Blender bird through the post-Brakka reveal, then releases five
// independently targetable arrow-swarm attacks for the player to defeat.
public class SegmentedRoc : MonoBehaviour
{
    Fighter hero;
    GameObject bird;
    Animator animator;
    Transform beak;
    ParticleSystem mouthArrows, skyWake;
    Material arrowMaterial;
    float t, nextSpawn;
    int spawned;
    bool combatStarted, announcedRetreat;
    bool cameraSet, perchStarted, attackStarted;
    public bool Complete { get; private set; }

    public void Begin(Fighter target, GameObject rocPrefab)
    {
        hero = target;
        if (rocPrefab == null)
        {
            Debug.LogError("Ashen Roc prefab was not assigned by SceneBuilder.");
            return;
        }
        bird = Instantiate(rocPrefab, transform);
        bird.name = "AshenRoc_BlenderModel";
        bird.transform.localPosition = Vector3.zero;
        bird.transform.localRotation = Quaternion.identity;
        animator = bird.GetComponentInChildren<Animator>();
        if (animator == null) Debug.LogError("Blender Ashen Roc prefab has no Animator.");
        else animator.Play("FlyIn", 0, 0);
        beak = FindChild(bird.transform, "Roc | hooked upper beak");
        if (beak == null) beak = bird.transform;
        transform.position = new Vector3(-15, 14, 17);
        arrowMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        arrowMaterial.color = new Color(.14f, .18f, .2f);
        arrowMaterial.SetFloat("_Metallic", .55f);
        arrowMaterial.SetFloat("_Smoothness", .55f);
        var ember = new Material(arrowMaterial);
        ember.color = new Color(.95f, .19f, .035f);
        arrowMaterial.SetTexture("_EmissionMap", Texture2D.whiteTexture);
        arrowMaterial.SetColor("_EmissionColor", new Color(.8f, .075f, .012f));
        CreatePlumes(ember);
        GameManager.I.hud.Announce("THE ASHEN ROC", "A SHADOW CROSSES THE TEMPLE ROOF.", 3.2f, new Color(1f, .36f, .12f));
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var tr in root.GetComponentsInChildren<Transform>(true)) if (tr.name == name) return tr;
        return null;
    }

    void CreatePlumes(Material ember)
    {
        var arrow = MakeArrowMesh();
        mouthArrows = MakeArrowParticles("MouthArrowParticles", 380, .18f, 8f, arrow, ember, true);
        mouthArrows.transform.SetParent(beak, false);
        mouthArrows.transform.localPosition = new Vector3(0, -.7f, -.05f);
        mouthArrows.transform.localRotation = Quaternion.Euler(90, 0, 0);
        skyWake = MakeArrowParticles("SkyArrowWake", 180, .12f, 4.2f, arrow, arrowMaterial, false);
        skyWake.transform.SetParent(bird.transform, false);
        skyWake.transform.localPosition = new Vector3(0, 1.8f, .7f);
    }

    ParticleSystem MakeArrowParticles(string name, int count, float size, float speed, Mesh mesh, Material mat, bool burst)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.loop = !burst; main.playOnAwake = false; main.duration = 2.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.4f); main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .55f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * .45f, size); main.startColor = Color.white;
        main.maxParticles = count * 2; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.rateOverTime = burst ? 0 : 32;
        if (burst) emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = burst ? 11 : 25; shape.radius = .13f;
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = mesh; renderer.material = mat; renderer.alignment = ParticleSystemRenderSpace.Velocity; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var trails = ps.trails; trails.enabled = true; trails.lifetime = .22f; trails.ratio = .65f; trails.widthOverTrail = new ParticleSystem.MinMaxCurve(.5f, .02f);
        return ps;
    }

    static Mesh MakeArrowMesh()
    {
        var mesh = new Mesh { name = "AshenRocArrowShard" };
        mesh.vertices = new[] {
            new Vector3(-.035f, -.5f, 0), new Vector3(.035f, -.5f, 0), new Vector3(.035f, .20f, 0), new Vector3(.13f, .18f, 0),
            new Vector3(0, .5f, 0), new Vector3(-.13f, .18f, 0), new Vector3(-.035f, .20f, 0),
            new Vector3(-.035f, -.5f, .025f), new Vector3(.035f, -.5f, .025f), new Vector3(.035f, .20f, .025f), new Vector3(.13f, .18f, .025f),
            new Vector3(0, .5f, .025f), new Vector3(-.13f, .18f, .025f), new Vector3(-.035f, .20f, .025f)
        };
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 1; i < 6; i++) { tris.AddRange(new[] { 0, i, i + 1 }); tris.AddRange(new[] { 7, 7 + i + 1, 7 + i }); }
        for (int i = 0; i < 7; i++) { int j = (i + 1) % 7; tris.AddRange(new[] { i, j, 7 + j, i, 7 + j, 7 + i }); }
        mesh.triangles = tris.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }

    void Update()
    {
        if (!hero || !bird) return;
        t += Time.deltaTime;
        if (t < 3.5f) FlyToRoof(t / 3.5f);
        else if (t < 5.3f) PerchAndWatch();
        else if (t < 7.2f) SpitArrows();
        else RunCombat();
    }

    void FlyToRoof(float k)
    {
        Vector3 start = new Vector3(-15, 14, 17), roof = new Vector3(0, 8.5f, 13.8f);
        transform.position = Vector3.Lerp(start, roof, Mathf.SmoothStep(0, 1, k));
        transform.LookAt(hero.transform.position + Vector3.up * 1.4f);
        if (k > .12f && !cameraSet) { cameraSet = true; GameManager.I.cam.SetRoc(bird.transform); }
        if (k > .94f && animator && !perchStarted) { perchStarted = true; animator.CrossFade("Perch", .2f); }
    }

    void PerchAndWatch()
    {
        transform.position = new Vector3(0, 8.5f + Mathf.Sin(t * 3.7f) * .045f, 13.8f);
        transform.LookAt(hero.transform.position + Vector3.up * 1.45f);
        if (t > 4.05f && t < 4.15f) GameManager.I.hud.Announce("KAITO", "WHAT IS THAT ON THE ROOF...?", 1.65f, hero.accent);
        if (animator && !perchStarted) { perchStarted = true; animator.CrossFade("Perch", .2f); }
        if (skyWake && !skyWake.isPlaying) skyWake.Play();
    }

    void SpitArrows()
    {
        if (animator && !attackStarted) { attackStarted = true; animator.CrossFade("MouthAttack", .12f); }
        if (t > 5.72f && t < 5.82f)
        {
            mouthArrows.Play(true);
            GameManager.I.hud.Announce("THE ROC SPITS ASHEN ARROWS", "THE SWARM TAKES SHAPE", 1.55f, new Color(1f, .33f, .08f));
            FightCamera.Shake(.45f);
        }
        if (t > 6.45f && animator) animator.CrossFade("Perch", .16f);
    }

    void RunCombat()
    {
        if (!combatStarted)
        {
            combatStarted = true; nextSpawn = t + .5f;
            GameManager.I.cam.b = hero; GameManager.I.cam.SetMode(FightCamera.Mode.Fight);
            GameManager.I.hud.Announce("ASHEN SWARM", "MOVE FREELY. STRIKE THE ARROW FORMS.", 2.2f, new Color(1f, .38f, .1f));
        }
        if (spawned < 5 && t >= nextSpawn)
        {
            string[] forms = { "Serpent Lance", "Roc Wing Blades", "Corkscrew Pike", "Falling Arrow Fan", "Ash Spiral" };
            var root = new GameObject("RocForm_" + forms[spawned]);
            root.transform.position = beak.position + bird.transform.forward * 1.1f;
            root.AddComponent<RocConstruct>().Begin(hero, spawned, arrowMaterial);
            spawned++; nextSpawn = t + 2.35f;
        }
        if (spawned >= 5 && RocConstruct.LiveCount == 0)
        {
            Complete = true;
            if (!announcedRetreat)
            {
                announcedRetreat = true; GameManager.I.hud.Announce("THE ASHEN ROC RETREATS", "THE TEMPLE IS QUIET AGAIN.", 3.2f, new Color(1f, .7f, .25f));
                if (animator) animator.CrossFade("FlyIn", .15f);
            }
            transform.position += (Vector3.up + Vector3.forward) * Time.deltaTime * 4f;
        }
    }

    void OnDestroy() { if (arrowMaterial) Destroy(arrowMaterial); }
}
