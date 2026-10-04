#if LMTE_SUPPORT_UGUI
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LitMotion.TweenEditor
{
    /// <summary>
    /// A <see cref="Button"/> that plays named tween animations for its interaction states.
    /// </summary>
    /// <remarks>
    /// Subclasses Button rather than listening to its events so that the full selection state
    /// machine is available: Unity reports Highlighted, Pressed, Selected and Disabled through
    /// <see cref="DoStateTransition"/>, and nothing else exposes them. The inherited colour and
    /// sprite transitions still work, so a button can use both.
    ///
    /// Animations are addressed by id, so the ids a project uses are the ones its designers
    /// type -- there is no fixed set of slots.
    /// </remarks>
    [AddComponentMenu("LitMotion/Tween Button")]
    public class TweenButton : Button
    {
        [Space]
        [Tooltip("Player holding the animations. Defaults to one on this GameObject.")]
        [SerializeField] TweenPlayer player;

        [Tooltip("Played when the button is clicked or submitted. Leave empty to skip.")]
        [SerializeField] string clickId = TweenAnimationId.Click;

        [Tooltip("Played when the pointer enters. Leave empty to skip.")]
        [SerializeField] string hoverId = TweenAnimationId.Hover;

        [Tooltip("Played when the pointer leaves. Leave empty to skip.")]
        [SerializeField] string unhoverId = TweenAnimationId.Unhover;

        [Tooltip("Played while the button is held down. Leave empty to skip.")]
        [SerializeField] string pressId = TweenAnimationId.Press;

        [Tooltip("Played when the button gains keyboard or gamepad selection. Leave empty to skip.")]
        [SerializeField] string selectId = string.Empty;

        [Tooltip("Played when the button becomes non-interactable. Leave empty to skip.")]
        [SerializeField] string disableId = string.Empty;

        SelectionState lastState = SelectionState.Normal;
        bool hasState;

        /// <summary>The player this button drives.</summary>
        public TweenPlayer Player
        {
            get
            {
                if (player == null) player = GetComponent<TweenPlayer>();
                return player;
            }
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);

            // Edit mode would otherwise animate the button while it is merely being authored,
            // and an instant transition is Unity setting up initial state, not an interaction.
            if (!Application.isPlaying || instant) return;

            // Unity re-reports the current state on unrelated changes; only act on real moves.
            if (hasState && lastState == state) return;
            lastState = state;
            hasState = true;

            switch (state)
            {
                case SelectionState.Highlighted:
                    Play(hoverId);
                    break;
                case SelectionState.Pressed:
                    Play(pressId);
                    break;
                case SelectionState.Selected:
                    Play(selectId);
                    break;
                case SelectionState.Disabled:
                    Play(disableId);
                    break;
                default:
                    Play(unhoverId);
                    break;
            }
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            base.OnPointerClick(eventData);
            if (IsInteractable()) Play(clickId);
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            base.OnSubmit(eventData);
            if (IsInteractable()) Play(clickId);
        }

        /// <summary>Plays one of this button's animations by id, if it is configured.</summary>
        public MotionHandle Play(string id)
        {
            if (string.IsNullOrEmpty(id)) return MotionHandle.None;

            var target = Player;
            return target == null ? MotionHandle.None : target.Play(id);
        }
    }
}
#endif
