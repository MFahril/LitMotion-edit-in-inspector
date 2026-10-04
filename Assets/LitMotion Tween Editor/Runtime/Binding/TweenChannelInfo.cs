using UnityEngine;
using Object = UnityEngine.Object;

namespace LitMotion.TweenEditor
{
    /// <summary>The kind of value a channel carries, and therefore the editor widget it needs.</summary>
    public enum TweenValueShape
    {
        Float = 0,
        Vector2 = 1,
        Vector3 = 2,
        Vector4 = 3,
        Color = 4,
    }

    /// <summary>
    /// A public description of the channel a step writes: its value shape, what its components
    /// are called, and whether its live value can be read.
    /// </summary>
    /// <remarks>
    /// This exists because the inspector has to know that a Move step is three numbers called
    /// X, Y and Z while a Fade step is one number between 0 and 1. Without it the inspector
    /// falls back to drawing the raw <see cref="TweenStep.From"/> field, which is a
    /// <c>Vector4</c> whose fourth component is meaningless for most types.
    ///
    /// It is deliberately public rather than exposing <c>TweenChannelAccessor</c> to the editor
    /// assembly. The editor is not the only caller that needs this answer -- an extension
    /// channel living in a user's own assembly needs it too, and internals cannot be granted to
    /// code that does not exist yet. One public description keeps the channel key table private.
    /// </remarks>
    public readonly struct TweenChannelInfo
    {
        static readonly string[] AxisLabels = { "X", "Y", "Z", "W" };
        static readonly string[] ColorLabels = { "R", "G", "B", "A" };
        static readonly string[] SizeLabels = { "W", "H" };
        static readonly string[] NoLabels = { "" };

        readonly string[] labels;

        TweenChannelInfo(TweenValueShape shape, string channelName, string unit, bool supportsRead,
            bool isResolved, string[] componentLabels)
        {
            Shape = shape;
            ChannelName = channelName;
            Unit = unit;
            SupportsRead = supportsRead;
            IsResolved = isResolved;
            labels = componentLabels;
        }

        /// <summary>Which widget this channel needs.</summary>
        public TweenValueShape Shape { get; }

        /// <summary>Human-readable name of the property being written, for labels and tooltips.</summary>
        public string ChannelName { get; }

        /// <summary>Unit suffix to show beside the value, or empty.</summary>
        public string Unit { get; }

        /// <summary>
        /// True when the live value can be read back, which is what "from current" and the
        /// inspector's grab button depend on.
        /// </summary>
        public bool SupportsRead { get; }

        /// <summary>True when a real target resolved, rather than this being a type-only guess.</summary>
        public bool IsResolved { get; }

        /// <summary>How many components the value has.</summary>
        public int ComponentCount
        {
            get
            {
                switch (Shape)
                {
                    case TweenValueShape.Float: return 1;
                    case TweenValueShape.Vector2: return 2;
                    case TweenValueShape.Vector3: return 3;
                    default: return 4;
                }
            }
        }

        /// <summary>Label for one component, such as "X" or "R".</summary>
        public string ComponentLabel(int index)
        {
            var set = labels ?? NoLabels;
            return index >= 0 && index < set.Length ? set[index] : "";
        }

        /// <summary>
        /// Describes the channel <paramref name="step"/> writes, resolving its target against
        /// <paramref name="fallback"/>.
        /// </summary>
        public static TweenChannelInfo Describe(TweenStep step, GameObject fallback)
        {
            if (step == null) return Unknown();

            var resolved = TweenBindingResolver.IsNonBinding(step.Type)
                ? null
                : TweenBindingResolver.Resolve(step, fallback, out _);

            return Describe(step, resolved);
        }

        /// <summary>
        /// Describes the channel <paramref name="step"/> writes against an already-resolved
        /// target. Pass null to get the description implied by the step's type alone, which is
        /// what the inspector shows while a target is still missing.
        /// </summary>
        public static TweenChannelInfo Describe(TweenStep step, Object resolved)
        {
            if (step == null) return Unknown();

            // Shape comes from the step's own type and sub-options, never from the resolved
            // object. That way the inspector draws the right widget before a target is assigned,
            // and the widget does not change shape the moment one is.
            var shape = ShapeOf(step);
            var key = TweenChannelAccessor.GetChannelKey(step, resolved);
            var isResolved = key != TweenChannelKey.None;

            var supportsRead = isResolved
                ? TweenChannelAccessor.SupportsRead(key)
                : SupportsReadByType(step.Type);

            return new TweenChannelInfo(shape, NameOf(step, resolved), UnitOf(step, resolved),
                supportsRead, isResolved, LabelsFor(step, shape));
        }

        /// <summary>
        /// Reads the live value of the channel <paramref name="step"/> writes.
        /// </summary>
        /// <remarks>
        /// Backs the inspector's grab button and anything else that captures a pose. Unused
        /// components are left at zero; a colour arrives as RGBA.
        /// </remarks>
        public static bool TryRead(TweenStep step, GameObject fallback, out Vector4 value)
        {
            value = Vector4.zero;
            if (!TryResolveChannel(step, fallback, out var resolved, out var key)) return false;

            return TweenChannelAccessor.TryRead(key, resolved, TweenChannelContext.For(step), out value);
        }

        /// <summary>
        /// Writes <paramref name="value"/> straight to the channel <paramref name="step"/> writes,
        /// without creating a motion.
        /// </summary>
        /// <remarks>
        /// Backs the inspector's apply button, which exists so an author can see an endpoint on
        /// the object without playing anything. The whole channel is written, ignoring the step's
        /// axis mask, because this means "put the object here", not "animate these axes".
        /// </remarks>
        public static bool TryWrite(TweenStep step, GameObject fallback, Vector4 value)
        {
            if (!TryResolveChannel(step, fallback, out var resolved, out var key)) return false;

            return TweenChannelAccessor.TryWrite(key, resolved, value, TweenAxis.All,
                TweenChannelContext.For(step));
        }

        /// <summary>
        /// The material a <see cref="TweenType.MaterialProperty"/> step would write to, so the
        /// inspector can offer that shader's properties instead of asking for a typed name.
        /// </summary>
        public static Material ResolveMaterial(TweenStep step, GameObject fallback)
        {
            if (step == null || step.Type != TweenType.MaterialProperty) return null;

            var resolved = TweenBindingResolver.Resolve(step, fallback, out _);
            return resolved == null ? null : TweenChannelAccessor.ResolveMaterial(resolved);
        }

        static bool TryResolveChannel(TweenStep step, GameObject fallback, out Object resolved,
            out TweenChannelKey key)
        {
            resolved = null;
            key = TweenChannelKey.None;

            if (step == null || TweenBindingResolver.IsNonBinding(step.Type)) return false;

            resolved = TweenBindingResolver.Resolve(step, fallback, out _);
            if (resolved == null) return false;

            key = TweenChannelAccessor.GetChannelKey(step, resolved);
            return key != TweenChannelKey.None;
        }

        static TweenChannelInfo Unknown()
        {
            return new TweenChannelInfo(TweenValueShape.Float, "", "", false, false, NoLabels);
        }

        /// <summary>The value shape implied by a step's type and sub-options.</summary>
        static TweenValueShape ShapeOf(TweenStep step)
        {
            switch (step.Type)
            {
                case TweenType.Move:
                case TweenType.Rotate:
                case TweenType.Scale:
                case TweenType.Jump:
                case TweenType.Punch:
                case TweenType.Shake:
                    return TweenValueShape.Vector3;

                case TweenType.SizeDelta:
                case TweenType.Pivot:
                case TweenType.Anchors:
                    return TweenValueShape.Vector2;

                case TweenType.Color:
                    return TweenValueShape.Color;

                case TweenType.CameraProperty:
                    return step.CameraProperty == TweenCameraProperty.BackgroundColor
                        ? TweenValueShape.Color
                        : TweenValueShape.Float;

                case TweenType.MaterialProperty:
                    switch (step.MaterialPropertyKind)
                    {
                        case TweenMaterialPropertyKind.Color: return TweenValueShape.Color;
                        case TweenMaterialPropertyKind.Vector: return TweenValueShape.Vector4;
                        default: return TweenValueShape.Float;
                    }

                case TweenType.TMPCharacter:
                    switch (step.TMPCharChannel)
                    {
                        case TweenTMPCharChannel.Color: return TweenValueShape.Color;
                        case TweenTMPCharChannel.Alpha: return TweenValueShape.Float;
                        default: return TweenValueShape.Vector3;
                    }

                case TweenType.Extension:
                    return TweenExtensionRegistry.Find(step.ExtensionId)?.Shape ?? TweenValueShape.Float;

                default:
                    // Fade, FillAmount, VolumeWeight, the audio channels, text reveal and the
                    // counter are all single numbers; the primitives carry no value at all.
                    return TweenValueShape.Float;
            }
        }

        static string[] LabelsFor(TweenStep step, TweenValueShape shape)
        {
            if (shape == TweenValueShape.Color) return ColorLabels;
            if (shape == TweenValueShape.Float) return NoLabels;
            if (step.Type == TweenType.SizeDelta) return SizeLabels;

            return AxisLabels;
        }

        /// <summary>
        /// Mirrors the channel table for the cases where nothing has resolved yet, so the
        /// inspector hides "from current" on a counter even before a target is assigned.
        /// </summary>
        static bool SupportsReadByType(TweenType type)
        {
            return type is not (TweenType.TextCounter
                or TweenType.TextScramble
                or TweenType.TMPCharacter
                or TweenType.Interval
                or TweenType.Callback
                or TweenType.Custom);
        }

        static string NameOf(TweenStep step, Object resolved)
        {
            var isRect = resolved is RectTransform;

            switch (step.Type)
            {
                case TweenType.Move:
                    if (step.Space == TweenSpace.World) return "World Position";
                    return isRect ? "Anchored Position" : "Local Position";

                case TweenType.Jump:
                    return isRect ? "Anchored Position" : "Local Position";

                case TweenType.Rotate:
                    return step.Space == TweenSpace.World ? "World Euler Angles" : "Local Euler Angles";

                case TweenType.Scale:
                    return "Local Scale";

                case TweenType.Punch:
                case TweenType.Shake:
                    return step.Channel.ToString();

                case TweenType.Fade:
                    return resolved is CanvasGroup ? "Canvas Group Alpha" : "Alpha";

                case TweenType.CameraProperty:
                    return Spaced(step.CameraProperty.ToString());

                case TweenType.MaterialProperty:
                    return string.IsNullOrEmpty(step.PropertyName)
                        ? "Material Property"
                        : step.PropertyName;

                case TweenType.TMPCharacter:
                    return "Character " + step.TMPCharChannel;

                case TweenType.TextReveal:
                    return "Visible " + step.TextUnit;

                case TweenType.Extension:
                    return step.DisplayName;

                default:
                    return Spaced(step.Type.ToString());
            }
        }

        /// <summary>
        /// Splits a PascalCase name into words. <c>ObjectNames.NicifyVariableName</c> would do
        /// this, but it lives in UnityEditor and this type is runtime code.
        /// </summary>
        static string Spaced(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var builder = new System.Text.StringBuilder(text.Length + 4);
            for (var i = 0; i < text.Length; i++)
            {
                if (i > 0 && char.IsUpper(text[i]) && !char.IsUpper(text[i - 1])) builder.Append(' ');
                builder.Append(text[i]);
            }

            return builder.ToString();
        }

        static string UnitOf(TweenStep step, Object resolved)
        {
            switch (step.Type)
            {
                case TweenType.Rotate:
                    return "deg";

                case TweenType.Scale:
                case TweenType.AudioPitch:
                    return "x";

                case TweenType.Move:
                case TweenType.Jump:
                    return resolved is RectTransform ? "px" : "";

                case TweenType.SizeDelta:
                    return "px";

                case TweenType.Fade:
                case TweenType.FillAmount:
                case TweenType.VolumeWeight:
                case TweenType.AudioVolume:
                case TweenType.TextReveal:
                    return "0-1";

                case TweenType.CameraProperty:
                    return step.CameraProperty == TweenCameraProperty.FieldOfView ? "deg" : "";

                case TweenType.Punch:
                case TweenType.Shake:
                    return step.Channel == TweenChannel.Rotation ? "deg" : "";

                case TweenType.Extension:
                    return TweenExtensionRegistry.Find(step.ExtensionId)?.Unit ?? "";

                default:
                    return "";
            }
        }
    }
}
