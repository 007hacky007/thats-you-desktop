// Bank loading for the managed audio engine.
//
// Banks are the converted JSON files listed in manifest.json (PaBankStore reads and registers
// them). Loading is reference counted, as in Wwise: Init.bnk is loaded twice by the game.
//   sync   LoadBank:      read and register on the calling thread, embedded media preloads start
//                         on the next tick
//   async  LoadBankAsync: read on a pool thread, register on a tick, wait until the bank's
//                         embedded media is ready (at most 5 s, the game gives up after 8 s),
//                         then AK_Bank
//   unload:               release; voices whose nodes went away stop with a short fade
// Async requests (loads, unloads, prepare requests) complete strictly in request order, like
// the single bank thread of Wwise.
// A bank that has no JSON file is registered as an empty bank and reported as loaded: the game
// retries a failed bank load forever, so a missing file must not fail.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PortAudio
{
    internal sealed class PaBankOp
    {
        public enum Kind { Load, Unload, Prepare }

        public Kind Op;
        public string Name;
        public uint BankId;
        public IntPtr Cookie;
        public PaBankInfo Info;
        public string LanguageKey;
        public volatile bool ReadDone;
        public PaBank Bank;
        public int ReadResult;
        public bool Registered;
        public double PreloadDeadline;
        public List<PaMediaEntry> Preloads;
        public Stopwatch Clock = Stopwatch.StartNew();
    }

    internal sealed partial class PaCore
    {
        public const double PreloadWaitSec = 5.0;

        readonly List<PaBankOp> bankOps = new List<PaBankOp>();
        readonly Dictionary<uint, object> bankOwners = new Dictionary<uint, object>();
        readonly List<uint> preloadQueue = new List<uint>();

        public static uint BankIdFromName(string name)
        {
            if (name != null && name.EndsWith(".bnk", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            uint id = PaIds.Hash(name);
            return id != 0 ? id : PaHandles.NewId();
        }

        PaBankInfo FindBank(string name, uint id)
        {
            PaBankInfo info = null;
            try
            {
                info = name != null ? banks.Find(name) : banks.Find(id);
            }
            catch (Exception e)
            {
                PaLog.Error("bank lookup failed for " + (name ?? id.ToString()) + ": " + e.Message);
            }
            return info;
        }

        // Reads a bank file. Missing JSON (or a bank the manifest does not know) becomes an
        // empty bank with a warning, so the load succeeds. Runs on any thread.
        PaBank ReadBank(PaBankInfo info, string name, uint id, string languageKey, out int result)
        {
            result = PaResult.Success;
            PaBank bank = null;
            if (info != null)
            {
                try
                {
                    bank = banks.Read(info, languageKey, out result);
                }
                catch (Exception e)
                {
                    result = PaResult.BankReadError;
                    PaLog.Error("bank " + info.Name + " could not be read: " + e.Message);
                }
            }
            if (bank != null && result == PaResult.Success)
                return bank;
            string label = info != null ? info.Name : (name ?? id.ToString());
            if (info == null || result == PaResult.FileNotFound)
                PaLog.Warn("bank " + label + " has no converted data; loaded as an empty bank");
            else
                PaLog.Error("bank " + label + " failed to read (result " + result + "); loaded as an empty bank");
            result = PaResult.Success;
            return EmptyBank(info != null ? info.Name : name, info != null ? info.Id : id);
        }

        static PaBank EmptyBank(string name, uint id)
        {
            return new PaBank
            {
                Format = 1, Name = name ?? "", Id = id, Language = "", LanguageKey = "",
                Events = new List<PaEvent>(), Actions = new List<PaAction>(), Nodes = new List<PaNode>(),
                Busses = new List<PaBus>(), Attenuations = new List<PaAttenuation>(), GameParameters = new List<PaGameParameter>(),
                StateGroups = new List<PaStateGroup>(), Effects = new List<PaEffect>(),
            };
        }

        static int ObjectCount(PaBank b)
        {
            return b.Events.Count + b.Actions.Count + b.Nodes.Count + b.Busses.Count + b.Attenuations.Count
                + b.GameParameters.Count + b.StateGroups.Count + b.Effects.Count;
        }

        // Registers a read bank (engine lock held). On the first reference the bank's embedded
        // media preloads are queued for the next tick.
        int RegisterBank(PaBank bank, uint id, out List<PaMediaEntry> preloads)
        {
            preloads = null;
            if (bank.Id == 0)
                bank.Id = id;
            int result = banks.Register(bank);
            if (result != PaResult.Success)
                return result;
            if (banks.RefCount(bank.Id) == 1)
            {
                preloads = EmbeddedMedia(bank);
                if (!bankOwners.ContainsKey(bank.Id))
                    bankOwners[bank.Id] = new object();
                preloadQueue.Add(bank.Id);
            }
            return result;
        }

        List<PaMediaEntry> EmbeddedMedia(PaBank bank)
        {
            List<PaMediaEntry> list = new List<PaMediaEntry>();
            string key = !string.IsNullOrEmpty(bank.LanguageKey) ? bank.LanguageKey : world.VoiceLanguageKey;
            HashSet<uint> seen = new HashSet<uint>();
            foreach (PaSource src in banks.EmbeddedSources(bank.Id))
            {
                if (src == null || !seen.Add(src.Media))
                    continue;
                PaMediaEntry e = media.Find(src.Media, src.LanguageSpecific, key);
                if (e != null)
                    list.Add(e);
            }
            return list;
        }

        // Tick: start the preloads of banks registered since the last tick.
        void RunPreloads()
        {
            if (preloadQueue.Count == 0)
                return;
            foreach (uint id in preloadQueue)
            {
                if (!banks.IsLoaded(id))
                    continue;
                PaBank bank = null;
                foreach (PaBank b in banks.Loaded)
                    if (b.Id == id)
                        bank = b;
                object owner;
                if (bank != null && bankOwners.TryGetValue(id, out owner))
                {
                    List<PaMediaEntry> list = EmbeddedMedia(bank);
                    if (list.Count > 0)
                        voices.Preload(list, owner);
                }
            }
            preloadQueue.Clear();
        }

        void TraceBank(string op, string name, uint id, string lang, int result, double ms, int objects)
        {
            if (PaLog.Tracing)
                PaLog.Trace("bank", "op", op, "name", name, "id", id, "lang", lang, "result", result,
                    "refCount", banks.RefCount(id), "ms", Math.Round(ms, 2), "objects", objects);
        }

        string BankLanguageKey(PaBankInfo info)
        {
            return info != null && info.Localized ? world.VoiceLanguageKey : "";
        }

        // ---- sync ----

        // Called without the engine lock held for the read, with it for the register.
        public int LoadBankSync(string name, uint id, object engineLock)
        {
            Stopwatch clock = Stopwatch.StartNew();
            PaBankInfo info;
            string lang;
            lock (engineLock)
            {
                info = FindBank(name, id);
                lang = BankLanguageKey(info);
                if (info != null)
                    id = info.Id;
                if (banks.IsLoaded(id))
                {
                    PaBank already = null;
                    foreach (PaBank b in banks.Loaded)
                        if (b.Id == id)
                            already = b;
                    if (already != null)
                    {
                        List<PaMediaEntry> unused;
                        RegisterBank(already, id, out unused);
                        TraceBank("load", info != null ? info.Name : name, id, already.LanguageKey, PaResult.Success, clock.Elapsed.TotalMilliseconds, ObjectCount(already));
                        return PaResult.Success;
                    }
                }
            }
            int readResult;
            PaBank bank = ReadBank(info, name, id, lang, out readResult);
            lock (engineLock)
            {
                List<PaMediaEntry> preloads;
                int result = RegisterBank(bank, id, out preloads);
                stats.BanksLoaded++;
                TraceBank("load", bank.Name, bank.Id, bank.LanguageKey, result, clock.Elapsed.TotalMilliseconds, ObjectCount(bank));
                return result;
            }
        }

        public int UnloadBankSync(uint id)
        {
            int result = banks.Release(id);
            AfterRelease(id);
            TraceBank("unload", banks.NameOf(id), id, "", result, 0, 0);
            return result;
        }

        void AfterRelease(uint id)
        {
            if (banks.IsLoaded(id))
                return;
            object owner;
            if (bankOwners.TryGetValue(id, out owner))
            {
                bankOwners.Remove(id);
                commands.Enqueue(() => voices.ReleaseOwner(owner));
            }
            commands.Enqueue(StopOrphans);
        }

        public int ClearAllBanks()
        {
            foreach (PaInstance inst in instanceOrder.ToArray())
                StopInstance(inst, 0, PaCurve.Linear);
            banks.Clear();
            foreach (object owner in bankOwners.Values)
            {
                object o = owner;
                commands.Enqueue(() => voices.ReleaseOwner(o));
            }
            bankOwners.Clear();
            preloadQueue.Clear();
            TraceBank("unload", "*", 0, "", PaResult.Success, 0, 0);
            return PaResult.Success;
        }

        // ---- async ----

        public void LoadBankAsync(string name, uint id, IntPtr cookie, object engineLock)
        {
            PaBankOp op = new PaBankOp { Op = PaBankOp.Kind.Load, Name = name, BankId = id, Cookie = cookie };
            op.Info = FindBank(name, id);
            if (op.Info != null)
                op.BankId = op.Info.Id;
            op.LanguageKey = BankLanguageKey(op.Info);
            bankOps.Add(op);
            TraceBank("loadAsync", op.Info != null ? op.Info.Name : name, op.BankId, op.LanguageKey, PaResult.Success, 0, 0);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int result;
                PaBank bank = ReadBank(op.Info, op.Name, op.BankId, op.LanguageKey, out result);
                lock (engineLock)
                {
                    op.Bank = bank;
                    op.ReadResult = result;
                    op.ReadDone = true;
                }
            });
        }

        public void UnloadBankAsync(uint id, IntPtr cookie)
        {
            bankOps.Add(new PaBankOp { Op = PaBankOp.Kind.Unload, BankId = id, Cookie = cookie, ReadDone = true });
        }

        public void PrepareRequest(uint id, IntPtr cookie)
        {
            if (cookie == IntPtr.Zero)
                return;
            bankOps.Add(new PaBankOp { Op = PaBankOp.Kind.Prepare, BankId = id, Cookie = cookie, ReadDone = true });
        }

        // Tick: advance queued bank requests in order; each one's AK_Bank goes out only after
        // every earlier request completed.
        void AdvanceBankOps()
        {
            double now = platform.RealtimeSeconds;
            while (bankOps.Count > 0)
            {
                PaBankOp op = bankOps[0];
                if (!AdvanceBankOp(op, now))
                    break;
                bankOps.RemoveAt(0);
            }
        }

        // Returns true when the request completed (and its callback was queued).
        bool AdvanceBankOp(PaBankOp op, double now)
        {
            switch (op.Op)
            {
                case PaBankOp.Kind.Unload:
                    {
                        int result = banks.Release(op.BankId);
                        AfterRelease(op.BankId);
                        TraceBank("unload", banks.NameOf(op.BankId), op.BankId, "", result, op.Clock.Elapsed.TotalMilliseconds, 0);
                        BankCallback(op, result);
                        return true;
                    }
                case PaBankOp.Kind.Prepare:
                    BankCallback(op, PaResult.Success);
                    return true;
            }
            if (!op.ReadDone)
                return false;
            if (!op.Registered)
            {
                List<PaMediaEntry> preloads;
                int result = RegisterBank(op.Bank, op.BankId, out preloads);
                op.Registered = true;
                op.ReadResult = result;
                op.Preloads = preloads ?? new List<PaMediaEntry>();
                op.PreloadDeadline = now + PreloadWaitSec;
                stats.BanksLoaded++;
                TraceBank("load", op.Bank.Name, op.Bank.Id, op.Bank.LanguageKey, result, op.Clock.Elapsed.TotalMilliseconds, ObjectCount(op.Bank));
                // Preloads start in this tick (RunPreloads runs after the bank requests).
                return false;
            }
            bool ready = true;
            foreach (PaMediaEntry e in op.Preloads)
                if (!voices.IsReady(e))
                {
                    ready = false;
                    break;
                }
            if (!ready && now < op.PreloadDeadline)
                return false;
            if (!ready)
                PaLog.Warn("bank " + op.Bank.Name + ": media not ready after " + PreloadWaitSec + " s; reporting it loaded");
            TraceBank("ready", op.Bank.Name, op.Bank.Id, op.Bank.LanguageKey, op.ReadResult, op.Clock.Elapsed.TotalMilliseconds, ObjectCount(op.Bank));
            BankCallback(op, op.ReadResult);
            return true;
        }

        void BankCallback(PaBankOp op, int result)
        {
            if (op.Cookie == IntPtr.Zero)
                return;
            PaCallbacks.QueueBank(op.Cookie, op.BankId, result);
            stats.BankCallbacks++;
            if (PaLog.Tracing)
                PaLog.Trace("callback", "type", "bank", "bankId", op.BankId, "result", result);
        }

        public int PendingBankOps
        {
            get { return bankOps.Count; }
        }
    }
}
