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
                previouslySelectedAction.Enrich(decisionContext);
            }

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
                    throw new NotSupportedException($"Case I: unexpected possible action {possibleActions[0]}");
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
                            throw new NotSupportedException($"Case II: unexpected possible action. selected gauge is {decisionContext.SelectedGauge}");
                    }
                }
                else if (possibleActions[0] == "selectField" && possibleActions[1] == "skip")
                {
                    if (DecisionContext.SelectedGauge == 1)
                    {
                        decisionContext.Phase = DecisionPhase.SelectDevelopmentHexOrSkip;
                    }
                    else if (DecisionContext.SelectedGauge == 2)
                    {
                        decisionContext.Phase = DecisionPhase.SelectConstructionHexOrSkip;
                    }
                    else
                    {
                        throw new NotSupportedException($"Case III: unexpected possible action. selected gauge is {decisionContext.SelectedGauge}");
                    }
                }
                else
                {
                    throw new NotSupportedException($"Case IV: unexpected combination of possible actions {possibleActions[0]} {possibleActions[1]}");
                }
            }
            else
            {
                throw new NotSupportedException($"Case V: unexpected combination of possible actions {possibleActions.Count} {possibleActions[0]} {possibleActions[1]} {possibleActions[2]}");
            }
        }
    }
}
