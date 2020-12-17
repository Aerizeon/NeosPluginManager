using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using BaseX;
using CloudX.Shared;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;

namespace NeosPluginManager
{
    [Category("Epsilion")]
    public class PluginManager : Component, ICustomInspector
    {
        public readonly Sync<int> NetworkVersion;
        public readonly Sync<bool> AllowUseInVanillaWorlds;

        public static Dictionary<string, Assembly> LoadedPlugins = new Dictionary<string, Assembly>();
        public static Dictionary<string, List<Type>> ActivePluginTypes = new Dictionary<string, List<Type>>();

        public static readonly HarmonyLib.Harmony HarmonyPatcher = new HarmonyLib.Harmony("com.aerizeon.neos.pluginmanager");
        private World lastFocusedWorld = null;


        /// <summary>
        /// Component initialized event
        /// </summary>
        protected override void OnAwake()
        {
            /*
             * Try to read the MSIL in the FrooxEngine.Initialize method, and find
             *  the constant that defines the current network version, so we can use
             *  it in our own calculations, and not rely on the user setting the
             *  value properly.
             */
            try
            {
                /*
                 * Get AsyncStateMachine attribute for FrooxEngine.Initialize, since it is an Async method
                 * This allows us to get the state machine's class from AsyncStateMachineAttribute.StateMachineType
                 */
                var asyncAttribute = typeof(FrooxEngine.Engine).GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance)?.GetCustomAttribute<AsyncStateMachineAttribute>();
                if (asyncAttribute != null && asyncAttribute.StateMachineType != null)
                {
                    //Get the .MoveNext method from our custom IAsyncStateMachine
                    var asyncTargetMethodInfo = asyncAttribute.StateMachineType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (asyncTargetMethodInfo != null)
                    {
                        //Read the MSIL from the MoveNext method
                        var ops = HarmonyLib.PatchProcessor.GetOriginalInstructions(asyncTargetMethodInfo);
                        /* 
                         * Store the method's local variable corresponding to a usage of ConcatenatedStream
                         * which occurs near the target value. We can begin searching from here.
                         */
                        var targetLocal = asyncTargetMethodInfo.GetMethodBody().LocalVariables.SingleOrDefault(l => l.LocalType == typeof(ConcatenatedStream));
                        for (int opIndex = 0; opIndex < ops.Count(); opIndex++)
                        {
                            var opcode = ops.ElementAt(opIndex);
                            //Check if this opcode corresponds to the previously defined local
                            if (opcode.IsLdloc(targetLocal as LocalBuilder))
                            {
                                //If so, check the opcode 2 places ahead to see if it's a call to BitConverter.GetBytes(int)
                                opcode = ops.ElementAt(opIndex + 2);
                                if (opcode.Is(OpCodes.Call, typeof(BitConverter).GetMethod("GetBytes", new Type[] { typeof(int) })))
                                {
                                    //If so, check that the opcode 1 place ahead is a constant
                                    opcode = ops.ElementAt(opIndex + 1);
                                    if (opcode != null && opcode.LoadsConstant())
                                    {
                                        //If it is a constant, then it's *probably* the correct constant for our purposes.
                                        if (opcode.operand is int version)
                                        {
                                            //Check if it's an int, and if so, assign it to NetworkVersion for use in OverrideHash below.
                                            NetworkVersion.Value = version;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UniLog.Log("Unable to determine Neos network version: " + ex);
            }

            //Override Neos' internal CompatabilityHash with the "correct" version
            OverrideHash(Engine, NetworkVersion.Value);

            //Load all of our patches through harmony.
            HarmonyPatcher.PatchAll();

            Engine.Cloud.Status.WorldManager.WorldFocused += WorldManager_WorldFocused;
            Userspace.UserspaceWorld.RunSynchronously(() =>
            {
                Userspace.UserspaceWorld.AddSlot("Plugin Manager Notification").AttachComponent<PluginNotifyWindow>();
            });
            NetworkVersion.OnValueChange += NetworkVersion_OnValueChange;
        }

        /// <summary>
        /// NetworkVersion changed
        /// </summary>
        /// <param name="syncField">networkVersion field</param>
        private void NetworkVersion_OnValueChange(SyncField<int> syncField)
        {
            OverrideHash(Engine, syncField.Value);
        }

        /// <summary>
        /// Component attach event
        /// </summary>
        protected override void OnAttach()
        {
            NetworkVersion.Value = 698;
            AllowUseInVanillaWorlds.Value = true;
        }

        /// <summary>
        /// World focus change event
        /// </summary>
        /// <param name="obj">the newly focused world</param>
        private void WorldManager_WorldFocused(World obj)
        {
            UniLog.Log("World Focused: " + obj.Name);

            if (lastFocusedWorld != null)
            {
                if (ActivePluginTypes.TryGetValue(lastFocusedWorld.SessionId, out List<Type> leavingWorldPlugins))
                    HidePluginTypes(leavingWorldPlugins);
                if (obj.HostUser.IsLocalUser && !lastFocusedWorld.HostUser.IsLocalUser)
                {
                    UniLog.Log("HostIsLocalUser");
                    ShowPluginTypes(new List<Type>() { typeof(PluginManager) }, "");
                }
                else if (!obj.HostUser.IsLocalUser && lastFocusedWorld.HostUser.IsLocalUser)
                {
                    UniLog.Log("HostIsNotLocalUser");
                    HidePluginTypes(new List<Type>() { typeof(PluginManager) }, "");
                }
            }
            if (ActivePluginTypes.TryGetValue(obj.SessionId, out List<Type> focusingWorldPlugins))
                ShowPluginTypes(focusingWorldPlugins);



            lastFocusedWorld = obj;
        }

        /// <summary>
        /// Neos' custom UI builder
        /// </summary>
        /// <param name="ui">the UIBuilder root from which to construct the UI</param>
        public void BuildInspectorUI(UIBuilder ui)
        {
            WorkerInspector.BuildInspectorUI(this, ui);
            
        }

        /// <summary>
        /// Calculates a new CompatabilityHash based on the specified networkVersion and
        /// the list of loaded plugins
        /// </summary>
        /// <param name="engine">FrooxEngine reference</param>
        /// <param name="networkVersion">the current network version from which to base the CompatabilityHash</param>
        /// <param name="loadedPlugins">additionally loaded assemblies, to factor into the CompatabilityHash</param>
        public static void OverrideHash(Engine engine, int networkVersion, Dictionary<string, string> loadedPlugins = null)
        {
            MD5CryptoServiceProvider csp = new MD5CryptoServiceProvider();
            ConcatenatedStream hashStream = new ConcatenatedStream();
            hashStream.EnqueueStream(new MemoryStream(BitConverter.GetBytes(networkVersion)));
            if (loadedPlugins != null)
            {
                string PluginsBase = PathUtility.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\Plugins\\";
                foreach (string assemblyPath in loadedPlugins.Keys)
                {
                    try
                    {
                        FileStream fileStream = File.OpenRead(PluginsBase + assemblyPath);
                        fileStream.Seek(375L, SeekOrigin.Current);
                        hashStream.EnqueueStream(fileStream);
                    }
                    catch
                    {
                        UniLog.Log("Failed to load assembly for hashing: " + PluginsBase + assemblyPath);
                    }
                }
            }
            
            SetHash(Convert.ToBase64String(csp.ComputeHash(hashStream)), "2020.Meow.Meow", engine);
        }

        /// <summary>
        /// Overrides the internal definitions of CompatabilityHash and Version String in FrooxEngine
        /// </summary>
        /// <param name="compatHash">the new CompatabilityHash string</param>
        /// <param name="appVersion">the new Version string</param>
        /// <param name="Engine">Reference to FrooxEngine</param>
        public static void SetHash(string compatHash, string appVersion, Engine Engine)
        {
            var versionStringField = typeof(Engine).GetField("_versionString", BindingFlags.NonPublic | BindingFlags.Static);
            var compatHashProperty = typeof(Engine).GetProperty("CompatibilityHash");
            var userStatusField = typeof(StatusManager).GetField("status", BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                versionStringField.SetValue(Engine, appVersion);
                compatHashProperty.SetValue(Engine, compatHash, null);
                if (userStatusField.GetValue(Engine.Cloud.Status) is UserStatus status)
                {
                    status.CompatibilityHash = compatHash;
                    status.NeosVersion = appVersion;
                }
                else
                    UniLog.Error("Failed to override UserStatus CompatibilityHash", false);
                
            }
            catch (Exception ex)
            {
                UniLog.Error("Failed to override Engine CompatibilityHash: " + ex.ToString(), false);
            }
        }

        /// <summary>
        /// Loads the requested assemblies into neos, and performs
        /// post-processing on FrooxEngine components
        /// </summary>
        /// <param name="sessionId">A unique ID for this session, to keep track of used plugins</param>
        /// <param name="requestedPlugins">A Dictionary of requested libraries and their respective versions</param>
        /// <returns>a bool indicating if plugins were successfully injected or not</returns>
        public static bool InjectPlugins(string sessionId, Dictionary<string, string> requestedPlugins)
        {
            /*
             * Try to parse the description string as JSON, and see if it has appropriate key:value pairs that
             * represent required plugins.
             * If so, try to load the plugins from the .\Plugins\ folder prior to world join, and inject them into Neos
             */
            try
            {
                string PluginsBase = PathUtility.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\Plugins\\";
                foreach (KeyValuePair<string, string> plugin in requestedPlugins)
                {
                    UniLog.Log("Requesting plugin: " + plugin.Key + " - " + plugin.Value);
                    if (!PluginManager.LoadedPlugins.TryGetValue(plugin.Key, out Assembly pluginAssembly) || PluginManager.LoadedPlugins[plugin.Key].GetName().Version.ToString() != plugin.Value)
                    {
                        if (Path.GetFullPath(PluginsBase + plugin.Key).StartsWith(PluginsBase))
                        {
                            UniLog.Log("Loading plugin from file: " + PluginsBase + plugin.Key);

                            PostX.NeosAssemblyPostProcessor.Process(PluginsBase + plugin.Key, Path.GetFullPath("Neos_Data\\Managed"));
                            pluginAssembly = Assembly.LoadFrom(PluginsBase + plugin.Key);
                            PluginManager.LoadedPlugins.Add(plugin.Key, pluginAssembly);
                            if (!ActivePluginTypes.TryGetValue(sessionId, out List<Type> pluginTypes))
                            {
                                pluginTypes = new List<Type>();
                                ActivePluginTypes.Add(sessionId, pluginTypes);
                            }

                            foreach (Type activeType in pluginAssembly.GetTypes())
                            {
                                pluginTypes.Add(activeType);
                            }    
                            
                            UniLog.Log("Plugin Loaded");
                        }
                        else
                        {
                            UniLog.Log("Path traversal attack detected in plugin: " + plugin.Key);
                        }
                    }
                    if(pluginAssembly != null)
                    {
                        ShowPluginTypes(new List<Type>(pluginAssembly.GetTypes()));
                    }
                }
            }
            catch (Exception ex)
            {
                UniLog.Log("Error loading plugin file: " + ex.ToString());
                return false;
            }
            return true;
        }

        /// <summary>
        /// Hides the plugins used in the specified session from the Component Attacher
        /// </summary>
        /// <param name="sessionId">ID of the session whose active plugins will be hidden</param>
        public static void HidePlugins(string sessionId)
        {
            UniLog.Log("Requesting Hide Plugins");
            if (ActivePluginTypes.TryGetValue(sessionId, out List<Type> pluginTypes))
            {
                if (pluginTypes != null)
                {
                    HidePluginTypes(pluginTypes);
                    pluginTypes.Clear();
                }
            }
        }

        /// <summary>
        /// Shows the specified plugin types in the Component Attacher
        /// </summary>
        /// <param name="pluginTypes">List of types to attempt to show, if they are a valid component</param>
        /// <param name="pathPrefix">prefix for component paths in the Component Attacher</param>
        public static void ShowPluginTypes(List<Type> pluginTypes, string pathPrefix = "Plugins//")
        {
            foreach (Type importedType in pluginTypes)
            {
                if (typeof(Component).IsAssignableFrom(importedType))
                {
                    string[] componentCategories = importedType.GetCustomAttribute<Category>().Paths;
                    if (componentCategories is null)
                        componentCategories = new[] { "Uncategorized" };
                    foreach (string categoryPath in componentCategories)
                    {
                        if (categoryPath != "Hidden")
                            WorkerInitializer.ComponentLibrary.GetSubcategory(pathPrefix + categoryPath).AddElement(importedType);
                    }
                }
            }
        }

        /// <summary>
        /// Hides the specified plugin types in the Component Attacher
        /// </summary>
        /// <param name="pluginTypes">List of types to attempt to hide, if they are a valid component</param>
        /// <param name="pathPrefix">prefix for component paths in the Component Attacher</param>
        public static void HidePluginTypes(List<Type> pluginTypes, string pathPrefix = "Plugins//")
        {
            FieldInfo elementListField = typeof(CategoryNode<Type>).GetField("_elements", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo sortedField = typeof(CategoryNode<Type>).GetField("_sorted", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo ensureSortedMethod = typeof(CategoryNode<Type>).GetMethod("EnsureSorted", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (Type importedType in pluginTypes)
            {
                UniLog.Log("Hiding type: " + importedType.Name);
                if (typeof(Component).IsAssignableFrom(importedType))
                {
                    string[] componentCategories = importedType.GetCustomAttribute<Category>().Paths;
                    if (componentCategories is null)
                        componentCategories = new[] { "Uncategorized" };
                    foreach (string categoryPath in componentCategories)
                    {
                        if (categoryPath != "Hidden")
                        {
                            UniLog.Log("Hiding unused component: " + pathPrefix + categoryPath);
                            CategoryNode<Type> parent = WorkerInitializer.ComponentLibrary.GetSubcategory(pathPrefix + categoryPath);
                            List<Type> elements = elementListField.GetValue(parent) as List<Type>;
                            elements.Remove(importedType);
                            sortedField.SetValue(parent, false);
                            ensureSortedMethod.Invoke(parent, new object[] { });
                        }
                    }
                }
            }
        }
    }
}
