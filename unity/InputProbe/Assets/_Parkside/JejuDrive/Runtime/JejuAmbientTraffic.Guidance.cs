using UnityEngine;

namespace Parkside.Jeju
{
    public sealed partial class JejuAmbientTraffic
    {
        const float GuideSampleStep = 1.5f;
        const float HalfCarLength = 2.4f;
        const float HalfCarWidth = 1f;

        float VehicleGap(Vehicle vehicle,Vector3 position,Vector3 heading)
        {
            if(!vehicle.inTurn||!Network.checkTurnFootprints)
                return ObstacleGap(vehicle.position,vehicle.heading,position,heading);
            if(Horizontal(position-vehicle.position)>25)return float.PositiveInfinity;

            // An angled lane may pass an oncoming queue. Measure clearance along
            // the turn the car will take, with its orientation at each sample.
            var obstacle=new TrafficFootprint(position,heading);
            float end=vehicle.turnDistances[vehicle.turnDistances.Length-1];
            float lookAhead=Mathf.Min(18,end-vehicle.distance);
            const float spacing=.45f;
            for(float ahead=Mathf.Min(spacing,lookAhead);ahead>0;)
            {
                var point=JejuTrafficNetwork.Sample(vehicle.turn,vehicle.turnDistances,vehicle.distance+ahead,out var direction);
                if(new TrafficFootprint(point,direction).Overlaps(obstacle))return Mathf.Max(0,ahead-spacing);
                if(ahead>=lookAhead)break;
                ahead=Mathf.Min(ahead+spacing,lookAhead);
            }
            return float.PositiveInfinity;
        }

        public float GuidedClearance(JejuDriveRoute route, float progress)
        {
            var start = GuidedPose(route, progress);
            float best = float.PositiveInfinity;
            for (int step=0;step<=30;step++)
            {
                float distance=step*GuideSampleStep;
                if (distance>=best) break;
                var sample=GuidedPose(route,progress+distance);
                foreach (var vehicle in vehicles)
                {
                    if (!vehicle.active || Horizontal(vehicle.position-sample.point)>6) continue;
                    var obstacle=new TrafficFootprint(vehicle.position,vehicle.heading);
                    // Let the player move away from a car alongside its rear quarter.
                    bool behind=Vector3.Dot(obstacle.point-start.point,start.forward)<-.1f;
                    if (behind && distance<7) continue;
                    if (!sample.Overlaps(obstacle)) continue;
                    float low=Mathf.Max(behind?7:0,distance-GuideSampleStep),high=distance;
                    for (int i=0;i<4;i++)
                    {
                        float middle=(low+high)*.5f;
                        if (GuidedPose(route,progress+middle).Overlaps(obstacle)) high=middle;
                        else low=middle;
                    }
                    best=Mathf.Min(best,low);
                }
            }
            return best;
        }

        static TrafficFootprint GuidedPose(JejuDriveRoute route, float distance)
        {
            Vector3 point=route.AtDistance(distance,out var forward,out float width);
            point+=Vector3.Cross(Vector3.up,forward).normalized*JejuDriveRoute.LaneOffset(width);
            return new TrafficFootprint(point,forward);
        }

        readonly struct TrafficFootprint
        {
            public readonly Vector3 point, forward;
            readonly Vector3 right;

            public TrafficFootprint(Vector3 point, Vector3 heading)
            {
                this.point=point;
                heading.y=0;
                forward=heading.normalized;
                right=Vector3.Cross(Vector3.up,forward);
            }

            public bool Overlaps(TrafficFootprint other)
            {
                Vector3 delta=other.point-point;
                if (delta.x*delta.x+delta.z*delta.z>36) return false;
                return !Separated(other,delta,forward) && !Separated(other,delta,right)
                    && !Separated(other,delta,other.forward) && !Separated(other,delta,other.right);
            }

            bool Separated(TrafficFootprint other, Vector3 delta, Vector3 axis)
            {
                float extent=HalfCarLength*(Mathf.Abs(Vector3.Dot(axis,forward))+Mathf.Abs(Vector3.Dot(axis,other.forward)))
                    +HalfCarWidth*(Mathf.Abs(Vector3.Dot(axis,right))+Mathf.Abs(Vector3.Dot(axis,other.right)));
                return Mathf.Abs(Vector3.Dot(delta,axis))>=extent;
            }
        }
    }
}
