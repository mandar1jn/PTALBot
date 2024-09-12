using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace PTALBot
{
    public class InteractionHandler(DiscordSocketClient client, InteractionService handler, IServiceProvider services, IConfiguration config)
    {
        private readonly DiscordSocketClient client = client;
        private readonly InteractionService handler = handler;
        private readonly IServiceProvider services = services;
        private readonly IConfiguration configuration = config;

        public async Task InitializeAsync()
        {
            client.Ready += OnReady;
            handler.Log += Log;

            await handler.AddModulesAsync(Assembly.GetExecutingAssembly(), services);

            client.InteractionCreated += HandleInteraction;

            handler.InteractionExecuted += HandleInteractionExecuted;
        }

        private async Task OnReady()
        {
            await handler.RegisterCommandsGloballyAsync();
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            try
            {
                var context = new SocketInteractionContext(client, interaction);

                var result = await handler.ExecuteCommandAsync(context, services);

                // Due to async nature of InteractionFramework, the result here may always be success.
                // That's why we also need to handle the InteractionExecuted event.
                if (!result.IsSuccess)
                {
                    switch (result.Error)
                    {
                        case InteractionCommandError.UnmetPrecondition:
                            // implement
                            break;
                        default:
                            break;
                    }
                }
            }
            catch
            {
                // Delete original response if an error occurs
                if (interaction.Type is InteractionType.ApplicationCommand)
                    await interaction.GetOriginalResponseAsync().ContinueWith(async (msg) => await msg.Result.DeleteAsync());
            }
        }

        private Task HandleInteractionExecuted(ICommandInfo info, IInteractionContext context, IResult result)
        {
            if (!result.IsSuccess)
            {
                switch (result.Error)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        // TODO: implement
                        break;
                    default:
                        break;
                }
            }

            return Task.CompletedTask;
        }

        private static Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());

            return Task.CompletedTask;
        }
    }
}
