using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// 将 Pico 4 手柄摇杆输入转发到 VR_InvectorBridge。
/// 挂到与 VR_InvectorBridge 同一个 GameObject 上。
/// 
/// 左手柄摇杆 = 移动方向
/// 右手柄摇杆按下 = 停止移动
/// </summary>
[RequireComponent(typeof(VR_InvectorBridge))]
public class PicoInputToBridge : MonoBehaviour
{
    private VR_InvectorBridge bridge;

    [Header("输入通道")]
    [Tooltip("用于移动的手柄摇杆（默认左手柄）")]
    public InputActionProperty moveAction;

    [Tooltip("按下时停止移动（默认右手柄摇杆按下）")]
    public InputActionProperty stopAction;

    [Header("灵敏度")]
    [Range(0.1f, 2f)]
    public float moveSpeed = 1f;

    void Awake()
    {
        bridge = GetComponent<VR_InvectorBridge>();
    }

    void OnEnable()
    {
        if (moveAction.action != null) moveAction.action.Enable();
        if (stopAction.action != null) stopAction.action.Enable();
    }

    void OnDisable()
    {
        if (moveAction.action != null) moveAction.action.Disable();
        if (stopAction.action != null) stopAction.action.Disable();
    }

    void Update()
    {
        if (bridge == null) return;

        // 读取摇杆输入
        Vector2 stickInput = moveAction.action?.ReadValue<Vector2>() ?? Vector2.zero;

        // 检测停止信号
        bool stopPressed = stopAction.action?.IsPressed() ?? false;

        if (stopPressed || stickInput.magnitude < 0.1f)
        {
            bridge.StopMovement();
        }
        else
        {
            // X=左右, Y=前后，与 Invector 坐标系一致
            bridge.SetMovement(stickInput * moveSpeed);
        }
    }
}
