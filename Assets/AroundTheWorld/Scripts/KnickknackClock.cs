#pragma warning disable 0649 // fields are filled by JsonUtility
using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

// Shows the local time of the attraction. The UTC offset comes from worldtimeapi.org
// (re-synced every 10 minutes); if that site is down it falls back to the offset sent
// by OpenWeather, and finally to a fixed offset typed in the inspector.
public class KnickknackClock : MonoBehaviour
{
    public enum ClockStyle { TwelveHour, TwentyFourHour }

    public TextMeshPro text;
    public string placeName = "Paris";
    public string ianaTimezone = "Europe/Paris";
    public ClockStyle style = ClockStyle.TwelveHour;
    public float resyncSeconds = 600f;
    public KnickknackWeather weatherFallback;
    public float fallbackUtcOffsetHours = 1f;

    [Serializable] class WorldTimeResponse { public long unixtime; public int raw_offset; public int dst_offset; }

    bool haveApiOffset;
    int apiOffsetSeconds;
    double deviceClockCorrection; // seconds to add to the device's UTC clock

    void Start()
    {
        StartCoroutine(SyncLoop());
        StartCoroutine(TickLoop());
    }

    IEnumerator SyncLoop()
    {
        while (true)
        {
            yield return Sync();
            yield return new WaitForSecondsRealtime(resyncSeconds);
        }
    }

    IEnumerator Sync()
    {
        using (UnityWebRequest req = UnityWebRequest.Get("https://worldtimeapi.org/api/timezone/" + ianaTimezone))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[{placeName}] worldtimeapi failed ({req.error}), using fallback offset");
                yield break;
            }
            try
            {
                var data = JsonUtility.FromJson<WorldTimeResponse>(req.downloadHandler.text);
                apiOffsetSeconds = data.raw_offset + data.dst_offset;
                haveApiOffset = true;
                double deviceUnix = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                double diff = data.unixtime - deviceUnix;
                // only correct the device clock if it is clearly wrong (network latency is ~1 s)
                deviceClockCorrection = Math.Abs(diff) > 5 ? diff : 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{placeName}] could not parse worldtimeapi: {e.Message}");
            }
        }
    }

    IEnumerator TickLoop()
    {
        var wait = new WaitForSecondsRealtime(0.25f);
        while (true)
        {
            Render();
            yield return wait;
        }
    }

    void Render()
    {
        if (text == null) return;

        int offset;
        if (haveApiOffset) offset = apiOffsetSeconds;
        else if (weatherFallback != null && weatherFallback.HasTimezoneOffset) offset = weatherFallback.TimezoneOffsetSeconds;
        else offset = Mathf.RoundToInt(fallbackUtcOffsetHours * 3600f);

        DateTime local = DateTime.UtcNow.AddSeconds(deviceClockCorrection + offset);
        var inv = CultureInfo.InvariantCulture;

        string time = style == ClockStyle.TwelveHour
            ? local.ToString("h:mm", inv) + "<size=55%>:" + local.ToString("ss", inv) + " " + local.ToString("tt", inv) + "</size>"
            : local.ToString("HH:mm", inv) + "<size=55%>:" + local.ToString("ss", inv) + "</size>";

        text.text = $"<size=55%>Local time in {placeName}</size>\n<b>{time}</b>\n<size=50%>{local.ToString("ddd d MMM yyyy", inv)}</size>";
    }
}
