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

                AddEnvironmentCollider(transform, player);
            }

            ConfigureOldWaterTowerLadder();
            ConfigureMainBuildingEntranceDoor(player, interactableLayer);
            ConfigureServerRoomDoors(player, interactableLayer);
            ConfigureBlackBuildingCollision();
            ConfigureConcreteBarriers();
            ConfigureFences();
            ConfigureMainBuildingEntrance(player);
        }

        private static void ConfigureFences()
        {
            int configuredCount = 0;
            foreach (var root in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                string rootName = root.name;
                if (rootName.IndexOf("fence", System.StringComparison.OrdinalIgnoreCase) < 0 ||
                    rootName.IndexOf("gate", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rootName.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                // Process only the highest fence transform so nested mesh names do not
                // receive duplicate colliders.
                bool nestedFence = false;
                for (Transform parent = root.parent; parent != null; parent = parent.parent)
                {
                    if (parent.name.IndexOf("fence", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        nestedFence = true;
                        break;
                    }
                }
                if (nestedFence)
                    continue;

                foreach (var part in root.GetComponentsInChildren<Transform>(true))
                {
                    string partName = part.name;
                    if (partName.IndexOf("gate", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        partName.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    var meshFilter = part.GetComponent<MeshFilter>();
                    if (meshFilter == null || meshFilter.sharedMesh == null)
                        continue;

                    Collider solidCollider = null;
                    foreach (var existing in part.GetComponents<Collider>())
                    {
                        if (!existing.isTrigger)
                        {
                            existing.enabled = true;
                            solidCollider = existing;
                            break;
                        }
                    }

                    if (solidCollider == null)
                    {
                        var box = part.gameObject.AddComponent<BoxCollider>();
                        box.center = meshFilter.sharedMesh.bounds.center;
                        box.size = meshFilter.sharedMesh.bounds.size;
                    }

                    if (part.GetComponent<GeneratedEnvironmentCollider>() == null)
                        part.gameObject.AddComponent<GeneratedEnvironmentCollider>();
                    configuredCount++;
                }
            }

            Debug.Log($"[PhysicsSetup] Enforced solid collision on {configuredCount} fence meshes.");
        }

        private static void ConfigureConcreteBarriers()
        {
            int configuredCount = 0;
            foreach (var root in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (!root.name.StartsWith("Concrete_Barrier",
                        System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // This asset is rigged/skinned (gModel/Concrete + gRig), so it has
                // renderers but no MeshFilter. Build one reliable solid proxy from
                // the complete rendered bounds instead of depending on mesh filters.
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    continue;

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                Transform proxy = null;
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "Concrete Barrier Solid Collider")
                    {
                        proxy = child;
                        break;
                    }
                }

                if (proxy == null)
                {
                    var proxyObject = new GameObject("Concrete Barrier Solid Collider");
                    proxy = proxyObject.transform;
                    proxy.SetParent(root, true);
                }

                proxy.position = bounds.center;
                proxy.rotation = Quaternion.identity;
                Vector3 scale = proxy.lossyScale;
                var boxCollider = proxy.GetComponent<BoxCollider>();
                if (boxCollider == null)
                    boxCollider = proxy.gameObject.AddComponent<BoxCollider>();
                boxCollider.center = Vector3.zero;
                boxCollider.size = new Vector3(
                    bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                    bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                    bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
                boxCollider.isTrigger = false;
                boxCollider.enabled = true;

                if (proxy.GetComponent<GeneratedEnvironmentCollider>() == null)
                    proxy.gameObject.AddComponent<GeneratedEnvironmentCollider>();
                configuredCount++;
            }

            Debug.Log($"[PhysicsSetup] Enforced solid collision on {configuredCount} concrete barriers.");
        }

        private static void MakeRendererDoubleSided(Renderer renderer)
        {
            if (renderer == null)
                return;

            // Imported door faces are single-sided. Clone only these renderer
            // materials and disable back-face culling so the open leaves remain
            // visible when the player looks back from inside the building.
            foreach (var material in renderer.materials)
            {
                if (material == null)
                    continue;
                if (material.HasProperty("_Cull"))
                    material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                if (material.HasProperty("_CullMode"))
                    material.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Off);
                if (material.HasProperty("_DoubleSidedEnable"))
                    material.SetFloat("_DoubleSidedEnable", 1f);
                material.EnableKeyword("_DOUBLESIDED_ON");
                material.doubleSidedGI = true;
            }
        }

        private static void ConfigureServerRoomDoors(FirstPersonController player, int interactableLayer)
        {
            if (player == null)
                return;

            const float authoredOpenTravel = 0.01154193f;
            int configuredCount = 0;

            foreach (var root in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (!root.name.StartsWith("Sci-Fi_door_3_with_animation",
                        System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform leaf = null;
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.Equals("entry_3_door", System.StringComparison.OrdinalIgnoreCase))
                    {
                        leaf = child;
                        break;
                    }
                }

                // The prefab name excludes its descendants from the general collider
                // pass. Restore exact collision on every static frame/wall mesh while
                // leaving entry_3_door as the independently moving leaf.
                foreach (var staticPart in root.GetComponentsInChildren<Transform>(true))
                {
                    if (staticPart.name.Equals("entry_3_door",
                            System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    var staticMesh = staticPart.GetComponent<MeshFilter>();
                    if (staticMesh == null || staticMesh.sharedMesh == null)
                        continue;

                    Collider solidCollider = null;
                    foreach (var existing in staticPart.GetComponents<Collider>())
                    {
                        if (!existing.isTrigger)
                        {
                            existing.enabled = true;
                            solidCollider = existing;
                            break;
                        }
                    }

                    if (solidCollider == null)
                    {
                        var meshCollider = staticPart.gameObject.AddComponent<MeshCollider>();
                        meshCollider.sharedMesh = staticMesh.sharedMesh;
                        meshCollider.convex = false;
                    }

                    if (staticPart.GetComponent<GeneratedEnvironmentCollider>() == null)
                        staticPart.gameObject.AddComponent<GeneratedEnvironmentCollider>();
                }

                if (leaf == null)
                    continue;

                var meshFilter = leaf.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                MakeRendererDoubleSided(leaf.GetComponent<Renderer>());

                // Prevent the imported clip from fighting the player-controlled position.
                var animator = root.GetComponent<Animator>();
                if (animator != null)
                    animator.enabled = false;
                var legacyAnimation = root.GetComponent<Animation>();
                if (legacyAnimation != null)
                    legacyAnimation.enabled = false;

                var collider = leaf.GetComponent<BoxCollider>();
                if (collider == null)
                    collider = leaf.gameObject.AddComponent<BoxCollider>();
                collider.center = meshFilter.sharedMesh.bounds.center;
                collider.size = meshFilter.sharedMesh.bounds.size;
                collider.isTrigger = false;
                collider.enabled = true;

                var controller = leaf.GetComponent<DoorController>();
                if (controller == null)
                    controller = leaf.gameObject.AddComponent<DoorController>();
                controller.ConfigureSlidingDoor(player.transform, leaf.localPosition,
                    Vector3.right * authoredOpenTravel);

                if (interactableLayer >= 0)
                    leaf.gameObject.layer = interactableLayer;
                configuredCount++;
            }

            Debug.Log($"[DoorSetup] Configured {configuredCount} server-room entry doors for F interaction.");
        }

        private static void ConfigureBlackBuildingCollision()
        {
            Transform building = null;
            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (transform.name.Equals("IBuilding49", System.StringComparison.OrdinalIgnoreCase))
                {
                    building = transform;
                    break;
                }
            }

            if (building == null)
                return;

            foreach (var part in building.GetComponentsInChildren<Transform>(true))
            {
                string partName = part.name;
                if (partName.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    partName.IndexOf("gate", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    partName.IndexOf("hinge", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    partName.IndexOf("handle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    partName.Equals("mcube", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var meshFilter = part.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                Collider activeCollider = null;
                foreach (var existing in part.GetComponents<Collider>())
                {
                    if (existing.enabled && !existing.isTrigger)
                    {
                        activeCollider = existing;
                        break;
                    }
                }

                if (activeCollider != null)
                    continue;

                var meshCollider = part.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                if (part.GetComponent<GeneratedEnvironmentCollider>() == null)
                    part.gameObject.AddComponent<GeneratedEnvironmentCollider>();
            }

            Debug.Log("[PhysicsSetup] Enforced collision on IBuilding49 walls and structure.");
        }

        private static void ConfigureMainBuildingEntranceDoor(FirstPersonController player, int interactableLayer)
        {
            if (player == null)
                return;

            Transform building = null;
            Transform door = null;
            Transform arch = null;

            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (transform.name.Equals("building_1", System.StringComparison.OrdinalIgnoreCase))
                {
                    building = transform;
                    break;
                }
            }

            if (building == null)
                return;

            foreach (var child in building.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Equals("door_middle", System.StringComparison.OrdinalIgnoreCase))
                    door = child;
                else if (child.name.Equals("arch_middle", System.StringComparison.OrdinalIgnoreCase))
                    arch = child;
            }

            if (door == null || arch == null)
                return;

            var doorRenderer = door.GetComponent<Renderer>();
            var archRenderer = arch.GetComponent<Renderer>();
            var meshFilter = door.GetComponent<MeshFilter>();
            if (doorRenderer == null || archRenderer == null || meshFilter == null || meshFilter.sharedMesh == null)
                return;

            MakeRendererDoubleSided(doorRenderer);

            // door_middle is authored half open. Close it by aligning its centre with
            // arch_middle along only the facade's lateral axis.
            Vector3 centerDelta = archRenderer.bounds.center - doorRenderer.bounds.center;
            centerDelta.y = 0f;
            if (Mathf.Abs(centerDelta.x) >= Mathf.Abs(centerDelta.z))
                centerDelta.z = 0f;
            else
                centerDelta.x = 0f;

            Vector3 closedWorldPosition = door.position + centerDelta;
            Vector3 closedLocalPosition = door.parent != null
                ? door.parent.InverseTransformPoint(closedWorldPosition)
                : closedWorldPosition;

            // door_middle contains the central door geometry for both faces of the
            // imported building. A box would fill the space between them, so retain
            // the mesh's disconnected openings instead.
            foreach (var broadBox in door.GetComponents<BoxCollider>())
                broadBox.enabled = false;
            var doorCollider = door.GetComponent<MeshCollider>();
            if (doorCollider == null)
                doorCollider = door.gameObject.AddComponent<MeshCollider>();
            doorCollider.sharedMesh = meshFilter.sharedMesh;
            doorCollider.convex = false;
            doorCollider.isTrigger = false;
            doorCollider.enabled = true;

            var controller = door.GetComponent<DoorController>();
            if (controller == null)
                controller = door.gameObject.AddComponent<DoorController>();
            controller.ConfigureAuthoredSlidingDoor(player.transform, closedLocalPosition);

            if (interactableLayer >= 0)
                door.gameObject.layer = interactableLayer;

            Debug.Log($"[DoorSetup] Configured building_1/door_middle; press F near the entrance.");
        }

        private static void ConfigureOldWaterTowerLadder()
        {
            Transform tower = null;

            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (transform.name == "cgtrader_optimized_old_water_tower_FBX")
                {
                    tower = transform;
                    break;
                }
            }

            if (tower == null)
                return;

            var renderers = tower.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            // The tank platform sits a little above the midpoint of this combined mesh.
            float platformHeight = bounds.min.y + bounds.size.y * 0.62f;
            float zoneTop = platformHeight + 2.2f;
            Vector3 center = new Vector3(bounds.center.x,
                (bounds.min.y + zoneTop) * 0.5f, bounds.center.z);
            Vector3 size = new Vector3(
                Mathf.Max(bounds.size.x + 1.5f, 4f),
                zoneTop - bounds.min.y,
                Mathf.Max(bounds.size.z + 1.5f, 4f));

            var zoneObject = new GameObject("Water Tower Ladder Climb Zone");
            zoneObject.transform.position = center;
            var zone = zoneObject.AddComponent<LadderClimbZone>();
            Vector3 platformCenter = new Vector3(bounds.center.x, platformHeight, bounds.center.z);
            zone.ConfigureTowardTarget(size, platformHeight + 1.1f, platformCenter);
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
