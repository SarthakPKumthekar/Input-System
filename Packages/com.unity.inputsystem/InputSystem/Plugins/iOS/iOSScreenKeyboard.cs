﻿#if UNITY_EDITOR || UNITY_IOS || UNITY_TVOS
using System;
using System.Runtime.InteropServices;
using AOT;

namespace UnityEngine.Experimental.Input.Plugins.iOS
{
    public class iOSScreenKeyboard : ScreenKeyboard
    {
        internal delegate void OnTextChanged(string text);

        internal delegate void OnStatusChanged(ScreenKeyboardStatus status);
        
        [StructLayout(LayoutKind.Sequential)]
        internal struct iOSScreenKeyboardCallbacks
        {
            internal OnTextChanged onTextChanged;
            internal OnStatusChanged onStatusChanged;
        }
        
        [DllImport("__Internal")]
        private static extern void _iOSScreenKeyboardShow(ref ScreenKeyboardShowParams showParams, int sizeOfShowParams, ref iOSScreenKeyboardCallbacks callbacks, int sizeOfCallbacks);
        
        [DllImport("__Internal")]
        private static extern Rect _iOSScreenKeyboardOccludingArea();

        [MonoPInvokeCallback(typeof(OnTextChanged))]
        private static void OnTextChangedCallback(string text)
        {
            var screenKeyboard = (iOSScreenKeyboard) ScreenKeyboard.GetInstance();
            if (screenKeyboard == null)
                throw new Exception("OnTextChangedCallback: Failed to get iOSScreenKeyboard instance");
            screenKeyboard.ChangeInputField(new InputFieldEventArgs() { text = text });
        }

        [MonoPInvokeCallback(typeof(OnStatusChanged))]
        private static void OnStatusChangedCallback(ScreenKeyboardStatus status)
        {
            var screenKeyboard = (iOSScreenKeyboard) ScreenKeyboard.GetInstance();
            if (screenKeyboard == null)
                throw new Exception("OnStatusChangedCallback: Failed to get iOSScreenKeyboard instance");
            screenKeyboard.ChangeStatus(status);
        }

        public override void Show(ScreenKeyboardShowParams showParams)
        {
            var callbacks = new iOSScreenKeyboardCallbacks()
            {
                onTextChanged = OnTextChangedCallback,
                onStatusChanged = OnStatusChangedCallback
            };
            _iOSScreenKeyboardShow(ref showParams, Marshal.SizeOf(showParams), ref callbacks, Marshal.SizeOf(callbacks));
        }

        public override void Hide()
        {
        }

        public override string inputFieldText
        {
            get
            {

                return string.Empty;
            }
            set
            {
     
            }
        }

        public override Rect occludingArea
        {
            get { return _iOSScreenKeyboardOccludingArea(); }
        }
    }
}
#endif