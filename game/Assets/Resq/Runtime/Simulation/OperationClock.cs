using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Resq.Content;
using Resq.Core;

namespace Resq.Simulation
{
    public readonly struct OperationContext
    {
        public long OperationTick { get; }
        public long ScenarioTick { get; }
        public bool HazardPulse { get; }
        public RuntimeEventSink Events { get; }
        public OperationContext(long operationTick, long scenarioTick, bool hazardPulse, RuntimeEventSink events = null) { OperationTick = operationTick; ScenarioTick = scenarioTick; HazardPulse = hazardPulse; Events = events; }
    }
    public sealed class OutcomeResult
    {
        public bool Success { get; }
        public int Stars { get; }
        public int Score { get; }
        public int Rescued { get; }
        public int BackedUp { get; }
        public OutcomeResult(bool success, int stars, int score, int rescued, int backedUp)
        {
            if (stars < 0 || stars > 3 || score < 0 || score > 10000 || rescued < 0 || backedUp < 0) throw new ArgumentOutOfRangeException("outcome");
            Success = success; Stars = stars; Score = score; Rescued = rescued; BackedUp = backedUp;
        }
        public JsonValue ToJson(string reason) => JsonValue.Object(new Dictionary<string, JsonValue> { {"success",JsonValue.Bool(Success)},{"stars",JsonValue.Number(Stars)},{"score",JsonValue.Number(Score)},{"rescued",JsonValue.Number(Rescued)},{"backed_up",JsonValue.Number(BackedUp)},{"reason",JsonValue.String(Success ? "success" : reason)} });
    }

    // Mandatory integration boundary for C03-C07. There is deliberately no
    // production no-op implementation or fallback success engine. Fork must
    // isolate all mutable domain state, so faults retain the preceding boundary.
    public interface IOperationSystems
    {
        IOperationSystems Fork();
        JsonValue ReadDomainState();
        ValidationResult Validate(GameCommand command, OperationContext context);
        void Apply(GameCommand command, OperationContext context);
        void EvaluateTides(OperationContext context);
        void PublishWarning(EventDef definition, OperationContext context);
        void ApplyScheduled(ResolvedEventOperation operation, OperationContext context);
        void ResolvePowerAndReservations(OperationContext context);
        void SimulateHazardInterval(OperationContext context);
        void RecomputeSafetyAndDamage(OperationContext context);
        void ResolveMovementAndTransfers(OperationContext context);
        void DeriveMetricsAndObjectives(OperationContext context);
        void EmitMilestone(string milestoneId, OperationContext context);
        void SampleInitialObjectives(OperationContext context);
        bool GuidedEpochReady { get; }
        bool RequiredConditionsHold { get; }
        OutcomeResult EvaluateOutcome(OperationContext context);
        void ValidateBoundary(OperationContext context);
    }

    public sealed class SimulationSnapshot
    {
        public JsonValue Value { get; }
        public string StateSha256 { get; }
        public SimulationSnapshot(JsonValue value) : this(value, CanonicalState.RuleHash(value)) { }
        private SimulationSnapshot(JsonValue value, string hash) { Value = value; StateSha256 = hash; }
        internal static ResumableWork<SimulationSnapshot> CreateWork(JsonValue value)
            => new ResumableWork<SimulationSnapshot>(done => CreateSteps(value, done));
        private static IEnumerable<string> CreateSteps(JsonValue value, Action<SimulationSnapshot> complete)
        {
            using (var hash = CanonicalState.RuleHashWork(value))
            {
                while (hash.MoveNext()) yield return hash.Stage;
                complete(new SimulationSnapshot(value, hash.Result));
            }
        }
    }
    public sealed class StepResult
    {
        public long OperationTick { get; }
        public long ScenarioTick { get; }
        public long StateRevision { get; }
        public string StateSha256 { get; }
        public bool Terminal { get; }
        public ReadOnlyCollection<RuntimeEvent> Events { get; }
        public StepResult(SimulationSnapshot snapshot, IEnumerable<RuntimeEvent> events) { var state = snapshot.Value; OperationTick = state["operation_tick"].Integer; ScenarioTick = state["scenario_tick"].Integer; StateRevision = state["state_revision"].Integer; StateSha256 = snapshot.StateSha256; Terminal = state["phase"].Text == "outcome"; Events = events.ToList().AsReadOnly(); }
    }
    public sealed class StepBatchResult
    {
        public ReadOnlyCollection<StepResult> Steps { get; }
        public string Fault { get; }
        public StepBatchResult(IEnumerable<StepResult> steps, string fault) { Steps = steps.ToList().AsReadOnly(); Fault = fault; }
    }
    public sealed class ApplicationResult
    {
        public CommandResult Receipt { get; }
        public string DesiredTransition { get; }
        public StepBatchResult Advanced { get; }
        public ApplicationResult(CommandResult receipt, string desiredTransition, StepBatchResult advanced) { Receipt = receipt; DesiredTransition = desiredTransition; Advanced = advanced; }
    }

    public interface ISimulationClock
    {
        bool IsPaused { get; }
        ApplicationResult Apply(GameCommand command);
        StepBatchResult Advance(int operationTicks);
    }

    public sealed class OperationClock : ISimulationClock
    {
        private readonly CompiledContent content;
        private readonly CompiledMission mission;
        private readonly string layoutId;
        private readonly string profileId;
        private readonly long numerator;
        private readonly JsonValue identity;
        // Assigned only by the private initialization job, before publication.
        private EventDef[] events;
        // Null for every ordinary/live/replay owner. Only an isolated forecast
        // freezes this set after exact saved-boundary validation has succeeded.
        private HashSet<string> forecastKnownEvents;
        private readonly Func<int, GameCommand> advanceCommandFactory;
        private RuntimeEventSink eventSink = new RuntimeEventSink();
        private IOperationSystems systems;
        private CommandLedger ledger = new CommandLedger();
        private List<GameCommand> pending = new List<GameCommand>();
        private SortedSet<string> warned = new SortedSet<string>(StringComparer.Ordinal);
        private SortedSet<string> fired = new SortedSet<string>(StringComparer.Ordinal);
        private SortedSet<string> revealed = new SortedSet<string>(StringComparer.Ordinal);
        private List<JsonValue> engineMilestones = new List<JsonValue>();
        private XorShift32 rng;
        private long operationTick, scenarioTick, revision;
        private int accumulator;
        private bool scenarioEnabled, insideStep, insideAdmission;
        private string phase = "planning", applicationState = "paused";
        private JsonValue endRequest = JsonValue.Null(), outcome = JsonValue.Null();
        private bool trackingAvailable = true;
        private string interruptionReason;
        public string Fault { get; private set; }
        public bool IsPaused => applicationState != "running";
        public long OperationTick => operationTick;
        public long StateRevision => revision;
        private SimulationSnapshot completedStepSnapshot;
        public SimulationSnapshot ConsumeCompletedStepSnapshot(StepResult step)
        {
            var snapshot = completedStepSnapshot;
            if (insideStep || insideAdmission || snapshot == null || step == null || step.StateRevision != revision || step.OperationTick != operationTick || step.StateSha256 != snapshot.StateSha256 || snapshot.Value["phase"].Text != phase)
                throw new InvalidOperationException("Completed snapshot does not belong to this unchanged step boundary");
            completedStepSnapshot = null; return snapshot;
        }
        public CommandReceipt ReadReceipt(string commandId)
            => ledger.TryGetReceipt(commandId, out var receipt) ? receipt : null;
        public long ScenarioTick => scenarioTick;
        public int ScenarioAccumulator => accumulator;
        public bool ScenarioEnabled => scenarioEnabled;
        public string Phase => phase;
        public string ApplicationState => applicationState;
        public long NextSequence => ledger.LastAdmittedSequence + 1;
        public ReadOnlyCollection<CommandReceipt> Receipts => ledger.Receipts;

        public OperationClock(CompiledContent content, string missionVersionId, string layoutId, JsonValue identity, IOperationSystems systems, Func<int, GameCommand> advanceCommandFactory = null)
            : this(content, missionVersionId, layoutId, identity, systems, advanceCommandFactory, true) { }
        private OperationClock(CompiledContent content, string missionVersionId, string layoutId, JsonValue identity, IOperationSystems systems, Func<int, GameCommand> advanceCommandFactory, bool initialize, bool deferEvents = false)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            this.systems = systems ?? throw new ArgumentNullException(nameof(systems));
            this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
            RunIdentity.Validate(identity);
            this.advanceCommandFactory = advanceCommandFactory;
            this.layoutId = layoutId;
            string challengeId = identity["challenge_id"].Text;
            string expectedModifiers;
            if (challengeId == "base") { this.content = content; expectedModifiers = JsonValue.Array(new JsonValue[0]).Sha256(); }
            else
            {
                var challenge = content.Envelope.payload.challenges.SingleOrDefault(item => item.id == challengeId && item.base_mission_version_id == missionVersionId);
                if (challenge == null || !content.ChallengeVariants.TryGetValue(challengeId, out var variant)) throw new InvalidOperationException("CONTENT_VERSION_MISMATCH: missing named expanded challenge");
                // The complete immutable modifier definition binds replacement
                // stock and budget as well as operations; no hidden projection.
                expectedModifiers = challenge.ToJson().Sha256(); this.content = variant;
            }
            content = this.content;
            mission = content.Envelope.payload.missions.Single(item => item.mission_version_id == missionVersionId);
            var layout = content.Envelope.payload.layouts.Single(item => item.id == layoutId && item.mission_version_id == missionVersionId);
            profileId = identity["rule_profile_id"].Text;
            var profile = content.Envelope.payload.rule_profiles.Single(item => item.id == profileId);
            string adapter = identity["input_adapter_id"].Text, guidance = identity["guidance_policy"].Text;
            // Mixed is saved hands/controller provenance, not a new physical
            // adapter or permission to start a run with unspecified input.
            bool restoredMixed = !initialize && adapter == "mixed" && profile.permitted_input_adapters.Contains("hands") && profile.permitted_input_adapters.Contains("controller");
            if (!profile.permitted_input_adapters.Contains(adapter) && !restoredMixed || identity["modifiers_hash"].Text != expectedModifiers) throw new InvalidOperationException("CONTENT_VERSION_MISMATCH: profile/adapter/modifier binding");
            if (adapter == "head_dwell" && profileId != "command" || challengeId != "base" && (profileId != "standard" || adapter != "hands" && adapter != "controller" && !restoredMixed)) throw new InvalidOperationException("CONTENT_VERSION_MISMATCH: unsupported rules/input/challenge combination");
            if (guidance != "unguided" && guidance != "guided_first_play" && guidance != "world_mechanic_guidance") throw new InvalidOperationException("CONTENT_VERSION_MISMATCH: unknown guidance policy");
            if (guidance == "guided_first_play" && (mission.id != "W1-M1" || mission.tutorial_policy != "guided_first_play_then_unguided") || guidance == "world_mechanic_guidance" && mission.tutorial_policy != "world_mechanic_guidance") throw new InvalidOperationException("CONTENT_VERSION_MISMATCH: guidance unavailable for mission");
            if (!mission.rule_profile_ids.Contains(profileId) || identity["mission_version_id"].Text != missionVersionId || identity["content_hash"].Text != content.ContentHash || identity["layout_version_id"].Text != layoutId || identity["layout_signature"].Text != content.LayoutSignature(layoutId) || identity["rules_version"].Text != "resq-rules-1") throw new InvalidOperationException("CONTENT_VERSION_MISMATCH");
            numerator = profile.scenario_numerator;
            if (profile.scenario_denominator != 2 || numerator != (profileId == "relaxed" ? 1 : 2)) throw new InvalidOperationException("clock profile mismatch");
            rng = new XorShift32(identity["seed"].Integer);
            scenarioEnabled = identity["guidance_policy"].Text != "guided_first_play";
            if (deferEvents) return;
            InitializeEventsWork().Drain();
            if (!initialize) return;
            eventSink.BeginBoundary(0, 0);
            var context = new OperationContext(0, 0, false, eventSink);
            if (scenarioEnabled) Milestone("operation_started", context);
            PublishWarnings(0, context);
            this.systems.SampleInitialObjectives(context);
            this.systems.ValidateBoundary(context);
            ReadSnapshot(); // Missing/extra domain fields fail before a run exists.
        }
        private ResumableWork<bool> InitializeEventsWork() => new ResumableWork<bool>(InitializeEventsRoutine);
        private IEnumerable<string> InitializeEventsRoutine(Action<bool> done)
        {
            using (var sort = ResumableSort.Create(mission.events, Comparer<EventDef>.Create((a, b) =>
            { int tick = a.at_scenario_tick.CompareTo(b.at_scenario_tick); return tick != 0 ? tick : string.CompareOrdinal(a.id, b.id); })))
            { while (sort.MoveNext()) yield return sort.Stage; events = sort.Result; }
            foreach (var definition in events)
            {
                if (definition.at_scenario_tick < 1 || definition.at_scenario_tick >= mission.duration_scenario_ticks || definition.warning_at_scenario_tick < 0 || definition.warning_at_scenario_tick > definition.at_scenario_tick) throw new InvalidOperationException("EVENT_BOUND");
                if (definition.known_at_start) revealed.Add(definition.id);
                yield return "clock.create.event";
            }
            done(true);
        }

        // The caller rebuilds and validates each actual domain owner before
        // transferring it here, including its engine milestone notifications.
        // No new-run callback, warning, objective sample or command is replayed.
        // Build provenance compatibility belongs to the persistence factory.
        public static OperationClock Restore(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems, Func<int, GameCommand> advanceCommandFactory = null)
            => RestoreWork(content, snapshot, restoredSystems, advanceCommandFactory).Drain();
        public static ResumableWork<OperationClock> RestoreWork(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems, Func<int, GameCommand> advanceCommandFactory = null)
            => new ResumableWork<OperationClock>(done => RestoreSteps(content, snapshot, restoredSystems, advanceCommandFactory, done));
        private static IEnumerable<string> RestoreSteps(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems, Func<int, GameCommand> advanceCommandFactory, Action<OperationClock> complete)
        {            SavedFields(snapshot, DomainFields.Concat(new[] { "schema_version", "identity", "state_revision", "phase", "operation_tick", "scenario_tick", "scenario_accumulator", "scenario_enabled", "rng", "last_admitted_sequence", "pending_commands", "command_receipts", "end_request", "outcome" }).ToArray());
            SavedNumber(snapshot["schema_version"], 1, 1); SavedNumber(snapshot["state_revision"], 0, int.MaxValue);
            SavedNumber(snapshot["operation_tick"], 0, Tick.Maximum); SavedNumber(snapshot["scenario_tick"], 0, Tick.Maximum); SavedNumber(snapshot["scenario_accumulator"], 0, 1);
            SavedNumber(snapshot["last_admitted_sequence"], 0, 10000);
            using (var size = snapshot.CanonicalByteCountWork())
            {
                while (size.MoveNext()) yield return size.Stage;
                if (size.Result > 16 * 1024 * 1024) throw new InvalidOperationException("Saved current-run payload exceeds 16 MiB");
            }
            var id = snapshot["identity"]; RunIdentity.Validate(id);
            var copy = new OperationClock(content, id["mission_version_id"].Text, id["layout_version_id"].Text, id, restoredSystems, advanceCommandFactory, false, true);
            yield return "restore.clock.binding";
            using (var initialize = copy.InitializeEventsWork()) { while (initialize.MoveNext()) yield return initialize.Stage; }
            copy.operationTick = snapshot["operation_tick"].Integer;
            copy.scenarioTick = snapshot["scenario_tick"].Integer;
            copy.accumulator = (int)snapshot["scenario_accumulator"].Integer;
            copy.scenarioEnabled = snapshot["scenario_enabled"].Boolean;
            copy.revision = snapshot["state_revision"].Integer;
            copy.phase = snapshot["phase"].Text;
            copy.endRequest = snapshot["end_request"];
            copy.outcome = snapshot["outcome"];
            copy.rng = XorShift32.Restore(snapshot["rng"]);
            yield return "restore.clock.metadata";
            if (copy.phase != "planning" && copy.phase != "active" && copy.phase != "stabilizing" && copy.phase != "outcome") throw new InvalidOperationException("Unknown saved operation phase");
            SavedArray(snapshot["pending_commands"], 32);
            copy.pending = new List<GameCommand>();
            foreach (var row in snapshot["pending_commands"].Items) { copy.pending.Add(GameCommand.Read(row)); yield return "restore.pending_command"; }
            using (var ledgerWork = CommandLedger.RestoreWork(snapshot["command_receipts"], copy.pending, id["run_id"].Text, snapshot["last_admitted_sequence"].Integer, copy.operationTick, copy.revision))
            { while (ledgerWork.MoveNext()) yield return ledgerWork.Stage; copy.ledger = ledgerWork.Result; }
            foreach (var unit in copy.RestoreMilestonesAndClocksSteps(snapshot["milestones"])) yield return unit;
            foreach (var unit in copy.RestoreStaticEventsSteps(snapshot["event_state"])) yield return unit;
            using (var eventWork = RuntimeEventSink.RestoreWork(snapshot["recent_events"], copy.operationTick, copy.scenarioTick))
            { while (eventWork.MoveNext()) yield return eventWork.Stage; copy.eventSink = eventWork.Result; }
            copy.ValidateRestoredTerminal(snapshot); yield return "restore.clock.terminal";
            foreach (var unit in copy.ValidateRestoredDiagnosticsSteps(snapshot)) yield return unit;
            var context = new OperationContext(copy.operationTick, copy.scenarioTick, false);
            using (var validate = StageWork(copy.systems, OperationStage.ValidateBoundary, context)) { while (validate.MoveNext()) yield return validate.Stage; }
            bool required;
            using (var conditions = ConditionsWork(copy.systems)) { while (conditions.MoveNext()) yield return conditions.Stage; required = conditions.Result; }
            foreach (var command in copy.pending)
            {
                ValidationResult preview;
                using (var validation = DomainWork.Validate(copy.systems, command, context))
                { while (validation.MoveNext()) yield return validation.Stage; preview = ValidationResult.First(validation.Result); }
                copy.ledger.TryGetReceipt(command.CommandId, out var receipt);
                if (!preview.Allowed || !preview.AffectedIds.SequenceEqual(receipt.Result.AffectedIds) || command.Kind == "end_operation" && !((EndOperation)command.Payload).ConfirmFailure && !required) throw new InvalidOperationException("Saved pending command disagrees with its boundary preview");
                yield return "restore.pending_preview";
            }
            if (copy.operationTick > 0 && !copy.scenarioEnabled && copy.systems.GuidedEpochReady) throw new InvalidOperationException("Saved guided epoch missed its completed ready boundary");
            // O=0 Active can follow the Start countdown before stage 9 has ever
            // selected Active/Stabilizing. Subsequent boundaries must agree.
            if (copy.operationTick > 0 && copy.phase != "outcome" && (copy.phase == "stabilizing") != required) throw new InvalidOperationException("Saved phase disagrees with current required conditions");
            using (var readback = copy.ReadSnapshotWork())
            {
                while (readback.MoveNext()) yield return readback.Stage;
                using (var equal = readback.Result.Value.CanonicalEqualsWork(snapshot))
                {
                    while (equal.MoveNext()) yield return equal.Stage;
                    if (!equal.Result) throw new InvalidOperationException("Restored domain/clock differs from the complete saved boundary");
                }
            }
            // Application lifecycle is not in Snapshot. Continue is explicitly
            // paused; ordinary loading preserves the saved assistance identity.
            copy.applicationState = "paused";
            complete(copy);
        }
        // Offline replay has no presentation lifecycle in Snapshot. This entry
        // point resumes only its recorded active phase without fabricating an
        // input command or revision. A replay beginning with a recorded paused
        // application operation must use ordinary Restore instead.
        public static OperationClock RestoreForReplay(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems, Func<int, GameCommand> advanceCommandFactory = null)
            => RestoreForReplayWork(content, snapshot, restoredSystems, advanceCommandFactory).Drain();
        public static ResumableWork<OperationClock> RestoreForReplayWork(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems, Func<int, GameCommand> advanceCommandFactory = null)
            => new ResumableWork<OperationClock>(done => RestoreReplaySteps(content, snapshot, restoredSystems, advanceCommandFactory, done));
        private static IEnumerable<string> RestoreReplaySteps(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems, Func<int, GameCommand> advanceCommandFactory, Action<OperationClock> complete)
        {
            OperationClock copy;
            using (var work = RestoreWork(content, snapshot, restoredSystems, advanceCommandFactory))
            { while (work.MoveNext()) yield return work.Stage; copy = work.Result; }
            copy.applicationState = copy.phase == "active" || copy.phase == "stabilizing" ? "running" : copy.phase == "outcome" ? "outcome" : "paused";
            complete(copy);
        }

        // Forecast is a hypothetical continuation of this exact boundary. The
        // source lifecycle is never touched and no fabricated resume command or
        // recovery journal enters the ledger. Planning becomes Active only in
        // the clone; real queued commands still execute through normal stage 3.
        public static OperationClock RestoreForForecast(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems)
            => RestoreForForecastWork(content, snapshot, restoredSystems).Drain();
        public static ResumableWork<OperationClock> RestoreForForecastWork(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems)
            => new ResumableWork<OperationClock>(done => RestoreForecastSteps(content, snapshot, restoredSystems, done));
        private static IEnumerable<string> RestoreForecastSteps(CompiledContent content, JsonValue snapshot, IOperationSystems restoredSystems, Action<OperationClock> complete)
        {
            OperationClock copy;
            using (var restore = RestoreWork(content, snapshot, restoredSystems))
            { while (restore.MoveNext()) yield return restore.Stage; copy = restore.Result; }
            if (copy.phase == "outcome") throw new InvalidOperationException("Terminal boundaries cannot forecast new commands");
            copy.forecastKnownEvents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in snapshot["event_state"]["revealed_event_ids"].Items)
            { copy.forecastKnownEvents.Add(row.Text); yield return "restore.forecast.knowledge"; }
            if (!(copy.systems is Resq.Simulation.Objectives.ObjectiveOperationSystems owner)) throw new InvalidOperationException("Actual forecast domain owner required");
            owner.FreezeForecastKnowledge(copy.forecastKnownEvents.ToList().AsReadOnly());
            if (copy.phase == "planning") copy.phase = "active";
            copy.applicationState = "running";
            yield return "restore.forecast.activate";
            complete(copy);
        }
        public ForecastBoundaryObservations ReadForecastBoundaryObservations() => ReadForecastBoundaryObservationsWork().Drain();
        public ResumableWork<ForecastBoundaryObservations> ReadForecastBoundaryObservationsWork()
            => new ResumableWork<ForecastBoundaryObservations>(ReadForecastObservationSteps);
        private IEnumerable<string> ReadForecastObservationSteps(Action<ForecastBoundaryObservations> complete)
        {
            Boundary();
            if (!(systems is IResumableOperationSystems owner)) throw new InvalidOperationException("Actual resumable objective/domain observation owner required");
            using (var work = owner.ObservationsWork())
            {
                while (work.MoveNext()) yield return work.Stage;
                if (work.Result.OperationTick != operationTick) throw new InvalidOperationException("Forecast observations do not match completed clock boundary");
                complete(work.Result);
            }
        }

        private IEnumerable<string> RestoreMilestonesAndClocksSteps(JsonValue saved)
        {
            SavedArray(saved, mission.milestones.Count + 2);
            string previous = null;
            foreach (var row in saved.Items)
            {
                SavedFields(row, "id", "operation_tick", "scenario_tick");
                string id = row["id"].Text;
                if (previous != null && string.CompareOrdinal(previous, id) >= 0) throw new InvalidOperationException("Saved milestone IDs must be sorted and unique");
                previous = id; SavedNumber(row["operation_tick"], 0, operationTick); SavedNumber(row["scenario_tick"], 0, scenarioTick);
                if (id == "operation_started" || id == "operation_ended") engineMilestones.Add(row);
                else if (!mission.milestones.Any(x => x.id == id)) throw new InvalidOperationException("Unbound saved milestone");
                yield return "restore.clock.milestone";
            }
            var start = engineMilestones.SingleOrDefault(x => x["id"].Text == "operation_started");
            bool guided = identity["guidance_policy"].Text == "guided_first_play";
            if (scenarioEnabled != (start != null) || !guided && (!scenarioEnabled || start["operation_tick"].Integer != 0)) throw new InvalidOperationException("Saved scenario epoch milestone mismatch");
            if (start != null && (start["scenario_tick"].Integer != 0 || guided && start["operation_tick"].Integer == 0)) throw new InvalidOperationException("Saved scenario epoch start boundary mismatch");
            long elapsed = start == null ? 0 : operationTick - start["operation_tick"].Integer;
            if (scenarioTick != elapsed * numerator / 2 || accumulator != elapsed * numerator % 2 || scenarioTick > mission.duration_scenario_ticks) throw new InvalidOperationException("Saved operation/scenario clock/remainder mismatch");
            if (phase == "planning" && operationTick != 0) throw new InvalidOperationException("Planning cannot contain completed operation steps");
            if (operationTick == 0 && phase != "planning" && (phase != "active" || ledger.LastAdmittedSequence == 0 || revision < ledger.LastAdmittedSequence + 1)) throw new InvalidOperationException("Saved initial phase requires a completed Start countdown");
            foreach (var row in saved.Items)
            { if (row["scenario_tick"].Integer != ScenarioAtOperation(row["operation_tick"].Integer)) throw new InvalidOperationException("Saved milestone scenario clock mismatch"); yield return "restore.clock.milestone_scenario"; }
        }
        private long ScenarioAtOperation(long atOperationTick)
        {
            var start = engineMilestones.SingleOrDefault(x => x["id"].Text == "operation_started");
            return start == null || atOperationTick <= start["operation_tick"].Integer ? 0 : (atOperationTick - start["operation_tick"].Integer) * numerator / 2;
        }
        private IEnumerable<string> RestoreStaticEventsSteps(JsonValue saved)
        {
            SavedFields(saved, "next_static_event_index", "fired_event_ids", "revealed_event_ids", "warned_event_ids", "dynamic_events", "administrative_edge_enabled", "edge_current_multipliers", "gate_open", "group_held", "wind_region_codes", "cooler_socket_enabled", "shelter_region_enabled", "resource_warning_thresholds");
            var expectedFired = new List<string>(); var expectedWarned = new List<string>(); var expectedRevealed = new List<string>();
            foreach (var definition in events)
            {
                if (definition.at_scenario_tick <= scenarioTick) expectedFired.Add(definition.id);
                if (definition.warning_at_scenario_tick <= scenarioTick) expectedWarned.Add(definition.id);
                if (definition.known_at_start || definition.warning_at_scenario_tick <= scenarioTick) expectedRevealed.Add(definition.id);
                yield return "restore.clock.event_expectation";
            }
            using (var work = RestoreEventSetWork(saved["fired_event_ids"], expectedFired)) { while (work.MoveNext()) yield return work.Stage; fired = work.Result; }
            using (var work = RestoreEventSetWork(saved["warned_event_ids"], expectedWarned)) { while (work.MoveNext()) yield return work.Stage; warned = work.Result; }
            using (var work = RestoreEventSetWork(saved["revealed_event_ids"], expectedRevealed)) { while (work.MoveNext()) yield return work.Stage; revealed = work.Result; }
            SavedNumber(saved["next_static_event_index"], fired.Count, fired.Count);
        }
        private static ResumableWork<SortedSet<string>> RestoreEventSetWork(JsonValue saved, IEnumerable<string> expected)
            => new ResumableWork<SortedSet<string>>(done => RestoreEventSetSteps(saved, expected, done));
        private static IEnumerable<string> RestoreEventSetSteps(JsonValue saved, IEnumerable<string> expected, Action<SortedSet<string>> done)
        {
            SavedArray(saved, 2048);
            var values = new List<string>();
            foreach (var row in saved.Items) { values.Add(row.Text); yield return "restore.clock.event_id"; }
            string[] sorted;
            using (var work = ResumableSort.Create(expected, StringComparer.Ordinal)) { while (work.MoveNext()) yield return work.Stage; sorted = work.Result; }
            if (values.Count != sorted.Length) throw new InvalidOperationException("Saved event once-only set disagrees with compiled schedule/boundary");
            for (int index = 0; index < sorted.Length; index++)
            { if (values[index] != sorted[index]) throw new InvalidOperationException("Saved event once-only set disagrees with compiled schedule/boundary"); yield return "restore.clock.event_compare"; }
            var result = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string value in values) { result.Add(value); yield return "restore.clock.event_set"; }
            done(result);
        }
        private void ValidateRestoredTerminal(JsonValue snapshot)
        {
            bool terminal = phase == "outcome";
            var ended = engineMilestones.SingleOrDefault(x => x["id"].Text == "operation_ended");
            if (terminal != (ended != null) || terminal != !outcome.IsNull) throw new InvalidOperationException("Saved terminal outcome/milestone/phase mismatch");
            if (!terminal && (!endRequest.IsNull || scenarioTick >= mission.duration_scenario_ticks)) throw new InvalidOperationException("Unfinished saved terminal boundary");
            if (!endRequest.IsNull)
            {
                SavedFields(endRequest, "command_id", "confirm_failure"); if (endRequest["confirm_failure"].Kind != "boolean") throw new InvalidOperationException("Saved terminal confirmation must be boolean");
                if (!ledger.TryGetReceipt(endRequest["command_id"].Text, out var receipt) || !receipt.Result.Accepted || receipt.Result.ExecutedTick != operationTick) throw new InvalidOperationException("Saved terminal request has no accepted boundary receipt");
            }
            if (!terminal) return;
            if (operationTick == 0 || pending.Count != 0 || ended["operation_tick"].Integer != operationTick || ended["scenario_tick"].Integer != scenarioTick || endRequest.IsNull && scenarioTick != mission.duration_scenario_ticks) throw new InvalidOperationException("Saved terminal boundary cause/clock mismatch");
            SavedFields(outcome, "success", "stars", "score", "rescued", "backed_up", "reason");
            bool success = outcome["success"].Boolean;
            SavedNumber(outcome["stars"], success ? 1 : 0, success ? 3 : 0); SavedNumber(outcome["score"], 0, 10000);
            SavedNumber(outcome["rescued"], 0, mission.total_residents); SavedNumber(outcome["backed_up"], 0, mission.total_residents);
            if (outcome["reason"].Text != (success ? "success" : endRequest.IsNull ? "deadline_unmet" : "player_ended_unmet")) throw new InvalidOperationException("Saved outcome reason disagrees with actual terminal cause");
            var residents = snapshot["residents"].Items;
            if (outcome["rescued"].Integer != residents.Count(x => x["state"].Text == "evacuated") || outcome["backed_up"].Integer != residents.Count(x => x["state"].Text == "awaiting_backup")) throw new InvalidOperationException("Saved outcome resident totals disagree with domain");
        }
        private IEnumerable<string> ValidateRestoredDiagnosticsSteps(JsonValue snapshot)
        {
            var reached = new HashSet<string>(StringComparer.Ordinal); var resolved = new HashSet<string>(StringComparer.Ordinal); bool outcomeEvent = false;
            foreach (var entry in eventSink.Recent)
            {
                var row = entry.ToJson(); long o = row["operation_tick"].Integer, s = row["scenario_tick"].Integer;
                if (s != ScenarioAtOperation(o)) throw new InvalidOperationException("Saved diagnostic scenario clock mismatch");
                if (!row["cause_command_id"].IsNull && !ledger.TryGetReceipt(row["cause_command_id"].Text, out _)) throw new InvalidOperationException("Saved event references absent command receipt");
                var payload = row["payload"];
                switch (row["kind"].Text)
                {
                    case "CommandResolved":
                        if (row["cause_command_id"].IsNull || !row["subject_id"].IsNull || !row["cause_event_id"].IsNull || !resolved.Add(row["cause_command_id"].Text)) throw new InvalidOperationException("Saved CommandResolved cause/uniqueness");
                        ledger.TryGetReceipt(row["cause_command_id"].Text, out var receipt);
                        if (receipt.Sequence != payload["sequence"].Integer || receipt.Result.Status != payload["status"].Text || receipt.Result.ReasonCode != payload["reason_code"].Text || receipt.Result.ExecutedTick != o) throw new InvalidOperationException("Saved CommandResolved disagrees with receipt");
                        break;
                    case "MilestoneReached":
                        string id = payload["milestone_id"].Text;
                        var milestone = snapshot["milestones"].Items.SingleOrDefault(x => x["id"].Text == id);
                        if (!reached.Add(id) || milestone == null || milestone["operation_tick"].Integer != o || milestone["scenario_tick"].Integer != s || row["subject_id"].IsNull || row["subject_id"].Text != id) throw new InvalidOperationException("Saved milestone diagnostic disagrees with durable milestone");
                        break;
                    case "OutcomeResolved":
                        if (outcomeEvent || phase != "outcome" || o != operationTick || payload["success"].Boolean != outcome["success"].Boolean || payload["stars"].Integer != outcome["stars"].Integer || payload["score"].Integer != outcome["score"].Integer) throw new InvalidOperationException("Saved outcome diagnostic disagrees with durable outcome");
                        outcomeEvent = true; break;
                }
                yield return "restore.clock.diagnostic";
            }
            var retained = snapshot["recent_events"].Items;
            Func<long, bool> mustBeRetained = tick => retained.Count < 256 || retained.Count > 0 && (tick > retained[0]["operation_tick"].Integer || tick == retained[0]["operation_tick"].Integer && retained[0]["ordinal"].Integer == 0);
            foreach (var receipt in ledger.EnumerateReceipts())
            { if (receipt.Result.Status != "queued" && mustBeRetained(receipt.Result.ExecutedTick) && !resolved.Contains(receipt.CommandId)) throw new InvalidOperationException("Saved diagnostics omit a retained resolved command"); yield return "restore.clock.receipt_diagnostic"; }
            foreach (var milestone in snapshot["milestones"].Items) if (mustBeRetained(milestone["operation_tick"].Integer) && !reached.Contains(milestone["id"].Text)) throw new InvalidOperationException("Saved diagnostics omit a retained milestone");
            if (phase == "outcome" && mustBeRetained(operationTick) && !outcomeEvent) throw new InvalidOperationException("Saved diagnostics omit retained terminal outcome");
        }
        private static void SavedFields(JsonValue value, params string[] fields) { if (value == null || value.Kind != "object" || value.Members.Count != fields.Length || fields.Any(x => !value.Has(x))) throw new InvalidOperationException("Invalid saved boundary closed fields"); }
        private static void SavedNumber(JsonValue value, long minimum, long maximum) { if (value.Kind != "integer" || value.Integer < minimum || value.Integer > maximum) throw new InvalidOperationException("Invalid saved boundary integer"); }
        private static void SavedArray(JsonValue value, int maximum) { if (value.Kind != "array" || value.Items.Count > maximum) throw new InvalidOperationException("Invalid saved boundary array/bound"); }

        public CommandResult Admit(GameCommand command) => AdmitWork(command).Drain();
        public ResumableWork<CommandResult> AdmitWork(GameCommand command)
            => new ResumableWork<CommandResult>(done => AdmitSteps(command, done));
        private IEnumerable<string> AdmitSteps(GameCommand command, Action<CommandResult> complete)
        {
            Boundary();
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.RunId != identity["run_id"].Text) { complete(CommandLedger.Reject(command, "WRONG_RUN", operationTick, revision)); yield break; }
            var duplicate = ledger.FindDuplicate(command, operationTick, revision); if (duplicate != null) { complete(duplicate); yield break; }
            var envelope = ledger.ValidateNew(command, identity["run_id"].Text);
            if (!envelope.Allowed) { complete(CommandLedger.Reject(command, envelope.ReasonCode, operationTick, revision)); yield break; }
            // Application commands retain their boundary API. Forecast refuses
            // these before admission; this continuation covers simulation input.
            if (command.IsApplication) { complete(ApplyNew(command).Receipt); yield break; }
            insideAdmission = true;
            try
            {
                var context = new OperationContext(operationTick, scenarioTick, false);
                ValidationResult validity;
                using (var validation = ValidateSimulationWork(command, context, true))
                { while (validation.MoveNext()) yield return validation.Stage; validity = validation.Result; }
                // Until this commit, validation has changed no receipt, pending
                // command, revision or owner. Disposal releases the boundary.
                yield return "command.admission.ready_to_commit";
                revision = checked(revision + 1);
                CommandResult receipt;
                if (validity.Allowed)
                {
                    receipt = new CommandResult(command, "queued", "PENDING_EXECUTION", -1, revision, validity.AffectedIds);
                    pending.Add(command);
                }
                else receipt = new CommandResult(command, "rejected", validity.ReasonCode, operationTick, revision, validity.AffectedIds);
                ledger.Admit(command, receipt); if (receipt.Status != "queued") EmitCommandResult(command, receipt);
                complete(receipt);
            }
            finally { insideAdmission = false; }
        }
        private ValidationResult ValidateSimulation(GameCommand command, OperationContext context, bool admission)
            => ValidateSimulationWork(command, context, admission).Drain();
        private ResumableWork<ValidationResult> ValidateSimulationWork(GameCommand command, OperationContext context, bool admission)
            => new ResumableWork<ValidationResult>(done => ValidateSimulationSteps(command, context, admission, done));
        private IEnumerable<string> ValidateSimulationSteps(GameCommand command, OperationContext context, bool admission, Action<ValidationResult> complete)
        {
            // No spending/reservations occur in this preview. The domain validator
            // is called only after the outer frozen header/tick/phase checks.
            if (admission && command.RequestedTick != operationTick + 1 || !admission && command.RequestedTick != operationTick) { complete(new ValidationResult("INVALID_TICK")); yield break; }
            if (phase == "outcome" || Fault != null || applicationState == "hub" || applicationState == "loading" || applicationState == "placement") { complete(new ValidationResult("INVALID_PHASE")); yield break; }
            ValidationResult domain;
            using (var validation = DomainWork.Validate(systems, command, context))
            { while (validation.MoveNext()) yield return validation.Stage; domain = validation.Result; }
            var tracking = trackingAvailable ? new ValidationResult("OK") : new ValidationResult("TRACKING_UNAVAILABLE");
            var queue = admission && pending.Count >= 32 ? new ValidationResult("QUEUE_FULL") : new ValidationResult("OK");
            if (command.Kind == "end_operation" && !((EndOperation)command.Payload).ConfirmFailure)
            {
                bool required;
                using (var conditions = ConditionsWork(systems))
                { while (conditions.MoveNext()) yield return conditions.Stage; required = conditions.Result; }
                if (!required) { complete(ValidationResult.First(domain, tracking, new ValidationResult("OBJECTIVES_UNMET"), queue)); yield break; }
            }
            complete(ValidationResult.First(domain, tracking, queue));
        }
        public ApplicationResult Apply(GameCommand command)
        {
            Boundary(); if (command == null || !command.IsApplication) throw new ArgumentException("application command required");
            if (command.RunId != identity["run_id"].Text) return new ApplicationResult(CommandLedger.Reject(command, "WRONG_RUN", operationTick, revision), "none", null);
            var duplicate = ledger.FindDuplicate(command, operationTick, revision);
            if (duplicate != null) return new ApplicationResult(duplicate, "none", null);
            var receipt = Admit(command);
            // The Admit path owns application side effects. Exact retries do not
            // perform another advance or request another scene/placement action.
            return lastApplication != null && ReferenceEquals(lastApplication.Receipt, receipt) ? lastApplication : new ApplicationResult(receipt, "none", null);
        }
        private ApplicationResult lastApplication;
        private ApplicationResult ApplyNew(GameCommand command)
        {
            string rejection = command.RequestedTick != operationTick ? "INVALID_TICK" : null;
            if (rejection == null && (Fault != null && command.Kind != "exit_to_hub" || phase == "outcome" && command.Kind != "exit_to_hub" || applicationState == "hub")) rejection = "INVALID_PHASE";
            if (rejection == null && command.Kind == "advance_ticks" && (phase == "planning" || !IsPaused || applicationState != "paused")) rejection = "INVALID_PHASE";
            if (rejection == null && command.Kind == "resume" && applicationState != "paused") rejection = "INVALID_PHASE";
            if (rejection == null && command.Kind == "confirm_placement" && applicationState != "placement") rejection = "INVALID_PHASE";
            if (rejection == null && !trackingAvailable && (command.Kind == "resume" || command.Kind == "advance_ticks")) rejection = "TRACKING_UNAVAILABLE";
            if (command.Kind == "confirm_placement")
            {
                var placement = (ConfirmPlacement)command.Payload;
                if (placement.LayoutVersionId != layoutId || placement.LayoutSignature != content.LayoutSignature(layoutId)) rejection = "CONTENT_VERSION_MISMATCH";
            }
            revision = checked(revision + 1);
            var receipt = rejection == null ? new CommandResult(command, "accepted", "OK", operationTick, revision, new string[0]) : CommandLedger.Reject(command, rejection, operationTick, revision);
            ledger.Admit(command, receipt); // Receipt precedes any explicit advance.
            EmitCommandResult(command, receipt);
            string transition = "none"; StepBatchResult advanced = null;
            if (receipt.Accepted)
            {
                switch (command.Kind)
                {
                    case "pause":
                        string reason = ((PauseOperation)command.Payload).Reason;
                        if (reason != "user") { interruptionReason = reason; if (reason == "tracking" || reason == "anchor") trackingAvailable = false; applicationState = "interrupted"; }
                        else if (interruptionReason == null) applicationState = "paused";
                        break;
                    case "resume": applicationState = "countdown"; transition = "resume_countdown"; break;
                    case "advance_ticks": advanced = AdvanceCore(((AdvanceTicks)command.Payload).OperationTicks); break;
                    case "enter_placement": if (interruptionReason == null) applicationState = "placement"; transition = "placement"; break;
                    case "confirm_placement": if (interruptionReason == null) applicationState = "paused"; transition = "continue"; break;
                    case "exit_to_hub": applicationState = "hub"; transition = ((ExitToHub)command.Payload).Disposition; break;
                }
            }
            lastApplication = new ApplicationResult(receipt, transition, advanced); return lastApplication;
        }
        public void CompleteResumeCountdown()
        {
            Boundary(); if (applicationState != "countdown" || !trackingAvailable || interruptionReason != null) throw new InvalidOperationException("No eligible resume countdown");
            if (phase == "planning") phase = "active";
            revision = checked(revision + 1);
            applicationState = "running";
        }
        // Platform/XR lifecycle signals latch at a boundary and never catch up.
        // Recovery returns to explicit paused Continue, never directly to Active.
        public void Interrupt(string reason)
        {
            Boundary(); if (reason != "tracking" && reason != "anchor" && reason != "platform" && reason != "loading") throw new ArgumentException("interrupt reason");
            if (reason == "tracking" || reason == "anchor") trackingAvailable = false;
            interruptionReason = reason;
            applicationState = reason == "loading" ? "loading" : "interrupted";
        }
        public void RecoverInterruption() { Boundary(); if (interruptionReason == null) throw new InvalidOperationException("No owned interruption to recover"); interruptionReason = null; trackingAvailable = true; applicationState = "paused"; }
        public StepResult Step() => StepWork().Drain();
        public ResumableWork<StepResult> StepWork() => new ResumableWork<StepResult>(done => ExecuteStepSteps(done, false));
        public StepBatchResult Advance(int operationTicks)
        {
            Boundary(); if (operationTicks != 10 && operationTicks != 50) throw new ArgumentOutOfRangeException(nameof(operationTicks));
            if (advanceCommandFactory == null) throw new InvalidOperationException("Advance requires the recorded input-command broker; use Apply(advance_ticks).");
            var command = advanceCommandFactory(operationTicks);
            if (command == null || command.Kind != "advance_ticks" || ((AdvanceTicks)command.Payload).OperationTicks != operationTicks) throw new InvalidOperationException("Advance broker returned an incompatible command");
            var result = Apply(command);
            return result.Advanced ?? new StepBatchResult(new StepResult[0], result.Receipt.ReasonCode);
        }
        private StepBatchResult AdvanceCore(int operationTicks)
        {
            // Application uses the same step executor. This explicit player
            // operation is eligible only at a paused Active/Stabilizing boundary.
            if (applicationState != "paused" || phase == "planning" || phase == "outcome" || Fault != null || !trackingAvailable) return new StepBatchResult(new StepResult[0], "INVALID_PHASE");
            var results = new List<StepResult>();
            for (int index = 0; index < operationTicks && phase != "outcome" && Fault == null; index++) { var step = ExecuteStep(); if (step != null) results.Add(step); }
            return new StepBatchResult(results, Fault);
        }
        private StepResult ExecuteStep() => new ResumableWork<StepResult>(done => ExecuteStepSteps(done, true)).Drain();
        private IEnumerable<string> ExecuteStepSteps(Action<StepResult> complete, bool allowPaused)
        {
            Boundary();
            if ((!allowPaused && IsPaused) || phase == "planning" || phase == "outcome" || Fault != null) { complete(null); yield break; }
            // The transaction is a private shell. Every mutable owner is forked
            // before use; the public clock remains at its completed boundary.
            insideStep = true;
            var transaction = (OperationClock)MemberwiseClone();
            ResumableWork<StepResult> work = null;
            try
            {
                work = new ResumableWork<StepResult>(transaction.CompleteStepSteps);
                while (true)
                {
                    bool more = false; Exception failure = null;
                    try { more = work.MoveNext(); } catch (Exception error) { failure = error; }
                    if (failure != null) { Fault = "SIMULATION_INVARIANT_FAULT: " + failure.Message; complete(null); yield break; }
                    if (!more) break;
                    yield return work.Stage;
                }
                yield return "operation.ready_to_commit";
                systems = transaction.systems; ledger = transaction.ledger; pending = transaction.pending;
                warned = transaction.warned; fired = transaction.fired; revealed = transaction.revealed;
                engineMilestones = transaction.engineMilestones; eventSink = transaction.eventSink; rng = transaction.rng;
                operationTick = transaction.operationTick; scenarioTick = transaction.scenarioTick; revision = transaction.revision;
                accumulator = transaction.accumulator; scenarioEnabled = transaction.scenarioEnabled;
                phase = transaction.phase; applicationState = transaction.applicationState;
                endRequest = transaction.endRequest; outcome = transaction.outcome;
                completedStepSnapshot = transaction.completedStepSnapshot;
                complete(work.Result);
            }
            finally { work?.Dispose(); insideStep = false; }
        }
        private IEnumerable<string> CompleteStepSteps(Action<StepResult> complete)
        {
            var previousSystems = systems;
            using (var fork = systems is IResumableOperationSystems resumable ? resumable.ForkWork() : Atomic(() => systems.Fork(), "fork.legacy_owner"))
            {
                while (fork.MoveNext()) yield return fork.Stage;
                systems = fork.Result ?? throw new InvalidOperationException("Missing isolated systems fork");
                if (ReferenceEquals(systems, previousSystems)) throw new InvalidOperationException("Systems fork aliases authority");
            }
            using (var copyLedger = ledger.CloneWork()) { while (copyLedger.MoveNext()) yield return copyLedger.Stage; ledger = copyLedger.Result; }
            pending = new List<GameCommand>(pending);
            var copyWarned = new SortedSet<string>(StringComparer.Ordinal); foreach (string id in warned) { copyWarned.Add(id); yield return "fork.warned"; } warned = copyWarned;
            var copyFired = new SortedSet<string>(StringComparer.Ordinal); foreach (string id in fired) { copyFired.Add(id); yield return "fork.fired"; } fired = copyFired;
            var copyRevealed = new SortedSet<string>(StringComparer.Ordinal); foreach (string id in revealed) { copyRevealed.Add(id); yield return "fork.revealed"; } revealed = copyRevealed;
            engineMilestones = new List<JsonValue>(engineMilestones); rng = rng.Clone();
            // 1. Integer operation/scenario clocks only.
            operationTick = new Tick(checked(operationTick + 1)).Value; bool pulse = false;
            if (scenarioEnabled) { accumulator = checked(accumulator + (int)numerator); if (accumulator >= 2) { accumulator -= 2; scenarioTick = new Tick(checked(scenarioTick + 1)).Value; pulse = true; } }
            eventSink = eventSink.Fork(); eventSink.BeginBoundary(operationTick, scenarioTick);
            var context = new OperationContext(operationTick, scenarioTick, pulse, eventSink);
            yield return "operation.clocks";
            // 2. Existing tide precedes warnings and scheduled ramps.
            if (pulse)
            {
                systems.EvaluateTides(context); yield return "operation.tides";
                foreach (var definition in events.Where(item => item.warning_at_scenario_tick == scenarioTick && (forecastKnownEvents == null || forecastKnownEvents.Contains(item.id))).OrderBy(item => item.id, StringComparer.Ordinal))
                { if (warned.Add(definition.id)) { revealed.Add(definition.id); systems.PublishWarning(definition, context); } yield return "operation.warning"; }
                foreach (var definition in events.Where(item => item.at_scenario_tick == scenarioTick && (forecastKnownEvents == null || forecastKnownEvents.Contains(item.id))).OrderBy(item => item.id, StringComparer.Ordinal))
                {
                    if (!fired.Add(definition.id)) throw new InvalidOperationException("duplicate scheduled effect");
                    foreach (var operation in content.LayoutOperations[layoutId].Where(item => item.EventId == definition.id))
                    { systems.ApplyScheduled(operation, context); yield return "operation.scheduled_effect"; }
                }
            }
            // 3. Same admission/revalidation order, held privately until commit.
            foreach (var command in pending.OrderBy(item => item.Sequence).ToArray())
            {
                ValidationResult validation;
                using (var work = ValidateSimulationWork(command, context, false))
                { while (work.MoveNext()) yield return work.Stage; validation = work.Result; }
                yield return "operation.command_validation";
                if (validation.Allowed)
                {
                    if (command.Kind == "end_operation") endRequest = JsonValue.Object(new Dictionary<string, JsonValue> { {"command_id",JsonValue.String(command.CommandId)}, {"confirm_failure",JsonValue.Bool(((EndOperation)command.Payload).ConfirmFailure)} });
                    else using (var work = DomainWork.Apply(systems, command, context))
                    { while (work.MoveNext()) yield return work.Stage; }
                }
                yield return "operation.command_apply";
                var resolution = new CommandResult(command, validation.Allowed ? "accepted" : "rejected", validation.ReasonCode, operationTick, checked(revision + 1), validation.AffectedIds);
                ledger.Resolve(command, resolution); EmitCommandResult(command, resolution); pending.Remove(command);
                yield return "operation.command_receipt";
            }
            // 4-8. Synchronous/replay callers drain these same owner units.
            using (var work = StageWork(systems, OperationStage.ResolvePowerAndReservations, context)) { while (work.MoveNext()) yield return work.Stage; }
            if (pulse) using (var work = StageWork(systems, OperationStage.SimulateHazardInterval, context)) { while (work.MoveNext()) yield return work.Stage; }
            using (var work = StageWork(systems, OperationStage.RecomputeSafetyAndDamage, context)) { while (work.MoveNext()) yield return work.Stage; }
            using (var work = StageWork(systems, OperationStage.ResolveMovementAndTransfers, context)) { while (work.MoveNext()) yield return work.Stage; }
            using (var work = StageWork(systems, OperationStage.DeriveMetricsAndObjectives, context)) { while (work.MoveNext()) yield return work.Stage; }
            if (!scenarioEnabled && systems.GuidedEpochReady) { scenarioEnabled = true; accumulator = 0; Milestone("operation_started", context); }
            yield return "operation.guided_epoch";
            // 9. Deadline arrivals/objectives still belong to this exact step.
            if (scenarioTick >= mission.duration_scenario_ticks || !endRequest.IsNull)
            {
                Milestone("operation_ended", context); yield return "operation.end_milestone";
                OutcomeResult final;
                using (var evaluation = systems is IResumableOperationSystems owner ? owner.OutcomeWork(context) : Atomic(() => systems.EvaluateOutcome(context), "operation.outcome"))
                { while (evaluation.MoveNext()) yield return evaluation.Stage; final = evaluation.Result ?? throw new InvalidOperationException("Missing outcome evaluator"); }
                outcome = final.ToJson(endRequest.IsNull ? "deadline_unmet" : "player_ended_unmet"); phase = "outcome"; applicationState = "outcome";
                eventSink.Emit("OutcomeResolved", null, null, null, JsonValue.Object(new Dictionary<string, JsonValue> { {"success",JsonValue.Bool(final.Success)},{"stars",JsonValue.Number(final.Stars)},{"score",JsonValue.Number(final.Score)} }));
            }
            else using (var required = ConditionsWork(systems)) { while (required.MoveNext()) yield return required.Stage; phase = required.Result ? "stabilizing" : "active"; }
            using (var validation = StageWork(systems, OperationStage.ValidateBoundary, context)) { while (validation.MoveNext()) yield return validation.Stage; }
            revision = checked(revision + 1);
            using (var snapshot = ReadSnapshotWork()) { while (snapshot.MoveNext()) yield return snapshot.Stage; completedStepSnapshot = snapshot.Result; }
            complete(new StepResult(completedStepSnapshot, eventSink.Current));
        }
        private static ResumableWork<T> Atomic<T>(Func<T> read, string stage) => new ResumableWork<T>(done => AtomicSteps(read, stage, done));
        private static IEnumerable<string> AtomicSteps<T>(Func<T> read, string stage, Action<T> done)
        { var value = read(); yield return stage; done(value); }
        private static ResumableWork<bool> ConditionsWork(IOperationSystems owner)
            => owner is IResumableOperationSystems work ? work.RequiredConditionsWork() : Atomic(() => owner.RequiredConditionsHold, "operation.conditions");
        private static ResumableWork<bool> StageWork(IOperationSystems owner, OperationStage stage, OperationContext context)
        {
            if (owner is IResumableOperationSystems work) return work.StageWork(stage, context);
            return Atomic(() => {
                switch (stage)
                {
                    case OperationStage.ResolvePowerAndReservations: owner.ResolvePowerAndReservations(context); break;
                    case OperationStage.SimulateHazardInterval: owner.SimulateHazardInterval(context); break;
                    case OperationStage.RecomputeSafetyAndDamage: owner.RecomputeSafetyAndDamage(context); break;
                    case OperationStage.ResolveMovementAndTransfers: owner.ResolveMovementAndTransfers(context); break;
                    case OperationStage.DeriveMetricsAndObjectives: owner.DeriveMetricsAndObjectives(context); break;
                    case OperationStage.ValidateBoundary: owner.ValidateBoundary(context); break;
                    case OperationStage.SampleInitialObjectives: owner.SampleInitialObjectives(context); break;
                    default: throw new ArgumentOutOfRangeException(nameof(stage));
                }
                return true;
            }, "operation.legacy_" + stage);
        }
        private void PublishWarnings(long tick, OperationContext context)
        {
            foreach (var definition in events.Where(item => item.warning_at_scenario_tick == tick && (forecastKnownEvents == null || forecastKnownEvents.Contains(item.id))).OrderBy(item => item.id, StringComparer.Ordinal))
                if (warned.Add(definition.id)) { revealed.Add(definition.id); systems.PublishWarning(definition, context); }
        }
        private void Milestone(string id, OperationContext context)
        {
            if (engineMilestones.Any(item => item["id"].Text == id)) throw new InvalidOperationException("duplicate engine milestone");
            engineMilestones.Add(JsonValue.Object(new Dictionary<string, JsonValue> { {"id",JsonValue.String(id)}, {"operation_tick",JsonValue.Number(context.OperationTick)}, {"scenario_tick",JsonValue.Number(context.ScenarioTick)} }));
            systems.EmitMilestone(id, context);
            eventSink.Emit("MilestoneReached", id, null, null, JsonValue.Object(new Dictionary<string, JsonValue> { {"milestone_id",JsonValue.String(id)} }));
        }
        private void EmitCommandResult(GameCommand command, CommandResult result)
        {
            if (!insideStep) eventSink.BeginBoundary(operationTick, scenarioTick);
            eventSink.Emit("CommandResolved", null, command.CommandId, null, JsonValue.Object(new Dictionary<string, JsonValue> { {"sequence",JsonValue.Number(command.Sequence)},{"status",JsonValue.String(result.Status)},{"reason_code",JsonValue.String(result.ReasonCode)} }));
        }
        private static readonly string[] DomainFields = { "guidance_state", "supplies_remaining", "selected_stock", "deployments", "water", "rate_remainders", "power", "equipment", "vehicles", "residents", "transits", "transfers", "reservations", "routes", "queues", "fire", "supports", "runouts", "tides", "lava", "stores", "producers", "event_state", "milestones", "metrics", "objective_monitors", "recent_events", "water_ledger" };
        public SimulationSnapshot ReadSnapshot() => ReadSnapshotWork().Drain();
        public ResumableWork<SimulationSnapshot> ReadSnapshotWork() => new ResumableWork<SimulationSnapshot>(ReadStableSnapshotSteps);
        private IEnumerable<string> ReadStableSnapshotSteps(Action<SimulationSnapshot> complete)
        {
            long expectedRevision = revision;
            using (var work = new ResumableWork<SimulationSnapshot>(ReadSnapshotSteps))
            {
                while (true)
                {
                    if (revision != expectedRevision) throw new InvalidOperationException("Snapshot boundary changed during read");
                    bool more = work.MoveNext();
                    if (revision != expectedRevision) throw new InvalidOperationException("Snapshot boundary changed during read");
                    if (!more) { complete(work.Result); yield break; }
                    yield return work.Stage;
                }
            }
        }
        private IEnumerable<string> ReadSnapshotSteps(Action<SimulationSnapshot> complete)
        {
            JsonValue domain;
            using (var work = systems is IResumableOperationSystems owner ? owner.ReadDomainStateWork() : Atomic(() => systems.ReadDomainState(), "snapshot.legacy_owner"))
            { while (work.MoveNext()) yield return work.Stage; domain = work.Result; }
            if (domain.Kind != "object" || domain.Members.Count != DomainFields.Length || DomainFields.Any(field => !domain.Has(field))) throw new InvalidOperationException("Incomplete/unknown domain Snapshot fields");
            var state = new Dictionary<string, JsonValue>(domain.Members, StringComparer.Ordinal);
            state["schema_version"] = JsonValue.Number(1); state["identity"] = identity; state["state_revision"] = JsonValue.Number(revision); state["phase"] = JsonValue.String(phase);
            state["operation_tick"] = JsonValue.Number(operationTick); state["scenario_tick"] = JsonValue.Number(scenarioTick); state["scenario_accumulator"] = JsonValue.Number(accumulator); state["scenario_enabled"] = JsonValue.Bool(scenarioEnabled); state["rng"] = rng.ToJson();
            state["last_admitted_sequence"] = JsonValue.Number(ledger.LastAdmittedSequence);
            var pendingRows = new List<JsonValue>();
            foreach (var row in pending.OrderBy(item => item.Sequence)) { pendingRows.Add(row.ToJson()); yield return "snapshot.pending_command"; }
            state["pending_commands"] = JsonValue.Array(pendingRows);
            var receiptRows = new List<JsonValue>();
            foreach (var row in ledger.EnumerateReceipts()) { receiptRows.Add(row.ToJson()); yield return "snapshot.receipt"; }
            state["command_receipts"] = JsonValue.Array(receiptRows);
            state["end_request"] = endRequest; state["outcome"] = outcome;
            if (state["recent_events"].Items.Count != 0) throw new InvalidOperationException("Domain events must use the owned OperationContext.Events sink");
            var recentRows = new List<JsonValue>(); foreach (var row in eventSink.Recent) { recentRows.Add(row.ToJson()); yield return "snapshot.event"; }
            state["recent_events"] = JsonValue.Array(recentRows);
            var eventState = new Dictionary<string, JsonValue>(state["event_state"].Members, StringComparer.Ordinal);
            eventState["next_static_event_index"] = JsonValue.Number(fired.Count);
            var firedRows = new List<JsonValue>(); foreach (string id in fired) { firedRows.Add(JsonValue.String(id)); yield return "snapshot.fired"; }
            var revealedRows = new List<JsonValue>(); foreach (string id in revealed) { revealedRows.Add(JsonValue.String(id)); yield return "snapshot.revealed"; }
            var warnedRows = new List<JsonValue>(); foreach (string id in warned) { warnedRows.Add(JsonValue.String(id)); yield return "snapshot.warned"; }
            eventState["fired_event_ids"] = JsonValue.Array(firedRows); eventState["revealed_event_ids"] = JsonValue.Array(revealedRows); eventState["warned_event_ids"] = JsonValue.Array(warnedRows);
            state["event_state"] = JsonValue.Object(eventState);
            using (var milestones = CanonicalState.SortedEntitiesWork(state["milestones"].Items.Concat(engineMilestones)))
            { while (milestones.MoveNext()) yield return milestones.Stage; state["milestones"] = milestones.Result; }
            yield return "snapshot.clock_fields";
            using (var snapshot = SimulationSnapshot.CreateWork(JsonValue.Object(state)))
            { while (snapshot.MoveNext()) yield return snapshot.Stage; complete(snapshot.Result); }
        }
        private void Boundary() { if (insideStep || insideAdmission) throw new InvalidOperationException("Application/command mutation during simulation step or admission"); }
    }

    // Presentation-only budget. Integer elapsed milliseconds choose how many
    // explicit steps to request; they are absent from state, commands and hashes.
    // Replays call Step/Advance directly and never read elapsed presentation time.
    public sealed class FrameStepBudget
    {
        private long debtMilliseconds;
        public long DiscardedMilliseconds { get; private set; }
        public long DebtMilliseconds => debtMilliseconds;
        public int Offer(long elapsedMilliseconds, bool running)
        {
            if (elapsedMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
            if (!running) { debtMilliseconds = 0; return 0; }
            long total = checked(debtMilliseconds + elapsedMilliseconds);
            int steps = (int)Math.Min(4, total / 100); long remainder = total - steps * 100;
            if (remainder > 400) { DiscardedMilliseconds = checked(DiscardedMilliseconds + remainder - 400); remainder = 400; }
            debtMilliseconds = remainder; return steps;
        }
    }
}
