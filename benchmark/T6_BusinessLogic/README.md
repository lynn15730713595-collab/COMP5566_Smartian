# T6 — Business Logic Flaw

**SWC ID:** None (protocol-specific semantics, not in SWC registry)
**Detection difficulty:** Extreme — requires understanding of intended protocol behavior
**Root cause:** The contract code executes correctly but violates an implicit invariant of the
economic model (e.g., insolvency check applied after state mutation, incorrect burn ordering)

| # | File | Date | Loss | Chain | Vulnerable Contract | Source |
|---|------|------|------|-------|-------------------|----|
| 1 | AlkemiEarn_exp.sol | 2026-03 | 43.45 ETH | Ethereum | [0x4822...88](https://etherscan.io/address/0x4822D9172e5b76b9Db37B75f5552F9988F98a888) | alkemiearn_vuln.sol |
| 2 | SynapLogic_exp.sol | 2026-01 | ~27.6 ETH + 3.4K USDC | Base/ETH | [0xC859...71](https://basescan.org/address/0xC859aC8429fB4A5E24F24a7BEd3fE3a8Db4fb371) | **未验证，已排除** |
| 3 | LAXO_Token_exp.sol | 2026-02 | ~$137K | BSC | [0x6295...CB](https://bscscan.com/address/0x62951CaD7659393BF07fbe790cF898A3B6d317CB) | laxo_token_vuln.sol |
| 4 | SharwaFinance_exp.sol | 2025-10 | $146K | Arbitrum | [0xd3fd...97](https://arbiscan.io/address/0xd3fde5af30da1f394d6e0d361b552648d0dff797) | sharwafinance_vuln.sol |

## Root cause detail

| Case | Specific flaw |
|------|--------------|
| AlkemiEarn | Business logic in lending pool allows unintended borrow path |
| SynapLogic | Business logic flaw in proxy implementation state handling |
| LAXO Token | Incorrect burn logic — burns from wrong address, allowing infinite mint |
| SharwaFinance | Solvency check executed *after* position mutation (post-insolvency-check) |

## Why tools fail

Business logic bugs require **specification**: the tool must know what the contract is *supposed to do*.
Static analysis tools only know what the contract *does do*.
Without a formal spec or protocol-level invariant, no automated tool can distinguish correct
from incorrect behavior.

## Tool evaluation notes

- All four tools expected to produce 0 true positives on these cases
- May produce false positives on unrelated patterns (count these separately in results)
- This category is the strongest evidence for the need of formal verification or LLM-assisted audit
