using System;
using System.Collections.Generic;
using System.Linq;
using Resq.Content;
using Resq.Persistence;
using Resq.Simulation;

namespace Resq.Editor.Replay
{
    // Separately recorded application inputs, bound to the canonical replay.
    // No poses, domain state, synthetic commands or expected results are stored.
    public sealed class ReplayApplicationJournal
    {
        public JsonValue Value { get; }
        public string Sha256 => Value.Sha256();
        public string InitialState => Value["initial_application_state"].Text;
        public int RecoveryCount => Value["recoveries"].Items.Count;
        private ReplayApplicationJournal(JsonValue value) { Value = value; }
        public static ReplayApplicationJournal Read(JsonValue value, ReplayDocument replay)
        {
            SnapshotContract.Fields(value, "schema_version", "replay_sha256", "initial_application_state", "initial_interruption_reason", "recoveries");
            SnapshotContract.Number(value["schema_version"], 1, 1); SnapshotContract.Hash(value["replay_sha256"]);
            if (replay == null || value["replay_sha256"].Text != replay.Value.Sha256()) throw new InvalidOperationException("Application journal replay hash mismatch");
            string state = value["initial_application_state"].Text, phase = replay.Value["initial_snapshot"]["phase"].Text;
            if (!new[] { "running", "paused", "interrupted", "loading", "outcome" }.Contains(state)) throw new InvalidOperationException("Application journal initial state");
            if (phase == "planning" && state != "paused" || phase == "outcome" && state != "outcome" || phase != "outcome" && state == "outcome") throw new InvalidOperationException("Application journal phase/lifecycle mismatch");
            var reason = value["initial_interruption_reason"];
            if (state == "interrupted")
            { if (reason.IsNull || !new[] { "tracking", "anchor", "platform" }.Contains(reason.Text)) throw new InvalidOperationException("Actual initial interruption reason required"); }
            else if (state == "loading")
            { if (reason.IsNull || reason.Text != "loading") throw new InvalidOperationException("Actual initial loading reason required"); }
            else if (!reason.IsNull) throw new InvalidOperationException("Unexpected initial interruption reason");
            if (value["recoveries"].Kind != "array" || value["recoveries"].Items.Count > 10000) throw new InvalidOperationException("Application recovery count");
            long previous = -1;
            foreach (var row in value["recoveries"].Items)
            {
                SnapshotContract.Fields(row, "before_command_index", "operation_tick");
                SnapshotContract.Number(row["before_command_index"], previous + 1, replay.Value["commands"].Items.Count - 1);
                int index = checked((int)row["before_command_index"].Integer); previous = index;
                SnapshotContract.Number(row["operation_tick"], 0, Resq.Core.Tick.Maximum);
                if (row["operation_tick"].Integer != replay.Value["commands"].Items[index]["admitted_at_operation_tick"].Integer) throw new InvalidOperationException("Application recovery admission tick mismatch");
            }
            return new ReplayApplicationJournal(value);
        }
        internal void ValidateBinding(ReplayDocument replay) { Read(Value, replay); }
        internal void Initialize(OperationClock clock)
        {
            string before = clock.ReadSnapshot().Value.Canonical();
            if (InitialState == "interrupted" || InitialState == "loading") clock.Interrupt(Value["initial_interruption_reason"].Text);
            if (clock.ApplicationState != InitialState) throw new InvalidOperationException("Restored application lifecycle disagrees with recorded journal");
            if (clock.ReadSnapshot().Value.Canonical() != before) throw new InvalidOperationException("Initial application lifecycle changed authoritative state");
        }
        internal bool BeforeCommand(int index, OperationClock clock)
        {
            var row = Value["recoveries"].Items.SingleOrDefault(x => x["before_command_index"].Integer == index);
            if (row == null) return false;
            if (clock.OperationTick != row["operation_tick"].Integer) throw new InvalidOperationException("Application recovery replay cursor mismatch");
            string before = clock.ReadSnapshot().Value.Canonical();
            clock.RecoverInterruption();
            if (clock.ReadSnapshot().Value.Canonical() != before) throw new InvalidOperationException("Application recovery changed authoritative state");
            return true;
        }
    }
}
