# LitMotion Tween Editor — Roadmap

A LitMotion-native "Tween in Inspector": author, chain, preview and scrub tween animations
entirely from the Unity Inspector.

- **Unity** 6000.6.3f1 (C# 9) · **LitMotion** 2.0.2 · **URP** 17.6.0 · **uGUI** 2.6.0
- **Dependencies:** LitMotion + Unity only. No third-party packages.
- **Status:** M1-M5 complete and verified in-editor. **367 / 367 EditMode tests pass.**

| Milestone | Scope | Status |
|---|---|---|
| **M1** | Data model, binding, sequencing, runtime player, edit-mode preview | ✅ Complete, verified |
| **M2** | UI Toolkit timeline, ease-curve graph, playhead scrubbing, copy/paste | ✅ Complete, verified |
| **M3** | Remaining 5 tween types, presets, named ids, TweenButton, TweenToggleable | ✅ Complete, verified |
| **M4** | Authoring UX overhaul, customization, modularity — see [UX.md](UX.md) | ✅ Complete, verified |
| **M5** | Runtime performance: per-frame GC, text hot paths, materials, `Play()` cost, one-step fast path | ✅ Complete, verified |

---

## Why this exists

The reference asset, [Tween In Inspector](https://assetstore.unity.com/packages/tools/gui/tween-in-inspector-animate-ui-in-few-clicks-371488)
by AnkleBreaker ($29.99), abstracts over three tween backends (its own, DOTween, PrimeTween).
This workflow is LitMotion-only, so that abstraction is pure overhead — and it is where most of
that asset's 117 files go. **Dropping the backend abstraction is the one feature deliberately
cut.** Everything else is matched or exceeded.

Reference feature set, for scoring: 17 tween types · UI Toolkit timeline with ease-curve preview
· live Edit Mode playback · copy/paste · 14 ScriptableObject presets · 13 named animation ids +
custom · Override/Additive blending · kill behavior · Restart/PingPong loops · unscaled time ·
3 components · 32 eases.

---

## The load-bearing design decision

**Preview runs the production code path.** It does not reimplement easing or loop maths for the
editor. It asks `TweenAnimationRunner` for the same `LSequence` the runtime builds, schedules
the driver motion on a *private* `ManualMotionDispatcher`, and scrubs `driverHandle.Time = t`.

```csharp
var seq = LSequence.Create();
foreach (var step in animation.Steps)
    seq.Insert(step.StartTime, TweenStepBuilder.Build(step, target));

var driver = seq.Run(cfg => {
    cfg.WithLoops(animation.Loops, animation.LoopType);
    cfg.WithScheduler(previewDispatcher.Scheduler);   // null at runtime
});
driver.Preserve();   // else scrubbing past the end releases the handle
```

Three invariants follow. **Keep them.**

1. **Never port LitMotion internals.** `MotionData.Update` — the progress/delay/loop/ease
   pipeline — is `internal`. Reusing the public `LSequence` + `handle.Time` path is what keeps
   preview bit-identical to runtime across LitMotion versions. `EaseUtility.Evaluate` is public
   and is used *only* to draw the curve graph.
2. **Always a private `ManualMotionDispatcher`, never `.Default`.** A private instance is never
   advanced by anything else, so preview owns the clock and cannot disturb the user's motions.
   It is also why `PlaybackSpeed` is applied in the preview loop without double-application:
   the dispatcher is never `Update()`d, so the driver moves only via absolute `Time` assignment.
3. **Teardown must restore on every exit path**, including the forgettable ones: assembly
   reload, entering play mode, inspector destroyed, target deleted mid-scrub.

### What LitMotion 2.0.2 already gives us

| Need | API |
|---|---|
| Edit-mode playback | `EditorMotionDispatcher`, auto-hooked via `[InitializeOnLoadMethod]` |
| Scrubbing | `MotionHandle.Time` — public setter → re-evaluates and re-applies immediately |
| Sequencing | `LSequence` → `Append`/`Join`/`Insert`/`AppendInterval`, one scrubbable driver |
| Manual clock | `ManualMotionDispatcher` — instantiable per preview |
| Ease graph | `EaseUtility.Evaluate` — public, Burst, 31 eases + `AnimationCurve` |
| Extensibility | `IMotionAdapter.Evaluate` is **public** → real custom adapters |

---

## M1 — Core ✅

**Runtime 2,323 lines** · 8 files · 47 of the 70 tests

```
Assets/LitMotion Tween Editor/
  Runtime/
    Core/        TweenType.cs  TweenStep.cs  TweenAnimation.cs
                 TweenAnimationId.cs  TweenEnums.cs
    Binding/     TweenStepBuilder.cs       # step -> MotionHandle (central dispatch)
                 TweenAnimationRunner.cs   # animation -> LSequence driver
                 TweenBindingResolver.cs   # component auto-resolution
                 TweenChannelAccessor.cs   # read + write per channel
                 TweenChannelWriter.cs  TweenChannel.cs  TweenValueSnapshot.cs
    Adapters/    JumpOptions.cs  JumpMotionAdapters.cs
    Components/  TweenPlayer.cs
  Editor/
    TweenPlayerEditor.cs
    Preview/     TweenPreviewController.cs  TweenSceneDirtyGuard.cs
    Drawers/     TweenStepDrawer.cs  TweenStepFields.cs
  Tests/Editor/  JumpArcTests  TweenChannelTests  TweenSnapshotTests  TweenTimingTests
```

### Verified in the running editor
- Compiles clean — zero errors or warnings from this package.
- **47 / 47 tests pass** (EditMode, 0.35s).
- **Preview exact**, including backward scrubbing. Relative Move +10 on X plus Fade to
  transparent, linear, from `(1,2,3)` / alpha 1:
  `t=0 → (1,2,3)/1` · `t=0.5 → (6,2,3)/0.5` · `t=1 → (11,2,3)/0` ·
  `t=0.25 backwards → (3.5,2,3)/0.75` · **after Stop → (1,2,3)/1**
- **19 of 24 tween types build**; `Anchors` set to Both correctly emits 2 handles; the resolver
  picks the right component each time (CanvasGroup on a panel, Graphic on an Image,
  SpriteRenderer on a sprite).

### Design notes worth not re-litigating
- **`TweenStep` is a flat `[Serializable] class`** with a `TweenType` discriminator and shared
  payload — not a `SerializeReference` hierarchy. Clean prefab overrides, trivial copy/paste and
  presets, no managed allocation per step. A *class* not a struct because C# field initializers
  only apply to classes; a `List<struct>` element is zero-filled, which would make every newly
  added step arrive with `Duration = 0, Loops = 0`.
- **Own channel read/write table, not LitMotion's `BindTo*` extensions.** Three features need to
  *read* a live value, which `BindTo*` cannot: `FromCurrent`, preview snapshot/restore, and
  per-axis masking. Reader and writer sit adjacent in `TweenChannelAccessor` so they cannot drift.
- **`TweenType` values are explicitly numbered in blocks.** They are persisted in scenes,
  prefabs and presets. Never renumber or reuse. Deferred types stay in the enum rather than being
  removed and re-added.
- **Optional integrations are referenced by GUID** in the asmdefs (`2bafac87…` UnityEngine.UI,
  `6055be8e…` Unity.TextMeshPro, `df380645…` Unity.RenderPipelines.Core.Runtime). Unity silently
  drops an unresolvable GUID reference, so the package still compiles without uGUI/TMP/SRP; a
  by-name reference to a missing assembly is a hard error. Note that `versionDefines` only
  defines a symbol — it does **not** add a reference. Getting this wrong is what caused the
  `CS0246: 'Volume' could not be found` build break.
- **Test assemblies need `includePlatforms: ["Editor"]`** to be EditMode tests. With `[]` Unity
  classifies them as PlayMode and an EditMode run silently finds 0 tests.
- **`Object.GetInstanceID()` is a compile error** (CS0619) on Unity 6000.5+. `TweenValueSnapshot`
  keys targets by object reference, which needs no version fork.

---

## M2 — Timeline and preview UI ✅

**11 editor files, 3,040 lines** · **70 tests** (47 from M1 + 23 new)

The flat step list is gone. Steps are now clips on a timeline, and the selected clip's fields
appear beneath it.

```
Editor/
  TweenPlayerEditor.cs             # animation bar, timeline, preview bar, step inspector
  Timeline/
    TweenTimelineView.cs           # tracks, toolbar, scroll, playhead, structural edits
    TweenClipElement.cs            # one clip: move, trim head, trim tail
    TweenTimelineContext.cs        # pixels-per-second + snapping
    TweenTimelineRuler.cs          # ticks, labels, click-drag scrubbing
    TweenTimelineStyles.cs         # colours and metrics, theme-aware
    EaseCurveElement.cs            # Painter2D curve graph
  Drawers/
    TweenStepDefaults.cs           # per-type defaults for newly added steps
    TweenStepClipboard.cs          # JSON copy/paste via the system clipboard
```

### What it does
- **Clips** drag to move, and the head and tail trim independently. Trimming the head holds the
  tail still, so a clip's out-point stays put while its in-point moves.
- **Snapping** to a 0.05s grid, to neighbouring clip edges, and to the playhead. The magnet
  threshold is measured in *pixels*, not seconds, so it feels identical at every zoom level.
  Clip edges and the playhead take priority over the grid, since lining up with another clip is
  nearly always the intent. Toggleable.
- **Zoom** by slider or mouse wheel, 40–1600 px/s. Ruler tick intervals step through a
  1 / 2.5 / 5 sequence per decade so labels never collide.
- **Ruler** click or drag scrubs the preview.
- **Ease curve graph** drawn with `Painter2D` from `EaseUtility.Evaluate`, the same public
  function the motion pipeline uses, so the drawn curve is the curve that plays. The vertical
  range is measured from the samples rather than assumed, so overshooting eases (Back, Elastic)
  are visible as a departure from the 0 and 1 guide lines instead of being clipped.
- **Clip colour by tween family** (transform / vibration / rect / graphics / text / rendering /
  camera / audio), so a timeline is scannable without reading every label.
- **Add, delete, duplicate, copy, paste.** The add menu is grouped by family and lists the 5
  unimplemented types as disabled "(coming soon)" entries, so the roadmap is visible in the UI
  rather than looking like missing features.
- **Toolbar readout** of total animation duration, `∞` when a step loops forever. An infinite
  step also carries an `∞` badge on its clip, because a step loop is clamped to one pass inside
  a sequence and that should never be a silent surprise.

### Implementation notes
- **One drag is one undo entry.** `Undo.RegisterCompleteObjectUndo` is taken once on pointer
  down, then every pointer-move writes with `ApplyModifiedPropertiesWithoutUndo`. Applying with
  undo per move would bury the history under hundreds of entries.
- **All edits go through `SerializedProperty`**, never the step objects, so Undo, prefab
  overrides and multi-object editing behave. Geometry is *read* from the live objects, which is
  safe only because `TweenStep` is a class — the instances survive `ApplyModifiedProperties`, so
  a drag is not invalidated mid-gesture.
- **The preview rebuilds once per drag, not per frame.** `TweenTimelineView.IsEditing` suppresses
  the inspector's `TrackSerializedObjectValue` rebuild while a drag is live; `EditFinished` fires
  the single rebuild on pointer up.
- **Styling is C#, not USS.** A USS asset has to be found through `AssetDatabase`, which breaks
  when the folder is renamed, moved or packaged. `TweenTimelineStyles` keeps it to one edit point
  and resolves every colour against `EditorGUIUtility.isProSkin`.
- **New steps get per-type defaults.** Unity's `InsertArrayElementAtIndex` *copies the preceding
  element*, so without `TweenStepDefaults` a new Fade would inherit a Move's end vector and
  animate nothing — which reads as a bug. Defaults are chosen to be visible immediately on preview.
- **Duplicate goes through Unity's array duplication** rather than a manual clone, because that
  is the only way to carry `UnityEvent` listeners across. Clipboard paste cannot: `JsonUtility`
  does not serialize event listeners or object references, so target and event wiring do not
  travel with a copied step.
- **Clipboard payloads are marker- and version-tagged** and validated before use, so an
  unrelated clipboard (a file path, JSON from a browser) is ignored rather than half-applied.

### Verified in the running editor
- Compiles clean — zero errors or warnings from this package.
- **70 / 70 tests pass** (EditMode, 0.50s). The 23 new ones cover snapping priority, the
  zoom-invariant threshold, dragged-step exclusion, ruler tick selection, per-type defaults, and
  the clipboard round trip including foreign and malformed payloads.
- **The inspector builds.** `CreateEditor` → `CreateInspectorGUI` on a player with 5 steps
  returns a 119-element tree containing 1 timeline, **5 clips** (one per step), 1 ruler,
  1 ease graph, 10 property fields, 9 buttons — no exception and nothing logged.

### Not machine-verifiable
Pointer-driven behaviour — the feel of dragging, trimming and snapping — cannot be exercised
programmatically in any meaningful way. Worth a few minutes of clicking: drag a clip, trim both
ends, confirm one Ctrl+Z undoes the whole gesture, and check the snap magnet feels right at both
zoom extremes.
---


## M3 — Full parity and beyond ✅

**50 files, 9,693 lines** (Runtime 3,370 / Editor 3,980 / Tests 2,284 / Samples 43) ·
**126 tests** · 14 generated presets. All 24 tween types are implemented; nothing reports
"not supported yet" any more.

### The 5 remaining tween types

| Type | How it is built |
|---|---|
| `MaterialProperty` | Float / Color / Vector keyed by `Shader.PropertyToID`, through the shared channel table. A `Material` asset can be the step's Target directly. |
| `TextReveal` | TMP `maxVisibleCharacters` / `Words` / `Lines`, driven as a **0–1 fraction** so a reveal survives the text changing length. |
| `TextCounter` | A float through `string.Format`, on TMP or uGUI `Text`. A bad format string is refused once, not thrown every frame. *M5 replaced the per-frame `string.Format` with a reused buffer.* |
| `TextScramble` ✦ | `LMotion.String.Create512Bytes` + `ScrambleMode`, bound with LitMotion's own `BindToText`. |
| `TMPCharacter` ✦ | LitMotion's `BindToTMPChar*` extensions. `CharacterIndex = -1` builds **one motion per character**, each delayed by `Stagger` — a wave from a single step. |

Three of these needed new machinery rather than another row in the channel table:

- **`TweenChannelContext`** carries the per-step parameters a `TweenChannelKey` alone cannot
  identify — shader property id, character index, reveal unit, format string. It is stored in
  `TweenValueSnapshot` too, because restoring a material property means knowing which property
  was written.
- **String and mesh state in the snapshot.** The numeric `Vector4` entry does not describe a
  text channel, so entries now also carry the original string; `TMPCharacter` entries carry
  nothing at all and restore by forcing a TMP mesh rebuild, since per-character state lives
  inside LitMotion's internal animator and cannot be read out.
- **`TweenChannelAccessor.SupportsRead`** marks the write-only channels. A counter cannot
  recover its number from the string it printed, so a step that was a Move a moment ago and
  still carries `FromCurrent` now ignores it instead of refusing to build.

### Materials in edit mode vs at runtime

`ResolveMaterial` returns `renderer.material` at runtime and `renderer.sharedMaterial` in the
editor. Instancing during a preview would leave a stray `(Instance)` material in the scene that
outlives the thing that created it; writing to the shared material is restored exactly on stop.
A `Graphic` using Unity's `defaultGraphicMaterial` is refused outright — that one asset backs
every unstyled Graphic in the project, so animating it would animate all of them.

### FromOffset — the mode the presets needed

A reusable slide-in has to start offset and finish **wherever the object already sits**. With
only `Relative` (end = start + To) that is impossible to express in an asset, because the asset
cannot know the destination. `TweenStep.FromOffset` reads the live value as the *destination*
and starts at live + `From`. It implies a read, so it does not depend on the author also
remembering to tick `FromCurrent`.

### Presets

`TweenAnimationAsset` (ScriptableObject) + `TweenPresetGenerator`, a
`Tools/LitMotion/Generate Tween Presets` menu item that writes the 14 presets: FadeIn, FadeOut,
PopIn, BounceIn, ElasticPop, PanelPopIn, SlideInLeft/Right/Top/Bottom, PunchFeedback,
ShakeError, Pulse, Jump.

Generated by code rather than shipped as `.asset` files, so a change to the step model is
fixed by regenerating instead of hand-merging serialized YAML. Existing assets are overwritten
in place so references to them keep working. Every preset is object-agnostic: no step holds a
Target, positional moves use `FromOffset`, and `Pulse` loops forever at the **animation** level
because a step cannot loop indefinitely inside a sequence.

A preset has its own inspector (`TweenAnimationAssetEditor`) carrying the same timeline, with a
**Preview On** field: pick any scene object and the preset previews against it, restoring it
afterwards. The choice is remembered for the session, since working through a library means
reusing one object.

### Components

- **`TweenButton : UnityEngine.UI.Button`** — subclasses Button rather than listening to it,
  because only `DoStateTransition` exposes Highlighted / Pressed / Selected / Disabled. Ids are
  per-state strings, so a project uses the names its designers type. Guarded against edit mode
  and against Unity re-reporting the current state, and the inherited colour/sprite transitions
  still work alongside.
- **`TweenToggleable`** — show activates then animates; hide animates then deactivates, waiting
  on a coroutine. That ordering is the whole component: deactivating in the same frame would cut
  the hide animation off, and never deactivating leaves an invisible panel eating raycasts.

### Player and inspector

- `TweenPlayer` holds `animationAssets` alongside its inline animations. `Find` searches inline
  first, so one object can override a shared preset without editing the asset.
- The animation Id stays free text with the 13 built-ins one click away in a `▾` menu — the set
  of ids is open, and a dropdown that closed it would be wrong.
- **Save as Preset / Load Preset** on the animation settings. Saving strips step Targets,
  because a scene reference cannot survive in an asset and a dangling one would silently
  resolve to the wrong object elsewhere.

### The Runtime Only trap, fixed

Unity creates persistent UnityEvent listeners with a call state of `RuntimeOnly`, so an
authored `Callback` or `Custom` step does nothing during preview and reads as broken. The step
drawer now detects that and offers **Enable in editor too**, which promotes the listeners to
`EditorAndRuntime`. Found while building the demo rig, where the callback was wired correctly
and still silent.

### Verified in the running editor

- **126 / 126 EditMode tests pass** (0.97s), up from 70.
- **All 14 presets** build with zero errors against a cube, and the four slide-ins land on the
  object's authored position to within 1e-3 — which is the `FromOffset` contract.
- **38 animations** across the 16-player demo rig build with zero errors and scrub forward and
  backward.
- `MaterialProperty`: `_BaseColor` white → magenta mid-flight, `_Smoothness` 0.5 → 0.875 at the
  half point, both **restored exactly** on stop.
- `TextCounter`: `"Score: 1,477"` at 0.75 with `OutCubic`, landing on `"Score: 1,500"`;
  `TextScramble`: `"SClErTXd3R"` → `"SCRAMBLED!"`. Both restore the original string.

### Two things the tests caught

1. A `Material` assigned as a step's Target was refused when the step had no fallback
   GameObject. A material needs no GameObject to live on, so the resolver now short-circuits
   for it — but only for `MaterialProperty`, so a stray material on a Move step still falls
   through to the fallback.
2. `RichText` now defaults to **off**, matching LitMotion. With it on, the tag-aware
   interpolation path trims the outermost characters while the scramble is in flight
   (`"original"` reads as `"rigina"` at t=0), so it is opt-in for text that actually has tags.

### The TMP-only types, verified against real text

TMP Essential Resources were imported to get here — without `TMP_Settings`, TextMeshPro cannot
lay out a single character and every TMP assertion would have been vacuous. `TweenTmpTests`
guards on `TMP_Settings.defaultFontAsset` and skips with that reason in a project that has no
TMP resources, rather than passing on empty text. A `TextMeshProUGUI` also needs a Canvas
ancestor before it generates geometry, which the fixture sets up and asserts.

Measured on a 9-character label:

- `TextReveal` — 0 → 4 → unbounded characters, and the words unit works the same way. A full
  reveal writes `int.MaxValue`, not `9`, so the text is not clipped if it later grows.
- `TMPCharacter` with `CharacterIndex = -1` — 9 motions, one per character, visibly staggered:
  at t=0.15 the first character has risen 12 units while the fourth has not started. Yoyo
  brings them back, and stopping restores every vertex.
- Per-character colour sweeps to cyan in the same staggered order and restores to white; a
  step with a fixed index doubles only that character's width and leaves its neighbours alone.

One sharp edge belongs to LitMotion, not to us: `TextMeshProMotionAnimator.CompleteCore` calls
`UpdateCore` without the null guard its own `TryUpdate` has, so a per-character motion that
*completes* after its text has been destroyed throws. Cancelling is safe, which is what
`TweenPreviewController.Stop` and `TweenPlayer` teardown both do — but a motion left running
past the object's lifetime will log.

**Per-character edits flush on the editor's update tick**, not synchronously inside the scrub:
LitMotion's TMP animator marks itself dirty and applies the vertex changes from its own
`EditorApplication.update` hook. Invisible in the real UI, since the flush lands before the
next repaint — but it means a probe that scrubs and samples in the same call sees nothing move.

### Known limitation: Custom steps cannot be restored

A `Custom` step writes through a `UnityEvent`, which can touch anything, so there is no channel
for `TweenValueSnapshot` to capture and nothing to put back when a preview stops. Whatever the
event last wrote stays written. This is inherent to the escape hatch, not a restore failure.


## M4 — Authoring UX, customization and modularity ✅

The package was feature-complete after M3 and still unpleasant: everything was reachable, little
was shaped for the hand. The full list of what an author needs is kept as a checklist in
[UX.md](UX.md); this section records the decisions and what is built.

**Agreed shape:** the UI lives in both the inspector and a dockable window sharing one view ·
modularity goes as far as user-defined channels · preview follows selection and edits, with a
toggle · delivered in slices, clip inspector and ease picker first.

| Slice | Scope | Status |
|---|---|---|
| **M4.1** | Clip inspector: right widget per channel, modes, grab/apply/swap, axis chips, sliders, shader dropdown | ✅ Verified |
| **M4.2** | Ease gallery with curve thumbnails, favourites, ghost curve, playhead dot | ✅ Verified |
| **M4.3** | Timeline interaction: context menu, keymap, multi-select, split, snap guides, zoom-to-fit | ✅ Verified |
| **M4.4** | Animation chips, preview bar controls, binding-status summary, fix buttons, empty states | ✅ Verified |
| **M4.5** | Preferences page, shared `TweenAuthoringView`, dockable window, extension registry, preset browser | ✅ Verified |
| **M4.6** | Keymap docs, README quickstart, remaining tests | ✅ Verified |

### The problem M4.1 solves

The selected-step panel rendered one `PropertyField` over the whole step, which went through
`TweenStepDrawer` into a **collapsed foldout of raw fields**. Because the model stores every
endpoint as a `Vector4`, "move from where to where" was answered with two four-component fields
whose fourth component means nothing — and the same for Scale, Rotate, Pivot, Anchors and
SizeDelta. Shake strength sat in a bare number box beside jump power the type does not use.

### `TweenChannelInfo` — why a public façade

The inspector has to know that a Move step is three numbers called X, Y, Z and a Fade step is
one number between 0 and 1. That answer lived in `TweenChannelAccessor` and `TweenChannelKey`,
both **internal to the runtime assembly and visible only to the test assembly** — so the editor
could not ask.

Granting internals to the editor would have worked. It was rejected because the editor is not
the only caller that needs the answer: a channel defined in a user's own assembly needs it too,
and internals cannot be granted to code that does not exist yet. So the question is answered by
a public type instead, and the key table stays private.

`TweenChannelInfo.Describe(step, target)` returns the value shape, component labels, unit,
whether the channel can be read, and the resolved channel's name. It also carries `TryRead`,
`TryWrite` and `ResolveMaterial`, which is what grab, apply and the shader dropdown are built on.

**Shape comes from the step's own type and options, never from what resolved.** That way the
inspector draws the right widget before a target is assigned, and the widget does not change
shape the moment one is.

### Decisions worth not re-litigating

- **Nothing is behind a foldout.** One clip is shown at a time, so all of its controls fit. A
  control you have to go looking for is a control you forget exists. This replaced the planned
  "sections with remembered expand state".
- **Four modes, not three booleans.** `FromCurrent`, `Relative` and `FromOffset` are eight
  combinations, four meaningful and four contradictions an author can tick their way into. The
  segmented control offers exactly the four, and `TweenValueModes` maps both ways. Reading is
  tolerant of legacy combinations — `Relative` without `FromCurrent` still reads as Relative,
  because displaying it as Absolute would describe the opposite behaviour — while *selecting*
  Relative normalizes to "by this much from where it is".
- **Field labels follow the mode.** "To" becomes "By" when relative and "Offset" when offsetting,
  and "Strength" for punch and shake, where the end value is not a destination at all.
- **Apply is undoable on its own.** Grab writes into the step, which the usual serialized-property
  undo covers. Apply writes onto a scene object and nothing will restore it, so it records an
  undo entry against that object first.
- **Masking is ignored by grab and apply.** They mean "put the object here", not "animate these
  axes", so they write the whole channel.
- **A full reveal, a 0-1 channel and an empty axis mask all say what they do.** The axis chips
  show a note when nothing is selected, because the builder substitutes XYZ there and the step
  would otherwise look disabled.
- **The ease gallery is a dropdown `EditorWindow`, not `PopupWindowContent`.** The cells are UI
  Toolkit elements that paint themselves with Painter2D, and popup content is IMGUI.
- **Favourites are stored by name, not ordinal**, so the list survives LitMotion gaining eases.
- **Auto-preview follows, it does not start.** While a preview is running, selecting a clip parks
  the playhead at its start and edits apply immediately. Clicking a clip never begins writing to
  the scene on its own, and an Auto toggle disables the following entirely.

### Built in M4.1 and M4.2

New `Editor/Inspector/`: `TweenClipInspector` (the panel), `TweenValueField` (the widget
chooser, with grab and apply), `TweenValueModeField` and `TweenValueModes` (the four modes),
`TweenAxisField` (chips), `TweenShaderPropertyField` (shader dropdown), `TweenFieldRanges`
(slider ranges), `TweenEaseCatalog` (families, search, favourites) and `TweenEasePicker` (the
gallery). `EaseCurveElement` gained a ghost curve, a progress dot and click-to-open.

`TweenStepDrawer` stays as the fallback drawer for raw `TweenStep` arrays elsewhere, and is
still where the Runtime-Only UnityEvent fix lives; the clip inspector reuses that fix.

Both hosts were rewired: `TweenPlayerEditor` gained auto-preview and solo preview, and
`TweenAnimationAssetEditor` the same against its borrowed preview target. The duplication
between those two is now obvious, which is the argument for the shared `TweenAuthoringView` in
M4.5.

### Verified

Compiles through the standalone Roslyn harness with zero errors. The only warning is the
pre-existing `CS0649` on `TweenToggleable.root`, a `[SerializeField]` the harness cannot know
Unity assigns — Unity's own compile suppresses it. Unity MCP was disconnected for this slice,
so the harness was the only compiler available.

**The 52 new tests** (written blind while MCP was down, since run and all passing) cover channel
shape for every type (with and without a target), component labels and units, read/write round
trips, mask-ignoring writes, the four-way mode mapping including legacy combinations, slider
ranges against the shipped defaults, ease search and family ordering, favourites round-tripping
through EditorPrefs, and — the assertion this slice exists for — that a Move step produces a
`Vector3Field` and **no** `Vector4Field`, a Pivot step a `Vector2Field`, a Fade step a `Slider`,
and that every one of the 24 types builds its inspector without throwing.

### M4.3 — timeline interaction

A right-click menu, a keymap, multi-select with group move, align and distribute, split at the
playhead, trim either edge to the playhead, solo and mute, reset to defaults, lane reordering,
a filter, vertical scrolling, a snap guide line, cursor-anchored zoom, zoom-to-fit, panning,
and callback markers on the ruler. The full list with keys is in [UX.md](UX.md).

**Commands plan before they write.** `TweenTimelineCommands` computes a list of new timings and
only then applies it through serialized properties, as one undo entry. Two reasons: the
arithmetic becomes testable without a window, a pointer or a `SerializedObject`; and a group
operation can be validated whole before any of it lands. That is what lets a nudge **clamp the
group together** at zero instead of flattening whichever clips reach it first — the clip nearest
zero sets the limit for everyone, so the arrangement survives.

Decisions worth not re-litigating:

- **Followers in a group drag are written without snapping.** The dragged clip is the one being
  aimed; re-snapping each follower independently would shear the group apart.
- **Trimming never carries the selection.** Dragging one clip's edge must not resize everything
  that happens to be selected, so only a *move* propagates.
- **Clicking a clip that is already part of a multi-selection keeps the group** — otherwise it
  could never be dragged as one — but that clip becomes primary, so the inspector follows the
  click rather than showing whichever clip was picked earliest.
- **The guide line appears only for a real latch**, not for the fixed grid. The grid catches
  nearly every drag, so a guide for it would be a line permanently on screen saying nothing.
- **Bare letters are shortcuts, so they stand down inside text fields.** Typing "shake" into the
  lane filter would otherwise split, mute and zoom-to-fit on the way through.
- **Menu items that cannot apply are disabled, not hidden**, so the menu doubles as a list of
  what is possible. Split is disabled unless the playhead is far enough inside the clip for both
  halves to survive.
- **Lane order is display order only** — timing comes from each step's `StartTime` — so
  reordering is purely about grouping related clips, and is safe.
- **Marquee coordinates are converted with `WorldToLocal`.** The handlers live on the lanes
  container but events arrive having bubbled from an individual row, so `localPosition` would be
  measured from that row and every Y would resolve to row zero.

Deferred rather than dropped: dragging header rows to reorder (the menu and Alt+↑/↓ cover the
capability), middle-drag panning, loop-region shading, and a sticky ruler when the lanes scroll
vertically — that one wants the dockable window's layout, so it rides with M4.5.

`TweenTimelineView` is now 1,321 lines, which is too big. The `TweenAuthoringView` extraction in
M4.5 is the moment to split the command wrappers out of it.

**36 more tests** (also written blind, since run and passing): split boundaries and that the halves meet exactly with no
gap or overlap, group nudge clamping, align moving rather than resizing, distribute working from
timeline order rather than click order, trim collapsing rather than inverting, zoom fit and
anchoring maths, selection and primary-index behaviour, and that the snap guide reports a latch
only when it really latched.

### M4.4 and M4.5 — one view, two hosts, a window, settings, extensions

```
Editor/
  Authoring/   TweenAuthoringView      the whole surface, hosted by inspector and window
               TweenAuthoringSource    player list vs. single preset: what actually differs
               TweenAnimationBar       chips · TweenAnimationListCommands (add/dup/rename/move)
               TweenPreviewBar         transport · TweenBindingStatusPanel · TweenKeymap
  Diagnostics/ TweenBindingStatus      up-front binding check · TweenStepFixes (fix buttons)
  Settings/    TweenEditorSettings     ScriptableSingleton · TweenEditorSettingsProvider
  Presets/     TweenPresetBrowser      Window → LitMotion → Tween Presets · TweenPresetFiles
  TweenEditorWindow                    Window → LitMotion → Tween Editor
Runtime/Extensions/  ITweenExtensionChannel  [TweenExtensionChannel]  TweenExtensionRegistry
Samples/Extensions/  Light intensity + colour channels, and a custom inspector section
```

`TweenPlayerEditor` went from 636 lines to 78, and `TweenAnimationAssetEditor` from 321 to 49:
both are now thin hosts. Everything they duplicated -- preview wiring, solo, auto-follow, the
settings foldout, the warnings -- lives once in `TweenAuthoringView`, and the window is a third
host for free.

**The window** follows the selection (a player, a GameObject with one, or a preset asset) until
locked, which is what picking a step's target in the scene needs. Laid out wide: timeline on the
left, clip inspector on the right of a split. "Open in Window" on both inspectors jumps there.

**Extension channels** route through every existing seam rather than a parallel path:
`TweenType.Extension = 300` plus `TweenStep.ExtensionId`; a `TweenChannelKey.Extension` whose
channel instance travels in `TweenChannelContext`, exactly as a material property id does; one
branch each in the resolver, the accessor's read and write, value-kind lookup and the channel
description. So easing, looping, value modes, axis masking, grab/apply, preview and exact
restore all work for a user channel with no extension-specific code. The scan uses `TypeCache`
in the editor and walks assemblies once in a player; `Register` covers IL2CPP stripping.

Decisions worth not re-litigating:

- **One preview at a time, globally.** Two views previewing the same object would each snapshot
  the other's half-animated pose and restore the wrong values. Starting a preview stops the
  previous owner's; a view of the same object shows "Previewing in the Tween Editor window".
- **Reading is mandatory for an extension.** A channel that cannot read cannot be restored after
  a preview, so `TryRead` is part of the interface rather than optional.
- **Extension ids are persisted like `TweenType` values.** A missing channel is a binding error
  naming the id, never an exception, and the step keeps displaying the raw id.
- **Settings are per user** (preferences folder), not per project. Their defaults are the old
  constants, enforced by a test, so nothing changes until someone changes it.
- **A preferred default duration/ease only replaces the baseline**, never a type's own choice: a
  per-character wave keeps its timing whatever the preference says.
- **Fixes are offered only when unambiguous.** CanvasGroup only on UI (on a 3D object it would
  bind and then do nothing); nothing for Pivot on a 3D object.
- **The status list is inline, not a popover**, so it stays open while fixes are applied and
  re-checks in place.
- **Preview loop and speed are preview-only** and survive a rebuild; they never touch the
  animation's own Loops or PlaybackSpeed.
- **The scramble sample runs LitMotion's real string motion** on a private dispatcher, the same
  production-path rule the preview follows.

### Bugs found while verifying M4

Running the 88 blind-written tests and driving the real editor turned up four defects, all fixed
with a test that would have caught them:

1. **A preview permanently changed a material when two of its properties were animated.** The
   snapshot deduplicated by (object, channel key), and both properties share the MaterialFloat
   key, so the second was never captured. Confirmed live: `_Metallic` stayed at 1 after stop
   instead of returning to 0.2. Captures are now keyed by property id (and by extension
   channel) too.
2. **The clip inspector's fields were never bound.** Fields added to a hierarchy after it is
   bound are not bound by that earlier bind, so in the window every PropertyField was empty.
   The inspector now binds what it builds.
3. **Binding would have recursed.** A PropertyField raises its change callback once,
   synchronously, when bound -- measured, not assumed -- and several callbacks rebuild the
   panel. Callbacks now ignore binds and re-binds that replay an unchanged value; text and
   number fields refresh samples instead of rebuilding, so the caret stays put while typing.
4. **`ScrollView.ScrollTo` throws on a view not yet in a panel**, which "select step" hit when
   called during construction.

### M4.6 — verified

- **297 / 297 EditMode tests pass** (1.4 s): the 214 from before plus 83 new ones covering the
  extension registry and every seam it routes through, binding status and fixes, the animation
  list commands, the authoring view in every host, settings defaults and theming, preview
  transport wrapping, save-selection-as-preset, the preset browser's search, the live samples,
  and the material snapshot regression.
- **In the running editor**: the window built on all 17 rig players; 49 step selections across
  every animation with zero exceptions, every animation reporting its bindings OK. Preview
  through the window: the Move rises +2 at 0.5 s, three frame-steps land on 0.55 s, Stop
  restores `(-15, 0.5, 0)` exactly. The Light extension: intensity 1 → 3 at the halfway point of
  1 → 5, colour interpolating, both restored; a missing channel id reports instead of throwing.
  Preferences page: 13 fields bound, 10 colour swatches. Preset browser: 14 presets, 14
  thumbnails. Console clean throughout.
- **A tooltip on every button** is a test, not a promise: it selects a step of each of the 24
  types and walks the whole view. It caught the axis chips.

- **In Unity's own Inspector window** too: the player shows 3 clips, and selecting the Scale
  step gives 9 / 9 fields bound with three-component From/To fields carrying its real values.

**Still needs a human:** the feel of chip renaming and of dragging a preset onto the chips, and
the window's split proportions on a small screen.

---

## M5 — Runtime performance ✅

Done on 4 Oct 2026. Tests went from **297 to 367 EditMode tests, all passing** (3.6 s). The demo
rig was played in real Play mode with a clean console.

### What the audit found

Measured in the editor (Mono) on 1,000 objects animating at once, against raw LitMotion:

| Case | Per `Play` | Per frame | GC per frame |
|---|---|---|---|
| 1 step, raw LitMotion | 0.64 µs | 0.060 ms | 0 B |
| 1 step, raw LitMotion inside `LSequence` | 1.70 µs | 0.240 ms | 0 B |
| 1 step, `TweenPlayer.Play` path | 3.4 µs | 0.27 ms | 0 B |
| 3 steps, raw LitMotion | 1.26 µs | 0.195 ms | 0 B |
| 3 steps, raw LitMotion inside `LSequence` | 2.6–2.8 µs | 0.61 ms | 0 B |
| 3 steps, `TweenPlayer.Play` path | 5.5 µs | 0.66 ms | 0 B |

- **Editor code costs nothing at runtime.** It is an `Editor`-only assembly and ticks only while a
  preview plays.
- **About 90% of the per-frame gap is `LSequence`, not this package.** LitMotion's Burst job skips
  sequence children (`MotionUpdateJob.cs:38`). The driver updates each child on the main thread
  through `SetTime`. Our channel write layer adds about 5–10% on top of that.
- **Transform, colour, alpha and camera channels allocated nothing per frame** even before M5.

| # | Culprit | Where | Kind |
|---|---|---|---|
| C1 | TextReveal called `ForceMeshUpdate()` on **every frame's write** | `TweenChannelAccessor.TryWrite` → `CountUnits` | Hot path, CPU |
| C2 | TextCounter ran `string.Format` + float boxing every frame | `TweenChannelAccessor.TryWrite`, `TextNumber` | Hot path, GC |
| C3 | Snapshot captured on every `Play`, used only by `KillBehavior.Rewind` | `TweenPlayer.Play` | `Play` cost + GC |
| C4 | `Play` allocated a run closure and an `OnComplete` delegate; step context built 2–3× | `TweenAnimationRunner.Build`, `TweenStepBuilder` | `Play` cost + GC |
| C5 | Runtime `renderer.material` instances never destroyed; a runtime write to `graphic.material` edited the **shared asset**; the material was re-resolved every frame | `TweenChannelAccessor.ResolveMaterial` | Leak + correctness + CPU |
| C6 | The first extension step in a player reflected over every type in every assembly | `TweenExtensionRegistry.FindAttributedTypes` | One-off hitch |
| C7 | A one-step animation still paid for an `LSequence` (about 4× per frame) | `TweenAnimationRunner.Build` | Per-frame CPU |

### Result

Same harness and machine as the audit table. Per-`Play` numbers are for the default kill
behavior. Rewind adds the snapshot, about +0.7 µs per step.

| Case | Before: per `Play` | After: per `Play` | Before: per frame | After: per frame |
|---|---|---|---|---|
| 1 step | 3.4 µs | **1.3–1.4 µs** | 0.26 ms | **0.094 ms** |
| 3 steps | 5.5 µs | 4.5–4.8 µs | 0.66 ms | 0.65 ms |

- **One-step animations are about 2.8× cheaper per frame**, now 1.5× raw LitMotion instead of
  4.5×. Many of the presets are one step.
- **Multi-step animations are unchanged per frame**, as expected: that cost is LitMotion's
  sequence. `Play` is cheaper because nothing is captured unless the kill behavior is Rewind.
- **No channel allocates per frame** on either path, enforced by a test per channel family. The one
  exception is a uGUI `Text` counter, which can only take a string, and gets one only when the shown
  number changes.

Rerun the numbers with the `[Explicit]` test `TweenPerformanceTests.Benchmark`.

### What was built

| Slice | Scope | Culprits | Status |
|---|---|---|---|
| **M5.0** | Zero-GC tests per channel family, TMP rebuild counter, `[Explicit]` benchmark | — | ✅ |
| **M5.1** | Cheaper `Play()` | C3, C4 | ✅ |
| **M5.2** | Text hot paths | C1, C2 | ✅ |
| **M5.3** | Materials: owned instances, no asset writes, resolved once | C5 | ✅ |
| **M5.4** | Extension registry: narrower player scan, `Prewarm()` | C6 | ✅ |
| **M5.5** | One-step fast path | C7 | ✅ |
| **M5.6** | Benchmark, demo rig in Play mode, README | — | ✅ |

**M5.1 — `Play()`.** `TweenPlayer` captures a snapshot only for Rewind, and pools its running
entries and snapshots. The runner's sequence configuration is a cached delegate reading
main-thread static fields, not a closure per build. `TweenAnimation.CompleteInvoker` makes the
`OnComplete.Invoke` delegate once per event; it is keyed by the event because `OnComplete` is a
public field that can be replaced. `TweenChannelContext` is built once per step and passed down.
One `TweenChannelWriter` per step per play remains, by decision.

**M5.2 — Text.**
- **TextReveal** reads TMP's existing `textInfo` and lays out only while `havePropertiesChanged`
  says it is stale (`CountUnitsForWrite`). So a reveal still follows text that changes length.
- **TextCounter** splits its format once into prefix, number spec and suffix
  (`TweenCounterText`), and formats with `float.TryFormat` into a reused buffer:
  - **TMP** gets the buffer through `SetCharArray`, and identical text is not rewritten.
  - **uGUI `Text`** is compared with its own current text and given a string only when it differs.
  - Formats the split cannot express fall back to `string.Format`: an alignment, two
    placeholders, or escaped braces next to a spec.
  - Output matches `string.Format` for every case tested, culture included.
  - **A bad format is now a build error**, reported once, instead of an exception caught on every
    frame.

**M5.3 — Materials.**
- Resolution is split in two.
  - `ResolveMaterial` reads and never instances, so inspecting a step in play mode changes
    nothing.
  - `ResolveMaterialForWrite` is used by the builder and by writes.
- At runtime, `TweenMaterialOwner` creates the target's instance, or a copy for a Graphic. It is a
  hidden `[ExecuteAlways]` component on the target itself, and it destroys the instances it caused
  when the object goes.
- **The owner lives on the target, not on `TweenPlayer`** as first planned. A step can target an
  object outside its player's hierarchy, and the instance belongs to the object, not to whoever
  animated it.
- A renderer still goes through `renderer.material`, so an instance the game already made is
  reused, not copied.
- The builder hands the resolved `Material` to the writer as its target, so the per-frame write is
  a type check.

**M5.4 — Registry.** In a player, `ScanAssemblies` skips runtime libraries by name and any
assembly that does not reference this one. An assembly whose references cannot be read is scanned
anyway. In this editor's domain, fewer than half the assemblies are considered, and the result
matches `TypeCache` exactly. `TweenExtensionRegistry.Prewarm()` moves the scan to a loading
screen.

**M5.5 — Fast path.** `TweenAnimationRunner.CanRunAlone` accepts an animation with exactly one
enabled step that:
- starts at zero;
- builds one motion: not a Callback, not per-character TMP, not Anchors set to Both;
- and, if the animation loops, does not loop or delay by itself and loops as Restart or Yoyo.

`TweenStepBuilder.BuildAlone` folds the animation's loops and completion into that step's own
motion. Two things found while building it:

- **Animation-level `Incremental` and `Flip` stay on the sequence.** The sequence's driver is a
  linear time value, so on it `Incremental` runs children past their end and `Flip` reverses time.
  On an eased motion the same settings mean something else.
- **A seeded Shake draws different noise on the two paths.** LitMotion keys shake noise on a hash
  of the exact motion time, and a sequence hands children `duration * (float)progress` rather than
  the raw time. Scrubbing to a given time agrees exactly, and a seed still reproduces itself. But
  the noise a seeded one-step shake draws is not the noise it drew before M5.

`TweenFastPathTests` builds every scenario both ways on two objects with separate clocks. It
compares them while scrubbing forwards and backwards, and while playing in real time at speed 1.5.
It also compares `TotalDuration`, which sets the preview's scrub range. The results match to 1e-5.
`TweenAnimationRunner.FastPathEnabled` exists so tests can force the sequence.

### How the plan changed while building
- The TMP rebuild check counts `TMPro_EventManager.TEXT_CHANGED_EVENT`, not a `ProfilerRecorder`.
  Recorders only update at the end of a frame, which an EditMode test never reaches.
- The TMP counter cannot be zero-GC *in the editor*: there, `SetCharArray` mirrors the text into
  a string for the inspector (`#if UNITY_EDITOR` in TMP). The test checks what is ours: an
  unchanged number is not rewritten.
- Nothing needed `[Ignore]`. The fixes landed before the zero-GC tests first ran.

### Still true, by design
Inside a multi-step animation, LitMotion keeps writing a **finished** step's end value every
frame until the whole sequence ends. A 0.2 s Fade in a 3 s animation sets alpha for 3 s. This is
documented in the README.

---
---
## Scoring against the reference

| Reference feature | Here |
|---|---|
| 17 tween types | **24, all built** |
| UI Toolkit timeline + ease preview | ✅ M2, with snapping, zoom and overshoot-aware curve graph |
| Live Edit Mode playback | ✅ M1, exact, with backward scrubbing |
| Copy/paste | ✅ M2, cross-scene and cross-project via the system clipboard |
| 14 ScriptableObject presets | ✅ M3, generated by a menu item so they survive model changes |
| 13 named ids + custom | ✅ M3, a one-click menu beside a free-text field |
| Override/Additive, kill behavior | ✅ M3, exposed in Animation Settings |
| Restart/PingPong loops, unscaled time | ✅ all 4 LitMotion loop types + `IgnoreTimeScale` |
| 32 ease functions | ✅ 31 + `AnimationCurve` |
| 3 components | ✅ `TweenPlayer`, `TweenButton`, `TweenToggleable` |
| Swappable DOTween/PrimeTween backends | **Deliberately cut** |
| — | **Beyond reference:** `TextScramble`, `TMPCharacter` per-character waves with stagger, `VolumeWeight` (URP), a real `JumpMotionAdapter` rather than two faked motions, per-axis masking, `FromOffset` reusable slide-ins, preset previewing against any scene object, user-defined extension channels, a dockable editor window, up-front binding checks with one-click fixes, a searchable preset browser |

---

## Working notes

**Verifying without closing the editor.** `Unity -batchmode` cannot run while the editor holds
the project lock. A standalone Roslyn harness lives in the session scratchpad
(`build/compile.sh runtime|editor|tests|all`). It mirrors Unity by restricting base references
to the Unity install and resolving package references from each asmdef's own `references` list
(GUIDs included), and by reading `versionDefines` out of the asmdefs so it cannot drift. An
earlier version handed the compiler every `.csproj` HintPath, which includes
`Library/ScriptAssemblies` — making it *more* permissive than Unity and hiding the missing SRP
reference. If that harness is lost, Unity MCP (`refresh_unity`, `read_console`, `run_tests`) is
the better path anyway.

**Running the tests:** `run_tests` with `mode: EditMode`, `assembly_names:
["LitMotionTweenEditor.Tests"]`, then poll `get_test_job`.

**Inspecting UI from `execute_code`.** It compiles into its own assembly, so the package's
internal types and the `Q`/`Query` extensions are unavailable: walk `hierarchy` by hand and reach
internals by reflection. UI Toolkit binding needs a panel, so check binding through the window,
not a detached element. And a backgrounded editor does not refresh the Inspector window, so a
selection change is not visible there until the editor is focused.

**The demo rig.** `Assets/Scenes/TestScene.unity` holds **LMTE Test Rig**: 17 `TweenPlayer`s
covering every implemented channel, with a canvas for the UI-only ones and `Samples/
TweenDemoReceiver.cs` as the target for the Callback and Custom steps. Cubes 01–09 and 12–17
are 3D; 10, 11, 15 and 16 live on the canvas. `12 Material` has its own
`Samples/DemoMaterial.mat` on purpose, so a preview never writes to a material shared with
anything else.

**A test that builds motions must track every handle it creates.** Building without a
scheduler schedules on the editor dispatcher, so a forgotten handle completes later — against
an object the test has already destroyed.

**Removed packages.** The three AnkleBreaker dependency packages (Core 1.1.0, Utils-Inspector
1.7.0, Utils-UI-Basics 0.4.3) that shipped in `Tween In Inspector/` were deleted — they were
IMGUI-only and `Utils-Inspector` registered `[CustomEditor(typeof(MonoBehaviour), true)]`,
making it the inspector for every script in the project. They contained **zero** trace of the
actual tween asset. Both are public MIT repos if ever needed:
`github.com/AnkleBreaker-Studio/AnkleBreaker-Core` and `/AnkleBreaker-Utils-Inspector`.
