namespace Asteriq.Models;

/// <summary>
/// Summary information about a profile (for listing)
/// </summary>
public class ProfileInfo
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int DeviceAssignmentCount { get; init; }
    public int AxisMappingCount { get; init; }
    public int ButtonMappingCount { get; init; }
    public int HatMappingCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public string FilePath { get; init; } = "";

    public int TotalMappings => AxisMappingCount + ButtonMappingCount + HatMappingCount;
}
