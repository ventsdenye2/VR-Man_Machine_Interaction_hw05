using System;
using UnityEngine;

/// <summary>
/// Keeps the VR rig and the Invector avatar together for a first-person teleport setup.
/// XR Origin owns the headset/camera. The avatar is used as the visible robot body and
/// is snapped to the XR Origin after teleporting.
/// </summary>
[DisallowMultipleComponent]
public class VRAvatarTeleportBinder : MonoBehaviour
{
    public enum StartAlignment
    {
        None,
        MoveXROriginToAvatar,
        MoveAvatarToXROrigin
    }

    [Header("VR Rig")]
    public Transform xrOrigin;
    public Transform xrCamera;

    [Header("Avatar")]
    public Transform avatarRoot;
    public VR_InvectorBridge invectorBridge;

    [Header("Startup")]
    public StartAlignment startAlignment = StartAlignment.MoveXROriginToAvatar;
    public bool disableKeyboardDebug = true;
    public bool bindBridgeCameraReference = true;

    [Header("Teleport Sync")]
    public bool snapAvatarToXROrigin = true;
    public bool matchAvatarYawToHeadset = true;
    public bool keepAvatarUpright = true;
    public bool preserveAvatarHeightDuringSync = true;
    public bool preserveRigHeightWhenAligningToAvatar = true;
    public float snapPositionThreshold = 0.05f;
    public float avatarYawOffset;

    [Header("First Person Body")]
    public bool hideHeadOnStart = true;
    public Renderer[] headRenderers;
    public GameObject[] headObjects;

    CharacterController avatarCharacterController;
    Vector3 lastRigPosition;

    void Awake()
    {
        AutoFillReferences();
        ApplyCameraBinding();
        ApplyHeadVisibility();
        ApplyStartAlignment();
        lastRigPosition = xrOrigin != null ? xrOrigin.position : Vector3.zero;
    }

    void LateUpdate()
    {
        if (!snapAvatarToXROrigin || xrOrigin == null || avatarRoot == null)
            return;

        var delta = xrOrigin.position - lastRigPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude >= snapPositionThreshold * snapPositionThreshold)
            SnapAvatarToXROrigin();

        if (matchAvatarYawToHeadset)
            MatchAvatarYaw();
        else if (keepAvatarUpright)
            KeepAvatarUpright();

        lastRigPosition = xrOrigin.position;
    }

    public void SnapAvatarToXROrigin()
    {
        if (xrOrigin == null || avatarRoot == null)
            return;

        SetAvatarPose(GetHorizontalSyncedPosition(avatarRoot.position, xrOrigin.position, preserveAvatarHeightDuringSync), avatarRoot.rotation);
    }

    public void MoveXROriginToAvatar()
    {
        if (xrOrigin == null || avatarRoot == null)
            return;

        xrOrigin.position = GetHorizontalSyncedPosition(xrOrigin.position, avatarRoot.position, preserveRigHeightWhenAligningToAvatar);
        lastRigPosition = xrOrigin.position;
    }

    public void MoveAvatarToXROrigin()
    {
        SnapAvatarToXROrigin();
        lastRigPosition = xrOrigin != null ? xrOrigin.position : Vector3.zero;
    }

    void AutoFillReferences()
    {
        if (xrCamera == null && Camera.main != null)
            xrCamera = Camera.main.transform;

        if (xrOrigin == null)
            xrOrigin = FindNamedTransform("XR Origin (XR Rig)") ?? FindNamedTransform("XR Origin");

        if (xrOrigin == null && xrCamera != null)
            xrOrigin = FindLikelyRigRoot(xrCamera);

        if (invectorBridge == null)
            invectorBridge = FindObjectOfType<VR_InvectorBridge>(true);

        if (avatarRoot == null && invectorBridge != null)
            avatarRoot = invectorBridge.transform;

        if (avatarRoot != null)
            avatarCharacterController = avatarRoot.GetComponent<CharacterController>();
    }

    void ApplyCameraBinding()
    {
        if (invectorBridge == null)
            return;

        if (disableKeyboardDebug)
        {
            invectorBridge.useKeyboardDebug = false;
            invectorBridge.StopMovement();
        }

        if (bindBridgeCameraReference && xrCamera != null)
            invectorBridge.referenceCamera = xrCamera;
    }

    void ApplyHeadVisibility()
    {
        if (!hideHeadOnStart)
            return;

        if (headRenderers != null)
        {
            foreach (var renderer in headRenderers)
            {
                if (renderer != null)
                    renderer.enabled = false;
            }
        }

        if (headObjects != null)
        {
            foreach (var headObject in headObjects)
            {
                if (headObject != null)
                    headObject.SetActive(false);
            }
        }
    }

    void ApplyStartAlignment()
    {
        switch (startAlignment)
        {
            case StartAlignment.MoveXROriginToAvatar:
                MoveXROriginToAvatar();
                break;
            case StartAlignment.MoveAvatarToXROrigin:
                MoveAvatarToXROrigin();
                break;
            case StartAlignment.None:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    void MatchAvatarYaw()
    {
        if (avatarRoot == null)
            return;

        var reference = xrCamera != null ? xrCamera : xrOrigin;
        if (reference == null)
            return;

        var euler = avatarRoot.eulerAngles;
        euler.y = reference.eulerAngles.y + avatarYawOffset;
        SetAvatarPose(avatarRoot.position, Quaternion.Euler(euler));
    }

    void KeepAvatarUpright()
    {
        if (avatarRoot == null)
            return;

        var euler = avatarRoot.eulerAngles;
        if (Mathf.Abs(Mathf.DeltaAngle(euler.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(euler.z, 0f)) < 0.01f)
            return;

        SetAvatarPose(avatarRoot.position, Quaternion.Euler(0f, euler.y, 0f));
    }

    void SetAvatarPose(Vector3 position, Quaternion rotation)
    {
        if (keepAvatarUpright)
        {
            var euler = rotation.eulerAngles;
            rotation = Quaternion.Euler(0f, euler.y, 0f);
        }

        if (avatarCharacterController != null && avatarCharacterController.enabled)
        {
            avatarCharacterController.enabled = false;
            avatarRoot.SetPositionAndRotation(position, rotation);
            avatarCharacterController.enabled = true;
        }
        else
        {
            avatarRoot.SetPositionAndRotation(position, rotation);
        }
    }

    Vector3 GetHorizontalSyncedPosition(Vector3 currentPosition, Vector3 sourcePosition, bool preserveHeight = true)
    {
        return new Vector3(
            sourcePosition.x,
            preserveHeight ? currentPosition.y : sourcePosition.y,
            sourcePosition.z);
    }

    static Transform FindNamedTransform(string objectName)
    {
        var go = GameObject.Find(objectName);
        return go != null ? go.transform : null;
    }

    static Transform FindLikelyRigRoot(Transform cameraTransform)
    {
        var current = cameraTransform;
        while (current != null)
        {
            if (current.name.IndexOf("XR Origin", StringComparison.OrdinalIgnoreCase) >= 0)
                return current;

            current = current.parent;
        }

        return cameraTransform.root;
    }
}
