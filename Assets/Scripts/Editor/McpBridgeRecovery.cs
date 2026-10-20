using McpUnity.Unity;
using UnityEditor;
using UnityEngine;

public static class McpBridgeRecovery
{
    [MenuItem("Operation 1989/Tools/Recover MCP Bridge")]
    public static void Recover()
    {
        var server = McpUnityServer.Instance;
        server.StopServer();
        var settings = McpUnitySettings.Instance;
        settings.Port = 8091;
        settings.AutoStartServer = true;
        settings.SaveSettings();
        server.StartServer();
        Debug.Log("MCP recovery requested on localhost:8091. Reconnect the MCP client to load the updated project settings. Unity remains running.");
    }
}
