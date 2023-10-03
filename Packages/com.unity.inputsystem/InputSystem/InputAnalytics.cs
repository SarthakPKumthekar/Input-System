#if UNITY_ANALYTICS || UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem.Layouts;
#if UNITY_EDITOR
using UnityEngine.InputSystem.Editor;
#endif

////FIXME: apparently shutdown events are not coming through in the analytics backend

namespace UnityEngine.InputSystem
{
    internal static class InputAnalytics
    {
        public const string kEventStartup = "input_startup";
        public const string kEventShutdown = "input_shutdown";

        public static void Initialize(InputManager manager)
        {
            Debug.Assert(manager.m_Runtime != null);
        }

        public static void OnStartup(InputManager manager)
        {
            var data = new StartupEventData
            {
                version = InputSystem.version.ToString(),
            };

            // Collect recognized devices.
            var devices = manager.devices;
            var deviceList = new List<StartupEventData.DeviceInfo>();
            for (var i = 0; i < devices.Count; ++i)
            {
                var device = devices[i];

                deviceList.Add(
                    StartupEventData.DeviceInfo.FromDescription(device.description, device.native, device.layout));
            }
            data.devices = deviceList.ToArray();

            // Collect unrecognized devices.
            deviceList.Clear();
            var availableDevices = manager.m_AvailableDevices;
            var availableDeviceCount = manager.m_AvailableDeviceCount;
            for (var i = 0; i < availableDeviceCount; ++i)
            {
                var deviceId = availableDevices[i].deviceId;
                if (manager.TryGetDeviceById(deviceId) != null)
                    continue;

                deviceList.Add(StartupEventData.DeviceInfo.FromDescription(availableDevices[i].description,
                    availableDevices[i].isNative));
            }

            data.unrecognized_devices = deviceList.ToArray();

            #if UNITY_EDITOR
            data.new_enabled = EditorPlayerSettingHelpers.newSystemBackendsEnabled;
            data.old_enabled = EditorPlayerSettingHelpers.oldSystemBackendsEnabled;
            #endif

            manager.m_Runtime.RegisterAnalyticsEvent(kEventStartup, 10, 100);
            manager.m_Runtime.SendAnalyticsEvent(kEventStartup, data);
        }

        public static void OnShutdown(InputManager manager)
        {
            var metrics = manager.metrics;
            var data = new ShutdownEventData
            {
                max_num_devices = metrics.maxNumDevices,
                max_state_size_in_bytes = metrics.maxStateSizeInBytes,
                total_event_bytes = metrics.totalEventBytes,
                total_event_count = metrics.totalEventCount,
                total_frame_count = metrics.totalUpdateCount,
                total_event_processing_time = (float)metrics.totalEventProcessingTime,
            };

            manager.m_Runtime.RegisterAnalyticsEvent(kEventShutdown, 10, 100);
            manager.m_Runtime.SendAnalyticsEvent(kEventShutdown, data);
        }

        /// <summary>
        /// Data about what configuration we start up with.
        /// </summary>
        /// <remarks>
        /// Has data about the devices present at startup so that we can know what's being
        /// used out there. Also has data about devices we couldn't recognize.
        ///
        /// Note that we exclude devices that are always present (e.g. keyboard and mouse
        /// on desktops or touchscreen on phones).
        /// </remarks>
        [Serializable]
        public struct StartupEventData
        {
            public string version;
            public DeviceInfo[] devices;
            public DeviceInfo[] unrecognized_devices;

            ////REVIEW: ATM we have no way of retrieving these in the player
            #if UNITY_EDITOR
            public bool new_enabled;
            public bool old_enabled;
            #endif

            [Serializable]
            public struct DeviceInfo
            {
                public string layout;
                public string @interface;
                public string product;
                public bool native;

                public static DeviceInfo FromDescription(InputDeviceDescription description, bool native = false, string layout = null)
                {
                    string product;
                    if (!string.IsNullOrEmpty(description.product) && !string.IsNullOrEmpty(description.manufacturer))
                        product = $"{description.manufacturer} {description.product}";
                    else if (!string.IsNullOrEmpty(description.product))
                        product = description.product;
                    else
                        product = description.manufacturer;

                    if (string.IsNullOrEmpty(layout))
                        layout = description.deviceClass;

                    return new DeviceInfo
                    {
                        layout = layout,
                        @interface = description.interfaceName,
                        product = product,
                        native = native
                    };
                }
            }
        }

        /// <summary>
        /// Data about when after startup the user first interacted with the application.
        /// </summary>
        [Serializable]
        public struct FirstUserInteractionEventData
        {
        }

        /// <summary>
        /// Data about what level of data we pumped through the system throughout its lifetime.
        /// </summary>
        [Serializable]
        public struct ShutdownEventData
        {
            public int max_num_devices;
            public int max_state_size_in_bytes;
            public int total_event_bytes;
            public int total_event_count;
            public int total_frame_count;
            public float total_event_processing_time;
        }

        public enum InputActionsEditorType
        {
            Invalid = 0,
            FreeFloatingEditorWindow = 1,
            EmbeddedInProjectSettings = 2
        }

        [Serializable]
        public struct InputActionsEditorSession
        {
            public InputActionsEditorSession(InputActionsEditorType type)
            {
                this.type = type;
                totalDurationSeconds = 0;
                totalFocusDurationSeconds = 0;
                totalActionMapEdits = 0;
                totalActionEdits = 0;
                totalBindingEdits = 0;
                numberOfUserSaves = 0;
                numberOfAutoSaves = 0;
                
                m_FocusStart = float.NaN;
                m_SessionStart = float.NaN;
            }

            public void RegisterActionMapEdit()
            {
                ++totalActionMapEdits;
            }

            public void RegisterActionEdit()
            {
                ++totalActionEdits;
            }

            public void RegisterBindingEdit()
            {
                ++totalBindingEdits;
            }

            public void RegisterFocusIn()
            {
                if (hasFocus)
                    return;

                m_FocusStart = CurrentTime();
            }

            public void RegisterFocusOut()
            {
                if (!hasFocus)
                    return;
                
                var focusDurationSeconds = CurrentTime() - m_FocusStart;
                this.totalFocusDurationSeconds += focusDurationSeconds;
            }

            public void StartSession()
            {
                if (hasSession)
                    return;
                
                m_SessionStart = CurrentTime();
            }

            public void EndSession()
            {
                var sessionDurationSeconds = CurrentTime() - m_SessionStart;
                totalDurationSeconds += sessionDurationSeconds;
            }

            public override string ToString()
            {
                return $"{nameof(type)}: {type}, " +
                       $"{nameof(totalDurationSeconds)}: {totalDurationSeconds} seconds, " +
                       $"{nameof(totalFocusDurationSeconds)}: {totalFocusDurationSeconds} seconds, " +
                       $"{nameof(totalActionMapEdits)}: {totalActionMapEdits}, " +
                       $"{nameof(totalActionEdits)}: {totalActionEdits}, " +
                       $"{nameof(totalBindingEdits)}: {totalBindingEdits}, " +
                       $"{nameof(numberOfUserSaves)}: {numberOfUserSaves}, " +
                       $"{nameof(numberOfAutoSaves)}: {numberOfAutoSaves}";
            }

            public InputActionsEditorType type;
            public float totalDurationSeconds;
            public float totalFocusDurationSeconds;
            public int totalActionMapEdits;
            public int totalActionEdits;
            public int totalBindingEdits;
            public int numberOfUserSaves;
            public int numberOfAutoSaves;
            
            [NonSerialized] private float m_FocusStart;
            [NonSerialized] private float m_SessionStart;

            private bool hasFocus => !float.IsNaN(m_FocusStart);
            private bool hasSession => !float.IsNaN(m_SessionStart);
            private float CurrentTime() => Time.realtimeSinceStartup;
        }

        public static InputActionsEditorSession OnInputActionsEditorBeginSession(InputActionsEditorType type)
        {
            Debug.Log("OnInputActionsEditorBeginSession");

            return new InputActionsEditorSession { type = type };
        }

        public static void OnInputActionsEditorSessionEnding(ref InputActionsEditorSession session)
        {
            Debug.Log("OnInputActionsEditorEndSession: " + session);
        }
    }
}
#endif // UNITY_ANALYTICS || UNITY_EDITOR
