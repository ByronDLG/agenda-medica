using System.ComponentModel.DataAnnotations;
using System.Data;
using AgendaMedica.Api.Data;
using AgendaMedica.Api.Domain;
using AgendaMedica.Api.Domain.Identity;
using AgendaMedica.Api.Domain.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AgendaMedica.Api.Infrastructure;

public static class FirstAdministratorBootstrapper
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AgendaDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await database.Database.CanConnectAsync())
        {
            Console.Error.WriteLine("No se pudo conectar a SQL Server.");
            return 1;
        }

        if ((await database.Database.GetPendingMigrationsAsync()).Any())
        {
            Console.Error.WriteLine("Aplica las migraciones pendientes antes de crear el administrador.");
            return 1;
        }

        Console.Write("Nombre del consultorio: ");
        var organizationName = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(organizationName) || organizationName.Length > 120)
        {
            Console.Error.WriteLine("El nombre es obligatorio y no debe exceder 120 caracteres.");
            return 1;
        }

        Console.Write("Correo del administrador: ");
        var email = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(email) ||
            email.Length > 254 ||
            !new EmailAddressAttribute().IsValid(email))
        {
            Console.Error.WriteLine("Ingresa una dirección de correo válida.");
            return 1;
        }

        Console.Write("Contraseña inicial (mínimo 12 caracteres, con mayúscula, minúscula, número y símbolo): ");
        var password = ReadPassword();
        Console.WriteLine();
        Console.Write("Confirma la contraseña: ");
        var confirmation = ReadPassword();
        Console.WriteLine();
        if (password.Length is < 12 or > 128 || password != confirmation)
        {
            Console.Error.WriteLine("La contraseña no cumple el mínimo o las confirmaciones difieren.");
            return 1;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        if (!await AcquireBootstrapLockAsync(database, transaction))
        {
            Console.Error.WriteLine("No se pudo bloquear el aprovisionamiento inicial; inténtalo de nuevo.");
            return 1;
        }

        if (await userManager.Users.AnyAsync())
        {
            Console.Error.WriteLine("Ya existe al menos una cuenta; el aprovisionamiento inicial está cerrado.");
            return 1;
        }

        var organization = Organization.Create(organizationName);
        database.Organizations.Add(organization);
        await database.SaveChangesAsync();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
        };
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync();
            Console.Error.WriteLine(
                $"No se pudo crear el administrador: {string.Join("; ", createResult.Errors.Select(error => error.Description))}");
            return 1;
        }

        database.OrganizationMemberships.Add(
            OrganizationMembership.Create(user, organization, OrganizationRole.ClinicAdmin));
        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        Console.WriteLine($"Administrador creado para el consultorio {organization.Id}.");
        return 0;
    }

    private static async Task<bool> AcquireBootstrapLockAsync(
        AgendaDbContext database,
        IDbContextTransaction transaction)
    {
        var connection = database.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = N'AgendaMedica:FirstAdministratorBootstrap',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;
            SELECT @result;
            """;

        var result = await command.ExecuteScalarAsync();
        return result is int lockResult && lockResult >= 0;
    }

    private static string ReadPassword()
    {
        var password = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Count > 0)
                {
                    password.RemoveAt(password.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Add(key.KeyChar);
            }
        }

        return new string(password.ToArray());
    }
}
