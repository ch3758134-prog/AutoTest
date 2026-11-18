namespace EPB.Core.Testing;

/// <summary>对明确标记为瞬时忙的命令做有限重试；其它结果只执行一次。</summary>
public static class TransientCommandRetry
{
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> command,
        Func<T, bool> isTransientBusy,
        int maxRetries = 3,
        int delayMs = 25,
        CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (isTransientBusy is null) throw new ArgumentNullException(nameof(isTransientBusy));
        if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(maxRetries));

        for (var attempt = 0; ; attempt++)
        {
            var result = await command().ConfigureAwait(false);
            if (!isTransientBusy(result) || attempt >= maxRetries) return result;
            if (delayMs > 0) await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            else cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
