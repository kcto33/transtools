# Screenshot Text Annotation Design

## Goal

Add a text annotation tool to screenshot editing. The tool lets users place typed text directly on the selected image, next to the existing brush tool in the annotation toolbar. Text annotations use the existing color palette and annotation size slider, where the slider controls font size.

## Scope

- Add text annotation to the rectangular screenshot editor in `ScreenshotOverlayWindow`.
- Add the same text annotation behavior to the freeform screenshot editor in `FreeformScreenshotWindow`.
- Include text annotations in copy, save, and pin output through the existing compositing renderer.
- Include text annotations in undo and clear behavior through the existing annotation session.
- Keep the existing brush, rectangle, arrow, and mosaic behavior unchanged.

Out of scope:

- Drag-resizable text boxes.
- Editing an already committed text annotation.
- Rich text, font family selection, background fills, outlines, or rotation.
- Persisting annotation defaults in app settings.

## User Interaction

The toolbar adds a `Text` button immediately after `Brush` and before `Rectangle`.

When the text tool is active:

1. The cursor indicates text placement.
2. The user clicks inside the editable screenshot area.
3. A lightweight inline text box appears at that point.
4. The user types text.
5. Pressing `Enter` commits the annotation. Losing focus also commits it.
6. Pressing `Esc` cancels the current inline text entry without adding an annotation.

Committed text uses the current annotation color and current annotation size. Empty or whitespace-only text is ignored. The annotation is clipped to the same editable mask used by the other annotation tools, so freeform screenshots do not draw text outside the selected shape.

## Architecture

The feature extends the existing annotation model instead of creating a separate rendering path.

- `ScreenshotAnnotationTool` gains a `Text` value.
- `ScreenshotAnnotationOperation` gains a `TextAnnotationOperation` record containing:
  - `Point Location`
  - `Geometry ClipMask`
  - `string Text`
  - `Color Color`
  - `double FontSize`
- `ScreenshotAnnotationSession` gains `CommitText(...)`.
- `ScreenshotAnnotationRenderer.RenderComposite(...)` handles text operations and draws formatted text with WPF drawing primitives.
- `ScreenshotOverlayWindow` and `FreeformScreenshotWindow` each host an inline `TextBox` inside `EditSurface` for text entry.

Coordinates are stored in image pixels, matching the existing brush, rectangle, and arrow operations. The inline text box is positioned in preview DIPs and converted to image coordinates when committed. Font size is scaled with the existing average edit scale so output size matches the preview across DPI and display-size differences.

## UI Details

The existing toolbar already has annotation color buttons and a compact size slider. Text reuses both controls without adding a second palette or font-size control.

Toolbar order:

`Save`, `Copy`, `LongScreenshot`, `Gif`, `Redraw`, `Pin`, `Brush`, `Text`, `Rectangle`, `Arrow`, size/color controls, `Mosaic`, `Undo`, `Cancel`

For freeform screenshot:

`Brush`, `Text`, `Rectangle`, `Arrow`, size/color controls, `Mosaic`, `Undo`, `Clear`, `Pin`, `Copy`, `Save`, `Redraw`, `Cancel`

The text button uses localized resources:

- `Screenshot_Annotate_Text`
- `Screenshot_Tooltip_Annotate_Text`

## Error Handling

Text commit is best-effort and local-only. Empty text is ignored. If the selected image is missing, existing save/copy/pin guards continue to prevent output operations. No modal errors are introduced.

If the user cancels or redraws while an inline text box is open, the draft text is discarded and the text box is hidden.

## Testing

Add focused tests before production changes:

- `ScreenshotAnnotationSession` stores a text operation with text, color, font size, location, and clip mask.
- Empty text does not add an operation.
- `ScreenshotAnnotationRenderer` changes pixels when rendering a text operation and preserves pixels outside the clip mask.
- `ScreenshotOverlayWindow.GetToolbarButtonOrder()` includes `Text` immediately after `Brush`.
- Existing output image tests continue to pass with mixed annotation operations.

Run:

- `dotnet test .\ScreenTranslator.Tests\ScreenTranslator.Tests.csproj -c Debug`
- `dotnet build .\ScreenTranslator\ScreenTranslator.csproj -c Debug`
