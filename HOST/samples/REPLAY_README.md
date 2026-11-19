# SCAN / PRODUCTION · Mock replay fixture (not physical evidence)

- The CSV lists deterministic input scenarios and expected rejection/status codes; it is **not** a scanner hardware log.
- Test code constructs `IdentityRuleSet` from named-capture regex `(?<model>EPB)-(?<serial>\d{5})` and identity key `model+serial`.
- `EPB` is an **illustrative test fixture** only: the production implementation has no EPB-specific match branch.
- Rule revision and project revision are distinct inputs; stale snapshots block matching.
- The business workflow consumes `IIdentityMatcher`/`MatchDecision` and injected stage executors; tests must supply Fake execution and Fake record stores. No physical side effect is generated.
- For real barcode input, discover and prove Honeywell device input mode and terminator separately. Do not claim these CSV rows prove keyboard wedge reception on Windows.
