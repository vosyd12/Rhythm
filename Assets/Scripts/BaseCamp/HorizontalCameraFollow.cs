using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 이 스크립트가 붙는 오브젝트에는 Camera가 필요하다.
[RequireComponent(typeof(Camera))]
public class HorizontalCameraFollow : MonoBehaviour
{
    // 따라갈 플레이어의 기준 오브젝트.
    [SerializeField]
    private Transform playerTarget;

    [SerializeField]
    private Transform leftBoundary;

    [SerializeField]
    private Transform rightBoundary;

    [SerializeField]
    private Transform bottomBoundary;

    [SerializeField]
    private Transform topBoundary;

    [Header("카메라 확대·축소")]

    [SerializeField, Min(0.1f)]
    private float minimumZoomSize = 4.0f;

    [SerializeField, Min(0.1f)]
    private float maximumZoomSize = 12.0f;

    [SerializeField, Min(0.01f)]
    private float zoomStep = 0.75f;

    private Camera followCamera;

    // 현재 맵의 경계는 실행 중 고정되어 있으므로
    // 시작할 때 월드 좌표를 읽어 보관한다.
    private float mapLeftX;
    private float mapRightX;

    private float mapBottomY;
    private float mapTopY;

    private float fixedZ;
    private Vector2 spectatorPosition;
    private bool wasSpectating;

    private void Start()
    {
        followCamera = GetComponent<Camera>();

        if (playerTarget == null ||
            leftBoundary == null ||
            rightBoundary == null ||
            bottomBoundary == null ||
            topBoundary == null)
        {
            Debug.LogError(
                "Player Target과 좌우·상하 경계를 연결해 주세요.",
                this);

            enabled = false;
            return;
        }

        if (!followCamera.orthographic)
        {
            Debug.LogError(
                "카메라의 Projection을 Orthographic으로 설정해 주세요.",
                this);

            enabled = false;
            return;
        }

        mapLeftX = leftBoundary.position.x;
        mapRightX = rightBoundary.position.x;
        mapBottomY = bottomBoundary.position.y;
        mapTopY = topBoundary.position.y;

        if (mapLeftX >= mapRightX ||
            mapBottomY >= mapTopY)
        {
            Debug.LogError(
                "좌우·상하 경계의 순서를 확인해 주세요.",
                this);

            enabled = false;
            return;
        }

        if (!(minimumZoomSize > 0.0f) ||
            !(maximumZoomSize >= minimumZoomSize) ||
            !(zoomStep > 0.0f) ||
            float.IsInfinity(minimumZoomSize) ||
            float.IsInfinity(maximumZoomSize) ||
            float.IsInfinity(zoomStep))
        {
            Debug.LogError(
                "줌의 최소·최대 크기와 변경량을 확인해 주세요.",
                this);

            enabled = false;
            return;
        }

        followCamera.orthographicSize = Mathf.Clamp(
            followCamera.orthographicSize,
            minimumZoomSize,
            maximumZoomSize);

        fixedZ = transform.position.z;
    }

    private void LateUpdate()
    {
        HandleZoom();
        bool spectating = PlayerCombatStats.ActionsBlocked;
        if (spectating && !wasSpectating) spectatorPosition = transform.position;
        if (spectating && Application.isFocused && Keyboard.current != null)
        {
            Keyboard keys = Keyboard.current;
            Vector2 input = new Vector2(
                (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1f : 0f) -
                (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1f : 0f),
                (keys.wKey.isPressed || keys.upArrowKey.isPressed ? 1f : 0f) -
                (keys.sKey.isPressed || keys.downArrowKey.isPressed ? 1f : 0f));
            spectatorPosition += Vector2.ClampMagnitude(input, 1f) * 10f * Time.deltaTime;
        }
        wasSpectating = spectating;
        Vector2 focus = spectating ? spectatorPosition : (Vector2)playerTarget.position;

        float halfViewHeight = followCamera.orthographicSize;
        float halfViewWidth = halfViewHeight * followCamera.aspect;

        float minimumCameraX = mapLeftX + halfViewWidth;
        float maximumCameraX = mapRightX - halfViewWidth;

        float minimumCameraY = mapBottomY + halfViewHeight;
        float maximumCameraY = mapTopY - halfViewHeight;

        float cameraX;
        float cameraY;

        if (minimumCameraX <= maximumCameraX)
        {
            cameraX = Mathf.Clamp(
                focus.x,
                minimumCameraX,
                maximumCameraX);
        }

        else
        {
            cameraX = (mapLeftX + mapRightX) * 0.5f;
        }

        if (minimumCameraY <= maximumCameraY)
        {
            cameraY = Mathf.Clamp(
                focus.y,
                minimumCameraY,
                maximumCameraY);
        }

        else
        {
            cameraY = (mapBottomY + mapTopY) * 0.5f;
        }

        if (spectating) spectatorPosition = new Vector2(cameraX, cameraY);
        transform.position = new Vector3(
            cameraX,
            cameraY,
            fixedZ);
    }

    private void HandleZoom()
    {
        if (Time.deltaTime <= 0.0f ||
            !Application.isFocused)
        {
            return;
        }

        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return;
        }

        // 게임 화면 안에서 휠을 돌렸을 때만 처리한다.
        if (!followCamera.pixelRect.Contains(
                mouse.position.ReadValue()))
        {
            return;
        }

        // UI를 스크롤할 때 카메라도 함께 움직이지 않게 한다.
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem != null &&
            eventSystem.IsPointerOverGameObject())
        {
            return;
        }

        float scroll = mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) < 0.001f)
        {
            return;
        }

        // 휠 위쪽: 확대 / 휠 아래쪽: 축소
        float direction = Mathf.Sign(scroll);

        followCamera.orthographicSize = Mathf.Clamp(
            followCamera.orthographicSize - direction * zoomStep,
            minimumZoomSize,
            maximumZoomSize);
    }
}