using System;
using UnityEngine.Experimental.Input.LowLevel;

////REVIEW: should timer expiration be a separate method on IInputInteraction?

namespace UnityEngine.Experimental.Input
{
    /// <summary>
    /// Information passed to <see cref="IInputInteraction">interactions</see>
    /// when their associated controls trigger.
    /// </summary>
    /// <seealso cref="IInputInteraction.Process"/>
    public struct InputInteractionContext
    {
        /// <summary>
        /// The action associated with the binding.
        /// </summary>
        /// <remarks>
        /// If the binding is not associated with an action, this is <c>null</c>.
        /// </remarks>
        public InputAction action => m_State.GetActionOrNull(ref m_TriggerState);

        /// <summary>
        /// The bound control that changed its state to trigger the binding associated
        /// with the interaction.
        /// </summary>
        public InputControl control => m_State.GetControl(ref m_TriggerState);

        public InputActionPhase phase => m_TriggerState.phase;

        /// <summary>
        /// Time stamp of the input event that caused <see cref="control"/> to trigger a change in the
        /// state of <see cref="action"/>.
        /// </summary>
        /// <seealso cref="InputEvent.time"/>
        public double time => m_TriggerState.time;

        public double startTime => m_TriggerState.startTime;

        public bool timerHasExpired
        {
            get => (m_Flags & Flags.TimerHasExpired) == Flags.TimerHasExpired;
            internal set
            {
                if (value)
                    m_Flags |= Flags.TimerHasExpired;
                else
                    m_Flags &= ~Flags.TimerHasExpired;
            }
        }

        /// <summary>
        /// If true, <see cref="action"/> is set to continuous mode (<see cref="InputAction.continuous"/>).
        /// </summary>
        /// <remarks>
        /// In continuous mode, an action, while triggered, is expected to be performed even if there is
        /// no associated input in a given frame.
        /// </remarks>
        public bool continuous
        {
            get
            {
                var actionIndex = m_State.bindingStates[m_TriggerState.bindingIndex].actionIndex;
                return m_State.actionStates[actionIndex].continuous;
            }
        }

        /// <summary>
        /// True if the interaction is waiting for input
        /// </summary>
        /// <remarks>
        /// By default, an interaction will return this this phase after every time it has been performed
        /// (<see cref="InputActionPhase.Performed"/>). This can be changed by using <see cref="PerformedAndStayStarted"/>
        /// or <see cref="PerformedAndStayPerformed"/>.
        /// </remarks>
        /// <seealso cref="InputActionPhase.Waiting"/>
        public bool isWaiting => phase == InputActionPhase.Waiting;

        /// <summary>
        /// True if the interaction has been started.
        /// </summary>
        /// <seealso cref="InputActionPhase.Started"/>
        /// <seealso cref="Started"/>
        public bool isStarted => phase == InputActionPhase.Started;

        /// <summary>
        /// Return true if the control that triggered the interaction has been actuated beyond the given threshold.
        /// </summary>
        /// <param name="threshold">Threshold that must be reached for the control to be considered actuated. If this is zero,
        /// the threshold must be exceeded. If it is any positive value, the value must be at least matched.</param>
        /// <returns>True if the trigger control is actuated.</returns>
        /// <seealso cref="InputControlExtensions.IsActuated"/>
        public bool ControlIsActuated(float threshold = 0)
        {
            // If we're looking at the default threshold, see if we've already checked for control
            // actuation and avoid doing it more than once.
            if (threshold <= 0 && (m_Flags & Flags.ControlIsActuatedInitialized) != 0)
                return (m_Flags & Flags.ControlIsActuated) != 0;

            var isActuated = m_State.IsActuated(bindingIndex, controlIndex, threshold);

            // Remember the result if we checked with default threshold.
            if (threshold <= 0)
            {
                if (isActuated)
                    m_Flags |= Flags.ControlIsActuated;
                m_Flags |= Flags.ControlIsActuatedInitialized;
            }

            return isActuated;
        }

        /// <summary>
        /// Mark the interaction has having begun.
        /// </summary>
        /// <remarks>
        /// Note that this affects the current interaction only. There may be multiple interactions on a binding
        /// and arbitrary many interactions may concurrently be in started state. However, only one interaction
        /// (usually the one that starts first) is allowed to drive the action's state as a whole. If an interaction
        /// that is currently driving an action is cancelled, however, the next interaction in the list that has
        /// been started will take over and continue driving the action.
        ///
        /// <example>
        /// <code>
        /// public class MyInteraction : IInputInteraction&lt;float&gt;
        /// {
        ///     public void Process(ref IInputInteractionContext context)
        ///     {
        ///         if (context.isWaiting && context.ControlIsActuated())
        ///         {
        ///             // We've waited for input and got it. Start the interaction.
        ///             context.Started();
        ///         }
        ///         else if (context.isStarted && !context.ControlIsActuated())
        ///         {
        ///             // Interaction has been completed.
        ///             context.PerformedAndGoBackToWaiting();
        ///         }
        ///     }
        ///
        ///     public void Reset()
        ///     {
        ///         // No reset code needed. We're not keeping any state locally in the interaction.
        ///     }
        /// }
        /// </code>
        /// </example>
        /// </remarks>
        public void Started()
        {
            m_TriggerState.startTime = time;
            m_State.ChangePhaseOfInteraction(InputActionPhase.Started, ref m_TriggerState);
        }

        public void PerformedAndGoBackToWaiting()
        {
            m_State.ChangePhaseOfInteraction(InputActionPhase.Performed, ref m_TriggerState);
        }

        public void PerformedAndStayStarted()
        {
            m_State.ChangePhaseOfInteraction(InputActionPhase.Performed, ref m_TriggerState,
                phaseAfterPerformed: InputActionPhase.Started);
        }

        public void PerformedAndStayPerformed()
        {
            m_State.ChangePhaseOfInteraction(InputActionPhase.Performed, ref m_TriggerState,
                phaseAfterPerformed: InputActionPhase.Performed);
        }

        public void Cancelled()
        {
            m_State.ChangePhaseOfInteraction(InputActionPhase.Cancelled, ref m_TriggerState);
        }

        public void SetTimeout(float seconds)
        {
            m_State.StartTimeout(seconds, ref m_TriggerState);
        }

        public TValue ReadValue<TValue>()
            where TValue : struct
        {
            return m_State.ReadValue<TValue>(m_TriggerState.bindingIndex, m_TriggerState.controlIndex);
        }

        internal InputActionMapState m_State;
        internal Flags m_Flags;
        internal InputActionMapState.TriggerState m_TriggerState;

        internal int mapIndex => m_TriggerState.mapIndex;

        internal int controlIndex => m_TriggerState.controlIndex;

        internal int bindingIndex => m_TriggerState.bindingIndex;

        internal int interactionIndex => m_TriggerState.interactionIndex;

        [Flags]
        internal enum Flags
        {
            ControlIsActuated = 1 << 0,
            ControlIsActuatedInitialized = 1 << 1,
            TimerHasExpired = 1 << 4,
        }
    }
}
