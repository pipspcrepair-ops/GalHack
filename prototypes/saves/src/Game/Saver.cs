using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalHack.Persistence;
using GalHack.Records;
using Godot;

namespace Saves;

/// <summary>
/// The host's save loop: the one writer. It owns the store and the record table, autosaves
/// every <see cref="AutosaveSeconds"/> on a worker thread (records are immutable, so the game
/// keeps playing while the disk works), commits synchronously when a zone crossing or unload
/// needs it, and takes rolling backups after saves.
/// </summary>
public partial class Saver : Node
{
    public double AutosaveSeconds { get; set; } = 30;

    public SaveStore Store { get; private set; }
    public RecordTable Table { get; private set; }
    public BackupScheduler Backups { get; private set; }
    public LoadResult LastLoad { get; private set; }
    public CommitResult LastCommit { get; private set; }
    public DateTime LastCommitAt { get; private set; }
    public string LastError { get; private set; }
    public string LastBackup { get; private set; }
    public double UntilAutosave => Math.Max(0, AutosaveSeconds - _sinceSave);
    public bool Saving => _pending != null;

    /// <summary>Raised on the main thread before every save: write live node state into records.</summary>
    public event Action BeforeSave;

    Task<CommitResult> _pending;
    CommitPlan _pendingPlan;
    double _sinceSave;

    /// <summary>Opens the save in <paramref name="folder"/>, starting a new world there if there is none.</summary>
    public LoadResult Open(string folder)
    {
        Store = new SaveStore(folder, GameRecords.Schemas());
        Backups = new BackupScheduler(Store);
        if (!Store.Exists && !System.IO.Directory.Exists(Store.RecordsFolder))
        {
            Store.Create();
            var table = new RecordTable();
            StarterWorld.Create(table);
            CommitPlan plan = table.PrepareCommit();
            Store.Commit(plan);
            table.CompleteCommit(plan);
            Store.CreateBackup("auto");
        }
        LastLoad = Store.Load();
        Table = LastLoad.Table;
        return LastLoad;
    }

    public override void _Process(double delta)
    {
        FinishPending(wait: false);
        if (Table == null) return;
        _sinceSave += delta;
        if (_sinceSave >= AutosaveSeconds) SaveSoon();
    }

    /// <summary>Starts a save on a worker thread, unless one is already running.</summary>
    public void SaveSoon(FaultInjector faults = null)
    {
        if (Table == null || _pending != null) return;
        _sinceSave = 0;
        BeforeSave?.Invoke();
        if (Table.PendingChanges == 0) return;
        if (!TryPrepare(out CommitPlan plan)) return;
        _pendingPlan = plan;
        _pending = Task.Run(() =>
        {
            CommitResult result = Store.Commit(plan, faults);
            try
            {
                BackupInfo made = Backups.Tick();
                if (made != null) LastBackup = made.Name;
            }
            catch (Exception e) when (e is System.IO.IOException or InvalidOperationException)
            {
                LastError = "backup failed: " + e.Message; // the save itself landed
            }
            return result;
        });
    }

    /// <summary>
    /// Saves right now on this thread, after any save already running. For zone crossings and
    /// unloads, which must not go on until their record is on disk. False if the save failed.
    /// </summary>
    public bool SaveNow(string why, FaultInjector faults = null)
    {
        if (Table == null) return false;
        FinishPending(wait: true);
        BeforeSave?.Invoke();
        if (!TryPrepare(out CommitPlan plan)) return false;
        try
        {
            LastCommit = Store.Commit(plan, faults);
            Table.CompleteCommit(plan);
            LastCommitAt = DateTime.Now;
            LastError = null;
            _sinceSave = 0;
            return true;
        }
        catch (Exception e) when (e is not SimulatedCrash)
        {
            LastError = $"save failed while {why}: {e.Message}";
            GD.PrintErr(LastError);
            return false;
        }
    }

    bool TryPrepare(out CommitPlan plan)
    {
        try
        {
            plan = Table.PrepareCommit();
            return true;
        }
        catch (SaveRefusedException e)
        {
            LastError = e.Message;
            GD.PrintErr(e.Message);
            plan = null;
            return false;
        }
    }

    /// <summary>Takes a finished background save's result. With <paramref name="wait"/>, waits for it.</summary>
    public void FinishPending(bool wait)
    {
        if (_pending == null || (!wait && !_pending.IsCompleted)) return;
        try
        {
            LastCommit = _pending.Result;
            Table.CompleteCommit(_pendingPlan);
            LastCommitAt = DateTime.Now;
            LastError = null;
        }
        catch (AggregateException e)
        {
            // The records stay dirty, and the next save tries again. The last commit is still the save.
            LastError = "save failed: " + e.InnerException?.Message;
            GD.PrintErr(LastError);
        }
        _pending = null;
        _pendingPlan = null;
    }

    /// <summary>
    /// The chaos key: marks every record dirty, starts a save, and kills the whole process from
    /// the save's own thread at a random write step. The next launch has to recover from it.
    /// </summary>
    public string ChaosKill(int seed)
    {
        FinishPending(wait: true);
        foreach (SaveRecord r in new List<SaveRecord>(Table.All)) Table.MarkDirty(r.Id);
        int changed = Table.PendingChanges;
        int points = 3 * changed + 4; // a torn and a written point per record, the flush, the manifest's two, the commit, a delete each
        var rng = new Random(seed);
        long crashAt = rng.Next(1, points);
        string message = $"CHAOS: killing the game at write step {crashAt} of about {points} of a {changed}-record save";
        GD.Print(message);
        var faults = new FaultInjector
        {
            CrashAt = crashAt,
            OnCrash = point =>
            {
                Console.Out.WriteLine($"CHAOS: killed at '{point}'");
                Console.Out.Flush();
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            },
        };
        _sinceSave = 0;
        BeforeSave?.Invoke();
        if (TryPrepare(out CommitPlan plan))
        {
            _pendingPlan = plan;
            _pending = Task.Run(() => Store.Commit(plan, faults));
        }
        return message;
    }
}
