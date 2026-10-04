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
    /// Resolves the concrete component a <see cref="TweenStep"/> should animate.
    /// </summary>
    /// <remarks>
    /// A step's target may be left empty (meaning the player's own GameObject), a GameObject,
    /// or a Component. In every case this picks the component that actually owns the channel
    /// the step's <see cref="TweenType"/> names, so authors never have to know that Fade means
    /// CanvasGroup on a panel but Graphic on an Image.
    /// </remarks>
    public static class TweenBindingResolver
    {
        /// <summary>
        /// Finds the component <paramref name="step"/> should write to.
        /// </summary>
        /// <param name="step">The step to resolve for.</param>
        /// <param name="fallback">GameObject used when the step has no explicit target.</param>
        /// <param name="error">A human-readable reason when resolution fails, otherwise null.</param>
        /// <returns>The resolved object, or null when the step cannot be bound.</returns>
        public static Object Resolve(TweenStep step, GameObject fallback, out string error)
        {
            error = null;
            if (step == null)
            {
                error = "Step is null.";
                return null;
            }

            // Timeline primitives animate nothing.
            if (IsNonBinding(step.Type)) return null;

            // A Material asset is a target in its own right: it needs no GameObject to live on,
            // so requiring a fallback would refuse a perfectly well-formed step. Only
            // MaterialProperty can use one, so other types still fall through to the fallback.
            if (step.Type == TweenType.MaterialProperty && step.Target is Material material)
            {
                return material;
            }

            var go = ResolveGameObject(step, fallback);
            if (go == null)
            {
                error = step.DisplayName + ": no target assigned and no fallback GameObject.";
                return null;
            }

            if (step.Type == TweenType.Extension) return ResolveExtension(step, go, out error);

            var resolved = ResolveComponent(step, go);
            if (resolved == null)
            {
                error = step.DisplayName + ": " + go.name + " has no component that can be animated by a "
                        + step.Type + " step. " + DescribeRequirement(step.Type);
            }

            return resolved;
        }

        /// <summary>True for step types that write no target value.</summary>
        public static bool IsNonBinding(TweenType type)
        {
            return type is TweenType.Interval or TweenType.Callback or TweenType.Custom;
        }

        /// <summary>True when <paramref name="step"/> can be bound against the given fallback.</summary>
        public static bool CanResolve(TweenStep step, GameObject fallback)
        {
            if (step == null) return false;
            if (IsNonBinding(step.Type)) return true;
            return Resolve(step, fallback, out _) != null;
        }

        /// <summary>
        /// Hands resolution to the extension channel, wrapping its answer in the same message
        /// shape the built-in types use.
        /// </summary>
        static Object ResolveExtension(TweenStep step, GameObject go, out string error)
        {
            error = null;

            var channel = TweenExtensionRegistry.Find(step.ExtensionId);
            if (channel == null)
            {
                error = string.IsNullOrEmpty(step.ExtensionId)
                    ? step.DisplayName + ": choose which extension channel this step animates."
                    : step.DisplayName + ": no extension channel is registered as '" + step.ExtensionId
                      + "'. Was the package that defined it removed?";
                return null;
            }

            Object target;
            string reason;
            try
            {
                if (channel.TryResolve(step, go, out target, out reason) && target != null) return target;
            }
            catch (System.Exception exception)
            {
                // User code: one throwing channel must not take the whole animation down.
                reason = exception.Message;
            }

            error = step.DisplayName + ": " + go.name + " has nothing the " + step.DisplayName
                    + " channel can animate."
                    + (string.IsNullOrEmpty(reason) ? string.Empty : " " + reason);
            return null;
        }

        static GameObject ResolveGameObject(TweenStep step, GameObject fallback)
        {
            return step.Target switch
            {
                GameObject go => go,
                Component component => component.gameObject,
                _ => fallback,
            };
        }

        static Object ResolveComponent(TweenStep step, GameObject go)
        {
            switch (step.Type)
            {
                // --- Transform ---
                case TweenType.Move:
                case TweenType.Scale:
                case TweenType.Rotate:
                case TweenType.Punch:
                case TweenType.Shake:
                case TweenType.Jump:
                    return go.transform;

                // --- RectTransform ---
                case TweenType.SizeDelta:
                case TweenType.Pivot:
                case TweenType.Anchors:
                    return go.transform as RectTransform;

                // --- Graphics ---
                case TweenType.Fade:
                    return ResolveFadeTarget(step, go);

                case TweenType.Color:
                    return ResolveColorTarget(step, go);

                case TweenType.FillAmount:
#if LMTE_SUPPORT_UGUI
                    return Pick<Image>(step, go);
#else
                    return null;
#endif

                // --- Text ---
                case TweenType.TextReveal:
                case TweenType.TMPCharacter:
#if LMTE_SUPPORT_TMP
                    return Pick<TMP_Text>(step, go);
#else
                    return null;
#endif

                case TweenType.TextCounter:
                case TweenType.TextScramble:
                    return ResolveTextTarget(step, go);

                // --- Material / rendering ---
                case TweenType.MaterialProperty:
                    return ResolveMaterialTarget(step, go);

                case TweenType.VolumeWeight:
#if LMTE_SUPPORT_RENDER_PIPELINES
                    return Pick<Volume>(step, go);
#else
                    return null;
#endif

                // --- Camera ---
                case TweenType.CameraProperty:
                    return Pick<Camera>(step, go);

                // --- Audio ---
                case TweenType.AudioVolume:
                case TweenType.AudioPitch:
                    return Pick<AudioSource>(step, go);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Fade resolution order: a CanvasGroup fades a whole subtree, so it wins when present.
        /// </summary>
        static Object ResolveFadeTarget(TweenStep step, GameObject go)
        {
            // An explicitly assigned component always wins over the search order below, so
            // assigning a Graphic on an object that also has a CanvasGroup does what it says.
            if (step.Target is Component assigned && IsFadeable(assigned)) return assigned;

            var canvasGroup = Pick<CanvasGroup>(step, go);
            if (canvasGroup != null) return canvasGroup;
#if LMTE_SUPPORT_UGUI
            var graphic = Pick<Graphic>(step, go);
            if (graphic != null) return graphic;
#endif
            return Pick<SpriteRenderer>(step, go);
        }

        static Object ResolveColorTarget(TweenStep step, GameObject go)
        {
            if (step.Target is Component assigned && IsColorable(assigned)) return assigned;

#if LMTE_SUPPORT_UGUI
            var graphic = Pick<Graphic>(step, go);
            if (graphic != null) return graphic;
#endif
            var sprite = Pick<SpriteRenderer>(step, go);
            if (sprite != null) return sprite;
            return Pick<Camera>(step, go);
        }

        static Object ResolveTextTarget(TweenStep step, GameObject go)
        {
#if LMTE_SUPPORT_TMP
            var tmp = Pick<TMP_Text>(step, go);
            if (tmp != null) return tmp;
#endif
#if LMTE_SUPPORT_UGUI
            return Pick<Text>(step, go);
#else
            return null;
#endif
        }

        static Object ResolveMaterialTarget(TweenStep step, GameObject go)
        {
            // A Material asset can be assigned straight to the step, which is the escape hatch
            // for animating a material nothing in the scene owns yet.
            if (step.Target is Material material) return material;

#if LMTE_SUPPORT_UGUI
            var graphic = Pick<Graphic>(step, go);
            if (graphic != null) return graphic;
#endif
            return Pick<Renderer>(step, go);
        }

        static bool IsFadeable(Component component)
        {
            if (component is CanvasGroup or SpriteRenderer) return true;
#if LMTE_SUPPORT_UGUI
            if (component is Graphic) return true;
#endif
            return false;
        }

        static bool IsColorable(Component component)
        {
            if (component is SpriteRenderer or Camera) return true;
#if LMTE_SUPPORT_UGUI
            if (component is Graphic) return true;
#endif
            return false;
        }

        /// <summary>
        /// Returns the step's own target when it is already a <typeparamref name="T"/>,
        /// otherwise the first <typeparamref name="T"/> on the GameObject.
        /// </summary>
        static T Pick<T>(TweenStep step, GameObject go) where T : Component
        {
            if (step.Target is T direct) return direct;
            return go.GetComponent<T>();
        }

        /// <summary>
        /// What a step of <paramref name="type"/> needs on its target, as a sentence. Empty for
        /// types that bind to nothing, and for extensions, whose channels say for themselves.
        /// </summary>
        public static string DescribeRequirement(TweenType type)
        {
            switch (type)
            {
                case TweenType.SizeDelta:
                case TweenType.Pivot:
                case TweenType.Anchors:
                    return "Requires a RectTransform.";
                case TweenType.Fade:
                    return "Requires a CanvasGroup, Graphic or SpriteRenderer.";
                case TweenType.Color:
                    return "Requires a Graphic, SpriteRenderer or Camera.";
                case TweenType.FillAmount:
                    return "Requires an Image.";
                case TweenType.TextReveal:
                case TweenType.TMPCharacter:
                    return "Requires a TMP_Text.";
                case TweenType.TextCounter:
                case TweenType.TextScramble:
                    return "Requires a TMP_Text or UI Text.";
                case TweenType.MaterialProperty:
                    return "Requires a Renderer or Graphic with a material, or a Material asset as the target.";
                case TweenType.VolumeWeight:
                    return "Requires a Volume component.";
                case TweenType.CameraProperty:
                    return "Requires a Camera.";
                case TweenType.AudioVolume:
                case TweenType.AudioPitch:
                    return "Requires an AudioSource.";
                default:
                    return string.Empty;
            }
        }
    }
}
