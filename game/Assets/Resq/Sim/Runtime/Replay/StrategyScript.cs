using System;
using System.Collections.Generic;

namespace Resq.Sim
{
    /// <summary>
    /// A reference strategy / replay: timed commands addressed by stable content IDs. Used by the validator,
    /// PlayMode tests, the on-device autopilot and trailer capture. Example step:
    /// {"t": 12, "place": "bridge:01"} · {"t": 30, "group": "bus", "to": "depot", "evacuate": true} ·
    /// {"t": 40, "vehicle": "skiff:01", "group": "low_dock", "pickup": "low_dock", "to": "high_landing"} · {"t": 50, "ack": true}
    /// </summary>
    public sealed class StrategyScript
    {
        public string Id;
        public string Description;
        public readonly List<(long t, JsonNode step)> Steps = new List<(long, JsonNode)>();

        public static StrategyScript Parse(JsonNode doc)
        {
            var s = new StrategyScript { Id = doc.Str("id"), Description = doc.Str("description") };
            foreach (var step in doc["steps"].Elements()) s.Steps.Add((step.Long("t"), step));
            s.Steps.Sort((a, b) => a.t.CompareTo(b.t));
            return s;
        }

        /// <summary>Submit every step due at the current operation tick (call before Step()).</summary>
        public void SubmitDue(Simulator sim, ref int cursor, List<string> log = null)
        {
            long next = sim.S.C.O + 1; // commands execute on the next operation step
            while (cursor < Steps.Count && Steps[cursor].t <= next)
            {
                var step = Steps[cursor++].step;
                if (step.Flag("ack")) { sim.Submit(new Command { Kind = CommandKind.AcknowledgeTutorial }); continue; }
                var cmd = Resolve(sim, step, out string err);
                if (err != null) { log?.Add($"t={next} unresolved step: {err}"); continue; }
                sim.Submit(cmd);
            }
        }

        public static Command Resolve(Simulator sim, JsonNode step, out string error)
        {
            var d = sim.D;
            error = null;
            if (step.Has("place"))
            {
                string sid = step.Str("place");
                if (!d.SocketIndex.TryGetValue(sid, out int s)) { error = "socket " + sid; return default; }
                return Command.Place(s, d.SocketKind[s], step.Int("orientation"));
            }
            if (step.Has("move"))
            {
                int inst = InstanceAt(sim, step.Str("move"));
                if (inst < 0 || !d.SocketIndex.TryGetValue(step.Str("to"), out int dest)) { error = "move " + step.Str("move"); return default; }
                return Command.Move(inst, dest, step.Int("orientation"));
            }
            if (step.Has("enable") || step.Has("disable"))
            {
                bool on = step.Has("enable");
                int inst = InstanceAt(sim, step.Str(on ? "enable" : "disable"));
                if (inst < 0) { error = "tool " + step.Str(on ? "enable" : "disable"); return default; }
                return on ? Command.Enable(inst) : Command.Disable(inst);
            }
            if (step.Has("vehicle"))
            {
                int v = VehicleIndex(sim, step.Str("vehicle"));
                if (v < 0) { error = "vehicle " + step.Str("vehicle"); return default; }
                int g = step.Str("group") != null ? d.GroupIndex[step.Str("group")] : -1;
                int pickup = step.Str("pickup") != null ? d.NodeIndex[step.Str("pickup")] : -1;
                int to = d.NodeIndex[step.Str("to")];
                return Command.VehicleRoute(v, g, pickup, to, step.Flag("evacuate", true));
            }
            if (step.Has("group"))
            {
                return Command.GroupRoute(d.GroupIndex[step.Str("group")], d.NodeIndex[step.Str("to")], step.Flag("evacuate", true));
            }
            if (step.Has("priority"))
            {
                int consumer = d.InfraIndex.TryGetValue(step.Str("priority"), out int infra) ? infra
                    : Simulator.ConsumerInstanceBase + InstanceAt(sim, step.Str("priority"));
                return Command.Priority(consumer, step.Int("value"));
            }
            if (step.Has("end")) return Command.End(step.Flag("confirm_failure"));
            error = "unknown step " + step.ToCanonical();
            return default;
        }

        /// <summary>Instance currently at the given socket ID.</summary>
        public static int InstanceAt(Simulator sim, string socketId)
        {
            if (!sim.D.SocketIndex.TryGetValue(socketId, out int s)) return -1;
            return sim.S.SocketInstance[s];
        }

        /// <summary>Fixed vehicle by ID, or the vehicle spawned by the tool placed at a socket ID.</summary>
        public static int VehicleIndex(Simulator sim, string id)
        {
            int f = Array.IndexOf(sim.D.FixedVehicleIds, id);
            if (f >= 0) return f;
            int inst = InstanceAt(sim, id);
            if (inst < 0) return -1;
            for (int v = 0; v < sim.S.C.VehicleCount; v++) if (sim.S.VInstance[v] == inst) return v;
            return -1;
        }
    }

    /// <summary>Runs a strategy to completion (headless, deterministic).</summary>
    public static class StrategyRunner
    {
        public struct Result
        {
            public bool Success;
            public int Stars;
            public long Score;
            public int Rescued, Backup;
            public long EndO, EndS;
            public ulong Hash;
        }

        public static Result Run(MissionDef def, RuleProfile profile, StrategyScript script, bool guided = false,
                                 ISimEventSink sink = null, List<string> log = null, long maxSteps = 20000)
        {
            var sim = new Simulator(def, profile, sink, guided);
            int cursor = 0;
            long steps = 0;
            while (!sim.IsOver && steps++ < maxSteps)
            {
                script?.SubmitDue(sim, ref cursor, log);
                sim.Step();
            }
            return new Result
            {
                Success = sim.S.C.Success,
                Stars = sim.S.C.Stars,
                Score = sim.S.C.ScoreValue,
                Rescued = sim.S.C.Evacuated,
                Backup = sim.S.C.AwaitingBackup,
                EndO = sim.S.C.O,
                EndS = sim.S.C.S,
                Hash = sim.S.Hash(),
            };
        }
    }
}
