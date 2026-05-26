using UnityEngine;

public class CanvasController : MonoBehaviour
{
    // 띄우고 싶은 캔버스나 패널 오브젝트를 인스펙터에서 드래그 앤 드롭할 변수
    [SerializeField] private GameObject targetCanvas;

    // 버튼을 누르면 실행될 함수
    public void OpenCanvas()
    {
        if (targetCanvas != null)
        {
            targetCanvas.SetActive(true); // 캔버스 켜기
        }
    }

    // (옵션) 나중에 닫기 버튼에 연결해서 쓸 함수
    public void CloseCanvas()
    {
        if (targetCanvas != null)
        {
            targetCanvas.SetActive(false); // 캔버스 끄기
        }
    }
}