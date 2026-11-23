using SkiaSharp;
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
        public List<PlayerState> Players { get; set; } = new();
        public List<CompanyState> Companies { get; set; } = new();
        public List<BoardField> Board { get; set; } = new();
    }

    public sealed class PlayerState
    {
        // cash money of the player
        public int PlayerCash { get; set; }

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

    public abstract record CandidateAction(ActionType Type)
    {
        public abstract string GetVerboseAction(string playerName);
        public abstract void Enrich(DecisionContext decisionContext);
        public abstract Task Execute(GameFrontendHandler frontendHandler); 
    }

    public record SelectGaugeAction(ActionType Type, int GaugeIndex = -1)
        : CandidateAction(Type)
    {
        public override string GetVerboseAction(string playerName)
        {
            return $"{playerName} selects action gauge {GaugeIndex}";
        }

        public override void Enrich(DecisionContext decisionContext)
        {
            decisionContext.SelectedGauge = GaugeIndex;
        }

        public override async Task Execute(GameFrontendHandler frontendHandler)
        {
            string selectorOrLocator = string.Empty;

            switch (GaugeIndex)
            {
                case 0:
                    selectorOrLocator = "div[id='auction-gauge']"; break;
                case 1:
                    selectorOrLocator = "div[id='development-gauge']"; break;
                case 2:
                    selectorOrLocator = "div[id='route-construction-gauge']"; break;
                default:
                    throw new NotSupportedException("unexpected action gauge");
            }

            await frontendHandler.Click(selectorOrLocator);
        }
    }

    public record SkipAction(ActionType Type)
        : CandidateAction(Type)
    {
        public override string GetVerboseAction(string playerName)
        {
            return $"{playerName} skips the move";
        }

        public override void Enrich(DecisionContext decisionContext)
        {
        }

        public override async Task Execute(GameFrontendHandler frontendHandler)
        {
            await frontendHandler.Click("form#skip button[type='submit']");
        }
    }

    public record SelectCompanyAction(ActionType Type, int CompanyIndex)
        : CandidateAction(Type)
    {
        public override string GetVerboseAction(string playerName)
        {
            if (Type == ActionType.SelectAuctionCompany)
                return $"{playerName} selects railway company {ChicagoExpressSetup.RailwayCompanies[CompanyIndex].Name} to auction off one of her shares";
            else
                return $"{playerName} selects railway company {ChicagoExpressSetup.RailwayCompanies[CompanyIndex].Name} for construction";
        }

        public override void Enrich(DecisionContext decisionContext)
        {
            decisionContext.SelectedCompany = CompanyIndex;
        }

        public override async Task Execute(GameFrontendHandler frontendHandler)
        {
            string selectorOrLocator = string.Empty;

            await frontendHandler.Click($"div[id='{ChicagoExpressSetup.RailwayCompanies[CompanyIndex].Id}']");
        }
    }

    public record BidAction(ActionType Type, int BidAmount)
        : CandidateAction(Type)
    {
        public override string GetVerboseAction(string playerName)
        {
            return $"{playerName} bids {BidAmount} $";
        }

        public override void Enrich(DecisionContext decisionContext)
        {
            decisionContext.CurrentHighBid = BidAmount;
        }

        public override async Task Execute(GameFrontendHandler frontendHandler)
        {
            await frontendHandler.Fill("form#bid input[name='amount']", BidAmount.ToString());
            await frontendHandler.Click("form#bid button[name='submit']");
        }
    }

    public record PassAction(ActionType Type)
        : CandidateAction(Type)
    {
        public override string GetVerboseAction(string playerName)
        {
            return $"{playerName} passes";
        }

        public override void Enrich(DecisionContext decisionContext)
        {
        }

        public override async Task Execute(GameFrontendHandler frontendHandler)
        {
            await frontendHandler.Click("form#pass button[type='submit']");
        }
    }

    public record SelectFieldAction(ActionType Type, int RowIndex = -1, int ColumnIndex = -1)
        : CandidateAction(Type)
    {
        public override string GetVerboseAction(string playerName)
        {
            if (Type == ActionType.DevelopHex)
                return $"{playerName} develops field hex-{RowIndex}-{ColumnIndex}";
            else
                return $"{playerName} constructs field hex-{RowIndex}-{ColumnIndex}";
        }

        public override void Enrich(DecisionContext decisionContext)
        {
        }

        public override async Task Execute(GameFrontendHandler frontendHandler)
        {
            await frontendHandler.Click($"div[id='hex-{RowIndex}-{ColumnIndex}']");
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
