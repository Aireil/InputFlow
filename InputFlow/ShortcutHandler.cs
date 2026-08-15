using System;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Component.GUI;

// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault

namespace InputFlow;

public unsafe class ShortcutHandler : IDisposable
{
    private readonly Configuration configuration;

    public ShortcutHandler(Plugin plugin)
    {
        configuration = plugin.Configuration;

        var address = AtkTextInput.Addresses.ProcessKeyShortcut.Value;
        if (address == nint.Zero)
        {
            Plugin.Log.Error("Could not find ProcessKeyShortcut address, aborting.");
            return;
        }

        processKeyShortcutHook = Plugin.GameInteropProvider.HookFromAddress<ProcessKeyShortcutDelegate>(
            address,
            ProcessKeyShortcutDetour
        );

        processKeyShortcutHook.Enable();
    }

    public void Dispose()
    {
        processKeyShortcutHook?.Dispose();
    }

    private const char MacroPlaceholder = '\x1F';

    private readonly Hook<ProcessKeyShortcutDelegate>? processKeyShortcutHook;

    private delegate byte ProcessKeyShortcutDelegate(AtkTextInput* atkTextInput, SeVirtualKey key, AtkTextInput.KeyModifiers* modifiers);

    private byte ProcessKeyShortcutDetour(AtkTextInput* atkTextInput, SeVirtualKey key, AtkTextInput.KeyModifiers* modifiers)
    {
        try
        {
            // skip when auto-translate is opened
            if (atkTextInput->CompletionDepth != 0)
            {
                return processKeyShortcutHook!.Original(atkTextInput, key, modifiers);
            }

            if (key == SeVirtualKey.INSERT)
            {
                var keyModifiersControl = new AtkTextInput.KeyModifiers
                {
                    IsControlDown = true
                };

                if (modifiers->IsControlDown)
                {
                    return processKeyShortcutHook!.Original(atkTextInput, SeVirtualKey.C, &keyModifiersControl);
                }

                if (modifiers->IsShiftDown)
                {
                    return processKeyShortcutHook!.Original(atkTextInput, SeVirtualKey.V, &keyModifiersControl);
                }
            }

            if (modifiers->IsControlDown)
            {
                switch (key)
                {
                    case SeVirtualKey.LEFT when modifiers->IsShiftDown:
                        SelectFromCursor(atkTextInput, GetPreviousWordBoundaryShift);
                        return 1;
                    case SeVirtualKey.LEFT:
                        MoveCursor(atkTextInput, GetPreviousWordBoundaryShift);
                        return 1;
                    case SeVirtualKey.RIGHT when modifiers->IsShiftDown:
                        SelectFromCursor(atkTextInput, GetNextWordBoundaryShift);
                        return 1;
                    case SeVirtualKey.RIGHT:
                        MoveCursor(atkTextInput, GetNextWordBoundaryShift);
                        return 1;
                    case SeVirtualKey.BACK:
                        DeleteFromCursor(atkTextInput, GetPreviousWordBoundaryShift);
                        return 1;
                    case SeVirtualKey.DELETE:
                        DeleteFromCursor(atkTextInput, GetNextWordBoundaryShift);
                        return 1;
                }
            }
            else if (key is SeVirtualKey.LEFT or SeVirtualKey.RIGHT && !modifiers->IsShiftDown)
            {
                if (BreakSelectionAndMoveCursor(key, atkTextInput))
                {
                    return 1; // the game would move the cursor
                }
            }
            else if (modifiers->IsShiftDown)
            {
                if (key is SeVirtualKey.DELETE)
                {
                    var keyModifiersControl = new AtkTextInput.KeyModifiers
                    {
                        IsControlDown = true
                    };

                    return processKeyShortcutHook!.Original(atkTextInput, SeVirtualKey.X, &keyModifiersControl);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Error in ProcessKeyShortcutDetour");
        }

        return processKeyShortcutHook!.Original(atkTextInput, key, modifiers);
    }

    private bool BreakSelectionAndMoveCursor(SeVirtualKey key, AtkTextInput* atkTextInput)
    {
        var (text, cursorPos) = GetTextAndCursorPos(atkTextInput);

        var isLeftKey = key is SeVirtualKey.LEFT;
        var isAtBoundary = isLeftKey
            ? cursorPos == 0 && text.Length > 0
            : cursorPos == text.Length && cursorPos > 0;
        var hasSelection = atkTextInput->SelectionStart != atkTextInput->SelectionEnd;

        if (!isAtBoundary && !hasSelection)
        {
            return false;
        }

        var finalPos = isLeftKey ? atkTextInput->SelectionStart : atkTextInput->SelectionEnd;

        // Wiggling to break the selection.
        // Wiggle is not needed when in the middle of the input, as move will unselect, but it does not matter.
        var keyModifiers = new AtkTextInput.KeyModifiers();
        processKeyShortcutHook!.Original(atkTextInput, isLeftKey ? SeVirtualKey.RIGHT : SeVirtualKey.LEFT, &keyModifiers);
        processKeyShortcutHook!.Original(atkTextInput, isLeftKey ? SeVirtualKey.LEFT : SeVirtualKey.RIGHT, &keyModifiers);

        MoveCursor(atkTextInput, (currentText, currentPos) =>
        {
            var startPos = (int)Math.Min(currentPos, finalPos);
            var endPos = (int)Math.Max(currentPos, finalPos);
            var shift = 0;

            for (var i = startPos; i < endPos; i++)
            {
                if (currentText[i] == MacroPlaceholder)
                {
                    var macroEndPos = currentText.IndexOf(MacroPlaceholder, i + 1);
                    if (macroEndPos == -1)
                    {
                        Plugin.Log.Error($"Could not find the end macro placeholder, this should not happen: {currentText} - {i}");
                        return 0;
                    }

                    i = macroEndPos;
                }

                shift++;
            }

            return currentPos < finalPos ? shift : -shift;
        });

        return true;
    }

    private static (string text, short cursorPos) GetTextAndCursorPos(AtkTextInput* atkTextInput)
    {
        var text = atkTextInput->EvaluatedInputString.AsReadOnlySeString().ExtractText(false, MacroPlaceholder.ToString());

        return (text, atkTextInput->CursorPos);
    }

    private static int GetPreviousWordBoundaryShift(string text, short currentPos)
    {
        if (text.Length <= 0 || currentPos <= 0)
        {
            return 0;
        }

        var pos = currentPos - 1;

        // skip non-word characters
        while (pos > 0 && !char.IsLetterOrDigit(text[pos]) && text[pos] != MacroPlaceholder) pos--;

        if (text[pos] == MacroPlaceholder)
        {
            // Directly return the shift, so it does not proceed inside the macro. Contrary to the next word
            // version, there is no need to look for the start as we will not proceed to shift more after this.
            return pos - currentPos;
        }

        // skip word characters
        while (pos > 0 && char.IsLetterOrDigit(text[pos - 1])) pos--;

        return pos - currentPos;
    }

    private int GetNextWordBoundaryShift(string text, short currentPos)
    {
        if (text.Length <= 0 || currentPos >= text.Length)
        {
            return 0;
        }

        var pos = (int)currentPos;
        var shift = 0;

        if (configuration.IsNextBoundaryAtWordEnd)
        {
            // Discord, VS Code, ...
            SkipNonWordCharacters();
            if (!TrySkipWordCharactersOrMacro())
            {
                return 0;
            }
        }
        else
        {
            // Windows, ImGui, Notepad, ...
            if (!TrySkipWordCharactersOrMacro())
            {
                return 0;
            }

            SkipNonWordCharacters();
        }

        return shift;

        bool TrySkipWordCharactersOrMacro()
        {
            // skip the macro if directly at the cursor
            if (pos < text.Length - 1 && text[pos] == MacroPlaceholder)
            {
                var macroEndPos = text.IndexOf(MacroPlaceholder, pos + 1);
                if (macroEndPos != -1)
                {
                    pos = macroEndPos + 1;
                }
                else
                {
                    Plugin.Log.Error($"Could not find the end macro placeholder, this should not happen: {text} - {pos}");
                    return false;
                }

                shift++;
            }
            else
            {
                // skip word characters
                while (pos < text.Length && char.IsLetterOrDigit(text[pos]))
                {
                    pos++;
                    shift++;
                }
            }

            return true;
        }

        void SkipNonWordCharacters()
        {
            // skip non-word characters
            while (pos < text.Length && !char.IsLetterOrDigit(text[pos]) && text[pos] != MacroPlaceholder)
            {
                pos++;
                shift++;
            }
        }
    }

    private void MoveCursor(AtkTextInput* atkTextInput, Func<string, short, int> getShift)
        => RepeatKey(atkTextInput, getShift, SeVirtualKey.LEFT, SeVirtualKey.RIGHT, new AtkTextInput.KeyModifiers());

    private void DeleteFromCursor(AtkTextInput* atkTextInput, Func<string, short, int> getShift)
        => RepeatKey(atkTextInput, getShift, SeVirtualKey.BACK, SeVirtualKey.DELETE, new AtkTextInput.KeyModifiers());

    private void SelectFromCursor(AtkTextInput* atkTextInput, Func<string, short, int> getShift)
        => RepeatKey(atkTextInput, getShift, SeVirtualKey.LEFT, SeVirtualKey.RIGHT, new AtkTextInput.KeyModifiers { IsShiftDown = true });

    private void RepeatKey(AtkTextInput* atkTextInput, Func<string, short, int> getShift, SeVirtualKey negativeKey, SeVirtualKey positiveKey, AtkTextInput.KeyModifiers keyModifiers)
    {
        var (text, currentPos) = GetTextAndCursorPos(atkTextInput);
        var shifts = getShift(text, currentPos);

        if (shifts == 0)
        {
            return;
        }

        var isMovingLeft = shifts < 0;
        var targetKey = isMovingLeft ? negativeKey : positiveKey;

        for (var i = 0; i < Math.Abs(shifts); i++)
        {
            processKeyShortcutHook!.Original(atkTextInput, targetKey, &keyModifiers);
        }
    }
}
