using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;


public class ProblemScanner : MonoBehaviour
{
    [SerializeField] private ARMarkerManager m_MarkerManager;


    private void OnEnable()
    {
        if (m_MarkerManager != null)
        {
            m_MarkerManager.trackablesChanged.AddListener(OnMarkersChanged);
        }
    }

    private void OnDisable()
    {
        if (m_MarkerManager != null)
        {
            m_MarkerManager.trackablesChanged.RemoveListener(OnMarkersChanged);
        }
    }

    private void OnMarkersChanged(ARTrackablesChangedEventArgs<ARMarker> changes)
    {
        if (changes.added.Count > 0)
        {
            ProcessMarkerData(changes.added[0]);    // process first QR code found
        }
    }

    private void ProcessMarkerData(ARMarker marker)
    {
        // Filter out non-QR markers
        if (marker.markerType != XRMarkerType.QRCode && marker.markerType != XRMarkerType.MicroQRCode)
        {
            return;
        }

        // Only process markers containing string data
        if (marker.dataBuffer.bufferType == XRSpatialBufferType.String)
        {
            string content = ReadStringData(marker);

            ProblemData problem = ProblemsManager.Instance.RetrieveProblem(content);

            if (problem != null)
            {
                ProblemsManager.Instance.TryProblemChange(problem);
            }
        }
    }

    private string ReadStringData(ARMarker marker)
    {
        var result = m_MarkerManager.TryGetStringData(marker);

        if (result.status.IsError())
        {
            Debug.LogWarning($"Failed to get string data from marker {marker.trackableId}: {result.status}");
            return null;
        }

        string qrContent = result.value;
        Debug.Log($"[ProblemScanner] QR String Data: {qrContent}");
        return qrContent;
    }
}