using System;
using UnityEngine;

namespace Resq.Probe.Unity
{
    // An explicit future integration point, not a scene component or SDK implementation.
    // Configure from the real candidate/session inventory; no platform/model guessing.
    public sealed class ProbeLoggingBridge
    {
        public const string LogPrefix = "RESQ_UNQUALIFIED_PROBE ";
        private readonly ProbeRecorder recorder;
        public ProbeLoggingBridge(string candidateSha256, string sessionId, string deviceAlias)
            : this(new ProbeRecorder(candidateSha256, sessionId, deviceAlias, new SystemProbeClock())) { }

        public ProbeLoggingBridge(ProbeRecorder recorder)
        {
            this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        }

        public void RecordSuccessfulCallback(string eventId)
        {
            recorder.RecordSuccessfulCallback(eventId);
            Emit(recorder.Snapshot());
        }

        public void LogCurrentSnapshot() => Emit(recorder.Snapshot());
        public void CloseAndLog() => Emit(recorder.Close());
        private static void Emit(ProbeSnapshot snapshot) => Debug.Log(LogPrefix + snapshot.ToJson());
    }
}
