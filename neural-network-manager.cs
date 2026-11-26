using System;
using System.Collections.Generic;
using System.Text;
using TorchSharp;

namespace chicago_express_bot
{
    public class NeuralNetworkManager : IDisposable
    {
        static private string POLICY_NETWORK_FILENAME = "policy-network.pt";
        static private string TARGET_NETWORK_FILENAME = "target-network.pt";
        static private string OPTIMIZER_FILENAME = "optimizer.pt";

        private string storageDirectory;
        public readonly string PolicyNetworkPath;
        public readonly string TargetNetworkPath;
        public readonly string OptimizerPath;

        private ActionQNetwork policyNetwork;
        private ActionQNetwork targetNetwork;
        TorchSharp.Modules.Adam optimizer;

        ReplayBuffer replayBuffer;
        int trainingSteps = 0;

        // the epsilon of greedy approach to select best actions
        float epsilon = 0.8f;

        public NeuralNetworkManager(string storageDirectory, int stateFeatureCount, int actionFeatureCount)
        {
            this.storageDirectory = storageDirectory;

            PolicyNetworkPath = Path.Combine(storageDirectory, POLICY_NETWORK_FILENAME);
            TargetNetworkPath = Path.Combine(storageDirectory, TARGET_NETWORK_FILENAME);
            OptimizerPath = Path.Combine(storageDirectory, OPTIMIZER_FILENAME);

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

        public void AddTransitionToReplayBuffer(ReplayTransition transition)
        {
            replayBuffer.Add(transition);
        }

        public int GetReplayBufferCount()
        {
            return replayBuffer.Count;
        }

        public void UpdateNetworks(bool forced = false)
        {
            if (forced || replayBuffer.Count >= 50)
            {
                ReplayTransition[] batch = replayBuffer.Sample(forced ? replayBuffer.Count : 32);

                DqnTrainer.TrainBatch(policyNetwork!, targetNetwork!, optimizer!, batch);

                if (forced || ++trainingSteps >= 50)
                {
                    targetNetwork.load_state_dict(policyNetwork!.state_dict());
                    trainingSteps = 0;
                }

                StoreNetworks();
            }
        }

        public int ChooseBestAction(float[] stateFeatures, IReadOnlyList<float[]> candidateActions)
        {
            return ActionSelector.ChooseBestAction(
                policyNetwork,
                stateFeatures,
                candidateActions,
                epsilon);
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
    }
}
