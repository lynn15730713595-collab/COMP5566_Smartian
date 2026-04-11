# T3 — Access Control

**SWC ID:** SWC-105 (Unprotected Ether Withdrawal), SWC-106 (Unprotected SELFDESTRUCT)
**Source:** SmartBugs Curated (`dataset/access_control/`)
**Detection difficulty:** Medium — Slither strong; symbolic execution and fuzzing need guidance
**Root cause:** Sensitive functions lack access modifiers, or constructor naming bugs leave init functions public

| # | File | Pragma | Description |
|---|------|--------|-------------|
| 1 | parity_wallet_bug_1.sol | 0.4.9 | Parity Wallet v1: unprotected `initWallet()` callable by anyone |
| 2 | parity_wallet_bug_2.sol | 0.4.9 | Parity Wallet v2: `kill()` in library triggers mass selfdestruct |
| 3 | phishable.sol | 0.4.22 | `tx.origin` phishing — owner check bypassable by intermediary |
| 4 | rubixi.sol | 0.4.15 | Real-world Rubixi: constructor renamed, ownership takeover |
| 5 | unprotected0.sol | 0.4.15 | Unprotected `withdrawAll()` — minimal pattern |

**Ground truth:** All 5 files labeled `access_control` in SmartBugs `vulnerabilities.json`.

## Tool evaluation notes

- **Slither:** `unprotected-upgrade`, `suicidal`, `tx-origin` detectors; expect high recall on 1/2/3/5
- **Mythril:** SWC-105/106 modules; detects unprotected selfdestruct and withdraw paths
- **Echidna:** Property: assert `owner == msg.sender` before privileged operations
- **Smartian:** Limited without explicit role model; may flag 1/2 via control-flow anomaly
