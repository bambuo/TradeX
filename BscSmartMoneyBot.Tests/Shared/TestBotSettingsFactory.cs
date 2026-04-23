using BscSmartMoneyBot.Configuration;

namespace BscSmartMoneyBot.Tests.Shared;

internal static class TestBotSettingsFactory
{
    public static BotSettings Create(string? stateFilePath = null, string? backupDirectory = null)
    {
        var settings = new BotSettings();

        if (!string.IsNullOrWhiteSpace(stateFilePath))
        {
            settings.StateFilePath = stateFilePath;
        }

        if (!string.IsNullOrWhiteSpace(backupDirectory))
        {
            settings.BackupDirectory = backupDirectory;
        }

        return settings;
    }
}
