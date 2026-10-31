namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private int _projectionTurnsSinceBatch;
    private int _projectionTurn;
    private bool _projectionBatchPending;

    private void AdvanceProjectionTurn()
    {
        _projectionTurn++;
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
        MaskObservations();
        _projectionTurnsSinceBatch = 0;
        _projectionBatchPending = false;
    }
}
