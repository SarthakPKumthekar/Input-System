using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Experimental.Input.Utilities;

////REVIEW: what should we do if none of the actions referenced by bindings could be found?

namespace UnityEngine.Experimental.Input
{
    /// <summary>
    /// Heart of the binding resolution machinery. Consumes lists of bindings
    /// and spits out out a list of resolved bindings together with their needed
    /// execution state.
    /// </summary>
    /// <remarks>
    /// One or more <see cref="InputActionMap">action maps</see> can be added to the same
    /// resolver. The result is a combination of the binding state of all maps.
    ///
    /// The data set up by a resolver is for consumption by <see cref="InputActionMapState"/>.
    /// Essentially, InputBindingResolver does all the wiring and <see cref="InputActionMapState"/>
    /// does all the actual execution based on the resulting data.
    /// </remarks>
    /// <seealso cref="InputActionMapState.Initialize"/>
    internal struct InputBindingResolver
    {
        public int totalMapCount;
        public int totalActionCount;
        public int totalBindingCount;
        public int totalControlCount;
        public int totalModifierCount;
        public int totalCompositeCount;

        public InputActionMap[] maps;
        public InputControl[] controls;
        public InputActionMapState.ModifierState[] modifierStates;
        public InputActionMapState.BindingState[] bindingStates;
        public InputActionMapState.TriggerState[] actionStates;
        public IInputBindingModifier[] modifiers;
        public object[] composites;

        public InputActionMapState.ActionMapIndices[] mapIndices;
        public int[] controlIndexToBindingIndex;

        private List<InputControlLayout.NameAndParameters> m_Parameters;

        /// <summary>
        /// Resolve and add all bindings and actions from the given map.
        /// </summary>
        /// <param name="map"></param>
        /// <exception cref="Exception"></exception>
        public void AddMap(InputActionMap map)
        {
            Debug.Assert(map != null);
            Debug.Assert(map.m_MapIndex == InputActionMapState.kInvalidIndex);

            // Keep track of indices for this map.
            var bindingStartIndex = totalBindingCount;
            var controlStartIndex = totalControlCount;
            var modifierStartIndex = totalModifierCount;
            var compositeStartIndex = totalCompositeCount;
            var actionStartIndex = totalActionCount;

            // Allocate binding states.
            var bindingsInThisMap = map.m_Bindings;
            var bindingCountInThisMap = bindingsInThisMap != null ? bindingsInThisMap.Length : 0;
            totalBindingCount += bindingCountInThisMap;
            ArrayHelpers.GrowBy(ref bindingStates, totalBindingCount);

            ////TODO: make sure composite objects get all the bindings they need
            ////TODO: handle case where we have bindings resolving to the same control
            ////      (not so clear cut what to do there; each binding may have a different modifier setup, for example)
            var currentCompositeIndex = InputActionMapState.kInvalidIndex;
            var actionsInThisMap = map.m_Actions;
            var actionCountInThisMap = actionsInThisMap != null ? actionsInThisMap.Length : 0;
            for (var n = 0; n < bindingCountInThisMap; ++n)
            {
                var unresolvedBinding = bindingsInThisMap[n];

                // Try to find action.
                var actionIndex = InputActionMapState.kInvalidIndex;
                var actionName = unresolvedBinding.action;
                if (!actionName.IsEmpty())
                {
                    for (var i = 0; i < actionCountInThisMap; ++i)
                    {
                        var currentAction = actionsInThisMap[i];
                        if (currentAction.m_Name == actionName) // This is an InternedString comparison and not a full String comparison.
                        {
                            actionIndex = totalActionCount + i;
                            break;
                        }
                    }
                }
                else if (map.m_SingletonAction != null)
                {
                    // Special-case for singleton actions that don't have names.
                    actionIndex = 0;
                }

                ////TODO: allow specifying parameters for composite on its path (same way as parameters work for modifiers)
                // If it's the start of a composite chain, create the composite.
                if (unresolvedBinding.isComposite)
                {
                    ////REVIEW: what to do about modifiers on composites?

                    // Instantiate. For composites, the path is the name of the composite.
                    var composite = InstantiateBindingComposite(unresolvedBinding.path);
                    var compositeIndex =
                        ArrayHelpers.AppendWithCapacity(ref composites, ref totalCompositeCount, composite);
                    bindingStates[bindingStartIndex + n] = new InputActionMapState.BindingState
                    {
                        actionIndex = actionIndex,
                        compositeIndex = compositeIndex,
                    };

                    // The composite binding entry itself does not resolve to any controls.
                    // It creates a composite binding object which is then populated from
                    // subsequent bindings.
                    continue;
                }

                // If we've reached the end of a composite chain, finish
                // off the current composite.
                if (!unresolvedBinding.isPartOfComposite && currentCompositeIndex != InputActionMapState.kInvalidIndex)
                    currentCompositeIndex = InputActionMapState.kInvalidIndex;

                // Use override path but fall back to default path if no
                // override set.
                var path = unresolvedBinding.overridePath ?? unresolvedBinding.path;

                // Look up controls.
                var firstControlIndex = totalControlCount;
                if (controls == null)
                    controls = new InputControl[10];
                var resolvedControls = new ArrayOrListWrapper<InputControl>(controls, totalControlCount);
                var numControls = InputSystem.GetControls(path, ref resolvedControls);
                controls = resolvedControls.array;
                totalControlCount = resolvedControls.count;

                // Instantiate modifiers.
                var firstModifierIndex = 0;
                var numModifiers = 0;
                if (!string.IsNullOrEmpty(unresolvedBinding.modifiers))
                {
                    firstModifierIndex = ResolveModifiers(unresolvedBinding.modifiers);
                    if (modifierStates != null)
                        numModifiers = totalModifierCount - firstModifierIndex;
                }

                // Add entry for resolved binding.
                bindingStates[bindingStartIndex + n] = new InputActionMapState.BindingState
                {
                    controlStartIndex = firstControlIndex,
                    controlCount = numControls,
                    modifierStartIndex = firstModifierIndex,
                    modifierCount = numModifiers,
                    isPartOfComposite = unresolvedBinding.isPartOfComposite,
                    actionIndex = actionIndex,
                    compositeIndex = currentCompositeIndex
                };

                // If the binding is part of a composite, pass the resolve controls
                // on to the composite.
                if (unresolvedBinding.isPartOfComposite && currentCompositeIndex != InputActionMapState.kInvalidIndex && numControls != 0)
                {
                    ////REVIEW: what should we do when a single binding in a composite resolves to multiple controls?
                    ////        if the composite has more than one bindable control, it's not readily apparent how we would group them
                    if (numControls > 1)
                        throw new NotImplementedException("Handling case where single binding in composite resolves to multiple controls");

                    // Make sure the binding is named. The name determines what in the composite
                    // to bind to.
                    if (string.IsNullOrEmpty(unresolvedBinding.name))
                        throw new Exception(string.Format(
                                "Binding that is part of composite '{0}' is missing a name",
                                composites[currentCompositeIndex]));

                    // Install the control on the binding.
                    BindControlInComposite(composites[currentCompositeIndex], unresolvedBinding.name,
                        controls[firstControlIndex]);
                }
            }

            // Set up control to binding index mapping.
            var controlCountInThisMap = totalControlCount - controlStartIndex;
            ArrayHelpers.GrowBy(ref controlIndexToBindingIndex, controlCountInThisMap);
            for (var i = 0; i < bindingCountInThisMap; ++i)
            {
                var numControls = bindingStates[bindingStartIndex + i].controlCount;
                var startIndex = bindingStates[bindingStartIndex + i].controlStartIndex;
                for (var n = 0; n < numControls; ++n)
                    controlIndexToBindingIndex[startIndex + n] = i;
            }

            // Store indices for map.
            var numMaps = totalMapCount;
            var mapIndex = ArrayHelpers.AppendWithCapacity(ref maps, ref numMaps, map);
            ArrayHelpers.AppendWithCapacity(ref mapIndices, ref totalMapCount, new InputActionMapState.ActionMapIndices
            {
                actionStartIndex = actionStartIndex,
                actionCount = actionCountInThisMap,
                controlStartIndex = controlStartIndex,
                controlCount = controlCountInThisMap,
                bindingStartIndex = bindingStartIndex,
                bindingCount = bindingCountInThisMap,
                modifierStartIndex = modifierStartIndex,
                modifierCount = totalModifierCount - modifierStartIndex,
                compositeStartIndex = compositeStartIndex,
                compositeCount = totalCompositeCount - compositeStartIndex,
            });
            map.m_MapIndex = mapIndex;

            // Allocate action states.
            if (actionCountInThisMap > 0)
            {
                // Assign action indices.
                var actions = map.m_Actions;
                for (var i = 0; i < actionCountInThisMap; ++i)
                    actions[i].m_ActionIndex = totalActionCount + i;

                ArrayHelpers.GrowBy(ref actionStates, actionCountInThisMap);
                totalActionCount += actionCountInThisMap;
                for (var i = 0; i < actionCountInThisMap; ++i)
                    actionStates[i].mapIndex = mapIndex;
            }
        }

        private int ResolveModifiers(string modifierString)
        {
            ////REVIEW: We're piggybacking off the processor parsing here as the two syntaxes are identical. Might consider
            ////        moving the logic to a shared place.
            ////        Alternatively, may split the paths. May help in getting rid of unnecessary allocations.

            var firstModifierIndex = totalModifierCount;
            if (!InputControlLayout.ParseNameAndParameterList(modifierString, ref m_Parameters))
                return firstModifierIndex;

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
                var modifierStateCount = totalModifierCount;
                ArrayHelpers.AppendWithCapacity(ref modifierStates, ref modifierStateCount,
                    new InputActionMapState.ModifierState
                {
                    phase = InputActionPhase.Waiting
                });
                ArrayHelpers.AppendWithCapacity(ref modifiers, ref totalModifierCount, modifier);
                Debug.Assert(modifierStateCount == totalModifierCount);
            }

            return firstModifierIndex;
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
}
