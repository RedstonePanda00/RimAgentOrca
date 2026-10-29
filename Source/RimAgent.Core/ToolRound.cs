namespace RimAgent.Core;

// Owns sequencing only. Business subclasses own permissions, budgets and result meaning.
public abstract class ToolRound<TCall, TOperation, TResult>
{
    public IEnumerable<bool> Run(IReadOnlyList<TCall> calls)
    {
        if (!BeginRound()) yield break;
        foreach (var call in calls)
        {
            if (!ShouldRun(call)) continue;
            var operation = StartCall(call);
            try
            {
                TResult result;
                while (!TryComplete(operation, out result)) yield return false;
                AcceptResult(call, operation, result);
            }
            finally { Release(operation); }
        }
        FinishRound();
    }
    protected virtual bool BeginRound() => true;
    protected virtual bool ShouldRun(TCall call) => true;
    protected abstract TOperation StartCall(TCall call);
    protected abstract bool TryComplete(TOperation operation, out TResult result);
    protected abstract void AcceptResult(TCall call, TOperation operation, TResult result);
    protected virtual void Release(TOperation operation) { }
    protected virtual void FinishRound() { }
}
