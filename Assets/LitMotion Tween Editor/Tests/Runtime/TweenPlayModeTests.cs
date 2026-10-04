using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if LMTE_SUPPORT_UGUI
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace LitMotion.TweenEditor.PlayModeTests
{
    /// <summary>
    /// Covers what only real play mode can show: animations advancing on the game clock,
    /// component lifecycles, the two interaction components, and runtime material instances.
    /// </summary>
    /// <remarks>
    /// The EditMode suite drives motions by setting their time, which never exercises Unity's
    /// frame loop, <c>Time.timeScale</c>, <c>OnEnable</c>/<c>OnDisable</c> or coroutines. Durations
    /// are kept short so the suite stays fast; waits are generous so a slow frame cannot fail it.
    /// </remarks>
    public sealed class TweenPlayModeTests
    {
        const float Duration = 0.2f;
        const float Tolerance = 1e-4f;
        const string MoveId = "Move";

        readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;

            for (var i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.Destroy(created[i]);
            }

            created.Clear();
        }

        T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        GameObject NewObject(string name, params System.Type[] components)
        {
            return Track(new GameObject(name, components));
        }

        static void SetField(object target, string field, object value)
        {
            var info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, target.GetType().Name + "." + field + " was renamed");
            info.SetValue(target, value);
        }

        static TweenPlayer AddPlayer(GameObject go, params TweenAnimation[] animations)
        {
            var player = go.AddComponent<TweenPlayer>();
            SetField(player, "animations", new List<TweenAnimation>(animations));
            return player;
        }

        static TweenStep Step(TweenType type, float duration = Duration)
        {
            return new TweenStep
            {
                Type = type,
                Enabled = true,
                Duration = duration,
                Ease = Ease.Linear,
                FromCurrent = true,
            };
        }

        static TweenAnimation Animation(string id, params TweenStep[] steps)
        {
            var animation = new TweenAnimation { Id = id };
            animation.Steps.Clear();
            animation.Steps.AddRange(steps);
            return animation;
        }

        static TweenAnimation MoveBy(float x, float duration = Duration)
        {
            var step = Step(TweenType.Move, duration);
            step.Relative = true;
            step.To = new Vector4(x, 0f, 0f, 0f);
            return Animation(MoveId, step);
        }

        /// <summary>A multi-step animation, so the sequence path is covered too.</summary>
        static TweenAnimation MoveThenScale(float x)
        {
            var move = Step(TweenType.Move);
            move.Relative = true;
            move.To = new Vector4(x, 0f, 0f, 0f);

            var scale = Step(TweenType.Scale);
            scale.FromCurrent = false;
            scale.From = Vector4.one;
            scale.To = new Vector4(2f, 2f, 2f, 0f);
            scale.StartTime = Duration;

            return Animation(MoveId, move, scale);
        }

        static IEnumerator WaitUntilOrTimeout(System.Func<bool> condition, float timeout = 3f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(condition(), "timed out after " + timeout + " s");
        }

        // --- Real-time playback ---

        [UnityTest]
        public IEnumerator AOneStepAnimationPlaysToItsEndOnTheGameClock()
        {
            var go = NewObject("mover");
            var player = AddPlayer(go, MoveBy(2f));

            player.Play(MoveId);
            yield return null;
            Assert.IsTrue(player.IsPlaying(MoveId));

            yield return WaitUntilOrTimeout(() => !player.IsPlaying(MoveId));
            Assert.AreEqual(2f, go.transform.localPosition.x, Tolerance);
        }

        [UnityTest]
        public IEnumerator AMultiStepAnimationPlaysEveryStepToItsEnd()
        {
            var go = NewObject("mover");
            var player = AddPlayer(go, MoveThenScale(3f));

            player.Play(MoveId);
            yield return WaitUntilOrTimeout(() => !player.IsPlaying(MoveId));

            Assert.AreEqual(3f, go.transform.localPosition.x, Tolerance);
            Assert.AreEqual(2f, go.transform.localScale.x, Tolerance);
        }

        [UnityTest]
        public IEnumerator IgnoreTimeScaleKeepsPlayingWhileTheGameIsPaused()
        {
            var unscaled = MoveBy(1f);
            unscaled.IgnoreTimeScale = true;

            var unscaledObject = NewObject("unscaled");
            var scaledObject = NewObject("scaled");
            var unscaledPlayer = AddPlayer(unscaledObject, unscaled);
            var scaledPlayer = AddPlayer(scaledObject, MoveBy(1f));

            Time.timeScale = 0f;
            unscaledPlayer.Play(MoveId);
            scaledPlayer.Play(MoveId);

            yield return new WaitForSecondsRealtime(Duration * 2f);

            Assert.AreEqual(1f, unscaledObject.transform.localPosition.x, Tolerance, "unscaled time did not advance it");
            Assert.AreEqual(0f, scaledObject.transform.localPosition.x, Tolerance, "a scaled animation moved at timeScale 0");
            Assert.IsTrue(scaledPlayer.IsPlaying(MoveId));
        }

        [UnityTest]
        public IEnumerator PlaybackSpeedShortensThePlay()
        {
            var fast = MoveBy(1f, 1f);
            fast.PlaybackSpeed = 10f;

            var go = NewObject("fast");
            var player = AddPlayer(go, fast);
            player.Play(MoveId);

            yield return new WaitForSeconds(0.3f);

            Assert.IsFalse(player.IsPlaying(MoveId), "a 1 s animation at 10x should be over in 0.1 s");
            Assert.AreEqual(1f, go.transform.localPosition.x, Tolerance);
        }

        // --- Lifecycle ---

        [UnityTest]
        public IEnumerator PlayOnEnablePlaysWhenTheObjectIsActivated()
        {
            var go = NewObject("deferred");
            go.SetActive(false);

            var player = AddPlayer(go, MoveBy(1f));
            SetField(player, "playOnEnableId", MoveId);

            go.SetActive(true);
            Assert.IsTrue(player.IsPlaying(MoveId));

            yield return WaitUntilOrTimeout(() => !player.IsPlaying(MoveId));
            Assert.AreEqual(1f, go.transform.localPosition.x, Tolerance);
        }

        [UnityTest]
        public IEnumerator DisablingThePlayerStopsItsAnimations()
        {
            var go = NewObject("disabled");
            var player = AddPlayer(go, MoveBy(1f, 1f));

            player.Play(MoveId);
            yield return new WaitForSeconds(0.1f);

            go.SetActive(false);
            Assert.IsFalse(player.IsPlaying(MoveId));
            var stoppedAt = go.transform.localPosition;

            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(stoppedAt, go.transform.localPosition, "a stopped motion kept writing");
        }

        [UnityTest]
        public IEnumerator ARewindPutsTheObjectBackAfterPlayingForAWhile()
        {
            var animation = MoveBy(5f, 1f);
            animation.KillBehavior = TweenKillBehavior.Rewind;

            var go = NewObject("rewound");
            go.transform.localPosition = new Vector3(1f, 2f, 3f);
            var player = AddPlayer(go, animation);

            player.Play(MoveId);
            yield return new WaitForSeconds(0.2f);
            Assert.Greater(go.transform.localPosition.x, 1f, "it never moved, so the rewind proves nothing");

            player.Stop(MoveId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), go.transform.localPosition);
        }

        // --- Code-facing API on the game clock ---

        [UnityTest]
        public IEnumerator PausingFreezesAndResumingFinishes()
        {
            var go = NewObject("paused");
            var player = AddPlayer(go, MoveThenScale(2f));

            player.Play(MoveId);
            yield return new WaitForSeconds(0.1f);

            player.Pause(MoveId);
            var heldAt = go.transform.localPosition;
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(heldAt, go.transform.localPosition, "it moved while paused");
            Assert.IsTrue(player.IsPlaying(MoveId));

            player.Resume(MoveId);
            yield return WaitUntilOrTimeout(() => !player.IsPlaying(MoveId));
            Assert.AreEqual(2f, go.transform.localPosition.x, Tolerance);
        }

        [UnityTest]
        public IEnumerator PlayAsyncResolvesWhenThePlayEnds()
        {
            var go = NewObject("awaited");
            var player = AddPlayer(go, MoveBy(1f));

            var play = player.PlayAsync(MoveId);
            Assert.IsFalse(play.GetAwaiter().IsCompleted);

            yield return WaitUntilOrTimeout(() => play.GetAwaiter().IsCompleted);
            Assert.IsTrue(play.GetAwaiter().GetResult());
            Assert.AreEqual(1f, go.transform.localPosition.x, Tolerance);
        }

        [UnityTest]
        public IEnumerator DestroyingThePlayerResolvesItsAwaitedPlays()
        {
            var go = NewObject("destroyed");
            var player = AddPlayer(go, MoveBy(1f, 5f));

            var play = player.PlayAsync(MoveId);
            Object.Destroy(go);
            yield return null;

            Assert.IsTrue(play.GetAwaiter().IsCompleted, "an await on a destroyed player would hang");
            Assert.IsFalse(play.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator OnePlayerCanAnimateSeveralPooledObjectsAtOnce()
        {
            var player = AddPlayer(NewObject("player"), MoveBy(4f));
            var a = NewObject("pooled a");
            var b = NewObject("pooled b");

            player.Play(MoveId, a);
            yield return new WaitForSeconds(Duration / 2f);
            player.Play(MoveId, b);

            yield return WaitUntilOrTimeout(() => !player.IsPlayingAny());
            Assert.AreEqual(4f, a.transform.localPosition.x, Tolerance);
            Assert.AreEqual(4f, b.transform.localPosition.x, Tolerance);
        }

#if LMTE_SUPPORT_UGUI
        // --- TweenToggleable ---

        TweenToggleable NewPanel(out GameObject panel, out TweenPlayer player)
        {
            var show = Step(TweenType.Fade);
            show.FromCurrent = false;
            show.From = Vector4.zero;
            show.To = new Vector4(1f, 0f, 0f, 0f);

            var hide = Step(TweenType.Fade);
            hide.FromCurrent = false;
            hide.From = new Vector4(1f, 0f, 0f, 0f);
            hide.To = Vector4.zero;

            panel = NewObject("panel", typeof(RectTransform), typeof(CanvasGroup));
            player = AddPlayer(panel,
                Animation(TweenAnimationId.Show, show),
                Animation(TweenAnimationId.Hide, hide));

            return panel.AddComponent<TweenToggleable>();
        }

        [UnityTest]
        public IEnumerator HidingWaitsForTheAnimationBeforeDeactivating()
        {
            var toggleable = NewPanel(out var panel, out var player);
            var hidden = 0;
            toggleable.OnHidden.AddListener(() => hidden++);
            yield return null;

            toggleable.Hide();
            yield return null;

            Assert.IsTrue(panel.activeSelf, "deactivated before the hide animation could run");
            Assert.IsTrue(player.IsPlaying(TweenAnimationId.Hide));
            Assert.AreEqual(0, hidden);

            yield return WaitUntilOrTimeout(() => !panel.activeSelf);
            Assert.AreEqual(1, hidden);
            Assert.AreEqual(0f, panel.GetComponent<CanvasGroup>().alpha, Tolerance);
        }

        [UnityTest]
        public IEnumerator ShowingDuringAHideKeepsThePanelUp()
        {
            var toggleable = NewPanel(out var panel, out _);
            yield return null;

            toggleable.Hide();
            yield return new WaitForSeconds(Duration / 4f);
            toggleable.Show();

            yield return new WaitForSeconds(Duration * 2f);
            Assert.IsTrue(panel.activeSelf, "the interrupted hide still deactivated the panel");
            Assert.IsTrue(toggleable.IsVisible);
            Assert.AreEqual(1f, panel.GetComponent<CanvasGroup>().alpha, Tolerance);
        }

        // --- TweenButton ---

        /// <summary>A button whose animations count their own plays through a callback step.</summary>
        TweenButton NewButton(Dictionary<string, int> plays)
        {
            var go = NewObject("button", typeof(RectTransform));

            TweenAnimation Counting(string id)
            {
                plays[id] = 0;
                var marker = Step(TweenType.Callback, 0f);
                marker.OnCallback.AddListener(() => plays[id]++);
                var animation = Animation(id, marker);
                animation.BlendMode = TweenBlendMode.Additive;
                return animation;
            }

            AddPlayer(go,
                Counting(TweenAnimationId.Hover),
                Counting(TweenAnimationId.Unhover),
                Counting(TweenAnimationId.Press),
                Counting(TweenAnimationId.Click));

            return go.AddComponent<TweenButton>();
        }

        static PointerEventData LeftClick()
        {
            return new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        }

        [UnityTest]
        public IEnumerator AButtonPlaysAnAnimationForEachInteraction()
        {
            var plays = new Dictionary<string, int>();
            var button = NewButton(plays);
            yield return null;

            button.OnPointerEnter(LeftClick());
            yield return null;
            Assert.AreEqual(1, plays[TweenAnimationId.Hover], "hover");

            button.OnPointerDown(LeftClick());
            yield return null;
            Assert.AreEqual(1, plays[TweenAnimationId.Press], "press");

            button.OnPointerUp(LeftClick());
            button.OnPointerClick(LeftClick());
            yield return null;
            Assert.AreEqual(1, plays[TweenAnimationId.Click], "click");

            button.OnPointerExit(LeftClick());
            yield return null;
            Assert.AreEqual(1, plays[TweenAnimationId.Unhover], "unhover");
        }

        [UnityTest]
        public IEnumerator AButtonDoesNotReplayAStateItIsAlreadyIn()
        {
            var plays = new Dictionary<string, int>();
            var button = NewButton(plays);
            yield return null;

            button.OnPointerEnter(LeftClick());
            button.OnPointerEnter(LeftClick());
            yield return null;

            Assert.AreEqual(1, plays[TweenAnimationId.Hover], "Unity re-reported the state and it replayed");
        }

        [UnityTest]
        public IEnumerator ANonInteractableButtonDoesNotPlayClick()
        {
            var plays = new Dictionary<string, int>();
            var button = NewButton(plays);
            yield return null;

            button.interactable = false;
            button.OnPointerClick(LeftClick());
            yield return null;

            Assert.AreEqual(0, plays[TweenAnimationId.Click]);
        }

        // --- Materials ---

        [UnityTest]
        public IEnumerator AGraphicGetsItsOwnMaterialThatDiesWithIt()
        {
            // A UI shader: uGUI sets _MainTex on whatever a Graphic renders with.
            var asset = Track(new Material(Shader.Find("UI/Default")));
            asset.SetColor("_Color", Color.white);

            var go = NewObject("image", typeof(RectTransform), typeof(Image));
            var image = go.GetComponent<Image>();
            image.material = asset;

            var step = Step(TweenType.MaterialProperty);
            step.MaterialPropertyKind = TweenMaterialPropertyKind.Color;
            step.PropertyName = "_Color";
            step.FromCurrent = false;
            step.FromColor = Color.white;
            step.ToColor = Color.red;

            var player = AddPlayer(go, Animation(MoveId, step));
            player.Play(MoveId);
            yield return WaitUntilOrTimeout(() => !player.IsPlaying(MoveId));

            var copy = image.material;
            Assert.AreNotSame(asset, copy);
            Assert.AreEqual(Color.white, asset.GetColor("_Color"), "the material asset was written");
            Assert.AreEqual(Color.red, copy.GetColor("_Color"));

            Object.Destroy(go);
            yield return null;
            Assert.IsTrue(copy == null, "the copy outlived its Graphic");
        }
#endif

        [UnityTest]
        public IEnumerator ARendererGetsItsOwnMaterialThatDiesWithIt()
        {
            var asset = Track(new Material(Shader.Find("Unlit/Color")));
            asset.SetColor("_Color", Color.white);

            var go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = asset;

            var step = Step(TweenType.MaterialProperty);
            step.MaterialPropertyKind = TweenMaterialPropertyKind.Color;
            step.PropertyName = "_Color";
            step.FromCurrent = false;
            step.FromColor = Color.white;
            step.ToColor = Color.red;

            var player = AddPlayer(go, Animation(MoveId, step));
            player.Play(MoveId);
            yield return WaitUntilOrTimeout(() => !player.IsPlaying(MoveId));

            var instance = renderer.sharedMaterial;
            Assert.AreNotSame(asset, instance);
            Assert.AreEqual(Color.white, asset.GetColor("_Color"), "the shared material was written");
            Assert.AreEqual(Color.red, instance.GetColor("_Color"));

            Object.Destroy(go);
            yield return null;
            Assert.IsTrue(instance == null, "the instance outlived its renderer");
        }
    }
}
