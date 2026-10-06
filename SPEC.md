# Loadpath — Spec

A desktop workbench for designing 2D trusses and seeing, instantly, how load travels through them.

Stage 1–4 of the build: product definition, UX architecture, visual system, and the Uno Platform mapping. Implementation follows the plan at the end.

---

## Stage 1 — Product

### What it does

Loadpath is a structural sketchpad. You place nodes, connect them with members, pin some nodes to the ground, hang loads on others, and the structure answers immediately: every member shows the axial force it carries, its stress utilization, and the deflected shape. Drag a node and the whole load path re-solves on every pointer move.

### Who it is for

- Structural and mechanical engineering students learning how trusses behave.
- Makers and fabricators sketching frames (roof trusses, stage rigging, bike racks, bridge models) before cutting steel.
- Engineers doing a concept check without opening a full FEA package.

### The primary problem

Understanding *where* the load goes and *which* member fails is invisible on paper and slow in FEA tools. Loadpath makes the structure a thing you can feel: you see the force ribbons swell and shrink while you move a node.

### Central interaction model

**Draw a structure, and it answers.** Every edit is a direct manipulation on a spatial canvas. The analysis is continuous, synchronous, and sub-millisecond, so results are part of the manipulation feedback rather than a separate "run" step. Everything else in the app (inspector, outline, status) is a lens on the same document and the same result.

### Why it needs a desktop-class interface

- Precision pointer work: placing nodes on a snapped grid, dragging load vectors, marquee selection.
- Keyboard as a first-class input: single-key tool switching, nudging, modifier-driven snapping, undo.
- A wide canvas next to a dense inspector, both visible at once.
- Files: documents are saved, reopened, and autosaved.

### What is deliberately out of scope

Frames with bending (beams), 3D, dynamic loading, distributed loads, code checks. Loadpath is a pin-jointed 2D truss tool and it goes deep on that.

---

## Stage 2 — UX architecture

### Application shell

```
┌────────────────────────────────────────────────────────────────────────────┐
│ ◧ Loadpath   Warren truss ●        ↶ ↷   ● Solved 0.3 ms      ⌘K  Save   │  title strip (36)
├──┬─────────────────────────────────────────────────────┬───────────────────┤
│V │                                                     │ INSPECTOR         │
│N │                                                     │  Node 4           │
│M │                                                     │  x 6.000  y 3.000 │
│L │            W O R K S P A C E                        │  Support  Pin ▾   │
│S │            (SKCanvasElement)                        │  Load  Fx 0  Fy -10│
│  │                                                     ├───────────────────┤
│H │                                                     │ STRUCTURE         │
│  │                                                     │  ▸ Nodes (9)      │
│  │                                                     │  ▸ Members (15)   │
│  │                                                     │                   │
├──┴─────────────────────────────────────────────────────┴───────────────────┤
│ x 4.50  y 1.00   snap 0.5 m   100%   Forces ▾  Deflection ▾   max 78%     │  status bar (26)
└────────────────────────────────────────────────────────────────────────────┘
```

- **Title strip**: app mark, document name with dirty marker, undo/redo, analysis status chip (Solved / Unstable / No loads / Empty), command palette trigger, Save.
- **Tool rail** (left, 40 px): Select, Node, Member, Load, Support, Pan. Each shows its single-key shortcut on hover.
- **Workspace**: the canvas. Owns pointer input, rendering, adorners.
- **Inspector column** (right, 280 px): contextual inspector on top, structure outline below. The column collapses with `Ctrl+\`.
- **Status bar**: cursor world coordinates, snap size, zoom, display-mode switches, headline result (max utilization), transient hints.

### Navigation model

Single document, single workspace. There is no page navigation. "Navigation" means moving through the structure: zoom, pan, fit, select-from-outline, jump-to-max-utilization.

### Panels, inspectors, contextual surfaces

| Surface | When it shows | What it holds |
|---|---|---|
| Inspector: Structure summary | nothing selected | node/member counts, supports, total load, max utilization, reactions, "jump to critical member" |
| Inspector: Node | 1 node selected | position (editable), support kind (segmented), load Fx/Fy (editable), connected members |
| Inspector: Member | 1 member selected | endpoints, length, section (dropdown), material, force, stress, utilization bar, buckling check |
| Inspector: Mixed | ≥2 elements selected | counts, bulk actions (set section for all members, clear loads, delete) |
| Structure outline | always (collapsible) | nodes and members with badges (support, load, force sign); click selects, hover highlights |
| Floating selection bar | on the canvas, above a selection | delete, duplicate, support toggle, add load |
| Context menu | right-click | element-specific actions with shortcut labels |
| Command palette | `Ctrl+K` | every command, searchable, with shortcuts; the discoverability surface |
| Toast | after an invalid or notable action | one line, self-dismissing |

### Progressive disclosure

- Persistent: tool rail, canvas, status bar, undo/redo, analysis status.
- Contextual: inspector content, floating selection bar, context menu.
- Progressive: display modes (Forces, Utilization, Deflection, Labels, Reactions) in the status bar; section library and material in the member inspector; the command palette reveals every command with its shortcut.

The user always knows: where they are (coordinates + zoom in status bar, grid on canvas), what they manipulate (hover highlight + cursor), what is selected (accent ring + inspector title), what actions are available (floating bar, context menu, palette), what changed (undo label, status chip, force ribbons re-render).

### Interaction model

**Selection**
- Click selects one element. `Shift`+click toggles membership. Click empty deselects.
- Drag on empty space draws a marquee. Elements fully inside are selected (`Alt` for touching).
- `Ctrl+A` selects all. `Esc` clears selection (and cancels an in-progress tool action).
- Selection is a set of element references (nodes and members), owned by one `SelectionSet`. Outline and canvas share it.

**Manipulation**
- Drag a selected node (or any node in Select tool) moves it; if several nodes are selected they move together. Snapping: grid, alignment guides to other nodes (H/V), `Shift` constrains to axis, `Alt` disables snapping.
- Dragging a member moves both its nodes.
- Arrow keys nudge by one snap step; `Shift`+arrow by 5 steps.
- A load arrow's tip is a handle: drag to change magnitude and direction; `Shift` snaps to 15°.
- Live analysis during drag, coalesced into one undo entry on release.

**Tools** (single-key, no modifier; always return to Select with `V` or `Esc`)
- `V` Select — as above.
- `N` Node — click places a node on the snapped point. Clicking on a member splits it at that point.
- `M` Member — click a node to start, click another node to connect; clicking empty space creates a node there and continues chaining. `Esc` or double-click ends the chain. Members that would duplicate an existing one are refused with a toast.
- `L` Load — click a node applies a default 10 kN downward load; press-and-drag from a node sets the vector directly (the drag length is the magnitude, 40 px = 10 kN at 100%).
- `S` Support — click a node cycles Pin → Roller → None. Context menu sets it explicitly.
- `H` Pan — drag pans. Also `Space`+drag or middle-button drag in any tool.

**Zoom and pan**
- Wheel zooms toward the cursor in 10% steps, clamped 5%–2000%, animated over 120 ms.
- `Ctrl+0` fits the structure, `Ctrl+1` goes to 100%, `Ctrl`+`=`/`-` steps.
- The grid is adaptive: minor lines every 0.5 m, major every 1 m; when minor spacing drops below 8 px the grid coarsens ×2, when major exceeds 200 px it refines.

**Drag and drop**
- Section presets in the member inspector can be dragged onto members on the canvas (pointer-based, in-app). Dropping applies the section; the target member highlights while hovering.
- Outline rows are not reorderable; order is by id.

**Keyboard**
- All commands are keyboard-reachable; the palette lists them. `Delete`/`Backspace` deletes, `Ctrl+D` duplicates (offset one grid step), `Ctrl+Z`/`Ctrl+Y` (and `Ctrl+Shift+Z`) undo/redo, `Ctrl+S` save, `Ctrl+O` open, `Ctrl+N` new, `F` fit, `1`–`4` display modes, `G` toggles grid snap.
- Shortcuts are suppressed while a text field has focus, except `Esc`, which commits and returns focus to the canvas.

**Context menus**
- Node: Set support ▸ (Pin / Roller / None), Clear load, Duplicate, Delete.
- Member: Set section ▸ (presets), Split at midpoint, Delete.
- Canvas: Add node here, Paste, Fit view, Show/hide grid.

**Commands and undo/redo**
- Every mutation is an `IEdit` with `Apply` and `Revert` and a human label ("Move 3 nodes"). The history is linear; redo clears on a new edit. Drags coalesce. The title strip shows the next undo label as a tooltip.

**Editing states**
- Tool states: Idle, Hover (element under pointer), Pressed, Dragging (with snap guide), Marquee, Chaining (member tool), VectorDrag (load tool), Panning.
- Field editing: number fields commit on `Enter`/blur, revert on `Esc`, step with arrows.

**Empty, loading, error states**
- Empty document: a quiet centered invitation on the canvas ("Press N to place a node, or start from a sample") with three sample buttons (Warren truss, Cantilever, Roof).
- Loading: file open is near-instant; the title shows the filename once loaded. Autosave restore on launch is silent.
- Unstable structure (mechanism): status chip turns to danger, a banner across the top of the canvas says "Mechanism — the structure can move freely. Add a support or triangulate." Forces are hidden; members draw neutral.
- No loads: chip says "No loads"; members draw neutral with a hint in the summary inspector.
- Invalid action (duplicate member, zero-length member, deleting nothing): toast with the reason.

**Feedback and confirmation**
- Destructive edits do not confirm; they are undoable. The only confirmation is "unsaved changes" on New/Open/close.
- Analysis status is always visible in the title strip and the max utilization in the status bar.

**Discoverability**
- Tool tooltips carry the key. The palette carries everything. Empty state teaches the first two keys. Status bar hint text changes per tool ("Click a node to start a member · Esc to finish").

### Persistent vs contextual vs progressive

| Persistent | Contextual | Progressive |
|---|---|---|
| tool rail, canvas, status bar, undo/redo, status chip | inspector content, floating bar, context menu, snap guides | display modes, section library, palette, deflection exaggeration |

---

## Stage 3 — Visual system

### Direction

Calm, precise, technical. The canvas is the hero; chrome is quiet. The reference world is the engineer's drafting table: dark blue-gray paper, hairline grid, one accent for the pointer's intent, and the *structure itself* carrying all the color.

**Signature element: force ribbons.** Members are drawn with stroke width proportional to |axial force| and colored by sign (copper for tension, steel-blue for compression), with a dark core line on compression members so sign is never color-alone. When a node is dragged, the ribbons breathe. This one element is what the app is remembered by. Everything else stays on hairlines and neutrals.

Anti-slop check: no cards, no gradients, no glow, no rounded 16 px containers, no icon-only toolbars without labels, no accent used for decoration.

### Color roles (dark, single theme)

| Role | Value | Use |
|---|---|---|
| Canvas | `#0F1216` | workspace background |
| Grid minor / major | `#FFFFFF` @ 4% / 8% | grid hairlines |
| Surface | `#151A20` | panels, bars |
| Surface raised | `#1B212A` | fields, hover rows, popovers |
| Hairline | `#FFFFFF` @ 7% | borders, dividers |
| Ink | `#E7EBF0` | primary text |
| Ink secondary | `#98A2B3` | labels, secondary text |
| Ink tertiary | `#5F6B7A` | hints, disabled |
| Accent | `#7DB1FF` | selection, focus, active tool, pointer intent only |
| Tension | `#D9924A` | member in tension |
| Compression | `#5D9AD6` | member in compression |
| Neutral member | `#8791A0` | zero force / unsolved |
| Danger | `#F25F5C` | overstressed members, unstable state, destructive confirm |
| Ground | `#3A424D` | support hatching, reactions |

Utilization ramp (Utilization mode only): `#4FB286` → `#D9C24A` → `#F25F5C` interpolated in OKLab-ish steps (three stops, linear).

### Typography

- **Inter** (static Regular 400, Medium 500, SemiBold 600) for UI. Sizes: 11 (captions, keycaps), 12 (body, rows), 13 (field values, inspector titles), 15 (document name). No display sizes; the biggest text in the app is 15 px.
- **JetBrains Mono** (static Regular, Medium) for every number: coordinates, forces, dimensions, status readouts, canvas labels. Tabular by nature; all numeric columns align.
- Line height 1.4 in panels. Uppercase 11 px Medium at +6% tracking for section labels (tracking implemented as spacing between per-character blocks only where it matters; elsewhere plain uppercase, because `CharacterSpacing` is a no-op on Skia).

### Spacing and density

4 px base. Panel padding 12. Row height 28. Field height 26. Tool button 32 in a 40 rail. Gaps: 4 within a control group, 8 between controls, 16 between sections. Density is high on purpose; whitespace is spent between sections, not inside controls.

### Surfaces, borders, elevation, corners

- Flat surfaces separated by 1 px hairlines. No drop shadows on docked panels.
- Popovers (context menu, palette, flyouts): Surface raised + hairline + one soft shadow (`#000` @ 24%, blur 24, y 8). This is the only elevation in the app.
- Corner radius: 4 on fields and buttons, 6 on popovers, 0 on docked panels. Pills use exactly half the height.

### Iconography

Path-based 16 px line icons at 1.5 px stroke, drawn as `PathIcon` geometry inside a `Viewbox` (Skia clips `PathIcon` rather than scaling). Icons always sit next to a label in menus and the palette; the tool rail is the one icon-only surface, and it is compensated by tooltips with keys.

### Selection, hover, focus, pressed

- Hover: element gets a 2 px lighter halo (ink @ 20%); cursor changes (hand for nodes, move for drag).
- Selected node: accent fill, ink core dot, 1.5 px accent ring at 6 px radius. Selected member: accent halo (8 px wide @ 35%) behind the force ribbon. Selection never replaces the force color.
- Marquee: accent 1 px stroke, accent @ 8% fill.
- Snap guide: accent dashed 1 px line across the canvas; snapped node flashes its ring 1 → 1.15 → 1 over 150 ms.
- Focus (keyboard): 1.5 px accent ring, 2 px offset, on every focusable control.
- Pressed: scale 0.98 over 150 ms.
- Active tool: accent icon on Surface raised with a 2 px accent bar on the rail's left edge.

### Grid and alignment

Panels align to an 8 px column grid; labels left-aligned, numbers right-aligned in a fixed 72 px value column. The canvas grid is world-space (meters), the chrome grid is pixel-space; they never mix.

### Motion principles

- Durations from the house tokens: fast 150, normal 200, slow 280 ms. Curves `EaseSmooth (0.22,1 0.36,1)` default, `EaseOut (0.17,1 0.32,1)` entrances, `EaseInOut (0.66,0 0.34,1)` viewport moves.
- Motion only communicates: viewport moves (fit/zoom) ease so the user keeps spatial context; inspector content cross-fades on selection change (150) so the eye sees a swap, not a flash; snap catch flashes (150); popovers fade + rise 6 px (200); toasts fade + rise (200), auto-dismiss 2.4 s; deflection exaggeration changes lerp (200).
- Nothing loops. Nothing delays input. Analysis feedback is immediate, unanimated.
- Reduced motion (`UISettings.AnimationsEnabled == false`): all of the above jump to the end state.

### Contextual emphasis

When one member is selected, other members dim to 55% opacity in the canvas so the selection and its connected nodes stand forward. Hover on an outline row highlights the element on canvas with the hover halo.

---

## Stage 4 — Core workspace, in detail

### Coordinate and layout model

- World space: meters, Y up. Document coordinates are doubles.
- Screen space: device-independent pixels, Y down, origin at the element's top-left.
- `Viewport { Scale (px per m), OffsetX, OffsetY }`, `ToScreen(p) = (p.X * Scale + OffsetX, -p.Y * Scale + OffsetY)`. Zoom-at-point keeps the world point under the cursor fixed.
- All hit tolerances are specified in screen pixels and converted to world by dividing by `Scale`.

### Rendering model

One `SKCanvasElement` (`WorkspaceView`) draws, in order: canvas fill, grid, axis, deflected ghost (when enabled), members (ribbons), member labels, loads, supports, reactions, nodes, snap guides, marquee, hover and selection adorners, chaining rubber band, empty state is XAML above the canvas. Text on the canvas uses `SKFont` with the JetBrains Mono typeface loaded once from assets. Paints are allocated once and reused; the renderer holds no per-frame allocations beyond paths.

The renderer reads from a `RenderSnapshot` assembled on the UI thread (document geometry, analysis result, selection, hover, tool overlay, viewport). Analysis and document are immutable-per-frame from the renderer's point of view.

Invalidation is explicit: any document, selection, viewport, or tool-state change calls `Invalidate()`. There is no `CompositionTarget.Rendering` subscription. Viewport animations run on a `DispatcherTimer` at ~60 Hz that stops when the animation settles.

### Viewport, zoom, pan behavior

As in Stage 2. Fit computes the structure's bounds with 15% padding. The initial view for an empty document is 100% with the origin at the lower-left third.

### Selection model and hit testing

- Hit test order: node handles (radius 8 px), load arrow tips (radius 7 px), members (distance to segment ≤ 5 px), else empty.
- Hit results are `ElementRef` (kind + id) or `LoadHandle(nodeId)`.
- Marquee uses the world-space AABB of the marquee against node points and member segments (both endpoints inside for "contained", any point inside or intersecting for "touching").

### Snapping and alignment

`SnapEngine.Snap(worldPoint, exclude: draggingNodes)`:
1. If a node (not excluded) is within 10 px, snap to it (member tool only; select tool ignores node snap while dragging).
2. Alignment guides: any node with |Δx| ≤ 6 px or |Δy| ≤ 6 px snaps that axis and yields a guide line.
3. Grid: round to the active grid step (0.5 m default; `G` toggles).
The result carries the snapped point and up to two guides for rendering.

### Object hierarchy

`StructureDocument` → `Nodes` (id, position, support, load) and `Members` (id, start, end, section) → `Sections` (id, name, area, second moment, material). Nodes own their support and load, so the outline stays two-level and one node has at most one load vector.

### Manipulation handles

Nodes are handles themselves. Load tips are handles. Members have no handles; dragging a member moves its nodes. No rotate/scale handles: trusses are edited node by node, on purpose.

### Contextual UI, overlays, guides

XAML overlay layer (a `Canvas` above the `SKCanvasElement`) hosts: the floating selection bar, the mechanism banner, the empty state, toasts. Positions come from `Viewport.ToScreen`. Snap guides, marquee, and rubber bands are drawn in Skia because they must be pixel-registered with the structure.

### Keyboard and pointer behavior

- Pointer events are handled on `WorkspaceView` and dispatched to the active `ITool` with a `ToolContext` (document, selection, history, viewport, snap engine, hit tester, toast). Tools return whether they consumed the event. Space and middle button pre-empt to Pan.
- Keyboard events are handled at the page (`PreviewKeyDown`) and dispatched to a `ShortcutMap` unless a text input has focus. The active tool also receives keys (`Esc`, `Shift`, `Alt` change snap behavior mid-drag).

### Command system

`ICommandRegistry` holds every `AppCommand { Id, Title, Category, Shortcut, CanExecute, Execute }`. Menus, palette, floating bar, and keyboard all invoke through the registry, so a shortcut label is never out of sync with the action.

---

## Architecture Brief

### Module structure

```
Loadpath/                        (solution root)
  Loadpath.Core/                 net10.0 class library, no UI dependencies
    Model/        StructureDocument, Node, Member, Section, Material, SupportKind, ElementRef
    Analysis/     TrussSolver, AnalysisResult, MemberResult, LinearSolver
    Editing/      IEdit, EditHistory, edits (AddNode, MoveNodes, AddMember, RemoveElements, SetSupport, SetLoad, SetSection, SplitMember, Duplicate)
    Selection/    SelectionSet
    Viewport/     Viewport, ViewportAnimator (pure math)
    Geometry/     Vec2, HitTester, SnapEngine, Bounds
    Serialization/ DocumentSerializer (JSON v1)
    Samples/      SampleStructures (Warren, Cantilever, Roof)
  Loadpath/                      Uno single-project app, net10.0-desktop
    App.xaml(.cs), MainPage.xaml(.cs)
    Presentation/ EditorViewModel, InspectorViewModel (+ Node/Member/Mixed/Summary), OutlineViewModel, StatusViewModel, PaletteViewModel, ViewOptions
    Commands/     AppCommand, CommandRegistry, ShortcutMap
    Workspace/    WorkspaceView (SKCanvasElement), WorkspaceRenderer, RenderSnapshot, Palette (Skia colors), Tools/ (ITool, ToolContext, SelectTool, NodeTool, MemberTool, LoadTool, SupportTool, PanTool)
    Controls/     NumberField, ToolButton, SegmentedControl, KeyCap, SectionLabel, UtilizationBar, Toast, CommandPalette, FloatingBar
    Services/     IFileService (+ Skia desktop implementation), SettingsService, AutosaveService, ClipboardService (in-app)
    Themes/       Tokens.xaml (colors, brushes, fonts, sizes), Typography.xaml, Controls.xaml (styles), MotionTokens.xaml, Icons.xaml
    Assets/Fonts/ Inter (3 static), JetBrainsMono (2 static)
  Loadpath.Tests/                xunit, references Core
```

### State model

**Decision: MVUX for the presentation layer, an imperative engine underneath.** (Revised after the first build shipped with MVVM; the owner asked for MVUX.)
Reason: everything the views show is app-level state that fits states and feeds, and every action is a generated or feed-gated command. The document itself is mutated synchronously at pointer-move rate with a coalescing undo history, so it stays imperative in `EditorEngine` and the model projects its events into immutable records.
Tradeoff: two layers, `{Binding}` with converters instead of compiled `x:Bind`, and a plain mirror of the option states for the renderer.

State ownership:
- `StructureDocument` (Core): the truth. Raises `Changed(DocumentChange)`.
- `EditHistory` (Core): undo/redo; the only path for mutations from the UI.
- `SelectionSet` (Core): selected `ElementRef`s; raises `Changed`.
- `Viewport` (Core): pan/zoom; raises `Changed`.
- `AnalysisResult` (Core): recomputed by `EditorViewModel` on every document change (synchronous; measured).
- `EditorViewModel` (App): composes the above, exposes tool state, view options, commands, and derived VMs. Sub-VMs subscribe to the same events. No sub-VM mutates the document except through `EditHistory`.
- `ViewOptions` (App, persisted): display mode, show labels, show deflection, exaggeration, show reactions, snap on, grid step.

Data flow: input → tool → `EditHistory.Do(edit)` → document `Changed` → `EditorViewModel` re-solves → `AnalysisChanged` → renderer snapshot + inspector + status update → `WorkspaceView.Invalidate()`.

### Navigation model

None (single page). The `Frame` hosts `MainPage` directly; no Uno navigation extensions.

### Services and dependencies

- `Uno.Sdk 6.7.30` (pinned via `global.json`, same as ReticleLab), `net10.0-desktop` only.
- UnoFeatures: `SkiaRenderer` (brings `Uno.WinUI.Graphics2DSK` for `SKCanvasElement`). No Material, no Toolkit, no Extensions hosting: the app has three services and constructs them in `App`.
- `CommunityToolkit.Mvvm` for `ObservableObject`/`RelayCommand`.
- `System.Text.Json` (in-box) for documents and settings.
- Fonts: Inter static TTFs (OFL), JetBrains Mono static TTFs (OFL, copied from the repo's Meridian assets).

### Platform constraints

- Desktop Skia only. `SKCanvasElement.IsSupportedOnCurrentPlatform()` is checked; a text fallback shows otherwise.
- `UIElement.Opacity` does not affect `SKCanvasElement`; alpha is baked into paints.
- No `CompositionTarget.Rendering` subscription held; timers only while animating.
- `CharacterSpacing` is a no-op on Skia; tracking is not relied on.
- Composition blur is unavailable; entrances use opacity + translate only.
- `UseStudio()` is gated behind `APP_NO_HOTDESIGN != "1"` so headless runs show a window.
- File pickers are called on the UI thread; a Documents-folder fallback path exists if the picker is unavailable on the host.

### Testing and validation

- `Loadpath.Tests` (xunit): solver on a 3-node triangle with a known analytic answer, a Warren truss symmetry check, mechanism detection, `EditHistory` undo/redo/coalesce, `Viewport` zoom-at-point invariants, `SnapEngine` guide output, `HitTester` tolerances, serializer round-trip.
- Runtime: build `net10.0-desktop`, run under Xvfb with `APP_NO_HOTDESIGN=1`, capture screenshots with `import`, and drive deterministic states through DEBUG env hooks (`LOADPATH_SAMPLE`, `LOADPATH_SELECT`, `LOADPATH_MODE`, `LOADPATH_PALETTE`).

---

## Design Brief

- **Visual direction**: dark drafting table; the structure carries all color; chrome is hairlines and neutrals. See Stage 3.
- **Layout structure**: title strip 36 / tool rail 40 / workspace / inspector 280 / status 26. Inspector column splits inspector (auto height, scrolls) over outline (fills).
- **Typography**: Inter 11/12/13/15 for UI, JetBrains Mono for all numbers. Section labels uppercase 11 Medium.
- **Spacing**: 4 px base; 12 panel padding; 28 rows; 26 fields; 16 between sections.
- **Component hierarchy**: MainPage → TitleStrip, ToolRail, WorkspaceHost (WorkspaceView + overlay Canvas: FloatingBar, MechanismBanner, EmptyState, ToastHost), InspectorColumn (InspectorHost → SummaryInspector | NodeInspector | MemberInspector | MixedInspector; OutlinePanel), StatusBar, CommandPalette (overlay).
- **Theme usage**: single dark theme. Tokens in a plain root dictionary merged in App.xaml. `RequestedTheme="Dark"`. No Material.
- **Responsive/adaptive**: below 1100 px width the inspector column collapses to an edge toggle; below 800 px the tool rail becomes a horizontal strip under the title. The canvas always fills the rest.

## Interaction Brief

- **User flows**: (1) Start → sample or empty → place nodes (N) → connect (M) → support (S) two nodes → load (L) a node → read ribbons → drag a node to explore → save. (2) Open file → select critical member from summary → change section → utilization drops → save. (3) Ctrl+K → "deflection" → toggle → adjust exaggeration in status bar.
- **Input behavior**: as specified in Stage 2. Pointer capture on press; release or capture-lost ends the gesture and commits or cancels cleanly.
- **Empty states**: empty document invitation; empty selection → summary inspector; outline with zero items shows "No elements yet".
- **Loading states**: file open and sample load are synchronous; the title strip shows the name when done. No spinners.
- **Error states**: mechanism banner + danger chip; overstressed members danger-colored with a hatch; toasts for refused actions; unsaved-changes prompt.
- **Animations/transitions**: viewport fit/zoom (280 EaseInOut / 120 EaseSmooth), inspector cross-fade (150), popover fade+rise (200 EaseOut), toast (200), snap flash (150), press 0.98 (150). Reduced motion respected.
- **Feedback states**: hover halo, selection ring, active tool bar, status hint per tool, undo label, chip color.
- **Accessibility**: every command on the keyboard; visible focus; `AutomationProperties.Name` on all controls; tension/compression carry a sign label and a line style, not only color; minimum 11 px text at ≥ 4.5:1 on Surface; tool buttons 32 px targets.
- **Runtime verification steps**: build; run under Xvfb; capture (a) empty state, (b) Warren sample solved in Forces mode, (c) node selected with inspector, (d) Utilization mode, (e) Deflection mode, (f) palette open, (g) mechanism state; assert the visual tree contains the inspector title for the selected node; run xunit.

---

## Implementation Plan

1. **Foundation**: solution, `Loadpath.Core` (model, edits, history, selection, viewport, geometry, serializer, samples), `Loadpath.Tests` passing, app project scaffolded from ReticleLab's layout with `SkiaRenderer` only, tokens and fonts wired, shell layout static.
2. **Core workspace**: `WorkspaceView` + renderer (grid, nodes, members, loads, supports), viewport zoom/pan, hit testing, Select tool (click, shift, marquee, drag with snap), Node and Member tools. Live analysis with force ribbons.
3. **Supporting tools**: Load and Support tools, inspectors (summary/node/member/mixed), outline, floating bar, context menus, command registry + shortcut map + palette, status bar.
4. **Functional depth**: undo/redo everywhere with coalescing, save/open/new/autosave/settings, duplicate/nudge/split, display modes (Utilization, Deflection, Labels, Reactions), section drag-drop, samples.
5. **Polish**: motion tokens applied, dimming on selection, empty/mechanism/toast states, reduced motion, focus visuals, tooltips with keycaps, responsive collapse.
6. **Validation**: xunit run, Xvfb screenshots of each state, fix what reads wrong, README.

## Unresolved Questions

- File pickers on the Linux X11 host: if `FileSavePicker` is not functional headless, the Documents-folder fallback ships and the picker path is verified on Windows by the user. Accepted as a risk, time-boxed to 10 minutes.
- `SKFont` text rendering of the bundled JetBrains Mono through `SKTypeface.FromStream` on `net10.0-desktop`: expected to work (Liveline draws text this way in this repo); verify in the first canvas screenshot.
- Keyboard focus routing from the page to the canvas after clicking a `SKCanvasElement` (not focusable): solved with a focusable `FocusSink` control; verify `PreviewKeyDown` reaches the page on Skia desktop within the first workspace milestone.

---

## Stage 5 — Share, presets, failure highlighting

Three additions that make a design leave the app, start faster, and say clearly where it breaks. Saving to `.loadpath` files already ships (Stage 4); this stage adds a text form of the same document that travels through chat, issues and email.

### Architecture Brief

- **Module structure**: all logic lands in `Loadpath.Core` so it is unit-tested without UI. `Serialization/ShareCode` (encode/decode), `Samples/TrussPresets` (parametric generators, the existing samples stay as fixed entries), `Analysis/FailureMode` plus a `Failures` list on `AnalysisResult`. The app adds commands, one flyout and renderer layers only.
- **State model**: no new document state. A share code is a pure function of the document. Preset parameters (span, panels, depth) are three MVUX states on `EditorModel`; the preset list is a list state rebuilt from them. Failures are part of `AnalysisResult`, projected into an `InspectorContent` field and a `FailureItem` list state.
- **Navigation model**: unchanged. Presets open in a title-strip flyout; share and paste are commands (title strip, palette, shortcuts).
- **Services/dependencies**: clipboard through `Windows.ApplicationModel.DataTransfer.Clipboard`. When it throws, the code is written next to the Documents fallback folder and a toast says where. Compression with `System.IO.Compression.DeflateStream`. No new packages.
- **Data flow**: Share: document → `DocumentSerializer` compact JSON → deflate → base64url → `LP1.` prefix → clipboard. Paste: clipboard text → `ShareCode.TryDecode` (accepts a code, a code inside surrounding text, or raw `.loadpath` JSON) → `DocumentSnapshot` → `ReplaceDocumentEdit`. Presets: parameters → generator → `DocumentSnapshot` → `ReplaceDocumentEdit`.
- **Platform constraints**: X11 clipboard works only with an owner window alive; the fallback file covers hosts without one.
- **Testing/validation approach**: xunit for share round-trip (exact equality of nodes, members, loads, supports, sections), corrupt and foreign input, raw JSON fallback; every preset at several parameter sets solves stable and below a sanity utilization; failure mode classification (yield vs buckling) on a hand-checked strut. Runtime: Xvfb drive through share → new → paste, preset flyout, an overloaded structure with buckling and yield failures.

### Design Brief

- **Visual direction**: the blueprint sheet stays. Failure is red, and its *mode* is spelled, never only colored: pills read `BUCKLES 132%` or `YIELDS 118%`.
- **Buckled shape**: a member failing by buckling gets a thin red half-sine bow beside its ribbon (the first Euler mode), drawn dashed like the deflected ghost. It is the one new drawing primitive and it reads as "this strut bows out".
- **Presets flyout**: same `SheetFlyoutPresenter` as Reduce noise. Header row of three mono number fields (SPAN m, PANELS, DEPTH m), then a two-column grid of preset tiles: a line thumbnail of the generated geometry (blueprint ink on paper) above a mono caption and a one-line description.
- **Typography/spacing**: existing tokens only. Mono 11–12 for captions, Inter for descriptions, 8/12/16 spacing.
- **Component hierarchy**: TitleStrip → `PRESETS` link button (flyout) · `SHARE` link button · `OPEN` · `SAVE`. Inspector summary → "Failures" section listing rows (M id, mode, utilization) under the max-utilization bar.
- **Responsive behavior**: `SHARE` and `PRESETS` collapse with the search button below 720 px; both stay reachable through the palette.

### Interaction Brief

- **User flows**: *Share*: click SHARE or `Ctrl+Shift+C` → toast "Share code copied (1.2 kB). Paste it in Loadpath with Ctrl+Shift+V." *Paste*: `Ctrl+Shift+V` or palette "Paste design" → document replaced, view fits, toast "Pasted Warren truss · Ctrl+Z to undo". *Preset*: PRESETS → adjust span/panels/depth → click a tile → document replaced, flyout closes, view fits, toast "Pratt truss · Ctrl+Z restores your previous structure". *Failures*: `J` cycles through failing members, worst first; with none failing it selects the governing member as before.
- **Input behavior**: number fields clamp (span 2–60 m, panels 2–16 even-rounded where a preset needs it, depth 0.3–12 m). Paste accepts leading/trailing text so a code copied out of a chat message still works.
- **Empty states**: the empty-state invitation adds "or paste a share code (Ctrl+Shift+V)". The inspector failures section is hidden when nothing fails.
- **Loading states**: none; every step is synchronous and sub-millisecond.
- **Error states**: clipboard empty or unreadable → toast "Clipboard has no Loadpath design". Malformed code → "That share code is damaged or incomplete". Newer version → the serializer's version message.
- **Animations/transitions**: flyout uses the existing sheet presenter motion; nothing new.
- **Feedback states**: toasts for copy/paste/preset; status pill unchanged; failing rows in the inspector highlight on hover and select on click.
- **Accessibility**: tiles and failure rows are buttons with `AutomationProperties.Name` ("Load Pratt truss preset", "Select member M7, buckles at 132%"); the failure mode is text, not color; shortcuts listed in tooltips and the palette.
- **Runtime verification steps**: build; Xvfb run; capture the presets flyout, a preset loaded, an overloaded Warren with `BUCKLES`/`YIELDS` pills and buckled glyphs plus the failures list, share → new → paste round trip (document name and member count match).

### Implementation Plan

1. Core: `ShareCode`, `TrussPresets`, failure mode on `MemberResult` and the `Failures` list. Tests.
2. Engine: share/paste/preset/cycle-failure commands; undoable replacement.
3. UI: title-strip PRESETS flyout and SHARE; inspector failures section; empty-state hint.
4. Renderer: mode-labelled pills, buckled half-sine glyph, legend entry.
5. Xvfb verification, README update.

### Unresolved Questions

- Should a share code also carry view options (display mode, exaggeration)? Left out: the code describes the structure, and the receiver keeps their own view.
- URL form (`https://…/#LP1.…`) needs a hosted viewer; out of scope until there is one.
