using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the shipped preset library: every preset must build and must stay reusable.
    /// </summary>
    public sealed class TweenPresetTests
    {
        const float Tolerance = 1e-3f;

        GameObject owner;

        [SetUp]
        public void SetUp()
        {
            // A cube with a CanvasGroup satisfies both the transform and the fade channels, so
            // one object can stand in for everything the presets touch.
            owner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owner.AddComponent<CanvasGroup>();
            owner.transform.localPosition = new Vector3(1f, 2f, 3f);
        }

        [TearDown]
        public void TearDown()
        {
            if (owner != null) Object.DestroyImmediate(owner);
        }

        [Test]
        public void TheLibraryHasFourteenPresets()
        {
            Assert.AreEqual(14, TweenPresetGenerator.Build().Count);
        }

        [Test]
        public void EveryPresetBuildsWithoutErrors()
        {
            foreach (var pair in TweenPresetGenerator.Build())
            {
                var errors = new List<string>();
                var handle = TweenAnimationRunner.Build(pair.Value, owner, null, errors);

                Assert.IsEmpty(errors, pair.Key + " reported: " + string.Join(" | ", errors));
                Assert.IsTrue(handle.IsActive(), pair.Key + " produced no motions");

                handle.Cancel();
            }
        }

        [Test]
        public void EveryPresetCarriesAnId()
        {
            foreach (var pair in TweenPresetGenerator.Build())
            {
                Assert.IsNotEmpty(pair.Value.Id, pair.Key + " has no id");
            }
        }

        [Test]
        public void NoPresetReferencesASceneObject()
        {
            // A preset is saved as an asset, and an asset cannot hold a scene reference. One
            // that tried would silently lose its target on save.
            foreach (var pair in TweenPresetGenerator.Build())
            {
                var steps = pair.Value.Steps;
                for (var i = 0; i < steps.Count; i++)
                {
                    Assert.IsNull(steps[i].Target, pair.Key + " step " + i + " has a target");
                }
            }
        }

        [Test]
        public void SlideInPresetsFinishWhereTheObjectAlreadyIs()
        {
            // The whole point of FromOffset: the same asset applied to any object lands on that
            // object's authored position rather than dragging it to a fixed one.
            var presets = TweenPresetGenerator.Build();
            var names = new[] { "SlideInLeft", "SlideInRight", "SlideInTop", "SlideInBottom" };

            foreach (var name in names)
            {
                var start = new Vector3(1f, 2f, 3f);
                owner.transform.localPosition = start;

                var handle = TweenAnimationRunner.Build(presets[name], owner, null);
                handle.Preserve();

                handle.Time = 0f;
                Assert.AreNotEqual(start, owner.transform.localPosition, name + " should start offset");

                handle.Time = handle.TotalDuration;
                Assert.AreEqual(start.x, owner.transform.localPosition.x, Tolerance, name + " x");
                Assert.AreEqual(start.y, owner.transform.localPosition.y, Tolerance, name + " y");
                Assert.AreEqual(start.z, owner.transform.localPosition.z, Tolerance, name + " z");

                handle.Cancel();
            }
        }

        [Test]
        public void PulseLoopsForeverAtTheAnimationLevel()
        {
            // A step cannot loop forever inside a sequence, so an endless pulse has to be
            // expressed as an animation-level loop.
            var pulse = TweenPresetGenerator.Build()["Pulse"];

            Assert.AreEqual(-1, pulse.Loops);
            Assert.AreEqual(LoopType.Yoyo, pulse.LoopType);

            for (var i = 0; i < pulse.Steps.Count; i++)
            {
                Assert.GreaterOrEqual(pulse.Steps[i].Loops, 1, "step " + i + " must not loop forever");
            }
        }

        [Test]
        public void SavedPresetsLoadAndStillBuild()
        {
            // Guards the round trip through serialization, which is where a model change would
            // break existing assets on disk.
            var guids = UnityEditor.AssetDatabase.FindAssets("t:" + nameof(TweenAnimationAsset));
            if (guids.Length == 0) Assert.Ignore("No preset assets in this project yet.");

            for (var i = 0; i < guids.Length; i++)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TweenAnimationAsset>(path);

                Assert.IsNotNull(asset, path);
                Assert.IsNotNull(asset.Animation, path + " has no animation");

                var errors = new List<string>();
                var handle = TweenAnimationRunner.Build(asset.Animation, owner, null, errors);

                Assert.IsEmpty(errors, asset.name + " reported: " + string.Join(" | ", errors));
                if (handle.IsActive()) handle.Cancel();
            }
        }

        [Test]
        public void SettingAnAnimationOnAnAssetCopiesIt()
        {
            // Aliasing would make editing a player's inline animation silently rewrite the asset.
            var source = TweenPresetGenerator.Build()["FadeIn"];
            var asset = ScriptableObject.CreateInstance<TweenAnimationAsset>();

            try
            {
                asset.SetAnimation(source);
                Assert.AreNotSame(source, asset.Animation);
                Assert.AreNotSame(source.Steps[0], asset.Animation.Steps[0]);

                source.Id = "Mutated";
                Assert.AreNotEqual("Mutated", asset.Id);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
