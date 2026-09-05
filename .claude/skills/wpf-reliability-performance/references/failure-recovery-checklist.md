# Failure and recovery checklist

## Failure model

For the changed workflow, identify:

- authoritative physical/service state
- saved checkpoint/configuration state
- cached live value and when it becomes stale
- exclusive resource owner
- safe state when command outcome is unknown
- operator-visible recovery action

Do not catch an exception merely to continue with a false success state. Best-effort cleanup may suppress its own failure only when the primary failure/state remains observable.

## Connection and retry

- Timeouts are finite and distinguish timeout, cancellation, malformed response, unavailable device, busy resource, and authorization failure where operator action differs.
- Only one reconnect loop may own a device at a time.
- Backoff is bounded and resets only after a verified successful operation, not merely an opened socket/port.
- Reconnect clears stale transport buffers where the protocol requires it and re-establishes protocol/session state in the validated order.
- Automatic recovery never steals a COM port, chamber, WIKA reference, PeakLogger mapping, or FBG ownership from another active owner.

## Checkpoint and restore

- Write critical checkpoints using a temporary sibling plus atomic replace/move.
- Persist before/after irreversible or externally visible transitions as appropriate.
- A checkpoint includes enough identity and progress information to avoid repeating completed steps or skipping incomplete ones.
- Restore validates schema, stable identities, configuration compatibility, and fresh device state.
- Corrupt or partial state is preserved for diagnosis and does not silently become a new empty/default file.
- Resume is explicit when physical state or the previous command outcome cannot be proven.

## Concurrency and cleanup

- Open/read/write/reconnect/close/dispose share the established synchronization and lease mechanisms.
- Disposal waits for or cancels in-flight work safely and is idempotent.
- Partial construction can be cleaned up without null races or leaked resources.
- Background callbacks verify lifetime, current selection, device identity, and generation before applying results.
- Shutdown has a bounded path and still attempts required device-local cleanup such as returning WIKA to local control.

## Regression prevention

- Reproduce the bug with a test or deterministic harness before fixing when feasible.
- Test failure at each important transition, including a failure after the external action but before local persistence.
- Test duplicate callbacks, out-of-order completions, repeated reconnect, repeated restore, and repeated dispose.
- Assert both safety and observability: no duplicate command/data loss, and the operator sees accurate state.
- Keep diagnostics timestamped and contextual with device/workflow identity, attempt, elapsed time, and exception category; redact secrets.
