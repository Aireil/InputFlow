# InputFlow
Improve the native text inputs by restoring common text-editing shortcuts.

Supported shortcuts:
- CTRL + CTRL + LEFT/RIGHT to jump words
- CTRL + SHIFT + LEFT/RIGHT to select words
- CTRL + BACK/DELETE to delete words
- CTRL/SHIFT + INSERT to copy/paste
- SHIFT + DELETE to cut

Changes:
- LEFT/RIGHT selects even if the cursor is at the start/end respectively
- LEFT/RIGHT with an active selection unselects and puts the cursor at the beginning/end of the selection
