using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using TorchSharp;

namespace chicago_express_bot
{
    public sealed record NeuralNetworkManagerConfiguration(float epsilonStart = 1.0f, float epsilonMinimum = 0.05f, float decayRate = 50_000.0f);

    public class NeuralNetworkManager : IDisposable
    {
        static private string POLICY_NETWORK_FILENAME = "policy-network.pt";
        static private string TARGET_NETWORK_FILENAME = "target-network.pt";
        static private string OPTIMIZER_FILENAME = "optimizer.pt";
        static private string BOT_CONFIGURATION_FILENAME = "bot-configuration.json";
        static private string RUNTIME_STATE_FILENAME = "runtime-state.bin";

        private string storageDirectory;
        public readonly string PolicyNetworkPath;
        public readonly string TargetNetworkPath;
        public readonly string OptimizerPath;
        public readonly string ConfigurationPath;
        public readonly string RuntimeStatePath;

        private ActionQNetwork policyNetwork;
        private ActionQNetwork targetNetwork;
        TorchSharp.Modules.Adam optimizer;

        ReplayBuffer replayBuffer;
        int numTrainingSteps = 0;
        int numMoves = 0;
        bool isTraining = false;

        public NeuralNetworkManagerConfiguration Configuration { get; set; } = new NeuralNetworkManagerConfiguration();

        public float Epsilon { get { return GetEpsilon(); } }
        public int NumTrainingSteps { get { return numTrainingSteps; } }
        public int NumMoves { get { return numMoves;  } }

        public NeuralNetworkManager(string storageDirectory, int stateFeatureCount, int actionFeatureCount, bool isTraining = false)
        {
            this.storageDirectory = storageDirectory;
            this.isTraining = isTraining;

            PolicyNetworkPath = Path.Combine(storageDirectory, POLICY_NETWORK_FILENAME);
            TargetNetworkPath = Path.Combine(storageDirectory, TARGET_NETWORK_FILENAME);
            OptimizerPath = Path.Combine(storageDirectory, OPTIMIZER_FILENAME);
            ConfigurationPath = Path.Combine(storageDirectory, BOT_CONFIGURATION_FILENAME);
            RuntimeStatePath = Path.Combine(storageDirectory, RUNTIME_STATE_FILENAME);

            var device = torch.CPU;

            policyNetwork = new ActionQNetwork(
                stateFeatureCount: stateFeatureCount,
                actionFeatureCount: actionFeatureCount,
                hiddenSize: 128
            ).to(device);

            targetNetwork = new ActionQNetwork(
                stateFeatureCount: stateFeatureCount,
                actionFeatureCount: actionFeatureCount,
                hiddenSize: 128
            ).to(device);

            targetNetwork.load_state_dict(policyNetwork.state_dict());

            // Adam will update the network weights during training.
            optimizer = torch.optim.Adam(policyNetwork.parameters(), lr: 0.0001);

            replayBuffer = new ReplayBuffer(5000);
        }

        public bool LoadPolicyNetwork()
        {
            bool loaded = false;

            if (File.Exists(PolicyNetworkPath))
            {
                policyNetwork.load(PolicyNetworkPath);
                loaded = true;
            }

            return loaded;
        }

        public bool LoadTargetNetwork()
        {
            bool loaded = false;

            if (File.Exists(TargetNetworkPath))
            {
                targetNetwork.load(TargetNetworkPath);
                loaded = true;
            }

            return loaded;
        }

        public bool LoadOptimizer()
        {
            bool loaded = false;

            if (File.Exists(OptimizerPath))
            {
                optimizer.load_state_dict(OptimizerPath);
                loaded = true;
            }

            return loaded;
        }

        public bool LoadConfiguration()
        {
            bool loaded = false;

            if (File.Exists(ConfigurationPath))
            {
                string json = File.ReadAllText(ConfigurationPath);

                try
                {
                    Configuration = JsonSerializer.Deserialize<NeuralNetworkManagerConfiguration>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException(
                        $"Configuration file '{ConfigurationPath}' does not contain a valid configuration.");

                    loaded = true;
                }
                catch (Exception)
                {
                }
            }

            return loaded;
        }

        public void AddTransitionToReplayBuffer(ReplayTransition transition)
        {
            replayBuffer.Add(transition);
            ++numMoves;
        }

        public int GetReplayBufferCount()
        {
            return replayBuffer.Count;
        }

        public void SaveRuntimeState()
        {
            Directory.CreateDirectory(storageDirectory);

            using FileStream fileStream = File.Create(RuntimeStatePath);
            using BinaryWriter writer = new(fileStream);

            writer.Write(1); // Runtime state format version.
            writer.Write(numTrainingSteps);
            writer.Write(numMoves);
            replayBuffer.WriteTo(writer);
        }

        public bool LoadRuntimeState()
        {
            if (!File.Exists(RuntimeStatePath))
                return false;

            using FileStream fileStream = File.OpenRead(RuntimeStatePath);
            using BinaryReader reader = new(fileStream);

            int version = reader.ReadInt32();

            if (version != 1)
                throw new InvalidDataException(
                    $"Unsupported runtime state format version: {version}.");

            int loadedNumTrainingSteps = reader.ReadInt32();
            int loadedNumMoves = reader.ReadInt32();
            ReplayBuffer loadedReplayBuffer = ReplayBuffer.ReadFrom(reader);

            if (loadedNumTrainingSteps < 0)
                throw new InvalidDataException("Training step count is invalid.");

            if (loadedNumMoves < 0)
                throw new InvalidDataException("Move count is invalid.");

            numTrainingSteps = loadedNumTrainingSteps;
            numMoves = loadedNumMoves;
            replayBuffer = loadedReplayBuffer;

            return true;
        }

        public void UpdateNetworks()
        {
            if (isTraining && replayBuffer.Count >= 50)
            {
                ReplayTransition[] batch = replayBuffer.Sample(32);

                DqnTrainer.TrainBatch(policyNetwork!, targetNetwork!, optimizer!, batch);

                if (++numTrainingSteps >= 50)
                {
                    targetNetwork.load_state_dict(policyNetwork!.state_dict());
                    numTrainingSteps = 0;
                }

                StoreNetworks();
            }
        }

        public int ChooseBestAction(float[] stateFeatures, IReadOnlyList<float[]> candidateActions, out bool actionWasExplored)
        {
            return ActionSelector.ChooseBestAction(
                policyNetwork,
                stateFeatures,
                candidateActions,
                GetEpsilon(),
                out actionWasExplored);
        }

        public void Dispose()
        {
            optimizer?.Dispose();
            targetNetwork?.Dispose();
            policyNetwork?.Dispose();
        }

        private void StoreNetworks()
        {
            policyNetwork!.save(PolicyNetworkPath);
            targetNetwork!.save(TargetNetworkPath);
            optimizer!.save_state_dict(OptimizerPath);
        }

        // the epsilon of greedy approach to select best actions
        private float GetEpsilon()
        {
            float epsilon;

            if (isTraining)
            {
                epsilon = Configuration.epsilonMinimum
                    + (Configuration.epsilonStart - Configuration.epsilonMinimum)
                    * MathF.Exp(-numMoves / Configuration.decayRate);
            }
            else
            {
                epsilon = 0.0f;
            }

            return epsilon;
        }
    }
}
