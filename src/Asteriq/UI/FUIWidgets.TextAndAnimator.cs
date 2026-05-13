using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

internal static partial class FUIWidgets
{
    /// <summary>Truncates text to fit within maxWidth at the given fontSize, appending "..." if needed.</summary>
    internal static string TruncateTextToWidth(string text, float maxWidth, float fontSize)
    {
        if (FUIRenderer.MeasureText(text, fontSize) <= maxWidth) return text;

        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (FUIRenderer.MeasureText(string.Concat(text.AsSpan(0, mid), "..."), fontSize) <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }
        return low > 0 ? string.Concat(text.AsSpan(0, low), "...") : "...";
    }

    /// <summary>
    /// Word-wraps text into lines that each fit within <paramref name="maxWidth"/> at the
    /// given font size. Long single words that exceed the width are emitted unbroken rather
    /// than hard-truncated — callers that need to clip can post-process with TruncateTextToWidth.
    /// </summary>
    internal static IReadOnlyList<string> WrapTextToWidth(string text, float maxWidth, float fontSize)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        var words = text.Split(' ');
        var current = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            var candidate = current + " " + word;
            if (FUIRenderer.MeasureText(candidate, fontSize) <= maxWidth)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    // ─── SC Bindings Shared Widgets ───────────────────────────────────────────


    /// <summary>
    /// Drives animated expand/collapse for a two-panel split layout.
    /// T lerps from 0 (panel A collapsed, B expanded) to 1 (A expanded, B collapsed).
    /// Call <see cref="Update"/> each tick. Use <see cref="ComputeBounds"/> to get animated bounds.
    /// </summary>
    internal struct PanelSplitAnimator
    {
        /// <summary>Current animation position: 0 = panel A collapsed, 1 = panel A expanded.</summary>
        public float T;

        /// <summary>True when panel B exists in the layout.</summary>
        public bool HasPanelB;

        /// <summary>True when panel B existed last frame (used to animate disappearance).</summary>
        public bool HadPanelB;

        private const float LerpSpeed = 0.18f;

        /// <summary>
        /// Call each tick. Returns true if the animation is still in progress (caller should MarkDirty).
        /// </summary>
        public bool Update(bool panelAExpanded, bool hasPanelB)
        {
            HasPanelB = hasPanelB;
            float target = (!hasPanelB || panelAExpanded) ? 1f : 0f;
            if (MathF.Abs(T - target) > 0.001f)
            {
                T += (target - T) * LerpSpeed;
                if (MathF.Abs(T - target) < 0.001f) T = target;
                return true;
            }
            if (!hasPanelB && T >= 0.999f)
                HadPanelB = false;
            else if (hasPanelB)
                HadPanelB = true;
            return false;
        }

        /// <summary>Whether the two-panel animated layout should be used.</summary>
        public readonly bool UseAnimatedLayout => HasPanelB || (HadPanelB && T < 0.999f);

        /// <summary>Whether panel B is animating out.</summary>
        public readonly bool IsAnimatingOut => !HasPanelB && HadPanelB && T < 0.999f;

        /// <summary>
        /// Computes the two panel bounds. Panel A height proportional to T, panel B gets the rest.
        /// Each panel's collapsed minimum height is specified separately so callers whose B contains
        /// a sub-stack of headers can reserve the full stack height (rather than a single header).
        /// </summary>
        public readonly (SKRect boundsA, SKRect boundsB) ComputeBounds(
            SKRect area, float gap, float collapsedAH, float collapsedBH)
        {
            float expandableH = area.Height - collapsedAH - collapsedBH - gap;
            float aH = collapsedAH + expandableH * T;
            var boundsA = new SKRect(area.Left, area.Top, area.Right, area.Top + aH);
            var boundsB = new SKRect(area.Left, boundsA.Bottom + gap, area.Right, area.Bottom);
            return (boundsA, boundsB);
        }
    }
}