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
