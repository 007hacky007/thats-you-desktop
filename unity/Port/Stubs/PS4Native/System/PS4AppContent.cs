// Host stand-in for the PS4 app-content plugin ("PS4AppContent").
//
// On PS4 the game mounts a per-title temporary-data volume and writes player
// photos, drawings and answers there for the session. Here the volume is a
// folder under Application.temporaryCachePath. The public surface (struct,
// enum, constant and static methods) keeps the names and signatures the game
// calls.
//
// The native mount-point struct only holds 16 characters, too short for a
// host path, so the struct receives a short volume label and
// GetTemporaryDataMountPath maps that label back to the host folder.

using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

public class PS4AppContent
{
    public struct SceAppContentMountPoint
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)]
        public string data;
    }

    public enum SceAppContentTemporaryDataOption
    {
        SCE_APP_CONTENT_TEMPORARY_DATA_OPTION_NONE = 0,
        SCE_APP_CONTENT_TEMPORARY_DATA_OPTION_FORMAT = 1
    }

    public const int SCE_APP_CONTENT_MOUNTPOINT_DATA_MAXSIZE = 16;

    // Generic SDK-style failure code returned when the host folder is unusable.
    private const int ErrorFailed = unchecked((int)0x80D90001);

    private const string VolumeLabel = "/temp0";

    // Reported when the free space of the host volume cannot be read.
    private const uint FallbackAvailableKb = 1024u * 1024u;

    private static string s_hostFolder;

    private static bool s_mounted;

    private static string HostFolder
    {
        get
        {
            if (s_hostFolder == null)
            {
                s_hostFolder = Path.Combine(Application.temporaryCachePath, "PS4TempData");
            }
            return s_hostFolder;
        }
    }

    public static int TemporaryDataFormat(IntPtr mountPoint)
    {
        try
        {
            ClearFolder(HostFolder);
            return 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Port] PS4AppContent: format of temp data failed: " + e.Message);
            return ErrorFailed;
        }
    }

    public static int TemporaryDataGetAvailableSpaceKb(IntPtr mountPoint, ref uint availableSpaceKb)
    {
        availableSpaceKb = QueryFreeSpaceKb(HostFolder);
        return 0;
    }

    public static int TemporaryDataMount2(uint opt, IntPtr mountPoint)
    {
        if (mountPoint == IntPtr.Zero)
        {
            return ErrorFailed;
        }
        try
        {
            string folder = HostFolder;
            Directory.CreateDirectory(folder);
            if (opt == (uint)SceAppContentTemporaryDataOption.SCE_APP_CONTENT_TEMPORARY_DATA_OPTION_FORMAT)
            {
                ClearFolder(folder);
            }
            SceAppContentMountPoint point = new SceAppContentMountPoint();
            point.data = VolumeLabel;
            Marshal.StructureToPtr(point, mountPoint, false);
            s_mounted = true;
            return 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Port] PS4AppContent: temp data mount failed: " + e.Message);
            s_mounted = false;
            return ErrorFailed;
        }
    }

    public static int TemporaryDataUnmount(IntPtr mountPoint)
    {
        s_mounted = false;
        return 0;
    }

    public static string GetTemporaryDataMountPath(IntPtr mountPointPtr)
    {
        if (mountPointPtr == IntPtr.Zero)
        {
            return HostFolder;
        }
        string label = ((SceAppContentMountPoint)Marshal.PtrToStructure(mountPointPtr, typeof(SceAppContentMountPoint))).data;
        if (label == VolumeLabel || string.IsNullOrEmpty(label))
        {
            if (!s_mounted)
            {
                Debug.LogWarning("[Port] PS4AppContent: temp data path requested while not mounted");
            }
            return HostFolder;
        }
        return label;
    }

    private static void ClearFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            return;
        }
        foreach (string file in Directory.GetFiles(folder))
        {
            File.Delete(file);
        }
        foreach (string dir in Directory.GetDirectories(folder))
        {
            Directory.Delete(dir, true);
        }
    }

    private static uint QueryFreeSpaceKb(string folder)
    {
        try
        {
            string full = Path.GetFullPath(folder);
            DriveInfo best = null;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                string root;
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }
                    root = drive.RootDirectory.FullName;
                }
                catch (Exception)
                {
                    continue;
                }
                if (full.StartsWith(root, StringComparison.Ordinal)
                    && (best == null || root.Length > best.RootDirectory.FullName.Length))
                {
                    best = drive;
                }
            }
            if (best != null)
            {
                long kb = best.AvailableFreeSpace / 1024L;
                if (kb < 0L)
                {
                    return 0u;
                }
                return kb > uint.MaxValue ? uint.MaxValue : (uint)kb;
            }
        }
        catch (Exception)
        {
        }
        return FallbackAvailableKb;
    }
}
