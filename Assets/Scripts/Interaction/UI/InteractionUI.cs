using UnityEngine;
using TMPro;

namespace Operation1989.Interaction
{
    /// <summary>
    /// Displays interaction prompt UI when player aims at IInteractable objects.
    /// Supports legacy UI Text and TextMeshProUGUI.
    /// </summary>
    public class InteractionUI : MonoBehaviour
    {
        public static InteractionUI Instance { get; private set; }

        [Header("UI References")]
        [SerializeField] private GameObject promptContainer;
        [SerializeField] private TextMeshProUGUI promptTextTMP;
        [SerializeField] private UnityEngine.UI.Text promptTextLegacy;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            HidePrompt();
        }

        public void ShowPrompt(string text)
        {
            if (promptContainer != null)
                promptContainer.SetActive(true);

            if (promptTextTMP != null)
                promptTextTMP.text = text;

            if (promptTextLegacy != null)
                promptTextLegacy.text = text;
        }

        public void HidePrompt()
        {
            if (promptContainer != null)
                promptContainer.SetActive(false);

            if (promptTextTMP != null)
                promptTextTMP.text = string.Empty;

            if (promptTextLegacy != null)
                promptTextLegacy.text = string.Empty;
        }
    }
}
