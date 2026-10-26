using UnityEngine;

namespace Operation1989.Interaction.Environment
{
    /// <summary>
    /// Provides a trigger volume around a ladder. FirstPersonController converts
    /// W/S input to vertical movement while the player is inside the volume.
    /// </summary>
    [DisallowMultipleComponent]
    public class LadderClimbZone : MonoBehaviour
    {
        [SerializeField] private Vector3 triggerSize = new Vector3(7f, 1.2f, 7f);
        [SerializeField] private float topExitHeight;
        [SerializeField] private Vector3 topExitDirection;
        [SerializeField] private Vector3 topExitTarget;
        [SerializeField] private bool useTopExitTarget;

        public void Configure(Vector3 worldSize, float exitHeight, Vector3 exitDirection)
        {
            triggerSize = worldSize;
            topExitHeight = exitHeight;
            topExitDirection = exitDirection.normalized;
            EnsureTrigger();
        }

        public void ConfigureTowardTarget(Vector3 worldSize, float exitHeight, Vector3 exitTarget)
        {
            triggerSize = worldSize;
            topExitHeight = exitHeight;
            topExitTarget = exitTarget;
            useTopExitTarget = true;
            EnsureTrigger();
        }

        public bool ShouldExitAtTop(Vector3 playerPosition)
        {
            return (useTopExitTarget || topExitDirection.sqrMagnitude > 0.01f) &&
                   playerPosition.y >= topExitHeight;
        }

        public Vector3 GetTopExitDirection(Vector3 playerPosition)
        {
            if (!useTopExitTarget)
                return topExitDirection;

            Vector3 direction = topExitTarget - playerPosition;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
        }

        private void Awake()
        {
            EnsureTrigger();
        }

        private void Reset()
        {
            EnsureTrigger();
        }

        private void EnsureTrigger()
        {
            Transform trigger = transform.Find("Ladder Climb Trigger");
            if (trigger == null)
            {
                var triggerObject = new GameObject("Ladder Climb Trigger");
                trigger = triggerObject.transform;
                trigger.SetParent(transform, false);
            }

            trigger.localPosition = Vector3.zero;
            trigger.localRotation = Quaternion.identity;
            trigger.localScale = Vector3.one;

            var box = trigger.GetComponent<BoxCollider>();
            if (box == null) box = trigger.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = triggerSize;
        }
    }
}
