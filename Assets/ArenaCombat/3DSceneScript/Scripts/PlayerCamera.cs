using ArenaCombat.Core;
using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Vector3 offset = new Vector3(0f, 15f, -5f);
    [SerializeField] private float smoothSpeed = 5f;

    private TopDownCameraFollow3D topDownFollow3D;

    public void SetTarget(Transform target)
    {
        player = target;
    }

    public Transform GetTarget()
    {
        return player;
    }

    public void ClearTarget()
    {
        player = null;
    }

    private void Awake()
    {
        topDownFollow3D = GetComponent<TopDownCameraFollow3D>();
    }

    private void LateUpdate()
    {
        // When the network follow component is active, avoid double-driving camera transform.
        if (topDownFollow3D != null && topDownFollow3D.enabled)
        {
            return;
        }

        if (player == null)
        {
            return;
        }

        Vector3 targetPosition = player.position + offset;
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);
    }
}
