using UnityEngine;

namespace LitMotion.TweenEditor.Samples
{
    /// <summary>
    /// Receiver for the demo rig's Callback and Custom steps, which are UnityEvents and so
    /// need a real method to point at before they do anything observable.
    /// </summary>
    [AddComponentMenu("LitMotion/Demo/Tween Demo Receiver")]
    public sealed class TweenDemoReceiver : MonoBehaviour
    {
        [Tooltip("Degrees of spin applied across a Custom step's full progress.")]
        [SerializeField] float spinDegrees = 360f;

        [Tooltip("How far a Custom step pushes the object up at full progress.")]
        [SerializeField] float riseHeight = 1f;

        Vector3 restPosition;
        bool hasRest;

        /// <summary>Target of a Callback step. Logs so the hit is visible in the console.</summary>
        public void LogHit()
        {
            Debug.Log("[Tween Demo] Callback fired on " + name, this);
        }

        /// <summary>
        /// Target of a Custom step's progress event. Drives rotation and height directly, so
        /// scrubbing the timeline shows the step working for a channel with no built-in binding.
        /// </summary>
        public void OnProgress(float progress)
        {
            if (!hasRest)
            {
                restPosition = transform.localPosition;
                hasRest = true;
            }

            transform.localRotation = Quaternion.Euler(0f, progress * spinDegrees, 0f);
            transform.localPosition = restPosition + new Vector3(0f, progress * riseHeight, 0f);
        }
    }
}
