using System.Text.Json;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.VJoy;

namespace Asteriq.Tests.Services;

public class NetworkInputServiceTests
{
    // ── 1. Packet codec round-trip ────────────────────────────────────────────

    [Fact]
    public void PacketCodec_InputRoundTrip_AxesButtonsHats()
    {
        var original = new DeviceInputState
        {
            DeviceIndex = 2,
            Axes = [-1f, 0f, 0.5f, 1f],
            Buttons = [true, false, true, false, false, true],
            Hats = [0, 180, -1],
            Timestamp = DateTime.UtcNow
        };
        byte deviceSlot = 2;

        var encoded = NetworkInputService.EncodeInputPayload(original, deviceSlot);
        var (decodedSlot, decoded) = NetworkInputService.DecodeInputPayload(encoded);

        Assert.Equal(deviceSlot, decodedSlot);
        Assert.Equal(original.Axes.Length, decoded.Axes.Length);
        for (int i = 0; i < original.Axes.Length; i++)
            Assert.Equal(original.Axes[i], decoded.Axes[i], precision: 5);

        Assert.Equal(original.Buttons.Length, decoded.Buttons.Length);
        for (int i = 0; i < original.Buttons.Length; i++)
            Assert.Equal(original.Buttons[i], decoded.Buttons[i]);

        Assert.Equal(original.Hats.Length, decoded.Hats.Length);
        for (int i = 0; i < original.Hats.Length; i++)
            Assert.Equal(original.Hats[i], decoded.Hats[i]);
    }

    [Fact]
    public void PacketCodec_EmptyArrays_DoesNotThrow()
    {
        var state = new DeviceInputState
        {
            DeviceIndex = 0,
            Axes = [],
            Buttons = [],
            Hats = [],
            Timestamp = DateTime.UtcNow
        };

        var encoded = NetworkInputService.EncodeInputPayload(state, 0);
        var (slot, decoded) = NetworkInputService.DecodeInputPayload(encoded);

        Assert.Equal(0, slot);
        Assert.Empty(decoded.Axes);
        Assert.Empty(decoded.Buttons);
        Assert.Empty(decoded.Hats);
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
}
