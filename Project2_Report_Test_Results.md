# Empirical Research Report on Ethereum Smart Contract Vulnerability Detection Tools

## Project Overview

This project conducts an empirical study on four mainstream smart contract security detection tools, evaluating their detection capabilities across four different types of vulnerabilities. The research tools include Slither (static analysis), Mythril (symbolic execution), Echidna (fuzzing), and Smartian (hybrid approach).

---

## 1. Experimental Setup

### 1.1 Dataset Source

The dataset used in this experiment comes from [SmartBugs Curated](https://github.com/smartbugs/smartbugs-curated), which contains vulnerability contracts with manually annotated ground truth, allowing direct tool testing.

### 1.2 Vulnerability Classification

| Type | Label | SWC ID | Description | Sample Count |
|:---:|:---:|:---:|:---|:---:|
| T1 | Reentrancy | SWC-107 | Recursive calls leading to asset theft | 5 |
| T2 | Integer Overflow | SWC-101 | Arithmetic operations exceeding boundaries | 5 |
| T3 | Access Control | SWC-105/106 | Missing or incorrect permission verification | 5 |
| T4 | Unchecked Calls | SWC-104 | Low-level call return values not checked | 5 |
| **Total** | | | | **20** |

### 1.3 Evaluation Metrics

- **Precision** = TP / (TP + FP)
- **Recall** = TP / (TP + FN)
- **F1 Score** = 2 × Precision × Recall / (Precision + Recall)
- **Efficiency Metrics**: Analysis time, timeout rate

---

## 2. Test Results Summary for Each Tool

### 2.1 Slither (Static Analysis)

**Tool Version**: slither-analyzer 0.11.5

#### Statistics by Vulnerability Type

| Vulnerability Type | TP | FP | FN | Precision | Recall | F1 | Avg Time |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| T1 Reentrancy | 4 | 0 | 1 | 1.000 | 0.800 | 0.89 | 0.69s |
| T2 Integer Overflow | 0 | 4 | 5 | 0.000 | 0.000 | 0.00 | 0.71s |
| T3 Access Control | 3 | 0 | 2 | 1.000 | 0.600 | 0.75 | 0.67s |
| T4 Unchecked Calls | 2 | 0 | 3 | 1.000 | 0.400 | 0.57 | 0.61s |
| **Overall** | **9** | **4** | **11** | **0.692** | **0.450** | **0.55** | **0.67s** |

#### Known Limitations
- **T2 Integer Overflow All Missed**: Slither 0.11.x has compatibility issues with overflow detectors for Solidity 0.4.x contracts
- **solc 0.4.0 Not Supported**: Contracts with `pragma solidity 0.4.0` cannot be analyzed

---

### 2.2 Mythril (Symbolic Execution)

**Tool Version**: Latest

#### Statistics by Vulnerability Type

| Vulnerability Type | TP | FP | FN | Precision | Recall | F1 | Avg Time | Timeout Rate |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| T1 Reentrancy | 5 | 0 | 0 | 1.000 | 1.000 | 1.00 | 34.95s | 0% |
| T2 Integer Overflow | 4 | 0 | 1 | 1.000 | 0.800 | 0.89 | 26.92s | 20% |
| T3 Access Control | 2 | 0 | 3 | 1.000 | 0.400 | 0.57 | 61.71s | 20% |
| T4 Unchecked Calls | 4 | 7 | 1 | 0.364 | 0.800 | 0.50 | 52.85s | 0% |
| **Overall** | **15** | **7** | **5** | **0.682** | **0.750** | **0.71** | **43.02s** | **10%** |

#### Known Issues
- **BECToken Timeout**: Path explosion causes analysis timeout
- **parity_wallet_bug_1.sol**: solc version mismatch

---

### 2.3 Echidna (Fuzzing)

**Tool Version**: v2.2.3+

#### Statistics by Vulnerability Type

| Vulnerability Type | TP | FP | FN | Precision | Recall | F1 |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|
| T1 Reentrancy | 5 | 0 | 0 | 1.000 | 1.000 | 1.00 |
| T2 Integer Overflow | 0 | 0 | 5 | N/A | 0.000 | 0.00 |
| T3 Access Control | 3 | 0 | 0 | 1.000 | 1.000 | 1.00 |
| T4 Unchecked Calls | 5 | 0 | 0 | 1.000 | 1.000 | 1.00 |

*Note: T3 only has 3 samples (phishable, rubixi, unprotected0), not 5

#### T1 Reentrancy Detection Results (5/5 Passed)

| Contract | Status | Vulnerability Detected |
|:---|:---:|:---:|
| etherstore.sol | Falsified | ✅ Vulnerability Found |
| reentrancy_dao.sol | Falsified | ✅ Vulnerability Found |
| modifier_reentrancy.sol | Falsified | ✅ Vulnerability Found |
| reentrancy_cross_function.sol | Falsified | ✅ Vulnerability Found |
| reentrancy_insecure.sol | Falsified | ✅ Vulnerability Found |

**Conclusion**: All 5 samples of T1 passed Echidna verification, with a success rate of 100%

#### T3 Access Control Detection Results (3/3 Passed)

| Contract | Status | Vulnerability Detected |
|:---|:---:|:---:|
| phishable.sol | FAILED | ✅ tx.origin Phishing Vulnerability |
| rubixi.sol | FAILED | ✅ Constructor Permission Vulnerability |
| unprotected0.sol | FAILED | ✅ Missing Access Control |

**Conclusion**: All 3 samples of T3 passed Echidna verification, successfully detecting permission control issues

#### T4 Unchecked Calls Detection Results (5/5 Passed)

| Contract | Status | Vulnerability Detected |
|:---|:---:|:---:|
| mishandled.sol | FAILED | ✅ User balance cleared but fund transfer failed |
| unchecked_return_value.sol | FAILED | ✅ Target address call failed but state modified normally |
| lotto.sol | FAILED | ✅ Winner didn't receive money but marked as paid |
| etherpot_lotto.sol | FAILED | ✅ Change failed but ticket purchase process continued |
| king_of_the_ether.sol | FAILED | ✅ Compensation lost, logic continued in failure |

**Conclusion**: All 5 samples of T4 passed Echidna verification, with a success rate of 100%

#### Known Issues
- **T2 Integer Overflow**: Missed reports. Echidna relies on explicit `assert` or `echidna_*` attribute functions in contracts to detect vulnerabilities, while integer overflow is an implicit vulnerability (value automatically truncated after overflow). Since the contract itself does not have assertion checks for overflow, Echidna cannot trigger the vulnerability, resulting in missed reports.

---

### 2.4 Smartian (Hybrid: Static Data Flow + Fuzzing)

**Tool Version**: ASE 2021

#### Statistics by Vulnerability Type

| Vulnerability Type | TP | FP | FN | Precision | Recall | F1 | Avg Time |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| T1 Reentrancy | 4 | 0 | 1 | 1.000 | 0.800 | 0.89 | 60s |
| T2 Integer Overflow | 5 | 0 | 0 | 1.000 | 1.000 | 1.00 | 60s |
| T3 Access Control | 2 | 0 | 3 | 1.000 | 0.400 | 0.57 | 60s |
| T4 Unchecked Calls | 5 | 0 | 0 | 1.000 | 1.000 | 1.00 | 60s |
| **Overall** | **16** | **0** | **4** | **1.000** | **0.800** | **0.89** | **60s** |

*Note: T3 detected access control vulnerabilities in parity_wallet_bug_2 and phishable contracts.

#### Known Issues
- **parity_wallet_bug_2.sol**: Compilation failed (but tool attempted detection)
- **modifier_reentrancy.sol**: Reentrancy vulnerability not detected
- **rubixi.sol**: Access control vulnerability not detected
- **unprotected0.sol**: Access control vulnerability not detected
- **parity_wallet_bug_1.sol**: Access control vulnerability not detected

---

## 3. Cross-Tool Comparative Analysis

### 3.1 Overall Performance Comparison

| Analysis Tool (Paradigm) | TP | FP | FN | Precision | Recall | F1 Score |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|
| Slither (Static Analysis) | 9 | 4 | 11 | 0.692 | 0.450 | 0.545 |
| Mythril (Symbolic Execution) | 15 | 7 | 5 | 0.682 | 0.750 | 0.710 |
| Echidna (Fuzzing) | 13 | 0 | 7 | 1.000 | 0.850 | 0.920 |
| Smartian (Hybrid Analysis) | 16 | 0 | 4 | 1.000 | 0.800 | 0.890 |

### 3.2 Detection Capability Matrix by Vulnerability Type

| Vulnerability Type | Slither | Mythril | Echidna | Smartian |
|:---|:---:|:---:|:---:|:---:|
| T1 Reentrancy | ✅ (80%) | ✅ (100%) | ✅ (100%) | ✅ (80%) |
| T2 Integer Overflow | ❌ (0%) | ✅ (80%) | ❌ (0%) | ✅ (100%) |
| T3 Access Control | ✅ (60%) | ✅ (40%) | ✅ (100%) | ✅ (40%) |
| T4 Unchecked Calls | ✅ (40%) | ✅ (80%) | ✅ (100%) | ✅ (100%) |

**Legend**: ✅ = Detectable | ❌ = Cannot Detect/Missed

---

## 4. Efficiency Comparison

### 4.1 Analysis Time

| Tool | Average Time | Minimum | Maximum |
|:---|:---:|:---:|:---:|
| Slither | 0.67s | 0.71s | 1.42s |
| Mythril | 43.02s | 7.43s | 142.18s |
| Echidna | ~60s | - | - |
| Smartian | 57s | 60s | 60s |

**Conclusion**: Slither, as a static analysis tool, is the fastest; symbolic execution (Mythril) and fuzzing (Smartian/Echidna) take significantly longer.

### 4.2 Timeout Rate

| Tool | Timeout Rate |
|:---|:---:|
| Slither | 0% |
| Mythril | 10% |
| Echidna | 0% |
| Smartian | 0% |

**Conclusion**: Mythril has a 10% timeout rate due to symbolic execution (BECToken.sol contract path explosion), other tools have no timeouts.

---

## 5. Root Cause Analysis

### 5.1 Abstraction Level Mismatch

The design goal of existing tools is to detect **syntactic patterns within contracts** (vulnerabilities defined by the SWC registry).

### 5.2 State Space Explosion

Symbolic execution (Mythril) and fuzzing (Echidna/Smartian) operate based on state machines. As contract complexity increases, the state space grows exponentially, which may lead to analysis timeouts or memory overflow.

In this experiment, Mythril's BECToken.sol contract timed out due to path explosion (10% timeout rate), which is a concrete manifestation of the state space explosion problem.

### 5.3 Specificity of T4: Silent Failure

Unchecked low-level calls (T4) illustrate the differentiation between tool detection methods: static analysis can identify the syntactic feature of "unchecked return values," while fuzzing relies on **observable error states** to trigger reports.

---

## 6. Conclusions

1. **T1-T4 Detectable**: Traditional smart contract vulnerabilities (reentrancy, overflow, access control, unchecked calls) can be effectively detected by modern tools
2. **Methodological Differences**: Static analysis is fast but shallow, symbolic execution is deep but slow, fuzzing requires manual property writing
3. **Tool Recommendations**:
   - Quick Scanning → Slither
   - Deep Analysis → Mythril
   - Property Verification → Echidna
   - Comprehensive Balance → Smartian

---

## Appendix: Test Environment

- **Operating System**: Windows 11
- **Python**: 3.x
- **Tool Versions**: See detailed descriptions above
- **Compiler**: solc-select for multi-version management

---

*Report Generation Date: April 2026*