namespace Lunate.Agent;

/// <summary>
/// Cumulative tokens the three context-admission mechanisms have kept out of the model's window,
/// in the harness's UTF-8-bytes-over-4 token estimate. Reported by <c>/context</c> as three
/// separate numbers (sign-off condition (f) of ADR-0012's addendum).
/// </summary>
/// <param name="Spills">Tokens a spill's pointer saved over admitting the full output.</param>
/// <param name="Masking">Tokens observation masking at batch points removed.</param>
/// <param name="ReadStubs">Tokens stale-read stubs removed.</param>
public sealed record ContextSavings(long Spills, long Masking, long ReadStubs);
