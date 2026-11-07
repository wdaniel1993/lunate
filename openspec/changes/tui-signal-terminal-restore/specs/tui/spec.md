## ADDED Requirements

### Requirement: Signal exits restore the terminal

While an interactive session is running, the process SHALL handle `SIGTERM` and `SIGHUP` by running the same terminal-restore path an ordinary quit uses — the raw-mode restore that puts the terminal back to the mode the process found it in — before exiting. The signal path SHALL NOT hold a second copy of the restore logic, and a signal that arrives while a turn is running SHALL restore the terminal all the same. A session whose environment cannot support interactive input (no raw mode entered) SHALL NOT install the handlers, so non-interactive runs keep the platform's default signal behaviour.

#### Scenario: SIGTERM mid-session restores the terminal

- **GIVEN** an interactive session running against a real terminal, with the terminal's attributes captured (for example `stty -a`) before launch
- **WHEN** `SIGTERM` is sent to the session process mid-session
- **THEN** the process exits and the terminal's attributes afterwards match the captured baseline — echo on, canonical mode on — without the user running anything

#### Scenario: SIGHUP restores the terminal the same way

- **GIVEN** an interactive session running against a real terminal captured at launch
- **WHEN** `SIGHUP` is sent to the session process mid-session
- **THEN** the process exits and the terminal's attributes match the baseline exactly as they do for `SIGTERM`

#### Scenario: A signal during a running turn still restores

- **GIVEN** an interactive session with a turn running
- **WHEN** `SIGTERM` or `SIGHUP` arrives before the turn completes
- **THEN** the process exits and the terminal's attributes match the launch-time baseline

#### Scenario: The restore path is the quit path

- **GIVEN** the signal handlers installed
- **WHEN** a signal exits the session
- **THEN** the terminal restore the handler performs is the same restore the ordinary quit path runs, not a second, parallel implementation
