using System.Runtime.CompilerServices;

// The test assembly verifies internal binding machinery (channel resolution, axis masking and
// the jump arc), which is deliberately not public API.
[assembly: InternalsVisibleTo("LitMotionTweenEditor.Tests")]
