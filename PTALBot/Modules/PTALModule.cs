﻿using Discord;
using Discord.Interactions;
using Discord.Utils;
using Discord.WebSocket;
using Octokit;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;

namespace PTALBot.Modules
{
    public struct GeneratedMessage
    {
        public string text;
        public Embed embed;
        public MessageComponent components;
    }
    enum PRState
    {
        PENDING,
        REVIEWED,
        CHANGES_REQUESTED,
        APPROVED,
        MERGED,
        CLOSED,
        DRAFT
    }
    public partial class PTALModule : InteractionModuleBase<SocketInteractionContext>
    {
        private static string PRStateToReviewText(PRState state) => state switch
        {
            PRState.PENDING => "⏳ Awaiting Review",
            PRState.REVIEWED => "💬 Reviewed",
            PRState.CHANGES_REQUESTED => "⭕ Blocked",
            PRState.APPROVED => "✅ Approved",
            PRState.MERGED => "🟣 Merged",
            PRState.CLOSED => "🗑️ Closed",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        private static uint PRStateToColor(PRState state) => state switch
        {
            PRState.PENDING => 0x3498db,
            PRState.REVIEWED => 0xf1c40f,
            PRState.CHANGES_REQUESTED => 0xed4245,
            PRState.APPROVED => 0x57f287,
            PRState.MERGED => 0xa590d4,
            PRState.CLOSED => 0x95a5a6,
            PRState.DRAFT => 0x6e7681,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        private static readonly AllowedMentions allowedMentions = new(AllowedMentionTypes.Roles | AllowedMentionTypes.Users);

        private static readonly GitHubClient githubClient = new(new ProductHeaderValue("mandar1jn-ptal-bot"));

        [GeneratedRegex("((https:\\/\\/)?github\\.com\\/)?(?<ORGANISATION>[\\w\\.-]+)\\/(?<REPOSITORY>[\\w\\.-]+)\\/pull\\/(?<NUMBER>\\d+)")]
        private static partial Regex GithubURLRegex();

        [GeneratedRegex("(?<ORGANISATION>[\\w\\.-]+)\\/(?<REPOSITORY>[\\w\\.-]+)#(?<NUMBER>\\d+)")]
        private static partial Regex SimplifiedRegex();

        public async Task<GeneratedMessage?> GenerateResponse(bool reload,
            string organisation,
            string repository,
            int number,
            string description,
            string originalAuthorName,
            string originalAuthorAvatarURL,
            string deployment)
        {
            #region Github requests
            PullRequest pr;
            try
            {
                pr = await githubClient.PullRequest.Get(organisation, repository, number);
            }
            catch
            {
                await RespondAsync(text: "Failed to retrieve the pull request from github. Are you sure it exists?", ephemeral: true);
                return null;
            }

            await DeferAsync();

            IReadOnlyList<PullRequestReview> reviews;

            try
            {
                reviews = (await githubClient.PullRequest.Review.GetAll(organisation, repository, number)).Where(review => review.User.Id != pr.User.Id && review.User.Login.EndsWith("[bot]") != true && review.State.Value != PullRequestReviewState.Pending).ToList();
            }
            catch
            {
                await FollowupAsync(text: "Failed to retrieve the pull request reviews from github.", ephemeral: reload);
                return null;
            }
            #endregion

            EmbedFooterBuilder footer = new EmbedFooterBuilder()
                .WithIconUrl(Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl())
                .WithText($"{(reload ? "Updated" : "Requested")} by @{Context.User.Username}");

            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle(pr.Title)
                .WithUrl(pr.HtmlUrl)
                .WithFooter(footer)
                .WithCurrentTimestamp()
                .WithAuthor(originalAuthorName, iconUrl: originalAuthorAvatarURL)
                .AddField("Repository", $"[{organisation}/{repository}#{number}]({pr.HtmlUrl})");

            if (pr.State.Value == ItemState.Closed)
            {
                string state = pr.Merged ? "MERGED" : "CLOSED";
                embed.Title = $"[{state}] {embed.Title}";
            }
            else if(pr.Draft)
            {
                embed.Title = $"[DRAFT] {embed.Title}";
            }

            #region Handle reviews
            PRState prState = PRState.PENDING;

            string reviewText = "";

            if (reviews.Count > 0)
            {
                bool open = (pr.State.Value == ItemState.Open);

                List<long> uniqueIDs = []; 

                for (int i = 0; i < reviews.Count; i++)
                {
                    if(!uniqueIDs.Contains(reviews[i].User.Id))
                    {
                        uniqueIDs.Add(reviews[i].User.Id);
                    }
                }

                List<PullRequestReview> mainReviews = [];

                for(int i = 0; i < uniqueIDs.Count; i++)
                {
                    List<PullRequestReview> userReviews = reviews.Where(rev => rev.User.Id == uniqueIDs[i]).Where(rev => rev.State.Value != PullRequestReviewState.Dismissed && rev.State.Value != PullRequestReviewState.Pending)
                        .ToList();

                    PullRequestReview mainReview = userReviews.First();

                    for (int j = 1; j < userReviews.Count; j++)
                    {
                        PullRequestReview nextReview = userReviews[j];
                        switch(nextReview.State.Value)
                        {
                            case PullRequestReviewState.Approved:
                                if(mainReview.State.Value == PullRequestReviewState.Commented)
                                {
                                    mainReview = nextReview;
                                }
                                else if(mainReview.State.Value == PullRequestReviewState.ChangesRequested && nextReview.SubmittedAt > mainReview.SubmittedAt)
                                {
                                    mainReview = nextReview;
                                }
                                break;
                            case PullRequestReviewState.ChangesRequested:
                                if (mainReview.State.Value == PullRequestReviewState.Commented)
                                {
                                    mainReview = nextReview;
                                }
                                break;
                        }
                    }

                    mainReviews.Add(mainReview);
                }

                for (int i = 0; i < mainReviews.Count; i++)
                {
                    var review = mainReviews[i];

                    switch (review.State.Value)
                    {
                        case PullRequestReviewState.Approved:
                            if (open)
                            {
                                reviewText += $"[✅ {review.User.Login}]({review.User.HtmlUrl})\n";
                            }
                            else
                            {
                                reviewText += "✅";
                            }

                            if (pr.State.Value == ItemState.Open && prState != PRState.CHANGES_REQUESTED)
                            {
                                prState = PRState.APPROVED;
                            }
                            break;
                        case PullRequestReviewState.ChangesRequested:

                            if (open)
                            {
                                reviewText += $"[⭕ {review.User.Login}]({review.User.HtmlUrl})\n";
                            }
                            else
                            {
                                reviewText += "⭕";
                            }

                            if (pr.State.Value == ItemState.Open)
                            {
                                prState = PRState.CHANGES_REQUESTED;
                            }
                            break;
                        case PullRequestReviewState.Dismissed:
                        case PullRequestReviewState.Commented:
                            if (open)
                            {
                                reviewText += $"[💬 {review.User.Login}]({review.User.HtmlUrl})\n";
                            }
                            else
                            {
                                reviewText += "💬";
                            }

                            if (pr.State.Value == ItemState.Open && prState == PRState.PENDING)
                            {
                                prState = PRState.REVIEWED;
                            }
                            break;
                    }
                }
            }

            if (pr.State.Value == ItemState.Closed)
            {
                prState = pr.Merged ? PRState.MERGED : PRState.CLOSED;
            }

            embed.AddField("Status", PRStateToReviewText(prState));

            if(pr.State.Value == ItemState.Open && pr.Draft)
            {
                prState = PRState.DRAFT;
            }
            embed.WithColor(PRStateToColor(prState));

            if (reviewText != "")
            {
                embed.AddField("Reviews", reviewText);
            }
            #endregion

            ComponentBuilder componentBuilder = new();

            if(deployment != "")
            {
                ButtonBuilder deploymentButton = new ButtonBuilder()
                    .WithLabel("View deployment")
                    .WithUrl(deployment)
                    .WithStyle(ButtonStyle.Link);

                componentBuilder.WithButton(deploymentButton);
            }

            ButtonBuilder githubButton = new ButtonBuilder()
                .WithEmote(Emote.Parse("<:github:1277903480291594303>"))
                .WithLabel("View on Github")
                .WithUrl(pr.HtmlUrl)
                .WithStyle(ButtonStyle.Link);

            componentBuilder.WithButton(githubButton);

            ButtonBuilder fileButton = new ButtonBuilder()
                .WithEmote(Discord.Emoji.Parse("📁"))
                .WithLabel("Files")
                .WithUrl(pr.HtmlUrl + "/files")
                .WithStyle(ButtonStyle.Link);

            componentBuilder.WithButton(fileButton);

            if (prState != PRState.MERGED)
            {

                ButtonBuilder refreshButton = new ButtonBuilder()
                    .WithEmote(Discord.Emoji.Parse("🔁"))
                    .WithLabel("Refresh")
                    .WithStyle(ButtonStyle.Primary)
                    .WithCustomId("ptal:refresh");

                componentBuilder.WithButton(refreshButton);
            }

            string descriptionPrefix = "**PTAL** ";

            return new GeneratedMessage()
            {
                text = descriptionPrefix + description,
                embed = embed.Build(),
                components = componentBuilder.Build()
            };
        }


        [SlashCommand("ptal", "test")]
        public async Task PTALCommand(string github, string description = "", string deployment = "")
        {
            #region Github parsing;
            string organisation;
            string repository;
            int number;

            Match githubURLMatch = GithubURLRegex().Match(github);
            if (githubURLMatch.Success == true)
            {
                organisation = githubURLMatch.Groups.GetValueOrDefault("ORGANISATION")!.Value;
                repository = githubURLMatch.Groups.GetValueOrDefault("REPOSITORY")!.Value;
                number = int.Parse(githubURLMatch.Groups.GetValueOrDefault("NUMBER")!.Value);
            }
            else
            {
                Match simplifiedMatch = SimplifiedRegex().Match(github);
                if (simplifiedMatch.Success)
                {
                    organisation = simplifiedMatch.Groups.GetValueOrDefault("ORGANISATION")!.Value;
                    repository = simplifiedMatch.Groups.GetValueOrDefault("REPOSITORY")!.Value;
                    number = int.Parse(simplifiedMatch.Groups.GetValueOrDefault("NUMBER")!.Value);
                }
                else
                {
                    await RespondAsync(text: "Please provide a valid pull request. Use COMMAND for the correct format.", ephemeral: true);
                    return;
                }
            }
            #endregion

            if(!deployment.StartsWith("http://", StringComparison.CurrentCultureIgnoreCase) && !deployment.StartsWith("https://", StringComparison.CurrentCultureIgnoreCase))
            {
                await RespondAsync("A deployment URL must start with either 'http://' or 'https://'", ephemeral: true);
                return;
            }

            GeneratedMessage? generatedMessage = await GenerateResponse(false, organisation, repository, number, description,
                Context.User.DiscriminatorValue != 0 ? $"{Context.User.Username}#{Context.User.Discriminator}" : Context.User.Username,
                Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl(),
                deployment);

            if(generatedMessage.HasValue)
            {
                await FollowupAsync(text: generatedMessage.Value.text, embed: generatedMessage.Value.embed, components: generatedMessage.Value.components, allowedMentions: allowedMentions);
            }
        }

        [ComponentInteraction("ptal:refresh")]
        public async Task ReloadButton()
        {
            SocketUserMessage original = ((SocketMessageComponent)Context.Interaction).Message;

            IEmbed embed = original.Embeds.First();
            string url = embed.Url;

            ActionRowComponent actionRow = original.Components.First();

            IEnumerable<ButtonComponent> deployButtons = actionRow.Components
                .Where(component => component.Type == ComponentType.Button)
                .Select(button => (ButtonComponent)button)
                .Where(button => button.Label == "View deployment");

            string deployment = "";

            if(deployButtons.Any())
            {
                deployment = deployButtons.First().Url;
            }

            Match urlMatch = GithubURLRegex().Match(url);

            if(!urlMatch.Success)
            {
                throw new Exception("Somehow parsed an incorrect URL from the embed");
            }

            GeneratedMessage? generatedMessage = await GenerateResponse(true,
                urlMatch.Groups.GetValueOrDefault("ORGANISATION")!.Value,
                urlMatch.Groups.GetValueOrDefault("REPOSITORY")!.Value,
                int.Parse(urlMatch.Groups.GetValueOrDefault("NUMBER")!.Value),
                original.Content.Trim() == "**PTAL**"? "" : original.Content[9..],
                embed.Author!.Value.Name,
                embed.Author!.Value.IconUrl,
                deployment);

            if (generatedMessage.HasValue)
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = generatedMessage.Value.text;
                    properties.Embed = generatedMessage.Value.embed;
                    properties.Components = generatedMessage.Value.components;
                });
            }
        }
    }
}
