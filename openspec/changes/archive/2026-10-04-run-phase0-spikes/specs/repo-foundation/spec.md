## ADDED Requirements

### Requirement: Calibrated performance budgets
The startup budget SHALL be calibrated per environment from measured baselines (dev machines and each CI runner) and re-calibrated when a baseline shifts; budgets SHALL keep documented headroom above the measured median so they catch regressions rather than machine noise.

#### Scenario: Budgets reflect measured baselines
- **GIVEN** measured startup medians for the dev machine and each CI runner (spike S-3)
- **WHEN** the budgets are set in `scripts/perf.sh` and the CI matrix
- **THEN** each budget sits above its measured median with documented headroom

#### Scenario: A regression trips the budget
- **GIVEN** a change that materially regresses startup (for example, doubling the median)
- **WHEN** the verification gate runs
- **THEN** the budget is exceeded and the gate fails

#### Scenario: Re-calibration is documented
- **GIVEN** a baseline shift (new hardware or a runner image change)
- **WHEN** the budgets are updated
- **THEN** the new measurements and the reason for the change are recorded in the spike report
