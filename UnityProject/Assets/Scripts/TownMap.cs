using UnityEngine;

// The temple town's solid footprints (buildings, walls, trees, lanterns, stalls...), baked from the
// Blender layout (Assets/Courtyard/Footprints.json) by SceneBuilder. Keeps fighters out of the
// buildings, and gives the camera and Moros's hunting swarms colliders (layer Solid) to test line of
// sight against, so Kaito can break away down the streets and hide behind houses.
// It also knows the town's climbable surfaces - sloped roofs, wall tops, stall canopies, crate
// stacks - so Kaito can wall-run onto a roof and run across the rooftops, and the enterable
// interiors (the Grand Dojo, the brewery, the tea house) so Moros can change tactics indoors.
public class TownMap : MonoBehaviour
{
    public static TownMap I;
    public const int SolidLayer = 9;
    public const int SolidMask = 1 << SolidLayer;

    [System.Serializable]
    public struct Box
    {
        public Vector2 c, half;   // centre and half size on the ground plane (x, z)
        public float yaw, h;      // Unity yaw in degrees, height
        public int roof;          // index into roofs (-1 = none)
        public float top;         // flat top you can stand on (-1 = none)
    }

    // hip roof: eaves at the overhang edge, rising to a ridge along the long axis
    [System.Serializable]
    public struct Roof
    {
        public Vector2 c, half;   // centre, half extents including the overhang
        public float eave, rise, ridge;
        public bool alongX;
    }

    [System.Serializable]
    public struct Room
    {
        public string name;
        public Vector2 c, half;
        public float ceiling;
    }

    public Box[] boxes = new Box[0];
    public Roof[] roofs = new Roof[0];
    public Room[] rooms = new Room[0];
    // the walkable town: courtyard, markets, storehouses, the south approach, the temple heights,
    // the brewery quarter and the tea-house quarter
    public Vector2 min = new Vector2(-79.6f, -45.2f), max = new Vector2(79.6f, 63.4f);

    void Awake() => I = this;

    static void ToLocal(in Box b, Vector3 p, out float lx, out float lz)
    {
        float dx = p.x - b.c.x, dz = p.z - b.c.y;
        float a = b.yaw * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
        lx = dx * ca - dz * sa;
        lz = dx * sa + dz * ca;
    }

    // height of a roof's surface above a ground point (-1 when the point is not under it)
    public float RoofHeight(int i, Vector3 p)
    {
        var r = roofs[i];
        float lx = Mathf.Abs(p.x - r.c.x), lz = Mathf.Abs(p.z - r.c.y);
        if (lx > r.half.x || lz > r.half.y) return -1;
        float longL = r.alongX ? lx : lz, shortL = r.alongX ? lz : lx;
        float longH = r.alongX ? r.half.x : r.half.y, shortH = r.alongX ? r.half.y : r.half.x;
        float fShort = 1 - shortL / shortH;
        float fLong = 1 - Mathf.Max(0, longL - r.ridge) / Mathf.Max(0.01f, longH - r.ridge);
        return r.eave + r.rise * Mathf.Clamp01(Mathf.Min(fShort, fLong));
    }

    // the top of a solid at a point, as something to stand on (-1 = cannot stand on it)
    float TopAt(int i, Vector3 p)
    {
        var b = boxes[i];
        if (b.roof >= 0) return RoofHeight(b.roof, p);
        return b.top;
    }

    // What Kaito stands on at p when his feet are at height feetY: the highest roof / wall top /
    // crate stack under him that is at most a small step above his feet, else the street.
    public float GroundAt(Vector3 p, float feetY)
    {
        float best = 0;
        float reach = feetY + 0.6f;
        if (feetY < 0.9f) return 0;   // on the street: nothing to climb onto without jumping
        for (int i = 0; i < roofs.Length; i++)
        {
            float h = RoofHeight(i, p);
            if (h > best && h <= reach) best = h;
        }
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i];
            if (b.top < 0 || b.roof >= 0) continue;
            ToLocal(b, p, out float lx, out float lz);
            if (Mathf.Abs(lx) > b.half.x || Mathf.Abs(lz) > b.half.y) continue;
            if (b.top > best && b.top <= reach) best = b.top;
        }
        return best;
    }

    // A surface Kaito can wall-run / vault onto: looking from p along dir, the first climbable
    // roof or wall top within reach whose edge is at least a storey above his feet.
    public bool ClimbTarget(Vector3 p, Vector3 dir, float feetY, out Vector3 land, out float edgeY)
    {
        land = p; edgeY = 0;
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return false;
        dir.Normalize();
        for (float k = 0.4f; k <= 2.6f; k += 0.2f)
        {
            Vector3 q = p + dir * k;
            float best = -1;
            for (int i = 0; i < roofs.Length; i++)
            {
                float h = RoofHeight(i, q);
                if (h > best) best = h;
            }
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                if (b.top < 0 || b.roof >= 0) continue;
                ToLocal(b, q, out float lx, out float lz);
                if (Mathf.Abs(lx) <= b.half.x && Mathf.Abs(lz) <= b.half.y && b.top > best) best = b.top;
            }
            // too low to need a wall run (the roof he is standing on, a crate): look further ahead
            if (best < 0 || best - feetY < 1.2f) continue;
            if (best - feetY > 10.5f) return false;
            // land a little way onto the surface
            Vector3 l = q + dir * 0.8f;
            float lh = -1;
            for (int i = 0; i < roofs.Length; i++) lh = Mathf.Max(lh, RoofHeight(i, l));
            if (lh < best - 0.2f) { l = q; lh = best; }
            if (InsideSolidAbove(l, lh)) return false;   // something (a pagoda tier, an upper storey) sits on it
            land = new Vector3(l.x, lh, l.z);
            edgeY = best;
            return true;
        }
        return false;
    }

    bool InsideSolidAbove(Vector3 p, float feetY)
    {
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i];
            ToLocal(b, p, out float lx, out float lz);
            if (Mathf.Abs(lx) >= b.half.x || Mathf.Abs(lz) >= b.half.y) continue;
            float top = TopAt(i, p);
            if (top < 0 && b.h > feetY + 0.5f) return true;
        }
        return false;
    }

    // push a circle of radius r (on the ground plane) out of every solid it overlaps; solids whose
    // top Kaito is standing on (or flying over) are ignored
    public Vector3 Resolve(Vector3 p, float r, float feetY = 0)
    {
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i];
            float dx = p.x - b.c.x, dz = p.z - b.c.y;
            float reach = b.half.x + b.half.y + r;
            if (dx * dx + dz * dz > reach * reach) continue;
            if (feetY > 0.25f)
            {
                if (feetY >= b.h - 0.05f) continue;
                float top = TopAt(i, p);
                if (top >= 0 && feetY >= top - 0.6f) continue;
            }
            float a = b.yaw * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            float lx = dx * ca - dz * sa, lz = dx * sa + dz * ca;   // into box space
            float cx = Mathf.Clamp(lx, -b.half.x, b.half.x), cz = Mathf.Clamp(lz, -b.half.y, b.half.y);
            float ox = lx - cx, oz = lz - cz;
            float d2 = ox * ox + oz * oz;
            if (d2 >= r * r) continue;
            if (d2 > 1e-8f)
            {
                float d = Mathf.Sqrt(d2);
                lx = cx + ox / d * r;
                lz = cz + oz / d * r;
            }
            else
            {
                // centre inside the box: leave through the nearest face
                float px = b.half.x - Mathf.Abs(lx), pz = b.half.y - Mathf.Abs(lz);
                if (px < pz) lx = Mathf.Sign(lx == 0 ? 1 : lx) * (b.half.x + r);
                else lz = Mathf.Sign(lz == 0 ? 1 : lz) * (b.half.y + r);
            }
            p.x = b.c.x + lx * ca + lz * sa;
            p.z = b.c.y - lx * sa + lz * ca;
        }
        p.x = Mathf.Clamp(p.x, min.x, max.x);
        p.z = Mathf.Clamp(p.z, min.y, max.y);
        return p;
    }

    public bool InsideSolid(Vector3 p, float r = 0)
    {
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i];
            ToLocal(b, p, out float lx, out float lz);
            if (Mathf.Abs(lx) < b.half.x + r && Mathf.Abs(lz) < b.half.y + r && p.y < b.h) return true;
        }
        return false;
    }

    // tallest solid under a ground point (so Moros floats over roofs instead of through them)
    public float HeightAt(Vector3 p, float r = 0)
    {
        float top = 0;
        for (int i = 0; i < boxes.Length; i++)
        {
            var b = boxes[i];
            ToLocal(b, p, out float lx, out float lz);
            if (Mathf.Abs(lx) < b.half.x + r && Mathf.Abs(lz) < b.half.y + r) top = Mathf.Max(top, b.h);
        }
        for (int i = 0; i < rooms.Length; i++)
        {
            var m = rooms[i];
            if (Mathf.Abs(p.x - m.c.x) < m.half.x + r && Mathf.Abs(p.z - m.c.y) < m.half.y + r) top = Mathf.Max(top, m.ceiling + 3f);
        }
        return top;
    }

    // the interior a point is inside (-1 = outdoors)
    public int RoomAt(Vector3 p)
    {
        for (int i = 0; i < rooms.Length; i++)
        {
            var m = rooms[i];
            if (Mathf.Abs(p.x - m.c.x) < m.half.x && Mathf.Abs(p.z - m.c.y) < m.half.y && p.y < m.ceiling - 0.5f) return i;
        }
        return -1;
    }

    // the doorway of a room nearest to a point (every room has an opening in the middle of each side)
    public Vector3 NearestDoor(int room, Vector3 from)
    {
        var m = rooms[room];
        Vector3 c = new Vector3(m.c.x, 0, m.c.y);
        Vector3[] doors =
        {
            c + new Vector3(0, 0, -m.half.y), c + new Vector3(0, 0, m.half.y),
            c + new Vector3(-m.half.x, 0, 0), c + new Vector3(m.half.x, 0, 0),
        };
        Vector3 best = doors[0];
        float bd = float.MaxValue;
        foreach (var d in doors)
        {
            float dd = (new Vector3(from.x - d.x, 0, from.z - d.z)).sqrMagnitude;
            if (dd < bd) { bd = dd; best = d; }
        }
        return best;
    }

    // is the straight line between two points blocked by a building / wall / tree?
    public static bool Blocked(Vector3 a, Vector3 b) => Physics.Linecast(a, b, SolidMask, QueryTriggerInteraction.Ignore);

    public Vector3 RandomStreetPoint()
    {
        for (int k = 0; k < 40; k++)
        {
            var p = new Vector3(Random.Range(min.x, max.x), 0, Random.Range(min.y, max.y));
            if (Walkable(p) && !InsideSolid(p + Vector3.up * 0.5f, 1.2f) && RoomAt(p) < 0) return p;
        }
        return Vector3.zero;
    }

    // inside one of the districts (the space between them is outside the town walls)
    public static bool Walkable(Vector3 p)
    {
        bool court = p.x > -15.5f && p.x < 15.5f && p.z > -16.5f && p.z < 15.4f;
        bool band = p.x > -79.5f && p.x < 79.5f && p.z > -15.5f && p.z < 15.5f;              // markets, storehouses, brewery, tea house
        bool south = p.x > -13.5f && p.x < 13.5f && p.z <= -16.5f && p.z > -45.5f;
        bool north = p.x > -44.5f && p.x < 44.5f && p.z >= 15.4f && p.z < 63.5f;             // temple heights
        return court || band || south || north;
    }

    // which part of town a point is in, for Moros's tactics
    public enum Zone { Courtyard, Streets, Rooftops, Indoors }
    public Zone ZoneAt(Vector3 p)
    {
        if (RoomAt(p) >= 0) return Zone.Indoors;
        if (p.y > 1.2f) return Zone.Rooftops;
        bool court = p.x > -16f && p.x < 16f && p.z > -17f && p.z < 16f;
        return court ? Zone.Courtyard : Zone.Streets;
    }
}
