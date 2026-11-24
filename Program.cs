
using chicago_express_bot;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using TorchSharp;

using var playwright = await Playwright.CreateAsync();

// game related variables
int tabIndex = 0;
bool firstConnection = true;
bool shutdownRequested = false;
bool gameEnded = false;

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdownRequested = true;
    Console.WriteLine("Shutdown requested. Cleaning up resources...");
};

// bot related variables
DecisionContextManager? decisionContextManager = null;
StateEncoder? stateEncoder = null;
RewardCalculator? rewardCalculator = null;
NeuralNetworkManager? neuralNetworkManager = null;
PendingReplayTransition? pendingTransition = null;


static bool AskYesNo(string question)
{
    while (true)
    {
        Console.Write($"{question} (y/n): ");

        string? input = Console.ReadLine()?.Trim();

        if (string.Equals(input, "y", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(input, "n", StringComparison.OrdinalIgnoreCase))
            return false;

        Console.WriteLine("invalid input");
    }
}

// persistence related variables
string? storagePath = null;

for (int i = 0; i < args.Length; i++)
{
    string argument = args[i];

    if (argument is "--storage" or "-s")
    {
        if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
        {
            throw new ArgumentException(
                $"Missing storage directory after '{argument}'.");
        }

        storagePath = args[++i];
    }
    else if (argument.StartsWith("--storage=", StringComparison.Ordinal))
    {
        storagePath = argument["--storage=".Length..];
    }
    else if (argument.StartsWith("-s=", StringComparison.Ordinal))
    {
        storagePath = argument["-s=".Length..];
    }
}

string checkpointDirectory = Path.GetFullPath(
    string.IsNullOrWhiteSpace(storagePath)
        ? AppContext.BaseDirectory
        : storagePath);
Directory.CreateDirectory(checkpointDirectory);

static float CalculateReward(RewardCalculator rewardCalculator, GameState previous, GameState next, int playerIndex)
{
    int playerCount = Math.Min(previous.Players.Count, next.Players.Count);
    for (int i = 0; i < playerCount; ++i)
    {
        Console.WriteLine($"Player {i} cash: {previous.Players[i].PlayerCash}$ {next.Players[i].PlayerCash}$");

        Console.Write("[");
        for (int j = 0; j < previous.Players[i].Shares.Length; ++j)
            Console.Write($"{previous.Players[i].Shares[j]} ");
        Console.WriteLine("]");

        Console.Write("[");
        for (int j = 0; j < next.Players[i].Shares.Length; ++j)
            Console.Write($"{next.Players[i].Shares[j]} ");
        Console.WriteLine("]");
    }

    float reward = rewardCalculator!.CalculateReward(previous, next, playerIndex);

    Console.WriteLine($"Player {playerIndex} reward is {reward}");

    return reward;
}


// main loop to implement Playwright reconnection on connection loss
try
{
    while (!shutdownRequested)
    {
        try
        {
            await using var browser =
                await playwright.Chromium.ConnectOverCDPAsync(
                    "http://127.0.0.1:9222");

            browser.Disconnected += (_, _) =>
            {
                Console.WriteLine("CDP connection disconnected.");
            };

            var context = browser.Contexts[0];
            var pages = context.Pages;

            if (firstConnection)
            {
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
                shutdownRequested = true;
                continue;
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

                neuralNetworkManager = new NeuralNetworkManager(checkpointDirectory, stateEncoder.FeatureCount, ActionEncoder.FeatureCount);
                if (neuralNetworkManager.LoadPolicyNetwork())
                {
                    Console.WriteLine($"policy-network loaded: {neuralNetworkManager.PolicyNetworkPath}");
                }

                if (neuralNetworkManager.LoadTargetNetwork())
                {
                    Console.WriteLine($"target-network loaded: {neuralNetworkManager.TargetNetworkPath}");
                }

                if (neuralNetworkManager.LoadOptimizer())
                {
                    Console.WriteLine($"optimizer loaded: {neuralNetworkManager.OptimizerPath}");
                }

                rewardCalculator = new RewardCalculator();
                // end neural network initialization
            }

            firstConnection = false;

            // main game loop
            while (browser.IsConnected && !gamePage.IsClosed && !gameEnded)
            {
                try
                {
                    if (await gameFrontendHandler.ItsMyTurn())
                    {
                        Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} is at turn");

                        if (decisionContextManager == null)
                            decisionContextManager = new DecisionContextManager();
                        CandidateAction? nextAction = null;

                        do
                        {
                            // update decision context...
                            await decisionContextManager!.Update(gameFrontendHandler, nextAction);

                            // ...and determine next legal actions...
                            var nextActions = await decisionContextManager.GetCandidateActions(gameFrontendHandler);

                            if (nextActions.Count > 0)
                            {
                                Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} can choose from:");
                                foreach (var n in nextActions)
                                    Console.WriteLine(n.GetVerboseAction(gameFrontendHandler.GetPlayerName()));
                                Console.WriteLine("end choice");

                                // if there are legal actions
                                // scrape current game state...
                                var gameState = await game_state_scraping.ScrapeGameState(gameFrontendHandler, gameFrontendHandler.GetNumPlayers(), gameFrontendHandler.GetSelectedPlayer());

                                // ...to encode current state and next actions
                                var stateFeatures = stateEncoder!.Encode(gameState, decisionContextManager.DecisionContext, gameFrontendHandler.GetSelectedPlayer());
                                var nextActionsFeatures = nextActions.Select(action => ActionEncoder.Encode(action)).ToList();

                                if (pendingTransition != null)
                                {
                                    // if there is a pending action from previous move to finalize, finalize it
                                    float reward = CalculateReward(rewardCalculator!, pendingTransition.State, gameState, gameFrontendHandler.GetSelectedPlayer());

                                    var transition = new ReplayTransition(
                                        StateFeatures: pendingTransition.StateFeatures,
                                        ChosenActionFeatures: pendingTransition.ChosenActionFeatures,
                                        Reward: reward,
                                        NextStateFeatures: stateFeatures,
                                        NextLegalActionsFeatures: nextActionsFeatures,
                                        IsTerminal: false);

                                    // store it in the replay buffer
                                    neuralNetworkManager!.AddTransitionToReplayBuffer(transition);
                                    Console.WriteLine($"Put transition into buffer. Buffer size is {neuralNetworkManager!.GetReplayBufferCount}");

                                    // update the networks which might trigger a taining step
                                    neuralNetworkManager.UpdateNetworks();

                                    // and remove pending transition because its final now
                                    pendingTransition = null;
                                }

                                // ...and let action selector determine the best action
                                int bestActionIndex = neuralNetworkManager!.ChooseBestAction(stateFeatures, nextActionsFeatures);
                                nextAction = nextActions[bestActionIndex];

                                // print the chosen action of the bot...
                                Console.WriteLine($"Selected action: {nextAction.GetVerboseAction(gameFrontendHandler.GetPlayerName())}");
                                await Task.Delay(1000);

                                // ...and await the execution
                                await nextAction.Execute(gameFrontendHandler);
                                await Task.Delay(3000);

                                // save game state and decision context
                                pendingTransition = new PendingReplayTransition(State: gameState, StateFeatures: stateFeatures, ChosenActionFeatures: nextActionsFeatures[bestActionIndex]);
                            }
                            else
                            {
                                Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} has finished the turn");

                                decisionContextManager = null;
                                nextAction = null;
                            }
                        } while (nextAction != null);
                    }
                    else
                    {
                        Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} is not at turn");
                    }
                }
                catch (TimeoutException)
                {
                    // this exception is hit when the bot logic checks for visibility of player-controls div
                    // if there are announcements visible the player-controls div stays hidden until the announcement is dismissed
                    bool isPopupVisible = await gameFrontendHandler.IsPopupVisible(100);
                    if (isPopupVisible)
                    {
                        Console.Write("There is a pop-up visible...");
                        if (gameFrontendHandler.IsLocalGame() && gameFrontendHandler.GetSelectedPlayer() == 0 || !gameFrontendHandler.IsLocalGame())
                        {
                            // dismiss the modal pop-up
                            await gameFrontendHandler.Click("div#announcement.modal-popup");
                            await Task.Delay(3000);
                            Console.Write("dismissed it");
                        }
                        Console.WriteLine();
                    }
                    else
                    {
                        // if player-controls are not visible and also no pop-up then the game ended
                        if (AskYesNo("Has game ended?"))
                            gameEnded = true;

                        if (gameEnded)
                        {
                            if (pendingTransition != null)
                            {
                                // scrape current game state
                                var gameState = await game_state_scraping.ScrapeGameState(gameFrontendHandler, gameFrontendHandler.GetNumPlayers(), gameFrontendHandler.GetSelectedPlayer());

                                // if there is a pending action from previous move to finalize, store it in the replay buffer
                                float reward = CalculateReward(rewardCalculator!, pendingTransition.State, gameState, gameFrontendHandler.GetSelectedPlayer());

                                var transition = new ReplayTransition(
                                    StateFeatures: pendingTransition.StateFeatures,
                                    ChosenActionFeatures: pendingTransition.ChosenActionFeatures,
                                    Reward: reward,
                                    NextStateFeatures: Array.Empty<float>(),
                                    NextLegalActionsFeatures: Array.Empty<float[]>(),
                                    IsTerminal: true);
                                neuralNetworkManager!.AddTransitionToReplayBuffer(transition);
                            }

                            // finally update the networks
                            neuralNetworkManager!.UpdateNetworks(true);
                        }
                    }
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

        if (!shutdownRequested)
            await Task.Delay(1000);
    }
}
finally
{
    neuralNetworkManager!.Dispose();
}