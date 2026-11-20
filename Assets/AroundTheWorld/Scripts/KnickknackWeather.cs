#pragma warning disable 0649 // fields are filled by JsonUtility
using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

// Downloads the current weather for a location from OpenWeather and shows it on one side of the cube.
// Refreshes every `refreshSeconds` (default 10 minutes).
public class KnickknackWeather : MonoBehaviour
{
    public enum TemperatureUnit { Fahrenheit, Celsius }

    public TextMeshPro text;
    public string placeName = "Paris";
    public double latitude = 48.8584;
    public double longitude = 2.2945;
    public TemperatureUnit unit = TemperatureUnit.Fahrenheit;
    public float refreshSeconds = 600f;

    // UTC offset (seconds) reported by OpenWeather; used by KnickknackClock as a fallback.
    public bool HasTimezoneOffset { get; private set; }
    public int TimezoneOffsetSeconds { get; private set; }

    [Serializable] class WeatherEntry { public string main; public string description; }
    [Serializable] class MainEntry { public float temp; public float feels_like; public int humidity; }
    [Serializable] class WeatherResponse { public WeatherEntry[] weather; public MainEntry main; public int timezone; public int cod; }

    void Start()
    {
        SetText("Weather\n<size=70%>loading...</size>");
        StartCoroutine(RefreshLoop());
    }

    IEnumerator RefreshLoop()
    {
        while (true)
        {
            yield return Fetch();
            yield return new WaitForSecondsRealtime(refreshSeconds);
        }
    }

    IEnumerator Fetch()
    {
        if (string.IsNullOrEmpty(ApiKeys.OpenWeather))
        {
            SetText("Weather\n<size=60%>Add your OpenWeather key in ApiKeys.cs</size>");
            yield break;
        }

        string units = unit == TemperatureUnit.Fahrenheit ? "imperial" : "metric";
        string url = string.Format(CultureInfo.InvariantCulture,
            "https://api.openweathermap.org/data/2.5/weather?lat={0}&lon={1}&units={2}&appid={3}",
            latitude, longitude, units, ApiKeys.OpenWeather);

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 15;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[{placeName}] weather request failed: {req.error} {req.downloadHandler?.text}");
                // keep the last good value on screen; only show the error if we never got one
                if (!hasData) SetText("Weather\n<size=60%>unavailable\n(retrying)</size>");
                yield break;
            }

            WeatherResponse data = null;
            try { data = JsonUtility.FromJson<WeatherResponse>(req.downloadHandler.text); }
            catch (Exception e) { Debug.LogWarning($"[{placeName}] could not parse weather: {e.Message}"); }

            if (data == null || data.main == null)
            {
                if (!hasData) SetText("Weather\n<size=60%>unavailable</size>");
                yield break;
            }

            hasData = true;
            TimezoneOffsetSeconds = data.timezone;
            HasTimezoneOffset = true;

            string symbol = unit == TemperatureUnit.Fahrenheit ? "°F" : "°C";
            string desc = (data.weather != null && data.weather.Length > 0) ? Capitalize(data.weather[0].description) : "";
            int temp = Mathf.RoundToInt(data.main.temp);

            SetText($"<size=55%>Weather in {placeName}</size>\n<b>{temp}{symbol}</b>\n<size=60%>{desc}\nHumidity {data.main.humidity}%</size>");
        }
    }

    bool hasData;

    void SetText(string s)
    {
        if (text != null) text.text = s;
    }

    static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
