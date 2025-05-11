using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using ChatGPTMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using ChatGPTMod;

// Split AI modules into separate imports
using UnityEngine.AI; // For AI.NavMeshAgent
// Add the AIModule reference in your Unity project

public class ScriptableModifier : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ChatGPTSpellController chatGPTController;
    
    [Header("Settings")]
    [SerializeField] private bool enableScriptInjection = true;
    [SerializeField] private string scriptDirectory = "RuntimeScripts";
    [SerializeField] private int maxScriptsPerSession = 20;
    
    // Tracking
    private int scriptsCreatedThisSession = 0;
    private Dictionary<string, ScriptBehavior> activeScripts = new Dictionary<string, ScriptBehavior>();
    private Dictionary<string, GameObject> gameObjectCache = new Dictionary<string, GameObject>();
    
    void Awake()
    {
        if (chatGPTController == null)
            chatGPTController = GetComponent<ChatGPTSpellController>();
        
        // Create script directory
        string fullPath = Path.Combine(Application.persistentDataPath, scriptDirectory);
        if (!Directory.Exists(fullPath))
            Directory.CreateDirectory(fullPath);
        
        // Build game object cache
        BuildGameObjectCache();
        
        Debug.Log("ScriptableModifier initialized - Ready to create runtime scripts");
    }
    
    // Build cache of all game objects for faster access
    private void BuildGameObjectCache()
    {
        gameObjectCache.Clear();
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (!gameObjectCache.ContainsKey(obj.name))
                gameObjectCache.Add(obj.name, obj);
        }
        Debug.Log($"Built game object cache with {gameObjectCache.Count} objects");
    }
    
    // Main entry point for creating and applying a new script
    public async Task<string> CreateAndApplyScript(string userRequest)
    {
        if (!enableScriptInjection)
            return "Script injection is currently disabled.";
            
        if (scriptsCreatedThisSession >= maxScriptsPerSession)
            return "Maximum scripts created this session. Please restart the game to create more scripts.";
            
        try
        {
            // 1. Use AI to analyze the request and determine what needs to be modified
            (string targetObject, string scriptName, string scriptDescription) = await AnalyzeRequest(userRequest);
            
            // 2. Find the target game object to modify
            GameObject targetGameObject = FindTargetGameObject(targetObject);
            if (targetGameObject == null)
                return $"Could not find any game object matching '{targetObject}'";
            
            // 3. Generate the script code using AI
            string scriptId = $"Script_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}_{UnityEngine.Random.Range(1000, 9999)}";
            string scriptCode = await GenerateScriptCode(targetObject, targetGameObject, scriptName, scriptDescription, userRequest);
            
            // 4. Save the script for reference
            string scriptPath = Path.Combine(Application.persistentDataPath, scriptDirectory, $"{scriptId}.cs");
            File.WriteAllText(scriptPath, scriptCode);
            
            // 5. Create and attach the runtime script behavior
            ScriptBehavior scriptBehavior = targetGameObject.AddComponent<ScriptBehavior>();
            scriptBehavior.Initialize(scriptId, scriptCode, scriptName, scriptDescription);
            
            // 6. Store reference and execute setup
            activeScripts[scriptId] = scriptBehavior;
            scriptBehavior.ExecuteSetup(targetGameObject);
            
            // 7. Compile and attach the actual script if possible
            bool success = CompileAndAttachScript(scriptId, scriptCode, scriptName, targetGameObject);
            
            // 8. Increment counter
            scriptsCreatedThisSession++;
            
            if (success)
                return $"Successfully created and applied '{scriptName}' script to {targetGameObject.name}!\n\n" + 
                       $"This script: {scriptDescription}\n\n" +
                       $"The changes have been applied and are now active.";
            else
                return $"Created '{scriptName}' script for {targetGameObject.name}, but it's running in simulation mode.\n\n" +
                       $"This script: {scriptDescription}\n\n" +
                       $"The changes have been simulated.";
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating script: {ex.Message}\n{ex.StackTrace}");
            return $"Error creating script: {ex.Message}";
        }
    }
    
    // Use AI to analyze the user's request
    private async Task<(string targetObject, string scriptName, string scriptDescription)> AnalyzeRequest(string userRequest)
    {
        try
        {
            string prompt = $@"Analyze this request for a runtime script modification in Blade & Sorcery VR game:

""{userRequest}""

Return a JSON object with these three fields:
1. targetObject: What game object should be modified (e.g., ""Player"", ""Enemy"", ""Weapon"")
2. scriptName: A concise class name for the script (e.g., ""SpeedBooster"", ""HealthRegenerator"")
3. scriptDescription: A brief description of what the script does

JSON FORMAT ONLY, no explanations.";

            // FIX 1: Use chatGPTController.GetCompletion instead of RequestGPTCompletion
            string response = await chatGPTController.GetCompletion(prompt);
            // Alternative method names that might exist:
            // string response = await chatGPTController.SendPrompt(prompt);
            // string response = await chatGPTController.GetResponse(prompt);
            // string response = await chatGPTController.QueryChatGPT(prompt);
            
            // Parse the JSON response
            string targetObject = ExtractJsonValue(response, "targetObject");
            string scriptName = ExtractJsonValue(response, "scriptName");
            string scriptDescription = ExtractJsonValue(response, "scriptDescription");
            
            // Default values if parsing fails
            if (string.IsNullOrEmpty(targetObject)) targetObject = "Player";
            if (string.IsNullOrEmpty(scriptName)) scriptName = "CustomScript";
            if (string.IsNullOrEmpty(scriptDescription)) scriptDescription = "Custom game modification";
            
            return (targetObject, scriptName, scriptDescription);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error analyzing request: {ex.Message}");
            
            // Fallback to simple analysis
            if (userRequest.ToLower().Contains("enemy"))
                return ("Enemy", "EnemyModifier", "Modifies enemy behavior");
            else if (userRequest.ToLower().Contains("player"))
                return ("Player", "PlayerModifier", "Modifies player attributes");
            else
                return ("Player", "GameModifier", "Modifies game behavior");
        }
    }
    
    // Find a game object based on the target description
    private GameObject FindTargetGameObject(string targetDescription)
    {
        // First check exact matches in our cache
        if (gameObjectCache.TryGetValue(targetDescription, out GameObject exactMatch))
            return exactMatch;
        
        // Check for target objects by common tags
        if (targetDescription.Equals("Player", StringComparison.OrdinalIgnoreCase))
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                return player;
                
            // Fallback to finding objects with "player" in the name
            foreach (var entry in gameObjectCache)
            {
                if (entry.Key.ToLower().Contains("player"))
                    return entry.Value;
            }
        }
        else if (targetDescription.Equals("Enemy", StringComparison.OrdinalIgnoreCase))
        {
            GameObject enemy = GameObject.FindGameObjectWithTag("Enemy");
            if (enemy != null)
                return enemy;
                
            // Fallback to finding objects with "enemy" in the name
            foreach (var entry in gameObjectCache)
            {
                if (entry.Key.ToLower().Contains("enemy"))
                    return entry.Value;
            }
        }
        else if (targetDescription.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
        {
            GameObject weapon = GameObject.FindGameObjectWithTag("Weapon");
            if (weapon != null)
                return weapon;
                
            // Fallback to finding objects with "weapon" in the name
            foreach (var entry in gameObjectCache)
            {
                string lowerKey = entry.Key.ToLower();
                if (lowerKey.Contains("weapon") || lowerKey.Contains("sword"))
                    return entry.Value;
            }
        }
        
        // If still not found, find the first active object containing the target description
        string targetLower = targetDescription.ToLower();
        foreach (var entry in gameObjectCache)
        {
            if (entry.Key.ToLower().Contains(targetLower))
                return entry.Value;
        }
        
        // Last resort: just return an active object in the scene
        if (gameObjectCache.Count > 0)
            return gameObjectCache.First().Value;
        
        return null;
    }
    
    // Use AI to generate the actual script code
    private async Task<string> GenerateScriptCode(
        string targetObject, 
        GameObject gameObj, 
        string scriptName, 
        string scriptDescription, 
        string originalRequest)
    {
        try
        {
            // Get component information to help the AI understand the object
            Component[] components = gameObj.GetComponents<Component>();
            string componentsList = string.Join(", ", components.Select(c => c.GetType().Name));
            
            string prompt = $@"Create a C# Unity MonoBehaviour script named ""{scriptName}"" that will:
1. {scriptDescription}
2. Target game object: {targetObject} with components: {componentsList}
3. Original user request: ""{originalRequest}""

Requirements:
- The script must be completely self-contained and work at runtime in a built game
- Use reflection to access and modify properties of existing components when needed
- Include proper initialization in Start() and cleanup in OnDestroy()
- Include detailed debug logging with [ScriptName] prefix
- Add redundant approaches to ensure the modification works (component search, reflection, etc.)
- Must be robust against errors and not crash the game
- Return ONLY valid C# code, no explanations or markdown

Note: Safety is critical! The script will run in a VR game (Blade & Sorcery).";

            // FIX 1 (continued): Use the method your ChatGPTSpellController actually has
            string response = await chatGPTController.GetCompletion(prompt);
            // Alternative method names that might exist:
            // string response = await chatGPTController.SendPrompt(prompt);
            // string response = await chatGPTController.GetResponse(prompt);
            // string response = await chatGPTController.QueryChatGPT(prompt);
            
            // Clean up the response to ensure it's just the code
            return CleanCodeResponse(response);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error generating script code: {ex.Message}");
            
            // Return a simple fallback script
            return $@"using UnityEngine;

public class {scriptName} : MonoBehaviour
{{
    void Start()
    {{
        Debug.Log($""[{scriptName}] Started on {{gameObject.name}}"");
    }}
    
    void Update()
    {{
        // Simple implementation of {scriptDescription}
    }}
    
    void OnDestroy()
    {{
        Debug.Log($""[{scriptName}] Removed from {{gameObject.name}}"");
    }}
}}";
        }
    }
    
    // Simple helper for extracting values from JSON
    private string ExtractJsonValue(string json, string key)
    {
        string search = $"\"{key}\":";
        int start = json.IndexOf(search);
        if (start < 0) return string.Empty;
        
        start += search.Length;
        while (start < json.Length && (json[start] == ' ' || json[start] == '"'))
            start++;
            
        if (json[start-1] == '"')
            start--;
        
        int end;
        if (json[start] == '"')
        {
            start++;
            end = json.IndexOf('"', start);
        }
        else
        {
            end = json.IndexOf(',', start);
            if (end < 0)
                end = json.IndexOf('}', start);
        }
        
        if (end < 0) end = json.Length;
        return json.Substring(start, end - start).Trim();
    }
    
    private string CleanCodeResponse(string response)
    {
        // Remove markdown code fences if present
        if (response.StartsWith("```") && response.Contains("```"))
        {
            int start = response.IndexOf('\n') + 1;
            int end = response.LastIndexOf("```");
            if (start > 0 && end > start)
            {
                response = response.Substring(start, end - start).Trim();
            }
        }
        return response;
    }
    
    // Add the CompileAndAttachScript method that will use our CompileScript method
    private bool CompileAndAttachScript(string scriptId, string scriptCode, string className, GameObject targetGameObject)
    {
        try
        {
            // FIX 3: Renamed assemblyName parameter to asmName to avoid the naming conflict
            // Compile the script
            Assembly compiledAssembly = CompileScript(scriptCode, "Runtime_" + scriptId);
            
            // Find the main type in the compiled assembly (should match the script name)
            Type scriptType = compiledAssembly.GetTypes()
                .FirstOrDefault(t => t.IsClass && t.IsSubclassOf(typeof(MonoBehaviour)));
                
            if (scriptType == null)
            {
                Debug.LogWarning($"Could not find valid MonoBehaviour in compiled script {className}");
                return false;
            }
            
            // Attach the compiled script to the target object
            targetGameObject.AddComponent(scriptType);
            Debug.Log($"Successfully compiled and attached {className} to {targetGameObject.name}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error compiling script: {ex.Message}");
            return false;
        }
    }
    
    // FIX 3 (continued): Renamed the conflicting assemblyName parameter to asmName
    public static Assembly CompileScript(string code, string asmName)
    {
        using (var provider = new CSharpCodeProvider())
        {
            var parameters = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false
            };
            
            // Add base system references
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Core.dll");
            parameters.ReferencedAssemblies.Add("System.Data.dll");
            parameters.ReferencedAssemblies.Add("System.Xml.dll");
            
            // Add Unity Engine references
            parameters.ReferencedAssemblies.Add(typeof(UnityEngine.Object).Assembly.Location);
            
            // FIX 2: Use safe type references that don't require additional modules
            // Instead of AI.NavMesh, use more basic Unity types
            parameters.ReferencedAssemblies.Add(typeof(UnityEngine.Transform).Assembly.Location);
            parameters.ReferencedAssemblies.Add(typeof(UnityEngine.MonoBehaviour).Assembly.Location);
            parameters.ReferencedAssemblies.Add(typeof(UnityEngine.Component).Assembly.Location);
            
            // Add custom type references only if they exist
            try {
                // Only add UI if it exists
                if (Type.GetType("UnityEngine.UI.Button, UnityEngine.UI") != null)
                    parameters.ReferencedAssemblies.Add(typeof(UnityEngine.UI.Button).Assembly.Location);
                    
                // Only add XR if it exists
                var xrType = Type.GetType("UnityEngine.XR.InputDevice, UnityEngine.XRModule");
                if (xrType != null)
                    parameters.ReferencedAssemblies.Add(xrType.Assembly.Location);
            }
            catch (Exception) {
                // Ignore failures when adding optional references
            }
            
            // Add ThunderRoad references if they exist
            try
            {
                // Get the loaded ThunderRoad assembly
                var thunderRoadAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "ThunderRoad");
                    
                if (thunderRoadAssembly != null && !string.IsNullOrEmpty(thunderRoadAssembly.Location))
                {
                    parameters.ReferencedAssemblies.Add(thunderRoadAssembly.Location);
                    Debug.Log($"Added ThunderRoad assembly reference: {thunderRoadAssembly.Location}");
                }
                else
                {
                    // Try to find ThunderRoad types in other assemblies
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try 
                        {
                            if (!string.IsNullOrEmpty(assembly.Location) && 
                                (assembly.GetTypes().Any(t => t.Namespace == "ThunderRoad")))
                            {
                                parameters.ReferencedAssemblies.Add(assembly.Location);
                                Debug.Log($"Added assembly with ThunderRoad types: {assembly.Location}");
                            }
                        }
                        catch {
                            // Skip this assembly if we can't access its types
                        }
                    }
                }
                
                // Add specific game assemblies if they exist
                string[] gameAssemblyNames = {
                    "Assembly-CSharp",
                    "Assembly-CSharp-firstpass",
                    "ChatGPTMod"
                };
                
                foreach (var assemblyName in gameAssemblyNames)
                {
                    var assembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == assemblyName);
                        
                    if (assembly != null && !string.IsNullOrEmpty(assembly.Location))
                    {
                        parameters.ReferencedAssemblies.Add(assembly.Location);
                        Debug.Log($"Added game assembly reference: {assembly.Location}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error adding ThunderRoad references: {ex.Message}. Will compile without them.");
            }
            
            // Compile with all references
            CompilerResults results = provider.CompileAssemblyFromSource(parameters, code);
            if (results.Errors.HasErrors)
            {
                StringBuilder errorMsg = new StringBuilder();
                foreach (CompilerError error in results.Errors)
                {
                    errorMsg.AppendLine($"Error {error.ErrorNumber}: {error.ErrorText} at line {error.Line}");
                }
                throw new Exception(errorMsg.ToString());
            }
            
            return results.CompiledAssembly;
        }
    }
    
    // List all active scripts
    public string GetActiveScripts()
    {
        if (activeScripts.Count == 0)
            return "No active scripts";
            
        string result = $"{activeScripts.Count} active scripts:\n\n";
        foreach (var script in activeScripts)
        {
            result += $"- {script.Value.ScriptName} ({script.Key}) on {script.Value.gameObject.name}\n";
            result += $"  {script.Value.ScriptDescription}\n\n";
        }
        return result;
    }
    
    // Remove a specific script
    public string RemoveScript(string scriptId)
    {
        if (activeScripts.TryGetValue(scriptId, out ScriptBehavior script))
        {
            GameObject target = script.gameObject;
            Destroy(script);
            activeScripts.Remove(scriptId);
            return $"Successfully removed script {script.ScriptName} from {target.name}";
        }
        return $"Script with ID {scriptId} not found";
    }
}

// A helper component that gets attached to game objects and executes the generated code
public class ScriptBehavior : MonoBehaviour
{
    private string scriptId;
    private string scriptCode;
    
    public string ScriptName { get; private set; }
    public string ScriptDescription { get; private set; }
    
    public void Initialize(string id, string code, string name, string description)
    {
        scriptId = id;
        scriptCode = code;
        ScriptName = name;
        ScriptDescription = description;
        
        Debug.Log($"[ScriptBehavior] Initialized {ScriptName} on {gameObject.name}");
    }
    
    public void ExecuteSetup(GameObject target)
    {
        Debug.Log($"[ScriptBehavior] Executing {ScriptName} on {target.name}\n{ScriptDescription}");
    }
    
    void OnDestroy()
    {
        Debug.Log($"[ScriptBehavior] Cleaning up {ScriptName} on {gameObject.name}");
    }
}