using UnityEngine;

namespace Parkside.Jeju
{
    public sealed partial class JejuDriveController
    {
        public string driveTitle = "JEJU  /  한라산으로 가는 길";
        public string mapCaption = "예래·중문·서귀포 / 1100고지";
        public string stationHelp = "1–5 주행 구간   6–9 해안 명소   0 서귀포";
        public string mapCredit = "© OpenStreetMap contributors · ODbL   |   DEM: Mapzen / USGS";
        Vector3 cameraVelocity;
        GUIStyle label, title, speedStyle, small;
        Texture2D pixel;

        void LateUpdate() { UpdateCamera(false); }
        public void UpdateCamera(bool instant)
        {
            if (view == null) return;
            Vector3 offset = cameraMode == 0 ? new Vector3(0, 3.5f, -8.5f) : cameraMode == 1 ? new Vector3(-.32f, 1.48f, .7f) : new Vector3(0, 38, -22);
            Vector3 destination = transform.TransformPoint(offset);
            view.transform.position = instant ? destination : Vector3.SmoothDamp(view.transform.position, destination, ref cameraVelocity, cameraMode == 1 ? .025f : .14f);
            Vector3 look = transform.position + transform.forward * (cameraMode == 1 ? 60 : 14) + Vector3.up * (cameraMode == 1 ? 1.5f : 1);
            view.transform.rotation = Quaternion.LookRotation(look - view.transform.position);
            view.fieldOfView = cameraMode == 1 ? 68 : 58;
            if (visual != null) visual.gameObject.SetActive(cameraMode != 1);
        }

        void UpdateScenery()
        {
            if (sceneryChunks == null) return;
            foreach (Transform chunk in sceneryChunks)
            {
                if (chunk == null) continue;
                Vector3 delta = chunk.position - transform.position; delta.y = 0;
                var range=chunk.GetComponent<JejuSceneryChunk>();float distance=range!=null?range.visibilityDistance:1100;
                bool active = delta.sqrMagnitude < distance*distance;
                if (chunk.gameObject.activeSelf != active) chunk.gameObject.SetActive(active);
            }
        }

        void InitStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { font = route.font, fontSize = 18, normal = { textColor = new Color(.88f, .92f, .93f) } };
            title = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold };
            speedStyle = new GUIStyle(title) { fontSize = 44 };
            small = new GUIStyle(label) { fontSize = 14 };
            pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply();
        }
        void Rect(Rect rect, Color colour) { Color old = GUI.color; GUI.color = colour; GUI.DrawTexture(rect, pixel); GUI.color = old; }

        void OnGUI()
        {
            if (!showHud || route == null) return;
            InitStyles();
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float w = Screen.width / scale, h = Screen.height / scale;
            Rect(new Rect(28, 28, 410, 130), new Color(.025f, .065f, .07f, .9f));
            Rect(new Rect(28, 28, 4, 130), new Color(.45f, .84f, .69f));
            GUI.Label(new Rect(50, 42, 365, 38), driveTitle, title);
            string station = route.stations[0].name;
            foreach (var s in route.stations) if (!s.branchSpawn&&CurrentSegment >= s.index) station = s.name;
            if(!guidedDrive&&!string.IsNullOrEmpty(CurrentRoadName))station=CurrentRoadName;
            GUI.Label(new Rect(50, 83, 365, 28), station + "    ·    " + (Progress / 1000).ToString("0.0") + " / " + (route.Length / 1000).ToString("0.0") + " km", label);
            Rect(new Rect(28, h - 154, 278, 94), new Color(.025f, .065f, .07f, .9f));
            GUI.Label(new Rect(48, h - 151, 133, 62), SpeedKph.ToString("00"), speedStyle);
            GUI.Label(new Rect(146, h - 122, 132, 30), "km/h   " + (speed < -.1f ? "R" : "D"), label);
            GUI.Label(new Rect(48, h - 96, 246, 25), guidedDrive ? "풍경 주행  ·  G로 직접 운전" : paused ? "일시정지  ·  Esc로 계속" : "도로 주행  ·  C로 시점 변경", small);
            Rect(new Rect(w - 272, 28, 244, 286), new Color(.025f, .065f, .07f, .92f));
            GUI.DrawTexture(new Rect(w - 262, 38, 224, 224), route.map);
            float mx = Mathf.InverseLerp(route.mapMin.x, route.mapMax.x, transform.position.x);
            float my = Mathf.InverseLerp(route.mapMin.y, route.mapMax.y, transform.position.z);
            Rect(new Rect(w - 262 + mx * 224 - 4, 38 + (1 - my) * 224 - 4, 8, 8), new Color(1, .77f, .32f));
            GUI.Label(new Rect(w - 252, 272, 225, 28), mapCaption, small);
            if(ambient!=null)GUI.Label(new Rect(50,119,360,26),"주변 차량  "+ambient.ActiveCount+"대",small);
            if(traffic!=null)
            {
                float stop=traffic.StopDistance(transform.position,transform.forward,out JejuSignalAspect aspect);
                if(stop<85)
                {
                    Rect(new Rect(w*.5f-145,30,290,45),new Color(.025f,.065f,.07f,.92f));
                    GUI.Label(new Rect(w*.5f-130,39,265,30),(aspect==JejuSignalAspect.Green?"진행 신호":aspect==JejuSignalAspect.Amber?"황색 신호":"정지 신호")+"   "+Mathf.Max(0,stop).ToString("0")+"m",label);
                }
                if(traffic.clock-traffic.LastViolationTime<4)GUI.Label(new Rect(w*.5f-150,83,420,30),"적색 신호 · 정지선을 확인하세요",label);
            }
            GUI.Label(new Rect(30, h - 43, w - 60, 28), "W/S 가속·후진   A/D 조향   Space 제동   R 복귀   C 시점   G 풍경 주행   " + stationHelp + "   Tab 안내", small);
            GUI.Label(new Rect(w - 660, h - 69, 635, 24), mapCredit, small);
            GUI.matrix = old;
        }
        void OnDestroy() { if (pixel != null) Destroy(pixel); }
    }
}
