#if UNITY_EDITOR || UNITY_IOS || UNITY_TVOS
using UnityEngine.InputSystem.Layouts;

namespace UnityEngine.InputSystem.Plugins.iOS
{
    public static class iOSSupport
    {
        public static void Initialize()
        {
            InputSystem.RegisterLayout<iOSGameController>("iOSGameController",
                matches: new InputDeviceMatcher()
                    .WithInterface("iOS")
                    .WithDeviceClass("iOSGameController"));
        }
    }
}
#endif // UNITY_EDITOR || UNITY_IOS || UNITY_TVOS
