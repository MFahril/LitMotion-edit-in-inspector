using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the material property channels and the FromOffset value mode.
    /// </summary>
    public sealed class TweenMaterialTests
    {
        const float Tolerance = 1e-4f;

        GameObject owner;
        Material material;

        readonly List<MotionHandle> handles = new();
        readonly List<MotionHandle> allHandles = new();

        [SetUp]
        public void SetUp()
        {
            handles.Clear();
            // An unlit shader is present in every project and has _Color plus _MainTex, so the
            // test does not depend on which render pipeline is installed.
            material = new Material(Shader.Find("Unlit/Color"));
            owner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owner.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        [TearDown]
        public void TearDown()
        {
            // Built motions live on the editor dispatcher, so an uncancelled one would keep
            // writing to a destroyed object after the test ends.
            for (var i = 0; i < allHandles.Count; i++)
            {
                if (allHandles[i].IsActive()) allHandles[i].Cancel();
            }

            handles.Clear();
            allHandles.Clear();

            if (owner != null) Object.DestroyImmediate(owner);
            if (material != null) Object.DestroyImmediate(material);
        }

        /// <summary>
        /// Builds a step and preserves its handles so setting Time past the end does not
        /// release them mid-assertion.
        /// </summary>
        /// <summary>MotionHandle is a struct, so the list element must be copied to drive it.</summary>
        void SetTime(float seconds)
        {
            var handle = handles[0];
            handle.Time = seconds;
        }

        int BuildPreserved(TweenStep step, out string error)
        {
            handles.Clear();
            var count = TweenStepBuilder.Build(step, owner, null, handles, out error);

            for (var i = 0; i < handles.Count; i++)
            {
                handles[i].Preserve();

                // Tracked separately: a test that builds more than once would otherwise
                // orphan the earlier motions, and they complete against a destroyed object.
                allHandles.Add(handles[i]);
            }

            return count;
        }

        static TweenStep Step(TweenMaterialPropertyKind kind, string property)
        {
            return new TweenStep
            {
                Type = TweenType.MaterialProperty,
                Enabled = true,
                Duration = 1f,
                MaterialPropertyKind = kind,
                PropertyName = property,
            };
        }

        [Test]
        public void MaterialKindChoosesTheChannel()
        {
            Assert.AreEqual(TweenChannelKey.MaterialFloat,
                TweenChannelAccessor.GetChannelKey(Step(TweenMaterialPropertyKind.Float, "_Cutoff"), material));
            Assert.AreEqual(TweenChannelKey.MaterialColor,
                TweenChannelAccessor.GetChannelKey(Step(TweenMaterialPropertyKind.Color, "_Color"), material));
            Assert.AreEqual(TweenChannelKey.MaterialVector,
                TweenChannelAccessor.GetChannelKey(Step(TweenMaterialPropertyKind.Vector, "_Offset"), material));
        }

        [Test]
        public void AMaterialAssetCanBeTargetedDirectly()
        {
            var step = Step(TweenMaterialPropertyKind.Color, "_Color");
            step.Target = material;

            var resolved = TweenBindingResolver.Resolve(step, null, out var error);

            Assert.AreSame(material, resolved, error);
        }

        [Test]
        public void ARendererResolvesToItsSharedMaterialInTheEditor()
        {
            // Instancing in edit mode would leave a stray "(Instance)" material behind after the
            // preview, which outlives the thing that created it.
            var step = Step(TweenMaterialPropertyKind.Color, "_Color");

            var resolved = TweenBindingResolver.Resolve(step, owner, out _);

            Assert.AreSame(TweenChannelAccessor.ResolveMaterial(resolved), material);
        }

        [Test]
        public void ColorPropertiesRoundTrip()
        {
            material.SetColor("_Color", Color.green);
            var step = Step(TweenMaterialPropertyKind.Color, "_Color");
            var context = TweenChannelContext.For(step);

            Assert.IsTrue(TweenChannelAccessor.TryRead(TweenChannelKey.MaterialColor, material, context, out var read));
            Assert.AreEqual(Color.green, (Color)read);

            Assert.IsTrue(TweenChannelAccessor.TryWrite(TweenChannelKey.MaterialColor, material,
                Color.red, TweenAxis.All, context));
            Assert.AreEqual(Color.red, material.GetColor("_Color"));
        }

        [Test]
        public void AMissingPropertyIsRefusedRatherThanWrittenBlind()
        {
            var step = Step(TweenMaterialPropertyKind.Float, "_NotAProperty");
            var context = TweenChannelContext.For(step);

            Assert.IsFalse(TweenChannelAccessor.TryRead(TweenChannelKey.MaterialFloat, material, context, out _));
        }

        [Test]
        public void AnEmptyPropertyNameIsRefused()
        {
            // Shader.PropertyToID("") is not 0, so the context has to be the thing that reports
            // an unset name, otherwise a nameless step would silently write to property id 0.
            var step = Step(TweenMaterialPropertyKind.Float, string.Empty);
            var context = TweenChannelContext.For(step);

            Assert.AreEqual(0, context.PropertyId);
            Assert.IsFalse(TweenChannelAccessor.TryRead(TweenChannelKey.MaterialFloat, material, context, out _));
        }

        [Test]
        public void AMaterialStepWithNoPropertyNameReportsWhatIsMissing()
        {
            var step = Step(TweenMaterialPropertyKind.Float, string.Empty);
            step.Target = material;

            var built = BuildPreserved(step, out var error);

            Assert.AreEqual(0, built);
            Assert.IsNotNull(error);
            StringAssert.Contains("property name", error);
        }

        [Test]
        public void AMaterialStepNamingAnAbsentPropertySaysSo()
        {
            var step = Step(TweenMaterialPropertyKind.Float, "_NotAProperty");
            step.Target = material;

            var built = BuildPreserved(step, out var error);

            Assert.AreEqual(0, built);
            StringAssert.Contains("_NotAProperty", error);
        }

        [Test]
        public void VectorPropertiesKeepTheirWComponent()
        {
            // The default axis mask is XYZ. A shader vector's fourth component is not a "Z axis"
            // the author chose to exclude, so masking must not silently drop it.
            var step = Step(TweenMaterialPropertyKind.Vector, "_Color");

            var axis = TweenChannelAccessor.GetEffectiveAxis(step, TweenChannelKey.MaterialVector);

            Assert.AreEqual(TweenAxis.All, axis);
        }

        // --- FromOffset ---

        [Test]
        public void FromOffsetStartsOffsetAndLandsOnTheLiveValue()
        {
            owner.transform.localPosition = new Vector3(5f, 6f, 7f);

            var step = new TweenStep
            {
                Type = TweenType.Move,
                Enabled = true,
                Duration = 1f,
                Ease = Ease.Linear,
                FromOffset = true,
                From = new Vector4(-10f, 0f, 0f, 0f),
            };

            var built = BuildPreserved(step, out var error);

            Assert.AreEqual(1, built, error);

            // At time zero the object sits at the offset; at the end it is back where it started.
            SetTime(0f);
            Assert.AreEqual(new Vector3(-5f, 6f, 7f), owner.transform.localPosition);

            SetTime(1f);
            Assert.AreEqual(new Vector3(5f, 6f, 7f), owner.transform.localPosition);

        }

        [Test]
        public void FromOffsetIgnoresTheEndValue()
        {
            owner.transform.localPosition = Vector3.zero;

            var step = new TweenStep
            {
                Type = TweenType.Move,
                Enabled = true,
                Duration = 1f,
                Ease = Ease.Linear,
                FromOffset = true,
                From = new Vector4(0f, 100f, 0f, 0f),
                To = new Vector4(999f, 999f, 999f, 0f),
            };

            Assert.AreEqual(1, BuildPreserved(step, out _));

            SetTime(1f);
            Assert.AreEqual(Vector3.zero, owner.transform.localPosition);

        }

        [Test]
        public void FromOffsetReadsTheLiveValueEvenWithoutFromCurrent()
        {
            // FromOffset is meaningless without the current value, so it must not depend on the
            // author also remembering to tick FromCurrent.
            owner.transform.localScale = new Vector3(2f, 2f, 2f);

            var step = new TweenStep
            {
                Type = TweenType.Scale,
                Enabled = true,
                Duration = 1f,
                Ease = Ease.Linear,
                FromCurrent = false,
                FromOffset = true,
                UniformScale = true,
                From = new Vector4(-1f, 0f, 0f, 0f),
            };

            Assert.AreEqual(1, BuildPreserved(step, out var error));

            SetTime(0f);
            Assert.AreEqual(1f, owner.transform.localScale.x, Tolerance, error);

            SetTime(1f);
            Assert.AreEqual(2f, owner.transform.localScale.x, Tolerance);

        }
    }
}
