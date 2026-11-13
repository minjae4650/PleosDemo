using System.Collections.Generic;
using UnityEngine;

namespace BusanEnvironment
{
    // The traffic signals of the landmark map in play: every signal keeps one phase (green, yellow, red for the cars; the walk
    // lights the other way round). Their lamps are their own materials (tools busan_kit/materials.py sig_* and walk_*); at
    // the start of play each is replaced by a copy (the material assets stay as built) and the copies are lit by phase.
    // LoopTraffic asks Phase() and stops its cars before the zebra crossings while it is not green.
    public sealed class SignalLights : MonoBehaviour
    {
        public const float Green = 20f, Yellow = 3f, Red = 8f;
        public const float Cycle = Green + Yellow + Red;

        public enum State { Green, Yellow, Red }

        public static State Phase(float t)
        {
            float c = Mathf.Repeat(t + 4f, Cycle);
            return c < Green ? State.Green : c < Green + Yellow ? State.Yellow : State.Red;
        }

        static readonly Dictionary<string, Color> Lit = new Dictionary<string, Color>
        {
            { "sig_red", new Color(1.0f, 0.10f, 0.06f) }, { "sig_yellow", new Color(1.0f, 0.62f, 0.05f) },
            { "sig_green", new Color(0.10f, 1.0f, 0.55f) }, { "walk_red", new Color(1.0f, 0.10f, 0.07f) },
            { "walk_green", new Color(0.15f, 1.0f, 0.50f) },
        };

        readonly Dictionary<string, Material> copies = new Dictionary<string, Material>();
        State shown = (State)(-1);

        static string KeyOf(Material m)
        {
            if (m == null) return null;
            foreach (var k in Lit.Keys)
                if (m.name.StartsWith(k)) return k;
            return null;
        }

        void Start()
        {
            foreach (var r in FindObjectsOfType<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var key = KeyOf(mats[i]);
                    if (key == null) continue;
                    if (!copies.TryGetValue(key, out var copy)) { copy = new Material(mats[i]) { name = key + " (play)" }; copies[key] = copy; }
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (copies.Count == 0) enabled = false;
        }

        void Set(string key, bool on)
        {
            if (!copies.TryGetValue(key, out var m)) return;
            var c = Lit[key];
            m.SetColor("_Color", on ? c : c * 0.22f);
            m.SetFloat("_Emit", on ? 1f : 0f);
        }

        void Update()
        {
            var s = Phase(Time.time);
            if (s == shown) return;
            shown = s;
            Set("sig_green", s == State.Green);
            Set("sig_yellow", s == State.Yellow);
            Set("sig_red", s == State.Red);
            Set("walk_green", s == State.Red);
            Set("walk_red", s != State.Red);
        }

        void OnDestroy()
        {
            foreach (var m in copies.Values) if (m != null) Destroy(m);
        }
    }
}
