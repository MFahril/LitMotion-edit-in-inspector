using System;
using UnityEditor;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// What a clip's colour says about it: the part of the object a tween type affects.
    /// </summary>
    internal enum TweenFamily
    {
        Transform = 0,
        Vibration = 1,
        RectTransform = 2,
        Graphics = 3,
        Text = 4,
        Rendering = 5,
        Camera = 6,
        Audio = 7,
        Timeline = 8,
        Extension = 9,
    }

    /// <summary>
    /// The author's preferences for the tween editor, shown under
    /// <b>Preferences → LitMotion Tween Editor</b>.
    /// </summary>
    /// <remarks>
    /// Stored in the user's preferences folder rather than the project, because these are
    /// matters of taste -- zoom range, colours, whether the preview follows the selection -- and
    /// one author changing them should not rewrite them for the whole team.
    ///
    /// Every default here is the constant the timeline shipped with before this existed, so
    /// nothing looks different until someone changes something. <see cref="TweenTimelineStyles"/>
    /// reads through this class; nothing else should hold a copy of these numbers.
    /// </remarks>
    [FilePath("LitMotionTweenEditor/Settings.asset", FilePathAttribute.Location.PreferencesFolder)]
    internal sealed class TweenEditorSettings : ScriptableSingleton<TweenEditorSettings>
    {
        // Former TweenTimelineStyles constants, kept as the defaults.
        internal const float DefaultTrackHeight = 22f;
        internal const float DefaultMinPixelsPerSecond = 40f;
        internal const float DefaultMaxPixelsPerSecond = 1600f;
        internal const float DefaultZoom = 220f;
        internal const float DefaultSnapInterval = 0.05f;
        internal const float DefaultSnapPixelThreshold = 6f;
        internal const float DefaultMaxTimelineHeight = 320f;
        internal const float DefaultStepDuration = 0.3f;
        internal const Ease DefaultStepEase = Ease.OutQuad;

        [Header("New steps")]
        [Tooltip("Length given to a step when it is added to the timeline.")]
        [SerializeField] float defaultDuration = DefaultStepDuration;

        [Tooltip("Ease given to a step when it is added to the timeline.")]
        [SerializeField] Ease defaultEase = DefaultStepEase;

        [Header("Snapping")]
        [Tooltip("Whether new timelines start with snapping on.")]
        [SerializeField] bool snapEnabled = true;

        [Tooltip("Grid interval clip edges snap to, in seconds. Also the arrow-key nudge.")]
        [SerializeField] float snapInterval = DefaultSnapInterval;

        [Tooltip("How close, in pixels, an edge must come to a neighbour or the playhead to latch on.")]
        [SerializeField] float snapPixelThreshold = DefaultSnapPixelThreshold;

        [Header("Timeline")]
        [Tooltip("Zoom new timelines open at, in pixels per second.")]
        [SerializeField] float defaultPixelsPerSecond = DefaultZoom;

        [Tooltip("Furthest zoomed-out the timeline goes, in pixels per second.")]
        [SerializeField] float minPixelsPerSecond = DefaultMinPixelsPerSecond;

        [Tooltip("Furthest zoomed-in the timeline goes, in pixels per second.")]
        [SerializeField] float maxPixelsPerSecond = DefaultMaxPixelsPerSecond;

        [Tooltip("Height of one lane, in pixels.")]
        [SerializeField] float trackHeight = DefaultTrackHeight;

        [Tooltip("Tallest the lanes grow in the inspector before scrolling.")]
        [SerializeField] float maxTimelineHeight = DefaultMaxTimelineHeight;

        [Header("Preview")]
        [Tooltip("While previewing, selecting a clip parks the playhead at its start and edits show at once.")]
        [SerializeField] bool autoPreview = true;

        [Tooltip("Show the one-line hint strip at the top of the editor.")]
        [SerializeField] bool showHints = true;

        [Header("Clip colours")]
        [Tooltip("Use the colours below instead of the built-in palette.")]
        [SerializeField] bool customClipColors;

        [Tooltip("One colour per family, in the order Transform, Vibration, RectTransform, Graphics, " +
                 "Text, Rendering, Camera, Audio, Timeline, Extension.")]
        [SerializeField] Color[] familyColors;

        /// <summary>Raised whenever a setting changes, so open editors can redraw.</summary>
        public static event Action Changed;

        public float DefaultDuration
        {
            get => Mathf.Max(0f, defaultDuration);
            set => Set(ref defaultDuration, Mathf.Max(0f, value));
        }

        public Ease DefaultEase
        {
            get => defaultEase;
            set
            {
                if (defaultEase == value) return;
                defaultEase = value;
                Commit();
            }
        }

        public bool SnapEnabled
        {
            get => snapEnabled;
            set => Set(ref snapEnabled, value);
        }

        /// <summary>Grid interval in seconds, never zero, since everything divides by it.</summary>
        public float SnapInterval
        {
            get => Mathf.Max(0.001f, snapInterval);
            set => Set(ref snapInterval, Mathf.Max(0.001f, value));
        }

        public float SnapPixelThreshold
        {
            get => Mathf.Max(0f, snapPixelThreshold);
            set => Set(ref snapPixelThreshold, Mathf.Max(0f, value));
        }

        public float MinPixelsPerSecond => Mathf.Max(1f, Mathf.Min(minPixelsPerSecond, maxPixelsPerSecond));

        public float MaxPixelsPerSecond => Mathf.Max(MinPixelsPerSecond + 1f, maxPixelsPerSecond);

        /// <summary>The opening zoom, clamped into the zoom range so it is always reachable.</summary>
        public float DefaultPixelsPerSecond =>
            Mathf.Clamp(defaultPixelsPerSecond, MinPixelsPerSecond, MaxPixelsPerSecond);

        public float TrackHeight => Mathf.Clamp(trackHeight, 14f, 64f);

        public float MaxTimelineHeight => Mathf.Max(80f, maxTimelineHeight);

        public bool AutoPreview
        {
            get => autoPreview;
            set => Set(ref autoPreview, value);
        }

        public bool ShowHints
        {
            get => showHints;
            set => Set(ref showHints, value);
        }

        public bool CustomClipColors
        {
            get => customClipColors;
            set => Set(ref customClipColors, value);
        }

        /// <summary>
        /// The author's colour for a family, or null when the built-in palette applies.
        /// </summary>
        public Color? FamilyColorOverride(TweenFamily family)
        {
            if (!customClipColors || familyColors == null) return null;

            var index = (int)family;
            return index >= 0 && index < familyColors.Length ? familyColors[index] : null;
        }

        public void SetFamilyColor(TweenFamily family, Color color)
        {
            EnsureColorSlots();
            familyColors[(int)family] = color;
            Commit();
        }

        /// <summary>Puts every setting back to the shipped default.</summary>
        public void ResetToDefaults()
        {
            ApplyDefaults(this);
            Commit();
        }

        /// <summary>Writes the shipped defaults into <paramref name="settings"/> without saving.</summary>
        internal static void ApplyDefaults(TweenEditorSettings settings)
        {
            settings.defaultDuration = DefaultStepDuration;
            settings.defaultEase = DefaultStepEase;
            settings.snapEnabled = true;
            settings.snapInterval = DefaultSnapInterval;
            settings.snapPixelThreshold = DefaultSnapPixelThreshold;
            settings.defaultPixelsPerSecond = DefaultZoom;
            settings.minPixelsPerSecond = DefaultMinPixelsPerSecond;
            settings.maxPixelsPerSecond = DefaultMaxPixelsPerSecond;
            settings.trackHeight = DefaultTrackHeight;
            settings.maxTimelineHeight = DefaultMaxTimelineHeight;
            settings.autoPreview = true;
            settings.showHints = true;
            settings.customClipColors = false;
            settings.familyColors = DefaultFamilyColors();
        }

        /// <summary>The built-in palette, used to seed the custom colour slots.</summary>
        internal static Color[] DefaultFamilyColors()
        {
            var families = (TweenFamily[])Enum.GetValues(typeof(TweenFamily));
            var colors = new Color[families.Length];

            for (var i = 0; i < families.Length; i++)
            {
                colors[(int)families[i]] = TweenTimelineStyles.BuiltInFamilyColor(families[i]);
            }

            return colors;
        }

        void EnsureColorSlots()
        {
            var count = Enum.GetValues(typeof(TweenFamily)).Length;
            if (familyColors != null && familyColors.Length == count) return;

            var defaults = DefaultFamilyColors();
            if (familyColors != null)
            {
                Array.Copy(familyColors, defaults, Mathf.Min(familyColors.Length, defaults.Length));
            }

            familyColors = defaults;
        }

        void OnEnable()
        {
            // Seeded here rather than in a field initializer: initializers run inside the
            // constructor, where the editor skin cannot be queried. This also pads a settings
            // file from an older version that knew fewer families.
            EnsureColorSlots();
        }

        void Set<T>(ref T field, T value)
        {
            if (Equals(field, value)) return;

            field = value;
            Commit();
        }

        /// <summary>Saves and tells open editors. Called by the Preferences page after edits too.</summary>
        internal void Commit()
        {
            Save(true);
            Changed?.Invoke();
        }
    }
}
