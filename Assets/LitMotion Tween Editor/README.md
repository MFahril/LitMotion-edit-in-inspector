# LitMotion Tween Editor

Author, chain, preview and scrub [LitMotion](https://github.com/annulusgames/LitMotion) tween
animations from the Unity editor — on a timeline, with exact edit-mode preview, and without
writing code.

- **Unity** 6000.6+ · **LitMotion** 2.0.2 · optional uGUI, TextMeshPro and URP support
- 24 tween types, 31 eases plus custom curves, presets, and your own channels via extensions

For what the tool does and why, see [UX.md](UX.md). For how it is built and the decisions
behind it, see [ROADMAP.md](ROADMAP.md).

---

## Quickstart

1. **Add a Tween Player.** Select a GameObject → Add Component → *LitMotion → Tween Player*.
   Or open **Window → LitMotion → Tween Editor** with the object selected and press
   *Add Tween Player*.
2. **Add an animation.** Press *Add Animation* — it is named after the first free built-in id
   (Show, Hide, …) — or *Add From Preset…*, or drag a preset from
   **Window → LitMotion → Tween Presets** onto the animation chips.
3. **Add steps.** *Add Step…* or the `+` on the timeline toolbar. Each step is a clip.
4. **Arrange them.** Drag a clip to move it, drag its edges to trim. Clips snap to the grid,
   to each other and to the playhead. Right-click for everything else.
5. **Tune a step.** Click a clip; its controls appear below the timeline (beside it in the
   window). Pick the mode — Absolute, Current, Relative or Offset — set the values, and click the
   curve to choose an ease from the gallery.
6. **Preview.** Press Play, or drag across the ruler to scrub. Everything is restored exactly
   when you press Stop.
7. **Play it at runtime.**

   ```csharp
   GetComponent<TweenPlayer>().Play(TweenAnimationId.Show);
   ```

   Or set *Play On Enable* in the player's settings, or use `TweenButton` / `TweenToggleable`.

If a step cannot find anything to animate, its clip shows a warning badge and the preview bar
says how many steps are failing. Click that readout for the reasons, and for a one-click fix
where there is an obvious one (Add Canvas Group, Add Audio Source, Create Material, …).

---

## Keys

Active while the timeline has focus. **Keys** in the editor shows the same list.

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
| `Alt+↑` / `Alt+↓` | Move the lane up or down |

| Pointer | Does |
|---|---|
| Drag a clip | Move it; its edges trim |
| Ctrl/Shift+click | Add to the selection |
| Drag empty space | Rubber-band select |
| Wheel / Shift+wheel | Zoom at the cursor / pan |
| Click the ease curve | Open the ease gallery |
| Double-click a chip | Rename the animation |

---

## Where things are

| Menu | What |
|---|---|
| Window → LitMotion → Tween Editor | Dockable editor; follows the selection until locked |
| Window → LitMotion → Tween Presets | Searchable preset browser; drag onto the chips |
| Preferences → LitMotion Tween Editor | Defaults for new steps, snapping, zoom, lane height, colours |
| Tools → LitMotion → Generate Tween Presets | Writes the 14 built-in presets |

---

## Adding your own tween channels

Anything the 24 built-in types do not cover can be added from your own code, and it then works
like a built-in type: it appears in the add menu, eases, loops, supports the value modes and
axis masking, previews and restores exactly.

```csharp
using LitMotion.TweenEditor;
using UnityEngine;
using UnityEngine.Scripting;

[Preserve]   // keeps IL2CPP from stripping a class found only by reflection
[TweenExtensionChannel("com.mystudio.light-intensity", Category = "Lighting")]
public sealed class LightIntensityChannel : ITweenExtensionChannel
{
    public string DisplayName => "Light Intensity";
    public TweenValueShape Shape => TweenValueShape.Float;   // picks the widget and interpolation
    public string Unit => "";

    public bool TryResolve(TweenStep step, GameObject gameObject, out Object target, out string error)
    {
        target = gameObject.GetComponent<Light>();
        error = target == null ? "Requires a Light." : null;
        return target != null;
    }

    public bool TryRead(Object target, out Vector4 value)       // required: preview restores through it
    {
        value = new Vector4(((Light)target).intensity, 0f, 0f, 0f);
        return true;
    }

    public bool TryWrite(Object target, Vector4 value)
    {
        ((Light)target).intensity = value.x;
        return true;
    }
}
```

- Values travel as a `Vector4`: a float in X, a Vector2 in XY, a Vector3 in XYZ, a colour as RGBA.
- **The id is saved in every step that uses the channel**, so choose it once and never change it.
  If a channel goes missing, its steps report a binding error naming the id rather than failing
  silently.
- To add controls of your own under a step's standard fields, derive from
  `TweenExtensionInspector` in an editor folder and mark it
  `[TweenExtensionInspector("com.mystudio.light-intensity")]`.

A complete worked example, with a colour channel and a custom inspector section, is in
[`Samples/Extensions`](Samples/Extensions).
