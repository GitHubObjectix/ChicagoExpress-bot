using Microsoft.Playwright;
using System.Text.RegularExpressions;
using static Google.Protobuf.Reflection.SourceCodeInfo.Types;

namespace chicago_express_bot
{
    public abstract class PreparationDecision
    {
        public abstract string GetPrompt();
        internal abstract Task<bool> MakeDecision(GameFrontendHandler gameFrontendHandler, string? decision);
    }

    internal class ChoosePlayerDecision : PreparationDecision
    {
        private ILocator playerButtons;
        private int numPlayers;
        private int selectedPlayer = -1;

        public ChoosePlayerDecision(ILocator playerButtons, int numPlayers)
        {
            this.playerButtons = playerButtons;
            this.numPlayers = numPlayers;
        }

        public override string GetPrompt()
        {
            return $"Found {numPlayers} players. Which player do you want me to play?";
        }

        internal override async Task<bool> MakeDecision(GameFrontendHandler gameFrontendHandler, string? decision)
        {
            bool ret = false;

            if (int.TryParse(decision, out selectedPlayer) && selectedPlayer >= 0 && selectedPlayer < numPlayers)
            {
                var playerButton = playerButtons.Nth(selectedPlayer);
                string? playerName = await playerButton.TextContentAsync();

                gameFrontendHandler.playerButton = playerButton;
                gameFrontendHandler.numPlayers = numPlayers;
                gameFrontendHandler.playerName = playerName == null ? string.Empty : playerName!;
                gameFrontendHandler.neededDecision = null;
                ret = true;
            }

            return ret;
        }
    }

    public sealed class GameFrontendHandler
    {
        private IPage gamePage;
        private IFrameLocator uiFrame;
        internal PreparationDecision? neededDecision = null;

        // the button of the player to play for in a local game
        internal ILocator? playerButton = null;

        internal int numPlayers = 0;
        internal string playerName = string.Empty;

        private GameFrontendHandler(IPage gamePage)
        {
            this.gamePage = gamePage;
            this.uiFrame = gamePage.FrameLocator("#ui");
        }

        public static async Task<GameFrontendHandler> Initialize(IPage gamePage)
        {
            GameFrontendHandler gameFrontendHandler = new GameFrontendHandler(gamePage);

            await gameFrontendHandler.DetectGameModus();

            return gameFrontendHandler;
        }

        public IFrameLocator GetLocator()
        {
            return uiFrame;
        }

        public PreparationDecision? NeedsDecision()
        {
            return neededDecision;
        }

        public async Task<bool> MakeDecision(string? decision)
        {
            return neededDecision != null && await neededDecision.MakeDecision(this, decision);
        }

        public bool ReadyToPlay()
        {
            return playerButton != null;
        }

        public int GetNumPlayers()
        {
            return numPlayers;
        }

        public string GetPlayerName()
        {
            return playerName;
        }

        public async Task<bool> IsGameStarted()
        {
            return await uiFrame.Locator("div[class='player-controls *']").CountAsync() > 0;
        }

        public async Task<bool> ItsMyTurn()
        {
            bool itsMyTurn = false;

            if (playerButton != null)
            {
                // local game
                string? style = await playerButton!.GetAttributeAsync("style");
                itsMyTurn = style?.Contains("2px solid black") == true;
            }
            else
            {
                // server game
                itsMyTurn = await uiFrame.Locator("div[class='player-controls *'] form").CountAsync() > 0;
            }

            // wait until player-controls are visible to ensure that
            // all other elements are visible so that game state can be scraped consistently
            // do this independently of whether its the bots turn to be able to detect announcements
            var playerControls = uiFrame.Locator("div[class='player-controls *']");

            await playerControls.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 2000
            });

            return itsMyTurn;
        }

        public async Task<List<string>> GetPossibleActions()
        {
            List<string> possibleActions = new List<string>();

            // first check if its the bot's turn
            // (basically are the player-controls visible and do they have actions to choose from)
            if (await ItsMyTurn())
            {
                var actionForms = uiFrame.Locator("div[class='player-controls *'] form");
                var actionCount = await actionForms!.CountAsync();

                for (int i = 0; i < actionCount; ++i)
                {
                    var actionClassName = await actionForms.Nth(i).GetAttributeAsync("class");
                    var actionName = Regex.Match(actionClassName!, @"(?:^|\s)action\s+([^\s]+)");

                    if (actionName.Success)
                    {
                        possibleActions.Add(actionName.Groups[1].Value);
                    }
                }
            }

            return possibleActions;
        }

        // Detects whether the game runs in a local setup by checking if player buttons exist
        private async Task DetectGameModus()
        {
            // Detection algorithm incl. decisions
            // 1. Detect if player buttons exist
            // 2. If player buttons exist, decide which player to play
            // 3. If player buttons not exist, decide if remote play is expected
            // 4. If remote play is expected, determine 
            // 5. If remote play is not expected, exit

            var buttons = gamePage.Locator("button.player");
            var playerCount = await buttons.CountAsync();

            if (playerCount > 0)
            {
                neededDecision = new ChoosePlayerDecision(buttons, playerCount);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public async Task<List<CandidateAction>> GetActionGaugeCandidates()
        {
            List<CandidateAction> candidateActions = new List<CandidateAction>();

            var selectableActionGauges = uiFrame.Locator("div[id$='-gauge'].clickable.selectable");
            int selectableActionCount = await selectableActionGauges.CountAsync();

            for (int i = 0; i < selectableActionCount; i++)
            {
                string? id = await selectableActionGauges.Nth(i).GetAttributeAsync("id");

                if (id is not null)
                {
                    string action = id[..^"-gauge".Length];

                    if (action == "auction")
                    {
                        candidateActions.Add(new SelectGaugeAction(ActionType.SelectGauge, GaugeIndex: 0));
                    }
                    else if (action == "development")
                    {
                        candidateActions.Add(new SelectGaugeAction(ActionType.SelectGauge, GaugeIndex: 1));
                    }
                    else if (action == "route-construction")
                    {
                        candidateActions.Add(new SelectGaugeAction(ActionType.SelectGauge, GaugeIndex: 2));
                    }
                    else
                    {
                        throw new NotSupportedException("unexpected action gauge");
                    }
                }
            }
            return candidateActions;
        }

        public async Task<List<CandidateAction>> GetAuctionCandidates(DecisionContext decisionContext)
        {
            List<CandidateAction> candidateActions = new List<CandidateAction>();

            var amountInput = uiFrame.Locator("div[class='selection amount'] input[name='amount']");
            string? min = await amountInput.GetAttributeAsync("min");
            string? max = await amountInput.GetAttributeAsync("max");
            string? value = await amountInput.GetAttributeAsync("value");

            int minValue = int.Parse(min!);
            int maxValue = int.Parse(max!);
            int currentValue = int.Parse(value!);

            for (int v = currentValue; v <= maxValue; ++v)
            {
                candidateActions.Add(new BidAction(ActionType.Bid, BidAmount: v));
            }

            candidateActions.Add(new PassAction(ActionType.Pass));

            return candidateActions;
        }

        public async Task<List<CandidateAction>> GetCompanySelectionCandidates(DecisionContext decisionContext, bool isAuction)
        {
            List<CandidateAction> candidateActions = new List<CandidateAction>();

            var companies = uiFrame.Locator("div.Space.RailwayCompany:has(div.company-panel.highlight)");

            int count = await companies.CountAsync();

            for (int i = 0; i < count; i++)
            {
                string? id = await companies.Nth(i).GetAttributeAsync("id");

                int companyIndex = Array.FindIndex(ChicagoExpressSetup.RailwayCompanies, company => company.Id == id);
                if (companyIndex < 0)
                {
                    throw new NotSupportedException($"unknown railway company id: {id}");
                }

                ActionType actionType = isAuction
                    ? ActionType.SelectAuctionCompany
                    : ActionType.SelectConstructionCompany;

                candidateActions.Add(new SelectCompanyAction(
                    isAuction ? ActionType.SelectAuctionCompany : ActionType.SelectConstructionCompany,
                    CompanyIndex: companyIndex));
            }

            candidateActions.Add(new SkipAction(ActionType.Skip));

            return candidateActions;
        }

        public async Task<List<CandidateAction>> GetFieldSelectionCandidates(DecisionContext decisionContext, bool isDevelopment)
        {
            List<CandidateAction> candidateActions = new List<CandidateAction>();

            var highlightedFields = uiFrame.Locator("div[id^='hex-']:has(> div.hexagonal-field-inner.highlight)");
            int count = await highlightedFields.CountAsync();

            for (int i = 0; i < count; i++)
            {
                var hex = highlightedFields.Nth(i);
                string? id = await hex.GetAttributeAsync("id");

                var match = System.Text.RegularExpressions.Regex.Match(
                    id ?? "",
                    @"^hex-(\d+)-(\d+)$"
                );

                if (match.Success)
                {
                    int row = int.Parse(match.Groups[1].Value);
                    int column = int.Parse(match.Groups[2].Value);

                    candidateActions.Add(new SelectFieldAction(
                        isDevelopment ? ActionType.DevelopHex : ActionType.ConstructHex,
                        RowIndex: row, ColumnIndex: column));
                }
            }

            candidateActions.Add(new SkipAction(ActionType.Skip));

            return candidateActions;
        }

        public async Task<bool> IsPopupVisible(int timeout)
        {
            bool isVisible = false;

            var announcement = uiFrame.Locator("div#announcement.modal-popup");
            try
            {
                await announcement.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = timeout
                });

                isVisible = true;
            }
            catch (TimeoutException)
            {
                isVisible = false;
            }

            return isVisible;
        }
    }
}
