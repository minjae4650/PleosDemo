using Parkside.Core;
using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// Fades the diagnostics panel out while driving so demo recordings stay
    /// clean, and brings it back when practice pauses. H toggles it manually.
    /// </summary>
    public sealed class DriveHudAutoHide : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private CanvasGroup panel;
        private bool manualShow;

        public CanvasGroup PanelOrNull => panel;

        private void Update()
        {
            if (gameManager == null || panel == null) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.H)) manualShow = !manualShow;
            if (!gameManager.IsRunning) manualShow = false;
            var visible = manualShow || !gameManager.IsRunning;
            panel.alpha = Mathf.MoveTowards(panel.alpha, visible ? 1f : 0f, Time.deltaTime * 3f);
            panel.interactable = visible;
            panel.blocksRaycasts = visible;
        }
    }
}
