namespace Meddle.Utils.Export;

public static class BackgroundExport
{
    /// <summary>
    /// Snapshot completion may run its continuations inline on the game thread.
    /// Explicitly queue CPU and file work after capture, regardless of which
    /// thread completes the snapshot task or whether it has already completed.
    /// </summary>
    public static async Task RunAfterCaptureAsync<T>(Task<T> capture, Action<T> export,
        CancellationToken token = default)
    {
        var snapshot = await capture.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await Task.Run(() => export(snapshot), token).ConfigureAwait(false);
    }
}
