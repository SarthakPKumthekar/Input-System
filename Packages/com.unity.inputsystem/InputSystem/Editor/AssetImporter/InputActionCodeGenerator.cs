#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Experimental.Input.Utilities;
using UnityEditor;

////TODO: unify the generated events so that performed, cancelled, and started all go into a single event

////TODO: look up actions and maps by ID rather than by name

////TODO: only generate @something if @ is really needed

////TODO: allow having an unnamed or default-named action set which spills actions directly into the toplevel wrapper

////TODO: add cleanup for ActionEvents

////TODO: nuke Clone()

////TODO: protect generated wrapper against modifications made to asset

////TODO: make capitalization consistent in the generated code

////REVIEW: allow putting *all* of the data from the inputactions asset into the generated class?

namespace UnityEngine.Experimental.Input.Editor
{
    /// <summary>
    /// Utility to generate code that makes it easier to work with action sets.
    /// </summary>
    public static class InputActionCodeGenerator
    {
        private const int kSpacesPerIndentLevel = 4;

        public struct Options
        {
            public string className { get; set; }
            public string namespaceName { get; set; }
            public string sourceAssetPath { get; set; }
            public bool generateEvents { get; set; }
            public bool generateInterfaces { get; set; }
        }

        public static string GenerateWrapperCode(InputActionAsset asset, Options options = new Options())
        {
            if (string.IsNullOrEmpty(options.sourceAssetPath))
                options.sourceAssetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(options.className) && !string.IsNullOrEmpty(asset.name))
                options.className =
                    CSharpCodeHelpers.MakeTypeName(asset.name);
            return GenerateWrapperCode(asset.actionMaps, asset.controlSchemes, options);
        }

        // Generate a string containing C# code that simplifies working with the given
        // action sets in code.
        public static string GenerateWrapperCode(IEnumerable<InputActionMap> maps, IEnumerable<InputControlScheme> schemes, Options options)
        {
            if (string.IsNullOrEmpty(options.className))
            {
                if (string.IsNullOrEmpty(options.sourceAssetPath))
                    throw new ArgumentException("options.sourceAssetPath");
                options.className =
                    CSharpCodeHelpers.MakeTypeName(Path.GetFileNameWithoutExtension(options.sourceAssetPath));
            }

            var writer = new Writer
            {
                buffer = new StringBuilder()
            };

            // Header.
            if (!string.IsNullOrEmpty(options.sourceAssetPath))
                writer.WriteLine($"// GENERATED AUTOMATICALLY FROM '{options.sourceAssetPath}'\n");

            // Usings.
            writer.WriteLine("using System;");
            writer.WriteLine("using UnityEngine;");
            if (options.generateEvents)
                writer.WriteLine("using UnityEngine.Events;");
            writer.WriteLine("using UnityEngine.Experimental.Input;");
            writer.WriteLine("\n");

            // Begin namespace.
            var haveNamespace = !string.IsNullOrEmpty(options.namespaceName);
            if (haveNamespace)
            {
                writer.WriteLine($"namespace {options.namespaceName}");
                writer.BeginBlock();
            }

            // Begin class.
            writer.WriteLine("[Serializable]");
            writer.WriteLine($"public class {options.className} : InputActionAssetReference");
            writer.BeginBlock();

            // Default constructor.
            writer.WriteLine($"public {options.className}()");
            writer.BeginBlock();
            writer.EndBlock();

            // Explicit constructor.
            writer.WriteLine($"public {options.className}(InputActionAsset asset)");
            ++writer.indentLevel;
            writer.WriteLine(": base(asset)");
            --writer.indentLevel;
            writer.BeginBlock();
            writer.EndBlock();

            // Initialize method.
            writer.WriteLine("[NonSerialized] private bool m_Initialized;");
            writer.WriteLine("private void Initialize()");
            writer.BeginBlock();
            foreach (var set in maps)
            {
                var setName = CSharpCodeHelpers.MakeIdentifier(set.name);
                writer.WriteLine($"// {set.name}");
                writer.WriteLine($"m_{setName} = asset.GetActionMap(\"{set.name}\");");

                foreach (var action in set.actions)
                {
                    var actionName = CSharpCodeHelpers.MakeIdentifier(action.name);
                    writer.WriteLine($"m_{setName}_{actionName} = m_{setName}.GetAction(\"{action.name}\");");

                    if (options.generateEvents)
                    {
                        WriteActionEventInitializer(setName, actionName, InputActionPhase.Started, writer);
                        WriteActionEventInitializer(setName, actionName, InputActionPhase.Performed, writer);
                        WriteActionEventInitializer(setName, actionName, InputActionPhase.Cancelled, writer);
                    }
                }
            }
            writer.WriteLine("m_Initialized = true;");
            writer.EndBlock();

            // Uninitialize method.
            writer.WriteLine("private void Uninitialize()");
            writer.BeginBlock();
            foreach (var map in maps)
            {
                var mapName = CSharpCodeHelpers.MakeIdentifier(map.name);

                if (options.generateInterfaces)
                {
                    var mapTypeName = CSharpCodeHelpers.MakeTypeName(map.name, "Actions");
                    writer.WriteLine($"if (m_{mapTypeName}CallbackInterface != null)");
                    writer.BeginBlock();
                    writer.WriteLine($"{mapName}.SetCallbacks(null);");
                    writer.EndBlock();
                }

                writer.WriteLine($"m_{mapName} = null;");

                foreach (var action in map.actions)
                {
                    var actionName = CSharpCodeHelpers.MakeIdentifier(action.name);
                    writer.WriteLine($"m_{mapName}_{actionName} = null;");

                    if (options.generateEvents)
                    {
                        WriteActionEventInitializer(mapName, actionName, InputActionPhase.Started, writer, removeCallback: true);
                        WriteActionEventInitializer(mapName, actionName, InputActionPhase.Performed, writer, removeCallback: true);
                        WriteActionEventInitializer(mapName, actionName, InputActionPhase.Cancelled, writer, removeCallback: true);
                    }
                }
            }
            writer.WriteLine("m_Initialized = false;");
            writer.EndBlock();

            // SwitchAsset method.
            writer.WriteLine("public void SetAsset(InputActionAsset newAsset)");
            writer.BeginBlock();
            writer.WriteLine("if (newAsset == asset) return;");
            if (options.generateInterfaces)
            {
                foreach (var map in maps)
                {
                    var mapName = CSharpCodeHelpers.MakeIdentifier(map.name);
                    var mapTypeName = CSharpCodeHelpers.MakeTypeName(map.name, "Actions");
                    writer.WriteLine($"var {mapName}Callbacks = m_{mapTypeName}CallbackInterface;");
                }
            }
            writer.WriteLine("if (m_Initialized) Uninitialize();");
            writer.WriteLine("asset = newAsset;");
            if (options.generateInterfaces)
            {
                foreach (var map in maps)
                {
                    var mapName = CSharpCodeHelpers.MakeIdentifier(map.name);
                    writer.WriteLine(string.Format("{0}.SetCallbacks({0}Callbacks);", mapName));
                }
            }
            writer.EndBlock();

            // MakePrivateCopyOfActions method.
            writer.WriteLine("public override void MakePrivateCopyOfActions()");
            writer.BeginBlock();
            writer.WriteLine("SetAsset(ScriptableObject.Instantiate(asset));");
            writer.EndBlock();

            // Action map accessors.
            foreach (var map in maps)
            {
                writer.WriteLine($"// {map.name}");

                var mapName = CSharpCodeHelpers.MakeIdentifier(map.name);
                var mapTypeName = CSharpCodeHelpers.MakeTypeName(mapName, "Actions");

                // Caching field for action map.
                writer.WriteLine($"private InputActionMap m_{mapName};");
                if (options.generateInterfaces)
                    writer.WriteLine(string.Format("private I{0} m_{0}CallbackInterface;", mapTypeName));

                // Caching fields for all actions.
                foreach (var action in map.actions)
                {
                    var actionName = CSharpCodeHelpers.MakeIdentifier(action.name);
                    writer.WriteLine($"private InputAction m_{mapName}_{actionName};");

                    if (options.generateEvents)
                    {
                        WriteActionEventField(mapName, actionName, InputActionPhase.Started, writer);
                        WriteActionEventField(mapName, actionName, InputActionPhase.Performed, writer);
                        WriteActionEventField(mapName, actionName, InputActionPhase.Cancelled, writer);
                    }
                }

                // Struct wrapping access to action set.
                writer.WriteLine($"public struct {mapTypeName}");
                writer.BeginBlock();

                // Constructor.
                writer.WriteLine($"private {options.className} m_Wrapper;");
                writer.WriteLine($"public {mapTypeName}({options.className} wrapper) {{ m_Wrapper = wrapper; }}");

                // Getter for each action.
                foreach (var action in map.actions)
                {
                    var actionName = CSharpCodeHelpers.MakeIdentifier(action.name);
                    writer.WriteLine(
                        $"public InputAction @{actionName} {{ get {{ return m_Wrapper.m_{mapName}_{actionName}; }} }}");

                    // Action event getters.
                    if (options.generateEvents)
                    {
                        WriteActionEventGetter(mapName, actionName, InputActionPhase.Started, writer);
                        WriteActionEventGetter(mapName, actionName, InputActionPhase.Performed, writer);
                        WriteActionEventGetter(mapName, actionName, InputActionPhase.Cancelled, writer);
                    }
                }

                // Action map getter.
                writer.WriteLine($"public InputActionMap Get() {{ return m_Wrapper.m_{mapName}; }}");

                // Enable/disable methods.
                writer.WriteLine("public void Enable() { Get().Enable(); }");
                writer.WriteLine("public void Disable() { Get().Disable(); }");
                writer.WriteLine("public bool enabled { get { return Get().enabled; } }");

                // Clone method.
                writer.WriteLine("public InputActionMap Clone() { return Get().Clone(); }");

                // Implicit conversion operator.
                writer.WriteLine(
                    $"public static implicit operator InputActionMap({mapTypeName} set) {{ return set.Get(); }}");

                // SetCallbacks method.
                if (options.generateInterfaces)
                {
                    writer.WriteLine($"public void SetCallbacks(I{mapTypeName} instance)");
                    writer.BeginBlock();

                    ////REVIEW: this would benefit from having a single callback on InputActions rather than three different endpoints

                    // Uninitialize existing interface.
                    writer.WriteLine($"if (m_Wrapper.m_{mapTypeName}CallbackInterface != null)");
                    writer.BeginBlock();
                    foreach (var action in map.actions)
                    {
                        var actionName = CSharpCodeHelpers.MakeIdentifier(action.name);
                        var actionTypeName = CSharpCodeHelpers.MakeTypeName(action.name);

                        writer.WriteLine($"{actionName}.started -= m_Wrapper.m_{mapTypeName}CallbackInterface.On{actionTypeName};");
                        writer.WriteLine($"{actionName}.performed -= m_Wrapper.m_{mapTypeName}CallbackInterface.On{actionTypeName};");
                        writer.WriteLine($"{actionName}.cancelled -= m_Wrapper.m_{mapTypeName}CallbackInterface.On{actionTypeName};");
                    }
                    writer.EndBlock();

                    // Initialize new interface.
                    writer.WriteLine($"m_Wrapper.m_{mapTypeName}CallbackInterface = instance;");
                    writer.WriteLine("if (instance != null)");
                    writer.BeginBlock();
                    foreach (var action in map.actions)
                    {
                        var actionName = CSharpCodeHelpers.MakeIdentifier(action.name);
                        var actionTypeName = CSharpCodeHelpers.MakeTypeName(action.name);

                        writer.WriteLine($"{actionName}.started += instance.On{actionTypeName};");
                        writer.WriteLine($"{actionName}.performed += instance.On{actionTypeName};");
                        writer.WriteLine($"{actionName}.cancelled += instance.On{actionTypeName};");
                    }
                    writer.EndBlock();

                    writer.EndBlock();
                }

                writer.EndBlock();

                // Getter for instance of struct.
                writer.WriteLine($"public {mapTypeName} @{mapName}");
                writer.BeginBlock();

                writer.WriteLine("get");
                writer.BeginBlock();
                writer.WriteLine("if (!m_Initialized) Initialize();");
                writer.WriteLine($"return new {mapTypeName}(this);");
                writer.EndBlock();

                writer.EndBlock();
            }

            // Control scheme accessors.
            foreach (var scheme in schemes)
            {
                var identifier = CSharpCodeHelpers.MakeIdentifier(scheme.name);

                writer.WriteLine($"private int m_{identifier}SchemeIndex = -1;");
                writer.WriteLine($"public InputControlScheme {identifier}Scheme");
                writer.BeginBlock();
                writer.WriteLine("get\n");
                writer.BeginBlock();
                writer.WriteLine($"if (m_{identifier}SchemeIndex == -1) m_{identifier}SchemeIndex = asset.GetControlSchemeIndex(\"{scheme.name}\");");
                writer.WriteLine($"return asset.controlSchemes[m_{identifier}SchemeIndex];");
                writer.EndBlock();
                writer.EndBlock();
            }

            // Action event class.
            if (options.generateEvents)
            {
                writer.WriteLine("[Serializable]");
                writer.WriteLine("public class ActionEvent : UnityEvent<InputAction.CallbackContext>");
                writer.BeginBlock();
                writer.EndBlock();
            }

            // End class.
            writer.EndBlock();

            // Generate interfaces.
            if (options.generateInterfaces)
            {
                foreach (var map in maps)
                {
                    var typeName = CSharpCodeHelpers.MakeTypeName(map.name);
                    writer.WriteLine($"public interface I{typeName}Actions");
                    writer.BeginBlock();

                    foreach (var action in map.actions)
                    {
                        var methodName = CSharpCodeHelpers.MakeTypeName(action.name);
                        writer.WriteLine($"void On{methodName}(InputAction.CallbackContext context);");
                    }

                    writer.EndBlock();
                }
            }

            // End namespace.
            if (haveNamespace)
                writer.EndBlock();

            return writer.buffer.ToString();
        }

        private static void WriteActionEventField(string setName, string actionName, InputActionPhase phase, Writer writer)
        {
            if (char.IsLower(actionName[0]))
                actionName = char.ToUpper(actionName[0]) + actionName.Substring(1);
            writer.WriteLine($"[SerializeField] private ActionEvent m_{setName}{actionName}Action{phase};");
        }

        private static void WriteActionEventGetter(string setName, string actionName, InputActionPhase phase, Writer writer)
        {
            var actionNameCased = actionName;
            if (char.IsLower(actionNameCased[0]))
                actionNameCased = char.ToUpper(actionNameCased[0]) + actionNameCased.Substring(1);

            writer.WriteLine(string.Format("public ActionEvent {1}{2} {{ get {{ return m_Wrapper.m_{0}{3}Action{2}; }} }}",
                setName, actionName, phase, actionNameCased));
        }

        private static void WriteActionEventInitializer(string setName, string actionName, InputActionPhase phase, Writer writer, bool removeCallback = false)
        {
            var actionNameCased = actionName;
            if (char.IsLower(actionNameCased[0]))
                actionNameCased = char.ToUpper(actionNameCased[0]) + actionNameCased.Substring(1);

            string callbackName;
            switch (phase)
            {
                case InputActionPhase.Started: callbackName = "started"; break;
                case InputActionPhase.Performed: callbackName = "performed"; break;
                case InputActionPhase.Cancelled: callbackName = "cancelled"; break;
                default:
                    throw new Exception("Internal error: No known callback for " + phase);
            }

            writer.WriteLine($"if (m_{setName}{actionNameCased}Action{phase} != null)");
            ++writer.indentLevel;
            writer.WriteLine($"m_{setName}_{CSharpCodeHelpers.MakeIdentifier(actionName)}.{callbackName} {(removeCallback ? "-" : "+")}= m_{setName}{actionNameCased}Action{phase}.Invoke;");
            --writer.indentLevel;
        }

        private struct Writer
        {
            public StringBuilder buffer;
            public int indentLevel;

            public void BeginBlock()
            {
                WriteIndent();
                buffer.Append("{\n");
                ++indentLevel;
            }

            public void EndBlock()
            {
                --indentLevel;
                WriteIndent();
                buffer.Append("}\n");
            }

            public void WriteLine(string text)
            {
                WriteIndent();
                buffer.Append(text);
                buffer.Append('\n');
            }

            private void WriteIndent()
            {
                for (var i = 0; i < indentLevel; ++i)
                {
                    for (var n = 0; n < kSpacesPerIndentLevel; ++n)
                        buffer.Append(' ');
                }
            }
        }

        // Updates the given file with wrapper code generated for the given action sets.
        // If the generated code is unchanged, does not touch the file.
        // Returns true if the file was touched, false otherwise.
        public static bool GenerateWrapperCode(string filePath, IEnumerable<InputActionMap> maps, IEnumerable<InputControlScheme> schemes, Options options)
        {
            // Generate code.
            var code = GenerateWrapperCode(maps, schemes, options);

            // Check if the code changed. Don't write if it hasn't.
            if (File.Exists(filePath))
            {
                var existingCode = File.ReadAllText(filePath);
                if (existingCode == code)
                    return false;
            }

            // Write.
            File.WriteAllText(filePath, code);
            return true;
        }
    }
}
#endif // UNITY_EDITOR
