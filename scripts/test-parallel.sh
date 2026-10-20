#!/usr/bin/env bash
# Test-phase runner for scripts/verify.sh (sourced, not executed).
#
# A solution-wide `dotnet test` already fans assemblies out inside one pass, but
# the gate runs that pass twice: once with the machine culture and once with
# de-AT. Those two passes are independent processes with independent
# environments, yet the second waited for the first and doubled the wall time.
# This runner starts both at once, each with its own bounded pool of JOBS
# slots, and reports each assembly's output in a deterministic order.
#
# Assemblies that spawn child processes or manipulate process-wide state keep a
# serial lane: a serial job owns every slot of its pass, so no other assembly of
# the same pass runs beside it. Measurement on the reference Mac (Release,
# 2026-10-20): Lunate.Coding.Tests takes 3m46s alone and 3m47s while the whole
# suite runs, so its time is spent waiting on children rather than on CPU. The
# lane is kept for isolation of process/env-sensitive tests, not for timing.
# The default culture and de-AT variants of a serial assembly still run beside
# each other: they are separate processes with separate environments.

# Test projects as the solution sees them, in a stable order. The built
# assembly check in verify.sh uses the same list, so a project that was not
# built cannot silently drop out of the gate.
verify_test_projects() {
  sed -n 's/^Project([^)]*) = "[^"]*", "\([^"]*\.Tests\.csproj\)", .*/\1/p' lunate.sln |
    tr '\\' '/' |
    LC_ALL=C sort
}

verify_test_is_serial() {
  case " ${SERIAL_TEST_PROJECTS:-} " in
    *" $1 "*) return 0 ;;
  esac
  return 1
}

# Queue items are "<project-index>:<serial|parallel>:<project>". Reporting
# stays in project order by using the project index for filenames; the order
# knob only decides when the serial lane starts.
#
# The default pass runs its serial lane first, the de-AT pass runs it last.
# That staggers the two copies of a serial assembly: they overlap for part of
# their run instead of starting together, which keeps the wall-time win while
# halving the window in which the two heaviest assemblies compete for the
# machine.
verify_test_queue() {
  local order="${1:-serial-first}"
  if [ "$order" = "serial-last" ]; then
    verify_test_queue_part parallel
    verify_test_queue_part serial
  else
    verify_test_queue_part serial
    verify_test_queue_part parallel
  fi
}

verify_test_queue_part() {
  local want="$1" project name index=0

  while IFS= read -r project; do
    name="$(basename "${project%/*}")"
    if { [ "$want" = "serial" ] && verify_test_is_serial "$name"; } ||
      { [ "$want" = "parallel" ] && ! verify_test_is_serial "$name"; }; then
      printf '%s:%s:%s\n' "$index" "$want" "$project"
    fi
    index=$((index + 1))
  done < <(verify_test_projects)
}

# Runs in a subshell. The status file is written even when the test run fails,
# and the marker is the scheduling signal: bash 3.2 (macOS) has no `wait -n`,
# and a background process's PID remains a valid child until it is reaped.
verify_test_job() {
  local dir="$1" index="$2" project="$3" status
  set +e
  dotnet test --project "$project" -c "$CONFIGURATION" --no-build \
    --filter-not-trait Category=Perf >"${dir}/${index}.log" 2>&1
  status=$?
  set -e
  printf '%s\n' "$status" >"${dir}/${index}.status"
  : >"${dir}/${index}.done"
}

# Runs the queue with at most JOBS slots in use at a time. A serial item needs
# all JOBS slots, so it waits until the pass is idle and then runs alone.
verify_run_test_pass() {
  local dir="$1" rest item index kind project need
  shift
  local -a queue=("$@") pids=() slots=() job_index=()
  local next=0 active=0 i

  mkdir -p "$dir"
  while [ "$next" -lt "${#queue[@]}" ] || [ "$active" -gt 0 ]; do
    while [ "$next" -lt "${#queue[@]}" ]; do
      item="${queue[$next]}"
      index="${item%%:*}"
      rest="${item#*:}"
      kind="${rest%%:*}"
      project="${rest#*:}"

      need=1
      if [ "$kind" = "serial" ]; then
        need="$JOBS"
      fi
      if [ $((active + need)) -gt "$JOBS" ]; then
        break
      fi

      (verify_test_job "$dir" "$index" "$project") &
      pids[$next]=$!
      slots[$next]="$need"
      job_index[$next]="$index"
      active=$((active + need))
      next=$((next + 1))
    done

    if [ "$active" -eq 0 ]; then
      echo "verify: internal error: no test job could be started" >&2
      return 1
    fi

    sleep 0.1
    i=0
    while [ "$i" -lt "$next" ]; do
      if [ -n "${pids[$i]:-}" ] && [ -f "${dir}/${job_index[$i]}.done" ]; then
        wait "${pids[$i]}" 2>/dev/null || true
        active=$((active - ${slots[$i]}))
        pids[$i]=""
      fi
      i=$((i + 1))
    done
  done
}

# Prints one pass in project order and checks every assembly: a non-zero exit
# fails the gate, and so does a missing or zero "total: N" summary. Failures
# accumulate in VERIFY_TEST_FAILURES so both passes report before the caller
# exits.
verify_check_test_pass() {
  local dir="$1" label="$2"
  local project log status_file status item_total index=0
  local total=0 assemblies=0 failures=0

  while IFS= read -r project; do
    log="${dir}/${index}.log"
    status_file="${dir}/${index}.status"
    if [ ! -f "$log" ] || [ ! -f "$status_file" ]; then
      echo "verify: ${label}: ${project} produced no result" >&2
      failures=$((failures + 1))
    else
      cat "$log"
      status="$(cat "$status_file")"
      item_total="$(grep -oE 'total: [0-9]+' "$log" | tail -n 1 | grep -oE '[0-9]+' || true)"
      if [ "$status" -ne 0 ]; then
        echo "verify: ${label} failed for ${project} (exit ${status})" >&2
        failures=$((failures + 1))
      elif [ -z "$item_total" ] || [ "$item_total" -eq 0 ]; then
        echo "verify: ${label} executed zero tests for ${project} — test discovery regression?" >&2
        failures=$((failures + 1))
      else
        total=$((total + item_total))
        assemblies=$((assemblies + 1))
      fi
    fi
    index=$((index + 1))
  done < <(verify_test_projects)

  printf 'verify: %s: %s tests across %s assemblies\n' "$label" "$total" "$assemblies"
  VERIFY_TEST_FAILURES=$((VERIFY_TEST_FAILURES + failures))
}
