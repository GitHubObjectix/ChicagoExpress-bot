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
            IReadOnlyList<float[]> candidateActions)
        {
            if (candidateActions.Count == 0)
                throw new ArgumentException("No legal actions.");

            if (stateFeatures.Length != network.StateFeatureCount)
                throw new ArgumentException("Incorrect state feature count.");

            network.eval();

            int bestIndex = -1;
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

            return bestIndex;
        }
    }
}
