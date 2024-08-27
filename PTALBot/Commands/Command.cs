using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PTALBot.Commands
{
    internal class Command : ICommand
    {
        public virtual string GetName() => throw new NotImplementedException();
        public virtual string GetDescription() => throw new NotImplementedException();
        public virtual SlashCommandOptionBuilder[]? GetOptions() => null;

        public virtual Task Execute(SocketSlashCommand command)
        {
            throw new NotImplementedException();
        }
    }
}
