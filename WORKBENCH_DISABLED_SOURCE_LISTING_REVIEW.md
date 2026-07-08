# Static Review: Workbench Listing Must Exclude Disabled or Unavailable Sources

No compilation or runtime execution was performed.

## Finding

The profile workbench was listing every YAML profile that could be parsed, including profiles with `enabled: false`. That conflicts with the current product boundary: the workbench is a helper for refining daemon profiles against runnable daemon-compatible sources. Disabled profiles and disabled/unavailable sources should not be selectable test targets.

The daemon already applies a similar gating sequence before it creates runtime bindings: enabled flag, platform match, profile condition, and Windows resource validation. The workbench catalog should apply equivalent high-level gates, but it should not turn the catalog into a warning report. Inapplicability and absence warnings belong to explicit validation/status flows; the selectable list should contain only usable profiles.

## Remediation

Added `ProfileAvailabilityFilter` in `DeltaZulu.Agent.ProfileWorkbench`. It omits profiles when:

- `profile.Enabled` is false.
- `resource.platform` does not match the current platform.
- the profile condition is not satisfied or cannot be evaluated.
- Windows resource validation reports Event Log or ETW as unavailable.
- the YAML/profile cannot be loaded.

`ProfileLibrary.ListProfiles()` now applies this filter before creating `ProfileLibraryItem` values. The TUI therefore receives only selectable workbench targets.

## Notes

This intentionally does not emit per-profile warnings during normal TUI listing. If a separate validation screen is added later, it can show disabled, skipped, unavailable, and invalid profiles with reasons. The workbench catalog itself should stay quiet and only list runnable candidates.

## Delete list

None.
