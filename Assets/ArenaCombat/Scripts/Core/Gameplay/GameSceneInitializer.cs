// ARCH TAG: SHARED
// ARCH SCOPE: Game scene bootstrap validation. Disconnect handling owned by SessionManager.
// ARCH STATUS: TARGET_3D_ACTIVE

using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using ArenaCombat.Core.Network;

namespace ArenaCombat.Core
{
    /// <summary>
    /// Validates required managers on game scene load.
    /// Falls back to title scene if a critical manager is missing.
    /// </summary>
    public class GameSceneInitializer : MonoBehaviour
    {
        [SerializeField] private string titleSceneName = "SampleScene";

        private void Start()
        {
            Debug.Log("[GameSceneInitializer] Game scene initialization started");

            if (!ValidateManagers())
                return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                Debug.Log("[GameSceneInitializer] Server ready in game scene");

            Debug.Log("[GameSceneInitializer] Game scene initialization complete");
        }

        private bool ValidateManagers()
        {
            bool valid = true;

            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[GameSceneInitializer] NetworkManager not found");
                valid = false;
            }

            if (RelayManager.Instance == null)
                Debug.LogWarning("[GameSceneInitializer] RelayManager not found");

            if (LobbyManager.Instance == null)
                Debug.LogWarning("[GameSceneInitializer] LobbyManager not found");

            if (SessionManager.Instance == null)
                Debug.LogWarning("[GameSceneInitializer] SessionManager not found");

            if (!valid)
            {
                Debug.LogError("[GameSceneInitializer] Required manager missing. Returning to title.");
                SceneManager.LoadScene(titleSceneName);
            }

            return valid;
        }
    }
}
