using Xunit;
using System.Windows.Threading;

namespace GarlicSaveMgr.Tests;

public sealed class DispatcherRefreshTests
{
    [Fact]
    public async Task Dispatcher_queues_refresh_back_to_owner_thread()
    {
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var threadReady = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            threadReady.TrySetResult(dispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var ownerDispatcher = await threadReady.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var ownerThreadId = thread.ManagedThreadId;

        _ = ownerDispatcher.BeginInvoke(new Action(() =>
        {
            completed.TrySetResult(Environment.CurrentManagedThreadId);
            ownerDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        }), DispatcherPriority.Normal);

        Assert.Equal(ownerThreadId, await completed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
