namespace LitMotion.TweenEditor
{
    /// <summary>
    /// The kind of animation a <see cref="TweenStep"/> performs.
    /// </summary>
    /// <remarks>
    /// Values are assigned explicitly and must never be reused or renumbered: they are
    /// persisted in scenes, prefabs and <c>TweenAnimationAsset</c> presets. Add new types
    /// with the next free number in the owning block.
    /// </remarks>
    public enum TweenType
    {
        // --- Transform (0-19) ---
        Move = 0,
        Scale = 1,
        Rotate = 2,
        Punch = 3,
        Shake = 4,
        Jump = 5,

        // --- RectTransform (20-39) ---
        SizeDelta = 20,
        Pivot = 21,
        Anchors = 22,

        // --- Graphics (40-59) ---
        Fade = 40,
        Color = 41,
        FillAmount = 42,

        // --- Text (60-79) ---
        TextReveal = 60,
        TextCounter = 61,
        TextScramble = 62,
        TMPCharacter = 63,

        // --- Material / rendering (80-99) ---
        MaterialProperty = 80,
        VolumeWeight = 81,

        // --- Camera (100-119) ---
        CameraProperty = 100,

        // --- Audio (120-139) ---
        AudioVolume = 120,
        AudioPitch = 121,

        // --- Timeline primitives (200+) ---
        Interval = 200,
        Callback = 201,
        Custom = 202,

        // --- Extensions (300+) ---

        /// <summary>
        /// A channel defined in user code, identified by <see cref="TweenStep.ExtensionId"/>.
        /// See <see cref="ITweenExtensionChannel"/>.
        /// </summary>
        Extension = 300,
    }
}
