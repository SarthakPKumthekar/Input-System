using System.Globalization;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Scripting;

namespace UnityEngine.InputSystem.Controls
{
    /// <summary>
    /// A key on a <see cref="Keyboard"/>.
    /// </summary>
    /// <remarks>
    /// This is an extended button control which adds various features to account for the fact that keys
    /// have symbols associated with them which may change depending on keyboard layout as well as in combination
    /// with other keys.
    ///
    /// Note:
    /// Unity input system key codes and input manager key codes are designed with game controls in mind.
    ///     
    /// This means the way they are assigned is intended to preserve the location of keys on keyboards, 
    /// so that pressing a key in the same location on different keyboards should result in the same action
    /// regardless of what is printed on a key or what current system language is set.
    /// 
    /// This means, for example, that <see cref="A"/> is always the key to the right of <see cref="CapsLock"/>,
    /// regardless of which key (if any) produces the "a" character on the current keyboard layout.
    /// 
    /// Unity relies on physical hardware in the keyboards to report same USB HID "usage" for the keys in
    /// the same location.This puts a practical limit on what can be achieved, because different keyboards
    /// might report different data, and this is outside of Unity's control.
    /// 
    /// For this reason, you should not use key codes to read text input.
    /// Instead, you should use the <see cref="Keyboard.onTextInput"/> callback.
    /// The `onTextInput` callback provides you with the actual text characters which correspond
    /// to the symbols printed on a keyboard, based on the end user's current system language layout.
    ///
    /// To find the text character (if any) generated by a key according to the currently active keyboard
    /// layout, use the <see cref="InputControl.displayName"/> property of <see cref="KeyControl"/>.
    /// </remarks>
    public class KeyControl : ButtonControl
    {
        /// <summary>
        /// The code used in Unity to identify the key.
        /// </summary>
        /// <remarks>
        /// This property must be initialized by <see cref="InputControl.FinishSetup"/> of
        /// the device owning the control.
        /// You should not use `keyCode` to read text input. For more information, <see cref="see Controls.KeyControl"/>
        /// </remarks>
        public Key keyCode { get; set; }

        ////REVIEW: rename this to something like platformKeyCode? We're not really dealing with scan code here.
        /// <summary>
        /// The code that the underlying platform uses to identify the key.
        /// </summary>
        public int scanCode
        {
            get
            {
                RefreshConfigurationIfNeeded();
                return m_ScanCode;
            }
        }

        protected override void RefreshConfiguration()
        {
            // Wipe our last cached set of data (if any).
            displayName = null;
            m_ScanCode = 0;

            var command = QueryKeyNameCommand.Create(keyCode);
            if (device.ExecuteCommand(ref command) > 0)
            {
                m_ScanCode = command.scanOrKeyCode;

                var rawKeyName = command.ReadKeyName();
                if (string.IsNullOrEmpty(rawKeyName))
                {
                    displayName = rawKeyName;
                    return;
                }

                var textInfo = CultureInfo.InvariantCulture.TextInfo;
                // We need to lower case first because ToTitleCase preserves upper casing.
                // For example on Swedish Windows layout right shift display name is "HÖGER SKIFT".
                // Just passing it to ToTitleCase won't change anything. But passing "höger skift" will return "Höger Skift".
                var keyNameLowerCase = textInfo.ToLower(rawKeyName);
                if (string.IsNullOrEmpty(keyNameLowerCase))
                {
                    displayName = rawKeyName;
                    return;
                }

                displayName = textInfo.ToTitleCase(keyNameLowerCase);
            }
        }

        // Cached configuration data for the key. We fetch this from the
        // device on demand.
        private int m_ScanCode;
    }
}
