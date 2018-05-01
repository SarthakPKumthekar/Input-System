using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Experimental.Input.Utilities;

////TODO: split off resolution code

namespace UnityEngine.Experimental.Input
{
    /// <summary>
    /// A set of input actions and bindings.
    /// </summary>
    /// <remarks>
    /// Also stores data for actions. All actions have to have an associated
    /// action set. "Lose" actions constructed without a set will internally
    /// create their own "set" to hold their data.
    ///
    /// A common usage pattern for action sets is to use them to group action
    /// "contexts". So one set could hold "menu" actions, for example, whereas
    /// another set holds "gameplay" actions. This kind of splitting can be
    /// made arbitrarily complex. Like, you could have separate "driving" and
    /// "walking" action sets, for example, that you enable and disable depending
    /// on whether the player is walking or driving around.
    /// </remarks>
    [Serializable]
    public class InputActionSet : ICloneable
    {
        /// <summary>
        /// Name of the action set.
        /// </summary>
        public string name
        {
            get { return m_Name; }
        }

        /// <summary>
        /// Whether any action in the set is currently enabled.
        /// </summary>
        public bool enabled
        {
            get { return m_EnabledActionsCount > 0; }
        }

        /// <summary>
        /// List of actions contained in the set.
        /// </summary>
        /// <remarks>
        /// Actions are owned by their set. The same action cannot appear in multiple sets.
        ///
        /// Does not allocate. Note that values returned by the property become invalid if
        /// the setup of actions in a set is changed.
        /// </remarks>
        public ReadOnlyArray<InputAction> actions
        {
            get { return new ReadOnlyArray<InputAction>(m_Actions); }
        }

        /// <summary>
        /// List of bindings in the set.
        /// </summary>
        /// <remarks>
        /// <see cref="InputBinding">InputBindings</see> are owned by action sets and not by individual
        /// actions. The bindings in a set can form a tree and conceptually, this array represents a depth-first
        /// traversal of the tree.
        /// </remarks>
        public ReadOnlyArray<InputBinding> bindings
        {
            get { return new ReadOnlyArray<InputBinding>(m_Bindings); }
        }

        public InputActionSet(string name = null)
        {
            m_Name = name;
        }

        ////TODO: move to InputActionSyntax
        ////TODO: remove binding arguments and make this return a syntax struct
        public InputAction AddAction(string name, string binding = null, string modifiers = null, string groups = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Action must have name", "name");
            if (TryGetAction(name) != null)
                throw new InvalidOperationException(
                    string.Format("Cannot add action with duplicate name '{0}' to set '{1}'", name, this.name));

            var action = new InputAction(name);
            ArrayHelpers.Append(ref m_Actions, action);
            action.m_ActionSet = this;

            if (!string.IsNullOrEmpty(binding))
                action.AppendBinding(binding, modifiers: modifiers, groups: groups);

            return action;
        }

        public InputAction TryGetAction(InternedString name)
        {
            ////REVIEW: have transient lookup table? worth optimizing this?

            if (m_Actions == null)
                return null;

            var actionCount = m_Actions.Length;
            for (var i = 0; i < actionCount; ++i)
                if (m_Actions[i].m_Name == name)
                    return m_Actions[i];

            return null;
        }

        public InputAction TryGetAction(string name)
        {
            var internedName = new InternedString(name);
            return TryGetAction(internedName);
        }

        public InputAction GetAction(string name)
        {
            var action = TryGetAction(name);
            if (action == null)
                throw new KeyNotFoundException(string.Format("Could not find action '{0}' in set '{1}'", name,
                        this.name));
            return action;
        }

        /// <summary>
        /// Enable all the actions in the set.
        /// </summary>
        public void Enable()
        {
            if (m_Actions == null || m_EnabledActionsCount == m_Actions.Length)
                return;

            for (var i = 0; i < m_Actions.Length; ++i)
                m_Actions[i].Enable();

            Debug.Assert(m_EnabledActionsCount == m_Actions.Length);
        }

        /// <summary>
        /// Disable all the actions in the set.
        /// </summary>
        public void Disable()
        {
            if (m_Actions == null || !enabled)
                return;

            for (var i = 0; i < m_Actions.Length; ++i)
                m_Actions[i].Disable();

            Debug.Assert(m_EnabledActionsCount == 0);
        }

        //?????
        public void EnableGroup(string group)
        {
            throw new NotImplementedException();
        }

        public void DisableGroup(string group)
        {
            throw new NotImplementedException();
        }

        public void ApplyOverrides(IEnumerable<InputBindingOverride> overrides)
        {
            if (enabled)
                throw new InvalidOperationException(
                    string.Format("Cannot change overrides on set '{0}' while the action is enabled", this.name));

            foreach (var binding in overrides)
            {
                var action = TryGetAction(binding.action);
                if (action == null)
                    continue;
                action.ApplyBindingOverride(binding);
            }
        }

        public void RemoveOverrides(IEnumerable<InputBindingOverride> overrides)
        {
            if (enabled)
                throw new InvalidOperationException(
                    string.Format("Cannot change overrides on set '{0}' while the action is enabled", this.name));

            foreach (var binding in overrides)
            {
                var action = TryGetAction(binding.action);
                if (action == null)
                    continue;
                action.RemoveBindingOverride(binding);
            }
        }

        // Restore all bindings on all actions in the set to their defaults.
        public void RemoveAllOverrides()
        {
            if (enabled)
                throw new InvalidOperationException(
                    string.Format("Cannot removed overrides from set '{0}' while the action is enabled", this.name));

            for (int i = 0; i < m_Actions.Length; ++i)
            {
                m_Actions[i].RemoveAllBindingOverrides();
            }
        }

        public int GetOverrides(List<InputBindingOverride> overrides)
        {
            throw new NotImplementedException();
        }

        ////REVIEW: right now the Clone() methods aren't overridable; do we want that?
        public InputActionSet Clone()
        {
            // Internal action sets from singleton actions should not be visible outside of
            // them. Cloning them is not allowed.
            if (m_SingletonAction != null)
                throw new InvalidOperationException(
                    string.Format("Cloning internal set of singleton action '{0}' is not allowed", m_SingletonAction));

            var clone = new InputActionSet
            {
                m_Name = m_Name,
                m_Actions = ArrayHelpers.Clone(m_Actions)
            };

            return clone;
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        [SerializeField] private string m_Name;////REVIEW: InternedString?

        /// <summary>
        /// List of actions in this set.
        /// </summary>
        [SerializeField] internal InputAction[] m_Actions;

        /// <summary>
        /// List of bindings in this set.
        /// </summary>
        /// <remarks>
        /// For singleton actions, we ensure this is always the same as <see cref="InputAction.m_SingletonActionBindings"/>.
        /// </remarks>
        [SerializeField] internal InputBinding[] m_Bindings;

        // These fields are caches. If m_Bindings is modified, these are thrown away
        // and re-computed only if needed.
        // NOTE: Because InputBindings are structs, m_BindingsForEachAction actually duplicates each binding
        //       (only in the case where m_Bindings has scattered references to actions).
        ////REVIEW: this will lead to problems when overrides are thrown into the mix
        [NonSerialized] internal InputBinding[] m_BindingsForEachAction;
        [NonSerialized] internal InputAction[] m_ActionForEachBinding;

        [NonSerialized] internal InputControl[] m_Controls;
        [NonSerialized] internal ModifierState[] m_Modifiers;
        [NonSerialized] internal object[] m_Composites;
        [NonSerialized] internal BindingState[] m_ResolvedBindings;

        // Action sets that are created internally by singleton actions to hold their data
        // are never exposed and never serialized so there is no point allocating an m_Actions
        // array.
        [NonSerialized] internal InputAction m_SingletonAction;

        /// <summary>
        /// Records the current state of a single modifier attached to a binding.
        /// Each modifier keeps track of its own trigger control and phase progression.
        /// </summary>
        internal struct ModifierState
        {
            public IInputBindingModifier modifier;
            public InputControl control;
            public Flags flags;
            public double startTime;

            [Flags]
            public enum Flags
            {
                TimerRunning = 1 << 8, // Reserve first 8 bits for phase.
            }

            public bool isTimerRunning
            {
                get { return (flags & Flags.TimerRunning) == Flags.TimerRunning; }
                set
                {
                    if (value)
                        flags |= Flags.TimerRunning;
                    else
                        flags &= ~Flags.TimerRunning;
                }
            }

            public InputActionPhase phase
            {
                // We store the phase in the low 8 bits of the flags field.
                get { return (InputActionPhase)((int)flags & 0xf); }
                set { flags = (Flags)(((uint)flags & 0xfffffff0) | (uint)value); }
            }
        }

        /// <summary>
        /// Runtime state for a single binding.
        /// </summary>
        /// <remarks>
        /// Correlated to the <see cref="InputBinding"/> it corresponds to by the index in the binding
        /// array.
        /// </remarks>
        internal struct BindingState
        {
            [Flags]
            public enum Flags
            {
                ChainsWithNext = 1 << 0,
                EndOfChain = 1 << 1,
                PartOfComposite = 1 << 2,
            }

            /// <summary>
            /// Controls that the binding resolved to.
            /// </summary>
            public ReadOnlyArray<InputControl> controls;

            /// <summary>
            /// State of modifiers applied to the binding.
            /// </summary>
            public ReadWriteArray<ModifierState> modifiers;

            public Flags flags;
            public int compositeIndex;

            public bool chainsWithNext
            {
                get { return (flags & Flags.ChainsWithNext) == Flags.ChainsWithNext; }
                set
                {
                    if (value)
                        flags |= Flags.ChainsWithNext;
                    else
                        flags &= ~Flags.ChainsWithNext;
                }
            }

            public bool isEndOfChain
            {
                get { return (flags & Flags.EndOfChain) == Flags.EndOfChain; }
                set
                {
                    if (value)
                        flags |= Flags.EndOfChain;
                    else
                        flags &= ~Flags.EndOfChain;
                }
            }

            public bool isPartOfChain
            {
                get { return chainsWithNext || isEndOfChain; }
            }

            public bool isPartOfComposite
            {
                get { return (flags & Flags.PartOfComposite) == Flags.PartOfComposite; }
                set
                {
                    if (value)
                        flags |= Flags.PartOfComposite;
                    else
                        flags &= ~Flags.PartOfComposite;
                }
            }
        }

        /// <summary>
        /// Heart of the binding resolution machinery. Consumes InputActions and spits
        /// out a list of resolved bindings.
        /// </summary>
        private struct BindingResolver
        {
            public int controlCount;
            public int modifierCount;
            public int bindingCount;
            public int compositeCount;

            public InputControl[] controls;
            public ModifierState[] modifiers;
            public BindingState[] bindings;
            public object[] composites;

            private List<InputControlLayout.NameAndParameters> m_Parameters;

            /// <summary>
            /// Resolve the bindings of a single action and add their data to the given lists of
            /// controls, modifiers, and resolved bindings.
            /// </summary>
            /// <param name="action">Action whose bindings to resolve and add.</param>
            public void ResolveAndAddBindings(InputAction action)
            {
                var unresolvedBindings = action.bindings;
                if (unresolvedBindings.Count == 0)
                    return;

                var controlStartIndex = controlCount;
                var bindingsStartIndex = bindingCount;

                object currentComposite = null;
                var currentCompositeIndex = -1;

                ////TODO: handle case where we have bindings resolving to the same control
                ////      (not so clear cut what to do there; each binding may have a different modifier setup, for example)
                for (var n = 0; n < unresolvedBindings.Count; ++n)
                {
                    var unresolvedBinding = unresolvedBindings[n];
                    var indexOfFirstControlInThisBinding = controlCount;

                    ////TODO: allow specifying parameters for composite on its path (same way as parameters work for modifiers)
                    // If it's the start of a composite chain, create the composite.
                    if (unresolvedBinding.isComposite)
                    {
                        // Instantiate. For composites, the path is the name of the composite.
                        currentComposite = InstantiateBindingComposite(unresolvedBinding.path);
                        currentCompositeIndex = compositeCount;

                        // The composite binding entry itself does not resolve to any controls.
                        // It creates a composite binding object which is then populated from
                        // subsequent bindings.
                        continue;
                    }

                    // If we've reached the end of a composite chain, finish
                    // of the current composite.
                    if (!unresolvedBinding.isPartOfComposite && currentComposite != null)
                    {
                        FinishBindingComposite(currentComposite);
                        currentComposite = null;
                        currentCompositeIndex = -1;
                    }

                    // Use override path but fall back to default path if no
                    // override set.
                    var path = unresolvedBinding.overridePath ?? unresolvedBinding.path;

                    // Look up controls.
                    if (controls == null)
                        controls = new InputControl[10];
                    var resolvedControls = new ArrayOrListWrapper<InputControl>(controls, controlCount);
                    var numControls = InputSystem.GetControls(path, ref resolvedControls);
                    if (numControls == 0)
                        continue;

                    controlCount = resolvedControls.count;
                    controls = resolvedControls.array;

                    // Instantiate modifiers.
                    var firstModifier = 0;
                    var numModifiers = 0;
                    if (!string.IsNullOrEmpty(unresolvedBinding.modifiers))
                    {
                        firstModifier = ResolveModifiers(unresolvedBinding.modifiers);
                        if (modifiers != null)
                            numModifiers = modifierCount - firstModifier;
                    }

                    // Add entry for resolved binding.
                    ArrayHelpers.AppendWithCapacity(ref bindings, ref bindingCount, new BindingState
                    {
                        controls = new ReadOnlyArray<InputControl>(null, indexOfFirstControlInThisBinding, numControls),
                        modifiers = new ReadWriteArray<ModifierState>(null, firstModifier, numModifiers),
                        isPartOfComposite = unresolvedBinding.isPartOfComposite,
                        compositeIndex = currentCompositeIndex,
                    });

                    // If the binding is part of a composite, pass the resolve controls
                    // on to the composite.
                    if (unresolvedBinding.isPartOfComposite && currentComposite != null)
                    {
                        ////REVIEW: what should we do when a single binding in a composite resolves to multiple controls?
                        ////        if the composite has more than one bindable control, it's not readily apparent how we would group them
                        if (numControls > 1)
                            throw new NotImplementedException("Handling case where single binding in composite resolves to multiple controls");

                        // Make sure the binding is named. The name determines what in the composite
                        // to bind to.
                        if (string.IsNullOrEmpty(unresolvedBinding.name))
                            throw new Exception(string.Format(
                                    "Binding that is part of composite '{0}' is missing a name", currentComposite));

                        // Install the control on the binding.
                        BindControlInComposite(currentComposite, unresolvedBinding.name,
                            controls[indexOfFirstControlInThisBinding]);
                    }
                }

                if (currentComposite != null)
                    FinishBindingComposite(currentComposite);

                // Let action know where its control and resolved binding entries are.
                action.m_Controls =
                    new ReadOnlyArray<InputControl>(null, controlStartIndex, controlCount - controlStartIndex);
                action.m_ResolvedBindings =
                    new ReadOnlyArray<BindingState>(null, bindingsStartIndex, bindingCount - bindingsStartIndex);
            }

            private int ResolveModifiers(string modifierString)
            {
                ////REVIEW: We're piggybacking off the processor parsing here as the two syntaxes are identical. Might consider
                ////        moving the logic to a shared place.
                ////        Alternatively, may split the paths. May help in getting rid of unnecessary allocations.

                var firstModifierIndex = modifierCount;

                if (InputControlLayout.ParseNameAndParameterList(modifierString, ref m_Parameters))
                {
                    for (var i = 0; i < m_Parameters.Count; ++i)
                    {
                        // Look up modifier.
                        var type = InputBindingModifier.s_Modifiers.LookupTypeRegisteration(m_Parameters[i].name);
                        if (type == null)
                            throw new Exception(string.Format(
                                    "No binding modifier with name '{0}' (mentioned in '{1}') has been registered", m_Parameters[i].name,
                                    modifierString));

                        // Instantiate it.
                        var modifier = Activator.CreateInstance(type) as IInputBindingModifier;
                        if (modifier == null)
                            throw new Exception(string.Format("Modifier '{0}' is not an IInputBindingModifier", m_Parameters[i].name));

                        // Pass parameters to it.
                        InputDeviceBuilder.SetParameters(modifier, m_Parameters[i].parameters);

                        // Add to list.
                        ArrayHelpers.AppendWithCapacity(ref modifiers, ref modifierCount,
                            new ModifierState
                        {
                            modifier = modifier,
                            phase = InputActionPhase.Waiting
                        });
                    }
                }

                return firstModifierIndex;
            }

            private void FinishBindingComposite(object composite)
            {
                ////TODO: check whether composite is fully initialized
                ArrayHelpers.AppendWithCapacity(ref composites, ref compositeCount, composite);
            }

            private static object InstantiateBindingComposite(string name)
            {
                // Look up.
                var type = InputBindingComposite.s_Composites.LookupTypeRegisteration(name);
                if (type == null)
                    throw new Exception(string.Format("No binding composite with name '{0}' has been registered",
                            name));

                // Instantiate.
                var instance = Activator.CreateInstance(type);
                ////REVIEW: typecheck for IInputBindingComposite? (at least in dev builds)

                return instance;
            }

            ////REVIEW: replace this with a method on the composite that receives the value?
            private static void BindControlInComposite(object composite, string name, InputControl control)
            {
                var type = composite.GetType();

                // Look up field.
                var field = type.GetField(name,
                        BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                    throw new Exception(string.Format("Cannot find public field '{0}' in binding composite '{1}' of type '{2}'",
                            name, composite, type));

                // Typecheck.
                if (!typeof(InputControl).IsAssignableFrom(field.FieldType))
                    throw new Exception(string.Format(
                            "Field '{0}' in binding composite '{1}' of type '{2}' is not an InputControl", name, composite,
                            type));

                field.SetValue(composite, control);
            }
        }

        ////TODO: when re-resolving, we need to preserve ModifierStates and not just reset them
        // Resolve all bindings to their controls and also add any action modifiers
        // from the bindings. The best way is for this to happen once for each action
        // set at the beginning of the game and to then enable and disable the sets
        // as needed. However, the system will also re-resolve bindings if the control
        // setup in the system changes (i.e. if devices are added or removed or if
        // layouts in the system are changed).
        internal void ResolveBindings()
        {
            if (m_Actions == null && m_SingletonAction == null)
                return;

            ////TODO: this codepath must be changed to not allocate! Must be possible to do .Enable() and Disable()
            ////      all the time during gameplay and not end up causing GC

            // Resolve all source paths.
            var resolver = new BindingResolver();
            if (m_SingletonAction != null)
            {
                resolver.ResolveAndAddBindings(m_SingletonAction);
            }
            else
            {
                for (var i = 0; i < m_Actions.Length; ++i)
                    resolver.ResolveAndAddBindings(m_Actions[i]);
            }

            // Grab final arrays.
            m_Controls = resolver.controls;
            m_Modifiers = resolver.modifiers;
            m_Composites = resolver.composites;
            m_ResolvedBindings = resolver.bindings;

            if (m_ResolvedBindings != null)
            {
                for (var i = 0; i < resolver.bindingCount; ++i)
                {
                    m_ResolvedBindings[i].controls.m_Array = m_Controls;
                    m_ResolvedBindings[i].modifiers.m_Array = m_Modifiers;
                }
            }

            // Patch up all the array references in the ReadOnlyArray structs.
            if (m_SingletonAction != null)
            {
                if (m_Controls != null)
                {
                    m_SingletonAction.m_Controls.m_Array = m_Controls;
                    m_SingletonAction.m_ResolvedBindings.m_Array = m_ResolvedBindings;
                }
            }
            else
            {
                for (var i = 0; i < m_Actions.Length; ++i)
                {
                    var action = m_Actions[i];
                    action.m_Controls.m_Array = m_Controls;
                    action.m_ResolvedBindings.m_Array = m_ResolvedBindings;
                }
            }
        }

        // We don't want to explicitly keep track of enabled actions as that will most likely be bookkeeping
        // that isn't used most of the time. However, we do want to be able to find all enabled actions. So,
        // instead we just link all action sets that have enabled actions together in a list that has its link
        // embedded right here in an action set.
        private static InputActionSet s_FirstSetInGlobalList;
        [NonSerialized] private int m_EnabledActionsCount;
        [NonSerialized] internal InputActionSet m_NextInGlobalList;
        [NonSerialized] internal InputActionSet m_PreviousInGlobalList;

        #if UNITY_EDITOR
        ////REVIEW: not sure yet whether this warrants a publicly accessible callback so keeping it a private hook for now
        internal static List<Action> s_OnEnabledActionsChanged;
        #endif

        internal static void ResetGlobals()
        {
            for (var set = s_FirstSetInGlobalList; set != null;)
            {
                var next = set.m_NextInGlobalList;
                set.m_NextInGlobalList = null;
                set.m_PreviousInGlobalList = null;
                set.m_EnabledActionsCount = 0;
                if (set.m_SingletonAction != null)
                    set.m_SingletonAction.enabled = false;
                else
                {
                    for (var i = 0; i < set.m_Actions.Length; ++i)
                        set.m_Actions[i].enabled = false;
                }

                set = next;
            }
            s_FirstSetInGlobalList = null;
        }

        // Walk all sets with enabled actions and add all enabled actions to the given list.
        internal static int FindEnabledActions(List<InputAction> actions)
        {
            var numFound = 0;
            for (var set = s_FirstSetInGlobalList; set != null; set = set.m_NextInGlobalList)
            {
                if (set.m_SingletonAction != null)
                {
                    actions.Add(set.m_SingletonAction);
                }
                else
                {
                    for (var i = 0; i < set.m_Actions.Length; ++i)
                    {
                        var action = set.m_Actions[i];
                        if (!action.enabled)
                            continue;

                        actions.Add(action);
                        ++numFound;
                    }
                }
            }
            return numFound;
        }

        ////REVIEW: can we do better than just re-resolving *every* enabled action? seems heavy-handed
        internal static void RefreshAllEnabledActions()
        {
            for (var set = s_FirstSetInGlobalList; set != null; set = set.m_NextInGlobalList)
            {
                // First get rid of all state change monitors currently installed by
                // actions in the set.
                if (set.m_SingletonAction != null)
                {
                    var action = set.m_SingletonAction;
                    if (action.enabled)
                        action.UninstallStateChangeMonitors();
                }
                else
                {
                    for (var i = 0; i < set.m_Actions.Length; ++i)
                    {
                        var action = set.m_Actions[i];
                        if (action.enabled)
                            action.UninstallStateChangeMonitors();
                    }
                }

                // Now re-resolve all the bindings to update the control lists.
                set.ResolveBindings();

                // And finally, re-install state change monitors.
                if (set.m_SingletonAction != null)
                {
                    var action = set.m_SingletonAction;
                    if (action.enabled)
                        action.InstallStateChangeMonitors();
                }
                else
                {
                    for (var i = 0; i < set.m_Actions.Length; ++i)
                    {
                        var action = set.m_Actions[i];
                        if (action.enabled)
                            action.InstallStateChangeMonitors();
                    }
                }
            }
        }

        internal static void DisableAllEnabledActions()
        {
            for (var set = s_FirstSetInGlobalList; set != null;)
            {
                var next = set.m_NextInGlobalList;

                if (set.m_SingletonAction != null)
                    set.m_SingletonAction.Disable();
                else
                    set.Disable();

                set = next;
            }
            Debug.Assert(s_FirstSetInGlobalList == null);
        }

        internal void TellAboutActionChangingEnabledStatus(InputAction action, bool enable)
        {
            if (enable)
            {
                ++m_EnabledActionsCount;
                if (m_EnabledActionsCount == 1)
                {
                    if (s_FirstSetInGlobalList != null)
                        s_FirstSetInGlobalList.m_PreviousInGlobalList = this;
                    m_NextInGlobalList = s_FirstSetInGlobalList;
                    s_FirstSetInGlobalList = this;
                }
            }
            else
            {
                --m_EnabledActionsCount;
                if (m_EnabledActionsCount == 0)
                {
                    if (m_NextInGlobalList != null)
                        m_NextInGlobalList.m_PreviousInGlobalList = m_PreviousInGlobalList;
                    if (m_PreviousInGlobalList != null)
                        m_PreviousInGlobalList.m_NextInGlobalList = m_NextInGlobalList;
                    if (s_FirstSetInGlobalList == this)
                        s_FirstSetInGlobalList = m_NextInGlobalList;
                    m_NextInGlobalList = null;
                    m_PreviousInGlobalList = null;
                }
            }

            #if UNITY_EDITOR
            if (s_OnEnabledActionsChanged != null)
                foreach (var listener in s_OnEnabledActionsChanged)
                    listener();
            #endif
        }

        /// <summary>
        /// Return the list of bindings for just the given actions.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        /// <remarks>
        /// The bindings for a single action may be contiguous in <see cref="m_Bindings"/> or may be scattered
        /// around. We don't keep persistent storage for these and instead set up a transient
        /// array if and when bindings are queried directly from an action. In the simple case,
        /// we don't even need a separate array but rather just need to find out which slice in the
        /// bindings array corresponds to which action.
        ///
        /// NOTE: Bindings for individual actions aren't queried by the system itself during normal
        ///       runtime operation so we only do this for cases where the user asks for the
        ///       information.
        /// </remarks>
        internal ReadOnlyArray<InputBinding> GetBindingsForAction(InputAction action)
        {
            Debug.Assert(action != null);
            Debug.Assert(action.m_ActionSet == this);

            // See if we need to refresh.
            if (m_BindingsForEachAction == null)
            {
                // Handle case where we don't have any bindings.
                if (m_Bindings == null)
                    return new ReadOnlyArray<InputBinding>();

                if (action.isSingletonAction)
                {
                    // Dead simple case: set is internally owned by action. The entire
                    // list of bindings is specific to the action.

                    Debug.Assert(m_Bindings == action.m_SingletonActionBindings);

                    m_BindingsForEachAction = m_Bindings;
                    m_ActionForEachBinding = null; // No point in having this for singleton actions.

                    action.m_BindingsStartIndex = 0;
                    action.m_BindingsCount = m_Bindings != null ? m_Bindings.Length : 0;
                }
                else
                {
                    // Go through all bindings and slice them out to individual actions.

                    Debug.Assert(m_Actions != null); // Action isn't a singleton so this has to be true.

                    // Allocate array to retain resolved actions, if need be.
                    var totalBindingsCount = m_Bindings.Length;
                    if (m_ActionForEachBinding == null || m_ActionForEachBinding.Length != totalBindingsCount)
                        m_ActionForEachBinding = new InputAction[totalBindingsCount];

                    // Reset state on each action. Important we have actions that are no longer
                    // referred to by bindings.
                    for (var i = 0; i < m_Actions.Length; ++i)
                    {
                        m_Actions[i].m_BindingsCount = 0;
                        m_Actions[i].m_BindingsStartIndex = 0;
                    }

                    // Collect actions and count bindings.
                    // After this loop, we can have one of two situations:
                    // 1) The bindings for any action X start at some index N and occupy the next m_BindingsCount slots.
                    // 2) The bindings for some or all actions are scattered across non-contiguous chunks of the array.
                    for (var i = 0; i < m_Bindings.Length; ++i)
                    {
                        // Look up action.
                        var actionForBinding = TryGetAction(m_Bindings[i].action);
                        m_ActionForEachBinding[i] = actionForBinding;
                        if (actionForBinding == null)
                            continue;

                        ++actionForBinding.m_BindingsCount;
                    }

                    // Collect the bindings and bundle them into chunks.
                    var newBindingsArrayIndex = 0;
                    InputBinding[] newBindingsArray = null;
                    for (var sourceBindingIndex = 0; sourceBindingIndex < m_Bindings.Length;)
                    {
                        var currentAction = m_ActionForEachBinding[sourceBindingIndex];
                        if (currentAction == null || currentAction.m_BindingsStartIndex != 0)
                        {
                            // Skip bindings not targeting an action or bindings whose actions we
                            // have already processed (when gathering bindings for a single actions scattered
                            // across the array we may be skipping ahead).
                            ++sourceBindingIndex;
                            continue;
                        }

                        // Bindings for current action start at current index.
                        currentAction.m_BindingsStartIndex = newBindingsArray != null
                            ? newBindingsArrayIndex
                            : sourceBindingIndex;

                        // Collect all bindings for the action.
                        var actionBindingsCount = currentAction.m_BindingsCount;
                        for (var i = 0; i < actionBindingsCount; ++i)
                        {
                            var sourceBindingToCopy = sourceBindingIndex;

                            if (m_ActionForEachBinding[i] != currentAction)
                            {
                                // If this is the first action that has its bindings scattered around, switch to
                                // having a separate bindings array and copy whatever bindings we already processed
                                // over to it.
                                if (newBindingsArray == null)
                                {
                                    newBindingsArray = new InputBinding[totalBindingsCount];
                                    newBindingsArrayIndex = sourceBindingIndex;
                                    Array.Copy(m_Bindings, 0, newBindingsArray, 0, sourceBindingIndex);
                                }

                                // Find the next binding belonging to the action. We've counted bindings for
                                // the action in the previous pass so we know exactly how many bindings we
                                // can expect.
                                do
                                {
                                    ++i;
                                    Debug.Assert(i < m_ActionForEachBinding.Length);
                                }
                                while (m_ActionForEachBinding[i] != currentAction);
                            }

                            // Copy binding over to new bindings array, if need be.
                            if (newBindingsArray != null)
                                newBindingsArray[newBindingsArrayIndex++] = m_Bindings[sourceBindingToCopy];
                        }
                    }

                    if (newBindingsArray == null)
                        m_BindingsForEachAction = m_Bindings;
                    else
                        m_BindingsForEachAction = newBindingsArray;
                }
            }

            return new ReadOnlyArray<InputBinding>(m_BindingsForEachAction, action.m_BindingsStartIndex, action.m_BindingsCount);
        }

        [Serializable]
        public struct BindingJson
        {
            public string name;
            public string path;
            public string modifiers;
            public string groups;
            public bool chainWithPrevious;
            public bool isComposite;
            public bool isPartOfComposite;

            public InputBinding ToBinding()
            {
                return new InputBinding
                {
                    name = string.IsNullOrEmpty(name) ? null : name,
                    path = string.IsNullOrEmpty(path) ? null : path,
                    modifiers = string.IsNullOrEmpty(modifiers) ? null : modifiers,
                    group = string.IsNullOrEmpty(groups) ? null : groups,
                    chainWithPrevious = chainWithPrevious,
                    isComposite = isComposite,
                    isPartOfComposite = isPartOfComposite,
                };
            }

            public static BindingJson FromBinding(InputBinding binding)
            {
                return new BindingJson
                {
                    name = binding.name,
                    path = binding.path,
                    modifiers = binding.modifiers,
                    groups = binding.group,
                    chainWithPrevious = binding.chainWithPrevious,
                    isComposite = binding.isComposite,
                    isPartOfComposite = binding.isPartOfComposite,
                };
            }
        }

        [Serializable]
        private struct ActionJson
        {
            public string name;
            public BindingJson[] bindings;

            // ToAction doesn't make sense because all bindings combine on the action set and
            // thus need conversion logic that operates on the actions in bulk.

            public static ActionJson FromAction(InputAction action)
            {
                var bindings = action.bindings;
                var bindingsCount = bindings.Count;
                var bindingsJson = new BindingJson[bindingsCount];

                for (var i = 0; i < bindingsCount; ++i)
                {
                    bindingsJson[i] = BindingJson.FromBinding(bindings[i]);
                }

                return new ActionJson
                {
                    name = action.name,
                    bindings = bindingsJson,
                };
            }
        }

        ////TODO: this needs to be updated to be in sync with the binding refactor
        // A JSON represention of one or more sets of actions.
        // Contains a list of actions. Each action may specify the set it belongs to
        // as part of its name ("set/action").
        [Serializable]
        private struct ActionFileJson
        {
            public ActionJson[] actions;

            public InputActionSet[] ToSets()
            {
                var sets = new List<InputActionSet>();

                var actions = new List<List<InputAction>>();
                var bindings = new List<List<InputBinding>>();

                var actionCount = this.actions != null ? this.actions.Length : 0;
                for (var i = 0; i < actionCount; ++i)
                {
                    var jsonAction = this.actions[i];

                    if (string.IsNullOrEmpty(jsonAction.name))
                        throw new Exception(string.Format("Action number {0} has no name", i + 1));

                    ////REVIEW: make sure all action names are unique?

                    // Determine name of action set.
                    string setName = null;
                    string actionName = jsonAction.name;
                    var indexOfFirstSlash = actionName.IndexOf('/');
                    if (indexOfFirstSlash != -1)
                    {
                        setName = actionName.Substring(0, indexOfFirstSlash);
                        actionName = actionName.Substring(indexOfFirstSlash + 1);

                        if (string.IsNullOrEmpty(actionName))
                            throw new Exception(string.Format(
                                    "Invalid action name '{0}' (missing action name after '/')", jsonAction.name));
                    }

                    // Try to find existing set.
                    InputActionSet set = null;
                    var setIndex = 0;
                    for (; setIndex < sets.Count; ++setIndex)
                    {
                        if (string.Compare(sets[setIndex].name, setName, StringComparison.InvariantCultureIgnoreCase) == 0)
                        {
                            set = sets[setIndex];
                            break;
                        }
                    }

                    // Create new set if it's the first action in the set.
                    if (set == null)
                    {
                        set = new InputActionSet(setName);
                        sets.Add(set);
                        actions.Add(new List<InputAction>());
                        bindings.Add(new List<InputBinding>());
                    }

                    // Create action.
                    var action = new InputAction(actionName);
                    actions[setIndex].Add(action);

                    // Add bindings.
                    if (jsonAction.bindings != null)
                    {
                        var bindingsForSet = bindings[setIndex];
                        var bindingsStartIndex = bindingsForSet.Count;

                        for (var n = 0; n < jsonAction.bindings.Length; ++n)
                        {
                            var jsonBinding = jsonAction.bindings[n];
                            var binding = jsonBinding.ToBinding();
                            bindingsForSet.Add(binding);
                        }

                        action.m_BindingsCount = bindingsForSet.Count - bindingsStartIndex;
                        action.m_BindingsStartIndex = bindingsStartIndex;
                    }
                }

                // Finalize arrays.
                for (var i = 0; i < sets.Count; ++i)
                {
                    var set = sets[i];

                    var actionArray = actions[i].ToArray();
                    var bindingArray = bindings[i].ToArray();

                    set.m_Actions = actionArray;
                    set.m_Bindings = bindingArray;

                    for (var n = 0; n < actionArray.Length; ++n)
                    {
                        var action = actionArray[n];
                        action.m_ActionSet = set;
                    }
                }

                return sets.ToArray();
            }

            public static ActionFileJson FromSet(InputActionSet set)
            {
                var actions = set.actions;
                var actionCount = actions.Count;
                var actionsJson = new ActionJson[actionCount];
                var haveSetName = !string.IsNullOrEmpty(set.name);

                for (var i = 0; i < actionCount; ++i)
                {
                    actionsJson[i] = ActionJson.FromAction(actions[i]);

                    if (haveSetName)
                        actionsJson[i].name = string.Format("{0}/{1}", set.name, actions[i].name);
                }

                return new ActionFileJson
                {
                    actions = actionsJson
                };
            }

            public static ActionFileJson FromSets(IEnumerable<InputActionSet> sets)
            {
                // Count total number of actions.
                var actionCount = 0;
                foreach (var set in sets)
                    actionCount += set.actions.Count;

                // Collect actions from all sets.
                var actionsJson = new ActionJson[actionCount];
                var actionIndex = 0;
                foreach (var set in sets)
                {
                    var haveSetName = !string.IsNullOrEmpty(set.name);
                    var actions = set.actions;

                    for (var i = 0; i < actions.Count; ++i)
                    {
                        actionsJson[actionIndex] = ActionJson.FromAction(actions[i]);

                        if (haveSetName)
                            actionsJson[actionIndex].name = string.Format("{0}/{1}", set.name, actions[i].name);

                        ++actionIndex;
                    }
                }

                return new ActionFileJson
                {
                    actions = actionsJson
                };
            }
        }

        // Load one or more action sets from JSON.
        public static InputActionSet[] FromJson(string json)
        {
            var fileJson = JsonUtility.FromJson<ActionFileJson>(json);
            return fileJson.ToSets();
        }

        public static string ToJson(IEnumerable<InputActionSet> sets)
        {
            var fileJson = ActionFileJson.FromSets(sets);
            return JsonUtility.ToJson(fileJson);
        }

        public string ToJson()
        {
            var fileJson = ActionFileJson.FromSet(this);
            return JsonUtility.ToJson(fileJson);
        }
    }
}
