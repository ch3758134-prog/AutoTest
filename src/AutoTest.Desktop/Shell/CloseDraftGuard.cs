namespace AutoTest.Desktop.Shell;

/// <summary>
/// One explicit exit/switch decision. It is independent of WPF chrome and persistence
/// implementations so that cancellation, failed saves and busy editors are testable.
/// </summary>
public enum CloseDraftDecision { Save, Discard, Cancel }

public sealed record CloseDraftDocument(
    string Title,
    Func<bool> IsBusy,
    Func<bool> IsDirty,
    Func<Task<bool>> SaveAsync);

public static class CloseDraftGuard
{
    public static async Task<bool> PrepareAsync(
        IReadOnlyList<CloseDraftDocument> documents,
        Func<IReadOnlyList<string>, CloseDraftDecision> prompt,
        Action<string> reportError)
    {
        try
        {
            // Never discard data while an editor is writing files or a real test is running.
            var busy = documents.FirstOrDefault(x => x.IsBusy());
            if (busy is not null)
            {
                reportError("“" + busy.Title + "”正在执行操作。请先完成或停止操作，再退出。");
                return false;
            }

            var changed = documents.Where(x => x.IsDirty()).ToArray();
            if (changed.Length == 0) return true;
            switch (prompt(changed.Select(x => x.Title).ToArray()))
            {
                case CloseDraftDecision.Cancel:
                    return false;
                case CloseDraftDecision.Discard:
                    return true; // caller closes without writing *any* draft
                case CloseDraftDecision.Save:
                    foreach (var document in changed)
                    {
                        // Wait for actual async persistence and re-check, not Execute()
                        // on an async-void DelegateCommand.
                        if (!await document.SaveAsync() || document.IsDirty())
                        {
                            reportError("“" + document.Title + "”尚未保存成功；已取消退出，草稿仍保留。");
                            return false;
                        }
                    }
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            reportError("保存或退出检查发生异常，已取消退出：" + e.Message);
            return false;
        }
    }
}
