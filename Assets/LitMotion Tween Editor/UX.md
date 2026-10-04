# Authoring UX — what the tool should feel like

The complete list of what an author needs from this package, grouped by the job they are doing.
Kept as a checklist rather than prose so it can be worked through and argued with.

| Mark | Meaning |
|---|---|
| ✅ | Shipped |
| 🔨 | Planned, not built yet |
| ⬜ | Wanted, not scheduled |

Every item planned for M4 has shipped; what is left is ⬜. Milestone history and the reasoning
behind each decision live in [ROADMAP.md](ROADMAP.md).

---

## A. Reading a timeline at a glance

- ✅ One lane per step, coloured by family, with a label and an enable toggle in a fixed header column
- ✅ Ruler with zoom-aware tick spacing, a duration readout, and an `∞` badge on endless steps
- ✅ **Callback markers on the ruler** — a callback is a zero-length clip, so on a busy timeline
  it is a sliver that is easy to lose
- ✅ **Trim handles appear on the clip under the pointer**, making the grab zone visible without
  cluttering a timeline at rest
- ✅ **Vertical scrolling with a maximum height**, so a twenty-step animation no longer pushes the
  rest of the inspector off screen. The window lifts the limit, since it has the room
- ✅ **A lane filter field and a "show only enabled" toggle**, matching on step name or type
- ✅ **A type icon on each clip**, so a clip says what it is when it is too narrow for a label,
  and to a reader who cannot tell the family colours apart
- ✅ **The duration drawn on the clip**, once it is wide enough to hold it
- ✅ **A warning badge on a clip that cannot bind**, with the reason on hover — checked up front,
  without building or playing anything
- ⬜ Loop-region shading behind the lanes
- ⬜ Lanes grouped by target object, with collapsible group headers

## B. Clicking a clip and tweaking it

This is the section the whole milestone was asked for.

- ✅ Click a clip and its controls appear below the timeline, or beside it in the window
- ✅ **The right widget for the channel** — a float for alpha, fill, field of view and volume; a
  Vector2 for pivot, anchors and size; a Vector3 for position, scale and rotation; a colour
  swatch for colours; a Vector4 only for shader vectors, which is the one case where the fourth
  component means something
- ✅ **Component labels that match the channel** — X/Y/Z, R/G/B/A, W/H for a rect size
- ✅ **Unit suffixes** — `deg`, `px`, `x`, `0-1`
- ✅ **Normalized channels get a 0-1 slider** rather than a free number box
- ✅ **One segmented Mode control** — Absolute · Current · Relative · Offset — replacing three
  loose booleans an author had to combine correctly in their head, with a line of plain English
  under it saying what the chosen mode will do
- ✅ **Field labels follow the mode** — "To" becomes "By" for a relative step and "Offset" for an
  offset one, and "Strength" for punch and shake where the end value is not a destination
- ✅ **⊕ Grab** writes the target's live value into an endpoint
- ✅ **↗ Apply** pushes an endpoint onto the object so it can be seen without playing
- ✅ **⇄ Swap** exchanges the two endpoints
- ✅ **Axis mask as chips**, one per component the channel actually has, with a note when an
  empty mask means "all axes" rather than "nothing"
- ✅ **Sliders with real ranges** — frequency 1–60, damping 0–1, arcs 1–10, jump height, stagger
- ✅ **Shader property dropdown** read off the material that will actually be written, grouped by
  type, setting the step's value kind to match so a colour cannot be tweened as a float
- ✅ **A live sample for the counter format**, because `{0:N0}` and `{0:0}` are indistinguishable
  as text
- ✅ **A live sample for the scramble**, computed by running LitMotion's own string motion to its
  halfway point, so "Uppercase" and "Numerals" show what they actually fill the gap with
- ✅ **A dice button** to re-roll a shake seed
- ✅ **A status line** naming what the step resolved to — Fade finding a CanvasGroup rather than
  the Image you were looking at is exactly the thing that needs saying out loud
- ✅ **Fix buttons beside a binding failure** — Add Canvas Group (UI only) · Add Image · Add Text
  · Add Audio Source · Add Volume · Create Material. Only unambiguous repairs are offered, and
  every one is undoable
- ✅ **Nothing behind a foldout.** One clip is shown at a time, so all of its controls fit, and a
  control you have to go looking for is a control you forget exists
- ⬜ Drag-scrub on numeric labels, and expression entry (`0.3*2`)

## C. Easing

- ✅ A curve graph drawn from `EaseUtility.Evaluate`, so the drawn curve is the curve that plays
- ✅ Overshoot-aware vertical range, with the 0 and 1 guides left in place to show by how much
- ✅ **A searchable gallery of all 31 eases as curve thumbnails**, grouped by family, with
  favourites and recents, type-to-filter from the first keystroke, and right-click to star
- ✅ **Clicking the curve graph opens the gallery** — the graph is the honest control, so it is
  the one you press
- ✅ **A ghost of the outgoing curve** behind the new one, so changing easing is a comparison
- ✅ **A dot riding the curve** during preview, which is what makes an overshoot legible
- ✅ Custom `AnimationCurve` reachable from the gallery as its own choice
- ⬜ A project ease library ("our standard panel ease") saved in settings

## D. Manipulating the timeline

- ✅ Drag to move, trim either edge, snap to the grid, to neighbours and to the playhead
- ✅ Snapping measured in pixels, so the magnet feels identical at every zoom
- ✅ One undo entry per drag gesture, not one per pointer move
- ✅ **Right-click menu** on clips and on empty space: duplicate · copy · paste at playhead ·
  delete · split at playhead · start at playhead · trim either edge to the playhead · align
  starts · align ends · distribute · mute · solo · reset to type defaults · save selection as
  preset · move up and down. Items that cannot apply are disabled rather than hidden, so the
  menu teaches what is possible
- ✅ **Keyboard**, with the shortcut shown in the matching tooltip and on the **Keys** sheet:

  | Key | Does |
  |---|---|
  | `Space` | Play / pause the preview |
  | `←` `→` | Nudge the selection one grid step; `Shift` for ten |
  | `Ctrl+D` | Duplicate |
  | `Del` / `Backspace` | Delete |
  | `Ctrl+C` / `Ctrl+V` | Copy, and paste at the playhead |
  | `Ctrl+A` | Select every visible clip |
  | `S` | Split at the playhead |
  | `M` | Mute or unmute |
  | `F` | Zoom to fit |
  | `Home` / `End` | Scrub to the start or the end |
  | `Alt+↑` / `Alt+↓` | Reorder the lane |

  Bare letters are shortcuts, so they stand down while a text field has focus — typing "shake"
  into the filter must not split, mute and zoom along the way
- ✅ **Multi-select** by Ctrl or Shift click and by rubber band, with group move and group nudge
  that clamp **as one**, so relative timing survives a drag into the left edge
- ✅ **Align starts, align ends, distribute evenly**; align moves clips rather than resizing them
- ✅ **A guide line at the moment of snapping**, shown only for a real latch and never for the
  fixed grid, which catches almost every drag and would mean a line permanently on screen
- ✅ **Zoom to fit**, and **cursor-anchored zoom** so the clip under the pointer stays under it
- ✅ **Shift+wheel to pan**
- ✅ **Reorder lanes** from the menu or with Alt+↑/↓
- ⬜ Reorder lanes by dragging the header rows
- ⬜ Middle-drag to pan
- ⬜ Drag a step from one animation's timeline to another

## E. Preview and feedback

- ✅ Preview runs the production code path, so what you see is what ships
- ✅ Backward scrubbing, and exact restoration of every value on stop
- ✅ **Auto-preview** — while a preview is running, selecting a clip parks the playhead at its
  start and edits show up immediately. Starting a preview stays explicit: clicking a clip must
  never begin writing to the scene on its own
- ✅ **An Auto toggle** in the preview bar for authors who want none of that
- ✅ **Solo preview** of one step, so a single clip can be judged without the rest playing over it
- ✅ **Loop, playback speed and frame-step** in the preview bar — preview-only, so watching a
  one-shot animation on repeat at quarter speed never changes what ships
- ✅ **A typed time field** — type 0.35 and press Enter to land exactly there
- ✅ **A binding status readout** — "7 OK · 1 failing" — that opens an inline list of the failures,
  each with a Select Step button and its fixes. Inline rather than a popover, so it stays open
  while the author works through it
- ✅ **The Custom-step restore limitation said in the UI**: on the clip and in the status list
- ✅ **One preview at a time**: starting one in the window stops the inspector's, and a view of the
  same object says where the preview is running instead of fighting it
- ⬜ Record a preview to a GIF

## F. Managing animations

- ✅ Add, remove and switch animations; full settings for loops, speed, blend, kill and unscaled time
- ✅ The 13 built-in ids one click away beside a free-text field, so the id set stays open
- ✅ Save as Preset / Load Preset
- ✅ **Chips instead of a dropdown**, so the whole set is visible at once, each with its own play
  button; double-click to rename, right-click to duplicate, reorder or delete
- ✅ **"+ From Preset"** when adding an animation, and **dropping a preset** on the chips adds it
- ✅ **Empty states with a primary action** — a player with no animations says so and offers Add
  Animation and Add From Preset; an empty timeline offers Add Step
- ⬜ A colour tag per animation

## G. Reuse — presets and library

- ✅ 14 presets generated by a menu item, so they survive a change to the step model
- ✅ Every preset is object-agnostic: no step holds a target, and slide-ins land on whatever
  position the object already has
- ✅ A preset has its own inspector with the same timeline and a borrowed preview target
- ✅ An inline animation overrides a preset with the same id, so one object can differ without
  editing the shared asset
- ✅ **A preset browser** (Window → LitMotion → Tween Presets): searchable by name, folder, step
  type or ease, with a curve thumbnail of each preset's main ease, and drag onto a player's
  animation chips to apply
- ✅ **"Save Selection as Preset"**, not only the whole animation — shifted so the earliest clip
  starts at zero
- ⬜ Preset variants and per-instance overrides

## H. Errors, guard rails, discoverability

- ✅ Per-step build errors that name the step, the object and the requirement
- ✅ A warning when a step asks to loop forever inside a sequence, which cannot be scheduled
- ✅ The Runtime-Only UnityEvent trap detected, explained and fixed in one click
- ✅ **Fix buttons for the common binding failures**
- ✅ **A tooltip on every button**, enforced by a test that selects a step of every type and
  walks the whole view
- ✅ **A keymap cheat-sheet** behind the Keys button, listing keys and pointer gestures
- ✅ **A dismissible hint strip**, which can be brought back from Preferences
- ⬜ A sample scene shipped inside the package rather than only in this project

## I. Settings and theming

- ✅ **A Preferences page** (Preferences → LitMotion Tween Editor): default duration and ease for
  new steps, snapping on/off, interval and magnet distance, opening zoom and zoom range, lane
  height, timeline height, auto-preview, the hint strip, and clip colours per family
- ✅ **`TweenTimelineStyles` reads through the settings**, whose defaults are the old constants,
  so nothing looks different until someone changes something
- ✅ Settings are per user, not per project, so one author's taste does not rewrite the team's
- ⬜ Export and import a settings profile for a team

Undo granularity was on the original list and was deliberately left out: one undo entry per
gesture is an invariant of the timeline, not a preference.

## J. Extensibility

- ✅ **`TweenChannelInfo`** — a public description of a channel (value shape, component labels,
  unit, readability) plus live read, write and material resolution. This is what the inspector
  draws from, and it is public precisely so a channel defined in someone else's assembly can
  answer the same questions
- ✅ **Extension channels**: implement `ITweenExtensionChannel`, mark it
  `[TweenExtensionChannel("your.id")]`, and it appears in the add menu. Build, preview, snapshot
  and restore, value modes, axis masking, grab and apply all work, with no further code. A
  worked example animating `Light` intensity and colour ships in `Samples/Extensions`
- ✅ **A custom inspector section per extension**: derive from `TweenExtensionInspector` and mark
  it `[TweenExtensionInspector("your.id")]`
- ⬜ Extension presets shipped by a third party

## K. Runtime and API ergonomics

- ✅ `Play` / `Stop` / `Complete` / `Restart` / `IsPlaying`, inline or asset lookup, blend and kill
- ✅ `TweenButton` mapping interaction states to ids; `TweenToggleable` ordering show and hide
  around activation
- ⬜ `await player.PlayAsync(id)`, a fluent code-side builder, and a `Play(id, target)` override
  for pooled objects
