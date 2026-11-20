using UnityEngine;
using Vuforia;

// Attaches this knickknack to a Vuforia MultiTarget (the Merge cube or the Class cube).
// If the scene already has a MultiTarget with that name it is used, otherwise one is created
// at runtime from the KnickKnack database. The content is only shown while the cube is tracked,
// and the ambient sound plays/pauses with it.
public class KnickknackTarget : MonoBehaviour
{
    public string targetName = "Merge";
    public string dataSetPath = "Vuforia/KnickKnack.xml";
    public GameObject content;
    public AudioSource ambientAudio;

    public ObserverBehaviour Observer { get; private set; }
    public bool IsTracked { get; private set; }

    void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        // each knickknack uses a few per-pixel lights; mobile quality levels default to 1
        QualitySettings.pixelLightCount = Mathf.Max(QualitySettings.pixelLightCount, 4);
        if (content != null) content.SetActive(false);
    }

    void Start()
    {
        if (VuforiaApplication.Instance.IsRunning) Attach();
        else VuforiaApplication.Instance.OnVuforiaStarted += Attach;
    }

    void OnDestroy()
    {
        if (VuforiaApplication.Instance != null) VuforiaApplication.Instance.OnVuforiaStarted -= Attach;
        if (Observer != null) Observer.OnTargetStatusChanged -= OnStatusChanged;
    }

    void Attach()
    {
        VuforiaApplication.Instance.OnVuforiaStarted -= Attach;
        if (Observer != null) return;

        foreach (var mt in FindObjectsOfType<MultiTargetBehaviour>(true))
        {
            if (mt.TargetName == targetName) { Observer = mt; break; }
        }
        if (Observer == null)
        {
            Observer = VuforiaBehaviour.Instance.ObserverFactory.CreateMultiTarget(dataSetPath, targetName);
        }
        if (Observer == null)
        {
            Debug.LogError($"Could not find or create MultiTarget '{targetName}' from {dataSetPath}");
            return;
        }

        transform.SetParent(Observer.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        Observer.OnTargetStatusChanged += OnStatusChanged;
        OnStatusChanged(Observer, Observer.TargetStatus);
    }

    void OnStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        bool tracked = status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED;
        if (tracked == IsTracked && content != null && content.activeSelf == tracked) return;
        IsTracked = tracked;

        if (content != null) content.SetActive(tracked);

        if (ambientAudio != null)
        {
            if (tracked)
            {
                if (ambientAudio.time > 0f) ambientAudio.UnPause();
                else ambientAudio.Play();
            }
            else ambientAudio.Pause();
        }
    }
}
