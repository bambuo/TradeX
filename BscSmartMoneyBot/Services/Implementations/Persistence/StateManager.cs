using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Clients;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace BscSmartMoneyBot.Services.Implementations.Persistence;

public class StateManager(
    ILogger<StateManager> logger,
    OnchainOSClient onchainOS,
    IOptions<BotSettings> settingsOptions) : IStateManager
{
    private const string StateVersion = "1.0";
    private const int BackupRetentionDays = 7;

    private readonly BotSettings _settings = settingsOptions.Value;
    private readonly string _stateFilePath = ResolvePath(settingsOptions.Value.StateFilePath);
    private readonly string _backupDir = ResolvePath(settingsOptions.Value.BackupDirectory);
    private int _pathsReady;

    public async Task<BotState> LoadStateAsync(CancellationToken ct)
    {
        try
        {
            EnsureStorageReady();

            if (!File.Exists(_stateFilePath))
            {
                logger.LogInformation("状态文件不存在，创建新状态");
                return new BotState();
            }

            var json = await File.ReadAllTextAsync(_stateFilePath, ct);
            var state = JsonConvert.DeserializeObject<BotState>(json);

            if (state == null)
            {
                logger.LogWarning("状态文件损坏，创建新状态");
                return new BotState();
            }

            await FixLegacyStateAsync(state);
            logger.LogInformation("状态加载成功: {SeenSignals}个已见信号, {Positions}个持仓",
                state.SeenSignals.Count, state.OpenPositions.Count);

            return state;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载状态失败");
            return new BotState();
        }
    }

    public async Task SaveStateAsync(BotState state, CancellationToken ct)
    {
        try
        {
            EnsureStorageReady();

            state.LastUpdate = DateTime.UtcNow;
            state.Version = StateVersion;

            var json = JsonConvert.SerializeObject(state, Formatting.Indented);
            var tempFile = _stateFilePath + ".tmp";
            await File.WriteAllTextAsync(tempFile, json, ct);
            File.Move(tempFile, _stateFilePath, true);

            logger.LogDebug("状态保存成功");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "保存状态失败");
        }
    }

    public Task BackupStateAsync(CancellationToken ct)
    {
        try
        {
            EnsureStorageReady();
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(_stateFilePath))
            {
                return Task.CompletedTask;
            }

            var backupFile = Path.Combine(_backupDir, $"state_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            File.Copy(_stateFilePath, backupFile, true);

            var oldBackups = Directory.GetFiles(_backupDir, "state_backup_*.json")
                .Select(file => new FileInfo(file))
                .Where(file => file.CreationTime < DateTime.UtcNow.AddDays(-BackupRetentionDays));

            foreach (var backup in oldBackups)
            {
                backup.Delete();
            }

            logger.LogInformation("状态备份完成: {BackupFile}", backupFile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "状态备份失败");
        }

        return Task.CompletedTask;
    }

    private async Task FixLegacyStateAsync(BotState state)
    {
        foreach (var position in state.OpenPositions.Values)
        {
            if (position.BuyFeeUSD == 0 && !string.IsNullOrEmpty(position.BuyTxHash))
            {
                logger.LogInformation("修复旧持仓手续费: {Token}", position.TokenSymbol);
                await TryBackfillFeesAsync(position);
            }
        }
    }

    private async Task TryBackfillFeesAsync(Position position)
    {
        try
        {
            var txDetail = await onchainOS.GetTransactionDetailAsync(
                _settings.Signals.Chain,
                position.BuyTxHash,
                CancellationToken.None);

            if (txDetail.ServiceChargeUsd <= 0)
            {
                return;
            }

            position.BuyFeeUSD = txDetail.ServiceChargeUsd;
            position.EstSellFeeUSD = txDetail.ServiceChargeUsd * 1.2m;
            logger.LogInformation("手续费修复成功: {Token} ${Fee}", position.TokenSymbol, position.BuyFeeUSD);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "修复手续费失败: {Token}", position.TokenSymbol);
        }
    }

    private void EnsureStorageReady()
    {
        if (Interlocked.Exchange(ref _pathsReady, 1) == 1)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
        Directory.CreateDirectory(_backupDir);
        logger.LogDebug("状态文件路径: {Path}", _stateFilePath);
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
