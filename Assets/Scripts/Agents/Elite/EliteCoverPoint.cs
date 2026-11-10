using UnityEngine;

/// <summary>Standing protection only: no crouch animation is assumed.</summary>
public sealed class EliteCoverPoint : MonoBehaviour
{
    public bool ProtectedFrom(Vector3 knownThreat, Transform guard, LayerMask obstacles)
        => !ElitePerception.Clear(knownThreat, transform.position + Vector3.up * 1.3f, guard, null, obstacles);

    void OnDrawGizmosSelected() { Gizmos.color = Color.green; Gizmos.DrawWireSphere(transform.position, .35f); }
}
