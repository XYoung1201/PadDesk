# Dashboard implementation plan

**Goal:** Implement approved 3D-first dashboard, app icon, editable D-pad steps and photo-reference controller variant.
**Architecture:** Preserve native WPF rendering and input engine. Add per-profile visual layout, used by dashboard and calibration without changing mappings.
**Spec:** ../specs/2026-11-12-dashboard-design.md plus user-approved photo extension.

- [x] Add regression checks for photo layout geometry and editable step validation; observe failure.
- [x] Implement photo variant (X top, A right, B bottom, Y left, black shell, minus/plus and home/capture), persisted per device; retain Xbox option.
- [x] Recompose Main.xaml into large model stage and right tabbed inspector; connect D-pad editing and model selection.
- [x] Generate original vector-derived multi-resolution icon; embed and use for window/tray.
- [x] Build, run logic and visual checks, inspect default/minimum render, review changes, deliver executable.

Constraints: preserve pre-existing uncommitted changes and profiles. Use preview output before final build. No automatic remapping based on photo; hardware calibration remains authoritative. Existing profiles missing layout default to Xbox. Photo variant is a reference approximation, not a manufacturer identification.

Execution ruling: User explicitly approved implementation. Continue inline in existing workspace to preserve earlier unfinished work, without further approval rounds. Do not commit unrelated existing edits.

Verified: 46 logic checks and 29 visual checks passed. Includes profile serialization, photo calibration geometry, minimum-size mapping list and expanded editor accessibility. Native input integration was not run while the old app is active. Final binary: PadDesk.updated.exe alongside existing Main.xaml and SDL2.dll; uses the same profiles directory. Existing PadDesk.exe remains running and unchanged by this task.
User follow-up: widened inspector to 460 px with draggable divider; expanded list viewport; renamed details fold away and keyboard picker appears only for keyboard mappings.
