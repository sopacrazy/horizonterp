using UnityEditor;
using UnityEngine;
public static class RealtimePackageSetup
{
    public static void Verify()
    {
        Debug.Log("REALTIME_PACKAGES_RESOLVED");
        foreach(var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            if(package.name=="com.unity.transport"||package.name=="com.unity.services.vivox")
                Debug.Log("REALTIME_PACKAGE "+package.name+" "+package.version);
    }
}
