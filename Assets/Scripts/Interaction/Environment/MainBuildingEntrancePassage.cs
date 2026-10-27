using System.Collections.Generic;
using UnityEngine;

namespace Operation1989.Interaction.Environment
{
    /// <summary>
    /// Opens a player-sized collision corridor through the central main-building entrance.
    /// The imported building uses combined collision geometry, so the visible opening
    /// cannot be separated from the surrounding wall collider.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainBuildingEntrancePassage : MonoBehaviour
    {
        private CharacterController player;
        private Collider[] doorwayObstacles;
        private float entranceX;
        private float entranceZFront;
        private float entranceZBack;
        private float groundY;
        private bool ignoring;

        public void Initialize(CharacterController characterController, Renderer interiorFloor)
        {
            player = characterController;
            if (interiorFloor == null)
            {
                enabled = false;
                return;
            }

            Bounds bounds = interiorFloor.bounds;
            entranceX = bounds.center.x;
            groundY = bounds.min.y;
            entranceZFront = bounds.min.z;
            entranceZBack = bounds.max.z;

            var obstacles = new HashSet<Collider>();
            CollectDoorwayObstacles(obstacles, entranceZFront);
            CollectDoorwayObstacles(obstacles, entranceZBack);

            doorwayObstacles = new Collider[obstacles.Count];
            obstacles.CopyTo(doorwayObstacles);
        }

        private void CollectDoorwayObstacles(HashSet<Collider> obstacles, float doorwayZ)
        {
            Vector3 doorwayCenter = new Vector3(entranceX, groundY + 3f, doorwayZ);
            foreach (var collider in Physics.OverlapBox(doorwayCenter, new Vector3(5f, 3f, 6f),
                         Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
            {
                // Preserve horizontal road/floor support while bypassing vertical blockers.
                if (collider != player && collider.bounds.size.y > 1f &&
                    collider.GetComponentInParent<DoorController>() == null &&
                    !IsProtectedObstacle(collider.transform))
                    obstacles.Add(collider);
            }
        }

        private static bool IsProtectedObstacle(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current.name.StartsWith("Concrete_Barrier",
                        System.StringComparison.OrdinalIgnoreCase) ||
                    current.name.IndexOf("fence", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.StartsWith("Sci-Fi_door_3_with_animation",
                        System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void Update()
        {
            if (player == null || doorwayObstacles == null)
                return;

            Vector3 position = player.transform.position;
            bool nearEitherDoor = Mathf.Abs(position.z - entranceZFront) <= 8f ||
                                  Mathf.Abs(position.z - entranceZBack) <= 8f;
            bool inEntranceCorridor =
                Mathf.Abs(position.x - entranceX) <= 5f &&
                nearEitherDoor &&
                position.y >= groundY - 1f &&
                position.y <= groundY + 6f;

            if (inEntranceCorridor != ignoring)
                SetIgnoring(inEntranceCorridor);
        }

        private void OnDisable()
        {
            if (ignoring)
                SetIgnoring(false);
        }

        private void SetIgnoring(bool value)
        {
            foreach (var obstacle in doorwayObstacles)
            {
                if (obstacle != null && obstacle != player &&
                    obstacle.GetComponentInParent<DoorController>() == null &&
                    !IsProtectedObstacle(obstacle.transform))
                    Physics.IgnoreCollision(player, obstacle, value);
            }
            ignoring = value;
        }
    }
}
