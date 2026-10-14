using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds the whole arena scene from the imported assets:
//   Unity -batchmode -quit -projectPath UnityProject -executeMethod SceneBuilder.BuildBatch
public static class SceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Arena.unity";
    const string Gen = "Assets/Generated";
    const string Ext = "Assets/External";

    [MenuItem("Last Stance/Build Arena Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Gen + "/Materials");
        Directory.CreateDirectory(Gen + "/Textures");
        Directory.CreateDirectory("Assets/Scenes");
        AssetDatabase.Refresh();

        MixamoImport.ExtractTextures();
        AnimAnalyzer.Build();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ConfigurePipeline();

        // ---------------------------------------------------------------- sky, light, fog
        var sky = new Material(Shader.Find("Skybox/Panoramic"));
        var hdr = LoadTex($"{Ext}/hdri/belfast_sunset_puresky_2k.hdr");
        sky.SetTexture("_MainTex", hdr);
        sky.SetFloat("_Mapping", 1);
        sky.SetFloat("_ImageType", 0);
        sky.SetFloat("_Exposure", 1.05f);
        sky.SetFloat("_Rotation", 160);
        sky.SetColor("_Tint", new Color(0.55f, 0.5f, 0.5f));
        SaveAsset(sky, $"{Gen}/Materials/Sky.mat");
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.78f, 0.55f, 0.45f);
        RenderSettings.fogDensity = 0.0045f;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.7f, 0.48f);
        sun.intensity = 1.75f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.transform.rotation = Quaternion.Euler(24, 62, 0);
        RenderSettings.sun = sun;
        var fill = new GameObject("SkyFill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.45f, 0.55f, 0.95f);
        fill.intensity = 0.35f;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(35, -125, 0);

        var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
        probe.transform.position = new Vector3(0, 3, 0);
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.size = new Vector3(80, 40, 80);
        probe.resolution = 128;

        // ---------------------------------------------------------------- floor + courtyard
        var floorMat = Lit("Floor", new Color(0.92f, 0.9f, 0.88f), 0.22f, 0, $"{Ext}/textures/monastery_stone_floor/monastery_stone_floor", new Vector2(64, 64));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(19, 1, 19);   // the temple town reaches ~80 m out
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        floor.isStatic = true;

        var courtyard = Spawn("Assets/Courtyard/Courtyard.glb", "Courtyard", Vector3.zero, 0);
        var remap = new Dictionary<string, Material>
        {
            { "Stone", Lit("Stone", new Color(0.9f, 0.88f, 0.85f), 0.15f, 0, $"{Ext}/textures/japanese_stone_wall/japanese_stone_wall", new Vector2(0.45f, 0.45f)) },
            { "Plaster", Lit("Plaster", new Color(0.96f, 0.93f, 0.87f), 0.06f, 0, $"{Ext}/textures/painted_plaster_wall/painted_plaster_wall", new Vector2(0.4f, 0.4f)) },
            { "Wood", Lit("Wood", new Color(0.92f, 0.86f, 0.8f), 0.3f, 0, $"{Ext}/textures/old_wood_floor/old_wood_floor", new Vector2(0.45f, 0.45f)) },
            { "DarkWood", Lit("DarkWood", new Color(0.3f, 0.25f, 0.25f), 0.35f, 0, $"{Ext}/textures/lacquered_cherry_wood/lacquered_cherry_wood", new Vector2(0.5f, 0.5f)) },
            { "Vermilion", Lit("Vermilion", new Color(0.78f, 0.14f, 0.06f), 0.4f, 0) },
            { "RoofTile", Lit("RoofTile", new Color(0.75f, 0.78f, 0.85f), 0.35f, 0, $"{Ext}/textures/grey_roof_tiles/grey_roof_tiles", new Vector2(0.45f, 0.45f)) },
            { "Bark", Lit("Bark", new Color(0.62f, 0.5f, 0.46f), 0.08f, 0, $"{Ext}/textures/japanese_cedar_bark/japanese_cedar_bark", new Vector2(0.9f, 0.9f)) },
            { "Blossom", Lit("Blossom", new Color(0.86f, 0.52f, 0.63f), 0.05f, 0, emission: new Color(0.1f, 0.03f, 0.05f)) },
            { "BlossomCard", BlossomCardMaterial() },
            { "LanternGlow", Lit("LanternGlow", new Color(1f, 0.75f, 0.45f), 0.3f, 0, emission: new Color(4f, 2.2f, 0.8f)) },
            { "Gold", Lit("Gold", new Color(0.95f, 0.7f, 0.3f), 0.72f, 1f) },
            { "Hills", Lit("Hills", new Color(0.32f, 0.26f, 0.36f), 0.05f, 0) },
            { "Moss", Lit("Moss", new Color(0.16f, 0.32f, 0.13f), 0.08f, 0) },
            { "Bamboo", Lit("Bamboo", new Color(0.28f, 0.48f, 0.16f), 0.28f, 0) },
            { "Cloth", Lit("Cloth", new Color(0.72f, 0.11f, 0.07f), 0.48f, 0) },
            { "Water", Lit("Water", new Color(0.03f, 0.2f, 0.25f), 0.85f, 0.32f, emission: new Color(0.01f, 0.04f, 0.06f)) },
            { "Tatami", Lit("Tatami", new Color(0.7f, 0.66f, 0.43f), 0.05f, 0) },
            { "Paper", Lit("Paper", new Color(0.97f, 0.94f, 0.86f), 0.05f, 0, emission: new Color(0.12f, 0.1f, 0.07f)) },
            { "Gravel", Lit("Gravel", new Color(0.95f, 0.93f, 0.9f), 0.05f, 0, $"{Ext}/textures/gravel_floor/gravel_floor", new Vector2(0.6f, 0.6f)) },
        };
        foreach (var r in courtyard.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterials = r.sharedMaterials.Select(m => m != null && remap.TryGetValue(Clean(m.name), out var nm) ? nm : m).ToArray();
            r.gameObject.isStatic = true;
        }
        foreach (var r in courtyard.GetComponentsInChildren<Renderer>().Where(r => r.name.StartsWith("Hills")))
            r.shadowCastingMode = ShadowCastingMode.Off;

        BuildTownMap();

        // ---------------------------------------------------------------- props (Poly Haven, CC0)
        var fireAnchors = new List<Transform>();
        foreach (var (x, z) in new[] { (-10.5f, -3f), (10.5f, -3f), (-10.5f, 6f), (10.5f, 6f) })
        {
            var pit = Spawn($"{Ext}/models/stone_fire_pit/stone_fire_pit.gltf", "FirePit", new Vector3(x, 0, z), Random.Range(0, 360f), 1.6f);
            var b = BoundsOf(pit);
            var a = new GameObject("FireAnchor").transform;
            a.SetParent(pit.transform);
            a.position = new Vector3(b.center.x, b.max.y - 0.12f, b.center.z);
            fireAnchors.Add(a);
        }
        Spawn($"{Ext}/models/wooden_lantern_01/wooden_lantern_01.gltf", "Lantern", new Vector3(-3.9f, 1.2f, 13.9f), 0, 1.4f);
        Spawn($"{Ext}/models/wooden_lantern_01/wooden_lantern_01.gltf", "Lantern", new Vector3(3.9f, 1.2f, 13.9f), 0, 1.4f);
        Spawn($"{Ext}/models/ceramic_vase_01/ceramic_vase_01.gltf", "Vase", new Vector3(-8.6f, 1.2f, 14.1f), 30, 2.2f);
        Spawn($"{Ext}/models/ceramic_vase_01/ceramic_vase_01.gltf", "Vase", new Vector3(8.6f, 1.2f, 14.1f), 70, 2.2f);
        var stool = Spawn($"{Ext}/models/chinese_stool/chinese_stool.gltf", "Stool", new Vector3(12.6f, 0, 9.3f), 20, 1f);
        var katana = Spawn($"{Ext}/models/antique_katana_01/antique_katana_01.gltf", "Katana", Vector3.zero, 0, 1f);
        var sb = BoundsOf(stool);
        katana.transform.position = new Vector3(sb.center.x, sb.max.y + 0.03f, sb.center.z);
        katana.transform.rotation = Quaternion.Euler(0, 110, 90);
        var lanternBoxes = new List<Transform>();
        foreach (var (x, z) in new[] { (-11f, 11f), (11f, 11f), (-11f, -11f), (11f, -11f), (-5.5f, 12.8f), (5.5f, 12.8f), (-6.5f, -15f), (6.5f, -15f) })
        {
            var t = new GameObject("LanternLight").transform;
            t.position = new Vector3(x, 1.85f * (Mathf.Abs(z) > 12 ? 1.1f : 1f), z);
            lanternBoxes.Add(t);
        }

        // ---------------------------------------------------------------- fighters (Mixamo mocap)
        var kaito = MakeMixamoFighter("Leonard", new Vector3(-1.5f, 0, 0), 90);
        var kf = kaito.GetComponent<Fighter>();
        kf.displayName = "Kaito"; kf.accent = new Color(0.25f, 0.55f, 1f);
        kf.idleClip = "BouncingIdle"; kf.introClip = "Bow"; kf.victoryClip = "VictoryBoxing";
        kf.maxHealth = 100; kf.walkSpeed = 2.4f; kf.attackSpeed = 1.08f; kf.damageMul = 1f; kf.weight = 1f; kf.bodyRadius = 0.22f;
        var brakka = MakeMixamoFighter("Brute", new Vector3(1.5f, 0, 0), -90);
        var bf = brakka.GetComponent<Fighter>();
        bf.displayName = "Brakka"; bf.accent = new Color(1f, 0.3f, 0.2f); bf.isBrute = true;
        bf.idleClip = "FightIdle"; bf.introClip = "Flexing"; bf.victoryClip = "VictoryCelebrate";
        bf.maxHealth = 125; bf.walkSpeed = 2.0f; bf.attackSpeed = 0.9f; bf.damageMul = 1.3f; bf.weight = 1.5f; bf.bodyRadius = 0.27f;

        // ---------------------------------------------------------------- camera + post
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 38;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 400;
        cam.allowHDR = true;
        camGo.AddComponent<AudioListener>();
        var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camGo.transform.position = new Vector3(0, 1.8f, -6);
        var fightCam = camGo.AddComponent<FightCamera>();
        camGo.AddComponent<Atmosphere>();

        var profile = MakeVolumeProfile();
        var vol = new GameObject("PostProcess").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;

        // ---------------------------------------------------------------- game systems
        var game = new GameObject("Game");
        var fx = game.AddComponent<FX>();
        var dot = GenTexture("Dot", Dot);
        fx.bloodMat = ParticleMat("Blood", dot, false);
        fx.sparkMat = ParticleMat("Spark", dot, true);
        fx.ringMat = ParticleMat("Ring", GenTexture("Ring", RingTex), true);
        fx.dustMat = ParticleMat("Dust", dot, false);
        fx.decalMat = DecalMat("Stain", GenTexture("Splat", Splat));
        fx.hitLight = Clip("HitLight"); fx.hitHeavy = Clip("HitHeavy");
        fx.whooshLight = Clip("WhooshLight"); fx.whooshHeavy = Clip("WhooshHeavy");
        fx.bodyFall = Clip("BodyFall"); fx.gong = Clip("Gong"); fx.koBoom = Clip("KOBoom"); fx.music = Clip("BattleTheme");
        fx.block = Clip("Block"); fx.parry = Clip("Parry");
        fx.volume = profile;

        var hud = game.AddComponent<HUD>();
        hud.display = AssetDatabase.LoadAssetAtPath<Font>($"{Ext}/fonts/Bangers-Regular.ttf");
        hud.body = AssetDatabase.LoadAssetAtPath<Font>($"{Ext}/fonts/Anton-Regular.ttf");

        var amb = game.AddComponent<Ambient>();
        amb.fireMat = ParticleMat("Fire", dot, true);
        amb.petalMat = ParticleMat("Petal", GenTexture("Petal", Petal), false);
        amb.fires = fireAnchors.ToArray();
        amb.lanterns = lanternBoxes.ToArray();

        var gm = game.AddComponent<GameManager>();
        gm.p1 = kf; gm.p2 = bf; gm.cam = fightCam; gm.hud = hud; gm.fx = fx;
        gm.rocPrefab = BuildRocPrefab();
        MonkBuilder.Build(gm);
        fightCam.a = kf; fightCam.b = bf;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"SceneBuilder: built {ScenePath} (fonts {(hud.display ? "ok" : "MISSING")}/{(hud.body ? "ok" : "MISSING")}, music {(fx.music ? "ok" : "MISSING")}, fires {fireAnchors.Count})");
    }

    public static void BuildBatch()
    {
        try { Build(); }
        catch (System.Exception e) { Debug.LogError("SceneBuilder: FAILED " + e); EditorApplication.Exit(1); }
        EditorApplication.Exit(0);
    }

    // ------------------------------------------------------------------ town solids
    [System.Serializable] class FootItem { public float x, z, sx, sz, yaw, h, top = -1; public int roof = -1; }
    [System.Serializable] class CanopyItem { public float x, z, size, y0, y1; }
    [System.Serializable] class RoofItem { public float x, z, hw, hd, eave, rise, ridge; public int alongX; }
    [System.Serializable] class RoomItem { public string name; public float x, z, hw, hd, ceiling; }
    [System.Serializable] class CeilItem { public float x, z, sx, sz, y0, y1; }
    [System.Serializable] class LightItem { public float x, y, z, range, intensity; }
    [System.Serializable] class PropItem { public string kind; public float x, z, yaw, size; }
    [System.Serializable]
    class FootFile
    {
        public FootItem[] items; public CanopyItem[] canopies; public RoofItem[] roofs; public RoomItem[] rooms;
        public CeilItem[] ceilings; public LightItem[] lights; public PropItem[] props;
    }

    // Footprints written by blender/build_courtyard.py become the TownMap (fighter collision) plus
    // invisible box colliders on the Solid layer (camera occlusion and swarm line of sight).
    static void BuildTownMap()
    {
        string path = "Assets/Courtyard/Footprints.json";
        if (!File.Exists(path)) { Debug.LogWarning("SceneBuilder: no town footprints"); return; }
        var file = JsonUtility.FromJson<FootFile>(File.ReadAllText(path));
        var go = new GameObject("TownMap");
        var map = go.AddComponent<TownMap>();
        var list = new List<TownMap.Box>();
        foreach (var f in file.items)
        {
            list.Add(new TownMap.Box { c = new Vector2(f.x, f.z), half = new Vector2(f.sx, f.sz) * 0.5f, yaw = f.yaw, h = f.h, roof = f.roof, top = f.top });
            if (f.h < 1.5f) continue;   // ponds and steps block walking but not sight
            var c = new GameObject("Solid");
            c.transform.SetParent(go.transform, false);
            c.layer = TownMap.SolidLayer;
            c.transform.SetPositionAndRotation(new Vector3(f.x, f.h * 0.5f, f.z), Quaternion.Euler(0, f.yaw, 0));
            var bc = c.AddComponent<BoxCollider>();
            bc.size = new Vector3(f.sx, f.h, f.sz);
            c.isStatic = true;
        }
        // tree canopies: you can walk under them, but they block the camera and the hunters' sight
        if (file.canopies != null)
            foreach (var t in file.canopies)
            {
                var c = new GameObject("Canopy");
                c.transform.SetParent(go.transform, false);
                c.layer = TownMap.SolidLayer;
                c.transform.position = new Vector3(t.x, (t.y0 + t.y1) * 0.5f, t.z);
                var bc = c.AddComponent<BoxCollider>();
                bc.size = new Vector3(t.size, t.y1 - t.y0, t.size);
                c.isStatic = true;
            }
        // interior ceilings: the camera and the hunters' sight stop at them
        if (file.ceilings != null)
            foreach (var t in file.ceilings)
            {
                var c = new GameObject("Ceiling");
                c.transform.SetParent(go.transform, false);
                c.layer = TownMap.SolidLayer;
                c.transform.position = new Vector3(t.x, (t.y0 + t.y1) * 0.5f, t.z);
                var bc = c.AddComponent<BoxCollider>();
                bc.size = new Vector3(t.sx, t.y1 - t.y0, t.sz);
                c.isStatic = true;
            }
        // warm paper lamps inside the dojo, the brewery and the tea house
        if (file.lights != null)
            foreach (var l in file.lights)
            {
                var lg = new GameObject("InteriorLamp").AddComponent<Light>();
                lg.transform.SetParent(go.transform, false);
                lg.transform.position = new Vector3(l.x, l.y - 0.2f, l.z);
                lg.type = LightType.Point;
                lg.color = new Color(1f, 0.68f, 0.38f);
                lg.range = l.range;
                lg.intensity = l.intensity;
                lg.shadows = LightShadows.None;
            }
        if (file.props != null)
        {
            var parent = new GameObject("Props").transform;
            foreach (var pr in file.props)
            {
                var at = new Vector3(pr.x, 0, pr.z);
                switch (pr.kind)
                {
                    case "barrels":
                        SpawnFit($"{Ext}/models/wooden_barrels_01/wooden_barrels_01.gltf", "Barrels", at, pr.yaw, pr.size * 0.95f, parent);
                        break;
                    case "boulder":
                        SpawnFit($"{Ext}/models/boulder_01/boulder_01.gltf", "Boulder", at + Vector3.down * 0.15f, pr.yaw, pr.size, parent);
                        break;
                    default:
                        // two crates side by side and one on top
                        var r = Quaternion.Euler(0, pr.yaw, 0);
                        SpawnFit($"{Ext}/models/wooden_crate_02/wooden_crate_02.gltf", "Crate", at + r * new Vector3(-0.48f, 0, 0), pr.yaw + 4, 0.92f, parent);
                        SpawnFit($"{Ext}/models/wooden_crate_02/wooden_crate_02.gltf", "Crate", at + r * new Vector3(0.48f, 0, 0.05f), pr.yaw - 7, 0.92f, parent);
                        SpawnFit($"{Ext}/models/wooden_crate_02/wooden_crate_02.gltf", "Crate", at + r * new Vector3(0.05f, 0.9f, 0), pr.yaw + 22, 0.9f, parent);
                        break;
                }
            }
            // buckets by the market well
            SpawnFit($"{Ext}/models/wooden_bucket_01/wooden_bucket_01.gltf", "Bucket", new Vector3(39.5f, 0, 1.6f), 30, 0.45f, parent);
            SpawnFit($"{Ext}/models/wooden_bucket_01/wooden_bucket_01.gltf", "Bucket", new Vector3(42.4f, 0, -0.7f), 200, 0.45f, parent);
            Debug.Log($"SceneBuilder: placed {file.props.Length} prop groups");
        }
        if (file.roofs != null)
            map.roofs = file.roofs.Select(r => new TownMap.Roof { c = new Vector2(r.x, r.z), half = new Vector2(r.hw, r.hd), eave = r.eave, rise = r.rise, ridge = r.ridge, alongX = r.alongX != 0 }).ToArray();
        if (file.rooms != null)
            map.rooms = file.rooms.Select(r => new TownMap.Room { name = r.name, c = new Vector2(r.x, r.z), half = new Vector2(r.hw, r.hd), ceiling = r.ceiling }).ToArray();
        // Poly Haven fire pits placed below
        foreach (var (x, z) in new[] { (-10.5f, -3f), (10.5f, -3f), (-10.5f, 6f), (10.5f, 6f) })
            list.Add(new TownMap.Box { c = new Vector2(x, z), half = new Vector2(1.1f, 1.1f), yaw = 0, h = 0.7f, roof = -1, top = -1 });
        map.boxes = list.ToArray();
        Debug.Log($"SceneBuilder: town map with {map.boxes.Length} solids, {map.roofs.Length} roofs, {map.rooms.Length} interiors");
    }

    // ------------------------------------------------------------------ fighters
    public static GameObject MakeMixamoFighter(string character, Vector3 pos, float yaw, bool withGame = true)
    {
        var go = Spawn(MixamoImport.CharDir + character + ".fbx", character, pos, yaw);
        MixamoMaterials.Apply(go, character);
        var animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = null;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var lib = AssetDatabase.LoadAssetAtPath<AnimLibrary>(AnimAnalyzer.LibraryPath);
        if (withGame)
        {
            var driver = go.AddComponent<AnimDriver>();
            driver.library = lib;
            go.AddComponent<Fighter>();
        }
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
        return go;
    }

    static GameObject BuildRocPrefab()
    {
        const string basePath = "Assets/Boss/AshenRoc.fbx";
        const string prefabPath = "Assets/Generated/AshenRoc.prefab";
        const string controllerPath = "Assets/Generated/AshenRoc.controller";
        AssetDatabase.Refresh();
        string[] clipFiles = { "AshenRoc_FlyIn.fbx", "AshenRoc_Perch.fbx", "AshenRoc_MouthAttack.fbx" };
        var importedClips = new Dictionary<string, AnimationClip>();
        foreach (var file in clipFiles)
        {
            string path = "Assets/Boss/" + file;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new System.Exception("Missing Blender Roc FBX: " + path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.optimizeGameObjects = false;
            var takes = importer.defaultClipAnimations;
            if (takes.Length > 0)
            {
                string takeName = file.Contains("FlyIn") ? "FlyIn" : file.Contains("Perch") ? "Perch" : "MouthAttack";
                takes[0].name = takeName;
                takes[0].loopTime = takeName != "MouthAttack";
                importer.clipAnimations = takes;
            }
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) throw new System.Exception("Blender Roc clip was not imported: " + path);
            importedClips[clip.name] = clip;
            Debug.Log("SceneBuilder: imported Roc clip " + clip.name + " from " + path);
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)) AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var machine = controller.layers[0].stateMachine;
        var perchState = machine.AddState("Perch");
        perchState.motion = importedClips["Perch"];
        machine.defaultState = perchState;
        foreach (var pair in importedClips.Where(pair => pair.Key != "Perch"))
        {
            var state = machine.AddState(pair.Key);
            state.motion = pair.Value;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
        if (model == null) throw new System.Exception("Missing Blender Roc model: " + basePath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "AshenRocModel";
        var animator = instance.GetComponentInChildren<Animator>();
        if (animator == null) animator = instance.AddComponent<Animator>();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(basePath).OfType<Avatar>().FirstOrDefault();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    // ------------------------------------------------------------------ helpers
    static GameObject Spawn(string path, string name, Vector3 pos, float yaw, float scale = 1f)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) throw new System.Exception("missing asset " + path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    // a model scaled so its footprint is `size` across, standing on the ground at pos
    static GameObject SpawnFit(string path, string name, Vector3 pos, float yaw, float size, Transform parent)
    {
        var go = Spawn(path, name, pos, yaw, 1f);
        var b = BoundsOf(go);
        float across = Mathf.Max(b.size.x, b.size.z);
        if (across > 1e-3f) go.transform.localScale = Vector3.one * (size / across);
        b = BoundsOf(go);
        go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
        go.transform.SetParent(parent, true);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.gameObject.isStatic = true;
            r.gameObject.layer = PerfGovernor.DetailLayer;   // culled beyond 60 m (PerfGovernor)
        }
        return go;
    }

    // blossom cards: clusters of five-petal flowers on a transparent card, alpha-cut and two-sided
    static Material BlossomCardMaterial()
    {
        var rng = new System.Random(3);
        var flowers = new List<Vector4>();
        for (int i = 0; i < 30; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2, r = Mathf.Sqrt((float)rng.NextDouble()) * 0.74f;
            flowers.Add(new Vector4(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0.12f + (float)rng.NextDouble() * 0.09f, (float)rng.NextDouble() * 6.28f));
        }
        var tex = GenTexture("BlossomCard", (u, v) =>
        {
            foreach (var f in flowers)
            {
                float dx = u - f.x, dy = v - f.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / f.z;
                if (d > 1.05f) continue;
                float ang = Mathf.Atan2(dy, dx) + f.w;
                float petal = 0.6f + 0.4f * Mathf.Abs(Mathf.Cos(ang * 2.5f));
                if (d > petal) continue;
                if (d < 0.2f) return new Color(1f, 0.86f, 0.55f, 1);
                float shade = Mathf.Lerp(1f, 0.8f, d / petal);
                return new Color(shade, shade * 0.9f, shade * 0.94f, 1);
            }
            return new Color(1, 0.9f, 0.94f, 0);
        }, 256);
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
        ti.mipMapsPreserveCoverage = true;
        ti.alphaTestReferenceValue = 0.4f;
        ti.SaveAndReimport();
        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex));
        var m = Lit("BlossomCard", new Color(1f, 0.74f, 0.84f), 0.05f, 0, emission: new Color(0.12f, 0.04f, 0.06f));
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_AlphaClip", 1);
        m.SetFloat("_Cutoff", 0.4f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", 0);
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = (int)RenderQueue.AlphaTest;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static string Clean(string n)
    {
        int i = n.IndexOfAny(new[] { ' ', '.', '(' });
        return i > 0 ? n.Substring(0, i) : n;
    }

    // The graphics pass: crisp 4K sun shadows out to the edge of the town, strong high-quality
    // ambient occlusion, HDR colour grading, and the Atmosphere full-screen pass (height fog glowing
    // toward the sunset sun + god rays).
    static void ConfigurePipeline()
    {
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (urp != null)
        {
            var so = new SerializedObject(urp);
            so.FindProperty("m_SupportsHDR").boolValue = true;
            so.FindProperty("m_RequireDepthTexture").boolValue = true;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            so.FindProperty("m_ShadowDistance").floatValue = 95;
            so.FindProperty("m_ShadowCascadeCount").intValue = 4;
            so.FindProperty("m_Cascade4Split").vector3Value = new Vector3(0.05f, 0.15f, 0.38f);
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowQuality").intValue = 3;
            so.FindProperty("m_ColorGradingMode").intValue = 1;   // HDR grading
            so.FindProperty("m_ColorGradingLutSize").intValue = 64;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
        }
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        if (data != null)
        {
            data.rendererFeatures.RemoveAll(f => f == null);
            foreach (var f in data.rendererFeatures)
            {
                if (f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var fs = new SerializedObject(f);
                fs.FindProperty("m_Settings.Intensity").floatValue = 0.9f;
                fs.FindProperty("m_Settings.Radius").floatValue = 0.5f;
                fs.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.3f;
                fs.FindProperty("m_Settings.Falloff").floatValue = 60f;
                fs.FindProperty("m_Settings.Samples").enumValueIndex = 0;       // high
                fs.FindProperty("m_Settings.BlurQuality").enumValueIndex = 0;   // high
                fs.ApplyModifiedPropertiesWithoutUndo();
            }
            AddAtmosphereFeature(data);
        }
        AssetDatabase.SaveAssets();
    }

    static void AddAtmosphereFeature(UniversalRendererData data)
    {
        string path = AssetDatabase.GetAssetPath(data);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is FullScreenPassRendererFeature old && old.name == "Atmosphere") { data.rendererFeatures.Remove(old); Object.DestroyImmediate(old, true); }
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Atmosphere.shader");
        var mat = new Material(shader) { name = "Atmosphere" };
        mat = SaveAsset(mat, $"{Gen}/Materials/Atmosphere.mat");
        var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
        feature.name = "Atmosphere";
        feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
        feature.requirements = ScriptableRenderPassInput.Depth;
        feature.fetchColorBuffer = true;
        feature.passMaterial = mat;
        AssetDatabase.AddObjectToAsset(feature, data);
        data.rendererFeatures.Add(feature);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        // keep the renderer's feature map in step with the list
        var so = new SerializedObject(data);
        var map = so.FindProperty("m_RendererFeatureMap");
        map.ClearArray();
        foreach (var f in data.rendererFeatures)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string _, out long id);
            map.InsertArrayElementAtIndex(map.arraySize);
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        data.SetDirty();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        Debug.Log($"SceneBuilder: renderer features = {string.Join(", ", data.rendererFeatures.ConvertAll(f => f.name))}");
    }

    static Texture2D LoadTex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{name}.mp3");

    public static Material Lit(string name, Color color, float smooth, float metal, string texBase = null, Vector2? tiling = null, Color? emission = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (texBase != null)
        {
            var dif = FindTex(texBase + "_Diffuse");
            var nrm = FindTex(texBase + "_nor_gl");
            if (dif) m.SetTexture("_BaseMap", dif);
            if (nrm)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(nrm));
                if (ti.textureType != TextureImporterType.NormalMap)
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    ti.SaveAndReimport();
                    nrm = FindTex(texBase + "_nor_gl");
                }
                m.SetTexture("_BumpMap", nrm);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            if (tiling.HasValue) m.SetTextureScale("_BaseMap", tiling.Value);
        }
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return SaveAsset(m, $"{Gen}/Materials/{name}.mat");
    }

    static Texture2D FindTex(string basePath)
    {
        foreach (var ext in new[] { ".jpg", ".png" })
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath + ext);
            if (t) return t;
        }
        return null;
    }

    // Creates the asset, or overwrites an existing one in place so scenes referencing it keep working.
    static T SaveAsset<T>(T obj, string path) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }
        EditorUtility.CopySerialized(obj, existing);
        if (obj is Material src && existing is Material dst) dst.shaderKeywords = src.shaderKeywords;
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static Material ParticleMat(string name, Texture2D tex, bool additive)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        Transparent(m, additive);
        return SaveAsset(m, $"{Gen}/Materials/FX_{name}.mat");
    }

    static Material DecalMat(string name, Texture2D tex)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", new Color(0.35f, 0.01f, 0.01f, 0.85f));
        Transparent(m, false);
        return SaveAsset(m, $"{Gen}/Materials/FX_{name}.mat");
    }

    static void Transparent(Material m, bool additive)
    {
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", additive ? 2 : 0);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    static VolumeProfile MakeVolumeProfile()
    {
        var path = $"{Gen}/ArenaPost.asset";
        AssetDatabase.DeleteAsset(path);
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, path);
        var bloom = p.Add<Bloom>(true);
        bloom.threshold.value = 1.0f; bloom.intensity.value = 0.85f; bloom.scatter.value = 0.72f;
        bloom.highQualityFiltering.value = true;
        bloom.dirtTexture.value = GenLensDirt();
        bloom.dirtIntensity.value = 0.9f;
        var tone = p.Add<Tonemapping>(true);
        tone.mode.value = TonemappingMode.ACES;
        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.value = -0.12f; ca.contrast.value = 24; ca.saturation.value = 18;
        // cool shadows, warm sunset highlights
        var split = p.Add<SplitToning>(true);
        split.shadows.value = new Color(0.34f, 0.44f, 0.62f);
        split.highlights.value = new Color(1f, 0.78f, 0.58f);
        split.balance.value = -12f;
        var smh = p.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.value = new Vector4(0.94f, 0.97f, 1.06f, -0.02f);
        smh.highlights.value = new Vector4(1.05f, 1.0f, 0.94f, 0.02f);
        var grain = p.Add<FilmGrain>(true);
        grain.type.value = FilmGrainLookup.Thin1; grain.intensity.value = 0.09f; grain.response.value = 0.8f;
        var blur = p.Add<MotionBlur>(true);
        blur.mode.value = MotionBlurMode.CameraOnly; blur.quality.value = MotionBlurQuality.Medium; blur.intensity.value = 0.22f; blur.clamp.value = 0.05f;
        var flare = p.Add<ScreenSpaceLensFlare>(true);
        flare.intensity.value = 0.35f;
        flare.firstFlareIntensity.value = 0.3f; flare.secondaryFlareIntensity.value = 0.25f; flare.warpedFlareIntensity.value = 0.15f;
        flare.streaksIntensity.value = 0.55f; flare.streaksLength.value = 0.6f; flare.streaksThreshold.value = 0.3f;
        // depth of field for the power close-ups (Atmosphere.Focus switches it on)
        var dof = p.Add<DepthOfField>(true);
        dof.mode.value = DepthOfFieldMode.Off;
        dof.focusDistance.value = 5f; dof.aperture.value = 16f; dof.focalLength.value = 55f; dof.bladeCount.value = 6;
        ca.colorFilter.overrideState = false;
        ca.hueShift.overrideState = false;
        var wb = p.Add<WhiteBalance>(true);
        wb.temperature.value = 10; wb.tint.value = 2;
        var vig = p.Add<Vignette>(true);
        vig.intensity.value = 0.3f; vig.smoothness.value = 0.45f; vig.color.value = new Color(0.12f, 0.02f, 0.02f);
        vig.center.overrideState = false; vig.rounded.overrideState = false;
        var chroma = p.Add<ChromaticAberration>(true);
        chroma.intensity.value = 0;
        foreach (var c in p.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, p); }
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }

    // soft smudges and specks on the "lens", lit up by bloom (bright sky, explosions, the spirit)
    static Texture2D GenLensDirt()
    {
        var rng = new System.Random(5);
        var blobs = new List<Vector4>();
        for (int i = 0; i < 70; i++)
            blobs.Add(new Vector4((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, 0.02f + (float)rng.NextDouble() * (i < 12 ? 0.3f : 0.07f), 0.15f + (float)rng.NextDouble() * 0.6f));
        var path = $"{Gen}/Textures/LensDirt.png";
        const int size = 512;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2 - 1, v = (y + 0.5f) / size * 2 - 1, a = 0;
                foreach (var b in blobs)
                {
                    float d = Mathf.Sqrt((u - b.x) * (u - b.x) + (v - b.y) * (v - b.y)) / b.z;
                    if (d < 1) a += (1 - d * d) * b.w * 0.5f;
                }
                a = Mathf.Clamp01(a) * (0.35f + 0.65f * Mathf.Clamp01(Mathf.Sqrt(u * u + v * v)));
                t.SetPixel(x, y, new Color(a, a * 0.96f, a * 0.9f, 1));
            }
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ generated FX textures
    delegate Color Painter(float u, float v);

    static Texture2D GenTexture(string name, Painter paint, int size = 128)
    {
        var path = $"{Gen}/Textures/{name}.png";
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                t.SetPixel(x, y, paint((x + 0.5f) / size * 2 - 1, (y + 0.5f) / size * 2 - 1));
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Color Dot(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Mathf.Clamp01(1 - r);
        return new Color(1, 1, 1, a * a);
    }

    static Color RingTex(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Mathf.Clamp01(1 - Mathf.Abs(r - 0.78f) / 0.14f);
        return new Color(1, 1, 1, a * a);
    }

    static Color Petal(float u, float v)
    {
        float d = (u * u) / 0.45f + (v * v) / 0.9f;
        float notch = v > 0.6f && Mathf.Abs(u) < 0.15f ? 0 : 1;
        float a = Mathf.Clamp01((1 - d) * 4) * notch;
        return new Color(1, 1 - 0.15f * (1 - d), 1 - 0.1f * (1 - d), a);
    }

    static Color Splat(float u, float v)
    {
        float ang = Mathf.Atan2(v, u);
        float r = Mathf.Sqrt(u * u + v * v);
        float edge = 0.55f + 0.12f * Mathf.Sin(ang * 5 + 1.3f) + 0.08f * Mathf.Sin(ang * 11 + 0.4f) + 0.05f * Mathf.Sin(ang * 17);
        float a = Mathf.Clamp01((edge - r) * 12);
        // satellite droplets
        for (int i = 0; i < 7; i++)
        {
            float da = i * 0.9f + 0.3f, dr = 0.72f + 0.05f * (i % 3);
            float dx = u - Mathf.Cos(da) * dr, dy = v - Mathf.Sin(da) * dr;
            a = Mathf.Max(a, Mathf.Clamp01((0.07f + 0.02f * (i % 2) - Mathf.Sqrt(dx * dx + dy * dy)) * 30));
        }
        float shade = 0.75f + 0.25f * Mathf.Clamp01(r / edge);
        return new Color(shade, shade, shade, a);
    }
}
