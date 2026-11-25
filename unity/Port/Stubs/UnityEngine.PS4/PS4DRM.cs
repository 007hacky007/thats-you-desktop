// PC stand-in for the PS4-only UnityEngine.PS4.PS4DRM class.
//
// On PS4 this class wrapped the system DRM library used to find and mount
// installed additional content (DLC packages). Desktop Unity has no such
// API. Only the members the game uses are declared here, with names and
// signatures matching the PS4 API (static bool methods, a value-type finder
// with a public int serviceLabel field and a read-only entitlementLabel).
//
// PC behaviour: no PS4 additional content is ever installed, so the finder
// never finds anything, content cannot be opened and has no mount point.
// Callers then take their existing "no DLC" path.

namespace UnityEngine.PS4
{
    public sealed class PS4DRM
    {
        public struct DrmContentFinder
        {
            public int serviceLabel;

            private string _entitlementLabel;

            // Always null on PC because the finder never yields an entry.
            public string entitlementLabel
            {
                get { return _entitlementLabel; }
            }
        }

        // No installed content to enumerate: always reports "nothing found".
        public static bool ContentFinderOpen(ref DrmContentFinder finder)
        {
            return false;
        }

        public static bool ContentFinderNext(ref DrmContentFinder finder)
        {
            return false;
        }

        // Nothing was opened, so there is nothing to release.
        public static bool ContentFinderClose(ref DrmContentFinder finder)
        {
            return true;
        }

        public static bool ContentOpen(string entitlementLabel)
        {
            return false;
        }

        public static bool ContentClose(string entitlementLabel)
        {
            return true;
        }

        // No content is mounted on PC.
        public static bool ContentGetMountPoint(string entitlementLabel, out string mountPoint)
        {
            mountPoint = string.Empty;
            return false;
        }
    }
}
