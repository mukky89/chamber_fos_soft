# JSON migrations and compatibility

## Adding a property

- Choose a CLR initializer/default that is safe when the JSON member is absent.
- Check whether that default should apply to existing installations or only fresh ones.
- If an older serialized value masks a new desired default, use an explicit one-time marker like `TimelineDefaultApplied` rather than resetting the user's choice on every load.

## Renaming or reshaping

- Read the old shape before writing the new one.
- Make the migration idempotent and safe after interruption.
- Keep the original file, a uniquely named backup, or enough recovery evidence when data is material.
- Do not delete legacy data merely because part of a migration succeeded.
- Do not interpret malformed JSON as consent to replace the file with defaults.

## Enums and identifiers

- Preserve existing numeric/string enum compatibility or provide an explicit mapping.
- Use stable device/profile identifiers for ownership and filenames; do not use mutable display names or measured values as identity.
- USB serial is preferred to COM number for physical reference identity; PeakLogger mappings use their established stable source identity.

## Writing

- For critical state, serialize to a sibling temporary file and atomically replace/move it into place.
- Clean up a temporary file only after resolving its absolute intended target and only within the owning settings directory.
- Keep serialization deterministic enough for diagnosis and human recovery (`WriteIndented` where existing stores use it).
- Bound and report I/O failures without freezing the UI thread.

## Tests

Cover fresh defaults, previous-version JSON, repeated migration, partially migrated state, malformed content, unknown members, and interrupted/failed writes where practical.
