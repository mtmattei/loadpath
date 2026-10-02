# HANDOFF — Loadpath blueprint v2, motion and polish
Updated: 2026-10-01 14:55

## Where we are
Cloned and built Loadpath, then reworked the UI in four steps:
- the light drafting-sheet look (v1);
- the blueprint v2 sheet with Reduce noise (12 layers you can keep or hide, `F`);
- an eased motion system (spline keyframes, the `Motion` Enter/Exit/Glide helper);
- a gold-standard polish pass: SI unit case (kN/m/ms), N7/M6 naming, 40 px inspector controls, a grid for the section chips, a bounded document name, and singular/plural counts.

Everything is merged to `main` and pushed. The user records walkthrough takes themselves from a scripted input run. The last take (14:51) ran every step except the CHS 76.1 chip drag: a comment I added had swallowed the drag-target line. That line is fixed in the script but has not been re-run.

## Last verified state
- Build: Release `net10.0-desktop` 0 warnings / 0 errors; 26/26 tests pass.
- Runtime: node, member, mixed and empty inspector states captured with fixture env hooks, maximised (PrintWindow). Narrow widths and long section names not checked.
- Git: `main` at `b635c14` (pushed). `polish/gold-standard` and `ui/blueprint-v2` are also pushed. Working tree clean apart from this file.
- Lint: CARD 0 · HEX 0 · TOKENTHEME 23 (pending the dark-mode decision) · BACKBAR 0 · CODEBEHIND 0 · OVERLAY 0 · RESPONSIVE 0 · BUILTIN 0 · ICON 0 · TEMPLATE 10 (info).

## Guidance applied
| Skill / rule / decision | Applied at (file:line) |
|---|---|
| xaml-design-polish: house curves as tokens | `Loadpath/Themes/MotionTokens.xaml:4` |
| xaml-design-polish: spline keyframes in templates | `Loadpath/Themes/Controls.xaml:95` |
| xaml-design-polish: honour the in-app animations setting | `Loadpath/Controls/Motion.cs:41` |
| winui-xaml: width states via AdaptiveTrigger in XAML | `Loadpath/MainPage.xaml:12` |
| gold-standard-pass Coherence: one inspector control height | `Loadpath/Views/InspectorPanel.xaml:19` |
| gold-standard-pass Hardening: section chips in a grid, no clipping | `Loadpath/Views/InspectorPanel.xaml:314` |
| gold-standard-pass Content Reality: SI unit case | `Loadpath/Workspace/WorkspaceRenderer.cs:748` |
| gold-standard-pass Coherence: N/M naming | `Loadpath/Presentation/EditorModel.cs:525` |

Read, not applied: `utu:Responsive` (the app uses no Toolkit, by README decision); uno-verify through the App MCP (verification used Win32 capture instead).

## Next actions (in order)
1. If the user wants a clean take with the chip drag: run `showcase.ps1 -Phase launch`, say ready, wait for "go", then run `-Phase run` ONCE.
2. Decide dark mode. This settles the 23 TOKENTHEME hits (add ThemeDictionaries, or rename the brushes `*Invariant`).
3. Refresh the README and docs screenshots for blueprint v2.
4. Check narrow widths (<1100, <640) and long section and document names.

## Open questions
- Dark mode: yes or no?
- Adopt the Uno Toolkit? The README currently says no.
- The exaggeration slider was removed from the UI and now lives only as palette commands. Is that OK?
- `F` now toggles Reduce noise; Fit moved to `Ctrl+0`. Is that OK?
- The user's original autosave backup is at scratchpad `autosave.before-showcase`. Fixture runs overwrite `%LOCALAPPDATA%\Loadpath\com.loadpath.app\LocalState\autosave.loadpath`, so back it up first.

## Relaunch
```powershell
cd C:\Users\Platform006\loadpath
dotnet build Loadpath\Loadpath.csproj -c Debug -f net10.0-desktop
$env:APP_NO_HOTDESIGN='1'; Start-Process Loadpath\bin\Debug\net10.0-desktop\Loadpath.exe
# Fixture hooks: LOADPATH_RESET=1, LOADPATH_SAMPLE=warren|cantilever|roof, LOADPATH_SELECT=n7,m6, LOADPATH_MODE=..., LOADPATH_FILE=path
# Walkthrough: <session scratchpad>\showcase.ps1 -Phase launch | run [-Capture] (Win32 input; window must be in front)
```
The scratchpad scripts (`showcase.ps1`, `lib.ps1`) live in this session's temp folder and won't survive cleanup. Copy them into the repo (for example `tools/`) if the walkthrough should be kept.
