// Host stand-in for the PS4 "PS4NpTrophy" native plugin.
//
// PS4 trophies do not exist on the host. Every call succeeds and does
// nothing; awarded trophy ids are written to the Unity log for reference.
//
// Member names match the PS4 plugin.

using UnityEngine;

public class PS4Trophies
{
    public static int RegisterTrophyPack(bool cacheIcons)
    {
        return 0;
    }

    public static int AwardTrophy(int trophyId)
    {
        Debug.Log("[PS4Trophies stub] trophy " + trophyId + " would be awarded");
        return 0;
    }

    public static int Initialise()
    {
        return 0;
    }

    public static int Uninitialise()
    {
        return 0;
    }
}
