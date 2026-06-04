using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public static class SupermarketVRSceneSetup
{
    const string k_SetupObjectName = "VR Teleport Avatar Binder";

    [MenuItem("Tools/VR/Setup Supermarket Teleport Rig")]
    public static void SetupTeleportRig()
    {
        var xrOrigin = FindNamedTransform("XR Origin (XR Rig)") ?? FindNamedTransform("XR Origin");
        var xrCamera = FindXRCamera(xrOrigin);
        var bridge = UnityEngine.Object.FindObjectOfType<VR_InvectorBridge>(true);

        if (xrOrigin == null)
        {
            EditorUtility.DisplayDialog("VR setup", "Could not find XR Origin (XR Rig) in the current scene.", "OK");
            return;
        }

        if (xrCamera == null)
        {
            EditorUtility.DisplayDialog("VR setup", "Could not find the XR Main Camera in the current scene.", "OK");
            return;
        }

        if (bridge == null)
        {
            EditorUtility.DisplayDialog("VR setup", "Could not find VR_InvectorBridge on the avatar.", "OK");
            return;
        }

        var setupObject = GameObject.Find(k_SetupObjectName);
        if (setupObject == null)
        {
            setupObject = new GameObject(k_SetupObjectName);
            Undo.RegisterCreatedObjectUndo(setupObject, "Create VR Teleport Avatar Binder");
        }

        var binder = setupObject.GetComponent<VRAvatarTeleportBinder>();
        if (binder == null)
            binder = Undo.AddComponent<VRAvatarTeleportBinder>(setupObject);

        Undo.RecordObject(binder, "Configure VR Teleport Avatar Binder");
        binder.xrOrigin = xrOrigin;
        binder.xrCamera = xrCamera;
        binder.avatarRoot = bridge.transform;
        binder.invectorBridge = bridge;
        binder.startAlignment = VRAvatarTeleportBinder.StartAlignment.MoveXROriginToAvatar;
        binder.snapAvatarToXROrigin = true;
        binder.matchAvatarYawToHeadset = true;
        binder.disableKeyboardDebug = true;
        binder.bindBridgeCameraReference = true;
        binder.hideHeadOnStart = true;
        binder.headRenderers = GuessHeadRenderers(bridge.transform);

        Undo.RecordObject(bridge, "Bind Invector Bridge To XR Camera");
        bridge.useKeyboardDebug = false;
        bridge.referenceCamera = xrCamera;

        DisableNonXRCameras(xrCamera);
        DisableExtraAudioListeners(xrCamera);

        EditorUtility.SetDirty(binder);
        EditorUtility.SetDirty(bridge);
        Selection.activeGameObject = setupObject;

        EditorUtility.DisplayDialog(
            "VR setup",
            "Supermarket VR teleport binding is ready.\n\nPlease review the binder object's Head Renderers list. If the robot head is not hidden in Play Mode, drag the head/hair/face renderers into that list.",
            "OK");
    }

    [MenuItem("Tools/VR/Add Teleport Area To Selected")]
    public static void AddTeleportAreaToSelected()
    {
        var changedCount = 0;

        foreach (var selectedObject in Selection.gameObjects)
        {
            var colliders = selectedObject.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider.isTrigger)
                    continue;

                var teleportArea = collider.GetComponent<TeleportationArea>();
                if (teleportArea == null)
                {
                    teleportArea = Undo.AddComponent<TeleportationArea>(collider.gameObject);
                    changedCount++;
                }

                EditorUtility.SetDirty(teleportArea);
            }
        }

        EditorUtility.DisplayDialog(
            "Teleport areas",
            changedCount == 0
                ? "No new Teleportation Area was added. Select floor/walkable objects that have non-trigger colliders."
                : $"Added Teleportation Area to {changedCount} collider object(s).",
            "OK");
    }

    [MenuItem("Tools/VR/Add Teleport Area To Selected", true)]
    public static bool ValidateAddTeleportAreaToSelected()
    {
        return Selection.gameObjects.Length > 0;
    }

    static Transform FindNamedTransform(string objectName)
    {
        var go = GameObject.Find(objectName);
        return go != null ? go.transform : null;
    }

    static Transform FindXRCamera(Transform xrOrigin)
    {
        if (xrOrigin != null)
        {
            foreach (var camera in xrOrigin.GetComponentsInChildren<Camera>(true))
            {
                if (camera.CompareTag("MainCamera") || camera.name.IndexOf("Main Camera", StringComparison.OrdinalIgnoreCase) >= 0)
                    return camera.transform;
            }
        }

        return Camera.main != null ? Camera.main.transform : null;
    }

    static Renderer[] GuessHeadRenderers(Transform avatarRoot)
    {
        var renderers = new List<Renderer>();
        var keywords = new[] { "head", "hair", "face", "eye", "neck" };

        foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
        {
            var lowerName = renderer.name.ToLowerInvariant();
            foreach (var keyword in keywords)
            {
                if (lowerName.Contains(keyword))
                {
                    renderers.Add(renderer);
                    break;
                }
            }
        }

        return renderers.ToArray();
    }

    static void DisableNonXRCameras(Transform xrCamera)
    {
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>(true))
        {
            if (camera.transform == xrCamera)
                continue;

            Undo.RecordObject(camera, "Disable Legacy Camera");
            camera.enabled = false;
            EditorUtility.SetDirty(camera);
        }
    }

    static void DisableExtraAudioListeners(Transform xrCamera)
    {
        foreach (var listener in UnityEngine.Object.FindObjectsOfType<AudioListener>(true))
        {
            if (listener.transform == xrCamera)
                continue;

            Undo.RecordObject(listener, "Disable Extra Audio Listener");
            listener.enabled = false;
            EditorUtility.SetDirty(listener);
        }
    }
}
