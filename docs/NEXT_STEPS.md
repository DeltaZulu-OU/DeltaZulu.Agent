# Next steps before merging

1. Restore and build locally with .NET 10 SDK.
2. Fix any `Microsoft.Rx.Kql` and `Tx.Windows` API compatibility issues exposed by the build.
3. Add test projects and fixture-based tests.
4. Prove whether nested field access works in profile KQL:
   - `EventData.TargetUserSid`,
   - `SYSCALL.SYSCALL`,
   - `EXECVE.ARGV`,
   - `_metadata.profileId`.
5. Decide whether KQL row exposure must be flattened for nested fields.
6. Add golden raw-input to NDJSON-output fixtures.
7. Harden auditd assembler completion rules and malformed-record handling.
8. Add daemon-facing examples after the library compiles.
