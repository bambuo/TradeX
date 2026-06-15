namespace TradeX.Trading.Rules;

/// <summary>
/// 信号依赖图，用于静态校验信号生成器之间的依赖关系。
/// 实现三色 DFS 检测环路、存在性检查、可达性检查、Kahn 算法拓扑排序。
/// </summary>
public sealed class DepGraph
{
    private readonly HashSet<string> _nodes = [];
    private readonly Dictionary<string, List<string>> _edges = [];
    private readonly HashSet<string> _sources = [];

    /// <summary>注册一个生成器及其依赖。若 deps 为空，该生成器被视为 source（初始信号）。</summary>
    public void AddGenerator(string name, string[] deps)
    {
        _nodes.Add(name);
        _edges[name] = [..deps];
        if (deps.Length == 0)
            _sources.Add(name);
    }

    /// <summary>依赖校验错误。</summary>
    public sealed record DepError(string Kind, string Node, string Detail);

    /// <summary>
    /// 执行完整的依赖图校验，返回所有错误（不短路）。
    /// 校验项：存在性检查 → 无环检查 → 可达性检查。
    /// </summary>
    public List<DepError> Validate()
    {
        var errs = new List<DepError>();
        errs.AddRange(CheckExistence());
        errs.AddRange(CheckCycles());
        errs.AddRange(CheckReachability());
        return errs;
    }

    // ─── 存在性检查 ───

    private List<DepError> CheckExistence()
    {
        var errs = new List<DepError>();
        foreach (var (name, deps) in _edges)
        {
            foreach (var dep in deps)
            {
                if (!_nodes.Contains(dep))
                    errs.Add(new DepError("missing", name, $"depends on '{dep}' which is not registered"));
            }
        }
        return errs;
    }

    // ─── 三色 DFS 环路检测 ───

    private enum Color { White, Gray, Black }

    private List<DepError> CheckCycles()
    {
        var errs = new List<DepError>();
        var colors = _nodes.ToDictionary(n => n, _ => Color.White);
        var path = new List<string>();

        void Dfs(string node)
        {
            colors[node] = Color.Gray;
            path.Add(node);

            if (_edges.TryGetValue(node, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (!_nodes.Contains(dep)) continue;
                    switch (colors.GetValueOrDefault(dep))
                    {
                        case Color.Gray:
                            var cycleStart = path.IndexOf(dep);
                            var cycle = path.Skip(cycleStart).Append(dep).ToList();
                            errs.Add(new DepError("cycle", node, $"cycle detected: [{string.Join(" -> ", cycle)}]"));
                            break;
                        case Color.White:
                            Dfs(dep);
                            break;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            colors[node] = Color.Black;
        }

        foreach (var name in _nodes)
        {
            if (colors[name] == Color.White)
                Dfs(name);
        }

        return errs;
    }

    // ─── 可达性检查 ───

    private List<DepError> CheckReachability()
    {
        if (_sources.Count == 0) return [];

        var reverseEdges = new Dictionary<string, List<string>>();
        foreach (var (name, deps) in _edges)
        {
            foreach (var dep in deps)
            {
                if (!reverseEdges.ContainsKey(dep))
                    reverseEdges[dep] = [];
                reverseEdges[dep].Add(name);
            }
        }

        var visited = new HashSet<string>(_sources);
        var queue = new Queue<string>(_sources);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (reverseEdges.TryGetValue(current, out var dependents))
            {
                foreach (var dependent in dependents)
                {
                    if (visited.Add(dependent))
                        queue.Enqueue(dependent);
                }
            }
        }

        var errs = new List<DepError>();
        foreach (var name in _nodes)
        {
            if (!visited.Contains(name))
                errs.Add(new DepError("unreachable", name, "cannot be reached from any source signal"));
        }
        return errs;
    }

    // ─── Kahn 算法拓扑排序 ───

    /// <summary>返回拓扑排序后的执行顺序。如果存在环路抛出 InvalidOperationException。</summary>
    public List<string> TopologicalOrder()
    {
        var inDegree = _nodes.ToDictionary(n => n, _ => 0);

        // 构建反向边用于减少入度
        var reverseEdges = new Dictionary<string, List<string>>();
        foreach (var (name, deps) in _edges)
        {
            foreach (var dep in deps)
            {
                if (!_nodes.Contains(dep)) continue;
                inDegree[name]++;

                if (!reverseEdges.ContainsKey(dep))
                    reverseEdges[dep] = [];
                reverseEdges[dep].Add(name);
            }
        }

        var queue = new Queue<string>(_nodes.Where(n => inDegree[n] == 0));
        var order = new List<string>(_nodes.Count);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            order.Add(current);

            if (reverseEdges.TryGetValue(current, out var dependents))
            {
                foreach (var dependent in dependents)
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                        queue.Enqueue(dependent);
                }
            }
        }

        if (order.Count != _nodes.Count)
            throw new InvalidOperationException(
                $"Dependency graph has cycle: processed {order.Count} of {_nodes.Count} nodes");

        return order;
    }
}
