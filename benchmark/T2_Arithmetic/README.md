# T2 — Arithmetic / Integer Overflow

**SWC ID:** SWC-101
**Source:** SmartBugs Curated (`dataset/arithmetic/`)
**Detection difficulty:** Low — tools are strong on pre-0.8 overflow patterns
**Root cause:** Integer wrap-around in unchecked arithmetic (Solidity < 0.8, no SafeMath)

| # | File | Pragma | Description |
|---|------|--------|-------------|
| 1 | BECToken.sol | 0.4.16 | Real-world: BeautyChain token, ~$900M lost (multiplication overflow in `batchTransfer`) |
| 2 | integer_overflow_1.sol | 0.4.15 | Minimal subtraction underflow pattern |
| 3 | integer_overflow_mul.sol | 0.4.19 | Multiplication overflow — distinct from addition path |
| 4 | integer_overflow_multitx_multifunc_feasible.sol | 0.4.23 | Multi-tx, multi-function path — tests fuzzer depth |
| 5 | overflow_single_tx.sol | 0.4.23 | Multiple overflow instances in single contract |

**Ground truth:** All 5 files labeled `arithmetic` in SmartBugs `vulnerabilities.json`.

## Tool evaluation notes

- **Slither:** `integer-overflow` / `tainted-arithmetic` (use `--detect` explicitly); strong recall
- **Mythril:** SWC-101 module; SMT solver catches overflow conditions symbolically
- **Echidna:** Property: assert no counter wraps past expected bounds
- **Smartian:** Static phase flags overflow paths; fuzzer confirms reachability

## Note on Solidity 0.8+

All selected contracts use pragma < 0.8. Modern contracts have built-in checked arithmetic,
making this pattern rare in production — but it remains a benchmark staple for tool validation.
