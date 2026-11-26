// Host stand-in for the PS4 "SystemCache" plugin component.
//
// The PS4 plugin mounted a title cache volume and offered small file helpers
// on it. No shipped scene or script uses this component, but its type and
// GUID are kept so any reference still resolves. On a host the cache is a
// folder under Application.temporaryCachePath and mounting completes at once.
// File helpers log and swallow errors instead of throwing.

using System;
using System.IO;
using UnityEngine;

public class SystemCache : DLLLoaderBase
{
    public enum CacheState
    {
        kCache_Unmounted = 0,
        kCache_Mounting = 1,
        kCache_Mounted = 2,
        kCache_Errored = 3
    }

    internal const bool clearCacheOnStartup = true;

    internal static SystemCache s;

    private static CacheState s_state = CacheState.kCache_Unmounted;

    private static string s_cachePath;

    private static string CacheFolder
    {
        get
        {
            if (s_cachePath == null)
            {
                s_cachePath = Path.Combine(Application.temporaryCachePath, "PS4SystemCache");
            }
            return s_cachePath;
        }
    }

    public string path
    {
        get { return CacheFolder; }
    }

    public CacheState state
    {
        get { return s_state; }
    }

    public string streamingAssets
    {
        get { return Application.streamingAssetsPath; }
    }

    protected override bool LoadInterface()
    {
        return true;
    }

    protected override bool UnLoadInterface()
    {
        return true;
    }

    public void Initialise()
    {
        Mount(clearCacheOnStartup);
    }

    public void OnDestroy()
    {
    }

    public void ClearSystemCache()
    {
        try
        {
            if (Directory.Exists(CacheFolder))
            {
                Directory.Delete(CacheFolder, true);
            }
            Directory.CreateDirectory(CacheFolder);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Port] SystemCache: clear failed: " + e.Message);
        }
    }

    public void BlockingStartSystemCache()
    {
        Mount(clearCacheOnStartup);
    }

    public void MakeDirectory(string target)
    {
        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Port] SystemCache: make directory failed for " + target + ": " + e.Message);
        }
    }

    public void CopySingleFile(string source, string destination)
    {
        try
        {
            string dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.Copy(source, destination, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Port] SystemCache: copy failed " + source + " -> " + destination + ": " + e.Message);
        }
    }

    private void Awake()
    {
        s = this;
    }

    private void Mount(bool clear)
    {
        try
        {
            if (clear)
            {
                ClearSystemCache();
            }
            else
            {
                Directory.CreateDirectory(CacheFolder);
            }
            s_state = CacheState.kCache_Mounted;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Port] SystemCache: mount failed: " + e.Message);
            s_state = CacheState.kCache_Errored;
        }
    }
}
