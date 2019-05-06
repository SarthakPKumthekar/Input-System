#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.InputSystem.Editor;
#endif

namespace UnityEngine.InputSystem.Interactions
{
    /// <summary>
    /// Interaction that requires multiple taps (press and release within <see cref="tapTime"/>) spaced no more
    /// than <see cref="tapDelay"/> seconds apart.
    /// </summary>
    /// <remarks>
    /// The interaction goes into <see cref="InputActionPhase.Started"/> on the first press and then will not
    /// trigger again until either the full tap sequence is performed (in which case the interaction triggers
    /// <see cref="InputActionPhase.Performed"/>) or the multi-tap is aborted by a timeout being hit (in which
    /// case the interaction will trigger <see cref="InputActionPhase.Cancelled"/>).
    /// </remarks>
    public class MultiTapInteraction : IInputInteraction<float>
    {
        [Tooltip("The maximum time (in seconds) allowed to elapse between pressing and releasing a control for it to register as a tap.")]
        public float tapTime;

        [Tooltip("The maximum delay (in seconds) allowed between each tap. If this time is exceeded, the multi-tap is cancelled.")]
        public float tapDelay;

        [Tooltip("How many taps need to be performed in succession. Two means double-tap, three means triple-tap, and so on.")]
        public int tapCount = 2;

        public float pressPoint;

        private float tapTimeOrDefault => tapTime > 0.0 ? tapTime : InputSystem.settings.defaultTapTime;
        internal float tapDelayOrDefault => tapDelay > 0.0 ? tapDelay : tapTimeOrDefault * 2;
        private float pressPointOrDefault => pressPoint > 0 ? pressPoint : InputSystem.settings.defaultButtonPressPoint;

        public void Process(ref InputInteractionContext context)
        {
            if (context.timerHasExpired)
            {
                // We use timers multiple times but no matter what, if they expire it means
                // that we didn't get input in time.
                context.Cancelled();
                return;
            }

            switch (m_CurrentTapPhase)
            {
                case TapPhase.None:
                    if (context.ControlIsActuated(pressPointOrDefault))
                    {
                        m_CurrentTapPhase = TapPhase.WaitingForNextRelease;
                        m_CurrentTapStartTime = context.time;
                        context.Started();
                        context.SetTimeout(tapTimeOrDefault);
                    }
                    break;

                case TapPhase.WaitingForNextRelease:
                    if (!context.ControlIsActuated(pressPointOrDefault))
                    {
                        if (context.time - m_CurrentTapStartTime <= tapTimeOrDefault)
                        {
                            ++m_CurrentTapCount;
                            if (m_CurrentTapCount >= tapCount)
                            {
                                context.PerformedAndGoBackToWaiting();
                            }
                            else
                            {
                                m_CurrentTapPhase = TapPhase.WaitingForNextPress;
                                m_LastTapReleaseTime = context.time;
                                context.SetTimeout(tapDelayOrDefault);
                            }
                        }
                        else
                        {
                            context.Cancelled();
                        }
                    }
                    break;

                case TapPhase.WaitingForNextPress:
                    if (context.ControlIsActuated(pressPointOrDefault))
                    {
                        if (context.time - m_LastTapReleaseTime <= tapDelayOrDefault)
                        {
                            m_CurrentTapPhase = TapPhase.WaitingForNextRelease;
                            m_CurrentTapStartTime = context.time;
                            context.SetTimeout(tapTimeOrDefault);
                        }
                        else
                        {
                            context.Cancelled();
                        }
                    }
                    break;
            }
        }

        public void Reset()
        {
            m_CurrentTapPhase = TapPhase.None;
            m_CurrentTapCount = 0;
            m_CurrentTapStartTime = 0;
            m_LastTapReleaseTime = 0;
        }

        private TapPhase m_CurrentTapPhase;
        private int m_CurrentTapCount;
        private double m_CurrentTapStartTime;
        private double m_LastTapReleaseTime;

        private enum TapPhase
        {
            None,
            WaitingForNextRelease,
            WaitingForNextPress,
        }
    }

    #if UNITY_EDITOR
    /// <summary>
    /// UI that is displayed when editing <see cref="HoldInteraction"/> in the editor.
    /// </summary>
    internal class MultiTapInteractionEditor : InputParameterEditor<MultiTapInteraction>
    {
        protected override void OnEnable()
        {
            m_TapTimeSetting.Initialize("Max Tap Duration",
                "Time (in seconds) within with a control has to be released again for it to register as a tap. If the control is held "
                + "for longer than this time, the tap is cancelled.",
                "Default Tap Time",
                () => target.tapTime, x => target.tapTime = x, () => InputSystem.settings.defaultTapTime);
            m_TapDelaySetting.Initialize("Max Tap Spacing",
                "The maximum delay (in seconds) allowed between each tap. If this time is exceeded, the multi-tap is cancelled.",
                "Default Tap Spacing",
                () => target.tapDelay, x => target.tapDelay = x, () => target.tapDelayOrDefault,
                defaultComesFromInputSettings: false);
            m_PressPointSetting.Initialize("Press Point",
                "The amount of actuation a control requires before being considered pressed. If not set, default to "
                + "'Default Button Press Point' in the global input settings.",
                "Default Button Press Point",
                () => target.pressPoint, v => target.pressPoint = v,
                () => InputSystem.settings.defaultButtonPressPoint);
        }

        public override void OnGUI()
        {
            target.tapCount = EditorGUILayout.IntField(m_TapCountLabel, target.tapCount);
            m_TapDelaySetting.OnGUI();
            m_TapTimeSetting.OnGUI();
            m_PressPointSetting.OnGUI();
        }

        private readonly GUIContent m_TapCountLabel = new GUIContent("Tap Count", "How many taps need to be performed in succession. Two means double-tap, three means triple-tap, and so on.");

        private CustomOrDefaultSetting m_PressPointSetting;
        private CustomOrDefaultSetting m_TapTimeSetting;
        private CustomOrDefaultSetting m_TapDelaySetting;
    }
    #endif
}
