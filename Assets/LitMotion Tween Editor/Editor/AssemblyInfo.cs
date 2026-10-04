using System.Runtime.CompilerServices;

// The test assembly covers the timeline's snapping maths, per-type step defaults and the
// clipboard round trip. None of those are public API -- they are editor internals -- but they
// carry real logic that deserves tests rather than manual clicking.
[assembly: InternalsVisibleTo("LitMotionTweenEditor.Tests")]
