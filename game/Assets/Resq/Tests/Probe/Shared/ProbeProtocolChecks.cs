using System;
using System.Globalization;
using System.Linq;
using Resq.Probe;

namespace Resq.Tests.Probe
{
    // Same checks are called by NUnit and the installed-toolchain standalone harness.
    public static class ProbeProtocolChecks
    {
        private const string Candidate = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        public static readonly string[] Names = {
            "exact_event_allowlist", "explicit_identity", "no_automatic_success",
            "duplicate_and_unknown", "denial_order", "reversed_monotonic", "reversed_utc",
            "equal_times_and_sequence", "incomplete_close", "closed_session",
            "immutable_snapshot", "exact_normalized_json", "culture_independence", "invalid_clock"
        };

        public static void Run(string name)
        {
            var clock = new ManualClock();
            var recorder = Create(clock);
            switch (name)
            {
                case "exact_event_allowlist":
                    Require(string.Join(",", ProbeProtocol.EventIds) == "passthrough_visible,hands_near_button,hands_ray_button,one_hand_left_button,one_hand_right_button,room_permission_denied,virtual_fallback,placement_confirmed,pause_resume,suspend_resume");
                    Reject<ArgumentException>(() => recorder.RecordSuccessfulCallback("Passthrough_visible"));
                    Reject<ArgumentException>(() => recorder.RecordSuccessfulCallback(null));
                    break;
                case "explicit_identity":
                    Reject<ArgumentException>(() => new ProbeRecorder("", "synthetic", "synthetic", clock));
                    Reject<ArgumentException>(() => new ProbeRecorder(new string('A', 64), "synthetic", "synthetic", clock));
                    Reject<ArgumentException>(() => new ProbeRecorder(Candidate, "", "synthetic", clock));
                    Reject<ArgumentException>(() => new ProbeRecorder(Candidate, "synthetic", "serial\nprivate", clock));
                    Reject<ArgumentNullException>(() => new ProbeRecorder(Candidate, "synthetic", "synthetic", null));
                    Require(recorder.Snapshot().CandidateSha256 == Candidate);
                    break;
                case "no_automatic_success":
                    Require(recorder.Snapshot().Events.Count == 0 && recorder.Snapshot().MissingEventIds.Count == 10);
                    Require(!recorder.Snapshot().ProtocolComplete && !recorder.Snapshot().SessionClosed);
                    break;
                case "duplicate_and_unknown":
                    recorder.RecordSuccessfulCallback("hands_near_button");
                    Reject<InvalidOperationException>(() => recorder.RecordSuccessfulCallback("hands_near_button"));
                    Reject<ArgumentException>(() => recorder.RecordSuccessfulCallback("human_comfort"));
                    Require(recorder.RecordSuccessfulCallback("hands_ray_button").Sequence == 1);
                    break;
                case "denial_order":
                    Reject<InvalidOperationException>(() => recorder.RecordSuccessfulCallback("virtual_fallback"));
                    Reject<InvalidOperationException>(() => recorder.RecordSuccessfulCallback("placement_confirmed"));
                    Require(recorder.Snapshot().Events.Count == 0);
                    recorder.RecordSuccessfulCallback("room_permission_denied");
                    Reject<InvalidOperationException>(() => recorder.RecordSuccessfulCallback("placement_confirmed"));
                    Require(recorder.RecordSuccessfulCallback("virtual_fallback").Sequence == 1);
                    Require(recorder.RecordSuccessfulCallback("placement_confirmed").Sequence == 2);
                    break;
                case "reversed_monotonic":
                    clock.Set(10, 10); recorder.RecordSuccessfulCallback("hands_near_button");
                    clock.Set(11, 9);
                    Reject<InvalidOperationException>(() => recorder.RecordSuccessfulCallback("hands_ray_button"));
                    Reject<InvalidOperationException>(() => recorder.Close());
                    Require(recorder.Snapshot().Events.Count == 1 && !recorder.Snapshot().SessionClosed);
                    clock.Set(12, 12); Require(recorder.RecordSuccessfulCallback("hands_ray_button").Sequence == 1);
                    break;
                case "reversed_utc":
                    clock.Set(10, 10); recorder.RecordSuccessfulCallback("hands_near_button");
                    clock.Set(9, 11);
                    Reject<InvalidOperationException>(() => recorder.RecordSuccessfulCallback("hands_ray_button"));
                    Reject<InvalidOperationException>(() => recorder.Close());
                    clock.Set(12, 12); Require(recorder.RecordSuccessfulCallback("hands_ray_button").Sequence == 1);
                    break;
                case "equal_times_and_sequence":
                    foreach (string id in ProbeProtocol.EventIds) recorder.RecordSuccessfulCallback(id);
                    ProbeSnapshot open = recorder.Snapshot();
                    Require(!open.ProtocolComplete && open.MissingEventIds.Count == 0);
                    for (int i = 0; i < open.Events.Count; i++)
                        Require(open.Events[i].Sequence == i && open.Events[i].MonotonicMilliseconds == 0 && open.Events[i].ObservedUtc == Epoch);
                    Reject<InvalidOperationException>(() => recorder.Close());
                    clock.Set(1, 0); Require(recorder.Close().ProtocolComplete);
                    break;
                case "incomplete_close":
                    recorder.RecordSuccessfulCallback("hands_near_button"); clock.Set(1, 1);
                    ProbeSnapshot incomplete = recorder.Close();
                    Require(incomplete.SessionClosed && !incomplete.ProtocolComplete && incomplete.Events.Count == 1);
                    Require(incomplete.MissingEventIds.Count == 9 && !incomplete.MissingEventIds.Contains("hands_near_button"));
                    Require(incomplete.ToJson().Contains("\"protocol_complete\":false"));
                    break;
                case "closed_session":
                    clock.Set(1, 1); recorder.Close();
                    Reject<InvalidOperationException>(() => recorder.RecordSuccessfulCallback("hands_near_button"));
                    Reject<InvalidOperationException>(() => recorder.Close());
                    Require(recorder.Snapshot().Events.Count == 0);
                    break;
                case "immutable_snapshot":
                    ProbeSnapshot before = recorder.Snapshot(); recorder.RecordSuccessfulCallback("hands_near_button");
                    Require(before.Events.Count == 0 && before.MissingEventIds.Count == 10);
                    var collection = (System.Collections.Generic.IList<ProbeEvent>)recorder.Snapshot().Events;
                    Reject<NotSupportedException>(() => collection.Clear());
                    Require(recorder.Snapshot().Events.Count == 1);
                    break;
                case "exact_normalized_json":
                    recorder.RecordSuccessfulCallback("hands_near_button");
                    string expected = "{\"schema_version\":1,\"record_type\":\"resq-xr-probe-runtime\",\"evidence_kind\":\"unqualified_runtime_instrumentation\",\"accepted_task\":false,\"candidate_sha256\":\"" + Candidate + "\",\"session_id\":\"harness-synthetic-session\",\"device_alias\":\"harness-synthetic-device\",\"started_utc\":\"2026-10-16T00:00:00.0000000Z\",\"ended_utc\":null,\"session_closed\":false,\"protocol_complete\":false,\"missing_event_ids\":[\"passthrough_visible\",\"hands_ray_button\",\"one_hand_left_button\",\"one_hand_right_button\",\"room_permission_denied\",\"virtual_fallback\",\"placement_confirmed\",\"pause_resume\",\"suspend_resume\"],\"events\":[{\"event_id\":\"hands_near_button\",\"result\":\"passed\",\"sequence\":0,\"monotonic_ms\":0,\"observed_utc\":\"2026-10-16T00:00:00.0000000Z\"}]}";
                    Require(recorder.Snapshot().ToJson() == expected);
                    break;
                case "culture_independence":
                    CultureInfo original = CultureInfo.CurrentCulture;
                    try
                    {
                        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
                        clock.Set(1234, 1234); recorder.RecordSuccessfulCallback("hands_near_button");
                        Require(recorder.Snapshot().ToJson().Contains("\"monotonic_ms\":1234"));
                        Require(recorder.Snapshot().ToJson().Contains("2026-10-16T00:00:01.2340000Z"));
                    }
                    finally { CultureInfo.CurrentCulture = original; }
                    break;
                case "invalid_clock":
                    Reject<ArgumentOutOfRangeException>(() => new ProbeTime(Epoch, -1));
                    Reject<ArgumentException>(() => new ProbeTime(Epoch.ToOffset(TimeSpan.FromHours(2)), 0));
                    clock.Set(-1, 0); Reject<InvalidOperationException>(() => recorder.RecordSuccessfulCallback("hands_near_button"));
                    break;
                default: throw new ArgumentException("Unknown probe check: " + name);
            }
        }

        public static string SyntheticSampleJson()
        {
            var clock = new ManualClock(); var recorder = Create(clock);
            int milliseconds = 0;
            foreach (string id in ProbeProtocol.EventIds)
            { clock.Set(++milliseconds, milliseconds); recorder.RecordSuccessfulCallback(id); }
            clock.Set(++milliseconds, milliseconds);
            return recorder.Close().ToJson();
        }

        private static ProbeRecorder Create(ManualClock clock) => new ProbeRecorder(Candidate,
            "harness-synthetic-session", "harness-synthetic-device", clock);
        private static void Require(bool condition)
        { if (!condition) throw new InvalidOperationException("Probe protocol check failed."); }
        private static void Reject<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected rejection: " + typeof(T).Name);
        }
        private sealed class ManualClock : IProbeClock
        {
            private long utc, monotonic;
            public void Set(long utcMilliseconds, long monotonicMilliseconds) { utc = utcMilliseconds; monotonic = monotonicMilliseconds; }
            public ProbeTime Sample() => new ProbeTime(Epoch.AddMilliseconds(utc), monotonic);
        }
    }
}
