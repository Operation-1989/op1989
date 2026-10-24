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
        private float entranceZ;
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
            entranceZ = Mathf.Abs(player.transform.position.z - bounds.min.z) <=
                        Mathf.Abs(player.transform.position.z - bounds.max.z)
                ? bounds.min.z
                : bounds.max.z;

            var obstacles = new HashSet<Collider>();
            Vector3 doorwayCenter = new Vector3(entranceX, groundY + 3f, entranceZ);
            foreach (var collider in Physics.OverlapBox(doorwayCenter, new Vector3(5f, 3f, 6f),
                         Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
            {
                // Preserve horizontal road/floor support while bypassing vertical blockers.
                if (collider != player && collider.bounds.size.y > 1f)
                    obstacles.Add(collider);
            }

            doorwayObstacles = new Collider[obstacles.Count];
            obstacles.CopyTo(doorwayObstacles);
        }

        private void Update()
        {
            if (player == null || doorwayObstacles == null)
                return;

            Vector3 position = player.transform.position;
            bool inEntranceCorridor =
                Mathf.Abs(position.x - entranceX) <= 5f &&
                Mathf.Abs(position.z - entranceZ) <= 8f &&
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
                if (obstacle != null && obstacle != player)
                    Physics.IgnoreCollision(player, obstacle, value);
            }
            ignoring = value;
        }
    }
}
