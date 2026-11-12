using Meddle.Utils.Export;

namespace Meddle.Utils.Tests;

[TestClass]
public sealed class BackgroundExportTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExportDoesNotHoldTheCaptureThread(bool alreadyCaptured)
    {
        // Deliberately allow inline continuations, as a framework-tick task can.
        var capture = new TaskCompletionSource<int>();
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var received = 0;
        void Export(int snapshot)
        {
            received = snapshot;
            entered.SetResult(Environment.CurrentManagedThreadId);
            release.Wait(TimeSpan.FromSeconds(10));
        }

        Task export = Task.CompletedTask;
        if (!alreadyCaptured) export = BackgroundExport.RunAfterCaptureAsync(capture.Task, Export);
        var gameThread = new Thread(() =>
        {
            if (alreadyCaptured)
                export = BackgroundExport.RunAfterCaptureAsync(Task.FromResult(42), Export);
            else
                capture.SetResult(42);
            returned.SetResult(Environment.CurrentManagedThreadId);
        }) { IsBackground = true };
        gameThread.Start();
        try
        {
            var workerId = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var gameId = await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreNotEqual(gameId, workerId);
            Assert.AreEqual(42, received);
            Assert.IsFalse(export.IsCompleted);
        }
        finally
        {
            release.Set();
            await export.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task CancellationBeforeCaptureCompletesSkipsExport()
    {
        using var cancel = new CancellationTokenSource();
        var capture = new TaskCompletionSource<int>();
        var called = false;
        var export = BackgroundExport.RunAfterCaptureAsync(capture.Task, _ => called = true, cancel.Token);
        cancel.Cancel();
        capture.SetResult(42);
        await Assert.ThrowsAsync<OperationCanceledException>(() => export);
        Assert.IsFalse(called);
    }

    [TestMethod]
    public async Task ExportFailureIsReportedToCaller()
    {
        var error = new InvalidOperationException("export failed");
        var export = BackgroundExport.RunAfterCaptureAsync(Task.FromResult(42), _ => throw error);
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => export);
        Assert.AreSame(error, actual);
    }
}
