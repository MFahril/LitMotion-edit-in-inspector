namespace LitMotion.TweenEditor
{
    /// <summary>
    /// A concrete property a step writes to, resolved from the step's type and sub-options.
    /// </summary>
    /// <remarks>
    /// This exists so that several step types can share one physical channel. A Move step in
    /// local space and a Punch step on the Position channel both resolve to
    /// <see cref="LocalPosition"/>, which is what lets snapshot capture deduplicate them and
    /// restore the value exactly once.
    /// </remarks>
    internal enum TweenChannelKey
    {
        None = 0,

        // Transform
        LocalPosition,
        WorldPosition,
        AnchoredPosition3D,
        LocalScale,
        LocalEulerAngles,
        WorldEulerAngles,

        // RectTransform
        SizeDelta,
        Pivot,
        AnchorMin,
        AnchorMax,

        // Graphics
        CanvasGroupAlpha,
        GraphicColor,
        GraphicAlpha,
        SpriteColor,
        SpriteAlpha,
        ImageFillAmount,

        // Camera
        CameraFieldOfView,
        CameraOrthographicSize,
        CameraBackgroundColor,
        CameraNearClipPlane,
        CameraFarClipPlane,

        // Audio
        AudioVolume,
        AudioPitch,

        // Rendering
        VolumeWeight,

        // Material properties. Which property is addressed comes from the channel context,
        // so one key per value kind is enough.
        MaterialFloat,
        MaterialColor,
        MaterialVector,

        // Text
        /// <summary>TMP's maxVisible* counters, carried as a 0-1 reveal fraction.</summary>
        TmpMaxVisible,
        /// <summary>A number formatted into a text component's string.</summary>
        TextNumber,
        /// <summary>The whole string of a text component.</summary>
        TextString,
        /// <summary>Per-character TMP vertex state, owned by LitMotion's TMP animator.</summary>
        TmpCharacter,

        // Extensions
        /// <summary>
        /// A channel defined in user code. Which one comes from the channel context, as the
        /// material property does.
        /// </summary>
        Extension,
    }

    /// <summary>
    /// The value type a channel traffics in. Determines which LitMotion adapter the step
    /// builder instantiates.
    /// </summary>
    internal enum TweenValueKind
    {
        Float = 0,
        Vector2,
        Vector3,
        Vector4,
    }
}
