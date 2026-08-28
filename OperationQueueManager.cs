namespace AzertyCommander;

internal enum QueuedOperationStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Canceled,
    Failed
}

internal sealed class QueuedOperation
{
    private readonly Func<IProgress<OperationProgress>, CancellationToken, Task> _work;
    private readonly Action? _completed;

    internal QueuedOperation(
        string title,
        string details,
        Func<IProgress<OperationProgress>, CancellationToken, Task> work,
        Action? completed)
    {
        Id = Guid.NewGuid();
        Title = title;
        Details = details;
        _work = work;
        _completed = completed;
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Details { get; }
    public QueuedOperationStatus Status { get; internal set; } = QueuedOperationStatus.Queued;
    public OperationProgress? Progress { get; internal set; }
    public string Error { get; internal set; } = string.Empty;
    public DateTime? StartedUtc { get; internal set; }
    public DateTime? FinishedUtc { get; internal set; }
    public DateTime? PauseStartedUtc { get; internal set; }
    public TimeSpan PausedDuration { get; internal set; }
    internal CancellationTokenSource Cancellation { get; private set; } = new();
    internal ManualResetEventSlim PauseGate { get; } = new(initialState: true);
    internal Func<IProgress<OperationProgress>, CancellationToken, Task> Work => _work;
    internal Action? Completed => _completed;

    internal void ResetForRetry()
    {
        Cancellation.Dispose();
        Cancellation = new CancellationTokenSource();
        PauseGate.Set();
        Status = QueuedOperationStatus.Queued;
        Progress = null;
        Error = string.Empty;
        StartedUtc = null;
        FinishedUtc = null;
        PauseStartedUtc = null;
        PausedDuration = TimeSpan.Zero;
    }

    public string StatusText => Status switch
    {
        QueuedOperationStatus.Queued => "Ожидает",
        QueuedOperationStatus.Running => "Выполняется",
        QueuedOperationStatus.Paused => "Пауза",
        QueuedOperationStatus.Completed => "Готово",
        QueuedOperationStatus.Canceled => "Отменено",
        QueuedOperationStatus.Failed => "Ошибка",
        _ => Status.ToString()
    };

    public string ProgressText
    {
        get
        {
            if (Status == QueuedOperationStatus.Completed)
            {
                return "100%";
            }

            if (Progress is null)
            {
                return string.Empty;
            }

            var total = Progress.BytesTotal > 0 ? Progress.BytesTotal : Progress.Total;
            var done = Progress.BytesTotal > 0 ? Progress.BytesDone : Progress.Current;
            if (total <= 0)
            {
                return "выполняется";
            }

            var percent = Math.Clamp((double)done / total * 100D, 0D, 100D);
            if (Progress.BytesTotal <= 0 || StartedUtc is null)
            {
                return $"{percent:0}%";
            }

            var paused = PausedDuration;
            if (PauseStartedUtc is { } pausedAt)
            {
                paused += DateTime.UtcNow - pausedAt;
            }
            var elapsed = Math.Max(0.001D, (DateTime.UtcNow - StartedUtc.Value - paused).TotalSeconds);
            var speed = Progress.BytesDone / elapsed;
            var remaining = speed > 1D
                ? TimeSpan.FromSeconds(Math.Max(0D, Progress.BytesTotal - Progress.BytesDone) / speed)
                : (TimeSpan?)null;
            return $"{percent:0}%  {FormatBytes(speed)}/с  {FormatEta(remaining)}";
        }
    }

    public string CurrentMessage => Status == QueuedOperationStatus.Failed && Error.Length > 0
        ? Error
        : Progress?.Message ?? Details;

    private static string FormatBytes(double bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var index = 0;
        while (bytes >= 1024D && index < units.Length - 1)
        {
            bytes /= 1024D;
            index++;
        }

        return index == 0 ? $"{bytes:N0} {units[index]}" : $"{bytes:N1} {units[index]}";
    }

    private static string FormatEta(TimeSpan? value)
    {
        if (value is null)
        {
            return "осталось: считаю";
        }

        return value.Value.TotalHours >= 1
            ? "осталось: " + value.Value.ToString(@"h\:mm\:ss")
            : "осталось: " + value.Value.ToString(@"m\:ss");
    }
}

internal sealed class OperationQueueManager : IDisposable
{
    private readonly List<QueuedOperation> _items = new();
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private bool _processing;
    private bool _disposed;

    public event EventHandler? Changed;

    public IReadOnlyList<QueuedOperation> Items => _items.ToList();

    public bool HasActiveOperations => _items.Any(item => item.Status is QueuedOperationStatus.Queued or QueuedOperationStatus.Running or QueuedOperationStatus.Paused);

    public QueuedOperation Enqueue(
        string title,
        string details,
        Func<IProgress<OperationProgress>, CancellationToken, Task> work,
        Action? completed = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var item = new QueuedOperation(title, details, work, completed);
        _items.Add(item);
        NotifyChanged();
        if (!_processing)
        {
            _ = ProcessAsync();
        }

        return item;
    }

    public void Pause(QueuedOperation item)
    {
        if (item.Status != QueuedOperationStatus.Running)
        {
            return;
        }

        item.PauseGate.Reset();
        item.Status = QueuedOperationStatus.Paused;
        item.PauseStartedUtc = DateTime.UtcNow;
        NotifyChanged();
    }

    public void Resume(QueuedOperation item)
    {
        if (item.Status != QueuedOperationStatus.Paused)
        {
            return;
        }

        item.Status = QueuedOperationStatus.Running;
        if (item.PauseStartedUtc is { } pausedAt)
        {
            item.PausedDuration += DateTime.UtcNow - pausedAt;
            item.PauseStartedUtc = null;
        }
        item.PauseGate.Set();
        NotifyChanged();
    }

    public void Cancel(QueuedOperation item)
    {
        if (item.Status == QueuedOperationStatus.Queued)
        {
            item.Status = QueuedOperationStatus.Canceled;
            item.FinishedUtc = DateTime.UtcNow;
        }

        if (item.Status is QueuedOperationStatus.Running or QueuedOperationStatus.Paused)
        {
            item.Cancellation.Cancel();
        }

        item.PauseGate.Set();
        NotifyChanged();
    }

    public void Retry(QueuedOperation item)
    {
        if (item.Status is not (QueuedOperationStatus.Failed or QueuedOperationStatus.Canceled))
        {
            return;
        }

        item.ResetForRetry();
        NotifyChanged();
        if (!_processing)
        {
            _ = ProcessAsync();
        }
    }

    public void MoveUp(QueuedOperation item)
    {
        MoveQueued(item, -1);
    }

    public void MoveDown(QueuedOperation item)
    {
        MoveQueued(item, 1);
    }

    public void ClearFinished()
    {
        foreach (var item in _items.Where(item => item.Status is QueuedOperationStatus.Completed or QueuedOperationStatus.Canceled or QueuedOperationStatus.Failed).ToList())
        {
            item.Cancellation.Dispose();
            item.PauseGate.Dispose();
            _items.Remove(item);
        }
        NotifyChanged();
    }

    public async Task WaitForIdleAsync()
    {
        while (HasActiveOperations)
        {
            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var item in _items)
        {
            item.Cancellation.Cancel();
            item.PauseGate.Set();
        }
    }

    private void MoveQueued(QueuedOperation item, int direction)
    {
        if (item.Status != QueuedOperationStatus.Queued)
        {
            return;
        }

        var index = _items.IndexOf(item);
        var candidate = index + direction;
        while (candidate >= 0 && candidate < _items.Count && _items[candidate].Status != QueuedOperationStatus.Queued)
        {
            candidate += direction;
        }

        if (candidate < 0 || candidate >= _items.Count)
        {
            return;
        }

        _items.RemoveAt(index);
        _items.Insert(candidate, item);
        NotifyChanged();
    }

    private async Task ProcessAsync()
    {
        if (_processing || _disposed)
        {
            return;
        }

        _processing = true;
        try
        {
            while (!_disposed)
            {
                var item = _items.FirstOrDefault(candidate => candidate.Status == QueuedOperationStatus.Queued);
                if (item is null)
                {
                    break;
                }

                item.Status = QueuedOperationStatus.Running;
                item.StartedUtc = DateTime.UtcNow;
                NotifyChanged();
                var progress = new BlockingProgress(item, NotifyChanged);
                try
                {
                    await item.Work(progress, item.Cancellation.Token);
                    item.Status = QueuedOperationStatus.Completed;
                    item.Progress = item.Progress is { } finalProgress
                        ? finalProgress with
                        {
                            Current = Math.Max(finalProgress.Current, finalProgress.Total),
                            BytesDone = Math.Max(finalProgress.BytesDone, finalProgress.BytesTotal)
                        }
                        : new OperationProgress(1, 1, "Готово");
                    item.Completed?.Invoke();
                }
                catch (OperationCanceledException)
                {
                    item.Status = QueuedOperationStatus.Canceled;
                }
                catch (Exception ex)
                {
                    item.Status = QueuedOperationStatus.Failed;
                    item.Error = ex.Message;
                }
                finally
                {
                    if (item.PauseStartedUtc is { } pausedAt)
                    {
                        item.PausedDuration += DateTime.UtcNow - pausedAt;
                        item.PauseStartedUtc = null;
                    }
                    item.FinishedUtc = DateTime.UtcNow;
                    item.PauseGate.Set();
                    NotifyChanged();
                }
            }
        }
        finally
        {
            _processing = false;
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        if (_synchronizationContext is not null && SynchronizationContext.Current != _synchronizationContext)
        {
            _synchronizationContext.Post(_ => Changed?.Invoke(this, EventArgs.Empty), null);
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class BlockingProgress : IProgress<OperationProgress>
    {
        private readonly QueuedOperation _item;
        private readonly Action _changed;

        public BlockingProgress(QueuedOperation item, Action changed)
        {
            _item = item;
            _changed = changed;
        }

        public void Report(OperationProgress value)
        {
            _item.PauseGate.Wait(_item.Cancellation.Token);
            _item.Progress = value;
            _changed();
        }
    }
}
