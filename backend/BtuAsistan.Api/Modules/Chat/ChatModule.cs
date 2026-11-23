using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Chat;

public static class ChatModule
{
    public static IServiceCollection AddChatModule(this IServiceCollection services, string aiServiceUrl)
    {
        services.AddOptions<ChatOptions>()
            .BindConfiguration(ChatOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IAnswerGenerator, AiServiceAnswerGenerator>((provider, client) =>
        {
            client.BaseAddress = new Uri(aiServiceUrl);

            // Only covers waiting for the answer to start; ChatService limits the whole answer.
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<ChatOptions>>().Value.GenerationTimeoutSeconds);
        });

        // Scoped: uses the request's AppDbContext.
        services.AddScoped<ChatService>();

        // One for the whole app: every answer in progress holds one of its permits.
        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<ChatOptions>>().Value;
            return new ChatConcurrency(new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = settings.MaxConcurrentAnswers,
                QueueLimit = settings.MaxQueuedAnswers,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));
        });

        services.AddHostedService<RetentionService>();

        return services;
    }
}
