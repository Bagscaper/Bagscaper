using System;
using UnityEngine;

public sealed class RuntimeWorldItem : MonoBehaviour
{
    public string ItemId { get; private set; }
    public string InstanceId { get; private set; }
    public bool IsHeld { get; private set; }

    private Rigidbody body;
    private Transform originalParent;
    private Vector3 holdLocalPosition;
    private Vector3 holdLocalEulerAngles;

    public void Initialize(
        string itemId,
        Rigidbody targetBody,
        Vector3 newHoldLocalPosition,
        Vector3 newHoldLocalEulerAngles)
    {
        ItemId = itemId;
        InstanceId = Guid.NewGuid().ToString();
        body = targetBody;
        originalParent = transform.parent;
        holdLocalPosition = newHoldLocalPosition;
        holdLocalEulerAngles = newHoldLocalEulerAngles;
    }

    public void Grab(Transform targetHoldPoint)
    {
        if (IsHeld || targetHoldPoint == null)
        {
            return;
        }

        originalParent = transform.parent;
        IsHeld = true;

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }

        transform.SetParent(targetHoldPoint, false);
        transform.localPosition = holdLocalPosition;
        transform.localRotation = Quaternion.Euler(holdLocalEulerAngles);
    }

    public void Drop()
    {
        if (!IsHeld)
        {
            return;
        }

        transform.SetParent(originalParent, true);
        IsHeld = false;

        if (body != null)
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
