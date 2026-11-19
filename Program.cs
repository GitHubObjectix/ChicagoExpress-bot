
using chicago_express_bot;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using TorchSharp;

using var playwright = await Playwright.CreateAsync();

// game related variables
int tabIndex = 0;
int playerIndex = 0;
bool firstConnection = true;

// bot related variables
StateEncoder? stateEncoder = null;
ActionQNetwork? policyNetwork = null;
ActionQNetwork? targetNetwork = null;
TorchSharp.Modules.Adam? optimizer = null;

var device = torch.CPU;

// persistence related variables
string checkpointDirectory =
    Path.Combine(AppContext.BaseDirectory, "checkpoints");
Directory.CreateDirectory(checkpointDirectory);

string policyNetworkPath =
    Path.Combine(checkpointDirectory, "policy-network.pt");
string targetNetworkPath =
    Path.Combine(checkpointDirectory, "target-network.pt");
string optimizerPath =
    Path.Combine(checkpointDirectory, "optimizer.pt");

// main loop to implement Playwright reconnection on connection loss
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

        while (!await gameFrontendHandler.IsGameStarted())
        {
            Console.WriteLine("Game has not yet started");
            await Task.Delay(250);
        }

        Console.WriteLine("Game starts");

        if (firstConnection)
        {
            // initialize neural network related variables
            stateEncoder = new StateEncoder(gameFrontendHandler.GetNumPlayers());

            policyNetwork = new ActionQNetwork(
                stateFeatureCount: stateEncoder.FeatureCount,
                actionFeatureCount: ActionEncoder.FeatureCount,
                hiddenSize: 128
            ).to(device);

            if (File.Exists(policyNetworkPath))
            {
                policyNetwork.load(policyNetworkPath);
                Console.WriteLine($"policy-network loaded: {policyNetworkPath}");
            }

            targetNetwork = new ActionQNetwork(
                stateFeatureCount: stateEncoder.FeatureCount,
                actionFeatureCount: ActionEncoder.FeatureCount,
                hiddenSize: 128
            ).to(device);

            if (File.Exists(targetNetworkPath))
            {
                targetNetwork.load(targetNetworkPath);
                Console.WriteLine($"target-network loaded: {targetNetworkPath}");
            }

            //targetNetwork.load_state_dict(policyNetwork.state_dict());

            // Adam will update the network weights during training.
            optimizer = torch.optim.Adam(policyNetwork.parameters(), lr: 0.0001 );
            if (File.Exists(optimizerPath))
            {
                optimizer.load_state_dict(optimizerPath);
                Console.WriteLine($"optimizer loaded: {optimizerPath}");
            }

            // end neural network initialization
        }

        firstConnection = false;

        // main game loop (TODO! game terminal condition not implemented yet)
        while (browser.IsConnected && !gamePage.IsClosed)
        {
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

                    // TODO! at this point a transition can be created and stored in the replay buffer

                    // end TODO!

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

                        // TODO! Implement automated action execution here!
                        // atm the bot prints the action description on console and waits for execution 
                        Console.Write(nextAction.GetVerboseAction(gameFrontendHandler.GetPlayerName()));
                        Console.ReadLine();

                        // save game state and decision context
                    }
                    else
                    {
                        nextAction = null;
                    }
                } while (nextAction != null);

                // TODO! train neural network here
                // Diese drei Zeilen müssen nach DqnTrainer.TrainBatch(...)
                // ausgeführt werden, sobald der Trainingsschritt implementiert ist.
                policyNetwork!.save(policyNetworkPath);
                targetNetwork!.save(targetNetworkPath);
                optimizer!.save_state_dict(optimizerPath);
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
    catch (Exception ex)
    {
        Console.WriteLine($"Exception occurred: {ex.Message}");
    }

    await Task.Delay(1000);
}