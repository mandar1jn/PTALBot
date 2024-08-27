using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PTALBot.Commands
{
    internal interface ICommand
    {

        string GetName();
        string GetDescription();

        SlashCommandOptionBuilder[]? GetOptions();

        Task Execute(SocketSlashCommand command);

    }
}
