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

        public float CalculateReward(GameState previous, GameState next)
        {
            return (EstimatePlayerValue(next) - EstimatePlayerValue(previous)) / rewardScale;
        }

        private float EstimatePlayerValue(GameState gameState)
        {
            float playerValue = gameState.CurrentPlayerCash;

            foreach (var (company, c) in ChicagoExpressSetup.RailwayCompanies.Select(
                    (company, index) => (company, index)))
            {
                if (company.Stocks - gameState.Companies[c].SharesAvailable > 0)
                {
                    int incomePerShare = (int)Math.Ceiling((double)gameState.Companies[c].Income / (double)(company.Stocks - gameState.Companies[c].SharesAvailable));
                    playerValue += gameState.Players[gameState.CurrentPlayer].Shares[c] * incomePerShare;
                }
            }

            return playerValue;
        }
    }
}
