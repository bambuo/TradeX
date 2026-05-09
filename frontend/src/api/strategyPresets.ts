export interface StrategyPreset {
  name: string
  description: string
  notes: string[]
  entryCondition: string
  exitCondition: string
  executionRule: string
}

export const strategyPresets: StrategyPreset[] = [
  {
    name: 'RSI 均值回归',
    description: '震荡市低买高卖。RSI < 30 超卖时分批买入、> 70 超买时分批卖出，在价格均值修复过程中获利',
    notes: [
      '数字货币波动大，层间距 1.5% 适配 BTC/ETH 的 15m~1h 级别，山寨币建议放大至 2.5~3%',
      '5 层满仓时总敞口 = 1000 USDT，连续 3 层被套应暂停加仓，等价格回归网格内再恢复',
      'RSI 30/70 适用于 15m/1h 短线，日线级别建议放宽至 25/75 以减少假信号',
      '每日亏损达 200 USDT（总敞口的 20%）时自动熔断，防止单边行情持续亏损'
    ],
    entryCondition: JSON.stringify({
      operator: '',
      indicator: 'RSI',
      comparison: '<',
      value: 30
    }),
    exitCondition: JSON.stringify({
      operator: '',
      indicator: 'RSI',
      comparison: '>',
      value: 70
    }),
    executionRule: JSON.stringify({
      type: 'grid',
      gridLevels: 5,
      gridSpacingPercent: 1.5,
      basePositionSize: 200,
      maxPositionSize: 1000,
      maxDailyLoss: 200,
      slippageTolerance: 0.0005
    })
  },
  {
    name: '趋势追踪策略',
    description: '捕捉趋势行情。MACD 上穿零轴同时 RSI > 50 确认多头后入场，MACD 下穿零轴且 RSI < 50 两信号同时确认趋势转弱时出场',
    notes: [
      '入场 AND + 出场 AND，确保趋势确认一致：入场需要双重确认，出场也要双重确认，避免假信号过早平仓',
      '追踪止损 2% 适配 15m/1h 短线——BTC 日均波幅 3~5%，2% 足够容纳正常回调又不会让利润回吐太多',
      '止盈建议 3 倍盈亏比：止损 2% 时止盈设在 6%，单比正期望值 = 60%胜率 × 6% - 40%败率 × 2% = +2.8%',
      '单笔风险 ≤ 总资金 2%，连续亏损 3 次应暂停策略，检查市场是否处于长期震荡不适合趋势策略'
    ],
    entryCondition: JSON.stringify({
      operator: 'AND',
      conditions: [
        { operator: '', indicator: 'MACD_LINE', comparison: 'CrossAbove', value: 0 },
        { operator: '', indicator: 'RSI', comparison: '>', value: 50 }
      ]
    }),
    exitCondition: JSON.stringify({
      operator: 'AND',
      conditions: [
        { operator: '', indicator: 'MACD_LINE', comparison: 'CrossBelow', value: 0 },
        { operator: '', indicator: 'RSI', comparison: '<', value: 50 }
      ]
    }),
    executionRule: JSON.stringify({
      type: 'trend_following',
      trailingStopPercent: 2.0,
      takeProfitPercent: 6.0,
      basePositionSize: 100,
      maxPositionSize: 300,
      maxDailyLoss: 200,
      slippageTolerance: 0.0005
    })
  },
  {
    name: '无限网格策略',
    description: '极端超跌后左侧布局。RSI 深度超卖（< 30）且 MACD 在零轴下时确认恐慌见底，分批金字塔建仓；RSI 回归超买区（> 70）且 MACD 翻红时确认反弹到位，分批出场',
    notes: [
      '左侧布局只做极端行情，RSI < 30 + MACD < 0 双重确认恐慌底，放弃模棱两可的浅度回调机会',
      '4 层金字塔：1x → 0.7x → 0.5x → 0.3x，合计 2.5 倍基础仓位，避免在下跌初期就重仓被套',
      '出场 AND 条件：必须 RSI > 70 超买且 MACD 翻红为正才出场，早了可能踏空反弹后半段',
      '每轮按 4 层 × 1% 间距 = 4% 价格覆盖范围，匹配 BTC 单次恐慌下跌的典型深度（3~8%）',
      '风控：每层独立止损 5%（超过 5 层间距），极端行情继续下探时逐层止损退出'
    ],
    entryCondition: JSON.stringify({
      operator: 'AND',
      conditions: [
        { operator: '', indicator: 'RSI', comparison: '<', value: 30 },
        { operator: '', indicator: 'MACD_LINE', comparison: '<', value: 0 }
      ]
    }),
    exitCondition: JSON.stringify({
      operator: 'AND',
      conditions: [
        { operator: '', indicator: 'RSI', comparison: '>', value: 70 },
        { operator: '', indicator: 'MACD_LINE', comparison: '>', value: 0 }
      ]
    }),
    executionRule: JSON.stringify({
      type: 'infinity_grid',
      gridLevels: 4,
      gridSpacingPercent: 1.0,
      basePositionSize: 100,
      maxPositionSize: 250,
      maxDailyLoss: 100,
      slippageTolerance: 0.0005,
      usePyramiding: true,
      maxPyramidingLevels: 4
    })
  },
  {
    name: '波幅均价再平衡',
    description: '5m/15m 观察波动，波幅达到阈值后小仓入场；随后围绕持仓均价按 1% 网格分批加仓与减仓，最多追加 5 次',
    notes: [
      '首单触发条件建议设置为 RANGE_PCT >= 1，适配高波动时段，低波动时不入场',
      '加仓条件由执行规则驱动：当前价 <= 均价 * (1 - 1%)，最多追加 5 次，防止无限摊平',
      '减仓条件由执行规则驱动：当前价 >= 均价 * (1 + 1%)，每次减一笔，逐步锁定利润',
      '该风格可关闭单笔止损，但建议保留 maxDailyLoss 作为账户级兜底'
    ],
    entryCondition: JSON.stringify({
      operator: '',
      indicator: 'RANGE_PCT',
      comparison: '>=',
      value: 1
    }),
    exitCondition: '{}',
    executionRule: JSON.stringify({
      type: 'volatility_grid',
      entryVolatilityPercent: 1.0,
      rebalancePercent: 1.0,
      basePositionSize: 100,
      maxPositionSize: 600,
      maxPyramidingLevels: 5,
      noStopLoss: true,
      slippageTolerance: 0.0005,
      maxDailyLoss: 200
    })
  }
]
