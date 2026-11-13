using Microsoft.Data.SqlClient;

namespace AgendaMedica.Api.Infrastructure;

public static class SqlServerConnection
{
    public static string Create(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredConnection = configuration.GetConnectionString("AgendaMedica");
        if (!string.IsNullOrWhiteSpace(configuredConnection))
        {
            return configuredConnection;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Configure ConnectionStrings:AgendaMedica through a secret provider.");
        }

        var localConnection = new SqlConnectionStringBuilder
        {
            DataSource = configuration["Database:Server"] ?? @"(localdb)\MSSQLLocalDB",
            InitialCatalog = configuration["Database:Name"] ?? "AgendaMedicaDev",
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true,
        };

        return localConnection.ConnectionString;
    }
}
