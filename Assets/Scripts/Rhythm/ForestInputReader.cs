using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 숲 플레이에 사용할 입력을 수집한다.
// 곡 시간 변환과 노트 판정은 담당하지 않는다.
public sealed class ForestInputReader : IDisposable
{
    private readonly InputAction leftAction;
    private readonly InputAction rightAction;
    private readonly InputAction syncAction;

    private bool isEnabled;

    private EventSystem pointerEventSystem;
    private PointerEventData pointerData;

    private readonly List<RaycastResult> pointerHits =
        new List<RaycastResult>();

    // 실제 입력 발생 시각, 입력 방향, 누름 여부.
    // 이 시각은 아직 곡 기준 시각으로 변환되지 않은 값이다.
    public event Action<double, RhythmInputSide, bool> InputReceived;

    public ForestInputReader()
    {
        leftAction = new InputAction(
            "ForestLeft",
            InputActionType.Button,
            "<Mouse>/leftButton");

        rightAction = new InputAction(
            "ForestRight",
            InputActionType.Button,
            "<Mouse>/rightButton");

        syncAction = new InputAction(
            "ForestSync",
            InputActionType.Button,
            "<Keyboard>/space");
    }

    public void Enable()
    {
        if (isEnabled)
        {
            return;
        }

        leftAction.performed += OnLeftPressed;
        leftAction.canceled += OnLeftReleased;

        rightAction.performed += OnRightPressed;
        rightAction.canceled += OnRightReleased;

        syncAction.performed += OnSyncPressed;

        isEnabled = true;

        syncAction.Enable();
        leftAction.Enable();
        rightAction.Enable();
    }

    public void Disable()
    {
        if (!isEnabled)
        {
            return;
        }

        isEnabled = false;

        // Disable도 canceled를 발생시킬 수 있으므로
        // 먼저 콜백을 해제한다.
        leftAction.performed -= OnLeftPressed;
        leftAction.canceled -= OnLeftReleased;

        rightAction.performed -= OnRightPressed;
        rightAction.canceled -= OnRightReleased;

        syncAction.performed -= OnSyncPressed;

        leftAction.Disable();
        rightAction.Disable();
        syncAction.Disable();
    }

    public void Dispose()
    {
        Disable();

        leftAction.Dispose();
        rightAction.Dispose();
        syncAction.Dispose();
    }

    public bool IsPressed(RhythmInputSide side)
    {
        if (!isEnabled)
        {
            return false;
        }

        switch (side)
        {
            case RhythmInputSide.Left:
                return leftAction.IsPressed();

            case RhythmInputSide.Right:
                return rightAction.IsPressed();

            default:
                return false;
        }
    }

    private void OnLeftPressed(InputAction.CallbackContext context)
    {
        ForwardMouseInput(context, RhythmInputSide.Left, true);
    }

    private void OnLeftReleased(InputAction.CallbackContext context)
    {
        ForwardMouseInput(context, RhythmInputSide.Left, false);
    }

    private void OnRightPressed(InputAction.CallbackContext context)
    {
        ForwardMouseInput(context, RhythmInputSide.Right, true);
    }

    private void OnRightReleased(InputAction.CallbackContext context)
    {
        ForwardMouseInput(context, RhythmInputSide.Right, false);
    }

    private void ForwardMouseInput(
        InputAction.CallbackContext context,
        RhythmInputSide side,
        bool isPress)
    {
        Mouse mouse = context.control.device as Mouse;

        if (mouse == null)
        {
            return;
        }

        // UI 위에서 누른 입력은 벌목에 사용하지 않는다.
        //
        // 놓기 입력은 UI 위에서도 전달한다.
        // Hold 도중 커서가 버튼 위로 이동해도 해제를 인식해야 한다.
        if (isPress &&
            (IsEditingText() ||
             IsPointerOverControl(mouse.position.ReadValue())))
        {
            return;
        }

        InputReceived?.Invoke(context.time, side, isPress);
    }

    private void OnSyncPressed(InputAction.CallbackContext context)
    {
        EventSystem eventSystem = EventSystem.current;

        // 선택된 UI의 Space 실행과 Sync가 겹치지 않도록 한다.
        if (eventSystem != null &&
            eventSystem.currentSelectedGameObject != null)
        {
            return;
        }

        InputReceived?.Invoke(
            context.time,
            RhythmInputSide.Both,
            true);
    }

    private static bool IsEditingText()
    {
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
        {
            return false;
        }

        GameObject selected =
            eventSystem.currentSelectedGameObject;

        if (selected == null)
        {
            return false;
        }

        TMP_InputField inputField =
            selected.GetComponentInParent<TMP_InputField>();

        return inputField != null && inputField.isFocused;
    }

    private bool IsPointerOverControl(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
        {
            return false;
        }

        if (pointerData == null ||
            pointerEventSystem != eventSystem)
        {
            pointerEventSystem = eventSystem;
            pointerData = new PointerEventData(eventSystem);
        }

        pointerData.Reset();
        pointerData.position = screenPosition;

        pointerHits.Clear();
        eventSystem.RaycastAll(pointerData, pointerHits);

        foreach (RaycastResult hit in pointerHits)
        {
            if (hit.gameObject == null)
            {
                continue;
            }

            Selectable control =
                hit.gameObject.GetComponentInParent<Selectable>();

            if (control != null)
            {
                return true;
            }
        }

        return false;
    }
}