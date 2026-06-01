using UnityEngine;
using Unity.Netcode;
using ArenaCombat.Core.Network;

namespace ArenaCombat.Core
{
    [DisallowMultipleComponent]
    public class BossVisualFollower : MonoBehaviour
    {
        [SerializeField] float _rotationSpeed = 8f;

        Transform _bossRoot;

        public void Initialize(Transform bossRoot)
        {
            _bossRoot = bossRoot;
            transform.SetParent(null);
            transform.position = bossRoot.position;
            transform.rotation = bossRoot.rotation;
        }

        void LateUpdate()
        {
            if (_bossRoot == null)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = _bossRoot.position;

            Transform nearest = FindNearestAlivePlayer();
            if (nearest == null) return;

            Vector3 dir = nearest.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, Time.deltaTime * _rotationSpeed);
        }

        static Transform FindNearestAlivePlayer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return null;

            float bestDist = float.MaxValue;
            Transform best = null;

            foreach (var client in nm.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                var pnc = client.PlayerObject.GetComponent<PlayerNetworkController3D>();
                if (pnc == null || !pnc.IsAlive) continue;

                float dist = Vector3.Distance(
                    client.PlayerObject.transform.position,
                    BossManager.Instance != null && BossManager.Instance.CurrentBoss != null
                        ? BossManager.Instance.CurrentBoss.transform.position
                        : Vector3.zero);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = client.PlayerObject.transform;
                }
            }
            return best;
        }
    }
}
