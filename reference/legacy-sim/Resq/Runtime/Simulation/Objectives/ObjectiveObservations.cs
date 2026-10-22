using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Resq.Content;
using Resq.Simulation.Rescue;

namespace Resq.Simulation.Objectives
{
    // Derived domain facts are interval observations, never a second saved authority.
    // Missing queried facts throw; later hazard owners must provide their actual
    // operational/path/region results rather than manufacturing safe defaults.
    public sealed class ObjectiveObservations
    {
        public long OperationTick { get; }
        public IReadOnlyDictionary<string, bool> InfrastructureOperational { get; }
        public IReadOnlyDictionary<string, bool> RoutesAvailable { get; }
        public IReadOnlyDictionary<string, long> RegionExposure { get; }
        public IReadOnlyDictionary<string, long> NodeOccupancyPeaks { get; }
        public ReadOnlyCollection<CompletedEdgePassage> EdgePassages { get; }
        public ReadOnlyCollection<DockWaitSample> DockWaits { get; }
        public ReadOnlyCollection<CompletedNodeDwell> NodeDwells { get; }
        public ObjectiveObservations(long operationTick, IDictionary<string, bool> infrastructureOperational, IDictionary<string, bool> routesAvailable, IDictionary<string, long> regionExposure, IEnumerable<CompletedEdgePassage> edgePassages, IEnumerable<DockWaitSample> dockWaits, IEnumerable<CompletedNodeDwell> nodeDwells, IDictionary<string, long> nodeOccupancyPeaks)
        {
            var value = CreateWork(operationTick, infrastructureOperational, routesAvailable, regionExposure, edgePassages, dockWaits, nodeDwells, nodeOccupancyPeaks).Drain(); OperationTick = value.OperationTick;
            InfrastructureOperational = value.InfrastructureOperational; RoutesAvailable = value.RoutesAvailable; RegionExposure = value.RegionExposure; NodeOccupancyPeaks = value.NodeOccupancyPeaks; EdgePassages = value.EdgePassages; DockWaits = value.DockWaits; NodeDwells = value.NodeDwells;
        }
        private ObjectiveObservations(long operationTick, ReadOnlyDictionary<string, bool> infrastructureOperational, ReadOnlyDictionary<string, bool> routesAvailable, ReadOnlyDictionary<string, long> regionExposure, ReadOnlyDictionary<string, long> nodeOccupancyPeaks, ReadOnlyCollection<CompletedEdgePassage> edgePassages, ReadOnlyCollection<DockWaitSample> dockWaits, ReadOnlyCollection<CompletedNodeDwell> nodeDwells)
        { OperationTick = operationTick; InfrastructureOperational = infrastructureOperational; RoutesAvailable = routesAvailable; RegionExposure = regionExposure; NodeOccupancyPeaks = nodeOccupancyPeaks; EdgePassages = edgePassages; DockWaits = dockWaits; NodeDwells = nodeDwells; }
        public static ResumableWork<ObjectiveObservations> CreateWork(long operationTick, IDictionary<string, bool> infrastructureOperational, IDictionary<string, bool> routesAvailable, IDictionary<string, long> regionExposure, IEnumerable<CompletedEdgePassage> edgePassages, IEnumerable<DockWaitSample> dockWaits, IEnumerable<CompletedNodeDwell> nodeDwells, IDictionary<string, long> nodeOccupancyPeaks) => new ResumableWork<ObjectiveObservations>(done => CreateRoutine(operationTick, infrastructureOperational, routesAvailable, regionExposure, edgePassages, dockWaits, nodeDwells, nodeOccupancyPeaks, done));
        private static IEnumerable<string> CreateRoutine(long operationTick, IDictionary<string, bool> infrastructureOperational, IDictionary<string, bool> routesAvailable, IDictionary<string, long> regionExposure, IEnumerable<CompletedEdgePassage> edgePassages, IEnumerable<DockWaitSample> dockWaits, IEnumerable<CompletedNodeDwell> nodeDwells, IDictionary<string, long> nodeOccupancyPeaks, Action<ObjectiveObservations> done)
        {
            Dictionary<string, bool> infrastructureOperationalCopy; using (var workinfrastructureOperationalCopy = DomainWork.Copy(infrastructureOperational, "objective.facts.infrastructureOperational")) { while (workinfrastructureOperationalCopy.MoveNext()) yield return workinfrastructureOperationalCopy.Stage; infrastructureOperationalCopy = workinfrastructureOperationalCopy.Result; }
            Dictionary<string, bool> routesAvailableCopy; using (var workroutesAvailableCopy = DomainWork.Copy(routesAvailable, "objective.facts.routesAvailable")) { while (workroutesAvailableCopy.MoveNext()) yield return workroutesAvailableCopy.Stage; routesAvailableCopy = workroutesAvailableCopy.Result; }
            Dictionary<string, long> regionExposureCopy; using (var workregionExposureCopy = DomainWork.Copy(regionExposure, "objective.facts.regionExposure")) { while (workregionExposureCopy.MoveNext()) yield return workregionExposureCopy.Stage; regionExposureCopy = workregionExposureCopy.Result; }
            Dictionary<string, long> nodeOccupancyPeaksCopy; using (var worknodeOccupancyPeaksCopy = DomainWork.Copy(nodeOccupancyPeaks, "objective.facts.nodeOccupancyPeaks")) { while (worknodeOccupancyPeaksCopy.MoveNext()) yield return worknodeOccupancyPeaksCopy.Stage; nodeOccupancyPeaksCopy = worknodeOccupancyPeaksCopy.Result; }
            List<CompletedEdgePassage> edgePassagesCopy; using (var workedgePassagesCopy = DomainWork.List(edgePassages, "objective.facts.edgePassages")) { while (workedgePassagesCopy.MoveNext()) yield return workedgePassagesCopy.Stage; edgePassagesCopy = workedgePassagesCopy.Result; }
            List<DockWaitSample> dockWaitsCopy; using (var workdockWaitsCopy = DomainWork.List(dockWaits, "objective.facts.dockWaits")) { while (workdockWaitsCopy.MoveNext()) yield return workdockWaitsCopy.Stage; dockWaitsCopy = workdockWaitsCopy.Result; }
            List<CompletedNodeDwell> nodeDwellsCopy; using (var worknodeDwellsCopy = DomainWork.List(nodeDwells, "objective.facts.nodeDwells")) { while (worknodeDwellsCopy.MoveNext()) yield return worknodeDwellsCopy.Stage; nodeDwellsCopy = worknodeDwellsCopy.Result; }
            foreach (long value in regionExposureCopy.Values) { if (value < 0) throw new InvalidOperationException("Invalid objective observations"); yield return "objective.facts.exposure.check"; }
            foreach (long value in nodeOccupancyPeaksCopy.Values) { if (value < 0) throw new InvalidOperationException("Invalid objective observations"); yield return "objective.facts.peak.check"; }
            var vehicles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var wait in dockWaitsCopy) { if (!vehicles.Add(wait.VehicleId)) throw new InvalidOperationException("Invalid objective observations"); yield return "objective.facts.dock.check"; }
            done(new ObjectiveObservations(operationTick, new ReadOnlyDictionary<string, bool>(infrastructureOperationalCopy), new ReadOnlyDictionary<string, bool>(routesAvailableCopy), new ReadOnlyDictionary<string, long>(regionExposureCopy), new ReadOnlyDictionary<string, long>(nodeOccupancyPeaksCopy), edgePassagesCopy.AsReadOnly(), dockWaitsCopy.AsReadOnly(), nodeDwellsCopy.AsReadOnly()));
        }
        public static string RouteKey(string route, string traversal) => route + ":" + traversal;
        public static string ExposureKey(string region, string hazard) => region + ":" + hazard;
    }
    public interface IObjectiveDomainObserver
    {
        // Must observe this exact fork's owners after movement/exposure. Read only.
        ObjectiveObservations ObserveObjectives(OperationContext context, JsonValue domainState);
    }
}
