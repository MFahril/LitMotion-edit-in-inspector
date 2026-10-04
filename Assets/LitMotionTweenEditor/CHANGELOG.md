# Changelog

All notable changes to this package. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version numbers follow the
milestones in [ROADMAP.md](ROADMAP.md).

## [0.6.0] - 2026-10-04

First release as a Unity package, installable from a Git URL.

### Added
- `TweenPlayer.PlayAsync`, which completes with `true` when a play finishes and `false` when it
  is stopped, replaced or cancelled.
- `TweenPlayer.Play(id, target)` for pooled objects, with per-target `Stop` and `IsPlaying`.
- `TweenPlayer.Play(id, onComplete)`, a completion callback for one play.
- `Pause`, `Resume`, `PauseAll`, `ResumeAll`, `IsPaused` and `IsPlayingAny`.
- `TweenExtensionRegistry.Prewarm()`, to run the extension scan on a loading screen.
- A PlayMode test suite, and a player smoke test in the development repository.

### Changed
- **A newly added clip starts at a modest size for what it animates.** Distances are world units
  on a 3D object (Move 1, Jump 1 forward and 0.5 high, Punch 0.25, Shake 0.1) and pixels on a UI
  element (Move 100, Jump 100 and 50, Punch 25, Shake 10). Before, every target got pixel-sized
  values, so a new Move sent a 3D object 100 units away.
- **Every default now changes a freshly made object.** Before, several went to values a new
  object already has:

  | Clip | Default before | Default now |
  |---|---|---|
  | Color | white | soft red |
  | Camera | field of view 60 | 45 |
  | Audio pitch | 1 | 1.5 |
  | Volume weight | 1 | 0, fading out |
  | Fill amount | to 1 | fills from 0 |
  | Pivot | (0.5, 0.5) | the left edge |
  | Anchors | (0.5, 0.5) | the top centre |
  | Rotate | half turn | quarter turn |
- The Jump height slider's range follows the target's units (0–4 or 0–400), and stretches to
  include any existing value instead of clamping it.
- A one-step animation runs as a single LitMotion motion instead of a sequence, which makes it
  about 2.8× cheaper per frame.
- No channel allocates per frame. The exception is a uGUI `Text` counter, which allocates only
  when its shown number changes.
- `Play()` captures values only for the Rewind kill behavior.
- A MaterialProperty step gives its target its own material instance at runtime, and the instance
  is destroyed with the object. A UI Graphic's material asset is never written.
- *Tools → LitMotion → Generate Tween Presets* writes into the project when the package is
  installed read-only.

### Fixed
- An animation of zero length, such as one made only of Callback steps, now fires its steps at
  runtime. Before, it fired them only in the editor preview.
- TextReveal no longer rebuilds the TMP mesh on every frame.
- A callback that played or stopped animations while a stop was underway could corrupt the
  player's bookkeeping.

## [0.1.0]

The editor tooling from milestones M1–M4: timeline, preview, 24 tween types, presets, the tween
editor window and extension channels.
