using System;
using System.Collections.Generic;
using UnityEngine;

// 현재 판정할 노트와 다음 노트를 조회한다.

// 실제 판정은 RhythmInputTester에서 처리한다.
// 이 클래스는 목록과 현재 위치를 관리한다.
public class RhythmEventTester : MonoBehaviour
{
    [SerializeField]
    private RhythmPatternGenerator patternGenerator;

    // 접근 설정은 생성기에서 읽는다.
    // 같은 설정을 이 클래스에 중복 저장하지 않는다.
    public int ApproachBeats =>
        patternGenerator != null
            ? patternGenerator.ApproachBeats
            : 0;

    public double ApproachSeconds =>
        patternGenerator != null
            ? patternGenerator.ApproachSeconds
            : 0.0;

    // 연결 전에는 빈 목록을 사용한다.
    private IReadOnlyList<RhythmNoteData> notes =
        Array.Empty<RhythmNoteData>();

    private int currentEventIndex;

    public bool HasCurrentEvent =>
        currentEventIndex < notes.Count;

    // 현재 나무의 종류, 방향, 목표 시각을 함께 조회할 수 있다.
    // 호출 전에 HasCurrentEvent를 확인해야 한다.
    public RhythmNoteData CurrentNote =>
        notes[currentEventIndex];

    // 기존 입력기와 표시기가 사용하는 시간 조회 이름을 유지한다.
    // 별도 시간을 저장하지 않고 현재 노트에서 읽는다.
    public double EventTimeSeconds =>
        CurrentNote.TargetTimeSeconds;

    private void Start()
    {
        if (patternGenerator == null)
        {
            Debug.LogError(
                "Rhythm Pattern Generator를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        if (ApproachBeats < 1)
        {
            Debug.LogError(
                "Approach Beats는 1 이상이어야 합니다.",
                this);

            enabled = false;
            return;
        }

        // BPM 선택 전에는 ApproachSeconds가 0일 수 있다.
        // 따라서 이 시점에서 0을 초기화 오류로 처리하지 않는다.

        // 복사본이 아닌 원본 노트 목록을 참조한다.
        // 이후 구간 분석으로 추가되는 노트도 확인할 수 있다.
        notes = patternGenerator.Notes;
    }

    public void ResetEvents()
    {
        currentEventIndex = 0;
    }

    public bool MoveNextEvent()
    {
        if (!HasCurrentEvent)
        {
            return false;
        }

        // 노트를 삭제하지 않고 현재 위치만 이동한다.
        currentEventIndex++;

        return HasCurrentEvent;
    }

    // 현재 위치를 기준으로 노트 전체 정보를 조회한다.

    // offset 0: 현재 판정할 노트
    // offset 1: 다음 노트
    // offset 2: 그다음 노트

    // 조회만 하므로 현재 판정 위치는 바뀌지 않는다.
    public bool TryGetUpcomingNote(
        int offset,
        out RhythmNoteData note)
    {
        note = null;

        int remainingCount =
            notes.Count - currentEventIndex;

        if (offset < 0 || offset >= remainingCount)
        {
            return false;
        }

        note = notes[currentEventIndex + offset];
        return true;
    }
}