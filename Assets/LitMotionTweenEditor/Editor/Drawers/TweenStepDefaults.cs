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
    /// Defaults aim to be immediately visible when previewed: a new step should animate something
    /// the moment it is added, so the author can see it on the timeline and adjust from there.
    /// </remarks>
    internal static class TweenStepDefaults
    {
        /// <summary>Resets <paramref name="step"/> to defaults for <paramref name="type"/>.</summary>
        /// <param name="step">Step to overwrite.</param>
        /// <param name="type">Type to configure for.</param>
        /// <param name="startTime">Where to place it on the timeline, normally after the last step.</param>
        public static void Apply(TweenStep step, TweenType type, float startTime)
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

            ApplyTypeSpecifics(step, type);
        }

        static void ApplyTypeSpecifics(TweenStep step, TweenType type)
        {
            switch (type)
            {
                case TweenType.Move:
                    // Relative, so it reads as "move 100 units right" regardless of where it starts.
                    step.Relative = true;
                    step.To = new Vector4(100f, 0f, 0f, 0f);
                    break;

                case TweenType.Scale:
                    step.UniformScale = true;
                    step.Relative = false;
                    step.To = new Vector4(1.2f, 1.2f, 1.2f, 0f);
                    step.Ease = Ease.OutBack;
                    break;

                case TweenType.Rotate:
                    step.Relative = true;
                    step.To = new Vector4(0f, 0f, 180f, 0f);
                    break;

                case TweenType.Jump:
                    step.Relative = true;
                    step.To = new Vector4(100f, 0f, 0f, 0f);
                    step.JumpPower = 60f;
                    // The arc follows eased progress, so linear gives a true parabola.
                    step.Ease = Ease.Linear;
                    step.Duration = 0.6f;
                    break;

                case TweenType.Punch:
                    // Punch and Shake read the end value as a strength, not a destination.
                    step.To = new Vector4(20f, 0f, 0f, 0f);
                    step.Ease = Ease.Linear;
                    step.Duration = 0.4f;
                    break;

                case TweenType.Shake:
                    step.To = new Vector4(12f, 12f, 0f, 0f);
                    step.Ease = Ease.Linear;
                    step.Duration = 0.4f;
                    break;

                case TweenType.SizeDelta:
                    step.Relative = true;
                    step.To = new Vector4(50f, 0f, 0f, 0f);
                    break;

                case TweenType.Pivot:
                case TweenType.Anchors:
                    step.FromCurrent = true;
                    step.To = new Vector4(0.5f, 0.5f, 0f, 0f);
                    break;

                case TweenType.Fade:
                    // Fade out is the common case and is obvious on screen.
                    step.FromCurrent = true;
                    step.To = Vector4.zero;
                    step.Ease = Ease.OutQuad;
                    break;

                case TweenType.Color:
                    step.FromCurrent = true;
                    step.ToColor = Color.white;
                    break;

                case TweenType.FillAmount:
                    step.FromCurrent = true;
                    step.To = new Vector4(1f, 0f, 0f, 0f);
                    break;

                case TweenType.CameraProperty:
                    step.FromCurrent = true;
                    step.To = new Vector4(60f, 0f, 0f, 0f);
                    break;

                case TweenType.AudioVolume:
                    step.FromCurrent = true;
                    step.To = Vector4.zero;
                    break;

                case TweenType.AudioPitch:
                    step.FromCurrent = true;
                    step.To = new Vector4(1f, 0f, 0f, 0f);
                    break;

                case TweenType.VolumeWeight:
                    step.FromCurrent = true;
                    step.To = new Vector4(1f, 0f, 0f, 0f);
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
                    step.To = new Vector4(0f, 20f, 0f, 0f);
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
