using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// Shows and hides an object through tween animations, deactivating it only once the hide
    /// animation has finished.
    /// </summary>
    /// <remarks>
    /// The ordering is the whole point. Deactivating on the same frame as the hide call would
    /// cut the animation off, and leaving the object active forever would keep an invisible
    /// panel eating raycasts. So show activates first and then animates, while hide animates
    /// first and then deactivates.
    /// </remarks>
    [AddComponentMenu("LitMotion/Tween Toggleable")]
    [DisallowMultipleComponent]
    public sealed class TweenToggleable : MonoBehaviour
    {
        [Tooltip("Player holding the animations. Defaults to one on this GameObject.")]
        [SerializeField] TweenPlayer player;

        [Tooltip("Object activated and deactivated. Defaults to this GameObject.")]
        [SerializeField] GameObject root;

        [Tooltip("Animation played to reveal. Leave empty to only activate.")]
        [SerializeField] string showId = TweenAnimationId.Show;

        [Tooltip("Animation played to conceal. Leave empty to only deactivate.")]
        [SerializeField] string hideId = TweenAnimationId.Hide;

        [Tooltip("Deactivate the object once the hide animation finishes.")]
        [SerializeField] bool deactivateWhenHidden = true;

        [Tooltip("State applied when this component starts.")]
        [SerializeField] bool startVisible = true;

        [Space]
        public UnityEvent OnShown = new();
        public UnityEvent OnHidden = new();

        Coroutine pending;
        bool visible = true;

        /// <summary>True when the object is shown, or is in the middle of being shown.</summary>
        public bool IsVisible => visible;

        GameObject Root => root != null ? root : gameObject;

        TweenPlayer Player
        {
            get
            {
                if (player == null) player = GetComponent<TweenPlayer>();
                return player;
            }
        }

        void Start()
        {
            SetVisibleImmediate(startVisible);
        }

        /// <summary>Shows the object, playing the show animation.</summary>
        public void Show() => SetVisible(true);

        /// <summary>Hides the object, playing the hide animation first.</summary>
        public void Hide() => SetVisible(false);

        /// <summary>Flips between shown and hidden.</summary>
        public void Toggle() => SetVisible(!visible);

        /// <summary>Shows or hides the object, animating the transition.</summary>
        public void SetVisible(bool value)
        {
            CancelPending();
            visible = value;

            if (value)
            {
                // Activate first: an inactive object cannot run its own show animation.
                Root.SetActive(true);
                PlayIfPresent(showId);
                OnShown?.Invoke();
                return;
            }

            var handle = PlayIfPresent(hideId);

            if (!deactivateWhenHidden)
            {
                OnHidden?.Invoke();
                return;
            }

            // Nothing to wait for when there is no hide animation, or when this object is not
            // active enough to run a coroutine.
            if (!handle.IsActive() || !gameObject.activeInHierarchy)
            {
                Root.SetActive(false);
                OnHidden?.Invoke();
                return;
            }

            pending = StartCoroutine(DeactivateWhenFinished());
        }

        /// <summary>Applies a state without animating, for initial setup.</summary>
        public void SetVisibleImmediate(bool value)
        {
            CancelPending();
            visible = value;

            var target = Player;
            if (target != null) target.Stop(value ? hideId : showId);

            if (value || deactivateWhenHidden) Root.SetActive(value);
        }

        void CancelPending()
        {
            if (pending == null) return;

            StopCoroutine(pending);
            pending = null;
        }

        IEnumerator DeactivateWhenFinished()
        {
            var target = Player;

            while (target != null && target.IsPlaying(hideId)) yield return null;

            pending = null;

            // A Show may have been requested while the hide was still running.
            if (visible) yield break;

            Root.SetActive(false);
            OnHidden?.Invoke();
        }

        MotionHandle PlayIfPresent(string id)
        {
            if (string.IsNullOrEmpty(id)) return MotionHandle.None;

            var target = Player;
            return target == null ? MotionHandle.None : target.Play(id);
        }
    }
}
