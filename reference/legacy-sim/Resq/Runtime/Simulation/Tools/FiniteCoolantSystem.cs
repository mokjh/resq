using System;
using System.Collections.Generic;
using System.Linq;
using Resq.Content;
using Resq.Core;

namespace Resq.Simulation.Tools
{
    // Non-water stores only. Water remains exclusively WaterSystem authority.
    public sealed class FiniteCoolantSystem
    {
        private sealed class Job { public int Work, Units; public Job Copy() => (Job)MemberwiseClone(); }
        private readonly LayoutDef layout;
        private readonly SortedDictionary<string, ResourceStoreDef> stores;
        private readonly SortedDictionary<string, ProducerDef> producerDefinitions;
        private SortedDictionary<string, long> amounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private SortedDictionary<string, Job> jobs = new SortedDictionary<string, Job>(StringComparer.Ordinal);
        private long lastOperationTick;
        public FiniteCoolantSystem(LayoutDef layout) : this(layout, true)
        {
            using (var work = InitializeWork()) work.Drain();
        }
        private FiniteCoolantSystem(LayoutDef layout, bool deferred)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            stores = new SortedDictionary<string, ResourceStoreDef>(StringComparer.Ordinal);
            producerDefinitions = new SortedDictionary<string, ProducerDef>(StringComparer.Ordinal);
        }
        public static ResumableWork<FiniteCoolantSystem> CreateWork(LayoutDef layout)
            => new ResumableWork<FiniteCoolantSystem>(done => CreateRoutine(layout, done));
        private static IEnumerable<string> CreateRoutine(LayoutDef layout, Action<FiniteCoolantSystem> done)
        {
            var next = new FiniteCoolantSystem(layout, true);
            using (var work = next.InitializeWork()) { while (work.MoveNext()) yield return work.Stage; }
            done(next);
        }
        private ResumableWork<bool> InitializeWork() => new ResumableWork<bool>(InitializeRoutine);
        private IEnumerable<string> InitializeRoutine(Action<bool> done)
        {
            yield return "coolant.create.begin";
            foreach (var store in layout.resources)
            {
                if (store.unit != "water_mm3") { amounts.Add(store.id, store.initial_amount); stores.Add(store.id, store); }
                yield return "coolant.create.store";
            }
            foreach (var producer in layout.producers)
            {
                if (Store(producer.input_store_id).unit != "coolant_feedstock" || Store(producer.output_store_id).unit != "coolant_charge" || producer.input_per_charge != 1 || producer.production_scenario_ticks != 100 || producer.power_demand != 1) throw new InvalidOperationException("Invalid finite producer binding");
                jobs.Add(producer.id, new Job());
                producerDefinitions.Add(producer.id, producer);
                yield return "coolant.create.producer";
            }
            using (var check = ValidateBoundaryWork()) { while (check.MoveNext()) yield return check.Stage; }
            done(true);
        }
        private FiniteCoolantSystem(FiniteCoolantSystem other)
        {
            layout = other.layout; stores = other.stores; producerDefinitions = other.producerDefinitions; lastOperationTick = other.lastOperationTick;
            
        }
        public FiniteCoolantSystem Fork() => ForkWork().Drain();
        public ResumableWork<FiniteCoolantSystem> ForkWork() => new ResumableWork<FiniteCoolantSystem>(ForkRoutine);
        private IEnumerable<string> ForkRoutine(Action<FiniteCoolantSystem> done)
        {
            var next = new FiniteCoolantSystem(this);
            foreach (var pair in amounts) { next.amounts.Add(pair.Key, pair.Value); yield return "coolant.fork.amount"; }
            foreach (var pair in jobs) { next.jobs.Add(pair.Key, pair.Value.Copy()); yield return "coolant.fork.job"; }
            done(next);
        }
        // Initialization rejects duplicate non-water stores/jobs before publishing
        // these indexes. Their definitions are immutable and may be shared by forks.
        private ResourceStoreDef Store(string id)
        {
            if (id != null && stores.TryGetValue(id, out var store)) return store;
            throw new InvalidOperationException("Sequence contains no matching element");
        }
        private ProducerDef Producer(string id)
        {
            if (id != null && producerDefinitions.TryGetValue(id, out var producer)) return producer;
            throw new InvalidOperationException("Sequence contains no matching element");
        }
        public long Amount(string id) { Store(id); return amounts[id]; }
        public void TakeCharge(string id)
        { if (Store(id).unit != "coolant_charge" || amounts[id] < 1) throw new InvalidOperationException("RESOURCE_EMPTY"); amounts[id]--; }
        public void Pulse(OperationContext context, IReadOnlyDictionary<string, bool> ready) => PulseWork(context, ready).Drain();
        public ResumableWork<bool> PulseWork(OperationContext context, IReadOnlyDictionary<string, bool> ready) => new ResumableWork<bool>(done => PulseRoutine(context, ready, done));
        private IEnumerable<string> PulseRoutine(OperationContext context, IReadOnlyDictionary<string, bool> ready, Action<bool> done)
        {
            if (context.OperationTick <= lastOperationTick) throw new InvalidOperationException("Exact producer readiness and fresh operation boundary required");
            string[] readyIds;
            using (var sort = ResumableSort.Create(ready.Keys, StringComparer.Ordinal)) { while (sort.MoveNext()) yield return sort.Stage; readyIds = sort.Result; }
            using (var check = EqualKeysWork(readyIds, jobs.Keys)) { while (check.MoveNext()) yield return check.Stage; if (!check.Result) throw new InvalidOperationException("Exact producer readiness and fresh operation boundary required"); }
            lastOperationTick = context.OperationTick;
            if (!context.HazardPulse) { done(true); yield break; }
            // Shared output capacity is reserved by WIP jobs, not independently
            // available to every producer targeting one physical store.
            foreach (var producer in producerDefinitions.Values)
            {yield return "coolant.pulse"; 
                var job = jobs[producer.id]; if (!ready[producer.id]) continue;
                long reserved = 0;
                foreach (var candidate in layout.producers) { if (candidate.output_store_id == producer.output_store_id) reserved = checked(reserved + (long)jobs[candidate.id].Units); yield return "coolant.pulse.reservation"; }
                if (job.Units == 0)
                {
                    if (amounts[producer.input_store_id] == 0 || amounts[producer.output_store_id] + reserved >= Store(producer.output_store_id).capacity) continue;
                    amounts[producer.input_store_id]--; job.Units = 1; job.Work = 0;
                }
                job.Work++;
                if (job.Work == 100) { amounts[producer.output_store_id]++; job.Units = 0; job.Work = 0; }
            }
            using (var validation = ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; }
        
            done(true);
        }
        public JsonValue ReadStores() => ReadStoresWork().Drain();
        public ResumableWork<JsonValue> ReadStoresWork() => DomainWork.Rows(amounts, x => EquipmentJson.O("id", x.Key, "unit", Store(x.Key).unit, "amount", x.Value), "coolant.read.store");
        public JsonValue ReadProducers() => ReadProducersWork().Drain();
        public ResumableWork<JsonValue> ReadProducersWork() => DomainWork.Rows(jobs, x => EquipmentJson.O("id", x.Key, "work_in_progress_units", x.Value.Units, "completed_work_scenario_ticks", x.Value.Work,
            "reserved_input_store_id", x.Value.Units == 0 ? null : Producer(x.Key).input_store_id), "coolant.read.producer");
        public void ValidateBoundary() => ValidateBoundaryWork().Drain();
        public ResumableWork<bool> ValidateBoundaryWork() => new ResumableWork<bool>(done => ValidateBoundaryRoutine(done));
        private IEnumerable<string> ValidateBoundaryRoutine(Action<bool> done)
        {
            foreach (var store in layout.resources)
            { yield return "coolant.validate"; if (store.unit != "water_mm3" && (amounts[store.id] < 0 || amounts[store.id] > store.capacity)) throw new InvalidOperationException("Finite store bounds"); }
            long consumedFeed = 0, charges = 0, initialCharges = 0;
            foreach (var store in layout.resources) { if (store.unit == "coolant_feedstock") { long consumed = store.initial_amount - amounts[store.id]; consumedFeed = checked(consumedFeed + consumed); } yield return "coolant.validate.feedstock"; }
            foreach (var store in layout.resources) { if (store.unit == "coolant_charge") charges = checked(charges + amounts[store.id]); yield return "coolant.validate.charge"; }
            foreach (var store in layout.resources) { if (store.unit == "coolant_charge") initialCharges = checked(initialCharges + store.initial_amount); yield return "coolant.validate.initial_charge"; }
            if (consumedFeed < 0) throw new InvalidOperationException("Finite feedstock/WIP/charge conservation");
            long wip = 0;
            foreach (var job in jobs.Values) { wip = checked(wip + (long)job.Units); yield return "coolant.validate.wip"; }
            if (charges > initialCharges + consumedFeed - wip) throw new InvalidOperationException("Finite feedstock/WIP/charge conservation");
            foreach (var producer in layout.producers)
            {yield return "coolant.validate"; 
                var job = jobs[producer.id]; if (job.Units < 0 || job.Units > 1 || job.Work < 0 || job.Work > 99 || job.Units == 0 && job.Work != 0) throw new InvalidOperationException("Producer WIP bounds");
                if (amounts[producer.input_store_id] > Store(producer.input_store_id).initial_amount) throw new InvalidOperationException("Feedstock duplication");
            }
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var producer in layout.producers)
            {
                string output = producer.output_store_id;
                yield return "coolant.validate";
                if (!outputs.Add(output)) continue;
                long amount = amounts[output], reserved = 0;
                foreach (var candidate in layout.producers) { if (candidate.output_store_id == output) reserved = checked(reserved + (long)jobs[candidate.id].Units); yield return "coolant.validate.reservation"; }
                if (amount + reserved > Store(output).capacity) throw new InvalidOperationException("Producer capacity reservations");
            }
        
            done(true);
        }
        // C07 supplies the saved operation boundary; no private continuation clock
        // is inferred from capped recent events or wall time.
        public FiniteCoolantSystem Restore(JsonValue stores, JsonValue producers, long operationTick) => RestoreWork(stores, producers, operationTick).Drain();
        public ResumableWork<FiniteCoolantSystem> RestoreWork(JsonValue stores, JsonValue producers, long operationTick) => new ResumableWork<FiniteCoolantSystem>(done => RestoreRoutine(stores, producers, operationTick, done));
        private IEnumerable<string> RestoreRoutine(JsonValue stores, JsonValue producers, long operationTick, Action<FiniteCoolantSystem> done)
        {
            FiniteCoolantSystem copy; using (var workcopy = ForkWork()) { while (workcopy.MoveNext()) yield return workcopy.Stage; copy = workcopy.Result; } copy.amounts.Clear(); copy.jobs.Clear();
            foreach (var row in stores.Items) {yield return "coolant.restore";  EquipmentJson.Fields(row, "id", "unit", "amount"); var store = copy.Store(row["id"].Text); if (row["unit"].Text != store.unit || row["amount"].Kind != "integer") throw new InvalidOperationException("Store identity"); copy.amounts.Add(store.id, row["amount"].Integer); }
            foreach (var row in producers.Items)
            {yield return "coolant.restore"; 
                EquipmentJson.Fields(row, "id", "work_in_progress_units", "completed_work_scenario_ticks", "reserved_input_store_id");
                var definition = Producer(row["id"].Text);
                long units = row["work_in_progress_units"].Integer, work = row["completed_work_scenario_ticks"].Integer;
                if (units < 0 || units > 1 || work < 0 || work > 99 || (units == 0 ? !row["reserved_input_store_id"].IsNull : row["reserved_input_store_id"].Text != definition.input_store_id)) throw new InvalidOperationException("Producer input reservation");
                copy.jobs.Add(definition.id, new Job { Units = (int)units, Work = (int)work });
            }
            using (var check = EqualKeysWork(copy.amounts.Keys, amounts.Keys)) { while (check.MoveNext()) yield return check.Stage; if (!check.Result) throw new InvalidOperationException("Exact coolant state set required"); }
            using (var check = EqualKeysWork(copy.jobs.Keys, jobs.Keys)) { while (check.MoveNext()) yield return check.Stage; if (!check.Result) throw new InvalidOperationException("Exact coolant state set required"); }
            if (operationTick < 0 || operationTick > Tick.Maximum) throw new InvalidOperationException("Exact coolant state set required");
            copy.lastOperationTick = operationTick; using (var validation = copy.ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; } done(copy);
                }
        private static ResumableWork<bool> EqualKeysWork(IEnumerable<string> actual, IEnumerable<string> expected)
            => new ResumableWork<bool>(done => EqualKeysRoutine(actual, expected, done));
        private static IEnumerable<string> EqualKeysRoutine(IEnumerable<string> actual, IEnumerable<string> expected, Action<bool> done)
        {
            using (var a = actual.GetEnumerator()) using (var b = expected.GetEnumerator())
            {
                while (a.MoveNext())
                {
                    if (!b.MoveNext() || a.Current != b.Current) { done(false); yield break; }
                    yield return "coolant.validate.exact";
                }
                done(!b.MoveNext());
            }
        }
    }
}
