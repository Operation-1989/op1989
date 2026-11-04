using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Operation1989.Interaction.Environment
{
    public enum DoorType
    {
        HingeSwing,
        SlidingGate
    }

    /// <summary>
    /// Controls interactive hinged doors and sliding base gates.
    /// Implements IInteractable for player interaction.
    /// </summary>
    public class DoorController : MonoBehaviour, IInteractable
    {
        [Header("Door Settings")]
        [SerializeField] private DoorType doorType = DoorType.HingeSwing;
        [SerializeField] private string openPrompt = "[E] Open Door";
        [SerializeField] private string closePrompt = "[E] Close Door";
        [SerializeField] private string lockedPrompt = "[Locked] Keycard Required";

        [Header("Door Physics & Animation")]
        [SerializeField] private float openSpeed = 3.0f;
        [SerializeField] private bool isLocked = false;
        [SerializeField] private bool isOpen = false;

        [Header("Hinge Swing Parameters")]
        [SerializeField] private Vector3 openRotationOffset = new Vector3(0, 90, 0);

        [Header("Sliding Gate Parameters")]
        [SerializeField] private Vector3 openPositionOffset = new Vector3(3, 0, 0);

        [Header("Audio (Optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;
        [SerializeField] private AudioClip lockedSound;

        private Vector3 closedPosition;
        private Vector3 targetPosition;
        private Quaternion closedRotation;
        private Quaternion targetRotation;
        private Coroutine transitionCoroutine;
        private Transform proximityInteractor;
        private float proximityDistance;
        private bool useProximityInput;

        public string InteractionPrompt
        {
            get
            {
                if (isLocked) return lockedPrompt;
                return isOpen ? closePrompt : openPrompt;
            }
        }

        private void Awake()
        {
            closedPosition = transform.localPosition;
            closedRotation = transform.localRotation;
            
            targetPosition = closedPosition;
            targetRotation = closedRotation;
        }

        private void Update()
        {
            if (!useProximityInput || proximityInteractor == null || Keyboard.current == null ||
                !Keyboard.current.fKey.wasPressedThisFrame)
                return;

            TryProximityInteract();
        }

        public bool IsOpen => isOpen;
        public bool TryProximityInteract()
        {
            if (!useProximityInput || !proximityInteractor || isLocked) return false;
            var intel = FindAnyObjectByType<IntelTheftMission>();
            if (intel && intel.OwnsTerminalInput) return false;

            var doorCollider = GetComponentInChildren<Collider>();
            Vector3 interactionPoint = doorCollider != null
                ? doorCollider.ClosestPoint(proximityInteractor.position)
                : transform.position;

            if (Vector3.Distance(proximityInteractor.position, interactionPoint) <= proximityDistance)
            { Interact(proximityInteractor); return true; }
            return false;
        }

        /// <summary>
        /// Configures an imported door whose authored position is its half-open pose.
        /// Doubling that offset produces a fully open passage without hard-coded world coordinates.
        /// </summary>
        public void ConfigureAuthoredSlidingDoor(Transform interactor, Vector3 closedLocalPosition,
            float interactionDistance = 3.5f, bool startOpen = false)
        {
            Vector3 authoredOpenPosition = transform.localPosition;
            doorType = DoorType.SlidingGate;
            openPrompt = "[F] Open Door";
            closePrompt = "[F] Close Door";
            proximityInteractor = interactor;
            proximityDistance = interactionDistance;
            useProximityInput = true;

            closedPosition = closedLocalPosition;
            openPositionOffset = (authoredOpenPosition - closedPosition) * 2f;
            closedRotation = transform.localRotation;
            isOpen = startOpen;
            targetPosition = isOpen ? closedPosition + openPositionOffset : closedPosition;
            targetRotation = closedRotation;
            transform.localPosition = targetPosition;
        }

        /// <summary>Configures a sliding leaf with an exact authored local-space travel vector.</summary>
        public void ConfigureSlidingDoor(Transform interactor, Vector3 closedLocalPosition,
            Vector3 localOpenOffset, float interactionDistance = 3.5f, bool startOpen = false)
        {
            doorType = DoorType.SlidingGate;
            openPrompt = "[F] Open Door";
            closePrompt = "[F] Close Door";
            proximityInteractor = interactor;
            proximityDistance = interactionDistance;
            useProximityInput = true;

            closedPosition = closedLocalPosition;
            openPositionOffset = localOpenOffset;
            closedRotation = transform.localRotation;
            isOpen = startOpen;
            targetPosition = isOpen ? closedPosition + openPositionOffset : closedPosition;
            targetRotation = closedRotation;
            transform.localPosition = targetPosition;
        }

        public void OnHoverEnter() { }

        public void OnHoverExit() { }

        public void Interact(Transform interactor)
        {
            if (isLocked)
            {
                PlaySound(lockedSound);
                return;
            }

            isOpen = !isOpen;

            if (doorType == DoorType.HingeSwing)
            {
                targetRotation = isOpen ? closedRotation * Quaternion.Euler(openRotationOffset) : closedRotation;
            }
            else if (doorType == DoorType.SlidingGate)
            {
                targetPosition = isOpen ? closedPosition + openPositionOffset : closedPosition;
            }

            PlaySound(isOpen ? openSound : closeSound);

            if (transitionCoroutine != null)
                StopCoroutine(transitionCoroutine);

            transitionCoroutine = StartCoroutine(AnimateDoor());
        }

        public void SetLocked(bool locked)
        {
            isLocked = locked;
        }

        private IEnumerator AnimateDoor()
        {
            while (Vector3.Distance(transform.localPosition, targetPosition) > 0.001f ||
                   Quaternion.Angle(transform.localRotation, targetRotation) > 0.1f)
            {
                transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, Time.deltaTime * openSpeed);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, Time.deltaTime * openSpeed);
                yield return null;
            }

            transform.localPosition = targetPosition;
            transform.localRotation = targetRotation;
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }
    }
}
