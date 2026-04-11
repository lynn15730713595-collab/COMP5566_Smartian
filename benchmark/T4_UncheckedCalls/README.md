# T4 — Unchecked Low-Level Calls

**SWC ID:** SWC-104
**Source:** SmartBugs Curated (`dataset/unchecked_low_level_calls/`)
**Detection difficulty:** Medium — Slither detects reliably; symbolic execution inconsistent; fuzzers rarely trigger
**Root cause:** Return values of `call()` / `send()` not checked; failed transfers silently ignored

| # | File | Pragma | Description |
|---|------|--------|-------------|
| 1 | king_of_the_ether_throne.sol | 0.4.0 | Real-world King of the Ether: unchecked `.send()` in throne transfer |
| 2 | mishandled.sol | 0.4.0 | Minimal mishandled send — canonical teaching example |
| 3 | unchecked_return_value.sol | 0.4.25 | Explicit unchecked `call()` return value |
| 4 | lotto.sol | 0.4.18 | Lottery contract: winner payout via unchecked `.send()` |
| 5 | etherpot_lotto.sol | 0.4.0 | EtherPot lottery: multiple unchecked low-level calls |

**Ground truth:** All 5 files labeled `unchecked_low_level_calls` in SmartBugs `vulnerabilities.json`.

## Tool evaluation notes

- **Slither:** `unchecked-send` / `unchecked-lowlevel` detectors — highest expected recall in this category
- **Mythril:** SWC-104 module; symbolic path coverage may miss some branches
- **Echidna:** Difficult to write a general property without knowing expected ETH flow; likely low recall
- **Smartian:** Fuzzer-driven; silent failure makes oracle difficult to define; partial coverage expected

## Why this type is in the "medium" zone

Unlike Reentrancy (T1) where the exploit has a clear trigger pattern, unchecked calls produce
*silent failures* — there is no crash or assertion violation to observe. Fuzzers that rely on
observable bad states (crash / assertion) will underperform here relative to static analyzers.
