# Screenshot Annotation Editor Design

Date: 2026-04-19

## Goal

Add post-selection screenshot editing so users can annotate screenshots before copying, saving, or pinning them.

Scope includes both existing screenshot entry points:

- rectangular screenshot flow in `ScreenshotOverlayWindow`
- freeform screenshot flow in `FreeformScreenshotWindow`

In scope editing tools:

- brush
- rectangle outline
- mosaic brush
- undo last operation
- clear all annotations while keeping the current selection

Out of scope:

- text annotations
- arrows, ellipses, stickers, or other markup tools
- changing long screenshot behavior
- introducing a separate dedicated editor window
- settings UI for annotation colors or brush sizes in this task

## Problem

The current screenshot flows only let the user select an area and immediately pin, copy, or save it.

That makes common workflows awkward:

- highlighting a UI element requires another external tool
- hiding sensitive data requires another editor
- rectangular and freeform screenshots have no shared editing capability

The new requirement is to keep the existing fast screenshot workflow, but insert an editing phase after selection and before output.

## Chosen Approach

Keep the existing selection windows and add a second "edit mode" inside each one.

Behavior:

- the user first completes a selection
- the window switches into edit mode instead of immediately finalizing
- the toolbar exposes annotation tools and output actions in the same window
- both screenshot windows reuse a shared annotation session model and shared composition pipeline
- copy, save, and pin all export the composited result rather than the raw selection

This preserves the current lightweight workflow while avoiding duplicated editing logic across rectangular and freeform screenshots.

## Alternatives Considered

### 1. Implement separate editing logic inside each screenshot window

Pros:

- direct to implement initially
- low conceptual overhead for the first patch

Cons:

- duplicated state handling for tools, undo, and export composition
- high chance of drift between rectangular and freeform behavior
- makes future tools harder to add

### 2. Open a dedicated screenshot editor window after selection

Pros:

- clean separation between selection and editing responsibilities
- editor UI could evolve independently

Cons:

- heavier interaction than the current screenshot workflow
- larger change surface across controller and window lifecycle
- unnecessary for the requested narrow editing feature set

### 3. Edit while the user is still drawing the selection

Pros:

- potentially fewer explicit mode changes

Cons:

- conflicts with the existing selection gestures
- harder to explain and harder to implement consistently
- explicitly not the user-requested interaction model

## Interaction Design

### Two-phase workflow

Both screenshot flows will use the same high-level sequence:

1. select capture area
2. enter edit mode
3. annotate
4. copy, save, or pin the annotated result

Selection remains the first phase only. Annotation is never active while the user is still defining the selection.

### Rectangular screenshot flow

- the user drags a rectangular selection as today
- once selection completes, the toolbar appears in edit mode
- the selection rectangle remains fixed while the user annotates inside it
- starting a new rectangular selection remains the way to change the capture area

### Freeform screenshot flow

- the user draws and closes the freeform region as today
- once the region completes, the toolbar appears in edit mode
- the freeform mask remains fixed while the user annotates the resulting cropped image
- `Redraw` continues to mean "discard this freeform selection and draw the region again"

### Toolbar

The editing toolbar will contain the editing tools and output actions together:

- brush
- rectangle outline
- mosaic brush
- undo
- clear annotations
- pin
- copy
- save
- cancel

For freeform screenshots, `Redraw` remains available because it changes the selection itself rather than the annotations.

For rectangular screenshots, `Long Screenshot` remains available in the same toolbar so the existing flow is not removed.

Recommended behavior:

- no annotation tool is auto-armed immediately on entering edit mode
- clicking a tool makes it active
- the active tool stays selected until changed
- undo removes the most recently committed annotation operation
- clear annotations removes all annotations but keeps the captured selection

### Editing constraints

Annotations must only affect the captured result:

- rectangular screenshots: editable area is the rectangular crop bounds
- freeform screenshots: editable area is the masked non-transparent area of the freeform crop

This avoids accidental drawing outside the selected screenshot content.

## Architecture

### Existing responsibilities that remain

`ScreenshotOverlayWindow` continues to own rectangular selection.

`FreeformScreenshotWindow` continues to own freeform path capture and freeform-specific reset behavior.

`ScreenshotController` continues to launch those windows and should not gain annotation logic.

## New shared editing layer

Add a shared annotation editing layer that both screenshot windows use after selection is complete.

Recommended responsibilities:

- annotation session state
- annotation operation models
- preview composition
- final export composition
- tool-specific geometry generation
- mosaic rendering over selected regions

This layer should not own screenshot selection. It should operate only on an already-cropped base image plus an editable-area mask.

## Data model

The shared annotation session should track at least:

- the cropped base bitmap
- an editable bounds or mask
- the active tool
- committed annotation operations
- the in-progress operation, if any

Operation types should be explicit rather than implicit pixel diffs:

- brush stroke
- rectangle outline
- mosaic stroke

Each committed user action becomes a single undoable operation.

This keeps undo simple and makes preview/export deterministic.

## Coordinate system

All annotation coordinates should be normalized to the local coordinate space of the cropped image, not the original full-screen virtual desktop.

That means:

- selection windows convert from on-screen input to local crop coordinates when edit mode begins
- shared annotation logic works only in image-local coordinates
- export does not depend on the original desktop position

This is especially important for freeform screenshots, which already convert a masked selection into a cropped image with transparency.

## Rendering and export

### Preview path

The on-screen preview should be produced from:

- base screenshot bitmap
- editable-area mask
- committed operations
- current in-progress operation

Preview and export should use the same composition rules so the user sees the same result that gets copied, pinned, or saved.

### Export path

Copy, save, and pin should all request a final composited bitmap from the shared renderer.

That bitmap becomes the single source for:

- clipboard image
- file encoding
- pin window display

The export path should not separately reimplement brush, rectangle, or mosaic behavior.

### Mosaic behavior

Mosaic should behave like a brush, but instead of drawing a color stroke it should pixelate the brushed region of the screenshot.

Requirements:

- it only affects pixels under the mosaic stroke path
- it is clipped to the editable region
- it participates in undo like any other operation
- preview and final export use the same mosaic block logic

The implementation does not need to be parameterized by user settings in this task. A fixed, visually obvious block size is acceptable.

## Window-level behavior changes

### `ScreenshotOverlayWindow`

Expected changes:

- split its internal state into selection mode and edit mode
- keep the selection rectangle logic for phase one
- once the region is finalized, build the cropped image and annotation session
- replace selection gestures with annotation gestures while in edit mode
- update pin, copy, and save actions to use the composited image

The long screenshot entry point remains visible in rectangular screenshot edit mode.

Behavior:

- selection completed
- toolbar appears with screenshot actions, editing tools, and long screenshot entry still present
- if long screenshot is chosen, annotations are discarded and the long screenshot flow starts from the raw selected region

### `FreeformScreenshotWindow`

Expected changes:

- preserve existing freeform capture behavior for selection mode
- after the freeform region completes, build the cropped masked image and annotation session
- keep `Redraw` as a selection reset action
- route copy, save, and pin through the composited result

Freeform screenshots should treat transparent pixels outside the selection mask as non-editable.

## Error handling

The feature should stay best-effort and non-disruptive like the current screenshot flows.

Rules:

- if composition fails, the window must not crash the app
- clipboard/save failures continue to be silently tolerated at the window boundary
- undo on an empty stack is a no-op
- clear annotations on an empty session is a no-op
- if no annotation tool is active, drag input in edit mode should not create an operation

## Testing

Automated coverage should focus on the shared annotation layer rather than WPF UI automation.

Minimum automated checks:

- brush stroke operations are added and undone in order
- rectangle outline operations are added and undone in order
- mosaic operations affect only their stroked region
- clear annotations removes committed operations but preserves the base image/session
- freeform editable masks reject drawing outside the valid shape
- final composition includes all committed operations

Window-specific automated checks can stay narrow:

- edit mode actions request composited output instead of raw crops
- selection reset in freeform mode clears both the freeform region and annotation session

Manual verification should cover:

1. rectangular screenshot -> annotate with brush -> copy
2. rectangular screenshot -> annotate with rectangle -> save
3. rectangular screenshot -> annotate with mosaic -> pin
4. rectangular screenshot -> undo and clear annotations
5. freeform screenshot -> annotate with all tools
6. freeform screenshot -> redraw selection after entering edit mode
7. verify pin, copy, and save output match the visible preview
8. verify long screenshot still works from rectangular selection

## Risks

- edit-state handling could become tangled if selection logic and annotation logic stay interleaved inside the same window code-behind
- preview/export mismatch would be user-visible and hard to trust
- mosaic rendering may become slow if implemented as repeated full-image recomposition per mouse move

These risks are reduced by:

- introducing a shared annotation session with explicit operation objects
- using one composition path for both preview and export
- keeping selection and editing as distinct phases

## Recommendation

Implement post-selection annotation as a shared editing layer reused by the existing rectangular and freeform screenshot windows.

That is the smallest design that satisfies:

- shared behavior across both screenshot modes
- brush, rectangle, and mosaic editing
- undo and clear annotations
- consistent copy/save/pin output

without introducing a new editor window or duplicating annotation logic.
