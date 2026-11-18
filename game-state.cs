using System;
using System.Collections.Generic;
using System.Text;

namespace chicago_express_bot
{
    // game states being scraped:
    // - per player
    //  - #shares held per company
    // - per company
    //  - #available trains 
    //  - #cash
    //  - #available shares
    //  - income
    // - action gauge states
    // - board state
    //  - per field
    //   - development possible
    //   - per company
    //    - construction of train possible
    //    - income increase
    //   - terrain type


    public sealed class GameState
    {
        public int CurrentPlayer { get; set; }
        public int CurrentPlayerCash { get; set; }

        public List<PlayerState> Players { get; set; } = new();
        public List<CompanyState> Companies { get; set; } = new();
        public List<BoardField> Board { get; set; } = new();
    }

    public sealed class PlayerState
    {
        // Shares held in company-index order.
        public int[] Shares { get; set; } = Array.Empty<int>();
    }

    public sealed class CompanyState
    {
        public int Trains { get; set; }
        public float Cash { get; set; }
        public int SharesAvailable { get; set; }
        public int Income { get; set; }
    }

    public sealed class BoardField
    {
        public string Terrain { get; set; } = "plain";
        public bool DevelopmentPossible { get; set; }

        public float ConstructionCost { get; set; }
        public float IncomeIncrease { get; set; }
        public float CashIncrease { get; set; }

        // One entry per company, in consistent company order.
        public List<FieldCompanyState> Companies { get; set; } = new();
    }

    public sealed class FieldCompanyState
    {
        public bool ConstructionPossible { get; set; }
    }

    public enum DecisionPhase
    {
        SelectActionGauge,
        SelectAuctionCompanyOrSkip,
        AuctionBidOrPass,
        SelectDevelopmentHexOrSkip,
        SelectConstructionCompanyOrSkip,
        SelectConstructionHexOrSkip
    }

    public enum ActionType
    {
        SelectGauge,
        Skip,
        SelectAuctionCompany,
        Bid,
        Pass,
        DevelopHex,
        SelectConstructionCompany,
        ConstructHex
    }

    public sealed record CandidateAction(
        ActionType Type,
        int GaugeIndex = -1,
        int CompanyIndex = -1,
        int RowIndex = -1,
        int ColumnIndex = -1,
        int BidAmount = 0
    )
    {
        public string GetVerboseAction(string playerName)
        {
            string verboseAction = string.Empty;

            switch (Type)
            {
                case ActionType.SelectGauge:
                    return $"{playerName} selects action gauge {GaugeIndex}";
                case ActionType.Skip:
                    return $"{playerName} skips the move";
                case ActionType.SelectAuctionCompany:
                    return $"{playerName} selects railway company {ChicagoExpressSetup.RailwayCompanies[CompanyIndex].Name} to auction off one of her shares";
                case ActionType.Bid:
                    return $"{playerName} bids {BidAmount} $";
                case ActionType.Pass:
                    return $"{playerName} passes";
                case ActionType.DevelopHex:
                    return $"{playerName} develops field hex-{RowIndex}-{ColumnIndex}";
                case ActionType.SelectConstructionCompany:
                    return $"{playerName} selects railway company {ChicagoExpressSetup.RailwayCompanies[CompanyIndex].Name} for construction";
                case ActionType.ConstructHex:
                    return $"{playerName} constructs field hex-{RowIndex}-{ColumnIndex}";
            }

            return verboseAction;
        }
    }   

    public sealed class DecisionContext
    {
        public DecisionPhase Phase { get; set; }

        // Information retained across the multi-step action.
        public int SelectedGauge { get; set; } = -1;
        public int SelectedCompany { get; set; } = -1;

        // Relevant when an auction is in progress.
        public int CurrentHighBid { get; set; }
        public int? CurrentHighBidder { get; set; }
    }
}
