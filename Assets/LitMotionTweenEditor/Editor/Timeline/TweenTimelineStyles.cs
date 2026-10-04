using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>
    /// Colours, metrics and icons for the timeline, in one place.
    /// </summary>
    /// <remarks>
    /// Styling is applied from C# rather than a USS asset on purpose. A USS file has to be
    /// located through <c>AssetDatabase</c>, which breaks the moment the folder is renamed,
    /// relocated or turned into a UPM package. Keeping it in code means the package is pure
    /// source with no asset-path assumptions, and this class keeps it to one edit point.
    ///
    /// The metrics an author can reasonably have an opinion about -- zoom range, snap grid,
    /// lane height -- read through <see cref="TweenEditorSettings"/>, whose defaults are the
    /// constants this class used to hold. The chrome colours stay fixed: they follow the editor
    /// theme, and there is nothing to gain from letting them drift from it.
    ///
    /// Every colour is resolved against <see cref="EditorGUIUtility.isProSkin"/> so the timeline
    /// reads correctly in both editor themes.
    /// </remarks>
    internal static class TweenTimelineStyles
    {
        // --- Metrics ---

        public const float TrackSpacing = 2f;
        public const float HeaderWidth = 132f;
        public const float RulerHeight = 20f;
        public const float ClipEdgeGrab = 6f;
        public const float MinClipWidth = 8f;

        static TweenEditorSettings Settings => TweenEditorSettings.instance;

        public static float TrackHeight => Settings.TrackHeight;

        public static float DefaultPixelsPerSecond => Settings.DefaultPixelsPerSecond;
        public static float MinPixelsPerSecond => Settings.MinPixelsPerSecond;
        public static float MaxPixelsPerSecond => Settings.MaxPixelsPerSecond;

        /// <summary>Grid interval the clip edges snap to, in seconds.</summary>
        public static float SnapInterval => Settings.SnapInterval;

        /// <summary>How close, in pixels, an edge must be to snap to a neighbour or the playhead.</summary>
        public static float SnapPixelThreshold => Settings.SnapPixelThreshold;

        /// <summary>Tallest the lane area grows before it scrolls.</summary>
        public static float MaxBodyHeight => Settings.MaxTimelineHeight;

        static bool Dark => EditorGUIUtility.isProSkin;

        // --- Chrome ---

        public static Color Background => Dark ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.70f, 0.70f, 0.70f);
        public static Color LaneBackground => Dark ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.76f, 0.76f, 0.76f);
        public static Color LaneAlternate => Dark ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.73f, 0.73f, 0.73f);
        public static Color Border => Dark ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.56f, 0.56f, 0.56f);
        public static Color GridLine => Dark ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.07f);
        public static Color GridLineMajor => Dark ? new Color(1f, 1f, 1f, 0.14f) : new Color(0f, 0f, 0f, 0.16f);
        public static Color RulerText => Dark ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.25f, 0.25f, 0.25f);

        public static Color Playhead => new Color(0.95f, 0.26f, 0.21f);
        public static Color SelectionOutline => Dark ? new Color(0.27f, 0.60f, 0.95f) : new Color(0.13f, 0.40f, 0.80f);
        public static Color CurveLine => Dark ? new Color(0.45f, 0.82f, 1f) : new Color(0.10f, 0.42f, 0.72f);
        public static Color CurveGuide => Dark ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.14f);

        /// <summary>Text and badge colour for a warning, readable on both themes.</summary>
        public static Color Warning => Dark ? new Color(1f, 0.76f, 0.03f) : new Color(0.62f, 0.42f, 0f);

        /// <summary>Text colour for a healthy status.</summary>
        public static Color Ok => Dark ? new Color(0.45f, 0.80f, 0.45f) : new Color(0.10f, 0.45f, 0.10f);

        /// <summary>Background of the animation chip that is the current one.</summary>
        public static Color ChipSelected => Dark ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.55f, 0.70f, 0.88f);

        /// <summary>Background of an animation chip at rest.</summary>
        public static Color ChipIdle => Dark ? new Color(0.27f, 0.27f, 0.27f) : new Color(0.80f, 0.80f, 0.80f);

        // --- Families ---

        /// <summary>Which family a tween type belongs to, which decides its colour.</summary>
        public static TweenFamily FamilyOf(TweenType type)
        {
            switch (type)
            {
                case TweenType.Move:
                case TweenType.Scale:
                case TweenType.Rotate:
                case TweenType.Jump:
                    return TweenFamily.Transform;

                case TweenType.Punch:
                case TweenType.Shake:
                    return TweenFamily.Vibration;

                case TweenType.SizeDelta:
                case TweenType.Pivot:
                case TweenType.Anchors:
                    return TweenFamily.RectTransform;

                case TweenType.Fade:
                case TweenType.Color:
                case TweenType.FillAmount:
                    return TweenFamily.Graphics;

                case TweenType.TextReveal:
                case TweenType.TextCounter:
                case TweenType.TextScramble:
                case TweenType.TMPCharacter:
                    return TweenFamily.Text;

                case TweenType.MaterialProperty:
                case TweenType.VolumeWeight:
                    return TweenFamily.Rendering;

                case TweenType.CameraProperty:
                    return TweenFamily.Camera;

                case TweenType.AudioVolume:
                case TweenType.AudioPitch:
                    return TweenFamily.Audio;

                case TweenType.Extension:
                    return TweenFamily.Extension;

                default:
                    return TweenFamily.Timeline;
            }
        }

        /// <summary>
        /// Clip colour, grouped by what the tween type affects, so a timeline is scannable by
        /// family at a glance rather than needing every label read. The author's own palette
        /// wins when they have set one.
        /// </summary>
        public static Color ClipColor(TweenType type)
        {
            var family = FamilyOf(type);
            return Settings.FamilyColorOverride(family) ?? BuiltInFamilyColor(family);
        }

        /// <summary>The colour a family has when the author has not chosen one.</summary>
        public static Color BuiltInFamilyColor(TweenFamily family)
        {
            switch (family)
            {
                case TweenFamily.Transform: return Tint(0.26f, 0.52f, 0.85f);
                case TweenFamily.Vibration: return Tint(0.90f, 0.55f, 0.18f);
                case TweenFamily.RectTransform: return Tint(0.18f, 0.66f, 0.62f);
                case TweenFamily.Graphics: return Tint(0.58f, 0.40f, 0.85f);
                case TweenFamily.Text: return Tint(0.35f, 0.70f, 0.35f);
                case TweenFamily.Rendering: return Tint(0.82f, 0.33f, 0.40f);
                case TweenFamily.Camera: return Tint(0.80f, 0.70f, 0.25f);
                case TweenFamily.Audio: return Tint(0.85f, 0.40f, 0.65f);
                case TweenFamily.Extension: return Tint(0.30f, 0.62f, 0.78f);

                // Timeline primitives read as chrome, not animation.
                default: return Tint(0.45f, 0.45f, 0.48f);
            }
        }

        /// <summary>Darkens a clip colour slightly in the light theme so white text stays legible.</summary>
        static Color Tint(float r, float g, float b)
        {
            return Dark ? new Color(r, g, b) : new Color(r * 0.86f, g * 0.86f, b * 0.86f);
        }

        /// <summary>Text colour that reads against <see cref="ClipColor"/>.</summary>
        public static Color ClipText => Color.white;

        // --- Icons ---

        static readonly Dictionary<TweenType, Texture> IconCache = new();
        static bool iconCacheIsDark;

        /// <summary>
        /// A small icon for a tween type, so a clip says what it is even when it is too narrow
        /// for its label, and to a reader who cannot tell the family colours apart.
        /// </summary>
        /// <remarks>
        /// Built-in icons only, found without logging: <c>EditorGUIUtility.IconContent</c> prints
        /// an error for a name this editor version does not have, so tool icons go through
        /// <c>FindTexture</c> and component icons through <c>ObjectContent</c>. Either may come
        /// back null, and the clip then simply draws without one.
        /// </remarks>
        public static Texture IconFor(TweenType type)
        {
            if (iconCacheIsDark != Dark)
            {
                IconCache.Clear();
                iconCacheIsDark = Dark;
            }

            if (IconCache.TryGetValue(type, out var cached) && cached != null) return cached;

            var icon = LoadIcon(type);
            IconCache[type] = icon;
            return icon;
        }

        static Texture LoadIcon(TweenType type)
        {
            switch (type)
            {
                case TweenType.Move:
                case TweenType.Jump:
                    return Tool("MoveTool");
                case TweenType.Scale:
                    return Tool("ScaleTool");
                case TweenType.Rotate:
                    return Tool("RotateTool");
                case TweenType.Punch:
                case TweenType.Shake:
                    return TypeIcon(typeof(Transform));
                case TweenType.SizeDelta:
                case TweenType.Pivot:
                case TweenType.Anchors:
                    return Tool("RectTool");
                case TweenType.Fade:
                    return TypeIcon(typeof(CanvasGroup));
                case TweenType.Color:
                    return Tool("ColorPicker.CycleColor");
                case TweenType.FillAmount:
#if LMTE_SUPPORT_UGUI
                    return TypeIcon(typeof(UnityEngine.UI.Image));
#else
                    return null;
#endif
                case TweenType.TextReveal:
                case TweenType.TextCounter:
                case TweenType.TextScramble:
                case TweenType.TMPCharacter:
#if LMTE_SUPPORT_TMP
                    return TypeIcon(typeof(TMPro.TextMeshProUGUI));
#else
                    return null;
#endif
                case TweenType.MaterialProperty:
                    return TypeIcon(typeof(Material));
                case TweenType.VolumeWeight:
                    return Tool("Volume Icon");
                case TweenType.CameraProperty:
                    return TypeIcon(typeof(Camera));
                case TweenType.AudioVolume:
                case TweenType.AudioPitch:
                    return TypeIcon(typeof(AudioSource));
                case TweenType.Interval:
                    return TypeIcon(typeof(Animation));
                case TweenType.Callback:
#if LMTE_SUPPORT_UGUI
                    return TypeIcon(typeof(UnityEngine.EventSystems.EventSystem));
#else
                    return null;
#endif
                case TweenType.Custom:
                case TweenType.Extension:
                    return Tool("cs Script Icon");
                default:
                    return null;
            }
        }

        /// <summary>The warning icon used for a clip that cannot bind.</summary>
        public static Texture WarningIcon => Tool("console.warnicon.sml");

        /// <summary>Finds a built-in texture by name, preferring the dark-skin variant.</summary>
        public static Texture Tool(string name)
        {
            if (Dark)
            {
                var dark = EditorGUIUtility.FindTexture("d_" + name);
                if (dark != null) return dark;
            }

            return EditorGUIUtility.FindTexture(name);
        }

        static Texture TypeIcon(Type type)
        {
            return EditorGUIUtility.ObjectContent(null, type)?.image;
        }

        /// <summary>Applies a uniform border, which UI Toolkit otherwise needs eight assignments for.</summary>
        public static void SetBorder(UnityEngine.UIElements.VisualElement element, float width, Color color)
        {
            if (element == null) return;

            var s = element.style;
            s.borderTopWidth = width;
            s.borderBottomWidth = width;
            s.borderLeftWidth = width;
            s.borderRightWidth = width;
            s.borderTopColor = color;
            s.borderBottomColor = color;
            s.borderLeftColor = color;
            s.borderRightColor = color;
        }

        /// <summary>Applies a uniform corner radius.</summary>
        public static void SetRadius(UnityEngine.UIElements.VisualElement element, float radius)
        {
            if (element == null) return;

            var s = element.style;
            s.borderTopLeftRadius = radius;
            s.borderTopRightRadius = radius;
            s.borderBottomLeftRadius = radius;
            s.borderBottomRightRadius = radius;
        }
    }
}
