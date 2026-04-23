# BSC Smart Money Trading Bot (C# 版本)

这是一个用C#重新实现的BSC聪明钱自动交易机器人，替换了原有的Python脚本版本。

## 技术标准

本项目严格遵守用户定义的C#项目技术标准：

- **.NET版本**: 10.0（最低要求）
- **C#语法**: 14（最新预览版）
- **目标平台**: 跨平台（支持Windows、macOS、Linux）
- **架构**: 分层架构 + 依赖注入
- **发布方式**: 单文件二进制（无需.NET运行时）
- **包管理**: 所有Microsoft.Extensions.*包使用10.0.0版本

### 验证技术标准

可用以下命令验证技术标准：

```bash
dotnet build BscSmartMoneyBot.slnx
dotnet test BscSmartMoneyBot.slnx
```

输出应显示所有检查项通过：
- ✅ .NET 10.x 已安装
- ✅ 目标框架: net10.0
- ✅ C#语法版本: preview (C# 14)
- ✅ 项目构建成功

## 特性

- ✅ 单文件二进制发布（无需.NET运行时）
- ✅ 完整的交易逻辑（信号监控、过滤、安全扫描、买入、止盈止损）
- ✅ 测试模式（dry-run）支持
- ✅ 状态持久化（JSON格式）
- ✅ 动态轮询间隔（根据持仓波动调整）
- ✅ 专业日志系统（Serilog）
- ✅ 命令行参数支持
- ✅ 配置文件管理（appsettings.json）

## 快速开始

### 1. 配置钱包地址

编辑 `BscSmartMoneyBot/appsettings.json` 文件，设置你的钱包地址：

```json
{
  "Wallet": {
    "Address": "0xYourWalletAddressHere"
  }
}
```

### 2. 运行测试模式

```bash
# 在Windows上
.\publish\BscSmartMoneyBot.exe --dry-run --verbose

# 在macOS/Linux上（需要安装.NET运行时）
dotnet run --project BscSmartMoneyBot/BscSmartMoneyBot.csproj -- --dry-run --verbose
```

### 3. 运行实盘模式

```bash
.\publish\BscSmartMoneyBot.exe
```

## 命令行参数

| 参数 | 说明 | 示例 |
|------|------|------|
| `--dry-run` | 测试模式（不实际交易） | `--dry-run` |
| `--verbose` | 详细日志输出 | `--verbose` |
| `--config` | 配置文件路径 | `--config myconfig.json` |
| `--state` | 状态文件路径 | `--state mystate.json` |
| `--interval` | 轮询间隔（秒） | `--interval 3` |
| `--max-positions` | 最大持仓数量 | `--max-positions 2` |
| `--wallet` | 钱包地址 | `--wallet 0xYourAddress` |
| `--chain` | 区块链网络 | `--chain bsc` |
| `--log-level` | 日志级别 | `--log-level Debug` |

## 配置文件说明

### 主要配置项

```json
{
  "BotSettings": {
    "DryRun": false,           // 是否测试模式
    "StateFilePath": "state/bot_state.json"  // 状态文件路径
  },
  
  "Monitoring": {
    "PollIntervalSeconds": 5,  // 基础轮询间隔
    "HighVolIntervalSeconds": 3  // 高波动时轮询间隔
  },
  
  "Signals": {
    "Chain": "bsc",            // 区块链网络
    "MinMarketCap": 50000,     // 最小市值（USD）
    "MinLiquidity": 100000,    // 最小流动性（USD）
    "MaxSoldRatio": 85,        // 最大卖出比例（%）
    "MinSmartMoneyWallets": 3  // 最小聪明钱包数量
  },
  
  "Trading": {
    "MaxOpenPositions": 1,     // 最大持仓数
    "CooldownMinutes": 30,     // 冷却时间（分钟）
    "MaxPositionSizeUSD": 100  // 最大持仓金额（USD）
  },
  
  "Risk": {
    "StopLossPercent": 20.0,   // 止损比例（%）
    "TakeProfitPercent": 50.0, // 止盈比例（%）
    "TrailingStopPercent": 10.0 // 移动止损比例（%）
  }
}
```

## 文件结构

```
BscSmartMoneyBot/
├── BscSmartMoneyBot.slnx         # 解决方案入口
├── BscSmartMoneyBot/             # 主项目
├── BscSmartMoneyBot.Tests/       # 测试项目
├── publish/                    # 发布目录
│   ├── BscSmartMoneyBot.exe   # 主程序（单文件二进制）
│   ├── appsettings.json       # 配置文件
│   ├── logs/                  # 日志目录
│   └── state/                 # 状态文件目录
├── Build/                     # 构建脚本
│   ├── build.ps1             # 构建脚本
│   └── run.ps1               # 运行脚本
└── ...                        # 源代码
```

## 构建说明

### 1. 开发构建

```bash
dotnet build BscSmartMoneyBot.slnx
dotnet run --project BscSmartMoneyBot/BscSmartMoneyBot.csproj -- --dry-run
```

### 2. 发布为单文件二进制

```bash
# Windows
dotnet publish BscSmartMoneyBot/BscSmartMoneyBot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# macOS
dotnet publish BscSmartMoneyBot/BscSmartMoneyBot.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true

# Linux
dotnet publish BscSmartMoneyBot/BscSmartMoneyBot.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

### 3. 使用构建脚本

```powershell
# Windows PowerShell
.\Build\build.ps1
.\Build\run.ps1 -DryRun -Verbose
```

## 替换Python版本

### 1. 停止Python脚本

```bash
# 找到并停止Python进程
pkill -f "python.*smartmoney"
```

### 2. 迁移状态文件（如果需要）

```bash
# 备份旧状态
cp ~/.hermes/onchainos_bsc_smartmoney_state.json ./state/legacy_state.json

# 启动C#版本（会自动创建新状态文件）
.\publish\BscSmartMoneyBot.exe --dry-run
```

### 3. 验证运行

```bash
# 查看日志
tail -f ./publish/logs/bot.log

# 查看状态
cat ./publish/state/bot_state.json | jq .
```

## 监控和管理

### 1. 查看实时日志

```bash
tail -f ./publish/logs/bot.log
```

### 2. 查看状态文件

```bash
# 使用jq格式化JSON
cat ./publish/state/bot_state.json | jq .

# 查看持仓摘要
cat ./publish/state/bot_state.json | jq '.OpenPositions | to_entries[] | .value'
```

### 3. 停止机器人

按 `Ctrl+C` 停止程序，状态会自动保存。

## 故障排除

### 1. onchainos命令找不到

确保onchainos在PATH环境变量中：

```bash
# Windows
$env:Path += ";C:\Users\YourName\AppData\Local\Programs\onchainos"

# macOS/Linux
export PATH="$PATH:/usr/local/bin"
```

### 2. 权限问题

```bash
# Windows（以管理员运行）
Start-Process PowerShell -Verb RunAs -ArgumentList "-File .\run.ps1"
```

### 3. 状态文件损坏

```bash
# 备份并重置状态
mv ./publish/state/bot_state.json ./publish/state/bot_state.json.bak
```

## 性能对比

| 特性 | Python原版 | C#新版 | 改进 |
|------|-----------|--------|------|
| 启动速度 | 较慢（解释器） | 快速（原生） | ✅ 秒级启动 |
| 内存占用 | ~100MB | ~50MB | ✅ 减少50% |
| 部署方式 | 需要Python环境 | 单文件二进制 | ✅ 无需环境依赖 |
| 错误处理 | try/except | 结构化异常 | ✅ 更健壮 |

## 支持

如有问题，请检查日志文件或联系开发者。

---

**注意**：交易有风险，请谨慎使用。建议先在测试模式下运行，确保理解所有功能后再使用实盘模式。
