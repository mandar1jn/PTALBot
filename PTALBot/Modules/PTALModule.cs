using Discord;
using Discord.Interactions;
using Octokit;
using PTALBot.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PTALBot.Modules
{
    public partial class PTALModule : InteractionModuleBase<SocketInteractionContext>
    {

        public InteractionService Commands { get; set; }

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
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        private static GitHubClient githubClient = new GitHubClient(new ProductHeaderValue("my-cool-app"));
        [GeneratedRegex("((https:\\/\\/)?github\\.com\\/)?(?<ORGANISATION>[\\w\\.-]+)\\/(?<REPOSITORY>[\\w\\.-]+)\\/pull\\/(?<NUMBER>\\d+)")]
        private static partial Regex GithubURLRegex();
        [GeneratedRegex("(?<ORGANISATION>[\\w\\.-]+)\\/(?<REPOSITORY>[\\w\\.-]+)#(?<NUMBER>\\d+)")]
        private static partial Regex SimplifiedRegex();


        [SlashCommand("ptal", "test")]
        public async Task PTALCommand(string github, string description = "")
        {
            #region Github parsing;
            string organisation = "";
            string repository = "";
            int number = -1;

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

            #region Github requests
            PullRequest pr;
            try
            {
                pr = await githubClient.PullRequest.Get(organisation, repository, number);
            }
            catch
            {
                await RespondAsync(text: "Failed to retrieve the pull request from github. Are you sure it exists?", ephemeral: true);
                return;
            }

            await DeferAsync();

            IReadOnlyList<PullRequestReview> reviews;

            try
            {
                reviews = (await githubClient.PullRequest.Review.GetAll(organisation, repository, number)).Where(review => review.User.Id != pr.User.Id && review.User.Login.EndsWith("[bot]") != true && review.State.Value != PullRequestReviewState.Pending).ToList();
            }
            catch
            {
                await FollowupAsync(text: "Failed to retrieve the pull request reviews from github.");
                return;
            }
            #endregion

            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle(pr.Title)
                .WithUrl(pr.HtmlUrl)
                .WithCurrentTimestamp()
                .WithAuthor(Context.User)

                .AddField("Repository", $"[{organisation}/{repository}#{number}]({pr.HtmlUrl})");

            if (pr.State.Value == ItemState.Closed)
            {
                string state = pr.Merged ? "MERGED" : "CLOSED";
                embed.Title = $"[{state}] " + embed.Title;
            }

            #region Handle reviews
            PRState prState = PRState.PENDING;

            string reviewText = "";

            if (reviews.Count > 0)
            {
                bool open = (pr.State.Value == ItemState.Open);

                for (int i = 0; i < reviews.Count; i++)
                {
                    var review = reviews[i];

                    switch (review.State.Value)
                    {
                        case PullRequestReviewState.Approved:
                            if (open)
                            {
                                reviewText += $"[✅ {review.User.Login}]({review.User.HtmlUrl})\n";
                            }
                            else
                            {
                                reviewText += "✅\n";
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
                                reviewText += "⭕\n";
                            }

                            if (pr.State.Value == ItemState.Open)
                            {
                                prState = PRState.CHANGES_REQUESTED;
                            }
                            break;
                        case PullRequestReviewState.Dismissed:
                        case PullRequestReviewState.Commented:
                            bool skip = false;
                            for (int j = 0; j < reviews.Count; j++)
                            {
                                var checkReview = reviews[j];

                                if (checkReview.User.Id == review.User.Id && (review.State.Value == PullRequestReviewState.Approved || review.State.Value == PullRequestReviewState.ChangesRequested))
                                {
                                    skip = true;
                                    continue;
                                }

                                if (checkReview.User.Id == review.User.Id && i < j)
                                {
                                    skip = true;
                                    continue;
                                }
                            }

                            if (skip)
                            {
                                continue;
                            }

                            if (open)
                            {
                                reviewText += $"[💬 {review.User.Login}]({review.User.HtmlUrl})\n";
                            }
                            else
                            {
                                reviewText += "💬\n";
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
            embed.WithColor(PRStateToColor(prState));

            if (reviewText != "")
            {
                embed.AddField("Reviews", reviewText);
            }
            #endregion

            ComponentBuilder componentBuilder = new ComponentBuilder();

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
                    .WithCustomId("ptal-refresh");

                componentBuilder.WithButton(refreshButton);
            }

            string descriptionPrefix = "**PTAL** ";

            await FollowupAsync(text: descriptionPrefix + description, embed: embed.Build(), components: componentBuilder.Build());
        }
    }
}
