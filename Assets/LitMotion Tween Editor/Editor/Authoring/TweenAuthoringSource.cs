using UnityEditor;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Where a <see cref="TweenAuthoringView"/> gets its animations from: a player's list, or the
    /// single animation inside a preset asset.
    /// </summary>
    /// <remarks>
    /// The two used to be two editors that differed in about a dozen lines and duplicated the
    /// other three hundred. Everything that actually differs -- how many animations there are,
    /// where they live in the serialized data, and which object a preview runs against -- is
    /// what this class answers. The view does the rest once.
    /// </remarks>
    internal abstract class TweenAuthoringSource
    {
        protected TweenAuthoringSource(SerializedObject serializedObject)
        {
            SerializedObject = serializedObject;
        }

        /// <summary>The serialized object every edit goes through.</summary>
        public SerializedObject SerializedObject { get; }

        /// <summary>The component or asset being edited.</summary>
        public Object Target => SerializedObject?.targetObject;

        /// <summary>False once the target has been destroyed.</summary>
        public bool IsValid => SerializedObject != null && SerializedObject.targetObject != null;

        /// <summary>True when there is a list of animations to switch between and edit.</summary>
        public abstract bool SupportsMultipleAnimations { get; }

        /// <summary>
        /// True when the preview target is picked by the author rather than being the target's
        /// own GameObject. A preset has no object of its own to animate.
        /// </summary>
        public abstract bool BorrowsPreviewTarget { get; }

        /// <summary>
        /// True when the target can hold scene references, which decides whether event fields
        /// such as OnComplete are offered at all.
        /// </summary>
        public abstract bool AllowsSceneReferences { get; }

        public abstract int AnimationCount { get; }

        public abstract TweenAnimation AnimationAt(int index);

        public abstract SerializedProperty AnimationPropertyAt(int index);

        /// <summary>The array property holding the animations, or null when there is only one.</summary>
        public virtual SerializedProperty AnimationsArray => null;

        /// <summary>The GameObject steps without a target resolve against, and previews run on.</summary>
        public abstract GameObject PreviewTarget { get; set; }

        /// <summary>Name for window titles and notes.</summary>
        public virtual string DisplayName => Target == null ? "(missing)" : Target.name;
    }

    /// <summary>A <see cref="TweenPlayer"/>'s inline animations.</summary>
    internal sealed class TweenPlayerSource : TweenAuthoringSource
    {
        public TweenPlayerSource(SerializedObject serializedObject) : base(serializedObject) { }

        TweenPlayer Player => SerializedObject?.targetObject as TweenPlayer;

        public override bool SupportsMultipleAnimations => true;
        public override bool BorrowsPreviewTarget => false;
        public override bool AllowsSceneReferences => true;

        public override int AnimationCount => Player == null ? 0 : Player.Animations.Count;

        public override TweenAnimation AnimationAt(int index)
        {
            var player = Player;
            if (player == null || index < 0 || index >= player.Animations.Count) return null;
            return player.Animations[index];
        }

        public override SerializedProperty AnimationsArray => SerializedObject?.FindProperty("animations");

        public override SerializedProperty AnimationPropertyAt(int index)
        {
            var array = AnimationsArray;
            if (array == null || index < 0 || index >= array.arraySize) return null;
            return array.GetArrayElementAtIndex(index);
        }

        public override GameObject PreviewTarget
        {
            get => Player == null ? null : Player.gameObject;
            set { }
        }
    }

    /// <summary>The one animation inside a <see cref="TweenAnimationAsset"/>.</summary>
    internal sealed class TweenAssetSource : TweenAuthoringSource
    {
        // Remembered for the session and shared by every preset: picking the preview object
        // once and having every preset use it is the normal way to work through a library.
        static GameObject sharedPreviewTarget;

        public TweenAssetSource(SerializedObject serializedObject) : base(serializedObject) { }

        TweenAnimationAsset Asset => SerializedObject?.targetObject as TweenAnimationAsset;

        public override bool SupportsMultipleAnimations => false;
        public override bool BorrowsPreviewTarget => true;

        // An asset cannot reference a scene object, so a listener wired here is dropped on save.
        public override bool AllowsSceneReferences => false;

        public override int AnimationCount => Asset?.Animation == null ? 0 : 1;

        public override TweenAnimation AnimationAt(int index)
        {
            return index == 0 ? Asset?.Animation : null;
        }

        public override SerializedProperty AnimationPropertyAt(int index)
        {
            return index == 0 ? SerializedObject?.FindProperty("animation") : null;
        }

        public override GameObject PreviewTarget
        {
            get => sharedPreviewTarget;
            set => sharedPreviewTarget = value;
        }
    }
}
