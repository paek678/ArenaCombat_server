using ArenaCombat.UI.Utility;
using UnityEngine;

namespace ArenaCombat.UI.TestUI
{
    public class OutgoingDataLog : MonoBehaviour
    {
        private const string LogPrefix = "OutgoingData";

        [SerializeField] private ScrollableLogDisplay logDisplay;

        public void LogOutgoingData(string dataType, string dataContent)
        {
            if (!logDisplay.IsValid) return;
            logDisplay.AppendRich(
                $"<color=#00FF00>[{dataType}]</color> {dataContent}",
                LogPrefix);
        }

        public void LogServerRpc(string rpcName, string parameters = "")
        {
            string content = string.IsNullOrEmpty(parameters) ? rpcName : $"{rpcName}({parameters})";
            LogOutgoingData("ServerRpc", content);
        }

        public void LogInputData(Vector2 moveInput, bool jump, bool attack)
        {
            string content = $"Move({moveInput.x:F2}, {moveInput.y:F2}) Jump:{jump} Attack:{attack}";
            LogOutgoingData("Input", content);
        }

        public void LogPositionSync(Vector3 position, Quaternion rotation)
        {
            string content = $"Pos({position.x:F2}, {position.y:F2}, {position.z:F2}) Rot({rotation.eulerAngles.y:F1}deg)";
            LogOutgoingData("Position", content);
        }

        public void LogCustomData(string message)
        {
            LogOutgoingData("Custom", message);
        }

        public void LogError(string errorMessage)
        {
            if (!logDisplay.IsValid) return;
            logDisplay.AppendRich(
                $"<color=#FF0000>[ERROR]</color> <color=#FFAAAA>{errorMessage}</color>",
                LogPrefix, LogType.Error);
        }

        public void LogWarning(string warningMessage)
        {
            if (!logDisplay.IsValid) return;
            logDisplay.AppendRich(
                $"<color=#FFFF00>[WARNING]</color> <color=#FFFFAA>{warningMessage}</color>",
                LogPrefix, LogType.Warning);
        }

        public void LogServerRpcFailed(string rpcName, string reason)
        {
            LogError($"ServerRpc failed: {rpcName} - {reason}");
        }

        public void LogNetworkError(string operation, string errorDetail)
        {
            LogError($"Network error [{operation}]: {errorDetail}");
        }

        public void ClearLog()
        {
            logDisplay.Clear();
        }
    }
}
