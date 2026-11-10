using UnityEngine;

/// <summary>Identifies the single solid collider enclosing an existing server rack.</summary>
[DisallowMultipleComponent]
public sealed class EliteServerObstacle : MonoBehaviour
{
    public BoxCollider volume;
    public bool ProvidesCollision => volume && volume.enabled && !volume.isTrigger;
}
