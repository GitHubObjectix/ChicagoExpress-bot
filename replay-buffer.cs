using System;
using System.Collections.Generic;
using System.Text;

namespace chicago_express_bot
{
    public sealed record PendingReplayTransition(
        GameState State,
        float[] StateFeatures,
        float[] ChosenActionFeatures);

    public sealed record ReplayTransition(
        float[] StateFeatures,
        float[] ChosenActionFeatures,
        float Reward,
        float[] NextStateFeatures,
        IReadOnlyList<float[]> NextLegalActionsFeatures,
        bool IsTerminal
    );


    public sealed class ReplayBuffer
    {
        private readonly ReplayTransition?[] _buffer;
        private readonly Random _random;
        private int _nextIndex;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;

        public ReplayBuffer(int capacity, int? seed = null)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(capacity));

            _buffer = new ReplayTransition[capacity];
            _random = seed.HasValue
                ? new Random(seed.Value)
                : new Random();
        }

        public void Add(ReplayTransition transition)
        {
            ArgumentNullException.ThrowIfNull(transition);

            _buffer[_nextIndex] = transition;

            _nextIndex = (_nextIndex + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }

        public ReplayTransition[] Sample(int batchSize)
        {
            if (batchSize <= 0 || batchSize > _count)
                throw new ArgumentOutOfRangeException(
                    nameof(batchSize),
                    "Batch size must be between 1 and Count.");

            // Sample without replacement.
            int[] indices = Enumerable.Range(0, _count).ToArray();

            for (int i = 0; i < batchSize; i++)
            {
                int j = _random.Next(i, _count);
                (indices[i], indices[j]) =
                    (indices[j], indices[i]);
            }

            var batch = new ReplayTransition[batchSize];

            for (int i = 0; i < batchSize; i++)
                batch[i] = _buffer[indices[i]]!;

            return batch;
        }
    }

}
