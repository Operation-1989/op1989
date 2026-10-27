using UnityEngine;

namespace Operation1989.Interaction.Environment
{
    /// <summary>
    /// Marks colliders added at runtime by WorldInteractionBootstrap.
    /// Cinematic vehicles can ignore these without disabling player collision.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedEnvironmentCollider : MonoBehaviour
    {
    }
}
