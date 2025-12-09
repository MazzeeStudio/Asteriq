using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class InputDetectionServiceTests
{
    #region DetectedInput Tests

    [Fact]
    public void DetectedInput_ToString_FormatsButtonCorrectly()
    {
        var input = new DetectedInput
        {
            DeviceGuid = Guid.NewGuid(),
            DeviceName = "VPC Stick WarBRD",
            Type = InputType.Button,
            Index = 5,
            Value = 1f
        };

        var result = input.ToString();

        Assert.Equal("VPC Stick WarBRD - Button 6", result); // 1-indexed for display
    }

    [Fact]
    public void DetectedInput_ToString_FormatsAxisCorrectly()
    {
        var input = new DetectedInput
        {
            DeviceGuid = Guid.NewGuid(),
            DeviceName = "VPC Throttle",
            Type = InputType.Axis,
            Index = 2,
            Value = 0.75f
        };

        var result = input.ToString();

        Assert.Equal("VPC Throttle - Axis 2", result);
    }

    [Fact]
    public void DetectedInput_ToString_FormatsHatCorrectly()
    {
        var input = new DetectedInput
        {
            DeviceGuid = Guid.NewGuid(),
            DeviceName = "Generic Joystick",
            Type = InputType.Hat,
            Index = 0,
            Value = 0.25f
        };

        var result = input.ToString();

        Assert.Equal("Generic Joystick - Hat 1", result); // 1-indexed for display
    }

    [Fact]
    public void DetectedInput_ToInputSource_ConvertsCorrectly()
    {
        var guid = Guid.NewGuid();
        var input = new DetectedInput
        {
            DeviceGuid = guid,
            DeviceName = "Test Device",
            Type = InputType.Button,
            Index = 3,
            Value = 1f
        };

        var source = input.ToInputSource();

        Assert.Equal(guid.ToString(), source.DeviceId);
        Assert.Equal("Test Device", source.DeviceName);
        Assert.Equal(InputType.Button, source.Type);
        Assert.Equal(3, source.Index);
    }

    [Fact]
    public void DetectedInput_ToInputSource_PreservesAxisInfo()
    {
        var guid = Guid.NewGuid();
        var input = new DetectedInput
        {
            DeviceGuid = guid,
            DeviceName = "Throttle",
            Type = InputType.Axis,
            Index = 0,
            Value = -0.5f
        };

        var source = input.ToInputSource();

        Assert.Equal(InputType.Axis, source.Type);
        Assert.Equal(0, source.Index);
    }

    #endregion

    #region InputDetectionFilter Tests

    [Fact]
    public void InputDetectionFilter_All_IncludesAllTypes()
    {
        var filter = InputDetectionFilter.All;

        Assert.True(filter.HasFlag(InputDetectionFilter.Buttons));
        Assert.True(filter.HasFlag(InputDetectionFilter.Axes));
        Assert.True(filter.HasFlag(InputDetectionFilter.Hats));
    }

    [Fact]
    public void InputDetectionFilter_None_IncludesNothing()
    {
        var filter = InputDetectionFilter.None;

        Assert.False(filter.HasFlag(InputDetectionFilter.Buttons));
        Assert.False(filter.HasFlag(InputDetectionFilter.Axes));
        Assert.False(filter.HasFlag(InputDetectionFilter.Hats));
    }

    [Fact]
    public void InputDetectionFilter_CanCombine()
    {
        var filter = InputDetectionFilter.Buttons | InputDetectionFilter.Axes;

        Assert.True(filter.HasFlag(InputDetectionFilter.Buttons));
        Assert.True(filter.HasFlag(InputDetectionFilter.Axes));
        Assert.False(filter.HasFlag(InputDetectionFilter.Hats));
    }

    [Fact]
    public void InputDetectionFilter_ButtonsOnly()
    {
        var filter = InputDetectionFilter.Buttons;

        Assert.True(filter.HasFlag(InputDetectionFilter.Buttons));
        Assert.False(filter.HasFlag(InputDetectionFilter.Axes));
        Assert.False(filter.HasFlag(InputDetectionFilter.Hats));
    }

    #endregion

    #region Service State Tests

    [Fact]
    public void Service_IsNotWaiting_Initially()
    {
        using var inputService = new InputService();
        using var service = new InputDetectionService(inputService);

        Assert.False(service.IsWaiting);
    }

    [Fact]
    public void Service_Dispose_DoesNotThrow()
    {
        using var inputService = new InputService();
        var service = new InputDetectionService(inputService);

        var exception = Record.Exception(() => service.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Service_Cancel_DoesNotThrow_WhenNotWaiting()
    {
        using var inputService = new InputService();
        using var service = new InputDetectionService(inputService);

        var exception = Record.Exception(() => service.Cancel());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Service_WaitForInput_ReturnsNull_OnTimeout()
    {
        using var inputService = new InputService();
        using var service = new InputDetectionService(inputService);

        var result = await service.WaitForInputAsync(timeoutMs: 100);

        Assert.Null(result);
    }

    [Fact]
    public async Task Service_WaitForInput_SetsIsWaiting()
    {
        using var inputService = new InputService();
        using var service = new InputDetectionService(inputService);
        bool wasWaiting = false;

        var waitTask = Task.Run(async () =>
        {
            var task = service.WaitForInputAsync(timeoutMs: 200);
            await Task.Delay(50); // Give it time to start
            wasWaiting = service.IsWaiting;
            return await task;
        });

        await waitTask;
        Assert.True(wasWaiting);
    }

    [Fact]
    public async Task Service_WaitForInput_ThrowsIfAlreadyWaiting()
    {
        using var inputService = new InputService();
        using var service = new InputDetectionService(inputService);

        var firstWait = service.WaitForInputAsync(timeoutMs: 500);
        await Task.Delay(50); // Let it start

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.WaitForInputAsync());

        service.Cancel();
        await firstWait;
    }

    [Fact]
    public async Task Service_Cancel_StopsWaiting()
    {
        using var inputService = new InputService();
        using var service = new InputDetectionService(inputService);

        var waitTask = service.WaitForInputAsync(timeoutMs: 10000);
        await Task.Delay(50);

        service.Cancel();
        var result = await waitTask;

        Assert.Null(result);
        Assert.False(service.IsWaiting);
    }

    #endregion
}
