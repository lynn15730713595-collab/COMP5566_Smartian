# T1 — Reentrancy

**SWC ID:** SWC-107
**Source:** SmartBugs Curated (`dataset/reentrancy/`)
**Detection difficulty:** Low — all four tools have dedicated detectors
**Root cause:** Callee re-enters caller before state is updated (CEI pattern violation)

| # | File | Pragma | Description |
|---|------|--------|-------------|
| 1 | reentrancy_dao.sol | 0.4.19 | Classic DAO-style single-function reentrancy |
| 2 | etherstore.sol | 0.4.10 | ETH Store withdraw loop — canonical teaching example |
| 3 | reentrancy_cross_function.sol | 0.4.24 | Cross-function variant: transfer + withdraw share state |
| 4 | modifier_reentrancy.sol | 0.4.24 | Reentrancy via modifier execution order |
| 5 | reentrancy_insecure.sol | 0.5.0 | Minimal insecure pattern, newer pragma |

**Ground truth:** All 5 files contain reentrancy vulnerabilities per SmartBugs `vulnerabilities.json`.

## Tool evaluation notes

- **Slither:** `reentrancy-eth` / `reentrancy-no-eth` detectors; expect all 5 flagged
- **Mythril:** SWC-107 module; symbolic execution traces re-entry paths
- **Echidna:** Property: assert balance does not decrease unexpectedly after external call
- **Smartian:** Built-in reentrancy oracle from static data-flow phase
