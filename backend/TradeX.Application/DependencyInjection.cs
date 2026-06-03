using Microsoft.Extensions.DependencyInjection;
using TradeX.Application.Common;
using TradeX.Application.Dashboard;
using TradeX.Application.Orders;
using TradeX.Application.Setup;
using TradeX.Application.Traders;

namespace TradeX.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // ═══ Use Cases（命令模式）═══
        services.AddSingleton<IUseCase<GetTradersQuery, Result<List<Traders.DTOs.TraderDto>>>, GetTradersUseCase>();
        services.AddSingleton<IUseCase<CreateTraderCommand, Result<Traders.DTOs.TraderDto>>, CreateTraderUseCase>();
        services.AddSingleton<IUseCase<GetTraderStatsQuery, Result<Traders.DTOs.TraderStatsDto>>, GetTraderStatsUseCase>();
        services.AddSingleton<IUseCase<GetTraderByIdQuery, Result<Traders.TraderDetailDto>>, GetTraderByIdUseCase>();
        services.AddSingleton<IUseCase<UpdateTraderCommand, Result<Traders.TraderDetailDto>>, UpdateTraderUseCase>();
        services.AddSingleton<IUseCase<DeleteTraderCommand, Result>, DeleteTraderUseCase>();
        services.AddSingleton<IUseCase<GetTraderOrdersQuery, Result<List<Orders.DTOs.OrderDto>>>, GetTraderOrdersUseCase>();
        services.AddSingleton<IUseCase<CreateManualOrderCommand, Result<Orders.DTOs.OrderDto>>, CreateManualOrderUseCase>();

        // ═══ 应用服务 ═══
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<ISetupService, SetupService>();

        return services;
    }
}
