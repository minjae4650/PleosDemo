using System;
using UnityEngine;

namespace Parkside.Jeju
{
    public enum JejuSignalAspect { Red, Amber, Green }
    [Serializable] public sealed class JejuJunction
    {
        public string name;
        public float distance;
        public Vector3 centre, forward;
        public float width, phaseOffset;
        public Renderer[] mainRed, mainAmber, mainGreen, crossRed, crossAmber, crossGreen;
    }
    [Serializable] public sealed class JejuSignalApproach
    {
        public int junction;
        public bool cross;
        public Vector3 line, forward;
        public float width, stopBeforeEdgeEnd;
    }

    /// <summary>Local demonstration timing at OSM signal locations; not live municipal signal timings.</summary>
    public sealed class JejuTrafficSystem : MonoBehaviour
    {
        public JejuJunction[] junctions = Array.Empty<JejuJunction>();
        public JejuSignalApproach[] approaches = Array.Empty<JejuSignalApproach>();
        public float clock;
        public int RedLightViolations { get; private set; }
        public float LastViolationTime { get; private set; } = -100;
        float refresh;
        MaterialPropertyBlock block;
        void Awake()=>RefreshLights();
        public JejuSignalAspect MainAspect(JejuJunction j)
        {
            float p = Mathf.Repeat(clock + j.phaseOffset, 64);
            return p < 30 ? JejuSignalAspect.Green : p < 34 ? JejuSignalAspect.Amber : JejuSignalAspect.Red;
        }
        public JejuSignalAspect CrossAspect(JejuJunction j)
        {
            float p = Mathf.Repeat(clock + j.phaseOffset, 64);
            return p >= 37 && p < 57 ? JejuSignalAspect.Green : p >= 57 && p < 61 ? JejuSignalAspect.Amber : JejuSignalAspect.Red;
        }
        public JejuSignalAspect ApproachAspect(JejuSignalApproach approach) =>
            approach.cross ? CrossAspect(junctions[approach.junction]) : MainAspect(junctions[approach.junction]);

        static float ApproachDistance(JejuSignalApproach approach,Vector3 position,Vector3 heading)
        {
            if(Vector3.Dot(heading,approach.forward)<.82f)return float.PositiveInfinity;
            var delta=approach.line-position;
            float lateral=Mathf.Abs(Vector3.Dot(delta,Vector3.Cross(Vector3.up,approach.forward)));
            if(lateral>approach.width*.5f+.35f)return float.PositiveInfinity;
            return Vector3.Dot(delta,approach.forward);
        }
        public void SetClock(float value) { clock = value; RefreshLights(); }
        public void Advance(float dt)
        {
            clock += dt; refresh -= dt;
            if (refresh <= 0) { RefreshLights(); refresh = .15f; }
        }
        public float StopDistance(Vector3 position, Vector3 heading, out JejuSignalAspect aspect)
        {
            float best = float.PositiveInfinity; aspect = JejuSignalAspect.Green;
            heading.y = 0; heading.Normalize();
            if(approaches.Length>0)
            {
                foreach(var approach in approaches)
                {
                    float distance=ApproachDistance(approach,position,heading);
                    if(distance<0||distance>100||distance>=best)continue;
                    best=distance;
                    aspect=ApproachAspect(approach);
                }
                return best;
            }
            foreach (var j in junctions)
            {
                Vector3 delta = j.centre - position; delta.y = 0;
                float along = Vector3.Dot(delta, heading), across = Mathf.Abs(Vector3.Dot(delta,Vector3.Cross(Vector3.up,heading)));
                if (along < 8 || along > 100 || across > j.width * .65f + 6) continue;
                var state = Mathf.Abs(Vector3.Dot(heading,j.forward)) > .65f ? MainAspect(j) : CrossAspect(j);
                if (along - 16 < best) { best = along - 16; aspect = state; }
            }
            return best;
        }
        public void ObserveMovement(Vector3 before, Vector3 after, Vector3 heading)
        {
            if (clock - LastViolationTime < 6) return;
            heading.y=0;heading.Normalize();
            if(approaches.Length>0)
            {
                foreach(var approach in approaches)
                {
                    if(ApproachDistance(approach,before+heading*2.4f,heading)<=0||
                       ApproachDistance(approach,after+heading*2.4f,heading)>0||
                       ApproachAspect(approach)!=JejuSignalAspect.Red)continue;
                    RedLightViolations++;LastViolationTime=clock;
                    break;
                }
                return;
            }
            foreach(var j in junctions)
            {
                Vector3 a=j.centre-before,b=j.centre-after;a.y=b.y=0;
                if(Mathf.Abs(Vector3.Dot(a,Vector3.Cross(Vector3.up,heading)))>j.width*.65f+3)continue;
                bool crossed=Vector3.Dot(a,heading)>16 && Vector3.Dot(b,heading)<=16;
                var state=Mathf.Abs(Vector3.Dot(heading,j.forward))>.65f?MainAspect(j):CrossAspect(j);
                if(crossed && state==JejuSignalAspect.Red){RedLightViolations++;LastViolationTime=clock;break;}
            }
        }
        public void RefreshLights()
        {
            if(block==null)block=new MaterialPropertyBlock();
            foreach(var j in junctions)
            {
                var main=MainAspect(j);var cross=CrossAspect(j);
                Lamps(j.mainRed,main==JejuSignalAspect.Red,new Color(1,.025f,.008f));
                Lamps(j.mainAmber,main==JejuSignalAspect.Amber,new Color(1,.48f,.02f));
                Lamps(j.mainGreen,main==JejuSignalAspect.Green,new Color(.03f,.85f,.28f));
                Lamps(j.crossRed,cross==JejuSignalAspect.Red,new Color(1,.025f,.008f));
                Lamps(j.crossAmber,cross==JejuSignalAspect.Amber,new Color(1,.48f,.02f));
                Lamps(j.crossGreen,cross==JejuSignalAspect.Green,new Color(.03f,.85f,.28f));
            }
        }
        void Lamps(Renderer[] lamps,bool on,Color colour)
        {
            if(lamps==null)return;
            block.Clear();block.SetColor("_Color",on?colour:colour*.12f);block.SetColor("_EmissionColor",on?colour*2.8f:Color.black);
            foreach(var renderer in lamps)if(renderer!=null)renderer.SetPropertyBlock(block);
        }
    }
}
