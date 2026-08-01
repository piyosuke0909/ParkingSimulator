using System.Collections;
using System.Globalization;
using UnityEngine;

[DefaultExecutionOrder(-475)]
public class P3BackendHealthChecker : MonoBehaviour
{
    [Header("Components")]
    public P3BackendSettings settings;
    public P3TransmissionStatus transmissionStatus;

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private bool requestInProgress;

    [SerializeField]
    private float nextHealthCheckAtUnscaledTime;

    private bool immediateCheckRequested;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        immediateCheckRequested = true;
        nextHealthCheckAtUnscaledTime = 0f;
    }

    private void Update()
    {
        if (requestInProgress || settings == null || !settings.enableHealthCheck)
        {
            return;
        }

        if (immediateCheckRequested || Time.unscaledTime >= nextHealthCheckAtUnscaledTime)
        {
            immediateCheckRequested = false;
            StartCoroutine(CheckHealth());
        }
    }

    [ContextMenu("Resolve P3 Health References")]
    public void ResolveReferences()
    {
        if (settings == null)
        {
            settings = GetComponent<P3BackendSettings>();
        }

        if (transmissionStatus == null)
        {
            transmissionStatus = GetComponent<P3TransmissionStatus>();
        }
    }

    [ContextMenu("Check Backend Health Now")]
    public void RequestHealthCheck()
    {
        immediateCheckRequested = true;
        nextHealthCheckAtUnscaledTime = 0f;
    }

    private IEnumerator CheckHealth()
    {
        string validationError = "P3BackendSettings is not assigned.";
        if (settings == null || !settings.TryValidate(out validationError))
        {
            if (transmissionStatus != null)
            {
                transmissionStatus.RecordHealth(false, 0, validationError);
            }
            ScheduleNextCheck();
            yield break;
        }

        requestInProgress = true;
        P3HttpResponse response = null;

        yield return P3HttpRequestUtility.Get(
            settings.BuildHealthUrl(),
            settings,
            value => response = value
        );

        requestInProgress = false;
        bool healthy = response != null && response.IsAccepted(settings);
        long statusCode = response != null ? response.statusCode : 0;
        string error = healthy
            ? string.Empty
            : (response != null
                ? "Health check HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) +
                  ": " + response.BuildDiagnosticMessage()
                : "Health check returned no response.");

        if (transmissionStatus != null)
        {
            transmissionStatus.RecordHealth(healthy, statusCode, error);
        }

        ScheduleNextCheck();
    }

    private void ScheduleNextCheck()
    {
        float interval = settings != null
            ? Mathf.Max(1f, settings.healthCheckIntervalSeconds)
            : 10f;
        nextHealthCheckAtUnscaledTime = Time.unscaledTime + interval;
    }
}
