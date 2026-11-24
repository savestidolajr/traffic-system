namespace ArnBulletinGenerator.E2E.Tests;

internal static class Eventually
{
    public static async Task UntilAsync(
        Func<Task<bool>> predicate,
        TimeSpan timeout,
        TimeSpan interval,
        string failureMessage)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await predicate())
                return;
            try
            {
                await Task.Delay(interval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException(failureMessage);
    }
}
