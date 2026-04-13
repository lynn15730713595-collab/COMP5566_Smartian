# scripts/

## fetch_vuln_contracts.py

从区块链浏览器批量下载 T5/T6 漏洞合约源码，使其与 T1-T4（SmartBugs）格式一致，可直接用于工具分析。

### 用法

```bash
# 需要 Python 3.10+（用了 str | None 类型语法）
python scripts/fetch_vuln_contracts.py --api-key <YOUR_ETHERSCAN_KEY>
```

API key 在 https://etherscan.io/myapikey 免费注册获取，**一个 key 覆盖全部四条链**（Ethereum、Arbitrum、BSC、Base）。

可选参数：

| 参数 | 默认值 | 说明 |
| :--- | :--- | :--- |
| `--api-key` | 必填 | Etherscan V2 API key |
| `--benchmark-dir` | `benchmark` | benchmark 目录路径 |

### 输出

脚本运行完成后，各文件夹内会生成 `*_vuln.sol` 文件：

```
benchmark/T5_OracleManipulation/
├── makina_caliber_vuln.sol    ← 新增（impl 合约）
├── makina_machine_vuln.sol    ← 新增（impl 合约）
├── moonwell_vuln.sol          ← 新增
├── impermaxv3_vuln.sol        ← 新增
├── uwulend_vuln.sol           ← 新增（proxy → impl 自动追踪）
├── compounduni_vuln.sol       ← 新增
└── makina_exp.sol / ...       ← 原有 PoC 文件（参考用）

benchmark/T6_BusinessLogic/
├── alkemiearn_vuln.sol        ← 新增
├── laxo_token_vuln.sol        ← 新增
└── sharwafinance_vuln.sol     ← 新增（proxy → impl 自动追踪）
```

**注意：** SynapLogic（T6）合约源码未在链上验证，无法自动获取，已从有效 benchmark case 中排除。T6 共有 3 个有效案例。

### 设计说明

**为什么用 Etherscan API V2？**

Etherscan 于 2024 年底废弃了 V1 API（`/api` 端点），所有链（Ethereum、Arbitrum、BSC、Base）现在统一走 V2（`/v2/api?chainid=<id>`）。V2 的最大变化是：

- 单一端点覆盖所有 EVM 链，用 `chainid` 参数区分
- 强制要求 API key（V1 允许无 key 限速访问）
- BSCscan、Arbiscan、Basescan 等均已迁移，不再维护独立 V1 端点

**为什么要追踪 Proxy → Implementation？**

DeFi 协议普遍使用可升级代理模式（EIP-1967 BeaconProxy / TransparentProxy）。链上的合约地址指向 Proxy，Proxy 本身只有路由逻辑，**实际业务代码和漏洞在 Implementation 合约里**。

Etherscan V2 的 `getsourcecode` 响应中有 `Implementation` 字段，当它非空时脚本自动用 Implementation 地址再请求一次。本次 benchmark 中 makina（两个合约）和 uwulend 均为 proxy，追踪后获取到了完整源码。

**为什么多文件合约要拼接成单文件？**

Etherscan 对多文件项目返回 `{{...}}` 包裹的 JSON（Solidity Standard Input JSON 格式），每个源文件在 `sources.<filename>.content` 字段中。脚本将所有文件按 `// ===== filename =====` 分隔拼接成一个 `.sol` 文件，方便工具直接 `slither <file>` 或 `myth analyze <file>` 运行，不需要搭建完整的 Hardhat/Foundry 项目环境。

代价是跨文件的 `import` 路径会失效（工具看到的是展平的源码），对 Slither 和 Mythril 的 AST 解析有影响，分析时需注意。

---

## run_slither.py

运行 Slither 静态分析工具对 T1-T4 benchmark 合约进行检测，收集准确率和性能指标。

### 用法

```bash
# 完整 benchmark 测试（支持断点续跑）
python scripts/run_slither.py

# 单个合约分析
python scripts/run_slither.py --single benchmark/T1_Reentrancy/reentrancy_dao.sol

# 清除断点重新开始
python scripts/run_slither.py --reset
```

### 依赖

```bash
pip install slither-analyzer psutil solc-select
```

### 输出

- `results/slither_results.csv` - 详细结果 CSV
- `results/slither_checkpoint.json` - 断点文件（支持续跑）

---

## run_smartian.py

通过 Docker 容器运行 Smartian 模糊测试工具对 T1-T4 benchmark 合约进行检测，收集准确率和性能指标。

### 前置条件

1. Docker 容器 `project2_eval` 必须运行中
2. 容器内已配置 Smartian 环境

```bash
# 启动容器
sudo docker start project2_eval

# 进入容器（可选）
sudo docker exec -it project2_eval /bin/bash
```

### 用法

```bash
# 完整 benchmark 测试（支持断点续跑）
python scripts/run_smartian.py

# 或使用 shell 包装脚本
./scripts/run_smartian.sh

# 单个合约分析
python scripts/run_smartian.py --single benchmark/T1_Reentrancy/reentrancy_dao.sol

# 清除断点重新开始
python scripts/run_smartian.py --reset
```

### 输出

- `results/smartian_results.csv` - 详细结果 CSV
- `results/smartian_checkpoint.json` - 断点文件（支持续跑）

### 结果字段说明

| 字段 | 说明 |
| :--- | :--- |
| `tool` | 工具名称 (smartian) |
| `type` | 漏洞类型 (T1-T4) |
| `contract` | 合约文件名 |
| `tp` | True Positive (是否检测到预期漏洞) |
| `fp` | False Positive (是否报告了非预期漏洞) |
| `fn` | False Negative (是否漏报预期漏洞) |
| `time_s` | 执行时间（秒） |
| `executions` | 模糊测试执行次数 |
| `test_cases` | 生成的测试用例数 |
| `covered_edges` | 覆盖的边数 |
| `covered_instructions` | 覆盖的指令数 |
| `found_bugs` | 检测到的漏洞类型及数量 |
| `note` | 备注说明 |

### Smartian 检测的漏洞类型

| 漏洞类型 | 对应 Benchmark |
| :--- | :--- |
| Reentrancy, Ether Leak, Block state Dependency | T1_Reentrancy |
| Integer Bug, Integer Overflow/Underflow, Assertion Failure | T2_Arithmetic |
| Transaction Origin Use, Unprotected Function, Suicidal, Assertion Failure | T3_AccessControl |
| Mishandled Exception, Unchecked Return Value, Assertion Failure | T4_UncheckedCalls |
