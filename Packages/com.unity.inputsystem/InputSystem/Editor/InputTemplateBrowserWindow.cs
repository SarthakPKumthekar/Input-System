#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using ISX.LowLevel;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

////TODO: explore a way to add a toggle that switches between browsing merged and unmerged templates

////FIXME: templates registered with an explicit device description don't show up in the products section

namespace ISX.Editor
{
    // Allows browsing through all templates currently registered with the input system.
    //
    // NOTE: Templates are shown in their fully merged form, i.e. all the information from base
    //       templates is merged into templates targeting them with the "extends" field.
    public class InputTemplateBrowserWindow : EditorWindow
    {
        private static InputTemplateBrowserWindow s_Instance;

        public static void CreateOrShowExisting()
        {
            if (s_Instance == null)
            {
                s_Instance = GetWindow<InputTemplateBrowserWindow>(desiredDockNextTo: typeof(InputDebuggerWindow));
                s_Instance.titleContent = new GUIContent("Input Templates");
            }

            s_Instance.Show();
            s_Instance.Focus();
        }

        public void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public void OnGUI()
        {
            if (m_TemplateTreeView == null)
                Initialize();

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(Contents.templates, GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            var toolbarRect = GUILayoutUtility.GetLastRect();
            var treeViewRect = new Rect(0, toolbarRect.height, position.width, position.height - toolbarRect.height);

            m_TemplateTreeView.OnGUI(treeViewRect);
        }

        private void Initialize()
        {
            if (m_TemplateTreeViewState == null)
                m_TemplateTreeViewState = new TreeViewState();

            m_TemplateTreeView = new TemplateTreeView(m_TemplateTreeViewState);
            EditorInputTemplateCache.onRefresh += m_TemplateTreeView.Reload;

            // Expand controls, devices, and products toplevel items.
            m_TemplateTreeView.SetExpanded(1, true);
            m_TemplateTreeView.SetExpanded(2, true);
            m_TemplateTreeView.SetExpanded(3, true);
        }

        [SerializeField] private TreeViewState m_TemplateTreeViewState;
        [NonSerialized] private TemplateTreeView m_TemplateTreeView;

        private static class Contents
        {
            public static GUIContent templates = new GUIContent("Templates");
        }

        private class TemplateTreeView : TreeView
        {
            public TemplateTreeView(TreeViewState state)
                : base(state)
            {
                Reload();
            }

            protected override TreeViewItem BuildRoot()
            {
                var id = 0;

                var root = new TreeViewItem
                {
                    id = id++,
                    depth = -1
                };

                // Split root into three different groups:
                // 1) Control templates
                // 2) Device templates that don't match specific products
                // 3) Device templates that match specific products

                var controls = new TreeViewItem
                {
                    id = id++,
                    depth = 0,
                    displayName = "Controls"
                };
                var devices = new TreeViewItem
                {
                    id = id++,
                    depth = 0,
                    displayName = "Devices"
                };
                var products = new TreeViewItem
                {
                    id = id++,
                    depth = 0,
                    displayName = "Products"
                };

                root.children = new List<TreeViewItem> { controls, devices, products };

                foreach (var template in EditorInputTemplateCache.allTemplates)
                {
                    TreeViewItem parent;
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
                            {
                                group = new TreeViewItem
                                {
                                    id = id++,
                                    depth = 1,
                                    displayName = rootBaseTemplateName
                                };
                                products.AddChild(group);
                            }

                            parent = group;
                        }
                        else
                            parent = devices;
                    }
                    else
                        parent = controls;

                    BuildItem(template, parent, ref id);
                }

                if (controls.children != null)
                    controls.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
                if (devices.children != null)
                    devices.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
                if (products.children != null)
                    products.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));

                return root;
            }

            private TreeViewItem BuildItem(InputTemplate template, TreeViewItem parent, ref int id)
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
                        BuildItem(control, controls, ref id);

                    controls.children.Sort((a, b) => string.Compare(a.displayName, b.displayName));
                }

                return item;
            }

            private void BuildItem(InputTemplate.ControlTemplate control, TreeViewItem parent, ref int id)
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
        }
    }
}
#endif // UNITY_EDITOR
