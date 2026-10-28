using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CampWindowController : MonoBehaviour
{
    [SerializeField]
    private GameObject constructionPanel;

    [SerializeField]
    private GameObject inventoryPanel;

    [SerializeField]
    private GameObject equipmentPanel;

    public GameObject ConstructionPanel => constructionPanel;
    public GameObject InventoryPanel => inventoryPanel;
    public GameObject EquipmentPanel => equipmentPanel;
    private static bool windowInputBlocked;
    public static bool WorldInputBlocked
    {
        get => windowInputBlocked || GameExitUI.IsMenuOpen || M7CampaignUI.IsModalOpen || ForestGatheringController.BlocksCommands;
        private set => windowInputBlocked = value;
    }

    private readonly List<GameObject> openedPanels = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        WorldInputBlocked = false;
    }

    private void Awake()
    {
        if (constructionPanel == null ||
            inventoryPanel == null ||
            equipmentPanel == null ||
            constructionPanel == inventoryPanel ||
            constructionPanel == equipmentPanel ||
            inventoryPanel == equipmentPanel)
        {
            Debug.LogError(
                "���� �ٸ� �Ǽ����κ��丮����� �г��� ������ �ּ���.",
                this);

            enabled = false;
            return;
        }

        CloseAll();
    }

    private void Start()
    {
        // 조작 화면은 창 자체에서 초기화한다. 다른 HUD의 초기화 성공 여부에 의존하지 않는다.
        NightDefenseTester battle = FindAnyObjectByType<NightDefenseTester>();
        TMPro.TMP_Text label = GetComponentInChildren<TMPro.TMP_Text>(true);
        if (battle == null || label == null) return;
        CampEconomyUI.Attach(constructionPanel, label.font, battle, false, true);
        CampEconomyUI.Attach(inventoryPanel, label.font, battle, false);
        CampEconomyUI.Attach(equipmentPanel, label.font, battle, true);
        CampEconomyUI.CreateMenuBar(this, label.font, battle);
    }

    public void ToggleConstruction() => Toggle(constructionPanel);
    public void ToggleInventory() => Toggle(inventoryPanel);
    public void ToggleEquipment() => Toggle(equipmentPanel);

    private void Update()
    {
        if (GameExitUI.IsMenuOpen || GameExitUI.EscapeHandledThisFrame || ForestGatheringController.BlocksCommands) return;
        if (PlayerCombatStats.ActionsBlocked)
        {
            if (openedPanels.Count > 0) CloseAll();
            return;
        }
        Keyboard keyboard = Keyboard.current;

        if (!Application.isFocused ||
            keyboard == null ||
            Time.deltaTime <= 0.0f)
        {
            return;
        }

        EventSystem events = EventSystem.current;

        if (events != null &&
            events.currentSelectedGameObject != null)
        {
            TMPro.TMP_InputField input =
                events.currentSelectedGameObject
                    .GetComponentInParent<TMPro.TMP_InputField>();

            if (input != null && input.isFocused)
            {
                return;
            }
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            CloseTop();
        }
        else if (keyboard.bKey.wasPressedThisFrame)
        {
            Toggle(constructionPanel);
        }
        else if (keyboard.iKey.wasPressedThisFrame)
        {
            Toggle(inventoryPanel);
        }
        else if (keyboard.eKey.wasPressedThisFrame)
        {
            Toggle(equipmentPanel);
        }
    }

    private void Toggle(GameObject panel)
    {
        if (GameExitUI.IsMenuOpen || GameExitUI.EscapeHandledThisFrame || ForestGatheringController.BlocksCommands) return;
        if (PlayerCombatStats.ActionsBlocked || panel == null) return;
        if (CampWallPlacementController.Instance != null)
            CampWallPlacementController.Instance.CancelPlacement();
        int topIndex = openedPanels.Count - 1;
        if (topIndex >= 0 && openedPanels[topIndex] == panel)
        {
            CloseTop();
            return;
        }

        // 메뉴를 바꾸면 이전 창을 닫고 요청한 창을 맨 앞으로 표시한다.
        CloseAll();
        openedPanels.Add(panel);
        panel.transform.SetAsLastSibling();
        panel.SetActive(true);
        WorldInputBlocked = true;
        ClearSelection();
    }

    public void CloseTop()
    {
        if (GameExitUI.IsMenuOpen || GameExitUI.EscapeHandledThisFrame || ForestGatheringController.BlocksCommands) return;
        if (PlayerCombatStats.ActionsBlocked) return;
        int topIndex = openedPanels.Count - 1;
        if (topIndex < 0) return;

        GameObject panel = openedPanels[topIndex];
        openedPanels.RemoveAt(topIndex);
        panel.SetActive(false);
        WorldInputBlocked = openedPanels.Count > 0;
        ClearSelection();
    }

    private static void ClearSelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void CloseAll()
    {
        if (constructionPanel != null)
            constructionPanel.SetActive(false);

        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        if (equipmentPanel != null)
            equipmentPanel.SetActive(false);

        openedPanels.Clear();
        WorldInputBlocked = false;

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void OnDisable()
    {
        CloseAll();
    }
}
