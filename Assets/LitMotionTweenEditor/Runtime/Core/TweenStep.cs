using System;
using UnityEngine;
using UnityEngine.Events;

namespace LitMotion.TweenEditor
{
    /// <summary>A UnityEvent carrying normalized progress, used by <see cref="TweenType.Custom"/>.</summary>
    [Serializable]
    public sealed class TweenProgressEvent : UnityEvent<float> { }

    /// <summary>
    /// A single animated channel placed on an animation's timeline.
    /// </summary>
    /// <remarks>
    /// This is deliberately one flat type with a <see cref="TweenType"/> discriminator and a
    /// shared payload, rather than a polymorphic <c>[SerializeReference]</c> hierarchy. That
    /// keeps prefab overrides well behaved, makes copy/paste and preset serialization trivial,
    /// and avoids a managed allocation per step. Only the fields named by the active
    /// <see cref="Type"/> are read -- see <c>TweenStepBuilder</c> for the authoritative mapping.
    ///
    /// It is a class rather than a struct so that the field initializers below actually apply
    /// when Unity adds a new element in the inspector; a <c>List&lt;struct&gt;</c> element is
    /// zero-filled instead, which would make <see cref="Loops"/> 0 and <see cref="Duration"/> 0
    /// on every freshly added step.
    /// </remarks>
    [Serializable]
    public sealed class TweenStep
    {
        // ---- Identity and timeline placement ----

        public TweenType Type = TweenType.Move;

        [Tooltip("Optional label shown on the timeline clip. Falls back to the tween type.")]
        public string Label = string.Empty;

        [Tooltip("Disabled steps are skipped entirely and contribute no duration.")]
        public bool Enabled = true;

        [Tooltip("Offset from the start of the animation, in seconds.")]
        public float StartTime;

        [Tooltip("Length of this step in seconds, excluding Delay.")]
        public float Duration = 0.3f;

        // ---- Easing ----

        public Ease Ease = Ease.OutQuad;

        [Tooltip("Used only when Ease is CustomAnimationCurve.")]
        public AnimationCurve CustomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // ---- Per-step delay and looping ----

        public float Delay;
        public DelayType DelayType = DelayType.FirstLoop;

        [Tooltip("Number of times this step plays. Negative means loop forever.")]
        public int Loops = 1;

        public LoopType LoopType = LoopType.Restart;

        // ---- Target ----

        [Tooltip("Object to animate. Leave empty to target the TweenPlayer's own GameObject.")]
        public UnityEngine.Object Target;

        [Tooltip("Shader or material property name, for MaterialProperty steps.")]
        public string PropertyName = string.Empty;

        [Tooltip("Id of the extension channel an Extension step animates.")]
        public string ExtensionId = string.Empty;

        // ---- Value payload, interpreted per Type ----

        [Tooltip("Start value. Ignored when FromCurrent is enabled.")]
        public Vector4 From;

        [Tooltip("End value. Treated as an offset from the start value when Relative is enabled.")]
        public Vector4 To;

        public Color FromColor = UnityEngine.Color.white;
        public Color ToColor = UnityEngine.Color.white;

        [Tooltip("Capture the start value from the target's live value when the animation starts.")]
        public bool FromCurrent = true;

        [Tooltip("Treat the end value as an offset from the start value rather than an absolute.")]
        public bool Relative;

        [Tooltip("Start at the target's live value plus From, and finish on the live value. " +
                 "This is what makes a slide-in reusable: the destination is wherever the " +
                 "object already sits, so the same step works on any object.")]
        public bool FromOffset;

        // ---- Sub-options ----

        public TweenSpace Space = TweenSpace.Local;
        public TweenAxis Axis = TweenAxis.XYZ;
        public TweenChannel Channel = TweenChannel.Position;
        public TweenAnchorTarget AnchorTarget = TweenAnchorTarget.Both;
        public TweenMaterialPropertyKind MaterialPropertyKind = TweenMaterialPropertyKind.Float;
        public TweenCameraProperty CameraProperty = TweenCameraProperty.FieldOfView;
        public TweenTMPCharChannel TMPCharChannel = TweenTMPCharChannel.Position;

        [Tooltip("Animate scale uniformly from a single value instead of per-axis.")]
        public bool UniformScale;

        // ---- Vibration options (Punch, Shake, Jump) ----

        [Tooltip("Oscillations over the step's duration.")]
        public int Frequency = 10;

        [Tooltip("How quickly the oscillation decays. 1 fully damps by the end.")]
        public float DampingRatio = 1f;

        [Tooltip("Seed for Shake's randomization. Equal seeds reproduce identical shakes.")]
        public uint RandomSeed;

        [Tooltip("Number of arcs a Jump step performs.")]
        public int JumpCount = 1;

        [Tooltip("Peak height of a Jump step's arc, in the step's coordinate space.")]
        public float JumpPower = 1f;

        // ---- Text options ----

        [Tooltip("Format string for TextCounter, e.g. \"Score: {0:N0}\".")]
        public string TextFormat = "{0}";

        public TweenTextUnit TextUnit = TweenTextUnit.Characters;
        public ScrambleMode ScrambleMode = ScrambleMode.None;

        [Tooltip("Target string for a TextScramble step.")]
        public string TargetText = string.Empty;

        [Tooltip("Parse rich text tags while scrambling so they are not treated as characters. " +
                 "Off by default, matching LitMotion, because the tag-aware path also trims the " +
                 "outermost characters while the scramble is in flight.")]
        public bool RichText;

        [Tooltip("Characters a TextScramble step fills with when ScrambleMode is Custom.")]
        public string ScrambleChars = string.Empty;

        [Tooltip("Index of the character a TMPCharacter step animates. " +
                 "Use -1 to animate every character, offset by Stagger.")]
        public int CharacterIndex;

        [Tooltip("Delay added per character when a TMPCharacter step animates all of them, " +
                 "which is what turns a single motion into a wave.")]
        public float Stagger = 0.03f;

        // ---- Events ----

        [Tooltip("Invoked when a Callback step is reached.")]
        public UnityEvent OnCallback = new();

        [Tooltip("Receives normalized progress each frame, for Custom steps.")]
        public TweenProgressEvent OnProgress = new();

        /// <summary>
        /// Total time this step occupies on the timeline, including delay and loops.
        /// Returns <see cref="float.PositiveInfinity"/> for infinite loops.
        /// </summary>
        public float TotalDuration
        {
            get
            {
                if (!Enabled) return 0f;
                if (Loops < 0) return float.PositiveInfinity;

                var delayTotal = Delay * (DelayType == DelayType.EveryLoop ? Loops : 1);
                return delayTotal + Duration * Loops;
            }
        }

        /// <summary>End time of this step on the animation timeline.</summary>
        public float EndTime => StartTime + TotalDuration;

        /// <summary>Label for timeline and inspector display, never empty.</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Label)) return Label;
                if (Type != TweenType.Extension) return Type.ToString();

                // An extension step is named after its channel, or its raw id when the channel
                // is missing, so a broken step still says what it was meant to be.
                if (TweenExtensionRegistry.TryGet(ExtensionId, out var entry)) return entry.DisplayName;
                return string.IsNullOrEmpty(ExtensionId) ? "Extension" : ExtensionId;
            }
        }

        /// <summary>Creates an independent copy. Reference-typed members are deep-copied.</summary>
        public TweenStep Clone()
        {
            var clone = (TweenStep)MemberwiseClone();
            clone.CustomCurve = new AnimationCurve(CustomCurve?.keys ?? Array.Empty<Keyframe>());
            if (CustomCurve != null)
            {
                clone.CustomCurve.preWrapMode = CustomCurve.preWrapMode;
                clone.CustomCurve.postWrapMode = CustomCurve.postWrapMode;
            }

            // UnityEvent persistent listeners cannot be copied outside of SerializedProperty
            // plumbing, so a cloned step starts with empty events rather than aliasing the
            // source's. TweenStepClipboard copies events via SerializedProperty instead.
            clone.OnCallback = new UnityEvent();
            clone.OnProgress = new TweenProgressEvent();
            return clone;
        }
    }
}
