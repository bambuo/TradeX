export interface StrategyPreset {
  name: string
  description: string
  notes: string[]
  entryConditionJson: string
  exitConditionJson: string
  executionRuleJson: string
}

export const strategyPresets: StrategyPreset[] = [
  {
    name: '网格策略',
    description: '震荡市低买高卖。RSI < 30 超卖时买入、> 70 超买时卖出，循环执行网格交易',
    notes: [
      '最佳实践：网格层数 3~5 层为宜，过多层数会过度分散资金',
      '网格间距 1% 适合 BTC/ETH 等主流币，山寨币建议放大到 1.5~2%',
      'RSI 阈值 30/70 是经典的超买超卖区间，适用于大部分交易对',
      '资金管理：每层仓位 = 总资金 ÷ (网格层数 × 2)，保留一半资金防极端行情'
    ],
    entryConditionJson: JSON.stringify({
      operator: '',
      indicator: 'RSI',
      comparison: '<',
      value: 30
    }),
    exitConditionJson: JSON.stringify({
      operator: '',
      indicator: 'RSI',
      comparison: '>',
      value: 70
    }),
    executionRuleJson: JSON.stringify({
      type: 'grid',
      gridLevels: 5,
      gridSpacingPercent: 1.0,
      basePositionSize: 200,
      maxPositionSize: 600,
      maxDailyLoss: 100,
      slippageTolerance: 0.0005
    })
  },
  {
    name: '趋势追踪策略',
    description: '捕捉趋势行情。MACD 上穿零轴 + RSI > 50 确认多头后入场，MACD 下穿零轴或 RSI < 50 趋势转弱时出场',
    notes: [
      '最佳实践：趋势确认使用双重过滤（MACD 趋势 + RSI 动能），避免假突破',
      '追踪止损 3% 适用于日线级别，15m/1h 短线建议缩至 1~1.5%',
      '止盈建议 2~3 倍盈亏比：止损 3% 时，止盈设在 6~9%',
      '单笔风险控制在总资金的 1~2% 以内，连续亏损 3 次应暂停策略'
    ],
    entryConditionJson: JSON.stringify({
      operator: 'AND',
      conditions: [
        { operator: '', indicator: 'MACD_LINE', comparison: 'CrossAbove', value: 0 },
        { operator: '', indicator: 'RSI', comparison: '>', value: 50 }
      ]
    }),
    exitConditionJson: JSON.stringify({
      operator: 'OR',
      conditions: [
        { operator: '', indicator: 'MACD_LINE', comparison: 'CrossBelow', value: 0 },
        { operator: '', indicator: 'RSI', comparison: '<', value: 50 }
      ]
    }),
    executionRuleJson: JSON.stringify({
      type: 'trend_following',
      trailingStopPercent: 3.0,
      takeProfitPercent: 8.0,
      basePositionSize: 100,
      maxPositionSize: 300,
      maxDailyLoss: 200,
      slippageTolerance: 0.0005
    })
  },
  {
    name: '无限网格策略',
    description: '大波动市场左侧布局。RSI 深度超卖 + 下跌趋势时分批建仓，反弹超买或趋势反转时分批出场，支持金字塔加仓',
    notes: [
      '最佳实践：RSI 阈值放大到 25/75，比标准网格更宽的区间适合大波动行情',
      '网格层数 8~10 层，间距 0.8% 捕捉底部区域的每一次下跌',
      '金字塔加仓：首次建仓 1x，后续按 0.7x、0.5x、0.3x 递减，防止顶部重仓',
      '无限网格不设止盈，依赖趋势反转自动出场，适合在暴跌后左侧布局',
      '风控要点：最大亏损设为总资金的 40%，极端行情有足够缓冲'
    ],
    entryConditionJson: JSON.stringify({
      operator: 'AND',
      conditions: [
        { operator: '', indicator: 'RSI', comparison: '<', value: 25 },
        { operator: '', indicator: 'MACD_LINE', comparison: '<', value: 0 }
      ]
    }),
    exitConditionJson: JSON.stringify({
      operator: 'OR',
      conditions: [
        { operator: '', indicator: 'RSI', comparison: '>', value: 75 },
        { operator: '', indicator: 'MACD_LINE', comparison: '>', value: 0 }
      ]
    }),
    executionRuleJson: JSON.stringify({
      type: 'infinity_grid',
      gridLevels: 8,
      gridSpacingPercent: 0.8,
      basePositionSize: 50,
      maxPositionSize: 800,
      maxDailyLoss: 400,
      slippageTolerance: 0.001,
      usePyramiding: true,
      maxPyramidingLevels: 4
    })
  }
]
