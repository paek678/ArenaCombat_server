using System;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using UnityEngine;

namespace ArenaCombat.Core.Network
{
    internal static class LobbyServiceHelper
    {
        private const string LogPrefix = "[LobbyManager]";

        public static async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            string opName,
            Action<string> onError = null)
        {
            try
            {
                return await operation();
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"{LogPrefix} {opName} failed: {e.Message}");
                onError?.Invoke($"{opName} failed: {e.Message}");
                return default;
            }
        }

        public static async Task ExecuteAsync(
            Func<Task> operation,
            string opName,
            Action<string> onError = null)
        {
            try
            {
                await operation();
            }
            catch (LobbyServiceException e)
            {
                Debug.LogError($"{LogPrefix} {opName} failed: {e.Message}");
                onError?.Invoke($"{opName} failed: {e.Message}");
            }
        }
    }
}
