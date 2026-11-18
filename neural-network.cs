using System;
using TorchSharp;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace chicago_express_bot
{
    public sealed class ActionQNetwork : Module<Tensor, Tensor>
    {
        private readonly Module<Tensor, Tensor> layers;        

        public int StateFeatureCount { get; }
        public int ActionFeatureCount { get; }
        public int InputFeatureCount => StateFeatureCount + ActionFeatureCount;

        public ActionQNetwork(
            int stateFeatureCount,
            int actionFeatureCount,
            int hiddenSize = 128)
            : base("ActionQNetwork")
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stateFeatureCount);

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actionFeatureCount);

            StateFeatureCount = stateFeatureCount;
            ActionFeatureCount = actionFeatureCount;

            layers = Sequential(
                ("linear1", Linear(InputFeatureCount, hiddenSize)),
                ("relu1", ReLU()),
                ("linear2", Linear(hiddenSize, hiddenSize)),
                ("relu2", ReLU()),
                ("output", Linear(hiddenSize, 1))
            );

            // Register layers so TorchSharp tracks their parameters.
            RegisterComponents();
        }

        // Input shape: [batchSize, stateFeatureCount + actionFeatureCount]
        // Output shape: [batchSize, 1]
        public override Tensor forward(Tensor input)
        {
            return layers.forward(input);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                layers.Dispose();
                ClearModules();
            }

            base.Dispose(disposing);
        }
    }
}
