using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class HidDeviceServiceTests
{
    #region HidDeviceInfo Tests

    [Fact]
    public void HidDeviceInfo_DefaultValues_AreCorrect()
    {
        var info = new HidDeviceService.HidDeviceInfo();

        Assert.Equal(string.Empty, info.DevicePath);
        Assert.Equal(string.Empty, info.ProductName);
        Assert.Equal(0, info.VendorId);
        Assert.Equal(0, info.ProductId);
        Assert.Equal(string.Empty, info.SerialNumber);
        Assert.NotNull(info.Axes);
        Assert.Empty(info.Axes);
        Assert.Equal(0, info.ButtonCount);
        Assert.Equal(0, info.HatCount);
    }

    [Fact]
    public void HidDeviceInfo_WithValues_StoresCorrectly()
    {
        var info = new HidDeviceService.HidDeviceInfo
        {
            DevicePath = @"\\?\HID#VID_3344&PID_0194#1234",
            ProductName = "VPC Stick MT-50CM2",
            VendorId = 0x3344,
            ProductId = 0x0194,
            SerialNumber = "ABC123",
            Axes = new List<AxisInfo>
            {
                new AxisInfo { Index = 0, Type = AxisType.X },
                new AxisInfo { Index = 1, Type = AxisType.Y }
            },
            ButtonCount = 32,
            HatCount = 1
        };

        Assert.Equal(@"\\?\HID#VID_3344&PID_0194#1234", info.DevicePath);
        Assert.Equal("VPC Stick MT-50CM2", info.ProductName);
        Assert.Equal(0x3344, info.VendorId);
        Assert.Equal(0x0194, info.ProductId);
        Assert.Equal("ABC123", info.SerialNumber);
        Assert.Equal(2, info.Axes.Count);
        Assert.Equal(AxisType.X, info.Axes[0].Type);
        Assert.Equal(AxisType.Y, info.Axes[1].Type);
        Assert.Equal(32, info.ButtonCount);
        Assert.Equal(1, info.HatCount);
    }

    #endregion

    #region PhysicalDeviceInfo HidDevicePath Tests

    [Fact]
    public void PhysicalDeviceInfo_HidDevicePath_DefaultsToEmpty()
    {
        var info = new PhysicalDeviceInfo();

        Assert.Equal(string.Empty, info.HidDevicePath);
    }

    [Fact]
    public void PhysicalDeviceInfo_HidDevicePath_CanBeSet()
    {
        var info = new PhysicalDeviceInfo();
        info.HidDevicePath = @"\\?\HID#VID_3344&PID_0194#unique123";

        Assert.Equal(@"\\?\HID#VID_3344&PID_0194#unique123", info.HidDevicePath);
    }

    [Fact]
    public void PhysicalDeviceInfo_TwoIdenticalDevices_HaveDifferentPaths()
    {
        // Simulates two Alpha Primes - same name but different HID paths
        var device1 = new PhysicalDeviceInfo
        {
            Name = "VPC Stick MT-50CM2",
            HidDevicePath = @"\\?\HID#VID_3344&PID_0194#instance1"
        };

        var device2 = new PhysicalDeviceInfo
        {
            Name = "VPC Stick MT-50CM2",
            HidDevicePath = @"\\?\HID#VID_3344&PID_0194#instance2"
        };

        // Same name (product type)
        Assert.Equal(device1.Name, device2.Name);

        // Different paths (unique per physical device)
        Assert.NotEqual(device1.HidDevicePath, device2.HidDevicePath);
    }

    #endregion

    #region HidDeviceService Integration Tests

    [Fact]
    public void HidDeviceService_CanBeInstantiated()
    {
        // This test verifies the service can be created without throwing
        var service = new HidDeviceService();
        Assert.NotNull(service);
    }

    [Fact]
    public void HidDeviceService_EnumerateDevices_ReturnsListNotNull()
    {
        var service = new HidDeviceService();

        var devices = service.EnumerateDevices();

        Assert.NotNull(devices);
        // We can't assert on count since it depends on hardware
    }

    [Fact]
    public void HidDeviceService_FindMatchingDevice_ReturnsNullForNonexistentDevice()
    {
        var service = new HidDeviceService();

        var result = service.FindMatchingDevice("NonExistent Device XYZ123");

        Assert.Null(result);
    }

    [Fact]
    public void HidDeviceService_FindMatchingDevice_RespectsExcludePaths()
    {
        var service = new HidDeviceService();
        var devices = service.EnumerateDevices();

        if (devices.Count == 0)
        {
            // Skip test if no devices available
            return;
        }

        var firstDevice = devices[0];
        var excludePaths = new HashSet<string> { firstDevice.DevicePath };

        // Try to find matching device with same name but excluding the path
        var result = service.FindMatchingDevice(firstDevice.ProductName, excludePaths);

        // If there's only one device with this name, result should be null
        // If there are multiple, result should have a different path
        if (result != null)
        {
            Assert.NotEqual(firstDevice.DevicePath, result.DevicePath);
        }
    }

    #endregion

    #region Device Matching Logic Tests

    [Fact]
    public void DeviceMatching_WithMultipleIdenticalDevices_EachGetsUniqueMatch()
    {
        // Simulate the matching logic used in InputService
        var hidDevices = new List<HidDeviceService.HidDeviceInfo>
        {
            new HidDeviceService.HidDeviceInfo
            {
                DevicePath = @"\\?\HID#path1",
                ProductName = "VPC Stick MT-50CM2",
                Axes = new List<AxisInfo>
                {
                    new AxisInfo { Index = 0, Type = AxisType.X },
                    new AxisInfo { Index = 1, Type = AxisType.Y },
                    new AxisInfo { Index = 5, Type = AxisType.Slider }
                }
            },
            new HidDeviceService.HidDeviceInfo
            {
                DevicePath = @"\\?\HID#path2",
                ProductName = "VPC Stick MT-50CM2",
                Axes = new List<AxisInfo>
                {
                    new AxisInfo { Index = 0, Type = AxisType.X },
                    new AxisInfo { Index = 1, Type = AxisType.Y },
                    new AxisInfo { Index = 5, Type = AxisType.Slider }
                }
            }
        };

        var matchedPaths = new HashSet<string>();
        var sdlDeviceNames = new[] { "VPC Stick MT-50CM2", "VPC Stick MT-50CM2" };
        var matchedDevices = new List<HidDeviceService.HidDeviceInfo>();

        // Simulate InputService matching logic
        foreach (var sdlName in sdlDeviceNames)
        {
            var match = hidDevices.FirstOrDefault(d =>
                !matchedPaths.Contains(d.DevicePath) &&
                d.ProductName.Equals(sdlName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                matchedPaths.Add(match.DevicePath);
                matchedDevices.Add(match);
            }
        }

        // Both SDL devices should get matched to different HID devices
        Assert.Equal(2, matchedDevices.Count);
        Assert.NotEqual(matchedDevices[0].DevicePath, matchedDevices[1].DevicePath);
    }

    [Fact]
    public void DeviceMatching_FirstDeviceGetsFirstMatch_SecondGetsSecond()
    {
        var hidDevices = new List<HidDeviceService.HidDeviceInfo>
        {
            new HidDeviceService.HidDeviceInfo { DevicePath = "path_A", ProductName = "Joystick" },
            new HidDeviceService.HidDeviceInfo { DevicePath = "path_B", ProductName = "Joystick" }
        };

        var matchedPaths = new HashSet<string>();

        // First match
        var match1 = hidDevices.FirstOrDefault(d =>
            !matchedPaths.Contains(d.DevicePath) &&
            d.ProductName == "Joystick");
        matchedPaths.Add(match1!.DevicePath);

        // Second match
        var match2 = hidDevices.FirstOrDefault(d =>
            !matchedPaths.Contains(d.DevicePath) &&
            d.ProductName == "Joystick");
        matchedPaths.Add(match2!.DevicePath);

        Assert.Equal("path_A", match1.DevicePath);
        Assert.Equal("path_B", match2.DevicePath);
    }

    #endregion
}
