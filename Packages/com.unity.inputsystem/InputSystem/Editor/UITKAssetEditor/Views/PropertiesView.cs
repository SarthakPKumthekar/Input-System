#if UNITY_EDITOR && UNITY_INPUT_SYSTEM_UI_TK_ASSET_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace UnityEngine.InputSystem.Editor
{
    internal class PropertiesView : ViewBase<PropertiesView.ViewState>
    {
        private readonly VisualElement m_Root;
        private ActionPropertiesView m_ActionPropertyView;
        private BindingPropertiesView m_BindingPropertyView;
        private NameAndParametersListView m_InteractionsListView;
        private NameAndParametersListView m_ProcessorsListView;

        private Foldout interactionsFoldout => m_Root.Q<Foldout>("interactions-foldout");
        private Foldout processorsFoldout => m_Root.Q<Foldout>("processors-foldout");

        private TextElement addInteractionButton;
        private TextElement addProcessorButton;

        public PropertiesView(VisualElement root, StateContainer stateContainer)
            : base(stateContainer)
        {
            m_Root = root;

            CreateSelector(
                Selectors.GetSelectedAction,
                Selectors.GetSelectedBinding,
                state => state.selectionType,
                (inputAction, inputBinding, selectionType, s) => new ViewState()
                {
                    selectionType = selectionType,
                    serializedInputAction = inputAction,
                    inputBinding = inputBinding,
                    relatedInputAction = Selectors.GetRelatedInputAction(s)
                });

            var interactionsToggle = interactionsFoldout.Q<Toggle>();
            interactionsToggle.AddToClassList("properties-foldout-toggle");
            if (addInteractionButton == null)
            {
                addInteractionButton = CreateAddButton(interactionsToggle, "add-new-interaction-button");
                new ContextualMenuManipulator(_ => {}){target = addInteractionButton, activators = {new ManipulatorActivationFilter(){button = MouseButton.LeftMouse}}};
            }
            var processorToggle = processorsFoldout.Q<Toggle>();
            processorToggle.AddToClassList("properties-foldout-toggle");
            if (addProcessorButton == null)
            {
                addProcessorButton = CreateAddButton(processorToggle, "add-new-processor-button");
                new ContextualMenuManipulator(_ => {}){target = addProcessorButton, activators = {new ManipulatorActivationFilter(){button = MouseButton.LeftMouse}}};
            }
        }

        private TextElement CreateAddButton(Toggle toggle, string name)
        {
            var addProcessorButton = new TextElement();
            addProcessorButton.text = "+";
            addProcessorButton.name = name;
            addProcessorButton.AddToClassList("add-interaction-processor-button");
            toggle.Add(addProcessorButton);
            return addProcessorButton;
        }

        private void CreateContextMenuProcessor(string expectedControlType, SerializedProperty serializedProperty)
        {
            var processors = InputProcessor.s_Processors;
            Type expectedValueType = null;
            if (!string.IsNullOrEmpty(expectedControlType))
                expectedValueType = EditorInputControlLayoutCache.GetValueType(expectedControlType);

            addProcessorButton.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                evt.menu.ClearItems();
                foreach (var name in processors.internedNames.Where(x => !processors.ShouldHideInUI(x)).OrderBy(x => x.ToString()))
                {
                    // Skip if not compatible with value type.
                    if (expectedValueType != null)
                    {
                        var type = processors.LookupTypeRegistration(name);
                        var valueType = InputProcessor.GetValueTypeFromType(type);
                        if (valueType != null && !expectedValueType.IsAssignableFrom(valueType))
                            continue;
                    }
                    var niceName = ObjectNames.NicifyVariableName(name);
                    var oldProcessors = serializedProperty.FindPropertyRelative(nameof(InputAction.m_Processors));
                    evt.menu.AppendAction(niceName, _ => m_ProcessorsListView.OnAddElement(name.ToString(), oldProcessors, oldProcessors.stringValue));
                }
            });
        }

        private void CreateContextMenuInteraction(string expectedControlType, SerializedProperty serializedProperty)
        {
            var interactions = InputInteraction.s_Interactions;
            Type expectedValueType = null;
            if (!string.IsNullOrEmpty(expectedControlType))
                expectedValueType = EditorInputControlLayoutCache.GetValueType(expectedControlType);
            addInteractionButton.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                evt.menu.ClearItems();
                foreach (var name in interactions.internedNames.Where(x => !interactions.ShouldHideInUI(x)).OrderBy(x => x.ToString()))
                {
                    // Skip if not compatible with value type.
                    if (expectedValueType != null)
                    {
                        var type = interactions.LookupTypeRegistration(name);
                        var valueType = InputInteraction.GetValueType(type);
                        if (valueType != null && !expectedValueType.IsAssignableFrom(valueType))
                            continue;
                    }

                    var niceName = ObjectNames.NicifyVariableName(name);
                    var oldInteractions = serializedProperty.FindPropertyRelative(nameof(InputAction.m_Interactions));
                    evt.menu.AppendAction(niceName, _ => m_InteractionsListView.OnAddElement(name.ToString(), oldInteractions, oldInteractions.stringValue));
                }
            });
        }

        public override void RedrawUI(ViewState viewState)
        {
            DestroyChildView(m_ActionPropertyView);
            DestroyChildView(m_BindingPropertyView);
            DestroyChildView(m_InteractionsListView);
            DestroyChildView(m_ProcessorsListView);

            var propertiesContainer = m_Root.Q<VisualElement>("properties-container");

            var foldout = propertiesContainer.Q<Foldout>("properties-foldout");
            foldout.Clear();

            var visualElement = new VisualElement();
            foldout.Add(visualElement);
            foldout.Q<Toggle>().AddToClassList("properties-foldout-toggle");

            var inputAction = viewState.serializedInputAction;
            var inputActionOrBinding = inputAction?.wrappedProperty;

            switch (viewState.selectionType)
            {
                case SelectionType.Action:
                    m_Root.Q<Label>("properties-header-label").text = "Action Properties";
                    m_ActionPropertyView = CreateChildView(new ActionPropertiesView(visualElement, stateContainer));
                    break;

                case SelectionType.Binding:
                    m_Root.Q<Label>("properties-header-label").text = "Binding Properties";
                    m_BindingPropertyView = CreateChildView(new BindingPropertiesView(visualElement, foldout, stateContainer));
                    inputAction = viewState.relatedInputAction;
                    inputActionOrBinding = viewState.inputBinding?.wrappedProperty;
                    break;
            }

            CreateContextMenuProcessor(inputAction?.expectedControlType, inputActionOrBinding);
            CreateContextMenuInteraction(inputAction?.expectedControlType, inputActionOrBinding);

            m_InteractionsListView = CreateChildView(new NameAndParametersListView(
                interactionsFoldout,
                stateContainer,
                state => Selectors.GetInteractionsAsParameterListViews(state, inputAction)));


            m_ProcessorsListView = CreateChildView(new NameAndParametersListView(
                processorsFoldout,
                stateContainer,
                state => Selectors.GetProcessorsAsParameterListViews(state, inputAction)));
        }

        internal class ViewState
        {
            public SerializedInputAction? relatedInputAction;
            public SerializedInputBinding? inputBinding;
            public SerializedInputAction? serializedInputAction;
            public SelectionType selectionType;
        }
    }
}

#endif
