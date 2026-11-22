using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assemblies;

public static class McpBridgeRecovery
{
    [MenuItem("Operation 1989/Tools/Recover MCP Bridge")]
    public static void Recover()
    {
        // The bridge is optional and may not be installed on every workstation.
        var serverType = FindBridgeType("McpUnity.Unity.McpUnityServer");
        var settingsType = FindBridgeType("McpUnity.Unity.McpUnitySettings");
        if (serverType == null || settingsType == null)
        {
            Debug.LogWarning("MCP recovery is unavailable. Install the MCP Unity package first.");
            return;
        }

        var server = serverType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
        var settings = settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
        serverType.GetMethod("StopServer").Invoke(server, null);
        settingsType.GetProperty("Port").SetValue(settings, 8091);
        settingsType.GetProperty("AutoStartServer").SetValue(settings, true);
        settingsType.GetMethod("SaveSettings").Invoke(settings, null);
        serverType.GetMethod("StartServer").Invoke(server, null);
        Debug.Log("MCP recovery requested on localhost:8091. Reconnect the MCP client to load the updated project settings. Unity remains running.");
    }

    private static Type FindBridgeType(string name)
    {
        return CurrentAssemblies.GetLoadedAssemblies()
            .Select(assembly => assembly.GetType(name))
            .FirstOrDefault(type => type != null);
    }
}
