using System;

// 숲에서 등장할 작업/방해 오브젝트의 종류.
// 여기서는 이름만 정의한다. 실제 동작은 이후 판정기와 표시기가 담당한다.
public enum ForestNoteKind
{
    Tap = 0,      // 한 번 누르는 일반 나무.
    Hold = 1,     // 누르고 유지한 뒤 끝 시각에 놓는 나무.
    Sync = 2,     // 양쪽 나무를 하나의 입력으로 처리.
    Rock = 3,     // 건드리지 않고 지나가야 하는 바위.
    Hive = 4,     // 건드리지 않고 지나가야 하는 벌집.
    Thief = 5,    // 임시 목재를 노리는 도둑.
    Recovery = 6  // 특정 도둑이 훔친 목재를 회수할 기회.
}

// 오브젝트가 나타나는 방향.
// 실제 키 할당은 입력 시스템에서 정한다.
public enum RhythmInputSide
{
    Left = 0,
    Right = 1,
    Both = 2,
    Upper = 3
}

// 노트 한 개의 '계획된 정보'를 보관한다.
// MonoBehaviour를 상속하지 않는 일반 C# 클래스다.
// 나중에 패턴 생성기에서 new RhythmNoteData(...)로 만들어 사용한다.
public sealed class RhythmNoteData
{
    // 이번 패턴 안에서 노트를 구분하는 고유 번호.
    // 0부터 사용할 수 있다.

    // 서로 다른 노트에 같은 Id를 주지 않는 일은
    // 패턴 생성기가 담당한다.
    public int Id { get; } // 노드 구분 번호

    public ForestNoteKind Kind { get; } // 노드의 종류

    public RhythmInputSide Side { get; } // 노드의 방향

    // 곡이 시작된 지 몇 초에 입력해야 하는지 나타낸다.
    // 프레임 번호나 AudioSettings.dspTime의 절대 시각이 아니다.
    public double TargetTimeSeconds { get; } // 곡 기준 입력 목표 시간

    // Hold는 버튼을 놓아야 하는 곡 시각.
    // 나머지 종류는 TargetTimeSeconds와 같은 값을 사용한다.
    public double EndTimeSeconds { get; } // Hold 해제 목표 시각

    // Recovery가 어느 도둑의 약탈과 연결되는지 나타낸다.
 
    // 관련 도둑이 없는 일반 노트는 -1을 사용한다.
    // 0은 실제 노트 Id로 쓸 수 있으므로
    // '없음' 표시로 사용하지 않는다.
    public int RelatedThiefId { get; } // 회수 대상 몬스터의 ID

    // => 오른쪽 조건을 조회할 때 계산한다.
    //
    // Kind와 별도로 bool 값을 저장하지 않아
    // 서로 다른 값이 되는 것을 막는다.
    public bool IsHold =>
        Kind == ForestNoteKind.Hold;

    public bool IsResource =>
        Kind == ForestNoteKind.Tap ||
        Kind == ForestNoteKind.Hold ||
        Kind == ForestNoteKind.Sync;

    public bool IsHazard =>
        Kind == ForestNoteKind.Rock ||
        Kind == ForestNoteKind.Hive;

    // 유지 시간 = 종료 시각 - 시작 목표 시각.
    // 예: 10초에 누르고 12초에 놓으면 12 - 10 = 2초.

    // Hold 이외에는 시작과 종료가 같으므로 0초다.
    public double DurationSeconds =>
        EndTimeSeconds - TargetTimeSeconds;

    // 생성자: new로 노트를 만들 때 한 번 실행된다.

    // get만 있는 프로퍼티는 생성자에서 값을 설정하고
    // 이후에는 읽기만 한다.
    public RhythmNoteData(
        int id,
        ForestNoteKind kind,
        RhythmInputSide side,
        double targetTimeSeconds,
        double endTimeSeconds,
        int relatedThiefId = -1)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id),
                "노트 Id는 0 이상이어야 합니다.");
        }

        // 숫자를 enum으로 강제 변환한 경우에도
        // 정의된 종류/방향인지 확인한다.
        if (!Enum.IsDefined(typeof(ForestNoteKind), kind) ||
            !Enum.IsDefined(typeof(RhythmInputSide), side))
        {
            throw new ArgumentException(
                "정의되지 않은 노트 종류 또는 방향입니다.");
        }

        // NaN은 유효한 숫자가 아닌 값,
        // Infinity는 무한대를 뜻한다.
        // 판정할 곡 시각에는 둘 다 사용할 수 없다.
        if (!IsValidTime(targetTimeSeconds) ||
            !IsValidTime(endTimeSeconds))
        {
            throw new ArgumentException(
                "노트 시각은 0 이상의 유한한 초 값이어야 합니다.");
        }

        bool isHold = kind == ForestNoteKind.Hold;

        // Hold에는 실제 유지 구간이 있어야 한다.
        // 일반 노트에는 별도의 종료 판정을 만들지 않는다.
        if ((isHold && endTimeSeconds <= targetTimeSeconds) ||
            (!isHold && endTimeSeconds != targetTimeSeconds))
        {
            throw new ArgumentException(
                "Hold는 종료가 목표보다 늦어야 하며, " +
                "나머지는 두 시각이 같아야 합니다.");
        }

        // 종류와 방향이 어긋난 데이터를 처음부터 차단한다.
        bool isValidSide;

        if (kind == ForestNoteKind.Sync)
        {
            isValidSide = side == RhythmInputSide.Both;
        }

        else if (kind == ForestNoteKind.Thief ||
                 kind == ForestNoteKind.Recovery)
        {
            isValidSide = side == RhythmInputSide.Upper;
        }

        else
        {
            isValidSide =
                side == RhythmInputSide.Left ||
                side == RhythmInputSide.Right;
        }

        if (!isValidSide)
        {
            throw new ArgumentException(
                "노트 종류에 맞는 방향을 지정해 주세요.");
        }

        // 회수 노트는 자신과 다른 도둑 Id가 필요하다.
        // 그 Id의 도둑이 실제 목록에 있는지는
        // 이후 패턴 생성기가 검사한다.
        if (kind == ForestNoteKind.Recovery)
        {
            if (relatedThiefId < 0 || relatedThiefId == id)
            {
                throw new ArgumentException(
                    "회수 대상 도둑의 Id를 지정해 주세요.");
            }
        }
        else if (relatedThiefId != -1)
        {
            throw new ArgumentException(
                "Recovery 이외의 Related Thief Id는 -1입니다.");
        }

        // 검사를 통과한 노트의 원본 정보를 보관한다.
        Id = id;
        Kind = kind;
        Side = side;
        TargetTimeSeconds = targetTimeSeconds;
        EndTimeSeconds = endTimeSeconds;
        RelatedThiefId = relatedThiefId;
    }

    private static bool IsValidTime(double seconds)
    {
        return !double.IsNaN(seconds) &&
               !double.IsInfinity(seconds) &&
               seconds >= 0.0;
    }
}