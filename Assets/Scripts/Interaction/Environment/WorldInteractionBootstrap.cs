using UnityEngine;
using UnityEngine.SceneManagement;
using Operation1989.Interaction.Core;

namespace Operation1989.Interaction.Environment
{
    /// <summary>
    /// Connects authored army-base geometry to the reusable interaction systems.
    /// </summary>
    public static class WorldInteractionBootstrap
    {
        private static readonly string[] NonPhysicalNames =
        {
            "camera", "weapon", "rifle", "bullet", "projectile", "particle", "vfx",
            "effect", "sky", "cloud", "water", "ui", "canvas", "light", "audio"
        };

        private static readonly string[] NoGeneratedColliderNames =
        {
            "entrance", "entry", "door", "gate", "road", "floor", "window", "opening"
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            ConfigureCurrentScene();
            SceneManager.sceneLoaded += (_, __) => ConfigureCurrentScene();
        }

        private static void ConfigureCurrentScene()
        {
            var player = Object.FindAnyObjectByType<FirstPersonController>();
            if (player != null)
            {
                var camera = player.playerCamera != null
                    ? player.playerCamera.GetComponent<Camera>()
                    : player.GetComponentInChildren<Camera>(true);

                if (camera != null)
                {
                    if (camera.GetComponent<PlayerInteractionManager>() == null)
                        camera.gameObject.AddComponent<PlayerInteractionManager>();
                    if (camera.GetComponent<PhysicsGrabber>() == null)
                        camera.gameObject.AddComponent<PhysicsGrabber>();
                }
            }

            int interactableLayer = LayerMask.NameToLayer("Interactable");
            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                DisablePassageCollider(transform);

                if (transform.name.Contains("Door") && transform.GetComponent<Collider>() != null)
                {
                    if (transform.GetComponent<DoorController>() == null)
                        transform.gameObject.AddComponent<DoorController>();
                    if (interactableLayer >= 0)
                        transform.gameObject.layer = interactableLayer;
                }

                if (transform.name.StartsWith("Tower_Ladder") && transform.GetComponent<LadderClimbZone>() == null)
                    transform.gameObject.AddComponent<LadderClimbZone>();

                AddEnvironmentCollider(transform, player);
            }

            ConfigureMainBuildingEntrance(player);
        }

        private static void ConfigureMainBuildingEntrance(FirstPersonController player)
        {
            if (player == null)
                return;

            var controller = player.GetComponent<CharacterController>();
            var interiorFloor = GameObject.Find("w");
            var floorRenderer = interiorFloor != null ? interiorFloor.GetComponent<Renderer>() : null;
            if (controller == null || floorRenderer == null)
                return;

            var passage = player.GetComponent<MainBuildingEntrancePassage>();
            if (passage == null)
                passage = player.gameObject.AddComponent<MainBuildingEntrancePassage>();

            passage.Initialize(controller, floorRenderer);
        }

        private static void DisablePassageCollider(Transform transform)
        {
            var name = transform.name;
            if (name.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("entrance", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("entry", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("opening", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            foreach (var collider in transform.GetComponents<Collider>())
                collider.enabled = false;
        }

        private static void AddEnvironmentCollider(Transform transform, FirstPersonController player)
        {
            if (transform.GetComponent<Collider>() != null ||
                transform.GetComponent<DoorController>() != null ||
                IsMainBuildingDoorMesh(transform) ||
                HasExcludedNameInHierarchy(transform) ||
                IsNonPhysical(transform, player))
                return;

            var rigidbody = transform.GetComponentInParent<Rigidbody>();
            var meshFilter = transform.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;

            if (rigidbody != null && !rigidbody.isKinematic)
            {
                var box = transform.gameObject.AddComponent<BoxCollider>();
                box.center = meshFilter.sharedMesh.bounds.center;
                box.size = meshFilter.sharedMesh.bounds.size;
                transform.gameObject.AddComponent<GeneratedEnvironmentCollider>();
                return;
            }

            var meshCollider = transform.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
            transform.gameObject.AddComponent<GeneratedEnvironmentCollider>();
        }

        private static bool IsMainBuildingDoorMesh(Transform transform)
        {
            if (!transform.name.Equals("mcube", System.StringComparison.OrdinalIgnoreCase))
                return false;

            for (Transform current = transform.parent; current != null; current = current.parent)
            {
                if (current.name.Equals("IBuilding49", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasExcludedNameInHierarchy(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                foreach (var term in NoGeneratedColliderNames)
                {
                    if (current.name.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        private static bool IsNonPhysical(Transform transform, FirstPersonController player)
        {
            if (player != null && transform.IsChildOf(player.transform))
                return true;

            if (transform.GetComponent<Camera>() != null ||
                transform.GetComponent<Light>() != null ||
                transform.GetComponent<ParticleSystem>() != null ||
                transform.GetComponent<AudioSource>() != null)
                return true;

            foreach (var term in NonPhysicalNames)
            {
                if (transform.name.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
