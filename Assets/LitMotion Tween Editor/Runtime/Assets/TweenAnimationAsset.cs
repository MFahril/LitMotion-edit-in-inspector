using UnityEngine;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// A reusable tween animation stored as a project asset.
    /// </summary>
    /// <remarks>
    /// The same <see cref="TweenAnimation"/> a player holds inline, saved once and referenced
    /// from many objects. Steps in an asset cannot point at a specific scene object -- an asset
    /// has no way to reference one -- so their Target is left empty and resolves against
    /// whichever player runs them. That restriction is what makes a preset portable.
    /// </remarks>
    [CreateAssetMenu(menuName = "LitMotion/Tween Animation", fileName = "TweenAnimation")]
    public sealed class TweenAnimationAsset : ScriptableObject
    {
        [SerializeField] TweenAnimation animation = new();

        /// <summary>The animation this asset holds.</summary>
        public TweenAnimation Animation => animation;

        /// <summary>The id this animation is played by.</summary>
        public string Id => animation == null ? string.Empty : animation.Id;

        /// <summary>Replaces the stored animation with a copy of <paramref name="source"/>.</summary>
        /// <remarks>
        /// Copies rather than aliases, so later edits to the player's inline animation do not
        /// silently rewrite the saved asset.
        /// </remarks>
        public void SetAnimation(TweenAnimation source)
        {
            animation = source?.Clone() ?? new TweenAnimation();
        }
    }
}
