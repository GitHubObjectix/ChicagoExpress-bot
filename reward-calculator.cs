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

        public float CalculateReward(GameState previous, GameState next, int rewardedPlayerIndex)
        {
            float reward = 0.0f;
            float opponentRewards = 0.0f;

            reward = EstimatePlayerValue(next, rewardedPlayerIndex) - EstimatePlayerValue(previous, rewardedPlayerIndex);


            int playerCount = Math.Min(previous.Players.Count, next.Players.Count);
            for (int i = 0; i < playerCount; ++i)
            {
                if (i == rewardedPlayerIndex)
                    continue;

                opponentRewards += EstimatePlayerValue(next, i) - EstimatePlayerValue(previous, i);
            }

            opponentRewards /= playerCount - 1;
            reward -= 0.5f * opponentRewards;

            // TODO! add a reward bonus for an auction when share was bought at reasonable price

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
