using UnityEngine;
using UnityEngine.SceneManagement;
using Operation1989.Interaction.Core;

namespace Operation1989.Interaction.Environment
{
    /// <summary>
    /// Connects the authored army-base geometry to the reusable interaction systems
    /// at runtime. This avoids relying on hand-edited scene YAML and applies to any
    /// scene containing the standard player and named environment props.
    /// </summary>
    public static class WorldInteractionBootstrap
    {
        private static readonly string[] NonPhysicalNames =
        {
            "camera", "weapon", "rifle", "bullet", "projectile", "particle", "vfx",
            "effect", "sky", "cloud", "water", "ui", "canvas", "light", "audio"
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
        }

        private static void AddEnvironmentCollider(Transform transform, FirstPersonController player)
        {
            if (transform.GetComponent<Collider>() != null ||
                transform.GetComponent<DoorController>() != null ||
                IsNonPhysical(transform, player))
                return;

            var meshFilter = transform.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;

            var rigidbody = transform.GetComponent<Rigidbody>();
            if (rigidbody != null && !rigidbody.isKinematic)
            {
                // Unity does not allow non-convex MeshColliders on moving bodies.
                // A bounds collider still makes vehicles and movable props solid.
                var box = transform.gameObject.AddComponent<BoxCollider>();
                box.center = meshFilter.sharedMesh.bounds.center;
                box.size = meshFilter.sharedMesh.bounds.size;
                return;
            }

            // Every visible static mesh in the outdoor base is physical. This covers
            // imported buildings, walls, tanks, jeeps, trees, barricades and props
            // even when the source asset arrived without a collider.
            var meshCollider = transform.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
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
