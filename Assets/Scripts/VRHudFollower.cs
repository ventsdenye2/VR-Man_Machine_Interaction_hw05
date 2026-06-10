using UnityEngine;

[DisallowMultipleComponent]
public class VRHudFollower : MonoBehaviour
{
    public Transform target;
    public Vector3 localOffset = new Vector3(0.42f, -0.24f, 1.15f);
    public float followSharpness = 18f;
    public bool yawOnly;

    void Awake()
    {
        if (target == null && Camera.main != null)
            target = Camera.main.transform;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        var rotation = yawOnly ? Quaternion.Euler(0f, target.eulerAngles.y, 0f) : target.rotation;
        var desiredPosition = target.position + rotation * localOffset;
        var desiredRotation = Quaternion.LookRotation(desiredPosition - target.position, Vector3.up);

        var t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, t);
    }
}
