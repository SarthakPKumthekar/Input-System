#if ((UNITY_EDITOR || UNITY_ANDROID) && UNITY_6000_0_OR_NEWER) || PACKAGE_DOCS_GENERATION

using System;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;

namespace UnityEngine.InputSystem.Android.LowLevel
{
    /// <summary>
    /// Input device that represents an Android handheld device.
    /// </summary>
    /// <remarks> Android device is available starting on 6000.0 and newer. </remarks>
    [InputControlLayout(displayName = "Android Device", hideInUI = true)]
    internal class AndroidDevice : InputDevice
    {
        [InputControl(synthetic = true)]
        public ButtonControl button { get; protected set; }

        /// <inheritdoc />
        protected override void OnAdded()
        {
            base.OnAdded();
            InputSystem.onSettingsChange += OnSettingsChange;
        }

        /// <inheritdoc />
        protected override void OnRemoved()
        {
            InputSystem.onSettingsChange -= OnSettingsChange;
            base.OnRemoved();
        }

        private void OnSettingsChange()
        {
            backButtonLeavesApp = InputSystem.settings.android.backButtonLeavesApp;
        }

        private bool backButtonLeavesApp
        {
            get
            {
                var command = GetCustomCommand.Create();
                if (ExecuteCommand(ref command) >= 0)
                {
                    return command.payload >= 1;
                }
                return false;
            }
            set
            {
                var command = SetCustomCommand.Create((uint)AndroidCustomCommand.BackButtonLeavesApp, Convert.ToUInt32(value));
                ExecuteCommand(ref command);
            }
        }

        internal enum AndroidCustomCommand
        {
            BackButtonLeavesApp = 0,
        }
    }
}
#endif // ((UNITY_EDITOR || UNITY_ANDROID) && UNITY_6000_0_OR_NEWER) || PACKAGE_DOCS_GENERATION
