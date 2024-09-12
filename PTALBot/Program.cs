using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PTALBot
{
    public class Program
    {
        private static readonly IConfiguration configuration = new ConfigurationBuilder().AddJsonFile("appconfig.json").Build();
        private static IServiceProvider services = null!;

        private static readonly DiscordSocketConfig socketConfiguration = new DiscordSocketConfig()
        {
            GatewayIntents = GatewayIntents.None
        };

        public static async Task Main()
        {
            services = new ServiceCollection()
                .AddSingleton(configuration)
                .AddSingleton(socketConfiguration)
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
                .AddSingleton<InteractionHandler>()
                .BuildServiceProvider();

            DiscordSocketClient client  = services.GetRequiredService<DiscordSocketClient>();

            client.Log += Log;

            await services.GetRequiredService<InteractionHandler>()
                .InitializeAsync();

            await client.LoginAsync(TokenType.Bot, configuration["token"]);
            await client.StartAsync();

            await Task.Delay(-1);
        }

        private static Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
