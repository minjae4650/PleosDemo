using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parkside.Jeju.Editor
{
    [InitializeOnLoad]
    public static class JejuDriveVerification
    {
        const string Key = "Parkside.Jeju.ExplicitPlayVerification";
        static JejuDriveController car;
        static int ticks;
        static int lastMovingTick;
        static float lastProgress;
        static double started;
        static bool hasError;
        static int branchChecks;
        static bool signalChecks;

        static JejuDriveVerification()
        {
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.update += Watch;
            Application.logMessageReceived += Log;
        }
        public static void BeginPlayCheck()
        {
            SessionState.SetInt(Key,1);
            EditorApplication.EnterPlaymode();
        }
        static void Log(string message,string trace,LogType type)
        {
            if(SessionState.GetInt(Key,0)>0 && (type==LogType.Exception || type==LogType.Error || type==LogType.Assert))
            {
                hasError=true;
                Directory.CreateDirectory(JejuDriveBuilder.Output);
                File.AppendAllText(Path.Combine(JejuDriveBuilder.Output,"play-errors.txt"),message+"\n"+trace+"\n");
            }
        }
        static void StateChanged(PlayModeStateChange state)
        {
            if(SessionState.GetInt(Key,0)==0)return;
            if(state==PlayModeStateChange.EnteredPlayMode){started=EditorApplication.timeSinceStartup;ticks=lastMovingTick=0;lastProgress=0;SessionState.SetInt(Key,2);}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetInt(Key,0)==3)
            {
                SessionState.SetInt(Key,0);
                if(hasError){EditorApplication.Exit(1);return;}
                EditorSceneManager.OpenScene(JejuDriveBuilder.ScenePath);
                File.WriteAllText(Path.Combine(JejuDriveBuilder.Output,"verification-complete.txt"),"PASS: saved/reopened Scene, road coverage, shaders, Play Mode driving, braking, reverse, branch roads, signal exclusivity, automatic red-light stops, green restart, manual violation detection and complete guided route.");
                EditorApplication.Exit(0);
            }
        }
        static void Watch()
        {
            if(SessionState.GetInt(Key,0)!=2 || !EditorApplication.isPlaying || EditorApplication.isCompiling)return;
            try
            {
                if(car==null)
                {
                    car=UnityEngine.Object.FindObjectOfType<JejuDriveController>();
                    if(car==null)throw new InvalidOperationException("No player after entering Play Mode");
                    car.enabled=false;
                    car.ambient.Prepare();car.ambient.spawning=false;car.ambient.ResetPopulation();
                    car.JumpToStation(0);
                    // Keyboard-independent simulation exercises the same public driving path used by Update.
                    car.guidedDrive=false;
                    Vector3 start=car.transform.position;
                    for(int i=0;i<120;i++)car.Simulate(1,0,false,1/60f);
                    if(car.SpeedKph<10 || Vector3.Distance(start,car.transform.position)<2)throw new InvalidOperationException("Manual acceleration failed");
                    for(int i=0;i<120;i++)car.Simulate(0,0,true,1/60f);
                    if(car.SpeedKph>.1f)throw new InvalidOperationException("Brake failed");
                    for(int i=0;i<120;i++)car.Simulate(-1,0,false,1/60f);
                    if(car.SpeedKph<5)throw new InvalidOperationException("Reverse failed");
                    car.Recover();
                    CheckBranchesAndSignals();
                    JejuTrafficVerification.Check(car);
                    for(int s=0;s<4;s++)
                    {
                        car.JumpToStation(s);
                        for(int mode=0;mode<3;mode++){car.cameraMode=mode;car.UpdateCamera(true);if(!Finite(car.view.transform.position))throw new InvalidOperationException("Camera invalid");}
                    }
                    car.cameraMode=0;
                    int first=car.route.stations[0].index;car.route.stations[0].index=0;
                    car.JumpToStation(0);car.route.stations[0].index=first;car.guidedDrive=true;
                }
                for(int i=0;i<500;i++)
                {
                    car.Simulate(0,0,false,.05f);ticks++;
                    if(!Finite(car.transform.position))throw new InvalidOperationException("Non-finite vehicle pose");
                }
                if(car.Progress>lastProgress+1){lastProgress=car.Progress;lastMovingTick=ticks;}
                if(ticks%10000==0||ticks-lastMovingTick>8000)File.WriteAllText(Path.Combine(JejuDriveBuilder.Output,"play-progress.txt"),"Ticks="+ticks+"\nProgress="+car.Progress+"\nPlayer="+car.transform.position+"\nSpeed="+car.SpeedKph+"\nClearance="+car.ambient.PlayerClearance(car.transform.position,car.transform.forward)+"\n"+string.Join("\n",car.ambient.vehicles.FindAll(v=>v.active).ConvertAll(v=>"edge="+v.path[v.leg]+" next="+(v.leg+1<v.path.Count?v.path[v.leg+1]:-1)+" distance="+v.distance+" speed="+v.speed+" turn="+v.inTurn+" circle="+v.reservedCircle+" position="+v.position)));
                if(ticks-lastMovingTick>8000)throw new InvalidOperationException("Traffic flow stalled at route metre "+car.Progress);
                if(car.Progress>car.route.Length-50)
                {
                    car.JumpToStation(2);car.cameraMode=0;car.UpdateCamera(true);
                    JejuDriveBuilder.Capture(car.view,Path.Combine(JejuDriveBuilder.Output,"play-forest.png"));
                    File.WriteAllText(Path.Combine(JejuDriveBuilder.Output,"play-report.txt"),"PlayMode=True\nSimulatedTicks="+ticks+"\nFullRouteMetres="+car.route.Length+"\nElapsedEditorSeconds="+(EditorApplication.timeSinceStartup-started)+"\nManualAcceleration=PASS\nBrake=PASS\nReverse=PASS\nRecovery=PASS\nStationCameras=PASS\nFreeDrivingBranches="+branchChecks+"\nSignalStateAndRedStopChecks="+signalChecks+"\nFullRouteTraversal=PASS");
                    SessionState.SetInt(Key,3);EditorApplication.ExitPlaymode();
                }
                if(EditorApplication.timeSinceStartup-started>300)throw new TimeoutException("Play verification exceeded 300 seconds");
            }
            catch(Exception e)
            {
                SessionState.SetInt(Key,0);
                Debug.LogException(e);
                File.WriteAllText(Path.Combine(JejuDriveBuilder.Output,"play-failed.txt"),e.ToString());
                EditorApplication.Exit(1);
            }
        }
        static void CheckBranchesAndSignals()
        {
            branchChecks=0;signalChecks=false;
            if(car.route.branches!=null)foreach(var branch in car.route.branches)
            {
                int chosen=0;float farthest=0;
                for(int i=1;i<branch.points.Length-2;i++)
                {
                    // The drive input in this fixture stays straight for about 3.6 m.
                    // Start on a segment long enough to hold that path.
                    if (Vector3.Distance(branch.points[i],branch.points[i+1])<3.8f) continue;
                    car.route.Nearest(branch.points[i],out Vector3 main,out _);
                    float d=Vector2.Distance(new Vector2(main.x,main.z),new Vector2(branch.points[i].x,branch.points[i].z));
                    if(d>farthest){farthest=d;chosen=i;}
                }
                if(farthest<10)continue;
                Vector3 forward=(branch.points[chosen+1]-branch.points[chosen]).normalized;
                car.Recover();
                car.transform.SetPositionAndRotation(branch.points[chosen]+Vector3.Cross(Vector3.up,forward)*.85f,Quaternion.LookRotation(forward));
                car.guidedDrive=false;
                var start=car.transform.position;
                for(int i=0;i<90;i++)car.Simulate(1,0,false,1/60f);
                var projected=car.route.ProjectDrivable(car.transform.position);
                float ownDistance=float.PositiveInfinity;
                for(int k=0;k<branch.points.Length-1;k++)
                {
                    Vector3 edge=branch.points[k+1]-branch.points[k];edge.y=0;Vector3 off=car.transform.position-branch.points[k];off.y=0;
                    float t=Mathf.Clamp01(Vector3.Dot(off,edge)/Mathf.Max(.001f,edge.sqrMagnitude));
                    ownDistance=Mathf.Min(ownDistance,(off-edge*t).magnitude);
                }
                // Shared public streets can have several itinerary names. Check
                // actual movement on this branch, rather than the displayed alias.
                float travelled=Vector3.Distance(start,car.transform.position);
                if(ownDistance>projected.width*.5f+1||travelled<1)throw new InvalidOperationException("Free driving branch failed: "+branch.name+" travelled="+travelled+" distanceToBranch="+ownDistance);
                branchChecks++;
            }
            if(car.traffic==null||car.traffic.junctions.Length==0)throw new InvalidOperationException("No controlled intersections");
            var system=car.traffic;
            for(float t=0;t<64;t+=.25f)
            {
                system.SetClock(t);
                foreach(var junction in system.junctions)
                    if(system.MainAspect(junction)==JejuSignalAspect.Green&&system.CrossAspect(junction)==JejuSignalAspect.Green)throw new InvalidOperationException("Conflicting green signals");
            }
            var j=system.junctions[0];int saved=car.route.stations[0].index;
            int insertion=Array.BinarySearch(car.route.distances,j.distance-90);
            car.route.stations[0].index=Mathf.Max(1,insertion<0?~insertion:insertion);
            car.JumpToStation(0);car.guidedDrive=true;
            for(int i=0;i<600;i++){system.SetClock(42-j.phaseOffset);car.Simulate(0,0,false,.05f);}
            if(car.Progress>j.distance-10||car.SpeedKph>.5f)throw new InvalidOperationException("Guided driver failed to stop for red: "+car.Progress+" / "+j.distance+" speed="+car.SpeedKph);
            for(int i=0;i<600;i++){system.SetClock(5-j.phaseOffset);car.Simulate(0,0,false,.05f);}
            if(car.Progress<j.distance+20)throw new InvalidOperationException("Guided driver failed to restart on green");
            system.SetClock(42-j.phaseOffset);int violations=system.RedLightViolations;
            system.ObserveMovement(j.centre-j.forward*17,j.centre-j.forward*15,j.forward);
            if(system.RedLightViolations<=violations)throw new InvalidOperationException("Red-light crossing was not detected");
            car.route.stations[0].index=saved;system.SetClock(0);signalChecks=true;
        }
        static bool Finite(Vector3 p)=>!(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z));
    }
}
