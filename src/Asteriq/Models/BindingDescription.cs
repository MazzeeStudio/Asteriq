namespace Asteriq.Models;

/// <summary>
/// Asteriq's authored long-form description for an SC action. Sourced from
/// <c>Resources/BindingDescriptions/{locale}.json</c>. Distinct from CIG's short
/// <c>ui_&lt;action&gt;_desc</c> tagline (loaded by <see cref="Services.SCLocalisationService"/>):
/// CIG provides ~14 short labels, this layer provides longer narrative + use cases for any
/// action we choose to document.
/// </summary>
public sealed record BindingDescription
{
    /// <summary>Longer narrative description shown in the Binding Definition panel.</summary>
    public required string Description { get; init; }

    /// <summary>Optional bullet list of practical scenarios; rendered below the description.</summary>
    public IReadOnlyList<string> UseCases { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Provenance tag — <c>"cig"</c>, <c>"asteriq"</c>, or <c>"community"</c>. Drives any future
    /// "official vs community-contributed" UI differentiation.
    /// </summary>
    public string Source { get; init; } = "asteriq";

    /// <summary>Last contributor's GitHub handle for crowd-sourced edits; null when authored in-tree.</summary>
    public string? LastEditedBy { get; init; }
}
