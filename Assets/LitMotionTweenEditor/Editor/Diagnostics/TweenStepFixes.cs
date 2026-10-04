using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
#if LMTE_SUPPORT_UGUI
using UnityEngine.UI;
#endif

namespace LitMotion.TweenEditor.Editor
{
    /// <summary>A one-click repair for a step that cannot bind.</summary>
    internal sealed class TweenStepFix
    {
        public TweenStepFix(string label, string tooltip, Action apply)
        {
            Label = label;
            Tooltip = tooltip;
            Apply = apply;
        }

        public string Label { get; }
        public string Tooltip { get; }
        public Action Apply { get; }
    }

    /// <summary>
    /// The repairs on offer when a step finds nothing to animate.
    /// </summary>
    /// <remarks>
    /// Only fixes that are unambiguous are offered. "Fade needs a CanvasGroup" has one obvious
    /// answer; "Pivot needs a RectTransform" does not, because turning a 3D object into a UI
    /// element is not a repair. Every fix is undoable, since it changes the scene rather than
    /// the step.
    /// </remarks>
    internal static class TweenStepFixes
    {
        static readonly List<TweenStepFix> None = new();

        /// <summary>Fixes for <paramref name="step"/> against <paramref name="fallback"/>, possibly none.</summary>
        public static IReadOnlyList<TweenStepFix> For(TweenStep step, GameObject fallback)
        {
            if (step == null || TweenBindingResolver.IsNonBinding(step.Type)) return None;

            var go = step.Target switch
            {
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                _ => fallback,
            };

            if (go == null) return None;

            var fixes = new List<TweenStepFix>();
            var isUi = go.transform is RectTransform;

            switch (step.Type)
            {
                case TweenType.Fade:
                    // Only on UI: a CanvasGroup on a 3D object would make the step bind and then
                    // change nothing on screen, which is worse than the error it replaced.
                    if (isUi)
                    {
                        AddComponentFix<CanvasGroup>(fixes, go, "fades this object and everything under it");
                    }
                    break;

#if LMTE_SUPPORT_UGUI
                case TweenType.FillAmount:
                case TweenType.Color:
                    if (isUi && go.GetComponent<Graphic>() == null)
                    {
                        AddComponentFix<Image>(fixes, go, "gives this UI object something to draw");
                    }
                    break;
#endif

#if LMTE_SUPPORT_TMP
                case TweenType.TextReveal:
                case TweenType.TextCounter:
                case TweenType.TextScramble:
                case TweenType.TMPCharacter:
#if LMTE_SUPPORT_UGUI
                    if (isUi && go.GetComponent<Graphic>() == null)
#else
                    if (isUi)
#endif
                    {
                        AddComponentFix<TMPro.TextMeshProUGUI>(fixes, go, "gives this UI object text to animate");
                    }
                    break;
#endif

                case TweenType.AudioVolume:
                case TweenType.AudioPitch:
                    AddComponentFix<AudioSource>(fixes, go, "gives this object a sound to control");
                    break;

#if LMTE_SUPPORT_RENDER_PIPELINES
                case TweenType.VolumeWeight:
                    AddComponentFix<Volume>(fixes, go, "gives this object a post-processing volume to blend");
                    break;
#endif

                case TweenType.MaterialProperty:
                    AddMaterialFix(fixes, step, go);
                    break;
            }

            return fixes;
        }

        static void AddComponentFix<T>(List<TweenStepFix> fixes, GameObject go, string why) where T : Component
        {
            if (go.GetComponent<T>() != null) return;

            var name = ObjectNames.NicifyVariableName(typeof(T).Name);
            fixes.Add(new TweenStepFix(
                "Add " + name,
                "Adds a " + name + " to " + go.name + ", which " + why + ". Undoable.",
                () => Undo.AddComponent<T>(go)));
        }

        static void AddMaterialFix(List<TweenStepFix> fixes, TweenStep step, GameObject go)
        {
            if (step.Target is Material) return;
            if (TweenChannelInfo.ResolveMaterial(step, go) != null) return;

            var resolved = TweenBindingResolver.Resolve(step, go, out _);

#if LMTE_SUPPORT_UGUI
            if (resolved is Graphic graphic)
            {
                fixes.Add(new TweenStepFix(
                    "Create Material",
                    "Creates a material asset for " + go.name + " so animating it cannot change every " +
                    "other unstyled UI element. Undoable.",
                    () => AssignNewMaterial(graphic, Shader.Find("UI/Default"))));
                return;
            }
#endif

            if (resolved is Renderer renderer)
            {
                fixes.Add(new TweenStepFix(
                    "Create Material",
                    "Creates a material asset and assigns it to " + go.name + ". Undoable.",
                    () => AssignNewMaterial(renderer, DefaultShader())));
            }
        }

        static Shader DefaultShader()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultMaterial != null) return pipeline.defaultMaterial.shader;

            return Shader.Find("Standard");
        }

        /// <summary>
        /// Creates a material asset beside the project root and assigns it, with undo.
        /// </summary>
        /// <returns>The created material, for tests.</returns>
        internal static Material AssignNewMaterial(Object owner, Shader shader)
        {
            if (owner == null || shader == null) return null;

            var material = new Material(shader) { name = owner.name + " Material" };
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/" + material.name + ".mat");
            AssetDatabase.CreateAsset(material, path);

            Undo.RecordObject(owner, "Assign Material");

            switch (owner)
            {
                case Renderer renderer:
                    renderer.sharedMaterial = material;
                    break;
#if LMTE_SUPPORT_UGUI
                case Graphic graphic:
                    graphic.material = material;
                    break;
#endif
            }

            EditorUtility.SetDirty(owner);
            return material;
        }
    }
}
