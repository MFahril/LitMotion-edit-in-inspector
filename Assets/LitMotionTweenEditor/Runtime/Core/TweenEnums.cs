using System;

namespace LitMotion.TweenEditor
{
    /// <summary>Coordinate space for transform-based steps.</summary>
    public enum TweenSpace
    {
        /// <summary>Local space. On a RectTransform this resolves to anchoredPosition.</summary>
        Local = 0,
        World = 1,
    }

    /// <summary>
    /// Which components of a vector a step writes. Unselected axes keep their live value.
    /// </summary>
    [Flags]
    public enum TweenAxis
    {
        None = 0,
        X = 1 << 0,
        Y = 1 << 1,
        Z = 1 << 2,
        W = 1 << 3,
        XY = X | Y,
        XYZ = X | Y | Z,
        All = X | Y | Z | W,
    }

    /// <summary>Transform channel targeted by Punch, Shake and Jump steps.</summary>
    public enum TweenChannel
    {
        Position = 0,
        Scale = 1,
        Rotation = 2,
    }

    /// <summary>Unit revealed progressively by a TextReveal step.</summary>
    public enum TweenTextUnit
    {
        Characters = 0,
        Words = 1,
        Lines = 2,
    }

    /// <summary>Which RectTransform anchor pair an Anchors step writes.</summary>
    public enum TweenAnchorTarget
    {
        Both = 0,
        Min = 1,
        Max = 2,
    }

    /// <summary>Value kind addressed by a MaterialProperty step.</summary>
    public enum TweenMaterialPropertyKind
    {
        Float = 0,
        Color = 1,
        Vector = 2,
    }

    /// <summary>Property animated by a CameraProperty step.</summary>
    public enum TweenCameraProperty
    {
        FieldOfView = 0,
        OrthographicSize = 1,
        BackgroundColor = 2,
        NearClipPlane = 3,
        FarClipPlane = 4,
    }

    /// <summary>Per-character attribute animated by a TMPCharacter step.</summary>
    public enum TweenTMPCharChannel
    {
        Position = 0,
        Scale = 1,
        Rotation = 2,
        Color = 3,
        Alpha = 4,
    }

    /// <summary>What happens to an already-running animation when it is played again.</summary>
    public enum TweenBlendMode
    {
        /// <summary>Stop the running instance according to its kill behavior, then start fresh.</summary>
        Override = 0,
        /// <summary>Leave the running instance alone and start another alongside it.</summary>
        Additive = 1,
    }

    /// <summary>How a running animation is torn down when it is overridden or stopped.</summary>
    public enum TweenKillBehavior
    {
        /// <summary>Cancel in place, leaving values wherever they are.</summary>
        Cancel = 0,
        /// <summary>Jump to the end value, then release.</summary>
        Complete = 1,
        /// <summary>Cancel, then restore the values captured when the animation started.</summary>
        Rewind = 2,
    }
}
