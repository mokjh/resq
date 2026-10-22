using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Resq.Content;

namespace Resq.Persistence
{
    public interface ISaveStore
    {
        SaveReadResult ReadValidated();
        SaveWriteHandle WriteGeneration(PlayerSaveSnapshot snapshot);
        SaveWriteResult Poll(SaveWriteHandle handle);
    }

    public enum SaveReadStatus { Empty, Valid, RecoveryRequired, NewProfileConfirmationRequired, IncompatibleSchema, IncompatibleRun, StorageError }
    public enum SaveWriteStatus { Queued, Writing, Succeeded, Failed, Rejected, Coalesced }
    public enum SaveSource { None, Current, Previous }
    public enum SaveFileStatus { Missing, Valid, Corrupt, FutureSchema, IncompatibleRun, StorageError }

    public sealed class SaveWriteResult
    {
        public SaveWriteStatus Status { get; }
        public long Generation { get; }
        public string PayloadSha256 { get; }
        public string ReasonCode { get; }
        public string Detail { get; }
        public bool IsRecovery { get; }
        public bool IsTerminal => Status != SaveWriteStatus.Queued && Status != SaveWriteStatus.Writing;
        internal SaveWriteResult(SaveWriteStatus status, long generation, string hash, string code, string detail, bool recovery)
        { Status = status; Generation = generation; PayloadSha256 = hash; ReasonCode = code; Detail = detail; IsRecovery = recovery; }
    }

    public sealed class SaveWriteHandle
    {
        internal object Owner { get; }
        internal SaveWriteResult Result;
        public long RequestId { get; }
        internal SaveWriteHandle(object owner, long id, SaveWriteResult result) { Owner = owner; RequestId = id; Result = result; }
    }

    public sealed class SaveFileReport
    {
        public SaveFileStatus Status { get; }
        public long Generation { get; }
        public long SchemaVersion { get; }
        public string FileSha256 { get; }
        public string PayloadSha256 { get; }
        public string ReasonCode { get; }
        public string Detail { get; }
        internal SaveFileReport(SaveFileStatus status, long generation, long schema, string fileHash, string payloadHash, string code, string detail)
        { Status = status; Generation = generation; SchemaVersion = schema; FileSha256 = fileHash; PayloadSha256 = payloadHash; ReasonCode = code; Detail = detail; }
    }

    // Tickets are opaque capabilities issued by one store for exact observed bytes.
    // A human/UI choice calls the named method; merely reading never grants consent.
    public sealed class SaveDecisionTicket
    {
        internal object Owner { get; }
        internal string CurrentStamp { get; }
        internal string PreviousStamp { get; }
        internal SaveReadStatus ReadStatus { get; }
        internal PlayerSaveSnapshot Backup { get; }
        public long BackupGeneration { get; }
        public string BackupPayloadSha256 => Backup == null ? null : Backup.PayloadSha256;
        public string BackupProfileId => Backup == null ? null : Backup.LocalProfileId;
        internal SaveDecisionTicket(object owner, FileState current, FileState previous, SaveReadStatus status)
        {
            Owner = owner; CurrentStamp = current.Stamp; PreviousStamp = previous.Stamp; ReadStatus = status;
            Backup = previous.Report.Status == SaveFileStatus.Valid ? previous.Snapshot : null;
            BackupGeneration = Backup == null ? 0 : previous.Report.Generation;
        }
    }

    public sealed class SaveReadResult
    {
        public SaveReadStatus Status { get; }
        public PlayerSaveSnapshot Snapshot { get; }
        public long Generation { get; }
        public SaveSource Source { get; }
        public string PayloadSha256 => Snapshot == null ? null : Snapshot.PayloadSha256;
        public string ReasonCode { get; }
        public SaveDecisionTicket Decision { get; }
        public SaveFileReport Current { get; }
        public SaveFileReport Previous { get; }
        // Reading and rebuilding never advance the simulation. The application
        // must show Continue for every loaded current run, including recovery.
        public bool RequiresPausedContinue => Snapshot != null && Snapshot.RequiresPausedContinue;
        internal SaveReadResult(SaveReadStatus status, FileState selected, SaveSource source, string code,
            SaveDecisionTicket decision, FileState current, FileState previous)
        {
            Status = status; Snapshot = selected == null ? null : selected.Snapshot;
            Generation = selected == null ? 0 : selected.Report.Generation; Source = source; ReasonCode = code;
            Decision = decision; Current = current.Report; Previous = previous.Report;
        }
    }

    public sealed class SaveFileTooLargeException : IOException
    {
        public SaveFileTooLargeException() : base("Save file exceeds the bounded strict-reader limit") { }
    }

    // Fault-injection seam. AtomicReplace must be one same-filesystem atomic move
    // or replace, never Delete(destination)+Move(source). Flush failures propagate.
    public interface ISaveFileSystem
    {
        bool Exists(string path);
        byte[] ReadAllBytes(string path, int maximumBytes);
        string FileSha256(string path);
        void CreateDirectory(string path);
        void WriteAndFlush(string path, byte[] bytes, bool createNew);
        void CopyAndFlush(string source, string destination, bool createNew);
        void AtomicReplace(string source, string destination);
    }

    public sealed class PhysicalSaveFileSystem : ISaveFileSystem
    {
        public bool Exists(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                    throw new IOException("A save filename resolves to a directory");
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public byte[] ReadAllBytes(string path, int maximumBytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > maximumBytes) throw new SaveFileTooLargeException();
                byte[] bytes = new byte[checked((int)stream.Length)];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) throw new EndOfStreamException("Save file changed while reading");
                    offset += read;
                }
                if (stream.ReadByte() != -1) throw new IOException("Save file grew while reading");
                return bytes;
            }
        }
        public string FileSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(stream));
        }
        public void WriteAndFlush(string path, byte[] bytes, bool createNew)
        {
            using (var stream = new FileStream(path, createNew ? FileMode.CreateNew : FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        public void CopyAndFlush(string source, string destination, bool createNew)
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, createNew ? FileMode.CreateNew : FileMode.Create, FileAccess.Write, FileShare.None))
            { input.CopyTo(output, 64 * 1024); output.Flush(true); }
        }
        public void AtomicReplace(string source, string destination)
        {
            string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(source));
            string destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.Equals(sourceDirectory, destinationDirectory, StringComparison.Ordinal))
                throw new IOException("Atomic save promotion requires the same directory");
            if (Exists(destination)) File.Replace(source, destination, null);
            else File.Move(source, destination);
        }
        internal static string BytesSha256(byte[] bytes)
        { using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes)); }
        private static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    internal sealed class FileState
    {
        public SaveFileReport Report { get; }
        public PlayerSaveSnapshot Snapshot { get; }
        public string Stamp => Report.Status == SaveFileStatus.Missing ? "missing" : Report.FileSha256;
        public FileState(SaveFileReport report, PlayerSaveSnapshot snapshot = null) { Report = report; Snapshot = snapshot; }
    }

    // One lifecycle owner per persistent-data directory. There is no hidden worker:
    // WriteGeneration captures/queues, Poll only observes, and the owner calls Pump
    // on its chosen I/O executor. Pump performs at most one complete generation.
    public sealed class SaveStore : ISaveStore
    {
        public const string CurrentFilename = "save.current.json";
        public const string PreviousFilename = "save.previous.json";
        public const string PendingFilename = "save.pending.json";
        private readonly object owner = new object(), queueGate = new object(), ioGate = new object();
        private readonly string directory, currentPath, previousPath, pendingPath, previousTemporaryPath;
        private readonly Action<JsonValue> validateCurrentRun;
        private readonly ISaveFileSystem files;
        private readonly Action<string> warning;
        private WorkItem queued;
        private long nextRequestId;
        private bool warningEpisode;

        private enum WriteKind { Ordinary, RecoverPrevious, NewProfile }
        private sealed class WorkItem
        {
            public PlayerSaveSnapshot Snapshot;
            public SaveWriteHandle Handle;
            public WriteKind Kind;
            public SaveDecisionTicket Decision;
        }

        public SaveStore(string persistentDataPath, Action<JsonValue> validateCurrentRun,
            ISaveFileSystem fileSystem = null, Action<string> warning = null)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath)) throw new ArgumentException("Persistent-data path is required", nameof(persistentDataPath));
            directory = Path.GetFullPath(persistentDataPath); this.validateCurrentRun = validateCurrentRun;
            files = fileSystem ?? new PhysicalSaveFileSystem(); this.warning = warning;
            currentPath = Path.Combine(directory, CurrentFilename); previousPath = Path.Combine(directory, PreviousFilename);
            pendingPath = Path.Combine(directory, PendingFilename); previousTemporaryPath = Path.Combine(directory, "save.previous.pending.json");
        }

        public SaveReadResult ReadValidated()
        {
            SaveReadResult result;
            lock (ioGate) result = ReadPair();
            if (result.Status == SaveReadStatus.StorageError) Warn(result.ReasonCode);
            return result;
        }

        public SaveWriteHandle WriteGeneration(PlayerSaveSnapshot snapshot) => Queue(snapshot, WriteKind.Ordinary, null);

        public SaveWriteResult Poll(SaveWriteHandle handle)
        {
            if (handle == null || !ReferenceEquals(handle.Owner, owner)) throw new ArgumentException("Handle belongs to another save store", nameof(handle));
            lock (queueGate) return handle.Result;
        }

        // Explicit user recovery of this exact readable backup, including a chosen
        // rollback while current remains valid. Assistance is never added on read.
        public SaveWriteHandle RecoverPrevious(SaveDecisionTicket decision)
        {
            CheckDecisionOwner(decision);
            if (decision.Backup == null || (decision.ReadStatus != SaveReadStatus.Valid && decision.ReadStatus != SaveReadStatus.RecoveryRequired))
                throw new ArgumentException("This decision does not offer a supported backup", nameof(decision));
            return Queue(decision.Backup.AsAssistedRecovery(), WriteKind.RecoverPrevious, decision);
        }

        // Calling this method is the application's explicit new-profile decision.
        // No invalid bytes are removed; all extant generations are archived first.
        public SaveWriteHandle AuthorizeNewProfile(SaveDecisionTicket decision, PlayerSaveSnapshot freshSnapshot)
        {
            CheckDecisionOwner(decision);
            if (decision.ReadStatus != SaveReadStatus.NewProfileConfirmationRequired)
                throw new ArgumentException("New-profile confirmation is not offered for this decision", nameof(decision));
            if (freshSnapshot == null) throw new ArgumentNullException(nameof(freshSnapshot));
            JsonValue value = freshSnapshot.ToJson();
            if (!freshSnapshot.CurrentRun.IsNull || new[] { "mission_progress", "unlocks", "reward_receipts", "completed_runs" }.Any(key => value[key].Items.Count != 0))
                throw new ArgumentException("A new profile must begin without fabricated run or campaign progress", nameof(freshSnapshot));
            return Queue(freshSnapshot, WriteKind.NewProfile, decision);
        }

        public bool Pump()
        {
            WorkItem work;
            lock (ioGate)
            {
                lock (queueGate)
                {
                    work = queued;
                    if (work == null) return false;
                    queued = null;
                    work.Handle.Result = Result(work, SaveWriteStatus.Writing, 0, "SAVE_WRITING", "");
                }
                try
                {
                    long generation = Write(work);
                    lock (queueGate)
                    {
                        work.Handle.Result = Result(work, SaveWriteStatus.Succeeded, generation, "SAVE_WRITTEN", "");
                        warningEpisode = false;
                    }
                }
                catch (SaveRejectedException error)
                {
                    lock (queueGate) work.Handle.Result = Result(work, SaveWriteStatus.Rejected, 0, error.Code, error.Message);
                    if (error.Code == "SAVE_READ_ERROR" || error.Code == "SAVE_GENERATION_CONFLICT" || error.Code == "SAVE_FILES_CHANGED") Warn(error.Code);
                }
                catch (Exception error) when (IsRecoverable(error))
                {
                    string code = ErrorCode(error);
                    lock (queueGate) work.Handle.Result = Result(work, SaveWriteStatus.Failed, 0, code, error.Message);
                    Warn(code);
                }
            }
            return true;
        }

        private SaveWriteHandle Queue(PlayerSaveSnapshot snapshot, WriteKind kind, SaveDecisionTicket decision)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (queueGate)
            {
                long requestId = checked(nextRequestId + 1); nextRequestId = requestId;
                var work = new WorkItem { Snapshot = snapshot, Kind = kind, Decision = decision };
                var handle = new SaveWriteHandle(owner, requestId, Result(work, SaveWriteStatus.Queued, 0, "SAVE_QUEUED", ""));
                work.Handle = handle;
                if (queued != null)
                    queued.Handle.Result = Result(queued, SaveWriteStatus.Coalesced, 0, "SAVE_COALESCED", "Superseded by a newer complete queued generation");
                queued = work;
                return handle;
            }
        }

        private long Write(WorkItem work)
        {
            SaveReadResult before = ReadPair();
            RejectUnavailable(before);
            if (work.Decision != null && (before.Current.FileSha256 != StampHash(work.Decision.CurrentStamp)
                || before.Previous.FileSha256 != StampHash(work.Decision.PreviousStamp)
                || (before.Current.Status == SaveFileStatus.Missing) != (work.Decision.CurrentStamp == "missing")
                || (before.Previous.Status == SaveFileStatus.Missing) != (work.Decision.PreviousStamp == "missing")))
                throw new SaveRejectedException("SAVE_DECISION_STALE", "The exact files shown for this decision have changed");

            if (work.Kind == WriteKind.Ordinary && before.Status != SaveReadStatus.Empty && before.Status != SaveReadStatus.Valid)
                throw new SaveRejectedException("SAVE_DECISION_REQUIRED", "Explicit backup recovery or new-profile confirmation is required");
            if (work.Kind == WriteKind.RecoverPrevious && (before.Previous.Status != SaveFileStatus.Valid
                || before.Previous.PayloadSha256 != work.Decision.BackupPayloadSha256 || before.Previous.Generation != work.Decision.BackupGeneration))
                throw new SaveRejectedException("SAVE_BACKUP_CHANGED", "The exact offered backup is unavailable");
            if (work.Kind == WriteKind.NewProfile && before.Status != SaveReadStatus.NewProfileConfirmationRequired)
                throw new SaveRejectedException("SAVE_DECISION_STALE", "The profile no longer has the confirmed invalid-file state");

            // Revalidate with this store's content/domain owner, even if another
            // caller constructed the immutable snapshot with a different validator.
            PlayerSaveSnapshot payload = PlayerSaveSnapshot.Create(work.Snapshot.ToJson(), validateCurrentRun);
            if (work.Kind == WriteKind.Ordinary && before.Snapshot != null)
                PlayerSaveSnapshot.ValidateNoProgressLoss(before.Snapshot, payload);
            long latest = Math.Max(before.Current.Status == SaveFileStatus.Valid ? before.Current.Generation : 0,
                before.Previous.Status == SaveFileStatus.Valid ? before.Previous.Generation : 0);
            long generation = checked(latest + 1);
            JsonValue envelope = PlayerSaveSnapshot.Row("schema_version", JsonValue.Number(PlayerSaveSnapshot.SchemaVersion),
                "generation", JsonValue.Number(generation), "payload_sha256", JsonValue.String(payload.PayloadSha256), "payload", payload.ToJson());
            byte[] bytes = envelope.CanonicalBytes();
            if (bytes.Length > PlayerSaveSnapshot.MaximumEnvelopeBytes) throw new SaveValidationException("SAVE_SIZE", "Complete save envelope exceeds 64 MiB");
            files.CreateDirectory(directory);
            if (work.Kind == WriteKind.NewProfile) ArchiveInvalidFiles(before);
            VerifyPairUnchanged(before);
            files.WriteAndFlush(pendingPath, bytes, false);
            FileState pending = ReadFile(pendingPath);
            RequireWritten(pending, generation, payload.PayloadSha256, PhysicalSaveFileSystem.BytesSha256(bytes));
            VerifyPairUnchanged(before);

            // Keep the newest supported generation available. A higher-generation
            // previous file must never be replaced by a stale lower current file.
            bool preserveCurrent = before.Current.Status == SaveFileStatus.Valid
                && (before.Previous.Status != SaveFileStatus.Valid || before.Current.Generation >= before.Previous.Generation);
            if (preserveCurrent)
            {
                files.CopyAndFlush(currentPath, previousTemporaryPath, false);
                FileState copy = ReadFile(previousTemporaryPath);
                RequireWritten(copy, before.Current.Generation, before.Current.PayloadSha256, before.Current.FileSha256);
                VerifyPairUnchanged(before);
                files.AtomicReplace(previousTemporaryPath, previousPath);
                FileState backup = ReadFile(previousPath);
                RequireWritten(backup, before.Current.Generation, before.Current.PayloadSha256, before.Current.FileSha256);
            }
            // Recheck after the previous promotion and before replacing current.
            VerifyStamp(currentPath, before.Current.Status, before.Current.FileSha256);
            VerifyStamp(previousPath, preserveCurrent ? SaveFileStatus.Valid : before.Previous.Status,
                preserveCurrent ? before.Current.FileSha256 : before.Previous.FileSha256);
            RequireWritten(ReadFile(pendingPath), generation, payload.PayloadSha256, PhysicalSaveFileSystem.BytesSha256(bytes));
            files.AtomicReplace(pendingPath, currentPath);
            RequireWritten(ReadFile(currentPath), generation, payload.PayloadSha256, PhysicalSaveFileSystem.BytesSha256(bytes));
            return generation;
        }

        private SaveReadResult ReadPair()
        {
            FileState current = ReadFile(currentPath), previous = ReadFile(previousPath);
            SaveReadStatus status; FileState selected = null; SaveSource source = SaveSource.None; string code;
            if (current.Report.Status == SaveFileStatus.FutureSchema || previous.Report.Status == SaveFileStatus.FutureSchema)
            { status = SaveReadStatus.IncompatibleSchema; code = "SAVE_FUTURE_SCHEMA"; }
            else if (current.Report.Status == SaveFileStatus.StorageError || previous.Report.Status == SaveFileStatus.StorageError)
            { status = SaveReadStatus.StorageError; code = "SAVE_READ_ERROR"; }
            else if (current.Report.Status == SaveFileStatus.IncompatibleRun || previous.Report.Status == SaveFileStatus.IncompatibleRun
                && !(current.Report.Status == SaveFileStatus.Valid && current.Report.Generation > previous.Report.Generation))
            { status = SaveReadStatus.IncompatibleRun; code = "SAVE_RUN_INCOMPATIBLE"; }
            else if (current.Report.Status == SaveFileStatus.Valid && previous.Report.Status == SaveFileStatus.Valid
                && current.Report.Generation == previous.Report.Generation && current.Report.PayloadSha256 != previous.Report.PayloadSha256)
            { status = SaveReadStatus.StorageError; code = "SAVE_GENERATION_CONFLICT"; }
            else if (current.Report.Status == SaveFileStatus.Valid)
            {
                status = SaveReadStatus.Valid; code = "SAVE_VALID";
                bool usePrevious = previous.Report.Status == SaveFileStatus.Valid && previous.Report.Generation > current.Report.Generation;
                selected = usePrevious ? previous : current; source = usePrevious ? SaveSource.Previous : SaveSource.Current;
            }
            else if (previous.Report.Status == SaveFileStatus.Valid)
            { status = SaveReadStatus.RecoveryRequired; selected = previous; source = SaveSource.Previous; code = "SAVE_RECOVERY_REQUIRED"; }
            else if (current.Report.Status == SaveFileStatus.Missing && previous.Report.Status == SaveFileStatus.Missing)
            { status = SaveReadStatus.Empty; code = "SAVE_EMPTY"; }
            else { status = SaveReadStatus.NewProfileConfirmationRequired; code = "SAVE_NEW_PROFILE_CONFIRMATION_REQUIRED"; }
            SaveDecisionTicket decision = status == SaveReadStatus.RecoveryRequired || status == SaveReadStatus.NewProfileConfirmationRequired
                || (status == SaveReadStatus.Valid && previous.Report.Status == SaveFileStatus.Valid)
                ? new SaveDecisionTicket(owner, current, previous, status) : null;
            return new SaveReadResult(status, selected, source, code, decision, current, previous);
        }

        private FileState ReadFile(string path)
        {
            string rawHash = null, payloadHash = null; long schema = 0, generation = 0;
            try
            {
                if (!files.Exists(path)) return State(SaveFileStatus.Missing, 0, 0, null, null, "SAVE_MISSING", "");
                byte[] bytes = files.ReadAllBytes(path, PlayerSaveSnapshot.MaximumEnvelopeBytes);
                rawHash = PhysicalSaveFileSystem.BytesSha256(bytes);
                JsonValue envelope = StrictJson.Parse(bytes);
                if (envelope.Kind != "object" || !envelope.Has("schema_version")) throw new SaveValidationException("SAVE_ENVELOPE", "Save envelope is absent");
                PlayerSaveSnapshot.Number(envelope["schema_version"], 1, long.MaxValue, "schema_version"); schema = envelope["schema_version"].Integer;
                // A future schema can add framing fields. Only common strict framing
                // plus a matching canonical payload digest establishes incompatibility;
                // merely setting a future version on corrupt bytes does not suffice.
                if (schema == PlayerSaveSnapshot.SchemaVersion)
                    PlayerSaveSnapshot.Fields(envelope, "schema_version", "generation", "payload_sha256", "payload");
                PlayerSaveSnapshot.Number(envelope["generation"], 1, long.MaxValue, "generation"); generation = envelope["generation"].Integer;
                PlayerSaveSnapshot.Hash(envelope["payload_sha256"]); payloadHash = envelope["payload_sha256"].Text;
                if (envelope["payload"].Kind != "object" || envelope["payload"].Sha256() != payloadHash)
                    throw new SaveValidationException("SAVE_INTEGRITY", "Payload SHA-256 does not match its canonical bytes");
                if (schema > PlayerSaveSnapshot.SchemaVersion)
                    return State(SaveFileStatus.FutureSchema, generation, schema, rawHash, payloadHash, "SAVE_FUTURE_SCHEMA", "A compatible future build is required");
                PlayerSaveSnapshot payload = PlayerSaveSnapshot.Create(envelope["payload"], validateCurrentRun);
                return State(SaveFileStatus.Valid, generation, schema, rawHash, payloadHash, "SAVE_VALID", "", payload);
            }
            catch (SaveRunIncompatibleException error)
            { return State(SaveFileStatus.IncompatibleRun, generation, schema, rawHash, payloadHash, "SAVE_RUN_INCOMPATIBLE", error.Message); }
            catch (SaveFileTooLargeException error)
            {
                // A future schema might have a larger framing bound. Without
                // bounded strict/hash inspection we cannot call it corrupt or
                // issue reset consent; preserve it and require diagnosis.
                return State(SaveFileStatus.StorageError, generation, schema, rawHash, payloadHash, "SAVE_SIZE_UNCLASSIFIED", error.Message);
            }
            catch (IOException error)
            { return State(SaveFileStatus.StorageError, generation, schema, rawHash, payloadHash, "SAVE_IO", error.Message); }
            catch (UnauthorizedAccessException error)
            { return State(SaveFileStatus.StorageError, generation, schema, rawHash, payloadHash, "SAVE_ACCESS", error.Message); }
            catch (Exception error) when (IsRecoverable(error))
            { return State(SaveFileStatus.Corrupt, generation, schema, rawHash, payloadHash, ErrorCode(error), error.Message); }
        }

        private void ArchiveInvalidFiles(SaveReadResult before)
        {
            VerifyPairUnchanged(before);
            string archive = Path.Combine(directory, "save.recovery-archive", Guid.NewGuid().ToString("N"));
            files.CreateDirectory(archive);
            ArchiveOne(currentPath, Path.Combine(archive, CurrentFilename));
            ArchiveOne(previousPath, Path.Combine(archive, PreviousFilename));
            ArchiveOne(pendingPath, Path.Combine(archive, PendingFilename));
            ArchiveOne(previousTemporaryPath, Path.Combine(archive, "save.previous.pending.json"));
            VerifyPairUnchanged(before);
        }
        private void ArchiveOne(string source, string destination)
        {
            if (!files.Exists(source)) return;
            string hash = files.FileSha256(source);
            files.CopyAndFlush(source, destination, true);
            if (files.FileSha256(destination) != hash || files.FileSha256(source) != hash)
                throw new IOException("Invalid save archive failed exact byte verification");
        }
        private void VerifyPairUnchanged(SaveReadResult before)
        {
            VerifyStamp(currentPath, before.Current.Status, before.Current.FileSha256);
            VerifyStamp(previousPath, before.Previous.Status, before.Previous.FileSha256);
        }
        private void VerifyStamp(string path, SaveFileStatus status, string hash)
        {
            bool exists = files.Exists(path);
            if (status == SaveFileStatus.Missing ? exists : !exists || hash == null || files.FileSha256(path) != hash)
                throw new SaveRejectedException("SAVE_FILES_CHANGED", "Save files changed outside the store during this operation");
        }
        private static void RequireWritten(FileState state, long generation, string payloadHash, string rawHash)
        {
            if (state.Report.Status != SaveFileStatus.Valid || state.Report.Generation != generation
                || state.Report.PayloadSha256 != payloadHash || state.Report.FileSha256 != rawHash)
                throw new IOException("Written save failed strict readback, generation or hash validation: " + state.Report.ReasonCode);
        }
        private static void RejectUnavailable(SaveReadResult before)
        {
            if (before.Status == SaveReadStatus.IncompatibleSchema || before.Status == SaveReadStatus.IncompatibleRun || before.Status == SaveReadStatus.StorageError)
                throw new SaveRejectedException(before.ReasonCode, "Existing save files require compatible content or a resolved read failure; they were preserved");
        }
        private void CheckDecisionOwner(SaveDecisionTicket decision)
        {
            if (decision == null || !ReferenceEquals(decision.Owner, owner)) throw new ArgumentException("Decision belongs to another save store", nameof(decision));
        }
        private void Warn(string code)
        {
            lock (queueGate) { if (warningEpisode) return; warningEpisode = true; }
            // A UI warning sink cannot make an actually persisted generation fail.
            if (warning != null) try { warning(code); } catch (Exception error) when (IsRecoverable(error)) { }
        }
        private static string StampHash(string stamp) => stamp == "missing" ? null : stamp;
        private static SaveWriteResult Result(WorkItem item, SaveWriteStatus status, long generation, string code, string detail)
            => new SaveWriteResult(status, generation, item.Snapshot.PayloadSha256, code, detail, item.Kind == WriteKind.RecoverPrevious);
        private static FileState State(SaveFileStatus status, long generation, long schema, string rawHash, string payloadHash,
            string code, string detail, PlayerSaveSnapshot payload = null)
            => new FileState(new SaveFileReport(status, generation, schema, rawHash, payloadHash, code, detail), payload);
        private static string ErrorCode(Exception error)
        {
            if (error is SaveRunIncompatibleException) return "SAVE_RUN_INCOMPATIBLE";
            if (error is SaveValidationException validation) return validation.Code;
            if (error is ContentException content) return content.Code;
            if (error is OverflowException) return "SAVE_OVERFLOW";
            if (error is UnauthorizedAccessException) return "SAVE_ACCESS";
            if (error is IOException) return "SAVE_IO";
            return "SAVE_VALIDATION";
        }
        private static bool IsRecoverable(Exception error) => !(error is OutOfMemoryException) && !(error is StackOverflowException) && !(error is AccessViolationException);
        private sealed class SaveRejectedException : Exception
        {
            public string Code { get; }
            public SaveRejectedException(string code, string detail) : base(detail) { Code = code; }
        }
    }
}
