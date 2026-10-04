# LitMotion Tween Editor

Author, chain, preview and scrub [LitMotion](https://github.com/annulusgames/LitMotion) tween
animations from the Unity editor. You get a timeline in the inspector, exact edit-mode preview,
24 tween types, presets, and your own channels through extensions, all without writing code.

- **Unity** 6000.6 or newer · **LitMotion** 2.0.2
- uGUI, TextMeshPro and URP support switch on by themselves when those packages are present

## Install with the Package Manager

You need [Git](https://git-scm.com/) installed and on your `PATH`. Unity's Package Manager runs
Git to fetch a Git URL package.

1. In Unity, open **Window → Package Manager**.
2. Click **+** at the top left and choose **Install package from git URL…**.
3. Enter LitMotion's URL and click **Install**:

   ```
   https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion
   ```

4. Do the same with this package's URL:

   ```
   https://github.com/MFahril/LitMotion-edit-in-inspector.git?path=Assets/LitMotionTweenEditor
   ```

**LitMotion has to go in first.** This package depends on it, but Unity cannot fetch a
dependency from a Git URL by itself. If you skip step 3, the install fails with
`com.annulusgames.lit-motion` not found.

### Or edit the manifest

Add both lines to `Packages/manifest.json` in your project:

```json
{
  "dependencies": {
    "com.annulusgames.lit-motion": "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion",
    "com.mfahril.litmotion-tween-editor": "https://github.com/MFahril/LitMotion-edit-in-inspector.git?path=Assets/LitMotionTweenEditor"
  }
}
```

### Pinning a version

Without anything after the path, you get the latest commit on `main` at the time you install.
Unity then stays on that commit, recorded in `Packages/packages-lock.json`, until you update. To
pin a release, add its Git tag to the end of the URL:

```
https://github.com/MFahril/LitMotion-edit-in-inspector.git?path=Assets/LitMotionTweenEditor#v0.6.0
```

### Updating

In the Package Manager, select **LitMotion Tween Editor** and click **Update**. If you pinned a
tag, change the tag in `manifest.json` instead.

### Running the package's tests in your project

Add the package to `testables` in `Packages/manifest.json`. Its EditMode and PlayMode tests then
appear in **Window → General → Test Runner**:

```json
{
  "testables": [ "com.mfahril.litmotion-tween-editor" ]
}
```

## Getting started

Add a **Tween Player** to a GameObject (**Add Component → LitMotion → Tween Player**). Add an
animation and some steps, and press Play in the preview bar. Then play it from code:

```csharp
GetComponent<TweenPlayer>().Play(TweenAnimationId.Show);
```

The full guide is in the package's [README](Assets/LitMotionTweenEditor/README.md): the
quickstart, playing from code, keys, extension channels and runtime cost. Changes are in the
[CHANGELOG](Assets/LitMotionTweenEditor/CHANGELOG.md).

## This repository

This is the development project. The package is the folder
[`Assets/LitMotionTweenEditor`](Assets/LitMotionTweenEditor); that folder is all that the Git URL
installs. The rest is only for developing it:

| Path | What |
|---|---|
| `Assets/LitMotionTweenEditor` | The package: runtime, editor, presets, tests, docs |
| `Assets/LitMotionTweenEditorSamples` | A sample extension channel, the demo rig's helpers, and the player smoke test (**Tools → LitMotion → Smoke Test**) |
| `Assets/Scenes/TestScene.unity` | The demo rig: 18 players covering every tween type |

How the package is built, and why, is in [ROADMAP.md](Assets/LitMotionTweenEditor/ROADMAP.md).
