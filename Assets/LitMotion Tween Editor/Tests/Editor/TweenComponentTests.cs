using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the player's asset lookup and the two interaction components.
    /// </summary>
    public sealed class TweenComponentTests
    {
        readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            for (var i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
        }

        GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        TweenAnimationAsset NewAsset(string id)
        {
            var animation = new TweenAnimation { Id = id };
            animation.Steps.Clear();
            animation.Steps.Add(new TweenStep
            {
                Type = TweenType.Move,
                Enabled = true,
                Duration = 0.2f,
                Relative = true,
                To = new Vector4(1f, 0f, 0f, 0f),
            });

            var asset = ScriptableObject.CreateInstance<TweenAnimationAsset>();
            asset.SetAnimation(animation);
            created.Add(asset);
            return asset;
        }

        /// <summary>
        /// Assigns the player's serialized asset list, which has no public setter because
        /// authoring it is the inspector's job.
        /// </summary>
        static void SetAssets(TweenPlayer player, params TweenAnimationAsset[] assets)
        {
            var field = typeof(TweenPlayer).GetField("animationAssets",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(field, "TweenPlayer.animationAssets was renamed");
            field.SetValue(player, new List<TweenAnimationAsset>(assets));
        }

        static void SetInlineAnimations(TweenPlayer player, params TweenAnimation[] animations)
        {
            var field = typeof(TweenPlayer).GetField("animations",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(field, "TweenPlayer.animations was renamed");
            field.SetValue(player, new List<TweenAnimation>(animations));
        }

        // --- Player / asset lookup ---

        [Test]
        public void AnAssetAnimationIsFoundByItsId()
        {
            var player = NewObject("player").AddComponent<TweenPlayer>();
            SetAssets(player, NewAsset("Show"));

            Assert.IsNotNull(player.Find("Show"));
        }

        [Test]
        public void AnInlineAnimationWinsOverAnAssetWithTheSameId()
        {
            // This is what lets one object override a shared preset without editing the asset.
            var player = NewObject("player").AddComponent<TweenPlayer>();
            var inline = new TweenAnimation { Id = "Show", PlaybackSpeed = 3f };

            SetInlineAnimations(player, inline);
            SetAssets(player, NewAsset("Show"));

            Assert.AreSame(inline, player.Find("Show"));
        }

        [Test]
        public void AMissingIdStillReturnsNull()
        {
            var player = NewObject("player").AddComponent<TweenPlayer>();
            SetAssets(player, NewAsset("Show"));

            Assert.IsNull(player.Find("Hide"));
        }

        [Test]
        public void ANullAssetInTheListIsSkipped()
        {
            var player = NewObject("player").AddComponent<TweenPlayer>();
            SetAssets(player, null, NewAsset("Show"));

            Assert.DoesNotThrow(() => player.Find("Show"));
            Assert.IsNotNull(player.Find("Show"));
        }

        // --- Toggleable ---

        [Test]
        public void HidingWithNoAnimationDeactivatesImmediately()
        {
            var go = NewObject("panel");
            var toggle = go.AddComponent<TweenToggleable>();

            toggle.SetVisible(false);

            Assert.IsFalse(go.activeSelf);
            Assert.IsFalse(toggle.IsVisible);
        }

        [Test]
        public void ShowingActivatesBeforeAnimating()
        {
            // An inactive object cannot run its own show animation, so activation has to happen
            // first; this is the ordering that makes the component work at all.
            var go = NewObject("panel");
            var toggle = go.AddComponent<TweenToggleable>();
            toggle.SetVisible(false);

            toggle.SetVisible(true);

            Assert.IsTrue(go.activeSelf);
            Assert.IsTrue(toggle.IsVisible);
        }

        [Test]
        public void ToggleFlipsTheState()
        {
            var go = NewObject("panel");
            var toggle = go.AddComponent<TweenToggleable>();

            toggle.Toggle();
            Assert.IsFalse(toggle.IsVisible);

            toggle.Toggle();
            Assert.IsTrue(toggle.IsVisible);
        }

        [Test]
        public void TheHiddenEventFiresWhenThereIsNothingToWaitFor()
        {
            var go = NewObject("panel");
            var toggle = go.AddComponent<TweenToggleable>();

            var fired = 0;
            toggle.OnHidden.AddListener(() => fired++);

            toggle.SetVisible(false);

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void ImmediateVisibilityDoesNotFireTheEvents()
        {
            // Initial setup is not a transition, so a listener wired to OnShown must not be
            // called just because the object started visible.
            var go = NewObject("panel");
            var toggle = go.AddComponent<TweenToggleable>();

            var fired = 0;
            toggle.OnShown.AddListener(() => fired++);
            toggle.OnHidden.AddListener(() => fired++);

            toggle.SetVisibleImmediate(false);
            toggle.SetVisibleImmediate(true);

            Assert.AreEqual(0, fired);
        }

#if LMTE_SUPPORT_UGUI

        // --- Button ---

        [Test]
        public void AButtonFindsThePlayerOnItsOwnObject()
        {
            var go = NewObject("button");
            var player = go.AddComponent<TweenPlayer>();
            var button = go.AddComponent<TweenButton>();

            Assert.AreSame(player, button.Player);
        }

        [Test]
        public void AButtonWithNoPlayerDoesNothingRatherThanThrowing()
        {
            var button = NewObject("button").AddComponent<TweenButton>();

            Assert.DoesNotThrow(() => button.Play(TweenAnimationId.Click));
            Assert.IsFalse(button.Play(TweenAnimationId.Click).IsActive());
        }

        [Test]
        public void AnEmptyIdIsTreatedAsNotConfigured()
        {
            var go = NewObject("button");
            go.AddComponent<TweenPlayer>();
            var button = go.AddComponent<TweenButton>();

            Assert.IsFalse(button.Play(string.Empty).IsActive());
        }

        [Test]
        public void AButtonPlaysAnAnimationFromItsPlayer()
        {
            var go = NewObject("button");
            var player = go.AddComponent<TweenPlayer>();
            var button = go.AddComponent<TweenButton>();

            var animation = new TweenAnimation { Id = TweenAnimationId.Click };
            animation.Steps.Clear();
            animation.Steps.Add(new TweenStep
            {
                Type = TweenType.Scale,
                Enabled = true,
                Duration = 0.2f,
                UniformScale = true,
                To = new Vector4(1.1f, 1.1f, 1.1f, 0f),
            });
            SetInlineAnimations(player, animation);

            var handle = button.Play(TweenAnimationId.Click);

            Assert.IsTrue(handle.IsActive());
            player.StopAll();
        }
#endif
    }
}
