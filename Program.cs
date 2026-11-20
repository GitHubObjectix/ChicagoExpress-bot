
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
ActionQNetwork? policyNetwork = null;
ActionQNetwork? targetNetwork = null;
TorchSharp.Modules.Adam? optimizer = null;
RewardCalculator? rewardCalculator = null;
int trainingSteps = 0;

PendingReplayTransition? pendingTransition = null;
ReplayBuffer? replayBuffer = null;

var device = torch.CPU;

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

string policyNetworkPath =
    Path.Combine(checkpointDirectory, "policy-network.pt");
string targetNetworkPath =
    Path.Combine(checkpointDirectory, "target-network.pt");
string optimizerPath =
    Path.Combine(checkpointDirectory, "optimizer.pt");

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
                else
                {
                    targetNetwork.load_state_dict(policyNetwork.state_dict());
                }

                // Adam will update the network weights during training.
                optimizer = torch.optim.Adam(policyNetwork.parameters(), lr: 0.0001);
                if (File.Exists(optimizerPath))
                {
                    optimizer.load_state_dict(optimizerPath);
                    Console.WriteLine($"optimizer loaded: {optimizerPath}");
                }

                rewardCalculator = new RewardCalculator();
                replayBuffer = new ReplayBuffer(5000);
                // end neural network initialization
            }

            firstConnection = false;

            // main game loop (TODO! game terminal condition not implemented yet)
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
                            await decisionContextManager.Update(gameFrontendHandler, nextAction);

                            // ...and determine next legal actions...
                            var nextActions = await decisionContextManager.GetCandidateActions(gameFrontendHandler);

                            Console.WriteLine($"{gameFrontendHandler.GetPlayerName()} can choose from:");
                            foreach (var n in nextActions)
                                Console.WriteLine(n.GetVerboseAction(gameFrontendHandler.GetPlayerName()));
                            Console.WriteLine("end choice");

                            if (nextActions.Count > 0)
                            {
                                // if there are legal actions
                                // scrape current game state...
                                var gameState = await game_state_scraping.ScrapeGameState(gameFrontendHandler, gameFrontendHandler.GetNumPlayers(), playerIndex);

                                // ...to encode current state and next actions
                                var stateFeatures = stateEncoder!.Encode(gameState, decisionContextManager.DecisionContext);
                                var nextActionsFeatures = nextActions.Select(action => ActionEncoder.Encode(action)).ToList();

                                if (pendingTransition != null)
                                {
                                    // if there is a pending action from previous move to finalize, store it in the replay buffer
                                    float reward = rewardCalculator!.CalculateReward(pendingTransition.State, gameState);

                                    var transition = new ReplayTransition(
                                        StateFeatures: pendingTransition.StateFeatures,
                                        ChosenActionFeatures: pendingTransition.ChosenActionFeatures,
                                        Reward: reward,
                                        NextStateFeatures: stateFeatures,
                                        NextLegalActionsFeatures: nextActionsFeatures,
                                        IsTerminal: false);
                                    replayBuffer!.Add(transition);

                                    Console.WriteLine($"Put transition into buffer. Buffer size is {replayBuffer.Count}");

                                    // TODO! determine a more reasonable count value
                                    if (replayBuffer.Count >= 50)
                                    {
                                        ReplayTransition[] batch = replayBuffer.Sample(32);

                                        DqnTrainer.TrainBatch(policyNetwork!, targetNetwork!, optimizer!, batch);

                                        if (++trainingSteps >= 50)
                                        {
                                            targetNetwork!.load_state_dict(policyNetwork!.state_dict());
                                            trainingSteps = 0;
                                        }

                                        policyNetwork!.save(policyNetworkPath);
                                        targetNetwork!.save(targetNetworkPath);
                                        optimizer!.save_state_dict(optimizerPath);
                                    }

                                    // and remove pending transition because its final now
                                    pendingTransition = null;
                                }

                                // ...and let action selector determine the best action
                                int bestActionIndex = ActionSelector.ChooseBestAction(policyNetwork!, stateFeatures, nextActionsFeatures);
                                nextAction = nextActions[bestActionIndex];

                                // TODO! Implement automated action execution here!
                                // atm the bot prints the action description on console and waits for execution 
                                Console.Write(nextAction.GetVerboseAction(gameFrontendHandler.GetPlayerName()));
                                Console.ReadLine();

                                // save game state and decision context
                                pendingTransition = new PendingReplayTransition(State: gameState, StateFeatures: stateFeatures, ChosenActionFeatures: nextActionsFeatures[bestActionIndex]);
                            }
                            else
                            {
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
                        Console.Write("There is a pop-up visible");
                        Console.ReadLine();
                    }
                    else
                    {
                        // if player-controls are not visible and also no pop-up then the game ended
                        if (AskYesNo("Has game ended?"))
                            gameEnded = true;
                        // TODO! determine how to finalize pending transitions
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
    optimizer?.Dispose();
    targetNetwork?.Dispose();
    policyNetwork?.Dispose();
}