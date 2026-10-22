using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Resq.Content;
using Resq.Core;

namespace Resq.Simulation.Forecast
{
    public interface IForecastService
    {
        ForecastHandle Begin(ForecastRequest request);
        void Cancel(long requestId);
        ForecastResult Poll(long requestId);
    }

    public sealed class ForecastRequest
    {
        public const int DefaultHorizon = 300;
        public long RequestId { get; }
        public long SourceStateRevision { get; }
        public JsonValue Snapshot { get; }
        public GameCommand PreviewCommand { get; }
        public int HorizonScenarioTicks { get; }
        public ForecastRequest(long requestId, long sourceStateRevision, JsonValue snapshot,
            GameCommand previewCommand, int horizonScenarioTicks = DefaultHorizon)
        {
            if (requestId <= 0 || sourceStateRevision < 0 || sourceStateRevision > int.MaxValue || horizonScenarioTicks < 1 || horizonScenarioTicks > 600)
                throw new ArgumentOutOfRangeException(nameof(requestId));
            RequestId = requestId; SourceStateRevision = sourceStateRevision;
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            PreviewCommand = previewCommand ?? throw new ArgumentNullException(nameof(previewCommand));
            HorizonScenarioTicks = horizonScenarioTicks;
        }
        public JsonValue ToJson() => ForecastJson.O("request_id", RequestId, "source_state_revision", SourceStateRevision,
            "snapshot", Snapshot, "preview_command", PreviewCommand.ToJson(), "horizon_scenario_ticks", HorizonScenarioTicks);
        public static ForecastRequest Read(JsonValue value)
        {
            ForecastJson.Fields(value, "request_id", "source_state_revision", "snapshot", "preview_command", "horizon_scenario_ticks");
            return new ForecastRequest(value["request_id"].Integer, value["source_state_revision"].Integer,
                value["snapshot"], GameCommand.Read(value["preview_command"]), checked((int)value["horizon_scenario_ticks"].Integer));
        }
    }
    // Handles have no command commit, scene, persistence or reward capability.
    public sealed class ForecastHandle
    {
        public long RequestId { get; }
        public long SourceStateRevision { get; }
        internal ForecastHandle(ForecastRequest request) { RequestId = request.RequestId; SourceStateRevision = request.SourceStateRevision; }
    }
    public sealed class RiskRegion
    {
        public string RegionId { get; }
        public string Hazard { get; }
        public long MaximumValue { get; }
        public long? FirstUnsafeScenarioTick { get; }
        internal RiskRegion(string region, string hazard, long maximum, long? firstUnsafe)
        { RegionId = region; Hazard = hazard; MaximumValue = maximum; FirstUnsafeScenarioTick = firstUnsafe; }
        public JsonValue ToJson() => ForecastJson.O("region_id", RegionId, "hazard", Hazard,
            "maximum_value", MaximumValue, "first_unsafe_scenario_tick", FirstUnsafeScenarioTick);
    }
    public sealed class RouteForecast
    {
        public string RouteId { get; }
        public bool Available { get; }
        public long? EstimatedArrivalOperationTick { get; }
        public string BlockingReasonCode { get; }
        internal RouteForecast(string route, bool available, long? arrival, string reason)
        { RouteId = route; Available = available; EstimatedArrivalOperationTick = arrival; BlockingReasonCode = reason; }
        public JsonValue ToJson() => ForecastJson.O("route_id", RouteId, "available", Available,
            "estimated_arrival_operation_tick", EstimatedArrivalOperationTick, "blocking_reason_code", BlockingReasonCode);
    }
    public sealed class ForecastResult
    {
        public long RequestId { get; }
        public long SourceStateRevision { get; }
        public string Status { get; }
        public string ReasonCode { get; }
        public string Knowledge { get; }
        public long SimulatedScenarioTicks { get; }
        public ReadOnlyCollection<RiskRegion> RiskRegions { get; }
        public ReadOnlyCollection<RouteForecast> Routes { get; }
        public CommandResult PreviewReceipt { get; }
        internal ForecastResult(ForecastRequest request, string status, string reason, string knowledge,
            long simulated = 0, IEnumerable<RiskRegion> risks = null, IEnumerable<RouteForecast> routes = null, CommandResult receipt = null)
        {
            RequestId = request.RequestId; SourceStateRevision = request.SourceStateRevision;
            Status = status; ReasonCode = reason; Knowledge = knowledge; SimulatedScenarioTicks = simulated;
            RiskRegions = (risks ?? Enumerable.Empty<RiskRegion>()).ToList().AsReadOnly();
            Routes = (routes ?? Enumerable.Empty<RouteForecast>()).ToList().AsReadOnly(); PreviewReceipt = receipt;
        }
        private ForecastResult(ForecastRequest request, string reason, string knowledge, long simulated,
            ReadOnlyCollection<RiskRegion> risks, ReadOnlyCollection<RouteForecast> routes, CommandResult receipt)
        {
            RequestId = request.RequestId; SourceStateRevision = request.SourceStateRevision;
            Status = "complete"; ReasonCode = reason; Knowledge = knowledge; SimulatedScenarioTicks = simulated;
            RiskRegions = risks; Routes = routes; PreviewReceipt = receipt;
        }
        // Lists are newly built private publication work and are never mutated after transfer.
        internal static ForecastResult FromOwnedRows(ForecastRequest request, string reason, string knowledge, long simulated,
            List<RiskRegion> risks, List<RouteForecast> routes, CommandResult receipt)
            => new ForecastResult(request, reason, knowledge, simulated, risks.AsReadOnly(), routes.AsReadOnly(), receipt);
        public JsonValue ToJson() => ForecastJson.O("request_id", RequestId, "source_state_revision", SourceStateRevision,
            "status", Status, "reason_code", ReasonCode, "knowledge", Knowledge,
            "simulated_scenario_ticks", SimulatedScenarioTicks, "risk_regions", JsonValue.Array(RiskRegions.Select(x => x.ToJson())),
            "routes", JsonValue.Array(Routes.Select(x => x.ToJson())), "preview_receipt", PreviewReceipt == null ? JsonValue.Null() : PreviewReceipt.ToJson());
    }
    // Presentation explanation is separate from the closed frozen ForecastResult.
    // Values are compiled costs/capacities and observed grants/queues, never a
    // promise of success and never a second validation or power allocator.
    public sealed class PreviewExplanation
    {
        public string Classification { get; }
        public ReadOnlyCollection<string> ReasonCodes { get; }
        public long SupplyCost { get; }
        public long SuppliesBefore { get; }
        public long SuppliesAfter { get; }
        public long ResidentSeats { get; }
        public long MovementSlots { get; }
        public long? StockRemaining { get; }
        public long PowerDemand { get; }
        public long? ActualPowerGrant { get; }
        public long WaitingCapacityOrders { get; }
        internal PreviewExplanation(string classification, IEnumerable<string> reasons, long cost, long before, long after,
            long seats, long slots, long? stock, long demand, long? grant, long waiting)
        {
            Classification = classification; ReasonCodes = reasons.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
            SupplyCost = cost; SuppliesBefore = before; SuppliesAfter = after; ResidentSeats = seats;
            MovementSlots = slots; StockRemaining = stock; PowerDemand = demand; ActualPowerGrant = grant; WaitingCapacityOrders = waiting;
        }
    }
    // Timing witnesses are presentation diagnostics only. No state trajectory
    // is retained and no timing value can change an authoritative outcome.
    public sealed class ForecastSliceMeasurement
    {
        public string Kind { get; }
        public long ElapsedMicroseconds { get; private set; }
        public ReadOnlyCollection<long> StepMicroseconds { get; }
        internal void CompleteTiming(long elapsed) { ElapsedMicroseconds = elapsed; }
        internal ForecastSliceMeasurement(string kind, long elapsed, IEnumerable<long> stepMicroseconds)
        { Kind = kind; ElapsedMicroseconds = elapsed; StepMicroseconds = stepMicroseconds.ToList().AsReadOnly(); }
    }
    public sealed class ForecastDiagnostics
    {
        public string FinalStateSha256 { get; }
        public long SimulatedOperationTicks { get; }
        public long MaximumSliceMicroseconds { get; private set; }
        public long RestoreMicroseconds { get; }
        public long MaximumStepMicroseconds { get; }
        public long StepBudgetOverruns { get; }
        public ReadOnlyCollection<ForecastSliceMeasurement> Slices { get; }
        public ReadOnlyCollection<long> CompletedStepMicroseconds { get; private set; }
        internal void SetCompletedStepTimings(ReadOnlyCollection<long> timings) { CompletedStepMicroseconds = timings; }
        public long ScheduledFrames { get; private set; }
        public long FrameBudgetOverruns { get; private set; }
        public long MaximumWorkUnitMicroseconds { get; private set; }
        public string LongestWorkUnit { get; private set; }
        public long? ManagedAllocatedBytes { get; private set; }
        public bool SliceSamplesTruncated { get; private set; }
        internal void CompleteFrameMetrics(long maximum, long frames, long overruns, long maximumUnit, string longestUnit, long? allocated, bool truncated)
        {
            MaximumSliceMicroseconds = maximum; ScheduledFrames = frames; FrameBudgetOverruns = overruns;
            MaximumWorkUnitMicroseconds = maximumUnit; LongestWorkUnit = longestUnit; ManagedAllocatedBytes = allocated; SliceSamplesTruncated = truncated;
        }
        internal ForecastDiagnostics(string hash, long steps, long maximumSlice, long restore, long maximumStep, long overruns, IEnumerable<ForecastSliceMeasurement> slices)
        { FinalStateSha256 = hash; SimulatedOperationTicks = steps; MaximumSliceMicroseconds = maximumSlice; RestoreMicroseconds = restore; MaximumStepMicroseconds = maximumStep; StepBudgetOverruns = overruns; Slices = slices as ReadOnlyCollection<ForecastSliceMeasurement> ?? slices.ToList().AsReadOnly(); }
    }
    internal static class ForecastJson
    {
        internal static JsonValue O(params object[] pairs)
        {
            var values = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                var value = pairs[i + 1];
                values.Add((string)pairs[i], value == null ? JsonValue.Null() : value is JsonValue json ? json :
                    value is string text ? JsonValue.String(text) : value is bool flag ? JsonValue.Bool(flag) : JsonValue.Number(Convert.ToInt64(value)));
            }
            return JsonValue.Object(values);
        }
        internal static void Fields(JsonValue value, params string[] fields)
        { if (value == null || value.Kind != "object" || value.Members.Count != fields.Length || fields.Any(x => !value.Has(x))) throw new ArgumentException("Closed forecast fields required"); }
    }
}
