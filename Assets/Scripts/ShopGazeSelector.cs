using UnityEngine;
using Unity.XR.PXR;

[DisallowMultipleComponent]
public class ShopGazeSelector : MonoBehaviour
{
    public Transform gazeOrigin;
    public ShoppingCart cart;
    public LayerMask productMask = ~0;
    public float maxDistance = 8f;
    public bool useRightIndexPinch = true;
    public KeyCode debugSelectKey = KeyCode.Return;

    [Header("Reticle")]
    public Transform reticle;
    public bool showReticle = true;

    [Header("Debug Visuals")]
    public bool showGazeRay = true;
    public float gazeRayWidth = 0.01f;
    public float emptyRayLength = 3f;
    public bool logTargetChanges = true;

    [Header("Hand Input")]
    public bool useJointPinchFallback = true;
    public float jointPinchDistance = 0.045f;

    [Header("Clap Checkout")]
    public bool useClapCheckout = true;
    public HandJoint clapJoint = HandJoint.JointPalm;
    public float clapDistance = 0.12f;
    public float clapHoldSeconds = 0.5f;

    HandAimState rightAimState;
    HandJointLocations rightJointLocations;
    HandJointLocations leftJointLocations;
    ShopProduct currentProduct;
    LineRenderer gazeLine;
    LineRenderer boundsLine;
    bool wasSelecting;
    bool hasRightAimState;
    bool hasRightJointLocations;
    bool hasLeftJointLocations;
    float clapHeldTime;
    bool clapCheckoutTriggered;
    readonly RaycastHit[] gazeHits = new RaycastHit[32];

    void Awake()
    {
        AutoFillReferences();
        EnsureDebugVisuals();
        if (reticle != null)
            reticle.gameObject.SetActive(false);
    }

    void Update()
    {
        UpdateGaze();
        SampleHandState();
        UpdateSelection();
        UpdateClapCheckout();
    }

    void AutoFillReferences()
    {
        if (gazeOrigin == null && Camera.main != null)
            gazeOrigin = Camera.main.transform;

        if (cart == null)
            cart = FindObjectOfType<ShoppingCart>(true);
    }

    void UpdateGaze()
    {
        var previous = currentProduct;
        var hit = default(RaycastHit);
        currentProduct = null;

        if (gazeOrigin != null && TryFindGazedProduct(out var product, out hit))
        {
            currentProduct = product;

            if (showReticle && reticle != null)
            {
                reticle.gameObject.SetActive(currentProduct != null);
                reticle.position = hit.point;
                reticle.rotation = Quaternion.LookRotation(hit.normal);
            }
        }
        else if (reticle != null)
        {
            reticle.gameObject.SetActive(false);
        }

        if (previous != currentProduct)
        {
            if (previous != null)
                previous.SetHighlighted(false);

            if (currentProduct != null)
                currentProduct.SetHighlighted(true);

            if (logTargetChanges)
                Debug.Log(currentProduct != null ? $"Gazing product: {currentProduct.GetCartLabel()}" : "Gazing product: none", this);
        }

        UpdateDebugVisuals(currentProduct != null, hit);
    }

    void UpdateSelection()
    {
        var selecting = IsSelectPressed();
        if (currentProduct != null && selecting && !wasSelecting)
        {
            if (cart != null)
            {
                cart.AddProduct(currentProduct);
                Debug.Log($"Purchased product: {currentProduct.GetCartLabel()}", currentProduct);
            }
            else
            {
                Debug.LogWarning("Cannot purchase product because ShoppingCart is missing.", this);
            }
        }

        wasSelecting = selecting;
    }

    void SampleHandState()
    {
        hasRightAimState = PXR_HandTracking.GetAimState(HandType.HandRight, ref rightAimState);
        hasRightJointLocations = (useJointPinchFallback || useClapCheckout) &&
            PXR_HandTracking.GetJointLocations(HandType.HandRight, ref rightJointLocations);
        hasLeftJointLocations = useClapCheckout &&
            PXR_HandTracking.GetJointLocations(HandType.HandLeft, ref leftJointLocations);
    }

    void UpdateClapCheckout()
    {
        if (!useClapCheckout || cart == null)
        {
            clapHeldTime = 0f;
            clapCheckoutTriggered = false;
            return;
        }

        if (!IsClapping())
        {
            clapHeldTime = 0f;
            clapCheckoutTriggered = false;
            return;
        }

        clapHeldTime += Time.deltaTime;

        if (!clapCheckoutTriggered && clapHeldTime >= clapHoldSeconds)
        {
            if (cart.Checkout())
                Debug.Log("Cart checkout triggered by clap gesture.", cart);

            clapCheckoutTriggered = true;
        }
    }

    bool IsSelectPressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(debugSelectKey))
            return true;
#endif

        if (!useRightIndexPinch)
            return false;

        if (!hasRightAimState && !hasRightJointLocations)
            return false;

        if (useClapCheckout && IsClapping())
            return false;

        return IsRightFingerPinching(HandAimStatus.AimIndexPinching, HandJoint.JointIndexTip) ||
            (hasRightAimState && HasFlag(rightAimState.aimStatus, HandAimStatus.AimRayTouched));
    }

    bool IsClapping()
    {
        return TryGetJointPosition(leftJointLocations, hasLeftJointLocations, clapJoint, out var leftPosition) &&
            TryGetJointPosition(rightJointLocations, hasRightJointLocations, clapJoint, out var rightPosition) &&
            Vector3.Distance(leftPosition, rightPosition) <= clapDistance;
    }

    bool IsRightFingerPinching(HandAimStatus statusFlag, HandJoint fingerTip)
    {
        if (hasRightAimState && HasFlag(rightAimState.aimStatus, statusFlag))
            return true;

        if (!useJointPinchFallback)
            return false;

        if (!TryGetJointPosition(rightJointLocations, hasRightJointLocations, HandJoint.JointThumbTip, out var thumbTip) ||
            !TryGetJointPosition(rightJointLocations, hasRightJointLocations, fingerTip, out var targetTip))
        {
            return false;
        }

        return Vector3.Distance(thumbTip, targetTip) <= jointPinchDistance;
    }

    bool TryGetJointPosition(HandJointLocations jointLocations, bool hasJointLocations, HandJoint joint, out Vector3 position)
    {
        position = default;

        if (!hasJointLocations ||
            jointLocations.isActive == 0 ||
            jointLocations.jointLocations == null ||
            jointLocations.jointLocations.Length <= (int)joint ||
            jointLocations.jointCount <= (uint)joint)
        {
            return false;
        }

        var location = jointLocations.jointLocations[(int)joint];
        position = location.pose.Position.ToVector3();

        var hasPositionStatus = HasFlag(location.locationStatus, HandLocationStatus.PositionValid) ||
            HasFlag(location.locationStatus, HandLocationStatus.PositionTracked);
        if (!hasPositionStatus && position == Vector3.zero)
        {
            return false;
        }

        return true;
    }

    static bool HasFlag(HandAimStatus value, HandAimStatus flag)
    {
        return (value & flag) != 0;
    }

    static bool HasFlag(HandLocationStatus value, HandLocationStatus flag)
    {
        return (value & flag) != 0;
    }

    bool TryFindGazedProduct(out ShopProduct product, out RaycastHit productHit)
    {
        product = null;
        productHit = default;

        var hitCount = Physics.RaycastNonAlloc(
            gazeOrigin.position,
            gazeOrigin.forward,
            gazeHits,
            maxDistance,
            productMask,
            QueryTriggerInteraction.Collide);

        var bestDistance = float.PositiveInfinity;
        for (var i = 0; i < hitCount; i++)
        {
            var hit = gazeHits[i];
            var hitProduct = hit.collider.GetComponentInParent<ShopProduct>();
            if (hitProduct == null || hit.distance >= bestDistance)
                continue;

            product = hitProduct;
            productHit = hit;
            bestDistance = hit.distance;
        }

        return product != null;
    }

    void EnsureDebugVisuals()
    {
        if (gazeLine == null)
        {
            var lineObject = new GameObject("Shop Gaze Ray");
            lineObject.transform.SetParent(transform, false);
            gazeLine = lineObject.AddComponent<LineRenderer>();
            ConfigureLine(gazeLine, new Color(1f, 0.85f, 0.05f, 1f), gazeRayWidth);
            gazeLine.positionCount = 2;
        }

        if (reticle == null)
        {
            var reticleObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticleObject.name = "Shop Gaze Reticle";
            reticleObject.transform.SetParent(transform, false);
            reticleObject.transform.localScale = Vector3.one * 0.045f;
            var collider = reticleObject.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = reticleObject.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = CreateDebugMaterial(new Color(1f, 0.85f, 0.05f, 1f));

            reticle = reticleObject.transform;
        }

        if (boundsLine == null)
        {
            var boundsObject = new GameObject("Shop Product Bounds");
            boundsObject.transform.SetParent(transform, false);
            boundsLine = boundsObject.AddComponent<LineRenderer>();
            ConfigureLine(boundsLine, new Color(0.1f, 1f, 0.85f, 1f), 0.014f);
            boundsLine.positionCount = 16;
            boundsLine.loop = false;
        }
    }

    void UpdateDebugVisuals(bool hasProduct, RaycastHit hit)
    {
        var origin = gazeOrigin != null ? gazeOrigin.position : transform.position;
        var direction = gazeOrigin != null ? gazeOrigin.forward : transform.forward;
        var end = hasProduct ? hit.point : origin + direction * emptyRayLength;

        if (gazeLine != null)
        {
            gazeLine.enabled = showGazeRay;
            gazeLine.startWidth = gazeRayWidth;
            gazeLine.endWidth = gazeRayWidth;
            gazeLine.SetPosition(0, origin);
            gazeLine.SetPosition(1, end);
        }

        if (reticle != null)
        {
            reticle.gameObject.SetActive(showReticle && hasProduct);
            if (hasProduct)
            {
                reticle.position = hit.point;
                reticle.rotation = Quaternion.LookRotation(hit.normal);
            }
        }

        if (boundsLine == null)
            return;

        var hasBounds = false;
        var bounds = default(Bounds);
        if (hasProduct && currentProduct != null)
            hasBounds = currentProduct.TryGetWorldBounds(out bounds);

        boundsLine.enabled = hasBounds;
        if (boundsLine.enabled)
            SetBoundsLine(boundsLine, bounds);
    }

    static void ConfigureLine(LineRenderer line, Color color, float width)
    {
        line.sharedMaterial = CreateDebugMaterial(color);
        line.startColor = color;
        line.endColor = color;
        line.startWidth = width;
        line.endWidth = width;
        line.useWorldSpace = true;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    static Material CreateDebugMaterial(Color color)
    {
        var shader = Shader.Find("Sprites/Default");
        var material = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
        material.color = color;
        return material;
    }

    static void SetBoundsLine(LineRenderer line, Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;
        var p0 = new Vector3(min.x, min.y, min.z);
        var p1 = new Vector3(max.x, min.y, min.z);
        var p2 = new Vector3(max.x, min.y, max.z);
        var p3 = new Vector3(min.x, min.y, max.z);
        var p4 = new Vector3(min.x, max.y, min.z);
        var p5 = new Vector3(max.x, max.y, min.z);
        var p6 = new Vector3(max.x, max.y, max.z);
        var p7 = new Vector3(min.x, max.y, max.z);

        line.SetPosition(0, p0);
        line.SetPosition(1, p1);
        line.SetPosition(2, p2);
        line.SetPosition(3, p3);
        line.SetPosition(4, p0);
        line.SetPosition(5, p4);
        line.SetPosition(6, p5);
        line.SetPosition(7, p1);
        line.SetPosition(8, p5);
        line.SetPosition(9, p6);
        line.SetPosition(10, p2);
        line.SetPosition(11, p6);
        line.SetPosition(12, p7);
        line.SetPosition(13, p3);
        line.SetPosition(14, p7);
        line.SetPosition(15, p4);
    }
}
