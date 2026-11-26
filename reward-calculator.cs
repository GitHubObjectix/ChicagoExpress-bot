using System;
using System.Collections.Generic;
using System.Text;

namespace chicago_express_bot
{
    public class RewardCalculator
    {
        private float rewardScale;

        public RewardCalculator(float rewardScale = 100f)
        {
            this.rewardScale = rewardScale;
        }

        /**
         * @isTerminal: true if game is finished after the move
         */
        public float CalculateReward(GameState previousGameState, CandidateAction chosenAction, GameState gameState, bool isTerminal, int rewardedPlayerIndex)
        {
            float reward = 0.0f;
            float opponentRewards = 0.0f;

            // calculate the main reward by estimate player value change 

            reward = EstimatePlayerValue(gameState, rewardedPlayerIndex) - EstimatePlayerValue(previousGameState, rewardedPlayerIndex);

            // calculate average opponent player value change and reduce main reward

            int playerCount = Math.Min(previousGameState.Players.Count, gameState.Players.Count);
            for (int i = 0; i < playerCount; ++i)
            {
                if (i == rewardedPlayerIndex)
                    continue;

                opponentRewards += EstimatePlayerValue(gameState, i) - EstimatePlayerValue(previousGameState, i);
            }

            opponentRewards /= playerCount - 1;
            reward -= 0.5f * opponentRewards;

            // TODO! add a reward bonus for an auction when share was bought at reasonable price

            switch (chosenAction.Type)
            {
                case ActionType.SelectGauge:
                    break;
                case ActionType.Skip:
                    break;
                case ActionType.SelectAuctionCompany:
                    break;
                case ActionType.Bid:
                    // reward bonus if share was bought at reasonable price
                    break;
                case ActionType.Pass:
                    break;
                case ActionType.DevelopHex:
                    {
                        var selectFieldAction = (SelectFieldAction)chosenAction;
                        reward += ChicagoExpressSetup.GetCostAndIncomeConfiguration(selectFieldAction.RowIndex, selectFieldAction.ColumnIndex).IncomeIfDeveloped * 0.05f * rewardScale;
                    }
                    break;
                case ActionType.SelectConstructionCompany:
                    break;
                case ActionType.ConstructHex:
                    {
                        var selectFieldAction = (SelectFieldAction)chosenAction;
                        reward += ChicagoExpressSetup.GetCostAndIncomeConfiguration(selectFieldAction.RowIndex, selectFieldAction.ColumnIndex).Income * 0.05f * rewardScale;
                    }
                    break;
            }

            // if game ended after chosen action calculate a reward bonus/malus
            if (isTerminal)
            {
                var winningPlayerIndexes = gameState.Players
                        .Select((p, i) => new { Player = p, Index = i })
                        .Where(x => x.Player.PlayerCash == gameState.Players.Max(p => p.PlayerCash))
                        .Select(x => x.Index).ToList();

                if (winningPlayerIndexes.Contains(rewardedPlayerIndex))
                {
                    reward += (winningPlayerIndexes.Count > 1 ? 0.5f : 1.0f) * rewardScale;
                }
                else
                {
                    reward += -1.0f * rewardScale;
                }
            }

            return reward / rewardScale;
        }

        private float EstimatePlayerValue(GameState gameState, int playerIndex)
        {
            float playerValue = gameState.Players[playerIndex].PlayerCash;

            foreach (var (company, c) in ChicagoExpressSetup.RailwayCompanies.Select(
                    (company, index) => (company, index)))
            {
                var numberOfHeldShares = company.Stocks - gameState.Companies[c].SharesAvailable;

                if (numberOfHeldShares > 0)
                {
                    int incomePerShare = (int)Math.Ceiling((double)gameState.Companies[c].Income / (double)numberOfHeldShares);

                    // add estimated value for upcoming dividends, to reward holding of shares
                    int numUpcomingDividends = ChicagoExpressSetup.IndustrialCityTrackCells[0].Length - gameState.Board.IndustrialTrackPositions[0];
                    for (int dividendPhaseIndex = 0; dividendPhaseIndex < numUpcomingDividends; ++dividendPhaseIndex)
                    {
                        playerValue += gameState.Players[playerIndex].Shares[c] * incomePerShare;
                    }
                }
            }

            return playerValue;
        }
    }
}
