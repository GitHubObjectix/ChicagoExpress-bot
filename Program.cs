
using chicago_express_bot;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using TorchSharp;

// game related variables
int tabIndex = 0;
bool shutdownRequested = false;
bool gameEnded = false;
bool isSupervisor = false;
bool isTraining = false;

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
    else if (argument == "--supervisor")
    {
        isSupervisor = true;
    }
    else if (argument == "--training")
    {
        isTraining = true;
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

static float CalculateReward(RewardCalculator rewardCalculator, GameState previousGameState, CandidateAction chosenAction, GameState gameState, bool isTerminal, int playerIndex)
{
    int playerCount = Math.Min(previousGameState.Players.Count, gameState.Players.Count);
    for (int i = 0; i < playerCount; ++i)
    {
        Console.WriteLine($"Player {i} cash: {previousGameState.Players[i].PlayerCash}$ {gameState.Players[i].PlayerCash}$");

        Console.Write("[");
        for (int j = 0; j < previousGameState.Players[i].Shares.Length; ++j)
            Console.Write($"{previousGameState.Players[i].Shares[j]} ");
        Console.WriteLine("]");

        Console.Write("[");
        for (int j = 0; j < gameState.Players[i].Shares.Length; ++j)
            Console.Write($"{gameState.Players[i].Shares[j]} ");
        Console.WriteLine("]");
    }

    float reward = rewardCalculator!.CalculateReward(previousGameState, chosenAction, gameState, isTerminal, playerIndex);

    Console.WriteLine($"Player {playerIndex} reward is {reward}");

    return reward;
}


try
{
    using var playwright = await Playwright.CreateAsync();

    // initialization code
    await using var browser =
        await playwright.Chromium.ConnectOverCDPAsync(
            "http://127.0.0.1:9222");

    browser.Disconnected += (_, _) =>
    {
        Console.WriteLine("CDP connection disconnected.");
    };

    var context = browser.Contexts[0];
    var pages = context.Pages;

    foreach (var (page, index) in pages.Select((p, i) => (p, i)))
    {
        Console.WriteLine(
            $"{index}: {page.Url} | {await page.TitleAsync()}");
    }

    Console.Write("Enter the Boardzilla tab number: ");
    tabIndex = int.Parse(Console.ReadLine() ?? "0");

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

    while (!gameFrontendHandler.ReadyToPlay())
    {
        Console.WriteLine("I am not ready to play");
    }

    Console.WriteLine($"Playing {gameFrontendHandler.GetPlayerName()} in this game {(isSupervisor ? "as supervisor" : "")}...");
    Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} {(isTraining ? " trains" : " just plays")}");    

    // initialize neural network related variables
    stateEncoder = new StateEncoder(gameFrontendHandler.GetNumPlayers());

    neuralNetworkManager = new NeuralNetworkManager(
        checkpointDirectory,
        stateEncoder.FeatureCount,
        ActionEncoder.FeatureCount,
        isTraining: isTraining);
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

    if (neuralNetworkManager.LoadConfiguration())
    {
        Console.WriteLine($"configuration loaded: {neuralNetworkManager.ConfigurationPath}. Configuration is {neuralNetworkManager.ConfigurationPath}");
    }

    if (neuralNetworkManager.LoadRuntimeState())
    {
        Console.WriteLine($"runtime state loaded: {neuralNetworkManager.RuntimeStatePath}");
    }

    rewardCalculator = new RewardCalculator();
    // end neural network initialization

    // main loop to play several games in series
    while (browser.IsConnected && !gamePage.IsClosed && !shutdownRequested)
    {
        gameEnded = false;

        while (!await gameFrontendHandler.IsGameStarted())
        {
            if (isSupervisor)
            {
                Console.WriteLine("Starting game as supervisor");
                await gameFrontendHandler.Click("button.ready");
            }
            else
            {
                Console.WriteLine("Game has not yet started");
            }

            await Task.Delay(250);
        }

        Console.WriteLine("Game starts");

        // main game loop
        while (browser.IsConnected && !gamePage.IsClosed && !shutdownRequested && !gameEnded)
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
                                float reward = CalculateReward(rewardCalculator!, pendingTransition.State, pendingTransition.ChosenAction, gameState, false, gameFrontendHandler.GetSelectedPlayer());

                                var transition = new ReplayTransition(
                                    StateFeatures: pendingTransition.StateFeatures,
                                    ChosenActionFeatures: pendingTransition.ChosenActionFeatures,
                                    Reward: reward,
                                    NextStateFeatures: stateFeatures,
                                    NextLegalActionsFeatures: nextActionsFeatures,
                                    IsTerminal: false);

                                // store it in the replay buffer
                                neuralNetworkManager!.AddTransitionToReplayBuffer(transition);
                                Console.WriteLine($"Put transition into buffer. Buffer size is {neuralNetworkManager!.GetReplayBufferCount()}");

                                // update the networks which might trigger a taining step
                                neuralNetworkManager.UpdateNetworks();

                                // and remove pending transition because its final now
                                pendingTransition = null;
                            }

                            // ...and let action selector determine the best action
                            bool actionWasExplored = false;
                            int bestActionIndex = neuralNetworkManager!.ChooseBestAction(stateFeatures, nextActionsFeatures, out actionWasExplored);
                            nextAction = nextActions[bestActionIndex];

                            // print the chosen action of the bot...
                            Console.WriteLine($"Epsilon is {neuralNetworkManager.Epsilon} after {neuralNetworkManager.NumMoves} moves, action was {(actionWasExplored ? "explored" : "chosen by network")}");
                            Console.WriteLine($"Selected action: {nextAction.GetVerboseAction(gameFrontendHandler.GetPlayerName())}");
                            await Task.Delay(1000);

                            // ...and await the execution
                            await nextAction.Execute(gameFrontendHandler);
                            await Task.Delay(3000);

                            // save game state and decision context
                            pendingTransition = new PendingReplayTransition(State: gameState, StateFeatures: stateFeatures, ChosenAction: nextAction, ChosenActionFeatures: nextActionsFeatures[bestActionIndex]);
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
                    bool popupCanBeDismissed = false;

                    Console.Write("There is a pop-up visible...");

                    if (!await gameFrontendHandler.IsGameEndPopup())
                    {
                        if (gameFrontendHandler.IsLocalGame() && isSupervisor || !gameFrontendHandler.IsLocalGame())
                        {
                            // dismiss the modal pop-up
                            popupCanBeDismissed = true;
                        }
                    }
                    else
                    {
                        Console.Write($"{gameFrontendHandler.GetPlayerName()} assumes that game has ended...");
                        gameEnded = true;
                    }

                    if (popupCanBeDismissed)
                    {
                        // dismiss the modal pop-up
                        await gameFrontendHandler.Click("div#announcement.modal-popup");
                        await Task.Delay(3000);
                        Console.Write("dismissed it");

                        popupCanBeDismissed = false;
                    }

                    Console.WriteLine();
                }

                if (gameEnded)
                {
                    if (pendingTransition != null)
                    {
                        // scrape current game state
                        var gameState = await game_state_scraping.ScrapeGameState(gameFrontendHandler, gameFrontendHandler.GetNumPlayers(), gameFrontendHandler.GetSelectedPlayer());

                        // if there is a pending action from previous move to finalize, store it in the replay buffer
                        float reward = CalculateReward(rewardCalculator!, pendingTransition.State, pendingTransition.ChosenAction, gameState, true, gameFrontendHandler.GetSelectedPlayer());

                        var transition = new ReplayTransition(
                            StateFeatures: pendingTransition.StateFeatures,
                            ChosenActionFeatures: pendingTransition.ChosenActionFeatures,
                            Reward: reward,
                            NextStateFeatures: Array.Empty<float>(),
                            NextLegalActionsFeatures: Array.Empty<float[]>(),
                            IsTerminal: true);
                        neuralNetworkManager!.AddTransitionToReplayBuffer(transition);

                        pendingTransition = null;
                    }
                }
            }

            if (!shutdownRequested && !gameEnded)
                await Task.Delay(250);
        }

        if (gameEnded && isSupervisor && !shutdownRequested)
        {
            // wait before dismissing the pop-up so that other bots can see it as well
            await Task.Delay(10000);

            Console.WriteLine("As supervisor i dismiss game end popup and reload the page");

            // if this bot is the supervisor it must dismiss the game end popup...
            await gameFrontendHandler.Click("div#announcement.modal-popup");
            await Task.Delay(3000);

            // end prepare restart of another game by reloading the page
            await gamePage.ReloadAsync();
            await Task.Delay(5000);
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Exception occurred: {ex.Message}");
}
finally
{
    try
    {
        if (neuralNetworkManager != null)
            neuralNetworkManager!.SaveRuntimeState();
    }
    finally
    {
        if (neuralNetworkManager != null)
            neuralNetworkManager!.Dispose();
    }
}