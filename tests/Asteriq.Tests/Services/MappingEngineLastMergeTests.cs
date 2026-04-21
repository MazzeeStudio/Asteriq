using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Asteriq.VJoy;
using Moq;

namespace Asteriq.Tests.Services;

/// <summary>
/// Integration tests for the LastSnap / LastTakeover axis merge modes.
/// Drives the real MappingEngine through ProcessInput against a mocked vJoy,
/// captures the most recent axis output, and asserts ownership behaviour.
/// </summary>
public class MappingEngineLastMergeTests
{
    private static readonly Guid DeviceA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DeviceB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class Harness
    {
        public required MappingEngine Engine { get; init; }
        public required Mock<IVJoyService> VJoy { get; init; }
        public float LastOutput { get; set; }

        public void Send(Guid device, float value)
        {
            Engine.ProcessInput(new DeviceInputState
            {
                DeviceName = device.ToString(),
                InstanceGuid = device,
                Axes = new[] { value },
                Buttons = Array.Empty<bool>(),
                Hats = Array.Empty<int>()
            });
        }

        // Seeds both devices with the given values and drains initialization ticks
        // so the next Send call reflects real movement, not first-tick setup.
        public void SeedBoth(float valueA, float valueB)
        {
            Send(DeviceA, valueA);
            Send(DeviceB, valueB);
            // One more pass so PreviousValues is fully populated from the two-input world
            Send(DeviceA, valueA);
            Send(DeviceB, valueB);
        }
    }

    private static Harness BuildHarness(MergeOperation mergeOp)
    {
        var vjoy = new Mock<IVJoyService>();
        vjoy.Setup(v => v.AcquireDevice(It.IsAny<uint>())).Returns(true);

        var engine = new MappingEngine(vjoy.Object);
        var harness = new Harness { Engine = engine, VJoy = vjoy };
        vjoy.Setup(v => v.SetAxis(It.IsAny<uint>(), It.IsAny<HID_USAGES>(), It.IsAny<float>()))
            .Callback<uint, HID_USAGES, float>((_, _, value) => harness.LastOutput = value);

        var profile = new MappingProfile
        {
            AxisMappings = new List<AxisMapping>
            {
                new()
                {
                    Name = "Merged Axis",
                    MergeOp = mergeOp,
                    Inputs = new List<InputSource>
                    {
                        new() { DeviceId = DeviceA.ToString(), DeviceName = "A", Type = InputType.Axis, Index = 0 },
                        new() { DeviceId = DeviceB.ToString(), DeviceName = "B", Type = InputType.Axis, Index = 0 }
                    },
                    Output = new OutputTarget { Type = OutputType.VJoyAxis, VJoyDevice = 1, Index = 0 }
                }
            }
        };

        engine.LoadProfile(profile);
        engine.Start();
        return harness;
    }

    [Fact]
    public void LastSnap_InitialOwnership_IsFirstInput()
    {
        var h = BuildHarness(MergeOperation.LastSnap);

        h.SeedBoth(valueA: 0.3f, valueB: 0.8f);

        Assert.Equal(0.3f, h.LastOutput, 3);
    }

    [Fact]
    public void LastSnap_NonOwnerMoves_OutputSnapsImmediately()
    {
        var h = BuildHarness(MergeOperation.LastSnap);
        h.SeedBoth(valueA: 0.3f, valueB: 0.8f);

        h.Send(DeviceB, 0.7f);

        Assert.Equal(0.7f, h.LastOutput, 3);
    }

    [Fact]
    public void LastSnap_MovementBelowDeadband_DoesNotTransferOwnership()
    {
        var h = BuildHarness(MergeOperation.LastSnap);
        h.SeedBoth(valueA: 0.3f, valueB: 0.8f);

        // Below the 0.005 deadband
        h.Send(DeviceB, 0.802f);

        Assert.Equal(0.3f, h.LastOutput, 3);
    }

    [Fact]
    public void LastTakeover_NonOwnerMovesButDoesNotCross_OwnershipUnchanged()
    {
        var h = BuildHarness(MergeOperation.LastTakeover);
        h.SeedBoth(valueA: 0.3f, valueB: 0.8f);

        // B drops from 0.8 → 0.5 but stays above owner's 0.3 → no crossing, no transfer
        h.Send(DeviceB, 0.5f);

        Assert.Equal(0.3f, h.LastOutput, 3);
    }

    [Fact]
    public void LastTakeover_NonOwnerCrossesOutput_TransfersOwnership()
    {
        var h = BuildHarness(MergeOperation.LastTakeover);
        h.SeedBoth(valueA: 0.3f, valueB: 0.8f);

        // B moves past owner's 0.3 → crossing detected, ownership transfers, output follows B
        h.Send(DeviceB, 0.2f);

        Assert.Equal(0.2f, h.LastOutput, 3);
    }

    [Fact]
    public void LastTakeover_AfterTransfer_NewOwnerDrivesOutput()
    {
        var h = BuildHarness(MergeOperation.LastTakeover);
        h.SeedBoth(valueA: 0.3f, valueB: 0.8f);
        h.Send(DeviceB, 0.2f); // transfer happens here

        // B now owns; moving B updates output
        h.Send(DeviceB, -0.4f);

        Assert.Equal(-0.4f, h.LastOutput, 3);
    }

    [Fact]
    public void LastTakeover_FormerOwnerMovesWithoutCrossing_DoesNotReclaim()
    {
        var h = BuildHarness(MergeOperation.LastTakeover);
        h.SeedBoth(valueA: 0.3f, valueB: 0.8f);
        h.Send(DeviceB, 0.2f); // B takes over, output = 0.2
        h.Send(DeviceB, -0.5f); // B drives to -0.5

        // A sits idle at 0.3 (above current output -0.5). Now A wiggles to 0.4 — it
        // hasn't crossed -0.5, so it should not reclaim ownership.
        h.Send(DeviceA, 0.4f);

        Assert.Equal(-0.5f, h.LastOutput, 3);
    }

    [Fact]
    public void LastTakeover_InputEqualToOwnershipValue_CountsAsCrossing()
    {
        var h = BuildHarness(MergeOperation.LastTakeover);
        h.SeedBoth(valueA: 0.3f, valueB: 0.8f);

        // B moves exactly to owner's value — touch counts as crossing
        h.Send(DeviceB, 0.3f);

        Assert.Equal(0.3f, h.LastOutput, 3);

        // B now owns; moving B updates the output
        h.Send(DeviceB, 0.1f);
        Assert.Equal(0.1f, h.LastOutput, 3);
    }

    [Fact]
    public void LastSnap_RapidAlternation_FollowsMostRecentMove()
    {
        var h = BuildHarness(MergeOperation.LastSnap);
        h.SeedBoth(valueA: 0.0f, valueB: 0.0f);

        h.Send(DeviceA, 0.4f);
        Assert.Equal(0.4f, h.LastOutput, 3);

        h.Send(DeviceB, -0.2f);
        Assert.Equal(-0.2f, h.LastOutput, 3);

        h.Send(DeviceA, 0.6f);
        Assert.Equal(0.6f, h.LastOutput, 3);
    }
}
