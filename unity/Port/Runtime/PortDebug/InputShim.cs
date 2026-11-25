// Global-namespace stand-in for UnityEngine.Input, used for scripted input (-portKeys).
//
// The game polls the legacy input manager directly (MultiControllerInput, Menu, NGUI's
// UICamera key delegates, the uGUI PS4InputModule, PlayMaker actions), and Unity offers no way
// to feed synthetic events into UnityEngine.Input. C# name lookup prefers a type in the global
// namespace of the compiling assembly over one imported with `using UnityEngine;`, so every
// unqualified `Input.X` in Assembly-CSharp binds to this class instead, without touching game
// code. Each member forwards to UnityEngine.Input and, for keys, buttons and axes, merges in
// what PortDebug.PortInput is injecting. With nothing injected the behaviour is unchanged.

using UnityEngine;

public static class Input
{
    // keys -------------------------------------------------------------------------------
    public static bool GetKey(KeyCode key) { return UnityEngine.Input.GetKey(key) || PortDebug.PortInput.Key(key); }
    public static bool GetKeyDown(KeyCode key) { return UnityEngine.Input.GetKeyDown(key) || PortDebug.PortInput.KeyDown(key); }
    public static bool GetKeyUp(KeyCode key) { return UnityEngine.Input.GetKeyUp(key) || PortDebug.PortInput.KeyUp(key); }

    public static bool GetKey(string name) { return UnityEngine.Input.GetKey(name) || PortDebug.PortInput.Key(PortDebug.PortInput.KeyFromName(name)); }
    public static bool GetKeyDown(string name) { return UnityEngine.Input.GetKeyDown(name) || PortDebug.PortInput.KeyDown(PortDebug.PortInput.KeyFromName(name)); }
    public static bool GetKeyUp(string name) { return UnityEngine.Input.GetKeyUp(name) || PortDebug.PortInput.KeyUp(PortDebug.PortInput.KeyFromName(name)); }

    public static bool anyKey { get { return UnityEngine.Input.anyKey || PortDebug.PortInput.AnyKey; } }
    public static bool anyKeyDown { get { return UnityEngine.Input.anyKeyDown || PortDebug.PortInput.AnyKeyDown; } }
    public static string inputString { get { return UnityEngine.Input.inputString; } }

    // virtual buttons and axes -----------------------------------------------------------
    public static bool GetButton(string name) { return UnityEngine.Input.GetButton(name) || PortDebug.PortInput.Button(name); }
    public static bool GetButtonDown(string name) { return UnityEngine.Input.GetButtonDown(name) || PortDebug.PortInput.ButtonDown(name); }
    public static bool GetButtonUp(string name) { return UnityEngine.Input.GetButtonUp(name) || PortDebug.PortInput.ButtonUp(name); }

    public static float GetAxis(string name)
    {
        float v;
        return PortDebug.PortInput.Axis(name, out v) ? v : UnityEngine.Input.GetAxis(name);
    }

    public static float GetAxisRaw(string name)
    {
        float v;
        return PortDebug.PortInput.Axis(name, out v) ? v : UnityEngine.Input.GetAxisRaw(name);
    }

    public static void ResetInputAxes() { UnityEngine.Input.ResetInputAxes(); }
    public static string[] GetJoystickNames() { return UnityEngine.Input.GetJoystickNames(); }

    // mouse --------------------------------------------------------------------------------
    public static bool GetMouseButton(int button) { return UnityEngine.Input.GetMouseButton(button); }
    public static bool GetMouseButtonDown(int button) { return UnityEngine.Input.GetMouseButtonDown(button); }
    public static bool GetMouseButtonUp(int button) { return UnityEngine.Input.GetMouseButtonUp(button); }
    public static Vector3 mousePosition { get { return UnityEngine.Input.mousePosition; } }
    public static Vector2 mouseScrollDelta { get { return UnityEngine.Input.mouseScrollDelta; } }
    public static bool mousePresent { get { return UnityEngine.Input.mousePresent; } }
    public static bool simulateMouseWithTouches
    {
        get { return UnityEngine.Input.simulateMouseWithTouches; }
        set { UnityEngine.Input.simulateMouseWithTouches = value; }
    }

    // touch --------------------------------------------------------------------------------
    public static Touch GetTouch(int index) { return UnityEngine.Input.GetTouch(index); }
    public static int touchCount { get { return UnityEngine.Input.touchCount; } }
    public static Touch[] touches { get { return UnityEngine.Input.touches; } }
    public static bool touchSupported { get { return UnityEngine.Input.touchSupported; } }
    public static bool touchPressureSupported { get { return UnityEngine.Input.touchPressureSupported; } }
    public static bool stylusTouchSupported { get { return UnityEngine.Input.stylusTouchSupported; } }
    public static bool multiTouchEnabled
    {
        get { return UnityEngine.Input.multiTouchEnabled; }
        set { UnityEngine.Input.multiTouchEnabled = value; }
    }

    // IME ----------------------------------------------------------------------------------
    public static IMECompositionMode imeCompositionMode
    {
        get { return UnityEngine.Input.imeCompositionMode; }
        set { UnityEngine.Input.imeCompositionMode = value; }
    }
    public static string compositionString { get { return UnityEngine.Input.compositionString; } }
    public static bool imeIsSelected { get { return UnityEngine.Input.imeIsSelected; } }
    public static Vector2 compositionCursorPos
    {
        get { return UnityEngine.Input.compositionCursorPos; }
        set { UnityEngine.Input.compositionCursorPos = value; }
    }

    // sensors ------------------------------------------------------------------------------
    public static Vector3 acceleration { get { return UnityEngine.Input.acceleration; } }
    public static int accelerationEventCount { get { return UnityEngine.Input.accelerationEventCount; } }
    public static AccelerationEvent[] accelerationEvents { get { return UnityEngine.Input.accelerationEvents; } }
    public static AccelerationEvent GetAccelerationEvent(int index) { return UnityEngine.Input.GetAccelerationEvent(index); }
    public static DeviceOrientation deviceOrientation { get { return UnityEngine.Input.deviceOrientation; } }
    public static bool compensateSensors
    {
        get { return UnityEngine.Input.compensateSensors; }
        set { UnityEngine.Input.compensateSensors = value; }
    }
    public static Gyroscope gyro { get { return UnityEngine.Input.gyro; } }
    public static Compass compass { get { return UnityEngine.Input.compass; } }
    public static LocationService location { get { return UnityEngine.Input.location; } }
    public static bool backButtonLeavesApp
    {
        get { return UnityEngine.Input.backButtonLeavesApp; }
        set { UnityEngine.Input.backButtonLeavesApp = value; }
    }
}
