using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the player's code-facing API: per-play callbacks, awaiting, pausing and playing on
    /// another target.
    /// </summary>
    /// <remarks>
    /// Edit mode never advances the motion clock inside a test, so these drive plays to their
    /// end with <c>Complete</c> and <c>Stop</c>, which LitMotion resolves synchronously. Real-time
    /// behaviour is covered by the PlayMode suite. Each test runs through the one-step fast path
    /// and through a sequence, since the callbacks are attached differently on each.
    /// </remarks>
    [TestFixture(true)]
    [TestFixture(false)]
    public sealed class TweenPlayerApiTests
    {
        const string MoveId = "Move";

        readonly bool fastPath;
        readonly List<Object> created = new();
        TweenPlayer player;

        public TweenPlayerApiTests(bool fastPath)
        {
            this.fastPath = fastPath;
        }

        [SetUp]
        public void SetUp()
        {
            TweenAnimationRunner.FastPathEnabled = fastPath;
            player = NewObject("player").AddComponent<TweenPlayer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null) player.StopAll();
            TweenAnimationRunner.FastPathEnabled = true;

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

        static TweenAnimation MoveBy(float x, TweenKillBehavior kill = TweenKillBehavior.Cancel)
        {
            var animation = new TweenAnimation { Id = MoveId, KillBehavior = kill };
            animation.Steps.Clear();
            animation.Steps.Add(new TweenStep
            {
                Type = TweenType.Move,
                Enabled = true,
                Duration = 1f,
                Ease = Ease.Linear,
                Relative = true,
                To = new Vector4(x, 0f, 0f, 0f),
            });
            return animation;
        }

        void SetAnimations(params TweenAnimation[] animations)
        {
            var field = typeof(TweenPlayer).GetField("animations",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(field, "TweenPlayer.animations was renamed");
            field.SetValue(player, new List<TweenAnimation>(animations));
        }

        static bool Result(Awaitable<bool> awaitable)
        {
            var awaiter = awaitable.GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, "the play should have resolved synchronously");
            return awaiter.GetResult();
        }

        // --- Per-play completion ---

        [Test]
        public void APerPlayCallbackFiresWhenThePlayFinishes()
        {
            SetAnimations(MoveBy(1f));
            var calls = 0;

            Assert.IsTrue(player.Play(MoveId, () => calls++).IsActive());
            player.Complete(MoveId);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void APerPlayCallbackDoesNotFireWhenThePlayIsStopped()
        {
            SetAnimations(MoveBy(1f));
            var calls = 0;

            player.Play(MoveId, () => calls++);
            player.Stop(MoveId);

            Assert.AreEqual(0, calls, "matches OnComplete, which a stop does not raise either");
        }

        [Test]
        public void ThePerPlayCallbackRunsAfterTheAnimationsOwnEvent()
        {
            var animation = MoveBy(1f);
            var order = new List<string>();
            animation.OnComplete.AddListener(() => order.Add("event"));
            SetAnimations(animation);

            player.Play(MoveId, () => order.Add("callback"));
            player.Complete(MoveId);

            CollectionAssert.AreEqual(new[] { "event", "callback" }, order);
        }

        [Test]
        public void APerPlayCallbackBelongsToThatPlayOnly()
        {
            var animation = MoveBy(1f);
            animation.BlendMode = TweenBlendMode.Additive;
            SetAnimations(animation);
            var calls = 0;

            player.Play(MoveId, () => calls++);
            player.Play(MoveId);
            player.Complete(MoveId);

            Assert.AreEqual(1, calls, "the plain play must not inherit the first play's callback");
        }

        [Test]
        public void CallbacksMayPlayAndStopWhileAStopIsUnderway()
        {
            // Completion runs callbacks synchronously, in the middle of the player walking its
            // list of running plays; a callback that plays or stops must not derail that walk.
            var move = MoveBy(1f);
            move.BlendMode = TweenBlendMode.Additive;
            var other = MoveBy(1f);
            other.Id = "Other";
            SetAnimations(move, other);

            player.Play(MoveId, () => player.Play("Other"));
            player.Play(MoveId, () => player.Stop("Other"));
            player.Play(MoveId, () => player.Play("Other"));

            Assert.DoesNotThrow(() => player.Complete(MoveId));
            Assert.IsFalse(player.IsPlaying(MoveId));
            Assert.IsTrue(player.IsPlaying("Other"), "the last callback's play must survive");

            Assert.DoesNotThrow(() => player.StopAll());
            Assert.IsFalse(player.IsPlayingAny());
        }

        // --- Awaiting ---

        [Test]
        public void PlayAsyncIsTrueWhenThePlayFinishes()
        {
            SetAnimations(MoveBy(1f));

            var play = player.PlayAsync(MoveId);
            Assert.IsFalse(play.GetAwaiter().IsCompleted);

            player.Complete(MoveId);
            Assert.IsTrue(Result(play));
        }

        [Test]
        public void PlayAsyncIsFalseWhenThePlayIsStopped()
        {
            SetAnimations(MoveBy(1f));

            var play = player.PlayAsync(MoveId);
            player.Stop(MoveId);

            Assert.IsFalse(Result(play));
        }

        [Test]
        public void PlayAsyncIsFalseWhenAnotherPlayReplacesIt()
        {
            SetAnimations(MoveBy(1f));

            var first = player.PlayAsync(MoveId);
            player.Play(MoveId);

            Assert.IsFalse(Result(first), "Override stopped it, so it did not finish");
        }

        [Test]
        public void PlayAsyncIsFalseForAnAnimationThatDoesNotExist()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(Result(player.PlayAsync("Missing")));
        }

        [Test]
        public void CancellingTheTokenStopsThePlay()
        {
            SetAnimations(MoveBy(1f, TweenKillBehavior.Rewind));
            var start = player.transform.localPosition;

            using var source = new CancellationTokenSource();
            var play = player.PlayAsync(MoveId, source.Token);
            player.transform.localPosition = new Vector3(0.5f, 0f, 0f);

            source.Cancel();

            Assert.IsFalse(Result(play));
            Assert.IsFalse(player.IsPlaying(MoveId));
            Assert.AreEqual(start, player.transform.localPosition, "the stop applied the kill behavior");
        }

        [Test]
        public void AnAlreadyCancelledTokenPlaysNothing()
        {
            SetAnimations(MoveBy(1f));

            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.IsFalse(Result(player.PlayAsync(MoveId, source.Token)));
            Assert.IsFalse(player.IsPlaying(MoveId));
        }

        [Test]
        public void CancellingAfterTheEndChangesNothing()
        {
            SetAnimations(MoveBy(1f));

            using var source = new CancellationTokenSource();
            var play = player.PlayAsync(MoveId, source.Token);
            player.Complete(MoveId);
            source.Cancel();

            Assert.IsTrue(Result(play));
        }

        // --- Pausing ---

        [Test]
        public void PausingHoldsTheAnimationAndResumingRestoresItsSpeed()
        {
            var animation = MoveBy(1f);
            animation.PlaybackSpeed = 2f;
            SetAnimations(animation);

            var handle = player.Play(MoveId);
            Assert.AreEqual(2f, handle.PlaybackSpeed, 1e-6f);

            player.Pause(MoveId);
            Assert.AreEqual(0f, handle.PlaybackSpeed);
            Assert.IsTrue(player.IsPaused(MoveId));
            Assert.IsTrue(player.IsPlaying(MoveId), "a paused animation is still playing");

            player.Pause(MoveId);
            player.Resume(MoveId);
            Assert.AreEqual(2f, handle.PlaybackSpeed, 1e-6f, "pausing twice must not lose the speed");
            Assert.IsFalse(player.IsPaused(MoveId));
        }

        [Test]
        public void PauseAllAndResumeAllCoverEveryPlay()
        {
            var other = MoveBy(1f);
            other.Id = "Other";
            SetAnimations(MoveBy(1f), other);

            var move = player.Play(MoveId);
            var second = player.Play("Other");

            player.PauseAll();
            Assert.AreEqual(0f, move.PlaybackSpeed);
            Assert.AreEqual(0f, second.PlaybackSpeed);

            player.ResumeAll();
            Assert.AreEqual(1f, move.PlaybackSpeed, 1e-6f);
            Assert.AreEqual(1f, second.PlaybackSpeed, 1e-6f);
        }

        [Test]
        public void StoppingAPausedRewindStillRestores()
        {
            SetAnimations(MoveBy(10f, TweenKillBehavior.Rewind));
            player.transform.localPosition = Vector3.one;

            var handle = player.Play(MoveId);
            handle.Time = 0.5f;
            player.Pause(MoveId);
            player.Stop(MoveId);

            Assert.AreEqual(Vector3.one, player.transform.localPosition);
            Assert.IsFalse(player.IsPaused(MoveId));
        }

        [Test]
        public void ANewPlayStartsUnpaused()
        {
            SetAnimations(MoveBy(1f));

            player.Play(MoveId);
            player.Pause(MoveId);
            var replay = player.Play(MoveId);

            Assert.IsFalse(player.IsPaused(MoveId));
            Assert.AreEqual(1f, replay.PlaybackSpeed, 1e-6f);
        }

        // --- Targets ---

        [Test]
        public void AnAnimationCanPlayOnAnotherObject()
        {
            SetAnimations(MoveBy(4f));
            var pooled = NewObject("pooled");

            // Mid-play: setting Time to the end would complete it and end the play.
            var handle = player.Play(MoveId, pooled);
            handle.Time = 0.5f;

            Assert.AreEqual(2f, pooled.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(0f, player.transform.localPosition.x, "the player's own object stayed put");
            Assert.IsTrue(player.IsPlaying(MoveId, pooled));
            Assert.IsFalse(player.IsPlaying(MoveId, player.gameObject));
        }

        [Test]
        public void OverrideReplacesAPlayOnTheSameTargetOnly()
        {
            SetAnimations(MoveBy(1f));
            var a = NewObject("a");
            var b = NewObject("b");

            var onA = player.Play(MoveId, a);
            var onB = player.Play(MoveId, b);
            Assert.IsTrue(onA.IsActive() && onB.IsActive(), "one player drives both at once");

            var againOnA = player.Play(MoveId, a);
            Assert.IsFalse(onA.IsActive(), "the second play on a replaced the first");
            Assert.IsTrue(onB.IsActive(), "b is a different target and was left alone");
            Assert.IsTrue(againOnA.IsActive());
        }

        [Test]
        public void StoppingOneTargetLeavesTheOthersRunning()
        {
            SetAnimations(MoveBy(1f));
            var a = NewObject("a");
            var b = NewObject("b");

            player.Play(MoveId, a);
            player.Play(MoveId, b);
            player.Stop(MoveId, a);

            Assert.IsFalse(player.IsPlaying(MoveId, a));
            Assert.IsTrue(player.IsPlaying(MoveId, b));
            Assert.IsTrue(player.IsPlayingAny());

            player.Stop(MoveId);
            Assert.IsFalse(player.IsPlayingAny(), "Stop without a target stops every target");
        }

        [Test]
        public void AStepWithItsOwnTargetIgnoresThePlayTarget()
        {
            var fixedTarget = NewObject("fixed");
            var animation = MoveBy(3f);
            animation.Steps[0].Target = fixedTarget.transform;
            SetAnimations(animation);
            var pooled = NewObject("pooled");

            var handle = player.Play(MoveId, pooled);
            handle.Time = 0.5f;

            Assert.AreEqual(1.5f, fixedTarget.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(0f, pooled.transform.localPosition.x);
        }

        [Test]
        public void ARewindOnAnotherTargetRestoresThatTarget()
        {
            SetAnimations(MoveBy(10f, TweenKillBehavior.Rewind));
            var pooled = NewObject("pooled");
            pooled.transform.localPosition = new Vector3(1f, 2f, 3f);

            var handle = player.Play(MoveId, pooled);
            handle.Time = 0.5f;
            player.Stop(MoveId, pooled);

            Assert.AreEqual(new Vector3(1f, 2f, 3f), pooled.transform.localPosition);
        }

        [Test]
        public void ANullTargetMeansThePlayersOwnObject()
        {
            SetAnimations(MoveBy(2f));

            var handle = player.Play(MoveId, (GameObject)null);
            handle.Time = 0.5f;

            Assert.AreEqual(1f, player.transform.localPosition.x, 1e-4f);
            Assert.IsTrue(player.IsPlaying(MoveId, null));
        }
    }
}
