using System;
using System.Collections.Generic;
using TorchSharp;
using static TorchSharp.torch;

namespace chicago_express_bot
{
    public static class ActionSelector
    {
        public static int ChooseBestAction(
            ActionQNetwork network,
            float[] stateFeatures,
            IReadOnlyList<float[]> candidateActions,
            float epsilon,
            out bool actionWasExplored)
        {
            if (candidateActions.Count == 0)
                throw new ArgumentException("No legal actions.");

            if (stateFeatures.Length != network.StateFeatureCount)
                throw new ArgumentException("Incorrect state feature count.");

            if (epsilon is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(epsilon),
                    "Epsilon must be between 0 and 1.");

            int bestIndex = -1;

            if (Random.Shared.NextSingle() < epsilon)
            {
                bestIndex = Random.Shared.Next(candidateActions.Count);
                actionWasExplored = true;
            }
            else
            {
                network.eval();

                float bestQ = float.NegativeInfinity;

                using var noGrad = torch.no_grad();

                for (int i = 0; i < candidateActions.Count; i++)
                {
                    float[] actionFeatures = candidateActions[i];

                    if (actionFeatures.Length != network.ActionFeatureCount)
                        throw new ArgumentException(
                            $"Candidate {i} has the wrong feature count.");

                    float[] combined = new float[network.InputFeatureCount];

                    Array.Copy(stateFeatures, 0, combined, 0,
                        stateFeatures.Length);

                    Array.Copy(actionFeatures, 0, combined,
                        stateFeatures.Length, actionFeatures.Length);

                    using var input = torch.tensor(combined)
                        .reshape(1, network.InputFeatureCount);

                    using var output = network.forward(input);

                    float q = output.item<float>();

                    if (q > bestQ)
                    {
                        bestQ = q;
                        bestIndex = i;
                    }
                }

                actionWasExplored = false;
            }

            return bestIndex;
        }
    }
}
