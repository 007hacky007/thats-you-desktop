// Host stand-in for the "UnityPlugin" native plugin component.
//
// The PS4 build bundled a generic native plugin with attach, update and
// detach hooks. Nothing in the game calls them; they are kept as no-ops with
// the original names and signatures, and the component keeps its GUID.

using System;

public class PluginDLLInterface : DLLLoaderBase
{
    public static void PluginAttach()
    {
    }

    // The native side returned a pointer to its per-frame data; there is none.
    public static IntPtr PluginUpdate()
    {
        return IntPtr.Zero;
    }

    public static void PluginDetach()
    {
    }

    protected override bool LoadInterface()
    {
        return true;
    }

    protected override bool UnLoadInterface()
    {
        return true;
    }

    private void Start()
    {
    }

    private void OnDestroy()
    {
    }
}
