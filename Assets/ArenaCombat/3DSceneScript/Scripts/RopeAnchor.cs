//using UnityEngine;

//public class RopeAnchor : MonoBehaviour
//{
//    private RopeAction ropeAction;

//    public void Init(RopeAction action)
//    {
//        ropeAction = action;
//    }

//    void OnCollisionEnter(Collision collision)
//    {
//        if (collision.collider.CompareTag("Wall"))
//        {
//            // 닻을 벽에 고정
//            Rigidbody rb = GetComponent<Rigidbody>();
//            if (rb != null) rb.isKinematic = true;

//            // 플레이어 끌기 시작
//            ropeAction.OnRopeHit(transform.position);
//        }
//    }
//}