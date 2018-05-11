using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.Experimental.Input.Utilities;

// This file is the workhorse of the action system. Binding resolution and action
// triggers all comes together here. Most of the rest of the action-related code
// is concerned with setting up data for processing here.

////TODO: get rid of references to InputManager

namespace UnityEngine.Experimental.Input
{
    /// <summary>
    /// A mapping of <see cref="InputBinding">input bindings</see> to <see cref="InputAction">
    /// input actions</see>.
    /// </summary>
    /// <remarks>
    /// Also stores data for actions. All actions have to have an associated
    /// action map. "Lose" actions constructed without a map will internally
    /// create their own map to hold their data.
    ///
    /// A common usage pattern for action maps is to use them to group action
    /// "contexts". So one map could hold "menu" actions, for example, whereas
    /// another set holds "gameplay" actions. This kind of splitting can be
    /// made arbitrarily complex. Like, you could have separate "driving" and
    /// "walking" action maps, for example, that you enable and disable depending
    /// on whether the player is walking or driving around.
    /// </remarks>
    [Serializable]
    public class InputActionMap : ICloneable, ISerializationCallbackReceiver
    {
        /// <summary>
        /// Name of the action map.
        /// </summary>
        public string name
        {
            get { return m_Name; }
        }

        ////REVIEW: how does this play with the shift towards bindings? what if there's no actions but there's bindings?
        /// <summary>
        /// Whether any action in the map is currently enabled.
        /// </summary>
        public bool enabled
        {
            get { return m_EnabledActionsCount > 0; }
        }

        /// <summary>
        /// List of actions contained in the map.
        /// </summary>
        /// <remarks>
        /// Actions are owned by their map. The same action cannot appear in multiple maps.
        ///
        /// Does not allocate. Note that values returned by the property become invalid if
        /// the setup of actions in a set is changed.
        /// </remarks>
        public ReadOnlyArray<InputAction> actions
        {
            get { return new ReadOnlyArray<InputAction>(m_Actions); }
        }

        /// <summary>
        /// List of bindings contained in the map.
        /// </summary>
        /// <remarks>
        /// <see cref="InputBinding">InputBindings</see> are owned by action maps and not by individual
        /// actions. The bindings in a map can form a tree and conceptually, this array represents a depth-first
        /// traversal of the tree.
        ///
        /// Bindings that trigger actions refer to the action by name.
        /// </remarks>
        public ReadOnlyArray<InputBinding> bindings
        {
            get { return new ReadOnlyArray<InputBinding>(m_Bindings); }
        }

        public InputActionMap(string name = null)
        {
            m_Name = name;
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
        /// Enable all the actions in the map.
        /// </summary>
        public void Enable()
        {
            if (m_Actions == null || m_EnabledActionsCount == m_Actions.Length)
                return;

            ResolveBindingsIfNecessary();
            m_State.EnableAllActions(this);
            m_EnabledActionsCount = m_Actions.Length;

            EnsureMapAddedToGlobalList();
            NotifyListenersEnabledActionsChanged();
        }

        /// <summary>
        /// Disable all the actions in the map.
        /// </summary>
        public void Disable()
        {
            if (!enabled)
                return;

            m_State.DisableAllActions(this);
            m_EnabledActionsCount = 0;

            RemoveMapFromGlobalList();
            NotifyListenersEnabledActionsChanged();
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
                    string.Format("Cannot change overrides on map '{0}' while actions in the map are enabled", name));

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
                    string.Format("Cannot remove overrides from map '{0}' while actions in the map are enabled", name));

            for (var i = 0; i < m_Actions.Length; ++i)
            {
                m_Actions[i].RemoveAllBindingOverrides();
            }
        }

        public int GetOverrides(List<InputBindingOverride> overrides)
        {
            throw new NotImplementedException();
        }

        ////REVIEW: right now the Clone() methods aren't overridable; do we want that?
        public InputActionMap Clone()
        {
            // Internal action sets from singleton actions should not be visible outside of
            // them. Cloning them is not allowed.
            if (m_SingletonAction != null)
                throw new InvalidOperationException(
                    string.Format("Cloning internal map of singleton action '{0}' is not allowed", m_SingletonAction));

            var clone = new InputActionMap
            {
                m_Name = m_Name,
                ////FIXME: this produces singleton actions! shouldn't call InputAction.Clone() and should set proper m_ActionSet references
                m_Actions = ArrayHelpers.Clone(m_Actions)
            };

            return clone;
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        #region Configuration Data

        // The state we persist is pretty much just a name, a flat list of actions, and a flat
        // list of bindings. The rest is state we keep at runtime when a map is in use.

        [SerializeField] private string m_Name;////REVIEW: InternedString?

        /// <summary>
        /// List of actions in this map.
        /// </summary>
        [SerializeField] internal InputAction[] m_Actions;

        /// <summary>
        /// List of bindings in this map.
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
        [NonSerialized] internal InputControl[] m_ControlsForEachAction;
        [NonSerialized] internal InputAction[] m_ActionForEachBinding;

        [NonSerialized] internal int m_EnabledActionsCount;

        // Action sets that are created internally by singleton actions to hold their data
        // are never exposed and never serialized so there is no point allocating an m_Actions
        // array.
        [NonSerialized] internal InputAction m_SingletonAction;

        ////REVIEW: make this also produce a list of controls?
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
        internal ReadOnlyArray<InputBinding> GetBindingsForSingleAction(InputAction action)
        {
            Debug.Assert(action != null);
            Debug.Assert(action.m_ActionMap == this);

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

        internal ReadOnlyArray<InputControl> GetControlsForSingleAction(InputAction action)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Execution Data

        [NonSerialized] internal int m_MapIndex = InputActionMapState.kInvalidIndex;

        /// <summary>
        /// Current execution state.
        /// </summary>
        /// <remarks>
        /// Initialized when map (or any action in it) is first enabled.
        /// </remarks>
        [NonSerialized] internal InputActionMapState m_State;

        internal void ResolveBindingsIfNecessary()
        {
            if (m_MapIndex == InputActionMapState.kInvalidIndex)
                ResolveBindings();
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
            Debug.Assert(m_State == null);

            if (m_Bindings == null)
                return;

            // Resolve all source paths.
            var resolver = new InputBindingResolver();
            resolver.AddMap(this);

            // Transfer final arrays into state.
            m_State = new InputActionMapState();
            m_State.Initialize(resolver);
        }

        #endregion

        #region Global State

        ////REVIEW: do we want to have a global InputActionManager after all?

        ////REVIEW: move this over to linking action map states instead of action maps themselves?
        // We don't want to explicitly keep track of enabled actions as that will most likely be bookkeeping
        // that isn't used most of the time. However, we do want to be able to find all enabled actions. So,
        // instead we just link all action sets that have enabled actions together in a list that has its link
        // embedded right here in an action set.
        private static InputActionMap s_FirstMapInGlobalList;
        [NonSerialized] internal InputActionMap m_NextMapInGlobalList;
        [NonSerialized] internal InputActionMap m_PreviousInGlobalList;

        #if UNITY_EDITOR
        ////REVIEW: not sure yet whether this warrants a publicly accessible callback so keeping it a private hook for now
        internal static List<Action> s_OnEnabledActionsChanged;
        #endif

        internal void EnsureMapAddedToGlobalList()
        {
            if (m_NextMapInGlobalList != null || m_PreviousInGlobalList != null || s_FirstMapInGlobalList == this)
                return;

            if (s_FirstMapInGlobalList != null)
                s_FirstMapInGlobalList.m_PreviousInGlobalList = this;
            m_NextMapInGlobalList = s_FirstMapInGlobalList;
            s_FirstMapInGlobalList = this;
        }

        internal void RemoveMapFromGlobalList()
        {
            if (m_NextMapInGlobalList == null && m_PreviousInGlobalList == null && s_FirstMapInGlobalList != this)
                return;

            if (m_NextMapInGlobalList != null)
                m_NextMapInGlobalList.m_PreviousInGlobalList = m_PreviousInGlobalList;
            if (m_PreviousInGlobalList != null)
                m_PreviousInGlobalList.m_NextMapInGlobalList = m_NextMapInGlobalList;
            if (s_FirstMapInGlobalList == this)
                s_FirstMapInGlobalList = m_NextMapInGlobalList;

            m_NextMapInGlobalList = null;
            m_PreviousInGlobalList = null;
        }

        internal void NotifyListenersEnabledActionsChanged()
        {
            #if UNITY_EDITOR
            if (s_OnEnabledActionsChanged != null)
                foreach (var listener in s_OnEnabledActionsChanged)
                    listener();
            #endif
        }

        internal static void ResetGlobals()
        {
            s_FirstMapInGlobalList = null;
        }

        ////TODO: this can be written in a much more efficient now using the state records
        // Walk all sets with enabled actions and add all enabled actions to the given list.
        internal static int FindEnabledActions(List<InputAction> actions)
        {
            var numFound = 0;
            for (var map = s_FirstMapInGlobalList; map != null; map = map.m_NextMapInGlobalList)
            {
                Debug.Assert(map.m_State != null);
                if (map.m_SingletonAction != null)
                {
                    actions.Add(map.m_SingletonAction);
                }
                else
                {
                    for (var i = 0; i < map.m_Actions.Length; ++i)
                    {
                        var action = map.m_Actions[i];
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
        internal static void ReResolveAllEnabledActions()
        {
            /*
            for (var map = s_FirstMapInGlobalList; map != null; map = map.m_NextMapInGlobalList)
            {
                // First get rid of all state change monitors currently installed by
                // actions in the set.
                if (map.m_SingletonAction != null)
                {
                    var action = map.m_SingletonAction;
                    if (action.enabled)
                        action.UninstallStateChangeMonitors();
                }
                else
                {
                    for (var i = 0; i < map.m_Actions.Length; ++i)
                    {
                        var action = map.m_Actions[i];
                        if (action.enabled)
                            action.UninstallStateChangeMonitors();
                    }
                }

                // Now re-resolve all the bindings to update the control lists.
                map.ResolveBindings();

                // And finally, re-install state change monitors.
                if (map.m_SingletonAction != null)
                {
                    var action = map.m_SingletonAction;
                    if (action.enabled)
                        action.InstallStateChangeMonitors();
                }
                else
                {
                    for (var i = 0; i < map.m_Actions.Length; ++i)
                    {
                        var action = map.m_Actions[i];
                        if (action.enabled)
                            action.InstallStateChangeMonitors();
                    }
                }
            }
            */
        }

        internal static void DisableAllEnabledActions()
        {
            for (var map = s_FirstMapInGlobalList; map != null;)
            {
                var next = map.m_NextMapInGlobalList;
                map.Disable();
                map = next;
            }
            Debug.Assert(s_FirstMapInGlobalList == null);
        }

        #endregion

        #region Serialization

        // Action maps are serialized in two different ways. For storage as imported assets in Unity's Library/ folder
        // and in player data and asset bundles as well as for surviving domain reloads, InputActionMaps are serialized
        // directly by Unity. For storage as source data in user projects, InputActionMaps are serialized indirectly
        // as JSON by setting up a separate set of structs that are then read and written using Unity's JSON serializer.

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

            public InputActionMap[] ToSets()
            {
                var sets = new List<InputActionMap>();

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
                    InputActionMap map = null;
                    var setIndex = 0;
                    for (; setIndex < sets.Count; ++setIndex)
                    {
                        if (string.Compare(sets[setIndex].name, setName, StringComparison.InvariantCultureIgnoreCase) == 0)
                        {
                            map = sets[setIndex];
                            break;
                        }
                    }

                    // Create new set if it's the first action in the set.
                    if (map == null)
                    {
                        map = new InputActionMap(setName);
                        sets.Add(map);
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
                        action.m_ActionMap = set;
                    }
                }

                return sets.ToArray();
            }

            public static ActionFileJson FromSet(InputActionMap map)
            {
                var actions = map.actions;
                var actionCount = actions.Count;
                var actionsJson = new ActionJson[actionCount];
                var haveSetName = !string.IsNullOrEmpty(map.name);

                for (var i = 0; i < actionCount; ++i)
                {
                    actionsJson[i] = ActionJson.FromAction(actions[i]);

                    if (haveSetName)
                        actionsJson[i].name = string.Format("{0}/{1}", map.name, actions[i].name);
                }

                return new ActionFileJson
                {
                    actions = actionsJson
                };
            }

            public static ActionFileJson FromSets(IEnumerable<InputActionMap> sets)
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
        public static InputActionMap[] FromJson(string json)
        {
            var fileJson = JsonUtility.FromJson<ActionFileJson>(json);
            return fileJson.ToSets();
        }

        public static string ToJson(IEnumerable<InputActionMap> sets)
        {
            var fileJson = ActionFileJson.FromSets(sets);
            return JsonUtility.ToJson(fileJson);
        }

        public string ToJson()
        {
            var fileJson = ActionFileJson.FromSet(this);
            return JsonUtility.ToJson(fileJson);
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            // Restore references of actions linking back to us.
            if (m_Actions != null)
            {
                var actionCount = m_Actions.Length;
                for (var i = 0; i < actionCount; ++i)
                    m_Actions[i].m_ActionMap = this;
            }
        }

        #endregion
    }
}
