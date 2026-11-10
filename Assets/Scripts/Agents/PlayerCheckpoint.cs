using UnityEngine;

/// <summary>Activates only after the living, controllable player's root is inside the authored volume.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class PlayerCheckpoint : MonoBehaviour
{
    public CombatHealth player;
    public BoxCollider activationVolume;
    public Transform respawnPoint;
    public string checkpointName = "Main building entrance";
    public int priority = 10;

    void Awake()
    {
        if (!activationVolume) activationVolume = GetComponent<BoxCollider>();
        activationVolume.isTrigger = true;
    }
    public bool Contains(Vector3 point)
    {
        if (!activationVolume || !activationVolume.enabled) return false;
        Vector3 local = activationVolume.transform.InverseTransformPoint(point) - activationVolume.center;
        Vector3 half = activationVolume.size * .5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }
    public bool TryActivate()
    {
        if (!isActiveAndEnabled || !player || !player.isPlayer || player.Dead || !respawnPoint || !Contains(player.transform.position)) return false;
        var controls = player.GetComponent<FirstPersonController>();
        if (!controls || !controls.isActiveAndEnabled) return false;
        var state = player.GetComponent<PlayerCheckpointState>();
        if (!state) state = player.gameObject.AddComponent<PlayerCheckpointState>();
        return state.Activate(this);
    }
    // Polling also covers CharacterController trigger callbacks missed by imported collision geometry.
    void Update() => TryActivate();
    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<CombatHealth>() == player) TryActivate();
    }
    void OnDrawGizmosSelected()
    {
        if (activationVolume)
        {
            var previous = Gizmos.matrix; Gizmos.matrix = activationVolume.transform.localToWorldMatrix;
            Gizmos.color = Color.green; Gizmos.DrawWireCube(activationVolume.center, activationVolume.size); Gizmos.matrix = previous;
        }
        if (respawnPoint) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(respawnPoint.position, .4f); Gizmos.DrawRay(respawnPoint.position, respawnPoint.forward * 2); }
    }
}
