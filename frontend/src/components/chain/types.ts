/** 规则链定义（对应后端 ChainDefinition） */
export interface ChainDefinition {
  key: string
  name: string
  description?: string
  schemaVersion: number
  nodes: NodeInstance[]
}

/** 链内的一个节点实例 */
export interface NodeInstance {
  nodeKind: string
  params: Record<string, unknown>
  priority: number
}

/** 生成唯一 key */
export function createChainKey(): string {
  return crypto.randomUUID()
}

/** 创建默认链 */
export function createDefaultChain(name?: string): ChainDefinition {
  return {
    key: createChainKey(),
    name: name ?? '链 1',
    description: '',
    schemaVersion: 1,
    nodes: []
  }
}
