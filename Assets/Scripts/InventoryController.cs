using System.Collections.Generic;
using UnityEngine;

public class InventoryController : MonoBehaviour
{
    [Header("인벤토리 UI")]
    [SerializeField] private GameObject inventoryRoot;
    [SerializeField] private List<InventorySlotView> slots =
        new List<InventorySlotView>();

    public bool IsOpen => inventoryRoot != null && inventoryRoot.activeSelf;

    public bool HasEmptySlot
    {
        get
        {
            foreach (InventorySlotView slot in slots)
            {
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
        CloseInventory();
    }

    public void ToggleInventory()
    {
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
        if (inventoryRoot != null)
        {
            inventoryRoot.SetActive(true);
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
        foreach (InventorySlotView slot in slots)
        {
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
}
