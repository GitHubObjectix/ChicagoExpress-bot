
using chicago_express_bot;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using TorchSharp;

using var playwright = await Playwright.CreateAsync();

int tabIndex = 0;
int playerIndex = 0;
bool firstConnection = true;

while (true)
{
    try
    {
        var browser = await playwright.Chromium.ConnectOverCDPAsync(
            "http://127.0.0.1:9222");

        browser.Disconnected += (_, _) =>
        {
            Console.WriteLine("CDP connection disconnected.");
        };

        var context = browser.Contexts[0];
        var pages = context.Pages;

        if (firstConnection)
        {
            Console.WriteLine($"Connected: {browser.IsConnected}");

            foreach (var (page, index) in pages.Select((p, i) => (p, i)))
            {
                Console.WriteLine(
                    $"{index}: {page.Url} | {await page.TitleAsync()}");
            }

            Console.Write("Enter the Boardzilla tab number: ");
            tabIndex = int.Parse(Console.ReadLine() ?? "0");
        }

        if (tabIndex < 0 || tabIndex >= pages.Count)
        {
            throw new InvalidOperationException(
                $"Tab {tabIndex} is not available.");
        }

        var gamePage = pages[tabIndex];

        var gameFrontendHandler = await GameFrontendHandler.Initialize(gamePage);
        while (gameFrontendHandler.NeedsDecision() != null)
        {
            var neededDecision = gameFrontendHandler.NeedsDecision()!;

            Console.WriteLine(neededDecision.GetPrompt());
            await gameFrontendHandler.MakeDecision(Console.ReadLine());
        }

        if (!gameFrontendHandler.ReadyToPlay())
        {
            Console.WriteLine("I am not ready to play");
            Environment.Exit(1);
        }

        Console.WriteLine($"Playing {gameFrontendHandler.GetPlayerName()} in this game...");

        firstConnection = false;

        var uiFrame = gamePage.FrameLocator("#ui");

        while (!await gameFrontendHandler.IsGameStarted())
        {
            Console.WriteLine("Game has not yet started");
            await Task.Delay(250);
        }

        Console.WriteLine("Game starts");

        while (browser.IsConnected && !gamePage.IsClosed)
        {
            // initialize neural network

            StateEncoder stateEncoder = new StateEncoder(gameFrontendHandler.GetNumPlayers());

            var device = torch.CPU;

            using var policyNetwork = new ActionQNetwork(
                stateFeatureCount: stateEncoder.FeatureCount,
                actionFeatureCount: ActionEncoder.FeatureCount,
                hiddenSize: 128
            ).to(device);

            //// Adam will update the network weights during training.
            //using var optimizer = torch.optim.Adam(
            //    policyNetwork.parameters(),
            //    lr: 0.0001
            //);
            // end initialize neural network

            if (await gameFrontendHandler.ItsMyTurn())
            {
                Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} is at turn");

                DecisionContextManager decisionContextManager = new DecisionContextManager();
                CandidateAction? nextAction = null;

                do
                {
                    // scrape current game state...
                    var gameState = await game_state_scraping.ScrapeGameState(gameFrontendHandler, gameFrontendHandler.GetNumPlayers(), playerIndex);

                    // ...and update decision context
                    await decisionContextManager.Update(gameFrontendHandler, nextAction);

                    // determine next legal actions...
                    var nextActions = await decisionContextManager.GetCandidateActions(gameFrontendHandler);

                    Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} can choose from:");
                    foreach (var n in nextActions)
                        Console.WriteLine(n.GetVerboseAction(gameFrontendHandler.GetPlayerName()));
                    Console.WriteLine("end choice");

                    if (nextActions.Count > 0)
                    {
                        // ...to encode current state
                        var stateFeatures = stateEncoder.Encode(gameState, decisionContextManager.DecisionContext);

                        // ...and let action selector determine the best action
                        int bestActionIndex = ActionSelector.ChooseBestAction(policyNetwork, stateFeatures, nextActions);
                        nextAction = nextActions[bestActionIndex];

                        Console.Write(nextAction.GetVerboseAction(gameFrontendHandler.GetPlayerName()));
                        Console.ReadLine();
                    }
                    else
                    {
                        nextAction = null;
                    }
                } while (nextAction != null);

                // TODO! train neural network here

            }
            else
            {
                Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} is not at turn");
            }


            await Task.Delay(250);
        }

        Console.WriteLine("Connection lost. Reconnecting...");
    }
    catch (PlaywrightException ex)
    {
        Console.WriteLine($"Playwright connection failed: {ex.Message}");
        Console.WriteLine("Retrying in one second...");
    }

    await Task.Delay(1000);
}