# T5 — Price Oracle Manipulation

**SWC ID:** None (DeFi-specific, not in SWC registry)
**Detection difficulty:** Extreme — all current tools are effectively blind to this class
**Root cause:** Price oracle reads from on-chain AMM spot price within the same block/tx,
allowing flash loan manipulation of the price feed before the vulnerable action executes

| # | File | Date | Loss | Chain | Vulnerable Contract |
|---|------|------|------|-------|-------------------|
| 1 | makina_exp.sol | 2026-01 | ~$5.1M USDC | Ethereum | Caliber + Machine contracts (see file) |
| 2 | Moonwell_exp.sol | 2026-02 | ~$1.78M | Ethereum | (see file — faulty oracle integration) |
| 3 | ImpermaxV3_exp.sol | 2025-04 | ~$300K | Base | [0x5d93...ee](https://basescan.org/address/0x5d93f216f17c225a8b5ffa34e74b7133436281ee) |
| 4 | UwuLend_First_exp.sol | 2024-06 | ~$19.3M | Ethereum | [0x2409...68](https://etherscan.io/address/0x2409af0251dcb89ee3dee572629291f9b087c668) |
| 5 | CompoundUni_exp.sol | 2024-02 | ~$440K | Ethereum | [0x50ce...41](https://etherscan.io/address/0x50ce56A3239671Ab62f185704Caedf626352741e) |

## Why tools fail

Oracle manipulation is a **cross-contract, cross-block semantic attack**:
1. Requires flash loan context (external state injection)
2. The vulnerable contract's code is locally correct — the flaw is in how it *trusts* an external price source
3. No static pattern exists; detection requires modeling the full DeFi protocol graph

This is the most important finding for the "root cause of tool differences" section.

## Tool evaluation notes

- **Slither:** No oracle-specific detector; `arbitrary-send-eth` is unrelated
- **Mythril:** Bounded symbolic execution cannot model multi-contract flash loan sequences
- **Echidna:** Would need a custom property asserting price stability — not generalizable
- **Smartian:** Dynamic analysis may exercise code paths but cannot model oracle trust assumptions

**Expected result:** 0/4 tools detect any of these cases → strongest argument for tool limitations.
