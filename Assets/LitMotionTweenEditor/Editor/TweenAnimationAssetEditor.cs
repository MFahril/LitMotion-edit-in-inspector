using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Inspector for <see cref="TweenAnimationAsset"/>: the same authoring view the player uses,
    /// with a chosen scene object to preview against.
    /// </summary>
    /// <remarks>
    /// A preset is only as good as the ability to see it, and an asset has no object of its own
    /// to animate. So it borrows one: pick anything in the scene and the preview runs the real
    /// animation on it, restoring it afterwards exactly as a player preview does.
    /// </remarks>
    [CustomEditor(typeof(TweenAnimationAsset))]
    public sealed class TweenAnimationAssetEditor : UnityEditor.Editor
    {
        TweenAuthoringView view;

        void OnDisable()
        {
            view?.Dispose();
            view = null;
        }

        public override VisualElement CreateInspectorGUI()
        {
            view?.Dispose();

            var root = new VisualElement();

            var toolbar = TweenUi.Row();
            toolbar.style.justifyContent = Justify.FlexEnd;
            toolbar.Add(new Button(() => TweenEditorWindow.Open(target))
            {
                text = "Open in Window",
                tooltip = "Edit this preset in the dockable Tween Editor window",
                style = { fontSize = 10f },
            });
            root.Add(toolbar);

            view = new TweenAuthoringView(new TweenAssetSource(serializedObject), TweenAuthoringView.Layout.Narrow,
                "Inspector");
            root.Add(view);
            return root;
        }
    }
}
