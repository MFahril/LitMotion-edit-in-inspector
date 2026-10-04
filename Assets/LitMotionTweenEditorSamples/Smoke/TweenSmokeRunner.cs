using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using LitMotion;
using LitMotion.TweenEditor;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace LitMotion.TweenEditor.Samples
{
    /// <summary>
    /// Exercises the package inside a built player and writes a pass/fail report.
    /// </summary>
    /// <remarks>
    /// Some code only exists in a player: the extension registry's assembly scan, code
    /// stripping, Burst compiling the jump adapter's job, and TMP taking a counter's characters
    /// without building a string. The editor test suites cannot reach any of it.
    ///
    /// Dormant unless the player is started with <c>-lmteSmoke</c>, so a normal build of the
    /// scene is unaffected. <c>Tools → LitMotion → Smoke Test</c> builds a player, starts it
    /// with that flag and reads the report back.
    /// </remarks>
    public sealed class TweenSmokeRunner : MonoBehaviour
    {
        const string Flag = "-lmteSmoke";
        const string ResultsFlag = "-lmteSmokeResults";

        readonly List<string> lines = new();
        readonly List<string> loggedErrors = new();
        bool failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0) return;

            var runner = new GameObject("LMTE Smoke Runner").AddComponent<TweenSmokeRunner>();
            DontDestroyOnLoad(runner.gameObject);
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type is LogType.Error or LogType.Exception or LogType.Assert)
            {
                loggedErrors.Add(type + ": " + message);
            }
        }

        void Check(string name, bool pass, string detail)
        {
            failed |= !pass;
            lines.Add((pass ? "PASS " : "FAIL ") + name + " -- " + detail);
        }

        IEnumerator Start()
        {
            // The window may never be focused when started from a script.
            Application.runInBackground = true;
            lines.Add("LitMotion Tween Editor smoke test, Unity " + Application.unityVersion
                      + ", " + (IsIl2Cpp() ? "IL2CPP" : "Mono") + ", " + Application.platform);

            CheckExtensionRegistry();
            yield return PlayEveryRigAnimation();
            yield return CheckJumpRunsOnTheJobPath();
            yield return CheckRuntimeMaterialInstances();
            yield return CheckTmpCounterAllocations();

            Check("console", loggedErrors.Count == 0,
                loggedErrors.Count == 0 ? "no errors or exceptions logged" : string.Join(" | ", loggedErrors));

            lines.Add("RESULT: " + (failed ? "FAIL" : "PASS"));
            WriteReport();
            Application.Quit(failed ? 1 : 0);
        }

        static bool IsIl2Cpp()
        {
#if ENABLE_IL2CPP
            return true;
#else
            return false;
#endif
        }

        void CheckExtensionRegistry()
        {
            var watch = Stopwatch.StartNew();
            TweenExtensionRegistry.Prewarm();
            watch.Stop();

            var intensity = TweenExtensionRegistry.TryGet("com.litmotion.samples.light-intensity", out _);
            var color = TweenExtensionRegistry.TryGet("com.litmotion.samples.light-color", out _);

            Check("extension channels survive the build", intensity && color,
                TweenExtensionRegistry.All.Count + " channels found by the player's scan in "
                + watch.Elapsed.TotalMilliseconds.ToString("F1") + " ms");
        }

        IEnumerator PlayEveryRigAnimation()
        {
            var players = FindObjectsByType<TweenPlayer>();
            var played = 0;
            var empty = 0;
            var refused = new List<string>();

            foreach (var player in players)
            {
                foreach (var animation in player.Animations)
                {
                    if (animation == null) continue;
                    if (animation.EnabledStepCount == 0)
                    {
                        empty++;
                        continue;
                    }

                    if (player.Play(animation).IsActive()) played++;
                    else refused.Add(player.name + "/" + animation.Id);
                }
            }

            // Long enough for every finite rig animation; endless ones are stopped below.
            yield return new WaitForSeconds(3f);

            foreach (var player in players)
            {
                if (player != null) player.StopAll();
            }

            Check("every rig animation plays", played > 0 && refused.Count == 0,
                played + " played across " + players.Length + " players, " + empty + " empty"
                + (refused.Count == 0 ? string.Empty : ", produced nothing: " + string.Join(", ", refused)));
        }

        IEnumerator CheckJumpRunsOnTheJobPath()
        {
            // A one-step Jump takes the fast path, so it runs in LitMotion's Burst job, the code
            // RegisterGenericJobType in JumpMotionAdapters.cs exists for.
            var jumper = new GameObject("smoke jumper");
            var animation = new TweenAnimation { Id = "Jump" };
            animation.Steps.Clear();
            animation.Steps.Add(new TweenStep
            {
                Type = TweenType.Jump,
                Duration = 0.4f,
                Ease = Ease.Linear,
                FromCurrent = false,
                From = Vector4.zero,
                To = new Vector4(2f, 0f, 0f, 0f),
                JumpPower = 1f,
                JumpCount = 1,
            });

            var player = jumper.AddComponent<TweenPlayer>();
            player.Play(animation);

            var peak = 0f;
            var end = Time.time + 1f;
            while (player.IsPlayingAny() && Time.time < end)
            {
                peak = Mathf.Max(peak, jumper.transform.localPosition.y);
                yield return null;
            }

            var landed = jumper.transform.localPosition;
            Check("jump runs in a player", peak > 0.5f && Mathf.Abs(landed.x - 2f) < 1e-3f && Mathf.Abs(landed.y) < 1e-3f,
                "peak " + peak.ToString("F3") + ", landed at " + landed.ToString("F3"));

            Destroy(jumper);
        }

        IEnumerator CheckRuntimeMaterialInstances()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = cube.GetComponent<MeshRenderer>();
            var shared = renderer.sharedMaterial;
            var originalColor = shared.color;

            var step = new TweenStep
            {
                Type = TweenType.MaterialProperty,
                Duration = 0.1f,
                MaterialPropertyKind = TweenMaterialPropertyKind.Color,
                PropertyName = shared.HasProperty("_BaseColor") ? "_BaseColor" : "_Color",
                FromCurrent = false,
                FromColor = Color.white,
                ToColor = Color.magenta,
            };

            var animation = new TweenAnimation { Id = "Tint" };
            animation.Steps.Clear();
            animation.Steps.Add(step);

            var player = cube.AddComponent<TweenPlayer>();
            player.Play(animation);
            yield return new WaitForSeconds(0.3f);

            var instance = renderer.sharedMaterial;
            var ownInstance = instance != shared;
            var sharedUntouched = shared.color == originalColor;

            Destroy(cube);
            yield return null;

            Check("material instances", ownInstance && sharedUntouched && instance == null,
                "own instance " + ownInstance + ", shared untouched " + sharedUntouched
                + ", instance destroyed with its object " + (instance == null));
        }

        IEnumerator CheckTmpCounterAllocations()
        {
            if (TMP_Settings.defaultFontAsset == null)
            {
                lines.Add("SKIP tmp counter -- no TMP default font in this build");
                yield break;
            }

            var canvas = new GameObject("smoke canvas", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var label = new GameObject("smoke counter", typeof(RectTransform));
            label.transform.SetParent(canvas.transform, false);
            var text = label.AddComponent<TextMeshProUGUI>();
            text.text = "0";

            var animation = new TweenAnimation { Id = "Count" };
            animation.Steps.Clear();
            animation.Steps.Add(new TweenStep
            {
                Type = TweenType.TextCounter,
                Duration = 5f,
                Ease = Ease.Linear,
                FromCurrent = false,
                From = Vector4.zero,
                To = new Vector4(1000000f, 0f, 0f, 0f),
                TextFormat = "Score: {0:N0}",
            });

            var player = label.AddComponent<TweenPlayer>();

            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            if (!recorder.Valid)
            {
                lines.Add("SKIP tmp counter -- GC recorder unavailable (not a development build)");
                Destroy(canvas);
                yield break;
            }

            // Three phases over the same label. Idle: what the player allocates on its own.
            // Direct: the label's text changed every frame through TMP's own SetCharArray, with
            // the characters made in advance -- the floor any changing TMP label costs, since TMP
            // re-lays it out. Counting: the same, through the counter. The counter is ours only
            // in what it adds over Direct.
            const int frames = 60;
            var texts = new char[frames][];
            for (var i = 0; i < frames; i++) texts[i] = ("Score: " + (i * 16661).ToString("N0")).ToCharArray();

            for (var i = 0; i < 10; i++) yield return null;
            var idle = 0L;
            for (var i = 0; i < frames; i++)
            {
                yield return null;
                idle += recorder.LastValue;
            }

            for (var i = 0; i < 10; i++)
            {
                text.SetCharArray(texts[i], 0, texts[i].Length);
                yield return null;
            }

            var direct = 0L;
            for (var i = 0; i < frames; i++)
            {
                text.SetCharArray(texts[i], 0, texts[i].Length);
                yield return null;
                direct += recorder.LastValue;
            }

            player.Play(animation);
            for (var i = 0; i < 10; i++) yield return null;

            var counting = 0L;
            var before = text.text;
            for (var i = 0; i < frames; i++)
            {
                yield return null;
                counting += recorder.LastValue;
            }

            // A real per-frame allocation is tens of bytes a frame at least; a few bytes over
            // sixty frames is measurement noise.
            const long noise = 256;
            var changed = text.text != before;
            Check("tmp counter adds no allocations to TMP's own", changed && counting <= direct + noise,
                "over " + frames + " frames: counter " + counting + " B, TMP changing directly " + direct
                + " B, idle " + idle + " B; text changed " + changed);

            player.StopAll();
            Destroy(canvas);
        }

        void WriteReport()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, ResultsFlag);
            var path = index >= 0 && index + 1 < args.Length
                ? args[index + 1]
                : Path.Combine(Application.persistentDataPath, "lmte-smoke.txt");

            var report = string.Join(Environment.NewLine, lines);
            File.WriteAllText(path, report, Encoding.UTF8);
            Debug.Log(report);
        }
    }
}
