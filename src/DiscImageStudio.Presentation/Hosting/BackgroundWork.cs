namespace DiscImageStudio.Hosting;

/// <summary>Runs CPU-bound generation, composition and decoding off the UI thread. No
/// apartment is required: imaging uses Skia and the burners own their threads.</summary>
internal static class BackgroundWork
{
    internal static Task<T> RunAsync<T>(Func<T> action)
        => Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
}
