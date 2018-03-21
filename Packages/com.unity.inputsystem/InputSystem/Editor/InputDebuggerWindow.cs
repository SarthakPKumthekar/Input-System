#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using ISX.LowLevel;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Networking.PlayerConnection;

////TODO: Ideally, I'd like all separate EditorWindows opened by the InputDebugger to automatically
////      be docked into the container window of InputDebuggerWindow

////TODO: add view to tweak InputConfiguration interactively in the editor

////TODO: display icons on devices depending on type of device

////TODO: make configuration update when changed

////TODO: refresh when unrecognized device pops up

namespace ISX.Editor
{
    // Allows looking at input activity in the editor.
    internal class InputDebuggerWindow : EditorWindow, ISerializationCallbackReceiver
    {
        private static InputDebuggerWindow s_Instance;

        [MenuItem("Window/Input Debugger", false, 2100)]
        public static void Init()
        {
            if (s_Instance == null)
            {
                s_Instance = GetWindow<InputDebuggerWindow>();
                s_Instance.Show();
                s_Instance.titleContent = new GUIContent("Input Debug");
            }
            else
            {
                s_Instance.Show();
                s_Instance.Focus();
            }
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            // Update tree if devices are added or removed.
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed)
                Refresh();
        }

        private void OnTemplateChange(string name, InputTemplateChange change)
        {
            // Update tree if template setup has changed.
            Refresh();
        }

        private string OnFindTemplate(int deviceId, ref InputDeviceDescription description, string matchedTemplate,
            IInputRuntime runtime)
        {
            // If there's no matched template, there's a chance this device will go in
            // the unsupported list. There's no direct notification for that so we
            // pre-emptively trigger a refresh.
            if (string.IsNullOrEmpty(matchedTemplate))
                Refresh();

            return null;
        }

        private void Refresh()
        {
            if (m_TreeView != null)
                m_TreeView.Reload();
            Repaint();
        }

        public void OnDestroy()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onTemplateChange -= OnTemplateChange;
            InputSystem.onFindTemplateForDevice -= OnFindTemplate;

            if (InputActionSet.s_OnEnabledActionsChanged != null)
                InputActionSet.s_OnEnabledActionsChanged.Remove(Repaint);
        }

        private void Initialize()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            InputSystem.onTemplateChange += OnTemplateChange;
            InputSystem.onFindTemplateForDevice += OnFindTemplate;

            if (InputActionSet.s_OnEnabledActionsChanged == null)
                InputActionSet.s_OnEnabledActionsChanged = new List<Action>();
            InputActionSet.s_OnEnabledActionsChanged.Add(Repaint);

            var newTreeViewState = m_TreeViewState == null;
            if (newTreeViewState)
                m_TreeViewState = new TreeViewState();

            m_TreeView = new InputSystemTreeView(m_TreeViewState);

            // Set default expansion states.
            if (newTreeViewState)
                m_TreeView.SetExpanded(m_TreeView.devicesItem.id, true);

            m_Initialized = true;
        }

        public void OnGUI()
        {
            // This also brings us back online after a domain reload.
            if (!m_Initialized)
                Initialize();

            DrawToolbarGUI();

            var rect = EditorGUILayout.GetControlRect(GUILayout.ExpandHeight(true));
            m_TreeView.OnGUI(rect);
        }

        private void DrawToolbarGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Enable/disable debug mode.
            var debugMode = GUILayout.Toggle(m_DebugMode, Contents.debugModeContent, EditorStyles.toolbarButton);
            if (debugMode != m_DebugMode)
            {
                if (debugMode)
                {
                    if (m_Debugger == null)
                        m_Debugger = new InputDebugger();
                    InputSystem.s_Manager.m_Debugger = m_Debugger;
                }
                else
                {
                    InputSystem.s_Manager.m_Debugger = null;
                }
                m_DebugMode = debugMode;
            }

            InputConfiguration.LockInputToGame = GUILayout.Toggle(InputConfiguration.LockInputToGame,
                    Contents.lockInputToGameContent, EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /*
        private void DrawActionsGUI()
        {
            GUILayout.Label(Contents.enabledActionsContent, EditorStyles.boldLabel);

            if (m_EnabledActions == null)
                m_EnabledActions = new List<InputAction>();
            else
                m_EnabledActions.Clear();

            InputSystem.ListEnabledActions(m_EnabledActions);

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));

            var numEnabledActions = m_EnabledActions.Count;
            if (numEnabledActions == 0)
            {
                GUILayout.Label(Contents.noneContent);
            }
            else
            {
                for (var i = 0; i < m_EnabledActions.Count; ++i)
                {
                    var action = m_EnabledActions[i];
                    if (GUILayout.Button(action.name))
                    {
                        InputActionDebuggerWindow.CreateOrShowExisting(m_EnabledActions[i]);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }
        */

        [SerializeField] private bool m_DebugMode;
        [SerializeField] private TreeViewState m_TreeViewState;

        [NonSerialized] private InputDebugger m_Debugger;
        [NonSerialized] private InputSystemTreeView m_TreeView;
        [NonSerialized] private bool m_Initialized;

        internal static void ReviveAfterDomainReload()
        {
            if (s_Instance != null)
            {
                InputSystem.onDeviceChange += s_Instance.OnDeviceChange;

                // Trigger an initial repaint now that we know the input system has come
                // back to life.
                s_Instance.Repaint();
            }
        }

        private static class Contents
        {
            public static GUIContent lockInputToGameContent = new GUIContent("Lock Input to Game");
            public static GUIContent debugModeContent = new GUIContent("Debug Mode");
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            s_Instance = this;
        }

        class InputSystemTreeView : TreeView
        {
            public TreeViewItem devicesItem { get; private set; }
            public TreeViewItem templatesItem { get; private set; }
            public TreeViewItem configurationItem { get; private set; }

            public InputSystemTreeView(TreeViewState state)
                : base(state)
            {
                Reload();
            }

            protected override void ContextClickedItem(int id)
            {
            }

            protected override void DoubleClickedItem(int id)
            {
                var item = FindItem(id, rootItem);
                if (item == null)
                    return;

                var deviceItem = item as DeviceItem;
                if (deviceItem != null)
                {
                    InputDeviceDebuggerWindow.CreateOrShowExisting(deviceItem.device);
                    return;
                }
            }

            protected override TreeViewItem BuildRoot()
            {
                var id = 0;

                var root = new TreeViewItem
                {
                    id = id++,
                    depth = -1
                };

                // Actions.
                //var actionsNode = AddChild(root, "Actions", ref id);

                // Devices.
                var devices = InputSystem.devices;
                devicesItem = AddChild(root, string.Format("Devices ({0})", devices.Count), ref id);
                var haveRemotes = devices.Any(x => x.remote);
                if (haveRemotes)
                {
                    // Split local and remote devices into groups.

                    var localDevicesNode = AddChild(devicesItem, "Local", ref id);
                    AddDevices(localDevicesNode, devices, ref id);

                    var remoteDevicesNode = AddChild(devicesItem, "Remote", ref id);
                    foreach (var player in EditorConnection.instance.ConnectedPlayers)
                    {
                        var playerNode = AddChild(remoteDevicesNode, player.name, ref id);
                        AddDevices(playerNode, devices, ref id, "Remote" + player.playerId + InputTemplate.kNamespaceQualifier);
                    }
                }
                else
                {
                    // We don't have remote devices so don't add an extra group for local devices.
                    // Put them all directly underneath the "Devices" node.
                    AddDevices(devicesItem, devices, ref id);
                }

                if (m_UnsupportedDevices == null)
                    m_UnsupportedDevices = new List<InputDeviceDescription>();
                m_UnsupportedDevices.Clear();
                InputSystem.GetUnsupportedDevices(m_UnsupportedDevices);
                if (m_UnsupportedDevices.Count > 0)
                {
                    var unsupportedDevicesNode = AddChild(devicesItem, string.Format("Unsupported ({0})", m_UnsupportedDevices.Count), ref id);
                    foreach (var device in m_UnsupportedDevices)
                        AddChild(unsupportedDevicesNode, device.ToString(), ref id);
                    unsupportedDevicesNode.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
                }

                // Templates.
                templatesItem = AddChild(root, "Templates", ref id);
                AddTemplates(templatesItem, ref id);

                ////FIXME: this shows local configuration only
                // Configuration.
                configurationItem = AddChild(root, "Configuration", ref id);
                AddConfigurationItem(configurationItem, "ButtonPressPoint", InputConfiguration.ButtonPressPoint, ref id);
                AddConfigurationItem(configurationItem, "DeadzoneMin", InputConfiguration.DeadzoneMin, ref id);
                AddConfigurationItem(configurationItem, "DeadzoneMax", InputConfiguration.DeadzoneMax, ref id);
                AddConfigurationItem(configurationItem, "LockInputToGame", InputConfiguration.LockInputToGame, ref id);
                configurationItem.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));

                return root;
            }

            private void AddDevices(TreeViewItem parent, IEnumerable<InputDevice> devices, ref int id, string namePrefix = null)
            {
                foreach (var device in devices)
                {
                    if (namePrefix != null)
                    {
                        if (!device.name.StartsWith(namePrefix))
                            continue;
                    }
                    else if (device.name.Contains(InputTemplate.kNamespaceQualifier))
                        continue;

                    var item = new DeviceItem
                    {
                        id = id++,
                        depth = parent.depth + 1,
                        displayName = namePrefix != null ? device.name.Substring(namePrefix.Length) : device.name,
                        device = device,
                    };
                    parent.AddChild(item);
                }

                if (parent.children != null)
                    parent.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
            }

            ////TODO: split remote and local templates
            private void AddTemplates(TreeViewItem parent, ref int id)
            {
                // Split root into three different groups:
                // 1) Control templates
                // 2) Device templates that don't match specific products
                // 3) Device templates that match specific products

                var controls = AddChild(parent, "Controls", ref id);
                var devices = AddChild(parent, "Devices", ref id);
                var products = AddChild(parent, "Products", ref id);

                foreach (var template in EditorInputTemplateCache.allTemplates)
                {
                    TreeViewItem parentForTemplate;
                    if (template.isDeviceTemplate)
                    {
                        ////REVIEW: should this split by base device templates derived device templates instead?
                        if (!template.deviceDescription.empty)
                        {
                            var rootBaseTemplateName = InputTemplate.s_Templates.GetRootTemplateName(template.name).ToString();
                            if (string.IsNullOrEmpty(rootBaseTemplateName))
                                rootBaseTemplateName = "Other";
                            else
                                rootBaseTemplateName += "s";

                            var group = products.children != null
                                ? products.children.FirstOrDefault(x => x.displayName == rootBaseTemplateName)
                                : null;
                            if (group == null)
                                group = AddChild(products, rootBaseTemplateName, ref id);

                            parentForTemplate = group;
                        }
                        else
                            parentForTemplate = devices;
                    }
                    else
                        parentForTemplate = controls;

                    AddTemplateItem(template, parentForTemplate, ref id);
                }

                if (controls.children != null)
                    controls.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
                if (devices.children != null)
                    devices.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
                if (products.children != null)
                    products.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
            }

            private TreeViewItem AddTemplateItem(InputTemplate template, TreeViewItem parent, ref int id)
            {
                var item = AddChild(parent, template.name, ref id);

                // Header.
                AddChild(item, "Type: " + template.type.Name, ref id);
                if (!string.IsNullOrEmpty(template.extendsTemplate))
                    AddChild(item, "Extends: " + template.extendsTemplate, ref id);
                if (template.stateFormat != 0)
                    AddChild(item, "Format: " + template.stateFormat, ref id);
                if (template.m_UpdateBeforeRender != null)
                {
                    var value = template.m_UpdateBeforeRender.Value ? "Update" : "Disabled";
                    AddChild(item, "Before Render: " + value, ref id);
                }
                if (template.commonUsages.Count > 0)
                {
                    AddChild(item,
                        "Common Usages: " +
                        string.Join(", ", template.commonUsages.Select(x => x.ToString()).ToArray()), ref id);
                }
                if (!template.deviceDescription.empty)
                {
                    var deviceDescription = AddChild(item, "Device Description", ref id);
                    if (!string.IsNullOrEmpty(template.deviceDescription.deviceClass))
                        AddChild(deviceDescription,
                            "Device Class: " + template.deviceDescription.deviceClass, ref id);
                    if (!string.IsNullOrEmpty(template.deviceDescription.interfaceName))
                        AddChild(deviceDescription,
                            "Interface: " + template.deviceDescription.interfaceName, ref id);
                    if (!string.IsNullOrEmpty(template.deviceDescription.product))
                        AddChild(deviceDescription, "Product: " + template.deviceDescription.product, ref id);
                    if (!string.IsNullOrEmpty(template.deviceDescription.manufacturer))
                        AddChild(deviceDescription,
                            "Manufacturer: " + template.deviceDescription.manufacturer, ref id);
                }

                // Controls.
                if (template.controls.Count > 0)
                {
                    var controls = AddChild(item, "Controls", ref id);
                    foreach (var control in template.controls)
                        AddControlTemplateItem(control, controls, ref id);

                    controls.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
                }

                return item;
            }

            private void AddControlTemplateItem(InputTemplate.ControlTemplate control, TreeViewItem parent, ref int id)
            {
                var item = AddChild(parent, control.variant.IsEmpty() ? control.name : string.Format("{0} ({1})",
                            control.name, control.variant), ref id);

                ////TODO: fully merge TreeViewItems from isModifyingChildControlByPath control templates into the control they modify

                ////TODO: allow clicking this field to jump to the template
                if (!control.template.IsEmpty())
                    AddChild(item, string.Format("Template: {0}", control.template), ref id);
                if (!control.variant.IsEmpty())
                    AddChild(item, string.Format("Variant: {0}", control.variant), ref id);
                if (control.format != 0)
                    AddChild(item, string.Format("Format: {0}", control.format), ref id);
                if (control.offset != InputStateBlock.kInvalidOffset)
                    AddChild(item, string.Format("Offset: {0}", control.offset), ref id);
                if (control.bit != InputStateBlock.kInvalidOffset)
                    AddChild(item, string.Format("Bit: {0}", control.bit), ref id);
                if (control.sizeInBits != 0)
                    AddChild(item, string.Format("Size In Bits: {0}", control.sizeInBits), ref id);
                if (!string.IsNullOrEmpty(control.useStateFrom))
                    AddChild(item, string.Format("Use State From: {0}", control.useStateFrom), ref id);

                if (control.usages.Count > 0)
                    AddChild(item, "Usages: " + string.Join(", ", control.usages.Select(x => x.ToString()).ToArray()), ref id);
                if (control.aliases.Count > 0)
                    AddChild(item, "Aliases: " + string.Join(", ", control.aliases.Select(x => x.ToString()).ToArray()), ref id);

                if (control.parameters.Count > 0)
                {
                    var parameters = AddChild(item, "Parameters", ref id);
                    foreach (var parameter in control.parameters)
                        AddChild(parameters, parameter.ToString(), ref id);
                }

                if (control.processors.Count > 0)
                {
                    var processors = AddChild(item, "Processors", ref id);
                    foreach (var processor in control.processors)
                    {
                        var processorItem = AddChild(processors, processor.name, ref id);
                        foreach (var parameter in processor.parameters)
                            AddChild(processorItem, parameter.ToString(), ref id);
                    }
                }
            }

            public void AddConfigurationItem<TValue>(TreeViewItem parent, string name, TValue value, ref int id)
            {
                var item = new ConfigurationItem
                {
                    id = id++,
                    depth = parent.depth + 1,
                    displayName = string.Format("{0}: {1}", name, value.ToString()),
                    name = name
                };
                parent.AddChild(item);
            }

            private TreeViewItem AddChild(TreeViewItem parent, string displayName, ref int id)
            {
                var item = new TreeViewItem
                {
                    id = id++,
                    depth = parent.depth + 1,
                    displayName = displayName
                };
                parent.AddChild(item);
                return item;
            }

            private List<InputDeviceDescription> m_UnsupportedDevices;

            class DeviceItem : TreeViewItem
            {
                public InputDevice device;
            }

            class ConfigurationItem : TreeViewItem
            {
                public string name;
            }

            //class ActionItem : TreeViewItem
            //{
            //public InputAction action;
            //}
        }
    }
}
#endif // UNITY_EDITOR
