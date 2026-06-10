/** 技术指标 → 中文标签映射 */
export const indicatorLabels: Record<string, string> = {
  RSI: 'RSI 相对强弱指数',
  SMA_20: 'SMA 20 简单移动均线',
  SMA_50: 'SMA 50 简单移动均线',
  EMA_20: 'EMA 20 指数移动均线',
  MACD_LINE: 'MACD 快线',
  MACD_SIGNAL: 'MACD 信号线',
  BB_UPPER: '布林带上轨',
  BB_LOWER: '布林带下轨',
  OBV: 'OBV 能量潮',
  VOLUME_SMA: 'VOLUME 均量线',
  RANGE_PCT: 'RANGE_PCT 振幅%',
}

/** 上下文指标 → 中文标签 */
export const contextIndicatorLabels: Record<string, string> = {
  DEVIATION_FROM_AVG: 'DEVIATION_FROM_AVG 偏离均价%',
  PYRAMIDING_LEVEL: 'PYRAMIDING_LEVEL 加仓层数',
  POSITION_NOTIONAL: 'POSITION_NOTIONAL 持仓市值',
  POSITION_PNL_PCT: 'POSITION_PNL_PCT 持仓盈亏%',
  POSITION_COUNT: 'POSITION_COUNT 持仓笔数',
}

/** 逻辑运算符 → 中文标签 */
export const operatorLabels: Record<string, string> = {
  AND: 'AND 且（全部满足）',
  OR: 'OR 或（任一满足）',
  NOT: 'NOT 非（取反）',
  TRUE: 'TRUE 恒真',
}

/** 比较运算符 → 中文标签 */
export const comparisonLabels: Record<string, string> = {
  '>': '> 大于',
  '<': '< 小于',
  '>=': '≥ 大于等于',
  '<=': '≤ 小于等于',
  '==': '＝ 等于',
  CA: '↗ 上穿（金叉）',
  CB: '↘ 下穿（死叉）',
}

/** 动作 → 中文标签 */
export const actionLabels: Record<string, string> = {
  buy: '买入',
  sell: '减仓',
  sellAll: '全平',
  hold: '保持',
}

/** 上下文 → 中文标签 */
export const contextLabels: Record<string, string> = {
  any: '不限',
  noPosition: '无持仓',
  hasPosition: '有持仓',
}

/** 下单量类型 → 中文标签 */
export const sizeTypeLabels: Record<string, string> = {
  fixed: '固定金额 ($)',
  multiplier: '倍率引用',
}

/** 获取指标的中文标签（含回退到英文） */
export function labelForIndicator(key: string): string {
  return indicatorLabels[key] || contextIndicatorLabels[key] || key
}

/** 获取比较符的中文标签（含回退到原始符号） */
export function labelForComparison(key: string): string {
  return comparisonLabels[key] || key
}

/** 获取运算符的中文标签（含回退） */
export function labelForOperator(key: string): string {
  return operatorLabels[key] || key
}

/** 运算符列表（保留供选择使用） */
export const operatorKeys = ['AND', 'OR', 'NOT', 'TRUE']
/** 默认指标列表 */
export const defaultIndicatorKeys = ['RSI', 'SMA_20', 'SMA_50', 'EMA_20', 'MACD_LINE', 'MACD_SIGNAL', 'BB_UPPER', 'BB_LOWER', 'OBV', 'VOLUME_SMA', 'RANGE_PCT']
/** 比较符列表 */
export const comparisonKeys: string[] = ['>', '<', '>=', '<=', '==', 'CA', 'CB']

/** 可合并显示用：技术指标 + 上下文指标的统一映射 */
export const allIndicatorLabels: Record<string, string> = {
  ...indicatorLabels,
  ...contextIndicatorLabels,
}
