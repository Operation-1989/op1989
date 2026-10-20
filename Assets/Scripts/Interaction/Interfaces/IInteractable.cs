using UnityEngine;

namespace Operation1989.Interaction
{
    /// <summary>
    /// Core interface implemented by all interactable objects in the game world.
    /// Used by Student 2 (Systems Engineer) for player interaction physics and mechanics.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// Prompt string shown in the UI crosshair (e.g., "[E] Open Gate", "[E] Move Barricade").
        /// </summary>
        string InteractionPrompt { get; }

        /// <summary>
        /// Called when the player starts aiming at this object.
        /// </summary>
        void OnHoverEnter();

        /// <summary>
        /// Called when the player stops aiming at this object.
        /// </summary>
        void OnHoverExit();

        /// <summary>
        /// Primary action triggered when player presses the interaction key.
        /// </summary>
        /// <param name="interactor">Transform of the player or camera initiating interaction.</param>
        void Interact(Transform interactor);
    }
}
