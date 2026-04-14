# Smartian 漏洞检测结果汇总表（4种标准分类）

## 总汇表

| 漏洞分类 | 合约数 | TP | FP | FN | Precision | Recall | F1-Score |
|----------|--------|----|----|----|-----------|--------|----------|
| T1_Reentrancy | 5 | 4 | 0 | 1 | 100.0% | 80.0% | 88.9% |
| T2_Arithmetic | 5 | 5 | 0 | 0 | 100.0% | 100.0% | 100.0% |
| T3_AccessControl | 5 | 2 | 0 | 3 | 100.0% | 40.0% | 57.1% |
| T4_UncheckedCalls | 5 | 5 | 0 | 0 | 100.0% | 100.0% | 100.0% |
| **总计/平均** | **20** | **16** | **0** | **4** | **100.0%** | **80.0%** | **86.5%** |

### 计算公式
- **Precision (精确率)** = TP / (TP + FP)
- **Recall (召回率)** = TP / (TP + FN)  
- **F1-Score** = 2 × (Precision × Recall) / (Precision + Recall)

---

## 详细分类统计

### T1_Reentrancy (重入漏洞)

| 合约名 | Smartian检测 | 实际漏洞 | 判定 |
|--------|--------------|----------|------|
| etherstore | ✓ | 有Reentrancy | TP |
| reentrancy_cross_function | ✓ | 有Reentrancy | TP |
| reentrancy_dao | ✓ | 有Reentrancy | TP |
| reentrancy_insecure | ✓ | 有Reentrancy | TP |
| modifier_reentrancy | ✗ | 有Reentrancy | FN |

**统计**: TP=4, FP=0, FN=1 | Precision=100.0%, Recall=80.0%, F1=88.9%

---

### T2_Arithmetic (算术溢出)

| 合约名 | Smartian检测 | 实际漏洞 | 判定 |
|--------|--------------|----------|------|
| BECToken | ✓ | 有Arithmetic | TP |
| integer_overflow_1 | ✓ | 有Arithmetic | TP |
| integer_overflow_mul | ✓ | 有Arithmetic | TP |
| integer_overflow_multitx_multifunc_feasible | ✓ | 有Arithmetic | TP |
| overflow_single_tx | ✓ | 有Arithmetic | TP |

**统计**: TP=5, FP=0, FN=0 | Precision=100.0%, Recall=100.0%, F1=100.0%

---

### T3_AccessControl (访问控制)

| 合约名 | Smartian检测 | 实际漏洞 | 判定 |
|--------|--------------|----------|------|
| parity_wallet_bug_2 | ✓ | 有AccessControl | TP |
| phishable | ✓ | 有AccessControl | TP |
| parity_wallet_bug_1 | ✗ | 有AccessControl | FN |
| rubixi | ✗ | 有AccessControl | FN |
| unprotected0 | ✗ | 有AccessControl | FN |

**统计**: TP=2, FP=0, FN=3 | Precision=100.0%, Recall=40.0%, F1=57.1%

---

### T4_UncheckedCalls (未检查调用)

| 合约名 | Smartian检测 | 实际漏洞 | 判定 |
|--------|--------------|----------|------|
| etherpot_lotto | ✓ | 有UncheckedCalls | TP |
| king_of_the_ether_throne | ✓ | 有UncheckedCalls | TP |
| lotto | ✓ | 有UncheckedCalls | TP |
| mishandled | ✓ | 有UncheckedCalls | TP |
| unchecked_return_value | ✓ | 有UncheckedCalls | TP |

**统计**: TP=5, FP=0, FN=0 | Precision=100.0%, Recall=100.0%, F1=100.0%

---

## 结果分析

### 关键发现
1. **精确率100%**: Smartian在检测到的漏洞中没有误报
2. **T2(Arithmetic)和T4(UncheckedCalls)**: 召回率和F1均为100%，表现完美
3. **T1(Reentrancy)**: 召回率80%，漏检1个合约（modifier_reentrancy）
4. **T3(AccessControl)**: 召回率40%最低，漏检3个合约（parity_wallet_bug_1, rubixi, unprotected0），是主要改进点

### 整体表现
- **总TP**: 16 / 20 合约被正确检测
- **总FN**: 4 / 20 合约被漏检
- **整体召回率**: 80.0%
- **整体F1**: 86.5%，表现良好

---

## 分类说明
- **Reentrancy (重入)**: 合约在调用外部合约时被重入攻击
- **Arithmetic (算术溢出)**: 整数运算溢出/下溢
- **AccessControl (访问控制)**: 权限控制不当，包括自杀合约、以太币泄露、任意写入等
- **UncheckedCalls (未检查调用)**: 外部调用返回值未检查
