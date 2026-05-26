using UnityEngine;
using UnityEngine.EventSystems; // 이벤트 시스템을 위해 반드시 필요합니다!

public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Vector3 originalScale;
    [SerializeField] private float hoverScaleMultiplier = 1.1f; // 호버 시 커질 배율

    void Start()
    {
        // 처음 시작할 때 오브젝트의 원래 크기를 저장합니다.
        originalScale = transform.localScale;
    }

    // 마우스가 버튼 영역 안으로 들어올 때 실행
    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.localScale = originalScale * hoverScaleMultiplier;
        Debug.Log("마우스가 버튼 위에 올라왔습니다!");
    }

    // 마우스가 버튼 영역 밖으로 나갈 때 실행
    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = originalScale;
        Debug.Log("마우스가 버튼을 벗어났습니다!");
    }
}