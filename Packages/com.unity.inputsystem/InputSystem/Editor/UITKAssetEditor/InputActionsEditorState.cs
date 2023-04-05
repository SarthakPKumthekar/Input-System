using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEditor;

namespace UnityEngine.InputSystem.Editor
{
	internal readonly struct InputActionsEditorState
    {
        private readonly Dictionary<(string, string), HashSet<int>> m_ExpandedCompositeBindings;

        public int selectedActionMapIndex { get; }
        public int selectedActionIndex { get; }
        public int selectedBindingIndex { get; }
        public SelectionType selectionType { get; }

        public SerializedObject serializedObject { get; }
        

        public InputActionsEditorState(
	        SerializedObject inputActionAsset, 
	        int selectedActionMapIndex = 0, 
	        int selectedActionIndex = 0, 
	        int selectedBindingIndex = 0,
	        SelectionType selectionType = SelectionType.Action,
	        Dictionary<(string, string), HashSet<int>> expandedBindingIndices = null)
        {
            serializedObject = inputActionAsset;

            this.selectedActionMapIndex = selectedActionMapIndex;
            this.selectedActionIndex = selectedActionIndex;
            this.selectedBindingIndex = selectedBindingIndex;
            this.selectionType = selectionType;

            m_ExpandedCompositeBindings = expandedBindingIndices == null ? 
	            new Dictionary<(string, string), HashSet<int>>() :
                new Dictionary<(string, string), HashSet<int>>(expandedBindingIndices);
        }

        public InputActionsEditorState With(
	        int? selectedActionMapIndex = null, 
	        int? selectedActionIndex = null, 
	        int? selectedBindingIndex = null,
	        SelectionType? selectionType = null,
	        Dictionary<(string, string), HashSet<int>> expandedBindingIndices = null)
        {
            return new InputActionsEditorState(
                serializedObject,
	            selectedActionMapIndex ?? this.selectedActionMapIndex,
	            selectedActionIndex ?? this.selectedActionIndex,
	            selectedBindingIndex ?? this.selectedBindingIndex,
	            selectionType ?? this.selectionType,
                expandedBindingIndices ?? m_ExpandedCompositeBindings);
        }
        
        public SerializedProperty GetActionMapByName(string actionMapName)
        {
	        return serializedObject
		        .FindProperty(nameof(InputActionAsset.m_ActionMaps))
		        .FirstOrDefault(p => p.FindPropertyRelative(nameof(InputActionMap.m_Name)).stringValue == actionMapName);
        }

        public InputActionsEditorState ExpandCompositeBinding(SerializedInputBinding binding)
        {
	        var key = GetSelectedActionMapAndActionKey();

	        var expandedCompositeBindings = new Dictionary<(string, string), HashSet<int>>(m_ExpandedCompositeBindings);
	        if (!expandedCompositeBindings.TryGetValue(key, out var expandedStates))
	        {
		        expandedStates = new HashSet<int>();
		        expandedCompositeBindings.Add(key, expandedStates);
	        }

	        expandedStates.Add(binding.indexOfBinding);

            return With(expandedBindingIndices: expandedCompositeBindings);
        }

        public InputActionsEditorState CollapseCompositeBinding(SerializedInputBinding binding)
        {
	        var key = GetSelectedActionMapAndActionKey();

            if (m_ExpandedCompositeBindings.ContainsKey(key) == false)
                throw new InvalidOperationException("Trying to collapse a composite binding tree that was never expanded.");

            // do the dance of C# immutability
            var oldExpandedCompositeBindings = m_ExpandedCompositeBindings;
            var expandedCompositeBindings = oldExpandedCompositeBindings.Keys.Where(dictKey => dictKey != key)
	            .ToDictionary(dictKey => dictKey, dictKey => oldExpandedCompositeBindings[dictKey]);
            var newHashset = new HashSet<int>(m_ExpandedCompositeBindings[key].Where(index => index != binding.indexOfBinding));
            expandedCompositeBindings.Add(key, newHashset);

            return With(expandedBindingIndices: expandedCompositeBindings);
        }

        public InputActionsEditorState SelectAction(string actionName)
        {
	        var actionMap = GetSelectedActionMap();
	        var actions = actionMap.FindPropertyRelative(nameof(InputActionMap.m_Actions));

	        for (var i = 0; i < actions.arraySize; i++)
	        {
		        if (actions.GetArrayElementAtIndex(i)
			            .FindPropertyRelative(nameof(InputAction.m_Name)).stringValue != actionName) continue;

		        return With(selectedActionIndex: i, selectionType: SelectionType.Action);
	        }

	        throw new InvalidOperationException($"Couldn't find an action map with name '{actionName}'.");
        }

        public InputActionsEditorState SelectActionMap(string actionMapName)
        {
	        var actionMap = GetActionMapByName(actionMapName);
	        return With(selectedBindingIndex: 0, selectedActionMapIndex: actionMap.GetIndexOfArrayElement());
        }

        public ReadOnlyCollection<int> GetOrCreateExpandedState()
        {
	        return new ReadOnlyCollection<int>(GetOrCreateExpandedStateInternal().ToList());
        }

        private HashSet<int> GetOrCreateExpandedStateInternal()
        {
	        var key = GetSelectedActionMapAndActionKey();

	        if (m_ExpandedCompositeBindings.TryGetValue(key, out var expandedStates))
		        return expandedStates;

	        expandedStates = new HashSet<int>();
	        m_ExpandedCompositeBindings.Add(key, expandedStates);
	        return expandedStates;
        }

        private (string, string) GetSelectedActionMapAndActionKey()
        {
	        var selectedActionMap = GetSelectedActionMap();

	        var selectedAction = selectedActionMap
		        .FindPropertyRelative(nameof(InputActionMap.m_Actions))
		        .GetArrayElementAtIndex(selectedActionIndex);

	        var key = (
		        selectedActionMap.FindPropertyRelative(nameof(InputActionMap.m_Name)).stringValue,
		        selectedAction.FindPropertyRelative(nameof(InputAction.m_Name)).stringValue
	        );
	        return key;
        }

        private SerializedProperty GetSelectedActionMap()
        {
	        return serializedObject
		        .FindProperty(nameof(InputActionAsset.m_ActionMaps))
		        .GetArrayElementAtIndex(selectedActionMapIndex);
        }
    }

    internal enum SelectionType
    {
	    Action,
	    Binding
    }
}