using System.Text;
using BtuAsistan.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Modules.Identity;

/// <summary>
/// dotnet run --project backend/BtuAsistan.Api -- create-admin &lt;email&gt;
///
/// Runs on the server only, so there is no way over the internet to become an admin.
/// An existing account is made an admin; otherwise a new account is created with the
/// password typed at the prompt.
/// </summary>
public static class CreateAdminCommand
{
    public const string Name = "create-admin";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length != 2 || !args[1].Contains('@'))
        {
            Console.Error.WriteLine($"Usage: dotnet run --project backend/BtuAsistan.Api -- {Name} <email>");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var email = args[1].Trim();
        var normalizedEmail = User.NormalizeEmail(email);

        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        if (user is not null)
        {
            user.Role = UserRole.Admin;
            user.IsActive = true;
            await db.SaveChangesAsync();
            Console.WriteLine($"{user.Email} is now an admin; the password is unchanged.");
            return 0;
        }

        var password = ReadPassword("Password: ");
        if (!PasswordRules.IsValid(password))
        {
            Console.Error.WriteLine(
                $"The password must be {PasswordRules.MinLength} to {PasswordRules.MaxLength} characters.");
            return 1;
        }

        if (ReadPassword("Password again: ") != password)
        {
            Console.Error.WriteLine("The passwords do not match.");
            return 1;
        }

        user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Role = UserRole.Admin,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        Console.WriteLine($"Admin account created: {user.Email}");
        return 0;
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);

        // Piped input (scripts) has no keys to hide.
        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine() ?? string.Empty;
            Console.WriteLine();
            return line;
        }

        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }
}
