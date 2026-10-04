using UnityEngine;
using UnityEngine.Scripting;

namespace LitMotion.TweenEditor.Samples
{
    /// <summary>
    /// A worked example of an extension channel: animates <see cref="Light.intensity"/>, which
    /// none of the built-in tween types cover.
    /// </summary>
    /// <remarks>
    /// This is the whole of it. Once this class exists, "Lighting/Light Intensity" appears in
    /// the timeline's add menu, and the step gets easing, looping, the value modes, grab and
    /// apply, preview and exact restore without any further code.
    ///
    /// <see cref="PreserveAttribute"/> keeps IL2CPP code stripping from removing a class that is
    /// only ever found by reflection.
    /// </remarks>
    [Preserve]
    [TweenExtensionChannel("com.litmotion.samples.light-intensity", Category = "Lighting")]
    public sealed class LightIntensityChannel : ITweenExtensionChannel
    {
        public string DisplayName => "Light Intensity";
        public TweenValueShape Shape => TweenValueShape.Float;
        public string Unit => "";

        public bool TryResolve(TweenStep step, GameObject gameObject, out Object target, out string error)
        {
            // Honour an explicitly assigned Light, otherwise use the one on the GameObject.
            target = step.Target as Light ?? gameObject.GetComponent<Light>();
            error = target == null ? "Requires a Light." : null;
            return target != null;
        }

        public bool TryRead(Object target, out Vector4 value)
        {
            value = Vector4.zero;
            if (target is not Light light) return false;

            value.x = light.intensity;
            return true;
        }

        public bool TryWrite(Object target, Vector4 value)
        {
            if (target is not Light light) return false;

            light.intensity = Mathf.Max(0f, value.x);
            return true;
        }
    }

    /// <summary>
    /// The colour counterpart of <see cref="LightIntensityChannel"/>: a channel whose shape is a
    /// colour gets a colour swatch in the inspector and RGBA interpolation for free.
    /// </summary>
    [Preserve]
    [TweenExtensionChannel("com.litmotion.samples.light-color", Category = "Lighting")]
    public sealed class LightColorChannel : ITweenExtensionChannel
    {
        public string DisplayName => "Light Color";
        public TweenValueShape Shape => TweenValueShape.Color;
        public string Unit => "";

        public bool TryResolve(TweenStep step, GameObject gameObject, out Object target, out string error)
        {
            target = step.Target as Light ?? gameObject.GetComponent<Light>();
            error = target == null ? "Requires a Light." : null;
            return target != null;
        }

        public bool TryRead(Object target, out Vector4 value)
        {
            value = Vector4.zero;
            if (target is not Light light) return false;

            value = light.color;
            return true;
        }

        public bool TryWrite(Object target, Vector4 value)
        {
            if (target is not Light light) return false;

            light.color = value;
            return true;
        }
    }
}
