using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public class SupermarketShoppingSetupTool : EditorWindow
{
    [Serializable]
    class ProductMapping
    {
        public string prefabName;
        public string displayName;
        public int price;
        public bool enabled = true;
    }

    const string ModelsRootName = "Models";
    const string ShoppingRootName = "Shopping Interaction System";
    const string CartCanvasName = "Shopping Cart Canvas";

    readonly List<ProductMapping> mappings = new List<ProductMapping>();
    bool onlyKnownProducts = true;
    bool removeUnknownProductComponents = true;
    bool addMissingColliders = true;
    bool createInteractionSystem = true;
    bool createCartUi = true;

    [MenuItem("Tools/Shopping/Setup Supermarket Products")]
    public static void ShowWindow()
    {
        var window = GetWindow<SupermarketShoppingSetupTool>("Shopping Setup");
        window.EnsureDefaultMappings();
        window.Show();
    }

    void OnEnable()
    {
        EnsureDefaultMappings();
    }

    void OnGUI()
    {
        GUILayout.Label("Supermarket Product Setup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Scans the Models root for hypercasual_prop### instances, adds ShopProduct and colliders, then creates a gaze selector and shopping cart.",
            MessageType.Info);

        onlyKnownProducts = EditorGUILayout.Toggle("Only Known Products", onlyKnownProducts);
        removeUnknownProductComponents = EditorGUILayout.Toggle("Remove Unknown Products", removeUnknownProductComponents);
        addMissingColliders = EditorGUILayout.Toggle("Add Missing Colliders", addMissingColliders);
        createInteractionSystem = EditorGUILayout.Toggle("Create Interaction System", createInteractionSystem);
        createCartUi = EditorGUILayout.Toggle("Create Cart UI", createCartUi);

        EditorGUILayout.Space();
        GUILayout.Label("Product Name Mapping", EditorStyles.boldLabel);

        for (var i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            EditorGUILayout.BeginHorizontal();
            mapping.enabled = EditorGUILayout.Toggle(mapping.enabled, GUILayout.Width(18));
            mapping.prefabName = EditorGUILayout.TextField(mapping.prefabName, GUILayout.Width(145));
            mapping.displayName = EditorGUILayout.TextField(mapping.displayName);
            mapping.price = EditorGUILayout.IntField(mapping.price, GUILayout.Width(55));
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Mapping"))
            mappings.Add(new ProductMapping { prefabName = "", displayName = "", price = 0 });

        if (GUILayout.Button("Reset Defaults"))
        {
            mappings.Clear();
            AddDefaultMappings();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Setup Products In Current Scene", GUILayout.Height(42)))
            Execute();
        GUI.backgroundColor = Color.white;
    }

    void Execute()
    {
        var modelsRoot = GameObject.Find(ModelsRootName);
        if (modelsRoot == null)
        {
            EditorUtility.DisplayDialog("Shopping setup", $"Could not find a scene object named '{ModelsRootName}'.", "OK");
            return;
        }

        var mappingByPrefab = BuildMappingLookup();
        var productCount = 0;
        var colliderCount = 0;

        if (removeUnknownProductComponents)
            CleanupStaleProductComponents(modelsRoot, mappingByPrefab);

        foreach (var transform in modelsRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!TryGetPrefabProductName(transform.name, out var prefabName))
                continue;

            mappingByPrefab.TryGetValue(prefabName, out var mapping);
            if (onlyKnownProducts && mapping == null)
            {
                continue;
            }

            var product = transform.GetComponent<ShopProduct>();
            if (product == null)
                product = Undo.AddComponent<ShopProduct>(transform.gameObject);
            else
                Undo.RecordObject(product, "Configure Shop Product");

            product.productId = prefabName;
            product.displayName = mapping != null ? mapping.displayName : prefabName;
            product.price = mapping != null ? mapping.price : 0;
            EditorUtility.SetDirty(product);
            productCount++;

            if (addMissingColliders)
                colliderCount += EnsureCollider(transform.gameObject);
        }

        if (createInteractionSystem)
            SetupInteractionSystem();

        EditorSceneManager.MarkSceneDirty(modelsRoot.scene);
        EditorUtility.DisplayDialog(
            "Shopping setup",
            $"Configured {productCount} product object(s).\nAdded {colliderCount} collider(s).",
            "OK");
    }

    void CleanupStaleProductComponents(GameObject modelsRoot, Dictionary<string, ProductMapping> mappingByPrefab)
    {
        foreach (var product in modelsRoot.GetComponentsInChildren<ShopProduct>(true))
        {
            if (product == null)
                continue;

            if (!TryGetPrefabProductName(product.name, out var prefabName) ||
                (onlyKnownProducts && !mappingByPrefab.ContainsKey(prefabName)))
            {
                Undo.DestroyObjectImmediate(product);
            }
        }
    }

    void SetupInteractionSystem()
    {
        var root = GameObject.Find(ShoppingRootName);
        if (root == null)
        {
            root = new GameObject(ShoppingRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Shopping Interaction System");
        }

        var cart = root.GetComponent<ShoppingCart>();
        if (cart == null)
            cart = Undo.AddComponent<ShoppingCart>(root);

        var selector = root.GetComponent<ShopGazeSelector>();
        if (selector == null)
            selector = Undo.AddComponent<ShopGazeSelector>(root);

        Undo.RecordObject(selector, "Configure Shop Gaze Selector");
        Undo.RecordObject(cart, "Configure Shopping Cart");

        selector.gazeOrigin = FindXRCamera();
        selector.cart = cart;
        selector.maxDistance = 8f;
        selector.showGazeRay = true;
        selector.showReticle = true;
        selector.logTargetChanges = true;

        ConfigureVRMovementSafety();

        if (createCartUi)
            ConfigureCartUi(cart, selector.gazeOrigin);

        EditorUtility.SetDirty(selector);
        EditorUtility.SetDirty(cart);
    }

    void ConfigureVRMovementSafety()
    {
        var binder = FindObjectOfType<VRAvatarTeleportBinder>(true);
        if (binder != null)
        {
            Undo.RecordObject(binder, "Configure VR Avatar Binder");
            binder.keepAvatarUpright = true;
            binder.preserveAvatarHeightDuringSync = true;
            binder.preserveRigHeightWhenAligningToAvatar = true;
            EditorUtility.SetDirty(binder);
        }

        var teleport = FindObjectOfType<PicoHandTeleport>(true);
        if (teleport != null)
        {
            Undo.RecordObject(teleport, "Configure Pico Hand Teleport");
            teleport.preserveCurrentAvatarHeight = true;
            EditorUtility.SetDirty(teleport);
        }

        var bridge = FindObjectOfType<VR_InvectorBridge>(true);
        if (bridge != null)
        {
            Undo.RecordObject(bridge, "Configure VR Invector Bridge");
            bridge.keepUpright = true;
            bridge.disableFallRagdoll = true;
            EditorUtility.SetDirty(bridge);
        }
    }

    void ConfigureCartUi(ShoppingCart cart, Transform xrCamera)
    {
        var canvasObject = GameObject.Find(CartCanvasName);
        if (canvasObject == null)
        {
            canvasObject = new GameObject(CartCanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Shopping Cart UI");
        }

        var canvas = canvasObject.GetComponent<Canvas>();
        if (canvas == null)
            canvas = Undo.AddComponent<Canvas>(canvasObject);

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = Undo.AddComponent<CanvasScaler>(canvasObject);

        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
            Undo.AddComponent<GraphicRaycaster>(canvasObject);

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = xrCamera != null ? xrCamera.GetComponent<Camera>() : Camera.main;
        scaler.dynamicPixelsPerUnit = 10;

        var rect = canvas.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(420, 300);
        canvasObject.transform.localScale = Vector3.one * 0.0035f;

        var background = canvasObject.GetComponent<Image>();
        if (background == null)
            background = Undo.AddComponent<Image>(canvasObject);
        background.color = new Color(0.03f, 0.035f, 0.04f, 0.78f);
        background.raycastTarget = false;

        if (xrCamera != null)
        {
            canvasObject.transform.position = xrCamera.position + xrCamera.forward * 1.4f + xrCamera.right * 0.55f - xrCamera.up * 0.2f;
            canvasObject.transform.rotation = Quaternion.LookRotation(canvasObject.transform.position - xrCamera.position);
        }

        var text = canvasObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null)
        {
            var textObject = new GameObject("Cart Text");
            Undo.RegisterCreatedObjectUndo(textObject, "Create Cart Text");
            textObject.transform.SetParent(canvasObject.transform, false);
            text = Undo.AddComponent<TextMeshProUGUI>(textObject);
        }

        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(18, 18);
        textRect.offsetMax = new Vector2(-18, -18);
        text.fontSize = 28;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;

        cart.textMeshProText = text;
        cart.RefreshDisplay();

        var follower = canvasObject.GetComponent<VRHudFollower>();
        if (follower == null)
            follower = Undo.AddComponent<VRHudFollower>(canvasObject);

        follower.target = xrCamera;
        follower.localOffset = new Vector3(0.42f, -0.24f, 1.15f);
        follower.followSharpness = 18f;
        follower.yawOnly = false;
        EditorUtility.SetDirty(follower);
    }

    Dictionary<string, ProductMapping> BuildMappingLookup()
    {
        var lookup = new Dictionary<string, ProductMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!mapping.enabled || string.IsNullOrWhiteSpace(mapping.prefabName))
                continue;

            lookup[mapping.prefabName.Trim()] = mapping;
        }

        return lookup;
    }

    static bool TryGetPrefabProductName(string objectName, out string prefabName)
    {
        var match = Regex.Match(objectName, @"^hypercasual_prop\d+(?:\s*\(\d+\))?$");
        if (match.Success)
        {
            prefabName = Regex.Match(objectName, @"hypercasual_prop\d+").Value;
            return true;
        }

        prefabName = null;
        return false;
    }

    static int EnsureCollider(GameObject target)
    {
        if (target.GetComponent<Collider>() != null)
            return 0;

        if (target.GetComponent<MeshFilter>() == null)
            return 0;

        Undo.AddComponent<MeshCollider>(target);
        return 1;
    }

    static Transform FindXRCamera()
    {
        if (Camera.main != null)
            return Camera.main.transform;

        foreach (var camera in FindObjectsOfType<Camera>(true))
        {
            if (camera.name.IndexOf("Main Camera", StringComparison.OrdinalIgnoreCase) >= 0)
                return camera.transform;
        }

        return null;
    }

    void EnsureDefaultMappings()
    {
        if (mappings.Count == 0)
            AddDefaultMappings();
    }

    void AddDefaultMappings()
    {
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop003", displayName = "Coffee", price = 12 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop004", displayName = "Chocolate Milk", price = 8 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop005", displayName = "Cereal", price = 15 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop006", displayName = "Bottled Water", price = 3 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop007", displayName = "Toilet Paper", price = 18 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop008", displayName = "Eggs", price = 16 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop009", displayName = "Eggs", price = 16 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop010", displayName = "Eggs", price = 16 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop011", displayName = "Eggs", price = 16 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop012", displayName = "Sandwich", price = 10 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop013", displayName = "Chocolate Bar", price = 6 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop014", displayName = "Coffee", price = 12 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop015", displayName = "Instant Noodles", price = 9 });
        mappings.Add(new ProductMapping { prefabName = "hypercasual_prop016", displayName = "Bottled Water", price = 3 });
    }
}
