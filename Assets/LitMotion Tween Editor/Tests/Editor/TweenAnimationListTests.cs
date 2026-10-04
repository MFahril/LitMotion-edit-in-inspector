using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers adding, duplicating, renaming, reordering and removing a player's animations.
    /// </summary>
    public sealed class TweenAnimationListTests
    {
        GameObject owner;
        TweenPlayer player;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("list-owner");
            player = owner.AddComponent<TweenPlayer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (owner != null) Object.DestroyImmediate(owner);
        }

        TweenPlayerSource Source() => new(new SerializedObject(player));

        void SetAnimations(params TweenAnimation[] animations)
        {
            var field = typeof(TweenPlayer).GetField("animations",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(player, new List<TweenAnimation>(animations));
        }

        static TweenAnimation WithSteps(string id, int count)
        {
            var animation = new TweenAnimation { Id = id };
            animation.Steps.Clear();
            for (var i = 0; i < count; i++) animation.Steps.Add(new TweenStep { StartTime = i });
            return animation;
        }

        // --- Ids ---

        [Test]
        public void AFreeIdIsReturnedAsIs()
        {
            Assert.AreEqual("Show", TweenAnimationListCommands.UniqueId(new[] { "Hide" }, "Show"));
        }

        [Test]
        public void ATakenIdGetsTheLowestFreeNumber()
        {
            Assert.AreEqual("Show 2", TweenAnimationListCommands.UniqueId(new[] { "Show" }, "Show"));
            Assert.AreEqual("Show 3", TweenAnimationListCommands.UniqueId(new[] { "Show", "Show 2" }, "Show"));
        }

        [Test]
        public void NewAnimationsWalkTheBuiltInIds()
        {
            Assert.AreEqual(TweenAnimationId.BuiltIn[0], TweenAnimationListCommands.NextFreeId(new string[0]));
            Assert.AreEqual(TweenAnimationId.BuiltIn[1],
                TweenAnimationListCommands.NextFreeId(new[] { TweenAnimationId.BuiltIn[0] }));
        }

        // --- Commands ---

        [Test]
        public void AddingStartsEmptyRatherThanCopyingThePrevious()
        {
            // Unity's array insert copies the previous element, steps and all.
            SetAnimations(WithSteps(TweenAnimationId.BuiltIn[0], 3));

            var index = TweenAnimationListCommands.Add(Source());

            Assert.AreEqual(1, index);
            Assert.AreEqual(2, player.Animations.Count);
            Assert.AreEqual(0, player.Animations[1].Steps.Count);
            Assert.AreEqual(TweenAnimationId.BuiltIn[1], player.Animations[1].Id);
            Assert.AreEqual(3, player.Animations[0].Steps.Count, "the original is untouched");
        }

        [Test]
        public void AddingFromAPresetCopiesItIndependently()
        {
            var preset = WithSteps("Pop", 2);
            preset.Loops = 3;

            var index = TweenAnimationListCommands.Add(Source(), preset);
            var added = player.Animations[index];

            Assert.AreEqual("Pop", added.Id);
            Assert.AreEqual(2, added.Steps.Count);
            Assert.AreEqual(3, added.Loops);

            added.Steps[0].StartTime = 99f;
            Assert.AreEqual(0f, preset.Steps[0].StartTime, "editing the copy must not edit the preset");
        }

        [Test]
        public void AddingAPresetTwiceKeepsIdsUnique()
        {
            var preset = WithSteps("Pop", 1);

            TweenAnimationListCommands.Add(Source(), preset);
            TweenAnimationListCommands.Add(Source(), preset);

            Assert.AreEqual("Pop", player.Animations[0].Id);
            Assert.AreEqual("Pop 2", player.Animations[1].Id);
        }

        [Test]
        public void DuplicatingKeepsStepsAndPlacesTheCopyNext()
        {
            SetAnimations(WithSteps("Show", 2), WithSteps("Hide", 1));

            var index = TweenAnimationListCommands.Duplicate(Source(), 0);

            Assert.AreEqual(1, index);
            Assert.AreEqual(3, player.Animations.Count);
            Assert.AreEqual("Show Copy", player.Animations[1].Id);
            Assert.AreEqual(2, player.Animations[1].Steps.Count);
            Assert.AreEqual("Hide", player.Animations[2].Id);
        }

        [Test]
        public void RenamingWritesTheId()
        {
            SetAnimations(WithSteps("Show", 0));

            TweenAnimationListCommands.Rename(Source(), 0, "Appear");

            Assert.AreEqual("Appear", player.Animations[0].Id);
        }

        [Test]
        public void MovingReordersAndClampsAtTheEnds()
        {
            SetAnimations(WithSteps("A", 0), WithSteps("B", 0), WithSteps("C", 0));

            Assert.AreEqual(1, TweenAnimationListCommands.Move(Source(), 0, 1));
            Assert.AreEqual("B", player.Animations[0].Id);
            Assert.AreEqual("A", player.Animations[1].Id);

            Assert.AreEqual(2, TweenAnimationListCommands.Move(Source(), 2, 5), "cannot move past the end");
        }

        [Test]
        public void RemovingDeletesOnlyThatAnimation()
        {
            SetAnimations(WithSteps("A", 0), WithSteps("B", 0));

            TweenAnimationListCommands.Remove(Source(), 0);

            Assert.AreEqual(1, player.Animations.Count);
            Assert.AreEqual("B", player.Animations[0].Id);
        }

        [Test]
        public void ThePresetSourceRefusesListEdits()
        {
            var asset = ScriptableObject.CreateInstance<TweenAnimationAsset>();
            try
            {
                var source = new TweenAssetSource(new SerializedObject(asset));

                Assert.AreEqual(-1, TweenAnimationListCommands.Add(source));
                Assert.AreEqual(1, source.AnimationCount);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
