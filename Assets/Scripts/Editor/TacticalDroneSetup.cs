using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Operation1989.AI.Drone;
using Operation1989.AI.InferenceEngine;
using Operation1989.Interaction.Environment;

namespace Operation1989.EditorScripts
{
    /// <summary>
    /// Editor setup tooling for the Tactical Recon Drone (Student 2 - Systems Engineer).
    /// Provides 1-click creation of the Tactical Drone prefab, isolated test scene, and military base integration.
    /// </summary>
    public static class TacticalDroneSetup
    {
        public const string Work = "Temp/TacticalDrone";
        public const string PrefabPath = "Assets/Prefabs/Agents/TacticalDrone.prefab";
        public const string TestScene = "Assets/Scenes/TacticalDroneTest.unity";
        public const string MainScene = "Assets/army base.unity";

        [InitializeOnLoadMethod]
        static void RegisterPoller()
        {
            EditorApplication.update -= PollRequest;
            EditorApplication.update += PollRequest;
        }

        static void PollRequest()
        {
            string path = Work + "/request.txt";
            if (!File.Exists(path) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }
            string command = File.ReadAllText(path).Trim();
            File.Delete(path);

            try
            {
                if (command == "build") BuildTestScene();
                else if (command == "validate") TacticalDroneValidation.Run();
                else if (command == "integrate") IntegrateIntoBaseScene();
                else throw new InvalidOperationException("Unknown drone command: " + command);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Work);
                File.WriteAllText(Work + "/error.txt", ex.ToString());
                Debug.LogException(ex);
            }
        }

        [MenuItem("Operation 1989/Tactical Drone/1 Build Isolated Test Scene")]
        public static void BuildTestScene()
        {
            Directory.CreateDirectory(Work);
            Directory.CreateDirectory("Assets/Prefabs/Agents");
            Directory.CreateDirectory("Assets/Scenes");

            var prefab = BuildPrefab();

            // Create new isolated test scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Ground Arena
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Test Arena Floor";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(12f, 1f, 12f);

            // Knowledge Base Manager
            var kbGo = new GameObject("KnowledgeBase");
            kbGo.AddComponent<KnowledgeBase>();

            // Obstacle & Barricades for cover testing
            var barricade1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barricade1.name = "Barricade Alpha (Cover)";
            barricade1.transform.position = new Vector3(0f, 1.5f, 10f);
            barricade1.transform.localScale = new Vector3(8f, 3f, 1f);
            barricade1.layer = 10; // Layer 10: Barricade
            var rbBarricade = barricade1.AddComponent<Rigidbody>();
            rbBarricade.mass = 500f;
            barricade1.AddComponent<PushableBarricade>();

            // Wall Obstacle for dynamic A* re-planning test
            var dynamicWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dynamicWall.name = "Dynamic Flight Barrier";
            dynamicWall.transform.position = new Vector3(0f, 12f, 20f);
            dynamicWall.transform.localScale = new Vector3(20f, 10f, 1f);
            dynamicWall.layer = 0; // Default layer

            // Player Setup: Create Interactive First Person Player
            var player = new GameObject("Player");
            player.tag = "Player";
            player.layer = 8; // Layer 8: Player
            player.transform.position = new Vector3(0f, 0.1f, -22f);

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var combatHealth = player.AddComponent<CombatHealth>();
            combatHealth.isPlayer = true;
            combatHealth.maximumHealth = 500f;
            combatHealth.regeneratePlayerHealth = true;
            var rifle = player.AddComponent<RifleWeapon>();
            rifle.playerWeapon = true;

            // Attach Main Camera to Player at eye level
            var mainCam = Camera.main;
            if (!mainCam)
            {
                var camObj = new GameObject("Main Camera");
                mainCam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }
            mainCam.transform.SetParent(player.transform, false);
            mainCam.transform.localPosition = new Vector3(0f, 1.6f, 0.15f);
            mainCam.transform.localRotation = Quaternion.identity;

            var testController = player.AddComponent<TestPlayerController>();
            testController.playerCamera = mainCam;
            testController.rifle = rifle;

            // Instantiate Tactical Drone
            var droneInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            droneInstance.name = "Tactical Recon Drone";
            droneInstance.transform.position = new Vector3(0f, 14f, -30f);

            var controller = droneInstance.GetComponent<TacticalDroneController>();
            var perception = droneInstance.GetComponent<TacticalDronePerception>();
            if (perception) perception.SetTargetPlayer(player.transform);

            // Configure 3D Flight Nodes
            var navGraph = droneInstance.GetComponent<TacticalDroneNavigationGraph>();
            if (navGraph)
            {
                navGraph.GenerateDefaultAirspaceNodes();
            }

            EditorSceneManager.SaveScene(scene, TestScene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            File.WriteAllText(Work + "/built.txt", DateTime.UtcNow.ToString("O"));
            Debug.Log($"[Tactical Drone Setup] Successfully built isolated test scene at {TestScene}");
        }

        public static GameObject BuildPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing) return existing;

            var root = new GameObject("TacticalDrone");
            root.tag = "Untagged";
            root.layer = 0;

            // Visual mesh
            var fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D model/RUKO F11GIM.fbx");
            Transform visual = null;
            if (fbxModel)
            {
                var fbxInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxModel, root.transform);
                fbxInstance.name = "RUKO Drone Visual";
                fbxInstance.transform.localPosition = Vector3.zero;
                fbxInstance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // Standard FBX orientation fix
                fbxInstance.transform.localScale = Vector3.one * 0.025f;
                visual = fbxInstance.transform;
            }
            else
            {
                // Fallback geometry if model missing
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(root.transform, false);
                body.transform.localScale = new Vector3(1.5f, 0.4f, 1.5f);
                visual = body.transform;
            }

            // Collider for bullet hits
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(2.5f, 0.8f, 2.5f);
            col.center = Vector3.zero;

            // Searchlight
            var lightGo = new GameObject("TacticalSearchlight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.3f, 0.5f);
            lightGo.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
            var spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 50f;
            spot.spotAngle = 45f;
            spot.intensity = 3f;
            spot.color = new Color(0.2f, 0.9f, 1f);

            // Muzzle
            var muzzleGo = new GameObject("MuzzlePoint");
            muzzleGo.transform.SetParent(root.transform, false);
            muzzleGo.transform.localPosition = new Vector3(0f, -0.2f, 1.2f);

            // Audio Source
            var audio = root.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            audio.minDistance = 5f;
            audio.maxDistance = 100f;

            // Drone Damage & Health
            var damage = root.AddComponent<DroneDamage>();

            // AI Components
            var perception = root.AddComponent<TacticalDronePerception>();
            var inference = root.AddComponent<TacticalDroneInferenceEngine>();
            var navGraph = root.AddComponent<TacticalDroneNavigationGraph>();
            var movement = root.AddComponent<TacticalDroneMovement>();
            var combat = root.AddComponent<TacticalDroneCombat>();
            var controller = root.AddComponent<TacticalDroneController>();

            // Save Prefab
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[Tactical Drone Setup] Saved prefab to {PrefabPath}");
            return prefab;
        }

        [MenuItem("Operation 1989/Tactical Drone/2 Integrate Into Military Base")]
        public static void IntegrateIntoBaseScene()
        {
            Directory.CreateDirectory(Work);
            if (!File.Exists(MainScene))
            {
                throw new FileNotFoundException("Military base scene not found at " + MainScene);
            }

            // Backup main scene before modifying
            string backupPath = Work + "/army base backup.unity";
            File.Copy(MainScene, backupPath, true);

            var scene = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);

            // Ensure KnowledgeBase exists in base scene
            if (!Object.FindAnyObjectByType<KnowledgeBase>())
            {
                var kbGo = new GameObject("KnowledgeBase_IS");
                kbGo.AddComponent<KnowledgeBase>();
            }

            // Check if Tactical Recon Drone already placed
            var existingDrone = GameObject.Find("Tactical Recon Drone");
            if (!existingDrone)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (!prefab) prefab = BuildPrefab();

                var drone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                drone.name = "Tactical Recon Drone";
                drone.transform.position = new Vector3(-35f, 18f, 50f); // Stationed at North Watchtower

                var player = GameObject.FindGameObjectWithTag("Player");
                if (player)
                {
                    var perception = drone.GetComponent<TacticalDronePerception>();
                    if (perception) perception.SetTargetPlayer(player.transform);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Tactical Drone Setup] Successfully integrated Tactical Recon Drone into army base scene!");
            }
            else
            {
                Debug.Log("[Tactical Drone Setup] Tactical Recon Drone is already present in army base scene.");
            }
        }
    }
}
