using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

public class InventorySlotView : MonoBehaviour
{
    [Header("슬롯 표시")]
    [SerializeField] private Transform previewAnchor;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text weightText;

    public bool IsOccupied { get; private set; }
    public string ItemId { get; private set; }

    private GameObject currentPreview;

    public void SetItem(
        SurvivalItemData item,
        GameObject previewPrefab,
        Vector3 localPosition,
        Vector3 localEulerAngles,
        Vector3 localScale)
    {
        ClearSlot();

        if (item == null)
        {
            return;
        }

        ItemId = item.itemId;
        IsOccupied = true;

        if (itemNameText != null)
        {
            itemNameText.text = item.itemName;
        }

        if (weightText != null)
        {
            weightText.text = $"{item.weight:0.0}kg";
        }

        if (previewPrefab == null || previewAnchor == null)
        {
            return;
        }

        currentPreview = Instantiate(previewPrefab, previewAnchor);
        currentPreview.name = $"{item.itemId}_InventoryPreview";
        currentPreview.transform.localPosition = localPosition;
        currentPreview.transform.localRotation = Quaternion.Euler(localEulerAngles);
        currentPreview.transform.localScale = localScale;

        PreparePreviewObject(currentPreview, previewAnchor.gameObject.layer);
    }

    public void ClearSlot()
    {
        IsOccupied = false;
        ItemId = string.Empty;

        if (currentPreview != null)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }

        if (itemNameText != null)
        {
            itemNameText.text = string.Empty;
        }

        if (weightText != null)
        {
            weightText.text = string.Empty;
        }
    }

    private static void PreparePreviewObject(GameObject target, int layer)
    {
        SetLayerRecursively(target, layer);

        foreach (Rigidbody body in target.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (RuntimeWorldItem runtimeItem in
                 target.GetComponentsInChildren<RuntimeWorldItem>(true))
        {
            runtimeItem.enabled = false;
        }

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;

        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
