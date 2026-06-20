# Floating Notes Design

## Goal

Add floating sticky notes to transtools. Users can press a hotkey to create a new note quickly, edit multiple notes at once, pin or unpin each note with the mouse middle button, and reopen saved notes from a note list.

## Confirmed Scope

- Add a dedicated floating-note hotkey, defaulting to `Ctrl+Alt+N`.
- Each hotkey press creates a new note instead of reopening the previous one.
- Multiple notes can be open at the same time.
- Each note stores its own content, position, size, pin state, and fixed color choice.
- A note hides its title buttons and formatting toolbar when it loses focus, so it looks like a plain sticky note.
- The middle mouse button toggles a note between pinned (`Topmost = true`) and unpinned (`Topmost = false`).
- Closing a note saves it automatically as an `.rtf` file in the configured note directory.
- Saved note files are named by date and time, for example `Note_20260602_204512.rtf`.
- Settings add a note save directory. If empty, the app uses `%AppData%\transtools\notes`.
- A note list window shows saved notes with file name, modified time, and preview text, and can reopen a saved note.
- The first version uses fixed color swatches only. It does not include arbitrary custom color selection.

## Architecture

`FloatingNoteController` owns the open note windows and note list window. It creates new notes for hotkey and tray requests, opens saved notes from the list, and closes all note windows at app exit.

`FloatingNoteStorageService` owns filesystem behavior: resolving the configured note directory, generating timestamped file names, saving RTF content, enumerating saved notes, and reading note preview text. The storage service keeps file operations explicit and scoped to the note directory.

`FloatingNoteWindow` is a WPF window that hosts a `RichTextBox` with a small title bar and formatting toolbar. It handles focus chrome visibility, drag, resize, fixed color selection, middle-button pin toggling, and close-time save.

`FloatingNoteListWindow` is a WPF list view for saved notes. It reads from the storage service and raises an open request when the user opens a note.

## Settings And Localization

`AppSettings` gains `FloatingNoteHotkey` and a `FloatingNotes` settings object. The settings object stores the note directory, default size, fixed color, and most recent position.

The settings window gains:

- hotkey field: Floating Note
- note save directory text box and browse button

Tray menu gains:

- Floating Note

Localized strings are added to both English and Chinese resource dictionaries.

## Testing

Focused tests cover non-visual behavior:

- note storage resolves the default directory and generates timestamped `.rtf` names
- note storage enumerates note files by modified time
- note window helper logic toggles pin state from middle mouse input
- settings hotkey conflict checks include the floating-note hotkey

Manual verification covers WPF-specific behavior:

- hotkey opens a new note each time
- multiple notes keep independent pinned/unpinned state
- losing focus hides buttons and toolbar
- closing a note writes an `.rtf` file to the configured directory
- note list reopens saved notes
