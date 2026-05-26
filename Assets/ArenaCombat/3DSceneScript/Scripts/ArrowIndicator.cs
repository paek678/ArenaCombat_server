//using UnityEngine;
//using UnityEngine.EventSystems; // UI 체크용

//public class ArrowIndicator : MonoBehaviour
//{
//    public Transform player;        // 플레이어 Transform 참조
//    public float distance = 2f;     // 플레이어 기준 거리
//    public float offsetY = 1f;      // 높이 오프셋

//    void Update()
//    {
//        if (player == null) return;

//        // UI 위에 마우스가 있으면 무시
//        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
//            return;

//        // 마우스 포인터 방향으로 Raycast
//        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
//        RaycastHit hit;

//        if (Physics.Raycast(ray, out hit))
//        {
//            // 마우스가 가리키는 지점
//            Vector3 targetPoint = hit.point;

//            // 플레이어 위치에서 targetPoint까지의 방향 (y축은 무시)
//            Vector3 dir = targetPoint - player.position;
//            dir.y = 0; // y축 고정
//            dir.Normalize();

//            // 화살표 위치: 플레이어 앞쪽 distance 만큼 + 높이 오프셋
//            transform.position = player.position + dir * distance + Vector3.up * offsetY;

//            // 화살표 회전: 마우스 방향을 바라보도록
//            if (dir != Vector3.zero)
//                transform.rotation = Quaternion.LookRotation(dir);
//        }
//    }

//    // 스킬 발사 방향을 가져올 수 있는 함수
//    public Vector3 GetDirection()
//    {
//        return transform.forward;
//    }
//}