using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Threading.Tasks;
using UnityEngine;
using System.Linq;
using System.Text;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class DynamicCodeReloader
{
    // Keeps track of loaded assemblies to prevent duplicates
    private Dictionary<string, Assembly> loadedDynamicAssemblies = new Dictionary<string, Assembly>();

    // Store AppDomains for isolation
    private List<AppDomain> dynamicDomains = new List<AppDomain>();

    // Plugin system
    private Dictionary<string, object> loadedPlugins = new Dictionary<string, object>();
    private string pluginsDirectory;

    // Configuration
    private bool useIsolatedDomains = true;
    private bool allowUnsafeCode = false;

    public DynamicCodeReloader(string pluginsPath = "Plugins")
    {
        // Set up plugins directory using safe path handling
        string basePath = Application.isEditor ? Application.dataPath : Application.persistentDataPath;
        pluginsDirectory = Path.Combine(basePath, pluginsPath);

        if (!Directory.Exists(pluginsDirectory))
        {
            try
            {
                Directory.CreateDirectory(pluginsDirectory);
                Debug.Log($"Created plugins directory at {pluginsDirectory}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create plugins directory: {ex.Message}");
            }
        }

        Debug.Log("Dynamic Code Reloader initialized");
    }

    /// <summary>
    /// Method 1: Assembly.Load approach for runtime code modification
    /// </summary>
    public Assembly CompileAndLoadAssembly(string sourceCode, string assemblyName)
    {
        Debug.Log($"Compiling dynamic assembly: {assemblyName}");

        try
        {
            // Setup compiler parameters
            CompilerParameters parameters = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false,
                IncludeDebugInformation = true,
                CompilerOptions = allowUnsafeCode ? "/unsafe" : ""
            };

            // Add references to Unity assemblies and other necessary assemblies
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Core.dll");
            parameters.ReferencedAssemblies.Add("UnityEngine.dll");
            parameters.ReferencedAssemblies.Add("ThunderRoad.dll");
            parameters.ReferencedAssemblies.Add(typeof(UnityEngine.Object).Assembly.Location);
            parameters.ReferencedAssemblies.Add(typeof(UnityEngine.UI.Button).Assembly.Location);

            // Add all currently loaded assemblies as references
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                    {
                        parameters.ReferencedAssemblies.Add(assembly.Location);
                    }
                }
                catch (Exception)
                {
                    // Some assemblies might throw exceptions when accessing their Location
                }
            }

            // Compile the code
            using (CSharpCodeProvider provider = new CSharpCodeProvider())
            {
                CompilerResults results = provider.CompileAssemblyFromSource(parameters, sourceCode);

                if (results.Errors.HasErrors)
                {
                    StringBuilder errorMessages = new StringBuilder();
                    foreach (CompilerError error in results.Errors)
                    {
                        errorMessages.AppendLine($"Line {error.Line}: {error.ErrorText}");
                    }

                    string errorLog = errorMessages.ToString();
                    Debug.LogError($"Compilation failed: {errorLog}");
                    return null;
                }

                Assembly compiledAssembly = results.CompiledAssembly;

                // Store in our dictionary
                if (loadedDynamicAssemblies.ContainsKey(assemblyName))
                {
                    loadedDynamicAssemblies[assemblyName] = compiledAssembly;
                }
                else
                {
                    loadedDynamicAssemblies.Add(assemblyName, compiledAssembly);
                }

                Debug.Log($"Successfully compiled assembly: {assemblyName}");
                return compiledAssembly;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error compiling assembly {assemblyName}: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Create an instance of a type from a dynamically loaded assembly
    /// </summary>
    public object CreateInstance(string assemblyName, string typeName, params object[] args)
    {
        try
        {
            if (loadedDynamicAssemblies.TryGetValue(assemblyName, out Assembly assembly))
            {
                Type type = assembly.GetType(typeName);
                if (type == null)
                {
                    Debug.LogError($"Type {typeName} not found in assembly {assemblyName}");
                    return null;
                }

                return Activator.CreateInstance(type, args);
            }

            Debug.LogError($"Assembly {assemblyName} not loaded");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating instance of {typeName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Load assembly in a separate AppDomain for isolation
    /// </summary>
    public bool LoadAssemblyInIsolatedDomain(string assemblyPath, string domainName)
    {
        if (!useIsolatedDomains)
        {
            Debug.LogWarning("Isolated domains are disabled. Using regular Assembly.Load instead.");
            return LoadAssemblyFromFile(assemblyPath) != null;
        }

        try
        {
            // Create new AppDomain
            AppDomainSetup setup = new AppDomainSetup();
            setup.ApplicationBase = AppDomain.CurrentDomain.BaseDirectory;

            AppDomain domain = AppDomain.CreateDomain(domainName, null, setup);
            dynamicDomains.Add(domain);

            // Create a proxy in the new domain to load the assembly
            AssemblyLoader loader = (AssemblyLoader)domain.CreateInstanceAndUnwrap(
                typeof(AssemblyLoader).Assembly.FullName,
                typeof(AssemblyLoader).FullName);

            bool success = loader.LoadAssembly(assemblyPath);

            Debug.Log($"Loaded assembly {Path.GetFileName(assemblyPath)} in isolated domain: {success}");
            return success;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading assembly in isolated domain: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Helper class to load assemblies across AppDomain boundaries
    /// </summary>
    public class AssemblyLoader : MarshalByRefObject
    {
        public bool LoadAssembly(string path)
        {
            try
            {
                Assembly.LoadFrom(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Method 2: Script reloading in editor (Unity Editor only)
    /// </summary>
    public bool ReloadScriptInEditor(string scriptPath)
    {
#if UNITY_EDITOR
        try
        {
            // Import the script asset
            AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);
            
            // Force Unity to recompile
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            
            Debug.Log($"Requested reload of script: {scriptPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error reloading script in editor: {ex.Message}");
            return false;
        }
#else
        Debug.LogWarning("ReloadScriptInEditor is only available in the Unity Editor");
        return false;
#endif
    }

    /// <summary>
    /// Method 3: External plugin system for hot-reloading
    /// </summary>
    public bool LoadPlugin(string pluginName, string sourceCode)
    {
        try
        {
            // Generate a unique filename for this plugin
            string timestamp = DateTime.Now.Ticks.ToString();
            string pluginFileName = $"{pluginName}_{timestamp}.dll";
            string pluginPath = Path.Combine(pluginsDirectory, pluginFileName);

            // Compile the plugin to a physical DLL
            CompilePluginToDll(sourceCode, pluginPath);

            // Load the compiled plugin
            Assembly pluginAssembly = LoadAssemblyFromFile(pluginPath);
            if (pluginAssembly == null)
            {
                return false;
            }

            // Find the main plugin class (assuming it follows naming convention)
            Type pluginType = pluginAssembly.GetTypes()
                .FirstOrDefault(t => t.Name.Equals(pluginName) ||
                                     t.Name.EndsWith("Plugin") ||
                                     t.GetInterfaces().Any(i => i.Name == "IPlugin"));

            if (pluginType == null)
            {
                Debug.LogError($"Could not find a valid plugin type in {pluginName}");
                return false;
            }

            // Create an instance of the plugin
            object pluginInstance = Activator.CreateInstance(pluginType);

            // Store the plugin
            if (loadedPlugins.ContainsKey(pluginName))
            {
                // Call Dispose on old plugin if it implements IDisposable
                if (loadedPlugins[pluginName] is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                loadedPlugins[pluginName] = pluginInstance;
            }
            else
            {
                loadedPlugins.Add(pluginName, pluginInstance);
            }

            // Initialize the plugin if it has an Initialize method
            MethodInfo initMethod = pluginType.GetMethod("Initialize");
            if (initMethod != null)
            {
                initMethod.Invoke(pluginInstance, null);
            }

            Debug.Log($"Successfully loaded plugin: {pluginName}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading plugin {pluginName}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Compile source code to a physical DLL file
    /// </summary>
    private bool CompilePluginToDll(string sourceCode, string outputPath)
    {
        try
        {
            CompilerParameters parameters = new CompilerParameters
            {
                GenerateInMemory = false,
                OutputAssembly = outputPath,
                GenerateExecutable = false,
                IncludeDebugInformation = true,
                CompilerOptions = allowUnsafeCode ? "/unsafe" : ""
            };

            // Add references
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Core.dll");
            parameters.ReferencedAssemblies.Add(typeof(UnityEngine.Object).Assembly.Location);

            // Add Unity-specific references
            string unityEngineDir = Path.GetDirectoryName(typeof(UnityEngine.Object).Assembly.Location);
            if (Directory.Exists(unityEngineDir))
            {
                string[] unityAssemblies = Directory.GetFiles(unityEngineDir, "UnityEngine*.dll");
                foreach (string assembly in unityAssemblies)
                {
                    parameters.ReferencedAssemblies.Add(assembly);
                }
            }

            // Compile
            using (CSharpCodeProvider provider = new CSharpCodeProvider())
            {
                CompilerResults results = provider.CompileAssemblyFromSource(parameters, sourceCode);

                if (results.Errors.HasErrors)
                {
                    foreach (CompilerError error in results.Errors)
                    {
                        Debug.LogError($"Line {error.Line}: {error.ErrorText}");
                    }
                    return false;
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error compiling plugin to DLL: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Load an assembly from file
    /// </summary>
    private Assembly LoadAssemblyFromFile(string path)
    {
        try
        {
            // First try LoadFrom which resolves dependencies better
            Assembly assembly = Assembly.LoadFrom(path);
            return assembly;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading assembly from file: {ex.Message}");

            try
            {
                // Fallback to Load which is more restrictive but sometimes works when LoadFrom fails
                byte[] assemblyBytes = File.ReadAllBytes(path);
                Assembly assembly = Assembly.Load(assemblyBytes);
                return assembly;
            }
            catch (Exception fallbackEx)
            {
                Debug.LogError($"Fallback loading also failed: {fallbackEx.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Call a method on a loaded plugin
    /// </summary>
    public object InvokePluginMethod(string pluginName, string methodName, params object[] args)
    {
        try
        {
            if (!loadedPlugins.TryGetValue(pluginName, out object pluginInstance))
            {
                Debug.LogError($"Plugin {pluginName} not loaded");
                return null;
            }

            Type pluginType = pluginInstance.GetType();
            MethodInfo method = pluginType.GetMethod(methodName);

            if (method == null)
            {
                Debug.LogError($"Method {methodName} not found in plugin {pluginName}");
                return null;
            }

            return method.Invoke(pluginInstance, args);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error invoking plugin method: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Clean up resources
    /// </summary>
    public void Cleanup()
    {
        // Dispose any plugins that implement IDisposable
        foreach (var plugin in loadedPlugins.Values)
        {
            if (plugin is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error disposing plugin: {ex.Message}");
                }
            }
        }

        // Unload any isolated domains
        foreach (AppDomain domain in dynamicDomains)
        {
            try
            {
                AppDomain.Unload(domain);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error unloading domain: {ex.Message}");
            }
        }

        dynamicDomains.Clear();
        loadedPlugins.Clear();
        loadedDynamicAssemblies.Clear();
    }
}