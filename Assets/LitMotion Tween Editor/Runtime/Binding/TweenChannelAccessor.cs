using System;
using UnityEngine;
using Object = UnityEngine.Object;
#if LMTE_SUPPORT_UGUI
using UnityEngine.UI;
#endif
#if LMTE_SUPPORT_TMP
using TMPro;
#endif
#if LMTE_SUPPORT_RENDER_PIPELINES
using UnityEngine.Rendering;
#endif

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Reads and writes the animatable channels, with every value carried as a
    /// <see cref="Vector4"/>.
    /// </summary>
    /// <remarks>
    /// Read and write live side by side here on purpose. Three features need to read a live
    /// value, not just write one: <c>FromCurrent</c> start-value capture, editor preview
    /// snapshot and restore, and per-axis masking (which must preserve the axes a step does
    /// not own). LitMotion's <c>BindTo*</c> extensions are write-only, so using them would
    /// have forced a second, separately maintained reader table that could silently drift
    /// out of step with the writers.
    ///
    /// Carrying everything as a Vector4 collapses float, Vector2/3/4 and Color into one
    /// signature: a float lives in X, and Color converts both ways implicitly.
    /// </remarks>
    internal static class TweenChannelAccessor
    {
        /// <summary>
        /// Maps a step and its resolved target onto the physical channel it writes.
        /// </summary>
        public static TweenChannelKey GetChannelKey(TweenStep step, Object resolved)
        {
            if (step == null || resolved == null) return TweenChannelKey.None;

            switch (step.Type)
            {
                case TweenType.Move:
                case TweenType.Jump:
                    // A RectTransform in local space means anchoredPosition: that is what UI
                    // authors expect to animate, and localPosition fights the anchor layout.
                    if (step.Space == TweenSpace.World) return TweenChannelKey.WorldPosition;
                    return resolved is RectTransform
                        ? TweenChannelKey.AnchoredPosition3D
                        : TweenChannelKey.LocalPosition;

                case TweenType.Scale:
                    return TweenChannelKey.LocalScale;

                case TweenType.Rotate:
                    return step.Space == TweenSpace.World
                        ? TweenChannelKey.WorldEulerAngles
                        : TweenChannelKey.LocalEulerAngles;

                case TweenType.Punch:
                case TweenType.Shake:
                    return ChannelFor(step, resolved);

                case TweenType.SizeDelta:
                    return TweenChannelKey.SizeDelta;

                case TweenType.Pivot:
                    return TweenChannelKey.Pivot;

                case TweenType.Anchors:
                    // Both is handled by the step builder, which emits two motions.
                    return step.AnchorTarget == TweenAnchorTarget.Max
                        ? TweenChannelKey.AnchorMax
                        : TweenChannelKey.AnchorMin;

                case TweenType.Fade:
                    if (resolved is CanvasGroup) return TweenChannelKey.CanvasGroupAlpha;
                    if (resolved is SpriteRenderer) return TweenChannelKey.SpriteAlpha;
#if LMTE_SUPPORT_UGUI
                    if (resolved is Graphic) return TweenChannelKey.GraphicAlpha;
#endif
                    return TweenChannelKey.None;

                case TweenType.Color:
                    if (resolved is SpriteRenderer) return TweenChannelKey.SpriteColor;
                    if (resolved is Camera) return TweenChannelKey.CameraBackgroundColor;
#if LMTE_SUPPORT_UGUI
                    if (resolved is Graphic) return TweenChannelKey.GraphicColor;
#endif
                    return TweenChannelKey.None;

                case TweenType.FillAmount:
                    return TweenChannelKey.ImageFillAmount;

                case TweenType.CameraProperty:
                    return step.CameraProperty switch
                    {
                        TweenCameraProperty.FieldOfView => TweenChannelKey.CameraFieldOfView,
                        TweenCameraProperty.OrthographicSize => TweenChannelKey.CameraOrthographicSize,
                        TweenCameraProperty.BackgroundColor => TweenChannelKey.CameraBackgroundColor,
                        TweenCameraProperty.NearClipPlane => TweenChannelKey.CameraNearClipPlane,
                        TweenCameraProperty.FarClipPlane => TweenChannelKey.CameraFarClipPlane,
                        _ => TweenChannelKey.None,
                    };

                case TweenType.AudioVolume:
                    return TweenChannelKey.AudioVolume;

                case TweenType.AudioPitch:
                    return TweenChannelKey.AudioPitch;

                case TweenType.VolumeWeight:
                    return TweenChannelKey.VolumeWeight;

                case TweenType.MaterialProperty:
                    return step.MaterialPropertyKind switch
                    {
                        TweenMaterialPropertyKind.Color => TweenChannelKey.MaterialColor,
                        TweenMaterialPropertyKind.Vector => TweenChannelKey.MaterialVector,
                        _ => TweenChannelKey.MaterialFloat,
                    };

                case TweenType.TextReveal:
                    return TweenChannelKey.TmpMaxVisible;

                case TweenType.TextCounter:
                    return TweenChannelKey.TextNumber;

                case TweenType.TextScramble:
                    return TweenChannelKey.TextString;

                case TweenType.TMPCharacter:
                    return TweenChannelKey.TmpCharacter;

                case TweenType.Extension:
                    // A missing channel means no key, which the builder reports as a binding
                    // failure rather than an exception.
                    return TweenExtensionRegistry.Find(step.ExtensionId) == null
                        ? TweenChannelKey.None
                        : TweenChannelKey.Extension;

                default:
                    return TweenChannelKey.None;
            }
        }

        static TweenChannelKey ChannelFor(TweenStep step, Object resolved)
        {
            switch (step.Channel)
            {
                case TweenChannel.Scale:
                    return TweenChannelKey.LocalScale;
                case TweenChannel.Rotation:
                    return TweenChannelKey.LocalEulerAngles;
                default:
                    return resolved is RectTransform
                        ? TweenChannelKey.AnchoredPosition3D
                        : TweenChannelKey.LocalPosition;
            }
        }

        /// <summary>The value type a channel carries.</summary>
        public static TweenValueKind GetValueKind(TweenChannelKey key)
        {
            switch (key)
            {
                case TweenChannelKey.CanvasGroupAlpha:
                case TweenChannelKey.GraphicAlpha:
                case TweenChannelKey.SpriteAlpha:
                case TweenChannelKey.ImageFillAmount:
                case TweenChannelKey.CameraFieldOfView:
                case TweenChannelKey.CameraOrthographicSize:
                case TweenChannelKey.CameraNearClipPlane:
                case TweenChannelKey.CameraFarClipPlane:
                case TweenChannelKey.AudioVolume:
                case TweenChannelKey.AudioPitch:
                case TweenChannelKey.VolumeWeight:
                case TweenChannelKey.MaterialFloat:
                case TweenChannelKey.TmpMaxVisible:
                case TweenChannelKey.TextNumber:
                    return TweenValueKind.Float;

                case TweenChannelKey.SizeDelta:
                case TweenChannelKey.Pivot:
                case TweenChannelKey.AnchorMin:
                case TweenChannelKey.AnchorMax:
                    return TweenValueKind.Vector2;

                case TweenChannelKey.GraphicColor:
                case TweenChannelKey.SpriteColor:
                case TweenChannelKey.CameraBackgroundColor:
                case TweenChannelKey.MaterialColor:
                case TweenChannelKey.MaterialVector:
                    return TweenValueKind.Vector4;

                default:
                    return TweenValueKind.Vector3;
            }
        }

        /// <summary>
        /// The value type a channel carries, consulting the context for an extension channel,
        /// whose shape is not known from its key alone.
        /// </summary>
        public static TweenValueKind GetValueKind(TweenChannelKey key, in TweenChannelContext context)
        {
            if (key == TweenChannelKey.Extension)
            {
                return context.Extension == null
                    ? TweenValueKind.Float
                    : TweenExtensionRegistry.KindOf(context.Extension.Shape);
            }

            return GetValueKind(key);
        }

        /// <summary>
        /// True when the channel carries a colour, consulting the context for an extension.
        /// </summary>
        public static bool IsColorChannel(TweenChannelKey key, in TweenChannelContext context)
        {
            if (key == TweenChannelKey.Extension)
            {
                return context.Extension != null && context.Extension.Shape == TweenValueShape.Color;
            }

            return IsColorChannel(key);
        }

        /// <summary>True when the channel carries a color rather than a plain vector.</summary>
        public static bool IsColorChannel(TweenChannelKey key)
        {
            return key is TweenChannelKey.GraphicColor
                or TweenChannelKey.SpriteColor
                or TweenChannelKey.CameraBackgroundColor
                or TweenChannelKey.MaterialColor;
        }

        /// <summary>
        /// True for channels whose state is a string rather than a number, and which therefore
        /// snapshot and restore through <see cref="TryReadString"/> and
        /// <see cref="TryWriteString"/>.
        /// </summary>
        public static bool IsTextChannel(TweenChannelKey key)
        {
            return key is TweenChannelKey.TextString or TweenChannelKey.TextNumber;
        }

        /// <summary>
        /// True when the channel's live value can be read back as a number.
        /// </summary>
        /// <remarks>
        /// False for the write-only channels: a counter cannot recover its number from the
        /// string it printed, and per-character TMP state is held in LitMotion's animator. The
        /// step builder consults this so that a step carrying a leftover <c>FromCurrent</c> --
        /// from having been another type a moment ago -- quietly ignores it rather than
        /// failing to build.
        /// </remarks>
        public static bool SupportsRead(TweenChannelKey key)
        {
            return key is not (TweenChannelKey.TextNumber
                or TweenChannelKey.TextString
                or TweenChannelKey.TmpCharacter);
        }

        /// <summary>
        /// The axis mask actually applied when writing <paramref name="key"/>.
        /// </summary>
        /// <remarks>
        /// Axis masking is a vector-space feature, so it is ignored for scalar channels and for
        /// four-component channels. Without this, a Color step would inherit the step's default
        /// <see cref="TweenAxis.XYZ"/> mask and silently refuse to animate alpha, and a shader
        /// vector property would lose its W. To hold one component steady, give it the same
        /// start and end value instead.
        /// </remarks>
        public static TweenAxis GetEffectiveAxis(TweenStep step, TweenChannelKey key)
        {
            var kind = key == TweenChannelKey.Extension
                ? GetValueKind(key, TweenChannelContext.For(step))
                : GetValueKind(key);
            if (kind is TweenValueKind.Float or TweenValueKind.Vector4) return TweenAxis.All;

            var axis = step == null ? TweenAxis.XYZ : step.Axis;

            // An empty mask would write nothing at all, which is never what an author means.
            if ((axis & TweenAxis.XYZ) == TweenAxis.None) axis = TweenAxis.XYZ;

            return kind == TweenValueKind.Vector2
                ? axis & TweenAxis.XY
                : axis & TweenAxis.XYZ;
        }

        /// <summary>True when the mask covers every component the channel's kind actually uses.</summary>
        static bool CoversAllComponents(TweenAxis axis, TweenValueKind kind)
        {
            switch (kind)
            {
                case TweenValueKind.Float:
                    return true;
                case TweenValueKind.Vector2:
                    return (axis & TweenAxis.XY) == TweenAxis.XY;
                case TweenValueKind.Vector3:
                    return (axis & TweenAxis.XYZ) == TweenAxis.XYZ;
                default:
                    return (axis & TweenAxis.All) == TweenAxis.All;
            }
        }

        /// <summary>
        /// Reads the channel's current value. Unused components are left at zero.
        /// </summary>
        /// <remarks>
        /// Every target is type-tested rather than hard-cast. Resolution and key selection
        /// should always agree, but a bound motion writes once per frame: turning a mismatch
        /// into a <c>false</c> return rather than an <c>InvalidCastException</c> keeps one bad
        /// step from flooding the console sixty times a second.
        /// </remarks>
        public static bool TryRead(TweenChannelKey key, Object target, out Vector4 value)
        {
            return TryRead(key, target, default, out value);
        }

        /// <inheritdoc cref="TryRead(TweenChannelKey, Object, out Vector4)"/>
        /// <param name="context">
        /// Per-step parameters for the channels a key alone does not identify, such as which
        /// material property or which TMP reveal unit.
        /// </param>
        public static bool TryRead(TweenChannelKey key, Object target, in TweenChannelContext context,
            out Vector4 value)
        {
            value = Vector4.zero;
            if (target == null || key == TweenChannelKey.None) return false;

            switch (key)
            {
                case TweenChannelKey.Extension:
                    return context.Extension != null && context.Extension.TryRead(target, out value);

                case TweenChannelKey.LocalPosition:
                    if (target is not Transform localPositionTarget) return false;
                    value = localPositionTarget.localPosition;
                    return true;
                case TweenChannelKey.WorldPosition:
                    if (target is not Transform worldPositionTarget) return false;
                    value = worldPositionTarget.position;
                    return true;
                case TweenChannelKey.AnchoredPosition3D:
                    if (target is not RectTransform anchoredTarget) return false;
                    value = anchoredTarget.anchoredPosition3D;
                    return true;
                case TweenChannelKey.LocalScale:
                    if (target is not Transform scaleTarget) return false;
                    value = scaleTarget.localScale;
                    return true;
                case TweenChannelKey.LocalEulerAngles:
                    if (target is not Transform localEulerTarget) return false;
                    value = localEulerTarget.localEulerAngles;
                    return true;
                case TweenChannelKey.WorldEulerAngles:
                    if (target is not Transform worldEulerTarget) return false;
                    value = worldEulerTarget.eulerAngles;
                    return true;

                case TweenChannelKey.SizeDelta:
                    if (target is not RectTransform sizeDeltaTarget) return false;
                    value = sizeDeltaTarget.sizeDelta;
                    return true;
                case TweenChannelKey.Pivot:
                    if (target is not RectTransform pivotTarget) return false;
                    value = pivotTarget.pivot;
                    return true;
                case TweenChannelKey.AnchorMin:
                    if (target is not RectTransform anchorMinTarget) return false;
                    value = anchorMinTarget.anchorMin;
                    return true;
                case TweenChannelKey.AnchorMax:
                    if (target is not RectTransform anchorMaxTarget) return false;
                    value = anchorMaxTarget.anchorMax;
                    return true;

                case TweenChannelKey.CanvasGroupAlpha:
                    if (target is not CanvasGroup canvasGroupTarget) return false;
                    value.x = canvasGroupTarget.alpha;
                    return true;
                case TweenChannelKey.SpriteColor:
                    if (target is not SpriteRenderer spriteColorTarget) return false;
                    value = spriteColorTarget.color;
                    return true;
                case TweenChannelKey.SpriteAlpha:
                    if (target is not SpriteRenderer spriteAlphaTarget) return false;
                    value.x = spriteAlphaTarget.color.a;
                    return true;
#if LMTE_SUPPORT_UGUI
                case TweenChannelKey.GraphicColor:
                    if (target is not Graphic graphicColorTarget) return false;
                    value = graphicColorTarget.color;
                    return true;
                case TweenChannelKey.GraphicAlpha:
                    if (target is not Graphic graphicAlphaTarget) return false;
                    value.x = graphicAlphaTarget.color.a;
                    return true;
                case TweenChannelKey.ImageFillAmount:
                    if (target is not Image fillAmountTarget) return false;
                    value.x = fillAmountTarget.fillAmount;
                    return true;
#endif

                case TweenChannelKey.CameraFieldOfView:
                    if (target is not Camera fovTarget) return false;
                    value.x = fovTarget.fieldOfView;
                    return true;
                case TweenChannelKey.CameraOrthographicSize:
                    if (target is not Camera orthoTarget) return false;
                    value.x = orthoTarget.orthographicSize;
                    return true;
                case TweenChannelKey.CameraBackgroundColor:
                    if (target is not Camera backgroundTarget) return false;
                    value = backgroundTarget.backgroundColor;
                    return true;
                case TweenChannelKey.CameraNearClipPlane:
                    if (target is not Camera nearTarget) return false;
                    value.x = nearTarget.nearClipPlane;
                    return true;
                case TweenChannelKey.CameraFarClipPlane:
                    if (target is not Camera farTarget) return false;
                    value.x = farTarget.farClipPlane;
                    return true;

                case TweenChannelKey.AudioVolume:
                    if (target is not AudioSource volumeTarget) return false;
                    value.x = volumeTarget.volume;
                    return true;
                case TweenChannelKey.AudioPitch:
                    if (target is not AudioSource pitchTarget) return false;
                    value.x = pitchTarget.pitch;
                    return true;

#if LMTE_SUPPORT_RENDER_PIPELINES
                case TweenChannelKey.VolumeWeight:
                    if (target is not Volume volumeWeightTarget) return false;
                    value.x = volumeWeightTarget.weight;
                    return true;
#endif

                case TweenChannelKey.MaterialFloat:
                {
                    var material = ResolveMaterial(target);
                    if (material == null || context.PropertyId == 0) return false;
                    if (!material.HasProperty(context.PropertyId)) return false;
                    value.x = material.GetFloat(context.PropertyId);
                    return true;
                }
                case TweenChannelKey.MaterialColor:
                {
                    var material = ResolveMaterial(target);
                    if (material == null || context.PropertyId == 0) return false;
                    if (!material.HasProperty(context.PropertyId)) return false;
                    value = material.GetColor(context.PropertyId);
                    return true;
                }
                case TweenChannelKey.MaterialVector:
                {
                    var material = ResolveMaterial(target);
                    if (material == null || context.PropertyId == 0) return false;
                    if (!material.HasProperty(context.PropertyId)) return false;
                    value = material.GetVector(context.PropertyId);
                    return true;
                }

#if LMTE_SUPPORT_TMP
                case TweenChannelKey.TmpMaxVisible:
                {
                    if (target is not TMP_Text revealTarget) return false;

                    // Carried as a 0-1 fraction so a reveal survives the text changing length.
                    var total = CountUnits(revealTarget, context.TextUnit);
                    if (total <= 0) return false;

                    var visible = context.TextUnit switch
                    {
                        TweenTextUnit.Words => revealTarget.maxVisibleWords,
                        TweenTextUnit.Lines => revealTarget.maxVisibleLines,
                        _ => revealTarget.maxVisibleCharacters,
                    };

                    // TMP parks these at int.MaxValue to mean "everything".
                    value.x = Mathf.Clamp01(visible / (float)total);
                    return true;
                }
#endif

                // A formatted counter and a scrambled string have no numeric state to read
                // back, so FromCurrent does not apply to them; their original text is preserved
                // through TryReadString instead.
                case TweenChannelKey.TextNumber:
                case TweenChannelKey.TextString:
                case TweenChannelKey.TmpCharacter:
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Reads the whole string of a text target, for snapshot and restore.
        /// </summary>
        public static bool TryReadString(Object target, out string value)
        {
            value = null;

#if LMTE_SUPPORT_TMP
            if (target is TMP_Text tmp)
            {
                value = tmp.text;
                return true;
            }
#endif
#if LMTE_SUPPORT_UGUI
            if (target is Text text)
            {
                value = text.text;
                return true;
            }
#endif
            return false;
        }

        /// <summary>Writes a whole string to a text target.</summary>
        public static bool TryWriteString(Object target, string value)
        {
            if (target == null) return false;

#if LMTE_SUPPORT_TMP
            if (target is TMP_Text tmp)
            {
                tmp.text = value ?? string.Empty;
                return true;
            }
#endif
#if LMTE_SUPPORT_UGUI
            if (target is Text text)
            {
                text.text = value ?? string.Empty;
                return true;
            }
#endif
            return false;
        }

        /// <summary>
        /// Rebuilds a TMP mesh from scratch, discarding per-character vertex edits.
        /// </summary>
        /// <remarks>
        /// Per-character state lives in LitMotion's internal TMP animator, not on the component,
        /// so it cannot be read into a snapshot. Forcing a mesh rebuild is how a preview undoes
        /// it: the animator only reapplies offsets while its motions are still running.
        /// </remarks>
        public static bool RefreshText(Object target)
        {
#if LMTE_SUPPORT_TMP
            if (target is TMP_Text tmp)
            {
                tmp.ForceMeshUpdate(true);
                return true;
            }
#endif
            return false;
        }

        /// <summary>
        /// The material a MaterialProperty step writes to.
        /// </summary>
        /// <remarks>
        /// At runtime this is the renderer's own material, so animating one object does not
        /// bleed into every other object sharing that material. In the editor it is the shared
        /// material instead: instancing there would leave a stray "(Instance)" material in the
        /// scene that outlives the preview, which is worse than a property that is written and
        /// then restored. Assign a Material directly to the step's Target to bypass the choice.
        /// </remarks>
        internal static Material ResolveMaterial(Object target)
        {
            if (target is Material direct) return direct;

            if (target is Renderer renderer)
            {
                return Application.isPlaying ? renderer.material : renderer.sharedMaterial;
            }

#if LMTE_SUPPORT_UGUI
            if (target is Graphic graphic)
            {
                var material = graphic.material;

                // The shared default UI material backs every unstyled Graphic in the project,
                // so writing to it would animate all of them at once.
                return material == Graphic.defaultGraphicMaterial ? null : material;
            }
#endif
            return null;
        }

#if LMTE_SUPPORT_TMP
        /// <summary>Total number of revealable units in a TMP text.</summary>
        internal static int CountUnits(TMP_Text text, TweenTextUnit unit)
        {
            if (text == null) return 0;

            // The counts come from the generated text info, which is stale until TMP lays out.
            text.ForceMeshUpdate();
            var info = text.textInfo;
            if (info == null) return 0;

            return unit switch
            {
                TweenTextUnit.Words => info.wordCount,
                TweenTextUnit.Lines => info.lineCount,
                _ => info.characterCount,
            };
        }
#endif

        /// <summary>
        /// Writes <paramref name="value"/> to the channel.
        /// </summary>
        /// <param name="axis">
        /// Components to write, as returned by <see cref="GetEffectiveAxis"/>. Any component not
        /// selected keeps the target's live value, which is what makes an X-only Move leave Y
        /// and Z under someone else's control.
        /// </param>
        public static bool TryWrite(TweenChannelKey key, Object target, Vector4 value, TweenAxis axis)
        {
            return TryWrite(key, target, value, axis, default);
        }

        /// <inheritdoc cref="TryWrite(TweenChannelKey, Object, Vector4, TweenAxis)"/>
        /// <param name="context">
        /// Per-step parameters for the channels a key alone does not identify, such as which
        /// material property or which format string.
        /// </param>
        public static bool TryWrite(TweenChannelKey key, Object target, Vector4 value, TweenAxis axis,
            in TweenChannelContext context)
        {
            if (target == null || key == TweenChannelKey.None) return false;

            // Only pay for a read-back when some component must be preserved.
            if (!CoversAllComponents(axis, GetValueKind(key, context)))
            {
                if (!TryRead(key, target, context, out var current)) return false;
                value = Mask(current, value, axis);
            }

            switch (key)
            {
                case TweenChannelKey.Extension:
                    return context.Extension != null && context.Extension.TryWrite(target, value);

                case TweenChannelKey.LocalPosition:
                    if (target is not Transform localPositionTarget) return false;
                    localPositionTarget.localPosition = value;
                    return true;
                case TweenChannelKey.WorldPosition:
                    if (target is not Transform worldPositionTarget) return false;
                    worldPositionTarget.position = value;
                    return true;
                case TweenChannelKey.AnchoredPosition3D:
                    if (target is not RectTransform anchoredTarget) return false;
                    anchoredTarget.anchoredPosition3D = value;
                    return true;
                case TweenChannelKey.LocalScale:
                    if (target is not Transform scaleTarget) return false;
                    scaleTarget.localScale = value;
                    return true;
                case TweenChannelKey.LocalEulerAngles:
                    if (target is not Transform localEulerTarget) return false;
                    localEulerTarget.localEulerAngles = value;
                    return true;
                case TweenChannelKey.WorldEulerAngles:
                    if (target is not Transform worldEulerTarget) return false;
                    worldEulerTarget.eulerAngles = value;
                    return true;

                case TweenChannelKey.SizeDelta:
                    if (target is not RectTransform sizeDeltaTarget) return false;
                    sizeDeltaTarget.sizeDelta = value;
                    return true;
                case TweenChannelKey.Pivot:
                    if (target is not RectTransform pivotTarget) return false;
                    pivotTarget.pivot = value;
                    return true;
                case TweenChannelKey.AnchorMin:
                    if (target is not RectTransform anchorMinTarget) return false;
                    anchorMinTarget.anchorMin = value;
                    return true;
                case TweenChannelKey.AnchorMax:
                    if (target is not RectTransform anchorMaxTarget) return false;
                    anchorMaxTarget.anchorMax = value;
                    return true;

                case TweenChannelKey.CanvasGroupAlpha:
                    if (target is not CanvasGroup canvasGroupTarget) return false;
                    canvasGroupTarget.alpha = value.x;
                    return true;
                case TweenChannelKey.SpriteColor:
                    if (target is not SpriteRenderer spriteColorTarget) return false;
                    spriteColorTarget.color = value;
                    return true;
                case TweenChannelKey.SpriteAlpha:
                {
                    if (target is not SpriteRenderer spriteAlphaTarget) return false;
                    var color = spriteAlphaTarget.color;
                    color.a = value.x;
                    spriteAlphaTarget.color = color;
                    return true;
                }
#if LMTE_SUPPORT_UGUI
                case TweenChannelKey.GraphicColor:
                    if (target is not Graphic graphicColorTarget) return false;
                    graphicColorTarget.color = value;
                    return true;
                case TweenChannelKey.GraphicAlpha:
                {
                    if (target is not Graphic graphicAlphaTarget) return false;
                    var color = graphicAlphaTarget.color;
                    color.a = value.x;
                    graphicAlphaTarget.color = color;
                    return true;
                }
                case TweenChannelKey.ImageFillAmount:
                    if (target is not Image fillAmountTarget) return false;
                    fillAmountTarget.fillAmount = value.x;
                    return true;
#endif

                case TweenChannelKey.CameraFieldOfView:
                    if (target is not Camera fovTarget) return false;
                    fovTarget.fieldOfView = value.x;
                    return true;
                case TweenChannelKey.CameraOrthographicSize:
                    if (target is not Camera orthoTarget) return false;
                    orthoTarget.orthographicSize = value.x;
                    return true;
                case TweenChannelKey.CameraBackgroundColor:
                    if (target is not Camera backgroundTarget) return false;
                    backgroundTarget.backgroundColor = value;
                    return true;
                case TweenChannelKey.CameraNearClipPlane:
                    if (target is not Camera nearTarget) return false;
                    nearTarget.nearClipPlane = value.x;
                    return true;
                case TweenChannelKey.CameraFarClipPlane:
                    if (target is not Camera farTarget) return false;
                    farTarget.farClipPlane = value.x;
                    return true;

                case TweenChannelKey.AudioVolume:
                    if (target is not AudioSource volumeTarget) return false;
                    volumeTarget.volume = value.x;
                    return true;
                case TweenChannelKey.AudioPitch:
                    if (target is not AudioSource pitchTarget) return false;
                    pitchTarget.pitch = value.x;
                    return true;

#if LMTE_SUPPORT_RENDER_PIPELINES
                case TweenChannelKey.VolumeWeight:
                    if (target is not Volume volumeWeightTarget) return false;
                    volumeWeightTarget.weight = value.x;
                    return true;
#endif

                case TweenChannelKey.MaterialFloat:
                {
                    var material = ResolveMaterial(target);
                    if (material == null || context.PropertyId == 0) return false;
                    material.SetFloat(context.PropertyId, value.x);
                    return true;
                }
                case TweenChannelKey.MaterialColor:
                {
                    var material = ResolveMaterial(target);
                    if (material == null || context.PropertyId == 0) return false;
                    material.SetColor(context.PropertyId, value);
                    return true;
                }
                case TweenChannelKey.MaterialVector:
                {
                    var material = ResolveMaterial(target);
                    if (material == null || context.PropertyId == 0) return false;
                    material.SetVector(context.PropertyId, value);
                    return true;
                }

#if LMTE_SUPPORT_TMP
                case TweenChannelKey.TmpMaxVisible:
                {
                    if (target is not TMP_Text revealTarget) return false;

                    var total = CountUnits(revealTarget, context.TextUnit);

                    // Fully revealed means "no limit", not "limited to today's character count":
                    // writing the count back would clip the text the moment it grows longer.
                    var visible = value.x >= 1f
                        ? int.MaxValue
                        : Mathf.Clamp(Mathf.RoundToInt(value.x * total), 0, total);

                    switch (context.TextUnit)
                    {
                        case TweenTextUnit.Words:
                            revealTarget.maxVisibleWords = visible;
                            break;
                        case TweenTextUnit.Lines:
                            revealTarget.maxVisibleLines = visible;
                            break;
                        default:
                            revealTarget.maxVisibleCharacters = visible;
                            break;
                    }

                    return true;
                }
#endif

                case TweenChannelKey.TextNumber:
                {
                    var format = string.IsNullOrEmpty(context.Format) ? "{0}" : context.Format;

                    // A bad format string would otherwise throw once per frame for the whole
                    // duration of the step.
                    string formatted;
                    try
                    {
                        formatted = string.Format(format, value.x);
                    }
                    catch (FormatException)
                    {
                        return false;
                    }

                    return TryWriteString(target, formatted);
                }

                default:
                    return false;
            }
        }

        static Vector4 Mask(Vector4 current, Vector4 incoming, TweenAxis axis)
        {
            return new Vector4(
                (axis & TweenAxis.X) != 0 ? incoming.x : current.x,
                (axis & TweenAxis.Y) != 0 ? incoming.y : current.y,
                (axis & TweenAxis.Z) != 0 ? incoming.z : current.z,
                (axis & TweenAxis.W) != 0 ? incoming.w : current.w);
        }
    }
}
