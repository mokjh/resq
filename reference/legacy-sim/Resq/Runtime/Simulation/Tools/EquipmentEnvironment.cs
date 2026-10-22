using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Resq.Content;
using Resq.Core;

namespace Resq.Simulation.Tools
{
    // A mandatory logical sample supplied by the actual hazard/route owners.
    // Missing samples never imply a dry, idle, unoccupied or safe socket.
    public sealed class EquipmentSocketSample
    {
        public string Id { get; }
        public long DepthMm { get; }
        public long Heat { get; }
        public bool Safe { get; }
        public bool Idle { get; }
        public long Occupants { get; }
        public long Reservations { get; }
        public bool PendingWork { get; }
        public bool DependentOrder { get; }
        public bool HotLava { get; }
        public bool HotLavaArrival { get; }
        public bool Collapsed { get; }
        public bool DamageWarningPublished { get; }
        public bool ExternallyHazardous { get; }
        public EquipmentSocketSample(string id, long depthMm, long heat, bool safe, bool idle, long occupants,
            long reservations, bool pendingWork, bool dependentOrder, bool hotLava, bool hotLavaArrival, bool collapsed, bool damageWarningPublished, bool externallyHazardous)
        {
            if (depthMm < 0 || heat < 0 || heat > 1000 || occupants < 0 || reservations < 0) throw new ArgumentOutOfRangeException(nameof(depthMm));
            if (hotLavaArrival && !hotLava) throw new ArgumentException("Lava arrival requires actual Hot state");
            Id = id; DepthMm = depthMm; Heat = heat; Safe = safe; Idle = idle; Occupants = occupants; Reservations = reservations;
            PendingWork = pendingWork; DependentOrder = dependentOrder; HotLava = hotLava; HotLavaArrival = hotLavaArrival; Collapsed = collapsed; DamageWarningPublished = damageWarningPublished; ExternallyHazardous = externallyHazardous;
        }
    }
    public sealed class EquipmentFrame
    {
        public long OperationTick { get; }
        public ReadOnlyDictionary<string, EquipmentSocketSample> Sockets { get; }
        public ReadOnlyDictionary<string, long> SeaHeads { get; }
        public EquipmentFrame(long operationTick, IEnumerable<EquipmentSocketSample> sockets, IDictionary<string, long> seaHeads)
        {
            var value = CreateWork(operationTick, sockets, seaHeads).Drain(); OperationTick = value.OperationTick;
            Sockets = value.Sockets; SeaHeads = value.SeaHeads;
        }
        private EquipmentFrame(long operationTick, ReadOnlyDictionary<string, EquipmentSocketSample> sockets, ReadOnlyDictionary<string, long> seaHeads)
        { OperationTick = operationTick; Sockets = sockets; SeaHeads = seaHeads; }
        public static ResumableWork<EquipmentFrame> CreateWork(long operationTick, IEnumerable<EquipmentSocketSample> sockets, IDictionary<string, long> seaHeads) => new ResumableWork<EquipmentFrame>(done => CreateRoutine(operationTick, sockets, seaHeads, done));
        private static IEnumerable<string> CreateRoutine(long operationTick, IEnumerable<EquipmentSocketSample> sockets, IDictionary<string, long> seaHeads, Action<EquipmentFrame> done)
        {
            new Tick(operationTick);
            Dictionary<string, EquipmentSocketSample> socketsCopy; using (var worksocketsCopy = DomainWork.Index(sockets, x => x.Id, "equipment.frame.socket")) { while (worksocketsCopy.MoveNext()) yield return worksocketsCopy.Stage; socketsCopy = worksocketsCopy.Result; }
            Dictionary<string, long> seaHeadsCopy; using (var workseaHeadsCopy = DomainWork.Copy(seaHeads, "equipment.frame.sea")) { while (workseaHeadsCopy.MoveNext()) yield return workseaHeadsCopy.Stage; seaHeadsCopy = workseaHeadsCopy.Result; }
            done(new EquipmentFrame(operationTick, new ReadOnlyDictionary<string, EquipmentSocketSample>(socketsCopy), new ReadOnlyDictionary<string, long>(seaHeadsCopy)));
        }
    }
    // Implemented by the applicable future world-mechanic integration, never by a success/no-op fallback.
    // Fork must isolate all mutable hazard state. All validators are nonmutating;
    // apply methods throw on disagreement and are invoked on an isolated fork.
    public interface IEquipmentMechanics
    {
        IEquipmentMechanics Fork();
        bool FireAvailable { get; }
        bool StructureAvailable { get; }
        bool LavaAvailable { get; }
        ValidationResult ValidateDeployment(EquipmentDef definition, Socket socket);
        ValidationResult ValidateFirebreak(Socket socket);
        void ClearFirebreak(Socket socket, long maximumCells);
        void ApplySpray(Socket socket, long actualGrantedMm3);
        void CommitBrace(string equipmentInstanceId, Socket socket, long stabilityBonus);
        ValidationResult ValidateCooling(Socket socket, string segmentId);
        void BeginCooling(string equipmentInstanceId, Socket socket, string segmentId, long scenarioTick, CoolerParameters parameters);
        bool CoolingActive(string equipmentInstanceId, string segmentId);
    }
    public sealed class EquipmentView
    {
        public string Id { get; }
        public string EquipmentId { get; }
        public string SocketId { get; }
        public bool Enabled { get; }
        public bool Intact { get; }
        public EquipmentView(string id, string equipmentId, string socketId, bool enabled, bool intact)
        { Id = id; EquipmentId = equipmentId; SocketId = socketId; Enabled = enabled; Intact = intact; }
    }
    internal static class EquipmentJson
    {
        internal static JsonValue O(params object[] pairs)
        {
            var result = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                var value = pairs[i + 1];
                result.Add((string)pairs[i], value == null ? JsonValue.Null() : value is JsonValue json ? json : value is string text ? JsonValue.String(text) : value is bool boolean ? JsonValue.Bool(boolean) : JsonValue.Number(Convert.ToInt64(value)));
            }
            return JsonValue.Object(result);
        }
        internal static void Fields(JsonValue value, params string[] names)
        { if (value.Kind != "object" || value.Members.Count != names.Length || names.Any(name => !value.Has(name))) throw new InvalidOperationException("Closed equipment state required"); }
    }
}
