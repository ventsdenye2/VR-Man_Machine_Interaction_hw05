using UnityEngine;
using Unity.XR.PXR;

/// <summary>
/// Right hand points at a teleport target. A left-hand fist confirms the teleport.
/// Move the avatar root, usually the vBasicController object, and keep the XR rig aligned.
/// </summary>
[DisallowMultipleComponent]
public class PicoHandTeleport : MonoBehaviour
{
    [Header("Teleport Target")]
    [Tooltip("The object to move. Use the vBasicController root.")]
    public Transform avatarRoot;
    public Transform xrOrigin;
    public VRAvatarTeleportBinder teleportBinder;
    public VR_InvectorBridge invectorBridge;

    [Header("Ray")]
    public Transform trackingOrigin;
    public LayerMask teleportMask = ~0;
    public float maxDistance = 20f;
    public bool useVisualHandJoints = true;
    public PXR_Hand rightHandModel;
    public Transform rightIndexTip;
    public Transform rightIndexDistal;
    public bool usePicoAimRay = false;
    public bool fallbackToIndexFinger = true;

    [Header("Fist Confirm")]
    [Tooltip("Distance from palm to curled fingertips. Increase if fist is hard to trigger.")]
    public float fistTipToPalmDistance = 0.09f;
    [Range(1, 4)]
    public int requiredCurledFingers = 4;
    public float cooldown = 0.35f;

    [Header("Landing")]
    public bool preserveInitialGroundOffset = true;
    public float manualGroundOffset;
    public float groundProbeDistance = 3f;
    public bool preserveCurrentAvatarHeight = true;

    [Header("Visuals")]
    public bool createLineRendererIfMissing = true;
    public LineRenderer lineRenderer;
    public Transform targetMarker;
    public bool showInvalidRay = true;
    public float rayWidth = 0.015f;
    public Color validRayColor = new Color(0f, 0.95f, 1f, 1f);
    public Color invalidRayColor = new Color(1f, 0.35f, 0.1f, 0.85f);

    readonly HandJoint[] fistTips =
    {
        HandJoint.JointIndexTip,
        HandJoint.JointMiddleTip,
        HandJoint.JointRingTip,
        HandJoint.JointLittleTip
    };

    HandAimState rightAimState;
    HandJointLocations rightHandJoints;
    HandJointLocations leftHandJoints;
    CharacterController avatarCharacterController;
    CharacterController xrCharacterController;
    RaycastHit currentHit;
    bool hasTarget;
    bool wasLeftFist;
    float nextTeleportTime;
    float avatarGroundOffset;

    void Awake()
    {
        AutoFillReferences();
        EnsureLineRenderer();
        CacheCharacterControllers();
        avatarGroundOffset = preserveInitialGroundOffset ? EstimateGroundOffset() : manualGroundOffset;

        if (targetMarker != null)
            targetMarker.gameObject.SetActive(false);
    }

    void Update()
    {
        hasTarget = false;

        if (TryGetRightHandRay(out var rayOrigin, out var rayDirection))
        {
            hasTarget = Physics.Raycast(rayOrigin, rayDirection, out currentHit, maxDistance, teleportMask, QueryTriggerInteraction.Ignore);
            UpdateVisuals(rayOrigin, rayDirection);
        }
        else
        {
            HideVisuals();
        }

        var leftFist = IsLeftFist();
        if (hasTarget && leftFist && !wasLeftFist && Time.time >= nextTeleportTime)
        {
            TeleportTo(currentHit.point);
            nextTeleportTime = Time.time + cooldown;
        }

        wasLeftFist = leftFist;
    }

    void AutoFillReferences()
    {
        if (teleportBinder == null)
            teleportBinder = FindObjectOfType<VRAvatarTeleportBinder>(true);

        if (invectorBridge == null)
            invectorBridge = FindObjectOfType<VR_InvectorBridge>(true);

        if (avatarRoot == null && teleportBinder != null)
            avatarRoot = teleportBinder.avatarRoot;

        if (avatarRoot == null && invectorBridge != null)
            avatarRoot = invectorBridge.transform;

        if (xrOrigin == null && teleportBinder != null)
            xrOrigin = teleportBinder.xrOrigin;

        if (trackingOrigin == null)
            trackingOrigin = xrOrigin;

        if (trackingOrigin == null && Camera.main != null)
            trackingOrigin = Camera.main.transform.root;

        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        AutoFillRightHandJoints();
    }

    void EnsureLineRenderer()
    {
        if (lineRenderer == null && createLineRendererIfMissing)
        {
            var lineObject = new GameObject("Pico Hand Teleport Ray");
            lineObject.transform.SetParent(transform, false);
            lineRenderer = lineObject.AddComponent<LineRenderer>();
        }

        if (lineRenderer == null)
            return;

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.widthMultiplier = rayWidth;
        lineRenderer.numCapVertices = 8;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;

        if (lineRenderer.sharedMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader != null)
                lineRenderer.material = new Material(shader);
        }

        lineRenderer.enabled = false;
    }

    void CacheCharacterControllers()
    {
        if (avatarRoot != null)
            avatarCharacterController = avatarRoot.GetComponent<CharacterController>();

        if (xrOrigin != null)
            xrCharacterController = xrOrigin.GetComponent<CharacterController>();
    }

    bool TryGetRightHandRay(out Vector3 origin, out Vector3 direction)
    {
        if (useVisualHandJoints && rightIndexTip != null && rightIndexDistal != null)
        {
            origin = rightIndexTip.position;
            direction = (rightIndexTip.position - rightIndexDistal.position).normalized;
            return direction.sqrMagnitude > 0.0001f;
        }

        if (usePicoAimRay && PXR_HandTracking.GetAimState(HandType.HandRight, ref rightAimState))
        {
            var aimReady = HasFlag(rightAimState.aimStatus, HandAimStatus.AimComputed) &&
                           HasFlag(rightAimState.aimStatus, HandAimStatus.AimRayValid);

            if (aimReady)
            {
                origin = ToWorldPoint(rightAimState.aimRayPose.Position.ToVector3());
                direction = ToWorldDirection(rightAimState.aimRayPose.Orientation.ToQuat() * Vector3.forward);
                return direction.sqrMagnitude > 0.0001f;
            }
        }

        if (fallbackToIndexFinger && PXR_HandTracking.GetJointLocations(HandType.HandRight, ref rightHandJoints))
        {
            if (TryGetJointLocal(rightHandJoints, HandJoint.JointIndexTip, out var tip) &&
                TryGetJointLocal(rightHandJoints, HandJoint.JointIndexDistal, out var distal))
            {
                origin = ToWorldPoint(tip);
                direction = ToWorldDirection((tip - distal).normalized);
                return direction.sqrMagnitude > 0.0001f;
            }
        }

        origin = Vector3.zero;
        direction = Vector3.forward;
        return false;
    }

    void AutoFillRightHandJoints()
    {
        if (rightHandModel == null)
        {
            var hands = FindObjectsOfType<PXR_Hand>(true);
            foreach (var hand in hands)
            {
                if (hand != null && hand.handType == HandType.HandRight)
                {
                    rightHandModel = hand;
                    break;
                }
            }
        }

        if (rightHandModel == null || rightHandModel.handJoints == null)
            return;

        if (rightIndexTip == null)
            rightIndexTip = GetHandJointTransform(rightHandModel, HandJoint.JointIndexTip);

        if (rightIndexDistal == null)
            rightIndexDistal = GetHandJointTransform(rightHandModel, HandJoint.JointIndexDistal);
    }

    bool IsLeftFist()
    {
        if (!PXR_HandTracking.GetJointLocations(HandType.HandLeft, ref leftHandJoints))
            return false;

        if (!TryGetJointLocal(leftHandJoints, HandJoint.JointPalm, out var palm))
            return false;

        var curled = 0;
        foreach (var tipJoint in fistTips)
        {
            if (!TryGetJointLocal(leftHandJoints, tipJoint, out var tip))
                continue;

            if (Vector3.Distance(palm, tip) <= fistTipToPalmDistance)
                curled++;
        }

        return curled >= requiredCurledFingers;
    }

    void TeleportTo(Vector3 groundPoint)
    {
        if (avatarRoot == null)
            return;

        if (invectorBridge != null)
            invectorBridge.StopMovement();

        var targetY = preserveCurrentAvatarHeight ? avatarRoot.position.y : groundPoint.y + avatarGroundOffset;
        var target = new Vector3(groundPoint.x, targetY, groundPoint.z);
        SetTransformPosition(avatarRoot, avatarCharacterController, target);

        if (teleportBinder != null)
        {
            teleportBinder.MoveXROriginToAvatar();
        }
        else if (xrOrigin != null)
        {
            SetTransformPosition(xrOrigin, xrCharacterController, target);
        }
    }

    void SetTransformPosition(Transform target, CharacterController controller, Vector3 position)
    {
        if (target == null)
            return;

        if (controller != null && controller.enabled)
        {
            controller.enabled = false;
            target.position = position;
            controller.enabled = true;
        }
        else
        {
            target.position = position;
        }
    }

    float EstimateGroundOffset()
    {
        if (avatarRoot == null)
            return manualGroundOffset;

        var rayOrigin = avatarRoot.position + Vector3.up * 0.2f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, groundProbeDistance, teleportMask, QueryTriggerInteraction.Ignore))
            return avatarRoot.position.y - hit.point.y;

        return manualGroundOffset;
    }

    void UpdateVisuals(Vector3 origin, Vector3 direction)
    {
        var shouldShowLine = hasTarget || showInvalidRay;

        if (lineRenderer != null)
        {
            lineRenderer.enabled = shouldShowLine;
            if (shouldShowLine)
            {
                var color = hasTarget ? validRayColor : invalidRayColor;
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
                lineRenderer.widthMultiplier = rayWidth;
                lineRenderer.positionCount = 2;
                lineRenderer.SetPosition(0, origin);
                lineRenderer.SetPosition(1, hasTarget ? currentHit.point : origin + direction.normalized * maxDistance);
            }
        }

        if (targetMarker != null)
        {
            targetMarker.gameObject.SetActive(hasTarget);
            if (hasTarget)
                targetMarker.position = currentHit.point;
        }
    }

    void HideVisuals()
    {
        if (lineRenderer != null)
            lineRenderer.enabled = false;

        if (targetMarker != null)
            targetMarker.gameObject.SetActive(false);
    }

    bool TryGetJointLocal(HandJointLocations joints, HandJoint joint, out Vector3 localPosition)
    {
        var index = (int)joint;
        if (joints.jointLocations == null || index < 0 || index >= joints.jointLocations.Length)
        {
            localPosition = Vector3.zero;
            return false;
        }

        var jointLocation = joints.jointLocations[index];
        if (!HasFlag(jointLocation.locationStatus, HandLocationStatus.PositionValid))
        {
            localPosition = Vector3.zero;
            return false;
        }

        localPosition = jointLocation.pose.Position.ToVector3();
        return true;
    }

    Vector3 ToWorldPoint(Vector3 localPoint)
    {
        return trackingOrigin != null ? trackingOrigin.TransformPoint(localPoint) : localPoint;
    }

    Vector3 ToWorldDirection(Vector3 localDirection)
    {
        return trackingOrigin != null ? trackingOrigin.TransformDirection(localDirection).normalized : localDirection.normalized;
    }

    static bool HasFlag(HandAimStatus value, HandAimStatus flag)
    {
        return (value & flag) == flag;
    }

    static bool HasFlag(HandLocationStatus value, HandLocationStatus flag)
    {
        return (value & flag) == flag;
    }

    static Transform GetHandJointTransform(PXR_Hand hand, HandJoint joint)
    {
        var index = (int)joint;
        if (hand.handJoints == null || index < 0 || index >= hand.handJoints.Count)
            return null;

        return hand.handJoints[index];
    }
}
