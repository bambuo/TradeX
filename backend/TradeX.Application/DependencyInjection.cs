using Microsoft.Extensions.DependencyInjection;
using TradeX.Application.Auth;
using TradeX.Application.Backtesting;
using TradeX.Application.Common;
using TradeX.Application.Dashboard;
using TradeX.Application.Orders;
using TradeX.Application.Positions;
using TradeX.Application.Setup;
using TradeX.Application.Strategies;
using TradeX.Application.StrategyBindings;
using TradeX.Application.Exchanges;
using TradeX.Application.System;
using TradeX.Application.Traders;
using TradeX.Application.Users;
using TradeX.Application.Users.DTOs;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

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

        // ── Positions ──
        services.AddSingleton<IUseCase<GetOpenPositionsQuery, Result<List<PositionDto>>>, GetOpenPositionsUseCase>();
        services.AddSingleton<IUseCase<GetPositionByIdQuery, Result<PositionDto>>, GetPositionByIdUseCase>();

        // ── Backtesting ──
        services.AddSingleton<IUseCase<GetBacktestTasksQuery, Result<List<BacktestTaskDto>>>, GetBacktestTasksUseCase>();
        services.AddSingleton<IUseCase<GetBacktestTaskByIdQuery, Result<BacktestTaskDto>>, GetBacktestTaskByIdUseCase>();
        services.AddSingleton<IUseCase<CancelBacktestCommand, Result>, CancelBacktestUseCase>();
        services.AddSingleton<IUseCase<GetBacktestAnalysisPageQuery, Result<BacktestAnalysisPageDto>>, GetBacktestAnalysisPageUseCase>();
        services.AddSingleton<IUseCase<GetBacktestAnalysisAllQuery, Result<BacktestKlineAnalysis[]>>, GetBacktestAnalysisAllUseCase>();
        services.AddSingleton<IUseCase<GetBacktestAnalysisCountQuery, Result<int>>, GetBacktestAnalysisCountUseCase>();

        // ── System ──
        services.AddSingleton<IUseCase<GetExchangeStatusQuery, Result<List<ExchangeStatusDto>>>, GetExchangeStatusUseCase>();
        services.AddSingleton<IUseCase<EmergencyStopCommand, Result<EmergencyStopResultDto>>, EmergencyStopUseCase>();
        services.AddSingleton<IUseCase<GetSystemLogsQuery, Result<List<SystemLogEntryDto>>>, GetSystemLogsUseCase>();

        // ── Strategies ──
        services.AddSingleton<IUseCase<GetStrategiesQuery, Result<List<StrategyDto>>>, GetStrategiesUseCase>();
        services.AddSingleton<IUseCase<GetStrategyByIdQuery, Result<StrategyDto>>, GetStrategyByIdUseCase>();
        services.AddSingleton<IUseCase<CreateStrategyCommand, Result<StrategyDto>>, CreateStrategyUseCase>();
        services.AddSingleton<IUseCase<UpdateStrategyCommand, Result<StrategyDto>>, UpdateStrategyUseCase>();
        services.AddSingleton<IUseCase<DeleteStrategyCommand, Result>, DeleteStrategyUseCase>();

        // ── StrategyBindings ──
        services.AddSingleton<IUseCase<GetBindingsQuery, Result<List<BindingDto>>>, GetBindingsUseCase>();
        services.AddSingleton<IUseCase<GetBindingByIdQuery, Result<BindingDto>>, GetBindingByIdUseCase>();
        services.AddSingleton<IUseCase<CreateBindingCommand, Result<BindingDto>>, CreateBindingUseCase>();
        services.AddSingleton<IUseCase<UpdateBindingCommand, Result<BindingDto>>, UpdateBindingUseCase>();
        services.AddSingleton<IUseCase<DeleteBindingCommand, Result>, DeleteBindingUseCase>();
        services.AddSingleton<IUseCase<ActivateBindingCommand, Result<BindingDto>>, ActivateBindingUseCase>();
        services.AddSingleton<IUseCase<DeactivateBindingCommand, Result<BindingDto>>, DeactivateBindingUseCase>();

        // ── Exchanges ──
        services.AddSingleton<IUseCase<GetExchangesQuery, Result<List<ExchangeDto>>>, GetExchangesUseCase>();
        services.AddSingleton<IUseCase<GetExchangeByIdQuery, Result<ExchangeDto>>, GetExchangeByIdUseCase>();
        services.AddSingleton<IUseCase<CreateExchangeCommand, Result<ExchangeDto>>, CreateExchangeUseCase>();
        services.AddSingleton<IUseCase<UpdateExchangeCommand, Result<ExchangeDto>>, UpdateExchangeUseCase>();
        services.AddSingleton<IUseCase<DeleteExchangeCommand, Result>, DeleteExchangeUseCase>();
        services.AddSingleton<IUseCase<TestExchangeCommand, Result<ExchangeTestResultDto>>, TestExchangeUseCase>();
        services.AddSingleton<IUseCase<GetExchangeAssetsCommand, Result<List<ExchangeAssetDto>>>, GetExchangeAssetsUseCase>();
        services.AddSingleton<IUseCase<GetExchangePairsCommand, Result<List<ExchangePairDto>>>, GetExchangePairsUseCase>();
        services.AddSingleton<IUseCase<GetExchangeOrdersQuery, Result<List<ExchangeOrderDto>>>, GetExchangeOrdersUseCase>();
        services.AddSingleton<IUseCase<ToggleExchangeCommand, Result>, ToggleExchangeUseCase>();

        // ── Users ──
        services.AddSingleton<IUseCase<GetUsersQuery, Result<List<UserDto>>>, GetUsersUseCase>();
        services.AddSingleton<IUseCase<GetUserByIdQuery, Result<UserDto>>, GetUserByIdUseCase>();
        services.AddSingleton<IUseCase<UpdateUserRoleCommand, Result>, UpdateUserRoleUseCase>();

        // ── Auth ──
        services.AddSingleton<IUseCase<LoginCommand, Result<AuthResultDto>>, LoginUseCase>();
        services.AddSingleton<IUseCase<RefreshTokenCommand, Result<AuthResultDto>>, RefreshTokenUseCase>();
        services.AddSingleton<IUseCase<GetCurrentUserQuery, Result<AuthResultDto>>, GetCurrentUserUseCase>();

        // ═══ 应用服务 ═══
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<ISetupService, SetupService>();
        services.AddSingleton<ISystemService, SystemService>();

        return services;
    }
}
