
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float gravity = -9.81f;

    [Header("Ladder Movement")]
    [Tooltip("Vertical speed while the player is inside a LadderClimbZone.")]
    public float ladderClimbSpeed = 3.5f;
    [Tooltip("Sideways movement used to step off a ladder.")]
    public float ladderExitSpeed = 2.5f;

    [Header("Mouse Look")]
    public Transform playerCamera;
    public float mouseSensitivity = 0.15f;
    public float maxLookAngle = 80f;
    public float LookSensitivityMultiplier { get; set; } = 1f;

    private readonly PlayerTreePassage treePassage = new PlayerTreePassage();
    private float nextTreeScan;
    private CharacterController controller;
    private float verticalRotation;
    private float verticalVelocity;
    private IntroTruckSequence introSequence;
    private Operation1989.Interaction.Environment.LadderClimbZone currentLadder;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        treePassage.Refresh(transform);
        introSequence = FindAnyObjectByType<IntroTruckSequence>();

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main.transform;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (GamePauseMenu.BlocksInput) return;
        if (Time.unscaledTime >= nextTreeScan)
        {
            nextTreeScan = Time.unscaledTime + 1f;
            treePassage.Refresh(transform);
        }
        treePassage.KeepPairsIgnored();
        HandleMouseLook();
        HandleMovement();

        // Mission buttons and their close shortcut own this click; do not recapture the cursor.
        if (introSequence && introSequence.MissionCapturesPointer) return;

        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void ResetLook()
    {
        treePassage.Refresh(transform);
        nextTreeScan = 0;
        verticalRotation = 0;
        verticalVelocity = 0;
        if (playerCamera) playerCamera.localRotation = Quaternion.identity;
    }

    void HandleMouseLook()
    {
        if (Mouse.current == null ||
            Cursor.lockState != CursorLockMode.Locked ||
            playerCamera == null)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * LookSensitivityMultiplier;

        transform.Rotate(Vector3.up * mouseDelta.x * mouseSensitivity);

        verticalRotation -= mouseDelta.y * mouseSensitivity;
        verticalRotation = Mathf.Clamp(
            verticalRotation, -maxLookAngle, maxLookAngle
        );

        playerCamera.localRotation =
            Quaternion.Euler(verticalRotation, 0f, 0f);
    }

    void HandleMovement()
    {
        if (Keyboard.current == null)
            return;

        // CharacterController trigger callbacks are not reliable on every imported
        // ladder prefab, so also query the climb volumes directly each frame.
        currentLadder = FindNearbyLadder();

        Vector2 input = Vector2.zero;

        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;

        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 moveDirection =
            transform.forward * input.y +
            transform.right * input.x;

        if (currentLadder != null)
        {
            // W/S climb. A/D lets the player step away from the ladder; leaving the
            // trigger naturally restores normal gravity and movement.
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                currentLadder = null;
                verticalVelocity = 0f;
                controller.Move(transform.forward * ladderExitSpeed * Time.deltaTime);
                return;
            }

            verticalVelocity = 0f;
            if (input.y > 0f && currentLadder.ShouldExitAtTop(transform.position))
            {
                Vector3 dismountVelocity = currentLadder.GetTopExitDirection(transform.position) * ladderExitSpeed +
                                            Vector3.up * (ladderClimbSpeed * 0.35f);
                controller.Move(dismountVelocity * Time.deltaTime);
                return;
            }

            Vector3 ladderVelocity = Vector3.up * input.y * ladderClimbSpeed +
                                     transform.right * input.x * ladderExitSpeed;
            controller.Move(ladderVelocity * Time.deltaTime);
            return;
        }

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity =
            moveDirection * moveSpeed +
            Vector3.up * verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    private void OnDestroy() => treePassage.Dispose();

    private void OnTriggerEnter(Collider other)
    {
        var ladder = other.GetComponentInParent<Operation1989.Interaction.Environment.LadderClimbZone>();
        if (ladder != null)
            currentLadder = ladder;
    }

    private void OnTriggerExit(Collider other)
    {
        var ladder = other.GetComponentInParent<Operation1989.Interaction.Environment.LadderClimbZone>();
        if (ladder != null && ladder == currentLadder)
            currentLadder = null;
    }

    private Operation1989.Interaction.Environment.LadderClimbZone FindNearbyLadder()
    {
        Vector3 center = transform.TransformPoint(controller.center);
        float halfHeight = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        Vector3 bottom = center - Vector3.up * halfHeight;
        Vector3 top = center + Vector3.up * halfHeight;

        foreach (var hit in Physics.OverlapCapsule(bottom, top, controller.radius + 0.15f,
                     ~0, QueryTriggerInteraction.Collide))
        {
            var ladder = hit.GetComponentInParent<Operation1989.Interaction.Environment.LadderClimbZone>();
            if (ladder != null)
                return ladder;
        }

        return null;
    }
}


/// <summary>Player-only tree passage. Tree colliders still participate in rays and other actors' collision queries.</summary>
public sealed class PlayerTreePassage : System.IDisposable
{
    readonly System.Collections.Generic.List<(Collider player, Collider tree)> pairs =
        new System.Collections.Generic.List<(Collider player, Collider tree)>();
    readonly System.Collections.Generic.HashSet<(Collider, Collider)> owned = new System.Collections.Generic.HashSet<(Collider, Collider)>();
    readonly System.Collections.Generic.List<Collider> bodies = new System.Collections.Generic.List<Collider>();
    readonly System.Collections.Generic.List<Collider> sceneColliders = new System.Collections.Generic.List<Collider>();
    readonly System.Collections.Generic.List<GameObject> sceneRoots = new System.Collections.Generic.List<GameObject>();
    readonly System.Collections.Generic.Dictionary<Transform, bool> treeHierarchy = new System.Collections.Generic.Dictionary<Transform, bool>();

    public static bool IsTreeCollider(Collider collider)
    {
        if (!collider || collider is TerrainCollider || collider.isTrigger) return false;
        for (var current = collider.transform; current; current = current.parent)
        {
            string name = current.name;
            if (current.GetComponent<Tree>() || name.StartsWith("TreeOld_", System.StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("TreeCreator_", System.StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Tree", System.StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Tree_", System.StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Tree (", System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    public void Refresh(Transform player)
    {
        if (!player) return;
        player.GetComponentsInChildren<Collider>(true, bodies);
        treeHierarchy.Clear(); // Renames/reparenting and newly spawned trees are seen on every scan.
        bool playerSceneIncluded = false;
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (scene == player.gameObject.scene) playerSceneIncluded = true;
            RefreshScene(scene, player);
        }
        // SceneManager.sceneCount does not include the DontDestroyOnLoad scene.
        if (!playerSceneIncluded) RefreshScene(player.gameObject.scene, player);
        KeepPairsIgnored();
    }
    bool IsTreeHierarchy(Transform current)
    {
        if (!current) return false;
        if (treeHierarchy.TryGetValue(current, out bool result)) return result;
        string name = current.name;
        result = current.GetComponent<Tree>() || name.StartsWith("TreeOld_", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("TreeCreator_", System.StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Tree", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Tree_", System.StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Tree (", System.StringComparison.OrdinalIgnoreCase) || IsTreeHierarchy(current.parent);
        treeHierarchy[current] = result;
        return result;
    }
    void RefreshScene(UnityEngine.SceneManagement.Scene scene, Transform player)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        scene.GetRootGameObjects(sceneRoots);
        foreach (var root in sceneRoots)
        {
            root.GetComponentsInChildren<Collider>(true, sceneColliders);
            foreach (var tree in sceneColliders)
            {
                if (!tree || tree is TerrainCollider || tree.isTrigger || !tree.enabled || !tree.gameObject.activeInHierarchy ||
                    !IsTreeHierarchy(tree.transform)) continue;
                foreach (var body in bodies)
                {
                    if (!body || !body.enabled || !body.gameObject.activeInHierarchy || body.isTrigger || body == tree || tree.transform.IsChildOf(player)) continue;
                    var key = (body, tree);
                    if (!owned.Contains(key) && !Physics.GetIgnoreCollision(body, tree))
                    {
                        owned.Add(key); pairs.Add((body, tree));
                    }
                }
            }
        }
    }
    public void KeepPairsIgnored()
    {
        foreach (var pair in pairs)
            if (pair.player && pair.tree && pair.player.enabled && pair.tree.enabled &&
                !Physics.GetIgnoreCollision(pair.player, pair.tree)) Physics.IgnoreCollision(pair.player, pair.tree, true);
    }
    public void Dispose()
    {
        foreach (var pair in pairs)
            if (pair.player && pair.tree) Physics.IgnoreCollision(pair.player, pair.tree, false);
        pairs.Clear(); owned.Clear();
        bodies.Clear(); sceneColliders.Clear(); sceneRoots.Clear(); treeHierarchy.Clear();
    }
}
