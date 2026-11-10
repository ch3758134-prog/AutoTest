# AutoTest lifecycle M1 — offline traceability kernel

**Experimental management tool**, independent from all AutoTest product code. Python 3.10+ only; no pip install, database, account or internet connection. It reads the *current checkout* and cannot approve designs, change SAEF or operate hardware.

First run after fetching PR #28 on the workstation:

```powershell
python tools/lifecycle/lifecycle.py --repo . inspect
python tools/lifecycle/lifecycle.py --repo . impact OBS-KEYSIM-20261109
python tools/lifecycle/lifecycle.py --repo . impact AT-SYS-011
python tools/lifecycle/lifecycle.py --repo . readiness WP-KEYSIM-001
python tools/lifecycle/lifecycle.py --repo . capture --out lifecycle-local-snapshot.json
python -m unittest discover -s tools/lifecycle/tests -v
```

The repo checkout root must contain `SAEF/`. When run against another revision, missing files/anchors are reported; they are never silently reconstructed. Outputs are not saved unless `--out` is explicitly supplied to `capture`/`context`.

**How to read results**:

- `ANCHORED` only confirms a referenced string is present in a file. It does **not** mean the code meets a requirement, that an architecture link is valid, or that real hardware has passed.
- `work_product_state` comes from SAEF `work_products.json`, `gate_state` from `project_state.json`, and `maturity` from the curated source entry; do not collapse these into one `approved` flag.
- `POTENTIALLY_IMPACTED` is a candidate review queue, not a declaration that all requirements/test results are invalid.
- `NOT_READY` on the seeded KEY_SIM work package is **expected**. It deliberately lacks B4 electrical evidence, an updated reviewed design baseline, verified source hashes and human approval.
- A `READY_TO_CODE` result is a software-work-package check only and never authorizes unsafe bench outputs or ECU flash. A scope-specific approval still applies.

To generate a coding context, create an approved candidate work-package revision with explicit approver/date, pinned `baseline` SHA-256 for every required source node, file scope, verification contract and no open affecting CR, then:

```powershell
python tools/lifecycle/lifecycle.py --repo . --model path\to\approved-manifest.json context WP-ID --out implementation-context.md
```

This first edition uses a **small curated graph**, because automatically inferred string matches would dangerously invent engineering traceability. Exact file references and anchors can be checked deterministically, while relation confidence is marked `documented` versus `review_required`. Later iterations can import SAEF relationships and require human verification, before expanding to full product coverage.

## First case: verified vs unverified

Static source inspection of main `4530aba` found `KeypadCommandCodec.EncodeMask` and `EncodeReleaseAll` encoding a 20-bit mask to CAN ID `0x730` on `BusRole.KeypadCan`. `KeypadControlService.SetMaskAsync` currently returns `BENCH_NOT_AUTHORIZED`; existing unit test `Verify_A2_T04` checks encoding. **These are source-level facts**, not proof that any one of 5 analog outputs attains an expected voltage or that ECU recognizes it. No physical voltage measurements were supplied to this tool.

The actual KEY_SIM behavior finding is logged under `docs/lifecycle-pilot/cases/CR-KEYSIM-001.md`. The graph traces a candidate route from this finding to `AT-SYS-011`, `IF-03/06`, `ICD-03`, `G3-A2`, source and tests. A source-name match does not establish complete semantic conformance. For a new physical-output work package, B4 measurement and explicit approval remain blocking requirements.
