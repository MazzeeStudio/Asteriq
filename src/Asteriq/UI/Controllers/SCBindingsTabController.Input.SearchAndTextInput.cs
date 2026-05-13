using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private bool HasSearchSelection() =>
        _searchFilter.SelectionStart >= 0 && _searchFilter.SelectionEnd >= 0
        && _searchFilter.SelectionStart != _searchFilter.SelectionEnd;

    private (int start, int end) GetOrderedSelection()
    {
        int s = _searchFilter.SelectionStart;
        int e = _searchFilter.SelectionEnd;
        return s <= e ? (s, e) : (e, s);
    }

    private void DeleteSearchSelection()
    {
        var (start, end) = GetOrderedSelection();
        _searchFilter.SearchText = string.Concat(
            _searchFilter.SearchText.AsSpan(0, start),
            _searchFilter.SearchText.AsSpan(end));
        _searchFilter.CursorPos = start;
        _searchFilter.SelectionStart = -1;
        _searchFilter.SelectionEnd = -1;
        _searchFilter.ButtonCaptureTextActive = false;
        _searchFilter.CaptureDeviceHidPath = null;
    }

    private void ClearSearchSelection()
    {
        _searchFilter.SelectionStart = -1;
        _searchFilter.SelectionEnd = -1;
    }

    private const int MaxSearchLength = 50;

    private void ResetSearchCaptureState()
    {
        _searchFilter.ButtonCaptureTextActive = false;
        _searchFilter.CaptureDeviceHidPath = null;
    }

    /// <summary>
    /// Applies cursor movement with optional shift-selection. Returns the final cursor position.
    /// When shift is held, extends selection. Without shift, collapses selection to the
    /// appropriate edge (start for left movement, end for right movement).
    /// </summary>
    private int ApplySearchCursorMove(int cursor, int newPos, bool shift, bool collapseToStart)
    {
        if (shift)
        {
            if (_searchFilter.SelectionStart < 0)
                _searchFilter.SelectionStart = cursor;
            _searchFilter.SelectionEnd = newPos;
        }
        else
        {
            if (HasSearchSelection())
            {
                var (s, e) = GetOrderedSelection();
                newPos = collapseToStart ? s : e;
            }
            ClearSearchSelection();
        }
        return newPos;
    }

    private bool HandleSearchBoxKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        bool ctrl = keyData.HasFlag(Keys.Control);
        bool shift = keyData.HasFlag(Keys.Shift);
        var text = _searchFilter.SearchText;
        int cursor = _searchFilter.CursorPos;

        if (key == Keys.Escape)
        {
            _searchFilter.SearchBoxFocused = false;
            ClearSearchSelection();
            return true;
        }

        // Arrow keys â€” move cursor and optionally extend selection
        if (key == Keys.Left)
        {
            int newPos = ctrl ? FindWordBoundaryLeft(text, cursor) : Math.Max(0, cursor - 1);
            _searchFilter.CursorPos = ApplySearchCursorMove(cursor, newPos, shift, collapseToStart: true);
            return true;
        }

        if (key == Keys.Right)
        {
            int newPos = ctrl ? FindWordBoundaryRight(text, cursor) : Math.Min(text.Length, cursor + 1);
            _searchFilter.CursorPos = ApplySearchCursorMove(cursor, newPos, shift, collapseToStart: false);
            return true;
        }

        if (key == Keys.Home)
        {
            _searchFilter.CursorPos = ApplySearchCursorMove(cursor, 0, shift, collapseToStart: true);
            return true;
        }

        if (key == Keys.End)
        {
            _searchFilter.CursorPos = ApplySearchCursorMove(cursor, text.Length, shift, collapseToStart: false);
            return true;
        }

        // Clipboard operations
        if (ctrl && key == Keys.C)
        {
            if (HasSearchSelection())
            {
                var (s, e) = GetOrderedSelection();
                Clipboard.SetText(text[s..e]);
            }
            else if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
            }
            return true;
        }

        if (ctrl && key == Keys.X)
        {
            if (HasSearchSelection())
            {
                var (s, e) = GetOrderedSelection();
                Clipboard.SetText(text[s..e]);
                DeleteSearchSelection();
            }
            else if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                _searchFilter.SearchText = "";
                _searchFilter.CursorPos = 0;
                ClearSearchSelection();
            }
            ResetSearchCaptureState();
            RefreshFilteredActions();
            return true;
        }

        if (ctrl && key == Keys.V)
        {
            if (Clipboard.ContainsText())
            {
                string pasted = Clipboard.GetText().ReplaceLineEndings(" ").Trim();
                ResetSearchCaptureState();

                if (HasSearchSelection())
                    DeleteSearchSelection();

                text = _searchFilter.SearchText;
                cursor = _searchFilter.CursorPos;
                int remaining = MaxSearchLength - text.Length;
                if (remaining > 0)
                {
                    string toInsert = pasted.Length > remaining ? pasted[..remaining] : pasted;
                    _searchFilter.SearchText = string.Concat(text.AsSpan(0, cursor), toInsert, text.AsSpan(cursor));
                    _searchFilter.CursorPos = cursor + toInsert.Length;
                    RefreshFilteredActions();
                }
            }
            return true;
        }

        if (ctrl && key == Keys.A)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _searchFilter.SelectionStart = 0;
                _searchFilter.SelectionEnd = text.Length;
                _searchFilter.CursorPos = text.Length;
            }
            return true;
        }

        // Text-modifying keys â€” reset capture state once up front
        bool modifiesText = key is Keys.Back or Keys.Delete || KeyToChar(key, shift) != '\0';
        if (modifiesText)
            ResetSearchCaptureState();

        if (key == Keys.Back)
        {
            if (HasSearchSelection())
            {
                DeleteSearchSelection();
            }
            else if (cursor > 0)
            {
                int deleteFrom = ctrl ? FindWordBoundaryLeft(text, cursor) : cursor - 1;
                _searchFilter.SearchText = string.Concat(text.AsSpan(0, deleteFrom), text.AsSpan(cursor));
                _searchFilter.CursorPos = deleteFrom;
            }
            RefreshFilteredActions();
            return true;
        }

        if (key == Keys.Delete)
        {
            if (HasSearchSelection())
            {
                DeleteSearchSelection();
            }
            else if (cursor < text.Length)
            {
                int deleteTo = ctrl ? FindWordBoundaryRight(text, cursor) : cursor + 1;
                _searchFilter.SearchText = string.Concat(text.AsSpan(0, cursor), text.AsSpan(deleteTo));
            }
            RefreshFilteredActions();
            return true;
        }

        char c = KeyToChar(key, shift);
        if (c != '\0')
        {
            if (HasSearchSelection())
                DeleteSearchSelection();

            text = _searchFilter.SearchText;
            cursor = _searchFilter.CursorPos;
            if (text.Length < MaxSearchLength)
            {
                _searchFilter.SearchText = string.Concat(text.AsSpan(0, cursor), c.ToString(), text.AsSpan(cursor));
                _searchFilter.CursorPos = cursor + 1;
                RefreshFilteredActions();
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Given a click offset (relative to text start) and font size,
    /// returns the character index closest to that position.
    /// </summary>
    private static int HitTestSearchCursorPos(string text, float clickOffset, float fontSize)
    {
        if (clickOffset <= 0) return 0;
        for (int i = 1; i <= text.Length; i++)
        {
            float w = FUIRenderer.MeasureText(text[..i], fontSize);
            float prevW = i > 1 ? FUIRenderer.MeasureText(text[..(i - 1)], fontSize) : 0;
            float midpoint = (prevW + w) / 2f;
            if (clickOffset < midpoint)
                return i - 1;
        }
        return text.Length;
    }

    private static int FindWordBoundaryLeft(string text, int pos)
    {
        if (pos <= 0) return 0;
        int i = pos - 1;
        while (i > 0 && text[i] == ' ') i--;
        while (i > 0 && text[i - 1] != ' ') i--;
        return i;
    }

    private static int FindWordBoundaryRight(string text, int pos)
    {
        if (pos >= text.Length) return text.Length;
        int i = pos;
        while (i < text.Length && text[i] != ' ') i++;
        while (i < text.Length && text[i] == ' ') i++;
        return i;
    }

    private bool HandleExportFilenameBoxKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;

        if (key == Keys.Escape || key == Keys.Enter)
        {
            _scExportFilenameBoxFocused = false;
            return true;
        }

        if (key == Keys.Back)
        {
            if (_scExportFilename.Length > 0)
            {
                _scExportFilename = _scExportFilename.Substring(0, _scExportFilename.Length - 1);
            }
            return true;
        }

        if (key == Keys.Delete)
        {
            _scExportFilename = "";
            return true;
        }

        char c = KeyToFilenameChar(key, (keyData & Keys.Shift) == Keys.Shift);
        if (c != '\0' && _scExportFilename.Length < 50)
        {
            _scExportFilename += c;
            return true;
        }

        return false;
    }

    private static char KeyToFilenameChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
        {
            char c = (char)('a' + (key - Keys.A));
            return shift ? char.ToUpper(c) : c;
        }

        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return (char)('0' + (key - Keys.D0));
        }

        return key switch
        {
            Keys.OemMinus => shift ? '_' : '-',
            Keys.Oemplus => '=',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
        {
            char c = (char)('a' + (key - Keys.A));
            return shift ? char.ToUpper(c) : c;
        }

        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return (char)('0' + (key - Keys.D0));
        }

        return key switch
        {
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.Oemplus => shift ? '+' : '=',
            Keys.OemPeriod => '.',
            Keys.Oemcomma => ',',
            _ => '\0'
        };
    }

}