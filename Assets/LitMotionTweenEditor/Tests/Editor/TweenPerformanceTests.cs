using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
#if LMTE_SUPPORT_UGUI
using UnityEngine.UI;
#endif
#if LMTE_SUPPORT_TMP
using TMPro;
#endif

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Guards the runtime cost of a playing animation: no channel may allocate per frame, and
    /// the benchmark that justified the fast path stays runnable.
    /// </summary>
    /// <remarks>
    /// Every animation runs on a private <see cref="ManualMotionDispatcher"/>, so one frame is
    /// exactly one <c>Update</c> and nothing else in the editor is measured. Each channel is
    /// checked twice: through the one-step fast path, and forced through a sequence.
    /// </remarks>
    public sealed class TweenPerformanceTests
    {
        const double Frame = 1.0 / 60.0;
        const string ExtensionId = "lmte.tests.performance-light-range";

        readonly List<Object> created = new();
        ManualMotionDispatcher dispatcher;
        MotionHandle driver;

        sealed class LightRangeChannel : ITweenExtensionChannel
        {
            public string DisplayName => "Light Range";
            public TweenValueShape Shape => TweenValueShape.Float;
            public string Unit => "m";

            public bool TryResolve(TweenStep step, GameObject gameObject, out Object target, out string error)
            {
                target = gameObject.GetComponent<Light>();
                error = target == null ? "Requires a Light." : null;
                return target != null;
            }

            public bool TryRead(Object target, out Vector4 value)
            {
                value = Vector4.zero;
                if (target is not Light light) return false;
                value.x = light.range;
                return true;
            }

            public bool TryWrite(Object target, Vector4 value)
            {
                if (target is not Light light) return false;
                light.range = value.x;
                return true;
            }
        }

        [SetUp]
        public void SetUp()
        {
            dispatcher = new ManualMotionDispatcher();
            TweenAnimationRunner.FastPathEnabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (driver.IsActive()) driver.Cancel();
            driver = MotionHandle.None;
            TweenAnimationRunner.FastPathEnabled = true;
            TweenExtensionRegistry.Unregister(ExtensionId);

            for (var i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
        }

        T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        GameObject NewObject(params System.Type[] components)
        {
            return Track(new GameObject("performance-target", components));
        }

        static TweenStep Step(TweenType type)
        {
            return new TweenStep
            {
                Type = type,
                Enabled = true,
                Duration = 10f,
                Ease = Ease.OutQuad,
                FromCurrent = false,
                From = Vector4.zero,
                To = Vector4.one,
                FromColor = Color.white,
                ToColor = Color.red,
            };
        }

        /// <summary>
        /// Plays <paramref name="step"/> for a few frames, then asserts the next frame allocates
        /// nothing -- through the fast path and through a sequence.
        /// </summary>
        /// <param name="betweenFrames">
        /// What a real frame does after the motion update, such as the canvas rebuild TMP waits on.
        /// </param>
        void AssertNoGarbagePerFrame(GameObject owner, TweenStep step, System.Action betweenFrames = null)
        {
            foreach (var fastPath in new[] { true, false })
            {
                TweenAnimationRunner.FastPathEnabled = fastPath;
                var path = fastPath ? "fast path" : "sequence";

                var animation = new TweenAnimation { Id = "Perf" };
                animation.Steps.Clear();
                animation.Steps.Add(step);

                var errors = new List<string>();
                driver = TweenAnimationRunner.Build(animation, owner, dispatcher.Scheduler, errors);
                Assert.IsTrue(driver.IsActive(), path + ": " + string.Join("\n", errors));

                // Warm-up: JIT, first-call caches, LitMotion storage growth.
                for (var i = 0; i < 5; i++)
                {
                    dispatcher.Update(Frame);
                    betweenFrames?.Invoke();
                }

                Assert.That(() => dispatcher.Update(Frame), Is.Not.AllocatingGCMemory(),
                    step.Type + " allocated on a frame (" + path + ")");

                driver.Cancel();
                driver = MotionHandle.None;
                betweenFrames?.Invoke();
            }
        }

        // --- Zero garbage per frame, per channel family ---

        [TestCase(TweenType.Move)]
        [TestCase(TweenType.Scale)]
        [TestCase(TweenType.Rotate)]
        [TestCase(TweenType.Punch)]
        [TestCase(TweenType.Shake)]
        [TestCase(TweenType.Jump)]
        public void TransformChannelsAllocateNothingPerFrame(TweenType type)
        {
            var step = Step(type);
            step.RandomSeed = 7u;
            AssertNoGarbagePerFrame(NewObject(), step);
        }

        [Test]
        public void AMaskedAxisReadsBackWithoutAllocating()
        {
            // Masking reads the live value every frame to keep the other axes; that read must
            // not cost garbage either.
            var step = Step(TweenType.Move);
            step.Axis = TweenAxis.X;
            AssertNoGarbagePerFrame(NewObject(), step);
        }

        [Test]
        public void RectChannelsAllocateNothingPerFrame()
        {
            AssertNoGarbagePerFrame(NewObject(typeof(RectTransform)), Step(TweenType.SizeDelta));
        }

        [Test]
        public void CanvasGroupFadeAllocatesNothingPerFrame()
        {
            AssertNoGarbagePerFrame(NewObject(typeof(CanvasGroup)), Step(TweenType.Fade));
        }

        [Test]
        public void SpriteColorAllocatesNothingPerFrame()
        {
            AssertNoGarbagePerFrame(NewObject(typeof(SpriteRenderer)), Step(TweenType.Color));
        }

        [Test]
        public void CameraPropertiesAllocateNothingPerFrame()
        {
            var step = Step(TweenType.CameraProperty);
            step.From = new Vector4(40f, 0f, 0f, 0f);
            step.To = new Vector4(70f, 0f, 0f, 0f);
            AssertNoGarbagePerFrame(NewObject(typeof(Camera)), step);
        }

        [Test]
        public void AudioVolumeAllocatesNothingPerFrame()
        {
            AssertNoGarbagePerFrame(NewObject(typeof(AudioSource)), Step(TweenType.AudioVolume));
        }

        [Test]
        public void MaterialPropertiesAllocateNothingPerFrame()
        {
            var material = Track(new Material(Shader.Find("Unlit/Color")));
            var step = Step(TweenType.MaterialProperty);
            step.MaterialPropertyKind = TweenMaterialPropertyKind.Color;
            step.PropertyName = "_Color";
            step.Target = material;

            AssertNoGarbagePerFrame(NewObject(), step);
        }

        [Test]
        public void CustomStepsAllocateNothingPerFrame()
        {
            AssertNoGarbagePerFrame(NewObject(), Step(TweenType.Custom));
        }

        [Test]
        public void ExtensionChannelsAllocateNothingPerFrame()
        {
            TweenExtensionRegistry.Register(ExtensionId, new LightRangeChannel(), "Tests");

            var step = Step(TweenType.Extension);
            step.ExtensionId = ExtensionId;
            AssertNoGarbagePerFrame(NewObject(typeof(Light)), step);
        }

#if LMTE_SUPPORT_UGUI
        [Test]
        public void GraphicFadeAllocatesNothingPerFrame()
        {
            var canvas = NewObject(typeof(Canvas));
            var image = NewObject(typeof(RectTransform), typeof(Image));
            image.transform.SetParent(canvas.transform, false);

            AssertNoGarbagePerFrame(image, Step(TweenType.Fade));
        }

        [Test]
        public void ACounterOnUguiTextAllocatesNothingWhileItsTextIsUnchanged()
        {
            // A uGUI Text can only take a string, so a new one is made when the shown number
            // changes -- and only then. Over this range "{0:0}" reads "5" throughout.
            var owner = NewObject();
            var text = owner.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var step = Step(TweenType.TextCounter);
            step.From = new Vector4(5f, 0f, 0f, 0f);
            step.To = new Vector4(5.4f, 0f, 0f, 0f);
            step.TextFormat = "{0:0}";

            AssertNoGarbagePerFrame(owner, step);
            Assert.AreEqual("5", text.text);
        }
#endif

        // --- The counter's formatter ---

        [TestCase("{0}")]
        [TestCase("{0:0}")]
        [TestCase("Score: {0:N0}")]
        [TestCase("{0:0.00} pts")]
        [TestCase("{{{0}}}")]
        public void TheCounterFormatsWithoutAllocating(string format)
        {
            var counter = TweenCounterText.Create(format);
            Assert.IsNotNull(counter);
            Assert.IsTrue(counter.IsBuffered, format + " should not need string.Format");

            counter.Format(1f);
            Assert.That(() => counter.Format(1234.5678f), Is.Not.AllocatingGCMemory(), format);
        }

#if LMTE_SUPPORT_TMP
        // --- TMP ---

        GameObject NewTmpText(string content, out TMP_Text text)
        {
            // TMP_Settings.defaultFontAsset throws when the settings asset itself is missing, as it is
            // in a fresh project, so the instance is checked first.
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Assert.Ignore("TMP Essential Resources are not imported in this project.");
            }

            var canvas = NewObject(typeof(Canvas));
            var owner = NewObject(typeof(RectTransform));
            owner.transform.SetParent(canvas.transform, false);

            text = owner.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.ForceMeshUpdate();
            return owner;
        }

        [Test]
        public void ARevealAllocatesNothingPerFrame()
        {
            var owner = NewTmpText("The quick brown fox", out _);
            var step = Step(TweenType.TextReveal);
            step.From = Vector4.zero;
            step.To = new Vector4(1f, 0f, 0f, 0f);

            // A real frame ends with the canvas rebuild, which is what clears TMP's dirty flag.
            AssertNoGarbagePerFrame(owner, step, Canvas.ForceUpdateCanvases);
        }

        [Test]
        public void ACounterOnTmpAllocatesNothingWhileItsTextIsUnchanged()
        {
            // In a player SetCharArray never builds a string; in the editor TMP mirrors the text
            // into a string for the inspector, so this checks the part that is ours: an
            // unchanged number is not rewritten at all.
            var owner = NewTmpText("0", out var text);

            var step = Step(TweenType.TextCounter);
            step.From = new Vector4(5f, 0f, 0f, 0f);
            step.To = new Vector4(5.4f, 0f, 0f, 0f);
            step.TextFormat = "{0:0}";

            AssertNoGarbagePerFrame(owner, step);
            Assert.AreEqual("5", text.text);
        }
#endif

        // --- Benchmark ---

        /// <summary>
        /// Prints play and per-frame costs against raw LitMotion. Explicit: timings are too
        /// machine-dependent to assert on, so this is run by hand when the runtime path changes.
        /// </summary>
        [Test, Explicit, Category("Performance")]
        public void Benchmark()
        {
            const int count = 1000;
            const int frames = 200;

            var objects = new List<GameObject>(count);
            for (var i = 0; i < count; i++) objects.Add(NewObject());

            var report = new StringBuilder();
            report.AppendLine($"{count} objects animating at once, {frames} frames");

            for (var round = 0; round < 2; round++)
            {
                // The first round warms the JIT and LitMotion's storage; only the second is kept.
                report.Length = 0;
                report.AppendLine($"{count} objects animating at once, {frames} frames");

                foreach (var steps in new[] { 1, 3 })
                {
                    Measure(report, steps + " step(s): raw LitMotion", objects, frames, steps, BenchmarkMode.Raw);
                    Measure(report, steps + " step(s): raw LitMotion in LSequence", objects, frames, steps, BenchmarkMode.RawSequence);
                    Measure(report, steps + " step(s): Build, sequence forced", objects, frames, steps, BenchmarkMode.ForcedSequence);
                    Measure(report, steps + " step(s): Build", objects, frames, steps, BenchmarkMode.Build);
                }
            }

            TestContext.WriteLine(report.ToString());
            UnityEngine.Debug.Log(report.ToString());
        }

        enum BenchmarkMode
        {
            Raw,
            RawSequence,
            ForcedSequence,
            Build,
        }

        static void Measure(StringBuilder report, string label, List<GameObject> objects, int frames, int steps,
            BenchmarkMode mode)
        {
            var clock = new ManualMotionDispatcher();
            var handles = new List<MotionHandle>(objects.Count * steps);
            var animations = new List<TweenAnimation>(objects.Count);

            for (var i = 0; i < objects.Count; i++)
            {
                var animation = new TweenAnimation { Id = "Bench" };
                animation.Steps.Clear();
                animation.Steps.Add(Step(TweenType.Move));
                if (steps > 1)
                {
                    animation.Steps.Add(Step(TweenType.Scale));
                    animation.Steps.Add(Step(TweenType.Rotate));
                }

                animations.Add(animation);
            }

            TweenAnimationRunner.FastPathEnabled = mode != BenchmarkMode.ForcedSequence;
            System.GC.Collect();

            var watch = Stopwatch.StartNew();
            for (var i = 0; i < objects.Count; i++)
            {
                var t = objects[i].transform;
                switch (mode)
                {
                    case BenchmarkMode.Raw:
                    case BenchmarkMode.RawSequence:
                    {
                        var a = LMotion.Create(Vector3.zero, Vector3.one, 10f).WithEase(Ease.OutQuad)
                            .WithScheduler(clock.Scheduler).Bind(t, static (v, x) => x.localPosition = v);
                        var b = MotionHandle.None;
                        var c = MotionHandle.None;

                        if (steps > 1)
                        {
                            b = LMotion.Create(Vector3.zero, Vector3.one, 10f).WithEase(Ease.OutQuad)
                                .WithScheduler(clock.Scheduler).Bind(t, static (v, x) => x.localScale = v);
                            c = LMotion.Create(Vector3.zero, Vector3.one, 10f).WithEase(Ease.OutQuad)
                                .WithScheduler(clock.Scheduler).Bind(t, static (v, x) => x.localEulerAngles = v);
                        }

                        if (mode == BenchmarkMode.Raw)
                        {
                            handles.Add(a);
                            if (steps > 1)
                            {
                                handles.Add(b);
                                handles.Add(c);
                            }

                            break;
                        }

                        var sequence = LSequence.Create().Insert(0f, a);
                        if (steps > 1) sequence.Insert(0f, b).Insert(0f, c);
                        var scheduler = clock.Scheduler;
                        handles.Add(sequence.Run(x => x.WithScheduler(scheduler)));
                        break;
                    }

                    default:
                        handles.Add(TweenAnimationRunner.Build(animations[i], objects[i], clock.Scheduler));
                        break;
                }
            }

            watch.Stop();
            var playMicroseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / objects.Count;

            for (var f = 0; f < 5; f++) clock.Update(Frame);

            watch.Restart();
            for (var f = 0; f < frames; f++) clock.Update(Frame);
            watch.Stop();

            report.AppendLine($"{label,-40} play {playMicroseconds,6:F2} us | frame {watch.Elapsed.TotalMilliseconds / frames,6:F3} ms");

            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i].IsActive()) handles[i].Cancel();
            }

            TweenAnimationRunner.FastPathEnabled = true;
        }
    }
}
