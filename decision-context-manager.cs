using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using static Google.Protobuf.Reflection.SourceCodeInfo.Types;

namespace chicago_express_bot
{
    public class DecisionContextManager
    {
        private DecisionContext? decisionContext = null;
        public DecisionContext DecisionContext { get { return decisionContext; } }

        public async Task Update(GameFrontendHandler gameFrontendHandler, CandidateAction? previouslySelectedAction = null)
        {
            if (previouslySelectedAction != null && decisionContext != null)
            {
                switch (previouslySelectedAction.Type)
                {
                    case ActionType.SelectGauge:
                        decisionContext.SelectedGauge = previouslySelectedAction.GaugeIndex; break;
                    case ActionType.Skip:
                        break;
                    case ActionType.SelectAuctionCompany:
                        decisionContext.SelectedCompany = previouslySelectedAction.CompanyIndex; break;
                    case ActionType.Bid:
                        decisionContext.CurrentHighBid = previouslySelectedAction.BidAmount; break;
                    case ActionType.Pass:
                        break;
                    case ActionType.DevelopHex:
                        break;
                    case ActionType.SelectConstructionCompany:
                        decisionContext.SelectedCompany = previouslySelectedAction.CompanyIndex; break;
                    case ActionType.ConstructHex:
                        break;
                }
            }

            // TODO! in a local run of the game the player-controls are always visible
            // the code needs to check if the bot player is still at turn

            EncodePhase(await gameFrontendHandler.GetPossibleActions());
        }

        public async Task<List<CandidateAction>> GetCandidateActions(GameFrontendHandler gameFrontendHandler)
        {
            List<CandidateAction> candidateActions = new List<CandidateAction>();

            if (DecisionContext != null)
            {
                // if DecisionContext is null, its not my turn anymore

                switch (DecisionContext.Phase)
                {
                    case DecisionPhase.SelectActionGauge:
                        candidateActions = await gameFrontendHandler.GetActionGaugeCandidates();
                        break;
                    case DecisionPhase.SelectAuctionCompanyOrSkip:
                        candidateActions = await gameFrontendHandler.GetCompanySelectionCandidates(decisionContext, true);
                        break;
                    case DecisionPhase.AuctionBidOrPass:
                        candidateActions = await gameFrontendHandler.GetAuctionCandidates(decisionContext);
                        break;
                    case DecisionPhase.SelectDevelopmentHexOrSkip:
                        candidateActions = await gameFrontendHandler.GetFieldSelectionCandidates(decisionContext, true);
                        break;
                    case DecisionPhase.SelectConstructionCompanyOrSkip:
                        candidateActions = await gameFrontendHandler.GetCompanySelectionCandidates(decisionContext, false);
                        break;
                    case DecisionPhase.SelectConstructionHexOrSkip:
                        candidateActions = await gameFrontendHandler.GetFieldSelectionCandidates(decisionContext, false);
                        break;
                }
            }

            return candidateActions;
        }

        private void EncodePhase(List<string> possibleActions)
        {
            if (possibleActions.Count == 0)
            {
                decisionContext = null;
            }
            else if (possibleActions.Count == 1)
            {
                if (decisionContext == null)
                    decisionContext = new DecisionContext();

                if (possibleActions[0] == "selectAction")
                {
                    decisionContext.Phase = DecisionPhase.SelectActionGauge;
                }
                else
                {
                    throw new NotSupportedException("unexpected possible action");
                }
            }
            else if (possibleActions.Count == 2)
            {
                if (decisionContext == null)
                    decisionContext = new DecisionContext();

                if (possibleActions[0] == "bid" && possibleActions[1] == "pass")
                {
                    decisionContext.Phase = DecisionPhase.AuctionBidOrPass;
                }
                else if (possibleActions[0] == "selectCompany" && possibleActions[1] == "skip")
                {
                    switch (decisionContext.SelectedGauge)
                    {
                        case 0:
                            decisionContext.Phase = DecisionPhase.SelectAuctionCompanyOrSkip; break;
                        case 2:
                            decisionContext.Phase = DecisionPhase.SelectConstructionCompanyOrSkip; break;
                        case 1:
                        default:
                            throw new NotSupportedException("unexpected possible action");
                    }
                }
                else if (possibleActions[0] == "selectField" && possibleActions[1] == "skip")
                {
                    if (DecisionContext.SelectedGauge == 1)
                    {
                        decisionContext.Phase = DecisionPhase.SelectConstructionHexOrSkip;
                    }
                    else if (DecisionContext.SelectedGauge == 2)
                    {
                        decisionContext.Phase = DecisionPhase.SelectDevelopmentHexOrSkip;
                    }
                    else
                    {
                        throw new NotSupportedException("unexpected possible action");
                    }
                }
                else
                {
                    throw new NotSupportedException("unexpected combination of possible actions");
                }
            }
            else
            {
                throw new NotSupportedException("unexpected combination of possible actions");
            }
        }
    }
}
