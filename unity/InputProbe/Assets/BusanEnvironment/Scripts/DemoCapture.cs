using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// Frames for the demo video (plan.md 10). Does nothing unless the player is started with
    ///   -democapture &lt;dir&gt; -route &lt;i&gt; -view driver|chase -from &lt;s&gt; -seconds &lt;n&gt; -fps 30
    /// Then it fixes the game clock to the frame rate (Time.captureFramerate, so the video is smooth whatever the real frame rate),
    /// makes route i the preferred one, puts the car on it at distance s, starts auto-drive and saves every frame as it is shown
    /// (HUD included) as &lt;dir&gt;/00000.jpg, 00001.jpg ... (JPG quality 92). log.txt lists the frame at which each jump point
    /// is passed. Quits when done.
    /// </summary>
    public sealed class DemoCapture : MonoBehaviour
    {
        [SerializeField] private DriveAssist assist;
        [SerializeField] private Parkside.Cameras.VehicleCamera vehicleCamera;
        [SerializeField, Range(1, 100)] private int jpgQuality = 92;

        public sealed class Settings
        {
            public string dir;
            public int route;
            public bool chase;
            public float from;
            public float seconds = 10f;
            public int fps = 30;
        }

#if UNITY_EDITOR
        // The editor bridge's way in for a play-mode test (the editor's own command line cannot be chosen): the same arguments,
        // separated by line breaks. Not used by a player.
        public const string EditorArgsKey = "BusanEnvironment.DemoCapture.Args";
        private bool fromEditor;
#endif
        private Settings settings;
        private bool clockSet;

        /// <summary>The settings from a command line; false (and no error) without -democapture.</summary>
        public static bool TryParse(string[] args, out Settings settings, out string error)
        {
            settings = null; error = null;
            int at = Array.IndexOf(args, "-democapture");
            if (at < 0) return false;
            if (at + 1 >= args.Length || args[at + 1].StartsWith("-")) { error = "-democapture needs a folder"; return false; }
            var s = new Settings { dir = args[at + 1] };
            string Value(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var inv = CultureInfo.InvariantCulture;
            try
            {
                var v = Value("-route"); if (v != null) s.route = int.Parse(v, inv);
                v = Value("-view"); if (v != null) s.chase = v.Equals("chase", StringComparison.OrdinalIgnoreCase);
                v = Value("-from"); if (v != null) s.from = float.Parse(v, inv);
                v = Value("-seconds"); if (v != null) s.seconds = float.Parse(v, inv);
                v = Value("-fps"); if (v != null) s.fps = Mathf.Clamp(int.Parse(v, inv), 1, 120);
            }
            catch (FormatException e) { error = "democapture arguments: " + e.Message; return false; }
            settings = s;
            return true;
        }

        private void Awake()
        {
            var args = Environment.GetCommandLineArgs();
#if UNITY_EDITOR
            var editorArgs = UnityEditor.SessionState.GetString(EditorArgsKey, "");
            if (editorArgs.Length > 0) { args = editorArgs.Split('\n'); fromEditor = true; }
#endif
            if (!TryParse(args, out settings, out var error))
            {
                if (error != null) Debug.LogError("DemoCapture: " + error);
                settings = null;
                enabled = false;
                return;
            }
            if (assist == null) assist = FindObjectOfType<DriveAssist>();
            if (vehicleCamera == null) vehicleCamera = FindObjectOfType<Parkside.Cameras.VehicleCamera>();
        }

        private void Start()
        {
            if (settings == null) return;
            if (assist == null) { Debug.LogError("DemoCapture: no DriveAssist in the scene"); Finish(); return; }
            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            if (clockSet) Time.captureFramerate = 0;
        }

        private IEnumerator Run()
        {
            Directory.CreateDirectory(settings.dir);
            Time.captureFramerate = settings.fps;
            clockSet = true;
            // DriveAssist starts the practice and chooses the driver view in its first Update
            yield return null;
            yield return null;
            assist.SetPreferredRoute(settings.route);
            assist.JumpTo(settings.route, settings.from);
            assist.BeginAutoDrive();
            if (vehicleCamera != null) vehicleCamera.SetView(settings.chase ? 0 : 2);

            // each jump point's distance along every route that drives past it (NaN where a route does not)
            int routes = Mathf.Max(1, assist.RouteCount), jumps = assist.JumpCount;
            var at = new float[routes, jumps];
            for (int j = 0; j < jumps; j++)
            {
                var point = assist.RouteLanePointAt(assist.JumpRoute(j), assist.JumpDistance(j));
                for (int k = 0; k < routes; k++)
                    at[k, j] = assist.RouteLaneDistance(k, point) < 4f ? assist.RouteDistanceNearest(k, point) : float.NaN;
            }

            var inv = CultureInfo.InvariantCulture;
            var log = new StringBuilder();
            log.AppendFormat(inv, "democapture route={0} '{1}' view={2} from={3:0.0} seconds={4:0.##} fps={5} screen={6}x{7} started={8:O}\n",
                settings.route, assist.RouteName(settings.route), settings.chase ? "chase" : "driver", settings.from, settings.seconds, settings.fps,
                Screen.width, Screen.height, DateTime.Now);
            log.Append("frame jump route s\n");
            int frames = Mathf.Max(1, Mathf.RoundToInt(settings.seconds * settings.fps));
            int lastRoute = assist.AutoRoute;
            float lastS = assist.AutoDistance;
            int saved = 0;
            for (int f = 0; f < frames; f++)
            {
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(settings.dir, f.ToString("00000", inv) + ".jpg"), shot.EncodeToJPG(jpgQuality));
                Destroy(shot);
                saved++;
                int r = assist.AutoRoute;
                float s = assist.AutoDistance;
                if (r == lastRoute && r < routes)
                {
                    float length = assist.RouteLength(r);
                    for (int j = 0; j < jumps; j++)
                    {
                        float js = at[r, j];
                        if (float.IsNaN(js)) continue;
                        bool passed = s >= lastS ? lastS < js && js <= s : js > lastS || js <= s;   // the second: round past the start
                        if (passed) log.AppendFormat(inv, "{0} {1} {2} {3:0.0}\n", f, assist.JumpName(j), r, js);
                    }
                }
                else if (r != lastRoute) log.AppendFormat(inv, "{0} (route {1} -> {2})\n", f, lastRoute, r);
                lastRoute = r;
                lastS = s;
                if (!assist.AutoDriving) { log.AppendFormat(inv, "{0} (auto-drive ended)\n", f); break; }
            }
            log.AppendFormat(inv, "frames={0} ended={1:O}\n", saved, DateTime.Now);
            File.WriteAllText(Path.Combine(settings.dir, "log.txt"), log.ToString());
            Finish();
        }

        private void Finish()
        {
            if (clockSet) { Time.captureFramerate = 0; clockSet = false; }
#if UNITY_EDITOR
            if (!fromEditor) UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
