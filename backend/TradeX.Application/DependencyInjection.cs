using Microsoft.Extensions.DependencyInjection;
using TradeX.Application.AuditLogs;
using TradeX.Application.Auth;
using TradeX.Application.Backtesting;
using TradeX.Application.Common;
using TradeX.Application.Dashboard;
using TradeX.Application.Orders;
using TradeX.Application.Positions;
using TradeX.Application.Setup;
using TradeX.Application.Settings;
using TradeX.Application.Strategies;
using TradeX.Application.StrategyBindings;
using TradeX.Application.Exchanges;
using TradeX.Application.Notifications;
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
        services.AddScoped<IUseCase<GetTradersQuery, Result<List<Traders.DTOs.TraderDto>>>, GetTradersUseCase>();
        services.AddScoped<IUseCase<CreateTraderCommand, Result<Traders.DTOs.TraderDto>>, CreateTraderUseCase>();
        services.AddScoped<IUseCase<GetTraderStatsQuery, Result<Traders.DTOs.TraderStatsDto>>, GetTraderStatsUseCase>();
        services.AddScoped<IUseCase<GetTraderByIdQuery, Result<Traders.TraderDetailDto>>, GetTraderByIdUseCase>();
        services.AddScoped<IUseCase<UpdateTraderCommand, Result<Traders.TraderDetailDto>>, UpdateTraderUseCase>();
        services.AddScoped<IUseCase<DeleteTraderCommand, Result>, DeleteTraderUseCase>();
        services.AddScoped<IUseCase<GetTraderOrdersQuery, Result<List<Orders.DTOs.OrderDto>>>, GetTraderOrdersUseCase>();
        services.AddScoped<IUseCase<CreateManualOrderCommand, Result<Orders.DTOs.OrderDto>>, CreateManualOrderUseCase>();

        // ── Positions ──
        services.AddScoped<IUseCase<GetOpenPositionsQuery, Result<List<PositionDto>>>, GetOpenPositionsUseCase>();
        services.AddScoped<IUseCase<GetPositionByIdQuery, Result<PositionDto>>, GetPositionByIdUseCase>();

        // ── Backtesting ──
        services.AddScoped<IUseCase<GetBacktestTasksQuery, Result<List<BacktestTaskDto>>>, GetBacktestTasksUseCase>();
        services.AddScoped<IUseCase<GetBacktestTaskByIdQuery, Result<BacktestTaskDto>>, GetBacktestTaskByIdUseCase>();
        services.AddScoped<IUseCase<CancelBacktestCommand, Result>, CancelBacktestUseCase>();
        services.AddScoped<IUseCase<GetBacktestAnalysisPageQuery, Result<BacktestAnalysisPageDto>>, GetBacktestAnalysisPageUseCase>();
        services.AddScoped<IUseCase<GetBacktestAnalysisAllQuery, Result<BacktestKlineAnalysis[]>>, GetBacktestAnalysisAllUseCase>();
        services.AddScoped<IUseCase<GetBacktestAnalysisCountQuery, Result<int>>, GetBacktestAnalysisCountUseCase>();

        // ── System ──
        services.AddScoped<IUseCase<GetExchangeStatusQuery, Result<List<ExchangeStatusDto>>>, GetExchangeStatusUseCase>();
        services.AddScoped<IUseCase<EmergencyStopCommand, Result<EmergencyStopResultDto>>, EmergencyStopUseCase>();
        services.AddScoped<IUseCase<GetSystemLogsQuery, Result<List<SystemLogEntryDto>>>, GetSystemLogsUseCase>();

        // ── Strategies ──
        services.AddScoped<IUseCase<GetStrategiesQuery, Result<List<StrategyDto>>>, GetStrategiesUseCase>();
        services.AddScoped<IUseCase<GetStrategyByIdQuery, Result<StrategyDto>>, GetStrategyByIdUseCase>();
        services.AddScoped<IUseCase<CreateStrategyCommand, Result<StrategyDto>>, CreateStrategyUseCase>();
        services.AddScoped<IUseCase<UpdateStrategyCommand, Result<StrategyDto>>, UpdateStrategyUseCase>();
        services.AddScoped<IUseCase<DeleteStrategyCommand, Result>, DeleteStrategyUseCase>();

        // ── StrategyBindings ──
        services.AddScoped<IUseCase<GetBindingsQuery, Result<List<BindingDto>>>, GetBindingsUseCase>();
        services.AddScoped<IUseCase<GetBindingByIdQuery, Result<BindingDto>>, GetBindingByIdUseCase>();
        services.AddScoped<IUseCase<CreateBindingCommand, Result<BindingDto>>, CreateBindingUseCase>();
        services.AddScoped<IUseCase<UpdateBindingCommand, Result<BindingDto>>, UpdateBindingUseCase>();
        services.AddScoped<IUseCase<DeleteBindingCommand, Result>, DeleteBindingUseCase>();
        services.AddScoped<IUseCase<ActivateBindingCommand, Result<BindingDto>>, ActivateBindingUseCase>();
        services.AddScoped<IUseCase<DeactivateBindingCommand, Result<BindingDto>>, DeactivateBindingUseCase>();

        // ── Exchanges ──
        services.AddScoped<IUseCase<GetExchangesQuery, Result<List<ExchangeDto>>>, GetExchangesUseCase>();
        services.AddScoped<IUseCase<GetExchangeByIdQuery, Result<ExchangeDto>>, GetExchangeByIdUseCase>();
        services.AddScoped<IUseCase<CreateExchangeCommand, Result<ExchangeDto>>, CreateExchangeUseCase>();
        services.AddScoped<IUseCase<UpdateExchangeCommand, Result<ExchangeDto>>, UpdateExchangeUseCase>();
        services.AddScoped<IUseCase<DeleteExchangeCommand, Result>, DeleteExchangeUseCase>();
        services.AddScoped<IUseCase<TestExchangeCommand, Result<ExchangeTestResultDto>>, TestExchangeUseCase>();
        services.AddScoped<IUseCase<GetExchangeAssetsCommand, Result<List<ExchangeAssetDto>>>, GetExchangeAssetsUseCase>();
        services.AddScoped<IUseCase<GetExchangePairsCommand, Result<List<ExchangePairDto>>>, GetExchangePairsUseCase>();
        services.AddScoped<IUseCase<GetExchangeOrdersQuery, Result<PagedExchangeOrderDto>>, GetExchangeOrdersUseCase>();
        services.AddScoped<IUseCase<ToggleExchangeCommand, Result>, ToggleExchangeUseCase>();

        // ── Users ──
        services.AddScoped<IUseCase<GetUsersQuery, Result<List<UserDto>>>, GetUsersUseCase>();
        services.AddScoped<IUseCase<GetUserByIdQuery, Result<UserDto>>, GetUserByIdUseCase>();
        services.AddScoped<IUseCase<UpdateUserCommand, Result>, UpdateUserUseCase>();
        services.AddScoped<IUseCase<DeleteUserCommand, Result>, DeleteUserUseCase>();
        services.AddScoped<IUseCase<UpdateUserRoleCommand, Result>, UpdateUserRoleUseCase>();

        // ── Auth ──
        services.AddScoped<IUseCase<LoginCommand, Result<AuthResultDto>>, LoginUseCase>();
        services.AddScoped<IUseCase<RefreshTokenCommand, Result<AuthResultDto>>, RefreshTokenUseCase>();
        services.AddScoped<IUseCase<GetCurrentUserQuery, Result<AuthResultDto>>, GetCurrentUserUseCase>();

        // ── AuditLogs ──
        services.AddScoped<IUseCase<GetAuditLogsQuery, Result<List<AuditLogDto>>>, GetAuditLogsUseCase>();
        services.AddScoped<IUseCase<GetAuditLogsCountQuery, Result<int>>, GetAuditLogsCountUseCase>();

        // ── Settings ──
        services.AddScoped<IUseCase<GetSettingsQuery, Result<Dictionary<string, string>>>, GetSettingsUseCase>();
        services.AddScoped<IUseCase<UpdateSettingCommand, Result>, UpdateSettingUseCase>();

        // ── Notifications ──
        services.AddScoped<IUseCase<GetNotificationChannelsQuery, Result<List<NotificationChannelDto>>>, GetChannelsUseCase>();
        services.AddScoped<IUseCase<CreateNotificationChannelCommand, Result<NotificationChannelDto>>, CreateChannelUseCase>();
        services.AddScoped<IUseCase<UpdateChannelStatusCommand, Result>, UpdateChannelStatusUseCase>();
        services.AddScoped<IUseCase<TestNotificationChannelCommand, Result>, TestChannelUseCase>();

        // ═══ 应用服务 ═══
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISetupService, SetupService>();
        services.AddScoped<ISystemService, SystemService>();

        return services;
    }
}
