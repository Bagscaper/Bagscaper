using System.Collections.Generic;
using UnityEngine;

public class InventoryController : MonoBehaviour
{
    public const int MaxItemCount = 16;

    [Header("인벤토리 UI")]
    [SerializeField] private GameObject inventoryRoot;
    [SerializeField] private List<InventorySlotView> slots =
        new List<InventorySlotView>();

    public bool IsOpen =>
        inventoryRoot != null && inventoryRoot.activeInHierarchy;
    public int Capacity => MaxItemCount;

    public int CurrentItemCount
    {
        get
        {
            int count = 0;
            int usableSlotCount = Mathf.Min(slots.Count, MaxItemCount);

            for (int i = 0; i < usableSlotCount; i++)
            {
                InventorySlotView slot = slots[i];
                if (slot != null && slot.IsOccupied)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool HasEmptySlot
    {
        get
        {
            if (CurrentItemCount >= MaxItemCount)
            {
                return false;
            }

            int usableSlotCount = Mathf.Min(slots.Count, MaxItemCount);

            for (int i = 0; i < usableSlotCount; i++)
            {
                InventorySlotView slot = slots[i];
                if (slot != null && !slot.IsOccupied)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void Awake()
    {
        ValidateSlotSetup();

        if (inventoryRoot == null)
        {
            Debug.LogError(
                "[InventoryController] Inventory Root가 연결되지 않았습니다.",
                this
            );
            return;
        }

        CloseInventory();
    }

    public void ToggleInventory()
    {
        if (inventoryRoot == null)
        {
            Debug.LogError(
                "[InventoryController] Inventory Root가 연결되지 않아 열 수 없습니다.",
                this
            );
            return;
        }

        if (IsOpen)
        {
            CloseInventory();
        }
        else
        {
            OpenInventory();
        }
    }

    public void OpenInventory()
    {
        if (inventoryRoot == null)
        {
            return;
        }

        inventoryRoot.SetActive(true);

        if (!inventoryRoot.activeInHierarchy)
        {
            Debug.LogError(
                "[InventoryController] Inventory Root의 상위 오브젝트가 비활성화되어 " +
                "인벤토리가 보이지 않습니다. Inventory Root를 InGameRoot 아래에 두고 " +
                "상위 오브젝트가 활성화되어 있는지 확인하세요.",
                inventoryRoot
            );
        }
    }

    public void CloseInventory()
    {
        if (inventoryRoot != null)
        {
            inventoryRoot.SetActive(false);
        }
    }

    public bool AddItem(
        SurvivalItemData item,
        GameObject previewPrefab,
        Vector3 previewLocalPosition,
        Vector3 previewLocalEulerAngles,
        Vector3 previewLocalScale)
    {
        if (item == null)
        {
            return false;
        }

        if (CurrentItemCount >= MaxItemCount)
        {
            Debug.LogWarning(
                $"[InventoryController] 아이템은 최대 {MaxItemCount}개까지만 넣을 수 있습니다."
            );
            return false;
        }

        int usableSlotCount = Mathf.Min(slots.Count, MaxItemCount);

        for (int i = 0; i < usableSlotCount; i++)
        {
            InventorySlotView slot = slots[i];

            if (slot == null || slot.IsOccupied)
            {
                continue;
            }

            slot.SetItem(
                item,
                previewPrefab,
                previewLocalPosition,
                previewLocalEulerAngles,
                previewLocalScale
            );

            return true;
        }

        Debug.LogWarning(
            $"[InventoryController] 사용 가능한 슬롯이 없습니다. 슬롯 {MaxItemCount}개가 연결됐는지 확인하세요."
        );
        return false;
    }

    public void ClearInventory()
    {
        foreach (InventorySlotView slot in slots)
        {
            if (slot != null)
            {
                slot.ClearSlot();
            }
        }

        CloseInventory();
    }

    private void ValidateSlotSetup()
    {
        if (slots.Count < MaxItemCount)
        {
            Debug.LogWarning(
                $"[InventoryController] 슬롯이 {slots.Count}개만 연결되어 있습니다. " +
                $"최대 수용량을 사용하려면 {MaxItemCount}개를 연결하세요."
            );
        }
        else if (slots.Count > MaxItemCount)
        {
            Debug.LogWarning(
                $"[InventoryController] 슬롯이 {slots.Count}개 연결되어 있지만 " +
                $"앞의 {MaxItemCount}개만 사용합니다."
            );
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (slots != null && slots.Count > MaxItemCount)
        {
            Debug.LogWarning(
                $"[InventoryController] 아이템 최대 개수는 {MaxItemCount}개입니다. " +
                $"Element 0~{MaxItemCount - 1}만 사용됩니다.",
                this
            );
        }
    }
#endif
}
