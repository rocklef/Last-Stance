using System.Collections.Generic;
using UnityEngine;

// All mocap clips plus what the editor analysis learned about each one: when a strike lands,
// which limb delivers it and how far it reaches. Built by AnimAnalyzer, read by Fighter/AnimDriver.
public class AnimLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string name;
        public AnimationClip clip;
        public float length;
        public float impact;      // seconds into the clip where the striking limb is fully extended
        public Limb limb;
        public float reach;       // forward distance of the limb from the body at impact (metres)
        public float height;      // height of the limb at impact
        public float travel;      // forward root travel up to impact
        public Vector3 offset;    // unused (was paired-clip offset)
        public float contact;     // grab clips: when the hands close on the opponent
        public float recover;     // get-up clips: when the body is back upright
    }

    public List<Entry> entries = new List<Entry>();
    Dictionary<string, Entry> map;

    public Entry this[string name]
    {
        get
        {
            if (map == null)
            {
                map = new Dictionary<string, Entry>();
                foreach (var e in entries) map[e.name] = e;
            }
            return map.TryGetValue(name, out var v) ? v : null;
        }
    }
}
