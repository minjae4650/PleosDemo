using System.Collections.Generic;
using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// Runs the fixed-time signal programs from SUMO and lights the lamps: every head is one texel of a small state
    /// texture (r red, g yellow, b left arrow, a green) read by the SignalLamp shader, so all lamps are one draw call.
    /// Ambient cars ask <see cref="HeadState"/> before their stop line; the player is never stopped.
    /// </summary>
    public sealed class TrafficSignals : MonoBehaviour
    {
        [SerializeField] private TrafficData data;
        [SerializeField] private Material lampMaterial;   // kept for reference only; the state texture is a global shader value

        public const int Red = 0, Yellow = 1, Green = 2;

        private Texture2D states;
        private Color32[] pixels;
        private int[] phase;
        private float[] cycle;
        private List<int>[] headsOfProgram;
        private static readonly int StatesId = Shader.PropertyToID("_SignalStates");
        private static readonly int CountId = Shader.PropertyToID("_SignalCount");

        public bool Ready => data != null && data.programs != null && data.heads != null && phase != null;

        private void Awake()
        {
            if (data == null || data.programs == null || data.heads == null || data.heads.Length == 0) { enabled = false; return; }
            int n = data.programs.Length;
            phase = new int[n];
            cycle = new float[n];
            headsOfProgram = new List<int>[n];
            for (int p = 0; p < n; p++)
            {
                headsOfProgram[p] = new List<int>();
                foreach (var d in data.programs[p].durations) cycle[p] += d;
                phase[p] = -1;
            }
            for (int h = 0; h < data.heads.Length; h++)
                if (data.heads[h].program >= 0 && data.heads[h].program < n) headsOfProgram[data.heads[h].program].Add(h);
            states = new Texture2D(data.heads.Length, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SignalStates" };
            pixels = new Color32[data.heads.Length];
            // global shader values, not material values: the lamp material is a project asset and must not pick up a runtime texture
            Shader.SetGlobalTexture(StatesId, states);
            Shader.SetGlobalFloat(CountId, data.heads.Length);
            Tick(true);
        }

        private void Update() => Tick(false);

        private void Tick(bool force)
        {
            float t = Time.time;
            bool changed = false;
            for (int p = 0; p < phase.Length; p++)
            {
                var prog = data.programs[p];
                if (cycle[p] <= 0f || prog.states == null || prog.states.Length == 0) continue;
                float local = Mathf.Repeat(t + prog.offset, cycle[p]);
                int k = 0;
                while (k < prog.durations.Length - 1 && local >= prog.durations[k]) { local -= prog.durations[k]; k++; }
                if (k == phase[p] && !force) continue;
                phase[p] = k;
                changed = true;
                foreach (int h in headsOfProgram[p])
                {
                    var head = data.heads[h];
                    int s = GroupState(head, 0), l = GroupState(head, 1);
                    pixels[h] = new Color32((byte)(s == Red ? 255 : 0), (byte)(s == Yellow ? 255 : 0), (byte)(head.left != null && head.left.Length > 0 && l == Green ? 255 : 0), (byte)(s == Green ? 255 : 0));
                }
            }
            if (changed || force)
            {
                states.SetPixels32(pixels);
                states.Apply(false, false);
            }
        }

        private int GroupState(SignalHead head, int group)
        {
            var links = group == 1 ? head.left : group == 2 ? head.right : head.straight;
            if (links == null || links.Length == 0) links = head.straight != null && head.straight.Length > 0 ? head.straight : (head.left != null && head.left.Length > 0 ? head.left : head.right);
            if (links == null || links.Length == 0) return Green;
            var prog = data.programs[head.program];
            if (prog.states == null || prog.states.Length == 0) return Green;
            int ph = Mathf.Clamp(phase[head.program], 0, prog.states.Length - 1);
            string st = prog.states[ph];
            int result = Red;
            foreach (int i in links)
            {
                if (i < 0 || i >= st.Length) continue;
                char c = st[i];
                if (c == 'G' || c == 'g' || c == 's' || c == 'O' || c == 'o') return Green;
                if (c == 'y' || c == 'Y') result = Yellow;   // 'u' (red-yellow before green) stays red
            }
            return result;
        }

        /// <summary>Red, Yellow or Green for the cars of one movement (0 straight, 1 left, 2 right) at a head.</summary>
        public int HeadState(int head, int group)
        {
            if (!Ready || head < 0 || head >= data.heads.Length) return Green;
            return GroupState(data.heads[head], group);
        }

        private void OnDestroy()
        {
            if (states != null) Destroy(states);
        }
    }
}
