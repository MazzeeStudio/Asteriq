using System.Text.Json;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.VJoy;

namespace Asteriq.Tests.Services;

public class NetworkInputServiceTests
{
    // ── 1. VJoy snapshot codec round-trip ─────────────────────────────────────

    [Fact]
    public void PacketCodec_VJoySnapshotRoundTrip_AxesButtonsHats()
    {
        var original = new VJoyOutputSnapshot
        {
            DeviceId    = 1,
            Axes        = [-1f, 0f, 0.5f, 1f, 0f, 0f, 0f, 0f],
            Buttons     = new bool[128],
            Hats        = [0, 18000, -1, 0],
            AxisCount   = 4,
            ButtonCount = 6,
            HatCount    = 3
        };
        original.Buttons[0] = true;
        original.Buttons[2] = true;
        original.Buttons[5] = true;

        var encoded = NetworkInputService.EncodeVJoyPayload(original);
        var decoded = NetworkInputService.DecodeVJoyPayload(encoded);

        Assert.Equal(original.DeviceId,    decoded.DeviceId);
        Assert.Equal(original.AxisCount,   decoded.AxisCount);
        Assert.Equal(original.ButtonCount, decoded.ButtonCount);
        Assert.Equal(original.HatCount,    decoded.HatCount);

        for (int i = 0; i < original.AxisCount; i++)
            Assert.Equal(original.Axes[i], decoded.Axes[i], precision: 5);

        for (int i = 0; i < original.ButtonCount; i++)
            Assert.Equal(original.Buttons[i], decoded.Buttons[i]);

        for (int i = 0; i < original.HatCount; i++)
            Assert.Equal(original.Hats[i], decoded.Hats[i]);
    }

    [Fact]
    public void PacketCodec_EmptySnapshot_DoesNotThrow()
    {
        var snapshot = new VJoyOutputSnapshot
        {
            DeviceId    = 2,
            AxisCount   = 0,
            ButtonCount = 0,
            HatCount    = 0
        };

        var encoded = NetworkInputService.EncodeVJoyPayload(snapshot);
        var decoded = NetworkInputService.DecodeVJoyPayload(encoded);

        Assert.Equal(2u, decoded.DeviceId);
        Assert.Equal(0,  decoded.AxisCount);
        Assert.Equal(0,  decoded.ButtonCount);
        Assert.Equal(0,  decoded.HatCount);
    }

    // ── 2. MappingProfile serialization ──────────────────────────────────────

    [Fact]
    public void MappingProfile_NetworkSwitchButton_SerializesAndDeserializes()
    {
        var profile = new MappingProfile
        {
            Name = "Test",
            NetworkSwitchButton = new NetworkSwitchConfig
            {
                DeviceIndex = 1,
                ButtonIndex = 11,
                DisplayName = "VPC WarBRD Base Button 12",
                DeviceId = "3344:0194"
            }
        };

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(profile, options);
        var restored = JsonSerializer.Deserialize<MappingProfile>(json, options);

        Assert.NotNull(restored?.NetworkSwitchButton);
        Assert.Equal(1, restored.NetworkSwitchButton.DeviceIndex);
        Assert.Equal(11, restored.NetworkSwitchButton.ButtonIndex);
        Assert.Equal("VPC WarBRD Base Button 12", restored.NetworkSwitchButton.DisplayName);
        Assert.Equal("3344:0194", restored.NetworkSwitchButton.DeviceId);
    }

    [Fact]
    public void MappingProfile_LegacyJson_DeserializesWithNullSwitchButton()
    {
        // JSON written before NetworkSwitchButton was added
        var legacyJson = """{"id":"00000000-0000-0000-0000-000000000000","name":"Legacy","description":""}""";

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var profile = JsonSerializer.Deserialize<MappingProfile>(legacyJson, options);

        Assert.NotNull(profile);
        Assert.Null(profile.NetworkSwitchButton);
    }

    // ── 3. VJoyAxisHelper ────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, HID_USAGES.X)]
    [InlineData(1, HID_USAGES.Y)]
    [InlineData(2, HID_USAGES.Z)]
    [InlineData(3, HID_USAGES.RX)]
    [InlineData(4, HID_USAGES.RY)]
    [InlineData(5, HID_USAGES.RZ)]
    [InlineData(6, HID_USAGES.SL0)]
    [InlineData(7, HID_USAGES.SL1)]
    public void VJoyAxisHelper_IndexToHidUsage_CorrectForAllEightAxes(int index, HID_USAGES expected)
    {
        var result = VJoyAxisHelper.IndexToHidUsage(index);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(HID_USAGES.X,   0)]
    [InlineData(HID_USAGES.Y,   1)]
    [InlineData(HID_USAGES.Z,   2)]
    [InlineData(HID_USAGES.RX,  3)]
    [InlineData(HID_USAGES.RY,  4)]
    [InlineData(HID_USAGES.RZ,  5)]
    [InlineData(HID_USAGES.SL0, 6)]
    [InlineData(HID_USAGES.SL1, 7)]
    public void VJoyAxisHelper_HidUsageToIndex_IsRoundTripOfIndexToHidUsage(HID_USAGES usage, int expectedIndex)
    {
        var result = VJoyAxisHelper.HidUsageToIndex(usage);
        Assert.Equal(expectedIndex, result);
    }
}
