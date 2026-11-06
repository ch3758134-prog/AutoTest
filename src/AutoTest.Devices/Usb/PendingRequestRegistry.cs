namespace AutoTest.Devices.Usb;
public sealed class PendingRequestRegistry
{
    private sealed record Entry(long Generation, byte Command, TaskCompletionSource<UsbResponse> Source);
    private readonly object _sync = new();
    private readonly Dictionary<ushort,Entry> _pending = new();
    private ushort _next;
    public (ushort Sequence, Task<UsbResponse> Response) Register(long generation,byte command)
    {
        lock(_sync)
        {
            for(int i=0;i<ushort.MaxValue;i++)
            {
                _next++; if(_next==0) _next=1;
                if(_pending.ContainsKey(_next)) continue;
                var source = new TaskCompletionSource<UsbResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(_next,new(generation,command,source)); return (_next,source.Task);
            }
            throw new InvalidOperationException("No USB request sequences available.");
        }
    }
    public bool TryComplete(long generation,UsbResponse response)
    {
        if(response.Sequence==0) return false;
        Entry? entry;
        lock(_sync)
        {
            if(!_pending.TryGetValue(response.Sequence,out entry) || entry.Generation!=generation || entry.Command!=response.Command) return false;
            _pending.Remove(response.Sequence);
        }
        entry.Source.TrySetResult(response); return true;
    }
    public void Fail(long generation,ushort sequence,Exception exception)
    {
        Entry? entry;
        lock(_sync)
        {
            if(!_pending.TryGetValue(sequence,out entry) || entry.Generation!=generation) return;
            _pending.Remove(sequence);
        }
        entry.Source.TrySetException(exception);
    }
    public void FailGeneration(long generation,Exception exception)
    {
        List<Entry> removed;
        lock(_sync)
        {
            var keys=_pending.Where(x=>x.Value.Generation==generation).Select(x=>x.Key).ToArray();
            removed=keys.Select(x=>_pending[x]).ToList(); foreach(var key in keys) _pending.Remove(key);
        }
        foreach(var entry in removed) entry.Source.TrySetException(exception);
    }
}
