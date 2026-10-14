using System.Collections.Generic;
using UnityEngine;

// Runtime-only story set: a breakable ceremonial wall and a pulsing boss aura.  It is kept out of
// the authored arena GLB so Manual Mode always starts with the pristine courtyard.
public class StoryArena : MonoBehaviour
{
    readonly List<Rigidbody> pieces = new List<Rigidbody>();
    Transform wall;
    Light aura;
    Fighter boss;
    bool broken;

    public void Begin(Fighter bossFighter)
    {
        boss = bossFighter;
        BuildWall();
        var go = new GameObject("VarkonAura");
        go.transform.SetParent(boss.transform, false);
        go.transform.localPosition = Vector3.up * 1.2f;
        aura = go.AddComponent<Light>();
        aura.type = LightType.Point;
        aura.color = new Color(0.95f, 0.13f, 0.04f);
        aura.range = 5.5f;
        aura.intensity = 0.9f;
        aura.shadows = LightShadows.None;
    }

    void BuildWall()
    {
        wall = new GameObject("StoryBreakWall").transform;
        wall.position = new Vector3(-6.5f, 0, 0);
        var stone = Resources.GetBuiltinResource<Material>("Default-Material.mat");
        for (int row = 0; row < 5; row++)
        for (int col = 0; col < 5; col++)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "BreakStone";
            block.transform.SetParent(wall);
            block.transform.localPosition = new Vector3(0, 0.45f + row * 0.62f, (col - 2) * 0.58f + (row % 2) * 0.14f);
            block.transform.localScale = new Vector3(0.42f, 0.56f, 0.52f);
            block.GetComponent<Renderer>().sharedMaterial = stone;
            block.GetComponent<Renderer>().material.color = Color.Lerp(new Color(0.20f, 0.16f, 0.13f), new Color(0.43f, 0.37f, 0.30f), Random.value);
            var body = block.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.mass = 1.6f;
            pieces.Add(body);
        }
    }

    public void Break(Vector3 forceDirection)
    {
        if (broken) return;
        broken = true;
        foreach (var piece in pieces)
        {
            piece.isKinematic = false;
            piece.AddForce((forceDirection.normalized + Random.insideUnitSphere * 0.55f + Vector3.up * 0.5f) * Random.Range(4.5f, 8.5f), ForceMode.Impulse);
            piece.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
            Destroy(piece.gameObject, 6f);
        }
        if (wall) Destroy(wall.gameObject, 6.2f);
    }

    void Update()
    {
        if (!aura || !boss) return;
        float danger = boss.health / Mathf.Max(1, boss.maxHealth) < 0.5f ? 1.8f : 1f;
        aura.intensity = danger * (0.75f + Mathf.Sin(Time.time * 8f) * 0.22f);
        aura.range = danger * (5.2f + Mathf.Sin(Time.time * 5f) * 0.45f);
    }
}
