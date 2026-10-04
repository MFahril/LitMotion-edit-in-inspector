namespace LitMotion.TweenEditor
{
    /// <summary>
    /// The built-in animation names a <c>TweenPlayer</c> recognizes.
    /// </summary>
    /// <remarks>
    /// Ids are matched as plain strings so that custom names work alongside these, and so a
    /// renamed built-in never silently stops matching. Use <see cref="TweenAnimationId.Show"/>
    /// and friends rather than retyping the literals.
    /// </remarks>
    public static class TweenAnimationId
    {
        public const string Show = "Show";
        public const string Hide = "Hide";
        public const string Click = "Click";
        public const string Hover = "Hover";
        public const string Unhover = "Unhover";
        public const string Press = "Press";
        public const string Release = "Release";
        public const string Select = "Select";
        public const string Deselect = "Deselect";
        public const string Enable = "Enable";
        public const string Disable = "Disable";
        public const string Focus = "Focus";
        public const string Blur = "Blur";

        /// <summary>All built-in ids, in the order the inspector offers them.</summary>
        public static readonly string[] BuiltIn =
        {
            Show, Hide, Click, Hover, Unhover, Press, Release,
            Select, Deselect, Enable, Disable, Focus, Blur,
        };
    }
}
