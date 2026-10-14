using System.IO;
using UnityEditor;
using UnityEngine;
using Operation1989.Interaction;
using Operation1989.Interaction.Core;
using Operation1989.Interaction.AI;
using Operation1989.Interaction.Vehicles;
using Operation1989.Interaction.Environment;

namespace Operation1989.Editor
{
    /// <summary>
    /// Systems Engineer Utility Tool: Generates all 6+ entity placeholder prefabs
    /// with exact Rigidbody physics, Colliders, Scripts, and Layer configurations.
    /// Provides the drop-in visual child structure for seamless 3D FBX swapping later.
    /// </summary>
    public class PlaceholderPrefabGenerator : EditorWindow
    {
        private const string PrefabDirectory = "Assets/Prefabs/Placeholders";

        [MenuItem("Operation 1989/Generate All Placeholder Prefabs", false, 10)]
        public static void GeneratePrefabs()
        {
            EnsureLayersConfigured();
            EnsureDirectoryExists(PrefabDirectory);

            GeneratePlayerPrefab();
            GeneratePartnerPrefab();
            GenerateSupplyTruckPrefab();
            GenerateMilitaryJeepPrefab();
            GenerateSurveillanceDronePrefab();
            GeneratePatrolSoldierPrefab();
            GenerateEliteSoldierPrefab();
            GeneratePushableBarricadePrefab();
            GeneratePickableIntelPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Systems Engineer - Prefab Generator",
                "✅ All entity placeholder prefabs have been successfully generated in:\n\n" + PrefabDirectory +
                "\n\nEach prefab features an isolated physics root and a 'Visual_Placeholder' child ready for 3D FBX swapping.",
                "Awesome!"
            );
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }

        private static void EnsureLayersConfigured()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layersProp = tagManager.FindProperty("layers");

            SetLayer(layersProp, 8, "Player");
            SetLayer(layersProp, 9, "Interactable");
            SetLayer(layersProp, 10, "Barricade");
            SetLayer(layersProp, 11, "EnemyAI");
            SetLayer(layersProp, 12, "Drone");
            SetLayer(layersProp, 13, "Jeep");

            tagManager.ApplyModifiedProperties();
        }

        private static void SetLayer(SerializedProperty layersProp, int index, string name)
        {
            if (index < layersProp.arraySize)
            {
                SerializedProperty sp = layersProp.GetArrayElementAtIndex(index);
                if (string.IsNullOrEmpty(sp.stringValue))
                {
                    sp.stringValue = name;
                }
            }
        }

        private static int GetLayerOrDefault(string layerName, int defaultLayer)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer != -1 ? layer : defaultLayer;
        }

        // ==========================================
        // 1. HUMAN PLAYER PLACEHOLDER
        // ==========================================
        private static void GeneratePlayerPrefab()
        {
            GameObject root = new GameObject("PF_Player_Placeholder");
            root.tag = "Player";
            root.layer = GetLayerOrDefault("Player", 8);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 70.0f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.height = 2.0f;
            col.radius = 0.5f;
            col.center = new Vector3(0, 1.0f, 0);

            // Child Camera & Interaction
            GameObject camHolder = new GameObject("PlayerCameraHolder");
            camHolder.transform.SetParent(root.transform);
            camHolder.transform.localPosition = new Vector3(0, 1.6f, 0);
            camHolder.AddComponent<PlayerInteractionManager>();
            camHolder.AddComponent<PhysicsGrabber>();

            // Child Primitive Visual Placeholder (to swap with FBX)
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0, 1.0f, 0);
            DestroyImmediate(visual.GetComponent<Collider>()); // Physics belongs to root collider only

            SaveAndCleanupPrefab(root, "PF_Player_Placeholder.prefab");
        }

        // ==========================================
        // 2. PARTNER PLAYER PLACEHOLDER
        // ==========================================
        private static void GeneratePartnerPrefab()
        {
            GameObject root = new GameObject("PF_Partner_Placeholder");
            root.tag = "Player";
            root.layer = GetLayerOrDefault("Player", 8);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 70.0f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.height = 2.0f;
            col.radius = 0.5f;
            col.center = new Vector3(0, 1.0f, 0);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0, 1.0f, 0);
            DestroyImmediate(visual.GetComponent<Collider>());

            SaveAndCleanupPrefab(root, "PF_Partner_Placeholder.prefab");
        }

        // ==========================================
        // 3. SUPPLY TRUCK PLACEHOLDER
        // ==========================================
        private static void GenerateSupplyTruckPrefab()
        {
            GameObject root = new GameObject("PF_SupplyTruck_Placeholder");
            root.layer = 0; // Default

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 4000.0f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(2.5f, 3.0f, 6.0f);
            col.center = new Vector3(0, 1.5f, 0);

            // Visual Child
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0, 1.5f, 0);
            visual.transform.localScale = new Vector3(2.5f, 3.0f, 6.0f);
            DestroyImmediate(visual.GetComponent<Collider>());

            // Trailing Stealth Zone Trigger
            GameObject stealthTrigger = new GameObject("Stealth_Zone_Trigger");
            stealthTrigger.transform.SetParent(root.transform);
            stealthTrigger.transform.localPosition = new Vector3(0, 1.0f, -4.5f);
            BoxCollider triggerCol = stealthTrigger.AddComponent<BoxCollider>();
            triggerCol.isTrigger = true;
            triggerCol.size = new Vector3(3.0f, 2.5f, 3.5f);
            stealthTrigger.AddComponent<SupplyTruckStealthZone>();

            SaveAndCleanupPrefab(root, "PF_SupplyTruck_Placeholder.prefab");
        }

        // ==========================================
        // 4. MILITARY JEEP PLACEHOLDER
        // ==========================================
        private static void GenerateMilitaryJeepPrefab()
        {
            GameObject root = new GameObject("PF_MilitaryJeep_Placeholder");
            root.layer = GetLayerOrDefault("Jeep", 13);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 1500.0f;
            rb.centerOfMass = new Vector3(0, -0.5f, 0);

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(2.0f, 1.8f, 4.0f);
            col.center = new Vector3(0, 0.9f, 0);

            root.AddComponent<JeepPursuitController>();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0, 0.9f, 0);
            visual.transform.localScale = new Vector3(2.0f, 1.8f, 4.0f);
            DestroyImmediate(visual.GetComponent<Collider>());

            SaveAndCleanupPrefab(root, "PF_MilitaryJeep_Placeholder.prefab");
        }

        // ==========================================
        // 5. SURVEILLANCE DRONE PLACEHOLDER
        // ==========================================
        private static void GenerateSurveillanceDronePrefab()
        {
            GameObject root = new GameObject("PF_SurveillanceDrone_Placeholder");
            root.layer = GetLayerOrDefault("Drone", 12);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 15.0f;
            rb.isKinematic = true; // Flight kinematics

            SphereCollider col = root.AddComponent<SphereCollider>();
            col.radius = 0.8f;

            root.AddComponent<DroneAlertSystem>();
            root.AddComponent<DroneSpotter>();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(1.6f, 1.6f, 1.6f);
            DestroyImmediate(visual.GetComponent<Collider>());

            SaveAndCleanupPrefab(root, "PF_SurveillanceDrone_Placeholder.prefab");
        }

        // ==========================================
        // 6. PATROL SOLDIER AI PLACEHOLDER
        // ==========================================
        private static void GeneratePatrolSoldierPrefab()
        {
            GameObject root = new GameObject("PF_PatrolSoldier_Placeholder");
            root.layer = GetLayerOrDefault("EnemyAI", 11);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 75.0f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.height = 2.0f;
            col.radius = 0.5f;
            col.center = new Vector3(0, 1.0f, 0);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0, 1.0f, 0);
            DestroyImmediate(visual.GetComponent<Collider>());

            SaveAndCleanupPrefab(root, "PF_PatrolSoldier_Placeholder.prefab");
        }

        // ==========================================
        // 7. ELITE SOLDIER AI PLACEHOLDER
        // ==========================================
        private static void GenerateEliteSoldierPrefab()
        {
            GameObject root = new GameObject("PF_EliteSoldier_Placeholder");
            root.layer = GetLayerOrDefault("EnemyAI", 11);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 80.0f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.height = 2.0f;
            col.radius = 0.5f;
            col.center = new Vector3(0, 1.0f, 0);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0, 1.0f, 0);
            DestroyImmediate(visual.GetComponent<Collider>());

            SaveAndCleanupPrefab(root, "PF_EliteSoldier_Placeholder.prefab");
        }

        // ==========================================
        // 8. PUSHABLE BARRICADE PROTOTYPE
        // ==========================================
        private static void GeneratePushableBarricadePrefab()
        {
            GameObject root = new GameObject("PF_PushableBarricade_Placeholder");
            root.layer = GetLayerOrDefault("Barricade", 10);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 120.0f;

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(3.0f, 1.2f, 0.6f);
            col.center = new Vector3(0, 0.6f, 0);

            root.AddComponent<PushableBarricade>();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = new Vector3(0, 0.6f, 0);
            visual.transform.localScale = new Vector3(3.0f, 1.2f, 0.6f);
            DestroyImmediate(visual.GetComponent<Collider>());

            SaveAndCleanupPrefab(root, "PF_PushableBarricade_Placeholder.prefab");
        }

        // ==========================================
        // 9. PICKABLE INTEL OBJECT PROTOTYPE
        // ==========================================
        private static void GeneratePickableIntelPrefab()
        {
            GameObject root = new GameObject("PF_PickableIntel_Placeholder");
            root.layer = GetLayerOrDefault("Interactable", 9);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 3.0f;

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.4f, 0.1f, 0.3f);
            col.center = Vector3.zero;

            PickableObject pickable = root.AddComponent<PickableObject>();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual_Placeholder";
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(0.4f, 0.1f, 0.3f);
            DestroyImmediate(visual.GetComponent<Collider>());

            SaveAndCleanupPrefab(root, "PF_PickableIntel_Placeholder.prefab");
        }

        private static void SaveAndCleanupPrefab(GameObject obj, string fileName)
        {
            string fullPath = Path.Combine(PrefabDirectory, fileName);
            PrefabUtility.SaveAsPrefabAsset(obj, fullPath);
            DestroyImmediate(obj);
        }
    }
}
