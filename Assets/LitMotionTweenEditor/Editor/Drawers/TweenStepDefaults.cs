using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Sensible starting values for a newly added step, per tween type.
    /// </summary>
    /// <remarks>
    /// Unity's <c>InsertArrayElementAtIndex</c> copies the preceding element rather than creating
    /// a fresh one, so a new step arrives carrying the previous step's values. Without this, a
    /// new Fade would inherit a Move's end vector and do nothing visible, which reads as a bug.
    ///
    /// Defaults aim to be immediately visible when previewed, and modest: a new step should do
    /// something the moment it is added, at a size that reads as a starting point rather than a
    /// surprise, so the author can see what the clip does and adjust from there. Every default
    /// therefore changes something on a freshly made object -- a colour that tweens to the white
    /// it already is, or a field of view to the 60 it already has, would look broken.
    ///
    /// Distances follow the target's units. A UI element is positioned in pixels and everything
    /// else in world units, and one number cannot suit both: 1 is a sensible move for a cube and an
    /// invisible one for a button. So distances are authored in world units and multiplied by
    /// <see cref="UiPixelsPerUnit"/> when the target lives on a canvas.
    /// </remarks>
    internal static class TweenStepDefaults
    {
        /// <summary>
        /// Pixels a UI default moves for each world unit a 3D default moves -- Unity's default
        /// sprite pixels-per-unit, so the two read as the same gesture.
        /// </summary>
        public const float UiPixelsPerUnit = 100f;

        /// <summary>Resets <paramref name="step"/> to defaults for <paramref name="type"/>.</summary>
        /// <param name="step">Step to overwrite.</param>
        /// <param name="type">Type to configure for.</param>
        /// <param name="startTime">Where to place it on the timeline, normally after the last step.</param>
        public static void Apply(TweenStep step, TweenType type, float startTime)
        {
            Apply(step, type, startTime, null);
        }

        /// <inheritdoc cref="Apply(TweenStep, TweenType, float)"/>
        /// <param name="target">
        /// The object the step will animate, which decides whether distances are in pixels or
        /// world units. Null means world units.
        /// </param>
        public static void Apply(TweenStep step, TweenType type, float startTime, GameObject target)
        {
            if (step == null) return;

            step.Type = type;
            step.Label = string.Empty;
            step.Enabled = true;
            step.StartTime = Mathf.Max(0f, startTime);
            step.Duration = 0.3f;
            step.Ease = Ease.OutQuad;
            step.CustomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            step.Delay = 0f;
            step.DelayType = DelayType.FirstLoop;
            step.Loops = 1;
            step.LoopType = LoopType.Restart;
            step.Target = null;
            step.PropertyName = string.Empty;

            // Which channel an extension step drives is its identity, not a tunable value, so a
            // reset keeps it. Any other type has no use for it.
            if (type != TweenType.Extension) step.ExtensionId = string.Empty;
            step.FromCurrent = true;
            step.Relative = false;
            step.FromOffset = false;
            step.From = Vector4.zero;
            step.To = Vector4.zero;
            step.FromColor = Color.white;
            step.ToColor = Color.white;
            step.Space = TweenSpace.Local;
            step.Axis = TweenAxis.XYZ;
            step.Channel = TweenChannel.Position;
            step.AnchorTarget = TweenAnchorTarget.Both;
            step.MaterialPropertyKind = TweenMaterialPropertyKind.Float;
            step.CameraProperty = TweenCameraProperty.FieldOfView;
            step.TMPCharChannel = TweenTMPCharChannel.Position;
            step.UniformScale = false;
            step.Frequency = 10;
            step.DampingRatio = 1f;
            step.RandomSeed = 0u;
            step.JumpCount = 1;
            step.JumpPower = 1f;
            step.TextFormat = "{0}";
            step.TextUnit = TweenTextUnit.Characters;
            step.ScrambleMode = ScrambleMode.None;
            step.ScrambleChars = string.Empty;
            step.RichText = false;
            step.TargetText = string.Empty;
            step.CharacterIndex = 0;
            step.Stagger = 0.03f;

            ApplyTypeSpecifics(step, type, DistanceScale(target));
        }

        /// <summary>
        /// Multiplier for distances on <paramref name="target"/>: <see cref="UiPixelsPerUnit"/>
        /// for a UI element, which is positioned in pixels, and 1 for anything else.
        /// </summary>
        public static float DistanceScale(GameObject target)
        {
            return IsUi(target) ? UiPixelsPerUnit : 1f;
        }

        /// <summary>True for an object laid out in a canvas, whose positions are pixels.</summary>
        /// <remarks>
        /// A RectTransform alone is not enough: a world-space TextMeshPro has one too, and is
        /// measured in world units.
        /// </remarks>
        static bool IsUi(GameObject target)
        {
            return target != null
                   && target.transform is RectTransform
                   && target.GetComponentInParent<Canvas>(true) != null;
        }

        static void ApplyTypeSpecifics(TweenStep step, TweenType type, float distance)
        {
            switch (type)
            {
                case TweenType.Move:
                    // Relative, so it reads as "move one unit right" wherever the object starts.
                    step.Relative = true;
                    step.To = new Vector4(1f * distance, 0f, 0f, 0f);
                    break;

                case TweenType.Scale:
                    step.UniformScale = true;
                    step.Relative = false;
                    step.To = new Vector4(1.2f, 1.2f, 1.2f, 0f);
                    step.Ease = Ease.OutBack;
                    break;

                case TweenType.Rotate:
                    // A quarter turn about Z, which reads the same on a 3D object and a UI element.
                    step.Relative = true;
                    step.To = new Vector4(0f, 0f, 90f, 0f);
                    break;

                case TweenType.Jump:
                    step.Relative = true;
                    step.To = new Vector4(1f * distance, 0f, 0f, 0f);
                    step.JumpPower = 0.5f * distance;
                    // The arc follows eased progress, so linear gives a true parabola.
                    step.Ease = Ease.Linear;
                    step.Duration = 0.6f;
                    break;

                case TweenType.Punch:
                    // Punch and Shake read the end value as a strength, not a destination.
                    step.To = new Vector4(0.25f * distance, 0f, 0f, 0f);
                    step.Ease = Ease.Linear;
                    step.Duration = 0.4f;
                    break;

                case TweenType.Shake:
                    step.To = new Vector4(0.1f * distance, 0.1f * distance, 0f, 0f);
                    step.Ease = Ease.Linear;
                    step.Duration = 0.4f;
                    break;

                case TweenType.SizeDelta:
                    // Only ever on a RectTransform, so always pixels.
                    step.Relative = true;
                    step.To = new Vector4(50f, 0f, 0f, 0f);
                    break;

                // A new RectTransform has a centred pivot and centred anchors, so (0.5, 0.5)
                // would change nothing.
                case TweenType.Pivot:
                    // To the left edge, which slides the element by half its width.
                    step.FromCurrent = true;
                    step.To = new Vector4(0f, 0.5f, 0f, 0f);
                    break;

                case TweenType.Anchors:
                    // To the top centre, the most common re-anchoring.
                    step.FromCurrent = true;
                    step.To = new Vector4(0.5f, 1f, 0f, 0f);
                    break;

                case TweenType.Fade:
                    // Fade out is the common case and is obvious on screen.
                    step.FromCurrent = true;
                    step.To = Vector4.zero;
                    step.Ease = Ease.OutQuad;
                    break;

                case TweenType.Color:
                    // A soft red: a new Graphic or sprite is white, so white would change nothing.
                    step.FromCurrent = true;
                    step.ToColor = new Color(1f, 0.4f, 0.4f, 1f);
                    break;

                case TweenType.FillAmount:
                    // A new Image is already full, so fill up from empty.
                    step.FromCurrent = false;
                    step.From = Vector4.zero;
                    step.To = new Vector4(1f, 0f, 0f, 0f);
                    break;

                case TweenType.CameraProperty:
                    // A gentle zoom in; a new camera's field of view is the 60 it would otherwise go to.
                    step.FromCurrent = true;
                    step.To = new Vector4(45f, 0f, 0f, 0f);
                    break;

                case TweenType.AudioVolume:
                    step.FromCurrent = true;
                    step.To = Vector4.zero;
                    break;

                case TweenType.AudioPitch:
                    // A new AudioSource already plays at pitch 1.
                    step.FromCurrent = true;
                    step.To = new Vector4(1.5f, 0f, 0f, 0f);
                    break;

                case TweenType.VolumeWeight:
                    // A new Volume already has full weight, so fade it out.
                    step.FromCurrent = true;
                    step.To = Vector4.zero;
                    break;

                case TweenType.MaterialProperty:
                    // No property name can be guessed, so start from the current value and let
                    // the author name the property; the step reports clearly until they do.
                    step.FromCurrent = true;
                    step.Duration = 0.5f;
                    step.To = new Vector4(1f, 0f, 0f, 0f);
                    break;

                case TweenType.TextReveal:
                    // Endpoints are a 0-1 fraction of the text, so a reveal keeps working when
                    // the string changes length.
                    step.FromCurrent = false;
                    step.From = Vector4.zero;
                    step.To = new Vector4(1f, 0f, 0f, 0f);
                    step.Duration = 0.6f;
                    step.Ease = Ease.Linear;
                    break;

                case TweenType.TextCounter:
                    step.FromCurrent = false;
                    step.From = Vector4.zero;
                    step.To = new Vector4(100f, 0f, 0f, 0f);
                    step.TextFormat = "{0:0}";
                    step.Duration = 0.8f;
                    step.Ease = Ease.OutQuad;
                    break;

                case TweenType.TextScramble:
                    step.FromCurrent = false;
                    step.TargetText = "Hello";
                    step.ScrambleMode = ScrambleMode.All;
                    step.Duration = 0.8f;
                    step.Ease = Ease.Linear;
                    break;

                case TweenType.TMPCharacter:
                    // Every character, offset in time, which is the wave this type exists for.
                    step.CharacterIndex = -1;
                    step.FromCurrent = false;
                    step.TMPCharChannel = TweenTMPCharChannel.Position;
                    step.From = Vector4.zero;
                    step.To = new Vector4(0f, 0.2f * distance, 0f, 0f);
                    step.Duration = 0.4f;
                    step.Stagger = 0.03f;
                    step.Loops = 2;
                    step.LoopType = LoopType.Yoyo;
                    step.Ease = Ease.OutSine;
                    break;

                case TweenType.Interval:
                    step.Duration = 0.2f;
                    step.Label = "Wait";
                    break;

                case TweenType.Callback:
                    step.Duration = 0f;
                    step.Label = "Event";
                    break;

                case TweenType.Custom:
                    step.Ease = Ease.Linear;
                    break;

                case TweenType.Extension:
                    // Nothing is known about a user channel's range, so start from the live value
                    // and head somewhere a float, vector or colour will visibly reach.
                    step.To = Vector4.one;
                    step.ToColor = Color.white;
                    break;
            }
        }
    }
}
