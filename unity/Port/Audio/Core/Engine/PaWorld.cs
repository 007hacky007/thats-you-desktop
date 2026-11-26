// Game syncs and game objects of the managed audio engine (IPaWorld).
//
// States are global. Switches and RTPCs exist per game object and globally; a lookup takes the
// object's value, then the global one, then the game parameter default from the loaded Init
// bank, then 0. RTPC changes can ramp over time with one of the Wwise interpolation curves, and
// game parameters with a slew rate or a filter over time follow their target gradually.
//
// The engine writes here under its lock; the mixer and the music player read on the main
// thread from inside PaEngine.Tick, which holds the same lock.

using System;
using System.Collections.Generic;

namespace PortAudio
{
    public sealed class PaWorld : IPaWorld
    {
        sealed class Rtpc
        {
            public float Value;
            public bool Ramping;
            public float From, To;
            public double Start, Duration;
            public PaCurve Curve;
            public bool Slewing;          // following Target through the game parameter's ramp type
            public float Target;
            public bool RemoveWhenDone;   // a reset of an object value: drop it once the ramp ends
        }

        sealed class GameObj
        {
            public string Name;
            public uint ListenerMask = 1;
            public PaVec3 Position;
            public PaVec3 Front = new PaVec3(0f, 0f, 1f);
            public float OutputBusVolume = 1f;
            public float AttenuationScaling = 1f;
        }

        readonly Dictionary<uint, uint> states = new Dictionary<uint, uint>();
        readonly Dictionary<uint, uint> globalSwitches = new Dictionary<uint, uint>();
        readonly Dictionary<ulong, Dictionary<uint, uint>> objectSwitches = new Dictionary<ulong, Dictionary<uint, uint>>();
        readonly Dictionary<uint, Rtpc> globalRtpcs = new Dictionary<uint, Rtpc>();
        readonly Dictionary<ulong, Dictionary<uint, Rtpc>> objectRtpcs = new Dictionary<ulong, Dictionary<uint, Rtpc>>();
        readonly Dictionary<ulong, GameObj> objects = new Dictionary<ulong, GameObj>();
        readonly List<KeyValuePair<ulong, uint>> removeScratch = new List<KeyValuePair<ulong, uint>>();
        PaListenerPose listener;
        double lastAdvance = double.NaN;

        public PaWorld(PaBankStore banks, PaMediaIndex media, uint seed)
        {
            Banks = banks;
            Media = media;
            Random = new PaRandom(seed);
            listener.Front = new PaVec3(0f, 0f, 1f);
            listener.Top = new PaVec3(0f, 1f, 0f);
            VoiceLanguageKey = "";
        }

        // ---- IPaWorld ----

        public PaBankStore Banks { get; private set; }
        public PaMediaIndex Media { get; private set; }
        public PaRandom Random { get; private set; }
        public bool BackgroundMusicMuted { get; set; }
        public string VoiceLanguageKey { get; set; }

        public PaListenerPose Listener
        {
            get { return listener; }
        }

        public float GetRtpc(uint rtpcId, ulong gameObject)
        {
            Rtpc r;
            Dictionary<uint, Rtpc> map;
            if (!PaIds.IsGlobal(gameObject) && objectRtpcs.TryGetValue(gameObject, out map) && map.TryGetValue(rtpcId, out r))
                return r.Value;
            if (globalRtpcs.TryGetValue(rtpcId, out r))
                return r.Value;
            return DefaultOf(rtpcId);
        }

        public uint GetState(uint stateGroup)
        {
            uint v;
            return states.TryGetValue(stateGroup, out v) ? v : 0u;
        }

        public uint GetSwitch(uint switchGroup, ulong gameObject)
        {
            uint v;
            Dictionary<uint, uint> map;
            if (!PaIds.IsGlobal(gameObject) && objectSwitches.TryGetValue(gameObject, out map) && map.TryGetValue(switchGroup, out v))
                return v;
            return globalSwitches.TryGetValue(switchGroup, out v) ? v : 0u;
        }

        public PaVec3 GetPosition(ulong gameObject)
        {
            GameObj o;
            return objects.TryGetValue(gameObject, out o) ? o.Position : default(PaVec3);
        }

        public PaVec3 GetFront(ulong gameObject)
        {
            GameObj o;
            return objects.TryGetValue(gameObject, out o) ? o.Front : new PaVec3(0f, 0f, 1f);
        }

        public uint GetListenerMask(ulong gameObject)
        {
            GameObj o;
            return objects.TryGetValue(gameObject, out o) ? o.ListenerMask : 1u;
        }

        public float GetOutputBusVolume(ulong gameObject)
        {
            GameObj o;
            return objects.TryGetValue(gameObject, out o) ? o.OutputBusVolume : 1f;
        }

        public float GetAttenuationScaling(ulong gameObject)
        {
            GameObj o;
            return objects.TryGetValue(gameObject, out o) ? o.AttenuationScaling : 1f;
        }

        public void SetMeterRtpc(uint rtpcId, float value)
        {
            Rtpc r = Slot(rtpcId, PaIds.GlobalObject, true);
            r.Ramping = false;
            r.Slewing = false;
            r.Value = value;
        }

        // ---- engine side ----

        public float DefaultOf(uint rtpcId)
        {
            PaGameParameter gp = Banks != null ? Banks.GameParameter(rtpcId) : null;
            return gp != null ? gp.Default : 0f;
        }

        public bool HasObjectValue(uint rtpcId, ulong gameObject)
        {
            Dictionary<uint, Rtpc> map;
            return !PaIds.IsGlobal(gameObject) && objectRtpcs.TryGetValue(gameObject, out map) && map.ContainsKey(rtpcId);
        }

        public bool HasGlobalValue(uint rtpcId)
        {
            return globalRtpcs.ContainsKey(rtpcId);
        }

        public void SetState(uint group, uint state)
        {
            states[group] = state;
        }

        public void SetSwitch(uint group, uint value, ulong gameObject)
        {
            if (PaIds.IsGlobal(gameObject))
            {
                globalSwitches[group] = value;
                return;
            }
            Dictionary<uint, uint> map;
            if (!objectSwitches.TryGetValue(gameObject, out map))
                objectSwitches[gameObject] = map = new Dictionary<uint, uint>();
            map[group] = value;
        }

        // Sets an RTPC on an object (or globally) at time `now` (voice clock seconds). With a
        // duration the value ramps from its current value along `curve`. Without one, a game
        // parameter with a ramp type follows the new value gradually unless `bypass` is set.
        // A global write also resets every object's own value, as in Wwise.
        public void SetRtpc(uint rtpcId, float value, ulong gameObject, double now, int durationMs, PaCurve curve, bool bypass)
        {
            float current = GetRtpc(rtpcId, gameObject);
            if (PaIds.IsGlobal(gameObject))
                foreach (Dictionary<uint, Rtpc> map in objectRtpcs.Values)
                    map.Remove(rtpcId);
            Rtpc r = Slot(rtpcId, gameObject, true);
            r.RemoveWhenDone = false;
            StartChange(r, rtpcId, current, value, now, durationMs, curve, bypass);
        }

        // Ramps an object (or global) value back to the game parameter default and then drops
        // the override, so later global changes reach the object again.
        public void ResetRtpc(uint rtpcId, ulong gameObject, double now, int durationMs, PaCurve curve, bool bypass)
        {
            float target = PaIds.IsGlobal(gameObject) ? DefaultOf(rtpcId) : GlobalOrDefault(rtpcId);
            Rtpc r = Slot(rtpcId, gameObject, false);
            if (r == null)
                return;
            float current = r.Value;
            StartChange(r, rtpcId, current, target, now, durationMs, curve, bypass);
            r.RemoveWhenDone = true;
            if (!r.Ramping && !r.Slewing)
                Remove(rtpcId, gameObject);
        }

        float GlobalOrDefault(uint rtpcId)
        {
            Rtpc r;
            return globalRtpcs.TryGetValue(rtpcId, out r) ? r.Value : DefaultOf(rtpcId);
        }

        void StartChange(Rtpc r, uint rtpcId, float current, float value, double now, int durationMs, PaCurve curve, bool bypass)
        {
            r.Slewing = false;
            r.Ramping = false;
            if (durationMs > 0 && !bypass)
            {
                r.Value = current;
                r.From = current;
                r.To = value;
                r.Start = now;
                r.Duration = durationMs / 1000.0;
                r.Curve = curve;
                r.Ramping = true;
                return;
            }
            PaGameParameter gp = Banks != null ? Banks.GameParameter(rtpcId) : null;
            if (!bypass && gp != null && IsGradual(gp) && current != value)
            {
                r.Value = current;
                r.Target = value;
                r.Slewing = true;
                return;
            }
            r.Value = value;
        }

        static bool IsGradual(PaGameParameter gp)
        {
            string t = gp.RampType;
            if (string.Equals(t, "slewRate", StringComparison.OrdinalIgnoreCase))
                return gp.RampUp > 0f || gp.RampDown > 0f;
            if (string.Equals(t, "filteringOverTime", StringComparison.OrdinalIgnoreCase))
                return gp.RampUp > 0f || gp.RampDown > 0f;
            return false;
        }

        // Advances ramps and slews to `now` (voice clock seconds).
        public void Advance(double now)
        {
            double dt = double.IsNaN(lastAdvance) ? 0.0 : Math.Max(0.0, now - lastAdvance);
            lastAdvance = now;
            removeScratch.Clear();
            foreach (KeyValuePair<uint, Rtpc> kv in globalRtpcs)
                if (Step(kv.Value, kv.Key, now, dt))
                    removeScratch.Add(new KeyValuePair<ulong, uint>(PaIds.GlobalObject, kv.Key));
            foreach (KeyValuePair<ulong, Dictionary<uint, Rtpc>> obj in objectRtpcs)
                foreach (KeyValuePair<uint, Rtpc> kv in obj.Value)
                    if (Step(kv.Value, kv.Key, now, dt))
                        removeScratch.Add(new KeyValuePair<ulong, uint>(obj.Key, kv.Key));
            foreach (KeyValuePair<ulong, uint> kv in removeScratch)
                Remove(kv.Value, kv.Key);
        }

        // Returns true when the value should be dropped (a finished reset).
        bool Step(Rtpc r, uint rtpcId, double now, double dt)
        {
            if (r.Ramping)
            {
                double t = r.Duration <= 0 ? 1.0 : (now - r.Start) / r.Duration;
                if (t >= 1.0)
                {
                    r.Value = r.To;
                    r.Ramping = false;
                }
                else
                {
                    r.Value = (float)(r.From + (r.To - r.From) * PaCurves.Shape(r.Curve, t));
                }
            }
            else if (r.Slewing)
            {
                PaGameParameter gp = Banks != null ? Banks.GameParameter(rtpcId) : null;
                if (gp == null)
                {
                    r.Value = r.Target;
                    r.Slewing = false;
                }
                else
                {
                    bool up = r.Target > r.Value;
                    float rate = up ? gp.RampUp : gp.RampDown;
                    if (string.Equals(gp.RampType, "slewRate", StringComparison.OrdinalIgnoreCase))
                    {
                        // RampUp/RampDown: units per second.
                        float step = rate > 0f ? (float)(rate * dt) : float.MaxValue;
                        if (Math.Abs(r.Target - r.Value) <= step)
                            r.Value = r.Target;
                        else
                            r.Value += up ? step : -step;
                    }
                    else
                    {
                        // filteringOverTime: first-order low pass, RampUp/RampDown is the time
                        // constant in milliseconds.
                        double tau = rate / 1000.0;
                        double k = tau > 0 ? 1.0 - Math.Exp(-dt / tau) : 1.0;
                        r.Value = (float)(r.Value + (r.Target - r.Value) * k);
                        if (Math.Abs(r.Target - r.Value) < 1e-4f)
                            r.Value = r.Target;
                    }
                    if (r.Value == r.Target)
                        r.Slewing = false;
                }
            }
            return r.RemoveWhenDone && !r.Ramping && !r.Slewing;
        }

        Rtpc Slot(uint rtpcId, ulong gameObject, bool create)
        {
            Rtpc r;
            if (PaIds.IsGlobal(gameObject))
            {
                if (!globalRtpcs.TryGetValue(rtpcId, out r) && create)
                    globalRtpcs[rtpcId] = r = new Rtpc { Value = DefaultOf(rtpcId) };
                return r;
            }
            Dictionary<uint, Rtpc> map;
            if (!objectRtpcs.TryGetValue(gameObject, out map))
            {
                if (!create)
                    return null;
                objectRtpcs[gameObject] = map = new Dictionary<uint, Rtpc>();
            }
            if (!map.TryGetValue(rtpcId, out r) && create)
                map[rtpcId] = r = new Rtpc { Value = GlobalOrDefault(rtpcId) };
            return r;
        }

        void Remove(uint rtpcId, ulong gameObject)
        {
            if (PaIds.IsGlobal(gameObject))
            {
                globalRtpcs.Remove(rtpcId);
                return;
            }
            Dictionary<uint, Rtpc> map;
            if (objectRtpcs.TryGetValue(gameObject, out map))
            {
                map.Remove(rtpcId);
                if (map.Count == 0)
                    objectRtpcs.Remove(gameObject);
            }
        }

        // ---- objects and listener ----

        public bool IsRegistered(ulong gameObject)
        {
            return objects.ContainsKey(gameObject);
        }

        public int ObjectCount
        {
            get { return objects.Count; }
        }

        public void Register(ulong gameObject, string name, uint listenerMask)
        {
            GameObj o = Obj(gameObject);
            if (name != null)
                o.Name = name;
            o.ListenerMask = listenerMask;
        }

        // Called for every posting object: unregistered objects are created on first use.
        public void Touch(ulong gameObject)
        {
            if (!PaIds.IsGlobal(gameObject))
                Obj(gameObject);
        }

        public string NameOfObject(ulong gameObject)
        {
            GameObj o;
            return objects.TryGetValue(gameObject, out o) ? o.Name : null;
        }

        // Drops the object and every per-object switch and RTPC value.
        public void Unregister(ulong gameObject)
        {
            objects.Remove(gameObject);
            objectSwitches.Remove(gameObject);
            objectRtpcs.Remove(gameObject);
        }

        public void UnregisterAll()
        {
            objects.Clear();
            objectSwitches.Clear();
            objectRtpcs.Clear();
        }

        public void SetPosition(ulong gameObject, PaVec3 position, PaVec3 front)
        {
            GameObj o = Obj(gameObject);
            o.Position = position;
            o.Front = front;
        }

        public void SetListenerMask(ulong gameObject, uint mask)
        {
            Obj(gameObject).ListenerMask = mask;
        }

        public void SetOutputBusVolume(ulong gameObject, float linear)
        {
            Obj(gameObject).OutputBusVolume = linear;
        }

        public void SetAttenuationScaling(ulong gameObject, float factor)
        {
            Obj(gameObject).AttenuationScaling = factor;
        }

        public void SetListener(PaVec3 position, PaVec3 front, PaVec3 top)
        {
            listener.Position = position;
            listener.Front = front;
            listener.Top = top;
        }

        GameObj Obj(ulong gameObject)
        {
            GameObj o;
            if (!objects.TryGetValue(gameObject, out o))
                objects[gameObject] = o = new GameObj();
            return o;
        }
    }
}
