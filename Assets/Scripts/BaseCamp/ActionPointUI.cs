using TMPro;
using UnityEngine;

// 기존 씬 참조를 유지하며 날짜를 표시한다. M5부터 AP는 사용하지 않는다.
public class ActionPointUI : MonoBehaviour
{
    [SerializeField]
    private TMP_Text actionPointText;
    private int lastDisplayedDay = -1;

    private void Awake()
    {
        if (actionPointText == null)
        {
            Debug.LogError("날짜 표시 Text를 연결해 주세요.");
            enabled = false;
        }
    }

    private void Start() => RefreshUI();
    private void Update() => RefreshUI();

    private void RefreshUI()
    {
        int day = RunProgress.CurrentDay;
        if (lastDisplayedDay == day) return;
        lastDisplayedDay = day;
        actionPointText.text = $"DAY {day}";
    }
}
