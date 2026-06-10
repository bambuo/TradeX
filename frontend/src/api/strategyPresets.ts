import martingale from '../assets/presets/martingale.json'
import maCrossover from '../assets/presets/ma-crossover.json'
import macdCrossover from '../assets/presets/macd-crossover.json'
import breakoutStrategy from '../assets/presets/breakout-strategy.json'

export interface StrategyPreset {
  name: string
  description: string
  notes: string[]
  executionRule: string
}

interface PresetJson {
  name: string
  description: string
  notes: string[]
  executionRule: Record<string, unknown>
}

function loadPreset(json: PresetJson): StrategyPreset {
  return {
    name: json.name,
    description: json.description,
    notes: json.notes,
    executionRule: JSON.stringify(json.executionRule)
  }
}

export const strategyPresets: StrategyPreset[] = [
  loadPreset(martingale as PresetJson),
  loadPreset(maCrossover as PresetJson),
  loadPreset(macdCrossover as PresetJson),
  loadPreset(breakoutStrategy as PresetJson)
]
