#if UNITY_EDITOR
using System;

namespace UnityEngine.InputSystem.Editor
{
#if UNITY_2023_2_OR_NEWER
    [UnityEngine.Analytics.AnalyticInfo(eventName: kEventName, maxEventsPerHour: kMaxEventsPerHour,
        maxNumberOfElements: kMaxNumberOfElements, vendorKey: UnityEngine.InputSystem.InputAnalytics.kVendorKey)]
#endif // UNITY_2023_2_OR_NEWER
    internal class PlayerInputManagerEditorAnalytic : UnityEngine.InputSystem.InputAnalytics.IInputAnalytic
    {
        public const string kEventName = "input_playerinputmanager_editor";
        public const int kMaxEventsPerHour = 100; // default: 1000
        public const int kMaxNumberOfElements = 100; // default: 1000

        public InputAnalytics.InputAnalyticInfo info =>
            new InputAnalytics.InputAnalyticInfo(kEventName, kMaxEventsPerHour, kMaxNumberOfElements);

        private readonly Data m_Data;

        public PlayerInputManagerEditorAnalytic(ref Data data)
        {
            m_Data = data;
        }

#if UNITY_EDITOR && UNITY_2023_2_OR_NEWER
        public bool TryGatherData(out UnityEngine.Analytics.IAnalytic.IData data, out Exception error)
#else
        public bool TryGatherData(out InputAnalytics.IInputAnalyticData data, out Exception error)
#endif
        {
            data = m_Data;
            error = null;
            return true;
        }

        internal struct Data : IEquatable<Data>,
                               UnityEngine.InputSystem.InputAnalytics.IInputAnalyticData
        {
            public enum PlayerJoinBehavior
            {
                JoinPlayersWhenButtonIsPressed = 0, // default
                JoinPlayersWhenJoinActionIsTriggered = 1,
                JoinPlayersManually = 2
            }

            public InputEditorAnalytics.PlayerNotificationBehavior behavior;
            public PlayerJoinBehavior join_behavior;
            public bool joining_enabled_by_default;
            public int max_player_count;

            public Data(PlayerInputManager value)
            {
                behavior = InputEditorAnalytics.ToNotificationBehavior(value.notificationBehavior);
                join_behavior = ToPlayerJoinBehavior(value.joinBehavior);
                joining_enabled_by_default = value.joiningEnabled;
                max_player_count = value.maxPlayerCount;
            }

            private static PlayerJoinBehavior ToPlayerJoinBehavior(UnityEngine.InputSystem.PlayerJoinBehavior value)
            {
                switch (value)
                {
                    case UnityEngine.InputSystem.PlayerJoinBehavior.JoinPlayersWhenButtonIsPressed:
                        return PlayerJoinBehavior.JoinPlayersWhenButtonIsPressed;
                    case UnityEngine.InputSystem.PlayerJoinBehavior.JoinPlayersWhenJoinActionIsTriggered:
                        return PlayerJoinBehavior.JoinPlayersWhenJoinActionIsTriggered;
                    case UnityEngine.InputSystem.PlayerJoinBehavior.JoinPlayersManually:
                        return PlayerJoinBehavior.JoinPlayersManually;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value));
                }
            }

            public bool Equals(Data other)
            {
                return behavior == other.behavior &&
                    join_behavior == other.join_behavior &&
                    joining_enabled_by_default == other.joining_enabled_by_default &&
                    max_player_count == other.max_player_count;
            }

            public override bool Equals(object obj)
            {
                return obj is Data other && Equals(other);
            }

            public override int GetHashCode()
            {
                // Note: Not using HashCode.Combine since not available in older C# versions
                var hashCode = behavior.GetHashCode();
                hashCode = (hashCode * 397) ^ join_behavior.GetHashCode();
                hashCode = (hashCode * 397) ^ joining_enabled_by_default.GetHashCode();
                hashCode = (hashCode * 397) ^ max_player_count.GetHashCode();
                return hashCode;
            }
        }
    }
}
#endif
