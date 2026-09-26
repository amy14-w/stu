using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class SurfaceManager : MonoBehaviour
{
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARAnchorManager anchorManager;
    [SerializeField] private ARPlaneManager planeManager;

    ARAnchor surfaceAnchor;

    GameObject surface;

    bool changingSurface;

    private void Update()
    {
        if (changingSurface)
        {
            CheckForInput();
        }
    }

    private void CheckForInput()
    {
        if (Pointer.current == null) return;

        // Check if pointer/touch pressed this frame
        if (Pointer.current.press.wasPressedThisFrame)
        {
            Vector2 screenPosition = Pointer.current.position.ReadValue();

            // Prevent spawning if the user tapped on a UI button/element
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            SetupSurface(screenPosition);
        }
    }

    private void SetupSurface(Vector2 touchPos)
    {
        var rayHits = new List<ARRaycastHit>();

        raycastManager.Raycast(touchPos, rayHits, TrackableType.PlaneWithinBounds);

        if (rayHits.Count > 0)
        {
            var hit = rayHits[0];

            Vector3 hitPosePos = hit.pose.position;

            Vector3 camPos = Camera.main.transform.position;
            Vector3 horizontalTargetPos = new Vector3(camPos.x, hitPosePos.y, camPos.z);

            Vector3 dir = (horizontalTargetPos - hitPosePos).normalized;
            Quaternion rotation = Quaternion.LookRotation(dir);

            ARPlane plane = planeManager.GetPlane(hit.trackableId);

            if (plane != null)
            {
                if (surfaceAnchor != null)
                {
                    anchorManager.TryRemoveAnchor(surfaceAnchor);
                }

                surfaceAnchor = anchorManager.AttachAnchor(plane, new Pose(hitPosePos, hit.pose.rotation));

                if (surface == null)
                {
                    surface = new GameObject("ARSurface");
                    
                }

                surface.transform.parent = surfaceAnchor.transform;
                surface.transform.localPosition = Vector3.zero;
                surface.transform.localRotation = Quaternion.identity;

                changingSurface = false;

                SetPlaneManagerActive(false);
            }
        }
    }

    public void ChangeSurface()
    {
        changingSurface = true;

        SetPlaneManagerActive(true);
    }

    private void SetPlaneManagerActive(bool enabled)
    {
        planeManager.enabled = enabled;

        foreach (ARPlane plane in planeManager.trackables) 
        {
            plane.gameObject.SetActive(enabled);
        }
    }

    public Transform GetMainSurface()
    {
        return surface.transform;
    }

    public bool IsSurfaceInitialized()
    {
        return surface != null;
    }
}
