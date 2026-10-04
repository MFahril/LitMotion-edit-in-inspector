using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// A tween channel defined outside this package: something a step can animate that none of
    /// the built-in <see cref="TweenType"/>s cover.
    /// </summary>
    /// <remarks>
    /// Implement this, mark the class with <see cref="TweenExtensionChannelAttribute"/>, and the
    /// channel appears in the timeline's add menu under its category. A step using it has
    /// <see cref="TweenType.Extension"/> as its type and the attribute's id in
    /// <see cref="TweenStep.ExtensionId"/>.
    ///
    /// Everything that works for a built-in channel works here, because an extension routes
    /// through the same builder, snapshot and inspector: easing, looping, delay, the four value
    /// modes, axis masking, grab and apply, preview and exact restore.
    ///
    /// <b>Reading is not optional.</b> The editor preview restores every value it wrote when it
    /// stops, and it can only do that for a channel it can read. A channel that cannot read
    /// would leave the scene changed after every preview, so <see cref="TryRead"/> is required.
    ///
    /// Every value is carried as a <see cref="Vector4"/>, exactly as the built-in channels do:
    /// a float lives in X, a Vector2 in XY, a Vector3 in XYZ, and a colour as RGBA. The
    /// <see cref="Shape"/> decides which widget the inspector draws and which interpolation
    /// LitMotion runs.
    ///
    /// Instances are created once, with a public parameterless constructor, and shared by every
    /// step that uses the channel. Keep them stateless. Under IL2CPP, add
    /// <c>[UnityEngine.Scripting.Preserve]</c> so code stripping does not remove a class that is
    /// only ever found by reflection, or register it explicitly with
    /// <see cref="TweenExtensionRegistry.Register"/>.
    /// </remarks>
    public interface ITweenExtensionChannel
    {
        /// <summary>Name shown on clips, in the add menu and in the inspector.</summary>
        string DisplayName { get; }

        /// <summary>The value this channel carries, which picks the widget and the interpolation.</summary>
        TweenValueShape Shape { get; }

        /// <summary>Unit suffix shown beside the value, such as "deg" or "px". May be empty.</summary>
        string Unit { get; }

        /// <summary>
        /// Finds the object this channel writes to.
        /// </summary>
        /// <param name="step">The step being resolved. Its <see cref="TweenStep.Target"/> may name
        /// a specific component.</param>
        /// <param name="gameObject">The GameObject the step targets: its own target's GameObject,
        /// or the player's when the step has none.</param>
        /// <param name="target">The object later passed to <see cref="TryRead"/> and
        /// <see cref="TryWrite"/>.</param>
        /// <param name="error">Why nothing resolved, when returning false. Phrase it as what is
        /// needed, e.g. "Requires a Light."</param>
        bool TryResolve(TweenStep step, GameObject gameObject, out Object target, out string error);

        /// <summary>Reads the live value. Unused components should be left at zero.</summary>
        bool TryRead(Object target, out Vector4 value);

        /// <summary>Writes a value. Called once per frame while the step runs.</summary>
        bool TryWrite(Object target, Vector4 value);
    }

    /// <summary>
    /// Registers an <see cref="ITweenExtensionChannel"/> under a stable id.
    /// </summary>
    /// <remarks>
    /// The id is persisted in every step that uses the channel, so treat it like a
    /// <see cref="TweenType"/> value: pick it once and never change it. A reverse-DNS style id
    /// ("com.studio.light-intensity") keeps two packages from colliding.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TweenExtensionChannelAttribute : Attribute
    {
        public TweenExtensionChannelAttribute(string id)
        {
            Id = id;
        }

        /// <summary>The id stored in <see cref="TweenStep.ExtensionId"/>.</summary>
        public string Id { get; }

        /// <summary>Add-menu group this channel is listed under. Slashes nest it further.</summary>
        public string Category { get; set; } = TweenExtensionRegistry.DefaultCategory;
    }
}
