using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;
#if LMTE_SUPPORT_UGUI
using UnityEngine.UI;
#endif
#if LMTE_SUPPORT_TMP
using TMPro;
#endif

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Checks that a freshly added clip of every type does something visible to a freshly made
    /// object, at a size that reads as a starting point rather than a surprise.
    /// </summary>
    /// <remarks>
    /// Each case makes the kind of object a type is meant for, applies the defaults the timeline
    /// would apply when adding a clip to it, plays the clip through at many points and measures
    /// the largest change in the channel it writes. Punch and shake end where they began, so the
    /// largest change along the way is the measure, not the end value.
    ///
    /// Distances are checked in world units on 3D objects and in pixels on UI elements, since the
    /// defaults are sized for whichever the target uses.
    /// </remarks>
    public sealed class TweenStepDefaultsTests
    {
        const int Samples = 40;

        readonly List<Object> created = new();
        readonly List<MotionHandle> handles = new();

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i].IsActive()) handles[i].Cancel();
            }

            handles.Clear();

            for (var i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
        }

        GameObject World(params System.Type[] components)
        {
            var go = new GameObject("world target", components);
            created.Add(go);
            return go;
        }

        GameObject Ui(params System.Type[] components)
        {
            var canvas = new GameObject("canvas", typeof(Canvas));
            created.Add(canvas);

            var go = new GameObject("ui target", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            foreach (var component in components) go.AddComponent(component);
            return go;
        }

        /// <summary>
        /// Adds a default clip of <paramref name="type"/> for <paramref name="target"/> and returns
        /// the largest change it makes to its channel while it plays.
        /// </summary>
        float LargestChange(TweenType type, GameObject target)
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, type, 0f, target);

            Assert.IsTrue(TweenChannelInfo.TryRead(step, target, out var start), type + ": channel unreadable");

            var built = new List<MotionHandle>();
            var count = TweenStepBuilder.Build(step, target, null, built, out var error);
            Assert.Greater(count, 0, type + ": " + error);

            for (var i = 0; i < built.Count; i++)
            {
                built[i].Preserve();
                handles.Add(built[i]);
            }

            var total = (float)built[0].TotalDuration;
            var largest = 0f;

            for (var i = 0; i <= Samples; i++)
            {
                var time = total * i / Samples;
                for (var h = 0; h < built.Count; h++)
                {
                    var handle = built[h];
                    handle.Time = time;
                }

                Assert.IsTrue(TweenChannelInfo.TryRead(step, target, out var value));
                largest = Mathf.Max(largest, (value - start).magnitude);
            }

            return largest;
        }

        static void AssertSized(float change, float atLeast, float atMost, string what)
        {
            Assert.That(change, Is.GreaterThanOrEqualTo(atLeast), what + " is too small to see: " + change);
            Assert.That(change, Is.LessThanOrEqualTo(atMost), what + " is bigger than a starting point should be: " + change);
        }

        // --- Transform types, on a 3D object and on a UI element ---

        [TestCase(TweenType.Move, 0.5f, 1.5f)]
        [TestCase(TweenType.Jump, 0.5f, 1.5f)]
        [TestCase(TweenType.Punch, 0.05f, 0.5f)]
        [TestCase(TweenType.Shake, 0.02f, 0.3f)]
        [TestCase(TweenType.Rotate, 45f, 100f)]
        [TestCase(TweenType.Scale, 0.1f, 0.7f)]
        public void ADefaultClipOnA3DObjectIsWorldSized(TweenType type, float atLeast, float atMost)
        {
            AssertSized(LargestChange(type, World()), atLeast, atMost, type + " on a 3D object");
        }

#if LMTE_SUPPORT_UGUI
        [TestCase(TweenType.Move, 50f, 150f)]
        [TestCase(TweenType.Jump, 50f, 150f)]
        [TestCase(TweenType.Punch, 5f, 50f)]
        [TestCase(TweenType.Shake, 2f, 30f)]
        [TestCase(TweenType.Rotate, 45f, 100f)]
        [TestCase(TweenType.Scale, 0.1f, 0.7f)]
        public void ADefaultClipOnAUiElementIsPixelSized(TweenType type, float atLeast, float atMost)
        {
            AssertSized(LargestChange(type, Ui(typeof(Image))), atLeast, atMost, type + " on a UI element");
        }

        [Test]
        public void DistancesArePixelsOnUiAndWorldUnitsElsewhere()
        {
            var world = new TweenStep();
            TweenStepDefaults.Apply(world, TweenType.Move, 0f, World());
            Assert.AreEqual(1f, world.To.x, 1e-6f);

            var ui = new TweenStep();
            TweenStepDefaults.Apply(ui, TweenType.Move, 0f, Ui());
            Assert.AreEqual(TweenStepDefaults.UiPixelsPerUnit, ui.To.x, 1e-6f);

            // A RectTransform outside any canvas, such as a world-space TMP label, is world units.
            var loose = World(typeof(RectTransform));
            Assert.AreEqual(1f, TweenStepDefaults.DistanceScale(loose));

            // No target known, as for a preset: world units.
            var none = new TweenStep();
            TweenStepDefaults.Apply(none, TweenType.Move, 0f);
            Assert.AreEqual(1f, none.To.x, 1e-6f);
        }

        // --- Rect, graphics and fill, on UI ---

        [TestCase(TweenType.SizeDelta, 25f, 100f)]
        [TestCase(TweenType.Pivot, 0.25f, 1f)]
        [TestCase(TweenType.Anchors, 0.25f, 1f)]
        public void ADefaultRectClipChangesAFreshUiElement(TweenType type, float atLeast, float atMost)
        {
            AssertSized(LargestChange(type, Ui(typeof(Image))), atLeast, atMost, type.ToString());
        }

        [TestCase(TweenType.Fade, 0.5f, 1.01f)]
        [TestCase(TweenType.Color, 0.3f, 1.2f)]
        [TestCase(TweenType.FillAmount, 0.5f, 1.01f)]
        public void ADefaultGraphicClipChangesAFreshImage(TweenType type, float atLeast, float atMost)
        {
            AssertSized(LargestChange(type, Ui(typeof(Image))), atLeast, atMost, type + " on an Image");
        }
#endif

        [TestCase(TweenType.Fade, 0.5f, 1.01f)]
        [TestCase(TweenType.Color, 0.3f, 1.2f)]
        public void ADefaultGraphicClipChangesAFreshSprite(TweenType type, float atLeast, float atMost)
        {
            AssertSized(LargestChange(type, World(typeof(SpriteRenderer))), atLeast, atMost, type + " on a sprite");
        }

        // --- Camera, audio, rendering ---

        [Test]
        public void ADefaultCameraClipZoomsAFreshCamera()
        {
            AssertSized(LargestChange(TweenType.CameraProperty, World(typeof(Camera))), 5f, 30f, "field of view");
        }

        [TestCase(TweenType.AudioVolume, 0.5f, 1.01f)]
        [TestCase(TweenType.AudioPitch, 0.25f, 1f)]
        public void ADefaultAudioClipChangesAFreshSource(TweenType type, float atLeast, float atMost)
        {
            AssertSized(LargestChange(type, World(typeof(AudioSource))), atLeast, atMost, type.ToString());
        }

#if LMTE_SUPPORT_RENDER_PIPELINES
        [Test]
        public void ADefaultVolumeClipChangesAFreshVolume()
        {
            AssertSized(LargestChange(TweenType.VolumeWeight, World(typeof(UnityEngine.Rendering.Volume))),
                0.5f, 1.01f, "volume weight");
        }
#endif

        // --- Text ---

#if LMTE_SUPPORT_UGUI
        [Test]
        public void ADefaultCounterCountsAFreshLabel()
        {
            var target = Ui(typeof(Text));
            var text = target.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            PlayToEnd(TweenType.TextCounter, target);
            Assert.AreEqual("100", text.text);
        }

        [Test]
        public void ADefaultScrambleWritesAFreshLabel()
        {
            var target = Ui(typeof(Text));
            var text = target.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            PlayToEnd(TweenType.TextScramble, target);
            Assert.AreEqual("Hello", text.text);
        }
#endif

#if LMTE_SUPPORT_TMP
        GameObject TmpLabel()
        {
            // The settings asset is missing in a project without TMP Essential Resources, and
            // defaultFontAsset throws then, so the instance is checked first.
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Assert.Ignore("TMP Essential Resources are not imported in this project.");
            }

            var target = Ui(typeof(TextMeshProUGUI));
            var text = target.GetComponent<TextMeshProUGUI>();
            text.text = "LitMotion";
            text.ForceMeshUpdate();
            return target;
        }

        [Test]
        public void ADefaultRevealRevealsAFreshTmpLabel()
        {
            AssertSized(LargestChange(TweenType.TextReveal, TmpLabel()), 0.5f, 1.01f, "reveal fraction");
        }

        [Test]
        public void ADefaultCharacterWaveBuildsForEveryCharacter()
        {
            var step = new TweenStep();
            var target = TmpLabel();
            TweenStepDefaults.Apply(step, TweenType.TMPCharacter, 0f, target);

            var built = new List<MotionHandle>();
            Assert.AreEqual(9, TweenStepBuilder.Build(step, target, null, built, out var error), error);
            handles.AddRange(built);

            Assert.AreEqual(20f, step.To.y, 1e-4f, "a UI label rises 20 pixels");
        }
#endif

        void PlayToEnd(TweenType type, GameObject target)
        {
            var step = new TweenStep();
            TweenStepDefaults.Apply(step, type, 0f, target);

            var built = new List<MotionHandle>();
            Assert.Greater(TweenStepBuilder.Build(step, target, null, built, out var error), 0, error);

            var handle = built[0];
            handle.Preserve();
            handles.Add(handle);
            handle.Time = handle.TotalDuration;
        }
    }
}
