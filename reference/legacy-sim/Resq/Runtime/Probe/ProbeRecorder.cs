using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Resq.Probe
{
    public readonly struct ProbeTime
    {
        public DateTimeOffset Utc { get; }
        public long MonotonicMilliseconds { get; }
        public ProbeTime(DateTimeOffset utc, long monotonicMilliseconds)
        {
            if (utc.Offset != TimeSpan.Zero) throw new ArgumentException("Clock must supply UTC.", nameof(utc));
            if (monotonicMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(monotonicMilliseconds));
            Utc = utc;
            MonotonicMilliseconds = monotonicMilliseconds;
        }
    }

    public interface IProbeClock { ProbeTime Sample(); }

    // This clock measures instrumentation only; it never drives rescue outcomes.
    public sealed class SystemProbeClock : IProbeClock
    {
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        public ProbeTime Sample() => new ProbeTime(DateTimeOffset.UtcNow, elapsed.ElapsedMilliseconds);
    }

    public static class ProbeProtocol
    {
        public const string EvidenceKind = "unqualified_runtime_instrumentation";
        private static readonly string[] ids = {
            "passthrough_visible", "hands_near_button", "hands_ray_button",
            "one_hand_left_button", "one_hand_right_button", "room_permission_denied",
            "virtual_fallback", "placement_confirmed", "pause_resume", "suspend_resume"
        };
        public static IReadOnlyList<string> EventIds { get; } = Array.AsReadOnly(ids);
        internal static bool IsAllowed(string id) => Array.IndexOf(ids, id) >= 0;
    }

    public sealed class ProbeEvent
    {
        public string EventId { get; }
        public string Result => "passed"; // A caller-reported successful software callback, not human proof.
        public int Sequence { get; }
        public long MonotonicMilliseconds { get; }
        public DateTimeOffset ObservedUtc { get; }
        internal ProbeEvent(string id, int sequence, ProbeTime time)
        {
            EventId = id; Sequence = sequence;
            MonotonicMilliseconds = time.MonotonicMilliseconds; ObservedUtc = time.Utc;
        }
    }

    public sealed class ProbeSnapshot
    {
        public string CandidateSha256 { get; }
        public string SessionId { get; }
        public string DeviceAlias { get; }
        public DateTimeOffset StartedUtc { get; }
        public DateTimeOffset? EndedUtc { get; }
        public IReadOnlyList<ProbeEvent> Events { get; }
        public IReadOnlyList<string> MissingEventIds { get; }
        public bool SessionClosed => EndedUtc.HasValue;
        public bool ProtocolComplete => SessionClosed && MissingEventIds.Count == 0;

        internal ProbeSnapshot(string candidate, string session, string alias, DateTimeOffset started,
            DateTimeOffset? ended, List<ProbeEvent> events)
        {
            CandidateSha256 = candidate; SessionId = session; DeviceAlias = alias;
            StartedUtc = started; EndedUtc = ended;
            Events = new ReadOnlyCollection<ProbeEvent>(events.ToArray());
            var missing = new List<string>();
            foreach (string id in ProbeProtocol.EventIds)
                if (!events.Exists(item => item.EventId == id)) missing.Add(id);
            MissingEventIds = missing.AsReadOnly();
        }

        // Stable key order, culture-independent integers and explicit escaping; no JSON package needed.
        public string ToJson()
        {
            var json = new StringBuilder();
            json.Append("{\"schema_version\":1,\"record_type\":\"resq-xr-probe-runtime\",\"evidence_kind\":");
            String(json, ProbeProtocol.EvidenceKind);
            json.Append(",\"accepted_task\":false,\"candidate_sha256\":"); String(json, CandidateSha256);
            json.Append(",\"session_id\":"); String(json, SessionId);
            json.Append(",\"device_alias\":"); String(json, DeviceAlias);
            json.Append(",\"started_utc\":"); String(json, Utc(StartedUtc));
            json.Append(",\"ended_utc\":");
            if (EndedUtc.HasValue) String(json, Utc(EndedUtc.Value)); else json.Append("null");
            json.Append(",\"session_closed\":").Append(SessionClosed ? "true" : "false");
            json.Append(",\"protocol_complete\":").Append(ProtocolComplete ? "true" : "false");
            json.Append(",\"missing_event_ids\":[");
            for (int i = 0; i < MissingEventIds.Count; i++)
            { if (i > 0) json.Append(','); String(json, MissingEventIds[i]); }
            json.Append("],\"events\":[");
            for (int i = 0; i < Events.Count; i++)
            {
                if (i > 0) json.Append(',');
                ProbeEvent entry = Events[i];
                json.Append("{\"event_id\":"); String(json, entry.EventId);
                json.Append(",\"result\":\"passed\",\"sequence\":").Append(entry.Sequence.ToString(CultureInfo.InvariantCulture));
                json.Append(",\"monotonic_ms\":").Append(entry.MonotonicMilliseconds.ToString(CultureInfo.InvariantCulture));
                json.Append(",\"observed_utc\":"); String(json, Utc(entry.ObservedUtc)); json.Append('}');
            }
            return json.Append("]}").ToString();
        }

        private static string Utc(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        private static void String(StringBuilder output, string value)
        {
            output.Append('"');
            foreach (char ch in value)
            {
                if (ch == '"' || ch == '\\') output.Append('\\').Append(ch);
                else if (ch < 0x20) output.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                else output.Append(ch);
            }
            output.Append('"');
        }
    }

    // No SDK, platform detection, physical qualification or automatic successful events.
    // All mutations are serialized so parallel callbacks cannot reuse sequence numbers.
    public sealed class ProbeRecorder
    {
        private readonly object sync = new object();
        private readonly IProbeClock clock;
        private readonly string candidate, session, alias;
        private readonly ProbeTime started;
        private ProbeTime previous;
        private DateTimeOffset? ended;
        private readonly List<ProbeEvent> events = new List<ProbeEvent>();
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        public ProbeRecorder(string candidateSha256, string sessionId, string deviceAlias, IProbeClock clock)
        {
            if (candidateSha256 == null || candidateSha256.Length != 64)
                throw new ArgumentException("An explicit lowercase SHA-256 candidate digest is required.", nameof(candidateSha256));
            foreach (char ch in candidateSha256)
                if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f')))
                    throw new ArgumentException("Candidate digest must be lowercase hex.", nameof(candidateSha256));
            Token(sessionId, nameof(sessionId)); Token(deviceAlias, nameof(deviceAlias));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            candidate = candidateSha256; session = sessionId; alias = deviceAlias;
            started = previous = clock.Sample();
        }

        // Call only after the corresponding actual callback reports its own successful behavior.
        // Calling this method does not establish what a headset or a human observed.
        public ProbeEvent RecordSuccessfulCallback(string eventId)
        {
            lock (sync)
            {
                if (ended.HasValue) throw new InvalidOperationException("Session is closed.");
                if (!ProbeProtocol.IsAllowed(eventId)) throw new ArgumentException("Unknown probe event.", nameof(eventId));
                if (seen.Contains(eventId)) throw new InvalidOperationException("Duplicate probe event: " + eventId);
                if (eventId == "virtual_fallback" && !seen.Contains("room_permission_denied"))
                    throw new InvalidOperationException("Fallback requires the actual denial callback first.");
                if (eventId == "placement_confirmed" && !seen.Contains("virtual_fallback"))
                    throw new InvalidOperationException("Placement requires fallback first in this probe.");
                ProbeTime time = clock.Sample();
                ValidateTime(time);
                var entry = new ProbeEvent(eventId, events.Count, time);
                events.Add(entry); seen.Add(eventId); previous = time;
                return entry;
            }
        }

        public ProbeSnapshot Snapshot()
        {
            lock (sync) return Copy();
        }

        // Incomplete sessions are retained as incomplete, never filled with synthetic successes.
        public ProbeSnapshot Close()
        {
            lock (sync)
            {
                if (ended.HasValue) throw new InvalidOperationException("Session is already closed.");
                ProbeTime time = clock.Sample(); ValidateTime(time);
                if (time.Utc <= started.Utc) throw new InvalidOperationException("Session must have a positive UTC interval.");
                ended = time.Utc; previous = time;
                return Copy();
            }
        }

        private ProbeSnapshot Copy() => new ProbeSnapshot(candidate, session, alias, started.Utc, ended, events);
        private void ValidateTime(ProbeTime time)
        {
            if (time.MonotonicMilliseconds < previous.MonotonicMilliseconds || time.Utc < previous.Utc)
                throw new InvalidOperationException("Probe clock reversed; session remains incomplete.");
        }
        private static void Token(string value, string name)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) throw new ArgumentException("Explicit pseudonymous token required.", name);
            foreach (char ch in value)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-'))
                    throw new ArgumentException("Use a pseudonymous ASCII token (letters, digits, '.', '_' or '-').", name);
        }
    }
}
