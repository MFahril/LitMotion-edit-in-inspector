using UnityEditor;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Which inspector fields each <see cref="TweenType"/> actually uses.
    /// </summary>
    /// <remarks>
    /// One table, consulted by <see cref="TweenStepDrawer"/> and later by the timeline's clip
    /// inspector. Keeping it here rather than inline in the drawer means adding a tween type
    /// means editing exactly one visibility rule, not hunting through UI construction code.
    ///
    /// Plain mutable fields rather than init-only properties and <c>with</c> expressions,
    /// because Unity 6000.6 compiles at C# 9 where <c>with</c> on a struct is not available.
    /// </remarks>
    internal struct TweenStepFields
    {
        public bool Target;
        public bool Duration;
        public bool Ease;
        public bool Values;
        public bool Space;
        public bool Axis;
        public bool UniformScale;
        public bool Channel;
        public bool AnchorTarget;
        public bool CameraProperty;
        public bool PropertyName;
        public bool TMPCharacter;
        public bool Vibration;
        public bool RandomSeed;
        public bool Jump;
        public bool TextFormat;
        public bool TextUnit;
        public bool Scramble;
        public bool Callback;
        public bool Progress;
        public bool ExtensionId;

        /// <summary>
        /// True when the step can read its start value off the target.
        /// </summary>
        /// <remarks>
        /// False for the channels with nothing readable behind them: a formatted counter cannot
        /// recover its number from the string it printed, and per-character TMP state lives in
        /// LitMotion's animator rather than on the component. Showing the toggle there would
        /// offer a setting that silently does nothing.
        /// </remarks>
        public bool SupportsFromCurrent;

        /// <summary>
        /// True when the end value means "how hard", not "where to". Punch and Shake are built on
        /// LitMotion adapters that read it as an oscillation strength.
        /// </summary>
        public bool EndIsStrength;

        /// <summary>The visibility rules for one tween type.</summary>
        public static TweenStepFields For(TweenType type)
        {
            // Shared baseline: nearly every type is a timed, eased interpolation on a target.
            var fields = new TweenStepFields
            {
                Target = true,
                Duration = true,
                Ease = true,
                Values = true,
                SupportsFromCurrent = true,
            };

            switch (type)
            {
                case TweenType.Move:
                    fields.Space = true;
                    fields.Axis = true;
                    return fields;

                case TweenType.Scale:
                    fields.Axis = true;
                    fields.UniformScale = true;
                    return fields;

                case TweenType.Rotate:
                    fields.Space = true;
                    fields.Axis = true;
                    return fields;

                case TweenType.Jump:
                    fields.Space = true;
                    fields.Axis = true;
                    fields.Jump = true;
                    return fields;

                case TweenType.Punch:
                    fields.Axis = true;
                    fields.Channel = true;
                    fields.Vibration = true;
                    fields.EndIsStrength = true;
                    return fields;

                case TweenType.Shake:
                    fields.Axis = true;
                    fields.Channel = true;
                    fields.Vibration = true;
                    fields.RandomSeed = true;
                    fields.EndIsStrength = true;
                    return fields;

                case TweenType.SizeDelta:
                case TweenType.Pivot:
                    fields.Axis = true;
                    return fields;

                case TweenType.Anchors:
                    fields.Axis = true;
                    fields.AnchorTarget = true;
                    return fields;

                case TweenType.CameraProperty:
                    fields.CameraProperty = true;
                    return fields;

                case TweenType.MaterialProperty:
                    fields.PropertyName = true;
                    return fields;

                case TweenType.TMPCharacter:
                    // Per-character motions bypass the shared channel table, so axis masking
                    // and start-value capture do not reach them.
                    fields.TMPCharacter = true;
                    fields.SupportsFromCurrent = false;
                    return fields;

                case TweenType.TextReveal:
                    fields.TextUnit = true;
                    return fields;

                case TweenType.TextCounter:
                    fields.TextFormat = true;
                    fields.SupportsFromCurrent = false;
                    return fields;

                case TweenType.TextScramble:
                    fields.Scramble = true;
                    fields.Values = false;
                    return fields;

                // Timeline primitives carry almost nothing.
                case TweenType.Interval:
                    return new TweenStepFields { Duration = true };

                case TweenType.Callback:
                    return new TweenStepFields { Callback = true };

                case TweenType.Custom:
                    return new TweenStepFields { Duration = true, Ease = true, Progress = true };

                case TweenType.Extension:
                    // Axis chips only appear when the channel's shape has components to mask.
                    fields.Axis = true;
                    fields.ExtensionId = true;
                    return fields;

                // Plain scalar and colour channels need nothing beyond the baseline.
                case TweenType.Fade:
                case TweenType.FillAmount:
                case TweenType.VolumeWeight:
                case TweenType.AudioVolume:
                case TweenType.AudioPitch:
                case TweenType.Color:
                default:
                    return fields;
            }
        }

        /// <summary>
        /// True when the step's endpoints are colours rather than numeric vectors.
        /// </summary>
        /// <param name="type">The step's type.</param>
        /// <param name="cameraProperty">
        /// The step's camera property, needed because only one of the camera properties
        /// (background colour) is a colour.
        /// </param>
        /// <param name="materialKind">
        /// The step's material property kind, needed because a material property may be a
        /// float, a colour or a vector.
        /// </param>
        /// <param name="tmpCharChannel">
        /// The step's per-character channel, needed because only one of them is a colour.
        /// </param>
        public static bool UsesColor(TweenType type, SerializedProperty cameraProperty,
            SerializedProperty materialKind = null, SerializedProperty tmpCharChannel = null)
        {
            if (type == TweenType.Color) return true;

            if (type == TweenType.CameraProperty && cameraProperty != null)
            {
                return cameraProperty.intValue == (int)TweenEditor.TweenCameraProperty.BackgroundColor;
            }

            if (type == TweenType.MaterialProperty && materialKind != null)
            {
                return materialKind.intValue == (int)TweenMaterialPropertyKind.Color;
            }

            if (type == TweenType.TMPCharacter && tmpCharChannel != null)
            {
                return tmpCharChannel.intValue == (int)TweenTMPCharChannel.Color;
            }

            return false;
        }
    }
}
