using BaseX;
using FrooxEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NeosPluginManager
{
    /// <summary>
    /// 
    /// </summary>
    public static class PluginHooks
    {
        /// <summary>
        /// Hook for FrooxEngine.Session.NewSession(owner, port);
        /// Allows us to intervene and load plugins prior to starting 
        /// the requested world
        /// </summary>
        /// <param name="owner">World object to populate while starting the listen server</param>
        /// <param name="port">Port to use for the listen server</param>
        /// <returns>Instance of Session that represents the newly created session</returns>
        public static Session NewSessionHook(World owner, ushort port = 0)
        {
            UniLog.Log("Session.NewSession hooked");
            /*
             * Use reflection to grab private fields/methods
             * InitLoadField - a DataTreeNode containing the initial world load batch, which includes the configuration data we need.
             * SessionConstructor - The private constructor for Session objects
             * SessionStartNew - The private Session.StartNew(port) method, which is used to start the listen server.
             */
            FieldInfo InitLoadField = typeof(World).GetField("worldInitLoad", BindingFlags.NonPublic | BindingFlags.Instance);
            ConstructorInfo SessionConstructor = typeof(Session).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(World) }, null);
            MethodInfo SessionStartNew = typeof(Session).GetMethod("StartNew", BindingFlags.NonPublic | BindingFlags.Instance);
            PropertyInfo SessionConnectionStatusDescriptionProperty = typeof(Session).GetProperty("ConnectionStatusDescription");

            Session session = SessionConstructor.Invoke(new[] { owner }) as Session;
            /*
             * Traverse the dictionary tree to find the world description, which is how we're presently storing
             * plugin loading information
             * Root > Configuration > WorldDescription > Data.LoadString()
             */
            DataTreeNode initNode = (DataTreeNode)InitLoadField.GetValue(owner);
            DataTreeDictionary initDict = initNode as DataTreeDictionary;
            DataTreeDictionary configDict = initDict.TryGetDictionary("Configuration");
            DataTreeNode descriptionDataNode = configDict.TryGetDictionary("WorldDescription").TryGetNode("Data");
            DataTreeNode sessionIdDataNode = configDict.TryGetNode("SessionID-ID");

            if (descriptionDataNode != null && !String.IsNullOrEmpty(descriptionDataNode.LoadString()))
            {
                try
                {
                    Dictionary<string, string> requestedPlugins = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(descriptionDataNode.LoadString());

                    Userspace.UserspaceWorld.RunSynchronously(() =>
                    {
                        //Action when plugins are accepted
                        Action successAction = new Action(() =>
                        {
                            /*Inject the plugin assembly into neos, and run postprocessing to bring in
                             * the relevant components. If plugin injection fails, abort the world
                             * connection process
                             */
                            if (PluginManager.InjectPlugins(sessionIdDataNode.LoadString(), requestedPlugins))
                            {
                                //Start a new session on the specified port
                                SessionStartNew.Invoke(session, new[] { (object)port });
                            }
                            else
                                owner.InitializationFailed(World.FailReason.JoinRejected, "Plugin injection failed");
                        });

                        //Action when plugins are rejected
                        Action failureAction = new Action(() =>
                        {
                            owner.InitializationFailed(World.FailReason.JoinRejected, "Plugins Rejected");
                        });

                        //Show plugin notification dialog
                        Userspace.UserspaceWorld.GetGloballyRegisteredComponent<PluginNotifyWindow>().ShowWindow(requestedPlugins.Keys.ToList(),
                            successAction, failureAction);
                    });
                    return session;
                }
                catch (Exception ex)
                {
                    UniLog.Log("Failed to load plugins: " + ex);
                    owner.InitializationFailed(World.FailReason.JoinRejected, "Eror loading plugins");
                    return session;
                }
            }

            PluginManager.HidePlugins(owner.Engine.WorldManager.FocusedWorld.SessionId);

            /*
             * Perform the normal operations expected in Session.NewSession
             * Construct a new Session object, and call Session.StartNew(port)
             */

            SessionConnectionStatusDescriptionProperty.SetValue(session, "World.Connection.PluginAuthorization");
            SessionStartNew.Invoke(session, new[] { (object)port });
            UniLog.Log("Session.NewSession Handled!");
            return session;
        }

        /// <summary>
        /// Hook for FrooxEngine.Session.JoinSession(owner, addresses);
        /// Allows us to intervene and load plugins prior to connecting to 
        /// the requested world
        /// </summary>
        /// <param name="owner">World object to populate once connected</param>
        /// <param name="addresses">List of possible session URIs to try using</param>
        /// <returns>Instance of Session that represents the session being connected to</returns>
        public static Session JoinSessionHook(World owner, IEnumerable<Uri> addresses)
        {
            UniLog.Log("Session.JoinSession hooked");
            /*
             * Use reflection to grab private fields/methods
             * SessionConstructor - The private constructor for Session objects
             * SessionConnectTo - The private Session.ConnectTo(addresses) method, which is used to connect to the target world session.
             */
            ConstructorInfo SessionConstructor = typeof(Session).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(World) }, null);
            MethodInfo SessionConnectTo = typeof(Session).GetMethod("ConnectTo", BindingFlags.NonPublic | BindingFlags.Instance);

            Session session = SessionConstructor.Invoke(new[] { owner }) as Session;
            /*
             * Query the WorldAnnouncer for information about the world we're joining,
             * so we can parse the Description for plugins to load
             */
            CloudX.Shared.SessionInfo info = owner.Engine.WorldAnnouncer.GetDiscoveredWorlds().SingleOrDefault(C => C.GetSessionURLs().All(P => addresses.Contains(P)));

            if (info != null && !string.IsNullOrEmpty(info.Description))
            {
                try
                {
                    Dictionary<string, string> requestedPlugins = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(info.Description);

                    Userspace.UserspaceWorld.RunSynchronously(() =>
                    {
                        //Action when plugins are accepted
                        Action successAction = new Action(() =>
                        {
                            /*Inject the plugin assembly into neos, and run postprocessing to bring in
                             * the relevant components. If plugin injection fails, abort the world
                             * connection process
                             */
                            if (PluginManager.InjectPlugins(info.SessionId, requestedPlugins))
                            {
                                //Connect to the requested session
                                SessionConnectTo.Invoke(session, new[] { addresses });
                            }
                            else
                                owner.InitializationFailed(World.FailReason.JoinRejected, "Plugin injection failed");
                        });

                        //Action when plugins are rejected
                        Action failureAction = new Action(() =>
                        {
                            owner.InitializationFailed(World.FailReason.JoinRejected, "Plugins Rejected");
                        });

                        //Show plugin notification dialog
                        Userspace.UserspaceWorld.GetGloballyRegisteredComponent<PluginNotifyWindow>().ShowWindow(requestedPlugins.Keys.ToList(),
                            successAction, failureAction);
                    });
                    return session;
                }
                catch (Exception ex)
                {
                    UniLog.Log("Failed to load plugins: " + ex);
                    owner.InitializationFailed(World.FailReason.JoinRejected, "Error loading plugins");
                    return session;
                }
            }

            /*
             * Perform the normal operations expected in Session.JoinSession
             * Construct a new Session object, and call Session.ConnectToo(addresses);
             */

            SessionConnectTo.Invoke(session, new[] { addresses });
            UniLog.Log("Session.JoinSession Handled!");
            return session;
        }
    }
}
