using Asteriq.Models;

namespace Asteriq.Tests.Models;

public class MappingProfileTests
{
    [Fact]
    public void GetVJoyDeviceForPhysical_MatchesByGuid()
    {
        var profile = new MappingProfile();
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Name = "VPC Stick",
                Guid = "abc123-def456",
                VidPid = "3344:0194"
            },
            VJoyDevice = 1
        });

        var result = profile.GetVJoyDeviceForPhysical("abc123-def456");

        Assert.Equal(1u, result);
    }

    [Fact]
    public void GetVJoyDeviceForPhysical_CaseInsensitiveGuid()
    {
        var profile = new MappingProfile();
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Guid = "ABC123-DEF456"
            },
            VJoyDevice = 2
        });

        var result = profile.GetVJoyDeviceForPhysical("abc123-def456");

        Assert.Equal(2u, result);
    }

    [Fact]
    public void GetVJoyDeviceForPhysical_FallbackToVidPid()
    {
        var profile = new MappingProfile();
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Guid = "different-guid",
                VidPid = "3344:0194"
            },
            VJoyDevice = 3
        });

        var result = profile.GetVJoyDeviceForPhysical("unknown-guid", "3344:0194");

        Assert.Equal(3u, result);
    }

    [Fact]
    public void GetVJoyDeviceForPhysical_CaseInsensitiveVidPid()
    {
        var profile = new MappingProfile();
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Guid = "",
                VidPid = "ABCD:1234"
            },
            VJoyDevice = 4
        });

        var result = profile.GetVJoyDeviceForPhysical("unknown", "abcd:1234");

        Assert.Equal(4u, result);
    }

    [Fact]
    public void GetVJoyDeviceForPhysical_PrefersGuidOverVidPid()
    {
        var profile = new MappingProfile();
        // First device: matches GUID
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Guid = "target-guid",
                VidPid = "0000:0000"
            },
            VJoyDevice = 1
        });
        // Second device: matches VID:PID
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Guid = "other-guid",
                VidPid = "3344:0194"
            },
            VJoyDevice = 2
        });

        // Should find GUID match first
        var result = profile.GetVJoyDeviceForPhysical("target-guid", "3344:0194");

        Assert.Equal(1u, result);
    }

    [Fact]
    public void GetVJoyDeviceForPhysical_NoMatch_ReturnsNull()
    {
        var profile = new MappingProfile();
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Guid = "some-guid",
                VidPid = "1111:2222"
            },
            VJoyDevice = 1
        });

        var result = profile.GetVJoyDeviceForPhysical("unknown-guid", "unknown:vidpid");

        Assert.Null(result);
    }

    [Fact]
    public void GetVJoyDeviceForPhysical_EmptyAssignments_ReturnsNull()
    {
        var profile = new MappingProfile();

        var result = profile.GetVJoyDeviceForPhysical("any-guid");

        Assert.Null(result);
    }

    [Fact]
    public void GetVJoyDeviceForPhysical_SkipsEmptyGuid()
    {
        var profile = new MappingProfile();
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Guid = "",
                VidPid = "3344:0194"
            },
            VJoyDevice = 1
        });

        // Should not match empty GUID, only VID:PID
        var result = profile.GetVJoyDeviceForPhysical("");

        Assert.Null(result);
    }

    [Fact]
    public void GetAssignmentForVJoy_ReturnsMatchingAssignment()
    {
        var profile = new MappingProfile();
        var assignment = new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Name = "Test Device",
                Guid = "test-guid"
            },
            VJoyDevice = 2,
            DeviceMapOverride = "custom"
        };
        profile.DeviceAssignments.Add(assignment);

        var result = profile.GetAssignmentForVJoy(2);

        Assert.NotNull(result);
        Assert.Equal("Test Device", result.PhysicalDevice.Name);
        Assert.Equal("custom", result.DeviceMapOverride);
    }

    [Fact]
    public void GetAssignmentForVJoy_NoMatch_ReturnsNull()
    {
        var profile = new MappingProfile();
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef { Name = "Device" },
            VJoyDevice = 1
        });

        var result = profile.GetAssignmentForVJoy(5);

        Assert.Null(result);
    }

    [Fact]
    public void GetAssignmentForVJoy_EmptyAssignments_ReturnsNull()
    {
        var profile = new MappingProfile();

        var result = profile.GetAssignmentForVJoy(1);

        Assert.Null(result);
    }

    [Fact]
    public void GetAssignmentForVJoy_MultipleDevices_ReturnsCorrect()
    {
        var profile = new MappingProfile();
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef { Name = "Left Stick" },
            VJoyDevice = 1
        });
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef { Name = "Right Stick" },
            VJoyDevice = 2
        });
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef { Name = "Throttle" },
            VJoyDevice = 3
        });

        var result = profile.GetAssignmentForVJoy(2);

        Assert.NotNull(result);
        Assert.Equal("Right Stick", result.PhysicalDevice.Name);
    }
}
