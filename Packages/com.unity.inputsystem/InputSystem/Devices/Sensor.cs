using System;
using System.Runtime.InteropServices;
using UnityEngine.Experimental.Input.Controls;
using UnityEngine.Experimental.Input.LowLevel;
using UnityEngine.Experimental.Input.Utilities;

////TODO: hook up all sensor controls to noise suppression (actually... for sensors we probably do NOT want that)

namespace UnityEngine.Experimental.Input.LowLevel
{
    public struct AccelerometerState : IInputStateTypeInfo
    {
        public static FourCC kFormat
        {
            get { return new FourCC('A', 'C', 'C', 'L'); }
        }

        [InputControl] public Vector3 acceleration;

        public FourCC GetFormat()
        {
            return kFormat;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 52)]
    public struct GyroscopeState : IInputStateTypeInfo
    {
        public static FourCC kFormat
        {
            get { return new FourCC('G', 'Y', 'R', 'O'); }
        }

        [InputControl][FieldOffset(0)] public Vector3 gravity;
        [InputControl][FieldOffset(12)] public Vector3 angularVelocity;
        [InputControl][FieldOffset(24)] public Quaternion orientation;
        [InputControl][FieldOffset(40)] public Vector3 acceleration;

        public FourCC GetFormat()
        {
            return kFormat;
        }
    }
}

namespace UnityEngine.Experimental.Input
{
    public abstract class Sensor : InputDevice
    {
        public float samplingFrequency
        {
            get
            {
                var command = QuerySamplingFrequencyCommand.Create();
                if (ExecuteCommand(ref command) >= 0)
                    return command.frequency;
                throw new NotSupportedException(string.Format("Device '{0}' does not support querying sampling frequency", this));
            }
            set
            {
                var command = SetSamplingFrequencyCommand.Create(value);
                ExecuteCommand(ref command);
            }
        }
    }

    [InputControlLayout(stateType = typeof(AccelerometerState))]
    public class Accelerometer : Sensor
    {
        public Vector3Control acceleration { get; private set; }

        public static Accelerometer current { get; private set; }

        protected override void FinishSetup(InputDeviceBuilder builder)
        {
            acceleration = builder.GetControl<Vector3Control>("acceleration");
            base.FinishSetup(builder);
        }

        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }
    }

    [InputControlLayout(stateType = typeof(GyroscopeState))]
    public class Gyroscope : Sensor
    {
        public QuaternionControl orientation { get; private set; }
        public Vector3Control acceleration { get; private set; }
        public Vector3Control angularVelocity { get; private set; }
        public Vector3Control gravity { get; private set; }

        public static Gyroscope current { get; private set; }

        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }

        protected override void FinishSetup(InputDeviceBuilder builder)
        {
            orientation = builder.GetControl<QuaternionControl>("orientation");
            acceleration = builder.GetControl<Vector3Control>("acceleration");
            angularVelocity = builder.GetControl<Vector3Control>("angularVelocity");
            gravity = builder.GetControl<Vector3Control>("gravity");
            base.FinishSetup(builder);
        }
    }

    public class GPS : Sensor
    {
    }
}
