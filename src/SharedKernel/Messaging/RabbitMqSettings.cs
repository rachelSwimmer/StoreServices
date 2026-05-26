namespace SharedKernel.Messaging;

/// <summary>
/// Connection settings for RabbitMQ, bound from the "RabbitMq" configuration
/// section (appsettings.json or RabbitMq__* environment variables in Docker).
/// </summary>
public class RabbitMqSettings
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}
