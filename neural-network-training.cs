using System;
using System.Collections.Generic;
using System.Text;
using TorchSharp;
using static TorchSharp.torch;

namespace chicago_express_bot
{
    public static class DqnTrainer
    {
        private static float[] Combine(float[] state, float[] action)
        {
            var result = new float[state.Length + action.Length];

            Array.Copy(state, 0, result, 0, state.Length);
            Array.Copy(action, 0, result, state.Length, action.Length);

            return result;
        }

        public static float TrainBatch(
            ActionQNetwork onlineNetwork,
            ActionQNetwork targetNetwork,
            torch.optim.Optimizer optimizer,
            IReadOnlyList<ReplayTransition> batch,
            float gamma = 0.99f)
        {
            if (batch.Count == 0)
                throw new ArgumentException("Training batch is empty.");

            onlineNetwork.train();
            targetNetwork.eval();

            var predictions = new List<Tensor>();

            try
            {
                // 1. Predict Q(s, a) with the online network.
                foreach (var transition in batch)
                {
                    float[] inputFeatures = Combine(
                        transition.StateFeatures,
                        transition.ActionFeatures);

                    using var input = torch.tensor(inputFeatures)
                        .reshape(1, inputFeatures.Length);

                    using var output = onlineNetwork.forward(input);

                    // Keep the computation graph for backpropagation.
                    predictions.Add(output.squeeze());
                }

                // 2. Calculate target values without gradients.
                var targetValues = new float[batch.Count];

                using (var noGrad = torch.no_grad())
                {
                    for (int i = 0; i < batch.Count; i++)
                    {
                        var transition = batch[i];

                        if (transition.IsTerminal ||
                            transition.NextLegalActionFeatures.Count == 0)
                        {
                            targetValues[i] = transition.Reward;
                            continue;
                        }

                        float maxNextQ = float.NegativeInfinity;

                        // Legal action count may differ for every transition.
                        foreach (var nextAction in
                                 transition.NextLegalActionFeatures)
                        {
                            float[] nextInputFeatures = Combine(
                                transition.NextStateFeatures,
                                nextAction);

                            using var nextInput =
                                torch.tensor(nextInputFeatures)
                                    .reshape(1, nextInputFeatures.Length);

                            using var nextOutput =
                                targetNetwork.forward(nextInput);

                            maxNextQ = Math.Max(
                                maxNextQ,
                                nextOutput.item<float>());
                        }

                        targetValues[i] = transition.Reward
                            + gamma * maxNextQ;
                    }
                }

                // 3. Calculate loss between predictions and targets.
                using var predictedBatch = torch.stack(predictions);
                using var targetBatch = torch.tensor(targetValues);

                using var loss = torch.nn.functional.mse_loss(
                    predictedBatch,
                    targetBatch);

                // 4. Backpropagation and weight update.
                optimizer.zero_grad();
                loss.backward();

                // Prevent excessively large gradient updates.
                torch.nn.utils.clip_grad_norm_(
                    onlineNetwork.parameters(), 1.0);

                optimizer.step();

                return loss.item<float>();
            }
            finally
            {
                foreach (var prediction in predictions)
                    prediction.Dispose();
            }
        }
    }
}
