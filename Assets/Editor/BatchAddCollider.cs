using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 一键工具：为选中的 GameObject 及其所有子物体添加 Collider。
/// 菜单路径：Tools > 批量添加碰撞体
/// </summary>
public class BatchAddCollider : EditorWindow
{
    private bool addToAllScene = true;
    private string rootName = "Models";
    private bool onlyMissingCollider = true;
    private bool makeConvex = false;
    private bool includeInactive = true;

    [MenuItem("Tools/批量添加碰撞体")]
    public static void ShowWindow()
    {
        GetWindow<BatchAddCollider>("批量添加碰撞体");
    }

    void OnGUI()
    {
        GUILayout.Label("批量添加 MeshCollider 工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        addToAllScene = EditorGUILayout.Toggle("处理整个场景", addToAllScene);

        if (addToAllScene)
        {
            rootName = EditorGUILayout.TextField("根物体名称", rootName);
        }
        else
        {
            EditorGUILayout.HelpBox("将使用 Hierarchy 中当前选中的物体作为根", MessageType.Info);
        }

        EditorGUILayout.Space();
        onlyMissingCollider = EditorGUILayout.Toggle("只处理没有 Collider 的物体", onlyMissingCollider);
        makeConvex = EditorGUILayout.Toggle("设为 Convex（非凸面体请勿勾选）", makeConvex);
        includeInactive = EditorGUILayout.Toggle("包含未激活的物体", includeInactive);

        EditorGUILayout.Space();

        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("▶ 一键添加碰撞体", GUILayout.Height(40)))
        {
            Execute();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "提示：\n" +
            "• 超市场景请确保根物体名称为 'Models'\n" +
            "• 地板等平面 Mesh 不要勾选 Convex\n" +
            "• 操作支持 Undo（Ctrl+Z 撤销）",
            MessageType.Info);
    }

    void Execute()
    {
        GameObject root = null;

        if (addToAllScene)
        {
            root = GameObject.Find(rootName);
            if (root == null)
            {
                EditorUtility.DisplayDialog("错误",
                    $"场景中找不到名为 '{rootName}' 的 GameObject！\n请检查名称是否正确。",
                    "确定");
                return;
            }
        }
        else
        {
            if (Selection.activeGameObject == null)
            {
                EditorUtility.DisplayDialog("错误",
                    "请先在 Hierarchy 中选中一个 GameObject 作为根！",
                    "确定");
                return;
            }
            root = Selection.activeGameObject;
        }

        if (!EditorUtility.DisplayDialog("确认操作",
            $"将为 '{root.name}' 及其所有子物体添加 MeshCollider，\n是否继续？",
            "确认添加", "取消"))
        {
            return;
        }

        int count = AddCollidersRecursive(root, onlyMissingCollider, makeConvex, includeInactive);

        EditorUtility.DisplayDialog("完成",
            $"操作完成！共为 {count} 个物体添加了 MeshCollider。",
            "好的");

        // 标记场景为已修改
        EditorUtility.SetDirty(root);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
    }

    /// <summary>
    /// 递归为子物体添加 MeshCollider
    /// </summary>
    static int AddCollidersRecursive(GameObject obj, bool onlyMissing, bool convex, bool includeInactive)
    {
        int count = 0;

        foreach (Transform child in obj.transform)
        {
            if (!includeInactive && !child.gameObject.activeInHierarchy)
                continue;

            bool hasMesh = child.GetComponent<MeshFilter>() != null;
            bool hasCollider = child.GetComponent<Collider>() != null;

            if (hasMesh && (!onlyMissing || !hasCollider))
            {
                // 移除旧 Collider（如果有）
                if (hasCollider)
                {
                    Collider[] oldColliders = child.GetComponents<Collider>();
                    foreach (var c in oldColliders)
                    {
                        Undo.DestroyObjectImmediate(c);
                    }
                }

                MeshCollider mc = Undo.AddComponent<MeshCollider>(child.gameObject);
                mc.convex = convex;
                count++;
            }

            // 递归处理子物体
            count += AddCollidersRecursive(child.gameObject, onlyMissing, convex, includeInactive);
        }

        return count;
    }
}
