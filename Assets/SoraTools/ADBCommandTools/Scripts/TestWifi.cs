using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestWifi : MonoBehaviour
{
    List<WifiProfile> profiles = new List<WifiProfile>();

    void Start()
    {
        profiles = WifiPasswordReader.GetSavedWifiProfiles();
    }

    [ContextMenu("FindWifi1")]
    public void FindWifi1()
    {
        profiles = WifiPasswordReader.GetSavedWifiProfiles();
        var profile = profiles.Find(profile => profile.Name == "SZ_YOMOV_5G");
        Debug.Log(profile.Password);
    }

    [ContextMenu("FindWifi2")]
    public void FindWifi2()
    {
        profiles = WifiPasswordReader.GetSavedWifiProfiles();
        var profile = profiles.Find(profile => profile.Name == "YOMOV-Test-5G");
        Debug.Log(profile.Password);
    }
}
