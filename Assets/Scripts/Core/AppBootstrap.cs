using UnityEngine; // unity에서 제공하는 타입을 이름만으로 사용할 수 있게 함

public class AppBootstrap : MonoBehaviour
    // unity 의 gameobject에 붙일 수 있는 Component 클래스 정의
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
        // unity가 첫 Update()함수 전에 한번 호출하는 함수(초기화 함수)
    {
        Debug.Log("Rhythm Defense boot completed.");
        // C#의 Console.WirteLine과 같은 메세지 출력함수
    }
}