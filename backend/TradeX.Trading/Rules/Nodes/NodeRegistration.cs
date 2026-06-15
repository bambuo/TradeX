using TradeX.Core.Rules;

namespace TradeX.Trading.Rules.Nodes;

/// <summary>
/// 将所有内置规则节点注册到 NodeRegistry。
/// 在应用启动时调用一次（如 Program.cs 或模块注册中）。
/// </summary>
public static class NodeRegistration
{
    /// <summary>
    /// 注册所有内置规则节点。新增节点工厂时在此方法中追加对应注册调用。
    /// </summary>
    public static void RegisterAllNodes(this NodeRegistry reg)
    {
        reg.RegisterGateNodes();
        reg.RegisterFilterNodes();
        reg.RegisterDeriveNodes();
        reg.RegisterSizeNodes();
        reg.RegisterActionNodes();
        reg.RegisterRiskNodes();
        reg.RegisterOverrideNodes();
        reg.RegisterCostAnchorNode();
    }
}
