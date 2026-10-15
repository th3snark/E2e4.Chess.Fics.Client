namespace E2e4.Chess.Fics.Protocol;

/// <summary>
/// Hands out block-mode command identifiers (1-9, cycling; 0 is reserved for
/// server-posed output) and completes the matching pending command when its
/// framed response arrives. When all nine ids are in flight, renting waits.
/// </summary>
internal sealed class BlockCommandTracker : IDisposable
{
    private const int MaxId = 9;

    private readonly SemaphoreSlim _slots = new(MaxId, MaxId);
    private readonly TaskCompletionSource<FicsResponse>?[] _pending = new TaskCompletionSource<FicsResponse>?[MaxId + 1];
    private readonly object _lock = new();
    private int _next = 1;

    /// <summary>Disposes the slot semaphore; later releases from commands still unwinding are ignored.</summary>
    public void Dispose() => _slots.Dispose();

    private void ReleaseSlot()
    {
        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task<int> RentAsync(TaskCompletionSource<FicsResponse> completion, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            for (int i = 0; i < MaxId; i++)
            {
                int id = _next;
                _next = _next % MaxId + 1;
                if (_pending[id] is null)
                {
                    _pending[id] = completion;
                    return id;
                }
            }
        }

        // Unreachable: the semaphore guarantees a free slot.
        throw new InvalidOperationException("No free block command identifier.");
    }

    /// <summary>
    /// Gives back an id whose command never reached the server (so no response will ever
    /// free it). Does nothing if the id has already been completed or reassigned.
    /// </summary>
    public void Release(int id, TaskCompletionSource<FicsResponse> completion)
    {
        if (id < 1 || id > MaxId)
        {
            return;
        }

        lock (_lock)
        {
            if (!ReferenceEquals(_pending[id], completion))
            {
                return;
            }

            _pending[id] = null;
        }

        ReleaseSlot();
    }

    public bool TryComplete(int id, FicsResponse response)
    {
        if (id < 1 || id > MaxId)
        {
            return false;
        }

        TaskCompletionSource<FicsResponse>? completion;
        lock (_lock)
        {
            completion = _pending[id];
            _pending[id] = null;
        }

        if (completion is null)
        {
            return false;
        }

        ReleaseSlot();
        return completion.TrySetResult(response);
    }

    public void FaultAll(Exception exception)
    {
        lock (_lock)
        {
            for (int id = 1; id <= MaxId; id++)
            {
                TaskCompletionSource<FicsResponse>? completion = _pending[id];
                _pending[id] = null;
                if (completion is not null)
                {
                    ReleaseSlot();
                    completion.TrySetException(exception);
                }
            }
        }
    }
}
