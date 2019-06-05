#if UNITY_EDITOR || UNITY_SWITCH || UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_WSA
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Switch;
using UnityEngine.InputSystem.HID;
using UnityEngine.InputSystem.Switch.LowLevel;
using UnityEngine.InputSystem.Processors;

internal class SwitchTests : InputTestFixture
{
#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_WSA
    [Test]
    [Category("Devices")]
    public void Devices_SupportsHIDNpad()
    {
        var hidDescriptor = new HID.HIDDeviceDescriptor
        {
            vendorId = 0x57e,
            productId = 0x2009,
        };

        var device = InputSystem.AddDevice(
            new InputDeviceDescription
            {
                interfaceName = HID.kHIDInterface,
                capabilities = hidDescriptor.ToJson()
            });

        Assert.That(device, Is.TypeOf<NPadHID>());
        var controller = (NPadHID)device;

        InputSystem.QueueStateEvent(controller,
            new NPadInputStateHID
            {
                leftStickX = 0x1000,
                leftStickY = 0x1000,
                rightStickX = 0x7fff,
                rightStickY = 0xefff,
            });
        InputSystem.Update();

        var leftStickDeadzone = controller.leftStick.TryGetProcessor<StickDeadzoneProcessor>();
        var rightStickDeadzone = controller.rightStick.TryGetProcessor<StickDeadzoneProcessor>();

        Assert.That(Vector2.Distance(controller.leftStick.ReadValue(), leftStickDeadzone.Process(new Vector2(-1.0f, 1.0f))), Is.LessThan(0.0001f));
        Assert.That(Vector2.Distance(controller.rightStick.ReadValue(), rightStickDeadzone.Process(new Vector2(0.0f, -1.0f))), Is.LessThan(0.0001f));

        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.A), controller.buttonEast);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.B), controller.buttonSouth);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.X), controller.buttonNorth);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.Y), controller.buttonWest);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.StickL), controller.leftStickButton);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.StickR), controller.rightStickButton);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.L), controller.leftShoulder);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.R), controller.rightShoulder);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.ZL), controller.leftTrigger);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.ZR), controller.rightTrigger);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.Plus), controller.startButton);
        AssertButtonPress(controller, new NPadInputStateHID().WithButton(NPadInputStateHID.Button.Minus), controller.selectButton);
    }

#endif

#if UNITY_EDITOR || UNITY_SWITCH
    [Test]
    [Category("Devices")]
    public void Devices_SupportsSwitchNpad()
    {
        var device = InputSystem.AddDevice(
            new InputDeviceDescription
            {
                interfaceName = "Switch",
                manufacturer = "Nintendo",
                product = "Wireless Controller",
            });

        Assert.That(device, Is.TypeOf<NPadSwitch>());
        var controller = (NPadSwitch)device;

        InputSystem.QueueStateEvent(controller,
            new NPadInputStateSwitch
            {
                leftStick = new Vector2(0.123f, 0.456f),
                rightStick = new Vector2(0.789f, 0.987f),
                acceleration = new Vector3(0.987f, 0.654f, 0.321f),
                attitude = new Quaternion(0.111f, 0.222f, 0.333f, 0.444f),
                angularVelocity = new Vector3(0.444f, 0.555f, 0.666f),
            });
        InputSystem.Update();

        var leftStickDeadzone = controller.leftStick.TryGetProcessor<StickDeadzoneProcessor>();
        var rightStickDeadzone = controller.leftStick.TryGetProcessor<StickDeadzoneProcessor>();

        Assert.That(controller.leftStick.ReadValue(), Is.EqualTo(leftStickDeadzone.Process(new Vector2(0.123f, 0.456f))));
        Assert.That(controller.rightStick.ReadValue(), Is.EqualTo(rightStickDeadzone.Process(new Vector2(0.789f, 0.987f))));

        Assert.That(controller.acceleration.x.ReadValue(), Is.EqualTo(0.987).Within(0.00001));
        Assert.That(controller.acceleration.y.ReadValue(), Is.EqualTo(0.654).Within(0.00001));
        Assert.That(controller.acceleration.z.ReadValue(), Is.EqualTo(0.321).Within(0.00001));

        Quaternion attitude = controller.attitude.ReadValue();

        Assert.That(attitude.x, Is.EqualTo(0.111).Within(0.00001));
        Assert.That(attitude.y, Is.EqualTo(0.222).Within(0.00001));
        Assert.That(attitude.z, Is.EqualTo(0.333).Within(0.00001));
        Assert.That(attitude.w, Is.EqualTo(0.444).Within(0.00001));

        Assert.That(controller.angularVelocity.x.ReadValue(), Is.EqualTo(0.444).Within(0.00001));
        Assert.That(controller.angularVelocity.y.ReadValue(), Is.EqualTo(0.555).Within(0.00001));
        Assert.That(controller.angularVelocity.z.ReadValue(), Is.EqualTo(0.666).Within(0.00001));

        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.A), controller.buttonEast);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.B), controller.buttonSouth);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.X), controller.buttonNorth);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.Y), controller.buttonWest);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.StickL), controller.leftStickButton);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.StickR), controller.rightStickButton);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.L), controller.leftShoulder);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.R), controller.rightShoulder);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.ZL), controller.leftTrigger);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.ZR), controller.rightTrigger);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.Plus), controller.startButton);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.Minus), controller.selectButton);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.LSL), controller.leftSL);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.LSR), controller.leftSR);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.RSL), controller.rightSL);
        AssertButtonPress(controller, new NPadInputStateSwitch().WithButton(NPadInputStateSwitch.Button.RSR), controller.rightSR);

        // Sensors should be marked as noisy.
        Assert.That(controller.attitude.noisy, Is.True);
        Assert.That(controller.angularVelocity.noisy, Is.True);
        Assert.That(controller.acceleration.noisy, Is.True);
    }

    [Test]
    [Category("Devices")]
    public unsafe void Devices_CanUpdateStatus()
    {
        var controller = InputSystem.AddDevice<NPadSwitch>();

        NPadStatusReport? receivedCommand = null;
        unsafe
        {
            runtime.SetDeviceCommandCallback(controller.id,
                (id, commandPtr) =>
                {
                    if (commandPtr->type == NPadStatusReport.Type)
                    {
                        Assert.That(receivedCommand.HasValue, Is.False);
                        receivedCommand = *((NPadStatusReport*)commandPtr);
                        ((NPadStatusReport*)commandPtr)->npadId = NPadSwitch.NpadId.Handheld;
                        ((NPadStatusReport*)commandPtr)->orientation = NPadSwitch.Orientation.Vertical;
                        ((NPadStatusReport*)commandPtr)->styleMask = NPadSwitch.NpadStyles.Handheld;

                        ((NPadStatusReport*)commandPtr)->colorLeftMain = ColorToNNColor(Color.red);
                        ((NPadStatusReport*)commandPtr)->colorLeftSub = ColorToNNColor(Color.black);
                        ((NPadStatusReport*)commandPtr)->colorRightMain = ColorToNNColor(Color.cyan);
                        ((NPadStatusReport*)commandPtr)->colorRightSub = ColorToNNColor(Color.gray);
                        return 1;
                    }

                    if (commandPtr->type == QueryUserIdCommand.Type)
                    {
                        // Sending this command happens before refreshing NPad status
                        return 1;
                    }

                    Assert.Fail("Received wrong type of command, " + commandPtr->type);
                    return InputDeviceCommand.GenericFailure;
                });
        }
        Assert.That(controller.npadId, Is.EqualTo(NPadSwitch.NpadId.Handheld));
        Assert.That(controller.orientation, Is.EqualTo(NPadSwitch.Orientation.Vertical));
        Assert.That(controller.styleMask, Is.EqualTo(NPadSwitch.NpadStyles.Handheld));
        Assert.That(controller.leftControllerColor.Main, Is.EqualTo((Color32)Color.red));
        Assert.That(controller.leftControllerColor.Sub, Is.EqualTo((Color32)Color.black));
        Assert.That(controller.rightControllerColor.Main, Is.EqualTo((Color32)Color.cyan));
        Assert.That(controller.rightControllerColor.Sub, Is.EqualTo((Color32)Color.gray));
    }

    private int ColorToNNColor(Color color)
    {
        Color32 color32 = color;

        return (int)color32.r | ((int)color32.g << 8) | ((int)color32.b << 16) | ((int)color32.a << 24);
    }

    [Test]
    [Category("Devices")]
    public unsafe void Devices_CanSetControllerOrientation()
    {
        var controller = InputSystem.AddDevice<NPadSwitch>();

        NpadDeviceIOCTLSetOrientation? receivedCommand = null;
        unsafe
        {
            runtime.SetDeviceCommandCallback(controller.id,
                (id, commandPtr) =>
                {
                    if (commandPtr->type == NpadDeviceIOCTLSetOrientation.Type)
                    {
                        Assert.That(receivedCommand.HasValue, Is.False);
                        receivedCommand = *((NpadDeviceIOCTLSetOrientation*)commandPtr);
                        return 1;
                    }

                    Assert.Fail("Received wrong type of command");
                    return InputDeviceCommand.GenericFailure;
                });
        }
        controller.SetOrientationToSingleJoyCon(NPadSwitch.Orientation.Horizontal);

        Assert.That(receivedCommand.HasValue, Is.True);
        Assert.That(receivedCommand.Value.orientation, Is.EqualTo(NPadSwitch.Orientation.Horizontal));

        receivedCommand = null;
        controller.SetOrientationToSingleJoyCon(NPadSwitch.Orientation.Vertical);

        Assert.That(receivedCommand.HasValue, Is.True);
        Assert.That(receivedCommand.Value.orientation, Is.EqualTo(NPadSwitch.Orientation.Vertical));
    }

    [Test]
    [Category("Devices")]
    public unsafe void Devices_CanStartSixAxisSensors()
    {
        var controller = InputSystem.AddDevice<NPadSwitch>();

        NpadDeviceIOCTLStartSixAxisSensor? receivedCommand = null;
        unsafe
        {
            runtime.SetDeviceCommandCallback(controller.id,
                (id, commandPtr) =>
                {
                    if (commandPtr->type == NpadDeviceIOCTLStartSixAxisSensor.Type)
                    {
                        Assert.That(receivedCommand.HasValue, Is.False);
                        receivedCommand = *((NpadDeviceIOCTLStartSixAxisSensor*)commandPtr);
                        return 1;
                    }

                    Assert.Fail("Received wrong type of command");
                    return InputDeviceCommand.GenericFailure;
                });
        }
        controller.StartSixAxisSensor();

        Assert.That(receivedCommand.HasValue, Is.True);
    }

    [Test]
    [Category("Devices")]
    public unsafe void Devices_CanStopSixAxisSensors()
    {
        var controller = InputSystem.AddDevice<NPadSwitch>();

        NpadDeviceIOCTLStopSixAxisSensor? receivedCommand = null;
        unsafe
        {
            runtime.SetDeviceCommandCallback(controller.id,
                (id, commandPtr) =>
                {
                    if (commandPtr->type == NpadDeviceIOCTLStopSixAxisSensor.Type)
                    {
                        Assert.That(receivedCommand.HasValue, Is.False);
                        receivedCommand = *((NpadDeviceIOCTLStopSixAxisSensor*)commandPtr);
                        return 1;
                    }

                    Assert.Fail("Received wrong type of command");
                    return InputDeviceCommand.GenericFailure;
                });
        }
        controller.StopSixAxisSensor();

        Assert.That(receivedCommand.HasValue, Is.True);
    }

    [Test]
    [Category("Devices")]
    public unsafe void Devices_CanSetNPadVibrationMotorValues()
    {
        var controller = InputSystem.AddDevice<NPadSwitch>();

        NPadDeviceIOCTLOutputCommand? receivedCommand = null;
        unsafe
        {
            runtime.SetDeviceCommandCallback(controller.id,
                (id, commandPtr) =>
                {
                    if (commandPtr->type == NPadDeviceIOCTLOutputCommand.Type)
                    {
                        Assert.That(receivedCommand.HasValue, Is.False);
                        receivedCommand = *((NPadDeviceIOCTLOutputCommand*)commandPtr);
                        return 1;
                    }

                    Assert.Fail("Received wrong type of command");
                    return InputDeviceCommand.GenericFailure;
                });
        }
        controller.SetMotorSpeeds(0.1234f, 0.5678f);

        Assert.That(receivedCommand.HasValue, Is.True);
        Assert.That(receivedCommand.Value.positions, Is.EqualTo(0xFF));
        Assert.That(receivedCommand.Value.amplitudeLow, Is.EqualTo(0.1234f));
        Assert.That(receivedCommand.Value.frequencyLow, Is.EqualTo(NPadDeviceIOCTLOutputCommand.DefaultFrequencyLow));
        Assert.That(receivedCommand.Value.amplitudeHigh, Is.EqualTo(0.5678f));
        Assert.That(receivedCommand.Value.frequencyHigh, Is.EqualTo(NPadDeviceIOCTLOutputCommand.DefaultFrequencyHigh));

        receivedCommand = null;
        controller.SetMotorSpeeds(0.1234f, 56.78f, 0.9012f, 345.6f);

        Assert.That(receivedCommand.HasValue, Is.True);
        Assert.That(receivedCommand.Value.positions, Is.EqualTo(0xFF));
        Assert.That(receivedCommand.Value.amplitudeLow, Is.EqualTo(0.1234f));
        Assert.That(receivedCommand.Value.frequencyLow, Is.EqualTo(56.78f));
        Assert.That(receivedCommand.Value.amplitudeHigh, Is.EqualTo(0.9012f));
        Assert.That(receivedCommand.Value.frequencyHigh, Is.EqualTo(345.6f));

        receivedCommand = null;
        controller.SetMotorSpeedLeft(0.1234f, 56.78f, 0.9012f, 345.6f);

        Assert.That(receivedCommand.HasValue, Is.True);
        Assert.That(receivedCommand.Value.positions, Is.EqualTo(0x02));
        Assert.That(receivedCommand.Value.amplitudeLow, Is.EqualTo(0.1234f));
        Assert.That(receivedCommand.Value.frequencyLow, Is.EqualTo(56.78f));
        Assert.That(receivedCommand.Value.amplitudeHigh, Is.EqualTo(0.9012f));
        Assert.That(receivedCommand.Value.frequencyHigh, Is.EqualTo(345.6f));

        receivedCommand = null;
        controller.SetMotorSpeedRight(0.1234f, 56.78f, 0.9012f, 345.6f);

        Assert.That(receivedCommand.HasValue, Is.True);
        Assert.That(receivedCommand.Value.positions, Is.EqualTo(0x04));
        Assert.That(receivedCommand.Value.amplitudeLow, Is.EqualTo(0.1234f));
        Assert.That(receivedCommand.Value.frequencyLow, Is.EqualTo(56.78f));
        Assert.That(receivedCommand.Value.amplitudeHigh, Is.EqualTo(0.9012f));
        Assert.That(receivedCommand.Value.frequencyHigh, Is.EqualTo(345.6f));
    }

#endif
}
#endif
