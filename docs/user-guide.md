# ScreenTranslator User Guide

## Requirements
- Windows 11 22H2+ (build 22621+)
- If running from source: .NET 8 SDK

## Launch and Tray Menu
- The app runs in the system tray (notification area).
- Double-click the tray icon to start a translation selection.
- Right-click the tray icon for the menu:
  - Start selection
  - Paste history
  - Screenshot
  - Settings
  - Start with Windows
  - Exit

## Translate by Selection
1) Press `Ctrl+Alt+T` or choose "Start selection" from the tray menu.
2) Drag to select a region. Press `Esc` to cancel.
3) A bubble appears above the selection with the translation.

Bubble actions:
- Left-click: toggle between translated text and original text.
- Right-click: copy the currently displayed text.
- The bubble auto-closes after about 6 seconds or when you click outside it.

Note: Selection is clamped to the monitor where you start the drag.

## Paste History (Clipboard Picker)
1) Press `Ctrl+Shift+V` or choose "Paste history" from the tray menu.
2) A small picker appears near the cursor with recent text entries.
3) Use `Up`/`Down` to select, `Enter` to paste, `Esc` to close.

Details:
- Only text is stored.
- Duplicates are removed (latest stays on top).
- History size is configurable (1-20 items).

## Screenshot and Pin
1) Press `Ctrl+Alt+S` or choose "Screenshot" from the tray menu.
2) Drag to select a rectangle.
3) Use the toolbar:
   - Pin: keep the captured image on screen (always on top).
   - Copy: copy to clipboard.
   - Save: save to file.
   - Long Screenshot: fixed-frame manual scroll stitching.
   - Freeform: switch to freeform capture.
   - Cancel: close the overlay.

Shortcuts in rectangle mode:
- `Enter`: copy selection.
- `Esc`: cancel.
- Middle mouse button: pin the selection.

Freeform mode:
- Hold left mouse button to draw a shape, release to finish.
- Toolbar offers Pin / Copy / Save / Redraw / Cancel.
- Press `Esc` to cancel.

Pinned image window:
- Drag to move.
- Mouse wheel to zoom.
- Right-click for Copy / Save / Close.
- Double-click or `Esc` to close.

## Long Screenshot (Manual Scroll Stitch)
1) Press `Ctrl+Alt+S` to open screenshot mode.
2) Drag a rectangle over the scrollable content area.
3) Click `Long Screenshot` in the toolbar.
4) The long screenshot session starts immediately:
   - A fixed red frame indicates the capture area.
   - A compact control bar appears near the frame.
   - A preview panel appears near the frame with current frame + stitched tail + match markers.
5) During capture:
   - Scroll manually (mouse wheel / scrollbar / PageDown / Space) inside the selected app.
   - `Pause`: switch to blue frame and adjust position/size.
   - `Continue`: lock frame again and resume capture.
   - `Stop`: finish and output current result.
   - `Cancel`: abort without output.
   - `Hide Preview` / `Show Preview`: toggle preview panel visibility.
6) If matching fails:
   - A red marker is added in preview.
   - Click the red marker or click on stitched preview to apply manual seam correction.
   - You can also click `Skip` to ignore that frame.
7) After completion:
   - Result is copied to clipboard automatically.
   - If `Auto save screenshot` is enabled, it is also saved to file.
   - You can still click `Copy` / `Save` / `Pin`.

Notes:
- Vertical stitching only in this version.
- The app only processes frames after detected manual scroll attempts.
- The app auto-stops when content no longer changes, and also respects safety limits.
- Too many consecutive matching failures stop the session with partial output preserved.

## Settings
Open from the tray menu.

General tab:
- Start with Windows.
- UI language (restart required to apply).
- Hotkeys (click a box, press the key combo; `Esc` cancels).
- Clipboard history size (1-20).
- Source and target languages for OCR and translation.
- Screenshot options: auto copy, auto save, save path, file name format.
- Long screenshot options:
  - Wheel notches per step.
  - Frame interval (ms).
  - Max frames.
  - Max total height (px).
  - No-change diff threshold (%).
  - No-change consecutive count.

API tab:
- Provider: `mock`, `youdao`, `deepl`, `google`.
- Credentials:
  - Youdao: AppId + AppSecret
  - DeepL/Google: API key
- Endpoint/Region are optional advanced fields.
- Settings file: `%AppData%\\ScreenTranslator\\settings.json`
  - Secrets are stored using Windows DPAPI.

Bubble Style tab:
- Customize colors, font, padding, and width for:
  - Translation bubble
  - Paste history bubble
- Use Reset to restore defaults.

## Troubleshooting
- Hotkey does not work: choose a different key combo if a conflict dialog appears.
- Translation fails: the bubble will show a "(failed)" message; check provider settings and network access.
