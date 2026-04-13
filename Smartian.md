# Smartian — 动态模糊测试工具

## 工具简介

Smartian 是基于模糊测试（fuzzing）的动态分析工具，专用于 Solidity 智能合约漏洞检测。

工作原理：通过符号执行和约束求解引导测试用例生成，在 EVM 模拟环境中执行合约，监控运行时状态变化，检测重入、整数溢出、访问控制等漏洞。与静态分析不同，Smartian 需要实际执行合约代码。

**运行环境：** Docker 容器 `project2_eval`

---

## 安装

Smartian 依赖复杂的环境配置，本项目通过 Docker 容器运行：

```bash
# 启动 Docker 容器
sudo docker start project2_eval

# 验证容器运行状态
sudo docker ps --filter name=project2_eval
```

容器内预装：
- Smartian 工具链
- solc 编译器
- 运行脚本 `run_smartian.sh`

---

## 输入

| 项目 | 说明 |
| :--- | :--- |
| 源文件 | 单个 `.sol` 文件 |
| 编译产物 | `.bin`（字节码）和 `.abi`（接口定义）文件 |
| 超时设置 | 默认 60 秒/合约 |

典型命令（容器内）：

```bash
# 编译合约
solc --bin --abi contract.sol -o ./output --overwrite

# 运行 Smartian
cd /home/test/scripts && ./run_smartian.sh 60 dummy.sol \
    /path/to/contract.bin \
    /path/to/contract.abi \
    contract_name ''
```

---

## 输出

Smartian 输出日志包含以下关键指标：

| 字段 | 含义 |
| :--- | :--- |
| `executions` | 执行次数 |
| `test_cases` | 生成的测试用例数 |
| `covered_edges` | 覆盖的控制流边数 |
| `covered_instructions` | 覆盖的指令数 |
| `found_bugs` | 检测到的漏洞类型及数量 |

漏洞类型标识符：

| 标识符 | 漏洞类型 |
| :--- | :--- |
| `Reentrancy` | 重入攻击 |
| `Ether Leak` | 以太币泄露 |
| `Integer Bug` | 整数溢出/下溢 |
| `Transaction Origin Use` | tx.origin 身份验证问题 |
| `Mishandled Exception` | 异常处理不当 |
| `Unchecked Return Value` | 未检查返回值 |
| `Block state Dependency` | 区块状态依赖 |
| `Assertion Failure` | 断言失败 |

---

## 本项目中的检测器映射

| 漏洞类型 | 对应检测器 |
| :--- | :--- |
| T1 重入 | `Reentrancy`, `Ether Leak`, `Block state Dependency` |
| T2 整数溢出 | `Integer Bug`, `Integer Overflow`, `Integer Underflow`, `Assertion Failure` |
| T3 访问控制 | `Transaction Origin Use`, `Unprotected Function`, `Suicidal`, `Assertion Failure` |
| T4 未检查调用 | `Mishandled Exception`, `Unchecked Return Value`, `Assertion Failure` |

---

## 实测结果摘要（T1–T4，20 个合约）

| Type | TP | FP | FN | Precision | Recall | F1 | Avg Time |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| T1 重入 | 4 | 0 | 1 | 1.000 | 0.800 | 0.89 | ~60s |
| T2 整数溢出 | 5 | 1 | 0 | 0.833 | 1.000 | 0.91 | ~60s |
| T3 访问控制 | 1 | 2 | 2 | 0.333 | 0.333 | 0.33 | ~60s |
| T4 未检查调用 | 5 | 2 | 0 | 0.714 | 1.000 | 0.83 | ~60s |
| **Overall** | **15** | **5** | **3** | **0.750** | **0.833** | **0.79** | **~60s** |

> **注意：** T3 访问控制类型共 5 个合约，其中 `parity_wallet_bug_2.sol` 因编译失败无法检测，实际有效测试仅 4 个合约。上表 T3 统计基于这 4 个可检测的合约。

每个合约超时设置为 60 秒，实际运行时间接近超时上限。

---

## 已知限制

**parity_wallet_bug_2.sol 编译失败：** T3 访问控制类型的 `parity_wallet_bug_2.sol` 合约无法通过 solc 编译，Smartian 无法分析该合约。该合约在结果中标记为：
```
tp=0, fp=0, fn=0, note="Compilation failed - unable to analyze"
```

**Assertion Failure 误报：** Smartian 在多个合约中报告 `Assertion Failure`，但该漏洞类型与目标漏洞类型不匹配，导致 FP 增加。例如：
- `BECToken.sol`：检测到 Integer Bug（TP）+ Assertion Failure（FP）
- `etherpot_lotto.sol`：检测到 Mishandled Exception（TP）+ Assertion Failure（FP）

**T3 访问控制检测较弱：** 5 个 T3 合约中仅 1 个正确检测，2 个产生 FP，2 个漏报，整体 F1 仅 0.33。

**需要预编译：** Smartian 需要 `.bin` 和 `.abi` 文件，对于复杂合约或跨文件依赖的合约，编译可能失败。

---

## 调试踩坑记录

### 坑 1：Docker 容器未启动

**现象**：运行脚本时报 `Docker container 'project2_eval' is not running`。

**原因**：Docker 容器未启动或已停止。

**解法**：先启动容器：

```bash
sudo docker start project2_eval
```

---

### 坑 2：合约编译失败

**现象**：部分合约运行后 `executions=0, test_cases=0`，note 显示 "Failed to compile contract"。

**原因**：合约可能使用了不兼容的 Solidity 语法，或合约名称与文件名不匹配。

**解法**：
1. 检查 solc 版本是否与合约 `pragma solidity` 匹配
2. 手动编译确认错误信息
3. 对于无法编译的合约，在结果中标记并排除

---

### 坑 3：sudo 密码硬编码

**现象**：脚本中硬编码了 sudo 密码。

**原因**：自动化脚本需要免交互执行 docker 命令。

**解法**：生产环境应使用 sudo 免密配置或 docker 用户组：

```bash
# 将用户加入 docker 组
sudo usermod -aG docker $USER
```

---

### 坑 4：超时处理

**现象**：部分合约运行时间超过 60 秒后被强制终止。

**原因**：Smartian 模糊测试可能需要较长时间才能覆盖关键路径。

**解法**：
1. 增加超时时间（如 120s）
2. 检查超时合约的覆盖度，判断是否需要更长时间

---

### 坑 5：结果解析不完整

**现象**：日志中检测到的漏洞未被正确解析。

**原因**：正则表达式匹配可能遗漏部分格式。

**解法**：扩展 `parse_smartian_log()` 中的 `bug_patterns` 列表，覆盖更多漏洞类型。