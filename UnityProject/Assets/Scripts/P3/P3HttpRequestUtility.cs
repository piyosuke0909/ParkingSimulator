using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class P3HttpResponse
{
    public bool requestCompleted;
    public long statusCode;
    public string responseBody;
    public string error;

    public bool IsAccepted(P3BackendSettings settings)
    {
        if (statusCode >= 200 && statusCode <= 299)
        {
            return true;
        }

        return settings != null && settings.treatHttp409AsSuccess && statusCode == 409;
    }

    public bool IsTransientFailure()
    {
        return statusCode == 0 ||
               statusCode == 408 ||
               statusCode == 425 ||
               statusCode == 429 ||
               (statusCode >= 500 && statusCode <= 599);
    }

    public string BuildDiagnosticMessage()
    {
        string message = string.IsNullOrWhiteSpace(error)
            ? "HTTP request failed."
            : error;

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            const int maximumLength = 1000;
            string body = responseBody.Length > maximumLength
                ? responseBody.Substring(0, maximumLength) + "..."
                : responseBody;
            message += " Response=" + body;
        }

        return message;
    }
}

public static class P3HttpRequestUtility
{
    public static IEnumerator Post(
        string url,
        string contentType,
        string body,
        P3BackendSettings settings,
        Action<P3HttpResponse> completed)
    {
        UnityWebRequest request = null;

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", contentType);
            request.SetRequestHeader("Accept", "application/json");
            ConfigureRequest(request, settings);
        }
        catch (Exception exception)
        {
            completed?.Invoke(new P3HttpResponse
            {
                requestCompleted = false,
                statusCode = 0,
                error = exception.Message,
                responseBody = string.Empty
            });
            request?.Dispose();
            yield break;
        }

        yield return request.SendWebRequest();
        P3HttpResponse response = BuildResponse(request);
        request.Dispose();
        completed?.Invoke(response);
    }

    public static IEnumerator Get(
        string url,
        P3BackendSettings settings,
        Action<P3HttpResponse> completed)
    {
        float timeoutSeconds = settings != null ? settings.requestTimeoutSeconds : 10f;
        yield return Get(url, settings, timeoutSeconds, completed);
    }

    public static IEnumerator Get(
        string url,
        P3BackendSettings settings,
        float timeoutSeconds,
        Action<P3HttpResponse> completed)
    {
        UnityWebRequest request = null;

        try
        {
            request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Accept", "application/json");
            ConfigureRequest(request, settings, timeoutSeconds);
        }
        catch (Exception exception)
        {
            completed?.Invoke(new P3HttpResponse
            {
                requestCompleted = false,
                statusCode = 0,
                error = exception.Message,
                responseBody = string.Empty
            });
            request?.Dispose();
            yield break;
        }

        yield return request.SendWebRequest();
        P3HttpResponse response = BuildResponse(request);
        request.Dispose();
        completed?.Invoke(response);
    }

    private static void ConfigureRequest(UnityWebRequest request, P3BackendSettings settings)
    {
        ConfigureRequest(
            request,
            settings,
            settings != null ? settings.requestTimeoutSeconds : 10f
        );
    }

    private static void ConfigureRequest(
        UnityWebRequest request,
        P3BackendSettings settings,
        float timeoutSeconds)
    {
        if (request == null || settings == null)
        {
            return;
        }

        request.timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));

        switch (settings.authenticationMode)
        {
            case P3AuthenticationMode.ApiKeyHeader:
                if (!string.IsNullOrWhiteSpace(settings.apiKeyHeaderName) &&
                    !string.IsNullOrWhiteSpace(settings.apiKey))
                {
                    request.SetRequestHeader(settings.apiKeyHeaderName.Trim(), settings.apiKey);
                }
                break;

            case P3AuthenticationMode.BearerToken:
                if (!string.IsNullOrWhiteSpace(settings.bearerToken))
                {
                    request.SetRequestHeader(
                        "Authorization",
                        "Bearer " + settings.bearerToken.Trim()
                    );
                }
                break;
        }
    }

    private static P3HttpResponse BuildResponse(UnityWebRequest request)
    {
        string body = request.downloadHandler != null
            ? request.downloadHandler.text
            : string.Empty;

#if UNITY_2020_2_OR_NEWER
        bool completed = request.result == UnityWebRequest.Result.Success ||
                         request.result == UnityWebRequest.Result.ProtocolError;
#else
        bool completed = !request.isNetworkError;
#endif

        return new P3HttpResponse
        {
            requestCompleted = completed,
            statusCode = request.responseCode,
            responseBody = body ?? string.Empty,
            error = request.error ?? string.Empty
        };
    }
}
