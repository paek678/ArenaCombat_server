using UnityEngine;

public class QuitController : MonoBehaviour
{
    // 버튼에 연결할 게임 종료 함수
    public void QuitGame()
    {
        #if UNITY_EDITOR
        // 유니티 에디터에서 실행 중일 때는 플레이 모드를 종료합니다.
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        // 실제 빌드된 게임(PC, 모바일 등)에서는 게임을 완전히 종료합니다.
        Application.Quit();
        #endif

        Debug.Log("게임 종료 버튼이 눌렸습니다.");
    }
}