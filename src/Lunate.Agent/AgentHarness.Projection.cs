namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private int _projectionTurnsSinceBatch;
    private bool _projectionBatchPending;

    private void AdvanceProjectionTurn()
    {
        _projectionTurnsSinceBatch++;
        if (_projectionTurnsSinceBatch >= _options.ProjectionBatchTurns)
        {
            _projectionBatchPending = true;
        }
    }

    private void ApplyProjectionBatchIfDue()
    {
        if (!_projectionBatchPending)
        {
            return;
        }

        StubStaleReadResults();
        _projectionTurnsSinceBatch = 0;
        _projectionBatchPending = false;
    }
}
