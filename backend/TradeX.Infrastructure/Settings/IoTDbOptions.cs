namespace TradeX.Infrastructure.Settings;

public class IoTDbOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6667;
    public bool AutoSetup { get; set; } = false;
    public string ContainerName { get; set; } = "tradex-iotdb";
}
