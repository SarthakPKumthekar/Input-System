using System.ComponentModel;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Haptics;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

////TODO: come up with consistent naming for buttons; (xxxButton? xxx?)

////REVIEW: should we add a gyro as a standard feature of gamepads?

////TODO: allow to be used for mouse simulation

namespace UnityEngine.InputSystem.LowLevel
{
    /// <summary>
    /// Default state layout for gamepads.
    /// </summary>
    /// <remarks>
    /// Be aware that unlike some other devices like <see cref="Mouse"/> or <see cref="Touchscreen"/>,
    /// gamepad devices tend to have wildly varying state formats, i.e. forms in which they internally
    /// store their input data. In practice, even on the same platform gamepads will often store
    /// their data in different formats. This means that <see cref="GamepadState"/> will often *not*
    /// be the format in which a particular gamepad (such as <see cref="XInput.XInputController"/>,
    /// for example) stores its data.
    /// </remarks>
    /// <seealso cref="Gamepad"/>
    // NOTE: Must match GamepadInputState in native.
    [StructLayout(LayoutKind.Explicit, Size = 28)]
    public struct GamepadState : IInputStateTypeInfo
    {
        public static FourCC kFormat => new FourCC('G', 'P', 'A', 'D');

        /// <summary>
        /// Button bit mask.
        /// </summary>
        /// <seealso cref="GamepadButton"/>
        ////REVIEW: do we want the name to correspond to what's actually on the device?
        [InputControl(name = "dpad", layout = "Dpad", usage = "Hatswitch", displayName = "D-Pad")]
        [InputControl(name = "buttonSouth", layout = "Button", bit = (uint)GamepadButton.South, usages = new[] { "PrimaryAction", "Submit" }, aliases = new[] { "a", "cross" }, displayName = "Button South", shortDisplayName = "A")]
        [InputControl(name = "buttonWest", layout = "Button", bit = (uint)GamepadButton.West, usage = "SecondaryAction", aliases = new[] { "x", "square" }, displayName = "Button West", shortDisplayName = "X")]
        [InputControl(name = "buttonNorth", layout = "Button", bit = (uint)GamepadButton.North, aliases = new[] { "y", "triangle" }, displayName = "Button North", shortDisplayName = "Y")]
        [InputControl(name = "buttonEast", layout = "Button", bit = (uint)GamepadButton.East, usages = new[] { "Back", "Cancel" }, aliases = new[] { "b", "circle" }, displayName = "Button East", shortDisplayName = "B")]
        ////FIXME: 'Press' naming is inconsistent with 'Button' naming
        [InputControl(name = "leftStickPress", layout = "Button", bit = (uint)GamepadButton.LeftStick, displayName = "Left Stick Press")]
        [InputControl(name = "rightStickPress", layout = "Button", bit = (uint)GamepadButton.RightStick, displayName = "Right Stick Press")]
        [InputControl(name = "leftShoulder", layout = "Button", bit = (uint)GamepadButton.LeftShoulder, displayName = "Left Shoulder", shortDisplayName = "LB")]
        [InputControl(name = "rightShoulder", layout = "Button", bit = (uint)GamepadButton.RightShoulder, displayName = "Right Shoulder", shortDisplayName = "RB")]
        ////REVIEW: seems like these two should get less ambiguous names as well
        [InputControl(name = "start", layout = "Button", bit = (uint)GamepadButton.Start, usage = "Menu", displayName = "Start")]
        [InputControl(name = "select", layout = "Button", bit = (uint)GamepadButton.Select, displayName = "Select")]
        [FieldOffset(0)]
        public uint buttons;

        /// <summary>
        /// Left stick position.
        /// </summary>
        [InputControl(layout = "Stick", usage = "Primary2DMotion", processors = "stickDeadzone", displayName = "Left Stick", shortDisplayName = "LS")]
        [FieldOffset(4)]
        public Vector2 leftStick;

        /// <summary>
        /// Right stick position.
        /// </summary>
        [InputControl(layout = "Stick", usage = "Secondary2DMotion", processors = "stickDeadzone", displayName = "Right Stick", shortDisplayName = "RS")]
        [FieldOffset(12)]
        public Vector2 rightStick;

        ////REVIEW: should left and right trigger get deadzones?

        /// <summary>
        /// Position of the left trigger.
        /// </summary>
        [InputControl(layout = "Button", format = "FLT", usage = "SecondaryTrigger", displayName = "Left Trigger", shortDisplayName = "LT")]
        [FieldOffset(20)]
        public float leftTrigger;

        /// <summary>
        /// Position of the right trigger.
        /// </summary>
        [InputControl(layout = "Button", format = "FLT", usage = "SecondaryTrigger", displayName = "Right Trigger", shortDisplayName = "RT")]
        [FieldOffset(24)]
        public float rightTrigger;

        public FourCC format => kFormat;

        public GamepadState(params GamepadButton[] buttons)
            : this()
        {
            if (buttons == null)
                throw new System.ArgumentNullException(nameof(buttons));

            foreach (var button in buttons)
            {
                var bit = (uint)1 << (int)button;
                this.buttons |= bit;
            }
        }

        public GamepadState WithButton(GamepadButton button, bool value = true)
        {
            var bit = (uint)1 << (int)button;
            if (value)
                buttons |= bit;
            else
                buttons &= ~bit;
            return this;
        }
    }


    /// <summary>
    /// Enum of common gamepad buttons.
    /// </summary>
    /// <remarks>
    /// Can be used as an array indexer on the <see cref="Gamepad"/> class to get individual button controls.
    /// </remarks>
    public enum GamepadButton
    {
        // Dpad buttons. Important to be first in the bitfield as we'll
        // point the DpadControl to it.
        // IMPORTANT: Order has to match what is expected by DpadControl.

        /// <summary>
        /// The up button on a gamepad's dpad.
        /// </summary>
        DpadUp,

        /// <summary>
        /// The down button on a gamepad's dpad.
        /// </summary>
        DpadDown,

        /// <summary>
        /// The left button on a gamepad's dpad.
        /// </summary>
        DpadLeft,

        /// <summary>
        /// The right button on a gamepad's dpad.
        /// </summary>
        DpadRight,

        // Face buttons. We go with a north/south/east/west naming as that
        // clearly disambiguates where we expect the respective button to be.

        /// <summary>
        /// The upper action button on a gamepad.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="Y"/> and <see cref="Triangle"/> which are the Xbox and PlayStation controller names for this button.
        /// </remarks>
        North,

        /// <summary>
        /// The right action button on a gamepad.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="B"/> and <see cref="Circle"/> which are the Xbox and PlayStation controller names for this button.
        /// </remarks>
        East,

        /// <summary>
        /// The lower action button on a gamepad.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="A"/> and <see cref="Cross"/> which are the Xbox and PlayStation controller names for this button.
        /// </remarks>
        South,

        /// <summary>
        /// The left action button on a gamepad.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="X"/> and <see cref="Square"/> which are the Xbox and PlayStation controller names for this button.
        /// </remarks>
        West,


        /// <summary>
        /// The button pressed by pressing down the left stick on a gamepad.
        /// </summary>
        LeftStick,

        /// <summary>
        /// The button pressed by pressing down the right stick on a gamepad.
        /// </summary>
        RightStick,

        /// <summary>
        /// The left shoulder button on a gamepad.
        /// </summary>
        LeftShoulder,

        /// <summary>
        /// The right shoulder button on a gamepad.
        /// </summary>
        RightShoulder,

        /// <summary>
        /// The left trigger button on a gamepad.
        /// </summary>
        LeftTrigger,

        /// <summary>
        /// The right trigger button on a gamepad.
        /// </summary>
        RightTrigger,

        /// <summary>
        /// The start button.
        /// </summary>
        Start,

        /// <summary>
        /// The select button.
        /// </summary>
        Select,

        /// <summary>
        /// The X button on an Xbox controller.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="West"/>, which is the generic name of this button.
        /// </remarks>
        X = West,
        /// <summary>
        /// The Y button on an Xbox controller.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="North"/>, which is the generic name of this button.
        /// </remarks>
        Y = North,
        /// <summary>
        /// The A button on an Xbox controller.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="South"/>, which is the generic name of this button.
        /// </remarks>
        A = South,
        /// <summary>
        /// The B button on an Xbox controller.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="East"/>, which is the generic name of this button.
        /// </remarks>
        B = East,

        /// <summary>
        /// The cross button on a PlayStation controller.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="South"/>, which is the generic name of this button.
        /// </remarks>
        Cross = South,
        /// <summary>
        /// The square button on a PlayStation controller.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="West"/>, which is the generic name of this button.
        /// </remarks>
        Square = West,
        /// <summary>
        /// The triangle button on a PlayStation controller.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="North"/>, which is the generic name of this button.
        /// </remarks>
        Triangle = North,
        /// <summary>
        /// The circle button on a PlayStation controller.
        /// </summary>
        /// <remarks>
        /// Identical to <see cref="East"/>, which is the generic name of this button.
        /// </remarks>
        Circle = East,
    }
}

namespace UnityEngine.InputSystem
{
    /// <summary>
    /// An Xbox-style gamepad with two sticks, a D-Pad, four face buttons, two triggers,
    /// two shoulder buttons, and two menu buttons that usually sit in the midsection of the gamepad.
    /// </summary>
    /// <remarks>
    /// The Gamepad layout provides a standardized layouts for gamepads. Generally, if a specific
    /// device is represented as a Gamepad, the controls, such as the face buttons, are guaranteed
    /// to be mapped correctly and consistently. If, based on the set of supported devices available
    /// to the input system, this cannot be guaranteed, a given device is usually represted as a
    /// generic <see cref="Joystick"/> or as just a plain <see cref="HID.HID"/> instead.
    ///
    /// <example>
    /// <code>
    /// // Show all gamepads in the system.
    /// Debug.Log(string.Join("\n", Gamepad.all));
    ///
    /// // Check whether the X button on the current gamepad is pressed.
    /// if (Gamepad.current.xButton.wasPressedThisFrame)
    ///     Debug.Log("Pressed");
    ///
    /// // Rumble the left motor on the current gamepad slightly.
    /// Gamepad.current.SetMotorSpeeds(0.2f, 0.
    /// </code>
    /// </example>
    /// </remarks>
    [InputControlLayout(stateType = typeof(GamepadState), isGenericTypeOfDevice = true)]
    [Scripting.Preserve]
    public class Gamepad : InputDevice, IDualMotorRumble
    {
        /// <summary>
        /// The left face button of the gamepad.
        /// </summary>
        /// <value>Control representing the X/Square face button.</value>
        /// <remarks>
        /// On an Xbox controller, this is the X button and on the PS4 controller, this is the
        /// square button.
        /// </remarks>
        /// <seealso cref="xButton"/>
        /// <seealso cref="squareButton"/>
        public ButtonControl buttonWest { get; private set; }

        /// <summary>
        /// The top face button of the gamepad.
        /// </summary>
        /// <value>Control representing the Y/Triangle face button.</value>
        /// <remarks>
        /// On an Xbox controller, this is the Y button and on the PS4 controller, this is the
        /// triangle button.
        /// </remarks>
        /// <seealso cref="yButton"/>
        /// <seealso cref="triangleButton"/>
        public ButtonControl buttonNorth { get; private set; }

        /// <summary>
        /// The bottom face button of the gamepad.
        /// </summary>
        /// <value>Control representing the A/Cross face button.</value>
        /// <remarks>
        /// On an Xbox controller, this is the A button and on the PS4 controller, this is the
        /// cross button.
        /// </remarks>
        /// <seealso cref="aButton"/>
        /// <seealso cref="crossButton"/>
        public ButtonControl buttonSouth { get; private set; }

        /// <summary>
        /// The right face button of the gamepad.
        /// </summary>
        /// <value>Control representing the B/Circle face button.</value>
        /// <remarks>
        /// On an Xbox controller, this is the B button and on the PS4 controller, this is the
        /// circle button.
        /// </remarks>
        /// <seealso cref="bButton"/>
        /// <seealso cref="circleButton"/>
        public ButtonControl buttonEast { get; private set; }

        /// <summary>
        /// The button that gets triggered when <see cref="leftStick"/> is pressed down.
        /// </summary>
        /// <value>Control representing a click with the left stick.</value>
        public ButtonControl leftStickButton { get; private set; }

        /// <summary>
        /// The button that gets triggered when <see cref="rightStick"/> is pressed down.
        /// </summary>
        /// <value>Control representing a click with the right stick.</value>
        public ButtonControl rightStickButton { get; private set; }

        public ButtonControl startButton { get; private set; }
        public ButtonControl selectButton { get; private set; }

        public DpadControl dpad { get; private set; }

        public ButtonControl leftShoulder { get; private set; }
        public ButtonControl rightShoulder { get; private set; }

        public StickControl leftStick { get; private set; }
        public StickControl rightStick { get; private set; }

        public ButtonControl leftTrigger { get; private set; }
        public ButtonControl rightTrigger { get; private set; }

        /// <summary>
        /// Same as <see cref="buttonSouth"/>. Xbox-style alias.
        /// </summary>
        /// <value>Same as <see cref="buttonSouth"/>.</value>
        public ButtonControl aButton => buttonSouth;

        /// <summary>
        /// Same as <see cref="buttonEast"/>. Xbox-style alias.
        /// </summary>
        /// <value>Same as <see cref="buttonEast"/>.</value>
        public ButtonControl bButton => buttonEast;

        /// <summary>
        /// Same as <see cref="buttonWest"/> Xbox-style alias.
        /// </summary>
        /// <value>Same as <see cref="buttonWest"/>.</value>
        public ButtonControl xButton => buttonWest;

        /// <summary>
        /// Same as <see cref="buttonNorth"/>. Xbox-style alias.
        /// </summary>
        /// <value>Same as <see cref="buttonNorth"/>.</value>
        public ButtonControl yButton => buttonNorth;

        /// <summary>
        /// Same as <see cref="buttonNorth"/>. PS4-style alias.
        /// </summary>
        /// <value>Same as <see cref="buttonNorth"/>.</value>
        public ButtonControl triangleButton => buttonNorth;

        /// <summary>
        /// Same as <see cref="buttonWest"/>. PS4-style alias.
        /// </summary>
        /// <value>Same as <see cref="buttonWest"/>.</value>
        public ButtonControl squareButton => buttonWest;

        /// <summary>
        /// Same as <see cref="buttonEast"/>. PS4-style alias.
        /// </summary>
        /// <value>Same as <see cref="buttonEast"/>.</value>
        public ButtonControl circleButton => buttonEast;

        /// <summary>
        /// Same as <see cref="buttonSouth"/>. PS4-style alias.
        /// </summary>
        /// <value>Same as <see cref="buttonSouth"/>.</value>
        public ButtonControl crossButton => buttonSouth;

        ////REVIEW: what about having 'axes' and 'buttons' read-only arrays like Joysticks and allowing to index that?
        public ButtonControl this[GamepadButton button]
        {
            get
            {
                switch (button)
                {
                    case GamepadButton.North: return buttonNorth;
                    case GamepadButton.South: return buttonSouth;
                    case GamepadButton.East: return buttonEast;
                    case GamepadButton.West: return buttonWest;
                    case GamepadButton.Start: return startButton;
                    case GamepadButton.Select: return selectButton;
                    case GamepadButton.LeftShoulder: return leftShoulder;
                    case GamepadButton.RightShoulder: return rightShoulder;
                    case GamepadButton.LeftTrigger: return leftTrigger;
                    case GamepadButton.RightTrigger: return rightTrigger;
                    case GamepadButton.LeftStick: return leftStickButton;
                    case GamepadButton.RightStick: return rightStickButton;
                    case GamepadButton.DpadUp: return dpad.up;
                    case GamepadButton.DpadDown: return dpad.down;
                    case GamepadButton.DpadLeft: return dpad.left;
                    case GamepadButton.DpadRight: return dpad.right;
                    default:
                        throw new InvalidEnumArgumentException(nameof(button), (int)button, typeof(GamepadButton));
                }
            }
        }

        /// <summary>
        /// The gamepad last used by the user or null if there is no gamepad connected to the system.
        /// </summary>
        public static Gamepad current { get; private set; }

        /// <summary>
        /// A list of gamepads currently connected to the system.
        /// </summary>
        /// <value>All currently connected gamepads.</value>
        /// <remarks>
        /// Does not cause GC allocation.
        ///
        /// Do *NOT* hold on to the value returned by this getter but rather query it whenever
        /// you need it. Whenever the gamepad setup changes, the value returned by this getter
        /// is invalidated.
        /// </remarks>
        public new static ReadOnlyArray<Gamepad> all => new ReadOnlyArray<Gamepad>(s_Gamepads, 0, s_GamepadCount);

        /// <inheritdoc />
        protected override void FinishSetup()
        {
            ////REVIEW: what's actually faster/better... storing these in properties or doing the lookup on the fly?
            buttonWest = GetChildControl<ButtonControl>("buttonWest");
            buttonNorth = GetChildControl<ButtonControl>("buttonNorth");
            buttonSouth = GetChildControl<ButtonControl>("buttonSouth");
            buttonEast = GetChildControl<ButtonControl>("buttonEast");

            startButton = GetChildControl<ButtonControl>("start");
            selectButton = GetChildControl<ButtonControl>("select");

            leftStickButton = GetChildControl<ButtonControl>("leftStickPress");
            rightStickButton = GetChildControl<ButtonControl>("rightStickPress");

            dpad = GetChildControl<DpadControl>("dpad");

            leftShoulder = GetChildControl<ButtonControl>("leftShoulder");
            rightShoulder = GetChildControl<ButtonControl>("rightShoulder");

            leftStick = GetChildControl<StickControl>("leftStick");
            rightStick = GetChildControl<StickControl>("rightStick");

            leftTrigger = GetChildControl<ButtonControl>("leftTrigger");
            rightTrigger = GetChildControl<ButtonControl>("rightTrigger");

            base.FinishSetup();
        }

        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }

        /// <summary>
        /// Called when the gamepad is added to the system.
        /// </summary>
        protected override void OnAdded()
        {
            ArrayHelpers.AppendWithCapacity(ref s_Gamepads, ref s_GamepadCount, this);
        }

        /// <summary>
        /// Called when the gamepad is removed from the system.
        /// </summary>
        protected override void OnRemoved()
        {
            if (current == this)
                current = null;

            // Remove from `all`.
            var index = ArrayHelpers.IndexOfReference(s_Gamepads, this, s_GamepadCount);
            if (index != -1)
                ArrayHelpers.EraseAtWithCapacity(s_Gamepads, ref s_GamepadCount, index);
            else
            {
                Debug.Assert(false,
                    string.Format("Gamepad {0} seems to not have been added but is being removed (gamepad list: {1})",
                        this, string.Join(", ", all))); // Put in else to not allocate on normal path.
            }
        }

        public virtual void PauseHaptics()
        {
            m_Rumble.PauseHaptics(this);
        }

        public virtual void ResumeHaptics()
        {
            m_Rumble.ResumeHaptics(this);
        }

        public virtual void ResetHaptics()
        {
            m_Rumble.ResetHaptics(this);
        }

        /// <inheritdoc />
        public virtual void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            m_Rumble.SetMotorSpeeds(this, lowFrequency, highFrequency);
        }

        private DualMotorRumble m_Rumble;

        private static int s_GamepadCount;
        private static Gamepad[] s_Gamepads;
    }
}
