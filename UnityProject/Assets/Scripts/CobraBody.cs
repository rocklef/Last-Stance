using UnityEngine;
using UnityEngine.Rendering;

// Draws THE KING COBRA: owns its CobraMesh and pushes it into a dynamic mesh every frame, with the
// painted materials from Resources/Cobra (scales, hood, head shields, mouth, fangs, tongue, eyes).
// The mesh is in world space, so the object itself never moves.
public class CobraBody : MonoBehaviour
{
    public readonly CobraMesh shape = new CobraMesh();
    public float glow = 1;       // how hot the ember seams between the scales burn
    Mesh mesh;
    MeshRenderer mr;
    MaterialPropertyBlock mpb;
    Color[] baseEmission;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly string[] MatNames = { "CobraBody", "CobraHood", "CobraHead", "CobraMouth", "CobraFang", "CobraTongue", "CobraEye" };

    public static CobraBody Create()
    {
        var go = new GameObject("KingCobra");
        var c = go.AddComponent<CobraBody>();
        c.Init();
        return c;
    }

    void Init()
    {
        mesh = new Mesh { name = "KingCobra", indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        mr = gameObject.AddComponent<MeshRenderer>();
        var mats = new Material[MatNames.Length];
        baseEmission = new Color[MatNames.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            mats[i] = Resources.Load<Material>("Cobra/" + MatNames[i]);
            if (mats[i] == null)
            {
                // the build lost the material: a plain stand-in rather than nothing
                mats[i] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mats[i].SetColor("_BaseColor", i == CobraMesh.Mouth ? new Color(0.5f, 0.08f, 0.1f) : i == CobraMesh.Eye ? new Color(1f, 0.5f, 0.1f) : new Color(0.1f, 0.1f, 0.11f));
                Debug.LogWarning("CobraBody: missing material Cobra/" + MatNames[i]);
            }
            baseEmission[i] = mats[i].HasProperty(EmissionId) ? mats[i].GetColor(EmissionId) : Color.black;
        }
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows = true;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        mpb = new MaterialPropertyBlock();
    }

    public bool Visible
    {
        get => mr != null && mr.enabled;
        set { if (mr != null) mr.enabled = value; }
    }

    // rebuild the mesh from the pose set on shape
    public void Apply()
    {
        shape.Build();
        mesh.Clear();
        mesh.SetVertices(shape.V);
        mesh.SetNormals(shape.N);
        mesh.SetTangents(shape.Tn);
        mesh.SetUVs(0, shape.UV);
        mesh.subMeshCount = CobraMesh.SubCount;
        for (int s = 0; s < CobraMesh.SubCount; s++) mesh.SetTriangles(shape.tris[s], s, false);
        mesh.RecalculateBounds();
        // the seams and the spectacle pulse with its fury
        for (int s = 0; s < 4; s++)
        {
            mpb.Clear();
            mpb.SetColor(EmissionId, baseEmission[s] * glow);
            mr.SetPropertyBlock(mpb, s);
        }
    }
}
