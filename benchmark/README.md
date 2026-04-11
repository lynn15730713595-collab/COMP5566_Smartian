# 基准数据集概览

&#x20;

样本总数： 28 个（T1–T4 来自 SmartBugs Curated；T5–T6 来自 DeFiHackLabs） 覆盖范围： 6 种漏洞类型，每类 4–5 个样本

## 数据来源

| 来源                | 涵盖类型  | 选用原因                           |
| :---------------- | :---- | :----------------------------- |
| SmartBugs Curated | T1–T4 | 预标注真实标签，Solidity 可直接编译，无需预处理   |
| DeFiHackLabs      | T5–T6 | DeFi 特有漏洞类型（预言机操控 / 业务逻辑）的唯一来源 |

## 目录结构

```
benchmark/
├── T1_Reentrancy/          5 个样本  SWC-107   SmartBugs     工具：检测能力强
├── T2_Arithmetic/          5 个样本  SWC-101   SmartBugs     工具：检测能力强
├── T3_AccessControl/       5 个样本  SWC-105/6 SmartBugs     工具：检测能力中等
├── T4_UncheckedCalls/      5 个样本  SWC-104   SmartBugs     工具：中等（模糊测试工具较弱）
├── T5_OracleManipulation/  5 个样本  —         DeFiHackLabs  工具：完全无法检测
└── T6_BusinessLogic/       4 个样本  —         DeFiHackLabs  工具：完全无法检测
```

## 样本索引

### T1–T4：SmartBugs Curated（漏洞合约源码可直接使用）

| 类型 | 文件                                              | Pragma 版本 | 备注                        |
| :- | :---------------------------------------------- | :-------- | :------------------------ |
| T1 | reentrancy_dao.sol                              | 0.4.19    | DAO 风格重入攻击                |
| T1 | etherstore.sol                                  | 0.4.10    | ETH Store 经典示例            |
| T1 | reentrancy_cross_function.sol                   | 0.4.24    | 跨函数重入变体                   |
| T1 | modifier_reentrancy.sol                         | 0.4.24    | 基于修饰符的重入                  |
| T1 | reentrancy_insecure.sol                         | 0.5.0     | 简单模式，较新编译器版本              |
| T2 | BECToken.sol                                    | 0.4.16    | 真实案例 BeautyChain，损失约 $9 亿 |
| T2 | integer_overflow_1.sol                          | 0.4.15    | 基础减法下溢                    |
| T2 | integer_overflow_mul.sol                        | 0.4.19    | 乘法溢出                      |
| T2 | integer_overflow_multitx_multifunc_feasible.sol | 0.4.23    | 多交易/多函数路径                 |
| T2 | overflow_single_tx.sol                          | 0.4.23    | 单合约内多处溢出                  |
| T3 | parity_wallet_bug_1.sol                         | 0.4.9     | Parity v1：无保护的 initWallet |
| T3 | parity_wallet_bug_2.sol                         | 0.4.9     | Parity v2：库自毁             |
| T3 | phishable.sol                                   | 0.4.22    | tx.origin 钓鱼攻击            |
| T3 | rubixi.sol                                      | 0.4.15    | 构造函数重命名导致所有权被篡夺           |
| T3 | unprotected0.sol                                | 0.4.15    | 最简无保护提款示例                 |
| T4 | king_of_the_ether_throne.sol                    | 0.4.0     | 真实案例 King of Ether        |
| T4 | mishandled.sol                                  | 0.4.0     | 经典未处理 send 示例             |
| T4 | unchecked_return_value.sol                      | 0.4.25    | 显式未检查 call() 返回值          |
| T4 | lotto.sol                                       | 0.4.18    | 彩票合约未检查支付结果               |
| T4 | etherpot_lotto.sol                              | 0.4.0     | EtherPot 多处未检查调用          |

### T5–T6：DeFiHackLabs（需从区块链浏览器获取漏洞合约源码）

| 类型 | 案例            | 日期      | 损失        | 链        |
| :- | :------------ | :------ | :-------- | :------- |
| T5 | Makina        | 2026-01 | ~$5.1M    | Ethereum |
| T5 | Moonwell      | 2026-02 | ~$1.78M   | Ethereum |
| T5 | ImpermaxV3    | 2025-04 | ~$300K    | Base     |
| T5 | UwuLend       | 2024-06 | ~$19.3M   | Ethereum |
| T5 | CompoundUni   | 2024-02 | ~$440K    | Ethereum |
| T6 | AlkemiEarn    | 2026-03 | 43.45 ETH | Ethereum |
| T6 | SynapLogic    | 2026-01 | ~27.6 ETH | Base/ETH |
| T6 | LAXO Token    | 2026-02 | ~$137K    | BSC      |
| T6 | SharwaFinance | 2025-10 | $146K     | Arbitrum |

## 工具输入流程

### T1–T4（SmartBugs——无需预处理）

```
直接对 .sol 文件运行工具：
  slither benchmark/T1_Reentrancy/reentrancy_dao.sol
  myth analyze benchmark/T1_Reentrancy/reentrancy_dao.sol
```

### T5–T6（DeFiHackLabs——需要预处理）

```
第 1 步：读取 PoC 文件头 → 找到"漏洞合约"地址
第 2 步：从 Etherscan / Arbiscan / BSCscan 获取源码
第 3 步：保存为同目录下的 <合约名>_vuln.sol
第 4 步：对 <合约名>_vuln.sol 运行检测工具
```

## 工具兼容性说明

T1–T4 与 T5–T6 在工具可用性上存在实质差异：

**T1–T4（SmartBugs）：** 小型独立合约，无外部 import，所有工具可直接运行。

**T5–T6（_vuln.sol）：** 由多文件项目拼接而成，文件内仍保留原始 `import` 路径，但对应文件并不存在于本地，导致编译器报错。

| 工具 | T1–T4 | T5–T6 |
| :--- | :--- | :--- |
| Slither | ✅ 直接运行 | ⚠️ 编译失败，需加 `--solc-remaps` 或改用字节码模式 |
| Mythril | ✅ 直接运行 | ⚠️ 编译失败，可改用 `--bin` 传入链上字节码 |
| Echidna | ✅ 加属性后运行 | ❌ 依赖成功编译，基本不可用 |
| Smartian | ✅ 编译后运行 | ❌ 依赖成功编译，基本不可用 |

**Slither / Mythril 对 T5–T6 的处理方式：**

```bash
# Slither：跳过本地编译，直接用 solc AST（功能受限但能运行）
slither <contract>_vuln.sol --no-solc-compile --solc-ast

# Mythril：从链上获取已编译字节码，绕过源码编译
# 先用 cast 或 web3 拉取 bytecode
cast code <address> --rpc-url https://mainnet.infura.io/v3/<KEY> > contract.bin
myth analyze --bin contract.bin
```

**Echidna / Smartian 对 T5–T6：** 记录为 "not applicable"，在结果表中单独标注。这本身就是预期结论——这两类漏洞超出模糊测试工具的能力范围，无法运行也是一种有效的实验结果。

---

## 真实标签与评分规则

* 文件夹名即为该文件夹内所有样本的真实标签

* SmartBugs 样本：vulnerabilities.json 中记录了精确的漏洞行号

* TP（真阳性）： 工具报告了正确的漏洞类型

* FP（假阳性）： 工具报告了错误类型，或对该合约产生无关警告

* FN（假阴性）： 工具未产生任何相关报告（含工具无法运行的情况）

