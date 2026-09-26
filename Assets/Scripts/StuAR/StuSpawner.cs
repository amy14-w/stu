using UnityEngine;

using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using static Mediapipe.CopyCalculatorOptions.Types;

public class StuSpawner : MonoBehaviour
{
    [SerializeField] private GameObject stuPrefab;

    Transform stuInstance;

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }

    public void TrySpawnStu()
    {
        if (SurfaceManager.Instance.IsSurfaceInitialized())
        {
            if (stuInstance == null)
            {
                stuInstance = Instantiate(stuPrefab).transform;
            }

            var surface = SurfaceManager.Instance.GetMainSurface();

            stuInstance.parent = surface;

            Vector3 camPos = Camera.main.transform.position;
            Vector3 horizontalTargetPos = new Vector3(camPos.x, surface.position.y, camPos.z);

            Vector3 dir = (horizontalTargetPos - surface.position).normalized;
            Quaternion rotation = Quaternion.LookRotation(dir);

            stuInstance.localPosition = Vector3.zero;
            stuInstance.rotation = rotation;

            stuInstance.GetComponent<Stu>().Initialize();
        }  else
        {
            Debug.LogWarning("Could not place Stu: surface not initialized");
        }
    }

    public Stu GetStu()
    {
        return stuInstance.GetComponent<Stu>();
    }

    /*private void SpawnStu(Vector2 touchPos) // spawns or repositions Stu to where user clicks, makes him face the user as well
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

            if (plane != null) // potentially make this a surface manager - creates a surface gameObject that is parented to a surface anchor and holds Stu and diagram
            {
                if (surfaceAnchor != null)
                {
                    anchorManager.TryRemoveAnchor(surfaceAnchor);
                }

                surfaceAnchor = anchorManager.AttachAnchor(plane, new Pose(hitPosePos, hit.pose.rotation));

                if (stuInstance == null)
                {
                    stuInstance = Instantiate(stuPrefab).transform;
                }

                stuInstance.parent = surfaceAnchor.transform;

                stuInstance.localPosition = Vector3.zero;
                stuInstance.rotation = rotation;
            }
        }
    }*/
}
