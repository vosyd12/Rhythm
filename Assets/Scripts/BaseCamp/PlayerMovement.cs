using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 플레이어의 좌우 이동, 평지 점프, 상호작용 입력을 담당한다.
// Player에 부착하면 자식 Visual도 함께 움직인다.
public class PlayerMovement : MonoBehaviour
{
    [SerializeField]
    private Transform playerSpawn; // 플레이어 스폰위치

    [SerializeField]
    private Transform leftBoundary; // 왼쪽 끝에 해당하는 좌표값

    [SerializeField]
    private Transform rightBoundary; // 오른쪽 끝에 해당하는 좌표값

    // 초당 이동할 월드 거리.
    [SerializeField, Min(0.01f)]
    private float moveSpeed = 6.0f;

    // Shift를 누르는 동안 사용할 초당 이동 거리.
    [SerializeField, Min(0.01f)]
    private float runHold = 10.0f;

    // 현재 몸체 폭 1의 절반만큼 경계 안쪽에 여유를 둔다.
    [SerializeField, Min(0.0f)]
    private float boundaryPadding = 0.5f;

    [SerializeField]
    private Transform bottomBoundary;

    [SerializeField]
    private Transform topBoundary;

    private float minimumPlayerX;
    private float maximumPlayerX;

    private float minimumPlayerY;
    private float maximumPlayerY;

    // 상호작용 키를 눌렀다는 알림.
    // 나중에 별도 상호작용 시스템에서 이 알림을 받아
    // 가까운 건물이나 NPC를 사용하는 처리를 연결한다.
    public event Action InteractionRequested;

    // 각 요청은 Player에 연결된 명령 컨트롤러 하나가 처리한다.
    public event Func<bool> NpcWorkRequested;
    public event Func<bool> NpcWorkContextRequested;
    public event Action NpcRecallRequested;

    public event Action NpcFollowRequested;

    public event Action NpcFollowReleaseRequested;
    public event Action NpcSelectionRequested;

    private const float CommandHoldSeconds = 0.45f;
    private float followHeldSeconds;
    private bool followArmed;
    private bool followHoldHandled;
    private float workHeldSeconds;
    private bool workArmed;
    private bool workHoldHandled;
    private bool spaceCommandMode;

    // NPC 추종 이동에서 사용한다.
    public float FacingDirection { get; private set; } = 1.0f;
    public float HorizontalSpeed { get; private set; }
    public float WalkSpeed => moveSpeed;

    // 마지막으로 입력한 평면 이동 방향.
    // 멈춰도 유지하여 NPC 대열의 뒤쪽 방향을 정한다.
    public Vector2 FacingDirection2D { get; private set; }
        = Vector2.right;

    public float CurrentMoveSpeed { get; private set; }

    private void Start()
    {
        if (playerSpawn == null ||
            leftBoundary == null ||
            rightBoundary == null ||
            bottomBoundary == null ||
            topBoundary == null)
        {
            Debug.LogError(
                "Player Spawn과 좌우·상하 경계를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        if (moveSpeed <= 0.0f ||
            runHold < moveSpeed ||
            boundaryPadding < 0.0f)
        {
            Debug.LogError(
                "이동 속도, 달리기 속도와 경계 여유를 확인해 주세요.",
                this);

            enabled = false;
            return;
        }

        minimumPlayerX =
            leftBoundary.position.x + boundaryPadding;

        maximumPlayerX =
            rightBoundary.position.x - boundaryPadding;

        minimumPlayerY =
            bottomBoundary.position.y + boundaryPadding;

        maximumPlayerY =
            topBoundary.position.y - boundaryPadding;

        if (minimumPlayerX > maximumPlayerX ||
            minimumPlayerY > maximumPlayerY)
        {
            Debug.LogError(
                "상하좌우 경계와 Boundary Padding을 확인해 주세요.",
                this);

            enabled = false;
            return;
        }

        Vector3 startPosition = playerSpawn.position;

        startPosition.x = Mathf.Clamp(
            startPosition.x, minimumPlayerX, maximumPlayerX);

        startPosition.y = Mathf.Clamp(
            startPosition.y, minimumPlayerY, maximumPlayerY);

        transform.position = startPosition;
    }

    private void Update()
    {
        if (GameExitUI.IsWorldPaused) return;
        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0.0f)
        {
            HorizontalSpeed = 0.0f;
            CurrentMoveSpeed = 0.0f;
            ResetCommandInput();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        EventSystem eventSystem = EventSystem.current;

        bool canReadInput =
            !PlayerCombatStats.ActionsBlocked &&
            Application.isFocused &&
            keyboard != null &&
            !CampWindowController.WorldInputBlocked &&
            (eventSystem == null ||
             eventSystem.currentSelectedGameObject == null);

        bool canReadCommands =
            canReadInput && !CampWallPlacementController.IsPlacing;

        Vector2 moveInput = Vector2.zero;

        bool runHeld = false;
        bool interactionPressed = false;
        bool selectionPressed = false;
        bool followTap = false;
        bool followHold = false;
        bool workTap = false;
        bool workHold = false;

        if (canReadInput)
        {
            if (keyboard.aKey.isPressed ||
                keyboard.leftArrowKey.isPressed)
            {
                moveInput.x -= 1.0f;
            }

            if (keyboard.dKey.isPressed ||
                keyboard.rightArrowKey.isPressed)
            {
                moveInput.x += 1.0f;
            }

            if (keyboard.sKey.isPressed ||
                keyboard.downArrowKey.isPressed)
            {
                moveInput.y -= 1.0f;
            }

            if (keyboard.wKey.isPressed ||
                keyboard.upArrowKey.isPressed)
            {
                moveInput.y += 1.0f;
            }

            runHeld =
                keyboard.leftShiftKey.isPressed ||
                keyboard.rightShiftKey.isPressed;

            if (canReadCommands)
            {
                interactionPressed = keyboard.rKey.wasPressedThisFrame;
                selectionPressed = keyboard.tabKey.wasPressedThisFrame;

                ReadCommandKey(
                    keyboard.fKey, deltaTime,
                    ref followHeldSeconds,
                    ref followArmed,
                    ref followHoldHandled,
                    out followTap,
                    out followHold);
            }
            else
            {
                ResetCommandInput();
            }
        }

        else
        {
            ResetCommandInput();
        }

        moveInput = Vector2.ClampMagnitude(moveInput, 1.0f);

        if (moveInput.sqrMagnitude > 0.0f)
        {
            FacingDirection2D = moveInput.normalized;
        }

        float runningFraction = PlayerCombatStats.GetRunningFraction(
            runHeld && moveInput.sqrMagnitude > 0f, deltaTime);
        float speed = Mathf.Lerp(moveSpeed, runHold, runningFraction);
        Vector3 previousPosition = transform.position;
        Vector3 position = previousPosition;

        position.x = Mathf.Clamp(
            position.x + moveInput.x * speed * deltaTime,
            minimumPlayerX,
            maximumPlayerX);

        position.y = Mathf.Clamp(
            position.y + moveInput.y * speed * deltaTime,
            minimumPlayerY,
            maximumPlayerY);

        // 현재 NPC 추종이 사용하는 좌우 정보는 유지한다.
        if (moveInput.x != 0.0f)
        {
            FacingDirection = moveInput.x > 0.0f ? 1.0f : -1.0f;
        }

        CampWallNavigation navigation = CampWallNavigation.Instance;

        if (navigation != null)
        {
            Vector2 blockedPosition = navigation.MoveAlly(
                previousPosition,
                position);

            position.x = blockedPosition.x;
            position.y = blockedPosition.y;
        }

        HorizontalSpeed =
            Mathf.Abs(position.x - previousPosition.x) / deltaTime;

        CurrentMoveSpeed = Vector2.Distance(
            (Vector2)previousPosition,
            (Vector2)position) / deltaTime;

        // 벽·경계에 완전히 막혀 제자리일 때는 달리기 자원을 소비하지 않는다.
        PlayerCombatStats.AdvanceStamina(
            runHeld && CurrentMoveSpeed > 0.001f, deltaTime);

        // NPC 명령의 대상 검색보다 먼저 이동 결과를 반영한다.
        transform.position = position;

        if (canReadCommands)
        {
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                spaceCommandMode =
                    NpcWorkContextRequested?.Invoke() ?? false;
            }

            ReadCommandKey(
                keyboard.spaceKey, deltaTime,
                ref workHeldSeconds,
                ref workArmed,
                ref workHoldHandled,
                out workTap,
                out workHold);

            workTap &= spaceCommandMode;
            workHold &= spaceCommandMode;
        }

        if (selectionPressed)
        {
            NpcSelectionRequested?.Invoke();
        }
        else if (followHold)
        {
            NpcFollowReleaseRequested?.Invoke();
        }

        else if (followTap)
        {
            NpcFollowRequested?.Invoke();
        }

        else if (interactionPressed)
        {
            InteractionRequested?.Invoke();
        }

        else if (workHold)
        {
            NpcRecallRequested?.Invoke();
        }

        else if (workTap)
        {
            NpcWorkRequested?.Invoke();
        }


    }

    private static void ReadCommandKey(
        UnityEngine.InputSystem.Controls.ButtonControl key,
        float deltaTime,
        ref float heldSeconds,
        ref bool armed,
        ref bool holdHandled,
        out bool tapped,
        out bool held)
    {
        tapped = false;
        held = false;
        if (key.wasPressedThisFrame)
        {
            armed = true;
            heldSeconds = 0.0f;
            holdHandled = false;
        }
        if (!armed) { return; }
        if (key.isPressed)
        {
            heldSeconds += deltaTime;
            if (!holdHandled && heldSeconds >= CommandHoldSeconds)
            {
                holdHandled = true;
                held = true;
            }
        }
        if (key.wasReleasedThisFrame)
        {
            tapped = !holdHandled;
            armed = false;
            heldSeconds = 0.0f;
            holdHandled = false;
        }
    }

    private void OnDisable()
    {
        HorizontalSpeed = 0.0f;
        CurrentMoveSpeed = 0.0f;
        ResetCommandInput();
    }

    private void ResetCommandInput()
    {
        followHeldSeconds = workHeldSeconds = 0.0f;
        followArmed = workArmed = false;
        followHoldHandled = workHoldHandled = false;
        spaceCommandMode = false;
    }

    public void RequestConstructionFromMenu()
    {
        if (!Application.isPlaying ||
            !isActiveAndEnabled)
        {
            return;
        }

        InteractionRequested?.Invoke();
    }
}
