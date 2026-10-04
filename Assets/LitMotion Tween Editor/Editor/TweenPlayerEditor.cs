using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Inspector for <see cref="TweenPlayer"/>: the shared <see cref="TweenAuthoringView"/>, plus
    /// the player's own settings.
    /// </summary>
    /// <remarks>
    /// Layout follows the authoring loop -- pick an animation, arrange clips on the timeline,
    /// tune the selected clip, scrub to check it. The step list is not shown as a flat array
    /// because arranging steps in time is the whole job, and a vertical list hides exactly the
    /// information that matters.
    /// </remarks>
    [CustomEditor(typeof(TweenPlayer))]
    [CanEditMultipleObjects]
    public sealed class TweenPlayerEditor : UnityEditor.Editor
    {
        TweenAuthoringView view;

        void OnDisable()
        {
            // Must run on every teardown path, or the previewed pose is left written into the
            // scene when the inspector goes away.
            view?.Dispose();
            view = null;
        }

        public override VisualElement CreateInspectorGUI()
        {
            // CreateInspectorGUI runs again on target reassignment; the old view must restore
            // the scene before it is dropped.
            view?.Dispose();
            view = null;

            var root = new VisualElement();

            if (targets.Length > 1)
            {
                // The timeline edits one animation at a time; multi-select has no single subject.
                root.Add(new HelpBox(
                    "Timeline editing is unavailable while multiple players are selected.",
                    HelpBoxMessageType.Info));
                root.Add(new PropertyField(serializedObject.FindProperty("animations")));
                return root;
            }

            var toolbar = TweenUi.Row();
            toolbar.style.justifyContent = Justify.FlexEnd;
            toolbar.Add(new Button(() => TweenEditorWindow.Open(target))
            {
                text = "Open in Window",
                tooltip = "Edit this player in the dockable Tween Editor window, with more room for the timeline",
                style = { fontSize = 10f },
            });
            root.Add(toolbar);

            view = new TweenAuthoringView(new TweenPlayerSource(serializedObject), TweenAuthoringView.Layout.Narrow,
                "Inspector");
            root.Add(view);

            root.Add(BuildPlayerSettings());
            return root;
        }

        VisualElement BuildPlayerSettings()
        {
            var foldout = new Foldout { text = "Player", value = false };
            foldout.Add(new PropertyField(serializedObject.FindProperty("playOnEnableId")));
            foldout.Add(new PropertyField(serializedObject.FindProperty("animationAssets")));
            foldout.Add(new PropertyField(serializedObject.FindProperty("logBindingWarnings")));
            return foldout;
        }
    }
}
