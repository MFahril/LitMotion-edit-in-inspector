using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using LitMotion.TweenEditor.Editor;

namespace LitMotion.TweenEditor.Tests
{
    /// <summary>
    /// Covers the shared authoring view in each of its hosts: that each builds, that the clip
    /// inspector's fields are actually bound, that every button explains itself, and that two
    /// views never preview at once.
    /// </summary>
    public sealed class TweenAuthoringViewTests
    {
        GameObject owner;
        TweenPlayer player;
        readonly List<TweenAuthoringView> views = new();
        readonly List<Object> created = new();

        [SetUp]
        public void SetUp()
        {
            owner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owner.transform.localPosition = new Vector3(1f, 2f, 3f);
            player = owner.AddComponent<TweenPlayer>();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < views.Count; i++) views[i].Dispose();
            views.Clear();

            for (var i = 0; i < created.Count; i++)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            if (owner != null) Object.DestroyImmediate(owner);
        }

        void SetAnimations(params TweenAnimation[] animations)
        {
            var field = typeof(TweenPlayer).GetField("animations",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(player, new List<TweenAnimation>(animations));
        }

        static TweenAnimation Animation(string id, params TweenType[] types)
        {
            var animation = new TweenAnimation { Id = id };
            animation.Steps.Clear();

            for (var i = 0; i < types.Length; i++)
            {
                var step = new TweenStep();
                TweenStepDefaults.Apply(step, types[i], i * 0.3f);
                animation.Steps.Add(step);
            }

            return animation;
        }

        TweenAuthoringView PlayerView(TweenAuthoringView.Layout layout = TweenAuthoringView.Layout.Narrow)
        {
            var view = new TweenAuthoringView(new TweenPlayerSource(new SerializedObject(player)), layout, "Test");
            views.Add(view);
            return view;
        }

        static List<T> All<T>(VisualElement root) where T : VisualElement
        {
            var found = new List<T>();
            var stack = new Stack<VisualElement>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var element = stack.Pop();
                if (element is T match) found.Add(match);

                for (var i = 0; i < element.hierarchy.childCount; i++) stack.Push(element.hierarchy[i]);
            }

            return found;
        }

        // --- Building ---

        [Test]
        public void ThePlayerViewBuildsAChipPerAnimationAndAClipPerStep()
        {
            SetAnimations(Animation("Show", TweenType.Move, TweenType.Fade), Animation("Hide", TweenType.Scale));

            var view = PlayerView();

            Assert.AreEqual(1, All<TweenAnimationBar>(view).Count);
            Assert.AreEqual(2, All<TweenClipElement>(view).Count, "the first animation's two steps");

            view.SelectedAnimationIndex = 1;
            Assert.AreEqual(1, All<TweenClipElement>(view).Count, "switching shows the other animation");
        }

        [Test]
        public void TheWideLayoutSplitsTimelineAndInspector()
        {
            SetAnimations(Animation("Show", TweenType.Move));

            var view = PlayerView(TweenAuthoringView.Layout.Wide);

            Assert.AreEqual(1, All<TwoPaneSplitView>(view).Count);
            Assert.AreEqual(1, All<TweenClipInspector>(view).Count);
        }

        [Test]
        public void ThePlayerInspectorBuildsThroughTheView()
        {
            SetAnimations(Animation("Show", TweenType.Move, TweenType.Rotate));

            var editor = UnityEditor.Editor.CreateEditor(player);
            created.Add(editor);

            var root = editor.CreateInspectorGUI();

            Assert.AreEqual(1, All<TweenAuthoringView>(root).Count);
            Assert.AreEqual(2, All<TweenClipElement>(root).Count);
        }

        [Test]
        public void ThePresetInspectorBuildsWithAPreviewTargetField()
        {
            var asset = ScriptableObject.CreateInstance<TweenAnimationAsset>();
            asset.SetAnimation(Animation("Pop", TweenType.Scale));
            created.Add(asset);

            var editor = UnityEditor.Editor.CreateEditor(asset);
            created.Add(editor);

            var root = editor.CreateInspectorGUI();

            Assert.AreEqual(1, All<TweenAuthoringView>(root).Count);
            Assert.AreEqual(0, All<TweenAnimationBar>(root).Count, "a preset has one animation, so no chips");
            Assert.IsTrue(All<ObjectField>(root).Exists(field => field.label == "Preview On"));
        }

        [Test]
        public void TheWindowOpensOnAPlayerAndOnAPreset()
        {
            SetAnimations(Animation("Show", TweenType.Move));

            var window = TweenEditorWindow.Open(player);
            try
            {
                Assert.AreEqual(player, window.Target);
                Assert.IsNotNull(window.View);
                Assert.AreEqual(1, All<TweenClipElement>(window.rootVisualElement).Count);

                var asset = ScriptableObject.CreateInstance<TweenAnimationAsset>();
                asset.SetAnimation(Animation("Pop", TweenType.Scale, TweenType.Fade));
                created.Add(asset);

                window.SetTarget(asset);
                Assert.AreEqual(asset, window.Target);
                Assert.AreEqual(2, All<TweenClipElement>(window.rootVisualElement).Count);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void AnEmptyPlayerShowsAnEmptyStateWithActions()
        {
            SetAnimations();

            var view = PlayerView();
            var labels = All<Label>(view).ConvertAll(label => label.text);

            CollectionAssert.Contains(labels, "This player has no animations yet.");
            Assert.IsTrue(All<Button>(view).Exists(button => button.text == "Add Animation"));
        }

        // --- The clip inspector ---

        [Test]
        public void SelectedClipFieldsAreBoundToTheStep()
        {
            // Fields added to an already-bound hierarchy are not bound by that earlier bind, so
            // the inspector has to bind its own. Unbound PropertyFields have no children and
            // show nothing at all. Binding needs a panel, hence the real window.
            var animation = Animation("Show", TweenType.Move);
            animation.Steps[0].Duration = 0.75f;
            SetAnimations(animation);

            var window = TweenEditorWindow.Open(player);
            try
            {
                window.View.SelectStep(0);

                var fields = All<PropertyField>(window.View.ClipInspector);
                Assert.IsNotEmpty(fields);

                for (var i = 0; i < fields.Count; i++)
                {
                    Assert.Greater(fields[i].childCount, 0, fields[i].bindingPath + " is not bound");
                }

                var duration = fields.Find(field => field.bindingPath.EndsWith("Duration"));
                Assert.IsNotNull(duration);
                Assert.AreEqual(0.75f, All<FloatField>(duration)[0].value, 1e-5f);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void SelectingClipsRepeatedlyDoesNotRecurse()
        {
            // A PropertyField raises its change callback once when bound, and several of those
            // callbacks rebuild the panel; unguarded, binding would rebuild, which binds...
            SetAnimations(Animation("Show", TweenType.TextScramble, TweenType.Shake, TweenType.Move));

            var window = TweenEditorWindow.Open(player);
            try
            {
                for (var round = 0; round < 3; round++)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        Assert.DoesNotThrow(() => window.View.SelectStep(i));
                    }
                }

                Assert.AreEqual(2, window.View.Timeline.SelectedIndex);
            }
            finally
            {
                window.Close();
            }
        }

        [Test]
        public void EveryButtonHasATooltip()
        {
            var types = new List<TweenType>();
            foreach (TweenType type in System.Enum.GetValues(typeof(TweenType)))
            {
                if (type != TweenType.Extension) types.Add(type);
            }

            SetAnimations(Animation("Show", types.ToArray()));
            var view = PlayerView(TweenAuthoringView.Layout.Wide);

            var missing = new List<string>();

            // Every type, since each one shows a different set of controls.
            for (var i = 0; i < types.Count; i++)
            {
                view.SelectStep(i);

                foreach (var button in All<Button>(view))
                {
                    if (string.IsNullOrEmpty(button.tooltip)) missing.Add(types[i] + ": '" + button.text + "'");
                }
            }

            CollectionAssert.IsEmpty(missing, string.Join("\n", missing));
        }

        // --- Preview ---

        [Test]
        public void PlayStartsAPreviewAndDisposeRestoresTheScene()
        {
            var animation = Animation("Show", TweenType.Move);
            SetAnimations(animation);

            var view = PlayerView();
            view.TogglePlay();

            Assert.IsTrue(view.Preview.IsActive);

            view.Preview.Pause();
            view.Preview.Scrub(animation.Steps[0].Duration);
            Assert.AreNotEqual(new Vector3(1f, 2f, 3f), owner.transform.localPosition, "the preview moved it");

            view.Dispose();

            Assert.IsFalse(view.Preview.IsActive);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), owner.transform.localPosition);
        }

        [Test]
        public void StartingASecondPreviewStopsTheFirst()
        {
            // Two previews on one object would each snapshot the other's half-animated pose and
            // restore the wrong values.
            SetAnimations(Animation("Show", TweenType.Move));

            var first = PlayerView();
            var second = PlayerView();

            first.TogglePlay();
            Assert.IsTrue(first.Preview.IsActive);

            second.TogglePlay();
            Assert.IsTrue(second.Preview.IsActive);
            Assert.IsFalse(first.Preview.IsActive, "the first view's preview was stopped and restored");

            second.StopPreview();
            Assert.AreEqual(new Vector3(1f, 2f, 3f), owner.transform.localPosition);
        }

        [Test]
        public void TheStatusReportFollowsTheSelectedAnimation()
        {
            SetAnimations(Animation("Show", TweenType.Move), Animation("Broken", TweenType.Fade));

            var view = PlayerView();
            Assert.AreEqual(0, view.Report.Failing);

            view.SelectedAnimationIndex = 1;
            Assert.AreEqual(1, view.Report.Failing, "a Fade on a 3D cube cannot bind");
        }
    }
}
