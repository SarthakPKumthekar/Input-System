using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ISX.Controls;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using ISX.LowLevel;
using ISX.Modifiers;
using ISX.Processors;
using ISX.Utilities;
using Unity.Collections;
using Debug = UnityEngine.Debug;
#if !(NET_4_0 || NET_4_6)
using ISX.Net35Compatibility;
#endif

////TODO: allow pushing events into the system any which way; decouple from the buffer in NativeInputSystem being the only source

////TODO: merge InputManager into InputSystem and have InputSystemObject store SerializedState directly

////REVIEW: change the event properties over to using IObservable?

////REVIEW: instead of RegisterModifier and RegisterProcessor, have a generic RegisterInterface (or something)?

namespace ISX
{
    using DeviceChangeListener = Action<InputDevice, InputDeviceChange>;
    using TemplateChangeListener = Action<string, InputTemplateChange>;
    using EventListener = Action<InputEventPtr>;
    using UpdateListener = Action<InputUpdateType>;

    public delegate string DeviceFindTemplateCallback(int deviceId, ref InputDeviceDescription description, string matchedTemplate,
        IInputRuntime runtime);

    // The hub of the input system.
    // All state is ultimately gathered here.
    // Not exposed. Use InputSystem as the public entry point to the system.
#if UNITY_EDITOR
    [Serializable]
#endif
    internal class InputManager
#if UNITY_EDITOR
        : ISerializationCallbackReceiver
#endif
    {
        public ReadOnlyArray<InputDevice> devices
        {
            get { return new ReadOnlyArray<InputDevice>(m_Devices); }
        }

        public InputUpdateType updateMask
        {
            get { return m_UpdateMask; }
            set
            {
                ////TODO: also actually turn off unnecessary updates on the native side (e.g. if fixed
                ////      updates are disabled, don't even have native fire onUpdate for fixed updates)
                throw new NotImplementedException();
            }
        }

        public event DeviceChangeListener onDeviceChange
        {
            add { m_DeviceChangeListeners.Append(value); }
            remove { m_DeviceChangeListeners.Remove(value); }
        }

        public event DeviceFindTemplateCallback onFindTemplateForDevice
        {
            add { m_DeviceFindTemplateCallbacks.Append(value); }
            remove { m_DeviceFindTemplateCallbacks.Remove(value); }
        }

        public event TemplateChangeListener onTemplateChange
        {
            add { m_TemplateChangeListeners.Append(value); }
            remove { m_TemplateChangeListeners.Remove(value); }
        }

        ////TODO: add InputEventBuffer struct that uses NativeArray underneath
        ////TODO: make InputEventTrace use NativeArray
        ////TODO: introduce an alternative that consumes events in bulk
        public event EventListener onEvent
        {
            add { m_EventListeners.Append(value); }
            remove { m_EventListeners.Remove(value); }
        }

        public event UpdateListener onUpdate
        {
            add
            {
                InstallBeforeUpdateHookIfNecessary();
                m_UpdateListeners.Append(value);
            }
            remove { m_UpdateListeners.Remove(value); }
        }

        ////TODO: when registering a template that exists as a template of a different type (type vs string vs constructor),
        ////      remove the existing registration

        // Add a template constructed from a type.
        // If a template with the same name already exists, the new template
        // takes its place.
        public void RegisterTemplate(string name, Type type, InputDeviceDescription? deviceDescription = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name");
            if (type == null)
                throw new ArgumentNullException("type");

            // Note that since InputDevice derives from InputControl, isDeviceTemplate implies
            // isControlTemplate to be true as well.
            var isDeviceTemplate = typeof(InputDevice).IsAssignableFrom(type);
            var isControlTemplate = typeof(InputControl).IsAssignableFrom(type);

            if (!isDeviceTemplate && !isControlTemplate)
                throw new ArgumentException("Types used as templates have to be InputControls or InputDevices",
                    "type");

            var internedName = new InternedString(name);
            var isReplacement = HaveTemplate(internedName);

            // All we do is enter the type into a map. We don't construct an InputTemplate
            // from it until we actually need it in an InputControlSetup to create a device.
            // This not only avoids us creating a bunch of objects on the managed heap but
            // also avoids us laboriously constructing a VRController template, for example,
            // in a game that never uses VR.
            m_Templates.templateTypes[internedName] = type;

            ////TODO: make this independent of initialization order
            ////TODO: re-scan base type information after domain reloads

            // Walk class hierarchy all the way up to InputControl to see
            // if there's another type that's been registered as a template.
            // If so, make it a base template for this one.
            string baseTemplate = null;
            for (var baseType = type.BaseType; baseTemplate == null && baseType != typeof(InputControl);
                 baseType = baseType.BaseType)
            {
                foreach (var entry in m_Templates.templateTypes)
                    if (entry.Value == baseType)
                    {
                        baseTemplate = entry.Key;
                        break;
                    }
            }

            PerformTemplatePostRegistration(internedName, baseTemplate, deviceDescription, isReplacement,
                isKnownToBeDeviceTemplate: isDeviceTemplate);
        }

        // Add a template constructed from a JSON string.
        public void RegisterTemplate(string json, string name = null, string @namespace = null)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("json");

            ////REVIEW: as long as no one has instantiated the template, the base template information is kinda pointless

            // Parse out name, device description, and base template.
            InputDeviceDescription deviceDescription;
            string baseTemplate;
            var nameFromJson = InputTemplate.ParseHeaderFromJson(json, out deviceDescription, out baseTemplate);

            // Decide whether to take name from JSON or from code.
            if (string.IsNullOrEmpty(name))
            {
                name = nameFromJson;

                // Make sure we have a name.
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException("Template name has not been given and is not set in JSON template",
                        "name");
            }

            if (@namespace != null)
                name = string.Format("{0}::{1}", @namespace, name);

            var internedName = new InternedString(name);
            var isReplacement = HaveTemplate(internedName);

            // Add it to our records.
            m_Templates.templateStrings[internedName] = json;

            PerformTemplatePostRegistration(internedName, baseTemplate, deviceDescription, isReplacement);
        }

        public void RegisterTemplateConstructor(MethodInfo method, object instance, string name,
            string baseTemplate = null, InputDeviceDescription? deviceDescription = null)
        {
            if (method == null)
                throw new ArgumentNullException("method");
            if (method.IsGenericMethod)
                throw new ArgumentException(string.Format("Method must not be generic ({0})", method), "method");
            if (method.GetParameters().Length > 0)
                throw new ArgumentException(string.Format("Method must not take arguments ({0})", method), "method");
            if (!typeof(InputTemplate).IsAssignableFrom(method.ReturnType))
                throw new ArgumentException(string.Format("Method msut return InputTemplate ({0})", method), "method");
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name");

            // If we have an instance, make sure it is [Serializable].
            if (instance != null)
            {
                var type = instance.GetType();
                if (type.GetCustomAttribute<SerializableAttribute>(true) == null)
                    throw new ArgumentException(
                        string.Format(
                            "Instance used with {0} to construct a template must be [Serializable] but {1} is not",
                            method, type),
                        "instance");
            }

            var internedName = new InternedString(name);
            var isReplacement = HaveTemplate(internedName);

            m_Templates.templateConstructors[internedName] = new InputTemplate.Constructor
            {
                method = method,
                instance = instance
            };

            PerformTemplatePostRegistration(internedName, baseTemplate, deviceDescription, isReplacement);
        }

        private void PerformTemplatePostRegistration(InternedString name, string baseTemplate,
            InputDeviceDescription? deviceDescription, bool isReplacement, bool isKnownToBeDeviceTemplate = false)
        {
            ++m_TemplateSetupVersion;

            if (!string.IsNullOrEmpty(baseTemplate))
                m_Templates.baseTemplateTable[name] = new InternedString(baseTemplate);

            // Re-create any devices using the template.
            RecreateDevicesUsingTemplate(name, isKnownToBeDeviceTemplate: isKnownToBeDeviceTemplate);

            // If the template has a device description, see if it allows us
            // to make sense of any device we couldn't make sense of so far.
            if (deviceDescription != null && !deviceDescription.Value.empty)
                AddSupportedDevice(deviceDescription.Value, name);

            // Let listeners know.
            var change = isReplacement ? InputTemplateChange.Replaced : InputTemplateChange.Added;
            for (var i = 0; i < m_TemplateChangeListeners.Count; ++i)
                m_TemplateChangeListeners[i](name.ToString(), change);
        }

        private void AddSupportedDevice(InputDeviceDescription description, InternedString template)
        {
            m_SupportedDevices.Add(new SupportedDevice
            {
                description = description,
                template = template
            });

            // See if the new description to template mapping allows us to make
            // sense of a device we couldn't make sense of so far.
            for (var i = 0; i < m_AvailableDevices.Count; ++i)
            {
                var deviceId = m_AvailableDevices[i].deviceId;
                if (TryGetDeviceById(deviceId) != null)
                    continue;

                if (description.Matches(m_AvailableDevices[i].description))
                {
                    AddDevice(template, deviceId, description, m_AvailableDevices[i].isNative);
                }
            }
        }

        private void RecreateDevicesUsingTemplate(InternedString template, bool isKnownToBeDeviceTemplate = false)
        {
            if (m_Devices == null)
                return;

            List<InputDevice> devicesUsingTemplate = null;

            // Find all devices using the template.
            for (var i = 0; i < m_Devices.Length; ++i)
            {
                var device = m_Devices[i];

                bool usesTemplate;
                if (isKnownToBeDeviceTemplate)
                    usesTemplate = IsControlUsingTemplate(device, template);
                else
                    usesTemplate = IsControlOrChildUsingTemplateRecursive(device, template);

                if (usesTemplate)
                {
                    if (devicesUsingTemplate == null)
                        devicesUsingTemplate = new List<InputDevice>();
                    devicesUsingTemplate.Add(device);
                }
            }

            // If there's none, we're good.
            if (devicesUsingTemplate == null)
                return;

            // Remove and re-add the matching devices.
            var setup = new InputControlSetup(m_Templates);
            for (var i = 0; i < devicesUsingTemplate.Count; ++i)
            {
                var device = devicesUsingTemplate[i];

                ////TODO: preserve state where possible

                // Remove.
                RemoveDevice(device);

                // Re-setup device.
                setup.Setup(device.m_Template, device, device.m_Variant);
                var newDevice = setup.Finish();

                // Re-add.
                AddDevice(newDevice);
            }
        }

        private bool IsControlOrChildUsingTemplateRecursive(InputControl control, InternedString template)
        {
            // Check control itself.
            if (IsControlUsingTemplate(control, template))
                return true;

            // Check children.
            var children = control.children;
            for (var i = 0; i < children.Count; ++i)
                if (IsControlOrChildUsingTemplateRecursive(children[i], template))
                    return true;

            return false;
        }

        private bool IsControlUsingTemplate(InputControl control, InternedString template)
        {
            // Check direct match.
            if (control.template == template)
                return true;

            // Check base template chain.
            var baseTemplate = control.m_Template;
            while (m_Templates.baseTemplateTable.TryGetValue(baseTemplate, out baseTemplate))
                if (baseTemplate == template)
                    return true;

            return false;
        }

        public void RemoveTemplate(string name, string @namespace = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name");

            if (@namespace != null)
                name = string.Format("{0}::{1}", @namespace, name);

            var internedName = new InternedString(name);

            // Remove all devices using the template.
            for (var i = 0; m_Devices != null && i < m_Devices.Length;)
            {
                var device = m_Devices[i];
                if (IsControlOrChildUsingTemplateRecursive(device, internedName))
                {
                    RemoveDevice(device);
                }
                else
                {
                    ++i;
                }
            }

            // Remove template record.
            m_Templates.templateTypes.Remove(internedName);
            m_Templates.templateStrings.Remove(internedName);
            m_Templates.templateConstructors.Remove(internedName);
            m_Templates.baseTemplateTable.Remove(internedName);

            ////TODO: check all template inheritance chain for whether they are based on the template and if so
            ////      remove those templates, too

            // Let listeners know.
            for (var i = 0; i < m_TemplateChangeListeners.Count; ++i)
                m_TemplateChangeListeners[i](name, InputTemplateChange.Removed);
        }

        public InputTemplate TryLoadTemplate(InternedString name)
        {
            return m_Templates.TryLoadTemplate(name);
        }

        public string TryFindMatchingTemplate(InputDeviceDescription deviceDescription)
        {
            ////TODO: this will want to take overrides into account

            // See if we can match by description.
            for (var i = 0; i < m_SupportedDevices.Count; ++i)
            {
                ////REVIEW: we don't only want to find any match, we want to find the best match
                if (m_SupportedDevices[i].description.Matches(deviceDescription))
                    return m_SupportedDevices[i].template;
            }

            // No, so try to match by device class. If we have a "Gamepad" template,
            // for example, a device that classifies itself as a "Gamepad" will match
            // that template.
            //
            // NOTE: Have to make sure here that we get a device template and not a
            //       control template.
            if (!string.IsNullOrEmpty(deviceDescription.deviceClass))
            {
                var deviceClassLowerCase = new InternedString(deviceDescription.deviceClass);
                var type = m_Templates.GetControlTypeForTemplate(deviceClassLowerCase);
                if (type != null && typeof(InputDevice).IsAssignableFrom(type))
                    return deviceDescription.deviceClass;
            }

            return null;
        }

        private bool HaveTemplate(InternedString name)
        {
            return m_Templates.templateTypes.ContainsKey(name) ||
                m_Templates.templateStrings.ContainsKey(name) ||
                m_Templates.templateConstructors.ContainsKey(name);
        }

        public int ListTemplates(List<string> templates)
        {
            if (templates == null)
                throw new ArgumentNullException("templates");

            var countBefore = templates.Count;

            ////FIXME: this may add a name twice; also allocates

            templates.AddRange(m_Templates.templateTypes.Keys.Select(x => x.ToString()));
            templates.AddRange(m_Templates.templateStrings.Keys.Select(x => x.ToString()));
            templates.AddRange(m_Templates.templateConstructors.Keys.Select(x => x.ToString()));

            return templates.Count - countBefore;
        }

        public void RegisterProcessor(string name, Type type)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name");
            if (type == null)
                throw new ArgumentNullException("type");

            ////REVIEW: probably good to typecheck here but it would require dealing with generic type stuff

            var internedName = new InternedString(name);
            m_Processors[internedName] = type;
        }

        public Type TryGetProcessor(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name");

            Type type;
            var internedName = new InternedString(name);
            if (m_Processors.TryGetValue(internedName, out type))
                return type;
            return null;
        }

        public void RegisterModifier(string name, Type type)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name");
            if (type == null)
                throw new ArgumentNullException("type");

            var internedName = new InternedString(name);
            m_Modifiers[internedName] = type;
        }

        public Type TryGetModifier(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name");

            Type type;
            var internedName = new InternedString(name);
            if (m_Modifiers.TryGetValue(internedName, out type))
                return type;
            return null;
        }

        public IEnumerable<string> ListModifiers()
        {
            return m_Modifiers.Keys.Select(x => x.ToString());
        }

        // Processes a path specification that may match more than a single control.
        // Adds all controls that match to the given list.
        // Returns true if at least one control was matched.
        // Must not generate garbage!
        public bool TryGetControls(string path, List<InputControl> controls)
        {
            throw new NotImplementedException();
        }

        // Return the first match for the given path or null if no control matches.
        // Must not generate garbage!
        public InputControl TryGetControl(string path)
        {
            throw new NotImplementedException();
        }

        public InputControl GetControl(string path)
        {
            throw new NotImplementedException();
        }

        // Adds all controls that match the given path spec to the given list.
        // Returns number of controls added to the list.
        // NOTE: Does not create garbage.
        public int GetControls(string path, List<InputControl> controls)
        {
            if (string.IsNullOrEmpty(path))
                return 0;
            if (controls == null)
                throw new ArgumentNullException("controls");
            if (m_Devices == null)
                return 0;

            var deviceCount = m_Devices.Length;
            var numMatches = 0;
            for (var i = 0; i < deviceCount; ++i)
            {
                var device = m_Devices[i];
                numMatches += InputControlPath.TryFindControls(device, path, 0, controls);
            }

            return numMatches;
        }

        public void SetVariant(InputControl control, string variant)
        {
            if (control == null)
                throw new ArgumentNullException("control");
            if (string.IsNullOrEmpty(variant))
                variant = "Default";

            //how can we do this efficiently without having to take the control's device out of the system?

            throw new NotImplementedException();
        }

        public void SetUsage(InputDevice device, InternedString usage)
        {
            if (device == null)
                throw new ArgumentNullException("device");
            device.SetUsage(usage);

            // Notify listeners.
            for (var i = 0; i < m_DeviceChangeListeners.Count; ++i)
                m_DeviceChangeListeners[i](device, InputDeviceChange.UsageChanged);

            // Usage may affect current device so update.
            device.MakeCurrent();
        }

        ////TODO: make sure that no device or control with a '/' in the name can creep into the system

        public InputDevice AddDevice(Type type, string name = null)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            // Find the template name that the given type was registered with.
            // First just try the name of the type and see if that produces a hit.
            var templateName = new InternedString(type.Name);
            Type registeredType;
            if (!m_Templates.templateTypes.TryGetValue(templateName, out registeredType)
                || registeredType != type)
            {
                // Didn't produce a hit so crawl through all registered template types
                // and look for a match.
                templateName = new InternedString();
                foreach (var entry in m_Templates.templateTypes)
                {
                    if (entry.Value == type)
                    {
                        templateName = entry.Key;
                        break;
                    }
                }

                if (templateName.IsEmpty())
                    throw new ArgumentException(string.Format("Cannot find template registered for type '{0}'", type.Name),
                        "type");
            }

            Debug.Assert(!templateName.IsEmpty(), name);

            // Note that since we go through the normal by-name lookup here, this will
            // still work if the template from the type was override with a string template.
            return AddDevice(templateName);
        }

        // Creates a device from the given template and adds it to the system.
        // NOTE: Creates garbage.
        public InputDevice AddDevice(string template, string name = null)
        {
            if (string.IsNullOrEmpty(template))
                throw new ArgumentException("template");

            var internedTemplateName = new InternedString(template);

            var setup = new InputControlSetup(m_Templates);
            setup.Setup(internedTemplateName, null, new InternedString());
            var device = setup.Finish();

            if (!string.IsNullOrEmpty(name))
                device.m_Name = new InternedString(name);

            AddDevice(device);

            return device;
        }

        // Add device with a forced ID. Used when creating devices reported to us by native.
        private InputDevice AddDevice(string template, int deviceId, InputDeviceDescription description, bool isNative)
        {
            var setup = new InputControlSetup(m_Templates);
            setup.Setup(new InternedString(template), null, new InternedString());
            var device = setup.Finish();

            device.m_Id = deviceId;
            device.m_Description = description;

            // Default display name to product name.
            if (!string.IsNullOrEmpty(description.product))
                device.m_DisplayName = description.product;

            if (isNative)
                device.m_Flags |= InputDevice.Flags.Native;

            AddDevice(device);

            return device;
        }

        public void AddDevice(InputDevice device)
        {
            if (device == null)
                throw new ArgumentNullException("device");
            if (string.IsNullOrEmpty(device.template))
                throw new ArgumentException("Device has no associated template", "device");

            // Ignore if the same device gets added multiple times.
            if (ArrayHelpers.Contains(m_Devices, device))
                return;

            MakeDeviceNameUnique(device);
            AssignUniqueDeviceId(device);

            // Add to list.
            device.m_DeviceIndex = ArrayHelpers.Append(ref m_Devices, device);

            ////REVIEW: Not sure a full-blown dictionary is the right way here. Alternatives are to keep
            ////        a sparse array that directly indexes using the linearly increasing IDs (though that
            ////        may get large over time). Or to just do a linear search through m_Devices (but
            ////        that may end up tapping a bunch of memory locations in the heap to find the right
            ////        device; could be improved by sorting m_Devices by ID and picking a good starting
            ////        point based on the ID we have instead of searching from [0] always).
            m_DevicesById[device.id] = device;

            // Let InputStateBuffers know this device doesn't have any associated state yet.
            device.m_StateBlock.byteOffset = InputStateBlock.kInvalidOffset;

            // Let InputStateBuffers allocate state buffers.
            ReallocateStateBuffers();

            // Make the device current.
            device.MakeCurrent();

            // Let actions re-resolve their paths.
            InputActionSet.RefreshAllEnabledActions();

            // If the device wants automatic callbacks before input updates,
            // put it on the list.
            var beforeUpdateCallbackReceiver = device as IInputUpdateCallbackReceiver;
            if (beforeUpdateCallbackReceiver != null)
                onUpdate += beforeUpdateCallbackReceiver.OnUpdate;

            // If the device has state callbacks, make a note of it.
            var stateCallbackReceiver = device as IInputStateCallbackReceiver;
            if (stateCallbackReceiver != null)
            {
                InstallBeforeUpdateHookIfNecessary();
                device.m_Flags |= InputDevice.Flags.HasStateCallbacks;
                m_HaveDevicesWithStateCallbackReceivers = true;
            }

            // Notify listeners.
            for (var i = 0; i < m_DeviceChangeListeners.Count; ++i)
                m_DeviceChangeListeners[i](device, InputDeviceChange.Added);
        }

        public InputDevice AddDevice(InputDeviceDescription description)
        {
            return AddDevice(description, throwIfNoTemplateFound: true);
        }

        public InputDevice AddDevice(InputDeviceDescription description, bool throwIfNoTemplateFound, int deviceId = InputDevice.kInvalidDeviceId, bool isNative = false)
        {
            var template = TryFindMatchingTemplate(description);

            ////REVIEW: listeners registering new templates from in here may potentially lead to the creation of devices; should we disallow that?
            // Give listeners a shot to select/create a template.
            for (var i = 0; i < m_DeviceFindTemplateCallbacks.Count; ++i)
            {
                var newTemplate = m_DeviceFindTemplateCallbacks[i](deviceId, ref description, template, m_Runtime);
                if (!string.IsNullOrEmpty(newTemplate))
                {
                    template = newTemplate;
                    break;
                }
            }

            if (template == null)
            {
                if (throwIfNoTemplateFound)
                    throw new ArgumentException(string.Format("Cannot find template matching device description '{0}'", description), "description");
                return null;
            }

            var device = AddDevice(template, deviceId, description, isNative);
            device.m_Description = description;

            return device;
        }

        ////TODO: get current&all getters to update
        public void RemoveDevice(InputDevice device)
        {
            if (device == null)
                throw new ArgumentNullException("device");

            // If device has not been added, ignore.
            if (device.m_DeviceIndex == InputDevice.kInvalidDeviceIndex)
                return;

            // Remove state monitors while device index is still valid.
            RemoveStateChangeMonitors(device);

            // Remove from device array.
            var deviceIndex = device.m_DeviceIndex;
            ArrayHelpers.EraseAt(ref m_Devices, deviceIndex);
            device.m_DeviceIndex = InputDevice.kInvalidDeviceIndex;
            m_DevicesById.Remove(device.id);

            if (m_Devices != null)
            {
                var oldDeviceIndices = new int[m_Devices.Length];
                for (var i = 0; i < m_Devices.Length; ++i)
                {
                    oldDeviceIndices[i] = m_Devices[i].m_DeviceIndex;
                    m_Devices[i].m_DeviceIndex = i;
                }

                // Remove from state buffers.
                ReallocateStateBuffers(oldDeviceIndices);
            }
            else
            {
                // No more devices. Kill state buffers.
                m_StateBuffers.FreeAll();
            }

            // Unbake offset into global state buffers.
            device.BakeOffsetIntoStateBlockRecursive((uint)(-device.m_StateBlock.byteOffset));

            // Force enabled actions to remove controls from the device.
            // We've already set the device index to be invalid so we any attempts
            // by actions to uninstall state monitors will get ignored.
            InputActionSet.RefreshAllEnabledActions();

            // Kill before update callback, if applicable.
            var beforeUpdateCallbackReceiver = device as IInputUpdateCallbackReceiver;
            if (beforeUpdateCallbackReceiver != null)
                onUpdate -= beforeUpdateCallbackReceiver.OnUpdate;

            // Let listeners know.
            for (var i = 0; i < m_DeviceChangeListeners.Count; ++i)
                m_DeviceChangeListeners[i](device, InputDeviceChange.Removed);
        }

        public InputDevice TryGetDevice(string nameOrTemplate)
        {
            if (string.IsNullOrEmpty(nameOrTemplate))
                throw new ArgumentException("nameOrTemplate");

            if (m_Devices == null)
                return null;

            var nameOrTemplateLowerCase = nameOrTemplate.ToLower();

            for (var i = 0; i < m_Devices.Length; ++i)
            {
                var device = m_Devices[i];
                if (device.m_Name.ToLower() == nameOrTemplateLowerCase ||
                    device.m_Template.ToLower() == nameOrTemplateLowerCase)
                    return device;
            }

            return null;
        }

        public InputDevice GetDevice(string nameOrTemplate)
        {
            var device = TryGetDevice(nameOrTemplate);
            if (device == null)
                throw new Exception(string.Format("Cannot find device with name or template '{0}'", nameOrTemplate));

            return device;
        }

        public InputDevice TryGetDeviceById(int id)
        {
            InputDevice result;
            if (m_DevicesById.TryGetValue(id, out result))
                return result;
            return null;
        }

        // Adds any device that's been reported to the system but could not be matched to
        // a template to the given list.
        public int GetUnrecognizedDevices(List<InputDeviceDescription> descriptions)
        {
            if (descriptions == null)
                throw new ArgumentNullException("descriptions");

            var numFound = 0;
            for (var i = 0; i < m_AvailableDevices.Count; ++i)
            {
                if (TryGetDeviceById(m_AvailableDevices[i].deviceId) != null)
                    continue;

                descriptions.Add(m_AvailableDevices[i].description);
                ++numFound;
            }

            return numFound;
        }

        // Report the availability of a device. The system will try to find a template that matches
        // the device and instantiate it. If no template matches but a template is added some time
        // in the future, the device will be created when the template becomes available.
        public void ReportAvailableDevice(InputDeviceDescription description)
        {
            if (string.IsNullOrEmpty(description.product) && string.IsNullOrEmpty(description.manufacturer) &&
                string.IsNullOrEmpty(description.deviceClass))
                throw new ArgumentException(
                    "Description must have at least one of 'product', 'manufacturer', or 'deviceClass'",
                    "description");

            var deviceId = m_Runtime.AllocateDeviceId();
            ReportAvailableDevice(description, deviceId);
        }

        private void ReportAvailableDevice(InputDeviceDescription description, int deviceId, bool isNative = false)
        {
            try
            {
                // Try to turn it into a device instance.
                AddDevice(description, throwIfNoTemplateFound: false, deviceId: deviceId, isNative: isNative);
            }
            finally
            {
                // Remember it. Do this *after* the AddDevice() call above so that if there's
                // a listener creating templates on the fly we won't end up matching this device and
                // create an InputDevice right away (which would then conflict with the one we
                // create in AddDevice).
                m_AvailableDevices.Add(new AvailableDevice
                {
                    description = description,
                    deviceId = deviceId,
                    isNative = true
                });
            }
        }

        public void QueueEvent(InputEventPtr ptr)
        {
            m_Runtime.QueueEvent(ptr.data);
        }

        public unsafe void QueueEvent<TEvent>(ref TEvent inputEvent)
            where TEvent : struct, IInputEventTypeInfo
        {
            // Don't bother keeping the data on the managed side. Just stuff the raw data directly
            // into the native buffers. This also means this method is thread-safe.
            m_Runtime.QueueEvent((IntPtr)UnsafeUtility.AddressOf(ref inputEvent));
        }

        public void Update()
        {
            Update(InputUpdateType.Dynamic);
        }

        public void Update(InputUpdateType updateType)
        {
            m_Runtime.Update(updateType);
        }

        internal void Initialize(IInputRuntime runtime)
        {
            InitializeData();
            InstallRuntime(runtime);
            InstallGlobals();
        }

        internal void Destroy()
        {
            if (ReferenceEquals(InputTemplate.s_Templates.baseTemplateTable, m_Templates.baseTemplateTable))
                InputTemplate.s_Templates = new InputTemplate.Collection();
            if (ReferenceEquals(InputProcessor.s_Processors, m_Processors))
                InputProcessor.s_Processors = null;

            if (m_Runtime != null)
            {
                m_Runtime.onUpdate = null;
                m_Runtime.onDeviceDiscovered = null;
                m_Runtime.onBeforeUpdate = null;

                if (ReferenceEquals(InputRuntime.s_Runtime, m_Runtime))
                    InputRuntime.s_Runtime = null;
            }
        }

        internal void InitializeData()
        {
            m_Templates.Allocate();
            m_SupportedDevices = new List<SupportedDevice>();
            m_Processors = new Dictionary<InternedString, Type>();
            m_Modifiers = new Dictionary<InternedString, Type>();
            m_DevicesById = new Dictionary<int, InputDevice>();
            m_AvailableDevices = new List<AvailableDevice>();

            // Determine our default set of enabled update types. By
            // default we enable both fixed and dynamic update because
            // we don't know which one the user is going to use. The user
            // can manually turn off one of them to optimize operation.
            m_UpdateMask = InputUpdateType.Dynamic | InputUpdateType.Fixed;
#if UNITY_EDITOR
            m_UpdateMask |= InputUpdateType.Editor;
#endif
            m_CurrentUpdate = InputUpdateType.Dynamic;

            // Register templates.
            RegisterTemplate("Button", typeof(ButtonControl)); // Controls.
            RegisterTemplate("DiscreteButton", typeof(DiscreteButtonControl));
            RegisterTemplate("Key", typeof(KeyControl));
            RegisterTemplate("Axis", typeof(AxisControl));
            RegisterTemplate("Analog", typeof(AxisControl));
            RegisterTemplate("Digital", typeof(IntegerControl));
            RegisterTemplate("Integer", typeof(IntegerControl));
            RegisterTemplate("PointerPhase", typeof(PointerPhaseControl));
            RegisterTemplate("Vector2", typeof(Vector2Control));
            RegisterTemplate("Vector3", typeof(Vector3Control));
            RegisterTemplate("Magnitude2", typeof(Magnitude2Control));
            RegisterTemplate("Magnitude3", typeof(Magnitude3Control));
            RegisterTemplate("Quaternion", typeof(QuaternionControl));
            RegisterTemplate("Pose", typeof(PoseControl));
            RegisterTemplate("Stick", typeof(StickControl));
            RegisterTemplate("Dpad", typeof(DpadControl));
            RegisterTemplate("AnyKey", typeof(AnyKeyControl));
            RegisterTemplate("Touch", typeof(TouchControl));
            RegisterTemplate("Color", typeof(ColorControl));
            RegisterTemplate("Audio", typeof(AudioControl));

            RegisterTemplate("Gamepad", typeof(Gamepad)); // Devices.
            RegisterTemplate("Joystick", typeof(Joystick));
            RegisterTemplate("Keyboard", typeof(Keyboard));
            RegisterTemplate("Pointer", typeof(Pointer));
            RegisterTemplate("Mouse", typeof(Mouse));
            RegisterTemplate("Pen", typeof(Pen));
            RegisterTemplate("Touchscreen", typeof(Touchscreen));
            RegisterTemplate("HMD", typeof(HMD));
            RegisterTemplate("XRController", typeof(XRController));
            RegisterTemplate("Accelerometer", typeof(Accelerometer));
            RegisterTemplate("Gyroscope", typeof(Gyroscope));

            ////REVIEW: #if templates to the platforms they make sense on?

            // Register processors.
            RegisterProcessor("Invert", typeof(InvertProcessor));
            RegisterProcessor("Clamp", typeof(ClampProcessor));
            RegisterProcessor("Normalize", typeof(NormalizeProcessor));
            RegisterProcessor("Deadzone", typeof(DeadzoneProcessor));
            RegisterProcessor("Curve", typeof(CurveProcessor));
            RegisterProcessor("Sensitivity", typeof(SensitivityProcessor));

            #if UNITY_EDITOR
            RegisterProcessor("AutoWindowSpace", typeof(EditorWindowSpaceProcessor));
            #endif

            // Register action modifiers.
            RegisterModifier("Press", typeof(PressModifier));
            RegisterModifier("Hold", typeof(HoldModifier));
            RegisterModifier("Tap", typeof(TapModifier));
            RegisterModifier("SlowTap", typeof(SlowTapModifier));
            RegisterModifier("DoubleTap", typeof(DoubleTapModifier));
            RegisterModifier("Swipe", typeof(SwipeModifier));
        }

        internal void InstallRuntime(IInputRuntime runtime)
        {
            if (m_Runtime != null)
            {
                m_Runtime.onUpdate = null;
                m_Runtime.onBeforeUpdate = null;
                m_Runtime.onDeviceDiscovered = null;
            }

            m_Runtime = runtime;
            m_Runtime.onUpdate = OnUpdate;
            m_Runtime.onDeviceDiscovered = OnDeviceDiscovered;

            // We only hook NativeInputSystem.onBeforeUpdate if necessary.
            if (m_UpdateListeners.Count > 0 || m_HaveDevicesWithStateCallbackReceivers)
            {
                m_Runtime.onBeforeUpdate = OnBeforeUpdate;
                m_NativeBeforeUpdateHooked = true;
            }
        }

        // Revive after domain reload.
        internal void InstallGlobals()
        {
            InputTemplate.s_Templates = m_Templates;
            InputProcessor.s_Processors = m_Processors;

            // During domain reload, when called from RestoreState(), we will get here with m_Runtime being null.
            // InputSystemObject will invoke InstallGlobals() a second time after it has called InstallRuntime().
            InputRuntime.s_Runtime = m_Runtime;
        }

        // Bundles a template name and a device description.
        [Serializable]
        internal struct SupportedDevice
        {
            public InputDeviceDescription description;
            public InternedString template;
        }

        [Serializable]
        internal struct AvailableDevice
        {
            public InputDeviceDescription description;
            public int deviceId;
            public bool isNative;
        }

        // Used by EditorInputTemplateCache to determine whether its state is outdated.
        [NonSerialized] internal int m_TemplateSetupVersion;

        [NonSerialized] internal InputTemplate.Collection m_Templates;
        [NonSerialized] private Dictionary<InternedString, Type> m_Processors;
        [NonSerialized] private Dictionary<InternedString, Type> m_Modifiers;

        [NonSerialized] private List<SupportedDevice> m_SupportedDevices; // A record of all device descriptions found in templates.
        [NonSerialized] private List<AvailableDevice> m_AvailableDevices; // A record of all devices reported to the system (from native or user code).

        [NonSerialized] private InputDevice[] m_Devices;
        [NonSerialized] private Dictionary<int, InputDevice> m_DevicesById;

        [NonSerialized] internal InputUpdateType m_CurrentUpdate;
        [NonSerialized] private InputUpdateType m_UpdateMask; // Which of our update types are enabled.
        [NonSerialized] internal InputStateBuffers m_StateBuffers;

        // We track dynamic and fixed updates to know when we need to flip device front and back buffers.
        // Because events are only sent once, we may need to flip dynamic update buffers in fixed updates
        // and fixed update buffers in dynamic updates as we have to update both front buffers simultaneously.
        // We apply the following rules to track this:
        // 1) There can be dynamic updates without fixed updates BUT
        // 2) There cannot be fixed updates without dynamic updates AND
        // 3) Fixed updates precede dynamic updates.
        [NonSerialized] internal uint m_CurrentDynamicUpdateCount;
        [NonSerialized] internal uint m_CurrentFixedUpdateCount;

        // We don't use UnityEvents and thus don't persist the callbacks during domain reloads.
        // Restoration of UnityActions is unreliable and it's too easy to end up with double
        // registrations what will lead to all kinds of misbehavior.
        [NonSerialized] private InlinedArray<DeviceChangeListener> m_DeviceChangeListeners;
        [NonSerialized] private InlinedArray<DeviceFindTemplateCallback> m_DeviceFindTemplateCallbacks;
        [NonSerialized] private InlinedArray<TemplateChangeListener> m_TemplateChangeListeners;
        [NonSerialized] private InlinedArray<EventListener> m_EventListeners;
        [NonSerialized] private InlinedArray<UpdateListener> m_UpdateListeners;
        [NonSerialized] private bool m_NativeBeforeUpdateHooked;
        [NonSerialized] private bool m_HaveDevicesWithStateCallbackReceivers;

        [NonSerialized] private IInputRuntime m_Runtime;

        #if UNITY_EDITOR
        [NonSerialized] internal IInputDebugger m_Debugger;
        #endif

        ////REVIEW: Right now actions are pretty tightly tied into the system; should this be opened up more
        ////        to present mechanisms that the user could build different action systems on?

        // Maps a single control to an action interested in the control. If
        // multiple actions are interested in the same control, we will end up
        // processing the control repeatedly but we assume this is the exception
        // and so optimize for the case where there's only one action going to
        // a control.
        //
        // Split into two structures to keep data needed only when there is an
        // actual value change out of the data we need for doing the scanning.
        private struct StateChangeMonitorMemoryRegion
        {
            public uint offsetRelativeToDevice;
            public uint sizeInBits; // Size of memory region to compare.
            public uint bitOffset;
        }
        private struct StateChangeMonitorListener
        {
            public InputControl control;
            ////REVIEW: this could easily be generalized to take an arbitrary user object plus a "user data" value
            public InputAction action;
            public int bindingIndex;
        }

        ////TODO: optimize the lists away
        ////REVIEW: I think these can be organized smarter to make bookkeeping cheaper
        // Indices correspond with those in m_Devices.
        [NonSerialized] private List<StateChangeMonitorMemoryRegion>[] m_StateChangeMonitorMemoryRegions;
        [NonSerialized] private List<StateChangeMonitorListener>[] m_StateChangeMonitorListeners;
        [NonSerialized] private List<bool>[] m_StateChangeSignalled; ////TODO: make bitfield

        private struct ActionTimeout
        {
            public double time;
            public InputAction action;
            public int bindingIndex;
            public int modifierIndex;
        }

        [NonSerialized] private List<ActionTimeout> m_ActionTimeouts;

        ////TODO: move this out into a generic mechanism that produces change events
        ////TODO: support combining monitors for bitfields
        internal void AddStateChangeMonitor(InputControl control, InputAction action, int bindingIndex)
        {
            var device = control.device;
            Debug.Assert(device != null);

            var deviceIndex = device.m_DeviceIndex;

            // Allocate/reallocate monitor arrays, if necessary.
            if (m_StateChangeMonitorListeners == null)
            {
                var deviceCount = m_Devices.Length;
                m_StateChangeMonitorListeners = new List<StateChangeMonitorListener>[deviceCount];
                m_StateChangeMonitorMemoryRegions = new List<StateChangeMonitorMemoryRegion>[deviceCount];
                m_StateChangeSignalled = new List<bool>[deviceCount];
            }
            else if (m_StateChangeMonitorListeners.Length <= deviceIndex)
            {
                var deviceCount = m_Devices.Length;
                Array.Resize(ref m_StateChangeMonitorListeners, deviceCount);
                Array.Resize(ref m_StateChangeMonitorMemoryRegions, deviceCount);
                Array.Resize(ref m_StateChangeSignalled, deviceCount);
            }

            // Allocate lists, if necessary.
            var listeners = m_StateChangeMonitorListeners[deviceIndex];
            var memoryRegions = m_StateChangeMonitorMemoryRegions[deviceIndex];
            var signals = m_StateChangeSignalled[deviceIndex];
            if (listeners == null)
            {
                listeners = new List<StateChangeMonitorListener>();
                memoryRegions = new List<StateChangeMonitorMemoryRegion>();
                signals = new List<bool>();

                m_StateChangeMonitorListeners[deviceIndex] = listeners;
                m_StateChangeMonitorMemoryRegions[deviceIndex] = memoryRegions;
                m_StateChangeSignalled[deviceIndex] = signals;
            }

            // Add monitor.
            listeners.Add(new StateChangeMonitorListener {action = action, bindingIndex = bindingIndex, control = control});
            memoryRegions.Add(new StateChangeMonitorMemoryRegion
            {
                offsetRelativeToDevice = control.stateBlock.byteOffset - control.device.stateBlock.byteOffset,
                sizeInBits = control.stateBlock.sizeInBits,
                bitOffset = control.stateBlock.bitOffset
            });
            signals.Add(false);
        }

        private void RemoveStateChangeMonitors(InputDevice device)
        {
            if (m_StateChangeMonitorListeners == null)
                return;

            var deviceIndex = device.m_DeviceIndex;
            Debug.Assert(deviceIndex != InputDevice.kInvalidDeviceIndex);

            if (deviceIndex >= m_StateChangeMonitorListeners.Length)
                return;

            ArrayHelpers.EraseAt(ref m_StateChangeMonitorListeners, deviceIndex);
            ArrayHelpers.EraseAt(ref m_StateChangeMonitorMemoryRegions, deviceIndex);
            ArrayHelpers.EraseAt(ref m_StateChangeSignalled, deviceIndex);
        }

        ////REVIEW: better to to just pass device+action and remove all state change monitors for the pair?
        internal void RemoveStateChangeMonitor(InputControl control, InputAction action)
        {
            if (m_StateChangeMonitorListeners == null)
                return;

            var device = control.device;
            var deviceIndex = device.m_DeviceIndex;

            // Ignore if device has already been removed.
            if (deviceIndex == InputDevice.kInvalidDeviceIndex)
                return;

            // Ignore if there are no state monitors set up for the device.
            if (deviceIndex >= m_StateChangeMonitorListeners.Length)
                return;

            var listeners = m_StateChangeMonitorListeners[deviceIndex];
            var regions = m_StateChangeMonitorMemoryRegions[deviceIndex];
            var signals = m_StateChangeSignalled[deviceIndex];

            for (var i = 0; i < listeners.Count; ++i)
            {
                if (listeners[i].action == action && listeners[i].control == control)
                {
                    ////TODO: use InlinedArrays for these and only null out entries; clean up array when traversing it during processing
                    listeners.RemoveAt(i);
                    regions.RemoveAt(i);
                    signals.RemoveAt(i);
                    break;
                }
            }
        }

        internal void AddActionTimeout(InputAction action, double time, int bindingIndex, int modifierIndex)
        {
            if (m_ActionTimeouts == null)
                m_ActionTimeouts = new List<ActionTimeout>();

            m_ActionTimeouts.Add(new ActionTimeout
            {
                time = time,
                action = action,
                bindingIndex = bindingIndex,
                modifierIndex = modifierIndex
            });
        }

        internal void RemoveActionTimeout(InputAction action, int bindingIndex, int modifierIndex)
        {
            if (m_ActionTimeouts == null)
                return;

            for (var i = 0; i < m_ActionTimeouts.Count; ++i)
            {
                if (m_ActionTimeouts[i].action == action
                    && m_ActionTimeouts[i].bindingIndex == bindingIndex
                    && m_ActionTimeouts[i].modifierIndex == modifierIndex)
                {
                    ////TODO: leave state empty and compact array lazily on traversal
                    m_ActionTimeouts.RemoveAt(i);
                    break;
                }
            }
        }

        ////REVIEW: Make it so that device names *always* have a number appended? (i.e. Gamepad1, Gamepad2, etc. instead of Gamepad, Gamepad1, etc)

        private void MakeDeviceNameUnique(InputDevice device)
        {
            if (m_Devices == null)
                return;

            var name = device.name;
            var nameLowerCase = device.m_Name.ToLower();
            var nameIsUnique = false;
            var namesTried = 1;

            // Find unique name.
            while (!nameIsUnique)
            {
                nameIsUnique = true;
                for (var i = 0; i < m_Devices.Length; ++i)
                {
                    if (m_Devices[i].name.ToLower() == nameLowerCase)
                    {
                        name = string.Format("{0}{1}", device.name, namesTried);
                        nameLowerCase = name.ToLower();
                        nameIsUnique = false;
                        ++namesTried;
                        break;
                    }
                }
            }

            // If we have changed the name of the device, nuke all path strings in the control
            // hiearchy so that they will get re-recreated when queried.
            if (namesTried >= 1)
                ResetControlPathsRecursive(device);

            // Assign name.
            device.m_Name = new InternedString(name);
        }

        private void ResetControlPathsRecursive(InputControl control)
        {
            control.m_Path = null;

            var children = control.children;
            var childCount = children.Count;

            for (var i = 0; i < childCount; ++i)
                ResetControlPathsRecursive(children[i]);
        }

        private void AssignUniqueDeviceId(InputDevice device)
        {
            // If the device already has an ID, make sure it's unique.
            if (device.id != InputDevice.kInvalidDeviceId)
            {
                // Safety check to make sure out IDs are really unique.
                // Given they are assigned by the native system they should be fine
                // but let's make sure.
                var existingDeviceWithId = TryGetDeviceById(device.id);
                if (existingDeviceWithId != null)
                    throw new Exception(
                        string.Format("Duplicate device ID {0} detected for devices '{1}' and '{2}'", device.id,
                            device.name, existingDeviceWithId.name));
            }
            else
            {
                device.m_Id = m_Runtime.AllocateDeviceId();
            }
        }

        // (Re)allocates state buffers and assigns each device that's been added
        // a segment of the buffer. Preserves the current state of devices.
        private void ReallocateStateBuffers(int[] oldDeviceIndices = null)
        {
            var devices = m_Devices;
            var oldBuffers = m_StateBuffers;

            // Allocate new buffers.
            var newBuffers = new InputStateBuffers();
            var newStateBlockOffsets = newBuffers.AllocateAll(m_UpdateMask, devices);

            // Migrate state.
            newBuffers.MigrateAll(devices, newStateBlockOffsets, oldBuffers, oldDeviceIndices);

            // Install the new buffers.
            oldBuffers.FreeAll();
            m_StateBuffers = newBuffers;
            m_StateBuffers.SwitchTo(m_CurrentUpdate);

            ////TODO: need to update state change monitors
        }

        private void OnDeviceDiscovered(int deviceId, string deviceDescriptor)
        {
            // Parse description.
            var description = InputDeviceDescription.FromJson(deviceDescriptor);

            // Report it.
            ReportAvailableDevice(description, deviceId, isNative: true);
        }

        private void InstallBeforeUpdateHookIfNecessary()
        {
            if (m_NativeBeforeUpdateHooked || m_Runtime == null)
                return;

            m_Runtime.onBeforeUpdate = OnBeforeUpdate;
            m_NativeBeforeUpdateHooked = true;
        }

        private unsafe void OnBeforeUpdate(InputUpdateType updateType)
        {
            // For devices that have state callbacks, tell them we're carrying state over
            // into the next frame.
            if (m_HaveDevicesWithStateCallbackReceivers && updateType != InputUpdateType.BeforeRender) ////REVIEW: before-render handling is probably wrong
            {
                var stateBuffers = m_StateBuffers.GetBuffers(updateType);
                var isDynamicOrFixedUpdate =
                    updateType == InputUpdateType.Dynamic || updateType == InputUpdateType.Fixed;

                // For the sake of action state monitors, we need to be able to detect when
                // an OnCarryStateForward() method writes new values into a state buffer. To do
                // so, we create a temporary buffer, copy state blocks into that buffer, and then
                // run the normal action change logic on the temporary and the current state buffer.
                using (var tempBuffer = new NativeArray<byte>((int)m_StateBuffers.sizePerBuffer, Allocator.Temp))
                {
                    var tempBufferPtr = (byte*)tempBuffer.GetUnsafeReadOnlyPtr();
                    var time = Time.time;

                    for (var i = 0; i < m_Devices.Length; ++i)
                    {
                        var device = m_Devices[i];
                        if ((device.m_Flags & InputDevice.Flags.HasStateCallbacks) != InputDevice.Flags.HasStateCallbacks)
                            continue;

                        // Depending on update ordering, we are writing events into *upcoming* updates inside of
                        // OnUpdate(). E.g. we may receive an event in fixed update and write it concurrently into
                        // the fixed and dynamic update buffer for the device.
                        //
                        // This means that we have to be extra careful here not to overwrite state which has already
                        // been updated with events. To check for this, we simply determine whether the device's update
                        // count for the current update type already corresponds to the count of the upcoming update.
                        //
                        // NOTE: This is only relevant for non-editor updates.
                        if (isDynamicOrFixedUpdate)
                        {
                            if (updateType == InputUpdateType.Dynamic)
                            {
                                if (device.m_CurrentDynamicUpdateCount == m_CurrentDynamicUpdateCount + 1)
                                    continue; // Device already received state for upcoming dynamic update.
                            }
                            else if (updateType == InputUpdateType.Fixed)
                            {
                                if (device.m_CurrentFixedUpdateCount == m_CurrentFixedUpdateCount + 1)
                                    continue; // Device already received state for upcoming fixed update.
                            }
                        }

                        var deviceStateOffset = device.m_StateBlock.byteOffset;
                        var deviceStateSize = device.m_StateBlock.alignedSizeInBytes;

                        // Grab current front buffer.
                        var frontBuffer = stateBuffers.GetFrontBuffer(device.m_DeviceIndex);

                        // Copy to temporary buffer.
                        var statePtr = (byte*)frontBuffer.ToPointer() + deviceStateOffset;
                        var tempStatePtr = tempBufferPtr + deviceStateOffset;
                        UnsafeUtility.MemCpy(tempStatePtr, statePtr, deviceStateSize);

                        // Show to device.
                        if (((IInputStateCallbackReceiver)device).OnCarryStateForward(frontBuffer))
                        {
                            // Let listeners know the device's state has changed.
                            for (var n = 0; n < m_DeviceChangeListeners.Count; ++n)
                                m_DeviceChangeListeners[n](device, InputDeviceChange.StateChanged);

                            // Process action state change monitors.
                            if (ProcessStateChangeMonitors(i, new IntPtr(statePtr), new IntPtr(tempStatePtr),
                                    deviceStateSize, 0))
                            {
                                ////REVIEW: should this make the device current?
                                FireActionStateChangeNotifications(i, time);
                            }
                        }
                    }
                }
            }

            ////REVIEW: should we activate the buffers for the given update here?
            for (var i = 0; i < m_UpdateListeners.Count; ++i)
                m_UpdateListeners[i](updateType);
        }

        // When we have the C# job system, this should be a job and NativeInputSystem should double
        // buffer input between frames. On top, the state change detection in here can be further
        // split off and put in its own job(s) (might not yield a gain; might be enough to just have
        // this thing in a job). The system can easily sync on a fence when some control goes
        // to the global state buffers so the user won't ever know that updates happen in the background.
        //
        // NOTE: Update types do *NOT* say what the events we receive are for. The update type only indicates
        //       where in the Unity's application loop we got called from.
        internal unsafe void OnUpdate(InputUpdateType updateType, int eventCount, IntPtr eventData)
        {
            ////TODO: switch from Profiler to CustomSampler API
#if ENABLE_PROFILER
            // NOTE: This is *not* using try/finally as we've seen unreliability in the EndSample()
            //       execution (and we're not sure where it's coming from).
            Profiler.BeginSample("InputUpdate");
#endif

            // In the editor, we need to decide where to route state. Whenever the game is playing and
            // has focus, we route all input to play mode buffers. When the game is stopped or if any
            // of the other editor windows has focus, we route input to edit mode buffers.
            var gameIsPlayingAndHasFocus = true;
            var buffersToUseForUpdate = updateType;
#if UNITY_EDITOR
            gameIsPlayingAndHasFocus = InputConfiguration.LockInputToGame ||
                (UnityEditor.EditorApplication.isPlaying && Application.isFocused);

            if (updateType == InputUpdateType.Editor && gameIsPlayingAndHasFocus)
            {
                // For actions, it is important we have play mode buffers active when
                // fire change notifications.
                if (m_StateBuffers.m_DynamicUpdateBuffers.valid)
                    buffersToUseForUpdate = InputUpdateType.Dynamic;
                else
                    buffersToUseForUpdate = InputUpdateType.Fixed;
            }
#endif

            m_CurrentUpdate = updateType;
            m_StateBuffers.SwitchTo(buffersToUseForUpdate);

            ////REVIEW: which set of buffers should we have active when processing timeouts?
            if (m_ActionTimeouts != null && gameIsPlayingAndHasFocus) ////REVIEW: for now, making actions exclusive to play mode
                ProcessActionTimeouts();

            var isBeforeRenderUpdate = false;
            if (updateType == InputUpdateType.Dynamic)
                ++m_CurrentDynamicUpdateCount;
            else if (updateType == InputUpdateType.Fixed)
                ++m_CurrentFixedUpdateCount;
            else if (updateType == InputUpdateType.BeforeRender)
                isBeforeRenderUpdate = true;

            // Early out if there's no events to process.
            if (eventCount <= 0)
            {
                if (buffersToUseForUpdate != updateType)
                    m_StateBuffers.SwitchTo(updateType);
                #if ENABLE_PROFILER
                Profiler.EndSample();
                #endif
                return;
            }

            // Before render updates work in a special way. For them, we only want specific devices (and
            // sometimes even just specific controls on those devices) to be updated. What native will do is
            // it will *not* clear the event buffer after showing it to us. This means that in the next
            // normal update, we will see the same events again. This gives us a chance to only fish out
            // what we want.
            //
            // In before render updates, we will only access StateEvents and DeltaEvents (the latter should
            // be used to, for example, *only* update tracking on a device that also contains buttons -- which
            // should not get updated in berfore render).

            var currentEventPtr = (InputEvent*)eventData;
            var remainingEventCount = eventCount;

            // Handle events.
            while (remainingEventCount > 0)
            {
                InputDevice device = null;
                var doNotMakeDeviceCurrent = false;

                // Bump firstEvent up to the next unhandled event (in before-render updates
                // the event needs to be *both* unhandled *and* for a device with before
                // render updates enabled).
                while (remainingEventCount > 0)
                {
                    if (isBeforeRenderUpdate)
                    {
                        if (!currentEventPtr->handled)
                        {
                            device = TryGetDeviceById(currentEventPtr->deviceId);
                            if (device != null && device.updateBeforeRender)
                                break;
                        }
                    }
                    else if (!currentEventPtr->handled)
                        break;

                    currentEventPtr = InputEvent.GetNextInMemory(currentEventPtr);
                    --remainingEventCount;
                }
                if (remainingEventCount == 0)
                    break;

                // Give listeners a shot at the event.
                var listenerCount = m_EventListeners.Count;
                if (listenerCount > 0)
                {
                    for (var i = 0; i < listenerCount; ++i)
                        m_EventListeners[i](new InputEventPtr(currentEventPtr));
                    if (currentEventPtr->handled)
                        continue;
                }

                // Grab device for event. In before-render updates, we already had to
                // check the device.
                if (!isBeforeRenderUpdate)
                    device = TryGetDeviceById(currentEventPtr->deviceId);
                if (device == null)
                {
                    #if UNITY_EDITOR
                    if (m_Debugger != null)
                        m_Debugger.OnCannotFindDeviceForEvent(new InputEventPtr(currentEventPtr));
                    #endif

                    // No device found matching event. Consider it handled.
                    currentEventPtr->handled = true;
                    continue;
                }

                // Process.
                var currentEventType = currentEventPtr->type;
                var currentEventTime = currentEventPtr->time;
                switch (currentEventType)
                {
                    case StateEvent.Type:
                    case DeltaStateEvent.Type:

                        // Ignore the event if the last state update we received for the device was
                        // newer than this state event is.
                        if (currentEventTime < device.m_LastUpdateTime)
                        {
                            #if UNITY_EDITOR
                            if (m_Debugger != null)
                                m_Debugger.OnEventTimestampOutdated(new InputEventPtr(currentEventPtr), device);
                            #endif
                            break;
                        }

                        var deviceIndex = device.m_DeviceIndex;
                        var stateBlock = device.m_StateBlock;
                        var stateBlockSize = stateBlock.alignedSizeInBytes;
                        var stateOffset = 0u;
                        uint stateSize;
                        IntPtr statePtr;
                        FourCC stateFormat;

                        // Grab state data from event.
                        if (currentEventType == StateEvent.Type)
                        {
                            var stateEventPtr = (StateEvent*)currentEventPtr;
                            stateFormat = stateEventPtr->stateFormat;
                            stateSize = stateEventPtr->stateSizeInBytes;
                            statePtr = stateEventPtr->state;

                            // Ignore extra state at end of event.
                            if (stateSize > stateBlockSize)
                                stateSize = stateBlockSize;
                        }
                        else
                        {
                            var deltaEventPtr = (DeltaStateEvent*)currentEventPtr;
                            stateFormat = deltaEventPtr->stateFormat;
                            stateSize = deltaEventPtr->deltaStateSizeInBytes;
                            statePtr = deltaEventPtr->deltaState;
                            stateOffset = deltaEventPtr->stateOffset;

                            // Ignore extra state at end of event.
                            if (stateOffset + stateSize > stateBlockSize)
                            {
                                if (stateOffset >= stateBlockSize)
                                    break; // Entire delta state is out of range.

                                stateSize = stateBlockSize - stateOffset;
                            }
                        }

                        // Ignore state event if the format doesn't match.
                        if (stateBlock.format != stateFormat)
                        {
                            #if UNITY_EDITOR
                            if (m_Debugger != null)
                                m_Debugger.OnEventFormatMismatch(new InputEventPtr(currentEventPtr), device);
                            #endif
                            break;
                        }

                        // If the device has state callbacks, give it a shot at running custom logic on
                        // the new state before we integrate it into the system.
                        var deviceHasStateCallbacks = (device.m_Flags & InputDevice.Flags.HasStateCallbacks) ==
                            InputDevice.Flags.HasStateCallbacks;
                        if (deviceHasStateCallbacks)
                        {
                            ////FIXME: this will read state from the current update, then combine it with the new state, and then write into all states
                            var currentState = InputStateBuffers.GetFrontBuffer(deviceIndex);
                            var newState = new IntPtr((byte*)statePtr.ToPointer() - stateBlock.byteOffset);  // Account for device offset in buffers.

                            ((IInputStateCallbackReceiver)device).OnBeforeWriteNewState(currentState, newState);
                        }

                        // Before we update state, let change monitors compare the old and the new state.
                        // We do this instead of first updating the front buffer and then comparing to the
                        // back buffer as that would require a buffer flip for each state change in order
                        // for the monitors to work reliably. By comparing the *event* data to the current
                        // state, we can have multiple state events in the same frame yet still get reliable
                        // change notifications.
                        var haveSignalledMonitors =
                            gameIsPlayingAndHasFocus && ////REVIEW: for now making actions exclusive to player
                            ProcessStateChangeMonitors(deviceIndex, statePtr,
                                new IntPtr(InputStateBuffers.GetFrontBuffer(deviceIndex).ToInt64() + stateBlock.byteOffset),
                                stateSize, stateOffset);

                        // Buffer flip.
                        var needToCopyFromBackBuffer = false;
                        if (FlipBuffersForDeviceIfNecessary(device, updateType, gameIsPlayingAndHasFocus))
                        {
                            // In case of a delta state event we need to carry forward all state we're
                            // not updating. Instead of optimizing the copy here, we're just bringing the
                            // entire state forward.
                            if (currentEventType == DeltaStateEvent.Type)
                                needToCopyFromBackBuffer = true;
                        }

                        // Now write the state.
                        var deviceStateOffset = device.m_StateBlock.byteOffset + stateOffset;

#if UNITY_EDITOR
                        if (!gameIsPlayingAndHasFocus)
                        {
                            var buffer = m_StateBuffers.m_EditorUpdateBuffers.GetFrontBuffer(deviceIndex);
                            Debug.Assert(buffer != IntPtr.Zero);

                            if (needToCopyFromBackBuffer)
                                UnsafeUtility.MemCpy(
                                    (void*)(buffer.ToInt64() + (int)device.m_StateBlock.byteOffset),
                                    (void*)(m_StateBuffers.m_EditorUpdateBuffers.GetBackBuffer(deviceIndex).ToInt64() +
                                            (int)device.m_StateBlock.byteOffset),
                                    device.m_StateBlock.alignedSizeInBytes);

                            UnsafeUtility.MemCpy((void*)(buffer.ToInt64() + (int)deviceStateOffset), statePtr.ToPointer(), stateSize);
                        }
                        else
#endif
                        {
                            // For dynamic and fixed updates, we have to write into the front buffer
                            // of both updates as a state change event comes in only once and we have
                            // to reflect the most current state in both update types.
                            //
                            // If one or the other update is disabled, however, we will perform a single
                            // memcpy here.
                            if (m_StateBuffers.m_DynamicUpdateBuffers.valid)
                            {
                                var buffer = m_StateBuffers.m_DynamicUpdateBuffers.GetFrontBuffer(deviceIndex);
                                Debug.Assert(buffer != IntPtr.Zero);

                                if (needToCopyFromBackBuffer)
                                    UnsafeUtility.MemCpy(
                                        (void*)(buffer.ToInt64() + (int)device.m_StateBlock.byteOffset),
                                        (void*)(m_StateBuffers.m_DynamicUpdateBuffers.GetBackBuffer(deviceIndex).ToInt64() +
                                                (int)device.m_StateBlock.byteOffset),
                                        device.m_StateBlock.alignedSizeInBytes);

                                UnsafeUtility.MemCpy((void*)(buffer.ToInt64() + (int)deviceStateOffset), statePtr.ToPointer(), stateSize);
                            }
                            if (m_StateBuffers.m_FixedUpdateBuffers.valid)
                            {
                                var buffer = m_StateBuffers.m_FixedUpdateBuffers.GetFrontBuffer(deviceIndex);
                                Debug.Assert(buffer != IntPtr.Zero);

                                if (needToCopyFromBackBuffer)
                                    UnsafeUtility.MemCpy(
                                        (void*)(buffer.ToInt64() + (int)device.m_StateBlock.byteOffset),
                                        (void*)(m_StateBuffers.m_FixedUpdateBuffers.GetBackBuffer(deviceIndex).ToInt64() +
                                                (int)device.m_StateBlock.byteOffset),
                                        device.m_StateBlock.alignedSizeInBytes);

                                UnsafeUtility.MemCpy((void*)(buffer.ToInt64() + (int)deviceStateOffset), statePtr.ToPointer(), stateSize);
                            }
                        }

                        device.m_LastUpdateTime = currentEventTime;

                        // Notify listeners.
                        for (var i = 0; i < m_DeviceChangeListeners.Count; ++i)
                            m_DeviceChangeListeners[i](device, InputDeviceChange.StateChanged);

                        // Now that we've committed the new state to memory, if any of the change
                        // monitors fired, let the associated actions know.
                        ////FIXME: this needs to happen with player buffers active
                        if (haveSignalledMonitors)
                            FireActionStateChangeNotifications(deviceIndex, currentEventTime);

                        break;

                    case TextEvent.Type:
                        var textEventPtr = (TextEvent*)currentEventPtr;
                        ////TODO: handle UTF-32 to UTF-16 conversion properly
                        device.OnTextInput((char)textEventPtr->character);
                        break;

                    case DeviceRemoveEvent.Type:
                        RemoveDevice(device);
                        doNotMakeDeviceCurrent = true;
                        break;

                    case DeviceConfigurationEvent.Type:
                        device.OnConfigurationChanged();
                        for (var i = 0; i < m_DeviceChangeListeners.Count; ++i)
                            m_DeviceChangeListeners[i](device, InputDeviceChange.ConfigurationChanged);
                        break;
                }

                // Mark as processed.
                currentEventPtr->handled = true;
                if (remainingEventCount >= 1)
                {
                    currentEventPtr = InputEvent.GetNextInMemory(currentEventPtr);
                    --remainingEventCount;
                }

                ////TODO: we need to filter out noisy devices; PS4 controller, for example, just spams constant reports and thus will always make itself current
                ////      (check for actual change and only make current if state changed?)
                // Device received event so make it current except if we got a
                // device removal event.
                if (!doNotMakeDeviceCurrent)
                    device.MakeCurrent();
            }

            ////TODO: fire event that allows code to update state *from* state we just updated

            if (buffersToUseForUpdate != updateType)
                m_StateBuffers.SwitchTo((InputUpdateType)updateType);

#if ENABLE_PROFILER
            Profiler.EndSample();
#endif
        }

        // If anyone is listening for state changes on the given device, run state change detections
        // for the two given state blocks of the device. If a value that is covered by a monitor
        // has changed in 'newState' compared to 'oldState', set m_StateChangeSignalled for the
        // monitor to true.
        //
        // Returns true if any monitors got signalled, false otherwise.
        //
        // This could easily be spun off into jobs.
        //
        // NOTE: 'newState' can be a subset of the full state stored at 'oldState'. In this case,
        //       'newStateOffset' must give the offset into the full state and 'newStateSize' must
        //       give the size of memory slice to be updated.
        private unsafe bool ProcessStateChangeMonitors(int deviceIndex, IntPtr newState, IntPtr oldState, uint newStateSize, uint newStateOffset)
        {
            if (m_StateChangeMonitorListeners == null)
                return false;

            // We resize the monitor arrays only when someone adds to them so they
            // may be out of sync with the size of m_Devices.
            if (deviceIndex >= m_StateChangeMonitorListeners.Length)
                return false;

            var changeMonitors = m_StateChangeMonitorMemoryRegions[deviceIndex];
            if (changeMonitors == null)
                return false; // No action cares about state changes on this device.

            var signals = m_StateChangeSignalled[deviceIndex];

            var numMonitors = changeMonitors.Count;
            var signalled = false;

            // Bake offsets into state pointers so that we don't have to adjust for
            // them repeatedly.
            if (newStateOffset != 0)
            {
                newState = new IntPtr(newState.ToInt64() - newStateOffset);
                oldState = new IntPtr(oldState.ToInt64() + newStateOffset);
            }

            for (var i = 0; i < numMonitors; ++i)
            {
                var memoryRegion = changeMonitors[i];
                var offset = (int)memoryRegion.offsetRelativeToDevice;
                var sizeInBits = memoryRegion.sizeInBits;
                var bitOffset = memoryRegion.bitOffset;

                // If we've updated only part of the state, see if the monitored region and the
                // updated region overlap. Ignore monitor if they don't.
                if (newStateOffset != 0 &&
                    !MemoryHelpers.MemoryOverlapsBitRegion((uint)offset, bitOffset, sizeInBits, newStateOffset, (uint)newStateSize))
                    continue;

                // See if we are comparing bits or bytes.
                if (sizeInBits % 8 != 0 || bitOffset != 0)
                {
                    // Not-so-simple path: compare bits.

                    if (sizeInBits > 1)
                        throw new NotImplementedException("state change detection on multi-bit fields");

                    // Check if bit offset is out of range of state we have.
                    if (MemoryHelpers.ComputeFollowingByteOffset((uint)offset + newStateOffset, bitOffset) > newStateSize)
                        continue;

                    if (MemoryHelpers.ReadSingleBit(new IntPtr(newState.ToInt64() + offset), bitOffset) ==
                        MemoryHelpers.ReadSingleBit(new IntPtr(oldState.ToInt64() + offset), bitOffset))
                        continue;
                }
                else
                {
                    // Simple path: compare whole bytes.

                    var sizeInBytes = sizeInBits / 8;
                    if (offset - newStateOffset + sizeInBytes > newStateSize)
                        continue;

                    if (UnsafeUtility.MemCmp((byte*)newState.ToPointer() + offset, (byte*)oldState.ToPointer() + offset, sizeInBytes) == 0)
                        continue;
                }

                signals[i] = true;
                signalled = true;
            }

            return signalled;
        }

        private void FireActionStateChangeNotifications(int deviceIndex, double time)
        {
            var signals = m_StateChangeSignalled[deviceIndex];
            var listeners = m_StateChangeMonitorListeners[deviceIndex];

            for (var i = 0; i < signals.Count; ++i)
            {
                if (signals[i])
                {
                    var listener = listeners[i];
                    listener.action.NotifyControlValueChanged(listener.control, listener.bindingIndex, time);
                    signals[i] = false;
                }
            }
        }

        private void ProcessActionTimeouts()
        {
            var time = Time.time;
            for (var i = 0; i < m_ActionTimeouts.Count; ++i)
                if (m_ActionTimeouts[i].time <= time)
                {
                    m_ActionTimeouts[i].action.NotifyTimerExpired(m_ActionTimeouts[i].bindingIndex, m_ActionTimeouts[i].modifierIndex, time);
                    ////TODO: use plain array and compact entries on traversal
                    m_ActionTimeouts.RemoveAt(i);
                }
        }

        // Flip front and back buffer for device, if necessary. May flip buffers for more than just
        // the given update type.
        // Returns true if there was a buffer flip.
        private bool FlipBuffersForDeviceIfNecessary(InputDevice device, InputUpdateType updateType, bool gameIsPlayingAndHasFocus)
        {
            if (updateType == InputUpdateType.BeforeRender)
            {
                ////REVIEW: I think this is wrong; if we haven't flipped in the current dynamic or fixed update, we should do so now
                // We never flip buffers for before render. Instead, we already write
                // into the front buffer.
                return false;
            }

#if UNITY_EDITOR
            // Updates go to the editor only if the game isn't playing or does not have focus.
            // Otherwise we fall through to the logic that flips for the *next* dynamic and
            // fixed updates.
            if (updateType == InputUpdateType.Editor && !gameIsPlayingAndHasFocus)
            {
                // The editor doesn't really have a concept of frame-to-frame operation the
                // same way the player does. So we simply flip buffers on a device whenever
                // a new state event for it comes in.
                m_StateBuffers.m_EditorUpdateBuffers.SwapBuffers(device.m_DeviceIndex);
                return true;
            }
#endif

            var flipped = false;

            // If it is *NOT* a fixed update, we need to flip for the *next* coming fixed
            // update if we haven't already.
            if (updateType != InputUpdateType.Fixed &&
                device.m_CurrentFixedUpdateCount != m_CurrentFixedUpdateCount + 1)
            {
                m_StateBuffers.m_FixedUpdateBuffers.SwapBuffers(device.m_DeviceIndex);
                device.m_CurrentFixedUpdateCount = m_CurrentFixedUpdateCount + 1;
                flipped = true;
            }

            // If it is *NOT* a dynamic update, we need to flip for the *next* coming
            // dynamic update if we haven't already.
            if (updateType != InputUpdateType.Dynamic &&
                device.m_CurrentDynamicUpdateCount != m_CurrentDynamicUpdateCount + 1)
            {
                m_StateBuffers.m_DynamicUpdateBuffers.SwapBuffers(device.m_DeviceIndex);
                device.m_CurrentDynamicUpdateCount = m_CurrentDynamicUpdateCount + 1;
                flipped = true;
            }

            // If it *is* a fixed update and we haven't flipped for the current update
            // yet, do it.
            if (updateType == InputUpdateType.Fixed &&
                device.m_CurrentFixedUpdateCount != m_CurrentFixedUpdateCount)
            {
                m_StateBuffers.m_FixedUpdateBuffers.SwapBuffers(device.m_DeviceIndex);
                device.m_CurrentFixedUpdateCount = m_CurrentFixedUpdateCount;
                flipped = true;
            }

            // If it *is* a dynamic update and we haven't flipped for the current update
            // yet, do it.
            if (updateType == InputUpdateType.Dynamic &&
                device.m_CurrentDynamicUpdateCount != m_CurrentDynamicUpdateCount)
            {
                m_StateBuffers.m_DynamicUpdateBuffers.SwapBuffers(device.m_DeviceIndex);
                device.m_CurrentDynamicUpdateCount = m_CurrentDynamicUpdateCount;
                flipped = true;
            }

            return flipped;
        }

        ////TODO: reset device states for dynamic and fixed updates when going in and out of play mode
        ////REVIEW: shouldn't resets be visible to actions by generating proper change notifications?
        ////        (might be a better idea to instead send an state event with default state)
        private unsafe void ResetDeviceState(InputDevice device)
        {
            var offset = (int)device.m_StateBlock.byteOffset;
            var sizeInBytes = device.m_StateBlock.alignedSizeInBytes;
            var deviceIndex = device.m_DeviceIndex;

            if (m_StateBuffers.m_DynamicUpdateBuffers.valid)
            {
                UnsafeUtility.MemClear((void*)(m_StateBuffers.m_DynamicUpdateBuffers.GetFrontBuffer(deviceIndex).ToInt64() + offset), sizeInBytes);
                UnsafeUtility.MemClear((void*)(m_StateBuffers.m_DynamicUpdateBuffers.GetBackBuffer(deviceIndex).ToInt64() + offset), sizeInBytes);
            }

            if (m_StateBuffers.m_FixedUpdateBuffers.valid)
            {
                UnsafeUtility.MemClear((void*)(m_StateBuffers.m_FixedUpdateBuffers.GetFrontBuffer(deviceIndex).ToInt64() + offset), sizeInBytes);
                UnsafeUtility.MemClear((void*)(m_StateBuffers.m_FixedUpdateBuffers.GetBackBuffer(deviceIndex).ToInt64() + offset), sizeInBytes);
            }

#if UNITY_EDITOR
            UnsafeUtility.MemClear((void*)(m_StateBuffers.m_EditorUpdateBuffers.GetFrontBuffer(deviceIndex).ToInt64() + offset), sizeInBytes);
            UnsafeUtility.MemClear((void*)(m_StateBuffers.m_EditorUpdateBuffers.GetBackBuffer(deviceIndex).ToInt64() + offset), sizeInBytes);
#endif
        }

        // Domain reload survival logic. Also used for pushing and popping input system
        // state for testing.

        // Stuff everything that we want to survive a domain reload into
        // a m_SerializedState.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Serializable]
        internal struct DeviceState
        {
            // Preserving InputDevices is somewhat tricky business. Serializing
            // them in full would involve pretty nasty work. We have the restriction,
            // however, that everything needs to be created from templates (it partly
            // exists for the sake of reload survivability), so we should be able to
            // just go and recreate the device from the template. This also has the
            // advantage that if the template changes between reloads, the change
            // automatically takes effect.
            public string name;
            public string template;
            public string variant;
            public string[] usages;
            public int deviceId;
            public uint stateOffset;
            public InputDevice.Flags flags;
            public InputDeviceDescription description;

            public void RestoreUsagesOnDevice(InputDevice device)
            {
                if (usages == null || usages.Length == 0)
                    return;
                var index = ArrayHelpers.Append(ref device.m_UsagesForEachControl, usages.Select(x => new InternedString(x)));
                device.m_UsagesReadOnly =
                    new ReadOnlyArray<InternedString>(device.m_UsagesForEachControl, index, usages.Length);
                device.UpdateUsageArraysOnControls();
            }
        }

        [Serializable]
        internal struct TemplateState
        {
            public string name;
            public string typeNameOrJson;
        }

        [Serializable]
        internal struct BaseTemplateState
        {
            public string baseTemplate;
            public string derivedTemplate;
        }

        [Serializable]
        internal struct TemplateConstructorState
        {
            public string name;
            public string typeName;
            public string methodName;
            public string instanceJson;
        }

        [Serializable]
        internal struct TypeRegistrationState
        {
            public string name;
            public string typeName;

            public static TypeRegistrationState[] SaveState(Dictionary<InternedString, Type> table)
            {
                var count = table.Count;
                var array = new TypeRegistrationState[count];

                var i = 0;
                foreach (var entry in table)
                    array[i++] = new TypeRegistrationState
                    {
                        name = entry.Key,
                        typeName = entry.Value.AssemblyQualifiedName
                    };

                return array;
            }
        }

        [Serializable]
        internal struct SerializedState
        {
            public int templateSetupVersion;
            public TemplateState[] templateTypes;
            public TemplateState[] templateStrings;
            public TemplateConstructorState[] templateConstructors;
            public BaseTemplateState[] baseTemplates;
            public TypeRegistrationState[] processors;
            public TypeRegistrationState[] modifiers;
            public SupportedDevice[] supportedDevices;
            public DeviceState[] devices;
            public AvailableDevice[] availableDevices;
            public InputStateBuffers buffers;
            public InputConfiguration.SerializedState configuration;
            public InputUpdateType updateMask;

            // The rest is state that we want to preserve across Save() and Restore() but
            // not across domain reloads.

            [NonSerialized] public InlinedArray<DeviceChangeListener> deviceChangeListeners;
            [NonSerialized] public InlinedArray<DeviceFindTemplateCallback> deviceFindTemplateCallbacks;
            [NonSerialized] public InlinedArray<TemplateChangeListener> templateChangeListeners;
            [NonSerialized] public InlinedArray<EventListener> eventListeners;

            [NonSerialized] public IInputRuntime runtime;

            #if UNITY_EDITOR
            [NonSerialized] public IInputDebugger debugger;
            #endif
        }

        internal SerializedState SaveState()
        {
            // Template types.
            var templateTypeCount = m_Templates.templateTypes.Count;
            var templateTypeArray = new TemplateState[templateTypeCount];

            var i = 0;
            foreach (var entry in m_Templates.templateTypes)
                templateTypeArray[i++] = new TemplateState
                {
                    name = entry.Key,
                    typeNameOrJson = entry.Value.AssemblyQualifiedName
                };

            // Template strings.
            var templateStringCount = m_Templates.templateStrings.Count;
            var templateStringArray = new TemplateState[templateStringCount];

            i = 0;
            foreach (var entry in m_Templates.templateStrings)
                templateStringArray[i++] = new TemplateState
                {
                    name = entry.Key,
                    typeNameOrJson = entry.Value
                };

            // Template constructors.
            var templateConstructorCount = m_Templates.templateConstructors.Count;
            var templateConstructorArray = new TemplateConstructorState[templateConstructorCount];

            i = 0;
            foreach (var entry in m_Templates.templateConstructors)
                templateConstructorArray[i++] = new TemplateConstructorState
                {
                    name = entry.Key,
                    typeName = entry.Value.method.DeclaringType.AssemblyQualifiedName,
                    methodName = entry.Value.method.Name,
                    instanceJson = entry.Value.instance != null ? JsonUtility.ToJson(entry.Value.instance) : null,
                };

            // Devices.
            var deviceCount = m_Devices != null ? m_Devices.Length : 0;
            var deviceArray = new DeviceState[deviceCount];
            for (i = 0; i < deviceCount; ++i)
            {
                var device = m_Devices[i];
                var deviceState = new DeviceState
                {
                    name = device.name,
                    template = device.template,
                    variant = device.variant,
                    deviceId = device.id,
                    usages = device.usages.Select(x => x.ToString()).ToArray(),
                    stateOffset = device.m_StateBlock.byteOffset,
                    description = device.m_Description,
                    flags = device.m_Flags
                };
                deviceArray[i] = deviceState;
            }

            return new SerializedState
            {
                templateSetupVersion = m_TemplateSetupVersion,
                templateTypes = templateTypeArray,
                templateStrings = templateStringArray,
                templateConstructors = templateConstructorArray,
                baseTemplates = m_Templates.baseTemplateTable.Select(x => new BaseTemplateState { derivedTemplate = x.Key, baseTemplate = x.Value }).ToArray(),
                processors = TypeRegistrationState.SaveState(m_Processors),
                modifiers = TypeRegistrationState.SaveState(m_Modifiers),
                supportedDevices = m_SupportedDevices.ToArray(),
                devices = deviceArray,
                availableDevices = m_AvailableDevices.ToArray(),
                buffers = m_StateBuffers,
                configuration = InputConfiguration.Save(),
                deviceChangeListeners = m_DeviceChangeListeners.Clone(),
                deviceFindTemplateCallbacks = m_DeviceFindTemplateCallbacks.Clone(),
                templateChangeListeners = m_TemplateChangeListeners.Clone(),
                eventListeners = m_EventListeners.Clone(),
                updateMask = m_UpdateMask,
                runtime = m_Runtime,

                #if UNITY_EDITOR
                debugger = m_Debugger
                #endif
            };

            // We don't bring monitors along. InputActions and related classes are equipped
            // with their own domain reload survival logic that will plug actions back into
            // the system after reloads -- *if* the user is serializing them as part of
            // MonoBehaviours/ScriptableObjects.
        }

        internal void RestoreState(SerializedState state)
        {
            m_SupportedDevices = state.supportedDevices.ToList();
            m_StateBuffers = state.buffers;
            m_CurrentUpdate = InputUpdateType.Dynamic;
            m_AvailableDevices = state.availableDevices.ToList();
            m_Devices = null;
            m_HaveDevicesWithStateCallbackReceivers = false;
            m_TemplateSetupVersion = state.templateSetupVersion + 1;
            m_DeviceChangeListeners = state.deviceChangeListeners;
            m_DeviceFindTemplateCallbacks = state.deviceFindTemplateCallbacks;
            m_TemplateChangeListeners = state.templateChangeListeners;
            m_EventListeners = state.eventListeners;
            m_UpdateMask = state.updateMask;

            InitializeData();
            if (state.runtime != null)
                InstallRuntime(state.runtime);
            InstallGlobals();

            #if UNITY_EDITOR
            m_Debugger = state.debugger;
            #endif

            // Configuration.
            InputConfiguration.Restore(state.configuration);

            // Template types.
            foreach (var template in state.templateTypes)
            {
                var name = new InternedString(template.name);
                if (m_Templates.templateTypes.ContainsKey(name))
                    continue; // Don't overwrite builtins as they have been updated.
                var type = Type.GetType(template.typeNameOrJson, false);
                if (type != null)
                    m_Templates.templateTypes[name] = type;
                else
                    Debug.Log(string.Format("Input template '{0}' has been removed (type '{1}' cannot be found)",
                            template.name, template.typeNameOrJson));
            }

            // Template strings.
            foreach (var template in state.templateStrings)
            {
                var name = new InternedString(template.name);
                if (m_Templates.templateStrings.ContainsKey(name))
                    continue; // Don't overwrite builtins as they may have been updated.
                m_Templates.templateStrings[name] = template.typeNameOrJson;
            }

            // Template constructors.
            foreach (var template in state.templateConstructors)
            {
                var name = new InternedString(template.name);
                // Don't need to check for builtin version. We don't have builtin template
                // constructors.

                var type = Type.GetType(template.typeName, false);
                if (type == null)
                {
                    Debug.Log(string.Format("Template constructor '{0}' has been removed (type '{1}' cannot be found)",
                            name, template.typeName));
                    continue;
                }

                ////TODO: deal with overloaded methods

                var method = type.GetMethod(template.methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

                m_Templates.templateConstructors[name] = new InputTemplate.Constructor
                {
                    method = method,
                    instance = template.instanceJson != null ? JsonUtility.FromJson(template.instanceJson, type) : null
                };
            }

            // Base templates.
            if (state.baseTemplates != null)
                foreach (var entry in state.baseTemplates)
                {
                    var name = new InternedString(entry.derivedTemplate);
                    if (!m_Templates.baseTemplateTable.ContainsKey(name))
                        m_Templates.baseTemplateTable[name] = new InternedString(entry.baseTemplate);
                }

            // Processors.
            foreach (var processor in state.processors)
            {
                var name = new InternedString(processor.name);
                if (m_Processors.ContainsKey(name))
                    continue;
                var type = Type.GetType(processor.typeName, false);
                if (type != null)
                    m_Processors[name] = type;
                else
                    Debug.Log(string.Format("Input processor '{0}' has been removed (type '{1}' cannot be found)",
                            processor.name, processor.typeName));
            }

            // Modifiers.
            foreach (var modifier in state.modifiers)
            {
                var name = new InternedString(modifier.name);
                if (m_Modifiers.ContainsKey(name))
                    continue;
                var type = Type.GetType(modifier.typeName, false);
                if (type != null)
                    m_Modifiers[name] = Type.GetType(modifier.typeName, true);
                else
                    Debug.Log(string.Format("Input action modifier '{0}' has been removed (type '{1}' cannot be found)",
                            modifier.name, modifier.typeName));
            }

            // Re-create devices.
            var deviceCount = state.devices.Length;
            var devices = new InputDevice[deviceCount];
            var setup = new InputControlSetup(m_Templates);
            for (var i = 0; i < deviceCount; ++i)
            {
                var deviceState = state.devices[i];

                // See if we still have the template that the device used. Might have
                // come from a type that was removed in the meantime. If so, just
                // don't re-add the device.
                var template = new InternedString(deviceState.template);
                if (!m_Templates.HasTemplate(template))
                    continue;

                setup.Setup(template, null, new InternedString(deviceState.variant));
                var device = setup.Finish();
                device.m_Name = new InternedString(deviceState.name);
                device.m_Id = deviceState.deviceId;
                device.m_DeviceIndex = i;
                device.m_Description = deviceState.description;
                if (!string.IsNullOrEmpty(device.m_Description.product))
                    device.m_DisplayName = device.m_Description.product;
                device.m_Flags = deviceState.flags;
                deviceState.RestoreUsagesOnDevice(device);

                device.BakeOffsetIntoStateBlockRecursive(deviceState.stateOffset);
                device.MakeCurrent();

                devices[i] = device;
                m_DevicesById[device.m_Id] = device;

                // Re-install update callback, if necessary.
                var beforeUpdateCallbackReceiver = device as IInputUpdateCallbackReceiver;
                if (beforeUpdateCallbackReceiver != null)
                {
                    // Can't use onUpdate here as that will install the hook. Can't do that
                    // during deserialization.
                    m_UpdateListeners.Append(beforeUpdateCallbackReceiver.OnUpdate);
                }

                m_HaveDevicesWithStateCallbackReceivers |= (device.m_Flags & InputDevice.Flags.HasStateCallbacks) ==
                    InputDevice.Flags.HasStateCallbacks;
            }
            m_Devices = devices;

            ////TODO: retry to make sense of available devices that we couldn't make sense of before; maybe we have a template now

            // At the moment, there's no support for taking state across domain reloads
            // as we don't have support ATM for taking state across format changes.
            m_StateBuffers.FreeAll();

            ReallocateStateBuffers();
        }

        [SerializeField] private SerializedState m_SerializedState;

#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
#if UNITY_EDITOR
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            m_SerializedState = SaveState();
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            RestoreState(m_SerializedState);
            m_SerializedState = default(SerializedState);
        }

#endif
    }
}
