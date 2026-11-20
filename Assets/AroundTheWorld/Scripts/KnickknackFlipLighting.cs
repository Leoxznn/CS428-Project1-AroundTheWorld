using UnityEngine;

// Turning the cube upside down and back up switches between two lighting styles.
// Doing it again switches back to the original style.
[RequireComponent(typeof(KnickknackTarget))]
public class KnickknackFlipLighting : MonoBehaviour
{
    public GameObject[] styleA; // original lighting
    public GameObject[] styleB; // alternative lighting
    public string styleAName = "Day";
    public string styleBName = "Night";

    [Tooltip("dot(cube up, camera up) below this counts as upside down")]
    public float upsideDownThreshold = -0.6f;
    [Tooltip("dot(cube up, camera up) above this counts as right side up")]
    public float uprightThreshold = 0.6f;

    public bool UsingStyleB { get; private set; }

    KnickknackTarget target;
    bool wasUpsideDown;

    void Awake()
    {
        target = GetComponent<KnickknackTarget>();
        Apply(false);
    }

    void Update()
    {
        if (!target.IsTracked) return;

        Camera cam = Camera.main;
        Vector3 refUp = cam != null ? cam.transform.up : Vector3.up;
        float d = Vector3.Dot(transform.up, refUp);

        if (d < upsideDownThreshold)
        {
            wasUpsideDown = true;
        }
        else if (d > uprightThreshold && wasUpsideDown)
        {
            wasUpsideDown = false;
            Apply(!UsingStyleB);
        }
    }

    public void Apply(bool useB)
    {
        UsingStyleB = useB;
        foreach (var go in styleA) if (go != null) go.SetActive(!useB);
        foreach (var go in styleB) if (go != null) go.SetActive(useB);
        Debug.Log($"{name}: lighting -> {(useB ? styleBName : styleAName)}");
    }
}
